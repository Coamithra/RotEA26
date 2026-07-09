# Tracker: fix/rendertarget-screenshot-text-spiders

Three render-target/present-path cards, one branch/PR (slot wt3). PR #102.

## Card 1 — 2df2cfd5 screenshot cropped
- [x] Root cause: SaveScreenShot composites via spriteBatchWrapper.Draw (RenderScale.Matrix) into a
      fixed 300x225 RT -> dest rect scaled up by RenderScale.Scale -> only top-left corner lands.
- [x] Fix: add DrawPresent(tex, Rectangle dest, color) identity overload; SaveScreenShot uses it.
- [x] Build clean. Committed ae85bd6.

## Card 2 — 37c4ccca jaggy / no-transparency HUD text
- [x] Investigated; orchestrator A/B confirmed the chrome mid-band component; user evidence: pops
      (metal:false) ALSO bad -> the flatten path itself.
- [x] TRUE root cause: RasteriseShadowText stacked TWO straight-alpha layers with
      BlendState.AlphaBlend (One/InvSrcAlpha) -> the text's AA edge texels land at full brightness
      over the shadow (premult equation fed straight colour) -> jaggy wherever text overlaps its
      backdrop. (Scale hypothesis disproven: raster already at final pixel size every frame.)
- [x] Fix: PremultiplyOver rasterise (premult flatten) + One/InvSrcAlpha composite w/ (a,a,a,a) tint.
- [x] MetalScore default true -> false (?metalscore re-enables); menus unaffected (not gated).
- [x] ?textshot frozen showcase (Compat/TextShowcaseScene.cs) + Game1 wiring + harness.html + flags.
- [x] Build clean.

## Card 3 — bc0c1ed3 flying spider fog opacity (wings double-brighten)
- [x] Root cause: background spider draws wing+body+wing each at alpha 0.2 NonPremultiplied ->
      overlaps composite to ~0.36 -> uneven fog.
- [x] Fix: BeginGroupFlatten/EndGroupFlatten shared grow-only RT; composite once at group alpha.
      Foreground (opaque) unchanged. Committed 9fe5c65.
- [x] Build clean.

## Ship
- [x] PR #102 open. Card 2 commit pending push.
- [ ] HOLDING AT GATE: orchestrator reviews full PR; then pull main, re-build, merge, cards ->
      Done, delete tracker, clean worktree.
