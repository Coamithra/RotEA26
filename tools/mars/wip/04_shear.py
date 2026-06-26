#!/usr/bin/env python
# Phase 3b: per-junction linear shear, "shear the higher side DOWN" (0 OVL).
# At each seam compare the two edge horizons; the higher edge (smaller row) is sheared
# downward to meet the lower edge (the anchor), via a linear ramp that is 0 at the quarter's
# OPPOSITE edge. A quarter higher than both neighbours gets both a left- and a right-shear.
# Only ever shears DOWN (exposes sky at top; no bottom space needed). Loop-safe: every edge
# moves only down to its partner, no accumulation.
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'registered')
OUT = os.path.join(HERE, 'sheared'); os.makedirs(OUT, exist_ok=True)
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
K = 1                                                        # PURE edge: literal rightmost/leftmost pixel horizon (no averaging, no peak-resistance)


def hz_of(al, thr=0.5):
    m = al > thr
    return np.where(m.any(0), m.argmax(0), al.shape[0] - 1).astype(np.float32)


def warp_cols(img, shifts):                                 # per-column vertical shift (+down)
    H, W = img.shape[:2]; yy = np.arange(H); out = np.empty_like(img)
    for x in range(W):
        src = yy - shifts[x]; s0 = np.floor(src).astype(int); fr = src - s0
        s0c = np.clip(s0, 0, H - 1); s1c = np.clip(s0 + 1, 0, H - 1)
        out[:, x] = img[s0c, x] * (1 - fr)[:, None] + img[s1c, x] * fr[:, None]
    return out


Q, hz = [], []
for n, h in order:
    arr = np.asarray(Image.open(os.path.join(SRC, f'mars{n}_{h}.png')).convert('RGBA')).astype(np.float32)
    Q.append(arr); hz.append(hz_of(arr[..., 3] / 255.0))
widths = [q.shape[1] for q in Q]

leftShear = [0.0] * 12; rightShear = [0.0] * 12
print(' seam  pair        k.right  m.left   -> shear')
print(' ' + '-' * 52)
for j in range(12):
    k, m = j, (j + 1) % 12
    hkr = float(hz[k][-1]); hml = float(hz[m][0])           # literal edge-pixel horizon
    if hkr < hml:                                           # Q_k higher -> shear its RIGHT down
        rightShear[k] = hml - hkr; who = f'{order[k][0]}{order[k][1]}.R down {hml-hkr:.1f}'
    else:                                                   # Q_m higher -> shear its LEFT down
        leftShear[m] = hkr - hml; who = f'{order[m][0]}{order[m][1]}.L down {hkr-hml:.1f}'
    tag = '  (wrap)' if j == 11 else ''
    print(f' J{j:<2} {order[k][0]}{order[k][1]}|{order[m][0]}{order[m][1]}   {hkr:6.1f}  {hml:6.1f}   {who}{tag}')

sh_hz = []
for k, (n, h) in enumerate(order):
    W = widths[k]; t = np.arange(W) / (W - 1)
    shift = leftShear[k] * (1 - t) + rightShear[k] * t
    out = warp_cols(Q[k], shift)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, f'mars{n}_{h}.png'))
    sh_hz.append(hz_of(out[..., 3] / 255.0))

print('\nresidual edge-pixel step per seam (after shear):')
for j in range(12):
    k, m = j, (j + 1) % 12
    r = float(sh_hz[k][-1] - sh_hz[m][0])
    print(f'  J{j:<2} {order[k][0]}{order[k][1]}|{order[m][0]}{order[m][1]}  {r:+.1f}')
print('\nwrote wip/sheared/mars{1..6}_{bl,br}.png (RGBA, 0-OVL edge shear, higher side down)')
