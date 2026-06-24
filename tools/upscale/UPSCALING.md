# Upscaling sprites with AI — field guide

How we took 48px blurs and put crisp 4× sprites in the game. Done so far: flying
`ufosheet`, `smallship`, `faceofdeath`, `deathstar`, `playersheet`, the `asteroid` set, the
`powerupbw` bubble, the awardment-screen `evilskull` decoration, the `spiderjump` soar +
`spider_sheet2` rear-up animations; landed
`ufometpootjes`, `Smallship_landed`, `Mediumship_landed`, `Mothership_landed`. Left
native BY CHOICE: the flying `mediumship` + `mothership` boss sheets (see "Choosing the
factor"). Read this before doing the next sprite; it's mostly *traps we already fell into*
— and **more sprites are still coming**, so keep this current.

## TL;DR pipeline (animated grid sheet)

1. **Make the magenta input** — composite the original frames on `#FF00FF`, with a
   generous margin around each sprite. `new_assets_raw/<name>_magenta_padded.png`.
2. **Generate** — feed the *whole sheet* to ChatGPT/Gemini with a prompt that (a) says
   "redraw faithfully, same design/angle", (b) **forbids decorations** (stars, sparkles,
   banners, captions, glows), (c) **locks the framing** ("keep each sprite the same size
   in its cell, keep the wide magenta border, keep the NxM grid"), (d) keeps a flat
   `#FF00FF` background, (e) describes tiny details it can't see (e.g. "a small green
   alien pilot in the dome" — otherwise it hallucinates garbage there).
3. **Key + align + repack** — `python repack_for_engine.py <gen.png> <orig.png> <factor> <crop> <out.png> [cols] [rows]`
   (single-blob sprites like the UFO) or `repack_player_engine.py` (multi-part sprites).
   This keys the magenta, aligns each frame to the low-res original, footprint-matches
   the scale, and packs the engine grid (1px separators, straight alpha). `cols`/`rows`
   default to `8 4` (`ufosheet`'s landscape grid); a **portrait** sheet (4 cols × 8 rows,
   e.g. `smallship` 195×391, or `mediumship`) needs `4 8` — check the original's
   `AnimationData(name, rows, cols, ...)` and dims, the script doesn't infer it.
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
| `repack_diffmin.py` | GENERALISED difference-min: key -> bbox-centre -> SAD-vs-prev sub-pixel align -> footprint repack. For morphing sheets with real interior features to lock onto (`faceofdeath`). Args incl. crop + cols/rows. NOT for a bare spinning sphere -- it wobbles; see `repack_circlecentre.py`. |
| `repack_circlecentre.py` | RADIALLY-SYMMETRIC sheets (a spinning sphere with a featureless circular silhouette, e.g. `deathstar`): key -> footprint-match by circle-fit DIAMETER -> least-squares circle-fit CENTRE (ignores both the rotating surface and the glow) -> pack. diffmin wobbles ~5px here (chases the surface), bbox-centre ~1.6px (glow pulls it); this pins to <0.3px. Optional 8th arg `circle`\|`bbox`. |
| `compare_methods.py` | render N method-sheets as a side-by-side animated GIF (fixed crop + crosshair, labelled with each method's wobble) + a centre-scatter proof PNG, so you eyeball which registration to ship. `python compare_methods.py <out> label=sheet.png ...` |
| `repack_landed.py` | single still frame: key -> match sprite WIDTH to origW*factor -> place at orig bbox-centre*factor in an origW*factor canvas (the landed stills + the `powerupbw` HD bubble). |
| `pack_small_asteroids.py` | slice a magenta GRID of variants -> key -> footprint-match each to origW*factor at a LOWER factor (for sprites drawn small). Built `AsteroidSmall1..4`. |
| `pack_anchored_anim.py` | FIXED-CAMERA frame folder -> ONE shared union-bbox crop (no per-frame jitter) -> key -> grid sheet. For anchored AnimGen anims (the spider rear-up). |
| `esrgan_test.py` | the Path-A experiment (kept for reference; not the chosen path) |

Heavy/scratch (gitignored): `models/` (ESRGAN weights, re-downloadable), `out/` (previews).
`*.png.orig` backups sit next to each swapped asset for easy revert.

## Method decision (which repack script) — pick by HOW it animates, not how it looks

- **Rigid single blob** (translates/rotates as one piece): `repack_for_engine.py`
  (overlap-to-original). Used: `ufosheet`, `smallship`.
- **Morphing / internally-animating WITH real interior features** (a face whose eyes+mouth
  change -- there IS interior structure to lock onto, but overlap-to-original is
  degenerate/wobbly): `repack_diffmin.py` (difference-minimisation — SAD vs previous frame,
  then de-trend). Used: `faceofdeath` (NOT `deathstar` -- see the bare-sphere bullet below). It's the generalised cousin of
  `repack_player_engine.py` (which is hardcoded to the player's dark-panel feature-lock);
  diffmin uses **bbox-centre** as the initial placement instead. Rule of thumb the owner uses:
  "needs the Difference method" == use `repack_diffmin.py` -- with ONE exception, below.
- **Radially-symmetric / bare spinning sphere** (a featureless circular silhouette -- the
  `deathstar`): `repack_circlecentre.py`, NOT diffmin. diffmin is WRONG here even though the
  sprite obviously "needs centring": SAD-vs-prev chases the rotating surface (a rotated radial
  pattern looks locally translated), so the sphere wanders ~5px and de-trend only removes the
  mean; bbox-centre is pulled ~1.6px by the moving glow. A least-squares circle fit on the
  silhouette EDGE ignores both the surface and the glow and pins the centre to <0.3px. The
  proof for deathstar is `compare_methods.py`'s centre-scatter (18.5px vs 7.7px vs 1.0px span).
  General rule when unsure which registration wins: render the candidates with
  `compare_methods.py` and eyeball the GIFs side-by-side rather than trusting a heuristic.
- **Single still frame** (a "landed" sprite, one frame): `repack_landed.py`.

Find grid + crop the SAME way every time: original dims / `AnimationData(name, rows, cols, ...)`
give the grid (pass `cols rows`; default `8 4`, portrait sheets are `4 8`); the crop is the
banner-top = first all-magenta-free row scanning UP from the bottom (the gens love to add an
"EVIL ALIENS ..." banner + a corner diamond — both land below the crop). If a gen has a BLACK
BORDER (added so the model stops clipping the sprite's sides), crop to the magenta FIELD's
bbox on ALL sides first -- the border isn't magenta, so the keyer leaves it as opaque junk and
the cell-slicer mis-aligns; and align the crop to the actual sprite-grid spacing, not the whole
field, since the field can have extra magenta margin (the deathstar redo).

## Choosing the factor (NOT always 4x)

Stage-10 presenter renders the whole 800x600 scene into a target up to 1440px tall, so EVERY
sprite is magnified up to ~2.4x at a large window. A sprite is crisp only if it has
>= designFootprint * 2.4 texels. So:
- **48px sprites -> 4x** (192 texels >> 48*2.4). Standard.
- **Large native sprites** (e.g. `mediumship` 216px, `mothership` 456px): they look fine at
  800x600 but the presenter STILL upscales them ~2.4x -> soft. To sharpen, supersample them
  too, but: (a) pick the factor so you don't UPSCALE the gen — target sprite width
  (origW*factor) must be <= the gen's sprite width, else you enlarge the AI output and it
  goes soft (`mothership_landed` gen was 1371px wide, so 4x=1652 would upscale -> used **3x**);
  (b) register with the sprite's NATIVE design width (216 / 456), NOT 48.
- **Sprites drawn at a SMALL scale need a LOWER factor.** On-screen footprint is
  `designWidth * drawScale`, so a sprite always drawn small only needs `designWidth *
  maxDrawScale * 2.4` texels -- pick the factor from THAT, not designWidth alone. The
  `asteroid` split is the worked example: the big level-opener (scale 3) keeps `Asteroid2` at
  **3x**; the normal asteroids (scale 0.45, ~80px on screen) were wasting a 537px sheet
  (2.7x oversampled), so they use `AsteroidSmall1..4` at **1.5x** (`pack_small_asteroids.py`)
  -- SAME registered design width (179) so on-screen size + collision are identical, ~half the
  texels. (The `powerupbw` bubble is the opposite end: 32px design, drawn ~scale 1-2, so **4x**.)
- The flying `mediumship` + `mothership` boss sheets are left native BY CHOICE (a big sheet
  gains little from a generative pass, and frame-by-frame redraw wrecks animation consistency).
  Their LANDED single-frame stills WERE upscaled (one frame, no consistency risk).

## Size-trap: registry + EVERY direct-draw site

`AlienDrawableGameComponent.DesignFrameWidth` maps `texture-name -> design frame width`;
LoadAnimation does `textureScale = actualFrameWidth/design`, `DrawScale = scale/textureScale`.
- Animated sheet: design = per-frame width (48 for the small sprites).
- Single-frame still: design = the WHOLE texture width (`ufometpootjes` 55, `Smallship_landed`
  48, `Mediumship_landed` 216, `Mothership_landed` 456) — the component sees columns=1.
- **Key must match the load string EXACTLY, including capital S** (`GFX/Sprites/Smallship_landed`,
  `GFX/Sprites/Mothership_landed`).
- Anything drawn through an `AlienDrawableGameComponent` subclass (UFO / EvilSkull / DeathStar /
  StarMine / StationaryBoss / ...) is auto-handled once registered.
- **Direct-draw sites bypass the component — must divide draw scale by `SuperSampleFactor`:**
  - `UFO.Draw` stationary branch (landed UFO stills): does
    `scale / SuperSampleFactor(stationarySpriteName, stationarySprite.Width)`.
  - `CastDisplayer` (the bestiary "CAST" screen): drew frames at actual texel size with NO
    compensation -> now divides by `SuperSampleFactor(texturename, frameWidth)` in its draw.
    (This also retro-fixed `ufosheet`/`playersheet`, which were 4x oversized there.)
  - `Spider.Draw` jump composite (body `spiderjump` + two `wing1` blades, drawn directly at
    scale 1): the HARDEST case. Each texture gets its own `SuperSampleFactor`; then (1) draw
    scale = `1f / f`; (2) the wing **rotation pivots** `(82,11)`/`(6,11)` are texture-pixel
    coords so scale them `* fWing`; (3) the wing POSITION is derived from the body size
    (`spiderJump.Width/2 - 69`) so use the body's DESIGN width `spiderJump.Width / fJump`.
    Result is provably identical to the original render (verify: footprint of each /f bbox
    matches the orig, so pivot at `(px*f)` lands on the same feature). Rule: any baked pixel
    offset / origin / rotation-pivot must scale with the texture; any size derived from a
    sibling texture must use that sibling's DESIGN size.
  - For any NEW sprite: grep its texture name across `Game/` and check for a hardcoded
    `Rectangle(0,0,48,48)` or a source-rect/position computed from the frame size.

## Verifying (the browser cache is a LIAR — always cache-bust)

After swapping a PNG the browser serves the STALE texture TO THE GAME, even on a fresh tab /
Ctrl+Shift+R. A `{cache:'reload'}` probe shows the SERVER has the new file while the GAME
already loaded the cached one — that's the "looks un-upscaled" symptom (a stale 48px texture
makes SuperSampleFactor=1, so it renders at normal size but blurry). Real test flow:
1. rebuild if code changed; kill stale `dotnet run` on :5280 (`netstat -ano | grep :5280` ->
   `taskkill //F //PID`); `preview_start`.
2. navigate to a level, then in the page console REPLACE the cache for the swapped textures:
   `for (const n of [...names]) await fetch('/Content/gfx/sprites/'+n+'.png',{cache:'reload'})`
   then reload the page so the game loads the fresh ones. (Confirm: the fetch returns the HD dims.)
3. verify in a real Chrome tab (claude-in-chrome), not preview_screenshot.
- Verifying a single sprite in isolation: `?harness=<Obj>` (see CLAUDE.md) boots ONE frozen
  object on space through the real pipeline -- ideal for sprites that are painful to spawn in a
  level (asteroids, the deathstar). Cache-bust the swapped textures BEFORE loading the harness:
  its first on-demand `TitleContainer` load can FileNotFound a just-swapped asset on a cold
  browser cache; a reload (or a prior `{cache:'reload'}` fetch) fixes it. Note the harness shows
  an object's IDLE pose (no gameplay Update), so Update-gated looks (the spider's airborne jump)
  don't appear there.
- **`?invuln`** (aliases `?invulnerability` / `?god`) keeps the player alive so you can watch a
  level without dying. Combine: `?level=Level3&noattract&invuln`.
- Where each enemy shows: small UFOs -> any level; landed small UFOs -> Level2 bottom; big
  landed UFO -> Level2 (deep, after small waves lift off); `faceofdeath` skulls -> Level3 (~30s)
  or ClassicAliens (press Start via `eaPress('Enter')`); `deathstar` mines -> ClassicAliens /
  Level3 StarMines; landed boss (`Mothership_landed`) -> Level2 deep / Demo2 only (hard to reach
  passively — construction-verify + cache-confirm the texture instead).
- The live-game rAF loop wedges the zoom/`document_idle` capture; full `screenshot`s work, zoom
  often doesn't. Assess crispness from full screenshots.

## Video-generated animations (AnimGen frame exports -> animated sprite)

A different source: the AnimGen pipeline (WAN video model) exports a FOLDER of individual
`frame_XXX.png` on magenta (e.g. 45 frames, 848x480) + a settings.txt -- NOT a magenta grid.
Use it to turn a STATIC sprite into a multi-frame animation (the spider's jump `spiderjump`
became a 24-frame soar; its start=end frame was the static spiderjump_upscaled, so it loops).

- `pack_frames.py <frames_dir> <orig.png> <factor> <cols> <rows> <out>`: selects cols*rows
  frames EVENLY (np.linspace, to thin the take), keys magenta, footprint-matches the median
  sprite width to orig*factor, bbox-centres each into a uniform cell, packs the grid. Prints
  the cell size (design-frame-width = cellW/factor) and checks the sheet is <=4096 (WebGL cap).
- `pack_anchored_anim.py <frames_dir> <out>`: for a FIXED-CAMERA take (subject anchored, only its
  parts move -- the spider's "rear up", AnimGen prompt "Fixed camera, static shot"). Unlike
  `pack_frames.py` it does NOT bbox-centre per frame (that jitters an anchored subject); it crops
  EVERY frame to ONE shared union-bbox window so motion is preserved, composites over magenta
  first (so frames with real transparency don't key to black), then keys + packs. `--every 2`
  halves fps; `--cellw N` sets an exact cell width.
- **Sizing a cell for a 1:1 in-game draw at a target window** (the `spider_sheet2` rear-up): the
  presenter scale at a `WxH` window is `min(W/800, H/600)` (`RenderScale`, capped 1440 tall), so a
  design-width-`G` sprite draws at `G * thatScale` window px. For 1 texel = 1 screen px, build the
  cell at `cellW = G * thatScale` and register design `G`. The spider: G=160 at 1280x1024 (scale
  1.6) -> `--cellw 256`, registered `spider_sheet2 -> 160`, drawn at scale 1 (so `textureScale =
  256/160 = 1.6`, exact). Replaced the old 4-frame crawl; `AnimationData(...,7,7,1,12f)`.
- Budget: N HD frames is a big texture. Pick cols*rows + factor so the sheet stays <=4096 AND
  the download stays small (24 frames @ factor 3 -> ~2400x735, ~1.7MB; drop to factor 2 or
  fewer frames for <1MB). Engine frame-interpolation keeps a thinned take smooth.
- Video-model magenta is slightly desaturated/varying (e.g. (223,33,206)) but key_magenta's
  thresholds catch it; the take is ~83% magenta and keys clean (0 fringe).
- Playing it when the sprite is drawn DIRECTLY (not via the component): in the draw, index the
  cell from a time-based frame and draw the source rect --
    `int f = (int)(gameTime.TotalGameTime.TotalMilliseconds / MS_PER_FRAME) % (cols*rows);`
    `Rectangle src = new Rectangle(f%cols*(cellW+1), f/cols*(cellH+1), cellW, cellH);`
    `spriteBatch.Draw(tex, src, pos, rotation, 1f/SuperSampleFactor(name, cellW), center:true, color);`
  Register design = cellW/factor (per-CELL, not the whole sheet). For the spider jump this
  REPLACED the old static-body + fake-flapping-`wing1` composite (wings dropped; the animation
  carries the motion). Hard to verify live: spiders spawn only from StationarySpawner's spider
  branch, boss-gated deep in Level2/InsaneBoss/Demo2 -- construction-verify + a real playthrough.
