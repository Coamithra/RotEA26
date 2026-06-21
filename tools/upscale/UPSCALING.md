# Upscaling sprites with AI — field guide

How we took 48px blurs and put crisp 4× sprites in the game (UFO + player ship done).
Read this before doing the next sprite; it's mostly *traps we already fell into*.

## TL;DR pipeline (animated grid sheet)

1. **Make the magenta input** — composite the original frames on `#FF00FF`, with a
   generous margin around each sprite. `new_assets_raw/<name>_magenta_padded.png`.
2. **Generate** — feed the *whole sheet* to ChatGPT/Gemini with a prompt that (a) says
   "redraw faithfully, same design/angle", (b) **forbids decorations** (stars, sparkles,
   banners, captions, glows), (c) **locks the framing** ("keep each sprite the same size
   in its cell, keep the wide magenta border, keep the NxM grid"), (d) keeps a flat
   `#FF00FF` background, (e) describes tiny details it can't see (e.g. "a small green
   alien pilot in the dome" — otherwise it hallucinates garbage there).
3. **Key + align + repack** — `python repack_for_engine.py <gen.png> <orig.png> <factor> <crop> <out.png>`
   (single-blob sprites like the UFO) or `repack_player_engine.py` (multi-part sprites).
   This keys the magenta, aligns each frame to the low-res original, footprint-matches
   the scale, and packs the engine grid (1px separators, straight alpha).
4. **Swap the asset** — back up (`cp -n x.png x.png.orig`), drop the HD sheet in.
5. **Register + draw fixes** — see "Engine integration" below.
6. **Build, restart server, verify in-browser** — see "Verification" below.

## Path A vs Path B (which AI)

- **Path A — super-resolution** (Real-ESRGAN, etc.): faithful, preserves motion exactly,
  but **hits a ceiling on small smooth sprites** — the anime model *fabricates jaggies* on
  curved silhouettes (a 48px saucer has no detail to reconstruct). Good only for sprites
  with a large, detailed *source*.
- **Path B — generative redraw** (ChatGPT / Gemini): **wins** for these sprites. Far
  crisper. Cost: it flattens *subtle* per-frame motion (rivet spin, dome pilot morph) and
  loves to add junk (see traps). We use Path B + heavy post-processing to recover registration.

## Registration (killing wobble) — three tiers, escalate as needed

1. **Overlap-to-original** (default, `repack_for_engine.py`): the low-res original is the
   *ground truth* for where each frame sits. Align each upscaled frame to its original by
   maximizing alpha-silhouette overlap (FFT cross-correlation). Inherits the authored motion.
2. **Feature-lock** (multi-part sprites): the full silhouette is too noisy when an interior
   element animates (the player's pulsing cross drags the centroid around). Anchor on the
   stable rigid sub-structure instead — e.g. the player's **dark panel rectangle** (constant
   size, so locking its center pins all four edges).
3. **Difference-minimization** (last sub-pixel bit, `repack_player_engine.py`): per frame,
   brute-force search (scale, dx, dy) that **minimizes the SAD vs the previous frame**
   ("Difference blend → sum → minimize"). MUST be sub-pixel AND include a tiny **scale**
   term (the residual drift is sub-pixel *scaling*, ~0.4%). MUST be brute-force grid — the
   difference surface is non-convex, small tweaks spike it. De-trend afterward (remove mean
   translation, normalize mean scale) so it stays centered over the loop.

The engine's `interpolateEffect` then blends frame N→N+1 in the shader, smoothing whatever
sub-pixel jank remains — but only because alignment made the frames *register* (else it ghosts).

## Engine integration — the size trap (important)

The engine sizes every sprite as **`frameTexels × scale`** (XNA `SpriteBatch.Draw` with
`origin = source.Size/2`). So a 4×-resolution frame renders **4× bigger** unless you
compensate. Fix = divide the draw scale by the factor. We did it centrally:

- `AlienDrawableGameComponent` has a `DesignFrameWidth` registry (`name -> original frame
  px`, both sheets are 48) and computes `textureScale = actualFrameWidth / designFrameWidth`.
  `DrawScale = scale / textureScale` is used in every draw + the collision box (which is
  also `frameTexels × scale`, so it auto-corrects). **Add the new sheet's name to that dict.**
- **Direct draws bypass the component** and need per-site fixes: anything that hardcodes
  `Rectangle(0,0,48,48)` (ScoreVisualiser lives, PowerupEffect particles) or computes a
  source rect + position from frame size (GammaMenu / ScreenResizeMenu UFO decorations).
  Grep for the texture name AND for `0, 0, 48`.
- **The interpolation offset is resolution-independent** (`frameStride / textureWidth` =
  `193/1543` = `49/391` = 0.125), so the interpolation path needs **zero** changes. Nice.
- **Repack footprint-matched**: align the gen sprite to `original_bbox × factor` so it fills
  the new frame like the original filled the 48px frame — otherwise it's the right resolution
  but the wrong on-screen *size*.

## Traps we actually fell into

- **Green vs magenta key**: the UFO dome is teal/green, so a green screen eats into the
  sprite. Use **magenta** (`#FF00FF`) — these sprites have no magenta. Key with proper
  chroma math (soft anti-aliased alpha + despill `R,B -= min(R,B)-G`), NOT blob masks.
- **`keep-largest-component` destroys multi-part sprites**: fine for the UFO (one blob),
  but the player is 4 orbs + a cross + a panel = 5 components. The magenta key already
  separates everything (every non-magenta pixel survives) — don't add blob logic on top.
- **Gemini/ChatGPT add junk**: stars, sparkles, "EVIL ALIENS 2026" banners, corner
  diamonds. Padding makes them land in the croppable margin; the prompt's "no decorations"
  stops most; auto-crop the banner; a stray-blob strip catches the rest (single-blob only).
- **Don't fix animation by frame-copying** — duplicating a neighbor frame stutters the spin.
- **Centroid centering wobbles** when an interior element animates — use bbox / feature-lock.
- **Browser HTTP cache serves STALE sprites** (the big one): after swapping a PNG the game
  kept loading the *old* one from cache — survived a fresh tab AND `Ctrl+Shift+R`. It was
  NOT a service worker and NOT the server (`curl localhost:5280/Content/...` sent the new
  file). Fix from the page: `await fetch(url, {cache:'reload'})` to force-replace the cache
  entry, then reload. **Deploy caveat:** returning visitors to the live site will see stale
  sprites the same way — wire in cache-busting (hashed filenames or `?v=` query) before
  shipping for real.

## Verification gotchas (dev server + screenshots)

- `preview_start` reuses a lingering `dotnet run` child on the port → serves the OLD build.
  Kill it: `netstat -ano | grep ':5280' | grep LISTENING` → `taskkill //F //PID <pid>`, then
  restart. Always restart after code changes (asset-only changes are served live).
- The screenshot tool waits for `document_idle`, which a live game loop never reaches **on a
  tab that's accumulated navigations**. Use a **fresh tab** (close + reopen) — that works.
- Boot a level directly with `?level=Level1&noattract` (UFOs visible) or `?level=ClassicAliens`
  (player centered). First load on a fresh server recompiles WASM (~20-40s) — wait it out.
- Confirm which texture the game actually loaded (not the cache):
  `new Image(); i.src='/Content/gfx/sprites/<x>.png'; await i.decode(); i.naturalWidth` —
  old = 391x195, new = 1543x771.

## Scripts (in tools/upscale/)

| script | does |
|---|---|
| `repack_for_engine.py` | single-blob: key → overlap-align to original → footprint repack |
| `repack_player_engine.py` | multi-part: key → panel-lock → difference-min → footprint repack |
| `align_to_original.py` | overlap-alignment GIF + overlap diagnostic |
| `align_difference.py` | difference-minimization GIF (the sub-pixel/scale pass) |
| `process_padded.py` | key + auto-crop banner + auto-strip strays + dome strip + GIF |
| `make_gifs.py` / `keycompare.py` | preview GIFs + the shared `key_magenta()` chroma keyer |
| `esrgan_test.py` | the Path-A experiment (kept for reference; not the chosen path) |

Heavy/scratch (gitignored): `models/` (ESRGAN weights, re-downloadable), `out/` (previews).
`*.png.orig` backups sit next to each swapped asset for easy revert.
