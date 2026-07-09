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

## Work breakdown

1. Projection + slice pass + tints in `Wall.Draw` (+ `walltowers` kill switch). The core.
2. Fog dissolve (bottom-slice alpha) + wisp pass above shafts.
3. `DebugFlags` knobs + `eaWalls` panel + `?level=Level3&wallsonly` boot.
4. Tune by eye with the panel, bake consts (user dials the final feel -- "For me" style).
5. Verify per the gate above; screenshot set for the PR.
6. (Only if needed) dedicated side texture via `tools/walls/`; (only if needed) Phase-2 true-3D.
