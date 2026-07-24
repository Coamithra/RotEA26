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
contact. Scroll is PINNED at the real Very_Hard wall speed: every real wall section in both levels
runs `Background.SetSpeed(4.3 * difficultyValue / 16.667)` (`Level3.speedup` and
`OwnLevel.setspeed` are the same expression) = **0.258 px/ms**. `Level3.popTestSlow`'s
`0.43 * difficultyValue` is NOT a wall speed -- it is `?wallpoptest` only and its own comment
calls it "10% of the normal wall-section speed".

| grid | w | rows | gapSw/s | latFlip/s | clampX/s | clampUp/s | contact/s | urgency% |
|---|---|---|---|---|---|---|---|---|
| var0 (Level 3) | 12 | 122 | 0.03 | 0.00 | 0.00 | 0.00 | 0.00 | 0.8% |
| var1 (Level 3) | 7 | 106 | 0.27 | 0.15 | 0.50 | 0.44 | 0.00 | 1.9% |
| **var2 (OwnLevel)** | 7 | 115 | **0.52** | **0.17** | **1.12** | **1.16** | **0.06** | **25.0%** |
| var3 (Level 3) | 9 | 179 | 0.43 | 0.16 | 0.61 | 0.77 | 0.03 | 4.5% |
| var4 (Level 3) | 3 | 11 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.0% |

**The card's hypothesis does not survive this.**

- **Gap-column switching is not thrashing.** 0.52/s on OwnLevel against 0.43/s on Level 3's var3
  -- 1.2x, one switch every two seconds. The lateral push flips sign 0.17/s vs 0.16/s. Neither is
  remotely enough to produce 3-5 heading reversals per second, whatever their amplitude.
- **OwnLevel's grid IS the hardest of the five, but by modest ratios**: `clampX/s` 1.8x var3,
  `clampUp/s` 1.5x, `contact/s` 2x (on 3 raw contacts against 2). None of these approach 4-7x.
- The one genuinely large gap is `urgency%` -- **25.0% vs 4.5%** -- so the maze does keep a
  blocked row inside reach far more of the time, exactly as the card guessed structurally. It
  simply is not converting that into proportional churn or contacts.
- `--react=2000` (the real `?aireact` knob) shifts `urgency%` and `clampX/s` around but leaves
  switching, sign flips and contacts unchanged on every grid. No tuning win in the look-ahead.

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
   above; `--react=<ms>` writes the same `DebugFlags` property `?aireact` does, `--grid=<n>`
   picks one variation and `--ladder` repeats the table at all five difficulty scroll speeds. This is the instrument card f4d1721f never had, and it is why OwnLevel was never in
   that tuning loop.
   - Two rig honesty notes go in its README and its header comment: (a) variation 2 is parsed from
     `level3.txt` by the rig, because `Wall.Setup` reads it through browser-only `TitleContainer`
     and would otherwise silently hand back its 5x19 emergency grid; (b) the ship model is the
     wall term only -- it cannot produce `turn deg/s`, and a verdict about the whole bot still
     needs `?aibench`.
2. **No AI tuning constant changes.** The bench finds no churn ratio worth chasing and no
   look-ahead setting that improves one. Shipping a tuning change here would be a guess dressed
   as a fix. (`WallScanRows`/`WallCrossPenalty` were also swept, but they are `private const`
   with no `DebugFlags` override, so that sweep needed an edit-and-rebuild and is NOT reproducible
   with the shipped tool -- its figures are deliberately not quoted anywhere.)
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
