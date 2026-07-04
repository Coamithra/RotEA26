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

## The flow (art step is yours; the tileable step is automated)

1. **Upscale the art (you).** Give ChatGPT / an image upscaler the current
   `wwwroot/Content/gfx/base/756-v1.png` and ask for a **higher-resolution** version at a
   **square, power-of-two size** (1024×1024 or 2048×2048 recommended; any multiple of 8
   works). Keep it a solid, opaque, roughly uniform "wall material" — no borders, no logos,
   edge-to-edge texture. See the desc idea on the card about infill for tileability — you do
   **not** need to make it tileable yourself; step 3 does that.
2. **Drop it** at `tools/walls/source/756-v1.png` (gitignored — it's raw art, like the earth
   / classic-music raw sources).
3. **Run the tool:**
   ```sh
   python tools/walls/build_wall_tileable.py
   ```
   It re-makes the (edge-broken) upscale **seamlessly tileable** and writes the shipped
   artifact `wwwroot/Content/gfx/base/756-v1.png`, printing a before/after seam report and a
   2×2 tiling `preview_756-v1.png` so you can eyeball it.

## How the tileable step works

Classic **offset-and-heal**, healed with the **same Laplacian multiband blend the mars-ground
stitcher uses** (`tools/mars/stitch_lib.py:pyr_blend` — the "similar toolchain as mars" the
card asked for). It rolls the image by half so the wrap seam moves to the centre where content
exists on both sides, keeps a seamless pure-`B` frame around all four edges, and cross-fades
the frame→centre transition per-frequency so each side keeps its own detail. Result: the output
border wraps exactly → tiles seamlessly. It reports the wrap seam as a **ratio to the texture's
own interior adjacency** (1.0 = as continuous as any interior pixel step; a broken seam is ≫1).

## Options

```
python tools/walls/build_wall_tileable.py --size 1024   # Lanczos-resize to 1024² first
python tools/walls/build_wall_tileable.py --check-only  # report + preview, don't write the asset
python tools/walls/build_wall_tileable.py --band-frac 0.2   # widen the heal band if a seam survives
python tools/walls/build_wall_tileable.py --in X.png --out Y.png
```

Offline (numpy + Pillow; cv2 optional → sharper pyramid). Don't hand-edit the shipped
`756-v1.png`; re-run this after a new upscale. **Verify on the LIVE Pages URL too** — content
paths are case-sensitive there (capital `Content/`, lowercase under it).

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
