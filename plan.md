# Revenge of the Evil Aliens — Web Port Plan

Reviving a 2008 Xbox Live Indie Game (XNA 3.x, C#) as a browser-playable game,
hosted as a static site on GitHub Pages. The source was lost; it was recovered by
decompiling the shipped Xbox 360 package.

This document is written so each remaining stage can be executed **independently with
fresh context** (a new chat session that has never seen the others). Read
"Quick reference" + "Key concepts" first, then jump to the stage you're doing.

---

## Quick reference

| Thing | Value |
|---|---|
| Web project (the port) | `web/EvilAliensWeb/` |
| Decompiled reference source (read-only) | `src_decompiled/` |
| Extracted game assets (from the Xbox pkg) | `extracted/584E07D1/Content/` |
| Compatibility shims | `web/EvilAliensWeb/Compat/` |
| One-off fixup scripts | `tools/` |
| Original Xbox package (53 MB, provenance) | `47D5BF15FF8CC1DB145E8BA9542C43FD3209A09A58` |

**Build:** `cd web/EvilAliensWeb && dotnet build -c Debug`
**Run (dev server):** `dotnet run --project web/EvilAliensWeb -c Debug --urls http://localhost:5280`
**Run + screenshot (preferred):** use the Claude "preview" tools against the `eaweb`
config in `.claude/launch.json` (`preview_start` → `preview_screenshot` → `preview_console_logs`).
Browser console **must** be checked — WASM errors surface there, not in the build.

**Installed toolchain (already set up on this machine):**
- .NET 8 SDK + `wasm-tools` workload (Emscripten, mono browser-wasm).
- `ilspycmd` 8.2 (decompiler) — invoke with `DOTNET_ROLL_FORWARD=LatestMajor ilspycmd ...`.
- KNI 4.1.9001.* NuGet packages (the XNA-4.0-compatible engine with a Blazor/WebGL backend).
- Python 3.12 (`python`, not `python3`; always set `PYTHONIOENCODING=utf-8`).

---

## Current status

- [x] **Stage 1 — Toolchain spike.** Proved KNI→Blazor→WebGL renders + animates in-browser
  with a clean console. (A throwaway `SpikeGame.cs` bouncing box; safe to delete once the
  real game boots.)
- [x] **Stage 2 — Whole game compiles to WASM.** All ~40k lines (222 game files) build
  against KNI with **0 errors**. Decompiler artifacts auto-fixed; the XNA 3.x→4.0 API gap
  bridged with shims. *It compiles but does not run yet.*
- [x] **Stage 3 — Content pipeline (assets load).** The shipped `.xnb` are XNA 3.1 /
  Xbox 360 / LZX-compressed and KNI can't read them. Built a Python unpacker
  (`tools/xnb/`) that LZX-decompresses (faithful port of KNI's own decoder) and decodes
  the Color/Dxt1/Dxt3/Dxt5 surfaces (Xbox is big-endian → 16-bit byte-swap on DXT;
  textures turned out to be **linear, not tiled**). Converted everything to web assets
  under `wwwroot/Content` (112 textures→PNG, menu font→atlas+metrics, 1 curve, +`.dat`/
  level text copied), all **lowercased** for case-sensitive Pages. A runtime
  `Compat/WebContentManager.cs` loads them so the game's `Content.Load<>()` calls work
  unchanged. Verified in-browser (textures of every format + the SpriteFont render, no
  `ContentLoadException`). See the Stage-3 notes below before doing Stage 4.
- [x] **Stage 4 — First boot / playable core.** `Game1` boots in-browser through
  splash → "Press Start" → main menu → controls screen → playable/attract gameplay
  (4-player HUD, scrolling DXT backgrounds, enemies, combos), with **0 exceptions**.
  Threading made synchronous; XBLIG sign-in gate replaced with the PC keyboard path
  (the keyboard-read block was `#if WINDOWS`-stripped from `InputHandler.Update` —
  reconstructed it); per-scene `ContentManager`s rewired to `WebContentManager`;
  `GraphicsProfile.HiDef` (Reach rejects the game's 32-bit index buffers); audio +
  shaders + bloom stubbed for their later stages. See the Stage-4 notes below.
- [x] **Stage 5 — Shaders (bloom + sprite effects).** The lost `.fx` were rewritten in
  HLSL (`tools/shaders/src/`) and compiled offline to KNI MGFX v10 GLSL blobs (`.mgfxo`)
  via MGCB targeting **BlazorGL**; `WebContentManager` loads them with `new Effect(gd,bytes)`.
  Gamma (composited on the present blit), the full bloom post-process (real
  `ResolveBackBuffer` + render-target ping-pong), and all the sprite effects
  (colorize/lighten/fade/interpolate via one master shader compiled into 13 variants,
  + outline/staticAlpha) render in-browser with 0 console errors. See the Stage-5 notes.
- [x] **Stage 6 — Audio (XACT → modern).** The Xbox XACT banks (`.xwb`/`.xsb`) were
  cracked offline in pure Python (`tools/audio/`): SFX/speech are big-endian PCM, the 8
  music tracks are **xWMA** (decoded via PyAV by rebuilding a `RIFF/XWMA` container). SFX +
  speech play natively on KNI `SoundEffect`/`SoundEffectInstance` (instance caps, pitch/volume
  variation, loop flags, Default/Speech gains); **music** plays through a WebAudio JS layer
  (`wwwroot/index.html` `eaMusic`, driven by `Compat/MusicInterop.cs`) for seamless loop
  points + the BrainBoss pitch sweep. Speech is re-cast with ElevenLabs (Brian announcer,
  Victor narrator). Verified in-browser: menu music + attract-mode SFX, 0 exceptions. See the
  Stage-6 notes below.
- [x] **Stage 7 — Saves & awardments persistence (localStorage).** The in-memory save tree
  (`/eaweb_save/EvilAliens/`) is mirrored to browser **localStorage**, so settings, unlockables,
  awardments and level-select screenshots survive a reload. Done at the `StorageStub` layer (a
  WASM-MEMFS ↔ localStorage mirror) so every `Savable` subclass is untouched: hydrate once before
  the first read, flush changed files on each container `Dispose`. Verified in-browser (toggle a
  setting → reload → it's remembered). See the Stage-7 notes below.
- [x] **Stage 8 — GitHub hosting + Pages deploy (public).** Live at
  **https://coamithra.github.io/RotEA26/**, auto-rebuilt on every push to `main` by
  `.github/workflows/deploy.yml` (CI does the `dotnet publish`; Pages only serves). The real
  catch was **case-sensitivity**: the published build white-screened on the Linux Pages host
  (`ManagedError: content/gfx/splash/ealogo.png`) because the C# loaders requested a lowercase
  `content/` root while the on-disk dir is `Content` — invisible on case-insensitive Windows.
  Also disabled `PublishTrimmed` (Release trims by default and would strip the XmlSerializer
  save types). See the Stage-8 notes below.
- [x] **Stage 9 — Polish.** Keyboard controls-help screen un-stubbed (web players see the
  keyboard layout, not just the Xbox joypad); real browser **fullscreen** (corner button + the
  in-menu option, via JS interop — KNI's `IsFullScreen` is a no-op on BlazorGL); **favicon +
  social/SEO meta**; **on-screen touch controls** (D-pad / fire / back, fed through the
  `DebugInput` seam) for phones; and the headline win — **WASM download trimmed 25.8 MB → 9.6 MB
  uncompressed (~2.9 MB brotli)** via `TrimMode=partial` + `InvariantGlobalization`, **verified
  on a local Release publish** so it can't repeat Stage 8's white-screen. See the Stage-9 notes.
- [x] **Stage 10 — Unified hi-res render path.** The whole frame — legacy 800×600 art (upscaled
  via one shared `RenderScale.Matrix`) AND the hi-res art (menu title, channel-flip splash) drawn at
  native density — now renders into ONE scene target sized to the window's 4:3 letterbox (capped at
  1440px tall), so everything shares one bloom, one gamma, one present blit. The Stage-9 bolt-on
  native-res `HiResOverlay` pass (with its own separate glow-bloom) is **deleted** — the title now
  blooms through the *main* bloom like everything else. Verified in real Chrome (menu, level-select,
  Level 1 gameplay with the crisp hi-res Earth backdrop, both splash paths incl. the channel-flip,
  and a window resize) with 0 console exceptions. See the Stage-10 notes below.
- [ ] Stage 11 — Online co-op multiplayer (networked couch co-op)

---

## Repository layout

```
RotEA26/
  47D5...A58                  original Xbox 360 STFS package (the only surviving artifact)
  extracted/584E07D1/         full dump of the package
    EvilAliens30.exe          the .NET assembly that was decompiled
    Content/                  154 .xnb, 2 .wmv, XACT audio banks, levels  <-- game assets
  src_decompiled/             ILSpy output = the Xbox BUILD's source (reference only)
  web/EvilAliensWeb/          THE PORT (KNI Blazor WASM project)
    EvilAliensWeb.csproj
    Program.cs / App.razor / Pages/Index.razor(.cs)   Blazor bootstrap + game loop driver
    wwwroot/index.html        host page + JS interop (initRenderJS / tickJS)
    Game/                     the ported game code (copied from src_decompiled + auto-fixed)
    Compat/                   shims (see Key concepts)
  tools/                      Python fixup scripts (reproducible transforms)
  plan.md                     this file
```

---

## Key concepts (read before any stage)

### 1. The recovered source is the **Xbox build**, not the PC build
Decompiling the Xbox package only yields code that was compiled **into the Xbox binary**.
Anything under `#if WINDOWS` / `#if !XBOX` / `[Conditional]` was stripped and is **gone**.
- The XBLIG "awardments" (fake-achievements) UI **did survive** — it's in
  `Game/EvilAliens/`: `Awardment*.cs`, `Achievements.cs`, `SubMenuAwardments*.cs`.
- If a PC build `.exe` is ever found, decompiling it would recover the PC-only paths
  (and would likely be plain XNA with no GamerServices). Until then, **re-create** any
  missing PC-only behaviour rather than trying to recover it.

### 2. KNI = the engine
KNI (NuGet `nkast.Xna.Framework.*`, namespace `Microsoft.Xna.Framework`) is an actively
maintained MonoGame fork that follows the **XNA 4.0** API and has a Blazor WebAssembly/WebGL
backend. MonoGame itself has no web target. The game was **XNA 3.x**, so 3.x→4.0 breaking
changes had to be bridged (see shims).

### 3. The game loop on the web
`wwwroot/index.html` defines `initRenderJS` (called once from `Index.razor.cs` `OnAfterRender`)
which starts a `requestAnimationFrame` loop calling back into C# `TickDotNet()`, which creates
the `Game` on first tick and calls `Game.Tick()` each frame. **Currently `Index.razor.cs`
instantiates `SpikeGame` — switch it to `new EvilAliens.Game1()` when Stage 4 is ready.**

### 4. The shims (`Compat/`) — what's faked and what's still a no-op
- `GamerServicesStub.cs` — entire `Microsoft.Xna.Framework.GamerServices` namespace.
  `Gamer.SignedInGamers` is always empty (so per-gamer loops no-op), `Guide.IsTrialMode=false`
  (full game unlocked), message boxes / sign-in / marketplace are no-ops.
- `StorageStub.cs` — replaces `Microsoft.Xna.Framework.Storage` (KNI's storage package was
  removed from the csproj). Saves go to the **WASM in-memory FS** → they work *within a
  session* but don't persist across reloads yet (Stage 7 fixes that).
- `Xna3Compat.cs` — XNA 3.x→4.0 bridges. **Some are real, some are NO-OPS that will affect
  visuals until later stages:**
  - REAL: `SpriteBatch.Begin(SpriteBlendMode,...)` overloads, `RenderTarget2D.GetTexture()`,
    indexed `SetRenderTarget`, stub enums/types (`TextureUsage`, `ShaderProfile`, `EffectPool`).
  - NO-OP (revisit in Stage 5): `Effect.Begin/End`, `EffectPass.Begin/End`,
    `GraphicsDevice.ResolveBackBuffer` (bloom). These compile and run but don't apply shaders.
  - TODO(visual): the `SpriteBlendMode` int→`BlendState` mapping is a guess; verify against
    real rendering (XNA 3.x enum was `None`/`Additive`/`AlphaBlend` — confirm the int values).

### 5. The fixup scripts (`tools/`) — how `Game/` was produced
`Game/` is **derived** from `src_decompiled/` by running, in order:
`fixup_transforms.py` (ref-cast + base-call artifacts) → build → `fix_ctors.py`
(`_002Ector`→`new T`) → `fix_apis.py` / `fix_apis2.py` / `fix_apis3.py` / `fix_apis4.py`
(3.x→4.0 textual edits) → `fix_quad.py` (Quad render block). If you ever need to re-derive
from scratch, re-run them in that order. Otherwise edit `Game/` directly.

---

## Stage 3 — Content pipeline (assets load)

**Goal:** `ContentManager.Load<T>(...)` calls in the game succeed in-browser, so the real
art/fonts/levels are available.

**Context:** The game loads ~154 `.xnb` files plus level data from `extracted/.../Content/`.
`.xnb` is XNA's compiled-content format; XNA 3.x `.xnb` may not load in KNI (format version
differences), and shader/audio `.xnb` definitely won't. The robust approach is to recompile
from **source assets** through KNI's content pipeline (`.mgcb`), but we only have the compiled
`.xnb` — so we must first unpack what we can.

**Steps:**
1. Triage the `.xnb` by type (Texture2D, SpriteFont, Effect, SoundEffect/Song, custom).
   Textures + fonts are the bulk and the priority.
2. Try the cheap path first: copy the existing `.xnb` into the project Content and see if KNI's
   runtime `ContentManager` loads XNA-3.x textures/fonts directly. Wire content via a
   `KniContentReference` to a `.mgcb` **or** load `.xnb` as embedded/streamed assets. (See the
   KNI sample `WebGLxnaProj` — `ResourceContentManager`, `KniContentReference`,
   `<KniPlatform>BlazorGL</KniPlatform>`, and `nkast.Xna.Framework.Content.Pipeline.Builder`.)
3. For anything that won't load: unpack `.xnb` → PNG/WAV (texture `.xnb` are easy to strip;
   there are open `.xnb` unpackers, or parse the header — it's documented), then recompile
   through the KNI `.mgcb` pipeline at build time.
4. Set `Content.RootDirectory` appropriately and make sure paths match how the game asks for
   them (e.g. `"GFX/Sprites/arrow"`). Note original folder casing.

**Gotchas:** shaders (`Content/Bloom/*`, `Content/GFX/Effects/*`) and audio
(`Content/SFX/*.xwb/.xsb/.xgs`) are handled in Stages 5/6 — don't block Stage 3 on them.
Fonts: XNA `SpriteFont` `.xnb` may need recompiling from a `.spritefont`.

**Done when:** the game can load at least the menu/HUD textures and a font without throwing
`ContentLoadException`.

### Stage 3 — DONE. How it was wired (read before Stage 4)
- **Unpacker:** `tools/xnb/` — `lzx.py` (LZX, line-for-line port of KNI's `LzxDecoder`,
  decompiled to `LzxDecoder.decompiled.cs`), `tex.py` (Color/Dxt1/Dxt3/Dxt5 → RGBA; everything is
  **big-endian Xbox**, so `Color` is stored **ARGB** [A,R,G,B] (NOT BGRA, or you get a blue
  cast + a wrong alpha mask); DXT needs a 16-bit byte-swap; surfaces are **linear, not
  tiled**),
  `xnb.py` (container + Texture2D/SpriteFont/Curve parsers; XNA-3.1 enums: Dxt1=28,
  Dxt3=30, Dxt5=32, Color=1). `unpack.py` drives it all → `wwwroot/Content`.
  **Re-run with `python unpack.py`** (idempotent; wipes+rebuilds `wwwroot/Content`).
- **Output layout:** every path **lowercased** (Pages is case-sensitive; the game asks
  with inconsistent casing). Textures→`<name>.png`; font→`<name>.fnt.png`+`<name>.fnt`
  (binary metrics, see `unpack.py:write_fnt`); curve→`<name>.curve`; `.dat`/`level3.txt`
  copied verbatim.
- **Alpha is PREMULTIPLIED on export** (`unpack.py:to_image`). XNA's pipeline premultiplied
  by default and KNI's default `SpriteBatch` blend (`BlendState.AlphaBlend`) is the
  premultiplied variant, but `Texture2D.FromStream` does NOT premultiply — so without this,
  transparent pixels with non-black RGB bleed in (the Dxt3 font atlas has white-on-
  transparent → text renders as solid "white squares"; soft sprites get bright halos).
  **Implication for Stage 5:** when verifying the `SpriteBlendMode`→`BlendState` mapping,
  treat all content as premultiplied (e.g. 3.x `Additive` → KNI `BlendState.Additive`,
  which is also premultiplied — OK; don't "fix" it back to non-premultiplied).
- **Runtime loader:** `Compat/WebContentManager.cs` (subclasses `ContentManager`). Texture2D
  via `Texture2D.FromStream` (KNI decodes PNG with StbImageSharp, synchronously);
  SpriteFont reconstructed via the public `SpriteFont(...)` ctor; Curve rebuilt from keys.
  `ResolvePath` normalises casing **and** collapses the stray `Content/` prefix so both the
  `content` field (root `"Content"`) and `base.Content` (root `""`) resolve to one
  `content/...` root. Files are fetched with `TitleContainer.OpenStream` (sync XHR).
- **Stage-4 wiring:** point the game at the web loader. In `Game1` ctor swap
  `content = new ContentManager(Services,"Content")` → `new WebContentManager(...)`, and set
  `((Game)this).Content = new WebContentManager(Services, "")` so the `"Content/"`-prefixed
  `base.Content` loads also use it. The current Stage-3 harness is `ContentTestGame.cs`
  (wired in `Pages/Index.razor.cs`); swap that to `new EvilAliens.Game1()` per Stage 4.
- **Still unhandled (by design):** `Effect` (Stage 5), `SoundEffect`/`Song`/`Video`
  (Stage 6) fall through to `base.Load<T>` and **will throw** — handle in those stages.
  `Game1.LoadContent` loads the `gamma` Effect, so booting Game1 hits this immediately.
- **Gotcha for Stage 4:** `AnimatedSprite.cs:~29` reads animation `.dat` via
  `File.OpenRead("Content/"+filename)` — no FS in WASM. The `.dat` files are already in
  `wwwroot/Content`; redirect that read to `TitleContainer.OpenStream` (lowercase the path).
- **Game loop fix (in `wwwroot/index.html`):** the loop now falls back to `setTimeout`
  when the tab is hidden (rAF pauses in background tabs — froze the game + headless
  capture) and the canvas size falls back to the window / 1280×720 when the holder hasn't
  laid out. (Verification tip: drive/inspect via the **claude-in-chrome** MCP on a real
  foreground tab — the preview renderer wedges when its tab is backgrounded.)

---

## Stage 4 — First boot / playable core

**Goal:** `Game1` boots, reaches the title screen + menu, and the first level is playable
with the keyboard. This is the headline "it actually runs" milestone.

**Steps:**
1. In `Pages/Index.razor.cs`, swap `new SpikeGame()` → `new EvilAliens.Game1()`. (Delete
   `SpikeGame.cs` once stable.)
2. **Threading (known blocker):** `Game1.cs:~488` and `Savable.cs:~62` do `new Thread(...).Start()`
   for async save/load. Browsers are single-threaded (no WASM threads by default) → this throws.
   Make those operations **synchronous** (simplest) or marshal to the game loop. Search for
   `new Thread`, `Thread.Start`, `Thread.Sleep`, `SetProcessorAffinity` (already commented).
3. Boot through the crashes iteratively: run, read the **browser console**, fix the top
   exception, repeat. Likely suspects: content paths (Stage 3), `GamerServicesComponent`
   (no-op, fine), service-locator init order, `Game.Services` registrations.
4. **Input:** verify `Keyboard.GetState()` works (KNI maps DOM key events). Map the original
   gamepad controls to keyboard. The original input layer is `InputHandler.cs` / `ControlDevice.cs`.
5. Accept placeholder visuals (no shaders/bloom yet) and possibly no audio — those are later.

**Gotchas:** the fixed timestep + the `GameTime` scaling code in `Game1.Update` (Stage 2 changed
a `GameTime` ctor). Watch for `Guide.IsTrialMode` gating (returns false = full game, good).

**Done when:** title → menu → first level renders and responds to input in the browser.

### Stage 4 — DONE. What was changed (read before Stage 5/6/9)
- **Game loop wiring:** `Pages/Index.razor.cs` now runs `new EvilAliens.Game1()`
  (was `ContentTestGame`). `ContentTestGame.cs`/`SpikeGame.cs` are now dead — safe to delete.
- **Content (the recurring trap):** `Game1` and **every scene that makes its own
  `ContentManager`** must use `WebContentManager`, or the load hits KNI's base manager and
  throws `The content file was not found for asset '...'` (it looks for `.xnb`, we ship PNG).
  Rewired: `Game1` (`content` + `base.Content`), `SplashScene`, `CreditsScene`, `HelpText`,
  `InstructionsMenu`. **`BloomComponent`'s is left as plain `ContentManager` on purpose** —
  it's not added to `Components` (see below) so it never loads. *If you add a new scene with a
  local `ContentManager`, make it a `WebContentManager`.*
- **Threading → synchronous** (browser WASM is single-threaded; `new Thread().Start()` throws):
  `Savable.SaveThreaded` now calls `SaveNoThread` (removed the thread + the `Thread.Sleep(100)`);
  `Game1.exitFunc`'s exit thread replaced with a direct `base.Exit()`.
- **The XBLIG sign-in gate (the "stuck on Press Start" blocker) — recreated the PC path.**
  The recovered Xbox build gated start on `isSignedIn(starter)` and, with the web's empty
  `SignedInGamers`, looped forever on the no-op `Guide.ShowSignIn`. The PC build had no
  signed-in-gamer requirement, and **its keyboard support is all still here as runtime code**
  (it was never `#if`-stripped) — only the one block in `InputHandler.Update()` that reads
  `keysToCheck[]` against `Keyboard.GetState()` was `#if WINDOWS` and got stripped (the Xbox
  build fetched `Keyboard.GetState()` and discarded it; the `keysToCheck` table was left dead).
  Fixes: (1) **reconstructed that keyboard-read block** — re-lights menu nav (`MyKeys.Enter/
  Esc/Up/Down/...`), Enter-to-start, and the in-game `ControlDevice.Keyboard` player
  (`PlayerShip` already reads `MyKeys` for movement, Mouse1 to fire); (2) `StartScreen` accepts
  keyboard Enter and starts as a local profile (no sign-in); (3) `Storage.Update` guards the
  `SignedInGamers[activePlayer]` indexer (Xbox sign-OUT check) against the empty collection.
  `SignedInGamers` stays **empty** (faithful: no Xbox sign-in on the web). Keyboard: arrows/WASD
  move, Enter select, Esc back.
- **`GraphicsProfile.HiDef`** set in the `Game1` ctor. KNI defaults a new device to **Reach**,
  which throws `Reach profile does not support 32 bit indices` the moment the menu draws (the
  game uses 32-bit index buffers). WebGL 2 supports them; HiDef matches the Xbox feature set.
- **Stubbed-until-their-stage (compile + run, no visual/audio yet):**
  - *Audio (Stage 6):* `SoundManager` ctor wraps the XACT `AudioEngine`/`WaveBank`/`SoundBank`
    (`.xgs/.xwb/.xsb`, not unpacked) in try/catch → null; `Play`/`PlayCue`/`Update` null-check.
  - *Shaders/bloom (Stage 5):* `BloomComponent` is **not added to `Components`** (kept as an
    object + `IBloomService` so `Settings`/`Visible` callers work) — re-add it in Stage 5.
    `EffectHandler.LoadGraphicsContent` is a no-op and `LoadEffects` early-outs while the
    `*EffectFile`s are null (original body preserved in `src_decompiled/`). `Game1`'s `gamma`
    effect load is now optional (try/catch → null) and **`DrawInner` skips the gamma/resolve
    composite when `gamma==null`** — otherwise, with the no-op `ResolveBackBuffer` shim, it
    would `Clear(Black)` and draw an empty resolve target over the scene = black screen.
- **`AnimatedSprite.loadData`** reads the animation `.dat` via `TitleContainer.OpenStream`
  (lowercased) instead of `File.OpenRead` (no WASM filesystem).
- **Resolution / presenter (don't fight KNI):** the game is authored at a fixed **800×600**,
  but KNI's BlazorGL backend **forces the back buffer to the browser window size and rewrites
  `PreferredBackBufferWidth/Height` on every resize** (`GameWindow.OnResize` →
  `UpdateBackBufferSize`, decompiled from `Kni.Platform`). So pinning `PreferredBackBuffer`
  does NOT stick — it reverts to the window size on the next resize / fullscreen toggle (this was
  the "sometimes the game suddenly reverts to the wrong resolution" bug). The fix: **`Game1.Draw`
  renders the whole 800×600 frame into an offscreen `sceneTarget` (`RenderTarget2D`), then blits
  it scaled + letterboxed to the window-sized back buffer.** The game's many "return to back
  buffer" calls are `SetRenderTarget(0, null)` through the Xna3 shim, so
  `Xna3GraphicsDeviceCompat.BaseRenderTarget` redirects those nulls to `sceneTarget` while a frame
  is in flight (single choke point — `Background`, `MenuScene`, `MenuSub1`, … all route through
  it). `index.html`/`Index.razor` just let the canvas fill the window 1:1 (KNI owns its size).
  This is effectively the **front half of the Stage-5 resolve composite**; Stage 5 adds the gamma
  shader on top of this same target, and Stage 9 adds fullscreen / integer-scale options.
- **Real keyboard input works** — KNI binds `keydown`/`keyup` on `window` and maps to XNA `Keys`
  by **`event.keyCode`** (verified by decompiling `Kni.Platform` BlazorGL: `Keys k = (Keys)keyCode`,
  with only keyCode 16/17/18 = Shift/Ctrl/Alt disambiguated by `location`; the `key` char is
  ignored). So Enter (13), arrows (37–40), WASD (65/87/83/68) and Esc (27) all register the right
  `Keys`. **Verifying input from a driven/headless browser (gotcha):** the game polls
  `Keyboard.GetState()` once per `requestAnimationFrame`, so a dispatched event must (a) carry the
  correct `keyCode`, and (b) be **held across ≥1 frame** — dispatch `keydown`, wait ~250 ms, then
  `keyup`. A fast tap is added-then-removed between polls and missed (this, not the char value, is
  why a quick synthetic Enter "didn't work" mid-debugging).
- **Known cosmetic / later-stage leftovers (not bugs):** no bloom/shaders so visuals are flat
  (Stage 5); no audio (Stage 6); saves are in-memory only (Stage 7); the controls help screen
  shows the **Xbox joypad** (the PC keyboard-help screen was `#if WINDOWS`-stripped) — a web
  input-help screen is Stage 9. The menu auto-runs an **attract-mode demo** (`Demo1/2/3`) after
  idle, which is why gameplay appears without selecting a level.

---

## Stage 5 — Shaders (bloom + sprite effects)

**Goal:** restore the post-processing (bloom) and the ~12 sprite shaders so visuals match.

**Context:** Source `.fx` files are **lost** (only compiled DX9 shader `.xnb` survive, which
KNI can't use). The shaders are small and standard; rewrite them in HLSL for KNI's MGFX
pipeline. Behaviour references: `Game/EvilAliens/EffectHandler.cs` and the `*Effect.cs`
wrappers (`ColorizeEffect`, `OutlineEffect`, `FadeEffect`, `LightenEffect`, `StaticAlphaEffect`,
`InterpolateEffect`, `PowerupEffect`, `MySpriteEffect`), plus `BloomPostprocess/` and the
asset names in `Content/Bloom/` and `Content/GFX/Effects/`.

**Steps:**
1. Replace the no-op `Effect.Begin/End`, `EffectPass.Begin/End` shims with real
   `EffectPass.Apply()` calls at the draw sites (or rework those call sites).
2. Reimplement `GraphicsDevice.ResolveBackBuffer` (currently no-op) using a `RenderTarget2D`
   so bloom has a source texture; port `BloomComponent` to 4.0 render targets.
3. Author the shaders as `.fx` and compile via KNI's pipeline to MGFX/WebGL (GLSL).
4. Verify the `SpriteBlendMode`→`BlendState` mapping in `Compat/Xna3Compat.cs`.

**Done when:** bloom + the colorize/outline/fade effects render correctly in-browser.

### Stage 5 — DONE. What was changed (read before Stage 6/9)
- **The toolchain (the make-or-break enabler).** KNI's `Effect(gd, byte[])` parses an
  **MGFX v10** blob (GLSL inside for the GL/Web profile). KNI ships the full Windows
  effect compiler in the nuget package **`nkast.Xna.Framework.Content.Pipeline.Builder.Windows`
  `4.1.9001`** (`MGCB.exe` + `SharpDX.D3DCompiler` + `libmojoshader_64.dll` + the
  `MojoProcessor`). Restore it into the nuget cache (any project referencing it, then
  `dotnet restore`). `BlazorGL` is a real MGCB `/platform`, and it emits exactly the v10
  GLSL the `4.1.9001` runtime wants. **Version must match the runtime** — a newer compiler
  emits a format the runtime rejects ("for a newer version of KNI").
- **Offline build, committed outputs (mirrors Stage 3's philosophy).**
  `tools/shaders/src/*.fx` (hand-written HLSL) → `tools/shaders/build_shaders.py` runs MGCB
  for BlazorGL, strips the `.xnb` wrapper to the raw MGFX blob, and writes lowercased
  `.mgfxo` into `wwwroot/Content/{gfx/effects,bloom}/`. Re-run with
  `PYTHONIOENCODING=utf-8 python tools/shaders/build_shaders.py`. The build is NOT wired
  into the .csproj (keeps the project + Stage-8 CI light); the `.mgfxo` are committed.
  `bin/`,`obj/`,`gen/`,`effects.mgcb` under `tools/shaders/` are gitignored.
- **Runtime loading:** `WebContentManager.Load<Effect>` reads `<name>.mgfxo` and calls
  `new Effect(gd, bytes)` — the exact ctor the stock `EffectReader` feeds.
- **The XNA 3.x→4.0 effect model.** 3.x set the device shader globally
  (`effect.Begin()` / `pass.Begin()`, then draw); 4.0/KNI passes the `Effect` to
  `SpriteBatch.Begin(...)` and applies it during the batch flush. KNI's `SpriteBatch.Setup`
  always applies its **internal** sprite vertex shader, so a custom effect needs **only a
  pixel shader** (a pass with no VS leaves the sprite VS bound). The no-op
  `Xna3EffectCompat.Begin/End` shims are now **dead** (no callers); the real work is the
  effect passed to `Begin`.
- **Gamma:** loaded in `Game1.LoadContent`; applied on the **final present blit** in
  `Game1.Draw` (sceneTarget → window, through the gamma PS). The old DrawInner
  resolve-based gamma block was removed (the Stage-4 presenter made it redundant).
- **Bloom:** `BloomComponent` re-added to `Components` (Visible follows `Settings.Bloom`).
  Its content manager is now a `WebContentManager`; targets sized to the **800×600** scene
  (not the window back buffer); `Draw`/`DrawFullscreenQuad` ported to pass the effect to
  `SpriteBatch.Begin`; the base scene is bound to sampler **s1** (`Textures[1]` +
  `SamplerStates[1]`) for the combine. `ResolveTexture2D` is now a real **`RenderTarget2D`**
  and **`ResolveBackBuffer` is implemented** (blits `BaseRenderTarget` = sceneTarget into the
  target via a private SpriteBatch, then restores it). The `sketch` shader was dead (loaded,
  never applied) — dropped. Shaders: `BloomExtract/GaussianBlur/BloomCombine.fx` (canonical
  XNA Bloom sample ports).
- **Sprite effects:** one master `tools/shaders/src/sprite.fx` with
  `#ifdef COLORIZE/LIGHTEN/FADE/INTERPOLATE`, compiled **13×** with different `#define`s
  (MGCB dedupes by source path, so `build_shaders.py` generates a stub per variant that
  `#define`s + `#include`s the master — the FX preprocessor only searches the stub's own
  dir, hence the master is copied beside the stubs in `gen/`). `EffectHandler` was rewritten:
  `LoadEffects()` SELECTS `currentEffect` and sets **named** params (no positional indexing,
  no `Begin`); `SpriteBatchWrapper._beginDrawing` reads `EffectHandler.CurrentEffect` and
  passes it to `SpriteBatch.Begin`. **HSV is computed in-shader** (Sam Hocevar rgb2hsv/
  hsv2rgb), so the old RGB↔HSV `Texture3D` lookup tables are gone. Tinting: FADE variants
  tint via `FadeValue`; non-fade variants via the vertex colour — single tint in every case
  the game draws (see the comment in `sprite.fx`). `outline`/`staticAlpha` are **never
  `.Enable()`d** in the shipped build (placeholder shaders that only need to load — LoadEffects
  uses `staticAlphaEffectFile != null` as its "loaded" sentinel).
- **Menu render targets (bug found during verification):** the menu classes (`MenuScene`,
  `MenuSub1` and its submenu subclasses) render into an offscreen `myRenderTarget` then blit
  it. These were created **window-sized** with **`(SurfaceFormat)1` = Bgr565** — and a Bgr565
  render target is not valid on WebGL (it renders nothing), so the whole menu (planet backdrop,
  the alien skull, the Start/Options/Tutorial/Exit entries, button tips) came out **black**,
  leaving only the separately-drawn title. Fixed by creating those targets at the **800×600**
  design resolution with **`SurfaceFormat.Color`** (RGBA8); the entries are laid out around
  origin (400,300) and blitted 1:1 / center-at-origin, so 800×600 also aligns them to the
  Stage-4 presenter target. (Same window-vs-design sizing issue as the bloom targets.) If a new
  menu/scene renders to an offscreen target, use Color at design res, not Bgr565 at back-buffer size.
  - **`MenuSubWithSkull` skull + title sizing:** that override sized the skull/title destination
    rects as fractions of `BackBufferWidth/Height` (the ~1300px window) but draws into the
    800x600 design target, so they were scaled for the window and overflowed (title/skull too big
    and cut off). Fixed to base the rects on 800x600. General rule: size/position menu elements
    against the 800x600 design space, not the back buffer.
  - **Intentional, left intact:** the menu's "lightspeed warp" star-trail effect during the
    zoom-in (`FadeToGame`) relies on NOT clearing `MenuScene.myRenderTarget` (the backdrop draw is
    skipped during the warp and the RT keeps `RenderTargetUsage.PreserveContents`). The size/format
    fix kept that usage flag, so the warp still works; the trail accumulates in myRenderTarget (not
    sceneTarget), so bloom compositing over sceneTarget doesn't wipe it.
- **Blend modes verified:** `SpriteBatchWrapper.ToBlendState` maps 3.x
  `None→Opaque`, `AlphaBlend→AlphaBlend`, `Additive→Additive` (all premultiplied, matching
  the premultiplied content) — additive glows and alpha blends render correctly in gameplay.
- **Verification:** booted in a fresh Chrome tab; gamma proven by a forced-value test
  (whole frame brightens, letterbox stays black); bloom visible as glow on text/sprites and
  correct on dark gameplay; attract-mode gameplay renders enemies/ships/HUD with correct
  colours + animation; **0 game/shader console errors** (the only console errors seen during
  debugging came from synthetic key-injection in the test harness, not the game).
- **Gotcha for later stages — driving keyboard input headlessly:** real OS keys via the
  claude-in-chrome `computer` `key` action work; **synthetic JS `KeyboardEvent`s do NOT** —
  KNI's WASM keyboard interop throws `JSON value could not be converted to System.Int32`
  reading the faked `keyCode` and can leave a key stuck. Use real key events (held across a
  frame) or click-to-focus + `computer key`.

---

## Stage 6 — Audio

**Goal:** music + SFX play in the browser.

**Context:** audio is XACT (`Content/SFX/*.xgs/.xsb/.xwb`), driven by
`Game/EvilAliens/SoundManager.cs` (+ `SongInstance.cs`). **There is NO XACT runtime in KNI's
BlazorGL backend** (MonoGame's XACT is legacy/desktop-only) — the `AudioEngine`/`SoundBank`/
`WaveBank` ctor is already try/caught to null, so audio silently no-ops today. We do not "port
XACT"; we rebuild its *behaviours* on `SoundEffect`/`SoundEffectInstance` (+ a JS/WebAudio music
layer). The good news from inspecting the banks: scope is small and well-defined.

### What XACT actually did here (inventory — read before scoping)
Pulled from `alienssfx.xgs` (542 B) + `Sound Bank.xsb` (2.3 KB) + the call sites:
- **Categories:** `Music`, `Speech`, `Default` (+ `Global`). Used for group volume + the
  `Settings.PlayMusic` toggle. No others.
- **Custom variables / DSP:** exactly **one** custom variable, `Pitch` (everything else in the
  `.xgs` — `NumCueInstances`, `AttackTime`, `DopplerPitchScalar`, ... — is XACT's stock built-in
  set). **No custom reverb/distortion/DSP presets exist** (the tiny `.xgs` confirms it), so
  there is no hidden effects chain to recreate. Good.
- **Cue list (`.xsb`):** SFX (`expl1/2`, `fire`, `head asplode`, `small head asplode`,
  `lazershot`, `lazercharge`, `lazershotnoloop`, `newwave`, `blast`, `powerup`,
  `targetacquired`, `hit_boss`, `bugdies`, `bees`, `wasp`, `spiderbossdeath`, `evillaugh`,
  `usepowerup`); **Speech** (`ttf_*` x10 — the `PlayText()` warnings/unlock stingers); **intro
  voice-over** (`humanityshope`, `inagalaxyundersiege`, `inanageofviolence`, `lieswithoneman`,
  `single heartbeat` — the movie-trailer narration); **music** (`stage1/2/3`, `bach`, `classic`,
  `sjaak`, `kylikova`, `sjaakslow`).

### The "cool effects" to preserve (and how each rebuilds)
1. **Reactive music pitch/rate** — the standout. `SetMusicRate(rate)` -> (XACT)
   `currentMusic.SetVariable("Pitch", rate)`. Call sites: **`BrainBoss`** sweeps music pitch with
   boss HP (`SetMusicRate(MyMath.PowerCurve(50f, 68f, 2f, 1f - HitPointsNormalized))`) and
   **`Level3`** starts Kylikova at `SetMusicRate(50f)`. Rebuild: WebAudio
   `AudioBufferSourceNode.playbackRate` (pitch+tempo together, faithful to XACT's resampling
   pitch). *Open question:* the `Pitch` variable's value range (~50-68) maps through an XACT RPC
   curve we don't have -> derive the rate multiplier by ear at impl time (50 == normal? a
   semitone scale?). This is the one bit needing tuning.
2. **Cue variation / anti-machine-stamp** — XACT cues randomize pitch/volume (and pick wave
   variations) per play so repeated `expl1`/`head asplode` don't sound identical. Rebuild:
   small random pitch+gain per shot (exactly Fighterproto's `0.94 + rand*0.12` trick).
3. **Instance limiting** — `InstancePlayLimitException` is caught everywhere; cues had max-instance
   caps + steal-oldest so rapid fire (`lazershot`, explosions) doesn't pile into mud. Rebuild: a
   per-cue active-voice cap in the new manager.
4. **Looping SFX** — naming (`lazershot`/`lazershotnoloop`, `lazercharge`/`lazerrepeatable`)
   implies some cues loop. Rebuild: per-cue loop flag on the `SoundEffectInstance`.
5. **Categories/volume + Speech priority** — `Music`/`Speech`/`Default` group volumes; `PlayText`
   carries a `priority` (the `currentspeechpriority` field hints at speech ducking, though the
   recovered Xbox build's priority logic is thin). Rebuild: a gain group per category.

### Decided architecture (split: native SFX + JS music)
KNI `SoundEffectInstance` natively gives `Pitch` (+/-1 octave), `Volume`, `Pan`, `IsLooped` and
manual instance management — that covers SFX, speech, variation, instance caps and looping SFX
**with zero interop**. The *only* thing it can't do is seamless music **loop points** (its
`IsLooped` loops the whole buffer). So:
- **SFX + speech + intro VO -> native C# (`SoundEffect`/`SoundEffectInstance`).** Many SFX already
  exist as standalone `.xnb` SoundEffects in `Content/SFX/` (`expl1.xnb`, `blast.xnb`, ...), so
  those decode directly; the rest come from the `.xwb`.
- **Music -> JS/WebAudio interop layer (Fighterproto approach).** This is where we "spend the
  time" the brief calls for: it buys seamless **intro -> loop-body** (`AudioBufferSourceNode`
  `loop`+`loopStart`/`loopEnd` from `pymusiclooper` tags) AND the reactive `playbackRate` pitch
  AND gain crossfades — i.e. it solves effect #1 and the looping problem in one layer. Reuse
  `C:\Programming\Fighterproto\src\render\audio.ts` (BGM half: tag parser + player) nearly
  verbatim; `SetMusicRate`/`PlayMusic`/`StopMusic` become Blazor JS-interop calls. Interop is
  already wired (`initRenderJS`/`tickJS`).

### Voice-over: re-cast with ElevenLabs (decided + generated 2026-06-20)
We are NOT reusing Microsoft Sam's `ttf_*` speech from the `.xwb` — the game's voice is re-cast
with ElevenLabs (Eleven v3), full commercial license. Scripts in `tools/tts/`, renders in
`tools/tts/out/`:
- **Announcer = "Brian"** (`[robotic announcer]` + per-line emotion, stability 0.5): the 10
  `ttf_*` cues named to the exact `SoundManager.PlayText` ids, in `out/announcer_final/`, PLUS two
  NEW barks `ttf_missionFailed` + `ttf_gameOver` (the defeat screen was silent in the original).
- **Narrator = "Victor"** (`[British accent][cinematic][reverent]`, stability 0.0): the
  `CreditsScene.SetupLevel1/2/3` story crawls in `out/narrator/` — a NEW cinematic narration layer
  the XBLIG never had (story text scrolled silently).
Wiring impact on the steps below:
- Speech no longer comes from the `.xwb`; the crack is only for music + intro VO.
- Add `Texts.MissionFailed` / `Texts.GameOver` (+ `PlayText` cases) and fire one on the defeat
  screen (`GameScene.cs:598`, currently `Texts.Nothing`).
- Renders are MP3 (API output); convert to WAV in this audio step.
- Open option: the intro movie-trailer VO (`humanityshope`, `inagalaxyundersiege`, ...) is a
  natural fit for Victor too — re-cast with the narrator if we want it voiced.
`CreditsScene` already credits Brian + Victor and retires Sam ("IN LOVING MEMORY OF: Microsoft Sam").

### Steps
1. **Crack the `.xwb`** (9.8 MB) to WAV and map cue->wave names via the `.xsb` (`unxwb`-style;
   format is documented). Needed for **music + intro VO** (no `.xnb` copies); **speech is now our
   own ElevenLabs renders (see above), not the `.xwb`.** SFX can instead reuse the existing `.xnb`.
   Re-encode music to `.ogg/.opus`.
2. **Loop-tag the 8 music tracks** offline with `pymusiclooper` (find seam -> bake
   `LOOPSTART`/`LOOPEND`). If a track has a one-shot intro, keep it; loop only the body.
3. **Build the JS music layer** (port `audio.ts` BGM half) + a thin C# `IMusicService` over JS
   interop. Wire `PlayMusic`/`StopMusic`/`SetMusicRate(rate->playbackRate)` to it.
4. **Rewrite `SoundManager` SFX path** on `SoundEffect`/`SoundEffectInstance`: per-cue
   instance cap, random pitch/volume variation, loop flags, `Music`/`Speech`/`Default` gain
   groups; drop `AudioEngine`/`SoundBank`/`WaveBank`. Keep `SongInstance.cs` only if useful.
5. **Autoplay policy:** unlock `AudioContext` (and KNI audio) on the first `keydown`/`pointerdown`
   (Fighterproto's `kick` pattern); queue any music requested before unlock.

**Done when:** title music loops seamlessly; core SFX play with variation (not machine-stamped);
the **BrainBoss music-pitch sweep** audibly tracks boss HP; `Music`/`Speech` volumes + the
`PlayMusic` setting work; nothing throws on the autoplay gate.

### Stage 6 — DONE. What was changed (read before Stage 7/9/10)
- **The banks were cracked offline in pure Python — no ffmpeg, no vgmstream, no external
  binaries** (mirrors Stage 3/5: a reproducible tool + committed outputs). `tools/audio/`:
  - `xact.py` — parses the **big-endian Xbox** Wave Bank (`.xwb`) + Sound Bank (`.xsb`) and
    decodes the waves. The `.xsb` header is **2 bytes shorter** than the PC/MonoGame layout
    (offset block at `0x22`); all 42 cues are *simple* cues but the sounds are a mix of simple
    (inline wave index) and complex (clip → play-wave event; wave index at `clipOffset+9`).
    The cue→wave map was validated by checking **all 44 waves are referenced exactly once**.
  - **Codecs:** SFX/speech are **PCM** (16-bit signed **big-endian**, or 8-bit unsigned); the
    8 music tracks are **xWMA** (entries 34–43). xWMA is decoded with **PyAV** (bundled
    libavcodec `wmav2`) by **rebuilding the `RIFF/XWMA` container** FFmpeg expects — the XACT
    mini-format `wBlockAlign` byte indexes the standard WMA bytes-per-sec / block-align tables;
    **no `dpds` seek table is needed** for a straight decode-from-start.
  - `build_audio.py` — the driver (`PYTHONIOENCODING=utf-8 python tools/audio/build_audio.py`).
    Writes `wwwroot/Content/sfx/*.wav` (PCM_16; SFX from the banks + ElevenLabs `ttf_*` speech
    from `tools/tts/out/announcer_final/*.mp3` via libsndfile MP3 read), `Content/vo/*.wav`
    (Victor narration), and `Content/music/*.ogg` (Vorbis) + `Content/music/music.json` (loop
    manifest). **Gotcha:** libsndfile's Vorbis encoder *aborts the whole process* on a single
    multi-MB write — OGG must be written in chunks via `sf.SoundFile` (see `write_ogg`).
  - **Music loop points come straight from XACT, not a guesser.** The `.xsb` play-wave events
    set **loop count 255 (infinite) = loop the whole wave**, and the wave-bank `LoopRegion`s are
    all `(0,0)` (no partial loops). So: the 6 single-wave tracks loop **whole** (`loopStart=0,
    loopEnd=duration`); the two intro+loop cues (`stage2`=waves 34+35, `stage3`=36+37) are
    concatenated with the leading intro wave played once (loop count 0) and `loopStart` = the
    intro length (the body wave has loop count 255). *(An earlier pass used pymusiclooper to find
    an internal seam — dropped: it discarded the post-loop tail and wasn't what the game encoded.)*
- **Music = a WebAudio JS layer, NOT KNI.** KNI's `SoundEffect` looping replays the *whole*
  buffer; music needs loop *points*. `wwwroot/index.html` defines `window.eaMusic`
  (`play(cue)`/`stop()`/`setRate(rate)`): it loads `music.json`, fetches+decodes the OGG, and
  loops an `AudioBufferSourceNode` with `loopStart`/`loopEnd` (seconds). **Autoplay unlock:**
  resumes the `AudioContext` on the first `keydown`/`pointerdown`, queuing any track requested
  before unlock. `Compat/MusicInterop.cs` bridges C#→JS via `IJSInProcessRuntime.InvokeVoid`
  (`MusicInterop.Init(JsRuntime)` is called from `Pages/Index.razor.cs`). `SetMusicRate` maps
  the game's XACT pitch value (`~50` = normal) to `playbackRate = rate/50` (the BrainBoss sweep).
- **SFX + speech = native KNI `SoundEffect`/`SoundEffectInstance`.** `SoundManager` was rewritten:
  per-cue **instance cap + steal-oldest**, random **pitch/volume variation** (skipped for speech
  and loops), **loop flags** (`lazershot`/`lazercharge`/`bees`), and **Default/Speech gain
  groups** (the game has only a `PlayMusic` on/off toggle, no volume sliders). The XACT
  `AudioEngine`/`WaveBank`/`SoundBank`/`Cue` are gone. **`Play(name)` now returns a
  `SoundEffectInstance`** (was `Cue`) — the 4 held-handle call sites were retyped: `Lazer`,
  `LazerGenerator`, `Level2`, `StarMine`. `WebContentManager` now loads `SoundEffect` (`.wav`)
  via `SoundEffect.FromStream`.
- **Speech re-cast (ElevenLabs).** `PlayText` plays the `ttf_*` cues (Brian announcer). Added
  `Texts.MissionFailed`/`Texts.GameOver` (+ `PlayText` cases); the **defeat screen now barks**
  "Mission Failed" — `AnimatedMessage.UpdateDefeat` plays its `speechText` (it didn't before),
  and `GameScene` passes `Texts.MissionFailed` (was `Texts.Nothing`). **Narrator (new layer):**
  `SoundManager.PlayNarration`/`StopNarration` play the Victor `victor_level{1,2,3_hard,
  3_normal}` clips over the `CreditsScene` story crawls (`SetupLevel1/2/3`; stopped in
  `Terminate`).
- **Dev commentary stays silent (decided).** The live-"MS Sam" path (`DevCommentEvent`,
  arbitrary `PlayText(speechText)`) was `#if WINDOWS` and is gone; it was an **abandoned,
  unhooked feature**, so `TTSIsSilent()` stays `true` and that code remains a no-op. (Fossils:
  `GetTTSName()`→"Karel", the dead `currentspeechpriority`.)
- **Toolchain used (already installed):** PyAV (xWMA decode), `soundfile`/libsndfile (MP3 read,
  OGG/WAV write), numpy. **No ffmpeg / vgmstream needed** (pymusiclooper is no longer used —
  loops come from the XACT data).
- **Verification (in real Chrome, per CLAUDE.md):** the menu's `PlayMusic(Sjaak)` fired the full
  C#→JS chain (`eaMusic.play("sjaak")` with the right loop points), attract-mode gameplay fired
  KNI SFX (`AudioBufferSourceNode` starts at the exact extracted durations — `fire` 0.23s,
  `expl1` 1.07s, `lazercharge` 2.25s loop), **0 console exceptions**. *Driving gotcha (worse than
  Stage 5's):* `computer key` presses release in **~0.7 ms**, far under the 33 ms input poll, so
  the `Pressed`-edge at "Press Start" never latches — pass it with a **forced-`keyCode` synthetic
  `keydown` held across frames** (`Object.defineProperty(e,'keyCode',{get:()=>13})`, dispatch
  `keydown`, wait, then `keyup`). The cache is also sticky: hard-reload / cache-bust the URL or
  the browser serves a stale `index.html` without `eaMusic`.

---

## Stage 7 — Saves & awardments persistence

**Goal:** settings, high scores, unlockables, and awardments persist across page reloads.

**Context:** `StorageStub.cs` currently writes to the WASM in-memory FS (lost on reload).
Persistence layer = browser localStorage (or IndexedDB) via Blazor JS interop.

**Steps:**
1. Add JS interop functions to read/write localStorage; back `StorageStub`'s container Path
   reads/writes with it (or intercept `Savable`/`Settings`/`Unlockables`/`Achievements` save/load).
2. Persist awardment unlocks; ensure the awardments UI (`AwardmentBlade`, `SubMenuAwardments`)
   reflects saved state. (This is the "fake achievements" experience for the web.)

**Done when:** change a setting / unlock something, reload, and it's remembered.

### Stage 7 — DONE. What was changed (read before Stage 8/9)
- **Approach: a MEMFS ↔ localStorage *mirror* at the `StorageStub` layer — the game's `Savable`
  subclasses are untouched.** The game still does the XNA 3.x dance (`OpenContainer` → `File`/
  `StreamWriter`/`BinaryWriter` against `container.Path` → `Dispose`) against the WASM in-memory FS.
  We just make that FS persistent: **hydrate** localStorage → MEMFS once before the first read, and
  **flush** changed files MEMFS → localStorage on every container `Dispose`. No edits to `Settings`/
  `Unlockables`/`Achievements`/`ScreenshotSaver` — they keep writing XML/`.dat` exactly as before.
- **The three pieces (mirrors the Stage-6 `eaMusic`/`MusicInterop` split):**
  - `wwwroot/index.html` **`window.eaSave`** — synchronous localStorage `get`/`set`/`remove`. Keys
    are `eaweb_save:<relpath>` (e.g. `eaweb_save:EvilAliens/Settings.xml`); values are **base64 of
    the raw file bytes** (handles the binary `.dat` screenshots too). `load()` returns a JSON
    `{relpath: b64}` of every entry; `set()` returns **false on `QuotaExceededError`** instead of
    throwing.
  - `Compat/SaveInterop.cs` — the C# bridge (like `MusicInterop`): `Init(IJSRuntime)` grabs the
    `IJSInProcessRuntime`; `Load()` parses `eaSave.load`'s JSON → `name→byte[]`; `Set`/`Remove`
    proxy through. Synchronous interop (`Invoke<string>`/`Invoke<bool>`), so it fits the game's
    synchronous save path with no async.
  - `Compat/StorageStub.cs` **`PersistentSave`** — `EnsureHydrated()` (once, called at the top of
    `StorageDevice.OpenContainer`, i.e. right before `StartScreen`'s `Settings/Unlockables/
    Achievements.Load()` read) writes every persisted entry into MEMFS. `Sync()` (called from
    `StorageContainer.Dispose`) walks the `/eaweb_save/**` tree and persists it. The save pattern
    is *open → write → Dispose*, so Dispose is the natural flush point.
  - `Pages/Index.razor.cs` calls `SaveInterop.Init(JsRuntime)` next to `MusicInterop.Init` (before
    the first tick, so saves persist from the very first frame).
- **What persists:** `Settings.xml`, `Unlockables.xml`, `Achievements.xml` (awardment unlocks +
  per-level hi-scores/difficulty) and the `<Level>.dat` level-select **screenshots** (binary).
- **Efficiency + correctness details that matter:**
  - `Sync` keeps an in-memory **`_mirror` (relpath → last-persisted bytes)** and **only writes
    files whose bytes changed** (byte-compare). `Dispose` fires on *read-only* opens too (every
    `LoadScreenshot`), so without this each menu-load would needlessly re-persist everything.
  - It also **prunes** localStorage keys for files the game deleted from MEMFS (e.g. *Reset All
    Progress* → `ScreenshotSaver.DeleteScreenshots`), keeping the two in sync.
  - Files are persisted **smallest-first**, so if a 300×225 screenshot (~360 KB base64) ever blows
    the ~5 MB localStorage quota, the tiny XML (settings/unlockables/awardments) is already saved.
    A failed `set` leaves the file *dirty* in `_mirror` so the next `Sync` retries it.
  - Hydrate / Sync are both wrapped in try/catch — a persistence hiccup (quota, private-mode
    storage, corrupt entry) must never break the game loop; corrupt base64 entries are skipped per
    file, and the game's own `loadData` try/catch still falls back to defaults.
- **Verification (real Chrome, per CLAUDE.md):** booted `?menu&noattract`, Options → toggled
  **Music: Enabled → Disabled** → `localStorage` gained `eaweb_save:EvilAliens/Settings.xml`
  (decoded `<PlayMusic>false</PlayMusic>`) + `Achievements.xml`; **reloaded** → Options read back
  **Music: Disabled** (hydrate works); toggled back to Enabled and confirmed the re-save. **0 game
  console errors.** (This stage's verification leaned on the merged-in **`eaPress` / `?menu`** debug
  helpers — synthetic `KeyboardEvent`s still throw the `System.Int32` interop error; use `eaPress`.)
- **Not done here (by design):** awardment-unlock *gameplay* triggers already call
  `Achievements/Unlockables.SaveThreaded()`, so they now persist for free — no new UI work was
  needed for `AwardmentBlade`/`SubMenuAwardments` to reflect saved state. IndexedDB was **not**
  needed: the save set is small and the synchronous `localStorage` path fits the game's synchronous
  save model cleanly (IndexedDB's async API would fight it).

---

## Stage 8 — GitHub hosting + Pages deploy (public)

**Goal:** the game is live at a public URL, rebuilt automatically on push.

**Decisions to make first:**
- **Repo name** (affects the `<base href>` for a *project* page → `/<repo>/`; a
  *user/org* page `user.github.io` or a custom domain → `/`).
- **What to commit publicly:** the web project + the content assets it needs to build. The raw
  53 MB Xbox package is provenance only — recommend **gitignore** it (or Git LFS). `bin/`,
  `obj/`, `.vs/` always ignored. Decide whether to commit `extracted/` or only the recovered
  PNG/WAV produced in Stage 3.

**Steps:**
1. `git init`; add `.gitignore` (`bin/`, `obj/`, `.vs/`, `*.user`, the raw package, scratch).
2. Create the public repo (`gh repo create <name> --public --source . --push`).
3. **Build Blazor WASM in CI** (GitHub Pages can't build .NET). Add
   `.github/workflows/deploy.yml`:
   ```yaml
   name: Deploy to GitHub Pages
   on: { push: { branches: [main] } }
   permissions: { contents: read, pages: write, id-token: write }
   jobs:
     build:
       runs-on: ubuntu-latest
       steps:
         - uses: actions/checkout@v4
         - uses: actions/setup-dotnet@v4
           with: { dotnet-version: '8.0.x' }
         - run: dotnet workload install wasm-tools
         - run: dotnet publish web/EvilAliensWeb -c Release -o release
         # Fix base href for a PROJECT page (skip if user/org page or custom domain):
         - run: sed -i 's|<base href="/" />|<base href="/<REPO-NAME>/" />|' release/wwwroot/index.html
         - run: touch release/wwwroot/.nojekyll            # Pages must not run Jekyll (_framework/)
         - run: cp release/wwwroot/index.html release/wwwroot/404.html   # SPA fallback
         - uses: actions/upload-pages-artifact@v3
           with: { path: release/wwwroot }
     deploy:
       needs: build
       runs-on: ubuntu-latest
       environment: { name: github-pages }
       steps: [ { uses: actions/deploy-pages@v4 } ]
   ```
4. Enable Pages: repo Settings → Pages → Source = "GitHub Actions".

**Pages gotchas (all addressed above):**
- `.nojekyll` is mandatory — Jekyll drops the `_framework/` folder (leading underscore).
- `<base href>` must match the serving path or nothing loads.
- `404.html` = copy of `index.html` so deep links / refresh work (SPA).
- Verify the `nkast.Wasm.*` JS `<script>` versions in `index.html` still match the restored
  package version after a Release publish (currently `8.0.5`).
- Optional: enable Brotli serving fallback in `index.html` (the toggle is already wired) since
  Pages doesn't negotiate `.br`.

**Done when:** the public URL loads and plays the game; pushing to `main` redeploys.

### Stage 8 — DONE. What was changed (read before Stage 9/10)
- **Live URL: https://coamithra.github.io/RotEA26/** — a **project page**, so the app serves from
  `/RotEA26/`. The repo (`Coamithra/RotEA26`) already existed, was already **public**, and `main`
  was already pushed, so the plan's `git init` / `gh repo create` steps were already done; `.gitignore`
  was already complete (`bin/`,`obj/`,`release/`, the raw 53 MB package, `.env`, `.claude/*.local.json`).
  Stage 8 reduced to: add CI, enable Pages, fix what only breaks on the live host.
- **CI: `.github/workflows/deploy.yml`** (push to `main` + `workflow_dispatch`). GitHub Pages can't
  build .NET, so CI does it: `setup-dotnet` 8 → `dotnet workload install wasm-tools` →
  `dotnet publish web/EvilAliensWeb -c Release -o release` → **rewrite `<base href="/" />` →
  `/RotEA26/`** (sed; the dev build keeps `/` for local `dotnet run`) → `.nojekyll` (Jekyll would drop
  the `_framework/` folder — leading underscore) → copy `index.html`→`404.html` (SPA deep-link
  fallback) → `upload-pages-artifact` → `deploy-pages`. Added a `concurrency: {group: pages,
  cancel-in-progress: true}` guard so a newer push supersedes an in-flight deploy. The committed
  shader `.mgfxo` + audio `.ogg/.wav` outputs mean CI needs **no** shader/audio toolchain — just
  `dotnet publish`.
- **Pages enabled via the API** (source = GitHub Actions): `gh api -X POST repos/Coamithra/RotEA26/pages
  -f build_type=workflow` (one-time; HTTPS auto-enforced). Equivalent to repo Settings → Pages →
  Source = "GitHub Actions".
- **`PublishTrimmed=false` (csproj).** A Release Blazor-WASM publish **trims by default**, and the game
  serialises `Settings`/`Unlockables`/`Achievements` with **`XmlSerializer`** (runtime reflection) —
  trimming strips those members and **white-screens the published build even though Debug runs clean**.
  Disabled trimming so the published build is at Debug parity. **This is why the published `wwwroot` is
  ~113 MB** (46 MB `_framework` + ~67 MB Content); shrinking it (trim roots / AOT / Brotli) is **Stage 9**,
  not Stage 8. (Well under Pages' 1 GB site limit.)
- **THE host-only bug — content path case-sensitivity (worked on Windows, 404'd on Pages).** The first
  deploy booted to a **black screen**; the browser console (not the build) showed
  `ManagedError: content/gfx/splash/ealogo.png`. Root cause: the physical asset root is **`wwwroot/Content`
  (capital C)** with all files lowercase *under* it, but the C# loaders forced a **lowercase `content/`
  root**. Case-insensitive Windows (dev box + `dotnet run`/Kestrel) resolved it fine; **case-sensitive
  Linux Pages 404s** `content/...` vs the real `Content/...`. The JS music layer + `music.json` +
  `General.Path` already used capital `Content/` (so music had been working on Pages). Fix = align the two
  C# path builders to the on-disk capital root:
  - `Compat/WebContentManager.cs` `ResolvePath` now prepends **`"Content/"`** (still lowercases everything
    after the root — disk is lowercase there).
  - `Game/EvilAliens/AnimatedSprite.cs` `loadData` keeps **`"Content/"` capital** and lowercases only the
    `.dat` filename (was `("Content/"+filename).ToLowerInvariant()` — lowercased the root too).
  - **Rule for future stages: every content request uses a capital `Content/` root, lowercase under it.**
    Don't reintroduce a lowercase `content/` request, and watch this when adding assets/scenes.
- **Verified on the live host (real Chrome, cache-busted `?cb=` because Pages serves a sticky
  `index.html`):** `?menu&noattract` renders the full menu (glowing title, planet backdrop, bloom/gamma);
  `?level=Level1` boots the space-intro then **Level 1 gameplay** (Earth hi-res backdrop, the animated
  player saucer via the fixed `.dat` path, powerup, HUD). **0 console exceptions** across both — the only
  console lines are KNI's benign `*Factory not found → registering through reflection` boot logs (same as
  Debug). HTTP-checked `index.html` (base href `= /RotEA26/`), `blazor.boot.json`, `.wasm`, the nkast
  `_content/*.8.0.5.js`, `music.json`, and `404.html` all 200.
- **Untouched / deferred (by design):** Brotli/gzip negotiation (Pages gzips text on the fly; `.br` files
  ship but aren't negotiated — Stage 9 size work); the nkast `8.0.5` JS `<script>` versions still match the
  restored package after the Release publish (re-check this if KNI is bumped). `Wall.cs:680` reads
  `level3.txt` via `new StreamReader(General.Path + "Levels/level3.txt")` — a WASM-filesystem read that
  can't hit the web server regardless of casing; it's a Level-3 gameplay concern, not on the menu/Level-1
  boot path, left for whoever does Level 3.

---

## Stage 9 — Polish

Input remapping/help screen for web, fullscreen + canvas resize handling, loading screen
(already styled in `wwwroot/css/app.css`), trim WASM download size
(`dotnet publish -c Release` + trimming/AOT), favicon/title/meta, mobile/touch (optional).

### Stage 9 — DONE. What was changed (read before Stage 10)

- **Download-size trim — the headline win (25.8 MB → 9.6 MB uncompressed boot payload; ~2.9 MB
  brotli; `_framework` 46 MB → 17 MB on disk; 217 → 77 payload files).** Stage 8 had to disable
  trimming because a *full* trim strips the `XmlSerializer` save-type members AND KNI's
  reflection-registered factories, white-screening the published build. The fix in
  `EvilAliensWeb.csproj`: **`PublishTrimmed=true` + `TrimMode=partial`**. Partial only trims
  assemblies marked `[IsTrimmable]` — i.e. the .NET BCL, which is where the bloat lived
  (`System.Private.Xml` ~3 MB, `CoreLib` 4.0→1.8 MB, and `System.Data.Common` /
  `DataContractSerialization` / `Encoding.CodePages` trimmed away entirely). The game assembly
  (`EvilAliensWeb`) and **every `nkast.*` engine assembly are NOT marked trimmable, so they stay
  WHOLE** — so the reflected save types and KNI's `Concrete*FactoryStrategy through reflection`
  registration all survive untouched. Plus **`InvariantGlobalization=true`** drops the ~2.5 MB ICU
  `.dat` set + globalization code (the game only uses culture-invariant `ToLower`/number
  formatting) and **relinks `dotnet.native.wasm` 2.4→1.1 MB** (it's a native rebuild — CI's
  existing `wasm-tools` workload covers it; the Release publish just takes a bit longer).
  `System.Private.Xml` is additionally pinned via `<TrimmerRootAssembly>` so XmlSerializer's own
  reflection internals can't be trimmed from under it (belt-and-braces vs the Stage-8 trap).
  - **Verified on a LOCAL Release publish before trusting CI** (the whole point of "verify
    locally first"): `dotnet publish -c Release -o <dir>` → served `<dir>/wwwroot` with a static
    server (`<base href>` stays `/` in the dev build, so it serves at localhost root) → real
    Chrome. Menu + tutorial level + attract gameplay all render, **0 console exceptions** (only the
    benign KNI factory-reflection boot logs), and the **save round-trip works under trimming**:
    toggled Music → Settings.xml written to localStorage with `<PlayMusic>false</PlayMusic>`
    (Serialize ✓) → reloaded → menu read it back as Disabled (Deserialize ✓). The CI
    (`deploy.yml`) is unchanged — it already runs `dotnet publish -c Release`, which now trims.
- **Keyboard controls-help screen un-stubbed.** Both `InstructionsMenu` (the menu Tutorial /
  in-game pause → "Instructions") and `HelpText` (the attract-mode demos `Demo1/2/3`) **already
  loaded `GFX/Help/Controls Keyboard`** but the Xbox build *skipped* `Displays.Keyboard` in its
  slide cycle (joypad-only — the `#if WINDOWS` PC-help was stripped). Removed those skip-branches
  so web players see the keyboard layout (WASD/arrows = move, mouse = fire/special/aim) alongside
  the gamepad slide. Pure logic change; the asset was always shipped.
- **Real browser fullscreen.** KNI's BlazorGL ignores `GraphicsDeviceManager.IsFullScreen`, so
  fullscreen is driven via the DOM Fullscreen API through **`Compat/FullscreenInterop.cs` →
  `window.eaFullscreen`** (`index.html`). Two entry points: (1) a always-visible **corner button**
  (the guaranteed-gesture path — verified its click handler fires and the API is available;
  couldn't enter fullscreen under *automation* only because synthetic clicks carry no
  `navigator.userActivation`, a harness limitation, not a code bug); (2) the existing in-menu
  **"Fullscreen"** option (`MenuScene` → `Game1.GoFullScreen`), now best-effort via interop within
  the keypress's transient-activation window. Entering/leaving fullscreen just resizes the canvas,
  which KNI already follows and `Game1.Draw` letterboxes — no graphics changes. The button is
  auto-hidden where `requestFullscreen` is unavailable (iOS Safari).
- **On-screen touch controls (basic mobile support).** `index.html` adds a touch overlay (D-pad,
  a FIRE/select button, a BACK/pause button), shown only on touch devices
  (`ontouchstart`/`maxTouchPoints`). It feeds the **same `DebugInput` injection seam** the `eaPress`
  test helper uses: a new persistent **`Hold(key,down)` / `touchHeld[]`** path (JS `eaHold`) added
  alongside the existing tick-countdown, both drained by `InputHandler` via `DebugInput.Consume`.
  So a held D-pad button reads exactly like a physical key held across frames (no rAF-timing race).
  **Verified end-to-end**: `eaHold('Right')` drove the player ship across the screen; layout
  (D-pad bottom-left, FIRE bottom-right, BACK top-left, fullscreen top-right) confirmed, with
  `env(safe-area-inset-*)` for notched phones. FIRE = hold `Mouse1` (gameplay) + a one-frame
  `Enter` tap on press (menu select / Press Start); BACK = `Esc`.
- **Favicon + SEO/social meta.** Added `wwwroot/favicon.svg` (an inline-SVG alien head — crisp at
  any size, ~1 KB) and `description` / `theme-color` / OpenGraph / Twitter-card meta in
  `index.html`. The `og:image` is an **absolute** URL to the live host (`…/Content/gfx/preview/
  poster.png`) on purpose — that's independent of the `<base href>` CI rewrites for Pages.
- **Loading screen:** already correct (the styled `#loading` in `app.css` shows during the
  `_framework` boot download and is replaced when Blazor mounts `App` into `#app`) — left as-is.
- **Gotchas for later stages:** (1) `InvariantGlobalization` now applies to **Debug runs too**, so
  all number/string formatting is culture-invariant everywhere — don't add culture-dependent
  parsing/formatting. (2) The on-screen UI lives in `index.html` **outside `#app`** so it survives
  Blazor's mount; a new scene needing a HUD button should follow that (or it gets wiped when `App`
  renders). (3) The keyboard-help slide's natural homes are the **attract demos** and the **in-game
  pause → Instructions** — there's no standalone "controls" menu entry. (4) Stage 10's larger
  design-resolution target will change the touch overlay's relationship to the scene only if it
  reads back-buffer size — it doesn't (it's pure DOM over the canvas).

---

## Stage 10 — Unified hi-res render path

**Goal:** the original 800×600 content and the new high-resolution textures render through
**one** scene target so they share the same effects (gamma, bloom, sprite shaders), the same
render-target tricks, and the same present/letterbox blit — instead of the current bolt-on
"separate pass" for the hi-res art.

**Context / why this exists:** the game is authored in a fixed **800×600** coordinate space, and
Stage 4 renders that whole frame into an offscreen `sceneTarget` (`RenderTarget2D`) which Stage 5's
gamma + bloom operate on before the present blit letterboxes it to the window (see the Stage 4/5
notes + "Resolution = a presenter" in `CLAUDE.md`). The new hi-res textures being added today are
drawn in a **separate pass**, so they bypass that pipeline: they get gamma/bloom/sprite-effects
inconsistently or not at all (e.g. the present-blit gamma applies to the 800×600 target but not to
a hi-res overlay drawn after it → mismatched brightness; bloom only ever sees the lo-res target).
That divergence gets worse with every effect added. We want a single scene the whole game draws
into, at a resolution high enough that the new textures keep their detail while the old 800×600 art
is upscaled into it.

**Proposed approach (the "render bigger + upscale the old sprites" idea):**
- Make the scene target a **larger "design-resolution" target** at the same 4:3 aspect (e.g.
  1600×1200 = 2×, or 1920×1440, or match the window capped to a max). Call the ratio
  `renderScale = renderW / 800`.
- Keep **all game logic in 800×600 space** (positions, hitboxes, the menu layout around origin
  400,300 from Stage 5). Don't touch gameplay coordinates.
- Upscale the legacy content for free by passing a uniform `Matrix.CreateScale(renderScale)`
  transform to every legacy `SpriteBatch.Begin`, so the 800×600 draws fill the big target.
  Centralize this scale (one source of truth) — many call sites `Begin` without a matrix today,
  and the bloom + menu offscreen targets are explicitly 800×600 (Stage 5), so they all have to
  move to the design res in lock-step.
- Draw the **new hi-res textures at native density into the same target**: position them in
  800×600 space (so they sit correctly relative to everything else) but let their footprint map to
  `renderScale×` more texels (draw at an explicit render-space destination rect / without the
  upscale baked in) so they stay crisp instead of being a blurred scaled-up 800×600-sized blit.
- Effects, render targets, and the present blit are then **structurally unchanged** — gamma + bloom
  + sprite shaders are pixel shaders that operate per-texel on whatever the target's size is, so
  they apply uniformly to both lo-res and hi-res content. The present blit keeps letterboxing the
  (now larger) target to the window.

**Steps:**
1. Introduce a single `renderScale` / design-resolution (res + scale-from-800×600) and route
   `sceneTarget`, the bloom targets, and the menu `myRenderTarget`s through it (replace the
   hard-coded 800×600 from Stages 4/5).
2. Make every legacy `SpriteBatch.Begin` use the shared scale transform (centralize in
   `SpriteBatchWrapper` + the few raw `Begin` sites). Verify the `BaseRenderTarget` redirect
   (Stage 4) still funnels every `SetRenderTarget(0, null)` into the resized scene target.
3. Convert the hi-res textures' draws to native-density (own destination rects, no double-upscale)
   and **delete the separate hi-res pass** once they go through the shared batches.
4. Re-tune anything pixel-size-relative: the bloom blur kernel/threshold (GaussianBlur offsets are
   texel-relative so the spread mostly tracks, but verify the look), and any layout code reading
   `BackBufferWidth/Height` (must use the design res, per the Stage-5 rule).
5. Pick texture filtering per content: linear for hi-res, and decide point vs linear for the
   upscaled 800×600 art (point = crisp pixels, linear = the soft look it has now) — sampler state
   per batch.

**Gotchas:**
- **Don't re-pin `PreferredBackBuffer`** — KNI still owns the window/back-buffer size (Stage 4);
  this all happens on the offscreen design target, blitted at present.
- The menu **"lightspeed warp" trail** relies on `myRenderTarget` keeping
  `RenderTargetUsage.PreserveContents` and not being cleared (Stage 5) — preserve that flag when
  you resize those targets.
- Larger targets cost VRAM + fill rate; cap the design res sensibly (a 4× target is 16× the
  pixels). Ties into Stage 9's download-size / perf budget.
- Tightly coupled to **Stage 4 (the presenter)** and **Stage 9 (fullscreen + canvas resize +
  integer-scale options)** — ideally sequence this *with* Stage 9's resize work rather than after,
  so the scene target and the fullscreen scaling are designed together.

**Open questions:**
- **Fixed design res vs. match-the-window?** Fixed (e.g. 2×) is simplest and matches "render
  bigger + upscale"; matching the window is crispest but means recreating every render target on
  resize and re-verifying bloom each time.
- How are the new hi-res assets authored/sized — at a known native resolution, or per-asset? That
  decides whether "native density" is one global factor or per-sprite.

**Done when:** the new hi-res textures and the original 800×600 art render in a single pass that
shares gamma, bloom, and the sprite effects (consistent brightness/blooming across both), the
separate hi-res pass is gone, and the result still letterboxes correctly to any window size with no
`PreferredBackBuffer` regression.

### Stage 10 — DONE. What was changed (read before Stage 11)

- **The plan above was written before the overlay existed — reality had diverged.** Between Stage 5
  and Stage 6, the "Revenged Edition" reskin (commit `20b9ba6`) added a **`HiResOverlay`**: a
  native-window-resolution pass at *present* time that drew the only two hi-res assets (the menu
  title `title-revenged.png` ~1.9 MB, and the channel-flip splash reveals up to ~3 MB) crisply,
  **already** with matched gamma AND its own glow-bloom. So the real divergence Stage 10 had to fix
  was narrow: **two separate bloom implementations** (the full `BloomComponent` over the 800×600
  scene vs. the overlay's simpler `BuildOverlayGlow`). The chosen fix (user-picked over a
  keep-the-overlay option and a fixed-2× option) is the **full rework to one window-resolution
  target** — the plan's intent, modernized to *match-the-window* instead of a fixed 2×.
- **One source of truth: `Compat/RenderScale.cs`.** Holds the current render resolution = the
  window's **4:3 letterbox** size (`min(win/800, win/600)`), **capped at 1440px tall** (a 4K
  fullscreen would otherwise render a ~3840×2880 scene + bloom every frame; the legacy art is 600px
  and the hi-res art ≤ ~1920px wide, so beyond the cap the present blit's bilinear upscale is
  invisible — tunable `MaxHeight`). Exposes `Width`/`Height` (the render-target size) and **`Matrix`**
  = per-axis `CreateScale(Width/800, Height/600)` so design corners map exactly onto the target
  (no sub-pixel edge seam). `Game1.Draw` calls `RenderScale.Update(backBufferW, backBufferH)` once
  per frame, before any size-dependent target is (re)created.
- **The design→render scale is applied at ONE choke point.** `SpriteBatchWrapper._beginDrawing` now
  passes `RenderScale.Matrix` to every `SpriteBatch.Begin`, so all the game's 800×600-design draws
  scale up to fill the bigger target automatically. (The custom sprite effects are pixel-only — the
  internal sprite VS stays bound — so the transform flows through them unchanged.) **Every** game
  draw goes through this wrapper (verified: the only raw `SpriteBatch`es are Bloom's own + Game1's
  present, both using explicit dest rects), so there were no scattered `Begin` sites to chase.
- **The two new wrapper methods for the exceptions to that rule:**
  - **`DrawPresent(tex, position, origin, scale, color)`** — an **identity-transform** 1:1 blit, for
    compositing a *render-sized* offscreen target back into the scene (the scale matrix would
    double-up here). Used by the menu (`MenuSub1`/`MenuScene`) and the background cross-fade
    (`Background`).
  - **`DrawEffect(tex, designRect, effect, configure)`** — a one-off batch with a **custom
    full-frame pixel effect** + the design→render matrix, for the splash **channel-flip**
    (`channelflip.fx`); `configure` sets the effect params (the old splash is s0, the reveal is the
    `NewTexture` param).
- **Every offscreen render target moved from 800×600 (or window-sized) to `RenderScale.Width×Height`,
  recreated on a size change** (a shared `EnsureTargets()`/`EnsureRenderTarget()` per owner, called at
  the top of its `Draw`): `Game1.sceneTarget`; the three `BloomComponent` targets (resolve +
  half-res ping-pong); the `MenuScene` + `MenuSub1` menu targets (the warp-trail `PreserveContents` +
  clear-once is kept); `Background`'s cross-fade target (also switched from the 16-bit
  `(SurfaceFormat)2`, which renders nothing on WebGL, to `SurfaceFormat.Color` — the same trap Stage 5
  hit); and `GameScene.MyScreenShot` (so the level-select thumbnail keeps a 1:1 4:3 capture). The
  bloom Gaussian offsets are texel-relative so the blur tracks the resolution automatically.
- **Full-screen fades were drawn at `viewport.Width/Height` (window pixels) — now `(0,0,800,600)`
  design space** (the matrix scales them to the target). Fixed in `Background`, `MenuScene`,
  `SplashScene`, `CreditsScene`, `ConfirmationMenu`, `HelpText` (the `Background.fadeBackBufferToBlack`
  one was *already* design-space — that was the precedent). Reading the viewport would over/under-cover
  the target once the wrapper applies the scale.
- **The hi-res art now rides the unified scene.** The menu **title** is drawn in-scene in
  `MenuSubWithSkull.DrawMenu` (it runs while the menu's render-sized target is bound, so it's crisp at
  render res) and **blooms through the main `BloomComponent`** — the whole point: consistent glow with
  everything else, no separate pass. The **channel-flip** reveal goes through `DrawEffect`.
- **Deleted:** the entire overlay/glow pass — `Game1.PresentHiResOverlay` + `BuildOverlayGlow` +
  `overlayTarget`/`glowTargetA`/`glowTargetB`/`glowBlur`/`PremultipliedAdditive` + a dead
  window-sized `resolveTarget` field, and the whole **`Compat/HiResOverlay.cs`** (its `Premultiply`
  helper — still needed for the straight-alpha title — moved to the new `Compat/TextureUtil.cs`).
  The `glowblur.mgfxo` asset is now unused (left on disk, harmless).
- **Present blit is structurally unchanged** and needed no edits: it still computes the window
  letterbox and stretches `sceneTarget` into it through the gamma shader — now a **1:1 copy** when
  uncapped, a bilinear **upscale** only when the 1440px cap engages on a very large window.
- **Verification (real Chrome, per CLAUDE.md — 0 console exceptions throughout, only the benign KNI
  `*Factory not found → reflection` boot logs):** `?menu` shows the crisp hi-res title now glowing
  via the main bloom over the planet backdrop; Start → **level-select** submenu composites correctly
  (the render-sized RT + entry animation via `DrawPresent`); `?level=Level1` renders the **hi-res
  Earth backdrop crisp at full window res** with the player ship + alien UFOs (colorize/additive +
  bloom) and the corner-anchored 4-player HUD all correctly scaled; a **normal boot** shows the EA
  splash and the **channel-flip "I Made This" reveal** (the `DrawEffect` path) crisply; and a
  **window resize** recreated every target with no exceptions/corruption. (The brief all-white frame
  right after `?level=Level1` is just the level's warp-in flash, not a bug — it resolves to gameplay.)
- **Gotchas for Stage 11 / future work:** (1) anything that draws into the scene must go through
  `SpriteBatchWrapper` (gets the scale) — a raw `SpriteBatch.Begin` would draw at design size in the
  top-left of the bigger target; if you must, multiply by `RenderScale.Matrix` yourself. (2) A
  *render-sized* offscreen target composited back into the scene must use `DrawPresent` (identity),
  NOT a normal scaled draw. (3) New offscreen targets should size to `RenderScale.Width×Height` with
  `SurfaceFormat.Color` and recreate on size change. (4) Full-screen overlays use `(0,0,800,600)`
  design coords, never the viewport. (5) The 1440px height cap (`RenderScale.MaxHeight`) is the one
  perf knob; raise it for crisper 4K at higher fill cost.

---

## Stage 11 — Online co-op multiplayer

**Goal:** two-to-four players on different machines play the same game together over the
internet (shared-screen co-op), joining a match via a shareable link.

**Why this is realistic here (the architecture is already on our side):** this was an Xbox
*couch co-op* game, and the N-player structure survived the decompile intact. We are NOT
adding multiplayer to a single-player game -- we are making existing **local** co-op work over
a wire. Four facts from the code make it tractable:
- **Already 4-player.** `InputHandler` tracks 4 independent pad slots (`padkeyspressed[4][]`,
  `PlayerIndex` 0-3) plus a keyboard player (`Game/EvilAliens/InputHandler.cs:57`); the sim is
  written around a variable player count -- `oracle.Players`, `oracle.GetShips()`, per-player
  `ControlDevice`, ships spawning at `600/(Players+1)` slots (`PlayerShip.cs:1040`).
- **Input is a clean injection seam.** Every ship reads its input by controller index through
  `IInputHandlerService` -- `input.LeftStick(i)`, `input.PadDown(key,i)`, `input.Down(MyKeys...)`
  (`PlayerShip.cs:388-460`). A remote player is just a **virtual gamepad**: wrap/replace
  `InputHandler` so pad slots 2-4 (or the `Generic` device) read network packets instead of
  `GamePad.GetState()`. Gameplay code never needs to know the input came from the network.
- **Randomness is centralized.** One global RNG -- `RandomHelper._random`
  (`Game/EvilAliens/RandomHelper.cs:8`) -- not `new Random()` scattered everywhere (the only
  other one, `SplashScene.rng`, is cosmetic and pre-gameplay). That makes deterministic netcode
  *auditable* instead of hopeless.
- **Shared-screen co-op = the friendly case.** Everyone plays in one 800x600 space against the
  aliens; there's no competitive fairness to protect, so latency only ever makes *your own*
  ship feel slightly heavy. No client-side prediction of *other* players is required for
  correctness.

**The one real tax (decide first):** Blazor WASM cannot open raw UDP, and the whole port's goal
is "output = a static site." Online play needs **a** server no matter what -- at minimum a tiny
signaling/relay process. "Static site on a CDN" becomes "static site + one always-on service."
That infrastructure, not the game code, is the genuinely new cost. **Keep single-player working
with zero server** (multiplayer opt-in) so the Stage-8 CDN build still stands alone.

### Decision 1 -- netcode model
- **Lockstep / input-sync** (send only inputs each tick; every client runs the identical sim):
  maps almost perfectly onto the input seam above -- architecturally we're most of the way there.
  Risk = **determinism**. It's `float`/`Vector2` physics with trig, but every client runs the
  *same wasm binary*, so cross-machine float divergence is far less scary than cross-platform;
  the single RNG makes seeding tractable. Hard requirements: a **fixed timestep** (impossible on
  a variable `dt`) and deterministic-or-disabled AI bots.
- **Host-authoritative / state-sync** (host runs the real sim; clients send inputs and render
  snapshots): sidesteps the determinism rabbit hole entirely -- which matters for *decompiled*
  code we only partly understand. Cost = serializing hundreds of entities (bullets, asteroids,
  explosions) per tick; doable for a 2D game with delta compression, but more new code, and it
  needs client-side prediction so the local ship feels responsive.

**Recommendation:** prefer **lockstep** *if* the determinism spike (Step 1) passes, because the
input architecture is so clean; fall back to host-authoritative state-sync if determinism proves
flaky. Either way it's **co-op only** (all-vs-aliens), shared camera.

### Decision 2 -- transport
- **WebSocket relay** -- TCP, reliable-ordered, trivial to host (one small process relays packets
  between players in a room). Head-of-line blocking adds latency under packet loss, but co-op is
  forgiving. **Least infrastructure -- recommended for v1.**
- **WebRTC DataChannel** -- UDP-like (unreliable/unordered available), lowest latency. Needs a
  signaling server to introduce peers *and* a STUN/TURN server for NAT traversal (a TURN relay
  for the ~10-20% who can't connect peer-to-peer). Move here if input latency feels bad.

### Determinism audit (the gate for lockstep -- spike it before committing)
1. **Fixed timestep?** Confirm `Game1` runs `IsFixedTimeStep=true` with a fixed
   `TargetElapsedTime`, and that gameplay advances on a fixed `dt`, not wall-clock
   `gameTime.ElapsedGameTime`. The JS rAF loop (`wwwroot/index.html` -> `TickDotNet`) and the
   Stage-4 `GameTime` scaling in `Game1.Update` both need checking; if movement scales by real
   elapsed time, a fixed-step conversion comes first. *(Not yet verified -- this is the first
   thing to check.)*
2. **Seed + step one RNG.** Seed `RandomHelper._random` from a shared match seed; `.Next()`
   consumption order is then identical iff the sim is identical (the usual lockstep
   circularity -- one RNG source makes it provable).
3. **AI bots.** `ControlDevice.AI` (attract-mode `Demo1/2/3` and AI-filled co-op slots,
   `PlayerShip.DoAIMove/DoAIFire`) must be deterministic or disabled in networked matches.
4. **No per-client nondeterminism in the sim:** wall-clock reads, `Date.now()`/`Math.random()`
   (banned in this project anyway), unordered-collection iteration, frame-rate-dependent timers.

### Steps
1. **Spike (decides the model).** Confirm the fixed timestep; add a shared-seed hook to
   `RandomHelper`; run two browser tabs feeding the *same scripted input stream* and diff the
   resulting game state after N frames (positions/score/RNG counter). Match -> lockstep is open;
   diverge -> go host-authoritative.
2. **Virtual-gamepad seam.** Add an `INetworkInputSource`; make `InputHandler` read the networked
   slots' `PadDown/PadPressed/LeftStick/RightStick` from it, leaving the local player on real
   hardware. This is the small, central game-side change everything else hangs off.
3. **Lobby + transport.** Stand up the WebSocket room server; add Blazor JS-interop for
   send/receive; build a "host / join by link" lobby. Assign each player a `ControlDevice` slot
   and gate match start on everyone ready.
4. **Sync layer (per chosen model).**
   - *Lockstep:* exchange per-tick input frames with a small **input-delay** buffer (2-3 frames);
     a client advances tick T only once all inputs for T arrive; add a periodic state checksum
     and disconnect + report on a desync mismatch.
   - *State-authoritative:* host serializes delta-compressed entity snapshots; clients
     interpolate remote entities and predict + reconcile the local ship.
5. **Drop / rejoin / pause.** Handle a mid-match disconnect (hand the ship to AI or despawn);
   surface latency/desync to the UI.
6. **Hosting.** Deploy the relay (small VPS / Fly.io / serverless WebSocket) and wire its URL
   into the static build's config. Update Stage 8's CI + docs to note the external dependency.

### Gotchas
- **Static-site break:** Stage 8 ships a pure static site; this adds a runtime server. Keep
  single-player fully working with **no** server so the CDN build still stands alone.
- **No WASM threads:** the network client must be async on the single game thread (same
  constraint that forced Stage 4 synchronous) -- poll the socket in the game loop, don't block.
- **Inject above KNI, not via DOM:** the virtual gamepad feeds the *game's* `IInputHandlerService`,
  NOT synthetic DOM `KeyboardEvent`s -- those throw in KNI's WASM keyboard interop (Stage 4/5
  notes: `JSON value could not be converted to System.Int32`).
- **Clock for timeouts only:** `Math.random()`/`Date.now()`/`new Date()` are banned in sim code;
  any netcode timestamps (RTT, timeouts) live in the JS/interop layer, never in the
  deterministic path.
- **TURN cost (WebRTC only):** ~10-20% of players need a TURN relay; budget for it or accept that
  some can't connect peer-to-peer.

### Open questions
- **Lockstep vs state-sync** -- resolved by the Step-1 spike, not up front.
- **Player cap:** keep the original 4, or limit online to 2 for bandwidth/latency headroom?
- **Matchmaking:** private "share a link" rooms only (cheap, no backend state), or a public game
  list (needs a lobby service)?
- **Mid-game join:** allow drop-in like the local game (`AddPlayer` on Start), or lock the roster
  at match start (simpler for sync)?

**Done when:** two players on different machines join one match via a link and play a level
together with responsive controls; scores/lives/enemies stay in sync for the whole level; a
disconnect is handled without crashing -- and the single-player static build still runs with no
server.

---

## Appendix — how the recovery was done (for reference)

- The package is an Xbox 360 **STFS "LIVE"** container. Extracted in Python by parsing the
  volume descriptor (base `0xB000`, single hash-table layout, file table at data-block 0) and
  reading each file's contiguous data blocks through the hash-block interleave. See the
  extraction logic in the session history; assets are already in `extracted/`.
- `EvilAliens30.exe` decompiled with `ilspycmd -p` → `src_decompiled/` (224 files, ~39.6k LOC).
- Title ID `584E07D2`, © 2008, XNA Game Studio 3.x, XBLIG.
