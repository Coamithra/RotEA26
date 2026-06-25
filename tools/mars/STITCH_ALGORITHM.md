# Mars tile stitching algorithm

How we reassemble AI-upscaled Mars-terrain quarters into a seamless, **looping**
scrolling strip. Each source quarter was upscaled **separately** (~3.238×), so they
disagree at the edges in three independent ways — **geometry** (horizon height),
**tone** (global colour drift), and **structure** (the actual rocks differ). The
algorithm gives each failure mode its own dedicated, *loop-safe* fix.

Developed over ~13 experiments in `new_assets_raw/mars_quarters_magenta/stitch/`.
Reference implementation: `stitch_lib.py` (pairwise) + `build_strip.py` (the ring).

---

## Inputs
- `mars{1..6}_{bl,br}.png` — the 12 quarters (6 tiles × bottom-left/bottom-right),
  each the **bottom half** of a `mars{n}` tile, **upscaled ~3.238× over a solid
  magenta backdrop** (so the transparent sky → magenta, to be keyed back later).
  `mars1..5` quarters are 500×300 design → 1619×971; `mars6` is 610×300 → must be
  **un-squished** to 1976×971 (the upscaler forced 1619, changing its aspect).
- The original low-res magenta quarters (pre-upscale) — used as the **recolour
  reference** (they were cut from one panorama, so they share a consistent palette).

## Per-quarter prep
1. **Recolour → original.** Per channel, match the upscale's terrain mean+std to its
   *original* quarter's (Reinhard transfer on non-magenta pixels; force magenta pure
   afterwards). Removes each upscale's colour cast and — because the originals were
   consistent — globally tone-aligns all 12 quarters up front. (`recolor()`)
2. **Soft sky alpha (fringe-aware).** `alpha = 1 − sky_score`, where sky_score = high
   blue **AND** low min(R,G). Catches magenta **and** its cyan/green anti-alias halo,
   while sparing bright (white-ish) rocks. (`sky_score`/`alpha_from`)
3. **Terrain-fill.** Replace the sky region's RGB with the horizon colour extended
   upward, so later blends/warps never pull magenta into the terrain. (`terrain_fill`)

## Loop-closed geometric alignment (the "shear")
The separately-upscaled quarters put the horizon at slightly different heights, and a
naive per-junction shear would propagate offsets around the loop until the wrap can't
close. Instead:
- Lay the 12 quarters out in a **ring** at cumulative positions (overlap `OVL` per
  junction); build a **global per-column horizon** by sampling every quarter's detected
  horizon into strip coordinates (averaging overlaps).
- **Circularly smooth** it → one smooth target horizon `Tgt` that is a closed loop.
- **Warp each quarter** per column: shift column `x` by `Tgt(stripPos) − horizon(x)`.
  Now every quarter's horizon follows the *same* smooth global curve → all junctions
  meet, **the loop is closed by construction**, and the per-quarter shear pins nothing
  (the global curve is the shared reference). Sub-pixel, edge-replicating. (`shear_v`)

## Per-junction blend (run at all 12 junctions, including the wrap)
For each adjacent pair (`A` = left quarter's right region, `B` = right quarter's left
region) over the `OVL`-wide overlap:

