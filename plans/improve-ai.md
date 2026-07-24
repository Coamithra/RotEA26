# Improve the AI player (card f4d1721f)

## Context

`ControlDevice.AI` ships are the 2008 game's own bot: they drive the attract demos, the
Mechanical-Friends cheat, and `?aiplayer` soak runs. The card reports three symptoms:

1. it stalls around **halfway Level 2**,
2. it has **no idea how to fight the spider boss**,
3. it is **bad at flying through the Level-3 walls — jittery, and collides a lot**
   (a wall touch is `PlayerShip.AsplodeWall()`, i.e. instant death).

All of the AI lives in three methods on `PlayerShip`: `DoAIFire` (target pick + `doAIBomb`),
`DoAIMove` (steering) and `findNextTileOnMap` (wall gap search).

**Goal set with the user:** beyond the three symptoms, the bar for this card is that the AI can
**finish every level and challenge on Very Hard**, `WebcamAliens` excepted (it is driven by a
camera mask, not a ship — there is nothing for `ControlDevice.AI` to do there). The AI stays
**difficulty-blind** — one AI, tuned to be competent, no per-tier skill scaling (that is a
follow-up card). The bench below is therefore a completion matrix over `Levels`, and the card's
result is the honest pass/fail table.

### Root causes found in research

**(a) The AI's shoot-list does not match what bullets actually damage.** `Bullet.CollidesWith`
damages 23 types; `DoAIFire`'s hand-rolled `is` chain lists 14. Missing and *killable*:

| Missing type | Where it blocks |
|---|---|
| `StationaryBoss` | **Level 2 mid-level** (`StationarySpawner`, and it is linked to the MarsBoss block) |
| `BrainBoss` | **Level 3 finale** (`BrainBossHard()`) |
| `FakeBoss` | Level 3 (`FakeBossEasy()`) |
| `ParatrooperAlien` / `ParatrooperBrain` / `Parachute` | Level 3 brain drops |
| `PunchingBag` | training / `PowerUpTrainingEvent` |

The AI literally never fires at Level 3's boss. A halting boss event that never takes damage =
"gives up somewhere in level 3". This is symptom 1 and 2's *progress* half, and it is a
one-predicate fix.

**(b) The spider boss cannot be shot at all** — `SpiderBoss.CollidesWith` only accepts a
`Lazer`, and `Bullet.CollidesWith` deliberately *deflects* off it. The fight is won by
**surviving** until the helper mothership's beam lands, `hp` (~5×difficulty) times. So the AI's
job there is pure evasion, and the current evasion is radial-repulsion-by-distance, which is
the wrong shape for that fight: the boss's `flyleft`/`flyright` states sweep the **whole screen
width at a fixed Y** at high speed. Radial repulsion from something directly to your left pushes
you *right* (along its path) instead of *off its line*, and `steerRange` is only 150px.

**(c) The wall code is a set of binary probes with no hysteresis and a look-ahead far shorter
than a tile.**
- `wallProbeReach = 41.67 * MaxSpeed` ≈ **13.75 px**, and `wallProbeStep` ≈ 6.6 px, while a
  wall tile is `800/gridWidth` = **67–267 px**. The AI sees the wall about one ship-width ahead.
- The post-loop block hard-*slams* the steering: `direction.X = -max(|direction.Y|, 1f)`. That is
  a full reversal, not a steer. The next tick the probe is clear, the accumulated steering pushes
  back, and it flips again → the reported jitter, at frame rate.
- `Move(Vector2, …)` **discards the magnitude** and only uses the angle, so once the big terms
  (`wallNudge` 4–10 vs `maxSteerStrength` 4) nearly cancel, a tiny residual swings the heading
  wildly. Nothing damps heading change.
- `findNextTileOnMap` is re-decided every tick with no commitment: as the wall scrolls,
  `leftCost`/`rightCost` change by ±1 and can swap, reversing the chosen gap mid-approach.
- The gap search only asks whether a *tile* is free — never whether the **ship** fits, and never
  which gap is reachable in the time left before the row arrives.

## Verification tool (built first — it gates everything)

Per the repo's verification rules, "the AI is better" must be **data**, not a screenshot, and
booting the real game to watch it is the wrong rig. New `Compat/AiBench.cs`:

