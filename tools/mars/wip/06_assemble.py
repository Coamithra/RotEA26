#!/usr/bin/env python
# Final assembler: graph-cut + adaptive feather at EVERY seam, with PER-JUNCTION OVL scaled to
# roughness. Each seam: probe cut-cost -> pick OVL in [OVL_MIN..OVL_MAX] (small where it fits,
# wide where it doesn't); low-freq tone pre-match (multiband-style "free" similarity); min-cost
# connected seam (routes around structure); adaptive feather width = b*FMAX*OVL (bounded to the
# overlap so we never go out of bounds); RGB feathered, ALPHA kept sharp (no floating-terrain
# streaks). Modular wrap. Outputs the looping RGBA strip + overview + per-seam zoom grid.
import os, sys
import numpy as np
import cv2
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from stitch_lib import pyr_blend   # noqa: E402  (Laplacian-pyramid blend for the OVL=0 seams)
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
OVL_MIN, OVL_MAX, OVL_PROBE = 5, 80, 120
D_LOW, D_HIGH, FMAX = 6.0, 35.0, 1.0
GAMMA = 2.0                                                  # roughness->OVL curve (data-driven min/max; >1 keeps good seams small)
BG = np.array([45, 55, 70], np.float32)

Q = [np.asarray(Image.open(os.path.join(HERE, 'sheared', f'mars{n}_{h}.png')).convert('RGBA')).astype(np.float32) for n, h in order]
H, W = Q[0].shape[:2]


def color_diff(A, B):
    dR, dG, dB = A[..., 0] - B[..., 0], A[..., 1] - B[..., 1], A[..., 2] - B[..., 2]
    return np.sqrt(2 * dR**2 + 4 * dG**2 + 3 * dB**2)


def dp_seam(cost):
    Hh, O = cost.shape; M = cost.copy(); back = np.zeros((Hh, O), int); idx = np.arange(O)
    for y in range(1, Hh):
        prev = M[y - 1]; left = np.r_[prev[0], prev[:-1]]; right = np.r_[prev[1:], prev[-1]]
        stk = np.vstack([left, prev, right]); ch = np.argmin(stk, 0)
        M[y] += stk[ch, idx]; back[y] = np.clip(idx + (ch - 1), 0, O - 1)
    seam = np.zeros(Hh, int); seam[-1] = int(np.argmin(M[-1]))
    for y in range(Hh - 1, 0, -1):
        seam[y - 1] = back[y, seam[y]]
    return seam


