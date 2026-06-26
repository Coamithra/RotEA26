# Mars tile stitching algorithm

How we reassemble AI-upscaled Mars-terrain quarters into a seamless, **looping**
scrolling strip. Each source quarter was upscaled **separately** (~3.238×), so they
disagree at the edges in three independent ways — **geometry** (horizon height),
**tone** (global colour drift), and **structure** (the actual rocks differ). The
algorithm gives each failure mode its own dedicated, *loop-safe* fix.

Developed over ~13 experiments (the `stitch/` scratch dir, since cleaned up).
Live implementation: the `wip/00..08_*.py` pipeline (see Files). `stitch_lib.py` (kept) supplies the
`pyr_blend` Laplacian helper used by `06`; the older single-script `build_strip.py` is gone.

---

## Inputs
- `mars{1..6}_{bl,br}.png` — the 12 quarters (6 tiles × bottom-left/bottom-right),
  each the **bottom half** of a `mars{n}` tile, **upscaled ~3.238× over a solid
  magenta backdrop** (so the transparent sky → magenta, to be keyed back later).
  all 12 are **1619×971** (`mars1..5` are 500×300 design → 1619×971; `mars6` is
  610×300 but the upscaler forced 1619 too — **kept squished by choice**, see Known gaps).
- The original low-res magenta quarters (pre-upscale) — used as the **recolour
  reference** (they were cut from one panorama, so they share a consistent palette).

## Per-quarter prep
1. **Magenta-screen matte (key + despill) -- on the pristine `hr/` upscales, runs FIRST.**
   ChatGPT upscaled each quarter over a magenta backdrop that is NOT pure and differs per
   quarter (measured: G ~= 2 everywhere, but B ranges 240..254 -- `mars1_br`/`mars5_br` are
   noticeably off-blue), so we MEASURE the backdrop `M` per quarter; never assume `(255,0,255)`.
   Each anti-aliased edge pixel is `C = alpha*F + (1-alpha)*M`; we recover the true brown `F`
   and an AA `alpha` (standard green-screen tech -- the colour stays brown, alpha does the AA):
   - **coverage** `beta = clip(min(R-G, B-G)/SPAN, 0, 1)` -- the magenta axis. Brown is <= 0,
     so it is untouched; because brown is FAR from magenta the key can be LOOSE (`SPAN ~= 40`)
     and still never eat terrain. `alpha = 1 - beta`.
   - **despill** `F = (C - beta*M)/alpha` (recover brown), then pull any residual toward the
     column's clean brown (median of solid non-magenta terrain). Deep sky (`alpha ~= 0`) -> that
     clean brown, so later warps/blends never pull magenta into the terrain.
   This REPLACES the old `sky_score` soft-alpha + `terrain_fill`, which left an opaque pink rim
   (the contaminated fringe sits at `sky_score` alpha=1, its green ~= terrain) and smeared pink
   into the upward fill. Running it FIRST means recolour/exposure only ever see clean brown --
   no magenta left to shift (the recolour-changed-the-magenta-shade bug). (`wip/00_matte.py`,
   SPAN=40; verified across all 12: rim alpha ~0.03, 0 residual magenta, <=0.03% interior erosion.)
2. **Recolour → original.** Per channel, match the matted foreground's terrain mean+std to its
   *original* quarter's (Reinhard transfer; `alpha` is the terrain mask). Removes each upscale's
   colour cast and -- because the originals were consistent -- globally tone-aligns all 12
   quarters up front. (`recolor()`)

## Loop-closed tone alignment (global ring exposure comp)
The photometric analogue of the geometric loop closure below. `recolour → original`
removes each quarter's *cast*, but the loop doesn't *close* in colour: small residual
quarter-to-quarter differences accumulate around the ring (here dominated by the dark
outlier `mars6_br` = Q12, ~15-25 lum below its neighbours) and land at the wrap as a
visible step. We solve a per-quarter, per-channel multiplicative **gain** that minimises
all 12 junction tone-diffs *simultaneously*, so the residual spreads evenly around the
loop instead of piling up at one seam.
- **Measure** each junction's terrain mean on both sides over the `OVL`-wide overlap band:
  `A_j` = quarter j's right band, `B_j` = quarter (j+1)'s left band (the wrap included).
- **Solve** per channel in log-domain (gains stay linear & loop-consistent): with
  `a_k = ln(g_k)`, each junction gives `a_k - a_{k+1} = ln(B_j) - ln(A_j)` -- a circular
  least-squares; `lstsq`'s min-norm solution is orthogonal to the null space (all-ones),
  so `Σ a_k = 0` => geometric-mean gain = 1 => **overall brightness is preserved**
  (anchored by construction, loop-safe).
