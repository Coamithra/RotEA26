# CLAUDE.md — web/EvilAliensWeb (the game + compat code)

Architecture and per-feature notes for the ported game. The root `CLAUDE.md` has workflow,
build/run, and the verification rules; `tools/CLAUDE.md` has the offline asset pipelines that
generate much of the art/audio referenced here.

## Architecture

- **Game loop is JS-driven:** `wwwroot/index.html` (`initRenderJS`/`tickJS`) →
  `Pages/Index.razor.cs` `TickDotNet()` → `new EvilAliens.Game1()`. `ContentTestGame.cs` /
  `SpikeGame.cs` are dead harnesses, safe to delete.
- **Resolution = a unified presenter, not a pinned back buffer.** KNI's BlazorGL forces the back
  buffer to the browser window size (rewrites `PreferredBackBuffer` on resize — don't reintroduce a
  pinned one). `Game1.Draw` renders the whole frame into one offscreen `sceneTarget` sized to the
  window's 4:3 letterbox (`Compat/RenderScale`, capped 1440px tall) and blits it scaled+letterboxed;
  the game's `SetRenderTarget(0, null)` calls redirect there via
  `Xna3GraphicsDeviceCompat.BaseRenderTarget`. Gamma applies on the present blit; 800x600-design
  draws scale up via `RenderScale.Matrix` at the `SpriteBatchWrapper` Begin choke; bloom + offscreen
  targets are `RenderScale`-sized and recreated on resize. Hi-res art (menu title, splash) draws
  straight into this one scene — no separate overlay pass. A render-sized offscreen target
  composited back uses `SpriteBatchWrapper.DrawPresent` (identity); full-screen overlays use
  `(0,0,800,600)` design coords, never the viewport.
- **Shims in `Compat/` fake the Xbox APIs.** GamerServices = no-ops (full game unlocked,
  `SignedInGamers` empty so per-gamer loops do nothing — the XBLIG sign-in gate itself is gone, the
  PC keyboard path was recreated incl. the `#if WINDOWS`-stripped keyboard-read block in
  `InputHandler.Update()`). Storage = WASM in-memory FS mirrored to the browser (`StorageStub`'s
  `PersistentSave` + `Compat/SaveInterop.cs` + `eaSave` in `index.html`). `ResolveBackBuffer` and
  the `SpriteBlendMode`→`BlendState` mapping are real.
- **Saves: small XML in localStorage; screenshot `.dat` blobs in IndexedDB.** The `eaSave` JS
  facade (`index.html`) routes by extension: `.dat` → `eaSaveBlob` (IndexedDB
  `eaweb_save/screenshots`), else localStorage. IndexedDB is async but the game reads saves
  synchronously, so `eaSaveBlob.preload()` (awaited by `initRenderJS` before the first tick, raced
  vs a 3s timeout) pulls every blob into memory; it also one-time-migrates old localStorage `.dat`s
  (deleted only after the IDB commit). IDB unavailable → `.dat` falls back to localStorage.
  **Keep the routing in JS — don't make C# talk to IndexedDB directly.**
- **Content loading (`WebContentManager`):** paths must be capital-`Content/` root, lowercase under
  it (case-sensitive on Pages — see root CLAUDE.md). `LoadTexture` prefers **`.dds` → `.rtex` →
  `.png`** (precompiled GPU-ready siblings from `tools/textures/`; PNG decode via StbImageSharp on
  the WASM main thread is the stutter — a cold multi-MP sheet is hundreds of ms). Shaders load via
  `new Effect(gd, bytes)` from offline-compiled `.mgfxo`; effects apply via
  `SpriteBatch.Begin(effect)` (XNA 4.0 model), not `effect.Begin()`.
- **Content-load diagnostics -- KNI's own exception tells you NOTHING, so don't read it as a 404**
  (card 35834236). `TitleContainer.OpenStream` ends in
  `catch (Exception inner) { throw new FileNotFoundException(name, inner); }`, so its `Message`
  is the bare PATH whatever actually failed (HTTP status, decode error, OOM, genuinely missing
  file) and the real cause is only in `InnerException`. index.html's tick guard prints
  `e.message`, so such a failure surfaces as a lone `[loop] TickDotNet threw (1/30):
  Content/gfx/base/756.png` -- which reads like a missing asset and is not one. A whole card was
  filed and investigated on that misreading. Two things now prevent the repeat:
  - **Every** `Load*` path opens through `WebContentManager.OpenOrThrow`, which rethrows as a
    `FlattenedContentLoadException` whose message carries the extension, the sibling it tried
    and the FLATTENED inner chain -- so the tick guard's one-liner names the cause. Textures,
    fonts, effects, sounds and curves alike; a bare `TitleContainer.OpenStream` added anywhere
    in that class reintroduces the trap for that asset kind. It must stay flattened INTO the
    message -- `e.message` is all the JS guard can see, and the wrapper's own TYPE is what tells
    a reader the message is already flattened (the `ContentLoadException` base would also match
    one raised elsewhere, and printing only ITS outer line is the very loss being fixed).
  - A registered sibling that fails to open is logged (`[dds]`/`[rtex] <key>: registered ...
    sibling could not be read -- <chain> -- falling back to PNG`). `PrecompiledTextures.Siblings`
    already said the file was shipped, so a failure there is an anomaly, NOT the ordinary
    PNG-only case -- the old bare `catch { return null; }` silently downgraded the asset to the
    unmipped/StbImageSharp path with no trace. Keep the two cases distinct.
  **Verify with `eaTexProbe('<asset>')`** (`Compat/TexProbe.cs`), not by booting and squinting:
  it drives the real `WebContentManager` path and reports the resolved key, the registered
  sibling, which file the texture ACTUALLY came from (`_textureSources` -- a silent .dds->.png
  fallback is otherwise undetectable, since .rtex and .png both yield `SurfaceFormat.Color`),
  actual vs logical size, and `LevelCount`. `eaTexProbe('GFX/Base/756')` reads 612x612/512x512,
  1 level, Dxt5; `eaTexProbe('GFX/Base/756-v1')` reads 1348x1348/1248x1248, 11 levels -- the
  mipped-vs-unmipped distinction that card conflated, in one call. **Its negative control needs
  no broken asset:** `eaTexProbe('GFX/Base/nope')` drives the rethrow end to end and must end in
  the real cause (`IOException: HTTP request failed. Status:404`), not a bare path -- that is the
  one-call check that the diagnostics still work. Caveat: the probe uses the SHARED manager, so
  an asset owned by a scene-local one (HelpText, Bloom, Credits) cannot be inspected -- probing
  it decodes a second copy and reports on that, not on the one being drawn.
- **DXT textures are PADDED to a mult-of-4; every consumer uses the LOGICAL size (`TextureDims.cs`).**
  BC3/`.dds` blocks are 4×4 and Chrome/ANGLE→D3D11 rejects a block texture whose W/H isn't a
  multiple of 4 (renders black). So `build_textures.py` pads each `.dds` up to a mult-of-4
  (bottom/right only — content keeps its top-left coords) and stamps the original
  ("logical") size into the DDS header's reserved dwords (offsets 32/36 + `"LOGD"` marker).
  The pad is transparent EXCEPT its first 4 px, which replicate the logical edge — see the
  edge-gutter bullet below; that gutter is load-bearing, don't "clean it up".
  `WebContentManager.TryLoadDds` reads it back and registers it in a `ConditionalWeakTable`; the
  extension methods **`Texture2D.LogicalWidth()/LogicalHeight()/LogicalBounds()`** return it (and
  fall through to real `.Width/.Height` for unpadded png/rtex/render targets — a safe no-op). **The
  rule:** PIXEL-space math (frame-rect slicing, cell size, origin, draw scale, aspect, supersample,
  hitbox) → **logical**; NORMALISED UV/texcoord math the GPU samples (shader UV offsets like
  `interpolate.fx`'s frame delta, per-vertex UVs in `Wall.DrawTowerShafts3D`, a shader feather
  window) → **actual padded** size. Whole-texture draws MUST clamp their source to `LogicalBounds()`
  (the wrapper's `Draw` overloads do — else the transparent pad reads BLACK under Opaque blend, e.g.
  the menu frame lines / `GammaMenu` tiling). RenderTargets are never padded. A content-extent
  shader (`starwindow`, `channelflip`) takes a `ContentScale` (= logical/padded) uniform and does its
  `[0,1]` frame math in `tc/ContentScale`; the `SpriteBatchWrapper` sets it centrally in
  `BeginCustom`/`DrawCustom` (the render-space custom-effect batch that `ProceduralStarfield`/
  `DriftingStars` use instead of a private `SpriteBatch`) and in `DrawEffect`.
- **The canary is LEFT ON in the shipped `.dds`. That is deliberate -- do NOT "fix" it back to 0.**
  `build_textures.py --padtest <px>` grossly over-pads every `.dds` so any missed padded-vs-logical
  site shows an obvious ~px artifact in play. The committed textures are built at `--padtest 100`
  (`756-v1.dds` is 1348x1348 for a 1248x1248 logical sheet, and 1248 is already a mult-of-4, so its
  minimal pad would be zero) even though the flag DEFAULTS to 0 -- so the canary is live in every
  build, not only during a test run. It costs ~17% in bytes and VRAM across all ~124 `.dds`, and
  that is the accepted price: a padded-vs-logical slip is runtime-only, easy to miss, and cheap to
  ship by accident. The over-pad has been mistaken for a build accident and reported as a bug
  before, hence this note -- if you think you have found stale padtest output, you have found this.
  Since card `06c6c741` the build ENFORCES it: `build_textures.py` aborts before writing anything
  if a run would pad an asset LESS than the `.dds` it is replacing, so the bare default can no
  longer strip the canary by accident. `--drop-canary` is the deliberate opt-out (tools/CLAUDE.md).
- **A clamped source rect does NOT stop the filter reaching the pad — hence the 4px edge gutter.**
  `LinearClamp` clamps at the TEXTURE border, not at the source rect, so a destination pixel whose
  centre lands in the last half texel bilinearly blends the last content texel with texel `[LW]`.
  While that texel was transparent black, the final ~1px of every tile lost up to 50% of its RGB
  **and alpha** — a hairline at every tile boundary, dark over the opaque Mars sky, bright where the
  `marshills` silhouettes sit over it (Trello `4ddcd13f`; measured -64 luminance in the sky band).
  **On a MIPPED `.dds` that gutter has to be re-derived per level, and the pipeline does it** — the
  pad is metadata, not content, so each level downsamples the LOGICAL image and pads *that* (see
  tools/CLAUDE.md). Filtering the padded canvas instead (a plain `texconv -m 0`) blends content
  into transparent pad as the levels shrink: a 4 px gutter survives `log2(4)=2` levels and then
  fails hard. `check_pad_bleed.py` asserts the property at **every** level, which is what catches it.
  `build_textures.py`'s `edge_gutter()` therefore replicates the logical edge into the first 4 px of
  the pad (last column right, last row down, corner), which makes the filtered result identical to a
  true clamp at **any** pad size, and keeps the sampled 4×4 BC3 blocks free of transparent-black
  endpoints on non-mult-of-4 art (`marsloop*` are 1587/1588 wide). Only 4 px are filled so the
  `--padtest` canary keeps its transparent hole. Guard: `tools/textures/check_pad_bleed.py` (a
  TOLERANCE check that no logical edge on any shipped `.dds` steps away from its replica by more
  than that image's own local variation there — not a proof of pixel equality, which BC3 cannot
  give). `build_textures.py` runs it automatically; **re-run it by hand after anything else touches
  the `.dds`**. Watch for this any time a texture is TILED or stretched far past its native size.
