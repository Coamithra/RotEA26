# CLAUDE.md — web/EvilAliensWeb (the game + compat code)

Architecture and per-feature notes for the ported game. The root `CLAUDE.md` has workflow,
build/run, and the verification rules; `tools/CLAUDE.md` has the offline asset pipelines that
generate much of the art/audio referenced here; `Compat/Net/CLAUDE.md` has the online co-op
net layer, split out of this file so it loads only when you work under `Compat/Net/`.

## Architecture

- **Game loop is JS-driven:** `wwwroot/index.html` (`initRenderJS`/`tickJS`) →
  `Pages/Index.razor.cs` `TickDotNet()` → `new EvilAliens.Game1()`. `ContentTestGame.cs` /
  `SpikeGame.cs` are dead harnesses, safe to delete.
- **The world-dt hitch clamp (card 430494a7).** The loop is `IsFixedTimeStep=false`, and after a
  main-thread stall (GC pause, cold decode, compositor jank) KNI's `GameStrategy.Tick` hands the
  game its whole real elapsed time as ONE dt, clamped only by `MaxElapsedTime` -- 500 ms, and its
  setter *throws* below 0.5 s. The 2008 build was fixed-step and never saw a dt over ~17 ms; a
  500 ms step teleports every mover half a second of travel in one frame (360 px of wall at the
  Level-3 pre-boss scroll -- the reported "brief stutter where a different set of walls is
  shown", reproduced and bounded with eahl's `stepdt 500`). `Game1.UpdateCore` therefore clamps
  the world dt to **`DefaultMaxWorldDtMs` (100 ms)** before anything consumes it, so a hitch
  costs LOST GAME TIME (KNI zeroes its accumulator per tick; the remainder is dropped) instead
  of a teleport. Things to know before touching it:
  - **Net sessions are exempt** (`ClampedWorldDtTicks`'s `netActive` arm): the co-op dead
    reckoning assumes both worlds track real time, and a host that quietly loses time after its
    own hitch produces exactly the backward corrections card 68f62e92 measured on a host stall.
    Same condition, and the same reason, as the `?aiff` exclusion beside it.
  - **The LEADING tick of a run over the 100 ms default prints a `[maxdt]` line, clamped or
    passed** -- the `?netstaleguard` rule (the flag changes the drag, never the measurement),
    with the `[hitch]` watchdog's edge detection: a backgrounded tab hands KNI's full 500 ms
    EVERY tick, so per-tick logging would print once a second forever. The delivered ms is
    read back OFF the reassigned `gameTime`, never restated from the local, so a mutant that
    prints without applying names the raw number and fails the probe (the card d44a49a4 "off
    an argument that reached a draw call" rule). Don't reformat the line -- the probe pair
    greps it.
  - **`eval WorldClock` CANNOT verify this** -- `WorldTime.Advance` caps its own dt at 0.1 s
    (card d79a2f48 gave cosmetic phases the same protection), so the world CLOCK reads 0.100
    for a 500 ms tick on the fixed and the broken build alike; an earlier cut of the probe had
    exactly that vacuous leg. Positions are what teleported, and the clamp ARITHMETIC is pinned
    by `logic_probe`'s `ProbeMaxWorldDt` against the real `Game1.ClampedWorldDtTicks`.
  - `?maxdt=<ms>` overrides, `0` = off (the deliberate bug reproduction, IN `Active` when
    overridden); Draw-side raw-dt cosmetics (bomb ripple, the slowmo-trail ease) deliberately
    keep the unclamped frame dt. Rig: eahl `stepdt <ms>`; pinned by
    `tools/headless/probes/maxdt_clamp.txt` + `maxdt_clamp_off.txt`.
- **Resolution = a unified presenter, not a pinned back buffer.** KNI's BlazorGL forces the back
  buffer to the browser window size (rewrites `PreferredBackBuffer` on resize — don't reintroduce a
  pinned one). `Game1.Draw` renders the whole frame into one offscreen `sceneTarget` sized to the
  window's 4:3 letterbox (`Compat/RenderScale`, capped 1440px tall) and blits it scaled+letterboxed;
  the game's `SetRenderTarget(0, null)` calls redirect there via
  `Xna3GraphicsDeviceCompat.BaseRenderTarget`. 800x600-design draws scale up via
  `RenderScale.Matrix` at the `SpriteBatchWrapper` Begin choke; bloom + offscreen
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
  the menu frame lines). RenderTargets are never padded.
  **That parenthesis was FALSE for two of them until card b7e9b106, and the way it failed is the
  reason `tools/audit_unclamped_draw.py` now lints the shape** (run it after adding any raw
  `spriteBatch.Draw` to the wrapper; `--selftest` pins the rule). `SealAlpha` and the
  `DrawPresent(texture, dest, color)` overload used the un-clamped
  `Draw(texture, dest, color)` form, so they stretched the whole PADDED canvas over `dest`. With
  `blank` (a 10x10 white pixel in a 112x112 `--padtest` canvas) that covers ~1/8 of the
  destination — which silently killed the DEATH CROSS-FADE: `SealAlpha` sealed only the top-left
  ~100x75 px of the 800x600 snapshot, the straight-alpha dissolve had no alpha to blend with
  anywhere else, and dying objects stayed fully solid for the whole 1.5 s and then vanished at
  the purge. **Note the failure shape** — the XFade timer, the `NonPremultiplied` blend state and
  the ramping tint alpha were all provably correct, so every intermediate reads healthy and only
  the composited result is wrong. Pinned by `tools/headless/probes/death_fade.txt`, which reads
  the `[xfade] seal ... src=` line `SealAlpha` prints on its first draw (derived from the rect it
  actually hands `Draw`, not restated beside it).
  **`DrawEffect` is the ONE deliberate un-clamped draw** and must stay that way: it is the
  `ContentScale` contract below, where the shader maps into the content itself, so clamping
  would double-correct. The audit exempts a batch begun with a custom effect — the NEAREST
  preceding `Begin`, and only when its effect argument is a bare identifier, since
  `_beginDrawing` passes `effectHandler.CurrentEffect`, which is textually non-null but null on
  the ordinary sprite path. A content-extent
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
- **`?seed=<n>` -- the reproducible-world flag** (card d937c721). Seeds `RandomHelper`, the
  gameplay RNG, at parse time (`RandomHelper.Reseed`); null => `new Random()` as shipped. It is
  what makes a gameplay-level eahl A/B measure the change instead of the divergence: unseeded,
  two runs of `?level=OwnLevel&noattract` differ by mean |diff| 0.2 / **MAX 210** of 255, which
  is more than most effects under test. **Near-deterministic, not deterministic -- a same-seed run
  lands in one of a handful of discrete worlds, and the count tracks machine load**, so capture
  each side of an A/B twice and require the same-side pair to match (the residual is eahl's boot
  `Tick`, not the RNG; `tools/headless/README.md` -> "Reproducibility"). Two more things to know
  before leaning on it:
  - **It reaches `RandomHelper` ONLY.** `Quad.fxr`, `ShipConnector.fxr`, `Juice.rng` and
    `SplashScene.rng` are separate instances *by design* -- a cosmetic draw must not advance the
    gameplay stream -- and stay unseeded, so a rig showing a laser, the connector, a shake or the
    splash keeps some jitter of its own.
  - **Deliberately OUT of `Active`**, unlike most world-affecting flags. It hijacks nothing (a
    seeded boot is a normal winnable level, the `?difficulty=` precedent), and `Active` refuses
    online play -- which would forbid exactly the two-peer netplay captures the flag exists for.
    It cannot desync a session either: co-op is distributed-authority replication, NOT lockstep,
    so the peers already run two different unseeded streams today. The mitigation for staying out
    is that a seeded boot prints its own `[debug] ?seed=` line regardless of the `Active` dump.
  Pinned by `logic_probe`'s `ProbeSeedFlag` (the claim is a SEQUENCE, so it cannot be a picture).
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
  `eaShake()` + `eaShake.state()` (fire a shake burst; read the PEAK offset/roll/zoom sampled
  since the last call -- the only honest observable for an effect re-rolled every tick and applied
  at the present blit, card 085ebddc), `eaHitstop(ms)`, `eaSlowmo()`,
  `eaRipple.fire(x,y,power)`/`.park(phase)`/`.state()` (throw a bomb ripple on demand, park one
  at a phase for a screenshot, read the knobs -- a real bomb needs a pickup and a live ship),
  `eaMouseState()` (where the cursor resolved to last tick and where it came from -- the only
  observable for a parked cursor, and under `eahl` for the suppressed physical mouse),
  `eaPreloadExport()`, `eaWallPerf(true)`+`eaWallStats()`,
  `eaFps()`+`eaFps.stats()`/`.test()`/`.uncap()`/`.gpu()`,
  `eaNetBg()`+`eaNetBgTest()` (the JIP scenery catch-up dump + its round-trip self-test),
  `eaScore()`+`eaNetScore.test()` (per-slot score/combo dump + the one-writer-per-slot score
  policy self-test, card af96bcc2 -- the superseded max() ratchet and two-writer settle run as
  its negative controls),
  `eaNetCombo.test()` (the co-op per-slot combo + powerup self-test — card 1a3ad45a),
  `eaNetPickup()` (the remote-powerup-pickup suite — cards 83271f3d / 10f9dba4 / d53431b4 /
  c5228350: the "2"/Linker arm and the connector it unblocks, the option count (now owner-
  authoritative over `MsgHudState`, incl. the join-in-progress catch-up and the downward
  reconcile), and the level-up sparkle. **Destructive** — it pairs a real host session onto the
  live level and spends real pickups into the live panels, so run it in a throwaway
  `?level=Level2&invuln` boot),
  `eaNetFire()` (the replicated shot count — cards a5c2a39b / a45b78f6: one tap used to reach the
  other peer as TWO bullets, because the wire carried firing as a LEVEL and the peer re-fired
  through its own cadence gate. The wire now carries a cumulative shot COUNT and the peer spends
  the wrapped delta. Asserts that arithmetic over the whole u8 domain, then the SENDER's counter
  against the bullets its ship really spawned, then the bullets a scripted burst spawns on a live
  remote puppet — including a burst with four of ten packets DROPPED, where the count must stay
  exact — with a reference implementation of the old firing-LEVEL rule beside it as the control.
  **Destructive** — it pairs a real host session onto the live level, fires real bullets into it
  and drives the local ship through scripted input, so run it in a throwaway
  `?level=Level2&invuln` boot),
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
  `eaNetMotion()` (anchored motion — card c1a38ef9: the sent Lazer rates and the FlyingSpider
  path anchor. Asserts the `NetPathAnchored` predicate, both descriptors' real byte layouts,
  and that a driven puppet grows/sweeps/bobs at the SENT parameters, each with the pre-card
  block beside it as the control. Every way this lane can break degrades to the pre-card build,
  which shipped and merely looks rougher — so nothing throws and no counter moves. Menu-runnable
  and leave-no-trace; whether it is actually SMOOTHER is
  `python tools/sim/net_puppet_drive_sim.py --smoothness`'s question, not this one's),
  `eaNetScriptedMotion()` (scripted motion -- card 76ec8bdb: the scripted-position bosses
  announce the velocity they are moving at, so the host stops sending a zero across a marked park
  and a whole-turn-stale difference at every phase boundary. Its ground-truth section drives the
  REAL `SpiderBoss.Update` through a full choreography cycle and finite-differences the
  displacement it actually produces, so the override cannot agree with a hand-copied expectation
  table instead of with the game. Menu-runnable and leave-no-trace; whether it is actually
  SMOOTHER is `python tools/sim/net_puppet_drive_sim.py --smoothness`'s question, not this one's),
  `eaNetWalls()` (the Level-3 wall's replication — cards 4392bd30 / 80749dc4: the base state's
  u16-at-1/256 scale is 4.9% out on a wall's tiny derived scale, which drew the joiner's grid 402px
  short of the host's and put its collision rows below its towers. Asserts that a real puppet keeps
  the scale its own Setup derived, that its collision tile IS its drawn block, and that the scroll
  is anchored — each with the wire's own (wrong) number beside it as the control. Menu-only and
  leave-no-trace; a screenshot cannot see any of it, since a mis-scaled wall looks perfectly
  ordinary on each screen taken alone),
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
  `eaNetLevelEnd.arm()`/`.check()` and `.armHost()`/`.menu()` (finishing an online co-op level
  keeps the pairing ALIVE and returns both peers to the lobby, and the remote ship flies off in
  the level's own spawn direction -- cards 3b6c12e7 / b4a9fe60. TWO CALLS with the game running
  in between, because the subject is the level's own ~7 s victory choreography plus, for the
  lobby half, the whole post-level crawl. **DESTRUCTIVE** -- they drive the live level to its
  END -- and Level-2-only, since it is the one shipped level whose `spawnType` is not the
  hard-coded South the puppets used to assume. Boot
  `?level=Level2&invuln&netallowdebug&noattract` (`&win` for the host half); pinned by
  `tools/headless/probes/net_level_end.txt` + `net_level_end_lobby.txt`),
  `eaNetLevelEnd.armLost()`/`.checkLost()` and `.armLostHost()`/`.menu()` (the same claim for a
  level you LOSE -- card c600c55a: a Mission Failed must end a co-op level the way a victory
  does, so the host can pick another. Same shape and the same boot, minus the `&win`; the wait
  is the ~14.5 s defeat choreography rather than the 7 s victory one, and the host half needs no
  script flag because its arm sets the level to its last life and asplodes the ships. Also
  **DESTRUCTIVE**. The lobby half reuses `.menu()` -- what a lobby return looks like does not
  depend on how the level ended. Pinned by `tools/headless/probes/net_level_lost.txt` +
  `net_level_lost_lobby.txt`; net CLAUDE.md has the two-changes-not-one story),
  `eaNetLevelEnd.armListed()`/`.menu()` (the same claim for the JOIN-IN-PROGRESS shape the first
  two left out -- card 51566427: a stranger who found our LISTED game in the browser and joined it
  mid-level used to be ejected the moment we finished that level together, with the host still
  sitting right there. `.armHost()`'s twin on a listed session, and it hands phase 2 to `.menu()`
  for the same reason the defeat half does. Also **DESTRUCTIVE**, same `&win` boot -- but with NO
  `?netallowdebug`, which a listed session genuinely does not need, since `HandleHello`'s debug
  refusal is menu-session-only. Pinned by `tools/headless/probes/net_level_end_listed.txt`),
  `eaNetIntroGate()` (Level 1's intro cinematic in co-op — card 8a7772d6: the replicated
  player-spawn hold and the cosmetic intro bullet volley, driven over the in-process wire against
  the REAL Level 1 script. **DESTRUCTIVE and LEVEL-1-ONLY** — it pairs real sessions onto the live
  level, ticks the real scene and spawns the local ship, so run it in a throwaway
  `?level=Level1&invuln` boot, and run it EARLY: on any other level, or after the ~10 s cutscene,
  its first precondition reports rather than asserting about nothing),
  `eaNetResetSpawn()` (the reset/`TryAdd` ship-puppet spawn scenario — card 25ad0659; the ONE
  **destructive** suite here: it pairs a real session onto the live level, so run it in a
  throwaway `?level=Level2&invuln` boot),
  `eaNetDeathFx()` (join-peer death FX — cards 4e406eba / 303bfb5b / 13aa596c / f62116b5 /
  ad9c8f8b / 1878b321: a self-detonating
  space mine goes out as `KillerSelf` instead of a silent despawn, and a deferred death
  (BattleSkull, the surviving MarsBoss) releases its frozen puppet so its own 2.5–5 s animation
  plays locally. Since ad9c8f8b it also WATCHES the four bosses' multi-phase deaths run to
  completion on the released puppet — and covers the SpiderBoss, which is not a `KillableAlien`
  and so used to stand intact for five seconds and then vanish on the joiner. Since 1878b321 it
  also covers the SpiderHelperMothership, the deferred death that stays functionally ALIVE —
  its dying mission is TRACKED frozen instead of released (a released puppet replayed its whole
  unreplicated entrance/charge/fire: "hangs around when dead"), the final EvDeath plays the
  crash impact locally, and a joiner's own kill of any deferred type files its claim at
  death-began instead of never (net CLAUDE.md has the design). MENU-only and
  leave-no-trace, but it plants real entities off-screen and really
  kills them, so it skips itself over a live session or level. Nothing it does is drawn, but it
  is not silent — the real death paths play their real cues),
  `eaNetRuler()` (the level-3 alien ruler on a joining peer -- card 5f506d11: three defects, one
  entity. Its body loop is the CLIENT'S to run (a per-type `animFrame` state extra is a
  replicated frame by another name, and staircases for the same reasons `NetFrameLocal` exists);
  a puppet released mid-death under a pause must join the pause layer, not animate through the
  freeze; and a released dying id must never be self-healed back, however long the host keeps
  streaming it. Measured, not argued -- 6 of 60 ticks in steps of 3 against the host's 20 in
  steps of 1, ~40 explosions on a frozen screen, a `Rebuilt` ghost that dies a second time.
  Menu-only and leave-no-trace; no frame can see any of the three),
  `eaNetCosmetic()` (the decorative-swarm replication self-test — card 9a3175d0; run it inside
  a level to cover the client apply leg),
  `eaNetLocalFx()` (which peer sees a presentation effect -- cards 7a8ec0d3 / a66e190a: a
  floating score is suppressed for a slot this peer does not own, and the 1up slow motion
  crosses the wire in both directions with no echo. Menu-only and leave-no-trace),
  `eaNetFx()` (the transient-feedback beats — cards 43e85936 / 57ea30cd / ee939dd1 / 8d063d33 /
  c146422f: real EvFx frames from a scripted host over a NetWire into a real client session,
  asserting the EFFECT on the live puppet. The hit blink and the detach burst are private state
  that no metric moves and no frame can be timed to — which is the same reason those effects
  needed a wire beat at all. Menu-only and leave-no-trace),
  `eaNetChargeAim()` (the enemy charge glow's AIM on a join peer -- card eb057163: a scripted host
  charges a MarsBoss puppet over a real client session while the client ticks between snapshot
  turns, and the drawn aim is sampled every tick. It reports a VERDICT, freeze vs staleness vs
  sweeping, because the two faults behind "the twin motherships do not change where they are
  aiming" need opposite fixes. Menu-only and leave-no-trace; no frame can see any of it, since a
  stepping aim and a sweeping aim are the same still picture),
  `eaNetIdReuse()` (the vanishing laser UFO — cards 9ccfe295 / 54e9a590: a client's replicated
  beam had no EMITTER, so `UFO.CollidesWith`'s `owner != this` test made a big laser UFO shoot
  itself dead on the joiner, and the `KillerNone` claim that followed deleted the host's live
  copy with no explosion at all. Asserts the emitter on the wire, the self-hit it prevents with
  the ownerless pre-card configuration beside it as the control, and that an unattributed claim
  now keeps and re-announces the entity. Menu-only and leave-no-trace),
  `eaBinTest()` (the ComponentBin lifecycle scenario suite — run from the main menu),
  `eaKickTest()` (the co-op kick/block rules + v6 handshake codec — best from the main menu),
  `eaSlotTest()` (the co-op primary-slot negotiation + the v8 handshake codec, plus the stale
  menu roster, `?netdropgrant`'s one-shot latch and couch-seat reuse -- leave-no-trace,
  so it is safe at any point in play),
  `eaKillShips()` (asplode the locally-owned ships to force a death/reset on demand),
  `eaKillShip(slot)` (asplode ONE -- the co-op case the all-at-once form cannot reach, card
  37f3a663),
  `eaRespawn.park(phase)`/`.state()` (park the respawn clock ring for a screenshot; read its
  fill/pulse/pop as data -- the pulse moves, so a frame pair cannot verify it),
  `eaRespawn.raise(slot)` (raise a real OWNED respawn summon now -- its production trigger is a
  CO-OP death, which no offline rig can produce; **DESTRUCTIVE**, and it leaves the slot with TWO
  ships because the pop spawns one over the live one -- card ed32efe1),
  `eaPowerupLevel(slot, type, level)` (set one slot's powerup level through the wire's own
  NetSetPowerupLevel -- nothing else can put a level on a slot without a live ship and a spawner
  roll; note an Option climb it triggers is not undone by setting the level back down),
  `eaNetRespawn()` (the co-op respawn-indicator suite -- card 37f3a663: the real death path's
  announcement, a puppet death announcing nothing, the peer's cosmetic copy, and that it pops
  into a blast and NOT a second ship. Menu-only and leave-no-trace),
  `eaAward('Pacifist')` (pop an awardment banner now -- every real trigger is minutes deep
  behind a condition a rig cannot produce; see the awardment bullet under "Feature notes"),
  `eaBgCull()` (the background tile-cull oracle — run from inside a level),
  `eaCast()` (which end-credits Cast member is up, what a pending advance is heading for, and
  whether the screen is done -- card 22e324d6. The advance input leaves no other trace, so it is
  the only way to tell a click that advanced the cast from one that was swallowed; reach the
  screen with `?cast`),
  `eaTeamSeat()` (TeamChallenge's partner-seat resolver over every pad-connection mask -- pure,
  so it needs neither a level nor a gamepad),
  `eaBossTrain()` (the Boss Train's checkpoint/section oracle -- **destructive**, so run it in a
  throwaway `?level=InsaneBossI` boot; see "Audio runtime"),
  `eaFlySpiders()` (the live flying-spider population split background/foreground plus the
  flatten settings in force — run from inside Level 2),
  `eaNetVelScan(on?)` (the offline audit behind the co-op teleport marker -- cards 8dabe812 ->
  e79bb994: arm it, soak a level, call again for each replicable type's fastest SUSTAINED speed
  against `NetSession.MaxObservedSpeedPxPerMs` **and how many repositions it ANNOUNCED**
  (`marked=`). Needs NO net session, since it measures the GAME's motion and the GAME's marking --
  which is exactly why it, and not the live-session diagnostic, is what a headless probe can
  assert. **REFUSES to arm inside a session**: it read-and-clears the same teleport latch, so it
  would eat the markers before the host could send them),
  `eaNetTeleport()` (the teleport marker end to end -- card e79bb994: a real HOST session's
  snapshot frames read off a `NetWire` (flag set, DECLARED velocity rather than the jump's finite
  difference, latch spent) and a real CLIENT session's puppet snapping a marked entry instead of
  blending it, each with the identical jump left UNMARKED beside it -- the pre-card code also
  ended up in the right PLACE, so position alone proves nothing. Menu-only, leave-no-trace),
  `eaNetRoster()` (dump the net roster + per-ship positions + reset counter at this instant),
  `eaOracleRoster()` (the OFFLINE roster -- works at the menu, where `eaNetRoster` refuses; its
  `aliveSlots=[..]` field is the ship-liveness readout -- a bracketed slot list, so `[0]` is slot
  0 flying and `[]` is a shipless world -- and is what a probe should assert a live-ship
  PRECONDITION on rather than `eaWorldCensus`, whose report is capped at the fourteen most
  populous types),
  `eaNetSnap()` (the world-snapshot unknown-id attribution suite -- run from the main menu),
  `eaNetCouchJoin()` (seat a couch player now, the way a gamepad Start does),
  `eaTexProbe('GFX/Base/756')` (drive the real texture load path for one asset and read the
  result as data -- see "Content-load diagnostics" above),
  `eaShotNow('arm')` then `eaShotNow('save')` (capture + persist THIS level's level-select
  thumbnail now -- card d67755d2. The real path runs only at level EXIT, behind an on-screen
  busy-ness heuristic and two timers, which put `ScreenshotSaver.SaveScreenShot` -- and the alpha
  seal in it -- out of reach of a cheap check. Needs a Draw between the two calls, and prints
  `[shot] <Level> 300x225 alphaMin=<n>` under `?loadlog`, where 255 is the pass. **DESTRUCTIVE**
  like `eaNetResetSpawn`: it overwrites the level's real saved `.dat`, so use a throwaway boot),
  `eaWorldCensus()` (SpriteBatch batches opened per frame + the live component population by
  type -- what PRODUCED the frame cost the FPS HUD only locates; arm once, let the scene settle,
  call again to read),
  `eaCollisionBench(n, iters)` (the collision broad-phase cost in absolute us PLUS its
  behaviour-neutrality check against the pre-card algorithm -- MENU-only; card 391e11d2),
  `eaBraineroidGlowBatch(on)` (flip the Braineroid glow draw between the batched driver and the
  pre-card per-brain path, for a same-frame appearance A/B),
  `eaWorldClock()` + `eaWorldClock.reset()` (the world clock every Draw-time cosmetic reads, and
  the ComponentBin freeze depth gating it -- see "Feel / post FX").

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
  `post` (slowmo trail + holo-sim), `present` (the letterbox blit), **`swap`** (`EndDraw`).
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

### Frame cost: what the profiler CANNOT tell you (`Compat/WorldCensus.cs`, card 391e11d2)

The FPS HUD answers **where** the time goes (per-phase ms). It cannot answer **why**: a frame that
costs 8 ms because 400 sprites are alive and one that costs 8 ms because 40 sprites each open their
own GL batch have identical phase rows. `eaWorldCensus()` / `eval Census true` supplies the missing
half -- **SpriteBatch batches opened per frame** (counted at `SpriteBatchWrapper._beginDrawing`, the
one place the wrapper opens a content batch) and the **live component population by type**. Arm it,
let the scene settle, call again to read; arming clears the window.

- **Batches are the number to watch, because BlazorGL's cost is per-CALL.** `_beginDrawing` flushes
  whenever the effect or blend state changes, so any per-sprite state write costs a draw call.
  Measured on the final boss: 7 batches/frame idle, **93** in the ufoz and brainz waves.
- **The two shapes that fragment a batch, and only one of them is fixable.** A component that flips
  `BlendMode` mid-draw and back (the old `Braineroid.DrawGlow`, `Explosion`) can be batched by
  hoisting the flip into a driver component -- see `BraineroidGlows`. A component drawn through
  `drawWithInterpolation` cannot: `Settings.Interpolate` is true, so every animated sprite writes
  its own `InterpOffset`/`InterpDelta`/`FadeValue` and is necessarily its own batch. That is
  ~1 call per animated enemy (58 UFOs -> 58 batches) and is **reported, not fixed** -- closing it
  means a sprite-sorting / state-batching layer in `SpriteBatchWrapper`, an architectural change
  rather than a tuning one.
- It also works under `eahl`, which is where it belongs -- a rendered browser frame rate is
  vsync-capped and cannot see any of this. See `tools/headless/README.md` -> "Profiling headlessly".

### Collision broad phase (`CollisionHandler`, card 391e11d2)

`DetectCollisions` was the single hottest phase of a busy boss wave (**43%** of the tick in the
ufoz wave, 204 collidables). Three changes, all behaviour-neutral, all pinned:

- **Entities that cannot collide are not gridded.** All three `ICollidable` implementors gate on the
  ADC's `Collides` (`ADC.DetectCollision`'s own `if (Collides)`, and `Floor`/`Floorbottom`'s
  `!(other is ADC) || ((ADC)other).Collides`), so a `Collides == false` entity can neither hit nor
  be hit -- and that is the BULK of a busy frame (up to 93 live `BloodExplosion`s on the brainz
  wave, plus `Explosion`, `MiniExplosion`, `SmokeDrawer`, `FloatingText`).
  **`CanCollide` is read ONCE per pass, and that is only sound because `Collides` is never SET from
  inside a collision callback** -- every write reachable from a `CollidesWith`/`KilledBy` is
  `= false`. **Add a `Collides = true` inside a collision callback and this hoist breaks** (that
  entity would wait a frame for its first hit).
- **Cells hold INDICES, and the candidate gather dedupes with an O(1) stamp** instead of
  `List.Contains`, which was O(k^2) per entity in a cluster -- and a boss wave is one big cluster.
  **The stamp is a MONOTONIC counter, not the resolution index.** The index looks equivalent and is
  not: after a pass `seen[j]` holds the stamp of the last entity that had j as a candidate, so the
  next pass's entity with that index reads its own stamp back and silently DROPS a real candidate.
  Caught by `eaBinTest` scenario 8; `eaCollisionBench` did NOT catch it (its grid is dense enough
  that an earlier entity overwrites the stale stamp first), so `net_selftests.txt` is the guard for
  that class.
- **`GetCollisionType()` is snapshotted once per collidable per pass.** It was evaluated up to four
  times per entity in the fill alone (once per `is` test in the dispatch chain, then again inside
  `FillCollisionMatrix*`), and every ADC evaluation runs `retrieveBoundsFromTexture` -- which now
  memoises its cell dimensions, so it is two `Vector2` writes instead of two `ConditionalWeakTable`
  lookups plus arithmetic. **The NARROW phase still recomputes**, through
  `ICollidable.DetectCollision` -- deliberate: post-card numbers show collision no longer
  dominates, so widening that signature was declined (card 391e11d2).

**Verify with `eaCollisionBench(n, iters)`** (`Compat/CollisionBench.cs`), MENU-only. Its
correctness half diffs the real pass's callback list against `ReferencePass`, a verbatim
transcription of the PRE-CARD algorithm -- the negative control, in the `eaNetScore.test` idiom. It
fails ITSELF as `FAIL vacuous` if the probe grid stops overlapping or the timed passes found
nothing, because an empty callback list matches an empty callback list. Committed as
`tools/headless/probes/collision_broadphase.txt`; mutation-tested three ways.

### BrainBoss: the hit flash and the overlay patches (card 9f90978c)

`KillableAlien.Draw` brackets `spriteBatch.lightenEffect` around `base.Draw` **only**.
`BrainBoss.Draw` calls `base.Draw` and THEN `overlays.Draw`, so the animated patches (the shipped
pair: `eye_reveal` and `pods_flicker`) sat outside the bracket and stayed unlit while the brain
under them flashed white.
`BrainBoss.Draw` now re-opens the bracket around the overlay draw. No shader work was needed --
`EffectHandler` already compiles both variants the overlay's two branches resolve to (`lighten` for
the plain one, whose tint still rides the vertex colour, and `lighten_interpolate_fade` for the
interpolated one, which already enables fade with the same tint).

