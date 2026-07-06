# tools/nebula — Level-1 Andromeda nebula sprite

Rebuilds `GFX/Sprites/andromeda.png` — the galaxy that flies past during Level 1's
"brains" section (`Background.QueueAndromeda`) — from a high-res source, normalised
into the game's straight-alpha format.

## Why
The shipped galaxy was 840×583. It's drawn at a fixed **840 design-px footprint**,
which `RenderScale` then upscales to the window, so on a 1080p+ window it's a ~2.4×
bilinear blur that reads as a flat sticker against the reskin's crisp nebula
starfield. Supplying a higher-res source fixes it — **without any game code change**,
because `Background.QueueAndromeda` now computes
`doodadscale = 840 / texture.Width`, so any output resolution keeps the same
on-screen size (more texels = more crispness, not a bigger galaxy).

## How to swap in a new galaxy
1. **Generate the art** (this is the manual/creative step — see the "For me" Trello
   card). A galaxy or nebula that fits the game's vivid, slightly-stylised space look.
   Either shape works:
   - a bright galaxy/nebula on a **black** background (the typical image-gen output), or
   - a pre-cut galaxy on a **transparent** background.
   Aim for a centred subject with some margin, ~1600–2600 px on the long side.
2. **Drop it at** `tools/nebula/source/andromeda.png` (gitignored — raw source stays local).
3. **Run** `python tools/nebula/build_nebula.py`.
4. It writes `web/EvilAliensWeb/wwwroot/Content/gfx/sprites/andromeda.png` (committed)
   and prints the resulting on-screen `doodadscale` (a sanity check; the game derives
   it live, so you don't edit any code).

## Current source / license
The shipped `andromeda.png` is built from **Adam Evans' natural-colour M31 mosaic**
("Andromeda Galaxy (with h-alpha)", Wikimedia Commons), licensed **CC BY 2.0** —
free to use *with attribution*. The credit is in the in-game end-credits crawl
(`CreditsScene.SetupCredits`, "ADDITIONAL GRAPHICS"). If you swap in a different
source, keep it public-domain (NASA/JPL) or CC-BY-with-credit and update that line.
The raw photo lives at `source/andromeda_raw.png` (gitignored).

That photo is a galaxy on a *dense starfield*, so it goes through
`make_cutout.py` first (see below) to become a soft elliptical CUTOUT — just the
galaxy blob, surrounding stars cut away — matching the original andromeda asset's
style so the fly-by doesn't lay a second field of stars over the game's own
starfield. That cutout is `source/andromeda.png`, which `build_nebula.py` then
normalises into the committed sprite.

## Cutting a star-heavy source (`make_cutout.py`)
If the raw galaxy sits on a rich starfield, run `python tools/nebula/make_cutout.py`
BEFORE `build_nebula.py`. It finds the galaxy's centre/tilt/size from image moments
of a blurred glow, builds a tilted elliptical soft mask, gates it by per-pixel
brightness (dark gaps stay transparent so the game starfield shows through), crops
to the galaxy, and writes `source/andromeda.png` (a pre-cut transparent-background
PNG that `build_nebula.py` auto-detects as `--alpha source`). Reads
`source/andromeda_raw.png` → writes `source/andromeda.png`; `--show` writes an
over-starfield preview; tunables (oval size/feather, brightness gate) are consts at
the top. Skip this step if your source is already a clean galaxy-on-black or a
pre-cut transparent PNG.

## What the tool does
- **Alpha** (`--alpha auto`, default): auto-detects the source shape. Opaque-on-black
  → derives alpha from luminance (black becomes transparent). Already-transparent →
  respects the source alpha. Force with `--alpha luma` / `--alpha source`.
- **Edge feather**: a per-axis vignette forces every frame edge to transparent, so
  there's never a hard rectangle over the starfield even if the galaxy reaches an edge.
- **Size cap**: downscales (never upscales) the long side to `--max-dim` (default 2048,
  ~1:1 texels at the largest supported window). Done in premultiplied space so the
  transparent limb doesn't bleed dark.
- **Straight-alpha RGBA out** — the andromeda doodad uses AlphaBlend →
  `BlendState.NonPremultiplied` (see `CLAUDE.md`); no premultiplied tints.
- **Safe no-op**: if `source/andromeda.png` is missing, the shipped PNG is left
  untouched (CI / a fresh clone never regresses the art).

## Knobs
`--opacity F` (overall translucency), `--gamma F` (luma→alpha falloff — >1 keeps more
faint outer wisps), `--feather F` (edge-feather start, 0..1), `--max-dim N`,
`--source PATH`, `--out PATH`, `--dry-run`. Baked defaults live at the top of
`build_nebula.py`.

Offline (numpy + Pillow), like `tools/earth`, `tools/textures`, `tools/audio`. CI just
ships the committed `andromeda.png`.

## Verifying in-game
It's a background fly-by, not a `?harness=` object, and it appears deep into Level 1
(after the first brain wave), so the practical check is to boot Level 1 and watch the
brains section — or trust the tool's over-space preview. Confirm on the **live Pages
URL** too (content paths are case-sensitive there).