- **`?bgfreeze=<designX>`** stops every background/foreground layer scrolling and parks a tile
  BOUNDARY of each at that design column (`Background.Update`). The Mars/alien-base layers scroll at
  six different speeds, so a tiling/wrap/parallax artifact can only be inspected once it holds
  still. `?bgfreeze=false` turns it off; `?bgfreeze=0` means design column 0 (numerics are parsed
  before the on/off convention, so `=0` can't read as "off"). Two caveats: (1) sub-pixel artifacts
  like the pad bleed vary in strength with where the boundary falls relative to render-target pixel
  centres, so sweep the FRACTIONAL part to cover phases — one frozen frame is one phase, and quite
  possibly a benign one, so it is NOT a worst case; (2) it freezes the tiled layers and the
  starfield only — the doodad fly-by, the holodeck `drawOffset` glitch and `switchTimer`
  cross-fades keep running, so simulator levels still jitter a few px between shots. GOTCHA:
  freezing every layer at the SAME design column stacks layers that normally never
  coincide — at `?bgfreeze=0` the alien base's two
  additive `2331-v5` fog layers land exactly on top of each other and the scene whites out. That is
  the flag doing its job, not a blend/alpha regression; drop the flag to see the real look.
- **The per-tile cull lives in ONE predicate, `BackgroundImage.TileOnScreen` (card 5216412d).** A
  tile at `(x,y)` covers `[x, x+W*size) x [y, y+H*size)` and is drawn iff that overlaps 800x600.
  It used to be four copy-pasted conditions and they had drifted: two measured the tile's WIDTH
  along Y, and the two mirrorX ones had lost their `* size`. **Both slips cull tiles that are
  VISIBLE** (a missing strip at the screen edge, not a spare tile) — a tall tile under-tests its
  height, and a layer drawn bigger than its art under-tests both axes. Neither can show on a
  shipped background (nothing sets `mirrorX`/`mirrorY`, and every live tile is square or wider than
  tall), so **keep the predicate single** — a new call site must call it, never re-inline the
  comparison. Sizes and shapes in play: `size` 1 / 1.5 / 2 / 2.4 / `1/3.238`; the Mars ground is the
  only `[12,1]` grid and the only layer whose `realsize.Y` (600) is not its tile height, which is
  what makes its Y term non-vacuous — for every `[1,1]` layer the Y term is trivially true.
  **Verify with console `eaBgCull()`** (`Compat/BgCullTest.cs`): sweeps the real predicate for
  soundness (a tile that intersects the screen is never culled), dry-runs whole scenario layers —
  mirrored and TALL, shapes no shipped background uses — through the REAL `Draw`, censuses the live
  layers' per-frame `drawn` / `off-screen` counts, then diffs the cull against its pre-card form
  across a scroll-phase sweep. A screenshot cannot verify this cull at all, since every shipping
  configuration errs invisibly; read the decisions as data instead.
- **The right/bottom edge test is STRICT `> 0` (card ef55b76e), and the tie it removed was the
  STEADY STATE, not a rare coincidence.** The interval is half-open, so a tile whose right or
  bottom edge lands exactly on 0 has zero on-screen area; the old `>= 0` submitted it to
  SpriteBatch for nothing. `Draw` starts its grid at `position - realsize`, so for any layer whose
  `realsize` matches its tile the first column sits exactly on the boundary at scroll phase 0 —
  **and only X ever scrolls, so `position.Y` stays 0 forever and the whole top ROW of every
  `[1,1]` layer was in this case on every frame of ordinary play** — half of every Mars parallax
  layer's draws per frame were zero-area. The census prints the pre-card count alongside the live
  one (`drawn 108 (pre-ef55b76e 130)`), so the size of the win stays reproducible from HEAD.
  **Do NOT loosen it back to `>=`.**
  **Why it changes no pixel:** the destination quad spans `[-w, 0]`, which contains no pixel
  centre, so it rasterises nothing — and `RenderScale.Matrix` is a pure scale, so that holds in
  render space too. Corroborated by a 40-image pre/post pixel diff (five backgrounds × eight
  `?bgfreeze` phases, all byte-identical).
  **Three of `eaBgCull()`'s four parts CANNOT FAIL against the current predicate — do not read a
  green tick there as evidence.** Parts 1 and 3 are tautologies (the tightened `TileOnScreen` and
  the suite's `Intersects` are now the same float expression); part 4's differential is one too,
  since `KeptByOldCull` differs only by `>=` vs `>` on the same arguments, so a flip *requires*
  zero area algebraically. They are SENTINELS for a future edit (a margin or inset would produce
  flips with real area), not proof of today's behaviour. Only part 2 — whose arguments come from
  the real `Draw` call sites — can fail on its own. Both differentials carry per-combination and
  per-layer positive controls so a vacuous run reports `VACUOUS`, not `PASS`.
- **Preload / hitch tooling (`Compat/LoadProfiler.cs`):** `?loadlog` times every texture decode,
  flags decodes outside a level's preload phase, accumulates a per-level set the preloader feeds
  back, and exports via console `eaPreloadExport()` → `wwwroot/Content/preload/manifest.txt` (read
  by all builds; release never writes). An **always-on frame-hitch watchdog** logs `[hitch] <ms>ms
  frame in <level>` for any tick > 120ms (not gated by `?loadlog`), skipping preload + boot warm-up.
  A still-hitching level is a manifest DATA gap — fix by playing with `?loadlog` +
  `eaPreloadExport()`, not by code.
  - **The whole capture loop runs HEADLESSLY** — `eahl --repl`, then `eval PreloadExport`
    (`DebugInput.PreloadExport`, a passthrough that exists only because `eval` binds to
    `DebugInput` statics while the browser's `eaPreloadExport` calls `LoadProfiler` direct). The
    "download" lands at `<dir of --out>/preload_manifest.txt`.
  - **`[hitch]` does NOT exist headlessly.** `LoadProfiler.NoteFrame` is called from
    `Pages/Index.razor.cs` alone, so hitch evidence is browser-only. COLD-vs-warm *is* headless-
    valid: it is the preload-BRACKET structure, not a timing threshold.
  - **CAPTURE FROM THE MENU PATH, never `?level=`.** A `?level=` boot has no splash, so
    `QueueIdleWarm`'s 21 space/star assets drain into live gameplay and are recorded as that
    level's assets — 20 junk entries. Drive the menu with `eval Press` instead (main menu ->
    carousel -> difficulty; Challenges is 3 `down` from Start).
  - **A level with ZERO manifest entries cannot be captured in one pass.** `WarmThenLaunch`
    returns early when `ManifestAssets` is empty, so no bracket is opened and the level's
    `Initialize` decodes are attributed to the `(boot)` sentinel. Seed the section from the
    `(boot)` block immediately preceding the `<Level> preload:` line, then re-capture.
  - **`(boot)` manifest lines are INERT** — `ManifestAssets` is only ever called with a `Levels`
    name. Boot/menu gaps can only be fixed in `QueueMenuWarm`/`QueueIdleWarm`, i.e. code.
  - **The warm queues are EXEMPT from the COLD report, by bracket (card 4d47c5ba).**
    `Game1.Warm<T>` wraps every queued decode in `LoadProfiler.BeginWarm`/`EndWarm`, so an asset
    the menu or idle warm *deliberately* decodes is recorded (and exports as `WARM`) but never
    logged as a gap. Before that, the queues reported themselves: a `?menu&loadlog` boot printed
    **50** `(boot)` COLD lines of which **34 were the warm queues doing their job**, and the
    resulting unreadable list is what made card 74b30beb's follow-up name four assets that
    cannot be warmed at all.
    It is a BRACKET, not a `_currentLevel == SentinelBoot` mute like `NoteFrame`'s, precisely so
    the boot decodes that are NOT warm-queue driven still surface -- they are the signal.
  - **The `(boot)` COLD lines that REMAIN are unreachable by any warm queue -- do not "fix" them
    by adding entries.** `QueueMenuWarm`/`QueueIdleWarm` are built in `Game1.LoadContent`, which
    `base.Initialize()` reaches only AFTER every component's own `LoadContent` has run. So
    `gfx/cursor2` (`MousePointer`), `gfx/sprites/awardmentblade` (`AwardmentBlade`) and the
    `gfx/splash/*` set (`SplashScene.AddSplash`, called from `Game1.Initialize`, and into that
    scene's OWN content manager besides) have all already decoded by the time the first queue
    entry is pumped. Warming them is a cache hit that changes nothing, including the log line.
    The steady state on a full-splash boot is that set and nothing else -- `easplashredone`,
    `uglysplash22`, its three `-revenged*` variants, `splash/blank` (twice), `cursor2` and
    `awardmentblade`. Anything OUTSIDE it is a real new gap. (Reducing the set means making a
    boot-time load lazy or deferred, not warming it: see the follow-up cards on the splash's
    three channel-flip variants, of which a run uses one, and on `AwardmentBlade`.)
  - **Do not reformat the `COLD decode in <Level>: <asset>` console line.** Committed headless
    probes under `tools/headless/` grep it, so its shape is an interface. Suppressing a line
    (as the warm bracket does) is fine; changing its wording is not.
  - **MERGE into `manifest.txt`, never replace it.** `Serialize()` emits only the run's own
    recordings — it never merges `Shipped()` — so overwriting the file deletes every curated
    entry for levels the run did not play. Captured per-level sections live in one block at the
    bottom of the file; a level must not also appear in the hand-written blocks above
    (`WarmThenLaunch` enqueues duplicates twice).
  - **A re-capture is a UNION with the existing section, never a blind overwrite** — a capture
    silently comes back SHORT in two ways. (a) The content manager is shared and never unloads,
    so anything an EARLIER level in the same process decoded is absent from a later level's
    section (this really bit: Demo2 captured from the attract rotation runs after Demo1 and its
    export has no `brainanimated` lines). Prefer a single-level process per capture. (b) A run
    that ends early never reaches a level's later phases, and some entries are DEFENSIVE rather
    than observed (`asteroidsmall1..4` is "the spawner picks one at random", not "a run saw all
    four") — those survive only because warming them makes them reappear. Check the section did
    not shrink.
  - **In the BROWSER the recording is not clean-slate** — `Hydrate()` reloads the localStorage
    learned set at boot, so an export can carry entries from earlier sessions. Headless is
    per-process (`HeadlessJsRuntime._loadProfile` is in-memory), which is another reason to
    capture with `eahl`.
- **Level launches are gated by a pre-launch manifest warm.** `Game1.WarmThenLaunch` (every launch
  path incl. attract demos and `?level=`) decodes the level's manifest textures ONE per tick
  (`PumpLevelWarm`) before the scene is Added, so the browser paints between decodes (no "page
  unresponsive"). The menu is frozen during the warm (`menuScene.Enabled=false`; `ComponentBin.Add`
  re-enables) and a Draw-time "LOADING" indicator (`DrawLevelWarmIndicator`, keyed off
  `pendingLevelLaunch != null`) shows meanwhile. The warm is bracketed `BeginPreload`/`EndPreload`
  (two preload summary lines per level under `?loadlog` is expected). No manifest entries → the old
  synchronous launch.
- **Menu art is warmed during the splash** (`Game1.QueueMenuWarm` enqueues; `PumpWarmQueue` drains
  one decode per Update tick; `DrainWarmQueue()` at `startScreen_OnFinished` finishes synchronously
  if the player mashes past the splash — the menu is always fully warm before first shown). A
  second LOW-priority queue (`QueueIdleWarm`) warms the space-background tile set (loaded
  synchronously in `Background.SetSpace()` before any preload bracket can catch it); it drains only
  after the menu queue and `DrainWarmQueue` never touches it. Put menu-critical art in
  `QueueMenuWarm`, pre-level art in `QueueIdleWarm`. Menus share ONE content manager
  (`Scene.Content` == `Game1.content`), which is why warming works. `BragScene.WouldShow()` routes
  credits → menu directly on web (no signed-in gamer).
  - **The level-select stock art is warmed too, off `ScreenshotSaver.StockShots` (card
    4d47c5ba).** `ScreenshotSaver.Init()` loads all twelve SYNCHRONOUSLY from
    `StartScreen.Update`, immediately BEFORE `OnFinished` -- i.e. before `DrainWarmQueue` -- so
    they used to block the Press-Start -> menu handoff for ~350-470ms in Chrome. The pump covers
    them during the splash instead. **`StockShots` is the single list both `Init()` and
    `QueueMenuWarm` iterate; keep it that way** -- `Init` used to hardcode eleven of the twelve
    and the one it missed (`webcamss`) decoded cold on first opening Challenges.
    **Since card 8d6883f3 that list is DERIVED, and `LevelArt` is the one source** -- every
    level with `LevelArt.HasCarouselEntry` contributes its `LevelArt.ScreenshotPath`, deduped,
    and `SubMenuLevelChoice` resolves each entry's image through the SAME lookup instead of
    being handed a path literal (`AddEntryData(briefing, level)`). So adding a carousel level
    means touching `LevelArt` and nothing else; the three hand-maintained copies that had to
    agree are gone. **`General.ScreenshotEnabled` is NOT the membership predicate and cannot be
    made into one** -- it answers "does this level CAPTURE a live thumbnail" and returns the
    `Settings.WebcamScreenshot` opt-in (default OFF) for `WebcamAliens`, so deriving off it
    re-drops the exact asset the original bug was about.
    **Pinned by `tools/headless/probes/stockshots_warm.txt`.** Note what it has to work around:
    a `SubMenuLevelChoice` loads its art in its own `Initialize`, which runs when the submenu is
    first ADDED -- i.e. when the player opens Challenges -- so a dropped level decodes in the
    beat between the keypress and the carousel appearing, and a probe that marks its assertion
    window after the carousel is up passes on the very regression it exists to catch.
    A `?skipsplash`/`?menu` boot auto-presses Start on frame ~1, so the pump never runs and all
    twelve decode at `Init` as before: that is the debug path, not a regression.
  - **`GFX/Help/Controls_Keyboard`/`_Joypad` are in the IDLE queue, and moving them there meant
    moving them to the shared content manager (card 4d47c5ba).** `HelpText` (every attract demo)
    and `InstructionsMenu` (every in-level pause -> Instructions) each used to own a private
    `WebContentManager` they `Unload()`ed on removal, so the 1548x1188 pair was re-decoded on
    every showing forever -- and **no warm and no manifest entry could ever reach them**, since
    `WebContentManager` shares no cache between managers (this is why card 74b30beb tried
    manifest entries, measured them still COLD, and removed them again). Both now read the pair
    from the shared manager; the defensive re-loads that guarded against their own `Unload` are
    gone with it. Idle rather than menu queue because `DrainWarmQueue` is synchronous and neither
    screen is menu-first-frame art. Cost: ~3.7 MB resident for the session -- less than the old
    per-`InstructionsMenu` copies, since every `GameScene` owns one.

## Debug flags & tuning conventions

- All URL flags parse once at boot in `Compat/DebugFlags.cs` (wired via `index.html`
  `getDebugQuery` → `Pages/Index.razor.cs`). **No query = normal boot; tuning overrides are null =>
  the baked `Default*` consts, so a shipped build is byte-identical.** When the user settles on
  values, bake them into the consts and keep the flag as an A/B override.
- `DebugFlags.Active` (the `[debug] flags active` console line) lists only flags that hijack
  boot/levels (`?level=`, `?brainboss`, `?texviewer`, ...). Pure render/feel toggles
  (`?metalscore`, `?slowmotrail`, `?holofilter`, shake/hitstop, reticle size, ...) stay OUT of it.
  **`Active` is not just a log line -- it REFUSES online play**: a menu-session pairing rejects if
  either peer has it (`NetSession.HandleHello`) and a flagged host won't list (`NetListing`, unless
  `?netjip`). So
  the test is "could this flag change the shared run?", not "is this a debug flag?" -- which is
  why `?noattract` is out (card af63f958): it unwires the main menu's idle timeout and nothing
  else, and a joiner needs it precisely because its lobby is a menu. A boot carrying only
  out-of-`Active` flags prints the `no boot-hijacking debug flags` hint instead.
- **Live slider panels** are HTML built in `index.html` OUTSIDE `#app`, only constructed on their
  trigger page (a normal boot has no extra DOM). Pattern: `window.eaXxx(...)` →
  `Compat/DebugInput.SetXxx` ([JSInvokable]) → `DebugFlags.SetXxxOverride`, read every Draw/tick;
  an orange readout prints the bake-ready query string. Existing panels: `eaLazer` (`?lazershot`),
  `eaHue` (`?harness=battleskull`), `eaWalls` (`?wallsonly`/`?walltune`), `eaSpider`
  (`?harness=spiderjump`/`?level=Level2&spiders`/`?spidertune`), `eaHolo`
  (`?level=Tutorial`/`ClassicAliens`/`?holotune`), `eaConnector`
  (`?level=TeamChallenge`/`?harness=connector`/`?connectortune`), `eaWcTune` (`?wctune`),
  `eaTexViewer` (`?texviewer`), `eaNetSim` (`?netsim` on a `?net=` boot, or `eaNetSim.show()`
  from the console). GOTCHA: range inputs need `autocomplete='off'` or Chrome's form restoration
  re-seeds them post-load and desyncs from the defaults.
- Console QA helpers (via `Compat/DebugInput.cs`): `eaPress`/`eaHold` (input), `eaHitboxes()`,
  `eaShake()`, `eaHitstop(ms)`, `eaSlowmo()`, `eaPreloadExport()`, `eaWallPerf(true)`+`eaWallStats()`,
  `eaFps()`+`eaFps.stats()`/`.test()`/`.uncap()`/`.gpu()`,
  `eaNetBg()`+`eaNetBgTest()` (the JIP scenery catch-up dump + its round-trip self-test),
  `eaScore()`+`eaNetScore.test()` (per-slot score/combo dump + the co-op score-reconciliation
  self-test),
  `eaNetCombo.test()` (the co-op per-slot combo + powerup self-test — card 1a3ad45a),
  `eaNetCosmetic()` (the decorative-swarm replication self-test — card 9a3175d0; run it inside
  a level to cover the client apply leg),
  `eaBinTest()` (the ComponentBin lifecycle scenario suite — run from the main menu),
  `eaKickTest()` (the co-op kick/block rules + v6 handshake codec — best from the main menu),
  `eaSlotTest()` (the co-op primary-slot negotiation + the v8 handshake codec, plus the stale
  menu roster, `?netdropgrant`'s one-shot latch and couch-seat reuse -- leave-no-trace,
  so it is safe at any point in play),
  `eaKillShips()` (asplode the locally-owned ships to force a death/reset on demand),
  `eaBgCull()` (the background tile-cull oracle — run from inside a level),
  `eaTeamSeat()` (TeamChallenge's partner-seat resolver over every pad-connection mask -- pure,
  so it needs neither a level nor a gamepad),
  `eaFlySpiders()` (the live flying-spider population split background/foreground plus the
  flatten settings in force — run from inside Level 2),
  `eaNetRoster()` (dump the net roster + per-ship positions + reset counter at this instant),
  `eaOracleRoster()` (the OFFLINE roster -- works at the menu, where `eaNetRoster` refuses),
  `eaNetSnap()` (the world-snapshot unknown-id attribution suite -- run from the main menu),
  `eaNetCouchJoin()` (seat a couch player now, the way a gamepad Start does),
  `eaTexProbe('GFX/Base/756')` (drive the real texture load path for one asset and read the
  result as data -- see "Content-load diagnostics" above).

### Frame profiler / FPS HUD (`Compat/FrameProfiler.cs` + `eaFps` in index.html, card 22e655b5)

**The loop is rAF-driven, so a frame RATE readout is vsync-capped and near-useless for
optimization**: at 100Hz a 2ms frame and a 9ms frame both read "100 fps". The HUD therefore
reports the measured rate AND the numbers that keep moving under the cap -- frame COST in ms and
`1000/tickMs` HEADROOM fps -- side by side, never conflated. (Same distinction `WallProfiler`
draws for the tower pass; the two agree on the same fight.)

- **Visible by default in every dev build, invisible in the published one**, keyed off
  `window.eaBuildHash === 'dev'` (deploy.yml stamps a real fingerprint at publish). That covers
  Debug AND a local Release publish with no `#if DEBUG`. When hidden, nothing is built, the tick
  hook stays null and the GL prototypes are unpatched -- zero cost, not just invisible.
- Compact by default (fps + headroom); click the mode tag to expand to the frame-time sparkline,
  the per-section ms rows and the GL draw-call count. **The panel is `pointer-events:none`** except
  that tag and the mode checkboxes -- it sits over a shoot-'em-up where Mouse1 anywhere on the
  canvas fires, and a clickable panel would eat every shot aimed at that corner.
- Sections (they sum to the tick; whatever is left shows as `other`): `update` (parent) with
  `components`/`collision`/`net` sub-rows, `scene` (= `DrawInner`, all components incl. bloom),
  `post` (slowmo trail + holo-sim), `present` (the letterbox gamma blit), **`swap`** (`EndDraw`).
  **`swap` matters more than it sounds:** `Game.Tick` presents in `EndDraw`, OUTSIDE the `Draw`
  override, and WebGL commands are queued -- so a GPU-bound frame's real cost lands there. Add a
  section in one line: an enum member + `long t = FrameProfiler.Begin(); ... End(Section.X, t)`.
- **GL draw calls are counted in JS** (`drawElements`/`drawArrays` prototypes are patched), not in
  `SpriteBatchWrapper`: BlazorGL's cost is per-CALL, and JS sees every source at once (sprite
  batches, bloom passes, the walls' 3D primitives) with no engine surgery. It is the most
  actionable single number in this port -- watch it, not the sprite count.
- **Headroom is CPU-derived and overstates a GPU-bound frame.** Two opt-in modes close that:
  `?fpsuncapped` (HUD "uncap") drives the loop off a `MessageChannel` instead of rAF so the
  MEASURED rate stops being vsync-gated (`setTimeout(0)` would not do -- it is clamped to ~4ms,
  i.e. a fake 250fps ceiling); `?fpsgpu` (HUD "gpu sync") issues `gl.finish()` per tick so GPU
  execution becomes a measurable wait. Cross-check: uncapped measured 233fps vs 241 derived
  headroom on the menu, so the derived number is honest there.
- **These flags are the one group NOT parsed in `DebugFlags.cs`** -- the HUD is JS-owned, so the
  `eaFps` IIFE regex-reads `location.search` itself (the `eaWalls`/`eaSpider` panel precedent).
  Nothing about them reaches C#, so they are inherently out of `DebugFlags.Active` and can never
  make a co-op session reject a peer. `?fpshud` (force on, works on the live site) /
  `?fpshud=full` (expanded) / `?nofps` (hide in a dev build) / `?fpsuncapped` / `?fpsgpu`.
  `?fps=` is NOT this (that is the sprite harness' playback rate).
- **Auto-suppressed on the screenshot-verification pages** (`?harness=`, `?textshot`,
  `?bulletshot`, `?lazershot`, `?castbrain`, `?castshow`, `?texviewer`, `?gamebrowser`,
  `?spiderphase=`, `?wcmothershipfreeze=`) -- those scenes draw their own readouts in the same
  top-left corner, and this project verifies almost everything by screenshot, so relying on
  someone remembering `?nofps` would put the HUD in every harness capture. `?fpshud` overrides.
- `?fpsuncapped` / `?fpsgpu` are LOOP flags and apply with or without the panel (so
  `?nofps&fpsuncapped` is a valid "measure, don't show me" boot).
- **GOTCHA -- an unfocused window makes every rate reading garbage** and Chrome throttles it to a
  rate the C#-side staleness test (mean interval > 100ms) does NOT catch: a focused menu read
  2.5ms/frame, the same page unfocused read 22.8ms. `document.hidden || !document.hasFocus()` is
  the authoritative signal, so BOTH the HUD and `eaFps.stats()` prefix an UNFOCUSED warning and the
  HUD re-arms (dropping the poisoned samples) when focus returns.
- **Verify the window maths as DATA with `eaFps.test(workMs, intervalMs, frames)`** -- it pushes a
  synthetic series through the real accumulator and asserts the vsync trap itself: `work` ms every
  `interval` ms must read `1000/interval` fps and `1000/work` headroom. A profiler that reported
  the work rate as "fps" fails it loudly. (The `eaNetSim.test` idiom; a python mirror would drift.)

## Component lifecycle (`ComponentBin`) — the spawn/death contract

Card 02d9ad67 hardened the 2008 deferred birth/death lists. The contract every spawn/despawn
site now lives under:

- **Births are INSTANT.** `ComponentBin.Add` puts the component straight into `Game.Components`
  (KNI journals the update/draw registrations, so it never Updates before the next tick, but it
  IS immediately visible to collisions, `Oracle.Get*` scans and purges — there is no hidden
  "pending spawn" world). **KNI runs `Initialize()` synchronously inside the Add**, so a call
  site must fully configure the object (Setup/Make*/property writes) BEFORE `Add` —
  `tools/audit_add_order.py` is the lint (run it after adding spawn sites; the repo is clean).
  Event subscriptions (`OnDeath +=`) after Add are fine.
- **Deaths stay QUEUED** (`Remove` → deathList) — instant removal would corrupt the collision
  pass and change within-tick gameplay — but the list flushes TWICE per tick: the original
  mid-tick point (after component updates, before collisions) AND `TopOfTickFlush` (before any
  component updates), so a collision-phase kill never gets one more "zombie" Update (the
  fires-from-the-grave / final-bullet-across-the-paused-screen bug class).
- **`Purge<T>` arms a standing filter** until the next top-of-tick flush: any `Add` of a T in
  that window (a component updating later the same tick, a kill side effect in that tick's
  collision phase) is diverted to the recycle pool — a clear-all followed by a late same-tick
  spawn now actually clears all. **Opt out with `Purge<T>(standing: false)` ONLY for a
  clear-the-field-and-respawn-NOW purge** whose own call chain re-adds a T in the same tick
  (sole current case: `GameScene.UpdateStartup`'s pre-spawn clear — the ships and Get Ready
  banners follow in the same tick; the filter would eat them, which is exactly the no-ship
  regression `?binlog` caught during development).
- **The puppet layer is EXEMPT from the standing filter** (card 74403f83). `Game1.UpdateInner`
  drains the net rx AFTER `base.Update` in the same tick, so a purge armed by
  `GameScene.UpdateWin`/`UpdateResetting` -- or by `NetApplyReset`, which purges from INSIDE the
  drain itself -- is still live when the host's authoritative spawns arrive. `ComponentBin.Add`
  therefore skips the filter while `NetPuppets.Constructing`, symmetric with the
  `SuppressWorldSpawn` exemption immediately above it. A client that ate one diverged
  PERMANENTLY and SILENTLY: `OnSpawn` registered the id either way, and `OnSnapshotEntry`'s
  self-heal only rebuilds ids it has NEVER seen, so the ghost was never drawn, never collidable,
  and `snapUnk` never climbed. Safe at teardown because `EvSpawn` and the snapshot path are both
  gated on `GameScene.NetActiveScene`, which `Terminate` nulls BEFORE its own purges.
- **A caller that ADOPTS what it adds must use `ComponentBin.TryAdd`, not `Add`.** `Add` diverts
  silently -- that is the point, ordinary game code must not have to care -- but the net layer's
  ship puppets keep the reference and gate their retry on it being null, so adopting a diverted
  ship stranded that player for the rest of the session (`NetSession.SpawnPuppet` and
  `SpawnFriend`; the couch/friend one bites more often, since couch players hit the resets that
  arm `Purge<PlayerShip>`). `TryAdd` reports whether the component actually landed; on false,
  leave the reference clear and let the retry fire next tick. Note the ship SHOULD be purged by
  a reset (`SpawnAllPlayers` respawns every seated slot), so verify-and-retry is correct here
  and exempting would be wrong.
- **Wire-driven banners are NOT exempt, deliberately** (card 74403f83). `NetSession`'s
  `EvMessage`/`EvUnlock` adds can be eaten by a standing `Purge<AnimatedMessage>`, and that
  MATCHES the host: the level script is host-only and only runs in `GameState.Normal`, so the
  host cannot emit a beat while it is itself in Win or Resetting, and both peers enter those
  states from the host's own broadcast. Reaching it needs the two state machines to have already
  diverged -- a different bug, which letting the banner through would only mask. Nothing dangles
  either way (one-shot, no reference held past the `Add`). Don't "fix" it.
- **Adds while the world is `Push`ed (paused) join the freeze**: an `AlienDrawableGameComponent`
  added under a pause goes in `Enabled=false` and registers in the newest pause layer, so
  `Pop()` thaws it. Non-world components (pause menus, darkener, overlays) stay live — they ARE
  the pause UI. A spawn that races the pause appears parked and resumes on unpause, by design.
- **A pass that walks a live collection must FREEZE its count first.** Instant births mean any
  callback that spawns (a kill's asteroid split / powerup drop, a wall-hit explosion) grows
  `Game.Components` — and every mirror list fed by `ComponentAdded` — *while* the pass is
  running. `CollisionHandler.DetectCollisions` sized its `boxes` list to a count taken at entry
  but re-read `collidables.Count` in its later loops, so a mid-pass spawn indexed `boxes` out of
  range (an intermittent `IndexOutOfRange` swallowed by the `tickJS` guard, i.e. a dropped
  frame, not a visible crash) and could read a previous frame's cells; the inner all-pairs
  `foreach` over the same live list was one spawn away from `InvalidOperationException`. Fixed
  by running the whole pass over the entry-time `count` — a collidable born mid-pass joins the
  NEXT pass, which is what the old deferred birthList did anyway. **Apply the same rule to any
  new phase that indexes a parallel array by collection position.**
  **The contract is PINNED by `eaBinTest()`** (card bcdc7430), scenarios 5 and 6. The fix
  froze THREE bounds: the outer fill loop, the all-pairs scan inside it, and the resolution
  loop. Scenario 6 covers the resolution loop; scenario 5 covers the other two together, and
  has to — only a non-gridded type's callback runs during the fill phase at all, so its
  `CollisionMultibox` spawner is the sole way to reach either bound.
  Neither scenario leans on the `boxes[m]` out-of-range throw: whether it fires depends on the
  high-water mark `boxes` accumulated from prior play, so such a test would pass on the broken
  code and its verdict would be a function of session history. Each instead PLANTS the fault's
  precondition — scenario 5 needs only `List<T>` version-checking its enumerator; scenario 6
  runs a warm-up pass plus a filler collidable it then removes, so the newborn lands on a stale
  `boxes` entry the clear loop (`i != count`) skipped — and then ASSERTS the plant took, since a
  silently-missing plant is the one way it could go quietly vacuous (a busy world can shift the
  index, which is why the suite is menu-only). Both also carry a positive control.
  Verified by reverting `DetectCollisions` to its pre-fix form: scenario 5 reports
  `InvalidOperationException` and scenario 6 reports the newborn participating, in the menu AND
  mid-level.
- **Diagnostics:** `?binlog` logs filter diverts + pause-frozen adds, and reports how many
  passes `DetectCollisions` carried through a mid-pass collidable add (the condition above —
  it fires in the hundreds during ordinary play, so it is a live proof the path is exercised,
  not a warning); `eaBinTest()` runs the scripted scenario suite (`Compat/BinTest.cs`) against
  the live bin and prints PASS/FAIL -- 27 assertions across 8 scenarios (four lifecycle, two
  net-layer, and the two collision-pass ones in the bullet above). **Run it from the MAIN
  MENU.** A few checks are PRECONDITIONS rather than assertions about the code, and a failed
  one short-circuits the rest of its scenario, so read the FAIL line rather than the tally.
  The two net scenarios cover `TryAdd`'s landed/diverted contract and the puppet-filter
  exemption, the latter driven END TO END through the real `NetPuppets.OnSpawn`
  (`NetPuppets.Enable` needs only a `Game` plus the ServiceHelper bin/score, so no transport
  and no paired session are required). They SKIP themselves when a co-op session OR any
  `GameScene` is up -- including the attract demo the menu launches by itself -- because they
  arm `Purge<AlienDrawableGameComponent>` for real, which near a live world would wipe it.
  The suite is strictly leave-no-trace and must stay that way: it expires the filter it arms
  and prunes every scratch component, so back-to-back runs in one tick all read the same
  tally; a run that leaked state would make the NEXT run report phantom failures.
  `eaKillShips()` asplodes every locally-owned `PlayerShip`
  through the real `Asplode()`→`Die()` path (remote/friend puppets skipped) — the repeatable
  way to reach a death/reset, since `AllShipsDead` needs BOTH co-op ships down and waiting on
  the `?aiplayer` AI to die is neither timely nor repeatable.

## Input

- **Real keyboard works** — KNI maps `event.keyCode` directly, so Enter/arrows/WASD/Esc are correct
  for users. **Synthetic JS `KeyboardEvent`s do NOT work** (KNI's WASM interop throws on the faked
  `keyCode` and can leave a key stuck). When driving the browser, use real OS keys
  (claude-in-chrome `computer` `key`), click-to-focus the canvas first — or better:
- **For automated/headless input use `eaPress(...)`.** `InputHandler` polls once per tick, so a
  scripted keydown+keyup between ticks is dropped. `eaPress('Enter')` (tap) / `eaPress('Left', 30)`
  (hold ~30 ticks) injects a per-key tick counter drained inside the tick. Keys:
  Up/Down/Left/Right/Enter/Esc/Mouse1/Generic_Start (+ w/a/s/d, start/select→Enter, back→Esc,
  fire→Mouse1). Rapid repeats of the SAME key collapse — space distinct taps by a tick.
  **Touch/mobile** uses the same seam: `eaHold(key, down)` → `DebugInput.Hold` holds until
  released; both drained by `DebugInput.Consume`. Driving fullscreen via automation fails
  (synthetic clicks carry no `navigator.userActivation`) — harness limit, not a bug.
- **Menus are mouse-selectable + clickable.** Each `DrawMenu` records the design-space box of every
  entry it draws via `MenuSub1.RecordEntryHit(index, centre, w, h)` (locked/undrawn entries
  skipped); `MenuSub1.HandleMouse()` (gated on the `normal` state) maps the cursor to hover-select
  and `MyKeys.Mouse1` to select+invoke, either resetting the attract idle timeout. **A new
  `DrawMenu` override must call `RecordEntryHit` per entry or its menu won't be clickable.** The
  level-choice carousel sets `mouseHoverSelects = false` (click picks directly). Out of scope: the
  `GammaMenu`/`ScreenResizeMenu` sliders and `PlayerSettingsMenu`.
- **Aiming cursor / reticle:** KNI never applies `IsMouseVisible` to the DOM, so C# owns
  `canvas.style.cursor` via `Compat/CursorInterop` → `eaCursor.set(mode)`: `menu` (arrow),
  `hidden` (during the level-start intro sprite), `reticle` (the reticle IS the OS cursor via
  `cursor:url(reticle/<px>.png)` — zero-lag; `HWMouse=true` opts back to the arrow). Driven off
  `MousePointer.Visible`. The reticle **size-tracks the window via a ladder of cursor images**
  (`wwwroot/reticle/<px>.png`, 24..96 step 8, built by `tools/cursor/build_cursor.py`):
  `MousePointer.ChooseCursorPx()` picks the rung nearest `ReticleDesignPx (30) * windowPerDesign`,
  re-picked every tick so a resize swaps rungs. **Invariant: every image's bars run edge to edge
  (alpha bbox == full canvas)** — `CssHandoffScale()` sizes the intro sprite from the SAME
  `ChooseCursorPx`, which is what makes the sprite→cursor handoff never pop. Tune with
  `?reticlesize=<designpx>`, bake into `MousePointer.DefaultReticleDesignPx`. Verify OFFLINE (an OS
  cursor never appears in a canvas screenshot): check PNG bboxes + simulate
  `ChooseCursorPx`/`CssHandoffScale` across window sizes.
- **Fullscreen:** DOM Fullscreen API via `Compat/FullscreenInterop.cs` → `window.eaFullscreen`
  (KNI's `IsFullScreen` is a no-op on BlazorGL); the in-menu option routes through it. The
  browser-reserved fullscreen-exit Esc also reaches KNI, so `fullscreenchange`→exit calls
  `eaSuppressEsc` → `DebugInput.SuppressEsc`, masking the raw Esc briefly. **F11** is a dedicated
  toggle. The corner fullscreen button + touch overlay (D-pad/FIRE/BACK, touch devices only) live in
  `index.html` **outside `#app`** so they survive Blazor's mount — any new HUD/overlay button
  follows the same pattern.
- **Local co-op needs a SECOND DEVICE, and TeamChallenge is the only level that seats one for you
  (card e6927ef8).** Every other seat comes from `GameScene.CheckPlayerJoins`, which needs a real
  `Start` press, so its device is present by construction. `TeamChallenge.Initialize` seats the
  partner itself, and the 2008 code seated `ControlDevice.PadOne` unconditionally -- which on this
  port made the level UNPLAYABLE for a keyboard-only player: `GameScene.Update` raises
  `pauseRequested` on every tick a seated pad reads `!InputHandler.PadConnected(i)`, so the world
  sat in the pause menu forever (`ticks=0 prog=2/52` over 37 sim-seconds).
  - **Both seats are resolved by pure functions now, and neither can hold a pad that is absent.**
    `ResolvePrimarySeat` seats slot 0 with the device that LAUNCHED the level when it can drive a
    ship (the 2008 code hard-seated `Keyboard`, which hands a pad-only player a ship they cannot
    steer), falling back to `Keyboard` otherwise -- including for a pad that has gone away since
    the menu, which would otherwise pause-loop just as badly. `ResolvePartnerSeat` then takes the
    lowest-indexed connected pad THE PRIMARY IS NOT USING (the original two-human co-op --
    gamepads do work here, KNI ships `nkast.Wasm.Dom/js/Gamepad.*.js`), else `ControlDevice.AI` as
    an auto-pilot partner. The level-select briefing says so; an in-level banner has nowhere safe
    to live (added during `Startup` it is eaten by `UpdateStartup`'s 1300ms
    `Purge<AnimatedMessage>`, added in `Normal` it collides with the script's own "Get ready!"
    beat).
  - **A pad Start press TAKES OVER the bot's seat -- and without that, two-human co-op would be
    broken by this fix.** The browser Gamepad API only exposes a pad after a button is pressed on
    it IN THE PAGE, so player two's idle pad reads DISCONNECTED while `Initialize` resolves the
    seats: the AI takes the seat and their later Start press would seat a THIRD player, which the
    tether (always `GetShips()[0]/[1]`) leaves flying free while the bot stays bolted to player
    one. `GameScene.AddPlayer` therefore consults a new `protected virtual TryAdoptJoinDevice`
    hook first; `TeamChallenge` claims a joining PAD for the seat the bot holds
    (`Oracle.SetController` re-points the seat, `PlayerShip.AdoptController` the live ship, so the
    slot keeps its score and its place in the tether). Only while the bot holds it -- a second pad
    joining a genuine two-human game still adds a player.
  - **How good the bot partner is remains unmeasured.** The completion matrix's TeamChallenge row
    is a TIMEOUT at ~90 deaths with both ships bot-driven; the only clean run (VICTORY 402s, 0
    deaths) was an `?invuln` CONTROL. So the auto-pilot makes the level playable and reachable, not
    provably finishable -- worth a look before promising more.
  - **INVARIANT: the resolved device is never a pad that is not connected** -- exactly the
    force-pause's precondition, so the loop is unreachable by construction. `GameScene`'s guard
    itself is deliberately UNTOUCHED: a pad dying MID-RUN should still say so.
  - The net-session branch is untouched (online the partner is the remote peer, and seating a
    local pad would both squat the puppet's slot and trip that guard).
  - **`ControlDevice.Generic` is NOT the fix and cannot be** until it has an input case: it is a
    first-class device in the menus, `CheckPlayerJoins`, the pause guard and `PlayerSettingsMenu`,
    but `PlayerShip.Update` has no `Generic` case AND **no key produces `MyKeys.Generic_Start` on
    this port** (`InputHandler.keysToCheck[8]` is an empty array), so it is unreachable outside
    `eaPress`. A real second KEYBOARD player -- that case plus a key map, i.e. keyboard local co-op
    on every level, which this port has never had -- is its own follow-up card.
  - Flags/verification: `?teampartner=ai|pad` (`pad` = the old unconditional `PadOne`, the
    deliberate bug reproduction) and console **`eaTeamSeat()`** -- all 16 pad masks x 3 overrides
    through the REAL resolver, plus the pre-card always-`PadOne` policy as the negative control
    (the `eaNetScore.test()` rule). Seating is a decision, not a picture: a screenshot cannot show
    it, and covering it live would need four physical gamepads.

## Rendering / text

- **Alpha is STRAIGHT everywhere** (see root CLAUDE.md: `AlphaBlend` → `BlendState.NonPremultiplied`,
  never `BlendState.AlphaBlend`). Two deliberate premultiplied-INTERMEDIATE exceptions, both
  "flatten translucent stacks into an RT, composite once": the text flatten and the group flatten
  below. Straight tints like `new Color(1,1,1,a)` are correct as written.
- **The custom font atlas is SUPERSAMPLED (3×) — never route `menufont` through stock
  `SpriteBatch.DrawString`.** `Cropping`/kerning/`LineSpacing` stay design-size (so raw
  `font.MeasureString` in ~40 layout sites is unchanged) while `BoundsInTexture` is 3×;
  `SpriteBatchWrapper.DrawStringScaled` (all four `DrawString` overloads) draws each glyph at
  `Cropping.Size / BoundsInTexture.Size`. Reverting to stock renders glyphs 3× too big. Builder +
  per-glyph overrides: `tools/font/` (see tools/CLAUDE.md).
- **Score / floating-text = ONE flattened sprite (`SpriteBatchWrapper.DrawShadowString`).** Two
  translucent shadow+text `DrawString`s bleed the shadow through the glyphs; `DrawShadowString`
  rasterises shadow-then-text at full opacity into the shared grow-only `metalRT`
  (**premultiplied**: `PremultiplyOver` rasterise → One/InvSrcAlpha composite — stacking two
  straight layers hard-edges the AA) and composites once at the target alpha. Don't revert
  `ScoreVisualiser.DrawStr` or the `FloatingText` "pop" type to two `DrawString`s. Chrome sheen
  (`metal.fx`) is ON for the score by default (`?metalscore=0` A/Bs plain; menus keep chrome
  regardless). **The score's glint sweep is EVENT-DRIVEN**: each player's score sweeps once when
  its leading digit rolls over (`ScoreInfo.UpdateGlint`; skips reset-to-0 and checkpoint restores);
  combo + "Press Start" prompts keep static chrome (`ParkedGlint`); menus keep the periodic
  `MetalTime` marquee. Sweep length = `SpriteBatchWrapper.MetalSweepDuration`.
- **Menu chrome rows are CACHED — don't revert to per-frame `DrawMetalString`.**
  `MenuSub1.DrawMenu`/`MenuSubWithSkull.DrawRows` use `DrawMetalStringCached` (plain-text raster is
  time-independent, content-addressed on `(text,tint)`; only the metal.fx composite runs per frame
  so the glint still sweeps). `DrawShadowStringCached` is the slot-keyed variant (score HUD owns
  slots 0..15; the tutorial banner uses 100). The skull frame FILL is likewise one cached mask
  texture (`EnsureFillMask`) drawn as one tinted quad per row.
- **Group-flatten for translucent multi-part sprites** (`SpriteBatchWrapper.BeginGroupFlatten`/
  `EndGroupFlatten`): overlapping straight-alpha sprites at partial alpha double-brighten; bracket
  their draws to flatten opaque into a shared RT (premultiplied capture), composite once at group
  alpha. Used by the background-fog `FlyingSpider` (body+wings fade as one silhouette) -- which
  since card 9c92962e ships on the SWARM variant: `FlyingSpiderSwarm` (owned by `Level2` like
  `floor`) brackets ONE flatten around the whole population per frame, and the per-spider bracket
  in `FlyingSpider.Draw` is only the `?flyspiderflatten=per` A/B override plus the fallback for a
  scene with no driver (the sprite harness).
  **Measured cost (card 9c92962e): +2.0 GL draw calls per flattened GROUP** -- it roughly DOUBLES
  the calls the group would otherwise cost (a fog spider is ~2.0 calls unflattened, ~4.0
  flattened), and BlazorGL's cost is per-CALL. Perfectly linear in the group count (pinned bench,
  N=0/40/80: 20.2 / 180.4 / 340.1 calls per frame vs 20.2 / 102.8 / -- unflattened). So the flatten
  is cheap for a handful of groups and expensive for a swarm; **if you are about to bracket
  something that exists in the dozens, flatten the whole POPULATION as one group instead** --
  `FlyingSpiderSwarm` does exactly that and measures at ~1 call TOTAL regardless of N (N=40/80:
  102.1 / 183.0, i.e. the same slope as no flatten at all), which is why it is the shipped default.
  GOTCHA: the shared `groupRT` is **grow-only** and `BeginGroupFlatten`'s `Clear` is whole-RT, so
  the largest group ever flattened in a session sets the clear cost of every later one. Compare box
  sizes on FRESH page loads, and don't mix a swarm-sized group with per-sprite ones in one scene.
- **Verify flattened-text changes with `?textshot`** (`Compat/TextShowcaseScene.cs` — frozen
  score/combo/pop rows, plain + chrome, live animation phases), not live screenshots.

## Feel / post FX

- **Juice (`Compat/Juice.cs`): screen shake + hit-stop.** Shake is the trauma model
  (`Juice.AddTrauma` from explosions/blasts/player death; strength = trauma², decays ~0.7s, max
  7px/1° — deliberately halved from the first pass, full shake impacted gameplay). Applied at the
  PRESENT BLIT only — no gameplay coordinate, collision, or mouse mapping is touched. An explosion
  series can opt out per instance (`Explosion.Setup(..., noShake: true)` — the L3 BattleSkull death
  does, so only its finale shakes). Hit-stop folds into `Game1.Update`'s time scale as
  `Juice.TimeScale` while REAL time keeps ticking Juice/shake/input; the per-kill micro-stop +
  boss-kill stop are **OFF by default** (read as stutter; `?hitstop=1` re-enables); player death
  keeps its 180ms stop. GOTCHA: hit-stop must decrement on UNSCALED dt (`Juice.Update` runs before
  the time scale) or it freezes and never thaws. Draw-time cosmetics keep animating during a freeze
  by design. `?shake=<0..3>`, `eaShake()`, `eaHitstop(ms)`.
- **Slow-motion ghost trails (`Game1.ApplySlowmoTrail`).** The 1up slowmo adds an accumulation-
  buffer motion blur on the composited+bloomed `sceneTarget` before the gamma blit:
  `trail = trail*decay + scene*(1-decay)`, mixed back with an eased `slowmoTrailMix` (~0.25s); the
  first slowmo frame seeds the trail (no dark flash). Static pixels converge to the input — no
  blow-out. Defaults decay 0.88 / strength 0.8, ON; `?slowmotrail=0` / `?slowmotraildecay=` /
  `?slowmotrailstrength=`; QA with console `eaSlowmo()` (no-op unless a ship is alive). **A new
  full-frame post-process goes in this same spot in `Draw`** (operate on `sceneTarget`, leave RT on
  it) and uses the raw `spriteBatch` (identity), not `spriteBatchWrapper`.
- **Tutorial/Classic "holo-sim" filter (`Compat/HoloSim.cs` + `tools/shaders/src/holosim.fx`).**
  Fullscreen scanlines + cyan edge cast + phosphor-green pull (breathing on a slow pulse) + hard
  "channel surf" glitch spikes on Activating/Terminating messages and `Background.Jump()` hiccups.
  Applied in `Game1.ApplyHoloSim` right after `ApplySlowmoTrail` (ping-pongs `sceneTarget` →
  `holoRT` → back — a SpriteBatch effect pass can't read its own target). **Lifecycle is
  POKE-driven:** `TutorialLevel.Update` / `ClassicAliens.Update` call `HoloSim.Poke()` every tick;
  the mix fades out when poking stops, so any exit path turns it off with no plumbing. Shipped
  values are dialed way down (`DefaultBurstScale` 0.1, ~10 hiccups/min). Flags: `?holofilter=` (0 =
  whole filter off) / `?holoburst=` / `?hologreen=` / `?hologreenpulse=` / `?holostaticrate=`;
  `eaHolo` panel. The tutorial's pacing: text + action land on the same beat (non-halting
  `message(..., halting:false)` over a halting spawner), powerup lessons advance-on-pickup
  (`WaitForPickupEvent`, timeout ceiling so a passive player still progresses — a full unattended
  run finishes ~2min). Layout rule: two `TutorialMessage` banners share one spot, so overlap is
  only ever text-with-ACTION — `LinkWith` each message to its gate.

## Sprite harness (details)

`?harness=<Obj>` boots one object frozen on a space background, drawn by its own `Draw()` through
the real pipeline (see root CLAUDE.md for when to use it). Code: `Compat/HarnessScene.cs` +
`Compat/HarnessRegistry.cs` (name→factory; **add an object in ONE line** — call its `New*`+`Setup`).
Human picker `wwwroot/harness.html` — keep its list in sync with the registry. Companion flags:
`?frame=` `?play` `?bg=space|spaceclassic|holodeck|mars|base|basedark` `?pos=x,y` `?objscale=`
(alias `?size`) `?rot=` `?fps=` (with `?play`; set low to watch the interpolation shader tween).
Parked objects with CIRCULAR hitboxes get their real collision ring drawn (green) — sprite-vs-hitbox
size mismatches (the supersample bug class: a rescaled sheet whose hand-rolled radius forgot
`DrawScale`) are visible by eye; box hitboxes show no ring. Caveat: objects whose Draw depends on
Update-reached state show their spawned/idle pose — bosses are best-effort. Special modes:
`?harness=eyeattract` forces the JunkBoss attract sheet (`HarnessForceAttract`; try `&play&fps=2` to
prove the `interpolate.fx` frame-interpolation shader tweens); `?harness=blast` loops the blast
lifecycle (`?blastloop=` sweep speed);
`?harness=spiderjump` loops the spider crawl→jump→land cycle; `?harness=connector` animates the
ship connector with no ships; `?harness=battleskull` shows the colorize tuner; `?harness=brainboss`
plays the boss overlays (Draw-driven); `?bulletshot` is another frozen showcase (bullets).
`?castbrain` boots the end-credits Cast screen (its own mode).

## Feature notes

- **Bomb blast (`Blast.cs`):** lifecycle math is `ApplyLifecycle(p)` (shared by live `Update` and
  the harness scrubber). Fade = `SmoothStep(1,0,p)`; collision tied to it (`Collides = fade >=
  ActiveAlpha` 0.5); hitbox radius uses `DrawScale` (supersample divided out) at
  `DefaultHitRadiusFactor` 0.8-of-visible. `?blastactive=`/`?blasthit=` override live;
  `?harness=blast` overlays the ring (green = damaging) + readout.
- **Flying spider (Level 2):** reuses the HD reared-up sheet, so `FlyingSpider.SizeFactor`
  (baked `DefaultSizeFactor` 0.75) scales sprite AND box hitbox together; `?flyspiderscale=`.
  Fast-boot a dense endless swarm with **`?level=Level2&flyspiders`** (background variant, the only
  user of the group-flatten RT round trip) or **`?flyspiders=fg`** (foreground, same sprites, NO
  flatten).
  - **`?flyspiders=fg` is NOT a flatten A/B and never was** (card 9c92962e corrects the earlier
    reading). Background and foreground spiders differ in SIX things -- the flatten, `Collides`
    (background ones are never killed, so they accumulate), `Speed` (x1.11 vs x1.35, so background
    ones linger ~22% longer), `scale`, alpha and `DrawOrder` -- and the populations were never
    equalised, so the old "background 7.3ms vs foreground 2.8ms" gap was mostly POPULATION.
  - **The honest rig is `?flyspidercount=<N>` + `?flyspiderflatten=`.** `?flyspidercount=<N>`
    replaces the endless 5.5/s stream with a PINNED bench: exactly N spiders on a deterministic
    grid, `Speed = 0` so none crosses off-screen and dies, timers still ticking so the draw work
    stays representative. Bench spiders are also forced `Collides = false`, which for the
    FOREGROUND variant is a real change from live play -- otherwise the player would shoot the
    pinned population down mid-run and an un-invulned ship could be killed by the grid it is
    measuring. So a foreground bench sits out the collision pass and is a DRAW-cost rig (GL calls
    / frame ms), not a whole-frame one; background spiders never collided anyway. `?flyspiderflatten=per|0|swarm` then varies ONLY the flatten:
    `swarm` (the SHIPPED default since this card: `FlyingSpiderSwarm`, one RT round trip for the
    whole population) / `per` (the pre-card path, one RT round trip per spider) / `0` (none).
    `?flyspiderbox=<half>` overrides the flatten bbox
    half-extent (baked `FlyingSpider.DefaultFlattenBoxHalf` 200 design px) -- a per-call cost is
    flat in the box size, a fill cost scales with its area, so the sweep tells the two apart.
    Console `eaFlySpiders()` prints the live count split background/foreground plus the settings
    in force, so a figure never travels without its conditions.
  - **Measured (pinned bench, GL draw calls per frame):** N=0 baseline 20.2; per-spider flatten
    180.4 (N=40) / 340.1 (N=80) = **3.99 calls/spider**; no flatten 102.8 (N=40) = 2.07
    calls/spider; swarm 102.1 (N=40) / 183.0 (N=80) = 2.02 calls/spider. So the per-spider flatten
    costs **+1.97 GL calls per background spider** and the swarm flatten costs **~1 call total**,
    at identical population.
  - **The flatten earns its keep visually** -- verify with `?harness=flyingspiderbg` (the fog
    variant, frozen) against `&flyspiderflatten=0`: with it off the wings read visibly more solid
    than the body (they composite to ~0.36 over a 0.2 body), with it on the silhouette fades as
    one. The harness pins the flap/swivel phase (`Initialize` skips `Randomize()` while
    `DebugFlags.Harness` is set) precisely so the two boots are the same pose and therefore
    comparable; live play keeps the randomization. The swarm variant preserves the per-spider
    silhouette exactly (identical body+wing math); it differs only where two SPIDERS overlap,
    which also stops double-brightening -- at alpha 0.2 over Mars dust that is not perceptible,
    which is what let it ship as the default.
  - In-game the fog layer draws at alpha 0.2 over bright Mars dust, where the spiders are already
    near-invisible -- **do not try to judge the flatten from a live Level 2 screenshot**, use the
    harness stills.
- **Laser FX (`Quad.cs` beam + `LazerGenerator` chargeup):** chargeup is a windup animation
  (per-particle scale ramps 1→`DefaultPeakChargeScale` 4) + a layered "energy well" orb (stacked
  additive `lazerglow`: blue halo → cyan-white → white-hot core — the same recipe the ship
  connector reuses); callers pass the real windup via `SetWindup(seconds, loop)`. Beam ends are
  domed with glow+core caps (`DefaultCapScale` 1.0). Tendrils spawn stochastically
  (`DefaultArcRate` 2/s on `Quad`'s private FX RNG — can't desync co-op), live 0.25..0.5s, drift
  along the beam clamped to its span (`DefaultTendrilSpeed` 30). Tune with **`?lazershot`**
  (`Compat/LazerShowcaseScene.cs` + the `eaLazer` panel); flags `?lazerchargescale= ?lazercapscale=
  ?lazerarcs= ?lazertendrilspeed= ?lazerarclife=`. Straight-alpha additive tints — do NOT
  premultiply.
- **Ship-connector docking lightning (`ShipConnector.cs`):** breathing base sprite + fractal
  lightning bolts + crackle tendrils + a churning energy-well orb per ship (decorrelated phases).
  Self-contained reimplementation of the Quad techniques (own FX RNG, static scratch buffers). FX
  advance on RAW Draw time (`fxTime += dt` in `Draw`) so they crackle through hit-stop — nothing in
  `Update`. Flags `?connectorbolts= ?connectorarcs= ?connectorjitter= ?connectorpulse=
  ?connectorglow=`; `eaConnector` panel; **verify with `?harness=connector`** (TeamChallenge
  auto-pauses on focus loss, a moving target the harness sidesteps).
- **BattleSkull colorize tuner (`Compat/HarnessColorize.cs`):** `BattleSkull.Draw` hue-remaps the
  alienboss sprite via `colorizeEffect.RangeTarget = (-10, 10, HitPointsNormalized*100)`; the
  harness overrides band+target from `?huestart= ?hueend= ?huetarget= ?huecycle ?hueloop=` (only
  while `?harness=battleskull` is up — play is byte-identical) + the `eaHue` live panel. Settled
  values get written back into `BattleSkull.Draw`'s hard-coded `Vector3`.
- **Mars jumping spider (`Spider.cs`) — jump is ANIMATION-DRIVEN and the dialing is done/baked.**
  A grounded spider launches when its unwrapped frame accumulator (`animAcc`) crosses the jump
  beat; a one-time "count back" presets the entry frame from the real scroll so the beat coincides
  with a (random) launch X: `entryFrame = jumpBeatFrame - fps*(dist/scroll)`. Baked:
  `DefaultJumpFrame` 5, `LandFrame` 42, `GroundY` 485; shadow (37,4)×0.95 + air offset (14,1) live
  as `DebugFlags` defaults (the air offset connects the airborne `spiderjump` sheet's differing
  anchor to the ground frames). `?spider*` knobs (`?spiderjumpframe= ?spiderlandframe= ?spiderjumpx=
  ?spidershadowx/y/scale= ?spiderloop= ?spiderphase=`) apply to live play AND the
  `?harness=spiderjump` viz (`Spider.HarnessApplyPhase` = the deterministic cycle sim;
  `?spiderphase=` freezes for a screenshot); `eaSpider` panel; watch live with
  `?level=Level2&spiders&invuln`. The harness shadow goes through the real `Floor.ShadowScalars` /
  `DrawShadowScalars` so the preview is byte-identical to the in-game cast.
- **Landed Mars-UFO placement offsets (`Compat/LandedOffsets.cs` + `Content/data/landed_offsets.json`
  + author tool `wwwroot/landed-editor.html`):** per parked-still sprite: `landed` (draw nudge),
  `takeoff` (one-time Position shift at lift-off), `shadow`/`shadowSize` (via the generic
  `AlienDrawableGameComponent.ShadowOffset`/`ShadowSize` fields that `Floor.CollidesWith` reads —
  one shadow, no double-draw). Identity/missing = original behaviour. Consumers: `UFO`
  (`SetStationary`/`Draw`/lift-off/`Setup` reset) and `StationaryBoss`. The JSON is read late via
  `TitleContainer`, so when iterating in-game bust the cache
  (`fetch('Content/data/landed_offsets.json',{cache:'reload'})`). Re-export from the tool; don't
  hand-edit.
- **Hitbox overlay (`Compat/HitboxOverlay.cs`):** `?hitboxes` or console `eaHitboxes()` draws every
  live collidable's shape over the game — box/multibox cyan, circle green, line orange; active
  bright, inactive dim. Hooked in `Game1.DrawInner` after game+bloom (un-bloomed, on top). Built to
  see draw-offset-vs-hitbox mismatches (e.g. the landed UFOs). Out of `DebugFlags.Active`.
- **SpiderBoss "helper mothership" (`SpiderHelperMothership.cs`):** the spider boss is only hurt by
  a `Lazer`, so after a difficulty-scaled un-damaged idle (counted from first landing;
  `SpiderBoss.EffectiveHelperIdleMs`, Easy ~6s → Inzane ~37s) a mothership eases in top-centre
  (quad ease-out to rest; leave = quad ease-in; speed difficulty-scaled), winds up a
  `LazerGenerator` swarm (`SpiderHelperWindupSeconds` 2.5), fires a `Lazer`, leaves. On Easy/Medium
  it AIMS at a standing boss (`SpiderBoss.GetAimPoint()`); flying or Hard+ = straight down. It's
  "fake killable" (enormous HP, blink + `fakeHits` redden); `Bullet.cs` lists it so bullets stop on
  it but do NOT sustain combo (immortal = combo farm). Trigger lives in `SpiderBoss.Update`
  (`helpTimer`, one helper at a time). Flags: `?spiderhelperidle= ?spiderhelperhovery=
  ?spiderhelperspeed= ?spiderhelperwindup= ?spiderhelperenterpower= ?spiderhelperfire=
  ?spiderhelperlead=`; `?harness=spiderhelper` (`?pos=400,10` for the in-game framing); fast boot
  `?level=Level2&spiderboss&invuln&spiderhelperidle=3`.
  **Perfectly-vertical lazers are SAFE — don't reintroduce a tilt.** The old
  `FillCollisionMatrixLine` DDA hung 100%-CPU on exactly-vertical lines (per-step X delta below the
  float32 ULP so the exit condition never advanced); every DDA loop is now bounded by a
  `maxLineSteps` cap, so the helper fires exactly `PiOver2` and no near-axis-aligned lazer can hang
  the game.
- **Level-3 walls are real 3D towers (`Wall.DrawTowerShafts3D`; design docs
  `plans/walls-3d-towers.md` + `plans/spike-wall3d.md`).** Each block extrudes downward to the
  ground; gameplay plane + `CollisionLevelMap` stay the flat tops (byte-identical). Key invariants:
  - Base projection factor **0.66 is NOT a taste knob** — it equals the alien-base ground layer's
    `scrollspeedmodifier`, which is what glues tower bases to the scrolling floor. Change it and
    the bases slide.
  - Side faces are genuine 3D geometry in ONE batched `DrawUserIndexedPrimitives` via a shared
    `BasicEffect` (`SpriteBatchWrapper.DrawGeometry3D`) — BlazorGL's cost is per-CALL, not
    per-vertex (~0.4 ms/tick over flat). Real 3D (not pre-projected quads) so the GPU perspective
    divide gives correct UVs; the camera reproduces `Wall.Project()` exactly
    (`tools/walls/preview_wall3d.py` asserts it).
  - **No depth buffer, provably:** occlusion about the VP is acyclic for equal-height shafts, so a
    CPU painter's sort by VP distance is exact — certified by `tools/walls/verify_tower_order.py`
    over the real grids (it also rejects two plausible wrong sort keys). Tops draw last.
  - Face emission: only outer edges (`isfree`) AND eye-facing. **UV orientation kills seams on both
    axes** — along-edge must follow the axis the edge runs along, down-the-shaft must start at the
    cell edge the wall hangs from (reverses west vs east). NO half-texel inset (adjacent atlas
    cells are the correct continuation).
  - **Down the shaft the sheet CONTINUES out of the block's cell — it must not run back across it**
    (card 0f7fc977). Starting at the hanging edge is the rim-seam fix and stays; running *into* the
    cell made every side face a mirror of its own cap, legible as a mirror precisely because the
    sheet tiles and could have carried on. How far it runs is `?wallsidetile` × the shaft's true
    height in block footprints (1 = a side texel the world size of a top-face one = 2.70 cells at
    the shipped numbers; **baked at 4** — honest scale reads short because a steeply foreshortened
    shaft buries most of its length in its far few pixels).
  - **The wrap is walked on the CPU in `AddFace`, never by a wrapping sampler.** `.dds` are padded
    to a mult-of-4 with content top-left, so GPU wrap would wrap at the PADDED edge and run every
    shaft into transparent pad (and 756-v1 is NPOT besides). So the face is cut at every cell
    crossing on top of the `bands` cuts and each strip maps through the one cell its midpoint falls
    in — every emitted UV stays inside the logical sheet. Cost: ~4 → ~14 quads/face at the baked
    tiling, one batched call unchanged, tower pass 0.73 → 1.29 ms.
  - **756-v1 SHIPS A MIP CHAIN** (card `110153c7`); it is the only mipped `.dds` in the project.
    At the baked `?wallsidetile=4` a shaft spends ~10.8 cells down its length, so its far end
    minifies hard, and with bilinear alone it aliased — which read as a SHIMMER because the wall
    scrolls, so no still frame could show it. Nothing engine-side opts in: KNI maps
    `TextureFilter.Linear` to `LINEAR_MIPMAP_LINEAR` as soon as `LevelCount > 1`, so uploading
    the levels (`WebContentManager.TryLoadDds`) is the whole change, and `SamplerState.LinearClamp`
    becomes trilinear by itself. NPOT + mips needs **WebGL 2**, which BlazorGL uses (verified: the
    canvas holds a `webgl2` context). **The tower TOPS are mipped too**, not just the shafts — they
    are sprite-drawn at ~5x minification (a 156px cell into a ~32px block), so they pick a level as
    well; that is a fix, not a regression, but it is why this touched more than the shafts.
    A/B it live with **`?nomips`** (uploads level 0 only, so every `.dds` falls back to bilinear).
    Weigh tilings on `preview_wall3d.py --ladder` (bilinear row over trilinear row) and
    **`--shimmer`**, which scores the aliasing as a number instead of by eye: bilinear worsens with
    density (4.15 / 6.26 / 8.21 / 9.93 at tile 1/2/4/8) while trilinear stays flat (~1.2-1.8). So
    mips at tile 4 are steadier than bilinear at tile 1, and **dropping `Wall.DefaultSideTile`
    toward 2 is NOT the cheaper fix** — it is strictly worse and loses the "reads tall" win.
    That tool's `sample()` is bilinear CLAMP on purpose: it models `DrawGeometry3D`'s `LinearClamp`
    exactly, so it neither invents a moire (point sampling would) nor prettifies the sheet's own
    8→0 wrap (wrapping would); `sample_tri` is the trilinear form layered on top.
  - Lifetime: `Wall.DeathY` defers unload past the bottom edge (a base leads/trails its cap by
    ~154px, so dying at y>600 popped visible towers — this also delays the level's next event
    ~0.6s, intended); `Wall.EntryLead` spawns higher so towers enter base-first (bottom-row grids
    used to pop in). Both collapse to the flat values with `?walltowers=0`.
  - Grid files load via `TitleContainer.OpenStream` (`Wall.OpenLevelGrid`) — **never
    `new StreamReader(path)`** (WASM FS has no wwwroot content; it throws on web).
  - Haze = real `BasicEffect` distance fog toward the measured floor colour (baked `?wallfog` 0.55,
    RGB(46,125,201)); per-face shading = flat vertex colour (`?wallfacelight` 0.35,
    `?wallfaceangle` 140). The old sprite-slice path's `FaceShadeEffect`/`756-v1-side.png`/
    `build_wall_side.py` are deleted — preserved in commit `906f344` if ever wanted. Fog wisps
    (additive `2331-v5`) tile by position, never a drifting source rect (null samplerState =
    LinearClamp — an out-of-bounds window clamps).
  - Flags: `?walltowers=0` (exact flat look) · `?walldepth= ?wallfog= ?wallfogcolor= ?wallsidedark=
    ?wallsidetile= ?wallfacelight= ?wallfaceangle= ?walltoplift= ?wall3dbands= ?wallwisps=
    ?wallwispspeed=` ·
    fast-boot `?level=Level3&wallsonly` (+ `eaWalls` panel) · diagnostics `?walltrace` (logs
    POP IN/OUT) + `?level=Level3&wallpoptest` (ten slow-scroll poptest grids).
    **`?wallsonly` also serves OwnLevel** (card b174b00f -- there it drops that level's two
    spawners and keeps its `Walls(2)`), with **`?nowalls`** as the OwnLevel-only complement; the
    pair is the churn-attribution rig -- see the AI section's OwnLevel row. `?walltoplift` is
    COSMETIC ONLY (collision unmoved — the sprite drifts off its hitbox; keep small, check
    `?hitboxes`).
  - **Verify drawing OFFLINE** (`tools/walls/preview_wall3d.py` contact sheet) — the wall scrolls
    and a backgrounded tab's canvas is black. **Measure frame cost with the tab FOCUSED** (Chrome
    throttles background tabs; FPS alone is vsync-capped). `eaWallPerf(true)` + the panel's stats
    readout give fps / frame ms / tower-pass ms.
  - The wall texture `GFX/Base/756-v1` is sampled as an 8×8 wrapping grid — it must tile seamlessly
    on all four edges and keep dims a multiple of 8; upscaling flow lives in `tools/walls/`
    (see tools/CLAUDE.md). Fixed in passing: variation 2 (OwnLevel) now loads the real
    `Content/levels/level3.txt` instead of silently falling back to a hard-coded grid.
- **BrainBoss animated overlay patches (`BrainBossOverlays.cs` + `Content/data/brainoverlays.json`):**
  selected regions of the huge static boss sprite are AI-animated offline (`tools/brainanim/`, see
  tools/CLAUDE.md) and composited back as feathered sprite-sheet patches. The game reads ONLY the
  manifest (`TitleContainer`+`JsonDocument`; missing/bad → static boss). `Draw` (after `base.Draw`)
  pins each patch to its brain-texel crop so it tracks Position/DrawScale/pulse, tints by the
  boss's live `color` (reddens on low HP; the death fade is an alpha fade of `color`, so overlays
  dissolve in lockstep), ping-pongs, and rides the frame-interpolation shader unless
  `interpolate:false` (the eye reads better stepped). **Invariant: never animate the top of the
  sprite** — texture rows < ~373 are above the screen at the boss's draw position; every region box
  has `ty0 >= ~400`. A region with `triggerAvgSeconds` rests on frame 0 (== the untouched crop) and
  plays one cycle on a `RandomFromAverage` roll (the eye uses 15s); omit for a continuous loop
  (pods). The roll consumes the shared RNG at frame rate — fine now, switch to a private FX RNG if
  lockstep ever matters. Verify: `tools/brainanim/preview_ingame.py` (offline contact sheet + gif)
  or `?harness=brainboss`. Shipped overlays: `eye_reveal`, `pods_flicker` (`lens_right` dropped).
- **Animated Braineroid (`Braineroid.cs`):** 20-frame 5×4 sheet `brainanimated` (built by
  `tools/textures/build_brain_sheet.py`) drawn through the interpolation shader
  (`interpolationOptions = always`; fps 0.4 → ~50s loop reads smooth) + an additive blue glow
  behind it (PNG — DXT would band the smooth gradient; the sheet itself is DXT). Registered in
  `AlienDrawableGameComponent.DesignFrameWidth` at 100 (on-screen size = 100×scale regardless of
  cell px). GOTCHAS: the off-screen wrap margin must use `texture.Width/columns * DrawScale` (ONE
  frame, not the whole row — else brains drift far off and the Braineroids minigame never clears);
  `Initialize` sets `pulsate = 1f` because the harness freezes Update (0 would draw scale-0
  invisible). Each instance randomizes start frame + pulse phases.
  The end-credits Cast "Brain Spawn" (`CastDisplayer.braineroid`) draws the same sheet by hand (no
  interpolation, `DefaultBrainFps` 10, `DefaultBrainScale` 1.7) + glow via `DrawBrainGlow`; tune
  via `?castbrain&castbrainscale=&castbrainfps=`, bake into the `DefaultBrain*` consts.
- **Earth fly-by (Level 1):** the hero earth texture is a vertical strip cropped to what shows —
  **invariant: `Background.QueueEarth`/`QueueEarthSim` set `doodadscrollspeed.X = 0`; don't
  re-enable X drift or the cut sides show.** `WaitForDoodadEvent` (polls `Background.DoodadActive`)
  gates the asteroid belt until the earth leaves. What sells the fly-by is freezing the STARS, not
  speeding the earth: `Background.DoodadStarSlowdownFactor()` pulls `scrollspeedmodifier` to 0.082
  while it crosses (wall-clock ramps ~1.2s in / ~1.6s out). The asteroid BELT gets the same depth
  cue via a second factor (`BeltStarSlowdownFactor` 0.37, `EngageBeltSlowdown`/`Disengage...` from
  `Level1.spawner_OnFinished` / `AsteroidSpawner.OnFinished`; Demo1 wired identically). The two
  factors combine by `MathHelper.Min`, not multiplication (Demo1 can overlap them).
  `Background.Reset()` clears belt state on level entry; don't add a checkpoint inside the belt.
  The `AsteroidChase`/`SpaceDodge` warp scroll is deliberately out of scope.
- **Andromeda fly-by:** straight-alpha (NOT additive — the enum value 1 is AlphaBlend);
  `QueueAndromeda` pins the footprint via `doodadscale = AndromedaDesignWidth(840)/doodad.Width`,
  so higher-res art drops in with no code change (builder: `tools/nebula/`).
- **Mars far-hills:** three parallax layers `marshills1/2/3` with own `scrollspeedmodifier`s
  (0.33/0.53/0.85 — `hillScrolls` in `Background.SetMars`); textures are procedural
  (`tools/mars/build_marshills.py` + live editor). Only ~design y 405..450 is ever visible above
  the `marsloop` ground.
- **Trailers = an embedded YouTube overlay, NOT ported video.** The old `TrailerScene`
  (`Content.Load<Video>`) is DEAD — constructed but never added; don't re-wire it or reintroduce
  any `VFX/*` load (VC-1 `.wmv` can't play in a browser). Options → Trailers calls
  `Compat/TrailerInterop.Play(youtubeId)` → `window.eaTrailer(id)` (outside `#app`):
  youtube-nocookie iframe + Back button, pauses/resumes `eaMusic` (AudioContext suspend/resume),
  refocuses the canvas on close. Ids live in `MenuScene.trailerMenu_*Selected`.
- **Splash channel-swap SFX:** the "I made this!" splash channel-flips the old meme into the
  revenged image (`channelflip.fx`); `SplashScene.Update` fires `PlayCue("channelswap")` once when
  the glitch starts (gated on `variantPicked`, one-shot via `flipSoundPlayed`). Autoplay caveat: the
  splash runs before any user gesture, so on a cold first load the burst may be silently dropped
  (suspended AudioContext) — **don't add a click-to-start gate to "fix" it**; the project boots
  straight through by design. The cue's owner is `tools/audio/pick_channelswap.py` (see
  tools/CLAUDE.md).

### The AI player (`ControlDevice.AI`) + the AI bench (card f4d1721f)

One bot drives three things: the attract demos, the Mechanical-Friends cheat and `?aiplayer`.
It lives entirely in `PlayerShip`: `DoAIFire` (target pick + `doAIBomb`), `DoAIMove` (steering),
and the wall-navigation helpers. Two of its knobs are **difficulty-scaled** (card c10e3e7f, below);
the rest are tier-independent.

- **`Oracle.GetBaddies()` IS the AI's entire world model.** A type missing from that list is a type
  the bot can neither shoot nor dodge, silently. That was the root cause of two of the three
  reported symptoms: `BrainBoss` and `FakeBoss` (which gate the end of Level 3) were absent, so the
  AI parked next to a halting boss shooting nothing; `SpiderBoss` and `PlasmaBall` were absent as
  HAZARDS, so the spider-boss fight was unwinnable-looking because the bot was not dodging a boss
  it could not see. **Adding an enemy type to the game means adding it here too.**
- **Two predicates decide what to DO with each entry, and each mirrors a contract elsewhere.**
  `PlayerShip.IsAiShootable` mirrors the type list in **`Bullet.CollidesWith`** (what a bullet can
  damage); `PlayerShip.IsAiThreat` mirrors the damage branch of **`PlayerShip.CollidesWith`** (what
  can kill the ship). Change the mirrored list and you must change the predicate -- the drift
  between them is what stalled the bot. `IsAiShootable` deliberately EXCLUDES three of the bullet
  types: `SpiderBoss` (bullets deflect off it by design -- only a `Lazer` hurts it), the
  `SpiderHelperMothership` (fake-killable with an enormous HP pool, and it is the thing that kills
  the spider boss for you), and `Asteroid` (no combo, splits when shot, the belt is to be flown
  through). `IsAiPriorityTarget` then discounts a level-HALTING boss's distance so it outranks the
  trash that boss keeps spawning.
- **`AlienDrawableGameComponent.ObservedVelocity`** is measured from real position deltas, sampled
  at the top of `Update`. Use it, not `SpeedVector`, for anything predicting where an enemy is
  going: `SpeedVector` is derived from `_speed`/`_direction` and reads ZERO for every type that
  writes `Position` directly -- including the spider boss's screen-crossing fly states. (Same idea
  as the net layer's observed velocity, kept independent because the AI must work with no session.)
- **Steering is low-passed as a VECTOR (`DefaultSteerSmoothMs` 90).** `DoAIMove` sums a dozen
  competing terms and `Move()` consumes only the resulting ANGLE, so when the big terms nearly
  cancelled a tiny residual swung the heading right round -- measured at ~1050 deg/s inside a
  Level-3 wall versus ~20 deg/s on an open screen. Smoothing the vector makes opposing votes cancel
  toward zero (the ship coasts, which is correct) while a sustained vote still converges in a few
  frames. Rate-limiting the ANGLE instead is wrong: it forces a genuine 180 reversal the long way
  round.
- **Wall navigation is look-ahead-by-TIME + a COMMITTED gap.** The 2008 code probed a fixed
  `41.67 * MaxSpeed` = ~13.75px against tiles 67..267px wide, SLAMMED the steer on a hit
  (`direction.X = -max(|direction.Y|,1)`), and re-picked left-vs-right every tick. Now:
  `WallReactionMs` (baked 420) times the real closing speed (`MaxSpeed` + the wall's own
  `ObservedVelocity`); `ColumnScore` grades every column by clearance/travel/columns-to-cross and
  `GapSwitchMargin` stops it flip-flopping; the steer is proportional.
  - **`ColumnScore` is GRADED, never a pass/fail `IsPassable`.** In a dense maze section no column
    is clear for the full look-ahead, so a boolean test reports "nothing passable" -- and an
    earlier revision then held station and let the wall scroll into the ship. There is always a
    least-bad column and the AI must always be heading for one.
  - **Urgency is measured in PIXELS to the blocking row (`DistanceToBlockedRow`), never in row
    COUNTS.** A row count cannot tell "a slab 60px above me" from "a slab 1000px above me"; using
    it made the avoidance push permanent and pinned the ship against the bottom of the screen.
  - **`ClampIntoWallSpace` runs AFTER the smoothing and writes back into `aiSteer`.** It is the
    hard "do not fly into that" override; low-passing it turns a full reversal into a suggestion
    (measured 46 wall contacts vs the old code's 8). Writing it back stops a flickering probe
    making the clamp its own oscillator.
  - **Bench a GRID offline with `tools/sim/aiwallnav`, not by booting the level** (card b4972696).
    It reflects into the built `EvilAliensWeb.dll` and calls these very methods against the real
    `Wall.Setup` grids, so it is the shipped code rather than a mirror, and it A/Bs a grid, or any
    of the `?aireact` / `?aiscanrows` / `?aicrosspenalty` knobs, in seconds with no browser. Per grid it reports `ChooseGapColumn` switches/s,
    lateral sign flips/s, `ClampIntoWallSpace` X-reversals and upward forces/s, contacts/s and the
    share of ticks under urgency. **It is the wall term ONLY** -- `turn deg/s` / `revs/s` are the
    whole steering sum, so a claim about the BOT still needs `?aibench`. **Rebuild the game before
    running it** (it references the built DLL, so an unrebuilt edit is benched in its old form,
    silently). This is the instrument card f4d1721f lacked, which is why OwnLevel's grid was never
    in its tuning loop; see tools/CLAUDE.md and the tool's README for the rest of its rig caveats.
- **Fast movers are dodged by CLOSEST APPROACH, not by current distance** (`EvadeMovingThreat`,
  `DefaultThreatLeadMs` 700). Radial repulsion from something crossing the screen pushes the ship
  ALONG its path -- precisely the spider boss's screen-wide sweep. Slow/static threats keep the
  original distance-based repulsion; `Lazer` keeps its own distance-to-line case.
- **Verify AI changes as DATA with `?aibench`** (`Compat/AiBench.cs`), never by watching it play.
  Counters: `contacts` (wall touches, counted in `PlayerShip.CollidesWith` **before** the
  invulnerability gate so a `?invuln` run still scores every clip AND survives to measure all six
  wall sections), `revs/s` + `turn` (the jitter pair), `coast%`/`ticks`/`pos`/`steer`,
  `idle%` (had a shootable on screen and did not shoot -- the signature of a target the AI is blind
  to), `prog=<event>/<total>` and the run verdict.
  **A low jitter score alone proves nothing** -- a bot wedged in a corner scores a perfect
  `revs/s=0.0 turn=0deg/s`, which happened during this card; that is why `coast%`/`ticks`/`pos`
  are in the line.
- **Soak headlessly with `eaAiBench.soak(simSeconds)`.** It ticks the real loop
  (`Game1.BenchTick`) at a fixed 60Hz dt with NO Draw, in chunks. This is the only reliable way to
  soak from automation: a **backgrounded tab throttles rAF *and* MessageChannel to ~1Hz**, so
  `?aiff` (or anything else rAF-driven) measures almost nothing unless the window is focused.
  `?aiff=<n>` runs n sims per rendered frame for a WATCHABLE fast-forward, each with a synthesised
  60Hz dt -- not the frame's own, which `IsFixedTimeStep=false` inflates by ~n.
- **Damping is DEMAND-DRIVEN, and both halves matter.** `Move()` discards the steer's magnitude
  and thrusts at full acceleration along its ANGLE, so a weak-but-nonzero steer is not a gentle
  nudge -- it is full throttle. Hence: **park** below `DefaultSteerParkDemand` (just above the
  0.8 station pull), so an idle ship coasts to a stop instead of sailing past its station and
  back forever; and **smooth adaptively**, collapsing the time constant from
  `DefaultSteerSmoothMs` toward `DefaultSteerSmoothUrgentMs` as the push grows, because heavy
  damping is exactly wrong when something is bearing down. Two things were tried against the same
  idle-fidget symptom and are documented in place as REVERTED -- don't re-derive them: a
  velocity-damped "arrive" at the station (it contains `-SpeedVector`, so it brakes every real
  manoeuvre: coast 28% -> 59%, spider-boss deaths 24 -> 70) and a tighter deadzone alone.
- **The SpiderBoss fight is scripted, so its counters are too** (unashamedly special-cased -- it
  is a set-piece with fixed choreography). Only a `Lazer` hurts it and a big UFO fires one AT THE
  PLAYER, so the AI spares the single big UFO furthest from every ship and lets the boss walk
  into the beam -- but NOT during a fly-by, where dodging a sweep and a beam at once is what kills
  it. Its three fixed lanes and its hard-coded X-600 landing column are avoided for the WHOLE
  manoeuvre (the boss is parked off-screen and stationary during the "Danger!" arrow, so neither
  the movement prediction nor the distance field can see it coming); escape is DOWNWARD out of a
  lane (UFOs enter from the top) and LEFT out of a landing.
- **`SpiderBoss`'s landing now sweeps to the right screen edge -- a deliberate GAMEPLAY change,
  not a port artifact.** The descent is hard-coded to X 600, which left a safe pocket beside it
  that trivialised the landing; the AI found it instantly and parked there. Marked as such in
  `SpiderBoss.cs`. It affects human players too.
- **The top screen edge gets its own strong push** (`TopEdgeAvoidStrength`): it is where UFOs
  spawn, and the stock edge term caps at `maxSteerStrength` 4, which loses to a lane escape (18)
  and pins the ship on the ceiling to be exploded by something spawning on it.
- **Every avoidance field here shares the `(1-t)^p` falloff shape** (`ThreatFieldStrength`) -- a
  flat push across a band fights the screen bounds instead of easing off once the ship is clear.
- **Per-tier skill (card c10e3e7f) is keyed off `Settings.EffectiveDifficulty`, NOT
  `CurrentDifficulty`.** `Demo1/2/3` call `LockDifficulty(Hard)` and `TutorialLevel` locks
  `Very_Hard`; `LockDifficulty` only redirects `DifficultyModifier` (what the ENEMIES scale by)
  while `CurrentDifficulty` keeps reporting the player's menu choice. Keying off the latter flies
  an Easy-tier pilot against a Hard-tier attract demo for anyone whose saved setting is Easy --
  invisible until someone changes their menu setting and wonders why the demo got worse. Anything
  picking a tier for the LIVE fight wants `EffectiveDifficulty`; menus and the save file want
  `CurrentDifficulty`. `DifficultyModifier` is wrong for this too: it ramps with elapsed play time
  and adapts on death, so a bot keyed to it would silently get smarter the longer a run went on
  and every `?aibench` number would become a function of run length.
  - `PlayerShip.AiSkillByDifficulty[]` -- ABSOLUTE final values per tier (the
    `WebcamLevel.Tunings[]` idiom: no modifier divisor, no within-run ramp). The **Very_Hard row
    IS the `Default*` consts**, so the configuration card f4d1721f measured stays exactly where it
    was measured. `?ai*` overrides still win over the row.
  - The spread is deliberately **subtle**: a Mechanical Friend that visibly cannot play defeats
    the point of having one. Expect the gradient to show in the readout and NOT to the eye.
  - **Only `ThreatFieldBasePx` and `AimSpreadRad` scale, and that is a MEASURED result.** Each
    candidate was isolated by holding the tier fixed (so the level's own difficulty scaling could
    not confound it) and moving one `?ai*` override: aim `15deg -> 57.3deg` moved Level1 progress
    `50/64 -> 45/64`; field `190 -> 30px` moved spider-boss deaths `11 -> 14`.
  - **`?aireact` and `?aithreatlead` were dropped as "dials that do nothing". That verdict is
    RETIRED (card b174b00f): both have large authority and the original RIGS were blind.** Each
    was a single run (n=1, on fights this file itself calls +-30% noise) and each happened to pick
    the one rig where its knob is inert. Re-measured through `eahl` with no browser, N=6,
    Very_Hard:
    - **`?aireact` 80 / 420 / 2000ms** on `?level=OwnLevel&wallsonly` -- deterministic, since with
      the spawners gone all six runs are identical and any movement is pure signal: turn
      **88 / 229 / 944 deg/s**, contacts **0 / 0 / 13**, and at 2000ms the level stops completing
      (`prog 5/5 -> 4/5`). Level 3's grids read 22 / 29 / 115 over the same sweep, which is exactly
      why the original Level-3 isolation saw `420 -> 80ms` move nothing. It doesn't move much
      THERE.
    - **`?aithreatlead` 80 / 700 / 2000ms** on **CrazyGame** (30 homing bullets, no walls): deaths
      **15.3 / 3.8 / 7.0** and progress **~6 / ~20 / ~16 of 21**, with the 80 and 700 ranges not
      overlapping on either measure. The baked 700 sits near an interior optimum. On the SPIDERBOSS
      rig -- the original's -- it still moves nothing (deaths 6.2 / 6.0 / 5.8, ranges fully
      overlapping), which is a fact about that rig and not about the knob.
    - **The 80ms CrazyGame row is the durable caution.** It posts the LOWEST churn anywhere in the
      sweep (117 deg/s against 411 at the baked value) while dying three times as often: the bot
      has stopped dodging, and a bot that has stopped dodging is smooth. **Never read `turn` as
      quality without a survival column beside it.**
    Whether either knob should be TIER-SCALED is therefore an OPEN tuning question with a working
    instrument, not a closed one. Choosing per-tier values is its own measurement campaign and is
    deliberately not done here.
    **`contacts` still cannot see wall look-ahead on a level with enemies** -- `ClampIntoWallSpace`
    is a hard override that runs however far ahead the bot looked, so it floors the metric; the
    reading above can see it because a walls-only rig lets `turn` carry the signal instead.
  - **Comparing tiers end-to-end cannot verify any of this** -- the enemies scale with the same
    tier (and Level3's wall SCROLL SPEED is `4.3 * GetDifficultyValue / 16.667`, i.e. 0.090 px/ms
    at Easy to 0.310 at Inzane -- the `0.43 *` variant is `Level3.popTestSlow`, `?wallpoptest`
    only, and is a TENTH of any real wall section), so an outcome delta
    between tiers is unattributable. The non-confounded observation is the `eaAiBench()` line's
    `skill effective=<tier> field= aim=` row, which reports the RESOLVED values; verifying the
    attract-demo case means booting `?menu&aibench&difficulty=Easy` and watching it flip from
    `effective=Easy` to `effective=Hard` as `Demo1` starts.
- Flags: `?aibench` · `?aiff=<2-64>` · `?aismooth= ?aismoothurgent= ?aipark= ?aireact=
  ?aigapmargin= ?aiscanrows= ?aicrosspenalty= ?aithreatlead= ?aibossbias= ?aiaim= ?aifieldpx=
  ?aifieldsize= ?aifieldfall=`
  (null => the baked `PlayerShip.Default*` consts, so a shipped build is unchanged).
  Console: `eaAiBench()`, `eaAiBench.soak(s)`, `eaAiBench.matrix(...)`, `eaAiBench.world()`,
  `eaAiBench.reset()`. Pair
  with `?aiplayer` and `?difficulty=Very_Hard`.
- **Where it stands (card f4d1721f, Very Hard unless noted).** Spider boss 36 deaths and the
  fight never resolving -> 17 and resolving; Level 3 stalled forever at event 53/60 -> kills the
  BrainBoss; wall heading churn ~1050 deg/s -> 70, reversals 6.5/s -> 1.3. It does NOT clear a
  story level on Very Hard (L1 game over at event 19/64, L2 at 45/104, L3 at 19/60) -- it dies to
  sustained bullet fire, and the sum-of-repulsions model is the wrong shape for bullet hell;
  "pick the safest reachable spot" is the next move. Level 1 on Medium is a VICTORY with 1 death.
  **Single runs of a stochastic fight vary a lot -- differences
  under ~30% are noise, which misled this card more than once.**

#### The challenge-level completion matrix (card 9391f95a)

**Measured: Very Hard, 3 runs each, 1800 sim-second cap, no `?invuln`, via `eaAiBench.matrix()`.
Six of nine PASS. All three failures are SURVIVAL failures -- there is no world-model,
targeting or level-progression defect anywhere in the nine.**

Measured just before card c10e3e7f's per-tier skill table landed, and **still current because
the Very_Hard row of `AiSkillByDifficulty[]` IS the old `Default*` consts** -- these numbers
describe the same bot. They do NOT carry to any other tier: below Very Hard the bot is
deliberately worse AND the enemies are weaker, so a lower tier has to be re-measured, not
inferred.

| level | r1 | r2 | r3 | |
|---|---|---|---|---|
| Tutorial | VICTORY 206s / 0 deaths | VICTORY 265s / 0 | VICTORY 361s / 1 | **pass** |
| Braineroids | VICTORY 239s / 0 | VICTORY 247s / 0 | VICTORY 305s / 0 | **pass** |
| SpaceDodge | VICTORY 365s / 15 | VICTORY 183s / 4 | VICTORY 865s / 43 | **pass** |
| OwnLevel | VICTORY 533s / 20 | VICTORY 73s / 1 | VICTORY 202s / 6 | **pass** |
| CrazyGame | VICTORY 482s / 20 | VICTORY 162s / 3 | VICTORY 94s / 2 | **pass** |
| Paratrooper | VICTORY 540s / 13 | VICTORY 557s / 13 | VICTORY 1210s / 31 | **pass** |
| ClassicAliens | TIMEOUT 38/47 / 10 | TIMEOUT 32/47 / 20 | TIMEOUT 42/47 / 19 | **fail** |
| InsaneBossI | GAME OVER 22/50 / 6 | GAME OVER 32/50 / 8 | GAME OVER 6/50 / 6 | **fail** |
| TeamChallenge | TIMEOUT 14/52 / 91 | TIMEOUT 14/52 / 89 | TIMEOUT 14/52 / 87 | **fail** |

**TeamChallenge's row still stands after card e6927ef8, and the reason is worth knowing.** It was
run with `?aiteam`, which seated the partner as `ControlDevice.Generic` -- a device
`PlayerShip.Update` has no case for -- but the switch is on `EffectiveController()`, and the sweep
always passes `?aiplayer`, which returns `AI` for every non-puppet. So BOTH ships were bot-driven
then, and both are bot-driven now that the seat resolves to `ControlDevice.AI` directly: the two
seatings are bench-equivalent and the numbers carry over. (Its 91/89/87 deaths and `prog` pinned at
14/52 across all three runs are the AI failing to survive a tethered pair, not an inert partner.)

**The `?invuln` control is what makes that diagnosis, and it is the cheapest one available here:
re-run a failing level with `?invuln` and the AI wins ALL THREE** -- ClassicAliens 341s,
TeamChallenge 402s, InsaneBossI 660s (which kills the BrainBoss), every one at 0 deaths. So the
bot can already target, dodge-enough-to-shoot and drive every level's script to its end; what it
cannot do is take less damage. That is the parent card's still-open "dies to sustained bullet
fire", now shown to be the ONLY thing between this AI and every challenge. Use this control
before hunting a blind spot in any future stalled-level report.

- **A VICTORY on most of these levels is worth less than it looks, because deaths are FREE.**
  `GameScene.Initialize` sets `score.Lives = -1` and `LoseLife`'s decrement/game-over block is
  gated on `score.Lives >= 0`, so EIGHT of the nine can never reach GAME OVER -- a death just
  reverts to the last checkpoint, forever. `InsaneBossI` is the only challenge that overrides it,
  and only above Medium (5 lives on Hard/Very Hard, 1 on Inzane -- at Easy/Medium even it is
  unlimited); the story levels get 7 via `ApplyDifficultyPolicy`, which no challenge calls.
  (`TutorialLevel.InitialLives = 7` is DEAD -- declared, never read; the Tutorial runs unlimited
  like the rest.) **So read the deaths column, not just the verdict**: SpaceDodge "passes"
  at 43 deaths in one run, and its 4-death run and its 43-death run are the same word.
- **Hence the sweep's third verdict, `TIMEOUT`.** `AiBench.BenchVerdict` only knows
  VICTORY/GAME OVER; on a `Lives = -1` level "never finished" is the ONLY way to fail, so the
  runner supplies TIMEOUT when the cap expires with the level still running. A row with a blank
  or missing verdict would read as a pass.
- **Per-level metric caveats -- some columns are vacuous by construction, do not read them:**
  - **Paratrooper is a TURRET.** `Paratrooper.Update` pins every ship to `(400,500)` each tick
    and `OnComponentAdded` clamps bullet angles upward. Only `DoAIFire` is under test there;
    `coast=0% turn=2deg/s revs=0.09` is the metric describing a ship that cannot move, not a
    perfectly smooth flier.
  - **Braineroids reports `prog=1/1`.** Its event list is one `WaitEvent` and the level loops by
    `RevertToCheckpoint` per wave, so progress carries no information -- the verdict does.
  - **CrazyGame fires ZERO shots in every run and still wins.** Nothing in it is shootable
    (`EvilBullet` is a threat, not a target), so `idle%` is 0 over an empty target set. It also
    posts the worst steering churn in the whole matrix by a wide margin -- `turn` 389-450 deg/s,
    `revs` 7.0-7.8/s, against 60-90 deg/s on a typical level. Dodging 30 homing bullets with the
    sum-of-repulsions model is the shape that churns; it is the natural rig for the next
    bullet-hell attempt.
  - **OwnLevel is the only challenge with WALLS** and the only one scoring `contacts` (13/1/4).
    Its churn (`turn` 254-477 deg/s) runs far above Level 3's wall sections. **That gap IS the
    walls -- an earlier revision of this file concluded the opposite, and card b174b00f measured
    it directly: the hypothesis lost.**
    The old comparison really was confounded, so the critique stands even though the conclusion
    drawn from it does not: the Level-3 baseline is a **`?wallsonly`** run (`PopulateWallsOnly`,
    whose own comment says "with nothing else spawning") while OwnLevel's 254-477 was the WHOLE
    level -- `Walls(game, 2)` alongside a continuous `SkullSpawner(0f, 2f, maze: true)` and a
    Very_Hard+ `StarMineSpawner`. Scroll speed was never the confounder (`?wallsonly` calls the
    same 4.3x `speedup`). But suppressing either half resolves it the other way. `?wallsonly` and
    `?nowalls` both work on OwnLevel now. Measured with `eahl`, Very_Hard, N=6, each run soaked by
    `eval AiBenchRun 60` x3:

    | OwnLevel, one rig | `turn` deg/s |
    |---|---|
    | walls only (`?wallsonly`) | **229** (deterministic -- all 6 runs identical) |
    | spawners only (`?nowalls`) | **~55** (41-67 over 14 runs) |
    | full level | **404** (304-525) |
    | *Level 3 walls only, same rig* | **29** (deterministic) |

    **`?invuln` must be OFF, and that cuts against the habit** -- every other doc line pairs
    `?wallsonly` with it. With `?invuln` on the bot cannot die, the checkpoint rewind never fires,
    and the same boot reads 426 deg/s / 3 contacts / VICTORY instead. The rigs verbatim:
    `?level=OwnLevel&aiplayer&aibench&difficulty=Very_Hard[&wallsonly|&nowalls]` and
    `?level=Level3&aiplayer&aibench&difficulty=Very_Hard&wallsonly`.
    **The two walls-only rigs are not the same SHAPE, which bounds the ratio**: Level 3 loops six
    sections (variations 1/0/3, twice) and is still running at 180 sim-seconds, while OwnLevel has
    a single `Walls(2)` and reaches victory at ~60, freezing its rate there. Both are rates over
    the ticks they ran, so they compare as rates -- but this is grid-against-grid, not
    run-against-run, and a soak length describes the command, not OwnLevel's window.

    So OwnLevel's grid ALONE churns **7.9x Level 3's grid alone**; the enemy stream alone accounts
    for 61 deg/s; and the two together are superadditive (404 against a 290 sum), which is the
    sum-of-repulsions model gaining another set of competing terms. **`?nowalls` is the control
    that makes the walls-only number readable at all** -- without it, "the walls are innocent" and
    "suppressing events broke the rig" are the same quiet number.
    (The Level-3 walls-only baseline re-measures at **29 deg/s on this rig, not the ~70** long
    quoted here from card f4d1721f's browser run. The 7.9x is a within-rig ratio, which is the
    comparison that carries; the absolute discrepancy across rigs is unexplained and worth
    remembering before quoting either figure on its own.)
  - **`tools/sim/aiwallnav`'s columns do NOT predict heading churn, and leaning on them to
    exonerate the walls was the actual error.** Its gap-switch (0.52 vs 0.43/s), lateral sign-flip
    (0.17 vs 0.16/s) and clamp (`clampX/s` 1.12 vs 0.61, `clampUp/s` 1.16 vs 0.77) ratios are all
    real measurements, and all read 1.2-1.8x where the live churn is 7.9x. The one column that
    DOES track it is **`urgency%` -- 25.0% vs 4.5%, 5.6x** -- the share of ticks with a blocked row
    inside reach. The bench states its own limit ("the WALL TERM ONLY -- `turn deg/s` is the whole
    steering sum"); honour it. **Read `urgency%` as the churn proxy** and treat every other column
    as a claim about routing mechanics rather than about the heading.
    **Before attributing churn on a walled level to the walls (or away from them), match the
    rigs** -- suppress the spawners with `?wallsonly`, keep `?nowalls` as the control, and never
    compare a whole-level figure against a walls-only one.
- **`eaAiBench.world()` has three standing FALSE POSITIVES -- do not "fix" them into
  `Oracle.GetBaddies`.** Its `LooksLikeEnemy` is a deliberately name-shaped heuristic, so it
  flags the SCENE class itself (`ClassicAliens`, `InsaneBossI` -- they contain "Alien"/"Boss"),
  the player's own `Bullet`, and `BrainAura` (the BrainBoss's cosmetic aura). All three are
  correctly outside the AI's world model.
- **TeamChallenge needed `?aiteam` to be BENCHED at all; card e6927ef8 fixed the underlying
  GAMEPLAY bug instead, so the flag is gone.** `TeamChallenge.Initialize` used to seat the second
  slot as `ControlDevice.PadOne` unconditionally, and `GameScene.Update` raises `pauseRequested`
  every tick a seated pad device reads `!InputHandler.PadConnected(i)`. With no gamepad attached
  the world was frozen in the pause menu permanently: measured `ticks=0 noship=1 prog=2/52` over
  37 sim-seconds, versus `ticks=1682 shots=1029 prog=6/52` with the old flag (which swapped in
  `ControlDevice.Generic` -- no connected-check, so the force-pause never armed). See the
  partner-seat bullet under "Input" for the fix; for benching, `?level=TeamChallenge&aiplayer` is
  now enough. **The matrix numbers carry over unchanged**: `PlayerShip.Update` switches on
  `EffectiveController()`, so under `?aiplayer` the `Generic`-seated ship was already flying the AI
  branch -- the old and new seatings are bench-equivalent. `?teampartner=pad` restores the pre-card
  seating verbatim if the force-pause itself is what you want to reach.
- **Sweep it with `eaAiBench.matrix(levels, simSeconds, runs, difficulty)`** (`index.html`;
  `.results()` `.status()` `.stop()`). ONE FRESH PAGE LOAD PER RUN, plan carried in
  `sessionStorage` and resumed at boot -- not an in-process relaunch, because a level
  `LockDifficulty()`s, seeds `score.Lives` and consumes the shared RNG, so runs sharing a page
  would measure each other's leftovers, and one wedged run would take the sweep down instead of
  one row. It rearms the counters once the level is actually up: `AiBench.Update` runs on every
  tick including the ~20s of real rAF frames spent booting WASM and warming textures, which
  otherwise overshoots the cap and dilutes `coast%`/`idle%` with level-less ticks.
  **Never `await` it** -- one run outlives any single devtools/CDP eval, and the sweep survives
  navigations a pending promise could not. `AiBench.Row()` is the machine-readable
  `key=value` line it consumes, kept separate from `Report()` so reformatting the human report
  cannot silently break a sweep; the verdict travels space-free (`GAME_OVER`) because it is the
  one value containing a space and it truncated to "GAME" in the first sweep.
- Wall-clock: a soak runs ~60x realtime on a light level and ~3x on a dense one, so the full
  9x3 sweep is ~40 minutes, dominated by the levels that run the cap out.

### Webcam challenge "I Made This!" (`Levels.WebcamAliens`)

The player's segmented camera image is the ship. **JS owns everything camera** (`wwwroot/webcam.js`:
setup dialog, getUserMedia, the mirrored person overlay canvas outside `#app`); **C# owns everything
gameplay** (`Compat/WebcamInterop.cs` + `WebcamLevel.cs`/`WebcamUfo.cs`/`WebcamPlasma.cs`/
`WebcamMothership.cs`/`WebcamMine.cs`/`WebcamZap.cs`). Collision surface = a 40×30 person-mask
occupancy grid in design space pushed ~30Hz from JS; the scene hit-tests against it (`HitCircle`)
and aims at its `Centroid`. Headless QA: fake a player via
`DotNet.invokeMethod('EvilAliensWeb','webcamMask', b64Grid, coverage)`.

- **GOTCHA — MediaPipe MUST stay in the worker (`webcam-worker.js`):** its Emscripten loader
  assigns the global `Module`, which Blazor's Mono runtime also uses — importing tasks-vision on
  the main thread kills the .NET runtime ("_malloc is not a function", reproduced). The ~10 MB
  runtime+model under `wwwroot/lib/mediapipe/` lazy-loads when the level starts; failure → a
  fixed-oval "simple mode". Vendored tasks-vision is 0.10.14 (0.10.35 exists; mechanical bump is a
  follow-up).
- **The mask is REFINED in the worker — don't strip it:** adaptive temporal EMA (delta-weighted) +
  a band-limited joint bilateral filter on edge pixels guided by the camera RGB (the Meet-style
  pipeline); both the visual alpha AND the occupancy grid come from the refined confidence. Knobs
  are consts at the top of `webcam-worker.js`. The overlay canvas backing store is device-pixel
  sized (capped 1280 wide).
- **A `Levels` enum member must only ever be APPENDED** (XmlSerializer keys on enum names);
  `Achievements.checkData` backfills missing level keys instead of wiping progress.
- **Per-difficulty tuning:** `WebcamLevel.Tunings[]` (Easy..Inzane) holds hearts, kills-to-win, max
  saucers, saucer/plasma speed ×, and ABSOLUTE cadence ms (`SpawnIntervalMs`, `ArmDelayMs` = the
  fire-rate lever since each saucer fires once per arm cycle, `ChargeTimeMs`), ±15% jittered
  (`CadenceJitter`) — no `DifficultyModifier` divisor, no within-run ramp. Plus `MaxMines` /
  `MineSpawnMs` / `MineLifeMs` / `MothershipMs`. Music via `SoundManager.ClassicForDifficulty()`.
  Live-tune with **`?wctune`** (the `eaWcTune` stepper panel — overrides are ABSOLUTE final values;
  "Reset to tier defaults" re-seeds from the resolved row; the orange readout prints the
  bake-ready `Tunings[]` row) or the URL flags `?wcdiff= ?wchearts= ?wckills= ?wcsaucers=
  ?wcsaucerspeed= ?wcplasmaspeed= ?wcspawn= ?wcarm= ?wccharge= ?wcminemax= ?wcminespawn=
  ?wcminelife= ?wcmothership=`.
- **Saucer behaviour:** plant-and-shoot (full stop → blink-charge → fire → accelerate away) and
  PERSISTENT (retreat ~50px past an edge, `ReturnToField()` off-screen after `ReturnDelayMs` 900 /
  `?wcreturndelay=`; only a player swat despawns). On-screen guards: wander containment is
  authoritative (avoidance steer first, edge-bounce last, `OffScreen(0)` watchdog); arming only
  starts ≥ `ArmInset` 60px inside every edge (a shot can never originate off-screen); fly-around
  avoidance orbits the player's mask via `WebcamInterop.AvoidanceVector` (`?wcavoid=`). Plasma aims
  at the mask centroid, locked at fire; on reaching the player it pops into a `WebcamZap` electric
  zap (not an explosion). Player-hit plays `hit_boss`. Hearts are top-centre.
- **Hazards (both `Collides=false`, mask-hit-tested, spawn only while `PlayerVisible`):**
  `WebcamMothership` slides in, winds up a `LazerGenerator` swarm, fires a beam that BISECTS the
  screen (a `Quad` drawn directly; fixed tier-independent sweep), holds, slides out — cannot be
  harmed. Orientations: VerticalDown (parks over centre/left-third/right-third, telegraphing the
  beam) or HorizontalFromLeft/Right (~33% down); `PickBisectOrientation` ~60/40. The mothershipB
  art is re-centred via `SpriteArtOffset` so the beam lines up with the visual hull. **The whole
  choreography is a pure function of elapsed ms (`WebcamMothership.PoseAt`)** — verify movement as
  DATA via `tools/sim/webcam_mothership_sim.py`; `?wcmothershipfreeze=<ms>` parks it for an
  appearance screenshot; `?wcmothershipdir=` forces orientation. The beam sweeps mines (mercy pop,
  `DestroyByLaser`) and saucers (full kill credit via the shared `KillSaucer`).
  `WebcamMine` reuses the DeathStar sprite, wanders exactly like `WebcamUfo` (flows AROUND the
  player — no homing); touching it costs a life + a beefy blue burst; lives `MineLifeMs` then
  flies off and despawns.
- **Bad-collision LEEWAY:** hazards that HURT (plasma/beam/mine) only land after the mask steadily
  overlaps for `HitLeewayMs` ~100 (`?wchitleeway=`) — per-hazard `ContactMs` accumulators in the
  three bad tests, reset the instant contact breaks (framerate-independent). **Killing saucers is
  deliberately NOT leewayed** (the player wants those hits). Verify the timing as data, not
  screenshots.
- **Level-select screenshots** now cover ALL carousel challenges (not just Level1/2/3). Capture on
  level EXIT (`GameScene.checkScreenShot` → `takeScreenShot` → `ScreenshotSaver.SaveScreenShot`
  writes `<Level>.dat`); `SubMenuLevelChoice` shows it, else the bundled art. **WebcamAliens is
  opt-in** (`Settings.WebcamScreenshot`, default false — the shot contains the camera image; an
  Options toggle). The webcam shot composites the JS overlay back in: `GameScene` fires
  `OnScreenshotResolved` at the snapshot instant → `ScreenshotSaver.CaptureWebcamOverlay` pulls the
  overlay RGBA from JS (`eaWebcam.overlayPixels`) into a `pendingOverlay` drawn over the frame
  (NonPremultiplied). The sparse webcam level never hits the >30-entity trigger, so `WebcamLevel`
  calls `GameScene.ForceSnapshot()` on the first kill.

### Online co-op (net layer, Stage 11 -- design `plans/stage11-online-coop.md`)

Distributed-authority state replication (NOT lockstep): each peer owns its own ship
completely (input read untouched, zero added latency); the wire carries ship STATE, never
inputs; the other peer's ship is an interpolated puppet. Code lives in `Compat/Net/`.
Shipped so far: card 11.1 (net skeleton + ship mirroring over a BroadcastChannel loopback),
card 11.2 (host world authority: client enemy puppets, world snapshots, generous claims,
score sync), card 11.3 (level-script beat replication, host-broadcast reset/victory,
replicated pause, TeamChallenge soft tether) and card 11.4 (real WebRTC transport, room-code
signaling on the shared VPS, menu-driven Host/Join lobby, build-hash handshake, match-end
semantics). Card 11.5 adds the hardening pass: powerup pickups replicate to the collector's HUD
slot, ONE match-end path, a drop-verdict grace window with a waiting-for-peer banner,
and the WebcamAliens net-lobby refusal explains itself. Remaining: the TURN go/no-go and
interpolation feel, both gated on real-network playtests.

- **Flags:** `?net=host` / `?net=join` opt a session in (in `Active`); `?room=<name>` picks
  the loopback room (BroadcastChannel `eanet-<room>`, default `dev` -- parallel test pairs
  must use distinct rooms); `?netlog` = verbose per-event logging; `?aiplayer` forces the
  LOCAL ship onto the existing AI branch (`PlayerShip.EffectiveController`) for unattended
  soak tests; `?aifriends=<0-3>` (pair with a `?level=` boot) seeds `Settings.Friends` so the
  host's Mechanical-Friends AI ships auto-join without the cheats menu -- the two-tab seam for
  AI-friend replication (note the budget is `Friends+1` TOTAL ships incl. the remote, so with a
  peer connected you need `aifriends>=2` to spawn any AI friend); `?netscript` (pair with `?level=Level1`) replaces the level's event list with
  a compressed ~60s script firing every replicated beat type (message, warning, background
  ops, checkpoints, music switch, victory) -- the purpose-built two-tab verification for
  script replication (`GameScene.PopulateNetScriptTest`). Card 11.4 adds `?rtc` (a
  `?net=` boot uses the REAL WebRtcTransport: host prints its room code to the console,
  join passes it via `?code=ABCDE`) and `?signal=<url>` (override the signaling server;
  a local rig runs `uvicorn main:app --port 8091` in `server/signal` and boots with
  `?signal=ws://localhost:8091/ws`). Card 40334a8f adds `?netlag=<ms>` / `?netloss=<0-100>`
  (impair INBOUND traffic -- see the impairment bullet below) and `?netsim` (show the live
  impairment panel; the knobs work without it). `?netfakehash=<s>` (card
  4717d3cf) overrides THIS tab's build-hash fingerprint so two dev tabs disagree, driving the
  real `peerHash`-mismatch -> reject flow (`RejectBuild` -> "update required") on the
  BroadcastChannel rig -- otherwise both tabs read `'dev'` and never mismatch (the two-tab
  verification for the reject handshake + its teardown grace). **No `?net` flag = the net layer is never constructed
  -- a plain boot is byte-identical single-player, and single-player NEVER contacts any
  server. Hard invariants; keep them.**
- **Transport is an interface** (`Compat/Net/INetTransport`): a STREAM lane
  (unreliable-class -- consumers must tolerate drops/reorder) + a RELIABLE lane (ordered,
  guaranteed), `OnData`/`OnPeerBye` events. Impl #1 `BroadcastChannelTransport` ->
  `NetInterop` ([JSInvokable] shim, the WebcamInterop pattern) -> `eaNet` in `index.html`
  (channel only constructed when opened; still the default dev rig). Impl #2 (card 11.4)
  `WebRtcTransport` -> `WebRtcInterop` -> `eaRtc` in `wwwroot/webrtc.js`: JS owns the
  RTCPeerConnection + signaling WS + the join-code overlay; two DataChannels map to the
  lanes ("s" unordered `maxRetransmits:0`, "r" reliable). A 1-byte `0x00` reliable frame
  is the JS-level pagehide "bye" (0x00 is reserved -- C# msg types start at 0x01). STUN =
  free Google servers, NO TURN in v1 (~10-15% of NAT pairs get a clean "could not
  connect"; 11.5 owns the TURN decision). Nothing above the interface may assume loopback
  reliability.
- **Artificial impairment (`Compat/Net/NetImpairment`, card 40334a8f) is what makes the
  drop-tolerance paths testable at all.** BroadcastChannel never loses or reorders a packet, so
  until this landed the interpolation underrun, the snapshot unknown-id self-heal, the claim
  ledgers and the peer timeout had NEVER executed -- every one of `sgap`/`extrap`/`pops`/
  `pupPops` was structurally pinned at 0. It DECORATES `INetTransport` (so it impairs the
  WebRTC transport unchanged) and is always in the chain inside a net session, forwarding
  inline at 0/0. **RX-ONLY** -- impairing our own inbound == the peer's outbound being bad, so
  an asymmetric link is just two tabs with different settings, and tx is untouched.
  - **Per lane: the STREAM lane takes delay + loss + jitter; the RELIABLE lane takes delay
    ONLY.** Dropping or reordering the reliable lane would break the contract everything above
    the interface assumes and could only manufacture fake bugs. Its release times are clamped
    monotone, so jitter can never reorder it.
  - The held stream packets are a LIST scanned for everything due, not a head-first FIFO: with
    jitter a late-stamped packet must not block a later one that came due earlier, or jitter
    silently degrades back into pure delay. Loss with no lag releases inline (queuing it would
    add a hidden tick of latency and make loss impossible to isolate).
  - `Pump(now)` runs at the top of `NetSession.Update` BEFORE `DrainRx`, on the same
    `TickCount64` clock as the rest of the cadence -- so **delay granularity is one tick
    (~16ms)**; a lag below that is indistinguishable from 0.
  - Private `Random`, never the shared game RNG (the `Quad`/`ShipConnector` rule) -- a dev knob
    must not be able to desync a co-op session.
  - Flags `?netlag=<ms>` (0-500) / `?netloss=<0-100>`; **jitter is panel-only** (no URL flag) --
    it is the knob that actually makes the stream lane REORDER. Live panel `eaNetSim` (built
    outside `#app`), **opt-in via `?netsim`** on top of the `?net` boot -- it sits over a co-op
    session you are usually trying to watch, and most `?net=` boots never impair anything. A bare
    `?net=` boot still defines the console entry points `eaNetSim(lag, loss, jitter)` /
    `eaNetSim.test(...)` / `eaNetSim.show()`+`.hide()` (summon the panel with no reload), and
    `?netlag=`/`?netloss=` are parsed C#-side in `DebugFlags`, so they impair panel or no panel.
  - **`?netloss=100` starves the ship stream so the stall banner raises after ~1.2s and the
    peer-drop verdict lands ~8s in, while the handshake
    stays alive on the reliable lane -- that is a simulated silent disconnect, not a bug.**
  - The `[net]` line gains `impLag/impLoss/impJit/impDrop/impHeld` ONLY while impairment is on,
    so a deliberately degraded log can never be mistaken for a genuinely broken one.
  - **Verify with `eaNetSim.test(lag, loss, jitter, n)`** -- pushes n synthetic packets per lane
    through the real wrapper on a VIRTUAL clock and prints measured delay/drop/per-lane reorder.
    Written in place of a `tools/sim/` python mirror on purpose: the policy is small enough that
    a mirror would drift from the C# and prove nothing. Reliable lane must read `drop=0
    reorder=0` in every configuration, including `loss=100`.
- **Signaling (card 11.4): room codes on the shared Hetzner VPS** (root CLAUDE.md has the
  box details). `server/signal/` in THIS repo = a FastAPI/uvicorn dumb relay (mints 5-char
  codes, no 0/O/1/I; relays SDP/ICE between exactly 2 peers; room TTL 10 min; `python
  test_signal.py` covers the protocol). Deployed at `/opt/rotea` (unit `rotea`, port
  8091 localhost) behind nginx `location /rotea/ws` in the `notzelda.haraldmaassen.com`
  443 vhost (existing cert; health check: `https://notzelda.haraldmaassen.com/rotea/health`).
  Deploy = scp `server/signal/*.py` + `requirements.txt` to `/opt/rotea/server`,
  `systemctl restart rotea` (full first-install steps: `server/signal/README.md`). The
  signaling WS closes once the DataChannels connect -- gameplay is pure P2P.
- **Menu lobby (card 11.4):** main menu "Online Co-op" -> Host Game (shows the room code
  + "waiting") / Join Game (HTML code-entry overlay outside `#app` -- `eaRtc.promptCode`).
  `Compat/Net/NetLobby` owns the pre-session flow (JS phase queue drained by
  `MenuScene.NetUpdate` on the game tick; `NetStatusMenu` = the re-textable
  ConfirmationMenu panel). On connect the HOST picks level+difficulty through the NORMAL
  select screens (netPickMenu -> the shared selectors; their OnExit reroutes in net mode;
  WebcamAliens selection is refused, and the carousel swaps its briefing for the reason) and `EvLaunch` mirrors the launch on the client
  (`MenuScene.NetLaunchMirror` -- same fade/warm path, difficulty locked, starter
  Keyboard). Turbo is forced to 100 while a session is Active (`Game1.Update`).
- **v4 handshake + match-end (card 11.4):** hello/welcome carry an 8-byte build hash
  (FNV-1a of `window.eaBuildHash`; deploy.yml stamps a sha256 of `blazor.boot.json` at
  publish, dev builds read 'dev') + a flags byte. Hash mismatch -> `MsgReject` -> "Update
  required" notice both sides (a stale-cached client can never desync a session); menu
  sessions also reject if EITHER side has `DebugFlags.Active` (dev `?net=` sessions are
  anything-goes). **Rejection is graceful (card 4717d3cf, `RejectGraceMs` 1s):**
  `SendRejectOnce` queues the reliable `MsgReject` but defers `NetSession.Stop()` by a tick
  budget instead of closing instantly -- an immediate `Stop()->transport.Close()->pc.close()`
  is ABORTIVE on WebRTC and would discard the still-buffered reject frame, leaving the peer to
  see only a channel close ("other player disconnected") instead of the real reason. Holding
  the session open for the grace keeps SCTP alive so the reject (and our hello, which drives
  the peer's own symmetric detection) actually egress; the peer's inbound reject during the
  grace ends our side early. The detection itself is symmetric (each side derives the notice
  from the peer's hello), so the frame is belt-and-braces; the grace is what makes it land.
  Match-end: any player leaving a MENU session (quit, tab close, drop,
  victory/game-over wind-down) ends it for both -- scene-down edge or `PeerLost` sends
  `EvLeave`/notice, `NetSession.Stop()` tears down (registries disabled, state reset,
  restartable), `GameScene.NetApplyPeerLeft` force-exits a running level (except in
  Victory/GameOver, which finish locally), and the menus surface `TakeMenuNotice()`.
  `EvReady` (client scene-up edge -> host `ReplayLive`) covers the lobby launch race
  where one peer out-warms the other; world messages are gated client-side while no
  GameScene is up. URL `?net=` sessions keep the old semantics (session survives peer
  loss, reconnect works).
- **Protocol (`Compat/Net/NetProtocol`, little-endian binary, 1-byte type, v9):** the 3
  layers -- `MsgShipState` (~30 Hz real-time cadence: pos, vel px/ms, last-fire aim,
  alive|firing flags, shotsPerSec, bulletLife -- 31 B), `MsgWorldSnapshot` (see the
  World-snapshots bullet below), `MsgEvent` envelope with a monotone ushort seq
  (EvSpawn full base state + spawn extras / EvDeath netId+killer+pos+per-slot award / EvBlast
  pos+level / EvClaim netId+killerSlot / EvScoreSync lives+scores) + `MsgHello`/
  `MsgWelcome` handshake (protocol version byte; both sides Hello until paired, opposite
  role replies Welcome; **v5** adds the host-granted primary slot byte -- card 4d904410;
  **v6** appends the peer-identity token to the handshake -- card 0b8a300b;
  **v7** widens EvDeath's trailing `points:u16` into an `f32 x MaxSlots` AWARD array --
  card b0ab09ec, see the Score/lives bullet;
  **v8** appends a `blockedSlots` mask to the handshake (HelloBytes 21 -> 22) so the host can
  grant a seat that is free on BOTH rosters -- card c0229c57, see the roster-slots bullet;
  **v9** adds `MsgHudState` (0x12) -- the owner-authoritative per-slot combo + powerup state,
  card 1a3ad45a, see the per-slot HUD state bullet;
  **v10** adds `EvCosmeticSwarm` -- a decorative swarm replicates as one on/off beat and its
  entities stop being replicated individually, card 9a3175d0, see the decorative-swarm bullet.
  No existing layout changed, but a v9 peer would ignore the beat AND still expect the
  per-entity spawns, i.e. see empty scenery -- a real incompatibility, hence the version move).
  Card 11.3 bumps the protocol to v3 and adds the shared-state
  events: EvMessage/EvUnlock/EvBackground/EvMusic/EvCheckpoint (script beats), EvReset
  (host LoseLife branch), EvVictory, EvPause (either peer), EvTetherBreak (either peer).
  Peer loss = JS `pagehide` bye OR a stream timeout (PeerTimeoutMs 3s + PeerGraceMs 5s of
  continuous silence; past PeerStallMs 1.2s a non-freezing "waiting for other player"
  banner goes up, so a hiccup or a backgrounded tab recovers instead of ending the run);
  the ship stream doubles as the
  heartbeat (sent even with no live ship, alive=false). **While either side holds a pause
  the timeout stretches to a 120s backstop** -- a paused tab is usually backgrounded AND
  the pause muffle ducks its audio, which revokes Chrome's audio exemption from intensive
  timer throttling, so its ticks arrive in ~1/min bursts; without the wide window the link
  flaps and the designed peer-lost failsafe silently unfreezes the world. A held local
  pause is re-announced on reconnect (`PeerConnected`).
- **NetIds (`Compat/Net/NetIdRegistry`):** host-side, on the ComponentBin seam
  (`Game.Components` ComponentAdded/Removed -- the same events Oracle uses, fired when a
  component actually enters/leaves the world). Replicable set = the `NetTypeRegistry`
  descriptor table (Oracle.GetBaddies' enemy types minus Explosion -- cosmetics never
  cross the wire -- plus Powerup). Emits spawn/death events; replays the live set to a
  late-joining peer; tracks per-entity OBSERVED velocity (position deltas between an
  entity's snapshot turns -- Speed/Direction lies for enemies that move Position directly).
  Minus the per-INSTANCE opt-outs -- see the decorative-swarm bullet below.
- **Decorative swarms replicate as one "effect on/off" beat, NOT per entity (card 9a3175d0).**
  Purely cosmetic entities were taking NetIds, `EvSpawn`/`EvDeath` pairs and a share of the
  16-per-60ms snapshot round robin for nothing: the `?flyspiders` rig measured `liveIds` 17-19,
  i.e. essentially the WHOLE budget spent on scenery, which directly stretches `snapTurn` --
  the mean blind dead-reckoning window of every enemy that DOES matter. Two halves:
  - **`AlienDrawableGameComponent.NetCosmeticOnly`** -- an INSTANCE-level opt-out (the
    `NetSpinPerMs` idiom), because the same `FlyingSpider` type is a real killable enemy in its
    foreground form and fog in its background one. Overridden by `FlyingSpider`
    (`isbackground`) and `Asteroid` (`SetBackground()`'s `DrawOrder == 1` marker). Read at the
    ComponentAdded seam, so it must be FINAL before `ComponentBin.Add` -- the configure-then-Add
    rule `tools/audit_add_order.py` already lints.
    **Two conditions, both required: the instance can never become collidable, and nothing
    gameplay-visible reads it.** Both members are in `Oracle.GetBaddies` -- the AI's whole world
    model -- and are invisible to it only because of `Collides`: `PlayerShip.IsAiShootable` has
    an explicit `baddy is FlyingSpider && baddy.Collides` and excludes `Asteroid` outright, and
    the threat scan gates at its CALL SITE (`PlayerShip.cs` `if (!baddy.Collides ||
    !IsAiThreat(baddy))`) rather than inside `IsAiThreat` -- so a future caller of `IsAiThreat`
    that forgets the gate would start dodging fog.
  - **`NetTypeRegistry.IsReplicableInstance`** is the predicate the LIVE world asks;
    `IsReplicable` is just the type table. Every decision site uses it -- and
    **`NetSession.SuppressWorldSpawn` is the load-bearing one**: with the type-level test there,
    the bin would divert the CLIENT'S OWN cosmetic spawns into the recycle pool and the joiner
    would see no scenery at all, with no counter moving anywhere.
  - **The SPAWNER replicates instead.** `EvCosmeticSwarm` (protocol **v10**,
    `[kind:1][on:1][rate:f32]`, `NetCosmeticKind` APPEND-ONLY) is announced by
    `FlyingSpiderEvent` / `AsteroidSpawner` from their first `Update` and from `OnFinished`
    (Level 2 ends its fog swarm by `LinkWith`, so lifetime alone would never fire). The client
    builds its own spawner and ticks it in `GameScene.UpdateNormal`, **in the very branch that
    skips `eventList.Update`** -- which is what gets pause / victory / resetting for free
    (`UpdateNormal` only runs in `GameState.Normal`, and a pause `Push` disables the scene).
    The asteroid copy uses the spawner's own `SetBackGroundOnly()` + `startWithBig:false`, so it
    never produces the collidable ones -- those still arrive as puppets.
  - **Latched on `GameScene`, replayed from the `EvReady` catch-up seam** next to
    `Background.NetReplayCatchUp`. Latched at the ANNOUNCE, not off the send path:
    `NetSession.OnCosmeticSwarm` early-returns with no peer connected, which for a LISTED
    single-player game is exactly the window a JIP peer must be caught up from (the same
    reasoning as Background's `netLast*`). Cleared at the checkpoint revert on BOTH peers -- the
    host's eventList drops active events without terminating them, so no "off" is ever sent --
    and in `Initialize`/`Terminate` (re-added singletons).
  - **The latch is REFCOUNTED per kind** and only emits on the 0<->1 edge. The beat is per kind
    but each spawner tracks its own announce, so two overlapping spawners of one kind (nothing
    ships that, but a level script is one line from it) would otherwise have the first one's
    `Terminate` send an "off" while the second still spawns -- the joiner's scenery gone for the
    rest of the level, silently, with the host's own screen full.
  - **A rate off the wire is clamped** (`NetCosmeticMaxRate` 12/s) and non-finite/negative
    refused: it drives `GenericSpawner`'s `while (num >= 1f) DoEvent()` loop, and a publicly
    listed game has a stranger on the far end. The ceiling bounds the AUTHORED rate, which is not
    the rate in flight -- `GenericSpawner` multiplies by `DifficultyModifier` and
    `MultiPlayerDifficultyModifier` per tick -- so it sits near the shipped rates (5.5/s fog,
    5/s belt), not at a round big number.
  - **KNOWN LIMIT (asteroids only), accepted:** `AsteroidSpawner` sweeps its entry HEADING on its
    own timers from `Reset`, so a peer's grey rocks fly parallel to the replicated real ones only
    while the two cycles stay in phase. A live pairing starts them within an RTT; a
    JOIN-IN-PROGRESS peer starts its cycle when the catch-up beat lands and is out of phase for
    the rest of that belt. Keeping them aligned would mean streaming the angle -- the per-entity
    cost this card exists to remove -- so it is a decoration-vs-decoration mismatch taken on
    purpose.
  - **Verify with `eaNetCosmetic()`** (`Compat/Net/NetCosmeticTest.cs`) -- codec, the instance
    predicate (every check beside its positive control, since a predicate answering "not
    replicated" for everything would pass a fog-spiders-only test and silently stop replicating
    the whole game), and the client apply path (skipped with a printed SKIP outside a level).
    **A screenshot diff cannot check this feature at all** -- the two peers' scenery is SUPPOSED
    to be in different places. `eaNetBg()`'s state line gains `cosmetic=<kind@rate,...>`, which
    both peers hold (host = latch, client = live spawners) and which IS diffable; `eaNetBgTest()`
    gained the matching round-trip leg, and `?netscript` fires both kinds so the two-window run
    covers them.
- **World authority (card 11.2): the host runs the real sim, a join peer mirrors it.**
  Client sim-split at two choke points: `GameScene.UpdateNormal` skips `eventList.Update`
  (spawners/the level script only act in GameEvent.Update) and `ComponentBin.Add` swallows
  any replicable-type add not made by the puppet layer (KilledBy side effects: asteroid
  splits, bonus powerup drops, stray spawns) into the recycle pool -- the host's
  authoritative copy replicates in instead. AI-friend auto-join is HOST-ONLY in a net
  session (the host runs the AI friends and streams them; the client shows them as
  `ControlDevice.RemoteFriend` puppets -- see the AI-friend bullet below). Because the script never runs on a client,
  `GameScene.spawnPlayerNormally` reads as true on a join peer -- a scripted no-ship phase
  (Level1's intro hands the ship spawn to its `demo_OnFinished` beat) would otherwise
  leave the client shipless forever; the client's ship always uses the generic
  startup/respawn path and the intro choreography stays host-only. Initial
  background/music are local; mid-level script beats (messages, music switches,
  boss-phase choreography) do NOT replicate yet -- that is the next card.
- **Client enemies = NetPuppets (`Compat/Net/NetPuppets`):** real game objects built by
  their own `New*+Setup` factories (the harness-proven path) on `EvSpawn`, then FROZEN --
  `Enabled=false` for life (gameplay Update/AI never runs; `ComponentBin.Pop` is patched to
  not thaw them on unpause) while Draw renders normally and a `CollisionHandler.IsActive`
  seam keeps them hit-testable by the local player's bullets. One `NetPuppetDriver`
  (UpdateOrder -1000, disabled by pause like everything else -- which also freezes puppet
  collisions) dead-reckons `Position += vel*dt`, advances `curframe` at the type's own fps,
  blends snapshot corrections over ~150ms (error > 100px snaps + counts a `pupPops`
  metric), lerps scale, ticks each puppet's `timers` (hit-blink decay), re-applies hp.
  **The driver ticks on REAL time (`Environment.TickCount64` delta, clamped 200ms), never the
  turbo/slow-mo/hit-stop-scaled `gameTime` Game1 folds into components** -- the host mirrors
  its world at its own real pace and stamps every snapshot's observed velocity on real time,
  so a client time-scale window (the wipe's 180ms death hit-stop, a 1-up slow-motion) must not
  stall the dead-reckoning or the correction blend, or the puppets fall behind the real-time
  snapshots and repeatedly snap (this was the first-wipe `pupPops` burst; same rule the
  remote-ship puppet follows). Characterised in `tools/sim/net_puppet_drive_sim.py`.
  - **`pupPops` is meaningless without `snapTurn`, which the `[net]` line now prints** (card
    48ab9b2f). The snapshot cursor round-robins 16 entries per 60ms packet, so an entity is
    corrected only every `live/16*60ms` on average (`NetSession.SnapshotTurnMs`) and dead-reckons
    blind in between. A big world stretches that -- 1.2s at 320 entities -- and how much a pop
    rate SHOULD be expected depends entirely on it. It is the MEAN, deliberately: the cursor
    wraps continuously instead of restarting per cycle, so rounding up to whole packets would
    report 120ms for a 17-entity world whose real blind window is 64ms. **The two peers derive
    it from different counts** -- the host from its authoritative `NetIdRegistry`, the joiner
    from its own puppet count, which lags during spawn bursts and JIP catch-up; the host's line
    is the one to trust.
  - **The 200ms dt clamp is what makes a starved client pop, and it is worth knowing about
    before blaming the link.** The clamp is deliberate (a pause Pop or tab refocus must advance
    the world by at most one over-long frame, never a fling), but a client ticking slower than
    5Hz silently loses `gap - 200ms` of real motion EVERY tick; the error integrates and the
    next snapshot snaps. `--population` measures it: at N=128 a client at 60/40/30/10/5 **and
    3** Hz logs **0 pops/s** -- 3Hz is already losing 133ms per tick to the clamp and still pops
    nothing -- while 1Hz logs **128/s**. So the cliff is between 3Hz and 1Hz, i.e. an OCCLUDED
    or hidden window (rAF paused, timers ~1Hz -- JIP trap 1). A merely SLOW client is fine.
  - **A long `snapTurn` hurts PERIODIC motion by resonance, not big worlds as such.** Same
    sweep, client healthy: a `?flyspiders` swarm logs 0 pops/s at every N from 16 to 2048 --
    flat zero, not a curve -- **except** at N=512, where `snapTurn` (1920ms) lands near half the
    spiders' 4000ms swivel period, the phase at which a velocity measured by finite difference
    across the interval is most wrong about where the entity goes next. There it jumps to 7.2/s
    on Very_Hard and **92.6/s on Inzane**, whose swivel is 20% bigger. Off that resonance the
    +/-25px swivel is simply too small to miss by 100px however long the turn grows, and the X
    drift is exactly linear so it costs a healthy client nothing. Worth remembering when a new
    replicated type moves on a cycle.
  - **But it was NOT the JIP pass' problem, and "the swarm was dense" explains nothing there.**
    `?flyspiders` spawns 5.5/s yet they die at `Position.X < -100`, so it settles at a MEASURED
    `liveIds` 17-19 -- `snapTurn` ~64-71ms, i.e. the floor. (It does not accumulate: the
    "background spiders have `Collides=false` so they pile up" reasoning is about kills, and
    off-screen death is what actually bounds them.) That is three orders of N away from the
    resonance, in the region where the sweep reads a flat zero.
- **World snapshots (`MsgWorldSnapshot` 0x20, stream lane, host->client, 60ms cadence):**
  round-robin cursor over the live NetId set, <=16 length-prefixed entries/packet (~500B).
  Entry = netId + typeIdx + the generic base block (`NetBaseState`: pos, observed vel
  px/ms, rotation, curframe x64, scale x256, hp) + per-type state extras. A snapshot entry
  for an unknown id self-heals: it REBUILDS the puppet from the snapshot (default spawn
  extras) unless that id was removed locally < 3s ago (a death still settling -- ours OR the
  host's). Which of those (plus a REFUSED rebuild) happened is reported per entry as a
  `SnapUnknownKind` and counted separately -- see the `snapNew`/`snapDead`/`snapBad` bullet
  under "Verify with LOGGED METRICS"; they all return "not applied", so the single total they
  used to share could not be judged.
- **Per-type descriptors (`Compat/Net/NetTypeRegistry` + `Compat/Net/Descriptors/`):**
  the wire typeIdx IS the registry order -- append-only, never reorder. A descriptor owns
  (a) puppet CONSTRUCTION: spawn extras pin every random/caller-chosen look (e.g. UFO's
  random small-sheet pick, Powerup's random type); (b) STATE extras: the fields a frozen
  Draw reads that the base block doesn't carry (sheet swaps, phases, landed stills). Types
  needing neither are explicit base-only descriptors with a justification. Private fields
  are reached via small `internal Net*` accessors at the bottom of the game type itself
  (see UFO.cs). Contract + author rules: NetTypeRegistry.cs header; worked example:
  UfoDescriptor.cs.
- **GENEROUS at-least-once claims -- no arbitration, no rejection path.** Kills: local
  hit-testing runs the REAL per-type death on whichever peer observed it (explosion,
  sound, score, combo paid locally); the client's removal seam sends `EvClaim(netId,
  killerSlot)` for every gameplay death (`IsDead` distinguishes `Die()` from teardown
  purges). Host on a claim: entity alive -> real kill via `KillableAlien.NetKill` with a
  scratch-Bullet killer carrying the claimant's slot (authoritative children spawn there
  and replicate); already dead -> pay the claimant once from a bounded recent-death
  record. Host broadcasts `EvDeath(netId, killerSlot, pos, points)` for every replicable
  removal (killerSlot from the `NetSession.NoteKill` hook in KillableAlien.HitBy);
  client: live puppet + killer -> local NetKill (FX + credit), no killer -> silent
  despawn, already dead -> pay the killer once. Per-(netId, slot) paid ledgers both sides
  = every distinct claimant credited, nobody credited twice. Powerups are the same claim
  shape: the real PlayerShip pickup runs instantly on the collector (a
  `NetSession.NotePowerupTaken` hook attributes it), first claim despawns the entity,
  overlapping collectors inside the RTT window BOTH keep it.
- **Score/lives: the AWARDED AMOUNT is replicated, not the combo (card b0ab09ec).** `EvDeath`
  carries what the host actually credited, per slot (`f32 x MaxSlots`), and that figure is
  authoritative on the client in every branch. Lives stay verbatim off `EvScoreSync`.
  - **Why: every kill is credited on BOTH peers, each with its own combo multiplier.**
    `comboModify = amount * (1 + combo/20)`, and the combo counter is a purely local
    simulation -- the only thing that raises it is a local bullet's first hit
    (`Bullet.CollidesWith` -> `SustainCombo`), and on a client those bullets hit frozen
    puppets interpolated ~100ms behind the host's real entities. So the same kill is worth a
    different number on each screen.
  - **`max(local, host)` adoption made that unbounded, and is GONE.** It kept every positive
    excursion of the error and discarded every negative one, so even a perfectly *unbiased*
    per-kill difference integrated into one-way drift (measured in the 11.5 playtest: a slot
    the host had at 294 read 304 on the joiner, and climbing). The old note here claimed this
    "self-corrects upward" -- it did not; it was a ratchet.
  - **Replicating the COMBO COUNTER instead would not have worked**: combo changes up to
    ~10x/second, so any replicated copy is stale by at least the latency and the credited
    numbers would still differ. The award is the only thing that can be exact.
  - **A client's own kill is credited instantly but PROVISIONALLY** (`NetScoreLedger`): the
    amount is booked until the host's `EvDeath` for that netId replaces it with the
    authoritative figure. `EvScoreSync` then adopts `host + unsettled`. Both ride the ORDERED
    reliable lane, which is what makes that sum exact either way round -- an `EvDeath` seen
    before a sync is inside that sync's number and off the books, one seen after is outside it
    and still on them. Carrying `unsettled` is also what stops verbatim adoption from
    sawtoothing: the host's 1Hz number never contains the client's in-flight claims, so
    adopting it bare would erase the last second of their own kills once a second.
  - Provisional entries EXPIRE after `AwardSettleWindowMs` (3s) because one path never echoes
    a figure back: if the host's copy was already dead when our claim landed it pays us from
    its recent-death record without re-broadcasting. Expiring lets the next sync land on the
    host's exact number instead of staying inflated forever.
  - The real death path still runs on the client for the FX, but `NetSuppressAward()` claims
    the award slot FIRST so its `AwardScore`/`AwardScoreToAll` no-ops -- otherwise it would
    re-derive the amount from this peer's combo. **Any new client-side death path must do the
    same**, or it silently reintroduces the divergence.
  - `AwardScoreToAll` (every boss) pays each seated slot with THAT slot's own multiplier,
    which is why the wire carries a per-slot array rather than one number. Wire width: the
    field went `u16` base-points -> `f32` per slot (protocol **v6**) because a combo-modified
    award overflows a ushort -- a 10000-point boss at a routine 40x combo is 30000, and
    `comboModify` has no ceiling.
  - **The combo COUNTER is no longer local -- see the per-slot HUD state bullet below.** It was
    left local by this card ("cosmetic, only the score is reconciled"); card `1a3ad45a` found
    that framing was wrong and replicated it.
  - **Verify with `eaNetScore.test()`, not two windows** (`NetScoreLedger.SelfTest` +
    `NetPuppets.WireRoundTripTest`). It drives the real policy on a virtual clock, and runs
    the OLD `max()` adoption over the identical kill stream first -- a green tick means
    nothing unless the same input is shown to break the old policy, because the failure is a
    slow drift no frame or screenshot can show. It also asserts the injected per-kill error is
    UNBIASED, so the drift it demonstrates is the ratchet and not a stacked deck. The second
    section round-trips a real `EncodeDeathEvent` through `ApplyAwards` against the live
    `ScoreVisualiser` (wire offsets, fresh-pay vs settle, at-most-once).
  - `eaScore()` dumps per-slot score/combo/unsettled -- the readable way to compare two peers.
    The `[net]` line gains `scSkew`/`scSkewMax` on the JOIN side only (the host is the
    authority and never adopts): displayed minus `host + unsettled` at each sync, worst ACROSS
    the slots, which should sit at 0. (Recording it per slot instead would leave the LAST one
    standing -- slot 3, unseated in any 2-peer session, so a hard-coded 0.0 that looks like
    proof.) Measured over a two-peer run: `scSkew=0.0` steady state, and `scSkewMax` held at
    10.0 while `clTx` grew 20 -> 67 -- i.e. the worst deviation is one kill's correction and
    does NOT accumulate with kill count, which is exactly the property max() lacked.
- **Per-slot HUD state: a slot's combo and powerup progression belong to its OWNER (card
  1a3ad45a).** Every peer used to simulate BOTH -- a remote ship's shots are re-fired locally
  through the real `FireAt` path, so they are ordinary local `Bullet`s stamped with that slot's
  owner and `Bullet.CollidesWith` sustains its combo. On a client those bullets hit frozen
  puppets interpolated ~100ms behind the host's real entities, so the sims diverge routinely.
  - **The counter diverging is cosmetic; feeding `AddExp` with it was not.** Card `4717d3cf`
    set `powerupactive` for a remote collector, which is exactly the gate on
    `ScoreVisualiser.increasecombo` -- so a peer levelled up powerups for a slot it did not own,
    and `ScoreVisualiser_onLevelUp` then called `PlayerShip.PowerUp` on the PUPPET. For `OneUp`
    that is `Oracle.SetSlowmotion(12f)`: **twelve seconds of global slow motion fired
    unilaterally on one peer**, off an invented combo. `Option` spawned a real extra Option ship;
    `FirePower`/`Range` gave the puppet a weapon its owner did not have; `checkPowerupAchievement`
    could grant `FullPower` off another slot's simulated progress.
  - **`NetSession.OwnsSlot(slot)` is the gate, and it sits on `SustainCombo` -- the whole
    simulation, not just the `AddExp` branch.** Gating only `AddExp` leaves `AddCombo`
    incrementing between the owner's 100ms packets and the 1s `combotimer` zeroing a live combo
    whenever OUR re-fired bullets miss, i.e. the replicated value fighting a local one.
    `NetSetHudState` therefore also refreshes that slot's `combotimer` while the owner reports a
    live combo, because the readout's alpha is driven by its `TimeLeft`.
    It asks the ROSTER, not a live ship -- a slot's combo and levels outlive its ship (they
    persist across a death and respawn), so a ship-keyed test would flip while the player waits
    to come back. **Offline it is true for every slot**, which is what keeps single-player and
    local co-op byte-identical. The decision is split into a pure `OwnsSlotCore(active, seat)`
    so the test can table-drive `Remote`/`RemoteFriend`/unseated -- offline the predicate is
    unconditionally true, so a live-roster-only test could never reach those cases at all.
  - **`MsgHudState` (0x12, stream lane, ~10 Hz, BIDIRECTIONAL) carries the owner's version**:
    `[type][count]` then `[slot][combo:2][activeType][progress][level x 5]` per owned slot.
    Protocol **v9**. **combo is a USHORT and that is load-bearing** -- the host SPENDS the
    adopted figure (`AwardScoreToAll` -> `comboModify`), so a byte would cap a client's real
    400x combo at 255 and underpay it; combos past 255 are expected (1000 precached combo
    strings, an explicit `>= 1000` draw fallback). Levels cover the leading 5 `Powerup.PowerupType` values -- `OneUp`'s level is
    pinned at 3 and never increments, so the wire index IS the enum value and a NEW TYPE MUST GO
    AFTER `OneUp` (or widen `HudLevelCount` and bump the version). Stream lane because it is a
    readout: a dropped packet only means one interval of staleness.
  - Received state applies only to slots we do NOT own (a peer claiming one of ours is ignored,
    not trusted), bounded against `ScoreVisualiser.SlotCount` like `ApplyRemotePowerup`. The
    receiving peer never re-derives its OWN awards from the adopted combo (its score is
    reconciled by `EvScoreSync` + the unsettled ledger) -- but the HOST does spend it, which is
    the point of the side effect below.
    Levels go through the real `PlayerShip.PowerUp(..., doEffect: false)` one step at a time, so
    the puppet's re-fired bullets match its owner's actual loadout. **`OneUp` is unreachable
    there and must stay so** -- slow motion is deliberately local, which is the same reason the
    puppet driver dead-reckons on real time.
  - **Side effect, deliberate:** `AwardScoreToAll` (every boss) pays each slot with THAT slot's
    own multiplier, so the host used to compute the client's boss share from a combo the client
    never had. It now uses the real one -- a payout change, and a correction.
  - **Verify with `eaNetCombo.test()`** (`Compat/Net/NetComboTest.cs`), not two windows: the
    failure is a peer levelling a powerup it does not own, minutes into a fight, and its visible
    consequence reads as a hiccup rather than a desync. Section 2 drives the REAL
    `PowerupData.AddExp` over two divergent combo streams and runs the OLD ungated behaviour over
    the identical stream FIRST, asserting it levels the slot and reaches the `OneUp` trigger --
    a green tick means nothing otherwise (the `eaNetScore.test()` rule). Section 1 round-trips
    the wire format against the live `ScoreVisualiser` on the unseated slot 3 and restores it;
    section 3 pins `OwnsSlot`'s offline answer. `eaScore()` gained `own=`/`pu=`/`lv=` per slot,
    and the `[net]` line `hudTx`/`hudRx` (`hudRx` counts ENTRIES, not packets -- a peer with a
    couch partner sends two slots per packet).
  - **GOTCHA -- a two-window co-op run cannot be driven at full rate from this rig.** A
    backgrounded tab throttles to ~1 tick/sec (measured: `txStream` advanced 43 in 40s where
    30Hz would be ~1200), `?fpsuncapped` does NOT defeat it, and two tabs in one window can
    never both be visible. BroadcastChannel does not cross browser profiles either, so two
    separate browsers can only pair via `?rtc` + signaling. Plan net verification around
    one-tab round trips (this test, `eaNetBgTest`) and treat a two-window run as a
    smoke check whose absolute rates are meaningless.
- **Roster slots are HOST-ALLOCATED and identity-mapped (card 4d904410 -- local co-op AND
  online co-op at once).** The oracle slot IS the wire slot on both peers; there is no
  host-relative translation anywhere (the old `TranslateSlot` 0<->1 mirror and the
  `ApplyJoinHues` compensating hue swap are both GONE -- per-slot hues now agree by
  construction). The host's own primary is always slot 0; the joiner's primary slot rides in
  `MsgWelcome` (v5); a couch player joining the CLIENT asks with `EvJoinRequest` and the host
  answers `EvSlotGrant(slot)`, reserving that seat as `RemoteFriend` the moment it grants so
  its own `AddPlayer(AI)` / a later grant can't reuse it. Host-side couch joins allocate
  locally. `GameScene.AddPlayer` routes to `NetSession.TrySeatLocalJoin` while a session is up;
  offline behaviour is byte-identical.
  - **The primary grant is a NEGOTIATION, not a guess (card c0229c57, protocol v8).** The host
    allocates out of its OWN free slots and cannot see the joiner's, so it used to grant a seat
    the joiner might already hold -- which desynced the pairing silently and permanently (JIP
    pass trap 3 has the full story). The client's hello now carries a `blockedSlots` mask of the
    slots it cannot seat its primary in, and `NetSession.FirstMutuallyFreeSlot(hostOccupied,
    peerBlocked)` picks one free on both. Three rules hold it together:
    - **The mask is only non-zero while a `GameScene` is up.** At the menu -- where BOTH the
      menu-lobby and the join-in-progress joiner hello from -- the roster is leftover
      bookkeeping from the last level or attract demo, which the launch path's `ResetPlayers()`
      wipes before seating us. Reporting it would refuse seats for no reason.
    - **`peerPrimarySlot` is assigned ONLY on a settled adoption.** `Update`'s retry condition
      is `!PeerUp || peerPrimarySlot == SlotNone`, so setting it on a FAILED adopt silences the
      1 Hz hello on both peers and the pairing can never recover. This is the bug the card was
      about; treat it as an invariant of `AdoptGrantedPrimarySlot`, not a fact about one branch.
    - **It terminates.** Each round either seats the joiner or adds a slot to the mask, and the
      host never re-offers a blocked seat; when nothing works on both sides it sends
      `RejectFull` ("Game full"). The host's own game SURVIVES that -- `Stop()` does not exit a
      level and `NetListing.ComputeEligible` needs `!NetSession.Active`, so a listed host drops
      back to single-player and re-lists. Verify with `eaSlotTest()`.
  - **The roster is cleared on the way OUT of a scene as well as in (card ee96ea61).**
    `GameScene.Terminate` ends with `oracle.ResetPlayers()`. Before that only the launch paths
    reset it, so between a scene ending and the next launch the roster held whatever the last
    level or attract demo left behind -- and that window is where BOTH menu-lobby handshakes and
    the join-in-progress joiner hello run. The client side was already guarded
    (`LocalBlockedSlots` returns 0 with no `GameScene`), but **`HostOccupiedSlots` reads the
    roster raw**, so an attract demo could make a host answer a good joiner with `RejectFull`
    ("Game full") with no real players aboard, or grant them slot 2 instead of 1 for the whole
    session. Safe because `PlayerInfo.Reset()` only clears `isPlaying` -- score lives in
    `ScoreVisualiser`, unlocks in `Achievements`, and the hue is deliberately left alone. It is
    LAST in `Terminate`: `OnFinished` fires mid-method and has already queued the next scene
    (credits/menu), neither of which seats anyone.
    **Do not add a second menu-guard to `HostOccupiedSlots` instead** -- the reset is the root
    cause and covers `AllocateSeat`/`HandleJoinRequest` too.
  - **Read the OFFLINE roster with `eaOracleRoster()`** (`eval OracleRoster` under `eahl`).
    `eaNetRoster()` early-returns without a net session, so it cannot see the menu roster at
    all -- which is exactly where a stale seat does its damage. Needs no session, level or
    gamepad. (`eaScore()` also shows seated-ness; what this adds is the DEVICE per seat, which
    is what tells an attract demo's leftover AI seats from a real player.)
    **The repro, headless and flag-free:** `eahl --repl --flags "?menu"`, `step 1500 nodraw` to
    idle past the 20s attract timeout, `eval Press esc 2` back to the menu, `eval OracleRoster`.
    Pre-fix the demo's seats are still there afterwards (`players=1 seated=0:AI`, or more --
    slot 0 always, plus 3 on a 20% roll and 1 on a further 40%); post-fix `players=0`.
    Do NOT read `info`'s `scene=` to tell whether the demo ran -- it reports the booted level and
    stays `Level2`/`menu` throughout; the roster dump is the signal. `eaSlotTest()` covers what that stale
    roster then COSTS at the allocator, but it seats its scratch roster by hand and never
    reaches `Terminate` -- so it cannot substitute for this run.
  - **Every seat-taking path must use `NetSession.LocalPrimarySlot`**, not "the first free
    slot": `Game1.MenuFinished`, `Game1.LaunchLevelDirect` (the `?level=` boot -- a `?net=join`
    tab pairs WHILE it boots, so the grant can land before the seat is taken) and
    `TeamChallenge.Initialize`. Getting this wrong is silent: the ship sits in a slot the wire
    doesn't know about and simply never replicates.
  - **Sparse rosters are legal now** -- a hole is normal (a granted seat not yet filled, a
    friend puppet that died). Anything walking the roster asks `Oracle.IsSeated(slot)` over
    `0..MaxPlayers-1` instead of assuming `0..Players-1`: `ScoreVisualiser`'s score-vs-
    "Press Start" panels and `GameScene.SpawnAllPlayers` (which spreads spawns by the player's
    ORDINAL among seated slots, so a dense offline roster spawns exactly where it always did).
    `Oracle.AddPlayer` returns the slot it seated; `GameScene.SpawnPlayer` takes it explicitly
    (`oracle.Players - 1` only agreed while dense).
  - `MsgFriendState` is now BIDIRECTIONAL and carries every locally-owned non-primary ship
    (AI friends *and* couch players) -- `ControlDevice.RemoteFriend` means "network-driven
    extra ship", whoever owns it. `EvBlast` gained a slot byte (a couch player's bomb used to
    detonate on the peer's PRIMARY puppet) and `EvScoreSync` widened from 2 slots to 4.
  - `DriveFriendShip` ADOPTS a ship the scene spawned into its slot (`SpawnAllPlayers` respawns
    every seated slot after a reset, puppet slots included) -- without it the re-spawned puppet
    matched no channel and froze on its spawn pose. The primary remote path always adopted;
    this one didn't, which only stopped being a corner case once couch players (who hit resets
    constantly) could exist.
  - **Verify with `?netlocal=<1-3>`**: queues that many synthetic couch joins on this peer a few
    seconds after the session goes live. A real couch join is a gamepad Start press, which the
    rig cannot produce -- no physical pads, and seating a Pad device with none connected trips
    GameScene's disconnected-gamepad force-pause every tick -- so it seats `Generic` (a real
    human device with no connected-check) then `AI`. The `[net]` line gained
    `roster=<slot:device[*]> pri=<local>/<peer> ships=<owner:device>`; **the two consoles must
    print mirror-image rosters** (`*` = ours). Recipe:
    `?level=Level2&net=host&aiplayer&invuln&netlocal=1&room=<r>` + the same with `net=join`
    (Level2, not Level1 -- Level1's intro hands the ship spawn to a script beat, so the host has
    no ship for the first minute). Expect
    `roster=0:Keyboard*,1:Remote,2:Generic*,3:RemoteFriend` on the host and
    `0:Remote,1:Keyboard*,2:RemoteFriend,3:Generic*` on the join side.
  - **A full RESET with couch players aboard is reached with `eaKillShips()` on both tabs**
    (card af0eb00a) -- and needs no new tooling, because the "all four ships dead at once"
    framing is weaker than it looks: `Oracle.AllShipsDead` is `playerShips.Count == 0` and
    NOTHING respawns until it fires, so **dead ships stay dead** and the two console calls need
    not land in the same frame. After the second tab fires, each peer's puppets die on their own
    existing paths (the primary remote on the `alive=false` edge, the couch puppet on the 500ms
    `FriendTimeoutMs`) and `AllShipsDead` then trips `LoseLife`. Read the result with
    **`eaNetRoster()`** on both peers either side of the kill: the 5s `[net]` cadence can
    straddle the whole ~2.7s reset, so a sampled before/after can show nothing. The gate is
    `resets` +1 on both, `roster=` (the seat map) IDENTICAL across the reset and still mirror-
    image, and `ships=` back to one entry per seat -- a **missing** owner is a puppet that never
    re-adopted (frozen on its spawn pose), a **duplicate** owner is a double spawn.
    **`ships=` alone cannot tell "adopted" from "frozen"** -- a never-adopted puppet still shows
    as a ship in its seat. That is what the dump's `at=<owner>:<device>@x,y` is for: sample it
    twice a second or so apart and check (a) the slot MOVES and (b) the two peers agree per slot
    within interpolation lag. Observed clean: slot 3 read `592,305|592,305` -> `521,283|521,283`
    -> `389,362|389,362` host|join across the field after a reset. Caveat: right after the reset
    the purge leaves no enemies, so the `?aiplayer` AI parks every ship on the spawn ladder
    (`y ~ 120/240/360/480`) for a few seconds -- that is the AI having no target, NOT a frozen
    puppet. Wait for the spawners to replay before reading motion.
  - **GOTCHA -- an OCCLUDED window freezes the whole run, and it fails silently.** Chrome marks a
    fully covered window `visibilityState:'hidden'` (even with `document.hasFocus()` true) and
    stops rAF entirely, so a peer parked behind another window simply stops ticking; the peers
    then time each other out and every metric is garbage. Two side-by-side windows is the
    documented answer, but when the surrounding tooling covers them (an automated run driving
    Chrome from another app) **add `?fpsuncapped` to BOTH peers** -- it drives the loop off a
    `MessageChannel` instead of rAF, so both keep ticking while occluded. Verified: an occluded
    `?fpsuncapped` pair ran a full reset cycle with `drop=0 sgap=0 ordViol=0 seqGap=0`. It is a
    LOOP flag, so it needs neither the HUD nor `?nofps`. Cost: the client runs far above vsync,
    which inflates `pupPops`/`dup`/`snapUnk` around id churn -- read those as not comparable to
    a normal-rate run, while roster/adopt/`resets` assertions stay valid.
  - **`?netdropgrant` (client) is the only trigger for `ExpireUnclaimedGrants`, and it is
    ONE-SHOT (card ee96ea61).** The host holds
    a granted couch seat as `RemoteFriend` until the peer's first stream for it lands; a client
    that silently fails to take the grant would otherwise leak that seat for the session (and the
    game stops being re-listable). `?netlocal` always TAKES its grant, so the expiry path had no
    trigger at all -- this flag drops the **first** `EvSlotGrant` of a session after clearing
    `joinRequestPending`, leaving this side exactly as a genuine failed take does, and lets every
    later grant through. Expect the host to log
    `granted peer couch join slot=N` then `released unclaimed couch grant slot=N` ~10s later
    (`GrantClaimTimeoutMs`), and the seat to leave `roster=` rather than leak.
    - **It dropped EVERY grant until card ee96ea61**, so a run could only show the DROP half and
      "the reclaimed seat is re-usable" went unverified. `?netlocal=2` now covers both halves in
      one run. Note the second join lands ~3s after the first while `GrantClaimTimeoutMs` is 10s,
      so it is handed a DIFFERENT free seat -- proving recovery, not reuse. For reuse proper,
      wait out the release and call `eaNetCouchJoin()`, or just read `eaSlotTest()`, which drives
      the whole reserve -> hold -> expire -> reallocate cycle as data.
    - **The latch is per SESSION and the clearing is the load-bearing half** -- a flag outliving
      the thing that set it is the exact bug class this seam exists to hunt, so it lives in
      `NetSession.ResetPerSessionState` beside `joinRequestPending`, and `eaSlotTest()` asserts a
      teardown clears it (driving `ResetPerSessionState` directly, since `Stop()` early-returns
      with nothing Active and would make the leg vacuous -- the `eaKickTest()` precedent).
  - **`RejectFull` needs `eaNetCouchJoin()`, NOT `?netlocal`.** Reaching it means the host roster
    is already full when a joiner says hello, which means couch players seated BEFORE pairing --
    and `TickLocalJoinSim` is deliberately gated behind `PeerUp` (pre-pairing, `AllocateSeat`
    cannot yet know which seat the joiner's primary will need, the very hazard its comment warns
    about). So `?netlocal=3` can never fill the roster in time: the joiner is the peer that
    ungates it, and it already holds a seat by then. `eaNetCouchJoin()` makes the same
    `TrySeatLocalJoin` call a real gamepad Start makes, which is NOT PeerUp-gated -- call it 3x
    on a `?net=host` boot to reach `roster=0:Keyboard*,1:Generic*,2:AI*,3:AI* peer=down`, then
    pair a `?net=join`. Host logs `no free roster slot for the joiner -- rejecting` +
    `session stop (pairing rejected)`; the joiner logs `peer rejected the pairing (reason=4)`
    (4 = `RejectFull`) + `session stop (rejected by peer)` -- an explicit reject rather than a
    bare channel close is what proves the `RejectGraceMs` deferral let the reliable frame out.
- **Remote ship:** `ControlDevice.Remote` (APPEND-ONLY enum position). Joins via
  `oracle.AddPlayer(Remote)` on the first alive stream (or is spawned by the GameScene's
  own SpawnAllPlayers reset flow -- NetSession adopts either). `PlayerShip.Update` case
  Remote -> `NetSession.DriveRemoteShip`: position sampled from `ShipStateBuffer`
  ~100 ms behind the newest sample (velocity-extrapolated max 250 ms on underrun), speed
  zeroed; shots re-fired locally through the real `FireAt` path from the replicated firing
  state; bombs arrive as EvBlast -> `NetDoBlast` (no local bomb-count gate). Remote ships
  take NO local damage (owner decides its own hits; death arrives as the alive-flag edge ->
  local explosion FX, slot stays reserved for respawn) and CANNOT take powerups locally --
  the owning peer collects on its own screen and the pickup arrives as a claim. Hues need no
  fixing up since card 4d904410: slots are host-allocated and identity-mapped, so a slot's
  colour is the same on both screens by construction and the old join-side hue swap is gone.
  (Caveat: `MenuScene.changeColor` lets a player recolour a slot and `PlayerInfo.Reset` doesn't
  restore it, so "host white / joiner purple" holds for DEFAULT colours; nothing normalises the
  two peers' hue tables.) The puppet's render clock advances on REAL time (never turbo/slowmo/
  hit-stop-scaled game time) -- a local hit-stop must not drag the interpolation point.
- **Verify with LOGGED METRICS, not screenshots** (`Compat/Net/NetMetrics`): a parseable
  `[net] role=... pops=... snapTx=... clRx=...` line every 5s. Healthy: buf ~100ms,
  extrap ~0, pops 0 (pop = a step no ship could physically make: > 2x MaxSpeed x realDt
  + 3px), drop/dup/ordViol/seqGap 0; on the world side, host `snapTx` climbing, client
  `snapRx/snapEnt` climbing with `snapUnk` small and non-climbing at steady state (but read
  its split -- see below), `pupPops` near 0 **judged against `snapTurn`** (next bullet),
  and the claim counters telling the kill story (`clTx` client-side ~=
  `clRx` host-side; `clKill` = claims that settled a live enemy, `clPaid` = generous
  payouts for already-dead enemies -- a nonzero `clPaid` IS the double-claim proof).
  **Two-tab test recipe:** the tabs must BOTH be visible (a backgrounded tab's rAF drops
  to ~1Hz and its peer times out / crawls) -- use two Chrome WINDOWS side by side:
  `?level=Level1&net=host&aiplayer&invuln&room=<r>` + same with `net=join`; both ships play
  themselves via `?aiplayer`, then read both consoles. `?room=` must be fresh per test pair.
  Add `?binlog` to both when the run is about lifecycle (it is the detector for a pause freeze,
  or for the purge filter eating a BANNER -- no longer for it eating a PUPPET, since card
  74403f83 exempted the puppet layer from the filter and the bin's divert log sits inside the
  branch that exemption skips; a puppet add that somehow still gets swallowed prints its own
  `[net] puppet add was diverted by the bin` line instead). For a death/reset, KEEP `?invuln`
  on both and call
  `eaKillShips()` in each console -- `Asplode()` only guards on `!IsDead`, so the helper bites
  through invulnerability, and leaving the flag on is what keeps the rest of the run from
  dying at random. `AllShipsDead` needs BOTH ships down, so fire it on both tabs.
  **`snapUnk` climbing is not by itself a leak -- read the SPLIT, never the total** (card
  48ab9b2f). Three unrelated things make a snapshot entry "unknown", and the `[net]` line breaks
  them out as `snapNew`/`snapDead`/`snapBad` (`snapUnk` remains their sum):
  - `snapNew` = an id we had never seen, which the self-heal REBUILT from the snapshot. The
    unreliable stream lane routinely outruns the ordered reliable one, so a fresh spawn's first
    correction can beat its `EvSpawn`. **Benign, and it tracks the world's SPAWN rate** -- in a
    continuously spawning fight it never stops climbing, which is not a fault.
  - `snapDead` = an id removed HERE inside the 3s `RecentRemovalWindowMs`, deliberately left
    dead. **Benign, and it tracks the world's TOTAL removal rate.** The old note here tied this
    to `clTx`, which was WRONG and cost card 48ab9b2f's JIP pass its verdict: `MarkRemoved`
    fires on every local removal, host-authoritative `EvDeath`s included, so an IDLE joiner
    watching the host's AI clear a field logs plenty of `snapDead` with `clTx` pinned at 0.
  - `snapBad` = the rebuild was REFUSED (no descriptor for the typeIdx, the descriptor declined,
    or the bin swallowed the add). **This is the one that means trouble** -- it re-counts on
    every turn the host streams that id. An unknown typeIdx re-counts on literally every turn;
    the other two mark the id removed first, so they show as one `snapBad` then `snapDead` for
    3s, then another retry -- i.e. a slow, steady tick rather than a burst. Any sustained
    `snapBad` deserves a look.
  Attribution is pinned by **`eaNetSnap()`** (`Compat/Net/NetSnapshotTest.cs`), which drives the
  real `OnSnapshotEntry` through all four outcomes from the main menu -- a classification is
  invisible in any frame, and a second peer tab throttles too hard to show it anyway.
  **A STRUCTURAL check (roster, slots, who-owns-what) is the one thing two HIDDEN tabs in one
  window can still do**, which is how the four-seat roster in the `?netlocal` bullet was
  captured without hand-arranging windows. Two things make it survive: `index.html` falls back
  to `setTimeout(tickJS, 33)` while `document.hidden` (a REQUESTED ~30Hz -- Chrome clamps
  hidden-tab timers after ~10s and much harder past 5 min, so treat it as a short window, not a
  rate you hold), and the roster simply does not depend on cadence -- once `PeerStalled` the
  friend timeout stretches to `PeerTimeoutMs + PeerGraceMs`, and a timed-out friend **keeps its
  seat** by design (`NetSession.Friends.cs`). It does NOT extend to anything timing-derived:
  `pops`/`pupPops`/`buf`/`extrap` off a hidden or unfocused tab are meaningless (the FPS HUD
  says so on its own readout), so every smoothness or feel verdict still needs two focused
  windows.
- **Script beats replicate at the side-effect PRIMITIVES (card 11.3), never per level:**
  the level script only runs on the host, so its observable side effects are hooked where
  they happen and mirrored as reliable events -- `MessageEvent`/`UnlockEvent` at their
  banner spawns (the unlock is also GRANTED on the join peer -- it played the level too),
  the mid-level `Background` ops (`SetSpeed`/`Queue*`/belt slowdowns/`SetAlienBase2..6`;
  the wire opcode enum `NetBackgroundOp` is APPEND-ONLY; Initialize-time setters are NOT
  hooked -- both peers run their own scene Initialize), `SoundManager.PlayMusic/StopMusic`
  (client applies via `NetApplyMusic`, deduped against the playing cue so the boot-time
  track never restarts), and the checkpoint callback (client mirrors `score.Save()` so a
  later reset restores the same baseline). Any future boss code calling these primitives
  replicates for free. `CrossFade` is deliberately NOT hooked (it belongs to the reset
  flow, which each side runs itself).
- **Death/checkpoint reset + victory are host-authoritative broadcasts (card 11.3):**
  `LoseLife` no-ops on a client; the host broadcasts the branch it took (EvReset:
  respawn / reset / game over) and `GameScene.NetApplyReset` mirrors the exact state
  transition -- the client then runs its own purge-and-replay flow while the host's
  post-revert spawner replay rebuilds the puppets. `Victory()` broadcasts EvVictory (the
  win trigger lives in the host-only script); the client runs its own `Victory()` from it,
  achievements included. `GameScene.NetActiveScene` (static, set in Initialize / cleared
  in Terminate) is how NetSession reaches the private state machine.
- **Host kick / kick+block (card 0b8a300b) -- the host's ONLY agency under a remote pause.**
  A remote pause freezes our world via `ComponentBin.Push`, which disables every collection
  component **including `GameScene`** -- so the host's own pause trigger never runs, and the
  drop failsafe can't help either (a held pause widens the timeout to the 120s
  `PausedPeerTimeoutMs` backstop). Before this card a stranger off the public game browser
  could freeze someone's run indefinitely.
  - **`NetKickMenu`** (a `ConfirmationMenu`) replaces `NetPauseOverlay` for the HOST once the
    pause outlasts `NetSession.KickOfferDelayMs` (4s): `Keep Waiting` / `Kick Player` /
    `Kick and Block`. It works for the same reason the local pause menu does -- **added AFTER
    the Push, so it stays `Enabled`**. Entry 0 is `Keep Waiting` and preselected, so a
    reflexive Enter over a suddenly-appearing menu is harmless. Declining **re-arms** the
    offer (`NetSession.RearmKickOffer`), so waiting once never forfeits it. The client keeps
    the plain overlay -- there is nobody for it to kick.
  - **The offer timer lives in `NetSession.Update`, not `GameScene`** -- `GameScene` is frozen
    by the Push, so it cannot time its own escape hatch. Real time (`NowMs`), like the rest of
    the net layer; `gameTime` means nothing in a frozen world.
  - **`KickPeer(block)` splits the teardown deliberately:** everything visible happens now
    (unfreeze, `ExplodePuppet`, `oracle.ReleasePlayer(Remote)` + `ReleaseAllFriendPuppets`),
    but `Stop()` waits out `RejectGraceMs` -- `Stop() -> pc.close()` is ABORTIVE on WebRTC and
    would discard the still-buffered `EvKick`, leaving the kicked player with a generic
    "disconnected" instead of a reason. Do NOT collapse it back into one call.
    The client's `EvKick` handler reuses `EndMatchPeerGone` -> `NetApplyPeerLeft`, which
    already unwinds its own pause-menu depth (it is almost certainly sitting in it) and exits.
    A kick applies to EVERY session kind and is never a match end for the KICKER: the host
    reverts to single-player and plays on (`RevertToSinglePlayer`, shared with JIP peer-loss).
  - **The block needs an identity, so the handshake gained one -- protocol v5 -> v6.**
    `eaRtc.peerId` = a random 128-bit token minted once into `localStorage`, FNV-hashed to 8
    wire bytes (`HelloBytes` 13 -> 21). **It is SELF-REPORTED: a speed bump against casual
    re-joining, not authentication** -- clearing site data or incognito mints a new one. Never
    sent to the signaling server, only to an already-connected peer. Don't build anything that
    must trust it on this. `peerId` 0 (JS could not produce one) is never recorded and never
    matched, so one broken `localStorage` can't get every such peer refused.
  - Enforced in `HandleHello` (`RejectBanned`) -- the ONE choke point both rejoin routes pass
    through (public browser AND a typed room code), and before `PeerConnected`/slot
    reservation, so a blocked peer re-pairing never touches the world. `blockedPeers`
    deliberately **survives `NetSession.Stop()`** (a kick stops the session and the host
    re-lists seconds later; the block must outlive that) and is cleared in
    `GameScene.Terminate` = the card's "for that session only".
  - **Verify with `eaKickTest()`** (`Compat/Net/NetKickTest.cs`) -- the block predicate + the
    v6 codec as DATA, because both dangerous failures are invisible in play: a block that
    fails to persist across the kick's own `Stop()`, and a wire-layout slip that decodes the
    wrong bytes as a peer id. It restores the live set, and SKIPS the survives-`Stop()` leg
    over a live session rather than ending a real match (it says so; a skipped leg is not a
    pass). `?netkickshot` (pair with `?level=`) parks the menu over a live level for a
    screenshot. **`?netfakepeer=<s>` is REQUIRED for any two-tab test** -- both dev tabs share
    one `localStorage`, so they present the SAME peer id and blocking the joiner would block
    yourself (the `?netfakehash=` trick, same reason).
- **Pause is a replicated event; the triggers stay local (card 11.3):** the local pause
  push / every resume path sends EvPause on/off. The receiving side freezes via
  `Collection.Push()` under a `NetPauseOverlay` ("OTHER PLAYER PAUSED") -- no interactive
  menu. Overlaps resolve in `GameScene.NetSetRemotePaused` + `NetLocalPauseReleased`:
  the world unfreezes only when BOTH sides are clear; a scene that Initializes while
  `NetSession.RemotePaused` picks the freeze up at the end of Initialize (level-load
  race). GOTCHA kept: net TeamChallenge seats ONLY the local device -- the offline
  `AddPlayer(PadOne)` would trip the disconnected-gamepad force-pause every tick and
  squat the remote slot.
- **TeamChallenge tether online = a LOCAL first-order pull (card 11.3):** the rigid
  midpoint +/-39px `SetPosition` pinning would fight the interpolation buffer, so in a net
  session each peer softly pulls only its OWN ship toward the puppet's on-screen position
  (`ShipConnector.NetPullOwnShip`; consts `NetRestPx` 78 / `NetPullK` 0.0018/ms /
  `NetMaxPullPxPerMs` 0.22, picked by `tools/sim/tether_sim.py` -- first-order, no
  velocity state, overdamped to 300ms one-way; if it ever wobbles SOFTEN K, never
  stiffen; the clamp sits below ship MaxSpeed so players can always fight the pull).
  Tether break is an or-of-either-peer idempotent event (local cause sends EvTetherBreak,
  the receiver breaks silently via `NetBreakSilently`); shared-fate death asplodes only
  locally-owned ships and defers the life/reset to the host. Connector creation waits for
  BOTH ships (the puppet joins a beat late -- `netConnectorPending` in TeamChallenge).
- **World-authority coverage gaps (follow-up to card 11.2):** the replicable set was extended
  to the enemy/boss types 11.2 left host-only -- PlasmaBall, the paratrooper family
  (ParatrooperAlien/ParatrooperBrain/Parachute), FakeBoss, SpiderBoss, BrainBoss,
  SpiderHelperMothership -- as `NetTypeRegistry` descriptors 21-28 (append-only;
  `Compat/Net/Descriptors/DescriptorsCoverage.cs`). The enemy laser-CHARGE glow (a child
  `LazerGenerator` the emitter draws by hand) now replicates too: rather than making
  LazerGenerator itself replicable (it is also the player-summon glow), the SweepUFO / MarsBoss /
  SpiderHelperMothership descriptors stream a tiny charge state and the puppet rebuilds a local,
  silent copy into the emitter's own generator field (`AlienDrawableGameComponent.NetDriveExtras`
  driver hook + `Compat/Net/NetChargeGlow`). The fired beam already replicated as its own `Lazer`.
- **AI "friend" ships replicate (host-authoritative), follow-up to card 11.2:** the Mechanical
  Friends cheat is re-enabled in net sessions -- but ONLY the host adds AI friends (it runs the
  real AI, whose enemy kills already replicate), and only after the client's Remote ship has
  taken its slot. The host streams each friend (`MsgFriendState`, slot-tagged) and the client
  shows it as a `ControlDevice.RemoteFriend` puppet (`Compat/Net/NetSession.Friends.cs`): its own
  per-slot jitter buffer/interpolation clock (a copy of the single-remote path, kept ISOLATED so
  it can't regress it), IDENTITY slot mapping (the puppet lands in the host's slot so per-slot
  score/lives sync lines up), bullets re-fired locally, death via a per-slot stream timeout. The
  budget is `Settings.Friends + 1` TOTAL ships incl. the remote (so a 2-human session needs the
  cheat >= 2 to spawn any AI friend). The whole path is dormant unless the cheat is on.
  `ControlDevice.RemoteFriend` is APPEND-ONLY. (NOTE: the game-browser JIP attach path below is a
  separate session and does not stream friends -- its listing stays refused while `Friends>0`.)
- **Hardening pass (card 4717d3cf / 11.5):**
  - **A powerup collected by EITHER peer drives that peer's HUD slot.** `PlayerShip.CollidesWith`
    is the only `SetPowerup` caller and is gated to the local ship, and each peer numbers its
    OWN ship slot 0 -- so the icon used to move only on the P1 panel, and a remote pickup
    settled as a bare despawn. Both settle paths (host `HandleClaim`, client
    `NetPuppets.OnRemoteDeath`) now call `NetSession.ApplyRemotePowerup`. This also restores the
    remote player's powerup LEVEL, since `ScoreVisualiser.increasecombo` only feeds `AddExp`
    while that slot's `powerupactive` is set. Only the INDICATOR is mirrored -- the Blast/bomb
    count deliberately is not, because the spend side (`NetDoBlast`) does not decrement it
    either. A slot off the wire must be bounded by `ScoreVisualiser.SlotCount` (4), NOT the 8 of
    the claim ledgers' PaidMask.
  - **`AlienDrawableGameComponent.NetSpinPerMs` opts a type out of REPLICATED rotation** and
    spins its puppets locally instead (Asteroid). A puppet's Update is frozen, so a
    continuously spinning type could only advance at its ~16.7 Hz round-robin snapshot turn --
    visibly choppy. Only override where rotation is cosmetic and no hitbox reads it.
  - **Peer stall != peer lost.** `PeerStallMs` raises `NetWaitOverlay` (banner only -- it does
    NOT push the collection, because the world staying live is the point and dimming a
    playfield the player is still dodging in would be worse than the hiccup) and parks puppet
    dead-reckoning (`NetSession.PeerStalled`; without that, the wider grace would let stale
    velocities fling the enemy world seconds off and then snap). The verdict only lands at
    `PeerTimeoutMs + PeerGraceMs`, through the single `EndMatchPeerGone` path shared by
    EvLeave / drop timeout / pagehide bye. GOTCHA: `GameScene.Terminate` must drop the banner
    BEFORE nulling `NetActiveScene` -- it is a plain `DrawableGameComponent` in the global bin
    that no `Purge<T>` covers, and level scenes are re-added singletons, so an orphan would
    both draw over the menus and poison the next play of that level.
- **Known limits (by design -- next cards):** a dead local player will NOT respawn while the
  remote puppet lives (LoseLife triggers on AllShipsDead); the session is exactly two PEERS
  (see the sub-bullet below); DevCommentEvent commentary is not replicated (profile-local
  setting).
  - **Two PEERS is not two PLAYERS -- 4-player online co-op already works today** (card
    2e0f908b), as two consoles with a couch partner each; the four-seat roster in the
    `?netlocal` bullet above IS that, measured. What does not exist is 3-4 separate MACHINES.
    The player dimension is already 4-wide everywhere (`Oracle.MaxPlayers`,
    `ScoreVisualiser.SlotCount`, slot-keyed `MsgFriendState`, `EvScoreSync`, the claim
    ledgers); only the peer dimension is 2-wide, across five layers. Feasibility answer,
    per-layer blocker list and the N-peer design (star/host-relay, forced by the no-TURN
    connection math) are in `plans/4p-online-coop.md`. Boss puppets are
  best-effort (the harness caveat): deep Update-reached attack poses may diverge until their
  state extras grow (the SpiderBoss debris death + BrainBoss/FakeBoss multi-phase asplode do not
  play on the client -- an attributed remote death removes the puppet). The time-scaling half of
  the old first-wipe `pupPops` burst is FIXED (the puppet driver now dead-reckons on real time,
  above); if a residual first-wipe burst ever shows, it's the reset/id-churn transition (purge +
  checkpoint replay), reproducible in the headless two-peer net sim's reset scenario, not the
  puppet clock.
- **Public game browser + join-in-progress (card 2001fbd8, design `plans/net-game-browser.md`):**
  a running single-player game can be LISTED so strangers find + join it, with NO `NetSession`
  constructed until someone actually arrives.
  - **One eligibility predicate drives everything** (`Compat/Net/NetListing.ComputeEligible`):
    any empty player slot (`oracle.Players < Oracle.MaxPlayers` -- card 4d904410 relaxed this
    from `== 1`, so a COUCH game with a spare seat lists too and the browser's players column
    genuinely varies 1..3) + `Settings.AllowOnlineJoins` (new Option,
    **default ON**) + no cheats/`DebugFlags.Active` + level not `WebcamAliens`/`TeamChallenge`
    + no session already up. The SAME predicate gates the listing, the beacon, and the pause
    indicator, so they can't disagree. `NetListing.Tick` runs each tick from
    `Game1.UpdateInner` (right after `NetSession.Update`).
  - **Listing != session.** A listed game keeps ONE lightweight signaling WS open (via
    `eaRtc.list`, reusing the 11.4 host machinery: `{t:host}` -> code -> `{t:list}` + a ~30 s
    `{t:beat}`, auto-answering browser `{t:ping}`s). It stays plain single-player (AI friends,
    no score sync, no Turbo lock) until a stranger pairs. This knowingly breaks 11.4's
    "single-player never touches a server" invariant -- the card's default-on premise; the
    Options toggle + pause "Listed online -- room XYZAB" indicator are the mitigation.
  - **Join-in-progress:** on pairing (`eaRtc` drives the host handshake -> "connected"),
    `NetSession.StartListedSession` attaches a HOST session to the running `GameScene`, sends
    the joiner `EvLaunch(currentLevel, difficulty)` + relies on the existing `EvReady`
    ->`ReplayLive` + 1 Hz `EvScoreSync` catch-up. The joiner is a normal menu-session client
    (`NetLobby.JoinWithCode`). A `listedSession` differs from a menu session ONLY in peer-loss:
    the joiner leaving reverts the host to single-player (NetListing re-lists) instead of
    force-exiting the host's own level.
  - **Ping is MEASURED, not estimated** (`server/signal/main.py` relays browser->host->back;
    `webrtc.js` auto-pongs in JS). Drop the old self-reported rtt idea entirely.
  - **Browser UI:** `SubMenuOnlineGames` (a `SubMenuCarousel`, the geometry extracted verbatim
    from `SubMenuLevelChoice` -- both now derive from it) shows one entry per open game with the
    level's screenshot art (`LevelArt`) + difficulty/players/ping/room-code. `NetGameBrowser`
    opens the browse socket, parses the room list, and fills each ping as its pong lands ("--"
    until then). Reached from the main-menu "Online Co-op" submenu's "Join Online Game".
  - **Beacon:** `ScoreVisualiser.drawPressStart`'s `Player X` <-> `Press Start` blink gains a
    third string `Room code: XYZAB` while listed, and its 4-cycle stop is suppressed, so the
    code surfaces ~every 15 s (the existing intermittent rhythm, never a static banner). The
    `bool showPressStart` became an index `promptPhase` (drawn `% (listed ? 3 : 2)`).
  - **Flags:** `?gamebrowser` boots straight to the carousel with injected FAKE entries (no
    server) for a screenshot; `?netjip` lets a `?level=` (`DebugFlags.Active`) host list anyway
    for the two-window JIP metrics test (it also drops the debug-flag bit from its hello so a
    clean joiner won't reject it).
  - **Verify:** `server/signal/test_signal.py` (registry/browse/build-filter/ping-relay/full->
    delist, all standalone); `?gamebrowser` for the carousel; the eligibility predicate as data;
    `?netjip` two windows -> `[net]` metrics. The full two-window pass was RUN in card c0398370;
    the five traps that make it hard, and the recipe, are the next five bullets.
  - **JIP pass trap 1 -- it needs two genuinely VISIBLE OS WINDOWS. Two TABS cannot work, and
    `?fpsuncapped` does not rescue them.** A background tab's rAF is *paused* outright (measured
    0 ticks), and the `MessageChannel` pump `?fpsuncapped` swaps in still ran at only ~3 ticks in
    3 s in one measurement -- roughly 1 Hz, nowhere near the ~30 Hz ship stream
    (`StreamIntervalMs` 33). Chrome's *documented* intensive throttling targets timers rather
    than `MessageChannel` macrotasks, so treat the exact mechanism as unconfirmed inference; the
    observation (rAF 0, uncapped ~1 Hz, both useless) is what matters. An OCCLUDED or MINIMISED
    window counts as hidden too, so the two windows must be tiled non-overlapping AND kept above
    everything else: pin exactly the two peers `HWND_TOPMOST` via Win32 and make sure the window
    DRIVING them is **not** topmost, or every interaction with the driver raises it over a peer
    and silently freezes that peer mid-run. Both peers ticking at the SAME rate is the check that
    the rig is honest.
  - **JIP pass trap 2 -- the joiner must boot FLAG-CLEAN.** The reject is
    `menuSession && (peer debug bit || DebugFlags.Active)` (`NetSession.cs`), and the joiner IS a
    menu session, so its OWN `Active` bit rejects the pairing. The net-relevant flags still open
    to it are `?noattract`, `?signal=`, `?binlog`, `?netlog`, `?netlag=` and `?netloss=` (none are
    in the `Active` expression), plus the JS-owned `?fpsuncapped`/`?nofps`, which never reach C#.
    **`?netsim` is NOT usable on a joiner**: it is parsed only in `index.html`, and that block
    early-returns unless `?net=` is present -- which sets `NetRole` -> `Active` -> rejected. The
    host is fine: `?netjip` drops its debug bit (`LocalHelloFlags`) and the check is
    `menuSession`-gated, so a `listedSession` host never rejects. **Put `?noattract` on the
    joiner's URL** (out of `Active` since card af63f958) rather than driving its lobby against a
    20s idle timer.
  - **JIP pass trap 3 -- a grant whose TARGET seat was taken used to desync SILENTLY and
    permanently. FIXED in card c0229c57 (protocol v8); the trap is recorded because the shape is
    instructive.** `Oracle.MovePlayerSlot` refuses when `players[to].isPlaying`, so it was the
    *granted* slot being occupied that bit -- a joiner merely seated in slot 0 with slot 1 free
    moves across fine and logs `moved local primary slot 0 -> 1`. On refusal
    `AdoptGrantedPrimarySlot` logged `... (slot busy) -- staying put` and the peers disagreed
    forever (`pri=0/0` vs `pri=0/1`), the joiner never built a remote puppet (`remoteShip=0`,
    `buf=0ms`), and NOTHING surfaced to the player.
    **It was reachable with no debug flags at all**, which is the part worth remembering: the
    menu's roster was whatever the last scene left behind (`GameScene.Terminate` did NOT reset
    it; only the launch paths' `ResetPlayers()` did -- card ee96ea61 has since made Terminate
    reset it too, see the roster-slots bullet), and the attract demo seats MORE than one --
    `mainMenu_DemoSelected` seats slot 0, then `Demo1/2/3.Initialize` adds 3 more on a 20% roll
    and 1 more on a further 40% roll. So "idle at the menu -> attract demo -> key out -> Online
    Co-op -> Join" left slot 1 seated ~60% of the time, and a couch session backed out to the
    menu did it every time.
    The fix is three things. (a) The host no longer GUESSES: the v8 handshake carries a
    `blockedSlots` mask (client -> host) so `ReserveRemotePrimarySlot` grants a seat free on
    BOTH rosters -- see the roster-slots bullet. (b) The client only moves a seat when a
    `GameScene` is up; at the menu the roster is bookkeeping `ResetPlayers()` is about to wipe,
    so there is nothing to move. (c) A grant that still lands badly RENEGOTIATES rather than
    settling -- `peerPrimarySlot` is now assigned only on a settled adoption, which is what keeps
    the 1 Hz hello alive so the host can re-grant. **That last one is the general lesson: any
    early return in `AdoptGrantedPrimarySlot` that leaves `peerPrimarySlot` set silences the
    retry on BOTH peers and makes the session unrecoverable.** Verify with `eaSlotTest()`.
    (Note the `?noattract` point in trap 2 is about the TEST RIG only -- a real player never
    passes flags, so the attract-demo roster is exactly how this reached them.)
  - **JIP pass trap 4 -- use a LOCAL signaling rig, not the deployed one.** All four entry points
    read `DebugFlags.NetSignal` (`NetListing.Tick`, `NetGameBrowser.Start`, `NetLobby` host/join,
    `WebRtcTransport`), so `uvicorn main:app --port 8091` in `server/signal` +
    `?signal=ws://localhost:8091/ws` on BOTH windows exercises the identical client code. The
    server is also the best non-perturbing STATE ORACLE: `GET /health` (`rooms`/`listed`/
    `browsers`) tells you the host listed and the joiner reached the carousel without touching a
    window, and a one-shot `{t:browse}` client prints the live room code.
  - **JIP pass trap 5 -- pick a host fight that does not END.** `?level=Level2&flyspiders` (the
    endless swarm) is ideal; a plain `?level=Level2&aiplayer` host finished the level on its own
    partway through one run (how fast depends on difficulty, AI and RNG), at which point the scene
    goes down, `NetListing` drops the room, and the joiner's carousel correctly falls back to
    "Searching for open games..." mid-test.
  - **JIP pass recipe:** host `?level=Level2&flyspiders&netjip&aiplayer&invuln&binlog&signal=...`,
    joiner `?signal=...&noattract&binlog&netlog` -> menu -> Online Co-op -> Join Online Game ->
    pick the room. **Pass looks like:** `session start role=host ... (join-in-progress)` +
    `... role=join ... (menu lobby)`, `granted joiner primary slot=1`, **mirror-image rosters**
    (`0:Keyboard*,1:Remote` `pri=0/1` vs `0:Remote,1:Keyboard*` `pri=1/0`), `localShip=1
    remoteShip=1` and `buf=` ~100ms BOTH sides, `drop`/`sgap`/`ordViol`/`seqGap`/`extrap` 0,
    **zero `[bin] purge-filter diverted`**, and identical `eaNetBg()` state lines.
  - **JIP pass trap 6 -- `pupPops`/`snapUnk` from this rig were UNREADABLE until card 48ab9b2f,
    and the two traps that made them so are still live.** The first pass logged `pupPops 207` /
    `snapUnk 344` over ~25s and could conclude nothing. (a) `snapUnk` was one counter for three
    unrelated causes -- now split into `snapNew`/`snapDead`/`snapBad`, and note the old "judge it
    against `clTx`" rule was simply wrong (see the metrics bullet). (b) `?flyspiders` looks like a
    dense-swarm explanation for the pops but is NOT one: `--population` shows that swarm logging
    0 pops/s across the whole range bar one far-off resonance, and the rig's live count measures
    only 17-19 (`snapTurn` at its 60ms floor) anyway. What DOES produce hundreds is a client
    ticking at ~1Hz -- i.e. trap 1 (an occluded window) intermittently biting, which the rig
    cannot rule out after the fact. So on a re-measure: read `snapTurn` alongside `pupPops`, keep
    both windows genuinely visible, and treat a pop rate from a run whose tick rate you did not
    watch as no evidence at all.
  - **Known JIP gaps -> follow-up cards (`plans/net-game-browser-followups.md`):** mechanical-friend
    ships unreplicated (listing refused while `Friends>0`); a mid-boss arrival hits the
    best-effort puppet limit; public-list abuse surface (rate limiting / hiding a room). (The
    deep mid-level background/doodad gap is largely closed -- see the catch-up bullet below --
    but a RESIDUAL piece remains: the whole-scene setters `SetSpace`/`SetMars`/`SetAlienBase`
    are Initialize-time and unhooked, yet `InsaneBossI` calls them MID-level (`GoAlienBase`/
    `GoSpace`/`GoMars`), and that level is listable. A peer joining after one of those still
    sees the scene the level started in.)
- **Deep mid-level scenery catch-up for a late joiner (card 45a4e48d):** a peer arriving
  mid-level runs its OWN scene Initialize, so it holds the level's INITIAL background + music and
  -- the script being host-only -- can never reach the beats that already fired. The host replays
  them once as ordinary reliable `NetBackgroundOp`/`EvMusic` events, so the client applies them
  through the same paths the live ops use.
  - **The seam is the `EvReady` handler, NOT `PeerConnected`** (next to the existing
    `ReplayLive()`). At pairing time a JIP joiner has no `GameScene` at all, and the Initialize
    that gives it one would clobber anything sent earlier. Being at `EvReady` also covers the
    menu-lobby launch race and the `?net=` loopback rig for free.
  - Replayed, in order (the order that matters is doodad kind before its position -- `Queue*`
    parks a doodad back at its entry point and `SetDoodadPos` then moves it to the host's; speed
    leading is readability, since `SetSpeed` only retargets a 1333ms lerp and so has NOT moved
    the `scrollspeed` that `Queue*` reads): last `SetSpeed`, last `SetAlienBaseN`,
    `EngageBeltSlowdown` if engaged, any in-flight doodad + `SetDoodadPos` (appended op 11,
    catch-up only) so the joiner picks the fly-by up MID-CROSSING, then the current song.
  - **The last-op state is latched by `Background` itself, not sniffed off the send path** --
    `NetSession.OnBackgroundOp` early-returns while no peer is connected, which for a listed
    single-player game is exactly the window whose ops must be remembered. The latches are
    `Vector2?`/`NetBackgroundOp?`: null means "the script never touched it", which is NOT the
    same as the default (before the first `SetSpeed`, `targetscrollspeed` is still zero while the
    real `scrollspeed` is whatever `SetSpace()`/`SetMars()` set -- replaying that zero would
    freeze the joiner's starfield). Cleared in `Background.Reset()`.
  - `QueueEarthSim` (holodeck) shares `QueueEarth`'s TEXTURE but has no wire op, so the doodad
    kind is tracked explicitly at queue time rather than inferred from `doodadname`; sim-earth
    sets the latch to null and is simply not replayed.
  - **Verify with `eaNetBgTest()`, not two windows:** the subject is a fly-by that moves every
    frame, so the gate is the one-tab round-trip self-test (capture the burst -> `NetTestWipe()`
    -> replay through the real client apply path -> diff the state line; prints PASS/FAIL, the
    ops it replayed, and all three lines). The state line deliberately reports the state the ops
    CONSUME (`targetscrollspeed`, the live layer-0 texture name), never the `netLast*` latches --
    printing the latches would make the round trip a tautology. It names the replayed ops because
    a leg the level never fired is simply absent, and a PASS must not be read as covering it (the
    `SetAlienBaseN` leg has no rig: `?netscript` is Level 1, whose `SetSpace` scene has no base
    layer to switch). `eaNetBg()` alone dumps the live state for a two-window comparison. Both are
    console-only; the self-test is destructive (Reset re-runs the hyperspace entry).
  - Music RATE (`SetMusicRate`, the BrainBoss HP sweep) still does NOT replicate -- it is driven
    per-tick from a client-frozen boss `Update`, so it belongs to the mid-boss puppet-fidelity
    follow-up, not here.

### Audio runtime (`SoundManager` / `eaMusic`)

- SFX/speech play on KNI `SoundEffect` (`SoundManager.Play()` returns a `SoundEffectInstance`);
  **music** is a WebAudio layer (`index.html` `eaMusic` via `Compat/MusicInterop.cs`) for seamless
  loop points, with the authored 2.5s crossfade (`MUSIC_FADE`).
- **XACT mix metadata is faithfully applied:** per-cue volume from the authored byte via the
  MonoGame logistic `SoundManager.VolToLinear` (byte 90 ≈ −12 dB; `_cfg` lists only deviating
  cues); no cross-bus trims (category gains are unity); instance limits are per-CATEGORY (SFX = 32
  concurrent FailToPlay, Speech unlimited, Music one-at-a-time); a subtle 5%-vol/~0.35-semi
  humanize is a deliberate embellishment (the bank authored none); the one RPC preset is the
  BrainBoss/Level3 music-rate sweep — `MusicInterop.SetRate` applies `2^((Pitch-50)/50)`. No
  DSP/reverb was ever authored.
- **`classic` ships in two difficulty-gated cuts:** `SoundManager.ClassicForDifficulty()` picks the
  Japanese-vocal `Songs.Classic` on Hard+ (an earned reward), else `Songs.ClassicClean`. The
  difficulty-selected challenges call the helper; the Tutorial forces clean (it locks difficulty
  for gameplay); TeamChallenge locks the menu-chosen difficulty and routes through the helper.
- **`Songs.LastSignal`** (`lastsignal.ogg`) is the end-of-level text-crawl theme in `CreditsScene`
  (played at rate 1.0). It replaced the bank's `sjaakslow` cue — both that cue and its ogg are
  gone; **don't reintroduce them.**
- Pipelines (bank cracking, loop points, external cues): `tools/audio/` — see tools/CLAUDE.md.