**Rig: `?brainoverlayphase=<0..1>` + `?brainhitflash`** (root CLAUDE.md).
**The repeatability half of that warning is now OBSOLETE (card d79a2f48) -- two `shot`s with no
`step` between them are byte-identical**, measured, so an ordinary BrainBoss screenshot no longer
needs the park. The overlays used to advance on RAW Draw time; they advance by however far
`Compat/WorldTime` moved since the last Draw, which is zero without a step (and zero under a pause,
which is what the card was actually about). The park is still what reaches a phase the boss will
not otherwise show you -- the eye rests CLOSED and opens on a ~15 s roll.

### Braineroid glows draw as one batch per band (`BraineroidGlows`, card 391e11d2)

Each `Braineroid` used to flip `BlendMode` to Additive for its glow and back for its brain, so a
brain and its glow could never share a batch with the next braineroid's: 29 brains cost 58.6
batches. A driver component now draws every glow in one additive batch. Measured on the same rig at
20 brains: **43.8 -> 29.0 batches/frame**, i.e. ~1.84 -> ~1.10 batches per Braineroid. (The brains
themselves stay one batch each -- the interpolation-shader half above.)

- **TWO INSTANCES, ONE PER DrawOrder BAND, and that is correctness rather than tidiness.**
  `Braineroid.Initialize` puts huge/medium brains at DrawOrder 20 and **SMALL ones at 800**, so a
  small brain's glow drew above the BrainBoss, the walls and everything up to 800. A single
  low-DrawOrder driver dragged those glows under the boss, where its opaque cables hid them:
  measured 7419 px, peak 129/255, one-directional dimming. `BraineroidGlows.Bands` is the list;
  **a new Braineroid size drawing at a new DrawOrder must be added to it** or its glow silently
  changes layer.
