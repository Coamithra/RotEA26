#!/usr/bin/env python
# Phase 0 (runs FIRST, on pristine hr/): magenta-screen MATTE (key + despill).
# Replaces the old sky_score soft-alpha + terrain_fill. Each edge pixel is
# C = alpha*F + (1-alpha)*M with M = the quarter's MEASURED magenta backdrop (ChatGPT's
# magenta is not pure and differs per quarter, esp. in blue -> measure, never assume).
#   beta  = clip(min(R-G, B-G)/SPAN, 0, 1)   # magenta axis; brown <=0 so untouched (LOOSE ok)
#   alpha = 1 - beta
#   F     = (C - beta*M)/alpha               # despill: recover true brown
#   pull residual -> column clean-brown; deep sky -> clean-brown so no magenta to bleed later
# Output: wip/matte/mars{n}_{h}.png (RGBA, brown + AA alpha) + a contact strip over checker.
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE); sys.path.insert(0, ROOT)
from stitch_lib import load, horizon, alpha_from   # noqa: E402

OUT = os.path.join(HERE, 'matte'); os.makedirs(OUT, exist_ok=True)
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
SPAN, KPULL, T0, EPS, BAND = 40.0, 2.0, 0.0, 0.05, 20


def matte(rgb):
    H, W = rgb.shape[:2]
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    magpix = (B - G > 200) & (R - G > 200)
    M = rgb[magpix].mean(0)
    mness = np.minimum(R - G, B - G)
    beta = np.clip((mness - T0) / SPAN, 0, 1)
    alpha = 1.0 - beta
    despill = (rgb - beta[..., None] * M) / np.clip(alpha, EPS, 1)[..., None]
    solid = mness < 0                                       # clean brown terrain
    fillcols = np.zeros((W, 3))
    for x in range(W):
        rows = np.where(solid[:, x])[0]
        if len(rows):
            top = rows[0]; sl = slice(top, top + BAND); bm = solid[sl, x]
            band = rgb[sl, x][bm]; fillcols[x] = np.median(band, 0) if len(band) else rgb[top, x]
        else:
            fillcols[x] = [150, 110, 75]
    fillimg = np.broadcast_to(fillcols[None, :, :], rgb.shape)
    pull = np.clip(beta * KPULL, 0, 1)[..., None]
    F = np.clip((1 - pull) * despill + pull * fillimg, 0, 255)
    return F, alpha, M, mness


def checker(w_, h_, s=14):
    yy, xx = np.mgrid[0:h_, 0:w_]; m = ((xx // s + yy // s) % 2) == 0
    o = np.empty((h_, w_, 3), np.uint8); o[m] = (60, 80, 100); o[~m] = (30, 40, 55); return o


cells = []
print('quarter   | measured M (R,G,B)   | rim alpha | interior erosion (deep px a<0.9) | residual mag px')
print('-' * 96)
for n, h in order:
    rgb = load(os.path.join(ROOT, 'hr', f'mars{n}_{h}.png'))
    H, W = rgb.shape[:2]
    F, alpha, M, mness = matte(rgb)
    Image.fromarray(np.dstack([F, np.clip(alpha, 0, 1) * 255.0]).astype(np.uint8), 'RGBA').save(
        os.path.join(OUT, f'mars{n}_{h}.png'))

    # verification metrics
    hz = horizon(alpha_from(rgb)); yy = np.arange(H)[:, None]
    deep = yy > (hz[None, :] + 120)
    eroded = (deep & (alpha < 0.9)).sum()
    rim = (mness > 20) & (mness < 200)
    resid = (((F[..., 2] > F[..., 1]) & (F[..., 0] > F[..., 1]) & (alpha > 0.3))).sum()
    print(f'mars{n}_{h:3}| ({M[0]:5.1f},{M[1]:4.1f},{M[2]:5.1f}) | {alpha[rim].mean():7.2f}   | '
          f'{eroded:6d} / {deep.sum():7d}  ({eroded/max(deep.sum(),1)*100:.3f}%)     | {resid}')

    # contact cell: matted over checker, downscaled
    al = np.clip(alpha, 0, 1)[..., None]
    comp = (checker(W, H).astype(np.float32) * (1 - al) + F * al).astype(np.uint8)
    hh = 200; ww = int(round(W * hh / H))
    cells.append(np.asarray(Image.fromarray(comp).resize((ww, hh), Image.LANCZOS)))
    cells.append(np.full((hh, 3, 3), 255, np.uint8))

strip = np.concatenate(cells[:-1], axis=1)
Image.fromarray(strip, 'RGB').save(os.path.join(HERE, '_matte_all_strip.png'))
print('\nwrote', os.path.join('wip', 'matte', 'mars{1..6}_{bl,br}.png'), '(12 RGBA mattes, SPAN=%g)' % SPAN)
print('wrote wip/_matte_all_strip.png  (all 12 over checker, ring order)')
