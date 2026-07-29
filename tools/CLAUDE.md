# CLAUDE.md — tools/ (offline asset + codegen pipelines)

Everything here runs OFFLINE on the dev box; CI never runs these — it ships the committed outputs.
Two standing rules:

- **Never hand-edit a generated output** (`.mgfxo`, `.dds`/`.rtex`, packed sheets, `music.json`,
  loop points, `menufont.fnt`, favicons, ...). Re-run the owning tool.
- **Never re-run the codegen scripts** (`fix_apis*.py`, `fix_ctors.py`, `fixup_transforms.py`,
  `fix_quad.py`, ...) — they DERIVED `web/EvilAliensWeb/Game/` from `src_decompiled/` once and
  would clobber every hand edit since. Edit `Game/` directly. The one exception is
  `strip_il_comments.py` (it deleted the 4020 ILSpy `//IL_<hex>: ...` warning comments the
  decompile sprayed across `Game/`) — it self-guards on `'//IL_' in src`, which nothing matches
  any more, so re-running it is a verified no-op rather than a landmine.

Heavy dev-box-only deps (all fine to be absent in CI/fresh clones): `texconv.exe` (gitignored),
a `blender` exe, the `../animgen` ComfyUI venv, `pymusiclooper`, PyAV. Raw sources live in
gitignored dirs (`new_assets_raw/`, `tools/*/source/`); the committed wwwroot artifacts are the
products of record.

## Headless logic oracle -- `tools/sim/logic_probe/`

**A PURE static method in the game can be verified with no browser at all** (card e6927ef8).
`EvilAliensWeb.dll` is ordinary IL and its whole dependency closure (`nkast.*` + the BCL) sits
next to it in `web/EvilAliensWeb/bin/Debug/net8.0`, so a desktop `net8.0` exe can
`AssemblyLoadContext`-load it and invoke the method FOR REAL:

```sh
dotnet build web/EvilAliensWeb -c Debug
dotnet run --project tools/sim/logic_probe -- web/EvilAliensWeb/bin/Debug/net8.0
```

Exit 0 = all cases pass, 1 = a mismatch, 2 = the target could not be reflected (renamed/moved).

- **The point is that there is NO MIRROR.** The `tools/sim/*.py` sims re-implement a choreography
  in python and can drift from the C#; this calls the shipped method itself, so a green run is
  evidence about the real code. Prefer it for anything shaped like a DECISION (a seating rule, a
  predicate, a resolver) rather than a picture or a motion.
- **Limits, and they matter:** anything touching `ServiceHelper` / `Game` / `GraphicsDevice` /
  content throws or NREs here, and loading a type resolves its base types (a method on a scene
  class drags the XNA assemblies in -- fine, they are managed, but a static ctor doing engine work
  would not be). It proves the FUNCTION, never the wiring: that a boot reads the flag, calls it and
  acts on the result still needs a live pass.
- **Hold it to the same standard as the IL oracle: sound AND sensitive.** Add a case set as a
  `Probe*` method, keep the expectation independent of the implementation where you can, and where
  a restatement is unavoidable add a negative control that runs the OLD behaviour over the same
  inputs and must FAIL. The TeamChallenge set was mutation-tested (`padConnected(i)` -> `true`
  turned 7 PASS lines into 4 FAIL), which is what makes its green run mean anything.
- **Second case set: the `?flyspider*` value-carrying flags** (card 6eb8dc9e), driven through the
  real `DebugFlags.Parse` -- pure string -> static property, so it reaches here. A bench flag fails
  by producing a run that measures the DEFAULT path while being labelled as the variant under test,
  which no picture can show, so what is asserted is that a malformed value is rejected, is
  REPORTED, and that the "staying on ..." clause names the setting actually in force. Note the
  statics persist across `Parse` calls in one process exactly as they do across a repeated flag in
  one query, which is what makes that last property testable at all. Also mutation-tested:
  restoring the hardcoded `DefaultFlattenBoxHalf` in the message turns the box line FAIL while a
  prior `?flyspiderbox=250` is in force. It additionally pins the `IsOn`/`IsExplicitlyOff` truth
  table (that card reordered them), including the row that matters -- a BARE flag is ON but is NOT
  explicitly off, so `!IsOn` and `IsExplicitlyOff` are genuinely different predicates.
- The probe deliberately does NOT reference `web/EvilAliensWeb` (that project targets
  browser-wasm and cannot be a `ProjectReference` of a desktop exe), so nothing in `web/` knows it
  exists and CI -- which only publishes `web/EvilAliensWeb` -- is untouched.

## Headless desktop test host -- `tools/headless/` (`eahl`)

