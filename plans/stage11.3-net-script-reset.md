# Stage 11.3 — Level-script replication, reset flow, pause, tether

Card `70c7aea2`. Parent design: `plans/stage11-online-coop.md`. Builds on 11.1 (ship
mirroring) + 11.2 (host world authority, NetPuppets, claims).

## Context

A join peer never runs the level script (`GameScene.UpdateNormal` skips
`eventList.Update`), so every *observable side effect* of the script is invisible to it:
messages, background changes, music switches, checkpoints, victory. Death/reset and pause
are likewise still local-only, and the TeamChallenge tether hard-pins both ships
(`ShipConnector.Update` → `SetPosition` at midpoint ±39px), which would fight the
interpolated remote puppet.

## Design

### 1. Script beats = hooks at the side-effect primitives (host → client reliable events)

Not per-level work: hook the choke points the scripts (and boss code) call, so every
level replicates for free. All new events ride the existing `MsgEvent` envelope; protocol
version 2 → 3.

| Event | Payload | Host hook | Client action |
|---|---|---|---|
| `EvMessage` | type b, speech b, angle f32, text utf8 | `MessageEvent.Update` at its `AnimatedMessage` spawn | spawn the same `AnimatedMessage` (+`SetWarningDirection` for redwarning) |
| `EvUnlock` | item b, unlockType b, text utf8 | `UnlockEvent.Update` at its banner spawn | same banner + grant the unlock locally (generous: the client earned it too) |
| `EvBackground` | op b, vec2 f32×2 | small `NetSession.OnBackgroundOp` calls inside `Background.SetSpeed / QueueEarth / QueueSmallEarth / QueueAndromeda / EngageBeltSlowdown / DisengageBeltSlowdown / SetAlienBase2..6` | invoke the same method (client never re-sends — sends are host-gated) |
| `EvMusic` | song b (0xFF = stop) | `SoundManager.PlayMusic` / `StopMusic` | `PlayMusic` iff different from the current song (dedupe: both sides play the initial track locally in `Initialize`) |
| `EvCheckpoint` | — | `eventList_OnCheckPointReached` | `score.Save()` so a later reset's `score.Load()` restores the same baseline |
| `EvReset` | mode b (respawn / reset / gameover) | `GameScene.LoseLife` (host) | mirror the exact branch via a `GameScene.NetApplyReset(mode)` seam |
| `EvVictory` | — | `GameScene.Victory` (host; script-fired) | run `Victory()` locally (achievements for the client too; Terminate stays per-side via `UpdateWin`) |
| `EvPause` | on/off b | local pause push / resume in `GameScene.Update` | freeze via `Collection.Push()` + darkener + "PEER PAUSED" hint; no interactive menu |
| `EvTetherBreak` | — | `ShipConnector.TakeHit` / A-or-B-null `Die` (local cause only) | break own connector if still alive (idempotent, or-of-either-peer) |

`CrossFade` is deliberately NOT hooked — it belongs to the reset flow, which each side
runs itself from `EvReset` (hooking it would double-fade the client).

### 2. Death / checkpoint reset — ONE host broadcast