- **Apply** `g_k` to each quarter's terrain (magenta left pure). On the real set: RMS
  junction step 9.2 → 2.6, wrap 25.8 → 2.5. The leftover uniform ~+2.5/junction is the
  within-quarter L→R gradient one per-quarter gain can't remove -- the per-junction
  multiband blend smooths it locally at each seam. (`wip/02_exposure.py`)

## Vertical registration to original (undo ChatGPT's ground-lift)
Before any shear: ChatGPT, upscaling each quarter independently, slid the GROUND upward on
several quarters and fabricated brand-new ground at the bottom. That per-quarter vertical
displacement is the dominant cause of the bad seams. We undo it by sliding each HD quarter
vertically to best overlap with its low-res ORIGINAL (best = max silhouette IoU of the terrain
masks; the originals share one consistent panorama geometry). The fabricated bottom falls
off-frame; the exposed top is sky. (`wip/03_register.py`)
- Measured shifts were ALL down or zero (ChatGPT only ever lifted, never lowered): mars5_bl
  +94px, mars6_bl +74, mars2_bl +58, mars1_br +52, mars4_br +23; the other 7 were 0.
- This ALONE collapsed most seam steps before any shear (e.g. J8 5bl|5br -91 -> +3, J7 +81 ->
  +10, J9 +85 -> +11): once each quarter sits at its true original height, the consistent
  originals line up.

## Loop-closed geometric alignment (the edge-pixel shear)
NOTE: this REPLACES the earlier global-horizon-warp approach (still in `build_strip.py`), which
forced every column onto one smooth global curve. That flattened the natural horizon and, by
warping each column independently to the smooth target, injected the detected-horizon bumps
into the content as vertical ZIG-ZAG tearing. The shear below preserves the natural horizon.

After registration only small natural seam offsets remain. Connect them with a per-junction
LINEAR shear -- "shear the higher side DOWN":
- At each seam compare the LITERAL edge-pixel horizon: the y of the rightmost column of the
  left quarter vs the leftmost column of the right quarter. NO averaging, NO percentile -- a
  mean/percentile masks a feature sitting exactly at the edge and can shear the WRONG way (the
  bug we hit at J5: a ground-percentile moved the wrong side and WIDENED the gap).
- The higher edge (smaller y) is sheared DOWN to meet the lower (the anchor), via a linear ramp
  that is 0 at the quarter's OPPOSITE edge. A quarter higher than BOTH neighbours gets a left-
  AND a right-shear (a gentle "tent" pushing both ends down).
- Only ever shears DOWN: exposes transparent sky at the top (which we have); never lifts a side
  up (that would need bottom content we do not have). Costs a sliver of bottom terrain on the
  sheared side -- shears are small (<= ~20px).
- LOOP-SAFE by construction: every edge only moves down to meet its partner and no edge is
  chained onto an already-sheared edge, so nothing accumulates around the ring and the wrap
  closes too. (The old "naive per-junction shear won't close" warning was about chaining onto
  sheared edges; this scheme does not.)
Result on the real set: residual edge-pixel step = +0.0 at ALL 12 seams. (`wip/04_shear.py`)

## Seam blend -- multiband (all seams) then per-junction slide-and-graph-cut
Final, hand-tuned. Two passes (a third, Poisson, was tried and dropped -- see below):

1. **Multiband ALL seams FIRST** (`multiband_edges`, Laplacian pyramid). For every junction take
   the two quarters' abutting edge strips, edge-extend each across a band, `pyr_blend` with a step
   mask, and write the blend BACK INTO both quarters in place: low frequencies (tone/shading)
   cross-fade across the seam, each side keeps its own high-freq detail. So the textures entering
   step 2 are already tone-matched ("free similarity"). Edge-width = `max(120, OVL+20)` so it always
   covers the overlap the slide will use. RGB only -- alpha untouched.
