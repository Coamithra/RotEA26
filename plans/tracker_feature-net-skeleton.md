# Tracker: feature/net-skeleton (card 4ebc2ad2 — Stage 11.1)

## Phase 1: Pick Up the Card
- [x] Claim card 4ebc2ad2 (moved to In Progress)
- [x] Pull latest main
- [x] Read the card + plans/stage11-online-coop.md
- [x] Create worktree (wt1 blocked by foreign lock -> slot wt3, port 5283) + branch feature/net-skeleton

## Phase 2: Research
- [x] Read Oracle.AddPlayer / player slots
- [x] Read InputHandler (per-slot input tiers, DebugInput.Consume)
- [x] Read PlayerShip (ControlDevice.AI, DoAIMove/DoAIFire, state surface)
- [x] Read ComponentBin (spawn/death seam)
- [x] Read GameScene (player joins, ship lifecycle)
- [x] Read WebcamInterop (JS-owns-platform interop pattern)
- [x] Read DebugFlags parse block + index.html glue

## Phase 3: Design
- [x] Settle transport interface + message layers + flags
- [x] Post approach TLDR comment on card

## Phase 4: Implement
- [x] DebugFlags: ?net=host/join, ?room=, ?aiplayer, ?netlog
- [x] INetTransport interface + BroadcastChannelTransport (eaNet JS + NetInterop shim)
- [x] NetId registry + spawn/death hooks (ComponentBin seam, host-side)
- [x] Protocol: ship stream / world snapshot (stub) / reliable events + encode/decode
- [x] NetSession: handshake, heartbeat/timeouts, remote ship join via Oracle.AddPlayer
- [x] Remote puppet: ~100ms interpolation buffer (real-time render clock), shots from firing state
- [x] ?aiplayer forces local ship onto AI branch (EffectiveController)
- [x] Sync metrics logging (buffer health, pops, event ordering) [net] line / 5s

## Phase 5: Verify
- [x] Clean dotnet build (0 errors, no new warnings)
- [x] Two-window BroadcastChannel gate BOTH roles via MCP tab (rooms g2 join / g3 host):
      pops=0 maxPop=0 extrap=0 drop=0 dup=0 ordViol=0 seqGap=0, buf 73-120ms,
      rx ~20-25/s, both ships visible + hue-swapped, evTx/evRx flowing, blast replicated,
      "remote ship joined slot=1", zero console errors. (Backgrounded-tab rAF ~1Hz means
      the tabs must be in two visible windows -- documented in web CLAUDE.md.)
- [x] Plain boot: normal splash -> Press Start -> menu, zero [net] lines, zero errors
- [x] Diff spot-check (no lowercase content/, no BlendState.AlphaBlend, no codegen rerun)

## Phase 6: Review & Ship
- [x] Commit + push
- [ ] /review + fix findings
- [ ] Pull main, re-verify
- [ ] PR create + merge, fast-forward main
- [ ] Update web/EvilAliensWeb/CLAUDE.md (Online co-op section)
- [ ] Remove worktree + branch, delete tracker
- [ ] Card -> Done + closing comment

## Phase 7: Clean up
- [ ] Kill dev server, close Chrome tabs
