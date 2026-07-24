# OwnLevel wall-nav churn (card b4972696)

## Context

The card reports that `OwnLevel` ("Base Pressure") measures 254-477 deg/s heading churn and
3.0-5.1 reversals/s, against the ~70 deg/s / ~1.3 revs/s that card f4d1721f settled Level 3's
wall navigation at -- a 4-7x gap. It hypothesises that `PlayerShip.ColumnScore`'s least-bad-column
choice is thrashing between two similar columns that `GapSwitchMargin` is not separating, because
`OwnLevel` runs `Walls(game, 2)` -- a grid that was never in f4d1721f's tuning loop.

The session constraint is **no live browser testing**, so the in-game `?aibench` / `eaAiBench.soak`
instrument is unavailable. That is not a blocker: the repo's own rule is that behaviour over time
is verified with an isolation sim, and no such tool exists for the wall navigation. Building one
is the first half of this card.

## What was measured (research, already done)

A headless bench was built that reflects into the **real** `EvilAliensWeb.dll` and drives the
actual `PlayerShip.SteerThroughWall` / `ChooseGapColumn` / `ColumnScore` / `DistanceToBlockedRow` /
`ClampIntoWallSpace` against the real `CollisionLevelMap` and the real `Wall.Setup` grids. No
Python mirror, so no drift. The game builds to plain `net8.0`, which is what makes this possible.

Ship model: `MaxSpeed` 0.33 px/ms, 29px box, the wall term alone, respawn in a clear cell on
contact. Scroll swept 0.026 - 0.31 px/ms (the real value both levels use is
`4.3 * difficultyValue / 16.667` = **0.258** at Very_Hard).

| grid | w | rows | gapSw/s | clampX/s | contacts | urgency% |
|---|---|---|---|---|---|---|
| var0 (Level 3) | 12 | 122 | 0.00 | 0.00 | 0 | 0.1% |
| var1 (Level 3) | 7 | 106 | 0.09 | 1.60 | 0 | 1.6% |
| **var2 (OwnLevel)** | 7 | 115 | **0.17** | **2.04** | **7** | **10.2%** |
| var3 (Level 3) | 9 | 179 | 0.13 | 1.09 | 10 | 1.9% |
| var4 (Level 3) | 3 | 11 | 0.00 | 0.00 | 0 | 0.0% |

**The card's hypothesis does not survive this.**

- **Gap-column switching is not thrashing.** 0.17/s on OwnLevel -- one switch every six seconds --
  against 0.13/s on Level 3's var3. A ratio of 1.3x, and nowhere near enough events to produce
  3-5 heading reversals per second whatever their amplitude.
- **Wall contacts are not elevated either**: 7 on OwnLevel vs 10 on Level 3's var3.
- The only genuinely elevated column is `urgency%` (10.2% vs 1.6-1.9%), i.e. OwnLevel's maze does
  keep a blocked row inside reach far more of the time -- the grid IS tighter, exactly as the card
  guessed structurally. But that tightness is not converting into switching or contacts.
- Sweeping the two plausible knobs changes nothing that matters: `WallScanRows` 4 -> 16 and
  `WallCrossPenalty` 4 -> 0 leave OwnLevel's contacts flat and make Level 3's var3 **worse**;
  `WallReactionMs` 420 -> 2000 (via the real `?aireact` knob) leaves contacts and `clampX/s` flat
  on every grid and only inflates `urgency%`. There is no tuning win available here.

## Why the live 4-7x is real but not about walls

The two live figures were measured on rigs that differ by more than the grid:

- The ~70 deg/s Level-3 baseline comes from **`?wallsonly`** -- `Level3.PopulateWallsOnly`, whose
  own comment says it runs the wall variations "**with nothing else spawning**". Walls, no enemies.
- OwnLevel's 254-477 deg/s is the **whole level**: `Walls(game, 2)` running concurrently with
  `SkullSpawner(0f, 2f, maze: true, ...)` and, at Very_Hard+, `StarMineSpawner(0f, 0.1f)`
  (`OwnLevel.PopulateEventList`). A continuous 2-per-second stream of maze skulls is live the
  entire time the wall is on screen.

So the comparison is walls-alone against walls-plus-a-sustained-enemy-stream. Scroll speed is
*not* a confounder -- `?wallsonly` calls the same `speedup` (4.3x) OwnLevel uses.

Independent support for reading the gap as threat-driven: **CrazyGame has no walls at all** and
posts the worst churn in the whole matrix -- `turn` 389-450 deg/s, `revs` 7.0-7.8/s -- which
web CLAUDE.md already attributes to "dodging 30 homing bullets with the sum-of-repulsions model".
OwnLevel's 254-477 sits inside that band. The known-open "sum-of-repulsions is the wrong shape for
many simultaneous threats" defect accounts for OwnLevel's numbers without the walls contributing
anything unusual.

## Design

1. **Ship the bench** as `tools/sim/aiwallnav/` -- a `net8.0` console project that references the
   built `EvilAliensWeb.dll` and drives the real wall-nav methods by reflection. Prints the table
   above; `--react=`/`--scanrows=`/`--scroll=` sweep the knobs through the real `DebugFlags`
   properties. This is the instrument card f4d1721f never had, and it is why OwnLevel was never in
   that tuning loop.
   - Two rig honesty notes go in its README and its header comment: (a) variation 2 is parsed from
     `level3.txt` by the rig, because `Wall.Setup` reads it through browser-only `TitleContainer`
     and would otherwise silently hand back its 5x19 emergency grid; (b) the ship model is the
     wall term only -- it cannot produce `turn deg/s`, and a verdict about the whole bot still
     needs `?aibench`.
2. **No AI tuning constant changes.** The bench says there is nothing to win in the wall nav, and
   every knob tried either did nothing or regressed Level 3. Shipping a tuning change here would
   be a guess dressed as a fix.
3. **Document** the bench in `tools/CLAUDE.md` and add the finding to web CLAUDE.md's AI section
   -- specifically that the matrix's OwnLevel caveat ("its churn runs far above the ~70 deg/s the
   parent card settled Level 3 at -- OwnLevel's maze is a harder case") is comparing `?wallsonly`
   against a full level, and should not be read as a wall-nav defect.
4. **Follow-up cards** (not this card's scope):
   - Run the *matched* live experiment: OwnLevel with its spawners suppressed vs `?wallsonly`, and
     OwnLevel with walls suppressed vs full. That is the measurement that would settle it in-game,
     and it needs the browser.
   - The real open item this rolls into: sum-of-repulsions churn under many simultaneous threats
     (already visible on CrazyGame).

## Verification

- Clean `dotnet build web/EvilAliensWeb -c Debug`.
- `dotnet run --project tools/sim/aiwallnav` prints the table; re-runs are deterministic.
- **The game itself is untouched** (tools-only + docs), so `python tools/verify_il_identical.py
  --ref main` must report a byte-identical `EvilAliensWeb.dll`. That is the strongest possible
  statement that this card cannot have changed play, and it is checkable with no browser.

## Out of scope

- Any change to the AI's steering, threat field or tuning constants.
- The live `eaAiBench.matrix(['OwnLevel'], 1800, 3)` before/after -- barred this session, and with
  no behaviour change there is nothing for it to compare.
- Making `Wall.Setup` variation 2 loadable outside the browser.