2. **Per-junction slide-and-graph-cut** (`seam_blend`) on those multiband'ed textures, with a
   MANUAL per-seam OVL (this one-off): `[0,0,0,0,0,0,0, 100, 0,0, 80, 200]` (J0..J11). Deducing OVL
   algorithmically wasn't worth it -- what looks best varies a lot per seam -- so it's hand-listed.
   - `OVL=0` seam: nothing more -- step 1 already multiband'ed it; the quarters just abut.
   - `OVL>0` seam: slide the two quarters together by OVL; per ROW pick the min-`color_diff` column
     (independent per-row best, then VERTICALLY SMOOTHED to stay coherent); adaptive feather width
     from the cut cost (`D_LOW..D_HIGH -> 0..OVL`, centre drifting to mid when wide). RGB feathered.
   - **ALPHA = per-pixel MIN** (most-transparent wins): a pixel is opaque only where BOTH sides are
     terrain (intersection silhouette). This is the strong safety net -- floating semi-transparent
     terrain / horizon smears are impossible by construction (verified 0 across the strip). BUT the
     intersection silhouette DROPS the horizon where a tall feature on one side meets lower ground on
     the other (the J7 cliff / J11 step) -- that drop is repaired by the **horizon pass (step 07)
     below**, which OVERWRITES alpha + near-horizon RGB at the OVL>0 seams. So 06 owns the BODY
     (tone + rock structure), 07 owns the SILHOUETTE.
   (`wip/06_assemble.py`; `MANUAL_OVL` is there; current result lost ~380px to overlaps.)

**Poisson final tone pass -- TRIED AND DROPPED.** A 150px membrane per seam on the assembled strip
made seams WORSE: after step 1 the seams are already tone-matched, so the membrane's measured
"offset" was really left-vs-right *content* difference (different rocks), and it "corrected" a
non-existent step -> visible artefact. Code kept (`poisson_seam`) but NOT applied.

History note: every experiment between `05` and this final one had dropped REAL multiband (OVL>0
used only a low-freq tone-match, OVL=0 was a hard abut), which invalidated those interim renders.
The live flow is multiband-everywhere -> slide-cut -> (no Poisson).

## Horizon continuity -- the VERTICAL-band blend (step 07)
`wip/07_horizon.py`, runs AFTER `06`. The seams' BODIES look great, but `06`'s `min`-alpha
silhouette DROPS the horizon LINE where a tall feature on one side abuts lower ground on the other
(the J7 cliff, J11 rocky-field->dune step). This was the long-standing open issue; it's now closed.
Prototyped on `wip/horizon{1,2}.png` (two synthetic overlapping silhouettes) and verified pixel-exact
by `wip/_horizon_proto.py` + `_horizon_proto_check.py` before going to real rock.

**KEY PRINCIPLE -- the two blend families are ORTHOGONAL and must own DISJOINT regions, or they
fight:** `06`'s multiband + graph-cut + feather are HORIZONTAL bands across the vertical seam (they
own tone + rock structure in the BODY); the horizon pass is a VERTICAL band at the horizon (it owns
the SILHOUETTE + near-horizon colour). `07` only ever rewrites the OVL>0 seams' overlap columns, and
within them only the near-horizon zone -- `06`'s body is left intact and handed off to via a feather.

