"""Slice a grid of magenta-background asteroid variants, key them, and footprint-match each
to the ORIGINAL asteroid design * FACTOR -- i.e. the same recipe that made asteroid2 HD
(silhouette = orig_silhouette*3, canvas = orig_canvas*3), but at a LOWER factor so the
"normal" asteroids (drawn at scale 0.45) aren't massively oversampled.

The big asteroid keeps the 3x asteroid2; these small ones are for the random normal pool.
Single still frame each (rotation is a draw-time transform), straight alpha, design width =
orig canvas width (registered in AlienDrawableGameComponent.DesignFrameWidth), so on-screen
size + collision are IDENTICAL to the old Asteroid1..4 while using far fewer texels.

usage: python pack_small_asteroids.py <grid.png> <orig.png> <cols> <rows> <factor> <out_prefix>
  e.g. python pack_small_asteroids.py ../../new_assets_raw/asteroids_hd_smaller.png \
       ../../web/.../sprites/asteroid2.png.orig 2 2 1.5 out/asteroidsmall
writes <out_prefix>1.png .. <out_prefix>N.png (+ _preview.png on a checker).
"""
import sys, numpy as np
from PIL import Image
from keycompare import key_magenta

GRID, ORIG = sys.argv[1], sys.argv[2]
COLS, ROWS = int(sys.argv[3]), int(sys.argv[4])
FACTOR = float(sys.argv[5])
OUTP = sys.argv[6]


def sbbox(a, thr=40):
    ys, xs = np.where(a > thr)
    return xs.min(), ys.min(), xs.max(), ys.max()


orig = np.asarray(Image.open(ORIG).convert("RGBA"))
oH, oW = orig.shape[:2]
ox0, oy0, ox1, oy1 = sbbox(orig[:, :, 3])
osw, osh = ox1 - ox0 + 1, oy1 - oy0 + 1            # orig silhouette w,h
canW, canH = round(oW * FACTOR), round(oH * FACTOR)  # target canvas = orig canvas * factor
tsw, tsh = osw * FACTOR, osh * FACTOR                 # target silhouette = orig silhouette * factor
print(f"orig canvas {oW}x{oH} silhouette {osw}x{osh}  ->  canvas {canW}x{canH} silhouette ~{tsw:.0f}x{tsh:.0f} (x{FACTOR})")

g = np.asarray(Image.open(GRID).convert("RGB"))
H, W = g.shape[:2]
cells = []
for r in range(ROWS):
    for c in range(COLS):
        quad = g[r * H // ROWS:(r + 1) * H // ROWS, c * W // COLS:(c + 1) * W // COLS]
        k = key_magenta(quad)
        x0, y0, x1, y1 = sbbox(k[:, :, 3])
        sil = k[y0:y1 + 1, x0:x1 + 1]
        sw, sh = sil.shape[1], sil.shape[0]
        sc = min(tsw / sw, tsh / sh)                 # footprint-match silhouette to orig*factor
        nw, nh = max(1, round(sw * sc)), max(1, round(sh * sc))
        im = Image.fromarray(sil, "RGBA").resize((nw, nh), Image.Resampling.LANCZOS)
        cv = Image.new("RGBA", (canW, canH), (0, 0, 0, 0))
        cv.alpha_composite(im, (round((canW - nw) / 2), round((canH - nh) / 2)))
        cells.append(cv)

for i, cv in enumerate(cells, 1):
    cv.save(f"{OUTP}{i}.png")
    print(f"  wrote {OUTP}{i}.png  {cv.size}")

# checker preview strip
Z = canH; gap = 6
strip = Image.new("RGBA", (len(cells) * (canW + gap) - gap, canH), (0, 0, 0, 0))
for i, cv in enumerate(cells):
    strip.alpha_composite(cv, (i * (canW + gap), 0))
arr = np.asarray(strip).astype(float); sa = arr[:, :, 3:4] / 255
yy, xx = np.mgrid[0:strip.size[1], 0:strip.size[0]]
bg = np.where(((xx // 16 + yy // 16) % 2)[..., None] == 0, 70, 120).astype(float) * np.ones((1, 1, 3))
Image.fromarray((arr[:, :, :3] * sa + bg * (1 - sa)).astype(np.uint8), "RGB").save(f"{OUTP}_preview.png")
print(f"  preview {OUTP}_preview.png")
