# tools/walls — Level-3 collidable-wall texture (`GFX/Base/756-v1`)

The front, collidable walls in **Level 3** (the `Wall` class, `Game/EvilAliens/Wall.cs`)
are drawn from **one** texture: `wwwroot/Content/gfx/base/756-v1.png`. `Wall.Draw` samples
it as an **8×8 grid** — block (i,j) draws source cell `(j%8, i%8)` — so a run of blocks
scans the image and **wraps around its edges**. That means the whole texture must **tile
seamlessly** (all four edges wrap) or a hard seam shows every 8 blocks. On-screen size is
computed dynamically (`scale = 800 / (texture.Width * width)`), and the 8×8 split is
`texture.Width/8` (integer), so a drop-in replacement only has to be **a multiple of 8** in
each dimension — any resolution works, and **no game code changes**.

The current `756-v1.png` is a low-res **512×512**; it looks blurry because each 64-px cell is
blown up hugely on screen. This folder is the pipeline to ship a **higher-res** version.

## Companion sheet: `756-v1-side.png` (`build_wall_side.py`)

Since card `d59266cc`, `Wall.Draw` also extrudes each block **downward into a 3D tower shaft**
(see `plans/walls-3d-towers.md`). The shaft slices sample a **separate, low-frequency sheet** —
the same 8×8 grid with every cell **area-averaged** down to 16×16 texels — because slicing the
full-res cell makes the shaft corduroy (consecutive slices redraw the same high-frequency detail
at slightly different scales, so the sliver each one leaves exposed repeats it rather than
smearing into a wall face).

**`756-v1-side.png` is derived from `756-v1.png`, so re-run the builder whenever you replace the
wall texture** (after the tileable step below):

```
python tools/walls/build_wall_side.py         # rebuild (128×128, a few KB)
python tools/walls/build_wall_side.py --cell 8   # flatter/smoother shafts
```

Area-averaging (rather than cropping the cell's centre) is load-bearing: the centre texel of some
cells is a bright highlight — RGB(121,194,240) against a cell-average luminance range of only
72..116 — which as a slice tint would render that block's tower as a glowing white slab.
Don't hand-edit the output.

## The flow (art step is yours; the tileable step is automated)

1. **Upscale the art (you).** Give ChatGPT / an image upscaler the current
   `wwwroot/Content/gfx/base/756-v1.png` and ask for a **higher-resolution** version at a
   **square, power-of-two size** (1024×1024 or 2048×2048 recommended; any multiple of 8 works —
   a multiple of **16** if you'll use the Flux infill path). Keep it a solid, opaque, roughly
   uniform "wall material" — no borders, no logos, edge-to-edge texture. You do **not** need to
   make it tileable yourself.
2. **Drop it** at `tools/walls/source/756-v1.png` (gitignored — it's raw art, like the earth /
   classic-music raw sources).
3. **Make it tileable** with one of the two methods below, then eyeball the 2×2 preview it writes.

## Two ways to make it seamless (A/B them)

Both offset the upscale so the wrap seam becomes a centre cross, then fix that cross while keeping
the outer border seamless. Each writes its own 2×2 tiling preview so you can compare side by side.

### A. BLEND — offline, no model (`build_wall_tileable.py`, default)

```sh
python tools/walls/build_wall_tileable.py            # source/756-v1.png -> content path
python tools/walls/build_wall_tileable.py --size 1024   # Lanczos-resize to 1024² first
python tools/walls/build_wall_tileable.py --check-only  # report + preview, don't write the asset
python tools/walls/build_wall_tileable.py --band-frac 0.2   # widen the heal frame if a seam survives
python tools/walls/build_wall_tileable.py --in X.png --out Y.png   # explicit source / destination
```

Offset-and-heal, healed with the **same Laplacian multiband blend the mars-ground stitcher uses**
(`tools/mars/stitch_lib.py:pyr_blend` — the "similar toolchain as mars" the card asked for): keep a
seamless pure-`B` frame around all four edges, cross-fade the frame→centre transition per-frequency
so each side keeps its detail. Deterministic and dependency-free — but it **relocates** edge content
(from the opposite half) and can faintly ghost near the seam; it blends existing pixels, it doesn't
invent. Writes `preview_blend_756-v1.png`.

### B. INFILL — a LOCAL inpainting model regenerates the seam (higher quality)

The modern "make seamless" method: mask the seam cross and let an inpainter **generate coherent new
detail** across it — no ghosting, no relocation. **ChatGPT can't do this** (it regenerates the whole
frame and breaks the unmasked borders); you need a real inpainter that preserves unmasked pixels
(**Flux Fill** / SD-inpaint, run locally). The reimport composites the fill **inside the mask only**
over the original offset, so the wrap borders stay pixel-exact → tiling is guaranteed.

One-shot with Flux (see `flux_infill.py` header for setup — needs a GPU + gated `FLUX.1-Fill-dev`):

```sh
python tools/walls/flux_infill.py --size 1024        # emit -> Flux Fill -> composite -> install
python tools/walls/flux_infill.py --check-only       # don't write the content file
```

Or model-agnostic, three steps (works with ANY local inpainter):

```sh
python tools/walls/build_wall_tileable.py --emit-seam            # -> seam/756-v1_offset.png + _mask.png
#   run your inpainter on those two (offset = image, mask = white band to fill)
python tools/walls/build_wall_tileable.py --reimport out.png     # composite in-mask + install
```

Writes `preview_infill_756-v1.png`. `--reimport` needs the `seam/` files from `--emit-seam` (it
composites the fill over that offset so the borders stay seamless).

> **NOTE:** the Flux path (`flux_infill.py`'s pipeline call) is **not run/verified in this repo** —
> it needs a GPU + the gated weights, which aren't available here. The seam/composite/install
> plumbing it shares with `build_wall_tileable.py` *is* verified; treat the Flux params as a
> starting point and tune for your box.

Offline core (numpy + Pillow; cv2 optional → sharper pyramid). Don't hand-edit the shipped
`756-v1.png`; re-run after a new upscale. **Verify on the LIVE Pages URL too** — content paths are
case-sensitive there (capital `Content/`, lowercase under it).

## Notes / follow-ups

- Only `756-v1` is the **collidable** wall (the 8×8 grid-sampled one). The other `756-v*`
  (`v3/v4/v5/v6/v8`) are loaded as single whole tiles into scrolling Base-level **background**
  layers in `Background.cs` — a different use whose four-edge wrap needs weren't verified, so
  they're out of scope here. This tool *could* be pointed at one (`--in`/`--out`), but confirm it
  actually needs seamless wrapping first.
- If Level-3 preload stutters on a big new PNG, precompile it to DXT: add `756-v1` to
  `tools/textures/textures.config` and run `tools/textures/build_textures.py`
  (`WebContentManager` prefers `.dds` → `.rtex` → `.png`). Dims are a multiple of 8, so the
  mult-of-4 DXT rule is already satisfied.
