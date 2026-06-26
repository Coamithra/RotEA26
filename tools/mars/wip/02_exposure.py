#!/usr/bin/env python
# Phase 2 (after matte+recolour): global ring exposure compensation on the matted+recoloured
# foreground. Per-quarter, per-channel multiplicative gain that minimises all 12 junction
# tone-diffs simultaneously (log-domain circular least-squares; lstsq min-norm => sum(ln g)=0
# => brightness preserved). Alpha (matte) is the terrain mask + carried through unchanged.
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'recolor')
OUT = os.path.join(HERE, 'exposure'); os.makedirs(OUT, exist_ok=True)
OVL = 100
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
LUMW = np.array([0.299, 0.587, 0.114])


def band_mean(rgb, al, cols):                     # terrain mean over a column band (alpha>0.5)
    sub = rgb[:, cols]; m = al[:, cols] > 0.5
    return sub[m].mean(0)


Q = []
for n, h in order:
    arr = np.asarray(Image.open(os.path.join(SRC, f'mars{n}_{h}.png')).convert('RGBA')).astype(np.float32)
    Q.append({'rgb': arr[..., :3], 'al': arr[..., 3] / 255.0, 'w': arr.shape[1]})
W = [q['w'] for q in Q]

# measure junctions (A = left quarter right band, B = right quarter left band)
A = np.zeros((12, 3)); B = np.zeros((12, 3))
for j in range(12):
    k, m = j, (j + 1) % 12
    A[j] = band_mean(Q[k]['rgb'], Q[k]['al'], slice(W[k] - OVL, W[k]))
    B[j] = band_mean(Q[m]['rgb'], Q[m]['al'], slice(0, OVL))

# per-channel circular least-squares for log-gains
M = np.zeros((12, 12))
for j in range(12):
    M[j, j] = 1.0; M[j, (j + 1) % 12] = -1.0
gains = np.zeros((12, 3))
for c in range(3):
    d = np.log(B[:, c]) - np.log(A[:, c])
    a, *_ = np.linalg.lstsq(M, d, rcond=None)
    gains[:, c] = np.exp(a)


def whole_lum(rgb, al):
    m = al > 0.5; return (rgb[m] @ LUMW).mean()


print('quarter   | gain R,G,B           | lum before -> after')
print('-' * 56)
for (n, h), q, g in zip(order, Q, gains):
    out = np.clip(q['rgb'] * g, 0, 255)
    Image.fromarray(np.dstack([out, q['al'] * 255.0]).astype(np.uint8), 'RGBA').save(os.path.join(OUT, f'mars{n}_{h}.png'))
    q['out'] = out
    print(f'mars{n}_{h:3}| ({g[0]:.3f},{g[1]:.3f},{g[2]:.3f}) | {whole_lum(q["rgb"],q["al"]):6.1f} -> {whole_lum(out,q["al"]):6.1f}')


def edge_steps(key):
    steps = []
    for j in range(12):
        k, m = j, (j + 1) % 12
        a = band_mean(Q[k][key], Q[k]['al'], slice(W[k] - OVL, W[k])) @ LUMW
        b = band_mean(Q[m][key], Q[m]['al'], slice(0, OVL)) @ LUMW
        steps.append(b - a)
    return np.array(steps)


sb, sa = edge_steps('rgb'), edge_steps('out')
print('\njunction edge-band luminance step (B - A):')
for j in range(12):
    k, m = order[j], order[(j + 1) % 12]
    tag = '  <- WRAP' if j == 11 else ''
    print(f'{j:2}  {k[0]}{k[1]}->{m[0]}{m[1]}  {sb[j]:7.1f} {sa[j]:7.1f}{tag}')
print(f'\nRMS junction step: before {np.sqrt((sb**2).mean()):.2f} -> after {np.sqrt((sa**2).mean()):.2f}'
      f'   max|step|: {np.abs(sb).max():.1f} -> {np.abs(sa).max():.1f}')

# 12+1 wrap-check strip (composite over dark grey)
seq = order + [order[0]]
H = Q[0]['rgb'].shape[0]; hh = 200
cells = []
qmap = {(n, h): q for (n, h), q in zip(order, Q)}
for i, (n, h) in enumerate(seq):
    q = qmap[(n, h)]; rgb, al = q['out'], q['al']
    ww = int(round(rgb.shape[1] * hh / H))
    im = np.asarray(Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).resize((ww, hh), Image.LANCZOS), np.float32)
    a = np.asarray(Image.fromarray((np.clip(al, 0, 1) * 255).astype(np.uint8)).resize((ww, hh), Image.LANCZOS), np.float32) / 255
    base = np.array([35, 35, 40], np.float32)[None, None] * np.ones((hh, ww, 1), np.float32)
    comp = (base * (1 - a[..., None]) + im * a[..., None]).astype(np.uint8)
    col = (255, 60, 60) if i == len(seq) - 2 else (90, 90, 90)
    cells.append(comp); cells.append(np.full((hh, 3, 3), col, np.uint8))
Image.fromarray(np.concatenate(cells[:-1], 1), 'RGB').save(os.path.join(HERE, '_exposure_ring_12plus1.png'))
print('\nwrote wip/exposure/mars{1..6}_{bl,br}.png (RGBA)  +  wip/_exposure_ring_12plus1.png (red bar = wrap)')