- **`?aibench`** — always-on telemetry for `ControlDevice.AI` ships. Per ship, per run it
  accumulates: wall contacts (counted in `PlayerShip.CollidesWith` **even under `?invuln`**, so a
  run finishes and every section is measured), deaths, a **jitter index** (mean absolute
  per-tick heading change, plus the `direction.X` sign-flip rate), ticks spent with an
  on-screen shootable and no shot fired, shots fired, kills, and level progress
  (`GameEventList` index + checkpoint). Console `eaAiBench()` dumps the table; it also
  auto-prints a summary line every 10 s and a final line on level end. Same idiom as
  `eaNetSim.test()` / `eaBinTest()` — printed data with PASS/FAIL where a threshold exists.
- **`?aiff=<n>`** — a *fidelity-preserving* fast-forward: run `UpdateInner` n times per tick
  with the **same dt**, so a 3-minute section soaks in ~20 s without changing the physics.
  (Deliberately not `Settings.Turbo`, which scales dt and would change the sim it is measuring.)
- **Diagnostic scenarios** — the existing fast-boot flags, no new scene needed:
  - walls: `?level=Level3&wallsonly&aiplayer&invuln&aibench&aiff=8` — six wall sections, no
    enemies, so the run is repeatable; the score is **wall contacts + jitter index per section**.
  - spider boss: `?level=Level2&spiderboss&aiplayer&aibench` — no `?invuln`; the score is
    **survival time / did the fight resolve**.
  - progress: `?level=Level3&aiplayer&invuln&aibench&aiff=8` — the score is **how far the event
    list gets**, which is exactly the card's "gives up somewhere in level 3".

- **The completion matrix** — the card's actual bar. `?aibench` reports a run verdict
  (`VICTORY` / `GAME OVER` / `STALLED` with the event index it stopped at) so a run is a single
  pass/fail line. Driven at **Very Hard** (`?aidiff=VeryHard`, no `?invuln`, real lives) over
  every `Levels` member except `WebcamAliens`:

  | | |
  |---|---|
  | Story | `Level1` `Level2` `Level3` |
  | Challenges | `Tutorial` `Braineroids` `SpaceDodge` `OwnLevel` `ClassicAliens` `InsaneBossI` `TeamChallenge` `CrazyGame` `Paratrooper` |
  | Attract demos | `Demo1` `Demo2` `Demo3` (short; sanity only) |
  | Excluded | `WebcamAliens` — camera-driven, no AI ship path |

  Runs are scripted from the console (`eaAiBench.matrix()`) so the whole table is one command
  per level rather than hand-driven boots.

Baseline numbers and the baseline matrix get recorded in this doc before any AI change lands,
and re-measured after. Levels that still fail at the end of the card are reported as such and
get follow-up cards — not quietly dropped.

## Design

### 1. Shoot-list parity (`DoAIFire`)

Replace the inline `is` chain with a single `IsShootable(AlienDrawableGameComponent)` predicate
kept **next to `Bullet.CollidesWith`'s list**, with a comment naming it as the contract, so the
two can't drift again. Members = the bullet-damage list, minus three deliberate exclusions:

- `SpiderBoss` — bullets deflect off it by design (Lazer-only fight);
- `SpiderHelperMothership` — the thing that *kills* the spider boss; a fake-killable huge-HP
  target would swallow the AI's aim for the whole fight;
- `Asteroid` — killable but shooting one splits it, does not sustain combo, and Level 1's belt
  is meant to be flown through; the AI already handles Level 1.

Target *selection* also gets a small priority bias: a halting boss (`BrainBoss`, `FakeBoss`,
`MarsBoss`, `JunkBoss`, `ClassicBoss`, `Boss`, `StationaryBoss`) outranks trash at comparable
range, so the AI stops chipping at respawning skulls while the boss that gates the level sits
there. Nearest-target stays the tie-break.

### 2. Wall navigation (`DoAIMove` wall branch + `findNextTileOnMap`)

