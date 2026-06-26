#!/usr/bin/env python
# Phase 4 (0-OVL multiband): abut the 12 sheared quarters at full width (NO overlap, no
# content discarded) and blend each seam with a Laplacian-pyramid (multiband) pass over a
# band straddling the seam. Low frequencies (tone/shading) blend wide; high frequencies
# (rock detail) stay sharp up to the seam. Edge-extended so the pyramid has data both sides.
# Wrap seam handled modularly. Outputs the blended RGBA strip + overview + seam zooms.
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE); sys.path.insert(0, ROOT)
from stitch_lib import pyr_blend, HAVE_CV2   # noqa: E402

SRC = os.path.join(HERE, 'sheared')
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
BW = 160                                                     # half blend-band width (px)
LEVELS = 6
BG = np.array([45, 55, 70], np.float32)
print(f'multiband: cv2={HAVE_CV2}, band=+-{BW}px, levels={LEVELS}')

Q = [np.asarray(Image.open(os.path.join(SRC, f'mars{n}_{h}.png')).convert('RGBA')).astype(np.float32) for n, h in order]
H, W = Q[0].shape[:2]
Wtot = 12 * W

# hard 0-OVL abut
strip = np.concatenate(Q, axis=1).copy()                    # H x Wtot x 4
hard = strip.copy()

# NARROW alpha transition centered on the seam: blend RGB wide (multiband) but keep the
# silhouette crisp, so we never create floating semi-transparent terrain (the horizon streaks).
AW = 16                                                      # alpha transition width (px)
_xb = np.arange(2 * BW)
_am = np.clip((_xb - (BW - AW / 2)) / AW, 0, 1)
sm = (_am * _am * (3 - 2 * _am))[None, :]                   # 1 x 2BW, sharp step at center

def setcols(dst, x0, band):                                 # write 2BW-wide band at x0 (modular)
    for i in range(band.shape[1]):
        dst[:, (x0 + i) % Wtot] = band[:, i]


for r in range(12):                                         # seam to the LEFT of quarter r
    Lq, Rq = Q[(r - 1) % 12], Q[r]
    L_band = np.concatenate([Lq[:, W - BW:W], np.repeat(Lq[:, W - 1:W], BW, axis=1)], axis=1)
    R_band = np.concatenate([np.repeat(Rq[:, 0:1], BW, axis=1), Rq[:, 0:BW]], axis=1)
    mask = np.concatenate([np.zeros(BW), np.ones(BW)])[None, :].repeat(H, 0).astype(np.float32)
    rgb = pyr_blend(L_band[..., :3], R_band[..., :3], mask, LEVELS)
    a = L_band[..., 3] * (1 - sm) + R_band[..., 3] * sm     # alpha: linear smoothstep
    band = np.dstack([np.clip(rgb, 0, 255), np.clip(a, 0, 255)])
    setcols(strip, (r * W - BW) % Wtot, band)

# save full RGBA strip
Image.fromarray(np.clip(strip, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(HERE, 'strip_multiband.png'))
print(f'wrote wip/strip_multiband.png  {Wtot}x{H} RGBA')


def over(rgba):
    a = (rgba[..., 3:4] / 255.0)
    return (BG[None, None] * (1 - a) + rgba[..., :3] * a).astype(np.uint8)


# overview (composited, downscaled)
ovr = over(strip); oh = 240; ow = int(round(Wtot * oh / H))
Image.fromarray(ovr).resize((ow, oh), Image.LANCZOS).save(os.path.join(HERE, '_mb_overview.png'))
print(f'wrote wip/_mb_overview.png  {ow}x{oh}')

# seam zooms: hard-abut (top) vs multiband (bottom), for a few seams incl. wrap
ZW = 180
def zoom_at(arr_strip, xc):
    cols = [(xc + i) % Wtot for i in range(-ZW, ZW)]
    crop = arr_strip[:, cols]
    # vertical crop around mid horizon
    al = crop[..., 3] / 255.0; m = al > 0.5
    hz = np.where(m.any(0), m.argmax(0), H - 1)
    cy = int(np.median(hz)); y0, y1 = max(0, cy - 130), min(H, cy + 130)
    return over(crop[y0:y1])


labels = {0: 'J11 wrap 6br|1bl', 6: 'J5 3br|4bl', 7: 'J6 4bl|4br', 11: 'J10 6bl|6br'}
panels = []
for r, lab in labels.items():
    xc = r * W
    top = zoom_at(hard, xc); bot = zoom_at(strip, xc)
    h = min(top.shape[0], bot.shape[0])
    pair = np.concatenate([top[:h], np.full((h, 3, 3), 255, np.uint8), bot[:h]], axis=1)
    img = Image.fromarray(pair); from PIL import ImageDraw; ImageDraw.Draw(img).text((3, 3), lab + '  (L=hard, R=multiband)', fill=(255, 255, 0))
    panels.append(np.asarray(img))
ph = max(p.shape[0] for p in panels); pw = max(p.shape[1] for p in panels)
def pad(p):
    o = np.zeros((ph, pw, 3), np.uint8); o[:p.shape[0], :p.shape[1]] = p; return o
grid = np.concatenate([np.concatenate([pad(panels[i]), np.full((ph, 4, 3), 255, np.uint8)], 1) for i in range(len(panels))], 1)
Image.fromarray(grid).save(os.path.join(HERE, '_mb_seams.png'))
print('wrote wip/_mb_seams.png  (per-seam: left=hard abut, right=multiband)')
