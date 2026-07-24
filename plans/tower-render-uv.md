# Tower side-face texturing — continue the tile, and scale it to tower height

Card `0f7fc977` "improve tower rendering". Code: `Wall.DrawTowerShafts3D` / `Wall.AddFace`
(`web/EvilAliensWeb/Game/EvilAliens/Wall.cs`).

## Context

Level-3 walls are real 3D towers: each block's top face is one cell of the seamless 8x8
`756-v1` sheet, and its outward-facing side walls extrude down to the alien-base floor. The
card reports two things about those side walls, both visible in the attached screenshot:

1. **The side texture is a MIRROR of the top face.** Confirmed in code. `AddFace` takes the
   down-the-shaft UV range `(down0, down1)`, and today every face starts at the cell edge it
   hangs from and runs *into* its own cell: west `u0->u1`, east `u1->u0`, north `v0->v1`,
   south `v1->v0`. Starting at the hanging edge is what kills the rim seam (and must stay),
   but running back *across* the cell retraces the top face's texture backwards — a mirror
   about the rim. The sheet tiles seamlessly, so the shaft could just keep going instead.

2. **One cell of texture is stretched down the whole shaft, whatever its height.** Also
   confirmed — the existing comment even states it as intentional: *"Every tower spans the
   same world z, so spending one whole cell down the shaft is uniform for every block."*
   Uniform, but wrong scale: the shaft is much taller than a block is wide, so the side
   texels are stretched ~2.7x vertically and the tower reads short and near.

Numbers for the shipped Level-3 wall (`level3.txt`, `width=7`, `?walldepth` 0.66):

| quantity | value |
|---|---|
| block footprint `blockW`/`blockH` | `800/7` = **114.3** design px |
| shaft height `zBase - zCap` = `EyeDistance*(1/d - 1)` | **309.1** world units (== design px at the plane) |
| cells the shaft *should* span at the top face's texel density | **2.70** |
| cells it spans today | **1.00** |

So matching the top face's density is a 2.7x compression down the shaft — which is exactly
the "towers should read as quite large, taller than their top is wide" the card asks for,
and it falls out of the geometry rather than being a taste number.

## Design

### 1. Continue the tiling instead of mirroring

Keep the start (the hanging cell edge — that is the rim-seam fix), reverse the direction of
travel so the sheet unfolds over the rim and keeps going:

| face | hangs from | today (mirror) | new (continue) |
|---|---|---|---|
| west  | `u0` | `u0 -> u1` | `u0 -> u0 - span` |
| east  | `u1` | `u1 -> u0` | `u1 -> u1 + span` |
| north | `v0` | `v0 -> v1` | `v0 -> v0 - span` |
| south | `v1` | `v1 -> v0` | `v1 -> v1 + span` |

This is just "unfold the box": on screen a shaft always runs *away* from the VP, and the
matching UV direction is the one that continues the top face's flow across the rim.

Coplanar-neighbour continuity is unaffected (two blocks sharing a wall plane differ only in
the *along-edge* coordinate, which is untouched, and both shafts start at the same cell
edge and travel the same way). Corners still won't line up — the card already expects that;
box-unwrapping two orthogonal faces from one cell can't, and `?wallfacelight` is what makes
corners read.

### 2. Span N cells, N derived from the real tower height

```
repeats = sideTile * (zBase - zCap) / blockW      // u-axis faces (west/east)
repeats = sideTile * (zBase - zCap) / blockH      // v-axis faces (north/south)
span    = repeats * cellUv
```

`sideTile` **baked at 4**, dialed on the live panel. 1.0 would be the physically honest
answer, but true scale still reads short: these shafts are steeply foreshortened, so most of
their length is compressed into the far few pixels and honest texel density spends its detail
where none of it survives. Over-tiling puts repeats back in the near part where they are
legible. `repeats` is derived per frame from the live `?walldepth` / `?walltoplift`, so those
knobs stay consistent, and is clamped so a degenerate `?walldepth=0` can't explode the buffer.

### 3. Walking cells on the CPU, NOT GPU texture wrap

The obvious implementation — let the UV run past the cell and set the sampler to
`LinearWrap` — is **wrong here**, for two independent reasons:

* **The `.dds` is PADDED.** Every shipped `.dds` currently carries a +100px pad
  (`756-v1.dds` is 1348x1348 for a 1248x1248 logical sheet — the `--padtest` harness).
  GPU wrap wraps at the PADDED edge, so a shaft would run off into transparent pad. This is
  precisely the padded-vs-logical rule in web CLAUDE.md.
* 1248 is **not** a power of two, so `REPEAT` is not guaranteed available on every backend
  path anyway.

