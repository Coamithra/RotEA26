# The AI player: everything the port does differently from 2008

Derived by reading `src_decompiled/EvilAliens/PlayerShip.cs` (`DoAIMove` 619–1166, `DoAIFire` 508–618) side by side with this branch's `Game/EvilAliens/PlayerShip.cs`. Every claim below was checked in both sources, not quoted from docs. State is as of branch `fix/orchbench-iterative-rep1` (iterative rep 1) — the two items marked **[this session]** are that run's own changes.

The 2008 baseline in one paragraph: each tick the bot sums forces into one vector and flies it raw. Threats within 150px push it away on a plateau curve (`4·(1−t²)`, still 75% strength at half range), with a per-seat dodge-angle twist. Powerups, a blast cluster, a JunkBoss or a partner-to-dock-with become the one destination (pull 0.8, powerups also get the same 150px plateau as a near-field); otherwise it parks at a fixed station. Screen edges push back with the same plateau. Walls get a per-tick left/right shove plus a hard clamp. If the total is ≤ 0.2 it stops. It fires at the nearest on-screen enemy of a fixed type list, straight at its current position ± π/12 random spread, when within bullet range.

## Removed / restored toward 2008

| what | 2008 | port had | now |
|---|---|---|---|
| Steering low-pass (`aiSteer`, 90ms/15ms) | none — raw sum every tick, no persistent steer state | blends each tick's sum into a ~90ms memory | **[this session] killed** — baked to 0; `?aismooth=`/`?aismoothurgent=` restore it as the A/B arm |
| Whole-sum park floor | 0.2 | was raised to 0.95 (a veto that deleted every destination) | back at 0.2 since card ada9e839, but restructured — see next section |

The low-pass was the port's anti-jitter device (near-cancelling forces spin the raw heading). Its memory also kept the ship thrusting ~125ms past any force that switched off — measured as the veer-past-and-return on every level-entry arrival; with it gone the same approach parks dead at 6px.

## Restructured: how the forces combine

2008 summed everything into one vector and applied one 0.2 floor at the end. The port splits the families: repellents (threats, beams, edges, escapes) accumulate separately and are zeroed as a group if their resultant is ≤ 0.2 (opposing pushes cancelling to a noise-direction vector was the "rattles between two walls" bug); attractors are never floored and each stops inside its own arrival deadzone instead. A second 0.2 noise floor catches the combined leftovers.

The seek arrival deadzone is 15px (2008: 10). The 10 was rejected because it sits under the ship's 11.3px stopping distance, so the ship coasts out the far side and re-triggers.

Station points are the same coordinates; multi-ship spacing indexes by seated ordinal instead of raw player number (2008's `player+1` puts a sparse co-op roster's ship off-screen).

## Changed shape: the threat field curve

**This is the single biggest behavioural difference that still stands.** Every threat repellent (and the beam term) runs `max·(1−t)^3` — a spike that is down to 12% strength at half range. 2008 ran `max·(1−t²)` — a plateau still at 75% there. Concretely at 75px from a beam edge: 2008 pushes 3.0, the port pushes 0.5, against the same 0.8 seek. The field's *range* also scales with the threat's size now (`150 + 1.8·half-extent`; per-difficulty base 118–150, Very_Hard = 150) where 2008 was a flat 150 for everything. The classic family is reachable: `?aifieldcurve=classic`, per-type `?aiasteroidcurve=`, flat range `?aiasteroidflatpx=`.

The beam (Lazer) term specifically: same 150px anchor and strength 4 as 2008, but the spike family, and 2008's dodge-angle twist on the push direction is kept. A windup sidestep exists but is baked OFF (`?ailazerdodge=`).

## New repellents (no 2008 counterpart)

