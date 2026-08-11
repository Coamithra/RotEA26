# Card df72b051 — co-op respawn: the peer's ship appears at the wrong position, then slides

## Context

Online co-op, both players die (a full reset) and respawn. On each peer's screen the OTHER player's ship materialises at the wrong spot and then "gets sync'd" — visibly zipping across the screen to where it actually is. Reported from a real two-screen playtest.

## Root cause (measured from the code, not guessed)

While a peer's ship is dead, `NetSession.SendShipState` keeps streaming `MsgShipState` as the heartbeat with `alive=false` — and its position field is `lastTxPos`, i.e. **the position the ship died at**, repeated every 33 ms for the whole death (a reset is ~4.5 s + the respawn wait). `HandleShipState` adds every one of those samples to the interpolation buffer (`ShipStateBuffer`).

On the respawn, the first `alive=true` sample lands (the spawn point / fly-in path), `ManagePuppet` → `SpawnPuppet` places the puppet at `buffer.Newest.Pos` (correct), but the render clock then samples `InterpDelayMs` (~100 ms) **behind** the newest sample — which lands among the dead-period samples. So the puppet's first driven frames read the **death position**, and over the next ~100 ms the interpolator lerps from the death spot to the spawn point: exactly the reported teleport-then-fast-slide. The buffer's 1 s trim cannot save it — `ShipStateBuffer.Add` always keeps at least the last two samples, so the bracketing pair straddling the death gap survives whatever the gap length.

The friend/couch channel (`NetSession.Friends.cs`) is immune: a dead extra ship simply stops being streamed, and any real death gap exceeds the 500 ms `FriendTimeoutMs`, which destroys the whole channel (buffer included) before the respawn stream rebuilds a fresh one.

## Design

One receiver-side change in `NetSession.HandleShipState`: detect the **dead→alive rising edge** on `sample.Alive` (against the previous `remoteAlive`) and, before adding the first alive sample, `buffer.Clear()` + `renderMs = NaN` + `hasLastPuppetPos = false`. A new life starts from its own samples only; the interpolator can never bridge a death. Both roles run this path, so host and client are fixed symmetrically. No wire change, no protocol bump, offline byte-identical (the path only runs inside a session).

Skipping the dead samples' `Add` instead is NOT sufficient: the trim keeps the last pre-death sample, so the bridge survives. Clearing on the edge is the whole fix.

## Verification

* **`eaNetRespawnPos()` / `eval NetRespawnPos`** (`Compat/Net/NetRespawnPosTest.cs`) — DESTRUCTIVE, the `eaNetFire` rig shape, run in a throwaway `?level=Level2&invuln` boot:
  * Section 1 (pure, menu-runnable): the bridge itself as the negative control — a `ShipStateBuffer` loaded with dead-period samples at A and fresh alive samples at B must read near A at `newest − InterpDelayMs` (the pre-card behaviour, in the `PreCardTapBullets` reference idiom).
  * Section 2 (end-to-end): a real HOST session over a `NetWire`, a scripted peer: alive at A → puppet spawns near A; `alive=false` heartbeats **at A** → puppet explodes (the falling edge, `RemoteShipExplosions` +1 as the precondition); alive at B (far side of the screen) → puppet spawns and every driven position stays near B — it never reads near A or in the A→B corridor.
* **Probe** `tools/headless/probes/net_respawn_pos.txt` (`?level=Level2&invuln&noattract`), tally-pinned.
* **Mutation test**: revert the clear — section 2's never-near-A leg must go red while section 1 stays green (recorded in the probe header).
* Chrome smoke: boot, zero console exceptions.

## Out of scope

* The friend channel (immune, see above).
* Anything about WHERE the respawned ship spawns (that is `SpawnAllPlayers` / card b4a9fe60 territory) — this card is only about the puppet bridging positions across a death.
* Reordered-packet alive-flag flicker (pre-existing: `remoteAlive` is latched raw per packet; a late dead packet after the clear self-heals on the next alive one).
