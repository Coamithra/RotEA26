# Net sync for asploding-bullet mini explosions (card 950bb70a)

## Context

Card: "online coop: are mini-explosions from exploding bullets synced properly? Seems random."

Answer to the question: **no, they are not synced, and "seems random" is literally the mechanism.** FirePower levels 1-4 set `asplodingbulletspercentage` (15/30/60/75%), and `PlayerShip.SpawnShot` rolls `RandomHelper.Random.Next(100)` **per bullet** to decide whether that bullet asplodes (drops a mini `Blast` on death / per bounce). The sibling roll one line up does the same for bounce+split. A remote ship's shots are deliberately respawned through the same `SpawnShot` on the observing peer (card a45b78f6: paced by the replicated cumulative shot count), so **each peer rolls its own dice** -- the RATE matches (FirePower level replicates via `MsgHudState`), but WHICH bullets pop a mini-blast differs between the two screens, bullet by bullet.

## Design

Wire-first, per the cheap-protocol ruling (the same shape as the cumulative shot counter that already rides this stream): carry the owner's roll OUTCOMES beside the count they belong to.

- **`MsgShipState` and `MsgFriendState` grow two trailing bytes** (31 -> 33): `[asplodeBits:1][bounceBits:1]`. Bit `i` = the roll outcome of the shot whose cumulative count is `ShotCount - i` (bit 0 = the newest counted shot). `NetMaxCatchUpShots` is 6, so an 8-bit ring covers every shot a packet can owe, including after loss. Protocol **v20 -> v21** (append-only + length-guarded, so the bump is the batch convention, not a forced one).
- **Owner side** (`PlayerShip`): two u8 ring fields (`netAsplodeBits`/`netBounceBits`), shifted inside `SpawnShot` beside the rolls they record, reset with `NetShotCount` in the per-life block. Both counts (ship-local and the per-slot wire counter) advance together per shot, so bit positions relative to "newest shot" survive the `AdvanceTxShotCount` mapping and ship swaps unchanged.
- **Receiver side**: `NetApplyRemoteState` gains the two ring bytes. When it spends an owed shot, that shot's distance from the newest is exactly `netShotsPending` after the decrement, so its rolls are `(bits >> netShotsPending) & 1`. `SpawnShot` gets a forced-rolls overload: the puppet applies the owner's outcome and never touches the shared RNG for it. A backlog deeper than the ring (pending > 8, unreachable in practice at 18 shots/s vs 60 Hz spend) reads as "no roll" -- same spirit as the resync bound.
- `ShipSample` carries the two bytes; both friend-stream directions (couch + AI ships) get the identical treatment for free via the shared `NetApplyRemoteState`.

Residuals, stated: the mini's POSITION on the observer is still where the re-fired bullet's own collision landed (interpolated world, ~100 ms behind) -- same-bullet, near-same-place, not pixel-identical. Post-first-bounce trajectories still diverge (the bounce angle re-roll and clone split angles stay local); the first hit's mini -- the common case -- is what this syncs. `asplodingbulletssize`/`bounceamount`/`bulletsSplit` stay derived from the replicated FirePower/Range levels (worst case one ~100 ms HUD packet stale).

## Verification

- `eaNetFire()` (`NetFireTest`) gains roll legs: the wire round trip, an end-to-end leg (owner rolls, puppet's bullets carry identical flags bullet-for-bullet, read back via new internal `Bullet` accessors), a loss leg (rolls stay exact when packets drop), and the pre-card re-roll behaviour as the negative control shape.
- `eaNetWire.test()` ship/friend codec legs updated for the new bytes.
- Probe: `tools/headless/probes/net_single_tap.txt` tally updated; run the full probe suite.
- Chrome smoke: boot, zero console exceptions.

## Out of scope

- Syncing bounce trajectories / clone split angles (would need per-bounce events; cosmetic divergence accepted -- the bullets are a re-fired simulation by design).
- Seeding or restructuring the offline RNG path (offline behaviour byte-identical).
