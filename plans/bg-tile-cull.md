# Fix `BackgroundImage` tile-cull: `LogicalWidth` in the Y test, missing `*size` in mirrorX

Card `5216412d`. Branch `fix/bg-tile-cull`.

## Context

`BackgroundImage.DrawBackground` decides per tile whether it overlaps the 800x600 design screen
before drawing it. A tile at `(x, y)` occupies `[x, x + W*size) x [y, y + H*size)`, so the correct
test is

```
x + W*size >= 0   &&   x < 800   &&   y + H*size >= 0   &&   y < 600
```

The shipped test gets two terms wrong. Both are pre-existing (spotted while renaming locals for
card `d26f0681`, which could not fix them — that card's guarantee was a byte-identical assembly).

**Bug 1 — `LogicalWidth()` in the VERTICAL term.** The Y half of the test measures the tile's
*width*: `position.Y + offset.Y + tiles[i,j].LogicalWidth() * size >= 0f`. For a non-square tile
this tests the wrong extent.

**Bug 2 — the mirrorX block drops `* size`.** Its tests read `... + tiles[c,r].LogicalWidth() >= 0f`
with no scale factor, so at any `size != 1` the mirrored half culls against *unscaled pixel*
extents. This affects both the X and the Y term of both mirrorX tests.

**There are FOUR cull tests, not two** — the card names two defects, but they are spread over four
copy-pasted conditions:

| # | site | bug 1 (Y uses W) | bug 2 (no `*size`) |
|---|---|---|---|
| 1 | main pass (`BackgroundImage.cs:163`) | yes | — |
| 2 | mirrorY inside the main pass (`:173`) | yes | — |
| 3 | mirrorX pass (`:191`) | yes | yes (X and Y) |
| 4 | mirrorX + mirrorY (`:201`) | yes | yes (X and Y) |

Four hand-maintained copies of one predicate is *why* they diverged, so the fix collapses them to
one shared helper rather than patching four conditions in place.

### Which way each bug errs (the card says "generous"; that is only half true)

- Bug 1 with a **wide** tile (`W > H`) tests a larger extent than real -> draws tiles that are
  off-screen: harmless over-draw.
- Bug 1 with a **tall** tile (`W < H`) tests a smaller extent -> **culls tiles that are visible**:
  a missing strip along the top edge.
- Bug 2 at **`size > 1`** tests a smaller extent -> **culls visible tiles**.
- Bug 2 at `size < 1` -> harmless over-draw.

So the failure mode is a *missing* tile, not a stray one, in exactly the two cases the card
predicts will arrive with a mirrored or non-square background.

### What it does today (measured, not assumed)

- **`mirrorX` / `mirrorY` are never set `true` anywhere in the codebase** (the sole assignment is
  `mirrorX = false` in `SetMars`, with a comment). Sites 3 and 4 are dead code, so **bug 2 is
  currently unreachable**.
- **Every live tile is square or wider than tall**, so bug 1 currently only over-draws:

  | texture | logical | used by |
  |---|---|---|
  | `756`, `756-v3/4/5/6/8` | 512x512 | alien base floor + its switches |
  | `2331-v5` | 512x512 | alien base fog (x2) |
  | `Starfield2` | 1024x768 | holodeck sim stars (size 1.5 and 2) |
  | `grid3` | 30x30 | holo grid (size 2.4 and 1.5) |
  | `clouds-background`, `clouds-foreground2` | 1024x600 | Mars sky / foreground |
  | `marshills1/2/3` | 1000x600 | Mars parallax ridges |
  | `marsloop1..12` | 1587x971 (1588 on 2,5,8,11) | Mars ground `[12,1]` |

- Running the real tiling loops over every one of those configurations, **the fix drops no visible
  tile anywhere** and changes exactly one layer:

  | layer | draws/frame before | after |
  |---|---|---|
  | Mars ground `[12,1]`, size `1/3.238`, `realsize.Y` forced to 600 | 6 | **3** |
  | every other layer | unchanged | unchanged |

  The Mars ground is the only layer whose `realsize.Y` (600) is not its tile height (971 * 1/3.238
  = 299.876), which is what makes its Y test non-vacuous. The three removed draws are the
  `cursor.Y = -300` row, spanning `y = [-300, -0.124)` — entirely above the screen. (For every
  `[1,1]` layer `origin.Y >= -realsize.Y = -H*size`, so `y + H*size >= 0` is trivially true and the
  Y term never culls at all, before or after.)

So: **no visual change in any shipping configuration**, three wasted sprite draws/frame removed on
Mars levels, and the latent correctness bug closed before a mirrored or tall background lands.

## Design

### `Game/EvilAliens/BackgroundImage.cs`

One private helper, four call sites collapsed onto it:

```csharp
// A tile at (x,y) covers [x, x+w*size) x [y, y+h*size); it needs drawing only if that
// overlaps the 800x600 design screen. Kept as ONE predicate because the four copies this
// replaced had drifted: two measured the tile's WIDTH along Y, and the two mirrorX ones
// had lost the * size factor entirely.
internal static bool TileOnScreen(float x, float y, int tileW, int tileH, float scale)
    => x + (float)tileW * scale >= 0f && x < 800f
    && y + (float)tileH * scale >= 0f && y < 600f;
```

Taking `(w, h, scale)` as plain numbers rather than a `Texture2D` is deliberate: it makes the
predicate a pure function of five floats, so the property test below needs no graphics device, no
content and no textures.

Each of the four conditions becomes e.g.

```csharp
if (TileOnScreen(position.X + offset.X, position.Y + offset.Y,
                 tiles[i, j].LogicalWidth(), tiles[i, j].LogicalHeight(), size))
```

`LogicalWidth()/LogicalHeight()` stay correct per the padded-DXT rule (this is PIXEL-space math, so
logical is right — see web `CLAUDE.md`).

### Verification seam — `BackgroundImage` cull counters

A change with **no visible delta** cannot be proven by a screenshot, so the tile decisions are made
readable as data. A static, off-by-default counter block incremented at the same leaf:

```csharp
internal static bool CullTrace;                      // armed by eaBgCull only
internal static int TracedDrawn, TracedCulled, TracedOffScreen;
```

`TracedOffScreen` counts tiles that were *drawn* but have zero on-screen area — the waste metric
that shows Mars going 6 -> 3. Off by default the cost is one static bool test per tile (the
`?binlog` / `?walltrace` / `LoadProfiler` idiom).

### `Compat/BgCullTest.cs` (new) — `eaBgCull()`

Two parts, printing `PASS`/`FAIL` in the `eaNetSim.test` / `eaFps.test` / `eaBinTest` style:

1. **Property test over the real predicate.** Sweep tile shapes (square, wide, tall), scales
   (0.3, 1, 1.5, 2.4) and positions across and beyond both screen edges, and compare
   `TileOnScreen` against an independently computed rectangle-vs-screen intersection. The
   invariant that matters is **soundness — a tile that intersects the screen is never culled**;
   it is precisely what the old expression violates for a tall tile or a mirrored one at
   `size != 1`, and it needs no loop duplication to state. Also reports tightness (culls
   everything fully off-screen bar the zero-overlap touching case, see Out of scope).
2. **Live layer census.** Arm `CullTrace` for one frame of the running background and print
   per-frame `drawn / culled / offScreen`. This is the integration evidence: it goes through the
   real `Draw`, so it proves all four call sites route through the fixed predicate, and it is
   where the Mars 6 -> 3 shows up in the actual pipeline.

Wired through `Compat/DebugInput.cs` as `eaBgCull()`, console-only (no URL flag — this is a QA
probe, not a boot mode).

## Verification

- `dotnet build -c Debug` clean. **The IL oracle does NOT apply** — this is a real behaviour change.
- `eaBgCull()` **on the pre-fix code**: the property test must FAIL on the tall-tile and
  mirrored-at-`size!=1` cases. That failure is the proof the bug is real; capture it before fixing.
- `eaBgCull()` **on the fixed code**: property test PASS; live census shows Mars ground
  `drawn 3` (was 6) with `offScreen 0` for that layer.
- **No visual regression**, at a parked tile boundary so the comparison is meaningful
  (`?bgfreeze=<designX>` — the layers scroll at six speeds otherwise): screenshot Mars, alien base
  and the holodeck before vs after; they must be pixel-identical.
- Real Chrome, zero console exceptions; final smoke boot of a Mars level.

## Out of scope

- **The `>= 0` boundary.** A tile whose right/bottom edge lands exactly on 0 passes the test and is
  drawn with zero on-screen area — 5 of the alien base's 9 tile draws per frame are this case. It
  is wasted work, not incorrectness, and tightening `>=` to `>` is a separate behaviour change with
  a float-equality edge. **Follow-up card.**
- Mirrored / non-square backgrounds themselves — this card only makes the cull correct for when one
  arrives.
- The `UpperDiv` repeat counts in `Draw` (they also over-cover by one), and the holo grid's 150
  tile draws/frame.
