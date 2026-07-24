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
- **A clamped source rect does NOT stop the filter reaching the pad — hence the 4px edge gutter.**
  `LinearClamp` clamps at the TEXTURE border, not at the source rect, so a destination pixel whose
  centre lands in the last half texel bilinearly blends the last content texel with texel `[LW]`.
  While that texel was transparent black, the final ~1px of every tile lost up to 50% of its RGB
  **and alpha** — a hairline at every tile boundary, dark over the opaque Mars sky, bright where the
  `marshills` silhouettes sit over it (Trello `4ddcd13f`; measured -64 luminance in the sky band).
  `build_textures.py`'s `edge_gutter()` therefore replicates the logical edge into the first 4 px of
  the pad (last column right, last row down, corner), which makes the filtered result identical to a
  true clamp at **any** pad size, and keeps the sampled 4×4 BC3 blocks free of transparent-black
  endpoints on non-mult-of-4 art (`marsloop*` are 1587/1588 wide). Only 4 px are filled so the
  `--padtest` canary keeps its transparent hole. Guard: `tools/textures/check_pad_bleed.py` (asserts
  the gutter matches the edge on every shipped `.dds`) — **re-run it after any `build_textures.py`
  rebuild**. Watch for this any time a texture is TILED or stretched far past its native size.
- **`?bgfreeze=<designX>`** stops every background/foreground layer scrolling and parks a tile
  BOUNDARY of each at that design column (`Background.Update`). The Mars/alien-base layers scroll at
  six different speeds, so a tiling/wrap/parallax artifact can only be inspected once it holds
  still. Caveat: sub-pixel artifacts like the pad bleed vary in strength with where the boundary
  falls relative to render-target pixel centres, so sweep the FRACTIONAL part to cover phases — one
  frozen frame is one phase, not the worst case. GOTCHA: freezing every layer at the SAME design
  column stacks layers that normally never coincide — at `?bgfreeze=0` the alien base's two
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
  mirrored and TALL, shapes no shipped background uses — through the REAL `Draw`, then censuses the
  live layers' per-frame `drawn` / `off-screen` counts. A screenshot cannot verify this cull at all,
  since every shipping configuration errs invisibly; read the decisions as data instead.
- **Preload / hitch tooling (`Compat/LoadProfiler.cs`):** `?loadlog` times every texture decode,
  flags decodes outside a level's preload phase, accumulates a per-level set the preloader feeds
  back, and exports via console `eaPreloadExport()` → `wwwroot/Content/preload/manifest.txt` (read
  by all builds; release never writes). An **always-on frame-hitch watchdog** logs `[hitch] <ms>ms
  frame in <level>` for any tick > 120ms (not gated by `?loadlog`), skipping preload + boot warm-up.
  A still-hitching level is a manifest DATA gap — fix by playing with `?loadlog` +
  `eaPreloadExport()`, not by code.
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

## Debug flags & tuning conventions

- All URL flags parse once at boot in `Compat/DebugFlags.cs` (wired via `index.html`
  `getDebugQuery` → `Pages/Index.razor.cs`). **No query = normal boot; tuning overrides are null =>
  the baked `Default*` consts, so a shipped build is byte-identical.** When the user settles on
  values, bake them into the consts and keep the flag as an A/B override.
