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
  (transparent, bottom/right only — content keeps its top-left coords) and stamps the original
  ("logical") size into the DDS header's reserved dwords (offsets 32/36 + `"LOGD"` marker).
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
  `DriftingStars` use instead of a private `SpriteBatch`) and in `DrawEffect`. **Test harness:**
  `build_textures.py --padtest <px>` grossly over-pads every `.dds` so any missed padded-vs-logical
  site shows an obvious ~px artifact in play; ship with `--padtest 0` (minimal mult-of-4 pad).
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
  `eaTexViewer` (`?texviewer`), `eaNetSim` (any `?net=` boot). GOTCHA: range inputs need
  `autocomplete='off'` or Chrome's form restoration re-seeds them post-load and desyncs from the
  defaults.
- Console QA helpers (via `Compat/DebugInput.cs`): `eaPress`/`eaHold` (input), `eaHitboxes()`,
  `eaShake()`, `eaHitstop(ms)`, `eaSlowmo()`, `eaPreloadExport()`, `eaWallPerf(true)`+`eaWallStats()`.

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
    ?wallfacelight= ?wallfaceangle= ?walltoplift= ?wall3dbands= ?wallwisps= ?wallwispspeed=` ·
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
semantics). Remaining: card 11.5 (hardening: TURN decision, reconnect/grace, UX polish).

- **Flags:** `?net=host` / `?net=join` opt a session in (in `Active`); `?room=<name>` picks
  the loopback room (BroadcastChannel `eanet-<room>`, default `dev` -- parallel test pairs
  must use distinct rooms); `?netlog` = verbose per-event logging; `?aiplayer` forces the
  LOCAL ship onto the existing AI branch (`PlayerShip.EffectiveController`) for unattended
  soak tests; `?netscript` (pair with `?level=Level1`) replaces the level's event list with
  a compressed ~60s script firing every replicated beat type (message, warning, background
  ops, checkpoints, music switch, victory) -- the purpose-built two-tab verification for
  script replication (`GameScene.PopulateNetScriptTest`). Card 11.4 adds `?rtc` (a
  `?net=` boot uses the REAL WebRtcTransport: host prints its room code to the console,
  join passes it via `?code=ABCDE`) and `?signal=<url>` (override the signaling server;
  a local rig runs `uvicorn main:app --port 8091` in `server/signal` and boots with
  `?signal=ws://localhost:8091/ws`). Card 40334a8f adds `?netlag=<ms>` / `?netloss=<0-100>`
  (impair INBOUND traffic -- see the impairment bullet below). **No `?net` flag = the net layer is never constructed
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
    outside `#app`, only on a `?net` boot) + console `eaNetSim(lag, loss, jitter)`.
  - **`?netloss=100` starves the ship stream so the 3s peer timeout fires while the handshake
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
  WebcamAliens selection no-ops) and `EvLaunch` mirrors the launch on the client
  (`MenuScene.NetLaunchMirror` -- same fade/warm path, difficulty locked, starter
  Keyboard). Turbo is forced to 100 while a session is Active (`Game1.Update`).
- **v4 handshake + match-end (card 11.4):** hello/welcome carry an 8-byte build hash
  (FNV-1a of `window.eaBuildHash`; deploy.yml stamps a sha256 of `blazor.boot.json` at
  publish, dev builds read 'dev') + a flags byte. Hash mismatch -> `MsgReject` -> "Update
  required" notice both sides (a stale-cached client can never desync a session); menu
  sessions also reject if EITHER side has `DebugFlags.Active` (dev `?net=` sessions are
  anything-goes). Match-end: any player leaving a MENU session (quit, tab close, drop,
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
  role replies Welcome). Card 11.3 bumps the protocol to v3 and adds the shared-state
  events: EvMessage/EvUnlock/EvBackground/EvMusic/EvCheckpoint (script beats), EvReset
  (host LoseLife branch), EvVictory, EvPause (either peer), EvTetherBreak (either peer).
  Peer loss = JS `pagehide` bye OR a 3s stream timeout; the ship stream doubles as the
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
- **Remote ship:** `ControlDevice.Remote` (APPEND-ONLY enum position). Joins via
  `oracle.AddPlayer(Remote)` on the first alive stream (or is spawned by the GameScene's
  own SpawnAllPlayers reset flow -- NetSession adopts either). `PlayerShip.Update` case
  Remote -> `NetSession.DriveRemoteShip`: position sampled from `ShipStateBuffer`
  ~100 ms behind the newest sample (velocity-extrapolated max 250 ms on underrun), speed
  zeroed; shots re-fired locally through the real `FireAt` path from the replicated firing
  state; bombs arrive as EvBlast -> `NetDoBlast` (no local bomb-count gate). Remote ships
  take NO local damage (owner decides its own hits; death arrives as the alive-flag edge ->
  local explosion FX, slot stays reserved for respawn) and CANNOT take powerups locally --
  the owning peer collects on its own screen and the pickup arrives as a claim. Hues: the join side swaps slot hues on connect so host=white / join=purple
  on BOTH screens. The puppet's render clock advances on REAL time (never turbo/slowmo/
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
  whole path is dormant unless the cheat is on. `ControlDevice.RemoteFriend` is APPEND-ONLY.
- **Known limits (by design -- next cards):** a dead local player will NOT respawn while the
  remote puppet lives (LoseLife triggers on AllShipsDead); roster is exactly two peers;
  DevCommentEvent commentary is not replicated (profile-local setting). Boss puppets are
  best-effort (the harness caveat): deep Update-reached attack poses may diverge until their
  state extras grow (the SpiderBoss debris death + BrainBoss/FakeBoss multi-phase asplode do not
  play on the client -- an attributed remote death removes the puppet). A one-time `pupPops`
  burst can appear during the FIRST wipe transition of a session (transient, self-heals,
  cosmetic under the death FX -- follow-up card).

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
