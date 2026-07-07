# Stage 11 — Online co-op: settled design (2026-07-07)

Supersedes the Stage 11 section of the archived `plans/plan.md` (which left lockstep-vs-state-sync
to a spike and preferred a WebSocket relay). Decisions below were made with the codebase audits of
2026-07-07 (determinism, input/co-op, state-surface) and user direction.

## Decision: distributed-authority state replication over P2P WebRTC

**NOT deterministic lockstep.** Rationale:
- Input delay without rollback feels bad, and this game's keyboard player aims with the MOUSE
  (`PlayerShip.cs:447`) — the worst case for delay (instant cursor, lagging aim). Rollback would
  need full per-frame state snapshot/restore, which this entity graph can't do cheaply — the same
  reason state-sync *serialization* looked expensive. Lockstep's main weakness is only fixable by
  building the thing it was chosen to avoid.
- Lockstep desyncs are FATAL (no snapshot = no resync), and this is an actively developed
  decompiled codebase: every future card would carry a permanent determinism tax (no local-
  conditional RNG draws, fixed timestep discipline), enforced only by a replay harness.
- State-sync needs REPLICATION, not serialization: cosmetics (explosions/particles/text) never
  cross the wire (spawned locally from events); bullets are ballistic (spawn-event only); only
  enemies + ships need continuous state. ~60 entities × ~20 B × 20 Hz ≈ 25 KB/s worst case.
- What made lockstep *possible* (scripted `GameEventList` levels, centralized spawning,
  Timer-driven progression) makes host authority *clean* instead.
- Float determinism was never the blocker (identical WASM binary on both peers) — the audit's
  determinism punch-list (fixed timestep, RNG seeding, hit-stop rework) is simply NOT NEEDED
  under this model. No determinism contract on future code. DebugFlags can't desync anything.

## Authority model

- **Each peer owns its own ship**: position, aim, firing, powerups, and GETTING HIT (you decide
  when something touched you — no dying to a thing you dodged on your screen). Zero input latency
  on everything you control.
- **Each peer owns its own bullets/bombs and kill claims.** Hit-testing your shots runs locally
  against your view.
- **One peer = world host**: runs the real sim for enemies, spawners, the `GameEventList` level
  script, AI friends, score tally, shared lives pool. Clients do NOT run spawners/script/enemy AI.
- **Generous at-least-once claims ("power to them"):** kills, score, combo credit, and powerups
  are honored for EVERY claimant — no arbitration, no rejection path. First claim marks the enemy
  dead / despawns the pickup (idempotent guard); overlapping claims still pay out score/effects to
  their senders. Double-crediting requires focus-firing one target within an RTT window — self-
  balancing in a horde shmup.
- **Shared-fate stuff is host-authoritative exactly-once:** lives pool, checkpoint resets, level
  progression, victory/game-over.

## Wire protocol (3 layers)

1. **Ship stream** (each peer → others, unreliable DataChannel, ~30 Hz): pos, velocity, aim angle,
   fire/blast state, powerup level, alive flag. Remote ships = interpolated puppets ~100 ms behind;
   their shots spawn locally from firing state.