- **Verify with `eaBraineroidGlowBatch(on)` between two `shot`s and NO `step`.** Gameplay RNG is
  unseeded by default, so two boots of a level never reach the same world state and a cross-boot
  pixel diff measures the wave, not the change (the bomb-ripple card's lesson). `?seed=<n>`
  (card d937c721) removes most of that -- but it does not make the in-process A/B above
  unnecessary: two `shot`s off ONE boot are exact,
  and the seed's residual outlier would land in a cross-boot diff. Pair it with
  `?brainoverlayphase=` or the boss overlays alone drift ~15 000 px between the two frames.

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
  **That block is `ComponentBin.PauseAdopt` since card 5f506d11, and it has a second caller**:
  `NetPuppets.ReleaseDyingPuppet` freezes a puppet it is (re-)enabling mid-pause the same way,
  instead of stepping outside the pause layer and animating a death over a stopped screen (net
  CLAUDE.md -> BODY LOOPS). The type gate stays at the `Add` site -- what counts as the pause UI
  is that call site's policy, not the helper's.
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
  - **`eaMouseAt(x, y)` is the CURSOR half, and a scripted click needs both.** `eaPress('Mouse1')`
    supplies only the button, but every mouse consumer is position-dependent -- `HandleMouse`
    hit-tests the cursor against the entry boxes, `BackTipHit` against the tip box -- and the
    position came only from `Mouse.GetState()`, which no script can move (under `eahl` it used
    to be the real DESKTOP pointer -- see the bullet below). So the whole mouse surface was
    unreachable from this seam and every
    menu click needed a real Chrome pass. `eaMouseAt` parks the pointer at a **design-space**
    (800x600) point, the same coordinates `RecordEntryHit` and `BackTipHit.Record` store, so a
    probe can read a box off a `[backtip]` line and click it. Persistent like `eaHold` (a click
    spans a press tick and a release tick); `eaMouseAt()` / `eaMouseClear()` hands the cursor
    back. `eval MouseAt <x> <y>` / `eval MouseClear` under `eahl`. A half-given or non-numeric
    position is REPORTED and refused on both sides rather than parking the cursor at NaN, where
    every hit test would silently miss (`eaMouseAt(0)` produces exactly that).
    **`InputHandler` reads it via `DebugInput.PeekScripted`, never `Consume`** -- the key loop
    does the one real consume for Mouse1, and a scripted hold is a countdown, so consuming twice
    in a tick would eat two ticks of it.
    **`PeekScripted` deliberately excludes `touchHeld`, unlike `Consume`.** That array is the
    on-screen FIRE button, so including it would let a touch player's held FIRE fire a synthetic
    Esc whenever the untouched mouse position happened to sit in the back tip's box -- shipped
    behaviour from a debug seam. Touch keeps its own BACK button and gains nothing here, the
    same line the `MouseLatch` pointerType filter draws.
  - **UNDER `eahl` THE PHYSICAL MOUSE IS SUPPRESSED, and before card 83054936 it was not --
    which silently flaked the probe suite.** KNI's SDL2 backend answers `Mouse.GetState()` from
    **`SDL_GetGlobalMouseState`** (decompiled from `Kni.Platform.dll`): the DESKTOP pointer and
    the DESKTOP button mask, with **no focus check**, minus the hidden window's origin. So a
    headless run sampled whatever the developer's hand was doing. It is the only physical input
    that gets in -- `Keyboard.GetState()` reads a key list filled from window key EVENTS and
    `eahl` never pumps the SDL event loop, so the real keyboard is inert. Two distinct failures
    came out of it, and both are worth recognising if a probe ever flakes again:
    POSITION (`HandleMouse` hover-selects on any cursor movement AND returns true, so
    `HandleInput` returns early and **swallows that tick's keypress** -- `menu_backtip.txt` failed
    **15 of 20** runs, once launching the Tutorial from one `down` off Start) and BUTTONS (a
    physically-held left button keeps `pressedAndIdle[Mouse1]` true, eating a scripted rising edge
    and making a scripted `Hold("Mouse1", false)` release a no-op -- which turned
    `net_single_tap.txt`'s two-taps-in-one-cadence-period leg into one continuous hold).
    `HeadlessHost.Boot` sets `DebugInput.SuppressPhysicalMouse` before the boot tick:
    `InputHandler` then parks the cursor at `(-1000,-1000)` design space and reads both buttons
    released. **Never set in the browser, so the shipped build is untouched**, and scripted input
    is unaffected either way (`eaMouseAt` still wins on position, `Consume`/`PeekScripted` still
    supply the buttons). Read it back with **`eaMouseState()`** / `eval MouseState`
    (`[mousestate] physical=<suppressed|live> override=<x,y|none> pos=<x,y>`) -- it changes no
    pixel, so that is its only observable; every eahl run also prints
    `[eahl] input    physical mouse suppressed`. **`eahl --real-mouse`** restores the old
    behaviour and is the mutation control `menu_backtip.txt` is pinned against. Side benefit: a
    `?level=` probe's mouse-aim is now a fixed off-screen point rather than the human's cursor.
- **Menus are mouse-selectable + clickable.** Each `DrawMenu` records the design-space box of every
  entry it draws via `MenuSub1.RecordEntryHit(index, centre, w, h)` (locked/undrawn entries
  skipped); `MenuSub1.HandleMouse()` (gated on the `normal` state) maps the cursor to hover-select
  and `MyKeys.Mouse1` to select+invoke, either resetting the attract idle timeout. **A new
  `DrawMenu` override must call `RecordEntryHit` per entry or its menu won't be clickable.** The
  level-choice carousel sets `mouseHoverSelects = false` (click picks directly). Out of scope: `PlayerSettingsMenu`.
  - **On a CAROUSEL a click on a side entry only SCROLLS to it** (card e3c78bb8,
    `mouseClickSelectsBeforeActivating`, set by `SubMenuCarousel` alongside `mouseHoverSelects`).
    Only the centred entry activates, so it is two clicks to launch a level you can see but are
    not on. The side tiles are small, half off-screen and still flying, so the default
    select-and-activate meant aiming at a moving target with a level start as the miss penalty.
    Kept as its own flag rather than folded into `mouseHoverSelects` -- that one answers "does
    HOVER select", a different question. Activation also waits on `MouseActivationSettled()`
    (the carousel overrides it to `!swaptimer.Active`): selection is instant but the scroll is
    an ANIMATION, so `hovered == selectedEntry` alone lets a quick double-click launch the tile
    that is still flying in -- exactly what the two-click rule exists to stop.
- **The bottom-left "(B) back" tip is CLICKABLE, via a synthetic Esc** (card 2a4110d0,
  `Compat/BackTipHit.cs`). It is scene chrome, not a menu entry -- drawn by
  `MenuScene.drawButtonTips`, `Darkener.drawButtons` (pause overlay) and `BragScene.drawButtons`,
  three verbatim copies of one 2008 layout -- so it records no `RecordEntryHit` box and
  `HandleMouse` cannot see it. Each drawer calls `BackTipHit.Record(left, right, top, bottom)`;
  `InputHandler.Update` consumes the box and ORs the RISING EDGE of a click inside it (never the
  button level -- a level fires on a press that began elsewhere, so a drag into the corner would
  back out and a held fire button would un-pause the frame the pause overlay drew the tip) into
  `MyKeys.Esc`. **Esc, not a
  per-scene back call, on purpose:** Esc is already "back" for every consumer, and the pause
  overlay's input belongs to `PausedScene` while `Darkener` only draws -- so one seam covers all
  three and none of their input owners is touched. The box lives exactly ONE frame (Draw records,
  the next Update spends it), so a frame that drew no tip -- i.e. gameplay -- offers a stray click
  nothing. Consequence to know: a menu bound to a PAD (`controller != Keyboard`) ignores Esc, so
  the click does nothing there; the mouse is the keyboard player's device.
  - **Invariant: the tip box must not overlap ANY menu entry box**, or one click both goes back
    and activates the row it landed on. It holds with room to spare (tip `40,534,106,36`; the
    widest main-menu frame starts at x~218) but it is held by two unrelated layouts. So every
    menu asserts it itself and prints `[backtip] menu=<T> entries=<n> tip=<x,y,w,h>
    overlap=<none|index>` on each layout change -- the POSITIVE case included, since a check that
    only printed failures would pass on a run that never opened a menu. Committed as
    `tools/headless/probes/menu_backtip.txt` (framed main menu / plain list / carousel).
  - **The click itself is HEADLESS now** -- that probe's section 2 parks the cursor on the tip
    with `eaMouseAt` and clicks it, asserting via `eaMenuCensus` that the live menu goes back
    (with the same click at a bare spot as the negative control). So the back tip needs no Chrome
    pass; before the `eaMouseAt` seam it did, because `eaPress` could not move the pointer.
- **`MyKeys.Mouse1` is a port ADDITION at every screen that takes it, and the list is short**:
  `StartScreen` (Press Start), `SplashScene` (skip), `MenuSub1.HandleMouse` (menu select), the
  back tip via its synthetic Esc, and — since card 22e324d6 — `CastDisplayer` (advance the
  end-credits Cast, i.e. asplode the current member). The 2008 build is the XBOX one and reads no
  mouse ANYWHERE, so none of these is a regression to hunt in `src_decompiled/` — a screen that
  ignores clicks was simply never given the treatment. **`CreditsScene`'s own crawl deliberately
  still does NOT take a click**: its advance key set calls `Terminate()`, so a stray click would
  skip the whole end sequence rather than step one beat. Verify a click as DATA with `eaCast()` /
  `eval Cast` on a `?cast` boot; pinned by `tools/headless/probes/cast_click.txt`.
- **The main menu shifts its row list UP when it would not fit** (card 45c16ef6,
  `MenuSubWithSkull.RowsStartY`). `curY0` keys off the FULL entry count, which unlocking does not
  change, so the list only ever grew DOWNWARD and at 8 visible rows EXIT was drawn clipped off the
  600px bottom. It now subtracts exactly the overflow past `RowsBottomLimit` (570 =
  `General.SafeZone.Bottom`): <=7 visible rows are pixel-identical to before, 8 shift up 39.5px and
  ride ~10px into the title's bottom banner, 9+ (debug menus only) keep shifting under the same one
  rule. **`DrawRows` and `GetListCentre` must both go through it** or the HUD ring parks where the
  rows no longer are.
- **A `ConfirmationMenu` lays its PROMPT and its ROWS out as one composite once they would
  collide** (card bec47239, `ConfirmationMenu.DrawMenu`). The prompt block and the entry rows were
  placed from two INDEPENDENT fixed anchors -- the prompt centred at y=240, the rows at
  `y=300+75` -- so nothing noticed when a prompt grew past the gap between them. The online co-op
  host lobby panel is 8 or 9 lines (room code + four roster seats + the call to action), and it
  drew "Start when your crew is aboard!" straight across Cancel (measured: 98px of overlap, 120px
  on the 9-line variant). `DrawMenu` now measures the prompt and, **only when the default layout
  WOULD collide**, centres prompt+gap+rows as one block inside a band (`BandTop` 48 ..
  `BandBottom` 524, which is where `MenuScene.drawButtonTips` puts the back/select tips),
  shrinking the prompt uniformly if the pair cannot fit. Every pre-existing (short) prompt takes
  the early-out and is pixel-identical to before -- the else branch IS the pre-card expression.
  - **The mouse hit boxes follow for free**: the rows are still drawn by the base
    `MenuSub1.DrawMenu`, which is what calls `RecordEntryHit`.
  - **`GetListCentre()`/`GetBelowListY()` are deliberately NOT overridden** and so keep reporting
    the un-offset layout -- pre-existing (the base class never knew about the `+75` either), and
    the only consumer is the HUD ring, which frames the whole panel rather than the rows (it lands
    at 285/292.5 against the band's centre of 286, so the relayout improves its framing).
  - **Verify with the `[confirm]` line, not a screenshot** -- "the prompt overlaps Cancel" and
    "the prompt sits above it" are both just text on a screen. It prints on CHANGE, prints the
    non-overlapping case too, and carries two numbers:
    `[confirm] lines=8 entries=2 layout=relaid overlap=none bottom=523`.
    - **BOTH ARE OBSERVED OFF THE RECORDED HIT BOXES** (`MenuSub1.TryGetFirstEntryTop` /
      `TryGetLastEntryBottom`), never re-derived from the layout formula. The first version of
      this line took the row top from the same expression that had just positioned the rows,
      which made `overlap` algebraically constant: a model that had stopped mirroring
      `MenuSub1.DrawMenu` then reproduced the reported bug ON SCREEN while the probe stayed
      green (demonstrated in review, `ListOrigin.Y` 300 -> 380). **Do not "simplify" it back to
      a formula.**
    - **`bottom=` is what says the BAND CLAMP bit.** Break the clamp and the layout is still
      non-overlapping -- the rows just draw lower, past `BandBottom` into the button tips.
      `[backtip]` cannot see that: it is a RECTANGLE test and a row centred on x=400 never meets
      the tip's x-range (40..146), so no purely vertical push trips it.
    - Pinned by `tools/headless/probes/confirm_prompt_layout.txt`, mutation-tested three ways
      (pre-card branch -> `overlap=98px`; model drift -> `overlap=56px`; clamp dropped ->
      `bottom=545`), each hitting a different assertion.
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
    on the very panel you clicked to diagnose it. **That scoping does NOT by itself keep
    off-canvas clicks out of the game, and this bullet used to imply it did** -- see the
    off-canvas bullet below; the latch is only the sub-tick rescue, and the primary poll leaked
    independently of it. And it is `pointerdown` filtered to
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
- **A click that is not ON the canvas is not game input** (card 0fe23476, `MouseLatch.Filter` +
  `SetSuppressed`). **KNI's own mouse listeners are on the WINDOW**
  (`nkast.Wasm.Dom/js/Window.8.0.5.js`: `mousemove`/`mousedown`/`mouseup`), so the button state of
  a click on ANY outside-`#app` overlay reaches `Mouse.GetState()` at that cursor position and the
  game acts on it -- the canvas scoping above only ever covered the latch. Reported case: the
  room-code prompt (`wwwroot/webrtc.js` `promptCode`) is a DOM overlay drawn over a live
  `NetStatusMenu` whose single entry is CANCEL, and the JOIN button sits on that row, so clicking
  JOIN cancelled the join. **No DOM z-order or `pointer-events` change can fix that class** -- the
  game never sees the DOM event. Same leak for the fullscreen button, the FPS HUD tag and the
  tuning panels. `index.html` now flags an off-canvas `pointerdown` (CAPTURE phase on window, so
  an overlay that stops propagation is still counted) and the buttons read released for the
  duration.
  - **It is a LEVEL released on `pointerup`/`pointercancel`, not a one-tick edge**, so a drag that
    starts on an overlay and ends over the canvas does not land either -- and `Filter` then keeps
    that button swallowed until it is PHYSICALLY released, so the tail of the refused press
    cannot read as a fresh rising edge. Per-button, since both are filtered in the same tick.
  - **A button that was ALREADY down keeps reporting held.** The flag is per-GESTURE in JS but
    applied per-BUTTON in C#, so without that carve-out right-clicking the FPS HUD while holding
    fire would stop the ship shooting mid-hold until you released and re-pressed -- a
    suppression fix breaking the input it exists to protect. Only a press that STARTS while
    suppressed is swallowed.
  - `logic_probe`'s `ProbeMouseLatch` section 3 pins all of it. **That leg cannot be checked by
    clicking in Chrome either**: the phantom-edge case needs the button held across the moment the
    flag lifts, and its evidence is the ABSENCE of an event.
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
- **An RGBA8 target that the ORIGINAL rendered as Bgr565 needs its alpha SEALED, and there are two
  of them.** The XBLIG had no alpha channel on its back buffer, so no translucent draw could erode
  one; every `NonPremultiplied` layer erodes this port's (`destA = srcA^2 + destA*(1-srcA)`), and a
  busy frame lands well under 1. It is invisible until something SAMPLES that target with alpha
  blending, and then it reads as the backdrop showing through in the shape of whatever layers drew.
  `SpriteBatchWrapper.SealAlpha` is the cure; the two callers are `Background.Draw` (the death
  cross-fade overlay) and `ScreenshotSaver.SaveScreenShot` (the level-select thumbnail, card
  d67755d2 -- measured 134..255 on a real save, whose alpha channel held a clean picture of the
  marshills parallax bands). **A new caller must pass its OWN `report` tag** -- `DrawStretched`'s
  first-draw latch is keyed per tag precisely so one caller cannot spend the line another's probe
  reads (`death_fade.txt` and `screenshot_alpha.txt` respectively).
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
- **The post-level text crawl is a true one-point PERSPECTIVE, centred, and the amount is
  CLAMPED** (card bee8f0e0's taper, reworked by card eac38cae, `CreditsScene`). Every line is
  CENTRED on x=400 and the whole crawl draws in ONE batch through a projective matrix
  (`CrawlPerspectiveMatrix` -> `SpriteBatchWrapper.BeginPerspective`, which premultiplies it
  onto `RenderScale.Matrix`): the block's edges converge symmetrically (the card's "triangle"),
  line spacing compresses toward the top, and each glyph quad keystones toward the vanishing
  point -- the "letters more slanted" half, which the old per-line uniform scale could not do.
  The on-screen scale is exactly LINEAR in screen Y (`s = 1 + skew*(screenY-300)/300` falls out
  of the `W = 1 - k*(y-300)` map), so the visible taper keeps the shape the probes always
  pinned. **DRAW-TIME ONLY** -- lines keep their nominal `textpos + i*LineSpacing` rows, so the
  scroll, the line-index math, the Cast handoff and the fade timers are untouched, and
  `?crawlskew=0` short-circuits to the original flat LEFT-ALIGNED 2008 `DrawString`s.
  - **KNI really does GPU perspective through a SpriteBatch Begin matrix** -- `SpriteEffect.
    OnApply` folds the matrix as a full 4x4 product (the M14/M24/M44 column survives) into the
    shader uniform, so the divide is per-vertex and UVs interpolate perspective-correct.
    **GOTCHA: layerDepth rides the divide too** (`z' = z/W`), so a draw at the crawl's usual
    depth 1 crosses the far clip plane wherever `W < 1` and the BOTTOM HALF of the screen
    silently vanishes -- `DrawStringPerspective` pins depth 0 for that reason. Lines whose
    nominal row sits far below the screen cross `W <= 0` (the credits tail is thousands of px
    down), so the Draw loop culls on mapped Y with a `W > 0.1` backstop.
  - **The +-20% ask still does not QUITE fit, but centring nearly closed the gap.** The widest
    line is ~661-669 of the 800 design px; centred, it grows into both margins, so the clamp
    saturates at **~0.177-0.185** (per crawl, off its own text) instead of the old left-aligned
    layout's 0.081/0.095. `DefaultCrawlSkew = 0.2f` is the ASK; read the
    `[crawl] skew=... effective=... fit=ok` line for what is drawn. Any `?crawlskew=` value is
    safe and simply saturates; if the credits text is edited, the clamp re-derives.
  - **Centring supersedes bee8f0e0's "mid-screen pixel-identical to 2008" property** -- card
    eac38cae (owner) explicitly asked for equal left/right margins, which left-aligned text
    cannot give. The 2008-identical crawl survives intact behind `?crawlskew=0`.
  - Rig: **`?creditsshot=<1|2|3>`** boots straight into the crawl for that level (the only other
    route is finishing a level, or `?level=Level2&win` -- `?win` is LEVEL-2-ONLY, see "Debug flags"),
    **`?crawlpos=<designY>`** parks the scroll so
    a taper that is a function of Y can be screenshot at all. Pinned by
    `tools/headless/probes/credits_crawl.txt` + `credits_crawl_clamp.txt` (taper present + fits;
    an over-large request saturates).
- **Verify flattened-text changes with `?textshot`** (`Compat/TextShowcaseScene.cs` — frozen
  score/combo/pop rows, plain + chrome, live animation phases), not live screenshots.

## Feel / post FX

- **The WORLD's clock: `Compat/WorldTime.cs` (card d79a2f48). A Draw in a world component reads
  `WorldTime.Seconds`, NEVER `gameTime.TotalGameTime`.** A pause is `ComponentBin.Push()` ->
  `Enabled = false`, which stops `Update` but not `Draw` (the frozen world is still drawn behind
  the pause menu), and `Game1` hands `Draw` the RAW `GameTime` -- the turbo / 1-up slow-mo /
  hit-stop rescale is on the `Update` path only. So a Draw-time animation on `TotalGameTime`
  ignores all four freezes at once, which is precisely what eleven call sites were doing.
  `Game1.UpdateScaled` advances `WorldTime` with the SCALED delta and only while
  `ComponentBin.FreezeDepth == 0`, so local pause, the net layer's remote pause, the `Guide`
  freeze, hit-stop and slow-mo are all honoured by construction rather than by each call site
  remembering to check.
  - **Prefer the shared ANIMATION classes where a component owns real per-instance state** --
    `LoadAnimation` + `curframe` (advanced in `AlienDrawableGameComponent.Update` off the scaled
    `gameTime`) for a sprite sheet, a `Timer` for a countdown. Both freeze correctly for free.
    This clock is for the stateless ambient case -- a shimmer, a hue cycle, a spin phase -- where
    a per-object accumulator would be pure overhead. **A component that needs a DELTA rather than
    a phase** (the BrainBoss overlay patches, the ship connector) keeps its own accumulator but
    feeds it `WorldTime.Seconds - lastWorldSeconds`, never `gameTime.ElapsedGameTime`.
  - **What deliberately does NOT use it, and why the list is not an oversight.** The menus
    (`MenuSub1`, `MenuSubWithSkull`, `Option`, `DifficultyMenu`, `PlayerSettingsMenu`,
    `SubMenuAwardment*`, `StartScreen`, `SplashScene`, `CastDisplayer`,
    `SpriteBatchWrapper.MetalTime`, `Game1.DrawLevelWarmIndicator`) keep real time -- they draw
    the pause menu ITSELF, or only exist outside a level, and freezing them would stop the pause
    menu's own glint and selection pulse. `WebcamLevel`'s "Step into view!" HUD prompt is in that
    class too. `BombRipple` is the one WORLD-side exception (its own bullet below).
  - **The sprite harness is unaffected**: it freezes an object with `Enabled = false`, not a pause
    layer, so the world clock keeps running there and every harness animation still plays.
  - **Verify with `eaWorldClock()` / `eval WorldClock`**, not with a screenshot pair alone -- a
    paused frame being identical also passes on a build that stopped drawing, or on a frame that
    held nothing animated. Pinned by `tools/headless/probes/pause_world_clock.txt` (runs, freezes,
    runs again; `eaWorldClock.reset()` is what makes its readings boot-independent).
  - The measurement that started it: BrainBoss paused, two frames 45 steps apart, **22 482 px
    differing outside the pause menu -> 0**, with the pause menu's own animation unchanged as the
    positive control.

- **Juice (`Compat/Juice.cs`): screen shake + hit-stop.** Shake is the trauma model
  (`Juice.AddTrauma` from explosions/blasts/player death; strength = trauma², decays ~0.7s, max
  **3.5px / 0.5° / a 0.03 blit zoom**). **HALVED TWICE**: 14/2 -> 7/1 (Trello 8e439865, full shake
  impacted gameplay rather than just adding juice), then 7/1 -> 3.5/0.5 (card 085ebddc, the owner
  asking for "a global reduction by 50% across the board").
  - **The blit's edge-covering ZOOM is one of the three and lives in `Juice.MaxBlitZoom`**, not as
    a literal at the blit -- so a later halving cannot take the offset and leave the swell behind.
    **DO NOT READ SPARE ROOM INTO IT.** Containing the destination rect inside the rotated, offset,
    scaled quad needs `Z >= A/300 + (4/3)*radians(R)` = 0.0235 at the shipped values, so 0.03 is a
    **1.28x** margin -- and the pre-card 7/1/0.06 triple had 1.281x. *The halving preserves the
    shipped safety factor exactly*; that is what makes it safe. **The roll is HALF that budget**,
    so dropping the zoom alone (to 0.02, say, which looks generous beside a 3.5px offset) puts
    black at the frame edge on every strong shake. Brute-forced over all sign choices, sixteen
    window shapes and `strength` 0.05..3.0: worst case 0.023496. `?shake=` is safe at its 3x
    ceiling because all three scale by the same `strength`, so the condition is scale-invariant.
  - **Verify with `eaShake.state()` / `eval ShakeState`, never a screenshot.** The offset and roll
    are re-rolled from a UNIFORM RANDOM every tick, so one tick is a sample and not a bound -- a
    halved build and an intact one read small on most ticks alike -- and the effect is applied at
    the present blit, so it moves no gameplay state and is by definition a moving picture. The
    seam accumulates the PEAK of what was actually sampled, never recomputed from the constants:
    **the zoom's peak is reported by `Game1`'s blit through `Juice.NoteBlitZoom`**, because a peak
    recomputed as `MaxBlitZoom * strength` in `Juice.Update` measures perfectly even on a build
    that dropped the zoom at the blit entirely (mutation-proven -- an earlier version of the probe
    stayed green through exactly that). **One burst is not enough**: trauma decays at 1.4/s, so
    re-arm to keep it pinned or the maximum lands short (measured 2.57 on one burst against 3.30
    over thirty), and the burst steps must DRAW or the zoom leg never moves. Pinned by
    `tools/headless/probes/shake_peak.txt`; four mutations, each reddening its own leg alone.
  Applied at the PRESENT BLIT only — no gameplay coordinate, collision, or mouse mapping is
  touched. An explosion
  series can opt out per instance (`Explosion.Setup(..., noShake: true)` — the L3 BattleSkull death
  does, so only its finale shakes). Hit-stop folds into `Game1.Update`'s time scale as
  `Juice.TimeScale` while REAL time keeps ticking Juice/shake/input; the per-kill micro-stop +
  boss-kill stop are **OFF by default** (read as stutter; `?hitstop=1` re-enables); player death
  keeps its 180ms stop OFFLINE. GOTCHA: hit-stop must decrement on UNSCALED dt (`Juice.Update` runs
  before the time scale) or it freezes and never thaws. **Draw-time cosmetics no longer keep
  animating during a freeze -- that line was true until card d79a2f48 and is now wrong except for
  `BombRipple`**: they read `Compat/WorldTime`, which carries the hit-stop scale like the rest of
  the world. The one deliberate exception is the bomb ripple, whose wavefront is a travelling wave
  that would read as a dropped frame if it stopped (see its bullet below). `?shake=<0..3>`, `eaShake()` / `eaShake.state()`, `eaHitstop(ms)`.
  - **ONLINE CO-OP REFUSES EVERY HIT-STOP, whatever the caller (card 68f62e92).** `AddHitStop`
    early-returns while `NetSession.Active`, so the death stop, the `?hitstop=1` kill/boss stops
    and `eaHitstop()` alike are no-ops in a session. It is a DESYNC fix, not a feel decision: a
    freeze halts that peer's whole world while the wire keeps streaming frozen positions, and the
    other peer's puppets are then corrected backward -- the mechanism, the measurement and
    `?nethitstop=1` are in `Compat/Net/CLAUDE.md`. **Shake is untouched** (present-blit only, no
    gameplay time), so a co-op death still reads as an impact.
    **The rule is "no ASYMMETRIC time scaling", not "no time scaling"** -- the 1up slow motion
    (`Oracle.SetSlowmotion`, a different mechanism entirely) DOES run in a session and since card
    a66e190a replicates as `EvSlowmo`, so both peers scale together. A hit-stop is scale ZERO on
    ONE peer; that is what makes it the banned shape.
- **Slow-motion ghost trails (`Game1.ApplySlowmoTrail`).** The 1up slowmo adds an accumulation-
  buffer motion blur on the composited+bloomed `sceneTarget` before the present blit:
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
  - **The ring FOLLOWS its blast, in location and duration (card 03c379f2).** `PlayerShip.Update`
    drags the live `Blast` with the ship every tick, so a ring parked at the detonation point was
    left behind the explosion it decorates -- and its fixed 0.75 s life ended 0.25..4.25 s before
    the blast's own `1000ms * (power+1)`. `Fire` now returns a generation TOKEN and seeds the
    ring's duration from the blast's real lifetime; `Blast.Update` pushes its live position
    through `BombRipple.MoveRing(token, pos)` -- so the local bomb, a remote peer's bomb (same
    `blast` field) and the respawn pop all follow with no per-caller code, and a stale token (ring
    evicted by a fifth bomb, pool-recycled Blast) no-ops instead of dragging someone else's ring.
    `DefaultDuration` 0.75 s is only the FALLBACK for a ring fired without a lifetime;
    `?rippleduration=` (and the slider) still overrides everything, which is what keeps the tuner
    and `bomb_ripple.txt`'s pinned expiry window valid. The `?ripplephase=` parked ring carries
    the duration a real bomb of `?ripplepower=` would (`(1+power)` s), so the scrub maps phase
    like a real detonation. Verify as DATA: `eaRipple.state()` now reports each live ring's
    centre + elapsed/resolved-duration; **`eaRipple.blast(x,y,power)` / `.blastMove(x,y)`**
    (`eval RippleBlast` / `RippleBlastMove`) spawn and drag a REAL `Blast` through the
    ComponentBin -- the wiring rig, because `eaRipple.fire` never touches `Blast` (menu or
    throwaway boot only; the blast is genuine and collides). Decision layer pinned by
    `logic_probe`'s `ProbeBombRippleFollow`, wiring by
    `tools/headless/probes/bomb_ripple_follow.txt`; a ring at the wrong place moves no counter
    and draws a plausible frame, so the probes are the only thing that would say so.
  - **Four slots** (a fifth ring evicts the oldest), each a separate `float4` uniform rather than
    a `float4[4]` array -- a plain uniform is the form MojoShader -> BlazorGL GLSL is guaranteed
    to handle. Distances are **aspect-corrected** (`Aspect` = target W/H) so the front is a circle
    on the 4:3 target; radius and width are therefore in fractions of screen HEIGHT. The
    wavefront is one sine cycle under a Gaussian envelope, so the frame is pushed out ahead of
    the crest and pulled in behind it. Amplitude decays `(1-t)^Falloff` on the C# side.
  - **Zero cost when no bomb is out** -- `BombRipple.Visible` is false and `Game1` skips the pass
    at the first branch, exactly like `HoloSim`. **Rings advance on RAW Draw time through a
    HIT-STOP but not through a PAUSE** (card d79a2f48): the freeze is a punctuation mark and a
    wave stopping dead in it reads as a dropped frame, but a 0.75 s ring left running under a
    pause expands, fades and is gone behind the menu, so unpausing resumes a bomb whose ripple
    finished while the player was reading. `Game1.ApplyBombRipple` zeroes the dt while
    `ComponentBin.FreezeDepth > 0`; everything else Draw-time is on `WorldTime` instead.
  - **Known property, not a bug: as a post pass it distorts the HUD/score where the ring reaches
    them.** The radius is bounded and the HUD sits in the corners; `?rippleradius=` limits reach.
    A pre-HUD seam would need a new hook inside `DrawInner` and is deliberately out of scope.
  - Baked defaults (`BombRipple.Default*`): amplitude 0.018, radius 0.55, duration 0.75 s (the
    no-lifetime FALLBACK -- a real ring runs on its blast's lifetime, see the follow bullet), width
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
  - **Every knob resolves per FRAME, in `PackedRings`** -- a ring stores only its centre,
    elapsed time, size scale and bomb power. So a slider drag retunes rings that are already
    travelling (three of the seven sliders looked dead when the values were baked in at
    `Fire`), and shortening the duration retires the rings in flight instead of stranding them.
  - Flags: `?ripple=` (master, 0 = off) `?rippleamp= ?rippleradius= ?rippleduration=
    ?ripplewidth= ?ripplefalloff= ?ripplerim= ?ripplemini ?ripplephase= ?ripplecenter=
    ?ripplepower= ?rippletune`. All out of `DebugFlags.Active` (pure render/feel).
    `?ripplephase=` takes a NEGATIVE value as "live" (not parked), matching the panel slider
    and `eaRipple.park(-1)`; `?ripplepower=<0..4>` gives the parked ring a bomb powerup level,
    which is the only way to screenshot a maxed bomb (1.88x amplitude, 1.72x radius).
    **`?rippletune` alone mounts the panel** -- `?ripplephase=` deliberately does NOT, or the
    270px overlay would land in the very screenshot that flag exists to take (the FPS HUD is
    suppressed on `?ripplephase=` for exactly that reason). Live panel `eaRipple`; console
    `eaRipple.fire()` / `.park()` / `.state()`. Pinned by
    `tools/headless/probes/bomb_ripple.txt` (a failed `.mgfxo` load is SILENT by construction --
    `Game1.LoadContent` swallows it -- so the probe is the only thing that would ever say so).

## Sprite harness (details)

`?harness=<Obj>` boots one object frozen on a space background, drawn by its own `Draw()` through
the real pipeline (see root CLAUDE.md for when to use it). Code: `Compat/HarnessScene.cs` +
`Compat/HarnessRegistry.cs` (name→factory; **add an object in ONE line** — call its `New*`+`Setup`).
Human picker `wwwroot/harness.html` — keep its list in sync with the registry. Companion flags:
`?frame=` `?play` `?harnessrun` `?bg=space|spaceclassic|holodeck|mars|base|basedark` `?pos=x,y`
`?objscale=` (alias `?size`) `?rot=` `?fps=` (with `?play`; set low to watch the interpolation
shader tween).

**`?harnessrun` (card d1ee8761) lifts the freeze on ANY registry key** — the object's own `Update`
runs and IT drives itself, for when that `Update` is the thing under test and no level can host it.
It replaced a per-object copy-paste (`["respawnrun"]`, a duplicate entry special-cased by name in
`HarnessScene`), and is a FLAG rather than a `<key>run` naming convention because a suffix rule
would silently collide with a future key legitimately ending in "run". **"Unfrozen" is one bit with
THREE enforcement sites** in `HarnessScene`: the initial `Enabled=false`, the defensive per-frame
re-assert, and the Update dispatch chain that re-parks `Position`/`curframe` — miss the third and
the object ticks and is then dragged back, which is the silent half. It is therefore mutually
exclusive with `?play` and with the blast/spiderjump phase scrubbers (all alternative drivers);
those are suppressed, never silently, and **the scene prints the RESOLVED mode once as
`[harness] <key>: frozen|RUNNING ...`, naming anything it overrode** — the `[debug]` dump reports
only the parse, and a run that believed it was unfrozen but was frozen produces a plausible, wrong
table (the `[aiwallnav] steering:` rule). The label shows `(RUNNING)` so a screenshot says so too.
**IT CANNOT NAME A PARK IT DOES NOT OWN, AND THAT IS THE ONE GAP.** `HarnessScene` only reports the
drivers *it* suppressed; an object that reads its OWN park flag still obeys it under `?harnessrun`
— `?respawnphase=` (`PlayerShipSummon` reads `DebugFlags.RespawnPhase` directly), and the same
shape in `?brainoverlayphase=` / `?ripplephase=`. So `?harness=respawn&harnessrun&respawnphase=0.5`
announces `RUNNING` over a fill its own flag has frozen — the announcement is honest about the
harness and silent about the object. Deliberately not fixed in `HarnessScene`: naming per-object
flags there is exactly the type-coupling this card deleted. **Drop the object's own phase flag when
you ask for a run**, and read the object's own state dump (`eval RespawnState` reports `park=`)
rather than trusting the mode line alone.
**The frozen default is unchanged and pinned**: `tools/headless/probes/harness_run.txt` +
`harness_run_absent.txt` (the summon, in numbers) and `harness_run_generic.txt` +
`harness_run_generic_absent.txt` (a `bullet`, in the world census — the only pair that catches
enforcement site 3, since a summon never moves).
Parked objects with CIRCULAR hitboxes get their real collision ring drawn (green) — sprite-vs-hitbox
size mismatches (the supersample bug class: a rescaled sheet whose hand-rolled radius forgot
`DrawScale`) are visible by eye; box hitboxes show no ring. Caveat: objects whose Draw depends on
Update-reached state show their spawned/idle pose — bosses are best-effort. Special modes:
`?harness=eyeattract` forces the JunkBoss attract sheet (`HarnessForceAttract`; try `&play&fps=2` to
prove the `interpolate.fx` frame-interpolation shader tweens); `?harness=blast` loops the blast
lifecycle (`?blastloop=` sweep speed); `?harness=respawn` shows the respawn clock ring, scrubbed
with `?respawnphase=<0..1>`, and **`&harnessrun` makes that same summon RUN** (its own `Update`
ticks, so the owned countdown reaches its pop and drops the reward blast -- the only offline rig
that can, see the respawn bullet below);
`?harness=spiderjump` loops the spider crawl→jump→land cycle; `?harness=connector` animates the
ship connector with no ships; `?harness=battleskull` shows the colorize tuner; `?harness=brainboss`
plays the boss overlays (they advance on `WorldTime`, which the harness does not freeze -- it
uses `Enabled=false`, not a pause layer -- so they still play here while a `shot` pair with no
`step` is identical); `?bulletshot` is another frozen showcase (bullets).
`?castbrain` boots the end-credits Cast screen (its own mode).

## Feature notes

- **Bomb blast (`Blast.cs`):** lifecycle math is `ApplyLifecycle(p)` (shared by live `Update` and
  the harness scrubber). Fade = `SmoothStep(1,0,p)`; collision tied to it (`Collides = fade >=
  ActiveAlpha` 0.5); hitbox radius uses `DrawScale` (supersample divided out) at
  `DefaultHitRadiusFactor` 0.8-of-visible. `?blastactive=`/`?blasthit=` override live;
  `?harness=blast` overlays the ring (green = damaging) + readout.
- **Respawn clock ring (`PlayerShipSummon.cs`, cards 37f3a663 / 045c5a92 / 258afd66).** The 2008
  respawn indicator was a
  `LazerGenerator` charge orb plus a DarkGoldenrod integer countdown; it is now a clock ring that
  fills clockwise from 12 o'clock, pulses as it nears full, and flares outward over its last 220 ms
  as the ship arrives -- dropping a **free `Blast` sized by the player's own "2" powerup** at the respawn point
  as the reward (card ed32efe1; it was a fixed level 3).
  - **CARD d44a49a4 -- four owner tweaks on top of that restyle, and one of them was not the
    one-line change it looked like.**
    - **The disc is a VEIL now** (`DiscAlpha` 0.95 -> **0.22**): *"the middle circle is pure black,
      obstructing the game - should be transparent (can be slightly darkened but very subtle)"*.
      Not 0 -- it is what the numeral reads against. Measured on `?bg=mars`, the only background
      bright enough to judge it -- against a THIRD arm built with no veil at all, because "how dark
      is it" means nothing without the undarkened reference: over the r=14..30 annulus the disc
      keeps **78.4%** of the background's luminance, against the pre-card disc's **15.5%**.
    - **...which forced the disc off the WEDGE FAN.** It was 96 rectangles widened to overlap, all
      crossing at the centre. Alpha blending is NOT idempotent -- 0.22 over 0.22 reads 0.39 -- so
      at the new alpha every overlap darkened twice and the disc drew a radial MOIRE, blackest in
      the middle. It is non-overlapping horizontal ROWS now (`DrawVeilDisc`), which is idempotent
      at any alpha, exact rather than a 96-gon, and cheaper (68 quads vs 96). Measured as the
      ANGULAR luminance spread inside the disc, which is what isolates a fan: at r=16/22/28 px the
      fan AT THE NEW ALPHA reads **7.4 / 9.9 / 10.1**, the scanlines **2.7 / 2.0 / 4.0** -- and the
      same frame with NO disc at all reads **3.4 / 2.7 / 3.8**, which is the number that matters:
      the scanline disc adds no angular structure rather than merely less of it. **Invisible over
      space**, the harness's default background -- so a bare screenshot would have passed it. The wedge fan STAYS for the round
      line caps, which draw opaque (idempotent) and would read as a staircase as scanlines.
    - **`ArcThickness` 10 -> 6** -- *"about 60% of what it is now"*, exactly. The round caps follow
      (they are `ArcThickness * 0.5`).
    - **THE WIDGET WEARS THE OWNER'S COLOUR** -- *"the color of the player who will respawn there
      (rather than pink)"*. A **hue rotation of the shipped design**, not a per-slot palette: every
      colour was tuned together off the mock (the rim sits 5 degrees off the arc core, the disc is
      a violet tint 35 degrees away), and a table of flat colours would throw that away. The anchor
      `DesignHue` is **300 because that IS slot 1's hue**, so **player 2's ring is byte-identical
      to the shipped pink** -- check that first if this ever looks wrong. Slot 0's hue is the
      **-1 sentinel** for "do not colorize", so it substitutes `UntintedShipHue` 215, the centre of
      `PlayerShip.Draw`'s own (180, 250) colorize band; passing -1 through as an angle would swing
      the ring 300 degrees to a near-identical pink, which is the one failure here that still looks
      plausible.
    - **The numeral is centred on its INK, not its line box** -- *"not nicely vertically centered
      rn. Needs to move down a bit."* `MeasureString`'s height is the font's LINE SPACING, a box
      sized for descenders a digit does not use, so centring the box left it high. DERIVED per
      string from `SpriteFont.Glyph.Cropping` (design units -- the atlas is supersampled but every
      metric is not), so it survives a font rebuild. Measured off the frame: the ink centre moves
      **y=295.5 -> 299.0** against the widget centre of 300.
    - **Verify all four as DATA on the `[respawn]` line** -- `wantHue= drawnHue= hueShift=
      discAlpha= arcPx= digitDy=`. Three of the four are exactly the kind of thing two people read
      two ways off one screenshot, and the widget pulses besides.
    - **EVERY ONE OF THOSE FIELDS COMES OFF AN ARGUMENT THAT REACHED A DRAW CALL**, and that is not
      pedantry -- two earlier cuts of this card got it wrong in two different ways and review
      demonstrated the reported defect back on screen with every probe green. Printing the CONSTANT
      restates the diagnostic's own subject (`DrawVeilDisc(r, 0.95f * popAlpha)` draws a near-opaque
      disc while the report still says 0.22); latching the expression on the line BEFORE the draw is
      no better (deleting the offset from the `DrawString` leaves the latch green). So: the alpha of
      the Color the veil was drawn with, the thickness read back out of the quad scale the arc was
      drawn with, the HUE of the Color the arc core was drawn with, and the offset derived back out
      of the position the numeral was drawn at. `wantHue` sits beside `drawnHue` for the same
      reason: one is the decision, the other is what happened, and a `Tint` that stopped rotating is
      caught by their disagreement. (The `[confirm] overlap=` tautology of card bec47239, twice
      more.) Pinned by
      `tools/headless/probes/respawn_ring_style.txt` (mutation-tested four ways) plus
      `logic_probe`'s `ProbeRespawnRingHue` for the rotation maths; the roster anchor
      (`hues=-1,300,0,39`) is pinned in the probe because `Oracle` cannot be constructed under
      `logic_probe`.
  - **Card 045c5a92 restyled it to the owner's mock** (`new_assets_raw/respawndesign.png`): a
    near-black disc, a magenta rim swept by a thick round-capped pink arc, 12 radiating spikes, a
    **whole-second countdown numeral** and an italic "RESPAWNING!" label. The numeral is the 2008
    integer countdown returning in a new form, with the owner's explicit approval; **whole seconds
    only** ("we dont need fractions of seconds there"), so the mock's decorative "2.1" is not what
    ships. **The arc still FILLS rather than drains** -- the mock is a hand-composited still, card
    37f3a663's fill-and-pop is approved shipped behaviour, and the ring reaching full is what makes
    the pop read as the arrival.
  - **The ring, disc and spikes are ~96 rotated quads of `GFX/Game/blank`** (a 10x10 OPAQUE WHITE
    texture the class already loaded, and never drew, as its own animation) plus
    `GFX/Sprites/lazerglow` for the soft halos and `menufont` for the text -- **no new asset**, no
    shader, no pipeline change. All three are in `GameScene.PreloadGraphicalContent` (every level
    override calls `base` first), so `LoadContent` is a cache hit rather than a decode at the first
    respawn. The draws go through the `SpriteBatchWrapper` overloads, which clamp the
    source to `LogicalBounds()`; a raw `SpriteBatch.Draw` would stretch the `--padtest` pad, the
    `SealAlpha` trap (card b7e9b106). Straight alpha throughout; `BlendState.AlphaBlend` appears
    nowhere.
  - **TWO SEAM RULES the restyle had to learn, and both are about overlapping quads.** Neighbouring
    arc segments must overlap or the ring seams, so (a) the bright core is drawn OPAQUE
    (`alpha = popAlpha`, the pulse living in RGB) because blending two translucent quads is not
    idempotent -- 0.72 over 0.72 reads 0.92, which drew a bright rib every few px; and (b) there is
    **no per-segment additive glow pass at all**, because additive overlap can never be idempotent
    at any alpha. Measured, that pass hatched the whole 39-45 px fringe by 70-126/255 while the
    stroke itself was flat to 1/255. The bloom comes from a radial `lazerglow` (a texture, so it
    has no seams) plus the engine's own bloom over a saturated opaque stroke. Segment quads are
    also sized at the stroke's OUTER radius, not its centre-line, or consecutive quads leave a
    `thickness/2 * step` wedge gap out there.
  - **The italic label is a real shear**, via `SpriteBatchWrapper.BeginPerspective` -- it takes an
    arbitrary design-space matrix (it exists for the credits crawl's projective one) and a shear is
    an affine member of that family. There is no italic face in the atlas and `SpriteBatch` cannot
    skew a single quad, so this is the only route.
  - **The COUNTDOWN itself is untouched.** The 1 Hz `countdowntimer`, the rumble ladder and the
    tick the ship arrives on are unchanged; `RemainingMs` is derived from them, so only the
    drawing moved. The pulse phase reads **`WorldTime.Seconds`**, so it freezes under a pause or a
    hit-stop like every other Draw-time cosmetic.
  - **THE PARK SEAM IS ON THE MILLISECONDS, and the fill is derived from THEM** (card 045c5a92
    inverted this). Everything the indicator looks like -- fill, pulse, pop, numeral, punch --
    comes off `ShownRemainingMs`, so `?respawnphase=` parks all five at once. The old shape (park
    the fill, let the ms run underneath) was invisible while the fill was the only thing drawn and
    is fatal to a numeral: `?harness=respawn` freezes `Update`, so a numeral read off the raw clock
    shows **the same digit at every phase** -- in the harness AND in the very screenshots this
    card is verified with. `respawn_digit.txt` pins it by asserting `remainMs=10000` on the same
    line as a changing `secs=`. **The live fill curve is arithmetically unchanged** and was proven
    so, not argued: a stepped unparked run through `?harness=respawn&harnessrun` produced a byte-identical
    fill column before and after (41 coarse samples over the whole clock + 260 per-frame samples
    spanning four 1 Hz wrap events -- the window the file's own `RemainingMs` comment warns about).
  - **The digit punch is a PURE FUNCTION of that clock** -- `sinceChange = 1000 - (remain mod
    1000)`, decaying over `PunchMs`. So it needs no per-instance accumulator (one less field for a
    missed `Initialize` to leak across a pool recycle -- the EvilSkull bug of card d8344c17) and it
    parks for free. **The coupling to the change is by construction**: the numeral steps exactly
    where the ms cross a whole second, which is exactly where this reads 0 elapsed. An edit that
    rounds the numeral differently silently decouples them, which is why `respawn_digit.txt`
    asserts the punch AT the boundary rather than near it.
  - **The reward blast is NOT `doBlast()`**: no bomb is spent, the BLAST powerup's own level is not
    what is read, and no `EvBlast` is sent -- in a session the far peer's own cosmetic
    summon drops its copy off its own `EvRespawn`. Reusing `EvBlast` would have raced the puppet's
    arrival (its rx handler needs a live ship in that slot, and at a respawn there may not be one).
  - **ITS LEVEL IS THE PLAYER'S OWN "2" POWERUP LEVEL (card ed32efe1), replacing the fixed 3.** The
    "2" is `Powerup.PowerupType.Linker` (`Powerup.PowerUpString` renders it as `"2"`), and it is
    already THE RESPAWN POWERUP -- `PlayerShip.PowerUp` spends its level on `respawntimebonus`,
    i.e. on this very clock's duration (2/4/7/14 s off a 15 s countdown). So the pop's size now
    scales with the same pickup that decides how long you waited for it. **Level 0 is a legal,
    small blast** and is the honest answer for a player who never invested: this is a reward, not
    a floor. It supersedes card 258afd66's constant, whose concern (a partner's death clearing the
    whole screen) is served better here -- a 4 now costs four pickups.
    - **IT IS LATCHED IN `Setup`, NOT READ AT THE POP, AND THAT ORDERING IS THE FIX.** `Update`'s
      pop `Add`s the new `PlayerShip` two lines before it calls `SpawnRewardBlast`,
      `ComponentBin.Add` runs `Initialize` synchronously, and `PlayerShip.Initialize` calls
      `Score.ResetPowerup(player)` -- which zeroes every level on that slot. A build reading the
      score at the pop measured **level 0 for a maxed "2" on every run**. `Setup` is called from
      `PlayerShip_OnDeath`, inside `Die()`, while the dying ship's progression still stands -- the
      same instant `respawntimebonus` is read off it.
    - **The COSMETIC copy needs no protocol change**: `Linker` is enum index 4 and
      `NetProtocol.HudLevelCount` is 5, so every peer's Linker level already rides `MsgHudState`
      at ~10 Hz and has long settled by the time a 10-15 s countdown pops.
    - **Rigs: `eaRespawn.raise(slot)` / `eval RespawnRaise <slot> <bonus>` and
      `eaPowerupLevel(slot, type, level)` / `eval PowerupLevel <slot> <type> <level>`.** The
      summon's production trigger is a CO-OP death (single player wipes the world and raises
      none), and `?harness=respawn&harnessrun` raises one at BOOT -- too early to be given a level
      first, which is exactly the ordering under test. `RespawnRaise` is **DESTRUCTIVE** (the pop
      spawns a real ship) and refuses an unseated slot -- the summon's own `Update` reads
      `oracle.Controller(slot)`, which THROWS on one. It also refuses a slot that already has a
      live summon (two would pop into two bombs), but it deliberately does NOT refuse a slot with
      a live SHIP: a real respawn follows a death, and killing the only ship offline makes
      `GameScene.LoseLife` wipe the world and purge the summon. So the pop leaves that slot with
      two bodies -- reported as `liveShipsOnSlot=` in the reply, and not a state to read any other
      measurement off. Pinned by the pair
      `tools/headless/probes/respawn_reward_level.txt` + `respawn_reward_level_zero.txt`; read
      them as one probe, since the max-level file alone passes on a build that returns a fixed
      maximum and the zero file alone passes on one that returns a fixed zero.
  - **SIDE-FIX: a death that WIPES the world raises no summon at all.** `PlayerShip_OnDeath` asks
    `PlayerShipSummon.ShouldSummon(otherLiveShips)` first -- and counts ships that are not
    `IsDead`, not list membership, because `Die()` only QUEUES the removal and a same-tick double
    death leaves both in the oracle's list. Before this, single player raised a summon on every
    death and `GameScene.LoseLife` purged it a tick later: one frame of countdown, which is the
    card's "looks a bit broken". A puppet dying raises none either -- that respawn is the other
    peer's.
    **A SECOND wipe shape needs a DRAW-time guard, not a spawn-time one**: in co-op the first ship
    can die while its partner is still flying (summon correctly raised) and the partner die
    immediately after -- TeamChallenge's tether does this in the same tick -- so `Draw` returns
    early while the world holds no live ship, reporting itself once. **That guard is unreachable
    from eahl's `eval KillShip*` path**, which is a property of the rig: a scripted death lands
    BETWEEN frames, so the death tick and the purge tick coalesce and the summon never reaches a
    Draw. `DebugStateLine`'s `wiped=` is the observable either way.
  - **Rigs:** `?harness=respawn` (frozen, real pipeline) + **`?respawnphase=<0..1>`** to scrub the
    fill (negative = live, the `?ripplephase=` convention; it parks the pop, the numeral and the
    punch too, since all of them are derived from the parked ms). Console `eaRespawn.park(p)` /
    `.state()` (`eval RespawnPark` /
    `RespawnState`, now reporting `secs=` and `punch=` as well). **Read the pulse and the punch as
    DATA, never from a screenshot pair** -- an identical frame
    also passes on a build that stopped drawing the ring. `eaKillShip(<slot>)` reaches the co-op
    case (`eaKillShips()` kills every locally-owned ship in one tick, which is the SUPPRESSED
    case). Pinned by `tools/headless/probes/respawn_summon.txt` (co-op positive + the wipe),
    `respawn_singleplayer.txt`, `respawn_digit.txt`, `respawn_reward_level.txt` and `logic_probe`'s
    `ProbeRespawnSummon`.
  - **`?harness=respawn&harnessrun` is the same summon with the freeze LIFTED**, and it exists
    because NOTHING ELSE OFFLINE CAN RUN AN OWNED SUMMON TO ITS POP *by itself*. (Since card
    ed32efe1 `eval RespawnRaise` can raise one on demand inside a real level, which is what a rig
    needs when the summon has to be CONFIGURED first -- see the reward-blast bullet above.) The reward blast only fires in
    co-op; the one level that seats a second local ship without a gamepad is TeamChallenge; and
    TeamChallenge is **shared-fate** (`UpdateNormal` asplodes the partner and calls `LoseLife` the
    moment either ship dies), so `GameScene.LoseLife` purges the summon within ~10 frames of it
    being raised -- measured, from either seat. That is why `respawn_summon.txt` only ever asserts
    the SPAWN lines. It needs no phase driver: the object's own `Update` is the thing under test,
    so the rig is just "do not freeze it". **This was its own registry key (`respawnrun`) until
    card d1ee8761 generalised it into `?harnessrun`, which works on every key** -- see the sprite
    harness section.
  - **The numeral is verified in BOTH modes, on two different clocks.** The owned mode (integer
    `countdown` + repeating timer) is `respawn_digit.txt`; the COSMETIC mode (a plain one-shot
    `Timer` fed a duration off the wire) is two legs in `NetRespawnTest` section 2, where
    `PeerRespawnMs` is deliberately **750** -- so a receiver that truncated would show 0 and one
    that kept fractions would not read a whole 1. That raised `net_selftests.txt`'s pinned
    assertion count 25 -> 27.
  - The netplay half -- both peers draw it, `EvRespawn`, protocol v17 -- is in
    [`Compat/Net/CLAUDE.md`](Compat/Net/CLAUDE.md).
- **The evil grinning face of death (`EvilSkull.cs`) fires in VOLLEYS, and the volley length is
  a ramp, not a bug (card d8344c17).** `shoottimer1` (5500 ms, 8000 ms once launched) rearms
  `shoottimer2` (133 ms); each beat fires one `EvilBullet` and the volley ends at
  `(int)(4 * Settings.DifficultyModifier)` beats. **The cap RAMPS with level time** -- the
  modifier climbs `+0.17 * tier` per minute to a ceiling of `2 * tier` (or 2.4 on
  Adaptive/Easy) and hard-resets to the tier floor on death, so the same enemy fires 4 early and
  8-9 six minutes later:

  | tier | volley at level start | volley at the ceiling (~6 min) |
  |---|---|---|
  | Easy, STORY levels only | 2 | **9** |
  | Easy, challenge levels | 1 | 2 |
  | Medium | 2 | 4 |
  | Hard | 3 | 6 |
  | Very_Hard | 4 | **8** |
  | Inzane | 4 | **9** |

  **The two Easy rows differ because `ApplyDifficultyPolicy` is called by `Level1/2/3` ALONE.**
  On a story level it seeds the modifier at Medium's 0.6 and sets `AdaptiveDifficulty`, which
  raises the ceiling to Inzane*2 = 2.4; the nine challenge levels instead inherit
  `GameScene.Initialize`'s `AdaptiveDifficulty = false`, so Easy there runs the bare
  `4 * 0.35 = 1` up to `2 * 0.35` -> 2. So the headline 9 is a story-level number.

  **That ramp is 2008 behaviour and was DECLINED for change by the user on this card** -- it is
  the intended balance, so do not "fix" a 9-shot volley. It is also why a bug report about this
  enemy needs a long, high-tier soak to reproduce: a short run only ever sees the short end.
  **The timer machinery itself is sound and was measured to be** -- 1236 fire events across three
  rigs and four simulated frame rates (60/30/12/6) produced no volley over the cap and no beat
  closer than 133 ms, so "some timer issue" is refuted. `Timer.Update`'s wrap loop SWALLOWS a
  big dt rather than multiplying it, so a hitch loses beats; it cannot dump them.
  - **TWO real defects were found and fixed, and both fail SILENTLY.** `bulletsfired` was the one
    piece of a pooled `EvilSkull`'s state nothing reset, so a skull killed mid-volley handed its
    count to the next skull out of the pool and truncated that one's volley (measured: 42 of 194
    volleys, 21.6%, opened part-way through) -- which is what made the length feel random. It is
    reset in `Initialize`, beside the `isdead`/`awarded`/`netTeleported` per-life block.
    Separately, the fire gate tested `fadeintimer.Active` and **not the fade OUT**, so a skull
    shot through its whole 800 ms dissolve, by the end of which `Draw` has ramped its alpha to
    zero -- bullets from a source the player cannot SEE (measured: 41 bullets, 6.1% of the
    level's skull bullets, including **7 volleys that started after the skull was already
    invisible**). Both branches now gate on **`Fading`**. Note what that predicate does and does
    not cover: `PlayerShip.CollidesWith` and both AI predicates exclude a `Fading` skull, so it
    can be neither rammed nor aimed at by the bot, but **it is still shootable** --
    `Bullet.CollidesWith` lists the type unconditionally.
  - **Fixing the counter RAISES the average bullet count** (it stops volleys being truncated) --
    intended: the complaint was unpredictability, not volume.
  - **Rig: `?skullvolley`** prints a `[skull]` line per beat (`shot=i/cap`, `fade=`,
    `shot_fired=`) and per rearm (`fired=`, which must always be 0). Console **`eaSkullVolley()`**
    / `eval SkullVolley` dumps every live skull's volley state. Nothing about a skull's
    appearance changes with its volley position, so there is nothing to screenshot.
    **The carried counter is NOT visible in the shot stream** -- it makes a truncated volley look
    like the seamless continuation of the previous skull's, and an analysis segmenting volleys by
    shot index alone reported the BROKEN build as clean during this card. That is why the rearm
    line exists. Pinned by `tools/headless/probes/evilskull_volley.txt` (both legs mutation-tested).
- **The space mine (`StarMine.cs`): the card-745728f9 report is CLOSED AS NOT REPRODUCIBLE, and
  the code structurally cannot produce it. Do not re-open it without new evidence.** Reported as
  *"space mines (lvl 3, aka death stars) seem to also explode when they reach a dead player's
  location"*. `StarMine` is Level 3's mine; `DeathStar` is `ClassicSpawner`'s, and the two share
  the `deathstarsheet2` sprite, which is where "aka death stars" comes from. **The owner's
  ruling on the evidence below was to change nothing** ("I suspect another fix may have cured
  this one also"), so what shipped is the INSTRUMENT and this write-up, not a behaviour change.
  - **THE STRUCTURAL ARGUMENT, which is the durable half.** `Asplode()` has exactly THREE call
    sites, and each anchors the detonation to a live player:
    (a) `timer.Finished` inside `attracted_to_player` -- reached only with a `target` that is
    non-null, `!IsDead`, in `GetShips()` AND inside `releaseRange` (`acquireRange * 1.08`, so
    ~270 px at the default difficulty factor), all four re-tested every tick;
    (b) a collision with an `Explosion` -- and an `Explosion` only sets `Collides` while its
    `collisiontimer` runs, which ONLY `MakeBlue()` starts, whose whole caller set is
    `StarMine.Asplode`, `WebcamMine.Asplode` (a different level) and `CastDisplayer` (the end
    credits). So a chain is recursively grounded in (a) or (c);
    (c) `NetReplayUnattributedDeath` -- a peer mirroring the host's own (a)/(b).
    **So every StarMine detonation in the game is rooted in a mine going off within ~270 px of
    a LIVE ship.** Measured median live-ship distance at detonation: **151 px** (n=250).
    A mine "exploding at a dead player's location" therefore requires a live ship AT that
    location -- which is what makes the report a co-op observation rather than a mine defect:
    in co-op the survivor is usually near where their partner just died.
  - **THE STATED BLOCKER IS GONE, and it did not help.** The card said the symptom needed two
    machines because "in single player a death wipes the world, mines included". That is a
    statement about the WIPE, not about the wire: **`?aifriends=1` gives an offline co-op world**
    where a death is survived by the AI partner and nothing is purged. Four rigs were run --
    that one, a scripted-kill loop (`eval KillShip 0`), Level 3's own dense `StarMineSpawner`
    sections, and a REAL two-process host+joiner session (`?net=jiphost` / `?net=jipjoin`, the
    `net_jip_sync.py` shape) with genuine deaths and respawns on both ends. Result:
    detonations land within 60 px of a recorded death spot **3.6% of the time against a 2.4%
    uniform-chance baseline** (pi*60^2 / 800x600), median distance ~240 px, and the rate is
    FLAT across the 0-4 s / 4-10 s / >10 s windows after a death. There is no clustering.
  - **THE ONE PATH THAT PUTS A LIVE SHIP ON A CORPSE IS THE RESPAWN, and it was measured**:
    `PlayerShip_OnDeath` raises the summon at `base.Position` and `PlayerShipSummon`'s pop calls
    `Setup(player, base.Position, ...)`, so a co-op respawn puts the ship back EXACTLY where it
    died (single player never sees this -- the wipe gets there first). It is still not the
    report: **0 of 52** detonations in the 10-18 s respawn window landed within 60 px, the
    LOWEST rate of any window, because the respawned ship is flying again long before any
    mine's 1800 ms clock finishes.
  - **FALSE POSITIVE TO RECOGNISE, because it cost this card a wrong answer.** "A mine detonated
    25 px from where slot 0 died, 1.7 s later" looks decisive and is not: the rig had slot 0 as
    a STATIONARY keyboard ship parked mid-screen where the AI partner hovers, so the mine was
    detonating on the LIVE partner. That is why the `[mine] boom` line carries `live=` beside
    `deathspot=` -- the distance to the corpse alone cannot tell "the survivor was standing on
    their partner's body" from "the mine went off with nobody there", and those need opposite
    conclusions. Any future look at this must read both fields.
  - **The `IsDead` guards are HARDENING, and the window they close is ONE TICK, not the
    1800 ms detonation clock.** `StarMine` has always been an `IComponentWatcher`, and its
    `OnComponentRemoved` nulls `target`; `Oracle` drops the ship out of `GetShips()` off the same
    `ComponentRemoved` event. `PlayerShip.Asplode` -> `Die()` queues the removal and the next
    `ComponentBin` flush fires it, so from that flush onward the PRE-CARD build already let a dead
    target go. **Measured in a real flushed world: `target` is null before the mine's next `Update`
    runs at all, with every `IsDead` clause removed** (`MineTargetTest` section 3b, which asserts
    exactly that and is designed to pass with the guards deleted).
  - **FIVE hypotheses REFUTED with evidence -- do not re-run any of them.** The three above (the
    structural argument, the four rigs, the respawn window) plus these two from the previous
    session. (a) The mine flying to a corpse
    and detonating there on the 1800 ms clock: refuted above. (b) Chain-detonation on the player's
    own death explosions -- `StarMine.CollidesWith` does `Asplode()` on any `Explosion`, but an
    `Explosion` only sets `Collides` while its `collisiontimer` runs and **only `MakeBlue()` starts
    that timer**; `PlayerShip.Asplode`'s two explosions are never made blue, so they are inert.
    Measured alongside: a freed mine keeps its inward `SpeedVector` and coasts straight through the
    death spot (200 px out -> 5 px at t=58 ticks) without detonating.
  - **What the guards ARE worth.** That one tick is real (between `Die()` and the flush a mine can
    acquire or hold a corpse), and `PlayerShip` is POOLED -- `Recycle<PlayerShip>` can hand a dead
    target's instance back out for a respawn, at which point a mine that kept the reference would
    be homing on a live ship it never acquired, on somebody else's timer.
  - **`GetShips()` UPDATES AT THE REMOVAL FLUSH, NOT AT `Die()`, and that window is the whole
    reason the acquire loop needs its own guard.** A ship that died this tick is still in the list
    with `IsDead` already true, so "in the list" is not "alive". A suite covering this must NOT
    flush between the death and the acquire, or it tests the wrong window.
  - **The two guards MASK EACH OTHER over several ticks** -- a mine locks onto the corpse on tick 1
    and the `attracted_to_player` death test drops it again on tick 2 -- so an acquire-loop
    assertion has to tick EXACTLY ONCE, or it passes on a build with the loop's guard removed
    (measured). The homing-cue REQUEST count is the leg that does not depend on the tick count at
    all: a lock that came and went still leaves the cue behind.
  - **SIDE-FIX, found by running the suite twice in one process: `Initialize` did not reset
    `target`.** `StarMine` is pooled, so a recycled mine inherited the previous one's `PlayerShip`
    reference -- possibly a corpse. `EvilSkull.bulletsfired` (card d8344c17) exactly again, and the
    reason a leave-no-trace suite is run twice rather than once.
  - **Nothing about a lock is DRAWN.** A locked mine and a free one are the same sprite, and
    whether the 1800 ms detonation clock is running is private state no frame and no counter shows
    -- so it is verified as DATA (`eaMineTarget()` / `eval MineTarget`, seams `NetLockedOn` /
    `NetTarget` / `NetDetonationClockRunning`), and one leg is deliberately asserted on the
    EXPLOSION COUNT rather than on `IsDead`: a freed mine re-attaches to the background scroll and
    can legitimately fly off screen and `Die()` on `OffScreen(100f)`, which is a mine leaving, not
    a mine exploding. `Asplode` spawns exactly two blue `Explosion`s and the fly-off spawns none.
  - **`?minelog` IS THE INSTRUMENT, and it is what a future look starts from** rather than
    re-deriving any of the above. It prints a `[mine]` line for every acquire, release and death:
    `[mine] boom id=9 at=506,374 reason=timer state=attracted_to_player target=slot1 lockMs=1817`
    `clock=- live=129.7 deathspot=slot1 d=59.5 ago=15.03`. `reason=` separates the 1800 ms clock
    from a neighbour's blue blast from a peer's replay -- three events that produce the identical
    pair of explosions. `live=` and `deathspot=` are the pair from the false-positive bullet above.
    `[mine] shipdied slot=<n> at=<x,y>` marks each recorded death.
    - **The death-spot registry is `Compat/MineLog`, NOT `Oracle`, and that is load-bearing.**
      `Oracle` keeps a per-slot cached position, but it is written by a LIVE ship's `Update`, so
      it holds a death spot only until that slot respawns -- and the respawn is the interesting
      moment (see the bullet above). An earlier cut of this read `Oracle` and was blind to exactly
      the window the report describes. It is fed from `PlayerShip.Asplode`, `AsplodeWall` and
      `NetSession.ExplodePuppet` -- that third one because a puppet leaves the world WITHOUT
      `Die()`, so neither of the other two sees a peer's death -- and cleared per level in
      `GameScene.Initialize`.
    - Out of `DebugFlags.Active` (the `?skullvolley` class: it changes no state, no position, no
      score and no packet). Pinned by the pair `tools/headless/probes/starmine_minelog.txt` +
      `starmine_minelog_absent.txt`, mutation-tested four ways; read them as ONE probe, since the
      hard-wired-on mutant is caught only by the absent file.
  - The netplay half -- the lock-on cue reaching a joining client as `NetFxKind.MineTargetAcquired`
    -- is in [`Compat/Net/CLAUDE.md`](Compat/Net/CLAUDE.md).
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
  advance in `Draw` (`fxTime += dt`), but on `Compat/WorldTime`'s delta rather than raw Draw time
  since card d79a2f48 — nothing in `Update`, so a pause would otherwise leave the connector
  crackling between two motionless ships. It therefore no longer crackles through a hit-stop
  either; `?harness=connector` is unaffected (the harness freezes with `Enabled=false`, not a
  pause layer). Flags `?connectorbolts= ?connectorarcs= ?connectorjitter= ?connectorpulse=
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
  - **`CollisionLevelMap`'s tile size comes FROM THE WALL, not from a second copy of the formula**
    (cards 4392bd30 / 80749dc4). It used to be the literal `800/width`, which equals the drawn
    block size (`texture.LogicalWidth() * scale`) only because `Setup` derives `scale` from that
    same expression — bit-for-bit true offline, and false the moment anything else sets `scale`.
    Online co-op did exactly that and the collision rows ended up 3.29px below the towers per row.
    A new consumer of the grid must ask `TileSize`, never re-derive it.
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
  **The Paratrooper challenge's falling brains draw it too (card c25883a2)** -- `ParatrooperBrain`
  was the last consumer of the pre-migration static `brainlargetransglow`, which is now DELETED
  (asset, `textures.config` line, `PrecompiledTextures` row, and the five dead
  `PreloadGraphicalContent` loads the earlier migrations left in `Level1`/`Level3`/`Demo1`/`Demo3`/
  `BraineroidsLevel`). Its scales went `0.1/0.2/0.33` -> `0.5/1.0/1.65`, the SAME x5 the Braineroid
  migration used -- not a derived size-preserving factor; the derivation and why x5 rather than
  x5.19 or x5.52 is in `ParatrooperBrain.cs`. **The additive glow is shared code now**
  (`BrainGlow.cs`, lifted verbatim out of `Braineroid.DrawGlow`): the sheet is chroma-keyed and
  carries no halo, unlike the sprite it replaced, so every consumer must draw it or the brain
  reads as a flat cut-out. `CastDisplayer` keeps its own copy -- it draws directly, with its own
  `?castbrain` scale, so it shares no call shape.
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

**OWNER REDESIGN (fable-iterative rep 1, 2026-08-09/10) -- THIS SECTION IS PART-HISTORICAL.** The owner rebuilt the whole force system live from the couch; the code (`PlayerShip.cs` consts + comments) is ground truth and every pre-redesign number, table and knob below is history unless restated since. The shape of what changed:

- **One curve family for every standard repulsor: `FieldCurve` = `max * (1-t)^p`, baked p=4** (`?aifieldpow=` sweeps it). The 2008 plateau `max*(1-t^2)` was an easing-direction slip -- 18 years of the gradient upside down -- and is gone from edges, beam, radial branch, swept shapes, wedge skirt and the boss-approach anchor alike. The derived anchors ride the curve: the single equilibrium floor is `FieldCurve(4, 0.8)` (0.0064 at p=4) and the static seeks are `FieldCurve(4, 0.55)` (0.164) -- no hand-picked 0.2/0.8 constants remain.
- **The swept shape is a UNION with a force competition**: the body circle (full peak) vs the triangle (whisper peak `?aitristrength=`, baked 2 -- empty space about to be claimed, not a body), higher force wins, ties to the circle. The beam's line field competes with its tip cone the same way (never sums). The old mesa knob family (`?aiconewidth=` etc.), the field-shape family (`?aifieldpx=/aifieldsize=/aifieldfall=/aiasteroid*`), `?airepeldelta=`, the per-tier skill ladder (`FixedSkill` now), the steering low-pass, the 20-strength top band and the beam's 30px boost band are all DELETED.
- **The spider fight runs on the field alone**: the hand-rolled lane escapes (`?ailaneescape=`) are deleted -- the boss's announced swept path feeds the ordinary cone + lane wedge. T4 (card 2c74d5b7) added the spare system: `?aispares=` (baked 1) big UFOs furthest from the ship are protected by forbidden WEDGES (tangent cone + aim spread, through-shot rule), `?aisparefair=` (baked 300, centre distance) is the fair-game circle, chargers are blanket non-targets during the fight, and the aimed-at ship BAITS the beam through a standing boss (mid-screen otherwise). `?aibench` reports `bossufos=` at boss death.
- **Landed UFOs announce the scroll carry** (`UFO.TryGetAiSweptPath` override) so they cone; seek arrival is a hysteresis latch (`?aiseekdeadzone=` park 8 / `?aiseekresume=` 20); `?aiseeklog` traces the seek kind; **`?cones`** draws every swept shape (yellow = the winning element, red X = fire-protected UFOs), `eaConesDump()` reads them as data.


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
  `1.2 * dtMs * MaxSpeed` = **~6.6px** at 60Hz against tiles 67..267px wide, SLAMMED the steer on
  a hit, and re-picked left-vs-right every tick. Now:
  `WallReactionMs` (baked 420) times the real closing speed (`MaxSpeed` + the wall's own
  `ObservedVelocity`); `ColumnScore` grades every column by clearance/travel/columns-to-cross and
  `GapSwitchMargin` stops it flip-flopping; the steer is proportional.
  - **This bullet used to say the 2008 probe was `41.67 * MaxSpeed` = ~13.75px, and that was the
    WRONG PROBE** (card d79b7ea7). ~13.75px is the 2008 *hard clamp*'s probe -- which the port
    KEPT -- so the description credited the replacement with replacing something untouched, and
    overstated the original's warning distance by 2x. The approach probe is a fifth of a
    ship-width, not one ship-width.
  - **`ClampIntoWallSpace` IS the 2008 block, not a port-era replacement** (same card, struck on
    source inspection rather than measured). `src_decompiled/EvilAliens/PlayerShip.cs` lines
    1114-1158: same two side probes at `41.666668 * MaxSpeed`, same ungated upward probe at 3x,
    same `direction.X = -max(|direction.Y|,1)` slam. The port differs only in `WallClampMs` being
    42 rather than 41.666668 (13.86px vs 13.75px, 0.8%), OR-ing the two corner probes into one
    bool instead of assigning identically twice, and naming the 3x. **So it was never a suspect**
    -- the 05a2b818 "struck on source inspection, 2008 == port" category. `?aiwallnav2008=1`
    deliberately does not switch this half; there is nothing to switch it to.
  - **The 2008 algorithm is REACHABLE, and that is what made this auditable** (`?aiwallnav2008=1`,
    card d79b7ea7). `SteerThroughWall2008` + `FindNextTileOnMap2008` are verbatim transcriptions
    with `src_decompiled` line ranges in their comments, kept so the comparison can be re-run
    rather than trusted. Announces itself as `[aiwallnav] steering: 2008`.
  - **THE WALL-NAV AUDIT (card d79b7ea7): NOTHING CHANGED, and here is the evidence.** The four
    port-era constants were re-measured under card 05a2b818's doctrine -- `ai_sweep.py`, Very_Hard,
    `?aiplayer`, `?invuln` OFF, seeds 1-30 x2 (N=60), paired diffs vs shipped, positive = worse.
    Every one of them predated merge f6b6504, so their old N=30 campaign numbers were treated as
    hypotheses and discarded rather than quoted.

    | suspect | 2008-ward arm | ownlevel diff | verdict |
    |---|---|---|---|
    | **the whole algorithm** (`ColumnScore` + committed gap) | `?aiwallnav2008=1` | **+3.93 +- 1.54** | **VALIDATED.** Wall kills 266 -> 508, victories 38 -> 30 of 60. Beats its null hypothesis on the approach steer (see the scope bullet below). |
    | **`WallScanRows` 4** | `?aiscanrows=1` (2008 saw rows y and y-1 only) | **+2.97 +- 1.21** | **VALIDATED.** |
    | **`WallReactionMs` 420** | `?aireact=80` | +0.13 +- 1.17 | **STANDS.** Flat here, but Level 3 Wall kills 188 -> 325; and Stage A had 800ms at +15.40 +- 1.53, 0 victories. A broad interior optimum. |
    | **`WallCrossPenalty` 4** | `?aicrosspenalty=0` (2008 had none) | +1.10 +- 0.83 | **STANDS, WEAK.** n.s. at 2 SEM; victories 38 -> 26 is suggestive only. |
    | **`GapSwitchMargin` 1.5** | `?aigapmargin=0` (2008 re-decided per tick) | +0.87 +- 0.80 | **STANDS, WEAK.** n.s. at 2 SEM. |

    **Read the two weak rows honestly: they are "not refuted", not "confirmed".** Both point the
    way the design argument says they should and neither clears 2 SEM at N=60, so the hysteresis
    and the cross penalty survive on the doctrine's tie-goes-to-nobody boundary rather than on a
    measured win. Their authority is real but small once the graded column search is in place --
    which is consistent with `og2008` (which removes ALL of it at once) being the only decisive row.
  - **THE ARM'S SCOPE, which bounds every number above: `?aiwallnav2008=1` swaps the APPROACH
    STEER ONLY.** `ClampIntoWallSpace` is 2008 code either way (bullet above), and everything
    downstream -- the adaptive low-pass, the repellent cancel floor, the noise floor -- is the
    port's and keeps running. So these rows say "the 2008 gap search, inside the port's steering
    stack, is worse", which is the question the constants live in; they do NOT price the 2008
    steering stack as a whole.
  - **DO NOT DEFEND THESE CONSTANTS ON CHURN -- the ~1050 deg/s belongs to a different knob.**
    This file long justified the wall-nav rewrite with card f4d1721f's commanded-heading churn.
    That figure is the missing LOW-PASS's, not this term's: card 05a2b818 reproduces it cleanly at
    `?aismooth=0` and validates the low-pass on it, and the low-pass is in force on both arms
    here. With it in place both wall algorithms are smooth and the 2008 one is *slightly smoother*
    (ownlevel `turn` 550 vs 769 deg/s, coast 22.7% vs 10.9%) while dying substantially more --
    the `revs/s=0 turn=0` trap in mirror image. **The wall-nav constants earn their keep on
    SURVIVAL.** (This is not evidence against the low-pass, which was measured separately and
    kept; it is evidence that churn cannot separate these two wall algorithms.)
  - **On `?level=Level3` the deaths column is SATURATED and cannot answer anything** (same card).
    All six arms read 8.3-8.5 deaths and 0/60 victories -- that is the life cap, i.e. GAME OVER at
    the same point every time, not six builds dodging equally well. Its `killers=Wall:` column is
    the informative one there, and even that needs care: `?aiscanrows=1` posts the FEWEST wall
    deaths of any arm (89 vs 188) while posting the most BattleSkull deaths (387 vs 302) -- a
    reallocation under a fixed cap, not a safer bot, and ownlevel says that arm is significantly
    WORSE overall. **Use `ownlevel` for a wall-nav verdict and Level 3 only for colour.**
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
  nudge -- it is full throttle. Hence: **two cancellation floors** (below), so a steer that has
  argued itself down to noise reads as "hold still" rather than "sprint that way"; and
  **smooth adaptively**, collapsing the time constant from
  `DefaultSteerSmoothMs` toward `DefaultSteerSmoothUrgentMs` as the push grows, because heavy
  damping is exactly wrong when something is bearing down. Two things were tried against the same
  idle-fidget symptom and are documented in place as REVERTED -- don't re-derive them: a
  velocity-damped "arrive" at the station (it contains `-SpeedVector`, so it brakes every real
  manoeuvre: coast 28% -> 59%, spider-boss deaths 24 -> 70) and a tighter deadzone alone.
- **THE FIELD PRINCIPLE: threat awareness belongs in the REPELLENT's shape, never in a gate on
  another force** (card ada9e839). `DoAIMove` is a potential field -- valleys at things worth
  reaching, mountains at things worth avoiding -- and the two families compose by SUMMING. Any
  mechanism that instead censors one force because another exists (a global threshold, a
  "suppress the seek while threatened" gate) breaks the composition and produces a bot that
  silently stops doing something. If the bot flies somewhere it should not, the answer is a
  steeper mountain there, not a veto on the valley.
  - **REPELLENTS** (every threat field, the lazer terms, the boss's lane escapes, the screen
    edges) sum into their own accumulator, and if that resultant falls to
    `DefaultRepulseCancelDelta` (0.2) or below the whole lot is dropped -- opposing pushes that
    cancel leave a vector whose DIRECTION is noise. **ATTRACTORS** (idle station, powerup, boss
    standoff) are never floored; each stops pulling inside its own DEADZONE. Then the two are
    summed and `DefaultSteerNoiseFloor` (0.2, applied AFTER the low-pass so the ship can actually
    reach zero rather than chase a decaying residual) catches the leftover equilibrium case.
  - **Each attractor's deadzone is sized by the ship's STOPPING DISTANCE**, which is
    `0.5 * ShipMaxSpeed^2 / ShipDeceleration` = **11.3px**. Below that the ship coasts out the far
    side still under the pull and pingpongs; `DefaultSeekArriveDeadzonePx` is **15** since card
    05a2b818, i.e. a margin of only 3.7px over that bound -- the 2008 value of 10 measured BETTER
    and was rejected precisely because it falls UNDER it. The powerup's
    pull needs none (contact collects it, so the target stops existing) and the boss standoff's is
    its standoff radius. `logic_probe`'s **`ProbeAiFieldComposition`** derives the bound from the
    real motion constants and pins it, along with "no floor sits above the weakest force".
  - **What this replaced, in one line:** the port ended `DoAIMove` with a 0.95 "park" where the
    2008 original had **0.2** -- above the 0.8 seek, so a lone seek produced no motion at all and
    every deliberate destination (station, powerup, boss standoff) was silently deleted. Restored
    to 0.2. `?aipark=` is GONE rather than renamed, because at 0.95 it was a veto, not a floor.
  - **MEASURED (eahl, Very_Hard, paired seeds 1-8 x2).** Powerup pickups on Level 1 `?invuln`:
    **72.4% -> 97.6%**. `SpiderBoss(standing)` deaths 22 -> 18. The jitter pair rose (revs/s
    1.71 -> 3.70) but the baseline bot was PARKED for 73-80% of ticks (`coast` 73% -> 42%), so
    that is a bot that moves versus one that did not -- the old "must not regress" bar was void.
  - **SHIPPED WITH SPACEDODGE AT 1/8 VICTORIES vs 4/8 base**, deliberately and with the number
    stated. The cause is NOT the composition: an asteroid's radial field contributes a **mean of
    0.42** against the **0.8** seek it must out-vote (`threats=` breakdown), so the bot correctly
    computes that a powerup across the belt is worth the trip. The designated fix is **card
    e425781b** (velocity-cone repellent shapes) -- the death heatmap shows mid-field lane
    collisions with clean edges, which is geometry a circular field cannot express.
    **That card LANDED and this is now history: SpaceDodge reads 16/16.** See "DIRECTIONAL
    REPELLENT SHAPES" below for the shape that did it; everything in this bullet about the
    radial field remains true and is exactly why a shape was needed.
    **Do NOT tune the asteroid field to chase it -- THREE axes were swept and none reaches the
    gate.** Magnitude (`?aiasteroidscale=` 1.5-4x) is whack-a-mole: asteroid kills 130 -> 99 while
    UFO kills 3 -> 19, 0 victories at every value. Shape (`?aiasteroidrange=` x{1.5,2,3} x
    `?aiasteroidfall=` {3,2,1} x magnitude {1.0,1.5}, 18 cells) tops out at **3/8** on full
    validation. **The "raise the mean field past the 0.8 seek" mechanism is REFUTED, not merely
    unachieved**: across that grid a higher mean field correlates with MORE deaths (mean 0.45 ->
    27.5 deaths, mean 2.79 -> 39.75), and the best cell barely moved the mean at all
    (0.44 -> 0.45). Wider and shallower does not help either; the belt needs a different SHAPE of
    repellent, which is card e425781b. Edge-death share stayed clean throughout (14%, no corner
    clustering), so the widened fields were not herding the ship into edges.
  - **THE 2008 FIELD CURVE WAS RESTORED AS A SEAM AND MEASURED -- IT IS NOT THE SPACEDODGE FIX**
    (card e88e21ca). The port swapped the curve FAMILY, not just its exponent: 2008 was
    `max*(1-t^2)` (a plateau -- 75% strength at half range), the port is `max*(1-t)^p` (a spike --
    12% at p=3), and `?aifieldfall=` only ever swept p *within* the port's family, so the original
    SHAPE had never been tested. `?aifieldcurve=classic` (global), `?aiasteroidcurve=` and
    `?aiasteroidflatpx=` (the flat 2008 150px range) restore it. SpaceDodge, seeds 1-4 x2:
    classic+flat150 asteroids-only 31.75 deaths, classic asteroids-only 35.75, classic GLOBAL
    38.75, control 34.50 -- **0/4 victories on every arm**. Not the fix.
  - **BOTH CURVES ARE EDGE-ANCHORED AT FULL STRENGTH, and the port's warning band is the WIDER
    one** -- so "the force at the object's edge is now so low the ship flies into it" is refuted
    by measurement, and the mechanism is elsewhere. `dist` subtracts the body term (2008 did the
    same, identically), so t=0 IS the collision edge and the live bench reads `max=4.00` there for
    both asteroid and SpiderBoss. Strength by edge distance, and the "warning perimeter" where
    each curve falls below the 0.8 seek:

    | edge dist | port, asteroid (R=480) | port, boss (R=406) | 2008 (flat 150) |
    |---|---|---|---|
    | 0px | 4.00 | 4.00 | 4.00 |
    | 50px | 2.88 | 2.70 | 3.56 |
    | 100px | 1.99 | 1.71 | 2.22 |
    | 200px | 0.79 | 0.52 | **0 (out of range)** |
    | **falls under 0.8 at** | **199px** | **169px** | **134px** |

    The 2008 curve is stronger up close and DEAD past 150px; the port is gentler up close and
    still pushing at 400. **The ship's measured mean edge distance is 252px from an asteroid and
    216px from the boss -- outside BOTH warning perimeters**, which is why neither curve fixes
    SpaceDodge and why the restored one is slightly worse: at the distances the bot actually flies
    the 2008 field contributes exactly nothing. (Beware the mean-strength readout here: restoring
    the flat 150px range RAISES `Asteroid(field)` mean from 0.42 to 1.63, but that is a SELECTION
    effect -- far contributions stop existing rather than getting stronger.)
  - **`EvadeMovingThreat` (closest-approach dodging) does the heavy lifting, rig-specifically.**
    Re-measured under the new composition with the `?aievade=0` seam: on **CrazyGame** (fast
    bullets, what it was originally justified on) deaths are **3.75 with it vs 14.25 without**,
    victories 2/4 vs 0/4 -- its original 27 -> 4 claim survives intact. On **SpaceDodge** it is
    FLAT (33.75 vs 34.5). The `threats=` breakdown says why: on CrazyGame the evade path handles
    6159 bullet contributions at mean **2.05**, against the radial field's 0.21, whereas asteroids
    are slow enough that most of the belt never passes its speed gate. So a threat's SPEED decides
    which path protects the ship, and a repellent change measured on one rig says nothing about
    the other.
  - Flags: `?airepeldelta= ?ainoisefloor= ?aiseekdeadzone= ?aiasteroidscale= ?aiasteroidrange=
    ?aiasteroidfall= ?aievade=`, the cone/wedge family above, plus
    `?aiseekapproach= ?aiseekpowerup= ?aipowerupreach=`. Wiring that `logic_probe` cannot reach is
    covered by `tools/headless/probes/ai_boss_approach.txt`.
  - **THE BOSS APPROACH IS SOLVED AGAINST THE BOSS'S OWN REPELLENT, with an INVERTED falloff**
    (card b56633fb, `PlayerShip.BossApproachWeight`). `DefaultSeekApproachWeight` 1.1 was a
    PARK-CLEARANCE number -- chosen only to sit above the 0.95 park -- and under the composition
    above it was mis-calibrated by construction: at the geometric standoff point the ship was
    being sent to, the boss's own repellent is **2.9** against that 1.1, so the net force pointed
    AWAY from its own destination and `bossfar` read ~99% forever. There is no constant now, and
    no standoff point: the target is the BOSS, and the weight `A(d)` is recomputed every tick.
    - **The shape is upside down on purpose** -- `A` GROWS with distance and quiets to ~0 inside
      firing range, anchored so `A(r*) = repel(r*)` at `r* = the live max bullet range` (gun range
      minus the boss's own body term, in edge space). So the net crosses zero exactly where the
      ship can shoot from, the whole-sum floor turns that crossing into a parked BAND, and outside
      it the attractor always outweighs the repellent **by shape rather than by a solved constant**
      -- which is what makes it survive a Range powerup, including one out-ranging the threat field
      entirely (`repel` is 0 out there, so the weight floors at `DefaultSteerNoiseFloor`).
    - **The band must stay wider than the ship's 11.3px stopping distance**, or it coasts through
      its own equilibrium. That is what damps the exponent: `k = min(1, budget * r* / w)` with the
      budget derived from that stopping distance and the repellent's real slope. `k = 1` for every
      boss, tier and weapon in the game **except BrainBoss up close** -- its hull is 233->257px of
      body term against a 351px gun range, which undamped bands 13.5px falling to 10.0px at its
      pulse peak. Do not delete the damping as dead code.
    - **MEASURED (eahl, Very_Hard, paired seeds 1-16 x2, N=32 per side) -- PHANTOM-ERA, so read
      the ratios and not the absolutes** (card 05a2b818). The re-baseline confirms the mechanism
      survives both PR #298 and the field-range revert: `bossfar` still sits at 25-27% on the
      `?brainboss` rig against the pre-card 99.9%, and `idle%` at 12-13% against 49.4%.
      `?level=Level3&brainboss`:
      `bossfar` **99.9% -> 27.1%**, `idle%` (a shootable target on screen and no shot fired)
      **49.4% -> 12.4%**, powerups collected **45% -> 51%**. On the FULL Level 3 deaths are
      IDENTICAL seed-by-seed (10/8/8/9 both sides) while `bossfar` still falls
      86/85/88/70 -> 44/67/61/76. **On the `?brainboss` fast-boot deaths rise 1.94 -> 2.88**, and
      there it is attributable -- sweeping `?aiseekapproach=` 0.05/0.5/1.0 reads 2.00/2.31/3.00,
      i.e. deaths track the pull. That rig is nothing but a boss fight, so it is the worst case
      rather than the representative one, and closing on a boss costing deaths is the trade card
      31ceb6ff already made.
    - **It only takes the wheel from a target something else CHOSE if it OUT-VOTES it** -- one
      `steerTarget` carries one destination, so a boss term quieted to a fraction of the 0.8
      powerup seek must not delete a live detour, least of all inside firing range where it is
      designed to fall silent. With nothing else chosen (the `X > 2000` sentinel) it takes the
      wheel at any weight, or the bot hovers at a station the boss may not be in range of.
    - **`?aiseekapproach=` IS NOW A SCALE on that solved weight (default 1), not a weight.** A
      value from before this card means something else; the two are not commensurable.
    - **SpiderBoss's exclusion from `IsAiPriorityTarget` is EXPLICIT now**, not by omission -- it
      must be dodged, not sought, and adding it to that list is the obvious edit that would make
      card b56633fb's own symptom (the bot walking into the PARKED boss) dramatically worse.
      `logic_probe`'s **`ProbeAiBossApproach`** pins that plus the crossing, the band bound over
      every tier x weapon x boss hull, the self-limiting interior and the pre-card configuration as
      a negative control; `tools/headless/probes/ai_boss_approach.txt` pins the wiring.
  - **Boss PROXIMITY is descriptive, never a gate.** `bossfar%` and `boss=<px>` describe where the
    ship is; the bot moving closer to a boss to dodge, collect or line up a shot is the field
    working. Gate boss work on OUTCOMES -- `SpiderBoss(standing)` deaths -- not on distance.
- **DIRECTIONAL REPELLENT SHAPES: every mover projects a MESA along its own velocity** (card
  e425781b). This is the fix the three failed radial campaigns above were pointing at, and it is
  the largest single win the bot has had: **SpaceDodge 2/16 -> 16/16 victories, 33.75 -> 3.25
  deaths** (eahl, Very_Hard, 600s cap, seeds 1-8 x2, same-side pairs agreeing on every seed), with
  the death heatmap over those runs going 540 -> 51.
  **NUMBERS SUPERSEDED (card 05a2b818): every figure in this bullet is phantom-era AND N=16.**
  The shape's VERDICT survived re-audit at N=60 -- the cone and the wedge both earn their keep,
  and `?aicone=0` / `?aiwedge=0` still cost 3.95 deaths on SpaceDodge -- but do not quote the
  magnitudes. Current reference numbers are in the re-baseline table below.
  - **WHY A SHAPE AND NOT MORE STRENGTH.** A circle can only say "I am here"; it cannot say "I am
    about to be THERE". The bot's measured mean edge distance from an asteroid is 252px while the
    radial field falls under the 0.8 seek at 199px -- it spends its life outside the only warning
    it has, and the deaths cluster MID-FIELD with clean edges. Four axes across three cards
    (magnitude, range, falloff, curve family) could not move that, because none of them changes
    the field's SHAPE.
  - **THE CONE, in cone-local coordinates.** With `axis` the unit travel direction, `u` how far
    AHEAD the ship is and `w` how far to the SIDE: the corridor's half-width tapers from the
    body's own half-extent at `u=0` to a point at the cone's length, the across-axis falloff is
    measured from that corridor's EDGE outward, and length = speed * `ConeLeadMs`. So it is at
    FULL strength anywhere inside the swept body (the card's mesa principle -- inside is death, so
    curve values there are wasted dynamic range) and it lengthens with speed by construction. One
    rule covers asteroids, bullets, UFOs and the boss's screen-crossing sweep with **no per-type
    code**; a fast mover automatically projects a long cone.
  - **THREE DECISIONS THAT ARE NOT ARBITRARY, all spelled out at the code:**
    - **The push is purely TRANSVERSE.** The mesa's along-axis gradient points FORWARD, down the
      mover's own track -- following it asks a 0.33px/ms ship to outrun a 0.38px/ms asteroid, and
      it is the identical failure the radial field already has against a screen-crosser. Only the
      sideways component is taken, which is what `EvadeMovingThreat` does and for the same reason.
    - **PLATEAU along the axis (`1 - t^p`, p=2), SPIKE across it (`(1-t)^p`, p=3).** Authority far
      out along the trajectory is the whole point; transverse clearance must stay cheap or
      threading a gap between two rocks stops being possible and the shape is just a wider circle.
      Note the along-axis family is the 2008 one that card e88e21ca measured and rejected -- but
      rejected as a RADIAL curve, where a plateau merely widens a circle. On a trajectory axis it
      is the idea, so that result does not carry.
    - **It ADDS to the radial field rather than replacing it** -- the shape is a circle with a hat
      on it, so both halves are real. Consequence for reading `threats=`: `(cone)` and `(wedge)`
      are NOT exclusive with `(field)` the way `(evade)` is, so ask which path carries the WEIGHT,
      not which one appears.
  - **THE LANE WEDGE is the one asymmetric case.** A symmetric cone offers the gap between a
    lane-hugging path and the screen edge as an escape, and that gap is a trap -- the ship dodges
    into it and is crushed against the wall. So when a swept band leaves less than a survivable
    gap on one side, everything from the path to that edge is closed at FULL strength and the only
    downhill direction is out of the lane; past the band's far edge it degrades with the cone's
    own across-axis falloff, so a ship that has already left is nudged rather than shoved back.
    - **Which edge is SCREEN GEOMETRY, not a type test**, and the survivable gap is derived
      (`2 * (shipHalfExtent + the 11.3px stopping distance)`), so the boss's three fixed lanes and
      its sweep-to-the-right-edge landing all resolve themselves -- including reproducing the
      hand-rolled escape's "always break LEFT out of a landing".
    - **A band NARROWER than the room a ship needs raises no wedge at all.** It is an obstacle to
      cross, not a corridor. Without that gate every UFO in SpaceDodge wedged (3263 contributions
      at mean 4.25) simply for entering from the top, which out-votes the entire rest of the field.
      **But it is a SIZE threshold (~63px of half-extent), NOT an "only the spider boss" test** --
      a big UFO or a reallyBig asteroid clears it and does raise a wedge when its path hugs an
      edge (`UFO(wedge)` 443 contributions at mean 1.81 on the spider rig, `Asteroid(wedge)` 296
      at 0.98 on SpaceDodge). That is the rule working, not leaking: a 90px-wide UFO sweeping the
      ceiling really does leave a gap the ship cannot cross in time. Expect those rows in
      `threats=` and do not read them as the gate having failed.
    - **The MIDDLE lane raises no wedge** -- it hugs neither edge, so either side is an escape,
      where the hand-rolled escape forces it DOWNWARD unconditionally. **In the shipped build that
      is not a behavioural change**, because the supersession A/B kept the escapes (below) and so
      the middle lane is still forced down exactly as before -- the wedge simply adds nothing
      there. Called out because it is the kind of quiet difference a future reader comes hunting
      for, and because it WOULD become live if the escapes were ever retired; pinned as its own
      `logic_probe` check.
  - **`AlienDrawableGameComponent.TryGetAiSweptPath` is the seam that feeds it, and its contract
    is ANNOUNCED rather than observed.** Default = `(Position, ObservedVelocity, half-extent)`,
    i.e. no per-type code for anything in the game. `SpiderBoss` is the ONE override, because all
    three defaults are wrong for a scripted set-piece: its `Update` early-returns through the whole
    "Danger!" hold, so its observed velocity is ZERO exactly when the warning matters; its lethal
    band SNAPS to one of three fixed lanes rather than tracking `Position.Y`; and its landing
    sweeps to the right screen edge, so the swept band is not symmetric about the body. That is
    choreography-as-data, which is what the field principle asks for -- the knowledge lives in the
    shape's INPUTS, not in a special case inside `DoAIMove`.
  - **THE DEFAULT REFUSES A TELEPORT-SHAPED PATH, and finding out why fixed a live bug** (card
    c1d783ad). `ObservedVelocity` is a raw ONE-FRAME position delta, so anything repositioned in a
    single tick reports an enormous speed for that frame -- and cone length is `speed * ConeLeadMs`
    capped at `ConeMaxLenPx`, so one such frame closes a full-screen corridor at full strength and
    shoves the bot somewhere arbitrary. The default now returns false above a ceiling and says so
    once per type on an `[ai] implausible swept path refused: <T> at <n> px/ms` line -- which is
    the term's ONLY observable, since a refused cone changes no pixel and moves no counter.
    - **The ceiling IS `NetSession.MaxObservedSpeedPxPerMs` (5.0), referenced, not copied** -- the
      project's existing "that was a teleport, not motion" number, measured by `eaNetVelScan` from
      the gap between the fastest genuine mover (~2.5 px/ms) and the slowest reposition (11.6).
      The two uses differ in RISK DIRECTION -- over there a wrong value prints a spurious line,
      here it deletes a real mover's cone -- so `logic_probe`'s `ProbeAiSweptPathGuard` asserts the
      SEPARATION property (>= 2x the fastest real mover, <= half the slowest reposition). A
      net-side retune that leaves that band fails there instead of quietly changing how the bot
      flies. `?aisweptmax=<px/ms>` overrides it; **`0` turns the guard off**, which is the A/B seam.
    - **THE HAZARD WAS LIVE, NOT LATENT, AND THE ROOT CAUSE WAS POOL RECYCLING.** `Initialize`
      reset `netTeleported` per life but NOT `_prevPosition`/`_hasPrevPosition`, so a recycled
      entity's first `Update` differenced its new spawn point against its PREVIOUS LIFE's last
      position. Measured before the fix: `EvilBullet` at **14.9 px/ms against a declared 0.24**,
      `UFO` 15.0, `EvilSkull` 21.4 -- i.e. every recycled bullet, UFO and skull was projecting a
      bogus full-length cone, and `EvadeMovingThreat` (which reads `ObservedVelocity` directly)
      was being fed the same phantom. The net layer never saw it: it keeps its own per-entity
      history in `NetIdRegistry`, and its scan hit the identical artefact and reported the same
      14.9. The three-line per-life reset is the fix; the guard is the safety net.
    - **`EvadeMovingThreat` CONSULTS THE SAME PREDICATE, and it needs it more than the cone does.**
      A reposition sails through its `ThreatMinSpeed` gate, collapses the time-to-closest-approach
      to almost nothing and so lands inside `ThreatPanicMs` -- a `ThreatPanicStrength` shove of
      **16**, four times `maxSteerStrength`, aimed along a course the thing never took. The screen
      wrappers reach it for real, so the reset alone would not have covered it. Measured on Level 1
      (the only rig where anything trips the guard), guarded vs `?aisweptmax=0`: 5.62 vs 3.75
      deaths on one pass and 6.00 vs 5.62 on another -- i.e. inside that rig's own lottery, which
      is the honest reading rather than a win.
    - **THREE TYPES STILL TRIP IT, all correctly.** The SCREEN WRAPPERS are the reason the card
      exists: a `wrapping` Braineroid and a wrapping `Ball` really do teleport across the screen
      (each already calls `NetNoteTeleport` at the same site), measured at ~52 refusals a run on
      Level 1 at 47.9 px/ms -- 52 bogus full-screen corridors that no longer exist. The third is
      `MarsBoss`, which reads 7.8 px/ms for a SINGLE frame at the top of its entry ramp:
      `eaNetVelScan` measured that same curve at 2.404 sustained over its 60 ms cadence, so 7.8 is
      a finite-difference artefact of an acceleration sampled at 16.7 ms rather than a speed it
      travels. Accepted at one frame of one cone per arrival; the body is still covered by the
      radial field. **This is the general caveat: the ceiling was measured at 60 ms and is applied
      per frame**, so an accelerating mover reads higher here than in the scan.
    - Pinned by `logic_probe`'s `ProbeAiSweptPathGuard` (the measured genuine/reposition tables,
      the boundary, the separation property) and the probe PAIR
      `tools/headless/probes/ai_swept_guard.txt` (the line is absent on a recycle-heavy rig) +
      `ai_swept_guard_trip.txt` (the same detector made to fire). Read the two as one probe.
  - **BOTH SUPERSESSION CANDIDATES WERE MEASURED AND BOTH SURVIVED -- nothing was deleted.** The
    card's ruling was to delete them outright; the match-the-number bar refuted it, which is what
    that bar is for. The cone is an ADDITION.
    - `EvadeMovingThreat`, CrazyGame seeds 1-4 x2: evade on / cone off **4.75 deaths, 8/8**; cone
      on / `?aievade=0` **38.00, 0/8**; neither 49.75, 0/8. So the cone helps a lot and replaces
      nothing.
    - The spider lane escapes, `?level=Level2&spiderboss` 180 sim-s, same seeds (standing deaths
      summed over the 8 runs): escapes on / cone off 6.50 deaths / standing 12 / pickup 60.9%;
      escapes off / wedge on 6.75 / 24 / 35.0%; both on 5.00 / 22 / 60.9%.
    - `?ailaneescape=0` is therefore a PERMANENT A/B seam rather than the temporary one it was
      built as -- the escapes it disables now live on beside the wedge.
  - **SHIPPED WITH TWO STATED REGRESSIONS**, both on levels whose victory verdict is unchanged and
    both smaller than what the shape buys. **BOTH ARE NOW GONE and neither was fixed by a shape
    change** (card 05a2b818): the CrazyGame one was recovered by PR #298's phantom fix and then
    improved past its own pre-card figure by the field-range revert (3.93 deaths at N=60 against
    the 4.75 this bullet calls the baseline), and the `SpiderBoss(standing)` figure was measured
    on a rig this file now runs for 300s rather than 180s, so the two counts are not comparable at
    all. Kept for the mechanism, not the numbers:
    - **CrazyGame deaths 4.75 -> 8.50** (victories 8/8 either way; it is a `Lives = -1` level).
    - **`SpiderBoss(standing)` deaths 12 -> 22**, while TOTAL spider deaths IMPROVE 6.50 -> 5.00.
      It is NOT the wedge -- `?aiwedge=0` makes it worse still (28). A standing boss sweeps nothing
      and so projects no cone at all, so these are deaths to the ship being pushed INTO a parked
      boss by OTHER objects' cones.
  - **THE ACROSS-AXIS WIDTH IS THE ONE UNDERIVED NUMBER, and the two rigs disagree about it.**
    SpaceDodge, paired seeds x2: 75px 21.50 deaths (2/4), 150px 8.56 (12/16), **300px 3.44
    (16/16)**, 450px 6.00 -- an INTERIOR optimum, not a gradient left half-walked. Magnitude
    reaches the same outcome and was declined (`?aiconescale=2.5` also 16/16, at 3.62) on the
    grounds cards ada9e839 and e88e21ca both used. But **CrazyGame wants the opposite width**
    (1.00 death at 60px against 8.50 at 300): it fields 30 simultaneous ~5px-half bullets, and a
    300px skirt on each buries the ship in transverse pushes that cancel.
    **The obvious generalisation was BUILT AND MEASURED AND DECLINED -- do not re-derive it.**
    Scaling the reach with the hull the way `ThreatFieldRange` does, floored so a swarm keeps a
    usable skirt (`?aiconespread=` x `?aiconewidthmin=`, 6 cells, both rigs in one pass, seeds 1-4
    x2) really does fix CrazyGame -- 8.50 -> 1.00, better than having no cone at all -- and the
    FLOOR is the axis that matters, not the multiplier. The best cell (k6.4 / 60px) took 8/8 at
    7.62 deaths on THAT screening set and then **14/16, also at 7.62, on the full seeds 1-8 x2**
    gate -- the equal means are a coincidence of two run sets, not one figure quoted twice --
    against the flat width's 16/16 at 3.25. It then fails the third gate
    outright: `SpiderBoss(standing)` 12 shipped-main / 22 flat / **34** scaled, by the mechanism
    in the bullet above -- a wider UFO skirt shoves the ship into the parked boss harder, visible
    as that arm's `UFO(wedge)` mean climbing 1.12 -> 1.96. So the flat number ships and both
    seams stay INERT. The rig disagreement is real and open.
  - Flags: `?aicone=` (master A/B control) `?aiwedge=` `?ailaneescape=` `?aiconelead=`
    `?aiconemaxlen=` `?aiconewidth=` `?aiconetaper=` `?aiconefallalong=` `?aiconefallacross=`
    `?aiconescale=` `?aiconespread=` `?aiconewidthmin=` `?aiwedgestrength=` `?aiwedgefall=`.
    `logic_probe`'s **`ProbeAiConeShape`** pins the shape at FIXED POINTS -- the mesa, the
    transverse-only push, nothing behind a mover, length scaling with speed, the wedge's flat
    trapped side and its far-side degradation, the middle lane, and the small-mover negative
    control. Fixed points rather than any aggregate, because a field's MEAN strength over a run is
    a selection effect (far contributions stop existing rather than getting weaker), so two shapes
    can only be compared point by point. It calls `PlayerShip.EvaluateSweptShape` directly -- the
    shape is a pure function of geometry, with no ship, component or `Game` involved.
- **`AiBench` answers "what is killing it" and "does it collect anything" now** (same cards).
  `killers=<Type>:<n>` is a histogram taken where the ship actually dies (`asplosionCauser`), and
  **`SpiderBoss` is split by state** -- `SpiderBoss(standing)` vs `SpiderBoss` -- because walking
  into a PARKED boss and losing a dodge against a screen-wide sweep are opposite failures that
  `deaths` alone cannot tell apart. `pickups=<n>/<spawned>(<pct>%)` is the powerup rate, with the
  denominator so "the bot ignores powerups" and "this run dropped two" are distinguishable, and
  `boss=<px> bossfar=<pct>` is the approach term measured where it acts. **`Row()` spells the
  first two differently** -- `pickups=<n> poffered=<spawned>` and no percentage -- because its
  parser is `split(. .)` then the first `=`, so a value carrying a bracket or a space is what
  breaks `eaAiBench.matrix`; the percentage exists in `Line()` only, for probes to regex.
  **Standing result worth knowing: on `?level=Level2&spiderboss` (Very_Hard, no `?invuln`, 180
  sim-s, N=16) `SpiderBoss(standing)` is 39 of 101 deaths -- the largest single killer, and more
  than double the moving boss's 25.** So "the AI happily runs into the spider boss when it is
  stationary" (card b56633fb) REPRODUCES, and it is a REPULSION problem: the seek fix above moves
  it not at all (deaths 6.31 -> 5.88, standing 39 -> 39).
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
  and pins the ship on the ceiling to be exploded by something spawning on it. **A port addition,
  and MEASURED (card 2248e5eb): removing it costs deaths on both rigs and takes deaths BY `UFO`
  from 132 to 356 on the spider rig** -- see the audit table below. `?aitopedgestrength=0` is the
  2008 arm; the generic 150px/strength-4 screen-bound push underneath it is untouched.
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
  - **Only `ThreatFieldBasePx` and `AimSpreadRad` scale.** WHICH two knobs scale is a measured
    result; the field column's VALUES are not (see the rescale note below). Each candidate was
    isolated by holding the tier fixed (so the level's own difficulty scaling could not confound
    it) and moving one `?ai*` override: aim `15deg -> 57.3deg` moved Level1 progress
    `50/64 -> 45/64`; field `190 -> 30px` moved spider-boss deaths `11 -> 14`.
    **That field pair is PARK-ERA and its 190 is no longer the anchor -- do not quote the
    numbers.** It survives only as evidence of the knob's DIRECTION at the extreme, and card
    05a2b818 showed the response is not monotone between 150 and 190 anyway.
  - **THE FIELD COLUMN IS RESCALED, NOT RE-MEASURED** (card 05a2b818). That card moved the anchor
    190 -> 150, which would have collapsed the ladder outright (the old Easy row was itself 150),
    so every field row is multiplied by 150/190: **118 / 129 / 139 / 150 / 150**. That preserves
    each tier's spacing below the new anchor and nothing more -- the lower rows are the old
    proportions and carry no evidence of their own. A fresh sweep is not the fix, either: the
    argument two bullets down, that tier-vs-tier cannot be measured end-to-end because the
    ENEMIES scale with the same tier, is exactly why only the anchor row is evidence-backed.
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
- Flags: `?aibench` · `?aiff=<2-64>` · `?aismooth= ?aismoothurgent= ?aireact=
  ?aigapmargin= ?aiscanrows= ?aicrosspenalty= ?aithreatlead= ?aibossbias= ?aiaim= ?aifieldpx=
  ?aifieldsize= ?aifieldfall= ?aiseekapproach= ?aiseekpowerup= ?aipowerupreach= ?airepeldelta=
  ?ainoisefloor= ?aiseekdeadzone= ?aiasteroidscale= ?aiasteroidrange=
  ?aiasteroidfall= ?aievade= ?aicone= ?aiwedge= ?ailaneescape= ?aiconelead= ?aiconemaxlen=
  ?aiconewidth= ?aiconetaper= ?aiconefallalong= ?aiconefallacross= ?aiconescale= ?aiconespread=
  ?aiconewidthmin= ?aiwedgestrength= ?aiwedgefall= ?aitopedgepx= ?aitopedgestrength= ?ailazerpx=
  ?ailazerstrength= ?ailazerdodge=`
  (null => the baked `PlayerShip.Default*` consts, so a shipped build is unchanged).
  A malformed value on any of them is REPORTED and ignored, never swallowed, per the file-wide
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
- **HOW TO MEASURE ANYTHING HERE, and it is stricter than this file used to say** (card 05a2b818).
  The old rule was "single runs vary a lot -- differences under ~30% are noise". That was right in
  spirit and far too weak in practice. Per-seed deaths on these rigs range 0-17, so a 16-run mean
  carries a standard error of ~2 deaths: during this audit a "46% improvement" at N=16 evaporated
  at N=60, and the arm that looked WORST at N=16 turned out to be among the best. The rules:
  - **N=60 per arm-rig is the floor** (seeds 1-30 x 2 captures). Nothing below N~60 is safe on
    these rigs, whatever the percentage looks like. The old CrazyGame-specific caution (N=6 and
    N=10 both misled card 21bb6849) was not a quirk of that rig -- it is the general case.
  - **PAIR BY SEED and quote +-1 SEM of the PAIRED DIFFERENCE.** Every arm runs the same seeds,
    and seeds are the dominant variance source, so pairing removes it and is far more powerful
    than comparing two independent means. **An interval spanning 0 is not evidence.**
  - **Same-side captures must AGREE before sides are compared.** A seed whose own two captures
    disagree is an UNSTABLE world; a cross-arm difference resting on one is not a result. (This
    is the `?seed=` near-determinism caveat, enforced rather than remembered.)
  - **Read TIME-TO-VICTORY beside deaths -- deaths are a COUNT, not a RATE.** A build that takes
    twice as long to finish the same world dies about twice as often in it while dodging exactly
    as well. This is not hypothetical: the whole of card c1d783ad's handed-off SpaceDodge seed-4
    "regression" was this and nothing else (that world: victory at 456s/17 deaths against
    165s/4 on the same seed), and at N=30 the two builds are indistinguishable (4.83 vs 4.87
    deaths). **There was no regression.**
  - **The instrument is `python tools/sim/ai_sweep.py`**, which does all four by construction --
    `--rig` x `--arm` x `--seeds`, paired stats, unstable-seed flagging, time-to-victory. Use it
    rather than hand-rolling a loop; see tools/CLAUDE.md.
  - **Every AI figure in this file predating merge f6b6504 (PR #298) was measured with
    recycle-phantom cones in the world, and most predate N=60 as well.** Treat all of them as
    hypotheses. The re-baselined reference set is in the next bullet.

#### THE RE-BASELINE, and what the port-era audit actually found (card 05a2b818)

The user ruled the July-24-era tuning generation contaminated (every value chosen then was
validated against a bot whose deliberate motion the 0.95 park was vetoing), and PR #298 then
invalidated the rest by removing the recycle phantoms. So every port-changed constant was
re-audited with the 2008 original as the **null hypothesis**: a port value survives only by
BEATING the original in a paired A/B, and ties revert.

**THE REFERENCE SET.** eahl, Very_Hard, `?aiplayer`, `?invuln` OFF, seeds 1-30 x2 (N=60), on the
SHIPPED configuration at merge `01009f9`. Quote these, not anything older:

| rig | flags | cap | deaths | victories | win@ |
|---|---|---|---|---|---|
| SpaceDodge | `?level=SpaceDodge` | 600s | 4.03 | 60/60 | 188s |
| CrazyGame | `?level=CrazyGame` | 300s | 3.93 | 50/60 | 106s |
| spider | `?level=Level2&spiderboss` | 300s | 8.87 | 3/60 | 83s |
| Level 1 | `?level=Level1` | 600s | 7.02 | 12/60 | 441s |
| BrainBoss | `?level=Level3&brainboss` | 300s | 5.00 | 2/60 | 299s |

**Level 1 is the weak rig** -- 3 of 8 seeds were unstable at N=16 and it needs the full 600s to
reach a verdict at all (at 300s it reads 0 victories on every arm, so the victory column carries
no signal). Require a large effect there.

**FOUR PORT VALUES WERE VALIDATED against their 2008 counterparts** -- paired diffs vs shipped,
N=60, positive = worse:

| suspect | 2008 arm | verdict |
|---|---|---|
| **steering low-pass** (90ms / 15ms urgent; 2008 had none) | `?aismooth=0&aismoothurgent=0` | **KEPT.** BrainBoss **+10.57 +- 0.30** -- 15.00 deaths on all 30 seeds, SEM 0.00, 900 PlasmaBall kills, coast 74%: a deterministic level-halting wedge. It helps the bullet rigs (-2.62 SpaceDodge, -3.67 CrazyGame) at `turn` 1488-2636 deg/s, i.e. **card f4d1721f's ~1050 deg/s jitter reproduces cleanly** post-park and post-phantom. |
| **field curve FAMILY** `(1-t)^p` | `?aifieldcurve=classic` | **KEPT.** +11.37 +- 1.79 SpaceDodge, +10.20 +- 0.81 CrazyGame (0/60 victories). Re-confirms card e88e21ca / PR #289 under clean conditions. |
| **falloff exponent p=3** | `?aifieldfall=2` | **KEPT.** +2.37 +- 0.94 CrazyGame, n.s. on the other four. |
| **spider lane/sweep escapes** (port-added) | `?ailaneescape=0` | **KEPT.** +1.83 +- 0.64 and victories 5/60 -> 0/60. The wedge too (+1.72 spider, +3.95 SpaceDodge). e425781b's phantom-era supersession A/B holds up. |

**TWO PORT VALUES WERE REFUTED AND CHANGED.**

- **`ThreatFieldRange` base 190 -> 150**, size scale 1.8 KEPT. The formula's two parameters
  separate cleanly and pull in opposite directions, so neither era's version is best:

  Paired diffs (N=60) **against the PRE-CARD arm, `190 + 1.8*he`** -- not against the shipped
  build, which is the `px150` row itself. Positive = that arm is worse than the pre-card one:

  | arm | spacedodge | crazygame | spider | level1 | brainboss |
  |---|---|---|---|---|---|
  | `og150` = 2008 exactly (`150 + 0`) | -0.65 +-1.12 | **-3.57 +-1.10** | **+1.28 +-0.59** (vic 4->1) | +0.02 +-0.66 | **-1.60 +-0.38** (vic 6->14) |
  | `px150` = what SHIPPED (`150 + 1.8*he`) | -0.68 +-1.32 | **-2.67 +-0.94** | -0.25 +-0.73 (vic 4->6) | +0.68 +-0.62 | +0.67 +-0.42 |

  **The spider victory counts move between sweeps and that is the point, not a typo**: the
  pre-card arm reads 4/60 here, 6/60 in the combined confirmation run, and the shipped build 6/60
  here against 3/60 there. Victories on that rig are a 3-6-of-60 event, so their run-to-run spread
  swamps any difference between these arms -- which is why the deaths column with its SEM is what
  the verdict rests on, and why no single victory count in this table should be quoted alone.

  The BASE is refuted (150 wins CrazyGame on both arms). The SIZE SCALE is validated (dropping it
  costs the spider rig +1.28 deaths and 3 of 4 victories -- big-UFO kills 117 -> 187 -- because a
  flat field is nothing next to a 90px-half UFO) and is simultaneously what makes BrainBoss
  expensive (a ~250px hull draws a ~600px field). **What ships is neither era's formula and it
  survives on measurement, not doctrine: zero significant losses on any rig.**
- **`DefaultSeekArriveDeadzonePx` 30 -> 15.** Monotone on CrazyGame, flat on the other four --
  paired against 30: **10px -3.60 +-0.86, 15px -2.87 +-0.85, 20px -1.97 +-0.96** (victories 36 ->
  50 / 48 / 44 of 60). **10 -- the 2008 value -- measured BEST and was rejected anyway**, on the
  bound rather than the number: it sits below the ship's 11.3px stopping distance, so the ship
  cannot come to rest inside it and it stops being a deadzone at all. 15 is the smallest value
  keeping `ProbeAiFieldComposition`'s invariant intact and takes ~80% of the win.

**COMBINED, vs the old baked values** (`?aifieldpx=190&aiseekdeadzone=30`, N=60): CrazyGame
**-3.70 +- 1.01** deaths (7.63 -> 3.93), victories 36 -> 50 of 60, win@172s -> 106s; SpaceDodge
4.70 -> 4.03 and 58 -> 60 victories; Level 1 flat. **Shipped with two stated non-significant
regressions**, in the e425781b tradition of stating rather than hiding them: spider deaths
8.37 -> 8.87 (**+0.50** +- 0.66, victories 6 -> 3 of 60) and BrainBoss 4.30 -> 5.00 (**+0.70**
+- 0.40, victories 6 -> 2), both driven by the size scale the audit kept. (Positive = the NEW
build is worse, as everywhere in this section.)

**The difficulty ladder's field column was RESCALED, not re-measured** -- see
`AiSkillByDifficulty`. Moving the anchor to 150 would otherwise have collapsed it (the old Easy
row WAS 150), so every row is multiplied by 150/190. The lower rows are the old proportions and
carry no evidence of their own; the doc's own argument that tier-vs-tier cannot be measured
end-to-end is why.

**Not suspects, struck on source inspection rather than measurement** (2008 == port, so there was
nothing to audit -- verified in `src_decompiled/EvilAliens/PlayerShip.cs` `DoAIMove`/`DoAIFire`):
the per-seat `dodgeAngle` (+-pi/16, +-pi/6), the four screen-edge repulsions (`steerRange` 150,
strengths 0..4, `PowerCurve` exp 2, the 560px Floor bottom), the powerup's direct pull, the aim
spread `Math.PI/12`, `SeekWeight` 0.8 and both 0.2 floors.

#### The last two port additions, audited (card 2248e5eb)

The audit above left two port ADDITIONS unmeasured because neither was reachable by a `?ai*`
flag. Both now are (`?aitopedgepx=` `?aitopedgestrength=` `?ailazerpx=` `?ailazerstrength=`
`?ailazerdodge=`), and both were run against the 2008 null hypothesis under 05a2b818's protocol
-- N=60 (seeds 1-30 x2), paired by seed, `python tools/sim/ai_sweep.py`. **One survived, one was
refuted and reverted.** Positive = that arm is worse than the shipped build of the day.

| suspect | 2008 arm | level1 (600s) | spider (300s) | verdict |
|---|---|---|---|---|
| **top-edge push** (`170px` / strength `20`) | `?aitopedgestrength=0` | **+0.88 +- 0.52** | **+0.67 +- 0.64** | **KEPT** |
| **beam field + sidestep** (`260px` / `14` / `7`) | `?ailazerpx=150&ailazerstrength=4&ailazerdodge=0` | +0.98 +- 0.65 | **-4.55 +- 0.69** | **REVERTED to 150 / 4 / 0** |

- **The top-edge term is confirmed by its own stated mechanism, not just by the deaths column.**
  Removing it multiplies exactly the death it was added to prevent: deaths by `UFO` 132 -> 356 on
  the spider rig and 150 -> 212 on Level 1, because a ship pinned on the ceiling is exploded by
  whatever spawns on top of it. **The spider margin is thin** (+0.67 +- 0.64 barely excludes
  zero) and rests on Level 1 agreeing independently plus that histogram; do not quote it alone.
- **The beam field lost on the rig built around a beam, and the killer histogram says why**:
  `SpiderBoss(standing)` 290 -> 6 deaths. A 260px field around a beam that the AI is
  *deliberately standing near* -- it spares one big UFO so the boss walks into the shot -- was
  shoving the ship off the beam and into the stationary boss, i.e. re-creating card b56633fb's
  original complaint from the other direction. Victories 3/60 -> 24/60.
- **Level 1 genuinely prefers the port values (+0.98 +- 0.65) and that regression ships**, stated
  rather than hidden, in the e425781b tradition: -4.55 on one rig against +0.98 on the other.
- **What reverted is the MAGNITUDES, not the shape.** 2008 ran `MyMath.PowerCurve`, the classic
  `max*(1-t^2)` plateau; this term keeps the port's `(1-t)^p` spike, which card 05a2b818 ruled on
  globally and decisively. So the beam field at 150px now pushes *less* than the original did at
  the same range. The configuration that was measured is exactly the one that ships, so the
  number stands -- but it is not "the 2008 treatment restored".
- **The sidestep is OFF, not deleted**: `DefaultLazerDodgeStrength` is 0 and `?ailazerdodge=7`
  brings it back. `DoAIMove`'s `> 0` branch is an early-out, not a behaviour difference (the zero
  vector would steer identically) -- it is there so the term reads as switched off at the point of
  use. `ProbeAiFieldComposition` now folds that constant into its weakest-repellent min only WHEN
  IT IS ON: that bound is about a pushing repellent being eaten by a floor, a term that is
  switched off pushes nothing, and making it a condition means the bound re-arms itself if the
  sidestep is ever baked back on.
- **PICK THE RIG WITH `killers=`, NOT WITH INTUITION.** The card specified "Level 3 sections" for
  the laser arm; the pre-flight refuted it -- `?level=Level3&brainboss` lands **zero** `Lazer`
  deaths over seeds 1-3 despite spawning big UFOs, as does plain `?level=Level3`. **Level 1 is
  the laser rig** (`Lazer` is its single largest killer). Worse, **the lazer knobs are inert on
  Level 1 at a 300s cap** -- no beam exists yet, and all three flags reproduce the shipped row
  digit for digit. A 300s laser sweep reads "no effect" and would have shipped a wrong verdict.

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
- **Every music switch prints `[music] play <Song> cue=<cue> was=<Song|none>` / `[music] stop
  was=<...>`** (`SoundManager`, card 4a3b22b7). It is the ONLY observable a music beat has:
  **`eahl` stubs `eaMusic.*` entirely**, so headlessly a beat that never fires and a beat that
  fires correctly are otherwise identical, and in the browser music has no pixels either. Both
  halves of a failed switch now name themselves -- the C# line says what was REQUESTED, and
  `eaMusic.play`'s two failure branches (`no music.json entry for cue '<x>'`, `could not load cue
  '<x>' (<file>): <err>`) say when the request could not be HONOURED. Both JS branches leave the
  previous track playing untouched and were silent before, which is exactly what made "the music
  did not change" unattributable. Don't reformat the `[music]` line --
  `tools/headless/probes/bosstrain_music.txt` greps it.
- **A checkpoint revert restores NO scene state -- music, backdrop and floor all survive it**, and
  `GameEventList.RevertToCheckpoint` walks back to the nearest checkpoint AT OR BEFORE the death,
  which can be an earlier SECTION than the one you died in. That combination is card 4a3b22b7:
  `InsaneBossI` (Boss Train) is the one level that walks three sections in a run, its alien-base
  transition sits at script index 33 while the next checkpoint is 36, so dying in that ~10 s window
  rewound the script to the SPIDER BOSS (checkpoint 24) with Level 3's backdrop and track still up
  -- Level 2's set-piece replayed on Level 3's scenery, and the second arrival at "level 3's
  bosses" changed no music because nothing had put it back.
  **The fix is the shape to copy if another level ever grows a second section:** the section is
  STATE, not an edge. `InsaneBossI.ApplySection` is idempotent, each `Go*` handler asks for a
  section, and every checkpoint declares its section (`CheckPointSection` beside its
  `SetLastEventAsCheckPoint`) and re-asserts it on entry -- a no-op on the forward pass, the fix on
  a revert. `GameEventList.OnCheckPointReached` passes the checkpoint EVENT so a level can tell its
  checkpoints apart. **Verify as DATA with console `eaBossTrain()`** (`Compat/BossTrainTest.cs`,
  `eval BossTrain` under `eahl`): it checks every checkpoint's declared section against a forward
  walk of the REAL script, then drives the REAL `RevertToCheckpoint` from the alien-base window and
  reads the section + track back, with the pre-card behaviour as the negative control. A checkpoint
  on a DIFFICULTY-CONDITIONAL event is the one gap: progressList tests the difficulty range before
  it tests `checkpoints`, so such a checkpoint does not re-assert on the tiers that skip it (today
  harmless -- see the caveat in InsaneBossI.cs). **It is
  DESTRUCTIVE** (it moves the script position and the section) -- throwaway `?level=InsaneBossI`
  boot only. A playthrough cannot cover this: eight full AI soaks, up to 25 deaths each, hit
  `revert 33 -> 24` and never once died one index later.
- **AT MOST ONE START OF ANY GIVEN CUE PER GAME TICK (card 8732568e).** Reported as *"multiplayer
  games (on a joining peer side) seem to have a lot of loud explosion effect sounds. I suspect we
  get a big packet with a bunch of dead enemies and play the sound a couple of times in the same
  frame perhaps?"* -- right on both counts, and the second half is what makes it read as LOUD
  rather than as busy: **N copies of the SAME sample started at the SAME instant are
  phase-identical and sum COHERENTLY**, amplitude x N, i.e. **+20*log10(N) dB**. Ten simultaneous
  `expl1` is one explosion twenty decibels louder, not ten explosions. (Two copies even a few ms
  apart sum incoherently and read as two hits, which is why the window is one tick and not longer.)
  **Exact at the ATTACK TRANSIENT, which is what "loud" means here; the tail is gentler.**
  `Spawn`'s 5% humanize gives every `vary` cue a +-2.1% pitch, so two copies drift a half period
  apart within ~12 ms at 1 kHz and the body decays toward +10*log10(N) plus comb filtering. The
  onset is still coherent, and the onset is the bang.
  - **A joining peer hits it harder but the defect is not net-specific.** `EvDeath` rides the
    reliable ORDERED lane and a client applies a whole batch in ONE `DrainRx`, inside one tick --
    so deaths the host spread over several of its own frames all fire their cue on the same client
    tick. Offline the same shape exists (a bomb clearing the screen) and is fixed with it, which
    is what makes the change verifiable with no second machine.
  - **PER CUE, not a global "one sound at a time".** The ticket offered both. A global cap would
    silence deliberate LAYERING -- `SpiderBoss.BeginDeathThroes` plays "spiderbossdeath" and
    "head asplode" together, `CastDisplayer` stacks three -- and none of that is the problem,
    because two DIFFERENT samples do not sum coherently.
  - **IT APPLIES TO `PlayCue` AND NOTHING ELSE. The rule is the SURFACE, not the cue.** `Play`
    RETURNS the instance and `PlayText` keeps it in `_speech`, and a caller that keeps a handle is
    a caller a null can break. **`PlayText` is where that bites**: it stops the in-flight announcer
    line and THEN assigns `Spawn`'s result, so coalescing it would stop the first line, return
    null and leave the announcer SILENT -- worse than either the old or the intended behaviour.
    An earlier cut of this said "looping cues are the only kept handles"; that is false
    (`StarMine`'s `targetacquired` is another) and it is what the surface rule replaces.
  - **Looping cues are exempt too, independently** (`lazershot`, `lazercharge`, `bees`). Nothing
    in the game `PlayCue`s one today, so this is defence in depth: a sustained cue folded into an
    earlier start would be an unstoppable loop, since `PlayCue` discards the handle and nothing
    reaps a Playing instance. (`eaSfx.burst` refuses a looping cue for the same reason.)
  - **ONE DELIBERATE OPT-OUT, `PlayCue(cue, allowSameTick: true)`.** `SpiderBoss.CollidesWith`
    plays `bugdies` TWICE in a row -- verbatim 2008 (`src_decompiled` line 673-674), i.e. an
    authored +6 dB emphasis on landing a beam on that boss, mirrored for the peer in `NetPlayFx`.
    Coalescing it would quietly halve a set-piece's hit. **Keep it at one call site**; every other
    same-tick repeat in this game is the pile-up. `BrainBoss`'s two `expl2` calls are NOT this --
    they sit in separate random branches and their coincidence is exactly what is being fixed.
  - **The window is `SoundManager.Update`'s own tick counter**, which `Game1.UpdateInner` pumps
    unconditionally BEFORE `base.Update`, the collision sweep and the net rx drain -- so everything
    one tick does falls under one number, and it keeps advancing under a pause (where the menus
    still play cues). Not `WorldTime`, which freezes there and would coalesce a menu cue forever.
  - **The decision is taken BEFORE the effect is loaded, deliberately.** On a machine with no audio
    device `GetEffect` caches null and `Spawn` bails early, so a decision behind that load could
    never be exercised headlessly -- which is where this is verified.
  - **`admitted` is not `played`.** The decision is counted before the effect loads and before the
    32-instance `Default` cap, which is what makes it readable on a box with no audio device;
    `played` only counts a real `inst.Play()`, and the gap between them is the cap plus any load
    failure. Read both in the browser, where the cap is live.
  - **Verify as DATA: `eaSfx()` / `eval SfxState`** (requests / admitted / played / coalesced /
    which cues),
    `eaSfx.reset()`, `eaSfx.burst(cue, n)`, and the whole suite `eaSfxBurst()` /
    `eval SfxBurstTest` (`Compat/Net/SfxBurstTest.cs`, menu-only and leave-no-trace). **A
    screenshot cannot see any of this and headlessly neither can a microphone** -- eahl silences
    the mixer and a container has no audio device at all. Its section 3 drives the REPORTED path:
    eight real `UFO` puppets killed by eight `NetPuppets.OnRemoteDeath` calls in one tick, which
    ask for eight `expl1` starts and get one. `?sfxcoalesce=0` is the A/B seam (a pure feel toggle,
    so deliberately OUT of `DebugFlags.Active` unlike its `?netstaleguard=0` lookalikes -- it
    changes no gameplay state, no position, no score and no packet). Pinned by the pair
    `tools/headless/probes/sfx_coalesce.txt` + `sfx_coalesce_off.txt`; read them as one probe,
    since the suite flips the property directly and cannot show the FLAG reaching it (`?sfxcoalesce=0`
    prints its own boot line, the `?seed` convention, because it is out of `Active` and so absent
    from the `[debug] flags active:` dump). Mutation-tested eight ways.
  - **The suite's leave-no-trace takes real work, not just pruning.** Section 3 drives REAL UFO
    deaths: they award score, spawn `Explosion`s into the live bin and add real screen TRAUMA
    (measured 0.000 -> 0.930 after ONE run, i.e. a visibly rattling main menu). It restores all
    three and ASSERTS each -- `Juice.SetTraumaForTest` exists for that. A menu-runnable suite that
    drives a real death path must do the same.
- **`Songs.LastSignal`** (`lastsignal.ogg`) is the end-of-level text-crawl theme in `CreditsScene`
  (played at rate 1.0). It replaced the bank's `sjaakslow` cue — both that cue and its ogg are
  gone; **don't reintroduce them.**
- Pipelines (bank cracking, loop points, external cues): `tools/audio/` — see tools/CLAUDE.md.
