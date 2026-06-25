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
#          factor, wash). factor = output texels / original. Because the engine
# decouples texture resolution from on-screen size (DesignFrameWidth registry), the
# factor is chosen ONLY for ~1:1 texel:pixel at the worst case + a little AA headroom
# -- NOT for "max upscale". On-screen px = designW * maxDrawScale * 2.4 (presenter cap),
# so factor ~= maxDrawScale * 2.4 * 1.25. maxDrawScale: bullets/option/photocamera/arrow
# =1, blooddrop ~size*0.22 (size<=6 -> ~1.3), braingoo ~size*0.044 (-> ~0.26),
# parachute/plasmaball2 =0.25, awardmentblade ~1 (capped by AI source res).
JOBS = {
    "parachute":          ("misc2.png", (12, 60, 620, 622), 0.8, False),
    "plasmaball2":        ("misc2.png", (12, 686, 620, 1246), 0.75, False),
    "blooddrop":          ("misc1.png", (608, 40, 898, 343), 3.0, False),
    "blooddrop_green":    ("misc1.png", (909, 40, 1199, 343), 3.0, False),
    "option":             ("misc1.png", (1210, 40, 1500, 343), 3.0, False),
    "braingoo":           ("misc1.png", (6, 388, 296, 691), 1.0, False),
    "photocamera":        ("misc1.png", (608, 388, 898, 691), 3.0, True),
    "shadow":             ("misc1.png", (909, 388, 1199, 691), 4.0, False),
    "singleconnectorglow": ("misc1.png", (6, 737, 296, 1039), 4.0, False),
    "connector":          ("misc1.png", (307, 737, 597, 1039), 4.0, False),
    "blast":              ("blast_new.png", None, 1.5, False),   # green-bg redo; None = whole image
    "awardmentblade":     ("misc2.png", (634, 682, 1248, 1246), 1.2, False),
}

# Optional alpha gamma per sprite (alpha' = alpha**g, g<1 LIFTS the faint halo so a
# glow reads stronger/airier). connector's AI redraw had a solid core + faint halo;
# lifting the halo makes it more of a glow.
ABOOST = { "connector": 0.6 }

# Per-sprite key knee override (default 0.14). blast's white-hot centre is pinkish over
# magenta, so the default knee eats into it -- use a gentle knee there.
KNEE = { "blast": 0.05 }

# Per-sprite chroma colour (default magenta). A magenta-ADJACENT subject (pink/purple/
# blue plasma) can't be separated from a magenta bg -- the key eats its interior. Have
# the AI redraw it on GREEN and key 'green' here. When blast's green-bg version lands:
# point its JOBS sheet at it, set KEY['blast']='green', and drop it from FILLGLOW (the
# real streaked sphere will key clean, no fill hack needed).
KEY = { "blast": "green" }

# Sprites the AI drew as a hollow ring (transparent centre) that should read SOLID:
# fill the enclosed interior with a radial glow in the ring's colour. (blast's green-bg
# redo keys clean, so it no longer needs this.)
FILLGLOW = set()

# Sprites whose opaque AI fill should be reduced to FOLLOW the glow's own brightness
# (bright = opaque, dark = transparent) -- turns a solid sphere into a glow. For blast's
# bright-rim/dark-centre plasma this drops the opaque navy core and leaves the glowing
# cyan structure, reading as the centre->edge alpha gradient. (value = brightness boost)
LUMA_ALPHA = { "blast": 1.35 }


def glow_alpha(rgba, boost):
    lum = (rgba[..., :3].astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)) / 255.0
    a = rgba[..., 3].astype(np.float32) / 255.0
    out = rgba.copy()
    out[..., 3] = (np.clip(a * np.clip(lum * boost, 0.0, 1.0), 0.0, 1.0) * 255 + 0.5).astype(np.uint8)
    return out


GREEN = np.array([0.0, 255.0, 0.0], np.float32)


