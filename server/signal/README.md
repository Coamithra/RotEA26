# RotEA signaling server

Tiny FastAPI WebSocket server that pairs two browsers by room code and relays
their WebRTC SDP/ICE blobs. Once the peers' DataChannels connect, the clients
close their sockets — the server holds no game state. 2 peers max per room,
200 rooms max, 10-minute room TTL.

## Protocol (JSON text frames on `/ws`)

| Client sends | Server replies / behavior |
|---|---|
| `{"t":"host"}` | `{"t":"code","code":"ABCDE"}` — creates a room (alphabet `23456789ABCDEFGHJKLMNPQRSTUVWXYZ`) |
| `{"t":"join","code":"ABCDE"}` | on success, **both** members get `{"t":"peer"}`; code is trimmed + case-insensitive |
| `{"t":"sdp",...}` / `{"t":"ice",...}` | relayed **verbatim** to the other room member (only after pairing) |
| anything else / malformed | `{"t":"error","reason":"bad"}` (socket stays open) |

Server-initiated / error frames:

| Frame | Meaning |
|---|---|
| `{"t":"error","reason":"nocode"}` | unknown or expired room code — retry with another code |
| `{"t":"error","reason":"full"}` | room already has 2 members |
| `{"t":"error","reason":"busy"}` | server at the 200-room cap |
| `{"t":"error","reason":"expired"}` | room hit its 10-minute TTL; server closes the socket |
| `{"t":"gone"}` | the other member disconnected; room deleted |

One socket can host/join at most one room (a second attempt gets `bad`).
`GET /health` returns `{"ok": true, "rooms": <count>, "listed": <count>, "browsers": <count>}`.

## Public game browser (card 2001fbd8)

A host may **list** its room so strangers can find and join it; a third kind of
socket — a **browser** — lists build-compatible open rooms and pings each host
through the relay for a real RTT. Listing is metadata on the same room object;
it never constructs a game session.

| Client sends | Server replies / behavior |
|---|---|
| `{"t":"list","level":L,"difficulty":D,"players":P,"proto":..,"hash":..}` | host only: mark the room listed + set metadata + refresh TTL. Idempotent (also the update path). No reply. |
| `{"t":"unlist"}` | host only: hide from browse; the room stays joinable by code |
| `{"t":"beat"}` | host only: refresh the room's TTL (send ~every 30 s while listed) |
| `{"t":"browse","proto":..,"hash":..}` | `{"t":"rooms","rooms":[{code,level,difficulty,players,ageSec},…]}` — only **listable** (listed + not full) rooms whose `proto` **and** `hash` match |
| `{"t":"ping","code":..,"id":..}` | browser only (after browse): forwarded to that room's host as `{"t":"ping","id":..,"ref":<opaque>}` |
| `{"t":"pong","id":..,"ref":..}` | host only: routed back to the originating browser as `{"t":"pong","id":..}` |

Room TTL now counts from the **last beat** (or creation, if never beaten), so a
listed game stays alive across a long level while unlisted 11.4 lobby rooms keep
the unchanged 10-minute-from-creation expiry. Pings are rate-limited per browser
socket; the forwarded `ref` is an opaque server-assigned id, so a host learns
nothing about who pinged it.

## Local run (Windows dev)

```sh
python -m venv venv
venv/Scripts/pip install -r requirements.txt
venv/Scripts/uvicorn main:app --port 8091
```

## Test

```sh
venv/Scripts/python test_signal.py
```

Starts the app in-process on an ephemeral port and checks every protocol case;
exits nonzero on failure.

## Deploy (VPS: notzelda.haraldmaassen.com box)

Do NOT touch the existing `notzelda*` / `fighterproto` services — this is a
separate unit on its own port (8091). **Already provisioned?** Skip to
"Updating an existing deployment" below; this section is first-time setup only.

```sh
mkdir -p /opt/rotea/server
# from the repo: scp server/signal/* root@<box>:/opt/rotea/server/
python3 -m venv /opt/rotea/venv
/opt/rotea/venv/bin/pip install -r /opt/rotea/server/requirements.txt
cp /opt/rotea/server/rotea.service /etc/systemd/system/rotea.service
# nginx: install the location snippet as its own file and include it from the
# EXISTING notzelda.haraldmaassen.com 443 server block (no new vhost):
cp /opt/rotea/server/nginx-location.conf /etc/nginx/rotea-locations.conf
#   ... add `include /etc/nginx/rotea-locations.conf;` inside that block, then:
nginx -t && systemctl reload nginx
systemctl daemon-reload
systemctl enable --now rotea
curl https://notzelda.haraldmaassen.com/rotea/health
# {"ok":true,"rooms":0,"listed":0,"browsers":0}
```

Clients connect to `wss://notzelda.haraldmaassen.com/rotea/ws`.

### Updating an existing deployment