- Client-side `LoseLife` is suppressed (`UpdateNormal`'s AllShipsDead check and
  TeamChallenge's shared-fate call gate on `!NetSession.IsClient`); the host's `LoseLife`
  broadcasts `EvReset` with the branch it took (DirectRespawn / normal reset / game over).
- Client `NetApplyReset` mirrors the branch: purge ships/summons, enter the same state
  (Resetting/GameOver), let its own `UpdateResetting` run the crossfade + purge +
  `score.Load()` + respawn — i.e. the existing purge-and-replay flow (`GameScene.cs:740`).
- Host purge → `NetIdRegistry` removal → killer-less `EvDeath`s already despawn client
  puppets silently; `RevertToCheckpoint` re-runs spawners → the post-reset spawn stream
  rebuilds the client world. `recentlyRemoved` (3s) already guards against stale-snapshot
  resurrection. Lives arrive verbatim via the existing 1Hz `EvScoreSync`.
- New scene seams live on `GameScene` (`internal static GameScene NetActiveScene`, set in
  Initialize / cleared in Terminate+OnComponentRemoved) so NetSession can reach the
  private state machine without widening its API.

### 3. Pause — replicated event, triggers stay local

- Local pause triggers are already local-device-only (keyboard/pads polled from the local
  `InputHandler`; `ControlDevice.Remote` is not a pad, so the pad-disconnect force-pause
  can't fire for the remote player) — EXCEPT `TeamChallenge.Initialize`, which seats
  `Keyboard + PadOne` unconditionally: in a net session the phantom PadOne would
  insta-force-pause via `!PadConnected(0)` and squat the slot the remote puppet needs.
  Net sessions seat only the local device; the remote joins through the net layer.
- Local pause push sends `EvPause(on)`; every resume path (`Continue`, exit-confirm,
  `pausedScene_OnExit`) sends `EvPause(off)`.
- Remote pause on the receiving side: `Collection.Push()` + darkener + a "peer paused"
  hint, no interactive menu (you can't navigate the peer's menu). Local and remote pause
  can overlap: the world unfreezes only when BOTH are clear; the seam tracks who holds it.
- NetSession.Update ticks above the component system (`Game1.UpdateInner`), so
  heartbeats/streams keep flowing while paused — a paused host simply stops producing
  spawns/snapshot motion.

### 4. TeamChallenge tether — local first-order pull, no reconciliation

- Net session: `ShipConnector.Update` skips the rigid `SetPosition` pinning. Instead each
  peer applies a SOFT positional pull to its OWN ship only, anchored at the remote
  puppet's on-screen position: `if dist > Rest(78px): move own ship toward anchor by
  min(K * (dist-Rest), MaxPullPxPerMs) * dt`.
  First-order (no velocity state) → cannot self-oscillate; the only instability channel
  is the mutual stale-anchor loop (each side pulls toward a ~100–200ms-old image of the
  other), bounded by `K * delay < 1`. Constants picked with `tools/sim/tether_sim.py`
  (below). If it ever wobbles: SOFTEN K, never stiffen.
- Midpoint/rotation/base-sprite + lightning draw already use the two on-screen ship
  positions — staleness reads as elastic stretch, unchanged code.
- Break: local cause (enemy hit via `TakeHit`, or an endpoint died) → break locally +
  send `EvTetherBreak`; receiving a break event breaks silently (no echo). Idempotent.
- Shared fate: either side seeing < 2 ships asplodes its LOCAL ship(s) + drops the
  connector (existing `TeamChallenge.UpdateNormal`); the life decrement + reset stay
  host-authoritative via `EvReset` (client's `LoseLife` call is suppressed per §2).
- Connector creation on a net client/host must wait for BOTH ships (local + puppet) —
  `TeamChallenge_OnReset` fires before the puppet exists; net mode defers creation to
  `UpdateNormal` when 2 ships are present (pending flag armed by OnReset/reset, cleared
  by creation or break).

### 5. Verification tooling (built as part of the card)

- **`tools/sim/tether_sim.py`** — two-peer data sim of the pull rule with configurable
  one-way delay, stream cadence, interpolation lag and input patterns (drag apart, orbit,
  stop). Asserts: no oscillation (stretch envelope monotone after input stops), bounded
  stretch, convergence to rest. Picks/bakes K + MaxPull. Run offline; no browser.
- **`?netscript` fast-boot flag** — replaces the booted level's event list with a ~60s
  script exercising EVERY beat type: message, warning, background ops, checkpoint,
  music switch, unlock banner, victory. Pairs with `?net=host/join` for a two-tab run
  that hits the whole card surface in a minute (the Level1 full-run gate stays the final
  soak, run once).
- **Metrics additions** (`NetMetrics`): `localShip=0/1 remoteShip=0/1` (the 11.2 blind
  spot), plus `beatRx` (script-beat events applied), `resets`, `pauses`, `tetherBrk`.
- Full two-`NetSession`-in-one-process headless harness: NOT in this card — NetSession
  and the game services it drives (Oracle/ComponentBin/Score) are static singletons; the
  de-static refactor is its own follow-up card.

## Out of scope

- WebRTC / signaling (11.4), leave/drop UX (11.5).
- Replicating boss-internal choreography beyond what the music/background/message hooks
  carry (boss puppet fidelity is the 11.2 follow-ups card).
- Host migration, >2 peers, mid-level join.
- De-staticing NetSession for the in-process harness (follow-up card).

## Verification

1. `dotnet build` clean.
2. `python tools/sim/tether_sim.py` — assertions pass; constants baked from its output.
3. Two-tab `?level=Level1&netscript&net=host/join&aiplayer&invuln` — every beat type
   observed on the join tab; `[net]` lines clean (beatRx counting, no seq gaps, ships 1/1).
4. Death/reset: same boot minus `invuln` on one side — host wipe → EvReset → join tab
   purges + rebuilds + score restores; then victory beat coherent on both.
5. TeamChallenge two-tab: tether present, stretches gracefully (no wobble), break event
   symmetric, shared-fate death → one reset.
6. Pause: pause on one tab → other freezes with hint; resume; cross pause overlap.
7. Final smoke: plain single-player boot (no `?net`) byte-identical behaviour, zero
   console errors.
