"""Chroma-key the title art -> transparent background.

Default source : title-revenged-cyan.png  (solid cyan backdrop ~RGB(3,252,252))

Cyan was chosen because the artwork already contains lots of green and purple;
cyan is the one bright corner of colour space the art never visits, so the key
is clean with real anti-aliasing instead of a binary matte.

Pipeline:
  1. backdrop colour : auto-detected as the median of the border ring.
  2. difference key  : keyness = min(high channels) - max(low channels), where
                       "high"/"low" are the channels the backdrop maxes / drops.
                       For cyan that's min(g,b) - r. This is LINEAR in coverage
                       (a c*bg + (1-c)*art blend has keyness linear in c) and
                       ignores brightness, so white/silver art isn't keyed.
  3. coverage + alpha: bg_cov = ramp(keyness) between art-level and backdrop-level
                       keyness, so it tracks the true backdrop fraction; opacity
                       alpha = 1 - bg_cov.
  4. decontaminate   : art = (pixel - bg_cov*bg) / (1 - bg_cov). Because bg_cov
                       tracks real coverage, this removes the backdrop's colour
                       from edge pixels exactly -> no cyan fringe. Identity on
                       fully-opaque interior art.
  5. edge bleed      : flood nearest art colour into fully-transparent pixels so
                       the GPU's bilinear edge sampling can't halo.
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = sys.argv[1] if len(sys.argv) > 1 else \
    "web/EvilAliensWeb/wwwroot/Content/gfx/menu/title-revenged-cyan.png"
OUT = sys.argv[2] if len(sys.argv) > 2 else \
    "web/EvilAliensWeb/wwwroot/Content/gfx/menu/title-revenged.png"
# Ramp endpoints in keyness units: K_LO ~ art level (-> opaque), K_HI ~ backdrop
# level (-> transparent). Set near the measured art (~20) and backdrop (~249).
K_LO = float(sys.argv[3]) if len(sys.argv) > 3 else 30.0
K_HI = float(sys.argv[4]) if len(sys.argv) > 4 else 235.0


def detect_bg(a):
    b = np.concatenate([a[:5].reshape(-1, 4), a[-5:].reshape(-1, 4),
                        a[:, :5].reshape(-1, 4), a[:, -5:].reshape(-1, 4)])
    return np.median(b[:, :3], axis=0)


def keyness_of(rgb, hi_ch, lo_ch):
    hi = rgb[..., hi_ch].min(axis=-1)
    lo = rgb[..., lo_ch].max(axis=-1)
    return hi - lo


def chroma_key(a, bg, k_lo, k_hi):
    a = a.astype(np.float32)
    rgb = a[..., :3]

    thr = bg.max() * 0.5
    hi_ch = [c for c in range(3) if bg[c] > thr]
    lo_ch = [c for c in range(3) if bg[c] <= thr]

    keyness = keyness_of(rgb, hi_ch, lo_ch)
    bg_cov = np.clip((keyness - k_lo) / (k_hi - k_lo), 0.0, 1.0)
    alpha = 1.0 - bg_cov

    # --- decontaminate: subtract the backdrop's colour contribution ---
    denom = np.maximum(1.0 - bg_cov, 1e-3)[..., None]
    art = (rgb - bg_cov[..., None] * bg) / denom
    visible = alpha[..., None] > 0.0
    rgb = np.where(visible, np.clip(art, 0, 255), rgb)

    a8 = np.clip(alpha * 255.0, 0, 255)

    # --- edge bleed into transparent pixels (straight-alpha safe) ---
    opaque = a8 > 0
    _, (iy, ix) = ndimage.distance_transform_edt(~opaque, return_indices=True)
    rgb = rgb[iy, ix]

    return np.concatenate([rgb, a8[..., None]], axis=-1).astype(np.uint8), (hi_ch, lo_ch)


def checkerboard(rgba, sq=16):
    h, w = rgba.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    base = np.where(((xx // sq) + (yy // sq)) % 2 == 0, 205, 130).astype(np.float32)
    bg = np.stack([base, base, base], axis=-1)
    a = rgba[..., 3:4].astype(np.float32) / 255.0
    return (rgba[..., :3].astype(np.float32) * a + bg * (1 - a)).clip(0, 255).astype(np.uint8)


im = Image.open(SRC).convert("RGBA")
arr = np.asarray(im)
bg = detect_bg(arr)
keyed, (hi_ch, lo_ch) = chroma_key(arr, bg, K_LO, K_HI)
Image.fromarray(keyed, "RGBA").save(OUT)
Image.fromarray(checkerboard(keyed)).save("tmp_preview.png")

alpha = keyed[..., 3]
print(f"src={SRC}")
print(f"bg={bg.astype(int)}  high_ch={hi_ch} low_ch={lo_ch}  K_LO={K_LO} K_HI={K_HI}")
print(f"-> {OUT}")
print(f"transparent: {(alpha==0).mean()*100:.1f}%   opaque: {(alpha==255).mean()*100:.1f}%   partial(AA): {((alpha>0)&(alpha<255)).mean()*100:.2f}%")