The box is already provisioned — unit installed, venv built, and the 443 block
already has `include /etc/nginx/rotea-locations.conf` — so a code-only update is
files + restart, with **no nginx or systemd work**. Stage and test *before* the
live process is swapped, so a bad push never reaches the running service.

**Run these one at a time and stop at the first failure** — this is a checklist,
not a paste-able script (the guard rails are yours, and the swap is the point of
no return).

**Never background or time-limit steps 4-5, and run the step-4 wait from your
own machine, not inside an `ssh` command.** Killing a local `ssh` does NOT kill
the shell it started on the box: a `until ...; done` + swap left running there
comes back to life the moment the condition clears and performs a SECOND swap.
That race really happened — the orphan moved the freshly-installed tree aside,
found no `server.new` to put in its place, and `&&`-skipped its restart, leaving
`/opt/rotea/server` missing under a still-running process. If you ever see the
directory gone, the newest `server.old-*` IS your code: `mv` it back and
restart.

```sh
# 1. From the REPO (local): stage the whole directory, LF-normalised.
ssh root@<box> 'mkdir -p /opt/rotea/server.new'
scp server/signal/* root@<box>:/opt/rotea/server.new/    # then, on the box:
sed -i 's/\r$//' /opt/rotea/server.new/*                # CRLF -> LF (see below)

# 2. Install deps FIRST, so the test runs against the libs it will ship with.
/opt/rotea/venv/bin/pip install -r /opt/rotea/server.new/requirements.txt

# 3. Test the staged code. Ephemeral port, in-process: does NOT touch the live
#    8091. STOP HERE if it is not green — nothing has been swapped yet.
cd /opt/rotea/server.new && /opt/rotea/venv/bin/python test_signal.py

# 4. Wait until no room is open, so no session is dropped (see below). `rooms`
#    subsumes `listed`; do NOT also gate on `browsers` (it lingers -- see below).
until curl -s https://notzelda.haraldmaassen.com/rotea/health \
      | grep -q '"rooms":0'; do sleep 5; done

# 5. Swap. The `mv` IS the backup — keep exactly one server.old-*, delete older.
cd /opt/rotea && TS=$(date -u +%Y%m%d-%H%M%S) \
  && mv server server.old-$TS && mv server.new server \
  && systemctl restart rotea        # ONLY rotea, never the notzelda* units

# 6. Verify, and roll back if any of these is wrong.
systemctl is-active rotea
curl https://notzelda.haraldmaassen.com/rotea/health   # needs listed + browsers
journalctl -u rotea -n 50 --no-pager                   # on any doubt
```

**Rollback.** Name the backup explicitly — `$TS` is gone in a later shell, and an
empty one silently expands to a path that does not exist. Never `rm -rf` the live
tree; move it aside so a failed restore is still recoverable, and restart
unconditionally (`&&` would skip the restart on a failed `mv`, leaving the
service down — `Restart=on-failure` cannot save a missing WorkingDirectory):

```sh
ls -d /opt/rotea/server.old-*                          # pick the one to restore
cd /opt/rotea
systemctl stop rotea
mv server server.failed-$(date -u +%Y%m%d-%H%M%S)
mv server.old-<TS> server
systemctl start rotea
```

**Changed `rotea.service` or `nginx-location.conf`?** Step 1 stages them, but
nothing installs them — they are only reference copies under `/opt/rotea/server`.
Re-run the matching line from the first-time block above (`cp` + `daemon-reload`,
or `cp` + `nginx -t && systemctl reload nginx`).

**`requirements.txt` bumps are NOT staged and NOT covered by the rollback.** The
venv is shared with the running service, so step 2 mutates what the live process
will use on its next restart. Pin deliberately and be ready to reinstall the old
pins by hand.

**A restart drops every open signaling socket.** Peers whose DataChannel is
already up are unaffected (WebRTC is P2P and those clients have hung up), but a
**listed host still waiting for joiners holds its socket open for the whole
level** to send its beats, and browse sockets stay open in the carousel — both
get `fail('signal')` / `rtcBrowseFailed` and **neither reconnects**. Hence step
4's idle wait.

**`browsers` in `/health` overstates reality — never gate a deploy on it.**
The count drops only in a socket's own disconnect handler, and the sweeper
expires rooms, not browser sockets; with nginx's `proxy_read_timeout 900s`, a
carousel tab that was simply CLOSED can hold the count above zero for up to 15
minutes with no client behind it. Gate on `rooms` — that is what has a game at
stake.

**Copy files with LF endings.** The Windows working tree is CRLF; Python
tolerates it, but a CRLF `rotea.service` will not parse if it is ever installed,
so normalise the whole staged directory rather than remembering which files care.

Deploying the server does **not** publish the game — GitHub Pages is a separate
manual `workflow_dispatch`. A client feature needs both, and the game browser's
`browse` filters on build hash, so listers and browsers only see each other when
they run the same published build.