- `DebugFlags.Active` (the `[debug] flags active` console line) lists only flags that hijack
  boot/levels (`?level=`, `?brainboss`, `?texviewer`, ...). Pure render/feel toggles
  (`?metalscore`, `?slowmotrail`, `?holofilter`, shake/hitstop, reticle size, ...) stay OUT of it.
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
  `eaBinTest()` (the ComponentBin lifecycle scenario suite — run from the main menu),
  `eaKillShips()` (asplode the locally-owned ships to force a death/reset on demand),
  `eaBgCull()` (the background tile-cull oracle — run from inside a level).

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
  the live bin and prints PASS/FAIL — 20 assertions across 6 scenarios (the four lifecycle
  ones plus the two collision-pass ones in the bullet above). A few of those are PRECONDITIONS
  rather than assertions about the code, and a failed one short-circuits the rest of its
  scenario, so read the FAIL line rather than the tally. `eaKillShips()` asplodes every
  locally-owned `PlayerShip`
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
  alpha. Used by the background-fog `FlyingSpider` (body+wings fade as one silhouette).
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
  (baked `DefaultSizeFactor` 0.85) scales sprite AND box hitbox together; `?flyspiderscale=`.
  Fast-boot a dense endless swarm with **`?level=Level2&flyspiders`** (background variant, the only
  user of the group-flatten RT round trip) or **`?flyspiders=fg`** (foreground, same sprites, NO
  flatten) -- the A/B built for the frame profiler, since the real level only reaches this state
  minutes in. Measured (frame profiler, focused): background 7.3ms/frame, scene 6.6ms, 98 GL calls,
  137fps headroom vs foreground 2.8ms/frame, scene 2.1ms, 42 GL calls, 356fps headroom. **Read that
  as indicative, not a clean per-spider flatten cost** -- background flying spiders have
  `Collides=false` so they are never killed and accumulate, while foreground ones die on the ship;
  the populations are not controlled. What the data does show is the mechanism: GL calls per frame
  roughly double, which is exactly what a per-group RT round trip does on a per-CALL-cost backend.
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
  - **756-v1 ships with NO mip chain**, so a high `?wallsidetile` minifies with bilinear and
    nothing else, and the far end of a shaft aliases — weigh it on `preview_wall3d.py --ladder`
    (one tower per tiling). That tool's `sample()` is bilinear CLAMP on purpose: it models
    `DrawGeometry3D`'s `LinearClamp` exactly, so it neither invents a moire (point sampling would)
    nor prettifies the sheet's own 8→0 wrap (wrapping would).
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
    POP IN/OUT) + `?level=Level3&wallpoptest` (ten slow-scroll poptest grids). `?walltoplift` is
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
- **Protocol (`Compat/Net/NetProtocol`, little-endian binary, 1-byte type, v2):** the 3
  layers -- `MsgShipState` (~30 Hz real-time cadence: pos, vel px/ms, last-fire aim,
  alive|firing flags, shotsPerSec, bulletLife -- 31 B), `MsgWorldSnapshot` (see the
  World-snapshots bullet below), `MsgEvent` envelope with a monotone ushort seq
  (EvSpawn full base state + spawn extras / EvDeath netId+killer+pos+points / EvBlast
  pos+level / EvClaim netId+killerSlot / EvScoreSync lives+scores) + `MsgHello`/
  `MsgWelcome` handshake (protocol version byte; both sides Hello until paired, opposite
  role replies Welcome; **v5** adds the host-granted primary slot byte -- card 4d904410).
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
- **World snapshots (`MsgWorldSnapshot` 0x20, stream lane, host->client, 60ms cadence):**
  round-robin cursor over the live NetId set, <=16 length-prefixed entries/packet (~500B).
  Entry = netId + typeIdx + the generic base block (`NetBaseState`: pos, observed vel
  px/ms, rotation, curframe x64, scale x256, hp) + per-type state extras. A snapshot entry
  for an unknown id self-heals: it REBUILDS the puppet from the snapshot (default spawn
  extras) unless that id died locally < 3s ago (claim in flight).
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
- **Score/lives:** immediate local generous crediting + host-authoritative `EvScoreSync`
  at 1Hz -- the client adopts `max(local, host)` per slot (monotone within a life; combo
  multiplier divergence self-corrects upward) and lives verbatim.
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
  `snapRx/snapEnt` climbing with `snapUnk` small and non-climbing at steady state,
  `pupPops` near 0, and the claim counters telling the kill story (`clTx` client-side ~=
  `clRx` host-side; `clKill` = claims that settled a live enemy, `clPaid` = generous
  payouts for already-dead enemies -- a nonzero `clPaid` IS the double-claim proof).
  **Two-tab test recipe:** the tabs must BOTH be visible (a backgrounded tab's rAF drops
  to ~1Hz and its peer times out / crawls) -- use two Chrome WINDOWS side by side:
  `?level=Level1&net=host&aiplayer&invuln&room=<r>` + same with `net=join`; both ships play
  themselves via `?aiplayer`, then read both consoles. `?room=` must be fresh per test pair.
  Add `?binlog` to both when the run is about lifecycle (it is the detector for a purge filter
  or pause freeze eating a puppet/banner). For a death/reset, KEEP `?invuln` on both and call
  `eaKillShips()` in each console -- `Asplode()` only guards on `!IsDead`, so the helper bites
  through invulnerability, and leaving the flag on is what keeps the rest of the run from
  dying at random. `AllShipsDead` needs BOTH ships down, so fire it on both tabs.
  **`snapUnk` climbing is not by itself a leak:** the host keeps snapshotting an entity for a
  turn or two while a client claim is in flight, and the client deliberately leaves that id
  dead, so `snapUnk` tracks `clTx` at roughly 1.1-1.4 per claim. Judge it against the claim
  rate -- flat `clTx` with climbing `snapUnk` is the shape that means trouble.
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
    `?netjip` two windows -> `[net]` metrics.
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
