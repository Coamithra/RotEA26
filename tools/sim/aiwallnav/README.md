# `aiwallnav` — headless bench for the AI's wall navigation

Runs the **real** `PlayerShip` wall-navigation code against the **real** `Wall.Setup` grids with
no browser, no game loop and no mirror.

```sh
dotnet build web/EvilAliensWeb -c Debug     # the bench references the built assembly
dotnet run --project tools/sim/aiwallnav
```

```
grid                w  rows | gapSw/s  latFlip/s  clampX/s  contacts | urgency%
--------------------------------------------------------------------------------
var0 (Level3)      12   122 |    0.00       0.00      0.00         0 |     0.1%
var1 (Level3)       7   106 |    0.09       0.03      1.60         0 |     1.6%
var2 (OwnLevel)     7   115 |    0.17       0.07      2.04         7 |    10.2%
var3 (Level3)       9   179 |    0.13       0.09      1.09        10 |     1.9%
var4 (Level3)       3    11 |    0.00       0.00      0.00         0 |     0.0%
```

`--react=<ms>` overrides `WallReactionMs` through the real `?aireact` `DebugFlags` property;
`--grid=<n>` benches one variation. Deterministic — no RNG anywhere, so two runs match exactly.

## How it can be drift-free

`EvilAliensWeb` targets plain `net8.0` (the BlazorWebAssembly SDK does not change that), so a
console host can load the shipped assembly and reflect into it. The bench therefore calls
`SteerThroughWall`, `ChooseGapColumn`, `ColumnScore`, `DistanceToBlockedRow` and
`ClampIntoWallSpace` **themselves**, against the real `CollisionLevelMap`. There is no copy of the
algorithm in this directory to fall out of sync — unlike the Python sims next door, which mirror
their subject and have to be kept honest by hand.

Two consequences worth knowing:

- It binds private members by name and **refuses to start** if any of them has been renamed,
  rather than benching nothing and printing a clean-looking table.
- `PlayerShip`/`Wall` are built with `GetUninitializedObject` (their constructors want a `Game`, a
  `GraphicsDevice` and the whole service graph). Only the handful of fields the wall-nav methods
  actually read are populated.

## What it cannot tell you

- **It drives the WALL TERM ONLY.** `?aibench`'s `turn deg/s` and `revs/s` are the heading of the
  *whole* steering sum — threats, seek, screen edges, the adaptive low-pass — and none of that is
  modelled here. This bench cannot produce those numbers and must not be quoted as if it had.
- The ship model is deliberately crude (full-speed motion along the steer angle, no acceleration
  ramp, no smoothing), which makes it *more* responsive than the real ship. Read `contacts` as an
  optimistic floor, not a prediction.
- A verdict about the bot as a whole still needs `eaAiBench.soak()` in the browser.

## Rig notes

- **Difficulty is forced to Very_Hard**, because `Wall.Setup` halves every grid at Easy/Medium.
- **Variation 2 is parsed from `level3.txt` by the bench, not by `Wall.Setup`.** Setup reads it
  through `TitleContainer`, which only exists in the browser, so outside it Setup lands in its own
  `catch` and returns the hard-coded 5×19 emergency grid — benching that would silently answer a
  question nobody asked. The parse is identical to Setup's `case 2` and is rig plumbing; the
  algorithm under test is untouched.
- **Scroll is swept, not picked.** Both Level 3 and OwnLevel drive
  `Background.SetSpeed(4.3 * difficultyValue / 16.667)` = 0.258 px/ms at Very_Hard, and Level 3's
  earlier sections run the 0.43 variant. OwnLevel's grid takes *every one* of its wall contacts at
  0.258 and above and none below, so a single slow speed would have reported it as clean.
- **A death respawns in a CLEAR cell.** Respawning at a fixed point drops the ship back inside the
  same slab, so one death counts as dozens and the metric stops responding to anything — it read a
  flat 226 contacts across four different look-ahead depths before this was fixed. That artifact
  looked exactly like a result; if a column ever stops responding to every knob, suspect the rig
  before the game.

## What it was built for (card b4972696)

Card f4d1721f tuned the wall navigation against Level 3's grids with only the in-browser
`?aibench` to measure by, so OwnLevel's `Walls(game, 2)` grid was never in that loop. Card
b4972696 then asked why OwnLevel churns 4–7× more — and the answer turned out to be that it
does not, in the wall term: gap-column switching is 0.17/s (one switch every six seconds, 1.3×
Level 3's var3) and contacts are *lower* than var3's. `WallScanRows` 4→16, `WallCrossPenalty`
4→0 and `WallReactionMs` 420→2000 all leave OwnLevel flat and some of them regress Level 3.

The live 4–7× gap is a rig difference: the ~70 deg/s Level-3 baseline comes from `?wallsonly`,
which by its own comment runs the wall sections "with nothing else spawning", while OwnLevel's
254–477 deg/s is the full level — walls plus a continuous `SkullSpawner(0f, 2f, maze: true)` and
a `StarMineSpawner`. See `web/EvilAliensWeb/CLAUDE.md` → the AI section.
