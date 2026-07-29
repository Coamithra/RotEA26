# Spike: real batched 3D for the Level-3 wall towers (`?wall3d`, flag since removed)

Trello `a66fc73e`. Branch `spike/wall3d-batched`, worktree `wt2`, dev port `5282`.

**STATUS: landed, and the follow-up landed too.** The spike shipped behind `?wall3d`; commit
`e511a08` then committed to real 3D as the ONLY tower path and deleted the flag along with the
entire sprite-slice machinery. Read what follows as the as-shipped design, except where a line
is explicitly marked as spike-era. This doc is kept because it is the design rationale for the
geometry, and is cited from `Wall.cs`, `DebugFlags.cs` and `web/EvilAliensWeb/CLAUDE.md`.

## Context

`Wall.DrawTowerShafts` extrudes each collidable block downward as a stack of up to 64
sprite **slices**. It costs ~600–1500 batched sprite draws (~3.6 ms/tick), and because a
slice only ever exposes a ~2px sliver of its cell, the side faces have no real texture —
they are a radial smear with a diagonal scan walked over them, sampling a bespoke
`756-v1-side.png` scan-plane sheet.

The card asks: replace the slice stack with **one batched 3D draw** of the tower side
faces, behind `?wall3d`. It landed: the side faces got genuine UVs, and the whole slice
machinery is gone (`e511a08`).

The stated blocker — "3D is unviable on WebGL, per the `Quad.cs` lesson" — does not
survive reading `Quad.cs:172-179`. That comment describes *three separate immediate-mode
`DrawUserIndexedPrimitives` calls per laser beam, each forcing a `SpriteBatch.Flush()`*.
That is a batching pathology, not evidence about 3D throughput. Nobody measured one
buffered draw.

## Research findings (all verified, not assumed)

### 1. KNI 4.1.9001 / BlazorGL capability — verified against the restored DLLs

| API | Status |
|---|---|
| `DrawUserIndexedPrimitives<T>` | **Present**, 4 overloads (`short[]` and `int[]` indices, all take `vertexOffset`) |
| `DrawUserPrimitives<T>` | Present |
| `DrawInstancedPrimitives` | **Throws** on BlazorGL (`PlatformNotSupportedException`, then `NotImplementedException`). Not needed. |
| `VertexPositionColorTexture` | Present |
| `BasicEffect` | **Present and usable.** Its ctor loads `Resources.BasicEffect.fxo`, and that resource *is* embedded in `Kni.Platform.dll` (the BlazorGL backend). |
| `DepthStencilState`, `RasterizerState.CullNone` | Present |
| `RenderTarget2D(..., DepthFormat.Depth24)` | Present |

`BasicEffect` being real is the single most useful finding: with `TextureEnabled` +
`VertexColorEnabled` it is exactly the "textured + vertex-colour" shader the card wanted
to hand-compile. **No new `.fx` is needed** — which is a relief, because this project has
*no* shader with its own vertex shader (all 11 are pixel-only and lean on SpriteBatch's
internal VS), so a hand-written `vs_3_0` through MGCB/BlazorGL would have been unproven.

**Cost model.** BlazorGL's `DrawUserIndexedPrimitives` creates *and destroys* a transient
vertex buffer and index buffer per call (`CreateBuffer` → `bufferData` → `bufferSubData`
→ `delete`), all through the WASM→JS interop boundary. That overhead is **per call, not
per vertex** — which is precisely why one draw per wall per frame is the right shape, and
why the original three-quads-per-laser pattern was slow. It also re-uploads the whole
index array every call, so the index buffer is cached and only regrown.

### 2. Occlusion: a CPU painter's sort is exact — no depth attachment needed

`sceneTarget` is `DepthFormat.None` and is re-bound mid-frame for bloom round-trips, so
adding depth would mean clearing it before the 3D pass and auditing every 2D `Begin` for
`DepthStencilState.None`. Happily it is unnecessary.

The shafts are genuinely **real vertical boxes of equal height standing on a ground
plane, seen by a perspective camera whose principal point is the vanishing point.**
`Wall.Project(top, d) = VP + (top - VP) * d` *is* that camera. So work in polar
coordinates about the VP: along a ray, a block's top rect spans radii `[r0, r1]`, its
shaft silhouette spans `[D*r0, r1]`, and the part that is *visible side face* spans
`[D*r0, r0]`. A point at radius `rho` on that face sits at depth `d = rho / r0`. For two
blocks sharing a ray with `r0_A < r0_B`, wherever their faces overlap:

    d_A = rho / r0_A  >  rho / r0_B = d_B

The block whose **near edge is closer to the VP always wins, at every shared radius**.
Depth along a ray is therefore strictly decreasing in the owning face's `r0`: the faces
cannot interleave, so no occlusion cycle exists and *some* painter's order is always
exact.

