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
- **Cards = the plan's stages (+ smaller follow-up tasks); the Trello board is the live tracker.**
  Done: Stages 1-10, 12-15 + the Stage 11 umbrella (design settled 2026-07-07 — see
  `plans/stage11-online-coop.md`; distributed-authority state replication over P2P WebRTC, NOT
  lockstep). In Progress: webcam difficulty-feel tuning. Backlog: the Stage 11 IMPLEMENTATION as
  five phase cards `Stage 11.1`-`11.5` (net skeleton/ship mirroring → world authority + generous
  claims → script replication/reset/tether → WebRTC + Hetzner signaling + lobby → hardening;
  strictly sequential except 11.4, which only needs 11.1's transport interface; per-card
  orchestration strategy is in each card's description) + the IndexedDB screenshot-storage
  migration. Each card's description summarises its task; the now-archived `plans/plan.md` holds
  the full per-stage detail. When a card's status changes, `card move <id> <listId>` it and keep
  the description in sync.
- Browse it visually with `trello --backend local --board 10989a3d serve` (drag-drop kanban web app).
- **When picking up a card/task, FOLLOW [`CONTRIBUTING.md`](CONTRIBUTING.md).** It's the step-by-step
  runbook for this repo — claim the card (Backlog → In Progress), tracker doc, a per-card worktree
  (mandatory; slot `wt1`..`wt8`, dev server on port `528<k>`),
  research → design → implement, the visual+console verification gate (no unit tests here), PR
  self-merge (deploy to Pages is MANUAL — `workflow_dispatch`, not on push), and the card-close paperwork (move to
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
- **Testing DYNAMIC behaviour (movement, attack patterns, timing, spawn cadence, physics, a
  tuning curve over time)? DO NOT verify it with the real game + Chrome screenshots.** Motion and
  over-time change are almost impossible to catch in a static frame — you can't time the capture to
  the right instant, and a boss's attack pattern, an easing curve, or a spawn rhythm looks identical
  across frames. Screenshots prove *drawing*, never *behaviour*. Instead, one of:
  1. **Simulate it in isolation and inspect the DATA, not a picture (preferred).** Stub the game
     (no Blazor/WASM boot needed), construct just the object, and tick its `Update` in a plain loop
     with a fixed reasonable `deltaTime`, recording the quantity of interest (position, velocity,
     hp, timer, fire events) each step. Then look at the numbers directly, or plot them
     (position-over-time / value-over-time graph) and read the SHAPE off the graph. This is exactly
     how the existing `HarnessApplyPhase`/`ApplyLifecycle`-style pure sims already work (see
     `Blast.ApplyLifecycle`, `Spider.HarnessApplyPhase`) — reuse/extract the deterministic core so
     the test drives the SAME math live play does. A throwaway console/xunit harness or a Python
     re-implementation of the curve is fine; the goal is a signal you can *read*, not a frame you
     have to *time*.
  2. **If you genuinely must see it in the real game, build a BREAK/PAUSE** so the game freezes at
     the exact moment worth capturing (e.g. a debug flag that halts `Update` on a condition — first
     shot fired, apex of the arc, Nth spawn — using the existing `DebugFlags` seam), THEN screenshot
     the frozen frame. Never chase a moving target hoping the shutter lands right.
  Reach for (1) FIRST for any change to how something MOVES or CHANGES OVER TIME; screenshots are
  for drawing/appearance only.
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
  **Level FAST-BOOTS replace a level's whole event list** — each skips its waves and drops straight
  into one fight/section, so a change there can be watched in seconds instead of minutes of play:
  `?spiderboss` (Level2's spider boss — `Level2.PopulateSpiderBossOnly`) · `?spiders` (Level2, a
  continuous pure-spider ground wave) · `?wallsonly` (Level3's walls sections, looped —
  `Level3.PopulateWallsOnly`) · **`?brainboss`** (Level3 straight into the REAL BrainBoss finale —
  `Level3.PopulateBrainBossOnly`; **spawns it UNCONDITIONALLY, bypassing the Hard+ gate in
  `BrainBossHard()`**, so the brain's animated overlays + `hit_boss` SFX can be verified on any
  difficulty). Pair with `?invuln`. All are `false` by default, and all are IN `DebugFlags.Active`
  (unlike the render/feel toggles, which stay out) so they print in the `[debug] flags active` line —
  they hijack a level, and `?brainboss` alone reaching Level 3 *from the menu* would otherwise do so
  silently. e.g. `…:5280/?level=Level3&brainboss&invuln`.
- **Sprite harness — USE THIS to debug an object's drawing code instead of booting the game
  and trying to screenshot a moving enemy at the right instant.** `?harness=<Obj>` boots
  straight onto a space background showing ONE game object, drawn by its OWN `Draw()` through
  the real pipeline (same `SpriteBatchWrapper` / `RenderScale` / blend mapping / bloom / gamma),
  FROZEN — so a screenshot at any moment is pixel-identical (no timing to catch). It's 1:1
  because it reuses each type's real `NewXxx`+`Setup` and the real draw path; it only freezes
  time (object `Enabled=false` so gameplay `Update` never runs; the harness sets
  `Position`/`curframe`/`scale`/`rotation`). Companion flags: `?frame=<n>` (freeze frame, default 0)
  · `?play` (animate in place instead) · `?bg=space|spaceclassic|holodeck|mars|base|basedark`
  · `?pos=<x,y>` (design space, default 400,300) · `?objscale=<f>` (alias `?size`) · `?rot=<deg>`
  · `?fps=<n>` (alias `?animfps`; override the played animation's fps, only with `?play` — turn it
  real low so the frame-interpolation shader carries the motion between frames). e.g.
  `…:5280/?harness=Spider&frame=2` · `…/?harness=DeathStar&play` · `…/?harness=ufo&bg=mars`.
  **Eye boss (`JunkBoss`) interpolation:** the frozen harness can't reach the boss's `attracting`
  state (its `UpdateEyeAnim` state machine never runs), so `?harness=junkboss` shows only the IDLE
  eye sheet. `?harness=eyeattract` forces the spin+lightning ATTRACT sheet (via `JunkBoss.HarnessForceAttract`,
  set only by that registry factory; hitbox `r` stays idle-based so the lightning halo doesn't inflate it).
  The eye interpolates by default (`interpolationOptions = as_specified` + `Settings.Interpolate = true` →
  `interpolate.fx`), so `?harness=eyeattract&play&fps=2` proves it: at 2 fps the sparse 72-frame sheet is
  smoothly tweened by the shader rather than stepping.
  Code: **`Compat/HarnessScene.cs`** (the scene) + **`Compat/HarnessRegistry.cs`** (name→factory;
  add an object in ONE line — call its `New*`+`Setup`). Wired in `Game1` next to the `?level=`
  path. Human picker: **`wwwroot/harness.html`** (dropdown + fields → builds the URL; keep its
  list in sync with the registry). Caveat: objects whose Draw depends on state only their Update
  reaches (mid-attack bosses, the spider's airborne sheet) show their spawned/idle pose — bosses
  are best-effort; the common per-frame sprite-sheet enemies are exact. Any parked object with a
  CIRCULAR hitbox also gets its REAL collision ring drawn at the live radius (green; the blast has a
  richer lifetime viz) -- so a sprite-vs-hitbox size mismatch (the supersample bug class, e.g.
  Blast/PlasmaBall, a re/downscaled sheet whose hand-rolled radius forgot `DrawScale`) is
  visible by eye; `?objscale` up a tiny entry-scale sprite (e.g. a plasmaball) to inspect. Only
  CIRCULAR hitboxes show a ring; box-hitbox members of the class (e.g. Braineroid) show none. Verify like any game
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
  **deployed publicly at https://coamithra.github.io/RotEA26/**, rebuilt by a MANUAL `workflow_dispatch` deploy (no longer on every push to `main`).
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
- **Music loop points are pymusiclooper-refined, not raw whole-wave — `tools/audio/refine_loops.py`.**
  XACT looped the whole wave (`loopStart=0, loopEnd=duration`), but WebAudio's native loop does a HARD
  SPLICE at the boundary, so a whole-wave point whose end doesn't connect to its start CLICKS every loop
  (worst on `stage1`/lvl1 — measured ~617× the signal's normal sample-step in Chrome's decoded buffer;
  the sister project Fighterproto loops cleanly for exactly this reason — its points are pymusiclooper
  samples). `refine_loops.py` measures each track's actual splice click and, for the few that click
  audibly (only `stage1`/`stage2`/`classic` on the committed banks), replaces the points with a
  waveform-matched pymusiclooper loop written into `music.json` (others kept byte-identical — re-looping
  an already-clean track only discards music). It's **click-aware + idempotent** (a refined low-click
  loop falls below the threshold and is left alone on re-run) and **intro-preserving** (won't pull
  `loopStart` in front of `introEnd`, the once-only intro; `build_audio.py` now records `introEnd`).
  `build_audio.py` calls it as its last step; re-run `python tools/audio/refine_loops.py` standalone
  after a bank rebuild (needs `pymusiclooper`; absent → whole-wave points are left in place). Per-track
  hand-tunes go in its `OVERRIDES`; don't hand-edit the loop points. `--dry-run` previews.
- **The BESPOKE EXTERNAL music cues (not in the XACT banks) — `tools/audio/install_external.py`.** Three
  tracks: the two `classic` variants (below) and **`lastsignal`** (below that). Each is copied straight into
  `wwwroot/Content/music/<cue>.ogg` and gets its `music.json` loop from pymusiclooper. Re-run
  `python tools/audio/install_external.py` after swapping a source; don't hand-edit the `.ogg`s /
  `music.json`. `--dry-run` previews; `--cue <name>` installs just one; `--source <path>` overrides that
  cue's source. `build_audio.py`'s `main()` calls `install_external.install()` and `build_music` **merges**
  into the existing `music.json`, so a full rebuild never drops or clobbers an external entry (a missing
  source leaves that cue's committed track untouched — safe in CI / fresh clones).
  **Loop choice is CLICK-AWARE, not just top-ranked.** pymusiclooper ranks pairs by how alike the two
  points *sound*; neighbouring candidates are the same musical loop shifted by a fraction of a beat, so
  they score nearly identically while their raw waveform step at the wrap differs a lot — and WebAudio's
  loop is a HARD SPLICE, so only a low step is inaudible. `find_loop` therefore takes the best-scoring
  pair whose `splice_click` (the shared `refine_loops.py` metric) is already `<= SEAMLESS` (3.0), falling
  back to the least-clicky. This reproduces the committed `classic`/`classicclean` points exactly (their
  top-ranked pair was already seamless) and only changes which pair `lastsignal` gets. **Don't trust a
  single `splice_click` reading to compare NEAR-IDENTICAL candidates** — it's a one-sample step, so it
  swings wildly under a ±20-sample shift and Chrome's OGG decode disagrees with libsndfile's. It's a
  coarse "does this wrap tick" screen (orders of magnitude: `stage1` read ~617 before refinement), not a
  fine ranking. To genuinely compare two candidate loops, use a windowed measure (RMS of the audio
  *preceding* `loopEnd` against the audio *preceding* `loopStart`).
- **`lastsignal` (`Songs.LastSignal` → `songFiles[7]` → `Content/music/lastsignal.ogg`) is the end-of-level
  TEXT-CRAWL theme** — "The Last Signal", played by `CreditsScene` (the screen after each level win). It
  REPLACED the bank's old **`sjaakslow`** cue, which was the menu theme (`sjaak`) cut to its first ~27.5s
  and rate-shifted to ≈0.40× (≈ −15.8 semitones) offline back in 2008. Both `sjaakslow` the cue and the
  `.ogg` are GONE (dropped from `MUSIC_CUES`, `music.json`, and the enum); don't reintroduce them. Source:
  `new_assets_raw/lastsignalloopable.ogg` (gitignored raw), 44100 stereo, 169.41s = a once-only ~67.6s
  intro then a `67.60→168.67` body loop. `CreditsScene` plays it at rate 1.0 — nothing calls
  `SoundManager.SetMusicRate` on this path (only `Level3`/`BrainBoss` sweep the rate).
- **The `classic` tune is a BESPOKE EXTERNAL track in TWO variants, difficulty-gated.**
  The retro-minigame song was replaced with a user-authored "Evil Aliens Revenged" track, and now ships
  in two cuts: **`classic`** (`Songs.Classic` → `songFiles[5]` → `Content/music/classic.ogg`) is the full
  Japanese-vocal cut, and **`classicclean`** (`Songs.ClassicClean` → `songFiles[8]` →
  `Content/music/classicclean.ogg`) is a lyric-free loopable instrumental. **Which one plays is chosen by
  difficulty** via `SoundManager.ClassicForDifficulty()` — lyrics (`Classic`) only when
  `Settings.CurrentDifficulty >= Hard`, else clean (`ClassicClean`) — so the vocal cut is an *earned*
  reward (higher challenge difficulties are gated behind finishing the challenge). The
  difficulty-selected challenges (`AsteroidChase`/`ClassicAliens`/`BraineroidsLevel`/`CrazyGame`/**Webcam**)
  call the helper; the **Tutorial** forces `ClassicClean` (it `LockDifficulty(Very_Hard)`s for gameplay, so it can't
  key on difficulty); **`TeamChallenge`** now routes through the helper too (card
  `7329fcd4` gave it real difficulty — its `Initialize` calls `LockDifficulty()` on the menu-chosen level
  instead of the old hard-coded `LockDifficulty(Medium)`, so the lyric cut is earned on Hard+ like the
  other challenges, not always on). Both cues are bespoke external tracks (NOT in
  the XACT banks, so both removed from `build_audio.py`'s cracked `MUSIC_CUES`), installed by
  `install_external.py` (same pattern as `build_channelswap.py` owning the one port-era SFX cue). Sources:
  `new_assets_raw/EvilAliensRevengedLoopable.ogg` (lyrics) and
  `new_assets_raw/classicaliensremixloopable_nolyrics.ogg` (clean) — gitignored raw; the committed `.ogg`s
  are the shipped artifacts. The tool copies each source straight (already OGG/Vorbis 44100 stereo — a copy
  avoids a re-encode) and writes each `music.json` loop (lyrics: intro ~75s, body loop `75.06→414.18`;
  clean: intro ~55s, body loop `54.78→208.76`; `introEnd = loopStart`).
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
- **Splash "static channel swap" SFX (a port-era cue, not in the banks) — `tools/audio/pick_channelswap.py`.**
  The "I made this!" splash (`SplashScene` index 1) channel-flips the old meme into the revenged image
  (`channelflip.fx`); a bright TV-static burst now punctuates it. `SplashScene.Update` fires
  `SoundManager.PlayCue("channelswap")` ONCE the instant the glitch starts (`stateTimer >= holdMs`),
  gated on `variantPicked` so it only sounds when the flip actually renders (shader + reveal present),
  one-shot via `flipSoundPlayed` (reset in `BeginDisplay`). The cue is now a picked ElevenLabs
  sound-effect render (candidate `02_channel_static`, prompt "Television static noise when switching
  channels") decoded to `Content/sfx/channelswap.wav` (mono 16-bit PCM, 44100 Hz, peak-normalized 0.92
  so the cue-volume calibration still holds) by `pick_channelswap.py`, which keeps the chosen MP3 as a
  committed source-of-record (`channelswap_source.mp3`) and SUPERSEDES the old numpy synth
  `build_channelswap.py` as the cue's owner -- don't re-run that synth or it clobbers the render; to swap
  the sound, re-render candidates with `eleven_channelswap.py` then `pick_channelswap.py <slug>`. Its `SoundManager._cfg` entry is `volByte:100, vary:false`
  (a touch above baseline, no pitch/vol humanize). **Autoplay caveat:** the splash runs BEFORE any user
  gesture, so on a truly cold first load the AudioContext may be suspended and the burst is silently
  dropped (standard browser policy); it sounds once anything has unlocked audio (any prior click/key).
  Don't add a click-to-start gate to "fix" it — the project boots straight through by design.
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
- **Aiming cursor + fullscreen-Esc (cards 51276dcd / b0a2f525).** KNI's BlazorGL NEVER applies
  `Game.IsMouseVisible` to the DOM (its `_isMouseHidden` is dead, `Mouse.PlatformSetCursor` throws), so
  the OS arrow is always over the canvas unless C# owns `canvas.style.cursor`. The reticle
  (`MousePointer`) now does: `Compat/CursorInterop` → `window.eaCursor.set(mode)` in `index.html` picks
  `menu` (plain arrow — all menus + the intro is OFF there), `hidden` (cursor:none while the scale+rotate
  intro SPRITE plays at the START of a keyboard level), or `reticle` (the aiming reticle IS the OS cursor
  via `cursor:url(reticle/<px>.png)` — ZERO-LAG, no trailing sprite; `HWMouse=true` opts back to the plain
  arrow). Driven off `MousePointer.Visible` (GameScene sets it, incl. Tutorial; MenuScene forces it off).
  **The reticle SIZE-TRACKS THE WINDOW via a LADDER of cursor images.** A CSS cursor is a fixed pixel size
  and cannot be scaled, but the game letterbox-upscales 800x600 design space to the window — so ONE fixed
  cursor is correctly sized at exactly one window size and reads tiny on a big monitor. So
  `tools/cursor/build_cursor.py` emits `wwwroot/reticle/<px>.png` for `SIZES = range(24, 97, 8)` and
  `MousePointer.ChooseCursorPx()` picks the rung nearest `ReticleDesignPx (30) * windowPerDesign`, clamped
  to [24,96] (browsers ignore a cursor image past ~128px). `CursorInterop.SetReticle(px)` →
  `eaCursor.set('reticle', px)` builds `url(reticle/<px>.png) px/2 px/2, crosshair`. `Update` re-picks
  every tick while handed off, so a **window resize swaps the rung**. Tune by eye with
  **`?reticlesize=<designpx>`** (`DebugFlags.ReticleSize`; null => the baked 30, so a shipped build is
  unchanged; kept OUT of `Active`), then bake into `MousePointer.DefaultReticleDesignPx`.
  **Every image is DRAWN, never resampled** — the reticle is four axis-aligned bars, so each rung plus the
  intro sprite `Content/gfx/cursor2.png` (384px = 4x the largest rung, i.e. the intro's start size) is
  rasterised at its native resolution. (The old tool tried to upscale the original 26px `cursor2.png` with
  `Image.thumbnail`, which only ever SHRINKS — so the shipped `reticle.png` was 26px of art floating in a
  48px canvas and the OS cursor came out ~half the size the intro ended at. That was the "reticle looks
  small since we upscale the game" bug.) **Invariant: in every image the bars run edge to edge (alpha bbox
  == full canvas)** — `MousePointer.CssHandoffScale()` sizes the intro sprite as
  `ChooseCursorPx() / windowPerDesign / texture.Width`, so padding would shrink it below the cursor, and
  the hotspot is the image centre. Deriving the end scale from the SAME `ChooseCursorPx` is what makes the
  sprite→cursor handoff never pop at any window size, and it's texture-resolution-independent (re-author
  any image bigger with no code change; only the ladder's step/min/max need stay in sync with the tool's
  `SIZES`). Verify OFFLINE — an OS cursor never appears in a canvas screenshot: check each PNG's bbox, and
  simulate `ChooseCursorPx`/`CssHandoffScale` across window sizes (the intro's on-screen size must equal
  the chosen rung exactly). Fullscreen:
  the browser reserves Esc to exit and it can't be preventDefault'd, but the same Esc ALSO reaches KNI and
  stepped back a menu — so `index.html`'s `fullscreenchange`→exit calls `eaSuppressEsc` →
  `DebugInput.SuppressEsc`/`EscSuppressActive`, which masks the raw Esc in `InputHandler` for a short
  window (grace + held-guard); **F11** is a dedicated toggle (preventDefault native, route through
  `eaFullscreen`).
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
- **Webcam challenge "I Made This!" (`Levels.WebcamAliens`)** — the remake of the 2004 webcam game the
  splash meme is from; last entry in the Challenges carousel (no Unlockables gate — it just needs a
  webcam). It DOES go through the challenge difficulty menu like the others. The player's SEGMENTED camera
  image is the ship: **JS owns everything camera** (`wwwroot/webcam.js` = the Teams-style setup dialog
  with device picker + preview, getUserMedia, and the mirrored person OVERLAY canvas positioned over the
  4:3 letterbox, outside `#app` like the touch/trailer overlays), **C# owns everything gameplay**
  (`Compat/WebcamInterop.cs` + `Game/EvilAliens/WebcamLevel.cs`/`WebcamUfo.cs`/`WebcamPlasma.cs`). The
  collision surface is a 40x30 person-mask occupancy grid in design space, pushed ~30Hz from JS
  (`webcamMask`, ~200 B base64); the scene hit-tests saucers/plasma against it (`HitCircle`) and aims at
  its `Centroid`. **The mask is REFINED in the worker (Meet-style post-processing — don't strip it):**
  raw per-frame segmentation shimmers and has blobby edges, so `webcam-worker.js` runs (1) an adaptive
  temporal EMA over the confidence (delta-weighted: stable pixels smooth hard, moving pixels snap — no
  ghosting) and (2) a band-limited JOINT BILATERAL FILTER on the uncertain edge pixels guided by the
  camera frame's RGB (the refinement MediaPipe's own docs + Google Meet's published pipeline use), then
  builds BOTH the visual alpha and the occupancy grid from the refined confidence (hitbox stops
  flickering too). The overlay canvas backing store is sized to the letterbox's DEVICE pixels (capped
  `OVERLAY_MAX_W` 1280 ~= a 720p camera's 4:3 crop) instead of a fixed 800x600 CSS-stretch, with
  `imageSmoothingQuality:"high"` on the composite — the player image is drawn at native res. Knobs are
  consts at the top of `webcam-worker.js` (`EMA_MIN/MAX`, `JBF_*`). Vendored tasks-vision is 0.10.14;
  0.10.35 exists (GL memory-barrier fix for GPU masks; WebGPU inference still unshipped) — a mechanical
  vendor-bump follow-up. Rules: touch a saucer -> it asplodes; ignored saucers blink at an accelerating rate then
  fire ONE big slow plasma orb at you; hearts + kills-to-win are per-difficulty (see below). **Per-difficulty
  tuning (card `8fcc7a8e`):** `WebcamLevel.Tunings[]` is an Easy..Inzane table of the DISCRETE knobs —
  hearts, kills-to-win, max simultaneous saucers, saucer-speed × and plasma-speed × (the generic
  arm/blink/spawn cadence is now authored as ABSOLUTE per-tier milliseconds -- `SpawnIntervalMs` (gap
  between spawns), `ArmDelayMs` (the "rate of fire" -- wander time before a saucer starts charging; bigger
  = fires less often) and `ChargeTimeMs` (the blink-charge windup before the orb releases), each +/-15%
  jittered at spawn (`CadenceJitter`) for variety. The `Settings.DifficultyModifier` divisor was REMOVED
  from these calcs (and the old within-run "arm faster over time" ramp too), so each tier's feel is set
  directly -- plan is to author Easy + Very_Hard by feel then interpolate the middle tiers off the
  modifier. NB the saucers do NOT use a `RandomFromAverage(rate*dt)` fire model -- each fires exactly once
  per arm cycle, so `ArmDelayMs` IS the fire-rate lever). `Initialize` reads
  `Settings.CurrentDifficulty` (the menu pick), resolves the row, and picks the music via
  `SoundManager.ClassicForDifficulty()` (Hard+ = lyrics). `WebcamUfo.Setup`/`WebcamPlasma.Setup` take a
  speed-× arg. **Live-tune the feel with the `?wc*` debug flags** (`Compat/DebugFlags.cs`): boot
  `?level=WebcamAliens&wcdiff=<tier>` and A/B `?wchearts=/?wckills=/?wcsaucers=/?wcsaucerspeed=/?wcplasmaspeed=`
  (+ the cadence `?wcspawn=/?wcarm=/?wccharge=`, now ABSOLUTE ms e.g. `?wcarm=5000`),
  then bake the chosen numbers back into `Tunings[]`. **Better: `?wctune` shows a LIVE stepper panel**
  (`eaWcTune` in `index.html`, outside `#app`, only built when the webcam level calls `show()` -- a boot
  without the flag has no extra DOM): +/- 10 live knobs (hearts/kills/max-saucers/saucer-speed/plasma-speed
  + spawn-gap/arm-delay/charge-time + max-mines/mine-spawn) in real time, mid-play or paused, no reload;
  mine-life + mothership-ms are shown DISPLAY-ONLY in the bake row (URL-tunable) so a copied row is complete.
  Edits drive `DebugInput.SetWcTune` -> `DebugFlags.SetWebcamTuneOverride` (ABSOLUTE final values,
  unlike the URL speed flags which are tier-baseline multipliers) and `WebcamLevel` re-resolves on its
  next tick (`WebcamTuneVersion`): hearts snap to the new count, KillTarget/MaxSaucers are read live
  anyway, and speed changes rescale the saucers/orbs already on screen (`SetSpeedMultiplier` on
  `WebcamUfo`/`WebcamPlasma`). "Reset to tier defaults" clears the overrides via `debugClearWcTune`
  and the level re-seeds the panel with its actual resolved row; the panel's orange readout prints the
  bake-ready `Tunings[]` row (click to copy). e.g. `?menu&unlockall&wctune` then pick the challenge +
  any tier from the menu. **GOTCHA — MediaPipe MUST stay in the
  worker (`webcam-worker.js`):** its Emscripten loader assigns the global `Module`, which Blazor's Mono
  runtime also uses — importing tasks-vision on the main thread kills the whole .NET runtime ("_malloc is
  not a function", reproduced). The ~10 MB runtime+model under `wwwroot/lib/mediapipe/` (see its README)
  is lazy-loaded only when the level starts, so the boot payload is unchanged; if it can't load the level
  falls back to a fixed-oval "simple mode". A `Levels` member must only ever be APPENDED (XmlSerializer
  keys on enum names) — `Achievements.checkData` now BACKFILLS missing level keys instead of wiping
  progress on the first post-update load. Derived art (`heart.png`, `Screenshots/webcamss` — cropped from
  the meme splash) is built by `tools/webcam/build_webcam_assets.py`; don't hand-edit. Headless QA: fake
  a player from the console via `DotNet.invokeMethod('EvilAliensWeb','webcamMask', b64Grid, coverage)`.
- **Level-select screenshots now cover ALL challenges, not just the 3 story levels — plus an opt-in
  webcam capture (`General.ScreenshotEnabled` + `Settings.WebcamScreenshot`).** The XBLIG only
  captured live thumbnails for `Level1/2/3` (`General.ScreenshotEnabled`); the web port extends it to
  every carousel challenge (`SpaceDodge`/`Braineroids`/`ClassicAliens`/`Paratrooper`/`OwnLevel`/
  `CrazyGame`/`InsaneBossI`/`TeamChallenge`). The capture→save→display path is unchanged
  (`GameScene.checkScreenShot` → `takeScreenShot` `ResolveBackBuffer` → `ScreenshotSaver.SaveScreenShot`
  writes `<Level>.dat` → localStorage; `SubMenuLevelChoice` shows it, else the bundled static art). The
  `.dat` is written on level EXIT (`Terminate`), not mid-play, and each is ~270 KB raw / ~360 KB base64
  in **localStorage (~5 MB cap)** — the `StorageStub.Sync` persists screenshots LAST so critical saves
  survive a quota blowout; IndexedDB migration is the follow-up if it bites. **WebcamAliens is opt-in**
  (privacy — the shot contains the player's camera image): `ScreenshotEnabled(WebcamAliens)` returns
  the new **`Settings.WebcamScreenshot`** bool (default false; a new Options-menu toggle "Webcam
  Screenshots", saved on options-exit like the others). The webcam shot **composites the player overlay
  back in**: the person is a JS canvas layered ABOVE the WebGL canvas, so `ResolveBackBuffer` can't see
  it — instead `WebcamLevel.OnScreenshotResolved` (a new `GameScene` hook fired at the snapshot instant,
  before the JS overlay is torn down) calls `ScreenshotSaver.CaptureWebcamOverlay`, which pulls the
  overlay's straight-alpha RGBA from JS (`WebcamInterop.GetOverlayPixels` → `eaWebcam.overlayPixels(w,h)`
  in `webcam.js`) into a `pendingOverlay` texture that `SaveScreenShot` draws over the game frame
  (NonPremultiplied; aliens already render behind the player). The sparse webcam level never hits the
  generic >30-entity capture trigger, so `WebcamLevel` calls the new `GameScene.ForceSnapshot()` on the
  first kill (guarded single-shot). All 9 challenges verified capturing; the webcam *composite* itself
  needs a real camera to verify end-to-end.
- **Webcam saucer FEEL pass (`WebcamUfo`/`WebcamLevel`/`WebcamInterop`).** A round of gameplay tuning on
  the "I Made This!" mode (behavioural, separate from the `Tunings[]` value dialing). (1) **Firing is
  plant-and-shoot**: a saucer decelerates to a COMPLETE on-screen stop (`HaltMs`), blink-charges in
  place, fires, then ACCELERATES away from rest (was: drift-while-blinking then snap to full speed). (2)
  **Saucers are PERSISTENT**: a fired saucer retreats only ~`RetreatMargin`(50px) past an edge, then
  `ReturnToField()` snap-turns it back in (the turn is off-screen so the cheat is invisible) to
  re-arm/fire again; the ONLY despawn is a player swat (`Asplode`) -- no more off-screen GC. Because they
  persist, the swarm fills to the tier's `MaxSaucers` and stays there (a new one spawns only after a
  swat). **On-screen hardening (keep-the-ufo-on-screen fixes):** three coupled guards keep a persistent
  saucer honest -- (a) wander containment is AUTHORITATIVE: `UpdateWander` applies the player-avoidance
  steer FIRST then the edge-bounce LAST (was the reverse, which let avoidance point a saucer out through
  an edge), plus an `OffScreen(0)` watchdog that steers a slipped saucer straight back to field centre --
  so a wanderer can't stall off-screen; (b) arming only STARTS when the saucer is >= `ArmInset`(60px)
  inside every edge (`OnScreenToArm`; if the arm timer expires while it's hugging an edge, arming DEFERS
  until containment reels it in) -- so a shot can never originate off-screen (was possible, the reported
  "fired from off-screen" bug); (c) after firing, the retreat holds off-screen for `ReturnDelayMs`(900)
  before `ReturnToField` loops it back (a beat of breathing room vs the old instant U-turn), live via
  **`?wcreturndelay=<ms>`** (`DebugFlags.WebcamReturnDelay`; 0 = old instant return). (3) **Fly-around AI**: while wandering they steer away from + orbit the player's mask via
  `WebcamInterop.AvoidanceVector` (image-driven, not just the `Centroid`), so a still player isn't
  drifted into but a lunge still swats them; strength is live-tunable with **`?wcavoid=<f>`**
  (`DebugFlags.WebcamAvoid`; 0 disables, null => baked `DefaultAvoidStrength`). (4) Plasma already aims
  at the mask **centre of mass** (`Centroid`), locked at fire. (5) Player-hit plays **`hit_boss`** (the
  Doom BrainBoss-hit cue) not `head_asplode`. (6) Hearts moved TOP-CENTRE + smaller (were overlapping the
  top-left score). "Explosions on top of the feed" was scoped out: the JS-overlay feed can't cheaply
  enter the C# scene (a ~5MB/frame canvas->WASM texture copy, or hacking KNI's private GL context).
- **Webcam hazards: screen-bisecting mothership (F1) + DeathStar mines (F2).** Two dodge hazards added to
  the "I Made This!" mode, both `Collides=false` (mask-hit-tested like the saucers, not via CollisionHandler).
  **F1 `WebcamMothership`** is a stripped cousin of `SpiderHelperMothership`: it SLIDES in horizontally from
  a screen edge (SpiderHelper-style), winds up a `LazerGenerator` spark swarm, then fires a big laser that
  BISECTS the screen, holds a beat, and slides out. It CANNOT be harmed (pure "get out the way!"); standing
  in the beam costs a life (`WebcamInterop.HitBeam` -- a mask-vs-thick-segment test -- -> `PlayerHit`,
  grace-gated). Two orientations (`WebcamLevel.PickBisectOrientation`, ~60/40 mix): **VerticalDown** slides
  in from a random side, parks HIGH over one of {dead-centre, left-third `x=133`, right-third `x=667`}
  (rolled 50/25/25 -- the ship telegraphs where the beam falls) ~half cut off at the top, fires straight
  DOWN, then passes out the far side; **HorizontalFrom{Left,Right}** slides in from that side to ~33% down
  (`BisectY`), parks with its centre off-screen so only ~40% shows, fires ACROSS (duck under), retreats.
  (The mothershipB art sits 16px left of its frame centre, so the DRAW is re-centred on Position via
  `SpriteArtOffset` -- the beam then lines up with the hull's VISUAL centre, not just its frame origin.) The beam is a `Quad` drawn
  DIRECTLY (like Lazer internally) so its length is a fixed tier-independent sweep-then-hold, not Lazer's
  difficulty-scaled growth. **The whole choreography is a PURE function of `elapsed` ms since spawn
  (`WebcamMothership.PoseAt` + phase thresholds)** -- so the MOVEMENT is verified as DATA by the isolation
  sim `tools/sim/webcam_mothership_sim.py` (which mirrors `PoseAt`; run it, read the trajectory + invariant
  checks), NOT screenshot-timed. **`?wcmothershipfreeze=<ms>`** halts a mothership at a chosen phase (e.g.
  ~3600 = beam mid-fire) so a frozen APPEARANCE can be captured without chasing the frame. Interval knob
  `MothershipMs` (per tier; `?wcmothership=<ms>`, 0 disables); `?wcmothershipdir=vertical|horizontal` forces
  the orientation for testing. **F2 `WebcamMine`** reuses the DeathStar sprite (`GFX/Sprites/deathstarsheet2`)
  and wanders EXACTLY like `WebcamUfo` (same containment + player-avoidance steering -- it flows AROUND a
  still player, does NOT home like the Level-3 `DeathStar`). TOUCHING it costs a life + bursts a BEEFY blue
  explosion (two stacked `Explosion.MakeBlue` bursts `3.5/2.5`+`2/1.3`, mirroring `StarMine.Asplode`) with
  the DeathStar's `targetacquired` "tweety" hone-in cue as a callback -- so you DODGE it (opposite of the
  swat-me saucers). The MOTHERSHIP BEAM also sweeps anything it crosses out of the field
  (`WebcamMothership.BeamHitsCircle`): MINES pop with a plain non-damaging explosion
  (`WebcamMine.DestroyByLaser`, a mercy), and SAUCERS are killed with FULL credit (the shared
  `KillSaucer` helper -- score + KillTarget progress, same as a body-swat, since it's a kill the player
  wanted anyway). Both are instant (no leeway -- leeway is only for hazards that HURT the player). Unlike the persistent saucers, a mine only lives `MineLifeMs` then flies off the
  nearest edge and despawns. **The saucer plasma orb, on reaching the player, POPS into an electric ZAP
  (`WebcamZap` -- a bloom + radiating jagged lightning streaks built from the laser's `lazermiddle`/
  `lazerglow` + midpoint-displaced bolts) INSTEAD of an explosion** (electricity doesn't explode); the life
  is still docked by `PlayerHit`. Its shape can be plotted in isolation (see `tools/sim/_zap_preview.png`).
  **Bad-collision LEEWAY (a gift to the player):** a hazard that HURTS you (plasma orb / mothership beam /
  mine) only lands its hit after the mask has STEADILY overlapped it for `HitLeewayMs` (~100ms;
  `?wchitleeway=<ms>`) -- a per-hazard `ContactMs`/`BeamContactMs` accumulator in `WebcamLevel`'s three
  BAD tests (`TestPlasmaHitsPlayer`/`TestMinesHitPlayer`/`TestBeamHitsPlayer`, all now taking `dt`) that
  accumulates real time while overlapping and RESETS the instant contact breaks. So a jittery mask or a
  split-second-late dodge (a brief graze, or on/off flicker) never costs a life; it's framerate-independent
  (accumulates ms, not frames). **Killing SAUCERS is deliberately NOT leewayed -- it stays instant**
  (`TestPlayerTouchesSaucers`, the one test with no `dt`), because the player WANTS to hit those. The
  leeway timing is verified as DATA, not screenshots (a tiny accumulate/reset/trigger sim). Per-tier knobs `MaxMines` (simultaneous cap) / `MineSpawnMs` (spawn gap) / `MineLifeMs`
  (lifetime), URL `?wcminemax=/?wcminespawn=/?wcminelife=` (absolute). **Tuning panel** now has 10 live
  steppers (added **Max mines** + **Mine spawn (ms)**); **Mine life** + **Mothership** ms are shown
  DISPLAY-ONLY in the panel's bake row (URL-tunable) so a copied `Tunings[]` row is complete. The panel
  interop (`debugSetWcTune`/`eaWcTune.show`/`SetWebcamTuneOverride`/`TuneShow`) grew accordingly; all default
  so a shipped build is unchanged. Both hazards spawn only while `PlayerVisible` (like the saucers). **VERIFY
  DYNAMIC BEHAVIOUR VIA THE SIM OR A FREEZE, NOT LIVE SCREENSHOTS** (per the "Testing DYNAMIC behaviour"
  rule in Build/run/verify).
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
- **In-game score / "Player X — Press Start" text = ONE flattened sprite, chrome-sheened by default (plain via ?metalscore=0)
  (`SpriteBatchWrapper.DrawShadowString`).** `ScoreVisualiser.DrawStr` no longer draws the
  drop shadow and the text as two separate translucent `DrawString`s (the old "shadow bleeds
  THROUGH the text" bug — both were at the same partial alpha, so the 2px-offset shadow showed
  through the glyph strokes). It now calls `DrawShadowString`, which rasterises shadow-then-text
  at FULL opacity into the shared grow-only text RT (`metalRT`, via the extracted `EnsureTextRT`,
  same plumbing as Stage-13 `DrawMetalString`) and composites the whole element ONCE at the
  target alpha — so shadow+text fade as a single sprite, no bleed-through. The chrome sheen
  (`metal.fx`) is ON by default (`DebugFlags.MetalScore`, default true -- card 16dad393 restored the
  Stage-13 chrome-on-score that card 37c4ccca had turned off; the user asked for the sheen back,
  including the score's event-driven glint sweep on a leading-digit rollover). **`?metalscore=0`**
  A/Bs the plain flatten; menus keep their chrome regardless -- they go through
  `DrawMetalString`/`Cached`, not gated by this flag. The metal path uses a touch more
  opacity (0.7 vs the plain 0.55) since the sheen darkens the mid-band. **The flatten RT is
  PREMULTIPLIED** (`PremultiplyOver` rasterise -> One/InvSrcAlpha composite, same card) -- the
  deliberate premult-INTERMEDIATE exception to the straight-alpha rule: stacking two straight-alpha
  layers with either stock blend hard-edges the text's AA over its own drop shadow (the "jaggy /
  no transparency" bug). Verify flattened-text changes with the frozen **`?textshot`** reference
  grid (`Compat/TextShowcaseScene.cs`; score/combo/pop rows, plain + chrome, live animation
  phases), not live-pop screenshots. Don't revert `DrawStr` to two `DrawString`s — that brings the bug
  back and (with the supersampled atlas) needs `DrawStringScaled`, not stock `DrawString`.
  The chrome **glint sweep is EVENT-DRIVEN on the score, not on a timer.** The static chrome
  gradient (GradTop/Mid/Bot) is time-independent and always shows; only the moving white-hot
  glint streak is gated. It used to ride the shared continuous `MetalTime` clock (the menu
  marquee's ~9s `SweepPeriod`), so the score glinted every ~9s regardless of play — read as
  "random". Now each player's score NUMBER sweeps ONCE when its leading (most-significant) digit
  rolls over (9->10, 1900->2000, …) and rests otherwise; the combo readout and the inactive-slot
  "Press Start"/"Player N" prompts keep the static chrome with NO sweep (`ParkedGlint`) — they
  have no "first digit" to roll over. `ScoreInfo.UpdateGlint` arms a one-shot clock
  on a leading-char change (skipping reset-to-"0" and `Load()` checkpoint restores), and
  `GlintTime(player)` feeds either that live sweep time or a parked value (`MetalSweepPeriod*0.5`,
  mid-rest → glint off) into `DrawShadowString(…, glintTime)`. The sweep window length is
  `SpriteBatchWrapper.MetalSweepDuration` (= `MetalSweepPeriod*MetalSweepActive` ≈ 1.08s); those
  two consts are public so the score and the shader params stay in lockstep. Menus keep the old
  periodic marquee sweep (the no-`glintTime` `DrawShadowString`/`DrawMetalString` overloads still
  use `MetalTime`) — only the score is event-driven.
  **Menu chrome rows are CACHED (perf card febc71de) — don't revert them to per-frame `DrawMetalString`.**
  The menu list rows (`MenuSub1.DrawMenu` + `MenuSubWithSkull.DrawRows`) are drawn every frame on an idle
  screen, and each row used to do a full metal-string RT ping-pong (target capture/restore + Clear +
  rasterise batch) per frame. They now call **`SpriteBatchWrapper.DrawMetalStringCached`** instead: the
  plain-text raster (Pass 1) is time-INDEPENDENT (the sheen, incl. the moving glint, is a Pass-2 composite
  input), so it's cached — content-addressed on `(text, tint)` in `metalSpriteCache`, built once per
  label+colour and reused every frame, while only the metal.fx composite runs per frame (glint still
  sweeps). Same idea as `DrawShadowStringCached` but keyed by content (menu text is fixed) rather than an
  int slot (the score's text changes). `DrawMetalString` still exists (uncached, shared `metalRT`) for
  dynamic call sites; it and the cached variant share the extracted `RasteriseMetalText`/`CompositeMetalText`
  helpers so their output can't diverge. `MenuSubWithSkull`'s octagon **frame FILL** is likewise cached: the
  ~one-strip-per-row loop is replaced by a white octagon alpha **mask texture** (`EnsureFillMask`, rebuilt
  only on a frame-size change) drawn as ONE tinted quad per row (`white*fill` = the fill colour, straight
  alpha, so both selection states reuse the one mask; chamfer-edge softening hides under the crisp outline).
  The floating **"Power Up!" / combo pops** (`FloatingText.ShowType.pop`, shown for powerup
  level-ups and every 10th combo) had the SAME bleed-through (two translucent `DrawString`s, a
  dark drop + bright text at one alpha) and now route through the same `DrawShadowString`
  (flattened, `metal:false` so the plain pop look is unchanged); the `scrollup` floating-score
  type is a single `DrawString` and was never affected.
- **Bomb blast (`Blast.cs`) — fade is a SMOOTHSTEP and the hitbox uses `DrawScale`, so "dangerous"
  matches "clearly visible" in both time and area.** Two bugs made the bomb "active longer/bigger
  than the sprite suggests": (1) SPATIAL — `blast.png` is a 1.5x supersampled sheet, but
  `CollisionType` sized the radius off raw `texture.Width * scale`, so the hitbox grew from the
  intended 0.8x-of-visible to 1.2x (damage reached outside the disc); it now uses `DrawScale`
  (supersample divided out), restoring 0.8x at any sheet resolution. (2) TEMPORAL — alpha was
  `1 - p^0.3`, which dimmed the disc to ~half within the first ~10% of life while collision stayed
  active to ~50%; the fade is now `MathHelper.SmoothStep(1,0,p)` so the blast holds visible through
  its active window then eases out, and collision is tied to that fade (`Collides = fade >=
  ActiveAlpha`, default 0.5 — same ~half-life active duration as before, but ending while the blast
  is still clearly visible). The growth curve (scale) is unchanged. The lifecycle math lives in
  `ApplyLifecycle(p)`, shared by the live `Update` and the harness scrubber `HarnessApplyPhase`.
  Tunables are constants (`DefaultActiveAlpha` 0.5 / `DefaultHitRadiusFactor` 0.8), overridable from
  the URL (`?blastactive=` / `?blasthit=`) so the feel can be tweaked live; null override => the
  baked consts ship unchanged. **Visualise/tune with `?harness=blast`**: the sprite harness LOOPS
  the blast through its lifetime (its own `Update` stays frozen — the harness drives the phase) and
  overlays the REAL collision ring (green = dealing damage, red = inert) + a live readout
  (phase/alpha/scale/hit-radius + the param values). `?blastloop=<sec>` sets the sweep speed,
  `?objscale=` shrinks a big bomb to fit. Registry default is power 1 (the curve is power-independent).
- **Flying-spider size (`FlyingSpider.DefaultSizeFactor` + `?flyspiderscale=`).** The port reuses the
  reared-up HD sheet (`spider_sheet2` frames 22..30) for the Level 2 flying spider instead of the OG
  1x4 crawl sheet, so it draws taller + a touch wider than the XBLIG (measured on-screen silhouette
  ~147x174 design px vs the OG's ~122x93). `FlyingSpider.SizeFactor` multiplies BOTH the foreground
  (1.0) and background (0.67) base scales in `Initialize`; the sprite AND its box hitbox (sized off the
  frame via `DrawScale`) shrink together, so collision tracks the visible size. Baked default is
  **0.85** (`DefaultSizeFactor`); override live with **`?flyspiderscale=<f>`** (null => the baked
  default, so a shipped build is unchanged). Applies in play AND the sprite harness, so
  `?harness=flyingspider&play&flyspiderscale=0.8` previews it (also a field in `wwwroot/harness.html`).
  To retune: pick a value by eye, then update the `DefaultSizeFactor` constant.
- **Group-flatten for translucent multi-part sprites (`SpriteBatchWrapper.BeginGroupFlatten`/
  `EndGroupFlatten`).** Overlapping straight-alpha sprites each drawn at partial alpha double-brighten
  where they overlap; bracket their draws to flatten them OPAQUE into a shared grow-only RT, then the
  union composites ONCE at the group alpha (used by the background fog `FlyingSpider` -- body + wings
  fade as one silhouette; foreground spiders draw directly). Like the text flatten, the capture is
  PREMULTIPLIED (`PremultiplyOver` -> One/InvSrcAlpha composite with a premultiplied tint; callers
  still pass a normal straight tint) so a layer's AA edges blend correctly over the layer below.
- **Laser FX (`Quad.cs` beam + `LazerGeneratorData.cs` chargeup) — LIVE tuning via `?lazershot`
  (Trello "improve laser animation").** The Protoss-style beam is `Quad.Draw` (a wide blue glow +
  white-hot core, each ONE continuous sprite, + tip/muzzle blooms + electric tendrils); the pre-fire
  chargeup swarm is `LazerGenerator` (10 converging `GFX/Menu/star` sparkles, additive). Three fixes
  landed with URL knobs so the feel can be A/B'd by eye (all null => baked defaults ship unchanged):
  (1) **chargeup is a WINDUP ANIMATION, not a flat swarm** — the per-particle scale RAMPS `1 -> peak`
  (`DefaultPeakChargeScale` **4**, ease-out near-linear via `ChargeEase`) over the windup, applied at
  DRAW time in `LazerGenerator.Draw` (so the whole swarm ramps crisply; `LazerGeneratorData` only bakes
  the base `0.015` scale). PLUS an **"energy well"**: a white-hot orb at the convergence centre,
  drawn as a STACK of additive `lazerglow` glows (blue halo -> cyan-white body -> white-hot core,
  colours == Quad's beam layers) so its centre saturates white like the laser tip (a single flat-blue
  draw read too dim); the core brightens with progress (glares before eruption). It grows `15% -> 100%`
  of `1.6x the laser tip`
  (`LaserTipDiameter` 48 = beam width 16 × `TipFlareScale` 3 × `WellTipFactor` 1.3) while fluctuating
  ~90-110% erratically (3 incommensurate fast sines) — energy gathering before it bursts. Both driven by
  `progress = elapsed/windupSeconds`. **The windup is FLEXIBLE**: callers pass the REAL (per-laser,
  difficulty-scaled) duration via `LazerGenerator.SetWindup(seconds, loop)` — UFO 2.5s, SpiderHelper
  `windupMs/1000`, showcase `loop:true` (repeats the ramp to watch it; in-game plays once + holds at full).
  `?lazerchargescale=` overrides the peak. (2) **beam ends looked "chopped" / a core-vs-flare seam** — the
  `lazermiddle` strip has no soft falloff ALONG its length, so `Quad.Draw` now domes each end with a
  width-sized round GLOW-then-CORE cap (`DefaultCapScale` **1.0**, `?lazercapscale=`). (3) **tendrils SPAWN
  STOCHASTICALLY + DRIFT** — `DrawArcs` is now STATEFUL (a `Tendril[]` pool): each frame one Bernoulli
  trial spawns a tendril at `DefaultArcRate` **2**/sec (the `RandomHelper.RandomFromAverage` `rate*dt`
  model, but on `Quad`'s FX RNG so it can't desync co-op), each lives a RANDOM `0.25..0.5s`
  (`DefaultArcLifeMin/Max`; `?lazerarclife=` overrides the MEAN, ±33%), fades via a `sin(pi*frac)` envelope,
  and DRIFTS along the beam at a random SIGNED speed up to `DefaultTendrilSpeed` **30** px/s (the whole
  tendril -- anchor AND lean'd free end -- is clamped to the beam span so drift can't push it past a tip)
  (`?lazertendrilspeed=`) — so they pop up out of sync "all over" and slide, vs the old fixed handful on a
  shared cadence. `SetProperties` clears the pool (recycled beams). The bolt still writhes smoothly within
  one life (time-driven midpoint displacement). `?lazerarcs=` is now the RATE (was a count).
  **Tune with `?lazershot`** — `Compat/LazerShowcaseScene.cs` shows the chargeup swarm+well (left) + a
  full-grown beam (right) ANIMATING (unlike the frozen `?harness`/`?bulletshot`); it drives a raw `Quad`
  for a stable beam + a real (Update-ticked, via `Collection.Add`) `LazerGenerator` (with `loop:true`
  windup) for the chargeup. **LIVE SLIDER PANEL (no reload needed):** `?lazershot` shows a top-right HTML
  slider panel (built in `index.html` outside `#app`, ONLY on that page so a normal boot is byte-identical)
  dragging the four knobs — **Chargeup peak scale / End-cap size / Tendril rate (/s) / Tendril speed (px/s)**
  — in REAL TIME via `window.eaLazer` -> `Compat/DebugInput.SetLazer` ([JSInvokable `debugSetLazer`]) ->
  `DebugFlags.SetLazerOverride` (`Quad` reads `CapScale` every Draw + `ArcRate`/`TendrilSpeed` at each
  spawn; `LazerGenerator` reads the peak every Draw). The readout prints the
  `lazerchargescale=/lazercapscale=/lazerarcs=/lazertendrilspeed=` string to paste back for baking in; the
  `?lazer*` URL flags seed the sliders. `eaLazer(charge,cap,rate,speed)` also works from the console. (The
  range inputs carry `autocomplete='off'` — without it Chrome's form-restoration re-seeds them post-load
  and desyncs the game from the defaults.) When the user settles on values, bake them into the `Default*`
  constants (`DefaultPeakChargeScale` in `LazerGenerator`; `DefaultCapScale`/`DefaultArcRate`/
  `DefaultTendrilSpeed`/`DefaultArcLifeMin`/`Max` in `Quad`). Straight-alpha additive tints throughout (do
  NOT premultiply).
- **Ship-connector docking lightning (`Game/EvilAliens/ShipConnector.cs`) — LIVE, not a frozen sprite
  (Trello "connector too static").** The multiplayer docking connector (formed when a linked ship picks
  up a `2`/Linker, holding two ships at a FIXED ±39px around their midpoint) was ONE static
  `GFX/Sprites/connector` sprite (twin blue orbs + baked crackle) — dead-still, worst with two ships
  parked. It now: (1) the base sprite **breathes** (a sine brightness pulse on `color`, straight-alpha,
  look otherwise unchanged); (2) live **fractal lightning** crackles over the gap between the two ship
  positions — a few continuously-writhing main bolts (blue-glow + white-hot core, `DrawLine` of
  `lazermiddle`) plus a stochastic pool of short **crackle tendrils** that spawn/whip/fade; (3) a churning
  **energy-well orb** on each ship (`DrawEnergyOrb`) — a STACK of additive `lazerglow` layers (blue halo
  -> cyan-white body -> white-hot core), each shimmering on its own incommensurate sines (`Wobble`) + a
  hard core glare, the two orbs on decorrelated phases so they don't pulse in lockstep. That's the SAME
  layered-well recipe as the laser chargeup (`LazerGenerator.DrawWell`), NOT the old single flat bloom
  (which read static). Same midpoint-displacement + time-driven
  `Wiggle` technique as the `Quad` laser (`BuildBolt`/`Wiggle`), reimplemented self-contained here (own
  FX RNG off `RandomHelper` so it can't desync a future lockstep co-op; static scratch buffers) rather
  than dragging in the beam pipeline. All **additive, straight-alpha (do NOT premultiply)**. **The FX
  advance on RAW Draw time** (`fxTime += dt` in `Draw`, like the metal sheen / brain overlays) so they
  keep crackling through a hit-stop freeze; nothing is in `Update`. **Tuning** (all null => baked
  `Default*` consts, so a shipped build is byte-identical): `?connectorbolts=<n>` (main bolt count) ·
  `?connectorarcs=<f>` (crackle tendrils/sec) · `?connectorjitter=<f>` (bolt zig-zag amplitude ×) ·
  `?connectorpulse=<f>` (breathe Hz) · `?connectorglow=<f>` (orb-bloom size/intensity, 0=off). Bake
  chosen values into the `Default*` consts in `ShipConnector`. **LIVE SLIDER PANEL** (`eaConnector` in
  `index.html`, outside `#app`, only built on `?level=TeamChallenge` / `?harness=connector` / a bare
  `?connectortune`) drives
  `DebugInput.SetConnector` → `DebugFlags.SetConnectorOverride` (read every Draw); the orange readout
  prints the bake-ready `?connector*` string; `eaConnector(bolts,rate,jitter,pulse,glow)` from the
  console too. **VERIFY WITH `?harness=connector`** — the sprite harness draws it with NO ships
  (`ShipConnector.HarnessMode` derives the two orbs from the component's own `Position`/`rotation`, which
  `HarnessScene` drives), and the FX still animate because they're Draw-driven. This is the way to see
  it: `TeamChallenge` (the real two-ship dock) auto-pauses on focus loss and the ships fly in, so it's a
  moving/paused target the harness sidesteps. Picker entry in `wwwroot/harness.html`. Reuses
  `lazermiddle`/`lazerglow`/`connector` (all already preloaded).
- **Level-3 alienboss "lightbulb" colorize tuner (`Compat/HarnessColorize.cs` + `?harness=battleskull`).**
  The alienboss sprite (`GFX/alienboss/alienboss`, used by `BattleSkull`/`FakeBoss`/`ClassicBoss`) is
  the "little lightbulb" boss. `BattleSkull` is the one that **hue-remaps** it (the others only do the
  `KillableAlien` red death-tint): its `Draw` sets `colorizeEffect.RangeTarget = (-10, 10, num)` where
  `num = HitPointsNormalized*100` (100 = green full HP -> 0 = red dead), which the `sprite.fx` COLORIZE
  path recolours toward (see the feathered hue-range remap; grays are untouched). To tune "it doesn't
  colorize well" by eye, `BattleSkull.Draw` routes its RangeTarget through **`HarnessColorize.Apply`**,
  which overrides the band + target from URL flags **only while the sprite harness is up**
  (`DebugFlags.Harness != null` **and** a hue flag present) — so normal play + the other alienboss
  bosses are byte-identical. Flags (`Compat/DebugFlags.cs`): `?huestart=<deg>` / `?hueend=<deg>` (the
  hue band, in-game -10/10), `?huetarget=<deg>` (alias `?hue`, pin the target; default = HP-based),
  `?huecycle` (auto-sweep the target 0..360 so a screenshot shows any point; `?hueloop=<sec>` period).
  `HarnessScene` shows a live `colorize band [..]  target ..` readout (`HarnessColorize.Describe`).
  **LIVE SLIDER PANEL (no reload needed):** `?harness=battleskull` now shows a top-right HTML slider
  panel (built in `index.html` outside `#app`, ONLY on that harness so a normal boot is byte-identical)
  that drags the band start/end + target hue + track-HP/auto-cycle toggles in REAL TIME — it drives
  `window.eaHue` -> `Compat/DebugInput.SetHue` ([JSInvokable `debugSetHue`]) -> `DebugFlags.SetHueOverride`,
  which `HarnessColorize.Apply` reads every frame, so a drag recolours the boss on the next Draw. The
  panel's readout line prints the `huestart=/hueend=/huetarget=` string to paste back for baking in. The
  `?hue*` URL flags still work and seed the sliders' initial values. `eaHue(start,end,target,trackHp,cycle,loop)`
  is also callable from the console.
  Picker: `wwwroot/harness.html` (battleskull option + hue fields). When the user settles on values,
  the chosen band/target get written back into `BattleSkull.Draw`'s hard-coded `new Vector3(...)` (and
  the target curve if they change how it tracks HP). e.g. `?harness=battleskull&huestart=-20&hueend=40&huecycle`.
- **Mars jumping-spider alignment tool (`?harness=spiderjump` + `Spider.HarnessApplyPhase` + `Compat/HarnessScene.cs`).**
  The Trello card "the jumping spiders on mars, we need a tool for me to align" asks for a tool to dial in
  the spider's **shadow position**, **jump-start X**, and **land-anim resume frame** (plus a future
  animation-driven jump: rear-up -> fling -> jump-prep -> launch, with a scrollspeed-compensated entry
  frame so a random jump-X lines up with the jump beat). This card built the **plumbing**: a sprite-harness
  mode that LOOPS the whole crawl -> launch -> arc -> land cycle (mirroring the blast/battleskull tuners).
  `Spider.HarnessApplyPhase(phase)` is a self-contained deterministic sim of that cycle (drives
  Position/curframe/rotation/hasJumped so the real `Draw` shows the ground sheet vs the airborne
  `spiderjump` sheet); it presets the entry frame via the "count back" `entryFrame = J - fps*tJump` so the
  jump beat coincides with `jumpX`. The spider crosses the screen over one loop (viz scroll is DERIVED from
  the loop for visibility, NOT the fast real Mars scroll). `HarnessScene` overlays the **shadow** (drawn by
  a low-DrawOrder `SpiderShadowDrawer` so it sits UNDER the sprite, like `Floor`; it tracks X, detaches +
  shrinks on the jump), **jump-X / ground / feet markers**, and a **readout** (scroll, entryFrame, curframe,
  jump/land frames, shadow offset). Tunable via `?spiderjumpframe= ?spiderlandframe= ?spiderjumpx=
  ?spidershadowx= ?spidershadowy= ?spidershadowscale= ?spiderloop= ?spiderphase=` (all read ONLY while the
  harness is up + kept OUT of `DebugFlags.Active` -> **live play is byte-identical**). `?spiderphase=<0..1>`
  freezes the cycle for a deterministic apex screenshot. Picker: `wwwroot/harness.html` (spiderjump option +
  fields). This card built the tool; the **live animation-driven jump** it previews is now wired (see the
  next bullet), and the `Spider.Initialize` **random animation start** it added (a cluster crawls out of
  lock-step) is preserved but overridden by the jump's count-back preset. Dialing the final values stays
  the "For me" card 5645a489.
  e.g. `?harness=spiderjump&bg=mars&spiderphase=0.535` (airborne apex) · `...&spidershadowy=-90` (align shadow).
- **Mars jumping-spider jump is now ANIMATION-DRIVEN in LIVE play (`Spider.cs`).** The grounded
  `Spider` no longer launches on `Position.X < jumpXposition`; it fires when the rear-up animation
  reaches a launch beat. On the first `Update` a one-time "count back" presets an UNWRAPPED frame
  accumulator (`animAcc`) from the REAL Mars scroll (`oracle.BackgroundSpeed.X`) so the beat coincides
  with the spider passing a (still random) launch X: `entryFrame = jumpBeatFrame - fps * (dist/scroll)`;
  it then fires when `animAcc >= jumpBeatFrame` (unwrapped, since `base.Update` wraps `curframe` mod the
  49-frame sheet, which can't be crossing-tested). `spider_sheet2` is one rear-up->fling->settle cycle
  with a believable "about to spring" peak (~frame 40 = `DefaultJumpFrame`), enough for a legible
  single-beat launch; the fancier rear->fling->down->rear->jump double-pump would need re-authored art
  (optional follow-up). The `?spider*` knobs now apply to LIVE play too (not just the `?harness=spiderjump`
  viz): `?spiderjumpframe=` (launch beat), `?spiderjumpx=` (pin the launch X, else random per spider),
  `?spiderlandframe=` (touchdown resume frame), `?spidershadowx=/y/scale=` (the dialed shadow, applied via
  the generic `Floor` shadow's `ShadowOffset`/`ShadowSize`). **All default to identity, so a shipped build
  with no query is byte-identical.** Bake dialed values into the `Default*` consts / `LandFrame` in
  `Spider.cs`. **Watch/dial it live with `?level=Level2&spiders`** -- a debug boot (mirrors `?spiderboss`)
  that skips the level and runs a continuous pure-spider GROUND wave, so the jump is seen in REAL play (the
  harness sim's arc is only illustrative). Pair `?invuln`. Final dialing is the "For me" card 5645a489.
  e.g. `?level=Level2&spiders&invuln&spiderjumpframe=42&spidershadowy=-40`.
- **Mars jumping-spider LIVE TUNER PANEL + the dialing is DONE and baked (card 5645a489).** The `?spider*`
  knobs now have a live slider PANEL (`index.html`, outside `#app`; auto-shown on `?harness=spiderjump` /
  `?level=Level2&spiders` / a bare `?spidertune`) -- drag jump-beat / land / launch-X / shadow x/y/scale +
  a **flying-sprite Air offset X/Y**, plus a **Freeze & scrub Phase** control that parks the harness on any
  point of the crawl->launch->land cycle (~0.47 = the last ground frame before launch) so the launch +
  landing pose transitions can be lined up by eye. It drives `Compat/DebugInput.SetSpider` ([JSInvokable
  `debugSetSpider`]) -> `DebugFlags.SetSpiderOverride` (all live, no reload; `eaSpider(...)` from the
  console too); the orange readout prints the bake-ready `?spider*` string. **Baked-in dialed values:**
  `DefaultJumpFrame` **5**, `LandFrame` **42**, `GroundY` 505 -> **485** (whole spider assembly lifted
  ~20px) in `Spider.cs`; shadow **(37,4) x0.95** + **air offset (14,1)** carried as `DebugFlags` defaults
  (read live by `Spider`). The **Air offset** nudges the airborne `spiderjump` sprite (whose visual anchor
  differs from the ground rear-up sheet) so the first/last in-air pose connects with the ground launch/land
  frames; the harness illustrative arc was raised (`v0` -600, ~200px apex, kept deterministic for
  phase-scrub) so the flying sprite is clearly airborne while dialing. Live play jump HEIGHT/variance was
  always fine (rand -8..-19 launch vel) -- only the old harness demo arc was low, which misled; not a
  regression. Panel gate is a URL regex only (no C# flag, no `harness.html` picker entry). **The harness
  SHADOW now goes through the real `Floor` math** (`Floor.ShadowScalars` / `Floor.DrawShadowScalars`,
  extracted static + behavior-preserving for live) instead of a hand-rolled drawer that was fainter /
  smaller / higher -- so the shadow the harness previews is byte-identical to what `Floor` casts in game
  (the tuning finally translates). `HarnessScene.DrawSpiderShadow` reads the shadow knobs LIVE from
  `DebugFlags` (what a freshly-spawned live spider would use) so a panel drag updates the preview at once.
- **Landed Mars-UFO placement offsets (`Compat/LandedOffsets.cs` + `wwwroot/landed-editor.html` +
  `Content/data/landed_offsets.json`).** The Mars saucers that start parked on the ground
  (`ufometpootjes`/`Smallship_landed`/`Mediumship_landed`, spawned by `StationarySpawner`) and the
  drifting `Mothership_landed` (`StationaryBoss`) each use a **different still sprite** than their flying
  animation, so their ground shadow can read off-centre and the landed->flying handoff can JUMP (the
  parked still's "landing feet" offset its visual centre from the flying frame's). This card built the
  **plumbing + an HTML author tool**; the actual values are the user's to dial (the "For me" card). Per
  sprite, `landed_offsets.json` holds (all DESIGN-space px, +y down): `landed` (nudges the parked still's
  draw), `takeoff` (shifts `Position` ONCE at lift-off so the flying sprite continues seamlessly),
  `shadow` (nudges the ground shadow x + y-along-the-floor), `shadowSize` (× the shadow width). Identity
  (0s / size 1, or a missing entry/file) reproduces the ORIGINAL untuned behaviour — so the shipped
  all-zero file changes nothing until tuned. `LandedOffsets.Get(name)` loads it once, lazily
  (`TitleContainer.OpenStream` + `JsonDocument`, both proven trim-safe; missing/bad file -> identity).
  Consumers: `UFO` (`SetStationary` applies the shadow tuning + caches the entry; `Draw` offsets the
  still by `landed`; lift-off adds `takeoff` to `Position` and clears the shadow tuning; `Setup` resets
  to identity for recycled instances) and `StationaryBoss` (`Initialize` applies shadow tuning; `Draw`
  nudges only the draw by `landed`). The shadow tuning rides the generic `Floor` shadow via two new
  identity-default fields on `AlienDrawableGameComponent` (`ShadowOffset`/`ShadowSize`) that
  `Floor.CollidesWith` reads — so there's ONE shadow, no double-draw, and every OTHER entity is
  byte-identical. **The tool is `wwwroot/landed-editor.html`** (served alongside the game, e.g.
  `…:5280/landed-editor.html`): it fetches the real sprites + shadow, lets you drag the parked still, the
  shadow, and a flying-sprite ghost (takeoff preview) on a Mars-ground canvas, loads the committed JSON
  to continue, and exports a new one. **GOTCHA when iterating in-game:** the JSON is read late (at the
  first stationary spawn) via `TitleContainer`, so a plain reload serves the STALE browser-cached copy
  (the same Content-cache trap as textures) — bust it (DevTools "Disable cache", or
  `fetch('Content/data/landed_offsets.json',{cache:'reload'})` then reload). Production self-heals via
  ETag. Re-export from the tool; don't hand-edit values you can author visually.
- **Hitbox debug overlay (`Compat/HitboxOverlay.cs` + `?hitboxes` / `eaHitboxes()`).** Draws EVERY
  live collidable's collision shape over the running game, colour-coded by kind — `CollisionBox`/
  `CollisionMultibox` -> **cyan** rectangle outline, `CollisionSimpleCircle` -> **green** ring,
  `CollisionLine` (bullets, lazers) -> **orange** segment; active hitboxes bright, inactive
  (`Collides == false`) dim. Built to SEE the many objects whose DRAW is offset from their
  Position/hitbox (the landed Mars UFOs + drifting `StationaryBoss` nudge only their Draw — see the
  LandedOffsets bullet), which the sprite harness can't show (it only rings *parked circular*
  hitboxes). `HitboxOverlay.Draw` iterates `CollisionHandler.Collidables` (a read-only accessor added
  for this) and draws each shape via the `spriteBatchWrapper` in 800x600 design space, hooked in
  `Game1.DrawInner` right after the game + bloom composite (same seam as the HideSafeArea overlay) so
  it sits on top of everything, un-bloomed. It owns a 1px white pixel (lines/box edges) + a ring
  texture (`BuildRing`, same approach as `HarnessScene`'s ring — a slightly tighter band). Enable with the **`?hitboxes`** URL flag or
  toggle live from the console with **`eaHitboxes()`/`eaHitboxes(false)`** (`DebugInput.Hitboxes` ->
  `DebugFlags.SetShowHitboxes`). OFF by default and deliberately kept OUT of `DebugFlags.Active`, so a
  shipped build is byte-identical unless it's asked for. Verify by eye in any level (e.g.
  `?level=Level1&hitboxes&invuln` — bullets show as orange segments, asteroids as green rings, UFOs as
  cyan boxes). Follow-up "big pass" (a "For me" card): now that hitboxes are visible, fold each
  draw-offset object's nudge into its `CollisionType` so sprite + hitbox align.
- **Game juice: screen shake + hit-stop (`Compat/Juice.cs`) — the two classic feel effects the port
  was missing** (per Vlambeer's "Art of Screenshake" / "Juice it or lose it"; hit flash, rumble,
  particles, slowmo, ghost trails, floating text already existed — `plans/juice.md` has the research
  -> mapping). **Shake** is the trauma model: events call `Juice.AddTrauma` (`Explosion.Initialize`
  sized by explosion size, `Blast.Initialize` by bomb power, player death), strength = trauma^2 (so
  stacked events read bigger than any one), decays ~0.7s, each tick samples a random offset (max 7
  design px) + roll (max 1 deg — both halved from 14/2, card 8e439865: full shake impacted
  gameplay). Applied at the PRESENT BLIT in `Game1.Draw` (offset + roll + a
  slight zoom so edges stay covered) — a pure camera effect: no gameplay coordinate, collision, or
  mouse mapping (`WindowToDesign`) is touched. An explosion SERIES can opt out of shake per
  instance (`Explosion.Setup(..., noShake: true)`) — the L3 `BattleSkull` miniboss death does,
  so only its finale blast shakes (card 8e439865). **Hit-stop** freezes GAME time (folded into
  `Game1.Update`'s existing turbo x slowmotion scale as `Juice.TimeScale`) while REAL time keeps
  ticking Juice/shake/input: the per-kill ~1.5-frame micro-stop + 90ms boss-kill stop
  (`Juice.KillPunch` in `KillableAlien.HitBy`) are **OFF by default** (card bd5efd9d — they read
  as stutter, not juice; `?hitstop=1` re-enables for A/B, kill-shake trauma unaffected); player
  death keeps its 180ms stop + extra trauma (`PlayerShip.Asplode`/
  `AsplodeWall`). Draw-time cosmetics (the Blast rim spin, metal sheen) keep animating during a
  freeze by design — Draw gets raw time. Tune/A-B: `?shake=<0..3>` (0 = off), `?hitstop=1`; QA from
  the console anywhere: `eaShake()`/`eaShake(1)`, `eaHitstop()`/`eaHitstop(500)` (DebugInput +
  index.html, same seam as eaSlowmo; eaHitstop always fires, ungated). Both are feel toggles kept
  OUT of `DebugFlags.Active`.
  GOTCHA: hit-stop must decrement on UNSCALED dt (`Juice.Update` runs in `Game1.Update` BEFORE the
  time scale) — a scaled-time timer would freeze and never thaw.
- **Cinematic slow-motion ghost trails (`Game1.ApplySlowmoTrail`).** The 1up-powerup slowmo
  (`PlayerShip` -> `Oracle.SetSlowmotion(12f)`) used to be ONLY a time-scale (0.4x) + a bloom-preset
  swap; it now also gets a movie bullet-time **motion blur** so moving objects smear into fading
  "ghost" echoes. It's a present-time post-process in `Game1.Draw`, run on the fully composited +
  bloomed `sceneTarget` *before* the gamma present blit (so the ghosts carry the glow). Technique =
  a frame-feedback / accumulation buffer (`slowmoTrail` RT, lazily created on first slowmo, recreated
  on resize like `sceneTarget`): `trail = trail*decay + scene*(1-decay)` (an EMA), then mixed back as
  `scene = lerp(scene, trail, k)`. The EMA converges to the input for a STATIC pixel, so still areas
  (HUD, idle sprites, background) are **unchanged — no blow-out**; only moving content, where the
  trail lags the live frame, leaves directional echoes. `slowmoTrailMix` eases the whole effect in/out
  (~0.25s) and the first slowmo frame **seeds** the trail with the current frame so engaging slowmo
  doesn't flash dark. Blends use straight alpha (`NonPremultiplied` decay-via-black + lerp, `Additive`
  feed); `blackPixel` is the shared white pixel tinted black. Defaults `decay 0.88` / `strength 0.8`
  (clearly cinematic but not muddy); ON by default. Tune/A-B live with `?slowmotrail=0` /
  `?slowmotraildecay=` / `?slowmotrailstrength=` (`DebugFlags`; like `MetalScore`, kept OUT of
  `Active` since it's a pure render look). **QA/demo:** console `eaSlowmo()` (or `eaSlowmo(6)`) fires
  the same slowmo burst on demand in a level — `Compat/DebugInput.Slowmo` ([JSInvokable]) ->
  `Oracle.SetSlowmotion`; no-op unless a level with a live ship is running (Oracle clears slowmo the
  instant no ships are alive). A new full-frame post-process should follow this same place in `Draw`
  (operate on `sceneTarget`, leave RT on it for the present block) and use the raw `spriteBatch`
  (identity), not `spriteBatchWrapper` (which applies `RenderScale.Matrix`).
- **Tutorial punch-up (card 4aab0629): paced-by-the-player event list + a fullscreen "holo-sim"
  filter (`Compat/HoloSim.cs` + `tools/shaders/src/holosim.fx` + `Game1.ApplyHoloSim`).** Two
  halves. (1) **PACING** -- `TutorialLevel.PopulateEventList` no longer runs strictly serial:
  text and action land on the SAME beat (a non-halting `message(..., halting: false)` over a
  halting spawner -- `GameEventList` always supported concurrency; the tutorial just never used
  it), and each powerup lesson's fixed 9.5s wait is replaced by **advance-on-pickup**
  (`Game/EvilAliens/WaitForPickupEvent.cs`: subscribes `PlayerShip.OnCollectPowerup` with a
  -=/+= per tick so ship recycling can't drop it; base lifetime = timeout ceiling, so a passive
  player still progresses -- a full unattended run finishes in ~2min). **Layout rule: two
  `TutorialMessage` banners draw at the same spot, so overlap is only ever text-with-ACTION,
  never text-with-text** -- a lesson's message is `LinkWith`'d to its pickup gate (and the fire
  prompt to its practice-UFO spawner) so the gate ending clears the banner before the next text.
  (2) **VISUALS** -- the holodeck gets a fullscreen sim filter: subtle scanlines + an edge-heavy
  cyan cast + interlace shimmer always-on, and a hard "channel surf" glitch spike (row jitter /
  static / contrast crunch, the `channelflip.fx` recipe) on "Activating/Terminating Tutorial..."
  (`TutorialLevel.burst()`) and small on each `Background.Jump()` hiccup so the grid slip and the
  screen glitch land together. Plumbing: `holosim.fx` (pixel-shader-only, compiled by
  `build_shaders.py` like every standalone `.fx`) applied in `Game1.ApplyHoloSim` right after
  `ApplySlowmoTrail` (ping-pongs `sceneTarget` -> `holoRT` -> back; a SpriteBatch effect pass
  can't read its own target). **Lifecycle is POKE-driven, not scene-wired:** `TutorialLevel.Update`
  calls `HoloSim.Poke()` every tick and the eased mix fades out the moment poking stops -- any
  exit path (victory / quit / game over) turns it off with no lifecycle plumbing. Every other
  scene skips at one branch; a non-tutorial boot is byte-identical. **The filter also runs in
  the EVIL ALIENS CLASSIC challenge (card 2382b514)** -- `ClassicAliens.Update` pokes + fires the
  same Jump()-paired hiccup bursts, and its "Activating/Terminating Training..." messages get the
  channel-surf spike, since it shares the trial-sim fiction. **Monochrome phosphor-green pull**:
  the shader's `Green` param pulls the whole frame toward a classic green terminal
  (`HoloSim.DefaultGreenPull` 0.45), breathing back toward true colour on a slow pulse
  (`DefaultGreenPulse` 0.6 depth, ~0.12Hz -- computed C#-side in `HoloSim.Green`, the shader
  just gets the final value). The glitch spikes ship dialed way down (`DefaultBurstScale` 0.1 --
  the full-strength burst read too harsh) at ~10 hiccups/min (`DefaultHiccupRate` 0.167/s); all
  user-dialed via the panel (card 2382b514). Tune with `?holofilter=<f>` (0 = the WHOLE filter
  off, green included) / `?holoburst=<f>` (glitch-spike scale) / `?hologreen=` /
  `?hologreenpulse=` / `?holostaticrate=` (random-hiccup rate per second, `HoloSim.HiccupRate`
  -- both simulator levels roll it) -- pure render looks, all kept OUT of `DebugFlags.Active`
  like `?slowmotrail`.
  **LIVE SLIDER PANEL (`eaHolo`)**: auto-shows on `?level=Tutorial` / `?level=ClassicAliens` / a
  bare `?holotune` (index.html, outside `#app`, eaLazer pattern) -- green pull / pulse depth /
  glitch strength / glitch rate per min / intensity, driving `DebugInput.SetHolo` -> 
  `DebugFlags.SetHoloOverride`, read every frame; the orange readout prints the bake-ready
  `?holo*` string; `eaHolo(green,pulse,burst,ratePerSec,filter)` from the console too. When
  values settle, bake them into `HoloSim`'s `Default*` consts AND the panel's seed literals
  (same duplication caveat as eaLazer/eaWalls). The tutorial BANNER (`TutorialMessage.Draw`) also
  routes through `DrawShadowStringCached` (slot 100; score HUD owns 0..15) in holo-cyan -- the
  same flattened shadow+text treatment as the score, so no shadow bleed-through; don't revert it
  to a raw `DrawString`. Verify with `?level=Tutorial&invuln` (real Chrome); the whole tutorial
  self-runs on the timeout ceilings, so an unattended boot exercises every beat.
- **Texture loads: PNG decode is the stutter; precompile hot sprites to DXT/raw (an offline asset
  build step).** `Texture2D.FromStream` decodes PNGs via **StbImageSharp — managed, on the WASM main
  thread, interpreted (no AOT)** — so a cold multi-megapixel sheet is a multi-hundred-ms to multi-second
  frame hitch (measured: `spider_sheet2` 5033 ms; a whole Level2 preload ~28 s). Two tools attack this:
  (1) **`Compat/LoadProfiler.cs`** (debug flag **`?loadlog`**) times every decode, flags ones that load
  *outside* a level's preload phase (the stutters), accumulates a per-level set in localStorage that the
  preloader feeds back (`GameScene.LoadContent` → `BeginPreload`/`ApplyManifest`/`EndPreload`), and exports
  a committable list via **`eaPreloadExport()`** in the console → `wwwroot/Content/preload/manifest.txt`
  (read by ALL builds at preload; release never writes). `LoadProfiler` also runs an **always-on frame-hitch
  watchdog**: `TickDotNet` times each `Game.Tick()` and `LoadProfiler.NoteFrame` logs a **`[hitch] <ms>ms
  frame in <level>`** line whenever a single tick exceeds `HitchMs` (120ms) — edge-detected (one line per
  spike, no spam), skipping the preload phase + boot warm-up. It's NOT gated by `?loadlog` (so a "the game
  froze here" report has a number + level even in a shipped build) and catches ANY long tick, incl.
  non-texture hangs `?loadlog` can't see; pair it with `?loadlog` to attribute a texture decode. (2)
  **`tools/textures/build_textures.py`** reads
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
  **The end-credits Cast "Brain Spawn" entry (`CastDisplayer.braineroid`) now draws this animated sheet too**
  (card 208da2fe — it was still the old static `brainlargetransglow`). `CastDisplayer` draws every cast member
  by hand (its own `Draw`, NO interpolation shader), so it plays the 20-frame sheet at a higher raw fps
  (`DefaultBrainFps` 10) than the in-game 0.4, and draws the additive blue glow (`brainanimatedglow`) behind it
  via `DrawBrainGlow` (mirrors `Braineroid.DrawGlow`; `scale/textureScale` = DrawScale so glow tracks the brain).
  Size/speed are baked defaults (`DefaultBrainScale` 1.7 / `DefaultBrainFps` 10) overridable by eye — the real
  Cast screen is only reachable after beating L3 on Hard, so **`?castbrain` boots straight to it** (reuses
  `HarnessScene` in a cast-brain mode; Esc → menu) with tuners `?castbrainscale=<f>` / `?castbrainfps=<f>`
  (null override => the baked defaults ship, blast/colorize-tuner pattern; `DebugFlags.CastBrain/CastBrainScale/CastBrainFps`).
  Picker link in `wwwroot/harness.html`. When the user settles on values, bake them into the `DefaultBrain*`
  consts in `CastDisplayer`.
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
  **Fly-by choreography (card "earth animation improvements"):** the earth KEEPS its own descent
  speed; what sells "it's closer, so it zooms past" is freezing the STARS, not speeding up the
  earth. `Background.DoodadStarSlowdownFactor()` multiplies `scrollspeedmodifier` (which the earth
  ignores) down to `doodadStarSlowdown` while a planet crosses — `0.082` for the hero earth, set so
  the earth = **5x the fastest near ("hero") star** (`1.55 / (5 * 3.8)`, `3.8` = `DriftingStars` max
  parallax). The ramps are WALL-CLOCK timed (converted to crossing-progress each frame via the
  doodad's speed): a rapid ~1.2s slow-down on entry, a long hold, a ~1.6s speed-up on exit — so the
  feel stays snappy even though the earth itself drifts across over ~90s. In Level 1 `QueueEarth()`
  is called from `demo_OnFinished` (player pop-in), NOT at level start, so the earth enters after the
  UFO intro as the player takes control; the slow-down engages with it and the asteroid belt waits on
  the same `WaitForDoodadEvent` gate.
  **Asteroid-belt star-slowdown (card "same as earth but for the asteroid field"):** the SAME depth
  cue is applied to Level 1's sideways asteroid-belt phase, because at the gameplay baseline the
  fastest near star (`scrollspeedmag 0.039 * maxParallax 3.8 ~= 0.148` design px/ms) was as fast as
  (and sometimes past) the slowest asteroid class — the dim decorative **background** asteroids
  (`0.38 * 0.4 ~= 0.152`, `-10%` jitter -> ~0.137 px/ms; the collidable foreground ones are 0.38).
  The belt is a WAVE (an `AsteroidSpawner` over ~42s), NOT a crossing doodad, so it does NOT ride the
  per-doodad `DoodadStarSlowdownFactor()` position hook — instead `Background.BeltStarSlowdownFactor()`
  is a SECOND factor multiplied into `effectiveModifier` alongside the doodad one, driven by an
  explicit engage/disengage + a wall-clock ramp (`EngageBeltSlowdown`/`DisengageBeltSlowdown`, stepped
  by `UpdateBeltSlowdown` over `BeltRampInMs 1200` / `BeltRampOutMs 1600`, smoothstep-eased like the
  doodad envelope). `BeltStarSlowdown = 0.37` pulls the fastest star to ~0.055 px/ms so the decor
  asteroid is ~2.5x it (mirrors the earth's "Nx" reasoning; foreground asteroids end up 5x+). Level1
  `spawner_OnFinished` (which sets the belt scroll speed) calls `EngageBeltSlowdown()`; the
  `AsteroidSpawner.OnFinished` calls `DisengageBeltSlowdown()`. `Demo1` (the attract-mode twin of the
  Level 1 belt) is wired identically so the mismatch is fixed in the menu attract loop too. The
  standalone `AsteroidChase`/`SpaceDodge` minigame is deliberately OUT of scope (its `SetSpeed(0.3,
  0.72)` is a raw, ~20x-faster "warp" scroll — a different feel, not the Level 1 field). The doodad
  and belt slowdowns are combined by `MathHelper.Min` (whichever is deeper wins), NOT multiplied: in
  Level1 they're disjoint (the belt gates on the earth leaving via `WaitForDoodadEvent`) so min ==
  product, but Demo1's attract belt has no such gate and its earth fly-by can still be crossing when
  the belt engages — min applies the deeper slowdown without stacking them into a crawl.
  `Background.Reset()` clears the belt state on every fresh level entry; a death mid-belt can skip the
  disengage but is self-correcting (the belt replays from the pre-belt checkpoint — see the
  `EngageBeltSlowdown` comment; don't add a checkpoint *inside* the belt).
- **Andromeda nebula fly-by (Level-1 brains section) is RESOLUTION-INDEPENDENT -- `tools/nebula/build_nebula.py`.**
  `GFX/Sprites/andromeda` is the galaxy that crosses during Level 1's brain waves (`Background.QueueAndromeda`,
  fired from `Level1.message_OnFinished` after the first `BrainSpawner`). It's a STRAIGHT-alpha sprite
  (`(SpriteBlendMode)1` == AlphaBlend -> `NonPremultiplied`, NOT additive -- the enum is None=0/AlphaBlend=1/
  Additive=2), drawn centred at x=400 scrolling vertically. `QueueAndromeda` now sets `doodadscale =
  AndromedaDesignWidth(840) / doodad.Width` instead of a hard-coded `1f`, so the on-screen footprint is pinned
  at 840 design px for ANY texture resolution -- a higher-res drop-in stays the same size, just crisper (the
  old fixed 840px asset was a ~2.4x blur once RenderScale upscaled it to a 1080p+ window). So swapping in HD
  art needs NO code change. Build it with `tools/nebula/build_nebula.py`: it takes a raw HD galaxy at
  `tools/nebula/source/andromeda.png` (gitignored) and normalises it to straight-alpha RGBA -- auto-deriving
  alpha from luminance if the source is opaque-on-black (else respecting its alpha), applying a per-axis edge
  feather so no hard rectangle shows over the starfield, and capping the long side at 2048. Safe no-op if the
  source is missing (CI ships the committed png). Re-run after swapping the source; don't hand-edit
  `andromeda.png`. Knobs + how-to: `tools/nebula/README.md`. It's a background fly-by (no `?harness=` entry),
  so verify by booting Level 1 to the brains section.
- **Mars far-hills are PROCEDURAL and PARALLAX -- three per-ridge textures from `tools/mars/build_marshills.py`.**
  The Mars hills (added by `Background.SetMars()` behind the HD `marsloop` ground / in front of
  `clouds-background`) used to be ONE low-res hand-drawn hazy tan silhouette with a visible repeating
  seam, then one synthesized texture; they are now **three separate layers** -- `marshills1` (far) /
  `marshills2` (mid) / `marshills3` (near), one texture per `RIDGES` entry -- each with its OWN
  `scrollspeedmodifier` (**far 0.33 / mid 0.53 / near 0.85**, `hillScrolls` in `SetMars`, between the
  sky's 0.3 and the ground's 1.0) so the ridges parallax against each other. Each layer is SYNTHESIZED:
  numpy builds its ridge as a circular-FFT (natively SEAMLESS -- `mirrorX=false`, so a layer just
  REPEATS every `realsize.X`; the wrap MUST be seamless) fractal heightfield with aerial perspective
  (farther ridges lighter/softer/higher, nearer darker/rougher/lower; every crest alpha-feathers +
  lerps toward the haze tone so it dissolves into the sky). The per-layer PNGs are STRAIGHT alpha
  (no OVER-compositing at build time -- the game's layer stack composites at draw); the old single
  `marshills.png` is gone (the tool deletes a stale copy). **Tight visible band:** in Level 2 the `marsloop` ground draws ON
  TOP from design y~448 down, so ONLY ~design y 405..450 (just above the rocky horizon) ever shows -- the
  `RIDGES` crests are placed to land there and each body just fills down to be occluded. All aesthetic
  knobs (palette, per-ridge base/amp/roughness/haze/feather, seed) are constants in the tool's CONFIG
  block. Re-run `python tools/mars/build_marshills.py` after tweaking; `--seed N`, `--preview` (2x-tiled
  seam check -> `tools/mars/_preview_marshills.png`, gitignored), `--show` (composite over the real sky
  -> `_context_marshills.png`). Deterministic/offline (numpy+Pillow), like tools/earth & tools/favicon;
  CI ships the committed `marshills.png`. It's a plain PNG decoded at level preload (tiny, not in
  `textures.config`). Don't hand-edit the PNG -- re-run the tool. GOTCHAS when editing the tool: (1) the
  alpha accumulator is 0..1 while RGB is 0..255, so the final cast must scale alpha by 255 (a missed *255
  makes the whole layer ~1/255 transparent -> invisible hills); (2) the OVER loop accumulates
  PREMULTIPLIED colour but the game renders the PNG with STRAIGHT alpha, so the export MUST un-premultiply
  (divide RGB by alpha; transparent texels filled with the haze tone for bilinear) -- exporting the
  accumulator verbatim turns every feathered crest into a DARK fringe (the original "hills stand out like
  a sore thumb" bug). The palette is MEASURED, not vibed: the sky at the horizon is ~(188,154,116) and the
  OG hand-drawn hills were ONE flat tone (177,144,107), a mere ~11 levels darker -- ridge bodies must stay
  within ~a dozen levels of the sky or they read way too stark. **Tune it with the LIVE EDITOR:**
  `python tools/mars/editor/serve.py` -> `http://localhost:5299/` -- sliders for every CONFIG knob
  (per-ridge crest/height/haze/feather/smoothness/detail + SCROLL speed, palette colour pickers, dust,
  seed+reroll), re-rendered per drag by the REAL generator (`build_layers(seed, cfg)` overrides) and
  composited over the real sky + marsloop ground with the full scene PARALLAX-ANIMATED at the layers'
  relative speeds (animate toggle + preview-speed slider; plus 2x horizon band + static wrap-seam check
  views), a "Write into game" button that saves the three layer PNGs straight into wwwroot (then
  cache-bust the game tab), and a paste-ready CONFIG block to bake the settled values back into
  `build_marshills.py` -- scroll-speed changes are baked by hand into `SetMars`'s `hillScrolls` (the
  block prints the line). Bake + re-run once before committing, so the committed tool reproduces the
  committed PNGs.
- **Tab favicon = the player-UFO sprite, not a drawn alien -- `tools/favicon/build_favicon.py`.** The
  browser tab icon used to be a hand-drawn green "grey alien" head (`wwwroot/favicon.svg`, deleted). It's
  now built from THE game art: frame 28 (top-3/4 "hero" pose) of the player saucer sheet
  `GFX/Sprites/ufosheet`, tight-cropped and composited onto the menu near-black rounded tile (`#05030a`,
  for contrast on light tab bars) -> `wwwroot/favicon.ico` (multi-res 16/32/48/64) + `favicon-180.png`
  (apple-touch). `index.html` references both (NO `favicon.svg` link -- browsers prefer SVG when offered,
  so leaving it would keep showing the old alien). Re-run `python tools/favicon/build_favicon.py` after
  changing the source sheet or the `FRAME`/margin knobs; don't hand-edit the `.ico`/`.png`. Offline
  (Pillow only), like the other `tools/` asset steps; CI just ships the committed outputs.
- **Level-3 collidable-wall texture is an 8x8-tiled SEAMLESS wrap sheet -- upscale via
  `tools/walls/build_wall_tileable.py`.** The front, collidable Level-3 walls (`Wall`,
  `Game/EvilAliens/Wall.cs`) all use ONE texture, `GFX/Base/756-v1`
  (`wwwroot/Content/gfx/base/756-v1.png`, currently a low-res 512x512). `Wall.Draw` samples it as
  an **8x8 grid** -- block (i,j) draws source cell `(j%8, i%8)` at an adjacent on-screen slot,
  wrapping every 8 cells -- so the WHOLE image must **tile seamlessly (all four edges wrap)** or a
  hard seam shows every 8 blocks. On-screen size is dynamic (`scale = 800/(texture.Width*width)`)
  and the cell split is `texture.Width/8` (integer), so a higher-res drop-in only needs **dims a
  multiple of 8** -- **no game code change**. To ship a higher-res wall: (1) upscale
  `756-v1.png` with ChatGPT/an upscaler to a square power-of-two (art step); (2) drop it at
  `tools/walls/source/756-v1.png` (gitignored raw source); (3) run ONE of the two make-tileable
  methods. Both offset the upscale so the wrap seam becomes a centre cross, then fix that cross while
  keeping the outer border seamless, and each writes its own 2x2 preview + a wrap-seam **ratio to the
  texture's own interior adjacency** (1.0 = seamless, >>1 = broken) so you can A/B them:
  **(A) BLEND** (`build_wall_tileable.py`, default; offline, no model) heals the seam with the mars
  stitcher's Laplacian `pyr_blend` (`tools/mars/stitch_lib.py` -- the "similar toolchain as mars" the
  card asked for): keep a pure-`B` seamless frame at all four edges, multiband cross-fade the
  transition. Deterministic, but it RELOCATES edge content (blends existing pixels, can faintly
  ghost). `preview_blend_756-v1.png`.
  **(B) INFILL** (higher quality) masks the seam cross and lets a LOCAL inpainting model REGENERATE
  new detail across it -- no ghosting. ChatGPT can't (it regenerates the whole frame + breaks the
  borders); needs a real inpainter that preserves unmasked pixels (**Flux Fill**/SD-inpaint). Flow:
  `--emit-seam` (writes `seam/756-v1_offset.png` + `_mask.png`) -> run the model -> `--reimport out.png`,
  which composites the fill **inside the mask only** over the offset so the wrap borders stay
  pixel-exact (tiling guaranteed). `tools/walls/flux_infill.py` is a one-shot Flux Fill runner
  (`FluxFillPipeline`); its pipeline call is **NOT run/verified here** (needs a GPU + gated weights) --
  the seam/composite/install plumbing it shares with `build_wall_tileable.py` IS verified.
  `preview_infill_756-v1.png`. Only `756-v1` is the collidable wall (8x8
  grid-sampled); the other `756-v*` are single whole-tile Base *background* layers in
  `Background.cs` (a different use whose tiling needs weren't verified -- out of scope). If Level-3
  preload stutters on a big new PNG, add
  `756-v1` to `textures.config` for DXT (mult-of-8 dims already satisfy the mult-of-4 rule). See
  `tools/walls/README.md`.
- **Level-3 walls are REAL 3D TOWERS rising out of the fog (`Wall.DrawTowerShafts3D`, cards d59266cc +
  a66fc73e, `plans/walls-3d-towers.md` + `plans/spike-wall3d.md`).**
  Each collidable block is extruded DOWNWARD into a shaft standing on the alien-base ground; the
  gameplay plane (ship + collision) stays the tower TOPS, and the top-face pass + `CollisionLevelMap`
  are byte-identical to the flat original. Vanishing point at design centre (400,300), base rect =
  `VP + (topRect - VP) * 0.66`. **The 0.66 is not a taste knob** -- it is exactly the alien-base GROUND
  layer's `scrollspeedmodifier` (`Background.SetAlienBase`), so a base projected at 0.66 moves at 0.66x
  the wall's speed = the floor's speed and the towers stay glued to the scrolling ground for free.
  Change it and the bases slide. Bases project TOWARD the VP, so towers lean away from screen centre
  (the GTA1 look) and a wall entering the top shows its bases before its top faces -- that's the
  emergence, no special-case animation.
  **The side faces are GENUINE 3D GEOMETRY in ONE batched draw** (`SpriteBatchWrapper.DrawGeometry3D`
  -> a single `DrawUserIndexedPrimitives` through a shared `BasicEffect`), NOT the stacked sprite
  slices this started as. The old "3D is unviable on WebGL, see `Quad.cs`" reading was wrong: that
  comment describes THREE immediate-mode draws PER BEAM, each forcing a leading `SpriteBatch` flush --
  a batching pathology. BlazorGL creates and destroys a transient vertex + index buffer per CALL, so
  the overhead is **per-call, not per-vertex**, and one call per wall is exactly the shape that path
  wants. Measured (focused tab, interleaved to defeat scroll drift): **towers now cost ~0.4 ms/tick over
  the flat baseline, down from ~3.8 ms** -- about a 10x cut. `BasicEffect` is real on BlazorGL (KNI
  embeds `Resources.BasicEffect.fxo` in `Kni.Platform.dll`), so no bespoke `.fx` -- and no hand-written
  vertex shader, which this project has never needed -- is involved.
  **The geometry is 3D rather than pre-projected** because flat pre-projected quads lose `w` and give
  affine (PS1-style) texture warp; emitting real boxes lets the GPU do the perspective divide, so the
  side faces sample the REAL `756-v1` cell with correct UVs. The camera (`View`/`Projection`) is built
  to reproduce `Wall.Project()` exactly -- eye at the VP, `z=0` the gameplay plane, `z=ZAtDepth(depth)`
  the ground -- and `tools/walls/preview_wall3d.py` asserts that to ~1e-13 px.
  **NO DEPTH BUFFER, and that is proved rather than lucky.** `sceneTarget` is `DepthFormat.None`. The
  shafts are equal-height vertical boxes on a ground plane under a perspective camera at the VP, so in
  polar coordinates about the VP a face's depth at radius `r` is `r / r0` (`r0` = its near edge). Two
  blocks sharing a ray can never interleave -- the one whose near edge is closer to the VP wins at every
  shared radius -- so the occludes relation is ACYCLIC and a CPU painter's sort by distance from the VP
  is EXACT. `tools/walls/verify_tower_order.py` certifies this over the real `level3.txt` and every
  `Wall.Setup` width (14k+ overlapping face pairs) and REJECTS two plausible-looking sort keys, so it
  isn't passing vacuously. Top faces sit at depth 1 (the maximum), so drawing them last stays correct.
  **Three things that are load-bearing:** (1) the shaft row cull is WIDER than the top-face loop's
  (`RowShaftVisible`), because a block off the bottom still shows its base and one above the screen
  already does; (2) a face is emitted only when it is BOTH an outer edge (`isfree` -- a side shared with
  a neighbour is interior to the solid, two coincident quads that shouldn't exist) AND turned toward the
  eye; (3) **UV orientation is what kills the seams, on both axes.** Blocks step through the sheet as
  (u -> columns, v -> rows), so a face's ALONG-EDGE coordinate must follow the axis its edge runs along
  (a vertical edge spans rows -> `v`); get it backwards and two stacked blocks' coplanar walls each
  restart the same range, hard-seaming every block boundary. And the DOWN-THE-SHAFT coordinate must
  START at the cell edge the wall hangs from, so the sheet folds over the top face's rim -- hence the
  down range reverses between the west wall and the east one. **No half-texel inset:** adjacent atlas
  cells ARE the correct continuation (row `i`'s `v1` is row `i+1`'s `v0`), so insetting re-opens the seam
  it means to close.
  **Unloading is DEFERRED past the bottom edge (`Wall.DeathY`).** A block's base projects TOWARD the VP,
  so a block below the VP has its shaft drawn ABOVE its cap -- when the last cap crosses y=600 the towers
  are still on screen, and the old `Position.Y > 600` `Die()` popped them out of existence. The last thing
  to leave is the base of the TOPMOST row, so the wall dies at `VanishY + (600 - VanishY)/depth` (= 754.5
  at depth 0.66; collapses to 600 at depth 1, and IS 600 with `?walltowers=0`). **This also delays
  `Walls.wall_OnDeath` -> `Terminate()`, i.e. the level's NEXT EVENT**, by the ~154 design px of extra
  scroll (~0.6s at Level 3's `4.3/16.667` px/ms wall-section speed). Intended: the section isn't over until
  its towers have gone.
  **Spawning is ADVANCED above the top edge (`Wall.EntryLead`) -- the mirror image.** The flat-era spawn
  (`Position.Y = -rowH*height`) only hides the TOP faces; a block's projected base leads its cap by
  `VanishY*(1/depth - 1)` (~154.5px) of scroll, so a grid with blocks in its bottom row(s) materialised
  its towers ~100-155px INTO the screen on the spawn frame (the "towers pop in as the section scrolls in"
  bug -- only bottom-row-occupied grids, hence "sometimes"; variation 3's lone corner block was the
  reliable single-pillar repro). `Setup`/`SetupFromFile` now spawn `EntryLead()` higher, so towers enter
  base-first through the edge; 0 with `?walltowers=0`, so the flat path spawns exactly as the original.
  Entry and exit are now symmetric (~0.6s each way at walls-section scroll).
  **Wall grid files load via `TitleContainer.OpenStream` (`Wall.OpenLevelGrid`, `Content/levels/...`),
  never `new StreamReader(path)`** -- a plain file read hits the WASM in-memory FS, which never contains
  wwwroot content (it's HTTP-only), so it throws on web. Consequence fixed in passing: variation 2
  (`level3.txt`, used by **OwnLevel**) had silently fallen back to its hard-coded 5x19 grid since the
  port began; it now renders the real committed `Content/levels/level3.txt`.
  **Entry diagnostics:** `?walltrace` logs each wall's spawn / first-shaft / first-top-face (posY +
  quad counts) and flags any block whose shaft starts or stops drawing while fully mid-screen
  (`POP IN`/`POP OUT` -- scroll only moves geometry through the edges, so any hit means a cull/spawn
  assumption broke). `?level=Level3&wallpoptest` chains ten SMALL grid-file sections
  (`Content/levels/poptest0..9.txt`, `Level3.PopulateWallPopTest`) and drops the scroll to ~10% once the
  second loads, so every entry is slow and unmistakable. Both are opt-in and OUT of `DebugFlags.Active`.
  **The haze is REAL DISTANCE FOG** (`BasicEffect.FogEnabled`, keyed on eye distance `e/d`; `?wallfog`
  baked **0.55**, fogging toward the measured FLOOR colour RGB(46,125,201) -- fog LERPS, so the old
  bright `DefaultFogColor` overshot and LIT the base up; the floor colour is darker than the shaft so
  the base recedes into it), which is
  something only real geometry can have: a sprite `Color` tint MULTIPLIES, so the slice path could only
  ever scale the wall texture down -- never paint it UP to a haze colour -- and had to lean on a bright
  `DefaultFogColor` plus the alpha dissolve to sell the fade. Fog LERPS toward the colour, so the base
  genuinely converges on it, and the fog factor is linear in world z so interpolating it is exact (more
  `?wall3dbands` does NOT smooth the fog -- the bands only resolve the smoothstep bottom dissolve, which
  rides per-vertex alpha and takes COVERAGE to zero so the shaft melts into the floor art). Fog touches
  rgb only, so the dissolve survives it. **Per-face shading** (`?wallfacelight`, baked 0.35; `?wallfaceangle` baked 140)
  is now just each quad's flat vertex colour -- real geometry knows which wall it is. The slice path had
  to fake this with a dedicated pixel shader reading a per-sprite face mask, which is what mitred a dark
  wedge into every interior corner; that shader, `FaceShadeEffect`, `Wall.FaceMask`, the `756-v1-side.png`
  companion sheet and `tools/walls/build_wall_side.py` are all **deleted** -- their logic is preserved in
  commit `906f344` ("Level-3 wall towers: feel pass") if it is ever wanted.
  Drifting fog **wisps** (additive `2331-v5`, the same texture + blend the two background fog layers use)
  draw BETWEEN the shafts and the crisp top faces; additive can't occlude, but fog over a dark object IS a
  brightening and the bright tops draw after, so it reads right. They tile **by position, never by a
  drifting source rect** (the batch begins with a null samplerState = LinearClamp, so an out-of-bounds
  source window clamps instead of wrapping), phase = `Position.Y * 0.8`, and their alpha is gated on the
  visible-block count so the screen-wide haze can't pop in with the `Wall` entity's spawn/death.
  **Tuning:** `?walltowers=0` is the kill switch (reproduces the old flat look exactly) ·
  `?walldepth= ?wallfog= ?wallfogcolor=<rrggbb> ?wallsidedark= ?wallfacelight= ?wallfaceangle=
  ?walltoplift= ?wall3dbands= ?wallwisps= ?wallwispspeed=` (all null => the baked `Wall.Default*` consts,
  so a plain boot is unchanged) · **`?level=Level3&wallsonly`** fast-boots a looping walls section
  (mirrors `?spiderboss`; pair with `?invuln`), and shows a **live `eaWalls` slider panel** (`index.html`,
  outside `#app`, also on a bare `?walltune`) driving `DebugInput.SetWalls` -> `DebugFlags.SetWallsOverride`,
  read every Draw; the orange readout prints the bake-ready query string. `eaWalls(...)` works from the
  console. **`?walltoplift=<f>`** (baked **0**) draws the tower TOPS at depth `1 + lift` so the caps sit
  proud of the gameplay plane; COSMETIC ONLY -- `CollisionLevelMap` still uses the unprojected block rects,
  so a lift drifts the sprite off its own hitbox by `lift * distance-from-VP` (~8 design px at a screen
  corner for lift 0.02). Keep it small and check with `?hitboxes`.
  **Cost meter:** `eaWallPerf(true)` arms `Compat/WallProfiler`; the `eaWalls` panel polls `eaWallStats()`
  ~4x/sec (a per-frame interop call would cost more than the thing it measures) and prints fps, frame ms +
  p95, and the tower-pass ms. **Verify the drawing OFFLINE, not with a live screenshot** -- the wall
  scrolls, and the canvas is black whenever its tab is backgrounded (Chrome won't composite the WebGL
  surface, though `setTimeout` keeps the loop ticking). `tools/walls/preview_wall3d.py` re-implements the
  exact projection + shading in numpy/Pillow against the real PNGs and writes a contact sheet. **And when
  you DO measure frame cost, the tab must be FOCUSED** -- Chrome throttles a background tab (an unfocused
  read said 14.2 ms/tick where the focused one said 6.2), and FPS alone tells you nothing because it is
  vsync-capped. Note `scheduleTick` uses `rAF` when visible and `setTimeout` when hidden, so a frame queued
  via `rAF` just before the tab hides never fires and the loop parks until it is visible again.
- **Menu art is warmed DURING THE SPLASH to kill the level->menu pop-in.** `Game1.QueueMenuWarm()` (end
  of `LoadContent`) decodes the menu's heavy PNGs (`planet`, `title-revenged`, + the rest) ONCE so the
  first menu show -- and especially the cold end-of-level credits->menu handoff (which never displayed
  the menu before) -- appears in a single frame instead of revealing in ~0.5s stages as each uncached
  MB-scale PNG decodes mid-transition on the WASM main thread. The menu scenes
  (`MenuScene`/`MenuSub1`/`MenuSubWithSkull`) all load through ONE shared content manager (`Scene.Content`
  == `IContentManagerService.ContentManager` == `Game1.content`), so warming that one instance populates
  the exact cache keys their `Load()` calls hit (same idea as a level's `PreloadGraphicalContent`).
  (`CreditsScene` uses its OWN content manager, so its bg isn't warmed -- but the crawl fades its bg in,
  so a cold decode there isn't the jarring part.) **The warm no longer blocks `LoadContent`** (which
  lengthened the black loading screen BEFORE the first splash, while the multi-second splash sequence --
  the natural place to hide loading -- sat idle): `QueueMenuWarm` ENQUEUES the decodes and `PumpWarmQueue`
  (in `UpdateInner`) drains ONE per Update tick during the splash / Press-Start idle time, so the splash
  appears sooner and the warm hides behind it. The "menu fully warm before first shown" invariant is
  preserved on every path by `DrainWarmQueue()` at the top of `startScreen_OnFinished` (the instant before
  `new MenuScene`): if a player mashes past the whole splash before the pump finishes, the drain decodes
  the rest synchronously (worst case == the old blocking batch). Pairs with skipping the brag interstitial: on web `BragScene` is
  always immediately `Done` (no signed-in gamer), so `Game1.creditsScene_OnFinished` checks
  `BragScene.WouldShow()` and routes credits -> menu directly instead of flashing one bare starfield frame.
  **A second, LOW-priority warm queue (`idleWarmQueue` / `QueueIdleWarm`) warms the space-background
  tile set** (`gfx/game/space/space00..11` + `star00..07` + the `starwindow` effect): `Background.SetSpace()`
  loads those synchronously inside a level's `Initialize()` -- BEFORE the level's preload bracket -- so
  neither `PreloadGraphicalContent` nor the manifest can ever warm them first; a one-time boot warm makes
  every `SetSpace` a session-long cache hit instead of ~0.5s extra on the first space level's loading tick.
  `PumpWarmQueue` drains it only after the menu queue empties, and `DrainWarmQueue` deliberately does NOT
  touch it (the menu must never wait on background tiles; a level entered before it drains just decodes
  the leftovers in `SetSpace` as before -- either order is safe). Put menu-critical art in `QueueMenuWarm`,
  pre-level art in `QueueIdleWarm`.
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
- **SpiderBoss "helper mothership" assist (`Game/EvilAliens/SpiderHelperMothership.cs`).** The Level2
  spider boss can ONLY be hurt by a `Lazer`, and in normal play the only lazers around are the big
  UFOs' player-aimed shots -- a very obscure "lure a lazer across the boss" mechanic. To make it
  legible, when the boss goes un-damaged for the helper-idle threshold -- counted from the boss's
  FIRST landing (not spawn, so the intro fly-bys don't count), and DIFFICULTY-SCALED by
  `SpiderBoss.EffectiveHelperIdleMs` (Easy ~6s, Medium ~15s, Hard ~23s, Very_Hard 30s, Inzane ~37s;
  `?spiderhelperidle` overrides raw) -- a mothership
  (mothershipA/B, same sprite as `Boss`/`MarsBoss`) EASES in from the left showing only its underside
  at the top (`hoverY ~10`), halts dead-centre, WINDS UP a converging spark swarm (a `LazerGenerator`,
  same effect + params a medium UFO uses to charge its laser in `UFOState.lazor`) for
  `SpiderHelperWindupSeconds` (default 2.5), fires a `Lazer` (it hits the boss on a fly-by via the
  normal Lazer->SpiderBoss path), then EASES out east (accelerating from rest, exits right). **Movement
  speed mirrors the twin "2 motherships" (`MarsBoss`), but the curves are gentler:** enter is a quad
  ease-OUT-to-rest (`1-(1-t)^2`, zero arrival velocity -- NOT MarsBoss's sqrt `PowerCurve` whoosh, which
  arrives still moving), leave is a quad ease-IN (`t^2` from rest); at a DIFFICULTY-SCALED fraction of their ~0.75 px/ms
  traverse speed -- `SpeedFrac = -0.0477 + 0.7077*DifficultyModifier` => Easy ~0.20 (1/5), Medium ~0.38
  (~1/3), Very_Hard 0.66, Inzane ~0.80; `?spiderhelperspeed` overrides with a raw px/ms value (default
  now unset = difficulty-scaled). **The laser's own descent speed is already difficulty-scaled** inside
  `Lazer.Update` (`growthspeed * DifficultyModifier`) -- nothing helper-specific. **AIM (Easy/Medium
  only):** at fire, if the boss is STANDING (`SpiderBoss.IsFlyingAround()` false) the beam is aimed AT
  it (`SpiderBoss.GetAimPoint()` = the standing hitbox centre) so it lands a direct hit; while the boss
  flies around, or on Hard+, the beam just goes straight down and a fly-by crosses it. It is **"fake
  killable"**: enormous hitpoints so it never dies in time, flashes (blink) + reddens (a separate
  `fakeHits` ramp, since real HP barely moves) like it's taking damage. The trigger lives in
  `SpiderBoss.Update` (idle `helpTimer`, reset on every landed hit + on spawn; one helper at a time via
  the `helper` ref + `OnDeath`). `Bullet.cs` lists it so bullets stop on it but do NOT sustain combo
  (it's immortal -- would be a combo farm). **Perfectly-vertical Lazers are now SAFE (card 7a3e70ad).**
  `CollisionHandler.FillCollisionMatrixLine` rasterises a `CollisionLine` cell-by-cell with a DDA whose
  near-vertical branch advances `val.X` by `80*cos(angle)` per step while its loop exits on `val.X`. For
  straight-down (`MathHelper.PiOver2`, `cos ~= -4.4e-8`) at x~400 that per-step delta is *below the
  float32 ULP* (~6e-5), so `val.X` never changed and the loop spun forever -- a hard 100%-CPU hang
  (reproduced in float32, confirmed live). It was worked around by tilting the helper's beam ~1.1 deg off
  vertical (`FireTilt`); that per-lazer band-aid is now REMOVED because the DDA itself is hardened: every
  DDA `while` loop is bounded by a `maxLineSteps` cap (a straight line touches at most `squaresX+squaresY`
  cells, so the cap only ever trips on a degenerate near-axis-aligned line, which still marks its correct
  column/row of cells first). So the helper fires exactly `PiOver2` again and no near-axis-aligned lazer
  (Boss/MarsBoss/UFO aimed nearly straight at a target) can hang the game. Don't reintroduce a per-lazer
  tilt. **Debug/tuning
  (`DebugFlags`):** `?spiderhelperidle=<sec>` `?spiderhelperhovery=<y>` `?spiderhelperspeed=<f>` (raw
  px/ms override; unset = difficulty-scaled) `?spiderhelperwindup=<sec>` `?spiderhelperenterpower=<p>`
  `?spiderhelperfire=<sec>` `?spiderhelperlead=<px>` tune the feel live (all have shipping defaults, so a
  plain boot is unchanged). **Sprite harness:** `?harness=spiderhelper` (use `?pos=400,10` to preview the
  in-game half-visible framing). **Fast test boot:** `?level=Level2&spiderboss` jumps straight into the
  spider-boss fight (skips the whole level, like `?win`); pair with `?invuln&spiderhelperidle=3` to watch
  the assist in seconds. See `Level2.PopulateSpiderBossOnly`.

- **3D model -> sprite-sheet pipeline for the big bosses (`tools/models/build_models.py`).** The big
  UFO / mothership / spider boss draw from 2D sheets at their original render resolution (the ceiling);
  this OFFLINE tool re-renders a boss from a supplied **3D model** at any supersample factor and emits a
  crisper drop-in sheet. Scope is **static hero pose OR N-angle turntable** (an online image-to-3D mesh
  has no rig, so it can't reproduce the multi-pose gameplay animations -- those stay hand-made). Renderer
  = **Blender headless** (`blender -b -P blender_render.py`; `bpy` has no 3.12 wheel, so it shells out to
  a `blender` exe via `$BLENDER`/config/`PATH`) -- a heavy dev-box-only dep like `tools/textures`'
  `texconv`; CI just ships the committed PNGs. Config-driven (`models.config`, per object: source `.glb`,
  camera/light, `layout`, `design_*`, `supersample`); **inert until a model is dropped at
  `tools/models/source/<name>.glb`** (gitignored), safe in CI/fresh clones. Two output layouts, each with
  a supersample seam so the hi-res sheet keeps its on-screen size: **`grid`** (uniform cols x rows for
  `AlienDrawableGameComponent` -- mothership/UFO; wire via a `DesignFrameWidth` entry, the existing
  mechanism) and **`atlas`** (packed sheet + `.dat` for `AnimatedSprite` -- spider boss; `AnimatedSprite`
  now takes an optional **`supersample`** ctor arg, default 1 = no-op, that divides the draw scale --
  mirrors the `DesignFrameWidth` factor). `datfmt.py` writes the `.dat` byte-for-byte to `loadData`'s read
  order; `build_models.py --selftest` proves the pack + `.dat` round-trip without Blender. The ACTUAL model
  creation + per-object wiring is the user's manual work (its own "For me" card); `tools/models/README.md`
  is the how-to. Don't hand-edit the built `.png`/`.dat` -- re-run the tool.
- **Brain final-boss LIVE ANIMATED OVERLAY PATCHES (`Game/EvilAliens/BrainBossOverlays.cs` +
  `tools/brainanim/` + `Content/data/brainoverlays.json`).** The big-brain final boss (`BrainBoss`,
  `brainbosshd` -- Level3 + InsaneBossI) is ONE static 1448x1086 sprite, too big to animate whole. So a
  few SELECTED on-screen regions are animated offline with the LOCAL Wan 2.2 14B Lightning i2v model
  (via the `../animgen` ComfyUI plumbing) and composited back as small feathered sprite-sheet patches
  that track the boss. Shipped overlays: **`eye_reveal`** (fleshy folds part to reveal an orange eye
  that looks around, centre) and **`pods_flicker`** (the bottom blue mechanical pod cluster flickering).
  A third, `lens_right` (a blue iris on the right), was DROPPED -- it never stopped clashing with the
  static art around it; its region stays in `regions.json` for reference. **INVARIANT --
  never animate the top of the sprite:** the boss draws at design `(400,100)` with `textureScale
  ~1.703`, so texture rows `< ~373` (incl. the central mechanical eye) are ABOVE the top of the screen;
  every `regions.json` box has `ty0 >= ~400`. **Pipeline (`tools/brainanim/`, run with the AnimGen venv
  `C:/Programming/animgen/.venv/Scripts/python.exe`):** (1) `regions.json` = the crop boxes (texture px)
  + i2v prompts + seeds + per-region `fps`/`triggerAvgSeconds`/`negative`; (2) `gen_brain_anims.py` crops
  each region and runs it
  through `comfy_client.generate` as an OPEN-ENDED i2v (start = crop, no end frame -> the FLF template
  degrades to I2V; auto-launches ComfyUI with the safe TDR flags), extracting mp4 frames into
  `new_assets_raw/brainanim/<name>/` (gitignored); (3) `build_brain_overlays.py <name>...` TRIAGES motion
  (`--list` prints mean inter-frame diff + a border-drift metric so static duds / camera-drift takes are
  spotted), colour-matches each frame's border to the original crop (undo VAE colour drift), multiplies
  the edge feather by the **brain's own alpha** (so the patch dissolves into the art AND never leaks the
  sprite's green backdrop where a crop overhangs the ball), packs a squarest grid ->
  `wwwroot/Content/gfx/sprites/brainov_<name>.png`, and writes the manifest. **The game reads ONLY
  `Content/data/brainoverlays.json`** (LandedOffsets-style trim-safe `TitleContainer`+`JsonDocument`
  load; a region not packed there is simply not drawn; a missing/bad manifest -> static boss).
  `BrainBossOverlays.Draw` (called from `BrainBoss.Draw` AFTER `base.Draw`) pins each patch's on-screen
  footprint to its brain-texel crop (`texCenter`/`texW`/`texH`, reference 1448x1086), so it sits exactly
  over the region it was cut from and **pulses + moves with the boss** (uses the boss's `DrawScale` +
  `Position`), tints by the boss's live `color` (so patches redden in lockstep on low HP), **ping-pongs**
  for a seamless loop, and (unless `interpolate:false`) rides the frame-interpolation shader
  (`interpolateEffect`, same path as the animated Braineroid) so the low frame count still plays smooth.
  `eye_reveal` sets **`interpolate:false`** -- its discrete open/look/close reads better STEPPED than
  cross-faded (the tween morphs the eyeball); `pods_flicker` keeps interpolation for smooth light changes.
  It advances on DRAW time (cosmetic --
  unaffected by hit-stop, like the metal sheen). Straight (non-premultiplied) alpha throughout.
  **The overlays fade out WITH the boss on death:** `BrainBoss`'s `smallwaitafterasplosion` end state is a
  quick (~300ms) ALPHA fade of the boss `color` (a scale-down would bare the single sprite's hard
  rectangular edges now that brain+cables are ONE sprite), and the overlays draw with that same `color`,
  so they dissolve in lockstep.
  **A patch with `triggerAvgSeconds` does NOT loop** -- it rests on frame 0 (which IS the untouched crop,
  so it reads as the static art) and plays ONE ping-pong cycle whenever
  `RandomHelper.RandomFromAverage(1/triggerAvgSeconds, gameTime)` fires, skipping the roll mid-cycle so
  the mean GAP between animations is that many seconds. `eye_reveal` uses **15** (an eye that opens, looks
  around and closes now and then is a punctuation mark; on repeat it's wallpaper). Omit the key for a
  continuous loop (`pods_flicker`). The roll happens in Draw, so it consumes the shared
  `RandomHelper.Random` at frame rate -- fine today (the boss is cosmetic and the planned co-op is
  state-replicated, not lockstep), but a `Quad`-style private FX RNG is the move if that ever changes.
  **`triggerAvgSeconds` + `fps` + `blend` + `interpolate` are PLAYBACK knobs, not pixels:**
  `build_brain_overlays.py` re-syncs them from `regions.json` into every existing manifest entry on each
  run (`--sync` does only that), so they can be retuned long after the gitignored raw frames are gone.
  **Verify WITHOUT a browser:** `tools/brainanim/preview_ingame.py` composites the boss + overlays in the
  exact 800x600 player framing (mirrors the Draw math) -> `_ingame_contact.png` (static vs 4 phases) +
  `_ingame.gif`. Live: **`?harness=brainboss`** shows the full boss with the overlays ANIMATING (they
  advance on Draw, so the frozen harness still plays them). Both boss levels warm the sheets in
  `preload/manifest.txt` so they don't decode mid-fight. **To retune:** edit `regions.json` (box/prompt/
  seed/fps/trigger), re-run `gen_brain_anims.py <name>` then `build_brain_overlays.py <name>...` with ONLY
  the winners (that rebuilds their sheets + rewrites the manifest); **`--drop <name>`** removes an overlay
  (manifest entry + its `brainov_<name>.png` + its `preload/manifest.txt` lines) and **`--sync`** re-syncs
  playback knobs only. Don't
  hand-edit the sheets / manifest -- re-run the tools. If a big new sheet stutters at preload, add it to
  `textures.config` for DXT (a follow-up; PNG-at-preload is fine for the boss-only load screen).
  **GOTCHA -- the model ALWAYS invents a slow camera zoom; the build STABILISES it out.** Each patch is
  composited over the STATIC brain, so any whole-frame camera motion Wan adds (a slow push/pull + drift)
  reads as the patch sliding against the surrounding art -- the one artifact this pipeline can't tolerate.
  Two things attack it. (1) The NEGATIVE prompt: `gen_brain_anims.py` originally passed `negative=None`,
  leaving animgen's SHARED workflow template's baked negative in place -- written for a fighting-game
  character, it ENDS with **`"frozen, still image, static pose"`**, which on a locked-off shot of a
  barely-moving pod cluster fights the whole point (the cheapest way to not be a "still image" is to move
  the frame). Saying "locked static camera" LOUDER in the positive prompt never won that argument (it was
  already there). `DEFAULT_NEGATIVE` replaces it with explicit anti-camera-motion terms, keeping the
  template's quality terms and dropping the anti-stillness ones. But it only HELPS -- measured on
  `pods_flicker`, the template negative gave a **6.4%** zoom + ~4px drift; `DEFAULT_NEGATIVE` still **5.1%**
  + 2px. (2) The real fix is `build_brain_overlays.py`'s **`stabilize()`**: it fits each frame's uniform
  zoom+translation against frame 0 (a coarse-to-fine outer-band SSD fit, scored on the border so the
  intended interior flicker doesn't dominate) and warps it back, exactly as `colour_match` undoes VAE
  colour drift. Frame 0 is the untouched crop, so locking to it also nails the resting pose to the sprite
  underneath. The build prints `stabilised out X% zoom / Ypx shift` + the before/after border-drift.
  **`--list`'s border-drift number alone can't tell a zoom from edge flicker** -- to eyeball a take's
  camera move, fit a global scale+translation of each frame against frame 0.
- **Screenshot `.dat` blobs live in IndexedDB, the small save XML in localStorage (card a5145e9e).**
  The `eaSave` JS facade (`index.html`) routes by extension: `.dat` -> `window.eaSaveBlob` (IndexedDB
  store `eaweb_save/screenshots`, huge quota), else localStorage -- C# (`SaveInterop`/`StorageStub`)
  is backend-agnostic. IndexedDB is async but the game reads saves synchronously, so
  `eaSaveBlob.preload()` (awaited by `initRenderJS` BEFORE the first game tick, raced against a 3s
  timeout so a wedged open can't hang boot) pulls every blob into an in-memory map that `eaSave.load`
  merges; it also one-time-migrates any `.dat` older builds left in localStorage (deleted only after
  the IDB transaction commits). IndexedDB unavailable/slow -> `.dat` falls back to localStorage (the
  pre-split path, no data loss). Don't make C# talk to IndexedDB directly -- keep the routing in JS.
- **Level launches are gated by a PRE-LAUNCH manifest warm (card fe25712a).** A level's whole preload
  used to decode in ONE JS tick (seconds of blocked event loop -> Chrome "page unresponsive").
  `Game1.WarmThenLaunch` (every launch path: menu incl. attract demos via `MenuFinished`, and
  `?level=` via `LaunchLevelDirect`) decodes the level's `Content/preload/manifest.txt` texture set
  ONE per tick (`levelWarmQueue`/`PumpLevelWarm`) BEFORE the scene is Added, so the browser paints
  between decodes; the level's own `PreloadGraphicalContent`/`ApplyManifest` stay synchronous and
  become cache hits. The menu is frozen during the warm (`menuScene.Enabled=false` -- an un-frozen
  menu re-fires OnFinished every tick from its held FadeToGame state; `ComponentBin.Add` re-enables
  on return) and keeps drawing its faded frame. The warm is bracketed `BeginPreload`/`EndPreload` so
  the hitch watchdog stays quiet and `?loadlog` counts it as preload (two preload summary lines per
  level under `?loadlog` -- warm + residual -- is expected). A level with NO manifest entries
  launches synchronously (the old behaviour, self-healing); a still-hitching level is a manifest
  DATA gap -- fix by playing it with `?loadlog` + `eaPreloadExport()`, not by code.
  **A subtle loading indicator shows while the warm runs (card 02a96ff6):** `Game1.DrawLevelWarmIndicator`
  draws a breathing "LOADING" (menu font) over a row of three marching pulse dots (`blackPixel` squares),
  centred low on the 800x600 design frame, in `DrawInner` gated on `pendingLevelLaunch != null` -- so the
  held-black fade no longer reads as a frozen frame. Pure Draw-time cosmetic keyed off `gameTime` (no state,
  no content, no debug flag); it appears only while a warm is in flight and vanishes the instant the launch
  fires. It's on the same overlay seam as the `?hitboxes`/HideSafeArea draws (design-space via the wrapper).

## Don'ts
- Don't commit `bin/`/`obj/` or the raw 52 MB Xbox package (all `.gitignore`d).
- Don't re-run `tools/*.py` against `Game/` (regenerates it from scratch).
- Don't trust a screenshot for colours/blending while shaders are stubbed.
