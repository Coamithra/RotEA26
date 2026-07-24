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
- **`check_pad_bleed.py`** is the guard for that gutter: it decodes every shipped `.dds` and
  asserts the texel just outside the logical edge still matches the edge (alpha-weighted, and
  calibrated per texture against its own column-to-column step, so BC3 noise doesn't cry wolf).
  **Run it after every `build_textures.py` rebuild** — a pass means bilinear at the logical edge is
  indistinguishable from a true clamp, so no pad-bleed seam is possible at any pad size. It checks
  **every mip level**, not just level 0 (decoding a level by re-heading its blocks as a standalone
  single-level DDS, so Pillow's decoder is reused verbatim); that is what catches a chain built the
  naive `-m 0` way, which passes levels 0–2 and fails from level 3.
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
  **`--mips` samples trilinear over a mip pyramid** (what the shipped mipped `756-v1.dds` gets);
  its LOD comes from screen-space UV derivatives, and those MUST be taken on the *unwrapped* cell
  walk: differencing after the `% 8` wrap steps a whole sheet at every crossing and would slam
  that pixel row to the coarsest level, which looks exactly like a seam.
  **`--shimmer` measures aliasing as a NUMBER** (mean per-pixel temporal stddev over a sub-pixel
  scroll sweep, per tiling, with and without mips). The card's complaint is a shimmer *under
  scroll*, which no still frame can show, so this is the honest read. Measured: bilinear worsens
  with density (4.55 / 5.57 / 6.56 / 7.38 at tile 1/2/4/8) while trilinear stays flat (~3.1-3.4),
  i.e. mips at the baked tile 4 beat bilinear at *any* tiling.
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
- **`tools/xnb/unpack.py`**: unpacked the original content; emits decoded RGBA verbatim (straight
  alpha — the basis for the project-wide straight-alpha rule).
- **`tools/audit_add_order.py`**: lint for the ComponentBin instant-add contract (card 02d9ad67)
  — flags any `ComponentBin.Add` call site that still configures the object (Setup/Make*/property
  write) AFTER the Add; KNI runs `Initialize()` synchronously inside the Add, so config must come
  first. Run after adding spawn sites; exit 0 = clean. See web CLAUDE.md "Component lifecycle".