1. **Poisson tone leveling.** Measure the residual low-frequency tone offset
   `offset(y) = level(B) − level(A)` (heavy blur removes texture). Add a **harmonic
   membrane** that ramps `+offset/2` into A (0 at A's far edge → +offset/2 at the seam)
   and `−offset/2` into B (−offset/2 at seam → 0 at B's far edge). The two sides now
   **meet in tone** with no step; the correction is a sub-perceptual low-freq tilt and
   the **far edges are pinned (loop-safe)**. Texture is untouched (adding a smooth field
   doesn't change gradients). This is the key fix for the "colour-flip": feather only
   *blurs* a tone offset; the membrane *removes* it.
2. **Per-row best seam + adaptive feather.** With tone matched, only *structure*
   mismatch remains. For each row find the column where A and B are closest
   (`color_diff`, blue/green-weighted for the brown spectrum) → that row's cut point
   `xbest(y)` and best-distance `dbest(y)` (both smoothed vertically to stay coherent).
   Map `dbest` through **static absolute thresholds** `D_LOW..D_HIGH` → a per-row feather
   width `0..(full overlap)`:
   - `dbest ≤ D_LOW` (rows that nearly match) → **feather 0**, a sharp cut at `xbest`
     (full detail preserved).
   - `dbest ≥ D_HIGH` (rows that truly disagree) → **wide crossfade across ~the whole
     overlap** (hides the unavoidable mismatch), centre drifting from `xbest` toward the
     overlap middle so the wide blend fits the room.
   Static (not per-tile-relative) thresholds make a near-perfect junction get ~0 feather
   everywhere and only spend feather where a row is *absolutely* bad — consistent across
   all junctions, and degrades to ~0 feather automatically with true overlap.
3. **Edge-extended composite.** Both quarters (and their alphas) are edge-extended to
   the full canvas before weighting, so a wide blend only ever mixes terrain-with-terrain
   — a soft mask can't drag the alpha below 1 and leak magenta. (The bug that taught us
   this: blurring the mask past the overlap into a still-zero region → magenta under the
   horizon.)

## Ring assembly + output
- Assemble the 12 leveled/aligned quarters into one strip of width `Σwidths − 12·OVL`,
  blending each junction with its weight field; the wrap (Q12.right ↔ Q1.left) is just
  the 12th junction, handled with **modular column indices** — so the linear strip
  **tiles seamlessly with no mirroring** (`Background.SetMars` drops `mirrorX` and the
  `realsize.X *= 2`).
- **Re-key magenta → straight alpha** (1px erode to trim the horizon halo), **pad the
  transparent top** back to the full 600-design height (these are bottom halves), split
  into `mars{1..6}` at the tile boundaries (one big texture would exceed the GPU size
  limit), and draw at **`size = 1/3.238 ≈ 0.309`** so the 3.238× detail lands on screen
  (texel:pixel ≈ 1.35 even at the 1440-tall render cap; see card discussion).

## Why each piece (failure → fix)
| Failure at a seam | Fix | Loop-safe via |
|---|---|---|
| horizon height step | global-horizon warp | one shared smooth global curve |
| colour cast drift | recolour→original + Poisson membrane | membrane far-edge pinning |
| rocks don't line up | per-row best-seam + adaptive feather | static thresholds, per-junction |
| magenta/cyan halo | fringe-aware alpha + recomposite + 1px erode | — |
| mask blur leaks magenta | edge-extended composite | — |
| wrap won't close | ring assembly, modular wrap junction | — (no mirror needed) |

## Known gaps / TODO (where to pick up)
- **Photometric loop closure — the wrap colour shift.** Geometry is loop-closed (global
  horizon warp); **tone is not**. `recolor→original` removes each quarter's *cast*, but
  small residual quarter-to-quarter differences accumulate around the ring and the total
  drift lands at the wrap (Q12→Q1) as a visible colour shift. **Fix:** a *global ring
  exposure compensation* — solve per-quarter gain/offset minimising all 12 junction
  tone-diffs *simultaneously* (distributes the residual evenly around the loop instead of
  dumping it at one seam), and/or a Poisson tone membrane solved **circularly**. This is
  the photometric analogue of the geometric loop closure already in place.
- The **per-junction Poisson tone leveling** (in the pairwise path / experiment 12-13) is
  NOT yet wired into the ring build — `build_strip.py` relies on `recolor` for tone. Wire
  it in *after* the global exposure-comp so they don't fight.
- **mars6 kept squished by choice** (hallucinated detail; un-squishing would only blur it).
  All 12 quarters treated as uniform 1619×971 → 500 design wide, `size = 1/3.238 ≈ 0.309`.
- **Only the ground (mars1-6) is rebuilt.** Sky (`clouds-background`), distant hills
  (`marshills`), foreground dust (`clouds-foreground2`) still original — TODO.
- In-game wiring not done yet: `Background.SetMars` still has `mirrorX=true` + `realsize.X*=2`;
  remove both, set the 4 (or just the mars) layer `size` to 0.309, load `tiles_out/mars{1..6}.png`.

## Files
- `stitch_lib.py` — pairwise primitives (recolor, alpha, terrain_fill, shear/warp, graph-cut,
  Poisson membrane, adaptive feather). `build_strip.py` — the 12-quarter ring build (run it
  from `new_assets_raw/mars_quarters_magenta/`, where the `hr/` upscales + magenta originals live).
  Outputs `tiles_out/mars{1..6}.png`.

## Knobs
`OVL` (overlap px), `D_LOW`/`D_HIGH` (feather thresholds, color_diff units),
`FMAX_FRAC` (max feather as fraction of overlap), horizon-smooth window, recolour
strength. Calibrate once on the real set; they generalize because the thresholds are
absolute.
