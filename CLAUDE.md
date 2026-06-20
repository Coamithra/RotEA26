# CLAUDE.md — Revenge of the Evil Aliens (web port)

Porting a recovered 2008 **XBLIG** (XNA 3.x, C#) to run in the browser via **KNI**
(a MonoGame fork with a Blazor WebAssembly / WebGL backend). Output = a static site.

**`plan.md` is the staged plan** (content → boot → shaders → audio → saves → hosting → polish);
each stage is written to be done independently with fresh context. This file is how to *work*
in the repo. (Global prefs — `rtk` git, `python` not `python3`, the `edit_unicode.py` helper —
still apply.)

## Build / run / verify
```sh
cd web/EvilAliensWeb
dotnet build -c Debug
dotnet run -c Debug --urls http://localhost:5280     # then open the URL
```
- **A clean `dotnet build` does NOT mean it runs.** WASM runtime errors only appear in the
  **browser console** — always verify visually *and* read the console. Use the preview tools
  against the `eaweb` config in `.claude/launch.json` (`preview_start` → `preview_screenshot`
  → `preview_console_logs`).
- **Verify with the `claude-in-chrome` MCP, not `preview_screenshot`.** A real foreground
  Chrome tab screenshots + reads console reliably; the built-in preview renderer wedges
  whenever *its* tab is backgrounded (the rAF game loop pauses, so it never paints). Flow:
  `preview_start` to serve → in Chrome `navigate` to `http://localhost:5280` → `wait` ~10s
  for WASM → `computer{screenshot}`/`zoom` + `read_console_messages`.
- **Debug boot shortcuts (opt-in via URL query — use these instead of fighting the splash/
  press-start/menu when testing).** Parsed once at boot in `Compat/DebugFlags.cs` (wired via
  `wwwroot/index.html` `getDebugQuery` → `Pages/Index.razor.cs`). No query = normal boot, so
  a shipped build is unaffected. Flags (combine with `&`):
  `?menu` (straight to main menu, skips splash + auto-"Press Start") ·
  `?noattract` (disable the menu's 20s idle→demo attract) ·
  `?level=<Name>` (boot straight into a level, bypassing the menu — `<Name>` is a `Levels`
  value, e.g. `Level1`/`Level2`/`Level3`/`ClassicAliens`/`SpaceDodge`) ·
  `?skipsplash` / `?autostart` as building blocks. e.g. `…:5280/?level=Level2&noattract`.

## Toolchain (already installed)
- .NET 8 SDK + `wasm-tools` workload (Emscripten / mono browser-wasm).
- KNI `4.1.9001.*` (`nkast.Xna.Framework.*`) — **this is the engine**; namespace is
  `Microsoft.Xna.Framework` and the API is XNA **4.0** (the game was 3.x → mind the gap).
- `ilspycmd` decompiler: run as `DOTNET_ROLL_FORWARD=LatestMajor ilspycmd ...`.

## Layout
| Path | What |
|---|---|
| `web/EvilAliensWeb/Game/` | the ported game code — **edit here** |
| `web/EvilAliensWeb/Compat/` | Xbox-API + XNA-3.x→4.0 shims |
| `web/EvilAliensWeb/wwwroot/` | host page + JS game-loop glue |
| `src_decompiled/` | decompiled reference source (read-only) |
| `extracted/584E07D1/Content/` | game assets unpacked from the package |
| `tools/` | scripts that DERIVED `Game/` from `src_decompiled/` |

## Critical context / gotchas
- **The recovered code is the Xbox BUILD.** Anything under `#if WINDOWS` / `[Conditional]`
  was stripped at compile time and is unrecoverable from this binary. Re-create PC-only
  behaviour; don't hunt for it. (If a PC `.exe` ever turns up, decompile that too.)
- **`Game/` is GENERATED** from `src_decompiled/` by `tools/*.py` (ref-cast artifacts,
  `_002Ector`→`new T(...)`, `((Base)this).` → `base.`, 3.x→4.0 edits). They're already applied.
  **Do NOT re-run them** — they rewrite `Game/` from the pristine source and would clobber any
  later hand edits. Edit `Game/` directly.
- **Shims in `Compat/` fake the Xbox APIs.** GamerServices = no-ops (full game unlocked,
  `SignedInGamers` empty so per-gamer loops do nothing); Storage = WASM in-memory FS, now
  **mirrored to browser localStorage** so saves persist across reloads (Stage 7 — `StorageStub`'s
  `PersistentSave` + `Compat/SaveInterop.cs` + `eaSave` in `index.html`). `ResolveBackBuffer` and
  the `SpriteBlendMode`→`BlendState` mapping are now **real** (Stage 5); the `Effect`/`EffectPass`
  `Begin/End` no-op shims are dead (no callers).
- **It runs (Stages 4–7 done).** `Game1` boots through splash → menu → playable/attract gameplay
  with shaders (gamma, bloom, sprite effects), **audio** (music, SFX, speech) **and persistent
  saves** (settings/unlockables/awardments/screenshots → localStorage) and 0 console exceptions.
  Remaining: hosting (8), polish/fullscreen (9), unified hi-res render path (10). See `plan.md`
  "Stage 4/5/6/7 — DONE" for what changed and the stubs each
  later stage must un-stub.
- **Shaders (Stage 5):** the lost `.fx` were rewritten in `tools/shaders/src/` and compiled
  offline to MGFX v10 GLSL `.mgfxo` by `tools/shaders/build_shaders.py` (KNI's MGCB, BlazorGL
  target — needs `nkast.Xna.Framework.Content.Pipeline.Builder.Windows 4.1.9001` restored in the
  nuget cache). `WebContentManager` loads them via `new Effect(gd,bytes)`. **Re-run the script
  after editing any `.fx`; don't hand-edit `.mgfxo`.** Effects apply via `SpriteBatch.Begin(effect)`
  (4.0 model), not `effect.Begin()`.
- **Audio (Stage 6):** the lost XACT runtime is replaced, not ported. `tools/audio/` cracks the
  big-endian Xbox banks in pure Python (`xact.py` parses `.xwb`/`.xsb`; PCM SFX + **xWMA music**
  decoded via **PyAV**) and `build_audio.py` writes `wwwroot/Content/{sfx,vo}/*.wav`,
  `music/*.ogg` + `music/music.json`. **Re-run `python tools/audio/build_audio.py` after changing
  the banks or the ElevenLabs renders; don't hand-edit the outputs.** SFX/speech play on KNI
  `SoundEffect`; **music** is a WebAudio layer (`index.html` `eaMusic`, via `Compat/MusicInterop.cs`)
  for seamless loop points. `SoundManager.Play()` now returns a `SoundEffectInstance` (not `Cue`).
- **Sign-in / keyboard:** `SignedInGamers` is still empty, but the XBLIG sign-in gate is gone —
  the PC keyboard path was recreated, incl. **reconstructing the `#if WINDOWS`-stripped
  keyboard-read block in `InputHandler.Update()`** (the Xbox build discarded `Keyboard.GetState()`
  and left the `keysToCheck` table dead). Keyboard: arrows/WASD move, Enter select/start, Esc back.
- **Game loop is JS-driven:** `wwwroot/index.html` (`initRenderJS`/`tickJS`) →
  `Pages/Index.razor.cs` `TickDotNet()` → now `new EvilAliens.Game1()`. `ContentTestGame.cs` /
  `SpikeGame.cs` are dead harnesses, safe to delete.
- **Real keyboard input works** — KNI maps keys via **`event.keyCode`** (decompiled from
  `Kni.Platform`: `Keys = (Keys)keyCode`), so Enter/arrows/WASD/Esc are correct for real users.
  When *driving* the browser, prefer **real OS keys** via the claude-in-chrome `computer` `key`
  action (held across a frame). **Synthetic JS `KeyboardEvent`s do NOT work** — KNI's WASM
  keyboard interop throws `JSON value could not be converted to System.Int32` on the faked
  `keyCode` and can leave a key stuck. Click-to-focus the canvas first.
- **For automated/headless input, use `eaPress(...)` — don't fight OS-key timing.** `InputHandler`
  polls `Keyboard.GetState()` once per tick; a scripted keydown+keyup fired between two ticks is
  added-and-removed before any poll sees it, so the press is dropped (the "stuck on Press Start"
  churn). `Compat/DebugInput.cs` (JS wrapper `eaPress` in `index.html`) injects a key as a per-key
  tick COUNTER that `InputHandler` drains *inside* the tick, so it can't fall between polls. From
  the console / automation: `eaPress('Enter')` (tap), `eaPress('Up')`, `eaPress('Left', 30)` (hold
  ~30 ticks). Keys: Up/Down/Left/Right/Enter/Esc/Mouse1/Generic_Start (+ w/a/s/d, start/select→Enter,
  back→Esc, fire→Mouse1). Rapid repeats of the SAME key collapse into one press — space them by a
  tick (one per automation step) to register distinct taps.
- **Stubs that will read as "broken" until their stage (not bugs):**
  the controls-help screen shows the **Xbox joypad** (PC keyboard help was `#if WINDOWS`-stripped,
  Stage 9). *(Audio is no longer stubbed — Stage 6 done. Saves now persist to localStorage —
  Stage 7 done.)*
- **Resolution = a presenter, not a pinned back buffer.** KNI's BlazorGL forces the back buffer to
  the browser window size and rewrites `PreferredBackBuffer` on every resize, so a fixed 800×600
  reverts. `Game1.Draw` renders the 800×600 frame into an offscreen `sceneTarget` and blits it
  scaled+letterboxed to the window; the game's `SetRenderTarget(0, null)` calls are redirected to
  that target via `Xna3GraphicsDeviceCompat.BaseRenderTarget`. Don't re-introduce a pinned
  `PreferredBackBuffer`. Stage 5 applies the gamma shader on the present blit of this target, and
  bloom renders into it (its targets are sized 800×600 to match); Stage 9 adds fullscreen.

## Don'ts
- Don't commit `bin/`/`obj/` or the raw 52 MB Xbox package (all `.gitignore`d).
- Don't re-run `tools/*.py` against `Game/` (regenerates it from scratch).
- Don't trust a screenshot for colours/blending while shaders are stubbed.
