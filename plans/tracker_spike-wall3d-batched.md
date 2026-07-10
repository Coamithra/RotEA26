# Tracker: spike/wall3d-batched

Card: `a66fc73e` — Spike: real batched 3D for the Level-3 wall towers (`?wall3d`)
Worktree: `.claude/worktrees/wt2` · dev port `5282`
Base: `main` @ 262b317 (root checkout's uncommitted scan-plane work deliberately NOT carried)

## Phase 1: Pick Up the Card
- [x] Claim the card (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree (slot wt2) + branch, push upstream
- [x] Create this tracker

## Phase 2: Research
- [ ] Read `Game/EvilAliens/Wall.cs` in full (Draw, DrawTowerShafts, Project, RowShaftVisible)
- [ ] Read `Compat/SpriteBatchWrapper.cs` (Begin choke, RenderScale.Matrix, state ownership)
- [ ] Read `Compat/RenderScale.cs` (design -> target pixel mapping)
- [ ] Read `Game1.cs` Draw/DrawInner (sceneTarget ctor, bloom round-trips, present blit)
- [ ] Read `Quad.cs` (the "3D is unviable" comment the card disputes)
- [ ] Read `BloomComponent.cs:~234` (existing DepthStencilState/RasterizerState usage)
- [ ] Read `tools/shaders/build_shaders.py` + an existing `.fx` (how to add a shader)
- [ ] Confirm KNI 4.1 `DrawUserIndexedPrimitives` + `VertexPositionColorTexture` availability
- [ ] Verify `?walltowers=0` / `?hitboxes` / CollisionLevelMap invariants I must not break
- [ ] Summarize findings

## Phase 3: Design
- [ ] Write `plans/spike-wall3d.md`: state save/restore, coord space, occlusion strategy
- [ ] Decide: CPU painter's sort (low risk) vs depth attachment on sceneTarget
- [ ] Decide: custom `.fx` (textured + vertex colour) vs BasicEffect
- [ ] Reuse the `DebugFlags` seam for `?wall3d`; keep it OUT of `DebugFlags.Active`
- [ ] Align with the user before writing code

## Phase 4: Implement
- [x] `?wall3d` + `?wall3dbands` flags in `Compat/DebugFlags.cs`
- [x] ~~Shader source in `tools/shaders/src/`~~ NOT NEEDED — `BasicEffect` exists on BlazorGL
      (`Resources.BasicEffect.fxo` is embedded in `Kni.Platform.dll`) and does textured +
      vertex-colour. Avoided writing this project's first vertex shader.
- [x] `Wall.Draw`: Flush() -> build verts/indices -> one DrawUserIndexedPrimitives -> resume
- [x] Top faces + wisps unchanged, still sprite-batched
- [x] `?walltowers=0` still reproduces the flat look
- [x] `eaWall3d()` console toggle (`DebugInput.Wall3d` + index.html) so the two paths can be A/B'd
      on the same frame
- [x] Seam fix: along-edge UV follows the edge's axis; down-shaft UV starts at the cell edge the
      wall hangs from; half-texel inset removed
- [x] `BasicEffect` made static/shared (was one per `Wall`, i.e. per level section)

## Phase 4b: Commit to 3D (user decision, 2026-07-10)
- [ ] Wait for the user's "sliced 3d" commit to land on `main`
- [ ] `rtk git pull origin main` into this branch
- [ ] Make 3D the ONLY tower path; `?walltowers=0` stays the flat kill switch
- [ ] Delete: `DrawTowerShafts`, `MaxSlices`, `DefaultSliceStep`, `DefaultSideScan`,
      `DefaultSliceTwist`, `DefaultTopLift`, `SideWindow`, the `side` texture field
- [ ] Delete flags: `?wallslicestep`, `?wallsidescan`, `?walltwist`, `?walltoplift`, `?wall3d`
      (now the default); keep `?wall3dbands`
- [ ] Delete assets/tools: `wwwroot/Content/gfx/base/756-v1-side.png`,
      `tools/walls/build_wall_side.py`, its `Level3.cs` preload, its `tools/walls/README.md` section
- [ ] Update the `eaWalls` slider panel in `index.html` (slice-step slider -> bands)
- [ ] Update `CLAUDE.md` + `plans/walls-3d-towers.md`

## Phase 5: Verify
- [x] `dotnet build -c Debug` clean
- [x] `dotnet run -c Debug --urls http://localhost:5282`
- [x] Offline: `verify_tower_order.py` certifies painter's order (14,162 face pairs, acyclic)
- [x] Offline: `preview_wall3d.py` matrix check — camera reproduces `Wall.Project()` to 2e-13 px
- [x] Offline: `preview_wall3d.py` image — towers lean to VP, real texture, correct occlusion
- [x] Real Chrome (claude-in-chrome), NOT preview_screenshot: towers render correctly
- [x] Perf, focused tab, interleaved: slice ~5.7ms, **3D ~2.3ms**, flat 1.86ms avg tick
- [ ] Zero console exceptions on a clean boot (two `[hitch]` lines seen; attribute them —
      likely level texture decode, but re-check now that `BasicEffect` is shared)
- [ ] Re-verify seams + towers in real Chrome after the seam fix (needs a focused tab)
- [ ] `?hitboxes` unchanged; `CollisionLevelMap` untouched
- [ ] Spot-check the diff
- [ ] Flag what needs manual testing

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review`, fix every finding
- [ ] Pull main into branch, resolve per the merge rules
- [ ] Re-build (+ re-verify if behaviour changed)
- [ ] Back to root checkout
- [ ] PR + self-merge (`gh pr create --fill` / `gh pr merge --merge`)
- [ ] Remove worktree + branch
- [ ] Delete this tracker
- [ ] Card -> Done + comment
- [ ] Follow-up cards (e.g. kill the slice machinery if the spike lands)
- [ ] Overview for the user

## Notes / findings
(append as I go)
