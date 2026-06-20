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
- [ ] Stage 5 — Shaders (bloom + sprite effects)
- [ ] Stage 6 — Audio (XACT → modern)
- [ ] Stage 7 — Saves & awardments persistence (localStorage)
- [ ] Stage 8 — GitHub hosting + Pages deploy (public)
- [ ] Stage 9 — Polish (input map, fullscreen, download size)

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

---

## Stage 6 — Audio

**Goal:** music + SFX play in the browser.

**Context:** audio is XACT (`Content/SFX/*.xgs/.xsb/.xwb`), driven by
`Game/EvilAliens/SoundManager.cs` (+ `SongInstance.cs`). KNI/web XACT support is limited.

**Steps:**
1. Extract the WAV cues from the `.xwb` wave bank (the format is documented; `unxwb`-style
   tools exist) and map cue names from the `.xsb` sound bank.
2. Rewrite `SoundManager` to use `SoundEffect`/`SoundEffectInstance` (SFX) and `Song`/
   `MediaPlayer` (music) loaded from WAV/OGG — drop `AudioEngine`/`SoundBank`/`WaveBank`.
3. Handle the browser autoplay policy (audio must start after a user gesture).

**Done when:** title music + core SFX play.

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

---

## Stage 9 — Polish

Input remapping/help screen for web, fullscreen + canvas resize handling, loading screen
(already styled in `wwwroot/css/app.css`), trim WASM download size
(`dotnet publish -c Release` + trimming/AOT), favicon/title/meta, mobile/touch (optional).

---

## Appendix — how the recovery was done (for reference)

- The package is an Xbox 360 **STFS "LIVE"** container. Extracted in Python by parsing the
  volume descriptor (base `0xB000`, single hash-table layout, file table at data-block 0) and
  reading each file's contiguous data blocks through the hash-block interleave. See the
  extraction logic in the session history; assets are already in `extracted/`.
- `EvilAliens30.exe` decompiled with `ilspycmd -p` → `src_decompiled/` (224 files, ~39.6k LOC).
- Title ID `584E07D2`, © 2008, XNA Game Studio 3.x, XBLIG.
