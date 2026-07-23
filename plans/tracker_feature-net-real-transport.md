# Tracker: feature/net-real-transport (Stage 11.4, card f74a2317)

## Phase 1: Pick Up the Card
- [x] Claim card f74a2317 → In Progress
- [x] Pull latest main
- [x] Read card + plans/stage11-online-coop.md
- [x] Create worktree wt1 + branch feature/net-real-transport, push -u

## Phase 2: Research
- [ ] Read 11.1 transport interface (BroadcastChannel dev transport, INetTransport or equivalent)
- [ ] Read WebcamInterop pattern (C# shim + JS-owns-platform)
- [ ] Read webcam.js / eaMusic / index.html glue patterns
- [ ] Read menu system (MenuScene) for Online Co-op entry + lobby
- [ ] Session start flow: how a net session boots today (dev transport)
- [ ] Build hash: how to get one (assembly hash? publish artifact?)
- [ ] Meridian deploy tooling precedent for Hetzner (signaling server deploy)
- [ ] Summarize findings

## Phase 3: Design
- [ ] Write plans/stage11.4-design notes (or extend tracker) — file-by-file
- [ ] User approval
- [ ] Comment approach TLDR on card

## Phase 4: Implement
- [ ] webrtc.js (RTCPeerConnection, 2 channels, signaling client)
- [ ] NetInterop C# shim + WebRtcTransport behind transport interface
- [ ] Signaling server (subagent: greenfield WebSocket room-code server)
- [ ] Deploy signaling server to Hetzner
- [ ] Menu: Online Co-op → Host (code) / Join (enter code) + lobby
- [ ] Handshake: protocol version + build hash, locked level/difficulty/turbo/DebugFlags
- [ ] Static-site invariant: no server contact unless online co-op entered

## Phase 5: Verify
- [ ] Clean Debug build
- [ ] Two-tab (or two-browser) session over real WebRTC via signaling server, level end-to-end
- [ ] Zero console exceptions in real Chrome
- [ ] Gate: two machines on different networks join via room code (user-assisted if needed)

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Merge main into branch, re-verify
- [ ] PR + self-merge, fast-forward root
- [ ] Clean worktree/branch, delete tracker
- [ ] Card → Done + closing comment; follow-up cards