**Runs the WHOLE game with no browser, no dev server and no visible window, and writes PNG frames.**
It links the same `Game/**` + `Compat/**` sources into a desktop exe on the KNI **SDL2** backend,
reads content live from `wwwroot`, and steps `Update`/`Draw` at a fixed 60 Hz dt.

```sh
dotnet build tools/headless -c Debug
tools/headless/bin/Debug/net8.0/eahl.exe --flags "?level=Level1&invuln" --frames 400 --out shot.png
tools/headless/bin/Debug/net8.0/eahl.exe --repl        # step / shot / eval / info / quit
```

Full docs + the option list: `tools/headless/README.md`. The essentials:

- **`--flags` is the URL query verbatim** -- every `DebugFlags.cs` flag works unchanged. So the
  sprite harness, the scrub/showcase scenes and the level fast-boots are all reachable without a
  browser, which is what makes this worth reaching for before `preview_start` + Chrome.
- **`eval` binds by reflection to `Compat/DebugInput.cs`'s public statics** -- the same curated
  surface the browser console exposes (`eaPress`, `eaAiBench`, `eaTexProbe`, `eaTeamSeat`, ...).
  There is NO mirror to drift: a method added there is callable here immediately.
- **`--nodraw` is ~1 ms/frame** (~17x real time); rendered is ~5.5 ms. An `eaAiBench.soak`-style
  run that needed a foregrounded tab can just run in the background here.
- **It does NOT replace the browser pass.** Trimming, IndexedDB saves, WebGL-specific shader
  behaviour, the `index.html` JS layer and real WebRTC only fail in Chrome. The `CONTRIBUTING.md`
  gate is unchanged -- this gets you to the frame/number faster, it is not the final smoke check.
- **GOTCHA -- a screenshot in the first ~2 s is a WHITE RECTANGLE and nothing is wrong.** Every
  scene that calls `Background.Reset()` (level entry AND `?harness=`/`?textshot`) starts in
  `LeavingHyperspace` with `fadeFactor = 0.998`, decaying at `0.0005/ms` = ~120 frames. Settle
  first (`--frames 150`, or `step 150 nodraw` in the REPL). The host prints a `NOTE` on a
  near-white frame inside that window; don't lean on it. This cost a full investigation once.
- Presenting the hidden window costs ~32 ms/frame for nothing, so `EndDraw` is skipped by default
  (`--present` restores it); the capture reads the back buffer BEFORE the swap either way, after
  bloom/post/gamma, so it is the finished frame.
- Audio is silent by default via OpenAL Soft's `null` backend -- no device is opened, so a box with
  no sound card runs too (`--audio` opts in). `--software` routes GL through Mesa llvmpipe for a
  machine with no GPU at all, and FAILS (exit 3) rather than quietly using the GPU.
- Same isolation rule as `logic_probe` below: nothing in `web/` references it, CI only publishes
  `web/EvilAliensWeb`, so the shipped build is untouched. Keep its `nkast.*` versions in lockstep
  with `web/EvilAliensWeb.csproj`.

## Refactor oracles — `verify_il_identical.py` / `verify_decompiled_diff.py`

Neither is codegen; both only build + inspect, so they are safe to run any number of times.

- **`verify_il_identical.py`** — the strong oracle: a cosmetic change must produce a byte-identical
  `EvilAliensWeb.dll`. Covers renames. Full rules in root `CLAUDE.md`. **`--optimize`** (card
  `0c624f9d`) additionally folds away dead stores and unused locals, which is what a refactor that
  DELETES a local needs — the default Debug build keeps every local for the debugger, so a deleted
  one changes the IL and the oracle would report DIFFERENT for a provably behaviour-preserving
  change. It is strictly WEAKER (it also hides differences a rename could never introduce), so use
  the default for a pure rename. Its own negative control: a clean tree plus one flipped constant
  reports DIFFERENT under `--optimize`.
  - **It does NOT fold a dead struct `initobj`, so MOST dead initializers are out of its reach**
    (card `cbdf0a6f`, measured over 5 sites). Roslyn has no dead-store elimination for `initobj`:
    it drops the initializer only when it eliminates the LOCAL ITSELF, which for
    `T x = default(T); (x) = expr;` means only the pure-return-temp shape (`...; return x;` --
    `AlienDrawableGameComponent.getFrameRectangle`, the positive control, commit `8bd1cf9`,
    IDENTICAL; that method's `Rectangle result = ...; return result;` local is
    deliberately LEFT standing so the control stays reproducible -- inlining it is the separate
    pure-return-temp class). Wherever the local SURVIVES -- even for one single use as a call argument -- its
    dead `initobj` survives with it and the hash reports DIFFERENT. It is neither the struct TYPE
    nor the constructor's ARGUMENT SHAPE: both hypotheses were tested and killed
    (`Rectangle`+flat folded; `Color`+flat, `Color`+nested and `Vector2`+flat all did not). That
    the store really is dead regardless is visible in the reference decompile -- `--optimize` had
    already inlined `FloatingText`'s construction into its call and STILL left
    `Color color = default(Color);` standing. So a dead-initializer sweep is bounded by
    `verify_decompiled_diff.py`, not proven by this oracle.
