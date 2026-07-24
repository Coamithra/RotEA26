# `aiwallnav` — headless bench for the AI's wall navigation

Runs the **real** `PlayerShip` wall-navigation code against the **real** `Wall.Setup` grids with
no browser, no game loop and no mirror.

```sh
dotnet build web/EvilAliensWeb -c Debug     # the bench references the built assembly
dotnet run --project tools/sim/aiwallnav
```

```
scroll 0.258 px/ms (4.3 * Very_Hard)
grid                w  rows |  secs | gapSw/s latFlip/s clampX/s clampUp/s | contact/s  n | urgency%
-------------------------------------------------------------------------------------------------------
var0 (Level3)      12   122 |  32.2 |    0.03      0.00     0.00      0.00 |      0.00  0 |     0.8%
var1 (Level3)       7   106 |  47.9 |    0.27      0.15     0.50      0.44 |      0.00  0 |     1.9%
var2 (OwnLevel)     7   115 |  51.8 |    0.52      0.17     1.12      1.16 |      0.06  3 |    25.0%
var3 (Level3)       9   179 |  62.5 |    0.43      0.16     0.61      0.77 |      0.03  2 |     4.5%
var4 (Level3)       3    11 |  12.9 |    0.00      0.00     0.00      0.00 |      0.00  0 |     0.0%
```

`--react=<ms>` sets `WallReactionMs` (the same `DebugFlags` property `?aireact` writes);
`--grid=<n>` benches one variation; `--ladder` repeats the table at all five difficulty scroll
speeds. Deterministic — no RNG anywhere, so two runs match exactly.

**Scroll speed is the axis that decides everything here, so the bench pins it rather than
averaging.** Every real wall section in both levels runs
`Background.SetSpeed(4.3 * difficultyValue / 16.667)` — `Level3.speedup` and `OwnLevel.setspeed`
are the same expression — so the only speeds a wall is ever flown at are that five-rung ladder,
0.090 (Easy) to 0.310 (Inzane). The default table is the Very_Hard rung, which is where the AI
matrix is measured. (`Level3.popTestSlow`'s `0.43 * difficultyValue` is **not** a wall speed: it
is `?wallpoptest` only and its own comment calls it "10% of the normal wall-section speed".)
An earlier revision averaged a sweep that included it, which weighted ~63% of all sampled ticks
onto a speed nothing in play uses — per-run duration is `distance / scroll`, so a slow rung
silently dominates any pooled average. If you add rungs, print them as rows; do not pool them.

`contact/s` is the comparable figure, not `n`: the grids differ ~2x in length (var4 is 12.9s of
scroll, var3 is 62.5s), so raw counts across rows compare different exposures.

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
  ramp, no smoothing), which makes it *more* responsive than the real ship. Read `contact/s` as an
  optimistic floor, not a prediction.
- **The modelled ship's Y only ever increases.** The wall term's sole Y component is
  `WallBackOff * urgency`, which points down, so once urgency first fires the ship sinks to
  `600 - ShipHalf` and spends the rest of the section there — which is where most of the
  `urgency%` and `contact/s` sampling happens. In play the screen-edge and threat terms push back
  up; nothing here does.
- **Contact is tested on the ship's CENTRE tile**, whereas the real collision spans the whole 29px
  box (`CollisionLevelMap.TestCollisionBox`). Another reason `contact/s` reads low.
- A verdict about the bot as a whole still needs `eaAiBench.soak()` in the browser.

## Rig notes

- **Difficulty is forced to Very_Hard**, because `Wall.Setup` halves every grid at Easy/Medium.
- **Variation 2 is parsed from `level3.txt` by the bench, not by `Wall.Setup`.** Setup reads it
  through `TitleContainer`, which only exists in the browser, so outside it Setup lands in its own
  `catch` and returns the hard-coded 5×19 emergency grid — benching that would silently answer a
  question nobody asked. The parse is identical to Setup's `case 2` and is rig plumbing; the
  algorithm under test is untouched.
- **A death respawns in a CLEAR cell.** Respawning at a fixed point drops the ship back inside the
  same slab, so one death counts as dozens and the metric stops responding to anything — it read a
  flat 226 contacts across four different look-ahead depths before this was fixed. That artifact
  looked exactly like a result; if a column ever stops responding to every knob, suspect the rig
  before the game.
- **Rebuild the game after changing it.** The bench references the built DLL, so an edited
  `PlayerShip.cs` that has not been rebuilt is benched in its *old* form — silently, with a
  perfectly plausible table. A first pass of this card published numbers taken against a stale
  assembly and inverted one of its own conclusions.

## What it was built for (card b4972696)

Card f4d1721f tuned the wall navigation against Level 3's grids with only the in-browser
`?aibench` to measure by, so OwnLevel's `Walls(game, 2)` grid was never in that loop. Card
b4972696 then asked whether that grid thrashes `ColumnScore`'s least-bad-column choice, and
the bench says it does not: at the real Very_Hard scroll, gap-column switching is **0.52/s on
OwnLevel against 0.43/s on Level 3's var3** — 1.2×, one switch every two seconds — and the
lateral push flips sign 0.17/s vs 0.16/s. Neither is remotely enough to produce the 3–5 heading
reversals/s the card measured live.

OwnLevel's grid *is* the hardest of the five for the wall term, and the honest ratios are modest:
`clampX/s` 1.12 vs var3's 0.61 (1.8×), `clampUp/s` 1.16 vs 0.77 (1.5×), `contact/s` 0.06 vs 0.03
(2×, on 3 raw contacts vs 2). The one big gap is `urgency%` — **25.0% vs 4.5%** — i.e. the maze
really does keep a blocked row inside reach far more of the time, it just is not converting that
into proportional churn. `--react=2000` moves `urgency%` and `clampX/s` around but leaves
`gapSw/s`, `latFlip/s` and contacts unchanged on every grid, so there is no tuning win in the
look-ahead either.

So the live 4–7× gap is not the wall navigation. It is a rig difference: the ~70 deg/s Level-3
baseline comes from `?wallsonly`, which by its own comment runs the wall sections "with nothing
else spawning", while OwnLevel's 254–477 deg/s is the full level — walls plus a continuous
`SkullSpawner(0f, 2f, maze: true)` and a `StarMineSpawner`. See
`web/EvilAliensWeb/CLAUDE.md` → the AI section.

`WallScanRows` and `WallCrossPenalty` were also swept during that investigation, but they are
`private const` with no `DebugFlags` override, so **that sweep needed an edit-and-rebuild of
`PlayerShip.cs` and cannot be reproduced with this tool as shipped**. Promote them to `?ai*`-style
overrides first if you want to re-run it; do not cite the old figures.
