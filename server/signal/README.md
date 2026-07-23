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
`GET /health` returns `{"ok": true, "rooms": <count>}`.

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
separate unit on its own port (8091).

```sh
mkdir -p /opt/rotea
# copy this server/signal directory to /opt/rotea/server
python3 -m venv /opt/rotea/venv
/opt/rotea/venv/bin/pip install -r /opt/rotea/server/requirements.txt
cp /opt/rotea/server/rotea.service /etc/systemd/system/rotea.service
# paste nginx-location.conf's blocks into the notzelda.haraldmaassen.com
# 443 server block, then:
nginx -t && systemctl reload nginx
systemctl daemon-reload
systemctl enable --now rotea
curl https://notzelda.haraldmaassen.com/rotea/health   # {"ok":true,"rooms":0}
```

Clients connect to `wss://notzelda.haraldmaassen.com/rotea/ws`.