`tools/walls/verify_tower_order.py` checks this numerically rather than trusting the
derivation. It solves each face's depth analytically (the ruled surface
`p(s,d) = VP + (lerp(A,B,s) - VP) * d` gives a 2x2 solve per pixel — no rasterisation
error), builds the real "occludes" relation from per-pixel depth comparisons over every
overlapping face pair, and asserts (a) the relation is acyclic and (b) the cheap shipping
key is a valid topological order of it. Result over 95 configurations — widths 3/7/9/12
(every `Wall.Setup` variation), densities 0.25–0.95, scroll offsets ±400, plus the real
`level3.txt` at 15 scroll positions, **14 162 overlapping face pairs**:

```
PASS
  * no mutual occlusion anywhere -> the occludes relation is acyclic
  * box-centre-distance-from-VP is a valid topological order of it
```

Two candidate keys that *look* reasonable (`min corner distance`, `nearest face-edge
end`) genuinely fail, so the test has teeth — it is not passing vacuously.

Top faces stay correct for free: they sit at `d == 1`, the maximum, so drawing them last
(unchanged) is exactly right.

## Design

SPIKE-ERA GATING (since removed): this sat behind **`?wall3d`**, default off, with the slice
path still the default, so a shipped build was byte-identical unless asked. `e511a08` deleted
both the flag and the slice path; **`?walltowers=0` remains the flat kill switch.**

**Geometry is genuinely 3D; the perspective divide happens on the GPU.** Pre-projecting
to 2D and sending flat quads would give affine (PS1-style) texture warp, because the `w`
is lost. Instead each block becomes a box in a world space where `z = 0` is the gameplay
plane and `z = h` is the ground, with the camera at `z = -E` looking down `+z`:

    d = E / (E + z)        =>   h = E * (1 - D) / D     (d == D at the ground)

    View       = CreateTranslation(-400, -300, E) * CreateScale(1, -1, 1)   // design y is down
    Projection = CreatePerspectiveOffCenter(-400, 400, -300, 300, E, E + h)

which reproduces `Wall.Project()` **exactly** while letting the rasteriser interpolate UV
and colour perspective-correctly.

Per frame, per `Wall`:

1. `spriteBatch.Flush()` — end the wrapper's active batch.
2. Collect visible blocks (reusing `RowShaftVisible`), sort by `-|centre - VP|`.
3. For each block emit only its **VP-facing** side faces — left face iff `x0 > 400`,
   right iff `x1 < 400`, top-edge face iff `y0 > 300`, bottom-edge iff `y1 < 300` (a
   block straddling the VP axis shows neither on that axis). That is the backface cull,
   done on the CPU, so `RasterizerState.CullNone`.
4. Each face is tessellated into `bands` vertical strips (default 4, `?wall3dbands=`) so
   the smoothstep bottom dissolve and the fog lerp — evaluated per slice in the path this
   replaced — survive as per-vertex colour that the GPU interpolates. UVs come from the
   block's **real 8x8 `756-v1` cell**, spanning the face's full width and height.
5. One `DrawUserIndexedPrimitives(TriangleList, verts, 0, nv, indices, 0, ntris)` with
   `BasicEffect` (`TextureEnabled`, `VertexColorEnabled`, `LightingEnabled = false`),
   `BlendState.NonPremultiplied` (straight alpha, as everywhere), `DepthStencilState.None`.
6. Restore nothing by hand: `SpriteBatch.Begin` re-applies blend/depth/rasterizer/sampler
   state on the wrapper's next `_beginDrawing()`. The wisps + top faces then draw as
   today.

Bloom is unaffected: `Wall.DrawOrder = 1`, `BloomComponent.DrawOrder = 950`, and the 3D
pass writes into the same bound `sceneTarget`, so the towers get bloom exactly as the
slices did.

### Files

| File | Change |
|---|---|
| `Game/EvilAliens/Wall.cs` | `DrawTowerShafts3D()` alongside the slice path; `Draw` branches on the flag. Since `e511a08` there is no slice path and no path branch: `Draw` calls `DrawTowerShafts3D` directly, still under the `?walltowers=0` gate |
| `Compat/DebugFlags.cs` | `?wall3d`, `?wall3dbands=` (out of `DebugFlags.Active`). `?wall3d` was removed by `e511a08`; `?wall3dbands=` is still there |
| `tools/walls/verify_tower_order.py` | new — the offline occlusion certification above |
| `CLAUDE.md` | document the flag + the finding |

### Out of scope for the spike

- ~~Deleting the slice machinery (`?wallslicestep`, `?wallsidescan`, `MaxSlices`,
  `756-v1-side.png`, `build_wall_side.py`).~~ **DONE** — this landed, so the follow-up ran:
  `e511a08` deleted all of it (plus `faceshade.fx` and `?walltwist`). `tools/walls/README.md`
  records the removal; the old code is recoverable from `906f344`.