def fuchsia_key(rgb, knee=0.14, key="magenta"):
    """RGB-on-chroma -> straight-alpha RGBA. "Keyness" is the deficit of the channel
    the subject HAS vs the chroma's dominant channel(s), NORMALISED BY LOCAL BRIGHTNESS,
    so a dark/off/noisy background still keys to ~0 (not a faint hase); a small knee
    snaps the last residue away; the un-mix recovers the straight colour.

    key='magenta' (default): keys (min(R,B)-G) -- for subjects with NO magenta.
    key='green': keys (G-max(R,B)) -- for magenta-ADJACENT subjects (pink/purple/blue
    plasma) that can't be separated from a magenta bg. Pick the chroma the subject lacks."""
    f = rgb.astype(np.float32)
    R, G, B = f[..., 0], f[..., 1], f[..., 2]
    if key == "green":
        g = np.maximum(R, B)
        m = np.clip((G - g) / np.maximum(G, 1.0), 0.0, 1.0)  # 1 on any-brightness green, 0 on white/red/blue/magenta
        K = GREEN
    else:
        mn = np.minimum(R, B)
        m = np.clip((mn - G) / np.maximum(mn, 1.0), 0.0, 1.0)  # 1 on any-brightness magenta
        K = MAGENTA
    alpha = np.clip(1.0 - m, 0.0, 1.0)
    alpha = np.clip((alpha - knee) / (1.0 - knee), 0.0, 1.0)  # kill residual background
    a3 = alpha[..., None]
    F = np.where(a3 > 1e-3, (f - (1.0 - a3) * K) / np.maximum(a3, 1e-3), 0.0)
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


def orig_ref(name):
    """Pristine original -- the .png.orig backup if a swap already happened (so a
    re-run doesn't footprint-match against our own upscaled output), else .png."""
    o = os.path.join(SPRITES, name + ".png.orig")
    return o if os.path.exists(o) else os.path.join(SPRITES, name + ".png")


def fill_glow(rgba, strength=1.0, falloff=0.75):
    """Fill the enclosed transparent interior of a ring-shaped sprite with a radial
    glow in the ring's own colour, so a hollow shockwave reads as a solid burst."""
    a = rgba[..., 3].astype(np.float32) / 255.0
    solid = ndimage.binary_fill_holes(a > 0.2)
    ys, xs = np.where(solid)
    if len(ys) == 0:
        return rgba
    cy, cx = ys.mean(), xs.mean()
    R = 0.5 * max(np.ptp(ys), np.ptp(xs)) + 1.0
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    radial = np.clip(1.0 - np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2) / (R * 0.98), 0.0, 1.0) ** falloff
    radial = radial * strength * solid
    bright = a > 0.5
    col = rgba[..., :3][bright].mean(0) if bright.any() else np.array([190, 220, 235])
    fill = radial > a
    out = rgba.copy()
    for c in range(3):
        ch = out[..., c]
        ch[fill] = col[c]
    out[..., 3] = (np.clip(np.maximum(a, radial), 0, 1) * 255 + 0.5).astype(np.uint8)
    return out


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
    x0, y0, x1, y1 = box if box is not None else (0, 0, sheet.shape[1], sheet.shape[0])
    rgba = keep_real_blobs(fuchsia_key(sheet[y0:y1, x0:x1], knee=KNEE.get(name, 0.14),
                                       key=KEY.get(name, "magenta")))
    if name in LUMA_ALPHA:
        rgba = glow_alpha(rgba, LUMA_ALPHA[name])
    if name in FILLGLOW:
        rgba = fill_glow(rgba)
    if wash:
        rgba = wash_out(rgba)
    g = ABOOST.get(name, 1.0)
    if g != 1.0:
        a = (rgba[..., 3].astype(np.float32) / 255.0) ** g
        rgba[..., 3] = (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)
    out, ow = footprint_match(rgba, orig_ref(name), factor)
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
