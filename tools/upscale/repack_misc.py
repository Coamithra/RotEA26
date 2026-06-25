"""
Slice single-frame sprites out of a ChatGPT contact sheet, key the magenta with
proper TRANSLUCENCY recovery, and footprint-match each to its original's exact
dimensions -> a pure drop-in (same on-screen size, no engine wiring).

The translucency keyer is the important bit. The old key_magenta (keycompare.py)
keys saturated sprites fine but OVER-keys soft/bright ones: a 50%-white-over-
magenta pixel reads as fully transparent. Here alpha = 1 - (min(R,B)-G)/255 (0 on
white/grey/green/red, 1 on pure magenta), and the straight colour is un-mixed via
F = (P - (1-a)*M)/a  with M = magenta -- so a soft glow keeps its real colour at
partial alpha instead of staying pink. Plasmaball2's halo + parachute's string
gaps dissolve correctly.

    python tools/upscale/repack_misc.py            # all configured sprites
    python tools/upscale/repack_misc.py plasmaball2

Outputs tools/upscale/gen_out/<name>.png (drop-in, original dims) and
<name>_check.png (checkerboard preview so the recovered alpha is visible).
"""
# pyright: reportAttributeAccessIssue=false
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITES = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites")
OUT = os.path.join(HERE, "gen_out")
MAGENTA = np.array([255.0, 0.0, 255.0], np.float32)

# name -> (sheet file under new_assets_raw, (x0,y0,x1,y1) sprite region in the
# sheet with the label text + grid lines already excluded).
JOBS = {
    "parachute":  ("misc2.png", (12, 60, 620, 622)),
    "plasmaball2": ("misc2.png", (12, 686, 620, 1246)),
}


def fuchsia_key(rgb):
    """RGB-on-magenta -> straight-alpha RGBA with translucency + colour un-mix."""
    f = rgb.astype(np.float32)
    R, G, B = f[..., 0], f[..., 1], f[..., 2]
    d = np.clip(np.minimum(R, B) - G, 0.0, 255.0)        # magenta-ness
    alpha = np.clip(1.0 - d / 255.0, 0.0, 1.0)
    a3 = alpha[..., None]
    F = np.where(a3 > 1e-3, (f - (1.0 - a3) * MAGENTA) / np.maximum(a3, 1e-3), 0.0)
    out = np.zeros((*rgb.shape[:2], 4), np.uint8)
    out[..., :3] = np.clip(F, 0, 255).astype(np.uint8)
    out[..., 3] = (alpha * 255 + 0.5).astype(np.uint8)
    return out


def keep_real_blobs(rgba, thr=40, frac=0.02):
    """Zero the alpha of stray specks (leftover label text / noise): keep only
    connected components at least `frac` of the largest one's area. Multi-part
    sprites (parachute canopy + strings) survive; tiny text dots don't."""
    a = rgba[..., 3]
    lab, n = ndimage.label(a > thr)
    if n == 0:
        return rgba
    sizes = ndimage.sum(np.ones_like(lab), lab, index=np.arange(1, n + 1))
    keep = np.flatnonzero(sizes >= frac * sizes.max()) + 1
    mask = np.isin(lab, keep)
    out = rgba.copy()
    out[..., 3] = np.where(mask, a, 0)
    return out


def alpha_bbox(a, thr=12):
    ys, xs = np.where(a > thr)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def footprint_match(sprite, orig_path):
    """Scale the keyed sprite so its alpha bbox fills the original's alpha bbox,
    centred, on a canvas of the original's exact dimensions."""
    orig = np.asarray(Image.open(orig_path).convert("RGBA"))
    OH, OW = orig.shape[:2]
    ox0, oy0, ox1, oy1 = alpha_bbox(orig[..., 3])
    obw, obh = ox1 - ox0, oy1 - oy0

    sx0, sy0, sx1, sy1 = alpha_bbox(sprite[..., 3])
    crop = sprite[sy0:sy1, sx0:sx1]
    sbw, sbh = sx1 - sx0, sy1 - sy0
    sc = min(obw / sbw, obh / sbh)                       # fit within orig footprint
    nw, nh = max(1, round(sbw * sc)), max(1, round(sbh * sc))
    rs = Image.fromarray(crop, "RGBA").resize((nw, nh), Image.LANCZOS)

    canvas = Image.new("RGBA", (OW, OH), (0, 0, 0, 0))
    ocx, ocy = (ox0 + ox1) / 2, (oy0 + oy1) / 2
    canvas.alpha_composite(rs, (round(ocx - nw / 2), round(ocy - nh / 2)))
    return canvas


def checker(img, tile=16):
    im = np.asarray(img).astype(float)
    a = im[..., 3:4] / 255.0
    H, W = im.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    bg = np.where(((xx // tile + yy // tile) % 2)[..., None] == 0, 150.0, 90.0)
    return Image.fromarray((im[..., :3] * a + bg * (1 - a)).astype(np.uint8), "RGB")


def run(name):
    sheet_file, box = JOBS[name]
    sheet = np.asarray(Image.open(os.path.join(HERE, "new_assets_raw", sheet_file)).convert("RGB"))
    x0, y0, x1, y1 = box
    rgba = keep_real_blobs(fuchsia_key(sheet[y0:y1, x0:x1]))
    out = footprint_match(rgba, os.path.join(SPRITES, name + ".png"))
    os.makedirs(OUT, exist_ok=True)
    out.save(os.path.join(OUT, name + ".png"))
    checker(out).save(os.path.join(OUT, name + "_check.png"))
    print("  %-12s -> gen_out/%s.png  (%dx%d)" % (name, name, out.width, out.height))


if __name__ == "__main__":
    todo = sys.argv[1:] or list(JOBS)
    for n in todo:
        run(n)