def smv(x, k):
    k = int(k) | 1; return np.convolve(np.pad(x, k // 2, 'edge'), np.ones(k) / k, 'valid')


def vsmooth_cols(x, k):                                      # smooth an Hx3 array per channel along rows
    k = int(k) | 1; pad = np.pad(x, ((k // 2, k // 2), (0, 0)), 'edge'); ker = np.ones(k) / k
    return np.stack([np.convolve(pad[:, c], ker, 'valid') for c in range(x.shape[1])], 1)


def poisson_seam(strip_in, c, Wt, width=150, vk=21):        # final membrane tone-level across one seam, `width` px, terrain-masked
    W2 = width // 2
    cols = [(c - W2 + i) % Wt for i in range(2 * W2)]
    rgb = strip_in[:, cols, :3]; a = (strip_in[:, cols, 3] / 255.0)[..., None]
    offset = vsmooth_cols(rgb[:, W2:].mean(1) - rgb[:, :W2].mean(1), vk)   # right level - left level, per row
    fac = np.empty(2 * W2)
    fac[:W2] = 0.5 * (np.arange(W2) / W2)                                  # +0..+0.5 into left, pinned at far edge
    fac[W2:] = -0.5 * (1 - np.arange(W2) / max(W2 - 1, 1))                 # -0.5..0 out of right, pinned at far edge
    corr = fac[None, :, None] * offset[:, None, :] * a                     # cancels the seam step, fades to 0, terrain only
    strip_in[:, cols, :3] = np.clip(rgb + corr, 0, 255)


def multiband_edges(L, R, BW=120, levels=5):                # STEP 1: multiband the two quarters' abutting edges IN PLACE (RGB; alpha untouched)
    A_real = L[:, -BW:, :3]; B_real = R[:, :BW, :3]
    A = np.concatenate([A_real, np.repeat(A_real[:, -1:], BW, axis=1)], axis=1)   # left edge, extended right
    B = np.concatenate([np.repeat(B_real[:, :1], BW, axis=1), B_real], axis=1)    # right edge, extended left
    mask = np.concatenate([np.zeros(BW), np.ones(BW)])[None, :].repeat(L.shape[0], 0).astype(np.float32)
    blend = pyr_blend(A, B, mask, levels)                   # low-freq cross-faded, each side's high-freq kept
    L[:, -BW:, :3] = np.clip(blend[:, :BW], 0, 255); R[:, :BW, :3] = np.clip(blend[:, BW:], 0, 255)


def seam_blend(L, R, ovl):
    Lov, Rov = L[:, -ovl:], R[:, :ovl]                       # candidates already multiband-tone-matched in step 1; just cut + feather
    d = color_diff(Lov[..., :3], Rov[..., :3])
    seam = np.argmin(d, 1)                                                         # per-row independent best
    db = smv(d[np.arange(H), seam], 25); xb = smv(seam.astype(float), 25)          # vertically blended (smoothed) to stay coherent
    b = np.clip((db - D_LOW) / (D_HIGH - D_LOW), 0, 1)
    wdt = np.clip(b * FMAX * ovl, 1.0, ovl)
    ctr = xb * (1 - b) + (ovl / 2) * b
    xx = np.arange(ovl)[None, :]
    e0 = (ctr - wdt / 2)[:, None]; e1 = (ctr + wdt / 2)[:, None]
    t = np.clip((xx - e0) / np.maximum(e1 - e0, 1e-6), 0, 1); m = (t * t * (3 - 2 * t))[..., None]
    rgb = Lov[..., :3] * (1 - m) + Rov[..., :3] * m
    a = np.minimum(Lov[..., 3], Rov[..., 3])                # per-pixel MOST TRANSPARENT wins (intersection silhouette)
    return np.dstack([rgb, a]), db


# --- per-seam OVL: MANUAL override (this one-off) or auto roughness ---
MANUAL_OVL = [0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 80, 200]      # J0..J11 (hand-tuned)
if MANUAL_OVL is not None:
    OVL = list(MANUAL_OVL); rough = [0.0] * 12
else:
    rough = []
    for j in range(12):
        k, m = j, (j + 1) % 12
        _, db = seam_blend(Q[k], Q[m], OVL_PROBE)
        terr = Q[k][:, -OVL_PROBE:, 3].max(1) > 127
        rough.append(float(db[terr].mean()) if terr.any() else 0.0)
    rlo, rhi = min(rough), max(rough)
    OVL = [int(round(OVL_MIN + (np.clip((r - rlo) / max(rhi - rlo, 1e-6), 0, 1) ** GAMMA) * (OVL_MAX - OVL_MIN))) for r in rough]

# === STEP 1: multiband ALL seams (edge-width covers each overlap) -> step 2 cuts multiband'ed textures ===
for j in range(12):
    multiband_edges(Q[j], Q[(j + 1) % 12], BW=max(120, OVL[j] + 20))

pos = [0]
for k in range(1, 12):
    pos.append(pos[-1] + W - OVL[k - 1])
Wtot = 12 * W - sum(OVL)
print('seam  pair        roughness  OVL')
for j in range(12):
    k, m = order[j], order[(j + 1) % 12]
    print(f' J{j:<2} {k[0]}{k[1]}|{m[0]}{m[1]}      {rough[j]:6.1f}   {OVL[j]:4d}')
print(f'Wtot={Wtot} (vs 0-OVL 19428; lost {19428-Wtot} to overlaps)')

strip = np.zeros((H, Wtot, 4), np.float32)
def setcols(start, band):
    for i in range(band.shape[1]):
        strip[:, (start + i) % Wtot] = band[:, i]

for k in range(12):                                         # private (non-overlap) regions
    lov, rov = OVL[(k - 1) % 12], OVL[k]
    setcols((pos[k] + lov) % Wtot, Q[k][:, lov:W - rov])
for j in range(12):                                         # STEP 2: OVL>0 seams -> slide-and-graph-cut on the multiband'ed edges
    if OVL[j] <= 0:                                          # OVL=0 -> already multiband'ed by step 1, just abuts (privates)
        continue
    k, m = j, (j + 1) % 12
    blended, _ = seam_blend(Q[k], Q[m], OVL[j])
    setcols(pos[m] % Wtot, blended)

Image.fromarray(np.clip(strip, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(HERE, 'strip_graphcut.png'))
print('wrote wip/strip_graphcut.png', Wtot, 'x', H)


def over(rgba):
    a = rgba[..., 3:4] / 255.0; return (BG[None, None] * (1 - a) + rgba[..., :3] * a).astype(np.uint8)


oh = 240; ow = int(round(Wtot * oh / H))
Image.fromarray(over(strip)).resize((ow, oh), Image.LANCZOS).save(os.path.join(HERE, '_gc_overview.png'))

# marked massive composite (+ wrap repeat): green line at each seam's overlap centre + OVL label
fullc = over(strip); full = np.concatenate([fullc, fullc[:, :W]], axis=1)
fimg = Image.fromarray(full); fd = ImageDraw.Draw(fimg)
for j in range(12):
    cx = (pos[(j + 1) % 12] + OVL[j] // 2) % Wtot
    for xx in [cx] + ([cx + Wtot] if cx < W else []):
        fd.line([(xx, 0), (xx, 60)], fill=(0, 255, 0), width=2)
        fd.text((xx + 4, 6), f'J{j} OVL{OVL[j]}', fill=(0, 255, 0))
fimg.save(os.path.join(HERE, '_gc_full.png'))
print('wrote wip/_gc_full.png', full.shape[1], 'x', full.shape[0])

# per-seam zoom grid (the blended overlap + context), 3x4
cells = []
for j in range(12):
    m = (j + 1) % 12; ovl = OVL[j]; cx = pos[m] + ovl // 2
    cols = [(cx + i) % Wtot for i in range(-150, 150)]
    crop = over(strip[:, cols])
    al = strip[:, cols, 3] / 255.0; mm = al > 0.5
    hz = np.where(mm.any(0), mm.argmax(0), H - 1); cy = int(np.median(hz))
    y0, y1 = max(0, cy - 130), min(H, cy + 130)
    img = Image.fromarray(crop[y0:y1]); d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 150, 16], fill=(0, 0, 0))
    d.text((3, 3), f'J{j} {order[j][0]}{order[j][1]}|{order[m][0]}{order[m][1]} OVL{ovl} r{rough[j]:.0f}', fill=(255, 255, 0))
    cells.append(np.asarray(img))
cw = max(c.shape[1] for c in cells); ch = max(c.shape[0] for c in cells)
def pad(c):
    o = np.zeros((ch, cw, 3), np.uint8); o[:c.shape[0], :c.shape[1]] = c; return o
rows = [np.concatenate([np.concatenate([pad(cells[r * 3 + cc]), np.full((ch, 4, 3), 255, np.uint8)], 1) for cc in range(3)], 1) for r in range(4)]
grid = np.concatenate([np.concatenate([gr, np.full((4, rows[0].shape[1], 3), 255, np.uint8)], 0) for gr in rows], 0)
Image.fromarray(grid).save(os.path.join(HERE, '_gc_junctions.png'))
print('wrote wip/_gc_overview.png + wip/_gc_junctions.png')
print('FINAL = strip_graphcut.png (multiband all seams -> slide-and-cut OVL>0; no Poisson)')
