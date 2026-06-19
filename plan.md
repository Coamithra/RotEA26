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
- [ ] Stage 3 — Content pipeline (assets load)
- [ ] Stage 4 — First boot / playable core (threading, startup, input)
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