- **`verify_decompiled_diff.py`** — the companion for changes the compiler legitimately DOES see:
  collapsing `bool num = held; held = num | X;` to `held |= X` (the `ldloc` moves), or collapsing
  four `x.Position - y.Position` recomputations into one local (Roslyn cannot CSE a property call).
  It decompiles both assemblies and diffs the C#, reporting which members changed. The question it
  answers is "is the difference CONFINED to what I edited", not "is it identical".
  - **Read a raw IL diff of such a change at your peril** — deleting a local renumbers every later
    slot, so `diff` mispairs `ldloc.s 53` with `ldloc.s 51` and drags in untouched code (measured:
    317 bogus lines in `PlayerShip.DoAIMove`). Decompiling first makes slot numbers vanish.
    If you do diff IL directly, normalise `// Method begins at RVA 0x…` away first — removing code
    shifts every later method's RVA and otherwise reports thousands of false positives.
  - **GOTCHA — ILSpy normalises, so this tool can hide a real difference.** Both `|=` shapes
    decompile to the same C#, so that method simply does not appear in the report. An absent
    method means "ILSpy considers these the same construct", NOT "the IL is identical" — only
    `verify_il_identical.py` answers the latter. It also decompiles the dll IN PLACE so ILSpy can
    resolve references; copying it somewhere isolated first yields noisier unresolved-type output
    (`((GamePadState)(ref state)).Buttons`) with the transforms disabled.
  - Its per-member attribution is the whole contract ("a member you did not touch appearing here
    is the finding"), so it has **`--selftest`** (no build, no git — run it after touching the
    regexes). At least one modifier is REQUIRED in the member pattern: relax it and `else if (…)`
    parses as a member named `if`, and every later hunk files under it.

