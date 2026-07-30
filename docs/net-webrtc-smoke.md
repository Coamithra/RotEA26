# The real-WebRTC two-window co-op smoke — YOUR run, not an agent's

**THIS ONE IS FOR YOU TO RUN. Nothing here has been run for you.** Everything else on card
`25ad0659` was verified headlessly or in a single Chrome tab; this is the one check that needs two
real browser windows talking over real WebRTC, on your screen, and it is the only thing that can
fail in a way none of the rigs can see.

**Why now.** The de-static card put four seams into the live co-op path — `INetHost`'s clock and
dev flags (2a), the four `ServiceHelper` services (2b), the scene (2c-i) and the entity (2c-ii).
All four are merged (`9bdbc5a`). The plan used to gate this smoke on "after step 3", but step 3
turned out not to be the last step that touches the shipped path — **2c-ii was** — and step 3 may
never happen at all (`plans/net-headless-sim.md`, banner correction 3). So this is due now, and
what it covers is those four seams end to end over a real peer connection.

**Roughly 15 minutes.** If it passes, the refactor is clear. If it fails, it will fail loudly —
one of the two windows will not pair, or will pair and then show you a frozen or empty world.

---

## What could actually break, i.e. what you are looking for

Not "does the game run" — that is already proven. Three specific things, because they are the
three the headless and single-tab rigs structurally cannot reach:

1. **Real WebRTC + real signaling.** Every automated run used either an in-process wire or a
   BroadcastChannel loopback. `WebRtcTransport`, the SDP/ICE exchange and the two DataChannels are
   only exercised here.
2. **WASM through real JS interop.** The build-hash and peer-identity fingerprints resolve through
   `WebRtcInterop` in JS. A desktop headless run stubs that.
3. **Two peers with genuinely independent worlds.** Every rig runs one real peer; this is the only
   configuration where two real `Oracle`/`ComponentBin`/`ScoreVisualiser` instances face each
   other, which is exactly what seams 2b and 2c-i/ii moved.

---

## Setup

**Two things running before you touch a browser.**

A local signaling server (do NOT point this at the deployed one — you want the identical client
code against a server you can read):

```bash
cd server/signal && uvicorn main:app --port 8091
```

The dev server, from the repo root:

```bash
dotnet run --project web/DevServer -c Debug --urls http://localhost:5280
```

**Two separate Chrome WINDOWS, tiled side by side so neither covers the other.** Not two tabs —
a background *or fully covered* window has `visibilityState:'hidden'`, Chrome stops its rAF
entirely, and the peers then time each other out. Every number you read would be garbage and the
run would look like a bug that isn't one. (You do not need `?fpsuncapped` here; that is the
workaround for when tooling has to cover the windows. You are watching them.)

Both windows use the same URL:

```
http://localhost:5280/?signal=ws://localhost:8091/ws
```

**That is the whole query string, and the shortness is deliberate.** Both peers pair from the
MENU, and a menu session refuses to pair if *either* side has a boot-hijacking debug flag set
(`NetSession.HandleHello` on `DebugFlags.Active`) — so `?net=`, `?level=`, `?invuln` and friends
would make this fail for a reason that has nothing to do with what you are testing. `?signal=` is
not in `Active`, which is why it is allowed. If you want the verbose log, `&netlog` and `&binlog`
are also safe to add to both.

---

## The run

**The menu path was rehearsed headlessly, so you are replaying a known-good sequence** — but
navigate by reading the screen, not by counting keypresses: **the main menu hides locked entries**,
so it is five rows on a fresh save (Start · Options · Tutorial · Online Co-op · Exit) and eight on
a fully unlocked one. Arrow down to **Online Co-op** and press Enter. The submenu opens on
**Host Game**, with Join by Code / Join Online Game / Back below it.

1. **Window A — host.** Online Co-op → **Host Game**. It shows a 5-character room code and
   "waiting". Note the code.
2. **Window B — joiner.** Online Co-op → **Join by Code** → type window A's code into the overlay.
3. **Window A** now picks the mission and difficulty through the normal select screens. Take
   **Mission 1 on Medium** — a story level, so both peers get a real world with real enemies, and
   Level 1's script hands the ship spawn to a beat, which is worth seeing replicate.
4. Both windows launch. **Play for about a minute, on both.** Shoot things in each window — the
   claims are the interesting part, and an idle peer proves much less.
5. Open DevTools on both (F12) and read the console.

---

## What a PASS looks like

Read both consoles. The `[net]` metrics line prints every 5 seconds.

**Pairing** — one line on each, and the roles must differ:

```
[net] session start role=host room=<CODE> protocol=v10 transport=WebRTC (menu lobby)
[net] session start role=join room=<CODE> protocol=v10 transport=WebRTC (menu lobby)
```

`transport=WebRTC` is the point of the whole exercise. If it says `BroadcastChannel`, the two
windows found each other locally and **you have not tested anything here** — check `?signal=`
reached both.

**The seat map — mirror images, and this is the sharpest single check.** Host and joiner should
read as each other's reflection, with `*` marking the local seat:

| window | expected |
|---|---|
| A (host) | `roster=0:Keyboard*,1:Remote` `pri=0/1` |
| B (join) | `roster=0:Remote,1:Keyboard*` `pri=1/0` |

**The link is healthy** — on both lines: `localShip=1 remoteShip=1`, `buf=` around 100ms, and
`drop` / `sgap` / `ordViol` / `seqGap` / `extrap` all at or near **0**. A `buf=0ms` with
`remoteShip=0` on one side is the classic "paired but the seat negotiation failed" shape.

**The world agrees** — the host's `snapTx` climbs; the joiner's `snapRx` and `snapEnt` climb.
`snapBad` should stay at **0**; `snapNew` and `snapDead` climbing is ordinary traffic and not a
fault (they track the world's spawn and removal rates).

**Kills are being credited both ways** — the joiner's `clTx` and the host's `clRx` should both be
non-zero and roughly track each other once you have shot things in both windows. A non-zero
`clPaid` is the generous-payout path working, not an error.

**And with your eyes, not the console:** both ships visible in both windows and moving smoothly;
enemies in roughly the same places; the scores tracking each other. Then let one ship die — the
reset should happen on both.

**Zero exceptions in either console.** That is the bar; a red line is a fail whatever the metrics
say.

---

## If it fails

Tell me what the two consoles said and I will pick it up — but two failures are known rig
problems rather than bugs, so check these first:

- **One window frozen / the peers timing each other out.** The windows are overlapping, or one is
  behind something. Re-tile them and rerun.
- **"Update required" on both.** Build-hash mismatch. Both windows must be served by the same dev
  server; a stale cached tab is the usual cause — hard-reload both.

`GET http://localhost:8091/health` reports `rooms` / `listed` / `browsers` and is the best way to
see what the server thinks is happening without touching either window.