- **Velocity cones**: every mover projects a mesa-shaped repellent along its own velocity — full strength across the swept body, transverse-only push, length scales with speed. Plus an asymmetric **lane wedge** closing the gap between an edge-hugging sweep and that screen edge. A guard refuses teleport-shaped paths (pool-recycle artifacts read as 40px/ms movers).
- **Closest-approach evade** (`EvadeMovingThreat`, 700ms lead): fast movers are dodged by where they are going, not where they are; radial repulsion from a screen-crosser pushes the ship *along* its path.
- **Top-edge danger band** (170px, strength 20): UFOs spawn there; the stock edge push (4) loses to a lane escape (18), so the port added a band strong enough to compete. 2008 had nothing above the generic edge push.
- **Spider-boss lane escapes**: hand-rolled downward/left escapes out of the boss's fixed lanes and landing column (`?ailaneescape=`).

## New attractors

- **Boss approach**: a level-halting boss becomes the destination, with a per-tick weight solved so the pull and the boss's own repellent cross exactly at bullet range. 2008 had no boss destination at all — the bot hovered at its station and the level stalled until the boss drifted into range.
- **[this session] Powerup edge yield**: while the chosen destination is a powerup inside an edge band, that band's push stands down (all four edges + the top band; `?aipowerupyield=0` reverts). The powerup *pull* itself — destination at 0.8 plus the 150px plateau near-field — is byte-for-byte the 2008 shape; what changed is only that the edge push no longer out-votes it on its own doorstep.

## Firing

- Target pick: 2008 took the nearest of an explicit type list. The port's list is a predicate (`IsAiShootable`) mirroring `Bullet.CollidesWith`, deliberately minus `SpiderBoss` (bullets deflect), the helper mothership (fake-killable, and it kills the boss for you) and `Asteroid` (splits, no combo); a level-halting boss's distance is discounted so it outranks its own trash.
- **Big-UFO sparing** at the spider boss: the roomiest big UFOs are left alive so the boss walks into their beams. Pure port addition — 2008 shot everything.
- Unchanged: fire range (`0.78 · bulletlifetime`), aim straight at current position (no lead), ±π/12 spread at Very_Hard, JunkBoss aimed exactly, bomb thresholds (10/7/4 blastables in range by bombs held), `isBlastable`.
- **Per-tier skill**: aim spread and field base now widen below Very_Hard (22.5° at Easy). 2008 was one fixed skill at every difficulty.

## Wall navigation

- **Approach steer replaced.** 2008 re-decided left-vs-right every tick via `findNextTileOnMap`, probing ~6.6px ahead, shoving at a per-seat strength (player 0/1/2/3 = 8/4/6/10). The port grades every column by clearance/travel/crossings (`ColumnScore`), commits to a gap with a switch margin, looks ahead by *time* (420ms × real closing speed) and steers proportionally. The 2008 algorithm is still runnable verbatim: `?aiwallnav2008=1`.
- **Hard clamp kept.** `ClampIntoWallSpace` *is* the 2008 emergency block (same probes at ~13.8px, same slam); the port only renamed constants (42 vs 41.666668 ms).

## World model

`Oracle.GetBaddies` is the AI's entire world; the port added the types whose absence made the bot blind — `BrainBoss`/`FakeBoss` as targets (Level 3 stalled at a boss it could not see), `SpiderBoss`/`PlasmaBall` as hazards. Threat reaction is filtered by `IsAiThreat` (mirrors what can actually kill the ship) — 2008 dodged anything collidable, harmless parachutes included. Prediction uses `ObservedVelocity` (measured position deltas, port infrastructure); 2008 had no velocity estimate at all.

## Unchanged, verified identical

Per-seat dodge angles (±π/16, ±π/6); the four screen-edge pushes (plateau, 150px, strength 4 — modulo the powerup yield gate above); bottom edge 560 with a Floor; station coordinates; seek weight 0.8; `wantsToTakePowerup` (including the progress>60% refuse-other-types rule); the blast-cluster and JunkBoss destinations; partner-docking seek; `doAIBomb`.

## Diagnostics only (no behaviour)

`?aibench` counters, `?aiseeklog` **[this session]** seek attribution, the `?ai*` override flags themselves (null ⇒ baked consts, byte-identical shipped build).
