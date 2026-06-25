"""Replace ONE cell of a packed animation sheet with the half-way tween of its two
neighbours (frame i := lerp(frame i-1, frame i+1, 0.5)). For patching a single
flickery/duplicate frame in an otherwise-good sheet without re-running the whole
repack. Straight-alpha in, straight-alpha out.

  python interp_frame.py <sheet.png> <frame> <out.png> [cols rows sep mode]

mode = premult (default; blend in premultiplied space -> clean edges, matches what
the interpolate shader looks like in the opaque interior) | straight (raw RGBA lerp,
exactly what sprite.fx INTERPOLATE does: lerp(texA, texB, 0.5)).
"""
import sys, numpy as np
from PIL import Image

SHEET, FRAME, OUT = sys.argv[1], int(sys.argv[2]), sys.argv[3]
COLS = int(sys.argv[4]) if len(sys.argv) > 4 else 8
ROWS = int(sys.argv[5]) if len(sys.argv) > 5 else 4
SEP  = int(sys.argv[6]) if len(sys.argv) > 6 else 1
MODE = sys.argv[7] if len(sys.argv) > 7 else "premult"

im = np.asarray(Image.open(SHEET).convert("RGBA")).astype(np.float32)
H, W = im.shape[:2]
cw = (W - (COLS - 1) * SEP) // COLS
ch = (H - (ROWS - 1) * SEP) // ROWS
N = COLS * ROWS


def box(i):
    r, c = i // COLS, i % COLS
    return slice(r * (ch + SEP), r * (ch + SEP) + ch), slice(c * (cw + SEP), c * (cw + SEP) + cw)


def cell(i):
    ys, xs = box(i)
    return im[ys, xs].copy()


a, b = cell((FRAME - 1) % N), cell((FRAME + 1) % N)

if MODE == "straight":
    out = 0.5 * a + 0.5 * b                                   # raw RGBA lerp (shader-exact)
else:                                                         # premultiplied lerp (clean edges)
    pa = np.dstack([a[:, :, :3] * (a[:, :, 3:4] / 255.0), a[:, :, 3]])
    pb = np.dstack([b[:, :, :3] * (b[:, :, 3:4] / 255.0), b[:, :, 3]])
    p = 0.5 * pa + 0.5 * pb
    al = p[:, :, 3:4] / 255.0                                 # normalise once so mask + divisor share a scale
    rgb = np.where(al > 1e-4, p[:, :, :3] / np.maximum(al, 1e-4), 0)
    out = np.dstack([rgb, p[:, :, 3]])

out = np.clip(out, 0, 255).astype(np.uint8)
ys, xs = box(FRAME)
sheet = im.copy().astype(np.uint8)
sheet[ys, xs] = out
Image.fromarray(sheet, "RGBA").save(OUT)
print(f"{OUT}: frame {FRAME} := lerp(frame {(FRAME-1)%N}, frame {(FRAME+1)%N}, 0.5)  [{MODE}]  cell={cw}x{ch}")
