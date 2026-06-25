"""
Slice single-frame sprites out of the ChatGPT contact sheets (misc1/misc2), key
the magenta with TRANSLUCENCY recovery, and repack each to original-dims * factor
so the engine's supersample registry (AlienDrawableGameComponent.DesignFrameWidth)
keeps on-screen size identical while the extra texels add crispness.

Translucency keyer: alpha = 1-(min(R,B)-G)/255 (0 on white/grey/green/red, 1 on
pure magenta), colour un-mixed via F = (P-(1-a)*M)/a -- soft glows/string-gaps
dissolve instead of staying pink.

    python tools/upscale/repack_misc.py            # all jobs + a montage
    python tools/upscale/repack_misc.py blast option

Outputs gen_out/<name>.png (the swap-in texture, OW*factor x OH*factor) and
gen_out/_misc_montage.png (every result on a checkerboard, to eyeball the keys).
Wiring (register design width = OW; fix direct-draw sites) is a separate step.
"""
# pyright: reportAttributeAccessIssue=false, reportGeneralTypeIssues=false
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITES = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites")
RAW = os.path.join(HERE, "new_assets_raw")
OUT = os.path.join(HERE, "gen_out")
MAGENTA = np.array([255.0, 0.0, 255.0], np.float32)

# name -> (sheet, (x0,y0,x1,y1) sprite region with label text + grid lines excluded,
#          factor, wash). factor = output size / original (registry design = OW).
JOBS = {
    "parachute":          ("misc2.png", (12, 60, 620, 622), 1.0, False),
    "plasmaball2":        ("misc2.png", (12, 686, 620, 1246), 1.0, False),
    "blooddrop":          ("misc1.png", (608, 40, 898, 343), 4.0, False),
    "blooddrop_green":    ("misc1.png", (909, 40, 1199, 343), 4.0, False),
    "option":             ("misc1.png", (1210, 40, 1500, 343), 4.0, False),
    "braingoo":           ("misc1.png", (6, 388, 296, 691), 4.0, False),
    "photocamera":        ("misc1.png", (608, 388, 898, 691), 4.0, True),
    "shadow":             ("misc1.png", (909, 388, 1199, 691), 4.0, False),
    "singleconnectorglow": ("misc1.png", (6, 737, 296, 1039), 4.0, False),
    "connector":          ("misc1.png", (307, 737, 597, 1039), 4.0, False),
    "blast":              ("misc2.png", (634, 55, 1248, 620), 1.5, False),
    "awardmentblade":     ("misc2.png", (634, 682, 1248, 1246), 1.2, False),
}


def fuchsia_key(rgb):
    f = rgb.astype(np.float32)
    R, G, B = f[..., 0], f[..., 1], f[..., 2]
    d = np.clip(np.minimum(R, B) - G, 0.0, 255.0)
    alpha = np.clip(1.0 - d / 255.0, 0.0, 1.0)
    a3 = alpha[..., None]
    F = np.where(a3 > 1e-3, (f - (1.0 - a3) * MAGENTA) / np.maximum(a3, 1e-3), 0.0)
    out = np.zeros((*rgb.shape[:2], 4), np.uint8)
    out[..., :3] = np.clip(F, 0, 255).astype(np.uint8)
    out[..., 3] = (alpha * 255 + 0.5).astype(np.uint8)
    return out


def keep_real_blobs(rgba, thr=40, frac=0.03):
    a = rgba[..., 3]
    lab, n = ndimage.label(a > thr)
    if n == 0:
        return rgba
    sizes = ndimage.sum(np.ones_like(lab), lab, index=np.arange(1, n + 1))
    keep = np.flatnonzero(sizes >= frac * sizes.max()) + 1
    out = rgba.copy()
    out[..., 3] = np.where(np.isin(lab, keep), a, 0)
    return out


def wash_out(rgba, lift=0.5):
    """Desaturate + lighten toward white + soften (photocamera: the original was a
    subtle white-only icon; the AI made it fully textured)."""
    rgb = rgba[..., :3].astype(np.float32)
    L = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    gray = 255.0 - (255.0 - L) * lift                    # lift darks toward white
    out = rgba.copy()
    out[..., :3] = np.clip(np.stack([gray] * 3, -1), 0, 255).astype(np.uint8)
    im = Image.fromarray(out, "RGBA").filter(ImageFilter.GaussianBlur(1.2))
    return np.asarray(im)


def alpha_bbox(a, thr=12):
    ys, xs = np.where(a > thr)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def footprint_match(sprite, orig_path, factor):
    orig = np.asarray(Image.open(orig_path).convert("RGBA"))
    OH, OW = orig.shape[:2]
    ox0, oy0, ox1, oy1 = alpha_bbox(orig[..., 3])
    obw, obh = (ox1 - ox0) * factor, (oy1 - oy0) * factor

    sx0, sy0, sx1, sy1 = alpha_bbox(sprite[..., 3])
    crop = sprite[sy0:sy1, sx0:sx1]
    sc = min(obw / (sx1 - sx0), obh / (sy1 - sy0))
    nw, nh = max(1, round((sx1 - sx0) * sc)), max(1, round((sy1 - sy0) * sc))
    rs = Image.fromarray(crop, "RGBA").resize((nw, nh), Image.LANCZOS)

    cw, ch = round(OW * factor), round(OH * factor)
    canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    ocx, ocy = (ox0 + ox1) / 2 * factor, (oy0 + oy1) / 2 * factor
    canvas.alpha_composite(rs, (round(ocx - nw / 2), round(ocy - nh / 2)))
    return canvas, OW


def checker(img, tile=16):
    im = np.asarray(img).astype(float)
    a = im[..., 3:4] / 255.0
    H, W = im.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    bg = np.where(((xx // tile + yy // tile) % 2)[..., None] == 0, 150.0, 90.0)
    return Image.fromarray((im[..., :3] * a + bg * (1 - a)).astype(np.uint8), "RGB")


def run(name):
    sheet_file, box, factor, wash = JOBS[name]
    sheet = np.asarray(Image.open(os.path.join(RAW, sheet_file)).convert("RGB"))
    x0, y0, x1, y1 = box
    rgba = keep_real_blobs(fuchsia_key(sheet[y0:y1, x0:x1]))
    if wash:
        rgba = wash_out(rgba)
    out, ow = footprint_match(rgba, os.path.join(SPRITES, name + ".png"), factor)
    os.makedirs(OUT, exist_ok=True)
    out.save(os.path.join(OUT, name + ".png"))
    print("  %-20s -> %dx%d  (design width %d, factor %.2g)" % (name, out.width, out.height, ow, factor))
    return name, checker(out)


def montage(results):
    cell = 240
    try:
        font = ImageFont.truetype("arial.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    cols = 4
    rows = (len(results) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 26)), (40, 40, 46))
    d = ImageDraw.Draw(sheet)
    for i, (name, chk) in enumerate(results):
        c, r = i % cols, i // cols
        x, y = c * cell, r * (cell + 26)
        sc = (cell - 16) / max(chk.width, chk.height)
        rs = chk.resize((round(chk.width * sc), round(chk.height * sc)), Image.LANCZOS)
        sheet.paste(rs, (x + (cell - rs.width) // 2, y + 26 + (cell - 16 - rs.height) // 2))
        d.text((x + 4, y + 4), name, fill=(235, 235, 235), font=font)
    sheet.save(os.path.join(OUT, "_misc_montage.png"))
    print("  _misc_montage.png")


if __name__ == "__main__":
    todo = sys.argv[1:] or list(JOBS)
    results = [run(n) for n in todo]
    montage(results)