2. **World snapshot** (host → clients, unreliable, ~15–20 Hz, round-robin/delta over live enemies):
   NetId, type, pos, vel, per-type state byte, hp. Clients dead-reckon between snapshots and
   lerp-correct. Failure mode = an enemy pops a few px and self-heals (vs lockstep's match-over).
3. **Events** (reliable channel): spawn (NetId + type + Setup params), death (NetId + killer →
   local explosion + score event), level-script beats (messages, boss phases, background/music,
   checkpoints), powerup spawn/claim, life lost / checkpoint-reset, pause, victory/game-over.

**Client-side enemies are puppets** — constructed via the same `NewXxx`+`Setup` factories the
sprite harness registry uses (proven: every enemy draws correctly with gameplay Update never
running), plus a small NetPuppet component applying snapshots. One generic replicator over the
`AlienDrawableGameComponent` base fields (position/speed/curframe/scale/hp) + small per-type
extras for stateful bosses.

## Specific mechanics

- **TeamChallenge tether:** the `ShipConnector` becomes a LOCAL FORCE each peer applies to its own
  ship, anchored at the remote puppet's position — nothing to reconcile. Soft, damped, clamped
  pull (stiff spring + mutual 100 ms-stale anchors = oscillation risk; if it wobbles, soften,
  never stiffen). Draw the tether between the two ON-SCREEN ship positions (not physics anchors) —
  staleness reads as elastic stretch. Break = or-of-either-peer idempotent event. Shared-fate
  death (one dies → both die → lose life) = player-owned death events + host-authoritative life
  decrement.
- **Enemies aim at ~RTT/2-stale ship positions** (host's view of your ship). Fine — enemy aim is
  fuzzy by design.
- **Any player leaves → the match ends.** Symmetrical; no host migration (stretch goal at most).
  Clean exit, drop timeout, closed tab — all one code path: "player left", back to menu. Optional
  later nicety: a few seconds' grace before the timeout verdict.
- **Excluded mode:** WebcamAliens only (the camera IS the controller; also wall-clock mask
  freshness `WebcamInterop.cs:230`). TeamChallenge is IN scope (tether solved above).
- **Session descriptor** (handshake, reliable channel): protocol version + BUILD HASH (peers must
  run the identical published binary — reject stale-cached clients), level, difficulty (locked
  and shared — it drives spawn tables), Friends/AI flags, Turbo forced 100, DebugFlags gameplay
  overrides forced to defaults.

## Transport / infra

- **WebRTC DataChannels, JS owns RTC** (house pattern, like `webcam.js`/`eaMusic`): a `webrtc.js`
  module owns RTCPeerConnection; C# sees only a `NetInterop` shim (`SendX(bytes)` /
  `[JSInvokable] OnX`) mirroring `WebcamInterop`. Unreliable+unordered channel
  (`maxRetransmits:0`) for streams; reliable channel for events/handshake.
- **Signaling = room codes** on a tiny WebSocket server on the Hetzner box (deploy tooling +
  precedent exist via Meridian): host gets a 4–5 char code, friend enters it, server shuttles
  SDP/ICE then drops out of the loop. **STUN:** free public. **TURN:** none in v1 (~10–15 % of
  NAT pairs will fail → clear error; coturn on Hetzner later if it bites).
- **Static-site invariant holds:** single-player never touches any server.
- **Dev transport:** `BroadcastChannel` between two local tabs = full sessions with zero network.
- Background-tab gotcha: rAF stops when hidden (sim falls to ~30 Hz setTimeout) — needs a
  "waiting for peer" indicator.

## Phases (each a card)

1. **Replication skeleton** — NetId registry, spawn/death hooks, 3-layer protocol, tested over
   BroadcastChannel two-tab loopback.
2. **Ship mirroring** — two peers, own ship + remote puppet. The "it feels good" gate: own ship
   identical to single-player, remote ship smooth.
3. **World authority** — host enemies → client puppets, kill claims, powerups, score/lives events.
4. **Level-script replication + reset flow** — script beats, checkpoint reset, victory/game-over,
   pause, TeamChallenge tether.
5. **Real transport** — webrtc.js, NetInterop, Hetzner signaling server, room codes, lobby,
   build-hash handshake.
6. **Hardening** — leave/drop paths, interpolation tuning, waiting-for-peer UI, TURN decision.

## Audit facts worth keeping (from the 2026-07-07 explorations)

- Input surface per player per tick: move (4 dir bits or left stick), aim (quantized design-space
  point or right stick) + fire HELD bit, blast bit, start/join, pause/back, scheme tag. NOT
  buttons-only — mouse aim is load-bearing (`PlayerShip.cs:388-467`).
- `DebugInput.Consume` (`InputHandler.cs:230`) is global + keyboard-only — can't target a player
  index. Remote input needs a per-slot tier read by `PadDown/PadPressed/LeftStick/RightStick` (or
  a new `ControlDevice.Remote`); joins via `Oracle.AddPlayer` (4 slots, `Oracle.cs:108`).
- Mid-level join exists locally (`GameScene.CheckPlayerJoins`, `GameScene.cs:764`) — online roster
  locked at start for v1 anyway.
- Death/checkpoint = purge world + `score.Load()` + `eventList.RevertToCheckpoint()` + respawn
  (`GameScene.cs:724-762`) — maps directly onto a broadcast reset event.
- Pause freezes the sim via `Collection.Push()` disabling components (`ComponentBin.cs:135`);
  pause/unpause replicate as events.
- A disconnected gamepad force-pauses (`GameScene.cs:597`) — don't let a remote player's pad-drop
  event pause the host's world; scope pause triggers to local devices + explicit pause events.