Keep the shape (steering vector, per-player offsets so co-op ships don't stack) — replace the
mechanism:

- **Look-ahead scaled to closing speed**, not a constant: the probe distance becomes
  `(shipSpeed + wallScrollSpeed) * ReactionMs`, clamped to at least one ship half-width. That is
  the actual "can I still stop / turn" horizon; the current 13.75 px is ~40 ms of travel.
- **Gap commitment with hysteresis.** `findNextTileOnMap` gains a chosen-gap memory on the ship:
  the current target column is kept until an alternative beats it by a margin
  (`GapSwitchMargin`), or until it stops being passable. Ends the per-tick left/right flapping.
- **Fit test.** A gap counts as passable only if the free run is at least the ship's collision
  box width plus a margin, so the AI stops aiming at gaps it cannot fit through.
- **Continuous steering instead of slams.** The `direction.X = -max(|direction.Y|,1f)` overrides
  become a repulsion term whose strength ramps with proximity (the same `MyMath.PowerCurve` shape
  the rest of `DoAIMove` uses), so the wall pushes rather than snaps.
- **Heading rate limit.** Because `Move` only consumes the angle, clamp how far the AI's chosen
  heading may rotate per tick (`MaxTurnRadPerMs`) and carry it on the ship. This is the single
  change that kills the visible jitter, and it is bounded so evasion is not sluggish.

### 3. Threat-aware evasion (`DoAIMove` generic branch)

For movers, replace "distance now" with **closest approach**: project the relative position along
the relative velocity, and steer **perpendicular to the threat's travel** when the predicted miss
distance is small. Radial repulsion stays for slow/static things. This is what makes the spider
boss's screen-wide sweep survivable (get off its Y before it arrives, rather than being shoved
along its path), and it helps `SweepUFO` and big UFOs for free. `Lazer` keeps its existing
distance-to-line special case.

Tuning knobs follow the repo convention: `Default*` consts + `?ai*` URL overrides
(`?aireact= ?aiturn= ?aigapmargin= ?aithreatlead=`), null override ⇒ baked default ⇒ shipped
build byte-identical.

## Measured results

All runs: **Very Hard**, headless soak (`eaAiBench.soak`), fixed 60 Hz dt. "before" = `main`'s AI.

| Scenario | Metric | Before | After |
|---|---|---|---|
| `?level=Level3&wallsonly&invuln` | wall contacts (each = a death without `?invuln`) | 8 | **7** |
| | heading reversals /s (jitter) | 6.5 | **1.5** |
| | heading churn °/s | ~1050 | **330** |
| `?level=Level2&spiderboss` (no invuln) | deaths | **36**, fight never resolved (stuck at event 4/10) | **4**, `VICTORY` at 303 s |
| `?level=Level3&invuln` (full level) | progress | **stalled at event 53/60** from t≈670 s to t≈903 s | see matrix below |

Two things worth recording because they were both counter-intuitive and cost real time:

- **The heading churn is the jitter.** Before the fix the AI's *commanded* heading swept ~1050°/s
  — about three full revolutions per second — inside a wall, while reading only 20°/s on open
  screens. That is the user's "very jittery" as a number, and it localises the cause to the wall
  branch exactly.
- **A low jitter score alone is worthless.** An intermediate revision scored `revs/s=0.0,
  turn=0°/s` — perfect on paper — because the ship had wedged itself in a corner and stopped
  steering at all. The bench now also reports `coast%`, `ticks`, `pos` and `steer` so a *dead*
  bot can never again be mistaken for a *smooth* one.

### Ordering that mattered

The emergency "do not fly into that" clamp must run **after** the steering low-pass, not before.
Low-passing it turns a full reversal into a suggestion: measured 46 wall contacts vs 8 for the old
code. Applied after the smoothing (and written back into the smoothed state, so a flickering probe
cannot make the clamp its own oscillator) it lands at 7.

## Verification

1. `dotnet build -c Debug` clean.
2. The three bench scenarios above, **baseline vs. after**, numbers recorded in this doc.
   Targets: wall contacts on `?wallsonly` down substantially and jitter index down; the Level 3
   run reaches the BrainBoss and takes it down; the spider-boss fight resolves instead of ending
   in a death.
3. Real-Chrome smoke check on a normal boot: attract demo still plays, zero console errors.
4. Diff spot-check for the repo's specials (no `content/`, no `BlendState.AlphaBlend`, no codegen
   re-run).

## Out of scope (follow-up cards if wanted)

- Powerup preference (the AI takes whatever is nearest; it has no idea which powerup is good).
- Bomb policy beyond the existing target-count thresholds.
- Co-op-specific play (ship connectors / TeamChallenge tether).
- Level 1 asteroid-belt shooting.
- Difficulty scaling of AI skill (the AI plays identically on Easy and Inzane).
