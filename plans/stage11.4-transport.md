# Stage 11.4 — Real transport: WebRTC + signaling + lobby (card f74a2317)

Parent design: `plans/stage11-online-coop.md`. Cards 11.1–11.3 shipped the whole
replication layer over a BroadcastChannel loopback; this card makes it work between two
real machines: a `webrtc.js` transport behind the existing `INetTransport` seam, a
room-code signaling server on the shared Hetzner VPS, and a menu-driven Host/Join lobby.

## Context / infra facts (surveyed 2026-07-24)

- VPS `46.225.218.207` (Hetzner CX22, Ubuntu 24.04), shared with NotZelda + Fighterproto.
  Ports in use: 8080/8443 (NotZelda), 8081 (llama), 8090 (fighterproto uvicorn,
  localhost), 80/443 (nginx). **We take 8091 (localhost-only).**
- House precedent (Fighterproto) = exactly our shape: FastAPI+uvicorn in
  `/opt/<Project>/server` + venv, systemd unit, nginx `location /<name>/ws` inside the
  `notzelda.haraldmaassen.com` 443 server block (existing Let's Encrypt cert). We clone
  it: `/opt/rotea/`, unit `rotea.service`, `location /rotea/ws` → `127.0.0.1:8091`.
  Public signaling URL: `wss://notzelda.haraldmaassen.com/rotea/ws`.
- No DNS or cert work needed. Single-player never touches the server (hard invariant).

## Signaling protocol (WebSocket, JSON, server is a dumb relay)

- `{t:"host"}` → `{t:"code", code:"ABCDE"}` — server mints a unique 5-char room code
  (unambiguous alphabet, no 0/O/1/I), room TTL ~10 min, max 2 members.
- `{t:"join", code}` → both members get `{t:"peer"}`; errors `{t:"error", reason}`
  (`nocode`, `full`, `bad`).
- After pairing, `{t:"sdp", ...}` / `{t:"ice", ...}` are relayed verbatim to the other
  member (trickle ICE). Members disconnect their WS once the DataChannels are up; the
  room is deleted on either WS closing or TTL.
- Server: `server/signal/main.py` in THIS repo (FastAPI, in-memory rooms, no state, no
  auth — codes are the capability). Deployed by `server/signal/deploy.py` (paramiko ssh,
  creds via `.env` `ROTEA_VPS_*` keys or `ssh root@…` directly).

## Game-side design

### A. `wwwroot/webrtc.js` — JS owns RTCPeerConnection (house pattern)

`window.eaRtc` facade, inert until called (plain boot does nothing):
- `host(signalUrl)` / `join(signalUrl, code)`: open the signaling WS, create the
  RTCPeerConnection (STUN: Google public servers; **no TURN in v1** — ~10-15% NAT pairs
  get a clean "could not connect"), host creates the two DataChannels: `"s"`
  `{ordered:false, maxRetransmits:0}` (stream lane) and `"r"` (reliable lane).
- `send(b64, rel)` / `close()`. Payloads cross the C# boundary as base64 (convention),
  the wire carries raw ArrayBuffers.
- Callbacks → `DotNet.invokeMethod('EvilAliensWeb', …)`:
  `rtcPhase(phase, detail)` (`code`→shows room code, `peer`, `connecting`, `connected`,
  `failed`, `closed`), `rtcData(b64, rel)`, `rtcBye` (channel close / pagehide).

### B. `Compat/Net/WebRtcInterop.cs` + `WebRtcTransport.cs`

- `WebRtcInterop`: static `[JSInvokable]` shim mirroring `NetInterop` (separate class —
  transports stay independent; BroadcastChannel dev path untouched).
- `WebRtcTransport : INetTransport`: `SendStream`→`eaRtc.send(b64,false)`, etc. The RTC
  connection is established BY THE LOBBY before `NetSession.Start`; `Open()` just
  attaches handlers.

### C. `Compat/Net/NetLobby.cs` — pre-session orchestration

Owns the host/join flow before a session exists: drives `eaRtc`, holds phase + room
code for the menu to draw, and on `connected` calls the new
`NetSession.Start(game, role, transport)` overload (role: host hosted the room). Peer
loss or `failed` before/after pairing → tear down, surface a message to the menu.
Cancel (Esc) → `eaRtc.close()`.

### D. Protocol v4: build-hash handshake + launch event

- `MsgHello` gains: 8-byte build hash + a flags byte (bit0 = DebugFlags.Active).
  Mismatch → new `MsgReject(reason)` → lobby shows "UPDATE REQUIRED — reload the page"
  (stale-cached client) and closes. Hash source: `window.eaBuildHash`, injected by
  `deploy.yml` at publish (sha256 of `_framework/blazor.boot.json`, which fingerprints
  every assembly). Dev builds: absent → "dev" → enforcement skipped.
- Menu-path sessions refuse to pair if EITHER side has `DebugFlags.Active` (gameplay-
  hijacking flags); the `?net=` URL dev path stays anything-goes.
- New reliable event `EvLaunch(level, difficulty)`: host picks level+difficulty through
  the NORMAL existing selection flow after the lobby connects; client sits on a
  "connected — host is choosing" panel and mirrors the launch (same warm+launch path),
  difficulty locked to the host's. WebcamAliens excluded (net roster locked at launch;
  camera-as-controller excluded by design). Turbo forced to 100 for both while the
  session is active.

