# Level 3 walls as 3D towers rising out of the foggy alien base

Goal: in Level 3's walls sections, make the collidable walls read as MASSIVE TOWERS standing on
the alien-base ground far below, emerging from the fog -- instead of flat floating tiles. The
gameplay plane (where the ship flies and collides) is the towers' TOPS; everything new is drawn
beneath/behind them. Collision, block layouts, difficulty halving, and the 8x8 texture scheme are
untouched.

## Why this is cheap to sell: the 0.66 coincidence

- `Wall` scrolls at `oracle.BackgroundSpeed * 1.0` (the gameplay plane).
- The alien-base GROUND layer (`GFX/Base/756`, set in `Background.SetAlienBase`) scrolls at
  `scrollspeedmodifier = 0.66`.
- Classic top-down fake perspective: pick a vanishing point VP at the screen centre `(400, 300)`
  (design space) and project each block's on-screen rect toward it by a depth factor `d`:
  `basePoint = VP + (topPoint - VP) * d`.

If `d == 0.66`, then as the top rect moves at speed 1.0 the projected base rect moves at exactly
0.66 -- **the same speed as the ground texture**. So the tower bases stay glued to the scrolling
alien-base floor with zero parallax bookkeeping: the perspective projection IS the parallax. That
one constant makes the whole illusion self-consistent (and it stays true through `slowdown`/
`speedup` events, since both wall and background scale off the same `BackgroundSpeed`).

Bonus emergence effect for free: a wall enters at the top of the screen, where the perspective
lean points away/up -- so each tower appears base-first, rising out of the fog as it approaches
the player. Exactly the asked-for feel, no special-case animation.

## Rendering approach: stacked-slice extrusion (sprite-batched, no 3D pipeline)

Do NOT use real 3D quads per block: `Quad.cs`'s port note documents that per-quad
`DrawUserIndexedPrimitives` + the batch flush is brutal on WebGL/WASM. Instead use the
"voxel slice" trick (GTA1-style top-down buildings), which stays 100% inside the existing
`SpriteBatchWrapper` batching:

For each visible `true` block, draw K interpolated copies of its 8x8 texture cell from depth
`d=0.66` (base) up to `d=1.0` (top face), each positioned/scaled by the projection above, then
draw the top face last exactly as today. Tint slices progressively darker toward the base and lerp
toward the fog haze colour, and alpha-fade the bottom 2-3 slices so the tower DISSOLVES into the
fog instead of ending on a hard edge.

Details:

- **Painter's order across the whole wall, not per block:** draw slice-depth k of EVERY block
  before depth k+1 (base pass first, tops last). Footprints at the same depth are disjoint (same
  projection of disjoint rects), so per-depth passes compose correctly and nearer slices always
  cover farther ones. This is just a loop-order change inside `Wall.Draw`.
- **Adaptive slice count:** the lean distance is `(1-d) * |corner - VP|` -- ~0 at screen centre,
  up to ~170 px at the far corners. Fixed K wastes draws at the centre and steps visibly at the
  edges; instead one slice per ~8-10 px of lean (`ceil(lean / step)`, clamped to a max). Blocks
  near the VP degenerate to (almost) just their top face -- which is correct perspective: you're
  looking straight down at them.
- **Slice texture:** start by reusing the block's own 8x8 cell (same texture -> the entire wall
  including sides stays ONE batch). Dark + fogged, the smeared cell reads as an extruded shaft.
  If it looks streaky/wrong in practice, add a dedicated tileable SIDE texture derived from
  `756-v1` via `tools/walls/` (offline tool, committed output, `textures.config` if big) -- but
  don't pre-pay that cost; evaluate the free version first.
- **Tint math:** straight alpha as always. Slices can draw fully opaque with a darkened tint
  (`lerp(fogColor, sideColor, t)` where t goes base->top), only the bottom few slices need
  translucency for the dissolve. No new shader needed -- vertex tint does the depth fog.
