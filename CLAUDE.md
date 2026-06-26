# CLAUDE.md — Revenge of the Evil Aliens (web port)

Porting a recovered 2008 **XBLIG** (XNA 3.x, C#) to run in the browser via **KNI**
(a MonoGame fork with a Blazor WebAssembly / WebGL backend). Output = a static site.

**The project tracker is now the local Trello board (see "Project tracking" below); `plans/plan.md` is a historical artifact — the archived staged plan** (content → boot → shaders → audio → saves → hosting → polish);
each stage was written to be done independently with fresh context. This file is how to *work*
in the repo. (Global prefs — `rtk` git, `python` not `python3`, the `edit_unicode.py` helper —
still apply.)

## Project tracking (Trello — local backend)
A **local** (offline, file-backed) Trello board tracks the staged plan. It is NOT on trello.com —
it lives in the `trello` CLI's local store at `C:\Users\coami\Dropbox\Programming\FakeTrelloData`.
- **Board:** `RotEA26 — Evil Aliens Web Port` · id `10989a3d`.
- **Always pass `--backend local --board 10989a3d`** (the CLI's default backend is `trello`, and the
  active board is a *different* one). e.g.
  `trello --backend local --board 10989a3d board` (show), `... list ls`, `... card ls <listId>`.
- **Columns (list ids):** `Backlog` `79158996` · `In Progress` `3b43cba3` · `Done` `9c204b80`.
- **Cards = the plan's stages; the Trello board is now the live tracker.** Done: Stages 1-10, 12, 15. In Progress: Stage 13 (menu reskin). Backlog:
  Stage 11 (online co-op) + Stage 14 (trailers). Each card's description summarises that
  stage; the now-archived `plans/plan.md` holds the full per-stage detail. When a stage's status changes, `card move <id> <listId>`
  it and keep the description in sync.
- Browse it visually with `trello --backend local --board 10989a3d serve` (drag-drop kanban web app).
- **When picking up a card/task, FOLLOW [`CONTRIBUTING.md`](CONTRIBUTING.md).** It's the step-by-step
  runbook for this repo — claim the card (Backlog → In Progress), tracker doc, a per-card worktree
  (mandatory; slot `wt1`..`wt8`, dev server on port `528<k>`),
  research → design → implement, the visual+console verification gate (no unit tests here), PR
  self-merge (every push to `main` auto-deploys to Pages), and the card-close paperwork (move to
  Done, comment, follow-ups). Read it at the start of any card and work the phases in order.

## Build / run / verify
```sh
cd web/EvilAliensWeb
dotnet build -c Debug
dotnet run -c Debug --urls http://localhost:5280     # then open the URL
```
- **Debugging how an ENEMY/OBJECT draws (a sprite, frame, blend, tint, scale)? STOP — do NOT
  boot the game and try to screenshot a moving target.** Use the **sprite harness**:
  `…:5280/?harness=<Obj>&frame=<n>` boots straight to that object, frozen, on a space
  background, drawn by the real pipeline — so a screenshot is reliable every time. Full flag
  list + how it works + how to add an object are in the "Sprite harness" bullet below; the
  human picker is `wwwroot/harness.html`. Reach for this FIRST for any drawing-code change.
- **A clean `dotnet build` does NOT mean it runs.** WASM runtime errors only appear in the
  **browser console** — always verify visually *and* read the console. Use the preview tools
  against the `eaweb` config in `.claude/launch.json` (`preview_start` → `preview_screenshot`
  → `preview_console_logs`).
- **Verify with the `claude-in-chrome` MCP, not `preview_screenshot`.** A real foreground
  Chrome tab screenshots + reads console reliably; the built-in preview renderer wedges
  whenever *its* tab is backgrounded (the rAF game loop pauses, so it never paints). Flow:
  `preview_start` to serve → in Chrome `navigate` to `http://localhost:5280` → `wait` ~10s
  for WASM → `computer{screenshot}`/`zoom` + `read_console_messages`.
- **When working on GRAPHICS/textures, DO A HARD RELOAD -- and not the normal kind.** The
  browser caches content assets (`Content/**`) aggressively and serves the STALE copy after you
  regenerate one, so an edited sprite/texture silently "doesn't take effect" (cost an hour on the
  earth swap: the game kept loading the old 735px `earth.png` while the server already served the
  new 1480px one). A plain reload -- EVEN Ctrl+Shift+R -- does NOT reliably refetch it, because
  textures load LATE (during level preload), after the hard-reload cache-bypass window has closed.
  Reliable busts: (1) DevTools open -> Network -> tick "Disable cache", then reload (best while
  iterating); (2) right-click reload -> "Empty Cache and Hard Reload"; (3) from the console,
  `fetch('Content/gfx/...png', {cache:'reload'})` to refresh just that entry, then reload. Symptom:
  an asset change not showing up, OR a wrong on-screen SIZE (stale-dimensioned texture drawn at the
  new scale -- exactly the "earth is small" bug). Production (GitHub Pages) self-heals via ETag
  revalidation; this is mainly a local-iteration trap.
- **Debug boot shortcuts (opt-in via URL query — use these instead of fighting the splash/
  press-start/menu when testing).** Parsed once at boot in `Compat/DebugFlags.cs` (wired via
  `wwwroot/index.html` `getDebugQuery` → `Pages/Index.razor.cs`). No query = normal boot, so
  a shipped build is unaffected. Flags (combine with `&`):
  `?menu` (straight to main menu, skips splash + auto-"Press Start") ·
  `?noattract` (disable the menu's 20s idle→demo attract) ·
  `?level=<Name>` (boot straight into a level, bypassing the menu — `<Name>` is a `Levels`
  value, e.g. `Level1`/`Level2`/`Level3`/`ClassicAliens`/`SpaceDodge`) ·
  `?invuln` (force the Invulnerability cheat ON so playtesting a level doesn't keep dying;
  aliases `?invulnerability`/`?god`); `?unlockall` (reveal every gated menu option);
  `?skipsplash` / `?autostart` as building blocks. e.g. `…:5280/?level=Level2&noattract`.
- **Sprite harness — USE THIS to debug an object's drawing code instead of booting the game
  and trying to screenshot a moving enemy at the right instant.** `?harness=<Obj>` boots
  straight onto a space background showing ONE game object, drawn by its OWN `Draw()` through
  the real pipeline (same `SpriteBatchWrapper` / `RenderScale` / blend mapping / bloom / gamma),
  FROZEN — so a screenshot at any moment is pixel-identical (no timing to catch). It's 1:1
  because it reuses each type's real `NewXxx`+`Setup` and the real draw path; it only freezes
  time (object `Enabled=false` so gameplay `Update` never runs; the harness sets
  `Position`/`curframe`/`scale`/`rotation`). Companion flags: `?frame=<n>` (freeze frame, default 0)
  · `?play` (animate in place instead) · `?bg=space|spaceclassic|holodeck|mars|base|basedark`
  · `?pos=<x,y>` (design space, default 400,300) · `?objscale=<f>` (alias `?size`) · `?rot=<deg>`.
  e.g. `…:5280/?harness=Spider&frame=2` · `…/?harness=DeathStar&play` · `…/?harness=ufo&bg=mars`.
  Code: **`Compat/HarnessScene.cs`** (the scene) + **`Compat/HarnessRegistry.cs`** (name→factory;
  add an object in ONE line — call its `New*`+`Setup`). Wired in `Game1` next to the `?level=`
  path. Human picker: **`wwwroot/harness.html`** (dropdown + fields → builds the URL; keep its
  list in sync with the registry). Caveat: objects whose Draw depends on state only their Update
  reaches (mid-attack bosses, the spider's airborne sheet) show their spawned/idle pose — bosses
  are best-effort; the common per-frame sprite-sheet enemies are exact. Verify like any game
  change: real Chrome, not `preview_screenshot` (the rAF loop pauses when the tab is backgrounded).

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
- **It runs AND it's live (Stages 4–10 done).** `Game1` boots through splash → menu → playable/attract
  gameplay with shaders (gamma, bloom, sprite effects), **audio** (music, SFX, speech), **persistent
  saves** (settings/unlockables/awardments/screenshots → localStorage), **polish** (keyboard controls-help,
  browser fullscreen, favicon/meta, on-screen touch controls), a **unified hi-res render path** (legacy + hi-res art share one window-resolution scene, one bloom/gamma) and a **trimmed download** (9.6 MB
  uncompressed boot payload, ~2.9 MB brotli — down from 25.8 MB) and 0 console exceptions —
  **deployed publicly at https://coamithra.github.io/RotEA26/**, auto-rebuilt on every push to `main`.
  Remaining: online co-op (11) + trailers (14); menu reskin (13) in progress. See the archived `plans/plan.md`
  "Stage 4/5/6/7/8/9/10 — DONE" for what changed and the stubs each later stage must un-stub.
- **Hosting (Stage 8):** `.github/workflows/deploy.yml` does `dotnet publish -c Release` in CI (Pages
  can't build .NET), rewrites `<base href>` to `/RotEA26/` (project page), adds `.nojekyll` + `404.html`,
  and deploys via `actions/deploy-pages`. **The dev build keeps `<base href="/" />`** for local
  `dotnet run` — CI flips it; don't hard-code `/RotEA26/` in `index.html`. CI is unchanged from Stage 8;
  it just `dotnet publish -c Release`, which now **trims** (Stage 9).
- **Download trim (Stage 9):** the csproj now uses **`PublishTrimmed=true` + `TrimMode=partial`** (NOT
  full — full strips the `XmlSerializer` save types + KNI's reflection factories → white screen, the
  Stage-8 trap). Partial trims only `[IsTrimmable]` assemblies (the BCL, where the bloat was); the game
  assembly + every `nkast.*` engine assembly stay WHOLE, so reflected save types + factory registration
  survive. **`InvariantGlobalization=true`** drops ICU + relinks `dotnet.native.wasm` (native rebuild —
  also means **Debug runs are culture-invariant too**; don't add culture-dependent parse/format).
  `System.Private.Xml` is pinned via `<TrimmerRootAssembly>`. **Always verify a trim change with a LOCAL
  Release publish (publish → serve `wwwroot` at localhost root → real Chrome, check saves round-trip)
  before pushing** — trimming breakage only shows at runtime in the browser, not in the build.
- **GOTCHA — content paths are CASE-SENSITIVE on the live host (not on Windows).** GitHub Pages serves
  from a case-sensitive Linux FS; the dev box + `dotnet run` are case-insensitive, so a casing mismatch
  passes locally and 404s in production (Stage 8's black-screen `ManagedError: content/gfx/...`). The
  on-disk asset root is **`wwwroot/Content` (capital C)** with everything lowercase under it. **Every
  content request must use a capital `Content/` root, lowercase under it** — `WebContentManager.ResolvePath`
  and `AnimatedSprite.loadData` do this; the JS `eaMusic`/`music.json` always did. Don't reintroduce a
  lowercase `content/` request, and verify new assets/scenes ON THE LIVE URL, not just locally.
- **Shaders (Stage 5):** the lost `.fx` were rewritten in `tools/shaders/src/` and compiled
  offline to MGFX v10 GLSL `.mgfxo` by `tools/shaders/build_shaders.py` (KNI's MGCB, BlazorGL
  target — needs `nkast.Xna.Framework.Content.Pipeline.Builder.Windows 4.1.9001` restored in the
  nuget cache). `WebContentManager` loads them via `new Effect(gd,bytes)`. **Re-run the script
  after editing any `.fx`; don't hand-edit `.mgfxo`.** Effects apply via `SpriteBatch.Begin(effect)`
  (4.0 model), not `effect.Begin()`.
- **Alpha is STRAIGHT (non-premultiplied); `AlphaBlend` -> `BlendState.NonPremultiplied`.** The unpacked
  content is straight alpha (the original Xbox 3.1 build was — proven from the source `.xnb` and the
  decompiled explosion's explicit `Additive` swap), so `unpack.py:to_image` emits the decoded RGBA
  verbatim and `SpriteBatchWrapper.ToBlendState` maps `AlphaBlend` -> `BlendState.NonPremultiplied`
  (SrcAlpha/InvSrcAlpha). **DON'T use `BlendState.AlphaBlend`** — that's KNI's *premultiplied* variant
  (One/InvSrcAlpha), a same-name trap: pairing it with straight content makes alpha fades go
  additive-bright instead of dissolving (the "bomb/blast vanishes suddenly" bug — we tried it, it's
  reverted). Don't premultiply on export, don't premultiply tints; straight tints like
  `new Color(1,1,1,a)` are correct as written. Evidence + the full story: `plans/plan.md` Stage 3.
- **Audio (Stage 6):** the lost XACT runtime is replaced, not ported. `tools/audio/` cracks the
  big-endian Xbox banks in pure Python (`xact.py` parses `.xwb`/`.xsb`; PCM SFX + **xWMA music**
  decoded via **PyAV**) and `build_audio.py` writes `wwwroot/Content/{sfx,vo}/*.wav`,
  `music/*.ogg` + `music/music.json`. **Re-run `python tools/audio/build_audio.py` after changing
  the banks or the ElevenLabs renders; don't hand-edit the outputs.** SFX/speech play on KNI
  `SoundEffect`; **music** is a WebAudio layer (`index.html` `eaMusic`, via `Compat/MusicInterop.cs`)
  for seamless loop points. `SoundManager.Play()` now returns a `SoundEffectInstance` (not `Cue`).
- **XACT mix metadata is un-stubbed (faithful, no offline boost).** Stage 6 cracked the banks to
  WAV/OGG but dropped XACT's per-cue mix data; it's now recovered and re-applied. `xact.py` parses it
  (`parse_soundbank_meta` = per-cue category/volume/pitch; `parse_xgs` = category gains + RPC presets;
  `cue_mix` = the resolved `category x sound` table) — these document the numbers; they don't
  regenerate assets (no re-run needed unless the banks change). The **volume law is MonoGame's logistic
  fit** `vol_to_linear` (byte `0xB4`=180 -> 0 dB; the modal SFX byte 90 -> **-12 dB**), NOT the old
  `(byte-90)*0.25` estimate — `SoundManager.VolToLinear` mirrors it. Consequences baked into
  `SoundManager`: (1) per-cue volume comes from the authored byte (`_cfg` lists only the deviating
  cues; default = byte 90); every played cue is <= ~0.57 linear so **no WAV needs boosting** and KNI's
  `Volume<=1` cap is never hit. (2) **Category gains are all 0 dB (unity)** per the `.xgs` — no
  SFX/Speech/Music cross-bus trim (the old `SfxGain=0.75` is gone); baseline SFX (~0.25) sits ~level
  with the music layer (`eaMusic` master .55 x track .6 = .33). (3) **Instance limits are per-CATEGORY**,
  not per-cue: Default(SFX)=32 concurrent **FailToPlay** (`SfxMaxInstances`, `CountActive`), Speech
  unlimited, Music one-at-a-time. (4) Variation: the bank authored none; a **subtle 5% vol / ~0.35-semi
  humanize** is kept as a deliberate embellishment. (5) **RPC**: the one authored preset (var "Pitch"
  -> Pitch, 0..100 -> +/-1200 cents) is the BrainBoss/Level3 music-rate sweep; `MusicInterop.SetRate`
  now applies the faithful curve `2^((Pitch-50)/50)` and `eaMusic.setRate` just sets `playbackRate`.
  (6) Music uses the authored **2.5s crossfade** (`MUSIC_FADE` in `index.html`). **There is NO DSP/reverb
  in the bank** (0 presets) — that XACT feature was never authored, nothing to port.
- **Sign-in / keyboard:** `SignedInGamers` is still empty, but the XBLIG sign-in gate is gone —
  the PC keyboard path was recreated, incl. **reconstructing the `#if WINDOWS`-stripped
  keyboard-read block in `InputHandler.Update()`** (the Xbox build discarded `Keyboard.GetState()`
  and left the `keysToCheck` table dead). Keyboard: arrows/WASD move, Enter select/start, Esc back.
- **Game loop is JS-driven:** `wwwroot/index.html` (`initRenderJS`/`tickJS`) →
  `Pages/Index.razor.cs` `TickDotNet()` → now `new EvilAliens.Game1()`. `ContentTestGame.cs` /
  `SpikeGame.cs` are dead harnesses, safe to delete.
- **Menus are mouse-selectable + clickable (hover highlights, left-click selects+activates).**
  Every list menu derives from `MenuSub1` and shares a `selectedEntry` + `ItemSelectedEvents`
  model. The menus' layouts differ too much (centred lists, the framed main menu `MenuSubWithSkull`,
  the left-aligned `DifficultyMenu` column, the `SubMenuLevelChoice` carousel) to hit-test from one
  formula, so **each `DrawMenu` records the design-space (800x600) box of every entry it draws** via
  `MenuSub1.RecordEntryHit(index, centre, w, h)` (locked/undrawn entries are skipped, so they never
  become hittable). `MenuSub1.HandleMouse()` (in `HandleInput`, gated on the `normal` state so it
  never fights the entry/exit zoom) maps the cursor — `InputHandler.MousePosition`, already design
  space via `RenderScale.WindowToDesign` — onto an entry: hover sets `selectedEntry`, `MyKeys.Mouse1`
  (already wired to the left button in `InputHandler`) selects+invokes it, and either resets the
  attract-demo idle timeout. **A new `DrawMenu` override must call `RecordEntryHit` per entry or its
  menu won't be clickable.** The carousel sets `mouseHoverSelects = false` (gliding over a flying
  screenshot shouldn't snap the selection; a click picks the mission directly). Out of scope: the
  `GammaMenu`/`ScreenResizeMenu` sliders (not `MenuSub1`, no entry list) and `PlayerSettingsMenu`
  (gamepad-config, its own per-device selection model, empty `menuEntries`).
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
  tick (one per automation step) to register distinct taps. **Touch/mobile (Stage 9)** uses the same
  seam: `eaHold(key, down)` (JS) → `DebugInput.Hold`/`touchHeld[]` holds a key down until released
  (vs `eaPress`'s tick countdown), both drained by `DebugInput.Consume` in `InputHandler`. Driving
  fullscreen via automation fails (synthetic clicks carry no `navigator.userActivation`); that's a
  harness limit, not a bug — a real click works.
- **Touch + fullscreen UI (Stage 9)** lives in `index.html` **outside `#app`** (so it survives
  Blazor's mount of `App` into `#app`): a corner fullscreen button + a touch overlay (D-pad / FIRE /
  BACK, shown only on touch devices). Fullscreen is the DOM Fullscreen API via `Compat/FullscreenInterop.cs`
  → `window.eaFullscreen` (KNI's `graphics.IsFullScreen` is a no-op on BlazorGL); the in-menu
  "Fullscreen" option routes through it too. A new HUD/overlay button should follow the same
  outside-`#app` pattern.
- **Trailers (Stage 14)** are an embedded **YouTube** overlay, NOT ported video. The original
  `Content/VFX/*.wmv` (VC-1) won't play in a browser and there's no video loader, so the old
  `TrailerScene`'s `Content.Load<Video>("VFX/..")` crashed the loop — it's now DEAD (constructed but
  never added; don't re-wire it / don't reintroduce any `VFX/*` `Content.Load`). The Options ->
  "Trailers" submenu's two handlers call `Compat/TrailerInterop.Play(youtubeId)` -> `window.eaTrailer(id)`
  in `index.html` (sibling of `eaFullscreen`/`eaMusic`, built **outside `#app`**), which overlays a
  `youtube-nocookie.com/embed?autoplay=1&rel=0` iframe + a Back button, pauses menu music
  (`eaMusic.pause()`/`resume()` = AudioContext suspend/resume, seamless) and on close (Back/Esc/backdrop,
  all JS-owned) resumes music + refocuses the canvas. Ids map `TrailerScene.TrailerMode` 1:1
  (EvilAliens=`v732YJ4wHjc`, RocketRiot=`4zN0h1xmwF8`); change them in `MenuScene.trailerMenu_*Selected`.
- **No longer stubbed:** audio (Stage 6), saves persist (Stage 7), and the **controls-help screen now
  shows the keyboard layout** (Stage 9 — un-skipped `Displays.Keyboard` in `InstructionsMenu` +
  `HelpText`; its homes are the attract demos and the in-game pause → "Instructions", there's no
  standalone controls menu entry).
- **Custom font (Stage 12) — the atlas is SUPERSAMPLED; never route `menufont` through stock
  `SpriteBatch.DrawString`.** `GFX/Menu/menufont` (the ONE font every text call site uses) is rebuilt
  by `tools/font/build_revenge_font.py` from `tools/font/sources/*.png` with a **3× atlas**
  (`BoundsInTexture` is 3×) while `Cropping`/kerning/`LineSpacing`/`Spacing` stay **design-size** (so
  `font.MeasureString(...)`, called raw in ~40 layout sites, is unchanged). The wrapper's
  **`SpriteBatchWrapper.DrawStringScaled`** draws each glyph at `Cropping.Size / BoundsInTexture.Size`
  (=1/3 for redrawn glyphs, =1 for merged originals) — all four `DrawString` overloads go through it.
  Reverting to `spriteBatch.DrawString(font,…)` would render glyphs 3× too big. Re-run the builder
  after editing a sheet (don't hand-edit `menufont.fnt`/`.fnt.png`); revert via the `*.orig` backups.
  Per-glyph capture-box / vertical-align / bearing tweaks live in **`tools/font/overrides.json`**,
  authored with the live editor (`tools/font/editor/serve.py`, after `--emit-editor`) and baked in on
  `--commit`; `tools/font/_diag.py` prints per-glyph baseline offsets.
- **In-game score / "Player X — Press Start" text = ONE flattened sprite, chrome by default
  (`SpriteBatchWrapper.DrawShadowString`).** `ScoreVisualiser.DrawStr` no longer draws the
  drop shadow and the text as two separate translucent `DrawString`s (the old "shadow bleeds
  THROUGH the text" bug — both were at the same partial alpha, so the 2px-offset shadow showed
  through the glyph strokes). It now calls `DrawShadowString`, which rasterises shadow-then-text
  at FULL opacity into the shared grow-only text RT (`metalRT`, via the extracted `EnsureTextRT`,
  same plumbing as Stage-13 `DrawMetalString`) and composites the whole element ONCE at the
  target alpha — so shadow+text fade as a single sprite, no bleed-through. The chrome sheen
  (`metal.fx`) is ON by default (`DebugFlags.MetalScore`, default true; **`?metalscore=0`** A/Bs
  the plain flatten); the metal path uses a touch more opacity (0.7 vs the plain 0.55) since the
  sheen darkens the mid-band. Don't revert `DrawStr` to two `DrawString`s — that brings the bug
  back and (with the supersampled atlas) needs `DrawStringScaled`, not stock `DrawString`.
- **Texture loads: PNG decode is the stutter; precompile hot sprites to DXT/raw (an offline asset
  build step).** `Texture2D.FromStream` decodes PNGs via **StbImageSharp — managed, on the WASM main
  thread, interpreted (no AOT)** — so a cold multi-megapixel sheet is a multi-hundred-ms to multi-second
  frame hitch (measured: `spider_sheet2` 5033 ms; a whole Level2 preload ~28 s). Two tools attack this:
  (1) **`Compat/LoadProfiler.cs`** (debug flag **`?loadlog`**) times every decode, flags ones that load
  *outside* a level's preload phase (the stutters), accumulates a per-level set in localStorage that the
  preloader feeds back (`GameScene.LoadContent` → `BeginPreload`/`ApplyManifest`/`EndPreload`), and exports
  a committable list via **`eaPreloadExport()`** in the console → `wwwroot/Content/preload/manifest.txt`
  (read by ALL builds at preload; release never writes). (2) **`tools/textures/build_textures.py`** reads
  **`tools/textures/textures.config`** and precompiles listed sprites to a GPU-ready sibling:
  **`.dds`** (BC3/DXT5, lossy, ~2.4× the PNG on disk, ~0 decode — needs `texconv.exe`, gitignored; dims
  auto-cropped to a mult-of-4 that preserves the `floor(W/cols)` cell pitch, since Chrome/ANGLE→D3D11
  rejects non-mult-of-4 block textures as black) or **`.rtex`** (uncompressed straight-alpha RGBA8,
  lossless, large, ~0 decode, any dims). `WebContentManager.LoadTexture` prefers **`.dds` → `.rtex` →
  `.png`**. Re-run the script after editing a source PNG or the config; don't hand-edit the `.dds`/`.rtex`.
  It's OFFLINE (texconv is Windows-only); CI just ships the committed outputs (like `tools/shaders`,
  `tools/audio`). Per-sprite dxt-vs-raw choices are pending the art rescale (Trello: "Revisit per-sprite
  texture format").
- **Animated Braineroid sprite (the lvl-1 brain enemy) — `tools/textures/build_brain_sheet.py`.** The
  `Braineroid` (huge/medium/small, `Game/EvilAliens/Braineroid.cs`) uses an animated cyborg-brain sheet
  built from an AnimGen export (81 magenta-backdrop frames). The builder chroma-keys the magenta to
  STRAIGHT alpha (reuses `tools/chroma_key_title.py`'s decontaminate+edge-bleed, plus a connected-component
  pass that keeps only the brain blob so noisy-backdrop corner speckles are dropped), fixed-crops to the
  union bbox, decimates to **20 frames**, packs a **5×4** grid of **512px cells** (near-native res, so the
  OG-size draw isn't upscaled) → `wwwroot/Content/gfx/sprites/brainanimated.png`, and builds a **blue glow**
  (blurred silhouette, padded so the falloff isn't clipped) → `brainanimatedglow.png`. The brain sheet is
  **`dxt`** in `textures.config` (→ `.dds`, ~4.6 MB vs ~18 MB raw; the brain's high-frequency detail hides BC3
  artifacts, like `spider_sheet2`); the **glow stays `raw`** (a smooth gradient DXT would band). Re-run the
  script then `build_textures.py` after a new export; don't hand-edit the outputs. The sheet is **drawn
  through the interpolation shader** (`Braineroid` sets `interpolationOptions = always`), so the low frame
  count + rate (`fps 0.4f` → ~50s loop) still plays smooth — the `interpolate.fx` path cross-fades frame
  N→N+1 (which is why 20 frames suffice). The glow is drawn additively *behind* the brain in `Braineroid.Draw`
  (BrainBoss-aura recipe, blue, subtle sine pulse) and sits under the bonus-colorize so a powerup Braineroid
  hue-shifts brain+glow together. `brainanimated` is registered in
  `AlienDrawableGameComponent.DesignFrameWidth` at **100** (the design width fixes on-screen size = 100×scale
  regardless of cell px, so bumping cell resolution only adds crispness); the Braineroid draws at scale
  **2/1/0.35** (huge/med/small) to match the original `brainlargetransglow` on-screen size. GOTCHA: the sheet
  is multi-frame, so `texture.Width` is the WHOLE frame row — `Braineroid`'s off-screen wrap margin must use
  `texture.Width/columns * DrawScale` (one frame), not `texture.Width * scale`, or brains drift far off-screen
  and the Braineroids minigame never clears a wave. Each instance also randomizes its start frame + pulse
  phases in `Initialize` so a cluster isn't lock-step. Preloads for every Braineroid scene are in
  `preload/manifest.txt`. NOTE:
  `Braineroid.Initialize` sets `pulsate = 1f` (not 0) — Update overwrites it in-game, but the sprite harness
  freezes Update, so a 0 baseline would draw the whole sprite at scale 0 (invisible).
- **Earth fly-by sprite (Level 1 hero earth) -- `tools/earth/build_earth.py`.** `GFX/Sprites/earth` is
  the masked NASA Blue Marble globe (`sources/globe_west_2048.jpg`, ~1822px disk). It's emitted at the
  FULL source resolution (NO downscale) so the fly-by renders crisp (1 texel ~= 1 pixel on a typical
  window) instead of the old ~1.3-1.9x bilinear upscale -- and because the hero earth is wider than the
  screen and stays HORIZONTALLY CENTRED, only a central VERTICAL STRIP ever shows, so the output is
  cropped to that strip (~1392x1822) and the never-seen sides aren't stored. `doodadscale` is **0.6467**
  (= 1168/solid-disk) so the on-screen size is unchanged; the script PRINTS the value to use if you
  change framing. **INVARIANT: the strip is only valid while the earth can't drift sideways into the
  cropped edge** -- `Background.QueueEarth` AND `QueueEarthSim` set `doodadscrollspeed.X = 0` (vertical
  descent only); don't re-enable X drift on the hero earth or the cut sides show. Level 1 also holds the
  sideways asteroid-belt phase until the earth leaves: **`WaitForDoodadEvent`** (polls
  `Background.DoodadActive`, race-free) gates `spawner_OnFinished`; Demo 1's earth is covered by the same
  X-lock. It's a PNG decoded at level preload (not in `textures.config`); `earth_small` is unchanged.
  Re-run `build_earth.py` after changing the source/knobs; don't hand-edit `earth.png`.
- **Menu art is warmed at boot to kill the level->menu pop-in.** `Game1.WarmMenuContent()` (end of
  `LoadContent`, behind the loading screen) decodes the menu's heavy PNGs (`planet`, `title-revenged`,
  + the rest) ONCE so the first menu show -- and especially the cold end-of-level credits->menu handoff
  (which never displayed the menu before) -- appears in a single frame instead of revealing in ~0.5s
  stages as each uncached MB-scale PNG decodes mid-transition on the WASM main thread. The menu scenes
  (`MenuScene`/`MenuSub1`/`MenuSubWithSkull`) all load through ONE shared content manager (`Scene.Content`
  == `IContentManagerService.ContentManager` == `Game1.content`), so warming that one instance populates
  the exact cache keys their `Load()` calls hit (same idea as a level's `PreloadGraphicalContent`).
  (`CreditsScene` uses its OWN content manager, so its bg isn't warmed -- but the crawl fades its bg in,
  so a cold decode there isn't the jarring part.) Pairs with skipping the brag interstitial: on web `BragScene` is
  always immediately `Done` (no signed-in gamer), so `Game1.creditsScene_OnFinished` checks
  `BragScene.WouldShow()` and routes credits -> menu directly instead of flashing one bare starfield frame.
- **Resolution = a unified presenter (Stage 10), not a pinned back buffer.** KNI's BlazorGL forces the back buffer to
  the browser window size and rewrites `PreferredBackBuffer` on every resize, so a fixed 800×600
  reverts. `Game1.Draw` renders the WHOLE frame into one offscreen `sceneTarget` sized to the window's 4:3 letterbox (`Compat/RenderScale`, capped 1440px tall) and blits it
  scaled+letterboxed to the window; the game's `SetRenderTarget(0, null)` calls are redirected to
  that target via `Xna3GraphicsDeviceCompat.BaseRenderTarget`. Don't re-introduce a pinned
  `PreferredBackBuffer`. Stage 5 applies the gamma shader on the present blit of this target, and
  the game's 800×600-design draws scale up to fill it via `RenderScale.Matrix` (applied at the `SpriteBatchWrapper` Begin choke), and bloom + the menu/background offscreen targets are all sized to `RenderScale` and recreated on resize. Stage 9 adds fullscreen; **Stage 10** drew the hi-res art (menu title, splash channel-flip) straight into this one scene — the separate `HiResOverlay` pass is GONE — so it shares the same bloom/gamma. A render-sized offscreen target composited back uses `SpriteBatchWrapper.DrawPresent` (identity, not a scaled draw); full-screen overlays use `(0,0,800,600)` design coords, never the viewport.
- **"Boss key" decoy + Games launcher = the SEPARATE `meridian` repo (main-menu Exit).** Exit doesn't
  quit (can't, in a tab) -- it hands off to **Meridian Workspace**, a fake corporate desktop OS that is
  now its OWN private repo/site (`github.com/Coamithra/meridian`), NO LONGER in this tree (it used to
  live in `wwwroot/office/`; extracted with history via `git subtree split`). It's a dependency-free
  stack (plain HTML/CSS/JS, no build) that can never touch the Blazor/KNI game. Flow:
  `MenuScene.mainMenu_ExitSelected` -> `Compat/ExitInterop.Quit()` -> `window.eaQuit` (in `index.html`)
  fades the canvas to black and navigates to the Meridian base (`MERIDIAN_BASE` in `index.html`) at
  `index.html?from=evilaliens`. The `?from=<id>` tells Meridian where "Shut Down" should return.
  Hub-and-spoke: every game deploys as a SIBLING of meridian on one origin, so when co-hosted the
  default relative `"../meridian/"` scheme works unchanged; for a cross-origin split set `MERIDIAN_BASE`
  (game side) or `CONFIG.GAME_ORIGIN` (meridian side) to an absolute base. **LIVE TOPOLOGY (2026-06):
  cross-origin** -- the game is on GitHub Pages (`coamithra.github.io/RotEA26/`) while Meridian is
  deployed on the Hetzner host (web root `/public_html`) at `https://haraldmaassen.com/meridian/`. So
  `MERIDIAN_BASE` is set to that absolute URL (game side) and `office.js` `CONFIG.GAME_ORIGIN` is set to
  `"https://coamithra.github.io/"` (meridian side). Flip both back to the relative default once the
  games move onto Meridian's origin. Meridian's `games.json` is the games registry -- add a game with no
  code edit (drop cover art, add one entry; see that repo's README + the `office.js` CONFIG block).
  **To edit the decoy/launcher itself, work in the meridian repo** -- this repo now keeps only the tiny
  `eaQuit` handoff. **Deploy Meridian** with `meridian/tools/deploy.py` (SFTP creds from the game repo's
  `.env`; `--base /public_html/meridian` -- prefix `MSYS_NO_PATHCONV=1` so Git Bash doesn't mangle the
  leading-slash arg; `--list`/`--dry-run`/`--rm` for inspect/preview/cleanup).
  The meridian repo stays PRIVATE on GitHub (source hidden); the deployed Hetzner site is public so the
  easter egg stays reachable.
- **Multi-game hub -- how the projects fit together (the setup this separation was built for).** The
  goal is many small games sharing ONE launcher/decoy without piling into one repo. Architecture is
  hub-and-spoke: **Meridian** = the shared launcher + "boss key" decoy (its own private repo, deployed
  at `haraldmaassen.com/meridian/`); **each game** = a standalone spoke in its OWN repo, deployed at its
  own sibling URL (RotEA26 on GitHub Pages, future games wherever). Games never import or depend on each
  other or on Meridian's internals -- the ONLY coupling is a URL contract. **To add a new game:**
  (1) *Meridian side, data-only:* drop cover art in `meridian/covers/` + add one object to
  `meridian/games.json` (`id`, `title`, `genre`, `blurb`, `status`, then `path` for a sibling slug OR
  `url` for an absolute off-hub link, plus a `cover` block), then re-deploy Meridian
  (`tools/deploy.py`). No Meridian code change. (2) *Game side, one handoff:* on the game's Exit/quit,
  navigate to `<MERIDIAN_BASE>index.html?from=<that game's id>` (copy RotEA26's `wwwroot/index.html`
  `eaQuit`: fade to black, then `location.href`). `MERIDIAN_BASE` is relative `"../meridian/"` when the
  game is co-hosted beside Meridian, else an absolute base when cross-origin. (3) *Hosting:* same origin
  as Meridian => every link stays relative; cross-origin => set the two absolute knobs (`MERIDIAN_BASE`
  game side, `CONFIG.GAME_ORIGIN` meridian side). `?from=<id>` is what Meridian's "Shut Down" uses to
  return the player to the right game. Add as many games as you like without touching the existing ones.

## Don'ts
- Don't commit `bin/`/`obj/` or the raw 52 MB Xbox package (all `.gitignore`d).
- Don't re-run `tools/*.py` against `Game/` (regenerates it from scratch).
- Don't trust a screenshot for colours/blending while shaders are stubbed.
