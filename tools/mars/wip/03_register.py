#!/usr/bin/env python
# Phase 3a (NEW first geometric transform): register each HD quarter to its ORIGINAL by a
# uniform vertical slide to best overlap. ChatGPT slid the ground UP on some quarters and
# fabricated new ground at the bottom; this undoes that per-quarter lift so each quarter sits
# at its true (original) vertical position. Best overlap = max silhouette IoU vs the original
# terrain mask. Result -> wip/registered/ (RGBA); the fabricated bottom falls off-frame.
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE); sys.path.insert(0, ROOT)
from stitch_lib import load   # noqa: E402

SRC = os.path.join(HERE, 'exposure')
OUT = os.path.join(HERE, 'registered'); os.makedirs(OUT, exist_ok=True)
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
DMAX = 90                                                   # search range, ORIGINAL px


def magmask(a):
    R, G, B = a[..., 0], a[..., 1], a[..., 2]
    return ~((R > 175) & (G < 95) & (B > 175))


def hz_of(mask):
    return np.where(mask.any(0), mask.argmax(0), mask.shape[0] - 1).astype(np.float32)


def vshift(img, D):                                        # uniform vertical slide (bilinear); +D = down
    H = img.shape[0]; src = np.arange(H) - D
    s0 = np.floor(src).astype(int); fr = src - s0
    s0c = np.clip(s0, 0, H - 1); s1c = np.clip(s0 + 1, 0, H - 1)
    f = fr[:, None, None]
    return img[s0c] * (1 - f) + img[s1c] * f


def vshift_mask(m, d):                                     # integer shift for IoU search (300-tall)
    out = np.zeros_like(m)
    if d >= 0: out[d:] = m[:m.shape[0] - d]
    elif d < 0: out[:m.shape[0] + d] = m[-d:]
    return out


print('quarter   | best d (orig px) | D (HD px) | IoU before -> after | horizon-median d (xcheck)')
print('-' * 86)
for n, h in order:
    arr = np.asarray(Image.open(os.path.join(SRC, f'mars{n}_{h}.png')).convert('RGBA')).astype(np.float32)
    Hh, Wh = arr.shape[:2]
    orig = load(os.path.join(ROOT, f'mars{n}_{h}.png'))
    Ho, Wo = orig.shape[:2]
    ratio = Hh / Ho

    om = magmask(orig)                                      # (Ho,Wo)
    hda = arr[..., 3] / 255.0
    hm = np.asarray(Image.fromarray((hda * 255).astype(np.uint8)).resize((Wo, Ho), Image.BILINEAR)).astype(np.float32) / 255 > 0.5

    iou0 = (hm & om).sum() / max((hm | om).sum(), 1)
    best_d, best_iou = 0, -1.0
    for d in range(-DMAX, DMAX + 1):
        s = vshift_mask(hm, d)
        iou = (s & om).sum() / max((s | om).sum(), 1)
        if iou > best_iou:
            best_iou, best_d = iou, d
    # cross-check: median horizon offset (orig px)
    hmed = float(np.median(hz_of(om) - hz_of(hm)))

    D_hd = best_d * ratio
    out = vshift(arr, D_hd)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, f'mars{n}_{h}.png'))
    arrow = 'down' if best_d > 0 else ('up' if best_d < 0 else 'none')
    print(f'mars{n}_{h:3}|   {best_d:+4d}  ({arrow:4})  | {D_hd:+7.1f}   | {iou0:.3f} -> {best_iou:.3f}      | {hmed:+.1f}')

print('\nwrote wip/registered/mars{1..6}_{bl,br}.png (RGBA, vertically registered to originals)')