- **Edge lines (`black line lalalal`):** keep on the top face only (they read as the top's bevel
  and preserve today's gameplay legibility). No lines on slices.
- **Fog wisps ABOVE the tower bases:** the "really coming out of the fog" moment needs fog to
  OCCLUDE the lower tower, not just tint it. `Wall.Draw` draws a low-alpha additive fog pass
  (reuse the existing `2331-v5` fog texture, scrolled at its own 0.52 modifier so it matches the
  background fog's motion) BETWEEN the slice pass and the top-face pass. Result: background fog ->
  tower shafts -> drifting wisps crossing the shafts -> crisp tops. Additive at low alpha, same
  blend the background layer already uses.
- **Draw order sanity:** `Wall.DrawOrder == 1` -- background (incl. its own fog layers) draws
  first, enemies/bullets/ship draw above. Slices+wisps live inside Wall's own Draw, so nothing
  else moves.

### Gameplay invariants (do not break)

- Top-face rects are byte-identical to today (position, size, texture cell, edge lines).
- `CollisionLevelMap` untouched. Nothing new is collidable; the hitbox overlay (`?hitboxes`)
  must show the same cyan map cells before/after.
- Difficulty grid-halving, wall recycling (`Setup` resets), and the file-loaded variation 2 all
  flow through unchanged (Draw only iterates `blocks`).
- A shipped no-query boot ships the towers ON (this is the feature), but keep a
  `?walltowers=0` kill switch to A/B against the flat look.

## Tuning: live panel + fast boot (house pattern)

Follow the eaLazer/eaHue/eaSpider pattern exactly:

- `DebugFlags` knobs, all defaulting to baked consts so a plain boot is deterministic:
  `?walldepth=` (0.66), `?wallslicestep=` (px per slice), `?wallfog=` (density 0..1),
  `?wallfogcolor=` (hex), `?wallsidedark=` (base darkening), `?wallwisps=` (wisp alpha, 0 = off),
  `?walltowers=0` (kill switch).
- **Live slider panel** `eaWalls` in `index.html` (outside `#app`, built only when the flag is
  present): drag depth / slice step / fog density / side darkening / wisp alpha in real time;
  orange readout prints the bake-ready query string. Wire through
  `DebugInput.SetWalls` ([JSInvokable]) -> `DebugFlags.SetWallsOverride`, read by `Wall.Draw`
  each frame.
- **Fast boot:** `?level=Level3&wallsonly` -- a `Level3.PopulateWallsOnly()` mirroring
  `Level2.PopulateSpiderBossOnly`: skip straight to a looping walls section (variation 1 is the
  densest/most tower-like) with the alien-base background up, pair with `?invuln`. Without this,
  reaching the first wall takes minutes of play per iteration.
- Optional if iteration wants a frozen subject: a tiny `?wallshot` showcase scene (LazerShowcase
  pattern) with a static wall chunk mid-screen. Probably unnecessary -- walls scroll slowly and
  the geometry reads fine in a live screenshot -- so build only if needed.

## Verification

- Visual: real Chrome (claude-in-chrome), NOT preview_screenshot. `?level=Level3&wallsonly&invuln`
  + the panel; screenshot at top-of-screen entry (emergence), mid-screen (lean direction), and
  near the VP (degenerates to flat -- expected).
- `?hitboxes` before/after: identical collision cells.
- Console clean; **hitch watchdog** quiet (the slice pass adds ~500-1000 batched draws worst
  case on the dense variation 1 -- same texture, one batch, should be nothing; the watchdog
  will say so).
- `?walltowers=0` renders byte-identical to today's flat look.
- Bloom/gamma: no special handling -- slices are dark (won't bloom), wisps additive (bloom
  nicely). Check the mid-level background swaps (`swapBG1..5` -> `SetAlienBase2..6`): fog colour
  is a tunable const; if a swapped ground reads too different, make fog colour per-phase
  (follow-up, not blocking).

## Explicit non-goals / rejected alternatives

- **Real 3D extrusion pass** (one `DrawUserIndexedPrimitives` with ALL visible side trapezoids +
  vertex fog): viable as a Phase 2 upgrade if slice stepping is objectionable -- it must be ONE
  buffered call per frame (the Quad lesson), needs state save/restore around the sprite batch and
  a painter's sort (scene RT has no guaranteed depth buffer). Don't start here; the slice version
  is 90% of the look for 20% of the risk.
- **Offline-rendered tower sprites** (tools/models Blender pipeline): can't work -- wall layouts
  are arbitrary per-variation grids, not a fixed set of sprites.
- **Fog as a full-screen shader pass:** overkill; tint + one wisp layer achieves the depth cue
  within the existing pipeline.

## As built (card d59266cc) -- where reality differed from the plan above

Steps 1-3 shipped, plus step 6's side texture (it turned out to be needed, not optional). Five
deltas from the design above, each forced by something the plan assumed wrong:

1. **Uniform global depth passes, not per-block adaptive slice counts.** The painter's-order
   argument ("footprints at the same depth are disjoint") only holds if every block shares the
   same `d` in a given pass. With per-block depth ladders a tall shaft can lean over a block
   nearer the VP and land in the wrong order. `slices` is now derived once from the *wall's*
   worst on-screen corner lean, and every block steps through the same depths.
2. **The slice pass needs a WIDER row cull than the top-face loop.** Bases project toward the VP,
   so a block whose top face has scrolled off the bottom (topY up to ~754) still shows its base,
   and a block still above the screen (topY down to ~-154) already shows its base below the top
   edge. That second case *is* the base-first emergence. `RowShaftVisible` handles both; the
   top-face loop's cull is deliberately untouched.
3. **A `Color` tint MULTIPLIES -- it cannot lerp a slice up to a haze colour.** The alien-base
   floor composites *bright* blue (756 plus its two additive 2331-v5 layers averages
   RGB(46,125,199)), so the shaft must BRIGHTEN and desaturate as it descends, then alpha-dissolve
   into that floor. `DefaultFogColor` is therefore a high-value blue-white *above* `DefaultSideDark`,
   not the dark teal the plan implies. Slices stay opaque except the bottom `DissolveFraction`;
   a translucent stack would accumulate back to opaque and defeat the dissolve.
4. **Wisps are full-screen (as planned) but their alpha is gated on the visible-block count.** A
   `Wall` spawns and dies per section, so an ungated screen-wide haze pops in and out with the
   entity. They tile by POSITION, never by a drifting source rect: `SpriteBatchWrapper` begins the
   batch with a null samplerState (LinearClamp), so an out-of-bounds source window clamps instead of
   wrapping. Drift modifier defaults to **0.8** (the existing near-fog layer, which sits inside the
   shaft's 0.66..1.0 depth band), not 0.52 -- that layer is slower than the ground it draws in
   front of.
5. **Step 6 was required.** Slicing the block's own full-res 8x8 cell makes the shaft *corduroy*:
   the exposed sliver per slice is ~2 design px, the same order as the cell's own detail, so slices
   repeat that detail instead of smearing into a face. Reducing the slice step does NOT fix it
   (verified by sweep -- identical comb at steps 3/4/6/8). `tools/walls/build_wall_side.py` emits a
   low-frequency companion sheet (each cell area-averaged to 16x16 texels). Slice step then only
   controls silhouette smoothness; it bands above ~8px, so the baked default is **5**.

Measured cost: ~600-1500 batched slice draws on the dense variations, **~3.6 ms/tick** over the flat
path (median 24.7 vs 21.1 ms, WASM Debug). No new hitch -- the 121 ms frame at wall spawn reproduces
with `?walltowers=0` and is pre-existing.

## Feel-dialing pass (card 9dcb4695)

Three deltas on top of "As built", all from watching it under `?level=Level3&wallsonly&invuln`:

6. **The side sheet needed MORE AXES; a square cell can never texture a side face.** Every slice
   sampled the same square, so the thin sliver it left exposed was always the same BORDER texels of
   that cell, smeared radially out of the VP -- the shafts read as vertical streaks with no surface
   of their own. This is inherent to the slice trick, not a tuning failure: no value of slice step,
   fog, or side darkening can put detail on an axis the sheet doesn't have. `build_wall_side.py` now
   emits a 2D **scan plane** per cell (640x640): area-averaged (which is what keeps delta 5's comb
   gone), mirror-tiled so it wraps seamlessly on both axes, wrap-padded by the window size.
   `?wallsidescan=` slides a `SideWindow`-sized window across the block's own plane as the shaft
   descends, so successive slivers expose successive texels.

   **The scan must travel PERPENDICULAR to the exposed edge**, and this is the whole reason it is a
   plane rather than a strip. The exposed sliver is the slice rect's edge FACING the VP. A block
   above/below the VP exposes a horizontal edge, whose sliver is a ROW of the window (perpendicular =
   Y). A block left/right of the VP exposes a vertical edge, whose sliver is a COLUMN (perpendicular
   = X); sliding in Y merely TRANSLATES that column's pattern along the face, re-showing the same
   texels, and stacked over 64 slices the features trace **hard diagonal streaks**. (A vertical-strip
   sheet was built first and did exactly that to every left/right shaft; caught on the first live
   look, not by the offline render -- which only exercised a below-VP block.)

   The fix is to scan **DIAGONALLY** -- the same offset on both axes. That advances the exposed row
   AND the exposed column by one texel per slice, so every orientation gets the full perpendicular
   travel at once, corner blocks (which expose two faces) included. It also depends only on `t`, so
   it is a pure function of depth.

   **Rejected: picking the axis per block from `|dx|` vs `|dy|`.** It looks right in a still -- each
   block scans perpendicular to its dominant face -- but a wall SCROLLS, so `dy` sweeps through zero
   while `dx` is fixed, and every block crosses the `|dx| = |dy|` boundary mid-screen. The axis flips
   in one frame and the texture **pops**. Shipped, spotted immediately, reverted. A static offline
   render cannot show a defect that only exists over time; this is the lesson of the delta.

   The price of the diagonal is that the offset perpendicular to one edge is PARALLEL to the other,
   so each face is also sheared ~45 degrees along its length. Consecutive slivers carry genuinely
   different texels, so it reads as diagonal grain rather than the coherent lines a pure translation
   produces. A dedicated per-axis scan is slightly crisper on the face it serves; the diagonal trades
   that for being correct, branch-free and pop-free everywhere.

   The scan advances `scan * scanSpan / slices` texels per slice, so the natural value is **one texel
   per slice**: `scan = MaxSlices / scanSpan` = 64/64 = **1** (baked). Below it slices repeat a window
   and smear; above it they skip texels and the shaft corrugates into visible ridges.
   `Wall.SideWindow` is a CONTRACT with the tool's `CELL`; the game derives
   `planePitch = side.Width/8` and `scanSpan = planePitch - SideWindow`, so a square (pre-plane) sheet
   gives span 0 and degrades to the old streaked look, never garbage.

7. **Baked slice step 1 -- but the step is inert there and `MaxSlices` is what binds.** Worst on-screen
   lean is ~170 px, so step 1 asks for ~170 slices and is clamped to 64: an effective ~2.7 px step at
   the far corners, finer everywhere else. That is exactly why 5 -> 1 barely moved the frame time
   (slice count 34 -> 64, not 34 -> 170). Resolving a true 1 px step means raising `MaxSlices`, at ~3x
   the slice draws -- not done; nobody has asked for it by eye.

8. **`?walltwist=<deg>` -- the shaft twists between cap and base.** Each depth-layer is rotated by
   `twist * (1 - t)` (zero at the cap, so it meets the unrotated top face cleanly). Baked **0**.

   The rotation is applied to the WHOLE LAYER about the VANISHING POINT, never to each slice about
   its own centre. A rigid rotation of a layer about the VP keeps every footprint at that depth an
   affine image of the others, so adjacent blocks stay glued edge to edge and footprints stay
   DISJOINT -- which is precisely what makes the single global painter's order (delta 1) correct.
   Rotating each slice in place tiles nothing: squares rotated about their own centres do not meet,
   so every solid block cluster opens X-shaped cracks down its shaft and corners swing outside their
   footprint into a neighbour's. Verified offline on a 2x2 cluster; the per-slice variant fans the
   slices apart into stacked offset cards.

   KNOW WHAT IT ACTUALLY DOES: rotating about the VP does not twist a tower about its OWN axis, it
   ORBITS it about screen centre. Tangential displacement is `radius * angle`, so towers far from
   the VP sweep long arcs and bend, while central ones barely move -- a swirl, not a uniform twist.
   A true per-tower twist would need a per-tower centre, and the slice pass has no notion of a
   tower, only of independent blocks. (SpriteBatch source rects are axis-aligned, so the texture
   cannot be rotated independently of the geometry either.)

9. **Seams between adjacent blocks: the side sheet must be ONE seamless image, not per-cell tiles.**
   The first plane sheet gave every block its own isolated, mirror-tiled island, so neighbouring shafts
   had unrelated texture phases and hard-edged at every block boundary. `756-v1` already tiles seamlessly
   (the top faces have always relied on that), so the side sheet is now a contiguous area-averaged copy of
   it, wrap-padded by the window size: block j's window sits at `j*CELL` and its neighbour's ABUTS it, so
   at a shared screen edge both sample the identical texel.

   A SECOND cause survived that fix: adjacent atlas windows do not filter across their shared edge -- each
   CLAMPS -- so a magnified window still leaves a mismatched band. The window is now 64 texels (~1 per
   on-screen pixel at a typical block), making the clamp sub-pixel. Measured across a block boundary: the
   step is 0.91x an ordinary interior texel step (1.0 = seamless). Also: `?wallsidescan=` is in TEXELS PER
   SLICE, not wrap-cycles, so its natural value stays 1 when the sheet is resized.

10. **Corners: per-face shading needs a PIXEL SHADER, not more draws.** With the texture continuous across
   blocks, nothing distinguishes a north face from an east face. A sprite carries one tint and a slice's
   visible sliver is the border RING of its square, spanning two faces -- so a per-sprite tint cannot shade
   them apart. `tools/shaders/src/faceshade.fx` classifies each pixel by which triangle of the square's two
   DIAGONALS it lands in (mitred corners, as a real box has) and scales rgb by that face's factor.

   The trick that keeps it one batch: SpriteBatch passes ATLAS texcoords, and every block samples a
   different window -- but the origins are `j*SideWindow + off` mod a multiple of `SideWindow`, so they are
   all congruent to `off` mod `SideWindow`. One per-slice uniform therefore recovers every sprite's local
   UV. Cost: ~64 batch flushes per wall, versus the ~1500 extra managed sprite draws a two-tints-per-slice
   geometric split would need (and managed per-draw cost is the bottleneck here -- see the 3D spike card).

   **Only OUTER edges are faces.** Shading all four sides of every block mitres a dark wedge into each
   block's corner, and two of them meet at every interior boundary -- a seam grid across what should be a
   continuous surface. (Shipped; spotted immediately in game.) The shader therefore shades by NEAREST
   EXPOSED EDGE, with hidden sides pushed out of the search: a north band then runs unbroken to the block's
   edge when there is no east face to mitre against, and the mitre survives only at genuine wall corners.
   The mask is per-BLOCK, and a per-block uniform would break the batch -- but the slice TINT is per-SLICE,
   so the two swap: tint becomes the `SliceTint` uniform, and the sprite's vertex colour carries the 4-bit
   mask (`Wall.FaceMask`, the same `isfree()` the edge lines use). Zero extra flushes. Verified as a unit
   test of the decision rule, not by eye: an image probe across a boundary averages several slices at
   different local v and smears the wedge away.

   Factors are DARKEN-ONLY (the tint already carries the fog lerp; >1 clips the hazy base to white) and
   lerp toward 1 with the haze so shading dissolves into fog at the base. The corner contrast comes from
   ORIENTATION (vertical faces darken, horizontal ones don't), not from the light: a block always shows one
   horizontal and one vertical face, so every corner in every quadrant reads. A pure directional light
   would give exactly zero contrast in two of the four quadrants. `?wallfaceangle` adds a weak directional
   term on top so north != south and east != west.

11. **Unloading must be deferred past the bottom edge (`Wall.DeathY`).** Bases project toward the VP, so a
   block below the VP has its shaft drawn ABOVE its cap: when the last cap crosses y=600 the towers are
   still on screen and `Position.Y > 600 -> Die()` pops them away. The last visible point is the base of
   the topmost row, at Position.Y, so solve `VanishY + (Position.Y - VanishY)*depth >= 600`, giving
   `deathY = VanishY + (600 - VanishY)/depth` (754.5 at depth 0.66; exactly 600 at depth 1, and 600 with
   the towers off, so `?walltowers=0` unloads unchanged).

   NOT purely cosmetic: `Walls.wall_OnDeath` calls `Terminate()`, so this delays the level's NEXT EVENT by
   the extra ~154 px of scroll (~0.6 s at the wall sections' `4.3/16.667` px/ms). Deliberate -- the section
   is not over until its towers have gone -- but it is a pacing change, not just a draw change.

12. **`?walltoplift=` -- tower tops drawn proud of the gameplay plane.** Top faces (and their edge
   lines) project at depth `1 + lift`, i.e. scaled away from the VP, exactly as their shaft's topmost
   slice is. Baked **0** (flush). COSMETIC ONLY: `CollisionType`/`CollisionLevelMap` keep using the
   unprojected block rects, so a lift drifts the sprite off its own hitbox by `lift * distance-from-VP`
   (~8 design px at a screen corner for lift 0.02). Small values only; check with `?hitboxes`.

Drawing verified offline (a Pillow re-implementation of the slice sampling against the real PNGs,
rendering a below-VP and a left-of-VP block under each scan axis) as well as live -- the wall
scrolls, and the canvas is black whenever its tab is backgrounded, so the offline render is what
makes a still comparison possible at all. Note the offline render is what the eye missed: the
strip sheet looked fine on a below-VP block, and only a LIVE look at a full walls section showed
the left/right shafts shearing. Render both orientations when touching this.

## Work breakdown

1. Projection + slice pass + tints in `Wall.Draw` (+ `walltowers` kill switch). The core.
2. Fog dissolve (bottom-slice alpha) + wisp pass above shafts.
3. `DebugFlags` knobs + `eaWalls` panel + `?level=Level3&wallsonly` boot.
4. Tune by eye with the panel, bake consts (user dials the final feel -- "For me" style).
5. Verify per the gate above; screenshot set for the PR.
6. (Only if needed) dedicated side texture via `tools/walls/`; (only if needed) Phase-2 true-3D.