For each OVL>0 seam (J7/J10/J11), in its `ovl` overlap columns (the same columns `06` placed at
`pos[m]`):
- **Blended horizon (no drop).** Extract each side's top-edge `h_L(x)`, `h_R(x)` from the TRUE
  (matte) alpha; `h_blend(x) = lerp(h_L, h_R, smoothstep(w))` with `w` pinned 0..1 across the
  overlap. `w=0` at the left edge (== left quarter's private to the left) and `w=1` at the right, so
  the blended horizon equals each neighbour's EXACTLY at the join -> C0-continuous, zero drop.
- **ALPHA = union (`max`) along `h_blend`, NEVER average.** Averaging an opaque pixel with a
  transparent one yields a translucent FRINGE (the same smear `min`-alpha existed to kill); `max`
  along the blended line gives a crisp edge with no drop AND no fringe.
- **Near-horizon RGB = "carry the taller".** Above the LOWER of the two horizons only the taller
  side has real rock, so it is drawn PURE (each side weighted by `w` GATED by its in-place alpha, so
  a transparent source is never sampled -- verified: every colour pixel is backed by an opaque
  source). Both sides cross-fade only LOWER, in the band where both genuinely have ground, via a
  vertical `smoothstep` feather (`FEATHER`). No warp, no stretched/fabricated rock at the horizon.
- **Sourced from the PRE-multiband `sheared/` quarters** (not `06`'s in-memory multiband'd RGB), so
  `06`'s horizontal tone cross-fade can't bleed sky/clean-brown UP into the horizon (the anti-
  interference rule).
- **Hand off to `06`'s body** below the lower horizon via a second vertical feather (`HANDOFF`):
  carry owns above, graph-cut body owns below, they meet smoothly.
Result on the real strip: the J7 cliff / J10 / J11 steps are gone (see `horizon_out/ba_compare.png`),
the horizon reads continuous, and the pass ADDS no interior translucency or floating terrain (interior
semi-transparent pixels 21327 -> 20073 vs `06`; floaters unchanged). FINAL strip = `wip/strip_horizon.png`.

## Output + finish (DONE -- shipped in-game)
- The pipeline produces ONE looping RGBA strip, `wip/strip_horizon.png` (06 assemble -> 07 horizon),
  width `sum(widths) - sum(OVL)` (currently 19048). It tiles seamlessly with NO mirroring --
  `Background.SetMars` drops `mirrorX` and the old `realsize.X *= 2`.
- Alpha comes from the MATTE (+ per-pixel min-alpha at OVL>0 seams, then OVERWRITTEN by the step-07
  `h_blend` union silhouette at those seams); the old "re-key magenta + erode" step is OBSOLETE (no
  magenta survives). No erode needed.
- **Split (step 08):** `wip/08_split.py`-style cut of `strip_horizon.png` into **12** contiguous
  near-equal tiles (~1587-1588 px wide, each < the GPU max) -> `wwwroot/Content/GFX/MarsBG/marsloop{1..12}.png`.
  Cut location is arbitrary (the strip is seamless), so equal cuts; the tiles re-abut pixel-exact.
- **NO padding** (we chose not to pad the transparent top back to 600). Instead the half-height
  (bottom-half, 971px = 300 design) ground is drawn LOWER: in `Background.SetMars` the mars layer is
  `position.Y = 300`, `size = 1f/3.238f`, `realsize.Y = 600f` (kept full so the 300-tall band is not
  repeated vertically), `realsize.X = sum(widths)*size`, `mirrorX = false`. This reproduces the OLD
  ground's EXACT on-screen position (horizon ~design Y 467) and scale -- just a higher-res texture.
  Verified in-game (Level2 / Demo2): loads with 0 console errors, horizon continuous, no tile-edge
  seams, no drop. (Old `mars{1..6}` PNGs left in place, just unreferenced.)

## Why each piece (failure -> fix)
| Failure at a seam | Fix | Loop-safe via |
|---|---|---|
| magenta/cyan AA halo | magenta-screen matte: per-quarter MEASURED M, key + despill | -- |
| per-quarter colour cast | recolour -> original (Reinhard, alpha-masked) | originals share one palette |
| colour LOOP drift (wrap shift) | global ring exposure-comp (per-quarter per-channel gains) | circular lstsq, sum(ln g)=0 |
| ChatGPT lifted the ground | register each quarter to its original (max silhouette IoU) | originals = consistent geometry |
| horizon STEP at seam (pre-blend) | edge-pixel shear, higher side DOWN, linear ramp | each edge only moves down; no chaining |
| zig-zag / flattened horizon | (avoided) edge-pixel LINEAR shear, not per-column warp-to-curve | -- |
| tone/shading step at seam | multiband (ALL seams) low-freq cross-fade, FIRST | -- |
| structure mismatch (rocks) | per-junction slide-and-graph-cut (manual OVL) + adaptive feather | -- |
| horizon smears (floating alpha) | per-pixel MIN alpha (intersection silhouette) | structurally impossible |
| horizon DROP at OVL>0 seam (min-alpha cliff/step) | step 07: `h_blend` union silhouette + carry-the-taller, sourced pre-multiband | w pinned to overlap edges -> C0 at joins |
| horizon FRINGE (translucent edge) | step 07: alpha = `max` (union), never average | -- |
| horiz. blend bleeding into horizon | step 07 sources pre-multiband + owns disjoint (vertical) region | -- |
| wrap won't close | non-accumulating edge shear + modular wrap | no mirror needed |

## Known gaps / TODO (where to pick up)
- **HORIZON CONTINUITY -- SOLVED (step 07).** The min-alpha intersection silhouette dropped the
  horizon LINE at the OVL>0 seams (tall feature meets lower ground). Fixed by `wip/07_horizon.py`
  (see "Horizon continuity" section above): blended-horizon `h_blend` + union (`max`) alpha +
  carry-the-taller near-horizon colour, sourced pre-multiband so the horizontal blend can't bleed
  into it. The J7/J10/J11 cliffs/steps are gone; tune via `FEATHER` / `HANDOFF`. Remaining horizon
  nuance to watch on a per-seam basis: a slight tone boundary where carry hands to body, or where a
  side's horizon dips far below `h_blend` (widen `FEATHER`/`HANDOFF` if it shows).
- **Finish + in-game wiring (DONE):** split into 12 `marsloop{1..12}` tiles, dropped `mirrorX` +
  `realsize.X*=2` in `Background.SetMars`, `size = 1f/3.238f`, `position.Y=300` (no padding), wired +
  verified in-game (Level2). REMAINING: verify on the LIVE host (Pages -- content paths are
  case-sensitive there; the `marsloop*.png` are lowercase under capital `Content/`), and consider
  precompiling the 12 tiles to DDS/`.rtex` (they're ~1.5 Mpx PNGs decoded on the WASM main thread at
  level preload -- add to `textures.config` + `preload/manifest.txt` if Level2 preload stutters).
- **Loop seam (J11 6br|1bl):** the originals were never a true loop, so it can't fully reconcile in
  content; multiband + OVL=200 graph-cut soften it. Accept, or hand-touch later.
- **Poisson tone pass dropped** (see blend section); a circular / content-aware version could be
  revisited if a residual tone step ever shows -- the current strip does not need it.
- **mars6 kept squished by choice** (hallucinated detail; un-squishing would only blur it). All 12
  treated as uniform 1619x971 -> 500 design wide, `size = 1/3.238 ~= 0.309`.
- **Only the ground (mars1-6) is rebuilt.** Sky (`clouds-background`), distant hills (`marshills`),
  foreground dust (`clouds-foreground2`) still original -- TODO.
- **`stitch_lib.py`** stays as a live dependency (`06` imports `pyr_blend`); the OLD single-script
  `build_strip.py` + `stitch/` + `tiles_out/` scratch have been removed. Fold the `wip/00..08` flow
  into a clean build script before shipping.

## Files (the live pipeline, in order)
Lives in `tools/mars/`. Run all from there (the scripts are in `wip/`; `hr/` upscales, the low-res
`mars{n}_{bl,br}.png` originals, and `stitch_lib.py` sit at `tools/mars/`):
- `wip/00_matte.py`     -- magenta-screen matte (key + despill) on `hr/`   -> `wip/matte/`
- `wip/01_recolor.py`   -- recolour matted foreground to originals         -> `wip/recolor/`
- `wip/02_exposure.py`  -- global ring exposure-comp                       -> `wip/exposure/`
- `wip/03_register.py`  -- vertical register to originals (undo GPT lift)  -> `wip/registered/`
- `wip/04_shear.py`     -- edge-pixel shear-higher-down (0 OVL)            -> `wip/sheared/`
- `wip/06_assemble.py`  -- multiband ALL seams -> per-junction slide-and-graph-cut (MANUAL_OVL);
                          BODY result = `wip/strip_graphcut.png` (looping RGBA) + `_gc_full/_overview/_junctions`
- `wip/07_horizon.py`   -- HORIZON pass: at OVL>0 seams, overwrite alpha (h_blend union) + near-horizon
                          RGB (carry-the-taller, pre-multiband) -> FINAL `wip/strip_horizon.png`
                          (+ `horizon_out/ba_compare.png`, before/after junction crops, sanity report)
- `wip/08_split.py`     -- cut `strip_horizon.png` into 12 `marsloop{1..12}` tiles -> wwwroot Content
- `wip/05_multiband.py` -- earlier 0-OVL-everywhere multiband (now = step 1 of `06`; kept for ref)
- `wip/_horizon_proto.py` + `wip/_horizon_proto_check.py` -- the step-07 prototype on the synthetic
  `wip/horizon{1,2}.png` (hand-made test silhouettes; kept). `stitch_lib.py` -- `pyr_blend` dep of `06`.
- The exploratory one-off helpers (`abut`, `_mb_zoom`, `_j5`, `diag_*`, `exp_*`, old `03_*`/`06_graphcut`)
  and all regenerated intermediates were removed in cleanup; re-run `00..08` to rebuild any of them.

## Knobs (final)
- matte: `SPAN` (~40), `KPULL`
- exposure: solved (no knob)
- register: IoU search range (orig px)
- shear: literal edge pixel (K=1) -- no knob
- assemble (`06`): `MANUAL_OVL` per seam (currently `[0,0,0,0,0,0,0,100,0,0,80,200]`); multiband
  edge-width `max(120, OVL+20)`, pyramid levels 5; feather `D_LOW/D_HIGH/FMAX`; seam vertical-smooth 25
- horizon (`07`): `FEATHER` (~110, carry->blend vertical fade), `HANDOFF` (~90, carry->body fade),
  `H_SMOOTH` (~9, horizon-curve smoothing). Operates only on the OVL>0 seams; must share `MANUAL_OVL`
  with `06` (asserts strip width matches).
