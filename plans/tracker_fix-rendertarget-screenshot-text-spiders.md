# Tracker: fix/rendertarget-screenshot-text-spiders

Three render-target/present-path cards, one branch/PR (slot wt3).

## Card 1 — 2df2cfd5 screenshot cropped
- [x] Root cause: SaveScreenShot composites via spriteBatchWrapper.Draw (RenderScale.Matrix) into a
      fixed 300x225 RT -> dest rect scaled up by RenderScale.Scale -> only top-left corner lands.
- [x] Fix: add DrawPresent(tex, Rectangle dest, color) identity overload; SaveScreenShot uses it.
- [ ] Build clean.

## Card 2 — 37c4ccca jaggy / no-transparency HUD text
- [ ] Investigate (path rasterises at render res w/ correct straight-alpha blends per read).
- [ ] DECISION NEEDED — present analysis, ask orchestrator (can't browser-verify).

## Card 3 — bc0c1ed3 flying spider fog opacity (wings double-brighten)
- [x] Root cause: background spider draws wing+body+wing each at alpha 0.2 NonPremultiplied ->
      overlaps composite to ~0.36 -> uneven fog.
- [ ] Fix: flatten the 3 sprites opaque into a shared grow-only RT (capture mode on wrapper),
      composite once at group alpha 0.2. Foreground (opaque) unchanged.
- [ ] Build clean.

## Ship
- [ ] Commit per card. PR --fill. PAUSE before merge; report + live-test checklist.