So `AddFace` walks cells itself and every emitted UV stays inside the logical region:

* express the down-the-shaft coordinate as a continuous **cell coordinate** `c` (0..8 spans
  the sheet), running from the hanging cell edge outward;
* split the face at every integer crossing of `c` **in addition to** the existing
  `bands` dissolve splits, so no quad ever straddles the sheet's wrap;
* per segment, pick the cell from the segment MIDPOINT (`((floor(mid) % 8) + 8) % 8`) and
  emit `(cellIdx + frac) * cellPx / paddedSize` — always within `[0, 8*cellPx/paddedSize]`.

Because the sheet tiles seamlessly, cell `m+1` *is* the continuation of cell `m` (they are
literally adjacent strips of one image) and cell 7 -> 0 is the image's own wrap — the same
"neighbouring atlas cells are the correct continuation, no half-texel inset" reasoning the
along-edge direction already relies on.

**Cost:** quads per face goes from `bands` (4) to `bands + crossings + 1` (~14 at the baked
tiling), in the one batched `DrawUserIndexedPrimitives`. BlazorGL's cost is per-CALL, not
per-vertex, and the call count is unchanged — measured tower pass **0.73 ms -> 1.29 ms**.
`EnsureTowerBuffers` is resized accordingly. Sampling stays `LinearClamp` — `DrawGeometry3D`
is untouched.

**Known cost of the high tiling:** `756-v1` ships with no mip chain, so a minified shaft is
filtered bilinearly and nothing more; at tiling 4 the far end of a shaft aliases and will
shimmer as the wall scrolls. Weigh it on `preview_wall3d.py --ladder`. The fix is a mip chain
on the wall sheet (a `tools/textures/build_textures.py` change) — follow-up card.

### 4. Tuning

* New flag **`?wallsidetile=<f>`** (`DebugFlags.WallSideTile`, default null -> the baked
  `Wall.DefaultSideTile = 4f`). 1 = match the top face's texel density; 4 = four times as many
  repeats down the shaft. Bounded `(0, 32]` at parse; `Wall.MaxSideTileCells` is the buffer-sizing
  backstop for the console/panel path, which does not go through the parser.
* New slider in the **`eaWalls`** panel (`?wallsonly` / `?walltune`), wired through
  `DebugInput.SetWalls` -> `DebugFlags.SetWallsOverride`, and added to the panel's
  bake-ready orange readout.
* **Fixed in passing:** `window.eaWalls` in `index.html` still has the dead slice-path
  signature (`sliceStep`/`sideScan`/`twist`) and passes 12 misaligned args to the 10-arg
  `debugSetWalls`, so the console helper has been silently wrong since the slice path was
  deleted. The panel calls `DotNet.invokeMethod` directly and is unaffected. Realigned while
  adding the new knob.

## Verification

Per root CLAUDE.md, wall drawing is verified **offline** — the wall scrolls and a
backgrounded tab paints black.

1. **`tools/walls/preview_wall3d.py`** is the tool of record; it re-implements the exact
   projection + UV in numpy. Update its rasteriser to mirror the new cell walk, and add a
   `--compare` sheet rendering **old (mirrored, 1 cell) vs new (continued, N cells)** side by
   side at the same scroll positions, so the change is readable by eye. Its existing matrix
   assertion (camera == `Wall.Project()`) must still pass.
2. **`tools/walls/verify_tower_order.py`** — the painter's sort is untouched, but it is the
   certificate for "tower geometry changed", so re-run it.
3. **Padded path:** the python preview samples the unpadded `.png`, so it proves the
   geometry but not the pad mapping. The live `.dds` IS padded (+100), so a clean live boot
   with no transparent bleed down the shafts is the padded-path proof.
4. **Live smoke** in real Chrome (claude-in-chrome): `?level=Level3&wallsonly&invuln` —
   clean `dotnet build -c Debug`, towers textured with no pad holes, **zero console
   exceptions**; plus `?walltowers=0` still reproducing the exact flat look.
5. **Diff spot-check** for the repo's usual traps (lowercase `content/`, `BlendState.AlphaBlend`,
   codegen re-run, hand-edited pipeline output).

## Out of scope

* Making tower **corners** line up (the card explicitly flags it as not-currently-working and
  not required; a real fix needs a purpose-authored unwrapped atlas, not a grid cell).
* The `+100 --padtest` pad on every shipped `.dds` — the working tree ships padtest textures.
  Not this card; **follow-up card** (it costs real bytes and VRAM in production).
* Any change to the top faces, the fog/haze, the painter's sort, the dissolve, or collision.