- Persistent `VertexBuffer`/`IndexBuffer` + `DrawIndexedPrimitives` (removes the
  per-frame transient-buffer upload). Only worth it if the transient path measures hot.
- Painter's order *across* two overlapping `Wall` entities (each does its own draw call).
  The slice path has the same property; sections don't overlap on screen in practice.
- `?walltwist` / `?wallsidescan` have no meaning in the 3D path (both were deleted outright
  by `e511a08`).

## Verification

Per CLAUDE.md, the wall scrolls and the canvas goes black when its tab is backgrounded,
so drawing is verified **offline** and the live check is a pixel diff, not eyeballing.
This was the spike's PLAN; all of it ran, and the outcome is under "Result" below:

1. `python tools/walls/verify_tower_order.py` — the occlusion proof above. **Done, passes.**
2. Offline numpy/Pillow render of `DrawTowerShafts3D`'s exact projection against the real
   `756-v1.png`, compared side by side with the slice path's output.
3. `dotnet build -c Debug`, then real Chrome (claude-in-chrome, **not** `preview_screenshot`)
   on `http://localhost:5282/?level=Level3&wallsonly&invuln&wall3d`. Zero console exceptions.
4. **Top-face invariant:** freeze with `eaHitstop(20000)`, then diff `gl.readPixels` between
   `?wall3d` and `?walltowers=0` restricted to top-face pixels — they must be identical.
5. **State-restore invariant (the card's KILL criterion):** confirm the 2D draws *after* the
   3D pass (wisps, top faces, edge lines, HUD) are unchanged. Same pixel diff catches it.
6. **Perf:** `[hitch]` watchdog quiet; compare tick time `?wall3d` vs `?walltowers=0`
   (flat baseline) vs the slice default.
7. `?hitboxes` unchanged; `CollisionLevelMap` untouched.

## Result — the spike landed

**Frame cost**, measured in a focused Chrome tab on `?level=Level3&wallsonly&invuln`, by wrapping
`window.tickJS` and timing `TickDotNet` itself. Samples are **interleaved** (slice, 3D, slice, 3D,
flat), 5 s each, because `?wallsonly` keeps scrolling and the visible block count drifts — a single
A-then-B would partly measure *when* it sampled rather than *which path*.

| Path | avg tick | max tick | over the flat baseline |
|---|---|---|---|
| slice (was shipped) | 6.23 / 5.11 ms | 8.1 ms | ~+3.8 ms |
| **3D batched** | 2.62 / 2.06 ms | 3.4 ms | **~+0.4 ms** |
| flat (`?walltowers=0`) | 1.86 ms | 3.2 ms | — |

Roughly a **10x cut in what the towers cost**, landing within half a millisecond of drawing no
towers at all. Exactly the card's success criterion.

Two measurement traps worth remembering:

* **FPS is useless here.** Every sample read a flat 100 fps — the display's vsync cap. The whole
  difference lives in tick time, which is why the probe times `TickDotNet` rather than counting
  frames.
* **A backgrounded tab lies.** The first reading was 14.2 ms/tick because Chrome throttles an
  unfocused tab. Measure only with the window focused. (Relatedly: `scheduleTick` uses `rAF` when
  visible and `setTimeout` when hidden, so a frame queued via `rAF` just before the tab hides never
  fires and the loop parks until it is visible again.)

**Texture seams**, on both axes, neither arbitrary — see the long comment in `DrawTowerShafts3D`:
* *Along* the edge: blocks step through the sheet as (u → columns, v → rows), so a face's along-edge
  coordinate must follow the axis its edge runs along. Using `u` for a vertical edge makes two
  stacked blocks' coplanar walls each restart the same range, hard-seaming every block boundary.
* *Down* the shaft: the wall hangs off one particular cell edge and must start at that edge's
  coordinate, so the sheet folds over the top face's rim instead of cutting to the far side of the
  cell. Hence the down range reverses between the left wall and the right one.
* The half-texel inset was **removed**: adjacent atlas cells *are* the correct continuation
  (row `i`'s `v1` is row `i+1`'s `v0`), so insetting re-opens the seam it means to close.

`BasicEffect` is constructed **once, statically**, not per `Wall`: Level 3 spawns a fresh `Wall` per
section, and each construction re-reads and re-links the precompiled shader.

## Success / kill

The criteria the spike was judged against. **Outcome: SUCCESS on every count** (see "Result"
above); the KILL condition never triggered, and `e511a08` went on to make 3D the only path.

- **SUCCESS:** towers render with correct occlusion, side faces show real wall texture
  (no smear, no scan), vertex colour carries the fog for free, and tick time drops toward
  the flat-path baseline.
- **KILL:** state save/restore around `SpriteBatch` corrupts subsequent 2D draws in a way
  that cannot be cleanly reset. That would be a KNI BlazorGL state-management limit worth
  filing upstream — *not* a verdict on 3D throughput.
