# CLAUDE.md — web/EvilAliensWeb (the game + compat code)

Architecture and per-feature notes for the ported game. The root `CLAUDE.md` has workflow,
build/run, and the verification rules; `tools/CLAUDE.md` has the offline asset pipelines that
generate much of the art/audio referenced here; `Compat/Net/CLAUDE.md` has the online co-op
net layer, split out of this file so it loads only when you work under `Compat/Net/`.

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
    **It also makes such a level READ AS CLEAN, which is how Demo3 hid 12 cold decodes**
    (card e63601a4): no `COLD decode in Demo3` line was ever printed, and the
    `[loadprofile] Demo3 preload:` summary still was -- that line comes from
    `GameScene.LoadContent`'s own `BeginPreload`/`EndPreload` bracket, which runs whatever the
    manifest section holds. A level with an empty section is
    not evidence of anything -- check the `(boot)` block before believing it.
  - **The ATTRACT DEMOS capture through `?demo=<1|2|3>`** (card e63601a4). The idle menu picks
    Demo1/2/3 with an unseeded `RandomHelper.Random.Next(3)` in
    `MenuScene.mainMenu_DemoSelected`, so without the flag reaching a chosen demo is a coin
    flip. It pins the roll only -- it is NOT the off-switch of `?nodemo`/`?noattract`, which
    unwire the idle timeout so no demo runs at all. ONE demo per process (the shared content
    manager makes every later demo warm), boot `?menu&loadlog&demo=<n>` and let it idle ~20 s.
    Pinned by `tools/headless/probes/preload_demo{1,2,3}.txt`.
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
    **`ScreenshotSaver.Init` is the bracket's SECOND caller (card 2367b39c), and the only one
    that passes a label** -- `BeginWarm("stockshots")` around its twelve-asset loop, which makes
    `EndWarm` print a one-line summary instead of the decodes vanishing silently. `Warm<T>` passes
    no label (one asset per bracket has nothing to summarise), so the warm queues' output is
    unchanged. Both close in a `finally`; keep it at two callers so the claim stays greppable.
  - **The `(boot)` COLD lines that REMAIN are unreachable by any warm queue -- do not "fix" them
    by adding entries.** `QueueMenuWarm`/`QueueIdleWarm` are built in `Game1.LoadContent`, which
    `base.Initialize()` reaches only AFTER every component's own `LoadContent` has run. So
    `gfx/cursor2` (`MousePointer`) and the `gfx/splash/*` set (`SplashScene.AddSplash`, called
    from `Game1.Initialize`, and into that scene's OWN content manager besides) have all
    already decoded by the time the first queue entry is pumped. Warming them is a cache hit
    that changes nothing, including the log line. **Reducing the set means making a boot-time
    load LAZY or DEFERRED, not warming it**, which card 57555583 did to the two that were pure
    waste:
    - `SplashScene` decoded all THREE channel-flip reveals and a run shows ONE. It now rolls
      the variant in `LoadContent` (`PickFlipVariant`) and decodes only the winner. Rolling
      that early is safe because the scene's `rng` is its own `Random`, NOT
      `RandomHelper.Random` -- the card's description said otherwise and was wrong.
    - `AwardmentBlade` decoded its sheet + `menufont` at boot for a banner that only appears
      when an awardment pops. Both loads are lazy (`EnsureContent`, called at the Idle -> Enter
      transition and defensively in `Draw`), and `QueueIdleWarm` warms the sheet during the
      splash so the lazy load is a cache hit. It is the IDLE queue on purpose: the banner pops
      mid-level, it is not menu-first-frame art, and `DrainWarmQueue` is synchronous.
    The steady state on a full-splash boot is now SIX lines -- `easplashredone`,
    `uglysplash22`, ONE `-revenged*` variant, `splash/blank` (twice -- `Game1.blackPixel` and
    the splash scene's own copy, from two different content managers) and `cursor2`. Anything
    outside that is a real new gap. Pinned by `tools/headless/probes/boot_cold.txt`.
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
    level whose `LevelArt.ScreenshotPath` is non-null contributes it, deduped, and
    `SubMenuLevelChoice` resolves each entry's image through the SAME lookup instead of
    being handed a path literal (`AddEntryData(briefing, level)`).
    **Card 0d166364 made `ScreenshotPath` itself the membership list**: it returns `null` for a
    level with no bundled art, and the `HasCarouselEntry` predicate that used to spell the same
    twelve levels out a second time is gone. So a carousel level now needs its
    `AddEntry`/`AddEntryData`/`AddEntryEvent` triple in `MenuScene` and ONE `LevelArt` line.
    **The fallback moved to the three call sites, and they deliberately differ** --
    `ScreenshotSaver.BuildStockShots` skips a null, `SubMenuOnlineGames.EnsureArt` draws
    `LevelArt.DefaultScreenshotPath` SILENTLY (a listed game's level is an int off the wire from
    a stranger's build, so an unmapped or out-of-enum level is a production case, not a bug), and
    `SubMenuLevelChoice.loadScreenshots` draws it while printing `[levelart] carousel entry
    <Level> has no bundled art`. **That warning is not decoration: it is the probe's only
    signal.** A level dropped from `ScreenshotPath` now falls back to `level1empty`, which is
    already warm, so nothing decodes cold and the pre-existing `expect-not COLD` goes green on
    the very mutation it exists to catch (measured on both carousels). Never make that fallback
    quiet. **`General.ScreenshotEnabled` is NOT the membership predicate and cannot be
    made into one** -- it answers "does this level CAPTURE a live thumbnail" and returns the
    `Settings.WebcamScreenshot` opt-in (default OFF) for `WebcamAliens`, so deriving off it
    re-drops the exact asset the original bug was about.
    **Pinned by `tools/headless/probes/stockshots_warm.txt`.** Note what it has to work around:
    a `SubMenuLevelChoice` loads its art in its own `Initialize`, which runs when the submenu is
    first ADDED -- i.e. when the player opens Challenges -- so a dropped level decodes in the
    beat between the keypress and the carousel appearing, and a probe that marks its assertion
    window after the carousel is up passes on the very regression it exists to catch.
    A `?skipsplash`/`?menu` boot auto-presses Start on frame ~1, so the pump never REACHES them
    (the menu's own twelve are queued ahead) and all
    twelve decode at `Init` as before: that is the debug path, not a regression. Since card
    2367b39c `Init` brackets that loop as a LABELLED warm, so those twelve report as one
    `[loadprofile] stockshots warm: 12 textures, <n>ms decode total ... -- deliberate, not a gap`
    line instead of twelve `(boot)` COLD gaps at the top of every capture. The tail reads "not a
    COLD gap", NOT "free" -- reaching the line means they really did decode synchronously on the
    Press-Start -> menu handoff, so read the ms. On a real full-splash boot the pump warms them
    first and the bracket sees zero decodes, so nothing prints; `boot_cold.txt` is unaffected for
    a different reason again -- it never presses Start, so `Init` does not run there at all.
    `stockshots_warm.txt` asserts the count, which is what catches a dropped carousel level at
    BOOT rather than only via the carousel navigation.
  - **A splash-skipping boot is NOT a distorted measurement, and `?menu`/`?skipsplash`/
    `?autostart` deliberately do NOT drain the warm queue before auto-pressing Start (card
    cccd763a -- decided, measured, don't re-open).** The menu queue is never "skipped": whatever
    the pump did not reach, `DrainWarmQueue` decodes at `startScreen_OnFinished`, so it is fully
    warm before the menu is built on EVERY path. Draining before the auto-press would relocate
    zero work (same 24 decodes, one tick earlier) and buy no fidelity -- it would only stop the
    `stockshots warm:` line printing, which is `stockshots_warm.txt`'s boot-leg count. The
    residual difference is the IDLE queue's 24 entries running ~24 ticks behind: measured, a
    `?menu` boot's warm set is complete at **frame 27** and IDENTICAL IN MEMBERSHIP to a
    1200-frame full-splash boot's (59 entries, `eval PreloadExport`). Every probe settles at least
    150 ticks before it asserts, so nothing measures inside that transient. (A `?level=` boot defers the
    idle queue further, behind `PumpLevelWarm` -- considered and left alone; it is the known edge
    `QueueIdleWarm` already documents, and `RecordTexture` drops those warms rather than filing
    them under the level.)
  - **The player-facing half of that is real but unfixed: the mash window is ~24 ticks wide.**
    Two Enter taps inside it (skip the splash, then Press Start) put the whole remaining menu warm
    on one synchronous tick. Measured, second tap at tick 7 / 13 / 19 / 23 -> **12 / 8 / 2 / 0**
    stock shots still cold. The wall clock is the same however it is drained, the screen already
    reads "Loading", and the `[hitch]` watchdog is muted under the `(boot)` sentinel -- so what
    was missing was a number, not a fix. `DrainWarmQueue` now reports itself under `?loadlog`
    (`[loadprofile] menu warm drain: <n> assets still queued, <ms>ms`, silent when it drained
    nothing, so a full-splash boot is unchanged); add it to the `stockshots warm:` ms for the
    whole handoff. Pacing that drain one-per-tick instead is a separate card, gated on reading
    those two numbers in Chrome. **The pump coverage this all rests on is pinned by
    `tools/headless/probes/stockshots_pump.txt`** -- neither `stockshots_warm` nor `boot_cold`
    can see it (measured; see that probe's header).
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
- **A VALUE-CARRYING flag REPORTS a value it cannot use -- never swallows it** (cards 6eb8dc9e ->
  48b7c6b1 -> 4e401005, which finished the sweep; ~95 flags now do this). One helper,
  `DebugFlags.RejectFlagValue`, one wording:
  `[debug] unknown ?wallsidetile= value '4x' (expected a number > 0 and <= 32) -- ignored, staying on the shipped default`
  (`staying on <number>` once something has actually set it, e.g. a repeated
  `?wallsidetile=6&wallsidetile=4x`).
  **Adding a new value-carrying case means adding its `else` too**, and there are three rules:
  - The "staying on" clause names the setting **actually IN FORCE**, never the baked default -- a
    repeated flag (`?wallfog=0.7&wallfog=nope`) keeps the earlier valid value, and a diagnostic
    that can state the wrong condition is worse than one that states none. Pass
    `InForce(<the property>)`; the nullable overloads print `the shipped default` when no override
    stands, because most defaults live in the consuming game class and are not reachable from
    `Parse`. Where the value space is not a number, name the mechanism instead
    (`the per-tier skill row`, `the random orientation roll`, `the level's own tier`).
  - It fires only on a value the guard **cannot use at all** -- unparseable, or refused by the
    range predicate (typically a negative). An out-of-RANGE value is still CLAMPED silently
    almost everywhere; `?flyspidercount` is the one deliberate exception.
  - Pass **`key`**, not a string literal, so an aliased flag reports under the alias that was used
    (`?objscale` vs `?size` -- lower-cased, since `key` is normalised) and the message cannot
    drift from its `case` label.
  Deliberately still silent: the on/off booleans (`IsOn`/`IsExplicitlyOff` have their own
  convention) and the free-form identity strings (`?netfakepeer=`, `?netfakehash=`, `?bg=`,
  `?room=`, `?code=`, `?signal=`), where any value is legal and an empty one is not a typo class.
  **`?shake=` and `?bgfreeze=` take a number OR an on/off spelling** and report only a value that
  is neither -- reading a typo'd number as "off" was the worse bug, since it turned off the very
  effect the run was labelled as sweeping. `?pos=` reports per AXIS. Pinned by `logic_probe`'s
  `ProbeFlagRejectionSweep` + `ProbeAiFlagRejection` + `ProbeFlySpiderFlags`; the control in each
  is that a VALID value reports nothing.
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
  (`?level=Tutorial`/`ClassicAliens`/`?holotune`), `eaRipple` (`?rippletune`/`?ripplephase=`),
  `eaConnector`
  (`?level=TeamChallenge`/`?harness=connector`/`?connectortune`), `eaWcTune` (`?wctune`),
  `eaTexViewer` (`?texviewer`), `eaNetSim` (`?netsim` on a `?net=` boot, or `eaNetSim.show()`
  from the console). GOTCHA: range inputs need `autocomplete='off'` or Chrome's form restoration
  re-seeds them post-load and desyncs from the defaults.
- Console QA helpers (via `Compat/DebugInput.cs`): `eaPress`/`eaHold` (input), `eaHitboxes()`,
  `eaShake()`, `eaHitstop(ms)`, `eaSlowmo()`,
  `eaRipple.fire(x,y,power)`/`.park(phase)`/`.state()` (throw a bomb ripple on demand, park one
  at a phase for a screenshot, read the knobs -- a real bomb needs a pickup and a live ship),
  `eaPreloadExport()`, `eaWallPerf(true)`+`eaWallStats()`,
  `eaFps()`+`eaFps.stats()`/`.test()`/`.uncap()`/`.gpu()`,
  `eaNetBg()`+`eaNetBgTest()` (the JIP scenery catch-up dump + its round-trip self-test),
  `eaScore()`+`eaNetScore.test()` (per-slot score/combo dump + the co-op score-reconciliation
  self-test),
  `eaNetCombo.test()` (the co-op per-slot combo + powerup self-test — card 1a3ad45a),
  `eaNetWire.test()` (the in-process net wire + every wire-level codec round trip — card
  25ad0659; needs no session, level or second tab, and also runs under `logic_probe`),
  `eaNetHost()` (the `INetHost` seam — card 25ad0659 step 2a: the production host's 1:1 mapping
  onto the clock/flag/fingerprint reads it replaced, plus proof the injected clock really drives
  the live `NetImpairment` queue; needs no session, level or second tab, and runs under
  `logic_probe`),
  `eaNetEntity()` (the `INetEntity` seam — card 25ad0659 step 2c-ii: that every explicit forward
  on `AlienDrawableGameComponent` fronts the member it claims to, and that the
  `NetKillable`/`NetPickup` discriminants agree with the `is KillableAlien`/`is Powerup` tests
  they replaced; run from the main menu),
  `eaNetPuppetBench(n, iters)` (the pinned many-puppet drive bench — the same card's instrument:
  n real puppets, the real `NetPuppets.Drive` timed in a plain loop, in ABSOLUTE us. The FPS HUD
  cannot answer this — `Drive` runs inside `base.Update`, so it is buried in `UpdComponents`
  while `UpdNet` covers only `NetSession.Update`. Args default to 128 / 2000 when OMITTED;
  menu-only, and its first run per process reads ~24% high — prefer `eahl` and compare at
  matched run ordinals),
  `eaNetScenarios()` (the step-4 scenario harness -- card 25ad0659: the three generous-claim
  shapes, the OneUp overlap and the id-churn self-heal, each over a real session with a
  scripted wire peer; MENU-only and leave-no-trace, and it carries the residual first-wipe
  `pupPops` probe),
  `eaNetSceneOrder()` (scenario 6 of the same harness -- reset/pause/checkpoint ordering
  against a REAL GameScene, so **destructive**: run it in a throwaway `?level=Level2&invuln`
  boot),
  `eaNetResetSpawn()` (the reset/`TryAdd` ship-puppet spawn scenario — card 25ad0659; the ONE
  **destructive** suite here: it pairs a real session onto the live level, so run it in a
  throwaway `?level=Level2&invuln` boot),
  `eaNetCosmetic()` (the decorative-swarm replication self-test — card 9a3175d0; run it inside
  a level to cover the client apply leg),
  `eaBinTest()` (the ComponentBin lifecycle scenario suite — run from the main menu),
  `eaKickTest()` (the co-op kick/block rules + v6 handshake codec — best from the main menu),
  `eaSlotTest()` (the co-op primary-slot negotiation + the v8 handshake codec, plus the stale
  menu roster, `?netdropgrant`'s one-shot latch and couch-seat reuse -- leave-no-trace,
  so it is safe at any point in play),
  `eaKillShips()` (asplode the locally-owned ships to force a death/reset on demand),
  `eaAward('Pacifist')` (pop an awardment banner now -- every real trigger is minutes deep
  behind a condition a rig cannot produce; see the awardment bullet under "Feature notes"),
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
  gated on `NetScene.Current`, whose production value IS `GameScene.NetActiveScene` -- which
  `Terminate` nulls BEFORE its own purges. (Card 25ad0659 step 2c-i put the seam in front of that
  field; the production argument is unchanged and is still about the field.)
- **A caller that ADOPTS what it adds must use `ComponentBin.TryAdd`, not `Add`.** `Add` diverts
  silently -- that is the point, ordinary game code must not have to care -- but the net layer's
  ship puppets keep the reference and gate their retry on it being null, so adopting a diverted
  ship points that reference at a ship the world does not have (`NetSession.SpawnPuppet` and
  `SpawnFriend`; the couch/friend one bites more often, since couch players hit the resets that
  arm `Purge<PlayerShip>`). `TryAdd` reports whether the component actually landed; on false,
  leave the reference clear and let the retry fire next tick. Note the ship SHOULD be purged by
  a reset (`SpawnAllPlayers` respawns every seated slot), so verify-and-retry is correct here
  and exempting would be wrong.
  **The window is ONE TICK, not the session -- this line used to say "for the rest of the
  session" and that was wrong** (measured by card 25ad0659's `eaNetResetSpawn()`, whose
  faithful pre-card mutation fails exactly one assertion). `ManagePuppet` and `TickFriends` both
  open by RELEASING a puppet the oracle does not hold, and that block predates the fix (Stage
  11.1, `6f36aae`), so the pre-card bug self-heals on the next tick. Keep the guard -- the
  release is a safety net, not the intended path -- but do not re-inflate the severity, and do
  not write a test that expects the broken code to stay broken past one tick.
- **Wire-driven banners are NOT exempt, deliberately** (card 74403f83). `NetSession`'s
  `EvMessage` add can be eaten by a standing `Purge<AnimatedMessage>`, and that
  MATCHES the host: the level script is host-only and only runs in `GameState.Normal`, so the
  host cannot emit a beat while it is itself in Win or Resetting, and both peers enter those
  states from the host's own broadcast. Reaching it needs the two state machines to have already
  diverged -- a different bug, which letting the banner through would only mask. Nothing dangles
  either way (one-shot, no reference held past the `Add`). Don't "fix" it.
  (**`EvUnlock` used to be the second banner here and no longer spawns one at all** -- card
  125490d9 made the join peer a guest, so it neither grants nor announces. This bullet is about
  `EvMessage` only now.)
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
- **A mouse click SHORTER than one tick is latched, not dropped** (card 724f2abc,
  `Compat/MouseLatch.cs`). `InputHandler.Update` polls `Mouse.GetState()` once per tick and
  edge-detects, so a mousedown/mouseup pair landing entirely BETWEEN two polls was never seen and
  `Pressed(MyKeys.Mouse1)` never fired -- while the cursor POSITION survived it. **So a menu row
  that hover-highlights on click and never invokes is THIS, not a menu bug**, and it is not
  confined to any one menu: every `Pressed(MyKeys.Mouse1)` consumer was affected
  (`MenuSub1.HandleMouse`, `StartScreen`, `SplashScene`'s skip). A human click is 50-150 ms
  (3-9 ticks at 60 Hz) and was safe; a click on a hitching frame was not, and an automated one
  never worked at all -- a CDP `left_click` holds the button for **0.9 ms** measured, so browser
  verification of ANY menu click was impossible before this. JS pushes the mousedown edge to
  `MouseLatch` and `InputHandler` ORs it into `held` for one tick -- the `DebugInput.Consume(i)`
  shape, one line further up the same loop. It is a FLAG, not a counter: two clicks inside one
  tick collapse to one rather than injecting a phantom on a later frame.
  - **TWO scopings, both narrowing it to exactly the input being restored; keep both.** The
    listener is on the CANVAS -- a `window` one would also fire a game press for every click on
    the outside-`#app` UI (fullscreen button, touch D-pad, FPS HUD, tuning panels), exactly the
    shots-eaten-by-an-overlay problem `pointer-events:none` exists to prevent, and it would fire
    on the very panel you clicked to diagnose it. And it is `pointerdown` filtered to
    `pointerType === 'mouse'` -- a touch tap synthesises a back-to-back mousedown/mouseup, so it
    is a sub-tick click BY CONSTRUCTION and an ungated latch would newly fire the ship (and invoke
    menu rows) on every tap of the canvas. **Touch deliberately gets NO new behaviour here**; it
    has its own overlay (`#ea-touch` -> `eaHold`). Making a tap select a menu entry is a separate,
    unverified-on-this-rig product decision, not a side effect to inherit.
  - **No headless guard is possible**: `eahl` has no DOM and SDL2 polls the same way, so a
    `--script` probe cannot tell the fixed code from the broken code. `logic_probe`'s
    `ProbeMouseLatch` is the regression guard (it drives the real `InputHandler.Update` over the
    real latch, so it covers the wiring, not just the latch); the Chrome pass is the only
    evidence about the shipped build.
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
  - **NO GAMEPAD? FAKE ONE FROM THE PAGE -- and the whole trick is the EVENT, not the override**
    (card 1cd47879). Overriding `navigator.getGamepads` alone changes NOTHING and reads as "the
    fake does not reach the code path": KNI never polls until a pad announces itself, because
    `_content/nkast.Wasm.Dom/js/Window.8.0.5.js:234` subscribes `gamepadconnected` and forwards
    `event.gamepad.index` to `nkast.Wasm.Dom`. Dispatch that event and the poll starts (the two
    `nv.getGamepads()` sites, `Navigator.8.0.5.js:17` and `Window.8.0.5.js:291`, then run every
    tick). The `GamepadEvent` constructor refuses a non-`Gamepad` object, so use a plain
    `Event('gamepadconnected')` with a `gamepad` property defined on it -- the listener reads only
    `.index`. The pad object needs `connected`, `index`, `mapping:'standard'`, 17
    `{value,pressed,touched}` buttons, 4 axes and a timestamp that ADVANCES (`nkGamepad.
    GetTimestamp` reads `gp.timestamp`). Standard mapping: **0 = A, 9 = Start**, 12-15 the d-pad.
    Verified end to end this way -- `PadConnected(0)` flips, a button-9 press fires
    `[teamchallenge] PadOne took over the auto-pilot partner seat 1`, `eaOracleRoster` reads
    `players=2 seated=0:Keyboard,1:PadOne` (re-pointed, NOT a third player), `eaScore` shows slot
    1 keeping its score and powerups, the tether survives, and holding an axis then really steers
    that ship. **What a fake CANNOT prove, and what therefore stays formally open:** that a
    physical pad enumerates over USB, and the browser quirk that a pad stays invisible until a
    real button is pressed on it -- which is the very reason the takeover hook exists.
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

- **Bomb ripple (`Compat/BombRipple.cs` + `tools/shaders/src/bombripple.fx`, card 5f38ed35).**
  A stone-in-water screen-space refraction ring radiating from every bomb detonation, applied in
  `Game1.ApplyBombRipple` immediately after `ApplyHoloSim` (same ping-pong through a private
  `rippleRT`, `BlendState.Opaque` both blits) so the wavefront refracts the finished frame,
  ghosts and scanlines included.
  - **Fired from `Blast.Initialize`, NOT from `PlayerShip.doBlast`** -- both the local bomb and a
    remote peer's (`PlayerShip.NetDoBlast`) go Setup -> `Add` -> `Initialize`, so the puppet
    ripples too with zero net plumbing. It is Draw-time only: no gameplay state, no new traffic,
    co-op determinism and the build-hash compat key untouched.
  - **Four slots** (a fifth ring evicts the oldest), each a separate `float4` uniform rather than
    a `float4[4]` array -- a plain uniform is the form MojoShader -> BlazorGL GLSL is guaranteed
    to handle. Distances are **aspect-corrected** (`Aspect` = target W/H) so the front is a circle
    on the 4:3 target; radius and width are therefore in fractions of screen HEIGHT. The
    wavefront is one sine cycle under a Gaussian envelope, so the frame is pushed out ahead of
    the crest and pulled in behind it. Amplitude decays `(1-t)^Falloff` on the C# side.
  - **Zero cost when no bomb is out** -- `BombRipple.Visible` is false and `Game1` skips the pass
    at the first branch, exactly like `HoloSim`. Rings advance on RAW Draw time, so the wave
    keeps travelling through hit-stop.
  - **Known property, not a bug: as a post pass it distorts the HUD/score where the ring reaches
    them.** The radius is bounded and the HUD sits in the corners; `?rippleradius=` limits reach.
    A pre-HUD seam would need a new hook inside `DrawInner` and is deliberately out of scope.
  - Baked defaults (`BombRipple.Default*`): amplitude 0.018, radius 0.55, duration 0.75 s, width
    0.055, falloff 1.6, rim 0.10; amplitude/radius scale mildly with the bomb's powerup level.
    Minis (asploding bullets) are OFF by default behind `?ripplemini` -- a dozen at once strobes.
  - **Verify with `?ripplephase=<0..1>`** (+ `?ripplecenter=x,y`), which parks one ring and stops
    it advancing; the effect is time-varying, so a timed live screenshot proves nothing.
    **The honest A/B is IN ONE PROCESS** -- `eahl --repl`, `eval RipplePark <p>` then `shot`
    between phases with no `step`: two separate boots of a live level do NOT land on the same
    background scroll phase, so a cross-boot pixel diff reads as a full-frame change and tells
    you nothing (this cost a false alarm). Measured that way, phase 0.3 changes pixels in a
    61-138 px band about the centre and phase 0.6 in a 168-243 px band -- centres 99 and 198 px,
    i.e. exactly `t * 0.55 * 600`.
  - **Judge it over CONTRAST.** Refraction of a flat gradient changes almost nothing: the same
    ring reads max delta 8/255 over the Mars sky and 94/255 once it reaches the rocks. A shot
    that "shows no ripple" over open sky is the physics, not a broken pass.
  - Flags: `?ripple=` (master, 0 = off) `?rippleamp= ?rippleradius= ?rippleduration=
    ?ripplewidth= ?ripplefalloff= ?ripplerim= ?ripplemini ?ripplephase= ?ripplecenter=
    ?rippletune`. All out of `DebugFlags.Active` (pure render/feel). Live panel `eaRipple`;
    console `eaRipple.fire()` / `.park()` / `.state()`. Pinned by
    `tools/headless/probes/bomb_ripple.txt` (a failed `.mgfxo` load is SILENT by construction --
    `Game1.LoadContent` swallows it -- so the probe is the only thing that would ever say so).

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
    stays representative. **A bench boot is now byte-deterministic** (card 1cd47879): a bench
    spider pins its wing-flap phase, swivel phase and tilt exactly as the sprite harness does
    (`FlyingSpider.PosePinned`), and tints from its grid slot instead of rolling, so two boots at
    the same N differ ONLY in the flag under test -- which is what makes a swarm-vs-per capture
    pair diffable instead of eyeballable. The pin REWINDS both timers rather than just skipping
    `Randomize()`: `NewFlyingSpider` recycles, so a pinned spider out of the pool would otherwise
    inherit the last one's phase. Live play (and the un-pinned `?flyspiders` stream) keeps every
    roll. The bench REPORTS the pin as `pose=pinned` on its `[flyspiders] bench:` line -- read
    back off the spiders it just added, not restated from the predicate -- and
    `tools/headless/probes/flyspider_bench.txt` asserts it, because a spider that started rolling
    again would change no behaviour and no other output, and only quietly make every capture pair
    below incomparable. Bench spiders are also forced `Collides = false`, which for the
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
    one. The harness pins the flap/swivel phase and the tilt (`FlyingSpider.PosePinned`, which the
    `?flyspidercount=` bench now shares) precisely so the two boots are the same pose and therefore
    comparable; live play keeps the randomization. **Measured on the harness** (card 1cd47879,
    `eahl`, 180 frames): the bare boot and `&flyspiderflatten=per` are BYTE-IDENTICAL -- which is
    the check that the harness really does fall back to the per-spider path under the `Swarm`
    default, since it adds no `FlyingSpiderSwarm` -- and `&flyspiderflatten=0` differs in 645
    pixels, all inside an 80x45 box on the spider, peaking at 7/channel. So the mechanism is
    confirmed and CONFINED to the wing/body overlap; "visibly" oversells it at this pose and on
    this background.
    **The swarm variant preserves the per-spider silhouette, and that is now measured, not
    argued** (same card, `?level=Level2&flyspiders&flyspidercount=N`, swarm vs per): **N=1
    byte-identical** (the union of one box IS that box), **N=4** -- the largest grid that does not
    overlap -- 5 pixels differing by at most 1/765 summed, i.e. RT rounding. It differs only where
    two SPIDERS overlap, and there it removes double-brightening rather than changing shape: over
    the fog band at N=40 the peak deviation from a spider-free frame runs none 57 > per 37 > swarm
    28. At alpha 0.2 over Mars dust that is not perceptible, which is what let it ship as the
    default.
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
- **Awardment banner (`AwardmentBlade`) -- pop one on demand with `eaAward('<Awardment>')`
  (`eval Award <name>` under `eahl`), card d2f746d5.** Nothing else can reach it in a test: every
  trigger is minutes deep behind a condition a rig cannot produce (Pacifist = 90 s of not firing
  on Hard+, Dunce = a 180 s spider-boss timer, the rest are level completions). Since card
  57555583 the blade's sheet + `menufont` load LAZILY on the Idle -> Enter transition, so this is
  also the only way that load gets exercised at all.
  - It **RE-LOCKS an already-unlocked awardment** before awarding, and says so on its own line --
    both `AwardAchievement` and `AwardmentBlade.Update` drop an unlocked one, so without that the
    seam does nothing on any save that has played the game. A capture taken after that line is NOT
    the untouched path. Not "in memory only": the blade's `Enter` calls `SaveThreaded()`, so the
    save ends up as it started only *because the banner runs*. **Hence the cheat gate
    (`CheckForCheats`) is tested BEFORE the re-lock** -- it is reported rather than bypassed, and
    re-locking and then bailing would drop a genuinely earned unlock on the floor.
  - **eahl's saves now live under `--saves` and ARE clean per run (card 36db5d75).** The XML
    container (`Achievements.xml`, `Settings.xml`, the screenshot `.dat`s) lands in
    `<--saves>/fs/EvilAliens/`, default a temp dir wiped at boot, alongside the b64 mirror
    `--saves` always owned.
    - **It did NOT, until that card, and the fallout is worth recognising.** `StorageDevice.Root`
      was the browser's `/eaweb_save/`, which on a desktop host is a real directory at the drive
      root that nothing wiped. Any probe booting `?unlockall` (three of the committed ones do)
      unlocks all ten awardments in memory, and the next `SaveThreaded` persisted that -- so on a
      machine that had ever run one, EVERY later run read all ten as unlocked and
      `AwardAchievement` dropped every award. It cost card 57555583 a long investigation into a
      Pacifist award that was being dropped, not missed.
    - **A pre-existing `C:\eaweb_save\` is now orphaned cruft that nothing reads.** Delete it if
      you like; leaving it changes nothing. Note the saves it holds are NOT migrated.
    - **`?unlockall` cannot poison a save any more, anywhere** -- `Achievements` and
      `Unlockables` refuse to save while it is set (`Savable.SuppressSave`). Saves already
      poisoned by the old behaviour are NOT healed retroactively, in the browser or on disk:
      unlike `Settings.Invulnerability` (whose loader forces `false`, since a `true` there can
      only be fallout), an unlocked awardment is indistinguishable from an earned one, so a
      blanket heal would erase real progress.
- **Splash channel-swap SFX:** the "I made this!" splash channel-flips the old meme into the
  revenged image (`channelflip.fx`); `SplashScene.Update` fires `PlayCue("channelswap")` once when
  the glitch starts (gated on `variantPicked`, one-shot via `flipSoundPlayed`). Autoplay caveat: the
  splash runs before any user gesture, so on a cold first load the burst may be silently dropped
  (suspended AudioContext) — **don't add a click-to-start gate to "fix" it**; the project boots
  straight through by design. The cue's owner is `tools/audio/pick_channelswap.py` (see
  tools/CLAUDE.md).
  **`?splashvariant=revenged|pure|glasses`** pins which reveal the flip lands on (card 57555583)
  -- the two portrait shots are a 5% branch each, so they are otherwise unreachable on demand for
  a screenshot, and the roll now also decides which texture is decoded at all. An unrecognised
  value is REPORTED and falls back to the random roll; null = roll as normal, so a shipped build
  is unchanged.

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
  cancelled a tiny residual swung the heading right round -- measured at ~1050 deg/s on a Level-3
  run versus ~20 deg/s on an open screen. (A whole-LEVEL figure, not a walls-only one -- card
  21bb6849; see the churn note under the completion matrix before comparing it to anything.)
  Smoothing the vector makes opposing votes cancel
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
  - **`?aireact` and `?aithreatlead` stay OUT of the table -- but NOT because they are inert.**
    That verdict (n=1 each, on the one rig where each knob happens to be inert) was retired by
    card b174b00f, and the tuning campaign it called for (card 21bb6849) confirms both have large
    authority. They are out because neither has a band that is at once **WORSE, SUBTLE and
    MONOTONE**, which is what a difficulty ladder needs. All figures: `eahl`, Very_Hard,
    `?invuln` OFF, 300 sim-s, **N=30** (the deterministic walls-only rigs N=2).
    - **`?aireact` on `?level=OwnLevel` (walls + spawners) -- deaths / VICTORIES of 30:**
      80ms **3.2 / 28** · 200ms 6.6 / 25 · **420ms (anchor) 3.8 / 25** · 500ms 7.0 / 20 ·
      550ms 8.7 / 16 · 600ms **15.1 / 0**.
      Below the anchor there is no MONOTONE direction to walk: victories are flat (28 / 25 / 25)
      and deaths go 3.2 -> 6.6 -> 3.8, i.e. the 200ms row is worse than both its neighbours. A
      ladder cannot be built on that whichever way you read it -- and 80ms, the far end, is if
      anything BETTER than the anchor (same survival, and it churns LESS: `turn` 198 vs 404
      deg/s). So the only degrading direction is a LONGER look-ahead, which models nothing
      recognisable as a novice -- and it has ~130ms of usable band before a cliff that costs the
      level outright.
    - On the deterministic **walls-only** rigs the knob moves `turn` and nothing else until it
      breaks. OwnLevel, at 80/200/300/**420**/600/800/1200/2000ms:
      88 / 86 / 166 / **229** / 647 / 919 / 948 / 944 deg/s, contacts 0 up to the anchor then
      6 / 13 / 13 / 13, and `prog 5/5 -> 4/5` from 600ms up. Level 3's grids over the same sweep:
      22 / 25 / 21 / **29** / 65 / 111 / 146 / 115 deg/s, contacts 0 throughout bar a single one
      at 2000ms. That flatness is exactly why the original Level-3 isolation saw `420 -> 80ms`
      move nothing.
    - **`?aithreatlead` on `?level=CrazyGame`** (30 homing bullets, no walls -- the ONLY rig
      where it moves anything) **-- deaths / VICTORIES of 30:**
      80ms **25.5 / 0** · 200ms 11.1 / 14 · 300ms 8.2 / 18 · 400ms 6.6 / 21 · 500ms 6.6 / 19 ·
      600ms 5.1 / 23 · **700ms (anchor) 5.5 / 23** · 900ms 6.9 / 18 · 1200ms 8.1 / 15 ·
      2000ms 8.7 / 17.
      A broad shallow interior optimum: nothing within +-200ms of the anchor is distinguishable.
      The nearest measurably-worse value is **200ms** (deaths 2x, 14 victories against 23,
      Fisher p ~ 0.03) -- a 3.5x change, one step above total collapse.
    - **And at 200ms it is INERT on every other rig**, N=30 against the anchor: SpaceDodge 12.9
      vs 12.6 deaths, Level3 6.9 vs 6.0 (prog 22.6 vs 24.2), Level1 5.1 vs 4.6 (prog 25.8 vs
      28.7) -- every range overlapping. (Add SPIDERBOSS, from b174b00f: inert there even at 80ms,
      deaths 6.2 / 6.0 / 5.8 across 80 / 700 / 2000.) So a tier row would change exactly one
      challenge level.
    - **N=6 IS NOT ENOUGH ON CRAZYGAME, AND NEITHER IS N=10.** b174b00f read the anchor at 3.8
      deaths over N=6; an N=10 pass during this campaign read it at **1.9 deaths and 10 of 10
      victories**, which made 700ms look like a sharp point optimum. N=15 (5.3) and N=30 (5.5)
      both refute that. The knob's real shape only appears at N>=30 -- treat any CrazyGame figure
      below that as a hypothesis, not a measurement. (The file-wide "under ~30% is noise" rule
      says the same thing; this rig is worse than the rule suggests.)
    - **The CrazyGame 80ms row is the durable caution** (name the rig -- the `?aireact` sweep has
      an 80ms row too, and it is the BEST row in its sweep, not the worst). It posts the lowest
      churn in the CrazyGame sweep by a wide margin (127 deg/s against 426 at the anchor) while
      dying five times as often and winning zero of 30: the bot has stopped dodging, and a bot
      that has stopped dodging is smooth. **Never read `turn` as quality without a survival
      column beside it.**
    **`contacts` still cannot see wall look-ahead on a level with enemies** -- `ClampIntoWallSpace`
    is a hard override that runs however far ahead the bot looked, so it floors the metric; the
    walls-only readings above can see it because they let `turn` carry the signal instead.
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
  A malformed value on any of the 14 is REPORTED and ignored, never swallowed, per the file-wide
  value-carrying-flag convention (see "Debug flags & tuning conventions" above; cards 48b7c6b1 +
  4e401005). The one wrinkle specific to this family: `?aiaim`/`?aifieldpx` name "the per-tier
  skill row" as the in-force setting when no override stands. Pinned by `ProbeAiFlagRejection`.
  Console: `eaAiBench()`, `eaAiBench.soak(s)`, `eaAiBench.matrix(...)`, `eaAiBench.world()`,
  `eaAiBench.reset()`. Pair
  with `?aiplayer` and `?difficulty=Very_Hard`.
- **Where it stands (card f4d1721f, Very Hard unless noted).** Spider boss 36 deaths and the
  fight never resolving -> 17 and resolving; Level 3 stalled forever at event 53/60 -> kills the
  BrainBoss; heading churn ~1050 deg/s -> 70, reversals 6.5/s -> 1.3 -- **a before/after of the
  FIX, not a rig baseline, and specifically NOT a walls-only one** (card 21bb6849: that grid
  reads 29 deg/s / 0.42 revs and no difficulty tier moves it near 70, while the whole level reads
  161; ~70 reproduces on neither rig today, so don't quote it as either). It does NOT clear a
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
    (**The Level-3 walls-only baseline is 29 deg/s, and the ~70 long quoted beside it here is NOT
    a figure this rig produces** -- card 21bb6849 chased it headlessly and could not reach 70 on
    the walls-only rig under any condition. 29 is the t=180s value and it is deterministic; four
    hypotheses died: run LENGTH (`turn` is a cumulative average, so it drifts with soak time --
    but over t=10..180s it only ranges 19-44 and never approaches 70), `?invuln` (identical, and
    this rig scores no deaths either way), coarse dt (`--fps 60/30/15` -> 29 / 25 / 31) and
    DIFFICULTY, which drives the wall scroll speed (Easy..Inzane reads 5 / 13 / 19 / **29** / 16,
    Inzane falling back because it starts dying). Meanwhile the FULL level with spawners live
    reads **161 deg/s [104-243] over N=30** -- also not 70, but the nearer of the two by far.
    So: 70 is somewhere between the two rigs and belongs to neither as measured today. **Quote 29
    for the GRID and 161 for the LEVEL; do not quote ~70 as a walls-only number.** The 7.9x above
    is a within-rig ratio and is unaffected either way.)
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

### Online co-op (net layer, Stage 11)

**Moved to [`Compat/Net/CLAUDE.md`](Compat/Net/CLAUDE.md)** — it loads automatically when you
work on files under `Compat/Net/`. Design doc: `plans/stage11-online-coop.md`.

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