**Decompiler-artifact cleanup: what has been done, and what deliberately has not.** Card `0c624f9d`
collapsed ILSpy's `bool numN = held; held = numN | X;` pairs and the duplicated
`x.Position - y.Position` temporaries. It left the neighbouring
`GamePadButtons buttonsN = (state).Buttons;` temps in `InputHandler.UpdateKeyPads` alone — inlining
them renumbers the method's local slots, so the byte-identical hash oracle cannot cover it. Card
`7d14a3cd` did it anyway, BOUNDING it with `verify_decompiled_diff.py --ref main` instead, which
is the tool for exactly that class — and that came back IDENTICAL, i.e. not merely confined to the
edited method but invisible to ILSpy altogether. 24 temporaries inlined, the case-block braces
dropped with them, and that one method's redundant parens cleared in passing. Card `cbdf0a6f`
then FINISHED the struct-temporary class -- the last four `GamePadThumbSticks thumbSticksN =
(stateN).ThumbSticks;` temps in `InputHandler.LeftStick`/`RightStick` -- and collapsed the 30
provably-dead `= default(T)` initializers, both bounded the same way. Its `state`/`state2` locals
were deliberately KEPT -- though NOT for the reason the card gave ("separate calls with different
arguments"): WITHIN one method the two calls are identical, and they sit on mutually exclusive
`if`/`else` branches, so there is nothing to merge. The deadzone differs only BETWEEN the two
methods. Hoisting the call above the `if` to create something mergeable would MOVE a call site,
which is what stops it being cosmetic.

**Still deliberately not done.** ILSpy's redundant parenthesisation (`(delta).LengthSquared()`)
everywhere else in `Game/` -- its own artifact class and its own card; don't fold it into an
unrelated change. The other 39 `= default(` occurrences also stay, and NOT because nobody got to
them -- card `cbdf0a6f` classified all 69 and took only the 30 provably-dead ones (see the initobj
bullet above): **7 field-by-field inits** (`AnimatedSprite`, `BrainBoss`, `Floor`, `GameEventList`,
`MyMath`, `Vibrator`, `Wall`), where the default does definite-assignment work; **29 where the
assignment is CONDITIONAL or hoisted out of a loop**, so ADJACENCY cannot settle them and each
needs its own per-path analysis. Do NOT read that as "these are live": spot-checking says most are
not (`SpriteBatchWrapper`'s eight `zero` sites assign in BOTH the `if` and the `else`), while
`ComponentBin`'s `T val = default(T);` -- a search loop that may match nothing, then
`if (val != null)` -- genuinely is. That one is why the sweep refused to guess. Collapsible work,
not forbidden ground; and **3
non-declarations**, including `SpriteBatchWrapper`'s `Vector3 fogColor = default(Vector3)` DEFAULT
PARAMETER, which a naive `= default(` sweep would corrupt into a signature change.

## Shaders — `tools/shaders/`

The lost `.fx` were rewritten in `src/` and compile offline to MGFX v10 GLSL `.mgfxo` via
`build_shaders.py` (KNI's MGCB, BlazorGL target — needs
`nkast.Xna.Framework.Content.Pipeline.Builder.Windows 4.1.9001` in the nuget cache). **Re-run the
script after editing any `.fx`.** Pixel-shader-only effects (e.g. `holosim.fx`) build the same way.

## Audio — `tools/audio/`

- **`build_audio.py`** cracks the big-endian Xbox XACT banks in pure Python (`xact.py` parses
  `.xwb`/`.xsb`; PCM SFX + xWMA music via PyAV) → `wwwroot/Content/{sfx,vo}/*.wav`, `music/*.ogg` +
  `music/music.json`. Re-run after changing the banks or the ElevenLabs renders. Its `main()` also
  calls `install_external.install()` and `build_music` MERGES into `music.json`, so a full rebuild
  never drops an external cue (a missing raw source leaves the committed track untouched).
- **`refine_loops.py`** (called as `build_audio.py`'s last step; re-runnable standalone): XACT
  looped whole waves, but WebAudio's loop is a HARD SPLICE, so a mismatched wrap CLICKS. The script
  measures each track's splice click and replaces only audibly-clicking loops with a
  pymusiclooper-matched pair written into `music.json` (click-aware + idempotent +
  intro-preserving — won't pull `loopStart` before `introEnd`). Per-track hand-tunes go in its
  `OVERRIDES`; `--dry-run` previews.
- **`install_external.py`** owns the bespoke non-bank music cues (`classic` lyrics + `classicclean`
  instrumental + `lastsignal`): copies each raw OGG straight into `wwwroot/Content/music/` (no
  re-encode) and writes its `music.json` loop from pymusiclooper. `--dry-run`, `--cue <name>`,
  `--source <path>`. **Loop choice is click-aware, not just top-ranked:** it takes the
  best-scoring pymusiclooper pair whose `splice_click <= SEAMLESS (3.0)`, falling back to the
  least-clicky. **Don't trust a single `splice_click` reading to compare near-identical
  candidates** — it's a one-sample step that swings wildly under a ±20-sample shift; it's a coarse
  "does this wrap tick" screen. To genuinely compare two candidates, use a windowed RMS measure of
  the audio preceding `loopEnd` vs preceding `loopStart`.
- **`pick_channelswap.py`** owns the splash `channelswap` SFX: decodes the picked ElevenLabs render
  (committed source-of-record `channelswap_source.mp3`) to `Content/sfx/channelswap.wav` (mono
  16-bit 44100, peak-normalized 0.92 so cue-volume calibration holds). It SUPERSEDES the old numpy
  synth `build_channelswap.py` — **don't re-run that synth** (it clobbers the render). To swap the
  sound: `eleven_channelswap.py` to render candidates, then `pick_channelswap.py <slug>`.
- **`xact.py`'s mix parsers** (`parse_soundbank_meta`, `parse_xgs`, `cue_mix`) document the
  authored per-cue volumes/categories/RPC — they don't regenerate assets. The volume law is
  MonoGame's logistic `vol_to_linear` (mirrored by `SoundManager.VolToLinear`), NOT a linear
  byte estimate.

## Textures — `tools/textures/`

- **`build_textures.py`** reads `textures.config` and precompiles listed sprites to a GPU-ready
  sibling that `WebContentManager` prefers over the PNG: **`.dds`** (BC3/DXT5, lossy, ~0 decode;
  needs `texconv.exe`; dims padded up to a mult-of-4 with the logical size stamped in the header —
  Chrome/ANGLE→D3D11 rejects non-mult-of-4 block textures as black) or **`.rtex`**
  (uncompressed straight-alpha RGBA8, lossless, any dims). Rule of thumb: high-frequency detail
  hides BC3 artifacts (spider sheet, brain) → dxt; smooth gradients/glows band → raw. Re-run after
  editing a source PNG or the config. **The first 4 px of the pad are NOT transparent** —
  `edge_gutter()` replicates the logical edge there, because `LinearClamp` clamps at the texture
  border and not at the (correctly clamped) source rect, so bilinear still reaches one texel past
  it; a transparent texel there is a hairline seam on every tiled sprite (web CLAUDE.md, Trello
  `4ddcd13f`). Every entry needs a real source PNG — a stale line aborts the whole run.
  **A trailing `mip` on a `dxt` config line adds a full mip chain** (card `110153c7`; only
  `gfx/base/756-v1` takes it — a tower shaft spends ~10.8 cells of it, so its far end minifies
  hard and bilinear alone shimmers as the wall scrolls). It is opt-in because mipping all ~124
  `.dds` would cost ~33% more bytes and soften every minified sprite. **The chain is built PER
  LEVEL, not by `texconv -m 0`** — each level downsamples the LOGICAL image, pads *that* to the
  level's padded size and re-runs `edge_gutter()`, then the levels are compressed separately and
  spliced. Handing texconv the padded canvas instead filters the pad along WITH the content, so
  the levels blend real pixels into transparent pad near the logical edge: measured on 756-v1, a
  4 px gutter survives `log2(4)=2` levels and then fails hard (alpha delta 0/0/0 at levels 0–2,
  then 127/191/223 at 3/4/5). The full chain must be shipped — KNI allocates every level and GL
  needs a mipmap-COMPLETE texture, so a short chain renders black.
  **Rebuild one asset with `--only <glob>`** rather than rewriting all ~124 committed `.dds`.
  **Rebuild with `--padtest 100`, not the bare default.** The shipped `.dds` deliberately carry the
  over-pad canary (web CLAUDE.md, "The canary is LEFT ON"), but `--padtest` DEFAULTS TO 0 — so a
  plain `python tools/textures/build_textures.py` silently strips it off every texture it touches
  and the diff looks like a harmless size win. Check `git diff --stat` on `Content/gfx/**.dds`
  before committing a rebuild: shrinking files mean you dropped the canary.
- **`check_pad_bleed.py`** is the guard for that gutter: it decodes every shipped `.dds` and checks
  the texel just outside the logical edge still looks like the edge it replicates (alpha-weighted,
  each texel calibrated against the image's own local across-edge step, so BC3 noise doesn't cry
  wolf). `build_textures.py` **runs it automatically** and fails the build on a regression; run it
  by hand after anything else touches the `.dds`. It's a tolerance check, not a proof of equality —
  a pass means no logical edge has a step big enough to read as a seam. It flags 85 of the 103
  pre-fix assets; the rest had already-transparent edges with nothing to see, so a clean run does
  not mean every texture was rebuilt. It checks **every mip level**, not just level 0 (decoding a
  level by re-heading its blocks as a standalone single-level DDS, so Pillow's decoder is reused
  verbatim); that is what catches a chain built the naive `-m 0` way, which passes levels 0–2 and
  fails from level 3.
  **The calibration is per TEXEL, against the worst intrinsic step within `WINDOW` either side —
  never a whole-edge maximum**, which is what it used to be and which let one high-contrast spot on
  a long edge license a real gap everywhere else along it (it passed a 116/255 alpha discontinuity
  on pre-fix `eye_idle`). Widening `WINDOW` re-opens exactly that hole: it is variation ALONG the
  edge being used to excuse a step ACROSS it.
  **`FLOOR` is swept, not guessed, and mips are why it is 64.** It absorbs CROSS-BLOCK BC3 error,
  which the intrinsic reference under-reads (adjacent content texels usually share endpoints).
  Downsampling flattens the content, so at higher levels the reference collapses toward 0 while the
  compressor's error does not — 32 was clean on level 0 and false-positived `756-v1` levels 1–3.
  Worst legitimate step measured over all 124 assets at every level is 41; 64 clears it by 1.6x and
  still sits 1.8x under the smallest real bleed on record. **Re-sweep before touching it.**
  **At that floor the per-texel reference earns its keep DOWNWARD, not upward** — only one
  shipped edge steps past a flat 64 (`controls_keyboard` level 0, by 2/255), but the superseded
  rule handed every texel on a busy edge the full `HARD`, where this one gives a quiet stretch
  64. That 64–128 band is where a real gap on an otherwise noisy edge hides, so don't
  "simplify" the rule to a constant.
  **`--selftest`** pins the rule itself against synthetic edges (no `.dds`, no texconv): a
  replicated gutter passes, a transparent pad is flagged, the licensed-gap case above is flagged
  by the per-texel rule while the superseded whole-edge rule misses it, and a pair of cases puts
  the SAME above-floor step inside vs outside a busy patch's window (passes / flagged), which is
  what pins `SLACK` and `WINDOW` — widen `WINDOW` and that second case silently starts passing.
- **`build_texviewer.py`** builds the `?texviewer` comparison set into
  `wwwroot/Content/texviewer/` (`<asset>.dds` + `manifest.json`, both GITIGNORED — kept separate
  from shipped siblings so an undecided sprite is never auto-loaded). `--only <glob>`,
  `--dry-run`, `--manifest-only`. The in-game `?texviewer` scene's Save button writes
  `textures.config` lines via a dev-only `POST /api/texdecide` on `web/DevServer` (serve via
  DevServer or Save 404s); after saving decisions, re-run `build_textures.py`.
- **`build_brain_sheet.py`** builds the animated Braineroid: chroma-keys 81 magenta-backdrop
  AnimGen frames to straight alpha (reuses `chroma_key_title.py`'s decontaminate+edge-bleed + a
  connected-component pass), decimates to 20 frames, packs a 5×4 grid of 512px cells →
  `gfx/sprites/brainanimated.png` + a blurred blue glow `brainanimatedglow.png`. Sheet is dxt in
  `textures.config`; the glow stays raw. Re-run the script then `build_textures.py` after a new
  export.

## Font — `tools/font/`

`build_revenge_font.py` rebuilds `GFX/Menu/menufont` from `sources/*.png` with a **3× supersampled
atlas** while `Cropping`/kerning/`LineSpacing` stay design-size (see web CLAUDE.md — never stock
`DrawString`). Per-glyph capture-box/vertical-align/bearing tweaks live in `overrides.json`,
authored with the live editor (`editor/serve.py` after `--emit-editor`) and baked on `--commit`;
`_diag.py` prints per-glyph baseline offsets. Revert via the `*.orig` backups.

## Cursor — `tools/cursor/`

`build_cursor.py` emits the reticle ladder `wwwroot/reticle/<px>.png` for `SIZES = range(24, 97, 8)`
plus the intro sprite `Content/gfx/cursor2.png` (384px = 4× the largest rung). **Every image is
DRAWN at its native resolution, never resampled**, and the bars must run edge to edge (alpha bbox ==
full canvas) — padding breaks the sprite→cursor size handoff (see web CLAUDE.md). Keep the ladder's
step/min/max in sync with `MousePointer` if `SIZES` changes.

## Backgrounds / doodads

- **Earth (`tools/earth/build_earth.py`):** masks the NASA Blue Marble globe at full source res,
  cropped to the central vertical strip that can ever show (the hero earth is X-locked in game).
  `doodadscale` 0.6467 keeps the on-screen size; the script PRINTS the value to use if framing
  changes. `earth_small` is untouched.
- **Andromeda (`tools/nebula/build_nebula.py`):** normalises a raw HD galaxy
  (`source/andromeda.png`, gitignored) to straight-alpha RGBA — derives alpha from luminance if
  opaque-on-black, per-axis edge feather, long side capped 2048. Safe no-op if the source is
  missing. Knobs: `tools/nebula/README.md`. The game pins the on-screen width, so higher-res art
  needs no code change.
- **Mars hills (`tools/mars/build_marshills.py`):** synthesizes the three parallax ridge layers
  `marshills1/2/3` as circular-FFT (natively seamless-wrapping) fractal heightfields with aerial
  perspective; per-layer PNGs are STRAIGHT alpha. Knobs in the CONFIG block; `--seed`, `--preview`
  (2×-tiled seam check), `--show` (composite over the real sky). GOTCHAS when editing: the alpha
  accumulator is 0..1 vs RGB 0..255 (missing `*255` → invisible layer), and the OVER loop
  accumulates PREMULTIPLIED colour so the export MUST un-premultiply (else dark fringes on every
  feathered crest). The palette is MEASURED: ridge bodies must stay within ~a dozen levels of the
  horizon sky tone or they read stark. **Tune with the live editor** `tools/mars/editor/serve.py`
  (→ localhost:5299): real-generator re-render per drag, parallax-animated composite, "Write into
  game" saves the PNGs, and a paste-ready CONFIG block to bake back — bake + re-run once before
  committing so the tool reproduces the committed PNGs. Scroll speeds bake by hand into
  `Background.SetMars`'s `hillScrolls`.

## Level-3 walls — `tools/walls/`

- **Wall texture upscale (`build_wall_tileable.py`):** the collidable wall texture
  `GFX/Base/756-v1` is sampled as an 8×8 wrapping grid, so it must tile seamlessly on all four
  edges and keep dims a multiple of 8 (then no game code change). Flow: drop an upscaled square
  power-of-two at `source/756-v1.png`, then either **(A) BLEND** (default; Laplacian `pyr_blend`
  from `stitch_lib.py` heals the recentred seam — deterministic, can faintly ghost) or **(B)
  INFILL** (`--emit-seam` → run a real inpainter that preserves unmasked pixels (Flux
  Fill/SD-inpaint — ChatGPT can't; it regenerates the whole frame) → `--reimport out.png`,
  composited inside the mask only so tiling is guaranteed). Both write a 2×2 preview + a wrap-seam
  ratio (1.0 = seamless). `flux_infill.py` is a one-shot Flux Fill runner (pipeline call needs a
  GPU + gated weights; the seam/composite plumbing is verified). Only `756-v1` is grid-sampled;
  the other `756-v*` are whole-tile background layers (out of scope). See `tools/walls/README.md`.
- **`preview_wall3d.py`**: offline contact-sheet renderer that re-implements the 3D-tower
  projection + shading in numpy/Pillow against the real PNGs, and asserts the `BasicEffect` camera
  reproduces `Wall.Project()` to ~1e-13 px. This is how tower drawing changes are verified (the
  live wall scrolls; a backgrounded tab's canvas is black). `--mirror` reproduces the pre-card
  0f7fc977 side texturing (one cell, mirrored about the rim), `--tile <f>` previews a candidate
  `Wall.DefaultSideTile`, `--compare` writes the before/after A/B and `--ladder` one tower per
  tiling, bilinear-only on top and trilinear below — both opt-in, each roughly doubles the run.
  **Trilinear over the mip pyramid is now the DEFAULT** -- that is what the shipped mipped
  `756-v1.dds` gets, so a bare run models the real game; **`--nomips`** gives the pre-card
  bilinear-only look, named and polarised to match the game's own `?nomips` flag.
  Its LOD comes from screen-space UV derivatives, and those MUST be taken on the *unwrapped* cell
  walk: differencing after the `% 8` wrap steps a whole sheet at every crossing and would slam
  that pixel row to the coarsest level, which looks exactly like a seam.
  **`--shimmer` measures aliasing as a NUMBER** (mean per-pixel temporal stddev over a sub-pixel
  scroll sweep, per tiling, with and without mips). The card's complaint is a shimmer *under
  scroll*, which no still frame can show, so this is the honest read. Measured: bilinear worsens
  with density (4.15 / 6.26 / 8.21 / 9.93 at tile 1/2/4/8) while trilinear stays flat (~1.2-1.8),
  i.e. mips at the baked tile 4 beat bilinear at *any* tiling. **Score SHAFT pixels only** -- the
  tops are an axis-aligned blit that snaps to whole pixels, so they jitter by an equal,
  mode-independent amount that would dilute the measurement (`render(want_mask=True)`), and pass
  the SAME mask to both modes.
  Its `sample()` is BILINEAR CLAMP, modelling `DrawGeometry3D`'s `LinearClamp` exactly: point
  sampling would invent a moire the GPU does not show, wrapping would prettify the sheet's own
  8→0 wrap. `SIDE_TILE` mirrors `Wall.DefaultSideTile`; re-bake one, update the other.
- **`verify_tower_order.py`**: certifies the no-depth-buffer painter's sort is exact over the real
  grid files + every `Wall.Setup` width (and rejects two plausible wrong sort keys, so it isn't
  vacuous). Run it if the tower geometry/sort changes.

## 3D model → sprite-sheet — `tools/models/`

`build_models.py` re-renders a boss from a supplied `.glb` at any supersample factor and emits a
drop-in sheet — static hero pose or N-angle turntable only (no rig → gameplay animations stay
hand-made). Renderer = headless Blender (`$BLENDER`/config/`PATH`). Config-driven
(`models.config`); **inert until a model is dropped at `source/<name>.glb`** (gitignored). Layouts:
`grid` (uniform cells for `AlienDrawableGameComponent` — wire via a `DesignFrameWidth` entry) or
`atlas` (packed sheet + `.dat` for `AnimatedSprite`, whose optional `supersample` ctor arg divides
the draw scale). `datfmt.py` writes the `.dat` byte-exact; `--selftest` proves the pack + round-trip
without Blender. How-to: `tools/models/README.md`.

## BrainBoss overlay animation — `tools/brainanim/`

Run with the AnimGen venv (`C:/Programming/animgen/.venv/Scripts/python.exe`). Pipeline:
`regions.json` (crop boxes in texture px + i2v prompts/seeds + playback knobs) →
`gen_brain_anims.py` (crops each region, runs open-ended i2v through Wan 2.2 via
`comfy_client.generate`, extracts frames to gitignored `new_assets_raw/brainanim/`) →
`build_brain_overlays.py <names>` (triage with `--list`, colour-match borders, feather × the
brain's own alpha, pack → `gfx/sprites/brainov_<name>.png` + the manifest
`Content/data/brainoverlays.json`). `--drop <name>` removes an overlay everywhere; `--sync`
re-syncs playback knobs only (`triggerAvgSeconds`/`fps`/`blend`/`interpolate` are re-synced from
`regions.json` into the manifest on every run, so they're retunable after the raw frames are gone).

- **Region invariant: never animate the top of the sprite** — every box needs `ty0 >= ~400`
  (texture rows < ~373 are above the screen at the boss's draw position).
- **GOTCHA — the model ALWAYS invents a slow camera zoom; the build stabilises it out.**
  `DEFAULT_NEGATIVE` replaces the shared template's negative (whose "frozen, still image, static
  pose" terms actively fight a locked-off shot) but only helps; the real fix is
  `build_brain_overlays.py`'s `stabilize()` — fits each frame's uniform zoom+translation against
  frame 0 (outer-band SSD, coarse-to-fine) and warps it back. `--list`'s border-drift number alone
  can't tell a zoom from edge flicker.
- **Verify without a browser:** `preview_ingame.py` composites boss + overlays in the exact player
  framing → `_ingame_contact.png` + `_ingame.gif`. Live: `?harness=brainboss`.

## Webcam assets — `tools/webcam/`

`build_webcam_assets.py` builds the challenge's derived art (`heart.png`, the `webcamss`
level-select screenshot cropped from the meme splash). Don't hand-edit.

## Misc

- **`tools/favicon/build_favicon.py`**: builds `wwwroot/favicon.ico` (16/32/48/64) +
  `favicon-180.png` from frame 28 of the player saucer sheet on the near-black menu tile. There is
  deliberately NO `favicon.svg` link in `index.html` (browsers would prefer it).
- **`tools/sim/`**: isolation sims for verifying behaviour as data (e.g.
  `webcam_mothership_sim.py`, which mirrors `WebcamMothership.PoseAt`). The repo's preferred
  verification style — see the root CLAUDE.md rules.
- **`tools/sim/aiwallnav/`** (card b4972696): the one sim here that is NOT a mirror. A `net8.0`
  console app that references the BUILT `EvilAliensWeb.dll` and reflects into it, so it calls the
  real `PlayerShip.SteerThroughWall` / `ChooseGapColumn` / `ColumnScore` / `DistanceToBlockedRow` /
  `ClampIntoWallSpace` against the real `CollisionLevelMap` and the real `Wall.Setup` grids. Build
  the game first, then `dotnet run --project tools/sim/aiwallnav` (`--react=<ms>` writes the same
  `DebugFlags` property `?aireact` does, `--grid=<n>` picks one variation, `--ladder` repeats the
  table at all five difficulty scroll speeds). **This is possible only because the game targets
  plain `net8.0`** despite the BlazorWebAssembly SDK -- keep it a `Reference` to the built DLL,
  never a `ProjectReference`. It binds private members by name and REFUSES to start if one has
  been renamed, rather than printing a clean-looking table of nothing.
  **It drives the WALL TERM ONLY** and cannot produce `?aibench`'s `turn deg/s` / `revs/s` (those
  are the whole steering sum); a verdict about the bot still needs `eaAiBench.soak()`.
  Four rig facts that each bit during development, all detailed in its README:
  **(1) rebuild the game first** -- it benches the built DLL, so an unrebuilt `PlayerShip.cs` edit
  is measured in its OLD form, silently and plausibly (this published an inverted conclusion once);
  **(2)** variation 2 must be parsed from `level3.txt` by the bench, because `Wall.Setup` reads it
  through browser-only `TitleContainer` and otherwise returns its 5x19 emergency grid;
  **(3)** a death must respawn in a CLEAR cell -- a fixed respawn lands back inside the same slab,
  which pinned `contacts` at a flat 226 across four look-ahead depths, an artifact that read
  exactly like a result; **(4)** scroll speed is PINNED per table, never pooled -- run duration is
  `distance / scroll`, so averaging a sweep silently weights it onto the slowest rung.
- **`tools/xnb/unpack.py`**: unpacked the original content; emits decoded RGBA verbatim (straight
  alpha — the basis for the project-wide straight-alpha rule).
- **`tools/audit_add_order.py`**: lint for the ComponentBin instant-add contract (card 02d9ad67)
  — flags any `ComponentBin.Add` call site that still configures the object (Setup/Make*/property
  write) AFTER the Add; KNI runs `Initialize()` synchronously inside the Add, so config must come
  first. Run after adding spawn sites; exit 0 = clean. See web CLAUDE.md "Component lifecycle".
