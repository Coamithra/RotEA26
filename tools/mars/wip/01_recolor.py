#!/usr/bin/env python
# Phase 1 (after the matte): Recolour the MATTED foreground -> its low-res original.
# Per channel, match the matted terrain mean+std (alpha>0.9 = solid terrain) to the
# original quarter's terrain (magenta-detect mask, since the low-res originals are still
# magenta-backed). Reinhard transfer; alpha carried through unchanged. No magenta to force.
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE); sys.path.insert(0, ROOT)
from stitch_lib import load   # noqa: E402

SRC = os.path.join(HERE, 'matte')
OUT = os.path.join(HERE, 'recolor'); os.makedirs(OUT, exist_ok=True)
order = [(n, h) for n in range(1, 7) for h in ('bl', 'br')]
LUMW = np.array([0.299, 0.587, 0.114])


def magmask(a):                                   # terrain = NOT magenta (for the originals)
    R, G, B = a[..., 0], a[..., 1], a[..., 2]
    return ~((R > 175) & (G < 95) & (B > 175))


def overlay(rgb, al, h=130, gap=3, bg=(35, 35, 40)):
    w = max(1, int(round(rgb.shape[1] * h / rgb.shape[0])))
    im = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)
    a = np.asarray(Image.fromarray((np.clip(al, 0, 1) * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    base = np.array(bg, np.float32)[None, None, :] * np.ones((h, w, 1), np.float32)
    comp = (base * (1 - a[..., None]) + np.asarray(im, np.float32) * a[..., None]).astype(np.uint8)
    return np.concatenate([comp, np.full((h, gap, 3), 255, np.uint8)], axis=1)


bef, aft = [], []
print(f"{'quarter':10} | orig terrain mean      | after recolour (should match)")
print('-' * 64)
for n, h in order:
    arr = np.asarray(Image.open(os.path.join(SRC, f'mars{n}_{h}.png')).convert('RGBA')).astype(np.float32)
    F, al = arr[..., :3], arr[..., 3] / 255.0
    orig = load(os.path.join(ROOT, f'mars{n}_{h}.png'))
    ht, ot = al > 0.9, magmask(orig)
    out = F.copy()
    for c in range(3):
        hm, hs = F[ht][:, c].mean(), F[ht][:, c].std() + 1e-6
        om, os_ = orig[ot][:, c].mean(), orig[ot][:, c].std() + 1e-6
        out[..., c] = (F[..., c] - hm) * (os_ / hs) + om
    out = np.clip(out, 0, 255)
    Image.fromarray(np.dstack([out, al * 255.0]).astype(np.uint8), 'RGBA').save(os.path.join(OUT, f'mars{n}_{h}.png'))

    om = np.array([orig[ot][:, c].mean() for c in range(3)])
    am = np.array([out[ht][:, c].mean() for c in range(3)])
    print(f"mars{n}_{h:3} | ({om[0]:5.1f},{om[1]:5.1f},{om[2]:5.1f})        | ({am[0]:5.1f},{am[1]:5.1f},{am[2]:5.1f})")
    bef.append(overlay(F, al)); aft.append(overlay(out, al))

W = min(min(b.shape[1] for b in bef), min(a.shape[1] for a in aft))
sep = np.full((10, W, 3), 0, np.uint8)
combo = np.concatenate([np.concatenate(bef, 1)[:, :W], sep, np.concatenate(aft, 1)[:, :W]], 0)
Image.fromarray(combo, 'RGB').save(os.path.join(HERE, '_recolor_compare.png'))
print('\nwrote wip/recolor/mars{1..6}_{bl,br}.png (RGBA)  +  wip/_recolor_compare.png (top=matte, bottom=recoloured)')