### E. Menu flow (`MenuScene`)

- Main menu entry **"Online Co-op"** → submenu **Host Game / Join Game / Back**.
- Host: `NetLobby.Host()` → menu panel shows `ROOM CODE: ABCDE` + "waiting for
  player…" (code big enough to read over a call). Esc cancels.
- Join: room-code entry via an HTML overlay OUTSIDE `#app` (the slider-panel/touch
  house pattern — real text input beats reinventing text entry in-engine), constructed
  on demand, feeding `DebugInput`-style `[JSInvokable]` → `NetLobby.Join(code)`.
- Connected (both): host flows into the normal level+difficulty select; client shows
  the waiting panel until `EvLaunch`.
- Peer lost at ANY phase (lobby or in-level) → existing `PeerLost` path + back to
  menu with a short message (the 11.5 card owns polishing the in-level UX).

### F. Dev/test flags (all in `DebugFlags`)

- `?rtc` — with `?net=host/join`: use WebRtcTransport instead of BroadcastChannel from
  boot (skips the menu; host prints its room code to console, join takes `?code=ABCDE`).
- `?signal=<url>` — override the signaling URL (local server `ws://localhost:8091/ws`).
- Existing `?net=…&room=…` BroadcastChannel path unchanged (still the default dev rig).

## File-by-file

| File | Change |
|---|---|
| `wwwroot/webrtc.js` | NEW — eaRtc facade: signaling client + RTCPeerConnection + 2 channels |
| `wwwroot/index.html` | script include + join-code HTML overlay + `eaBuildHash` default |
| `Compat/Net/WebRtcInterop.cs` | NEW — [JSInvokable] shim |
| `Compat/Net/WebRtcTransport.cs` | NEW — INetTransport impl |
| `Compat/Net/NetLobby.cs` | NEW — host/join orchestration, phase for menu |
| `Compat/Net/NetSession.cs` | Start(role, transport) overload; v4 hello (hash+flags); MsgReject; EvLaunch; Turbo force |
| `Compat/Net/NetProtocol.cs` | v4: hello fields, MsgReject, EvLaunch |
| `Compat/DebugFlags.cs` | `?rtc` `?signal=` `?code=` |
| `Game/EvilAliens/MenuScene.cs` | Online Co-op entry + host/join/lobby panels + client launch mirror |
| `Game/EvilAliens/Game1.cs` | client EvLaunch → warm+launch seam (if needed beyond MenuScene) |
| `server/signal/main.py` | NEW — FastAPI room-code relay |
| `server/signal/` (unit, nginx snippet, deploy notes/script) | NEW |
| `.github/workflows/deploy.yml` | inject `eaBuildHash` at publish |
| `web/EvilAliensWeb/CLAUDE.md`, root `CLAUDE.md` | docs (VPS section already added) |

## Verification

1. **Signaling server unit-level**: run locally (`uvicorn main:app --port 8091`), drive
   with a tiny ws test script — host/join/relay/expiry paths.
2. **Two Chrome windows over REAL WebRTC, local signaling**:
   `?level=Level1&net=host&rtc&signal=ws://localhost:8091/ws&aiplayer&invuln` + join
   with `&code=` — the 11.1–11.3 metrics gate must stay healthy (pops 0, claims
   flowing, drop/dup tolerated on the stream lane now that it's genuinely unreliable).
3. **Menu flow end-to-end** (eaPress + overlay JS hook): Host → code → Join → lobby →
   host picks level → both in-level; peer-drop paths back to menu. Zero console errors.
4. **Deployed signaling**: same two-window test against
   `wss://notzelda.haraldmaassen.com/rotea/ws`; confirm nginx upgrade + TLS + fighterproto/
   notzelda untouched.
5. **THE CARD GATE — two machines on different networks**: needs a second human/machine
   (user + a friend, or user's phone hotspot). Flagged for manual testing at close.

## Out of scope (→ 11.5)

TURN relay; reconnection/grace; waiting-for-peer background-tab UI polish; host
migration; in-level drop UX beyond "peer left → menu"; lobby chat/ready-up.
