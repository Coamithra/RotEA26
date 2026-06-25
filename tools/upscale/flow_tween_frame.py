"""Replace ONE cell of a packed animation sheet with a MOTION-COMPENSATED tween of
its two neighbours, via dense optical flow (OpenCV Farneback) -- the same approach
as Fighter/scripts/make_seam_tween.py, adapted for the pre-registered RGBA sprite
cells here (no feet-align needed: cells are already bbox-centred on a common grid).

A straight alpha blend of two poses is a translucent double-exposure (ghost). This
instead estimates flow both ways, warps each neighbour HALFWAY toward the midpoint,
and blends the warps -- a real intermediate pose. Straight-alpha is premultiplied
for the warp/blend (so transparent regions don't bleed) and un-premultiplied back.

  python flow_tween_frame.py <sheet.png> <frame> <out.png> [cols rows sep t winsize]
"""
import sys, numpy as np, cv2
from PIL import Image

SHEET, FRAME, OUT = sys.argv[1], int(sys.argv[2]), sys.argv[3]
COLS = int(sys.argv[4]) if len(sys.argv) > 4 else 8
ROWS = int(sys.argv[5]) if len(sys.argv) > 5 else 4
SEP  = int(sys.argv[6]) if len(sys.argv) > 6 else 1
T    = float(sys.argv[7]) if len(sys.argv) > 7 else 0.5
WIN  = int(sys.argv[8]) if len(sys.argv) > 8 else 25     # Farneback winsize (make_seam_tween used 25)

im = np.asarray(Image.open(SHEET).convert("RGBA")).astype(np.uint8)
H, W = im.shape[:2]
cw = (W - (COLS - 1) * SEP) // COLS
ch = (H - (ROWS - 1) * SEP) // ROWS
N = COLS * ROWS


def box(i):
    r, c = i // COLS, i % COLS
    return slice(r * (ch + SEP), r * (ch + SEP) + ch), slice(c * (cw + SEP), c * (cw + SEP) + cw)


def cell(i):
    ys, xs = box(i)
    return im[ys, xs].astype(np.float32)


def premult(a):                                  # straight RGBA -> premultiplied RGBA (float)
    al = a[:, :, 3:4] / 255.0
    return np.dstack([a[:, :, :3] * al, a[:, :, 3]])


def gray(p):                                     # luma of premult RGB, uint8 for Farneback
    g = 0.299 * p[:, :, 0] + 0.587 * p[:, :, 1] + 0.114 * p[:, :, 2]
    return np.clip(g, 0, 255).astype(np.uint8)


def flow(g0, g1):
    # None initial-flow is the standard OpenCV call; cv2's type stubs are over-strict.
    return cv2.calcOpticalFlowFarneback(g0, g1, None, 0.5, 4, WIN, 5, 7, 1.5, 0)  # type: ignore[call-overload]


def warp_half(img, fl, t):                       # backward-remap: sample img along t*flow
    h, w = img.shape[:2]
    gx, gy = np.meshgrid(np.arange(w, dtype=np.float32), np.arange(h, dtype=np.float32))
    mapx = (gx + t * fl[..., 0]).astype(np.float32)
    mapy = (gy + t * fl[..., 1]).astype(np.float32)
    return cv2.remap(img, mapx, mapy, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)


A, B = cell((FRAME - 1) % N), cell((FRAME + 1) % N)
pA, pB = premult(A), premult(B)
gA, gB = gray(pA), gray(pB)
F_AB, F_BA = flow(gA, gB), flow(gB, gA)
wA = warp_half(pA, F_BA, T)                      # move A toward midpoint along reverse flow
wB = warp_half(pB, F_AB, 1 - T)
mid = (1 - T) * wA + T * wB                      # premultiplied midpoint

al = mid[:, :, 3:4]
rgb = np.where(al > 1e-4, mid[:, :, :3] / np.maximum(al / 255.0, 1e-4), 0)
out = np.clip(np.dstack([rgb, mid[:, :, 3]]), 0, 255).astype(np.uint8)

ys, xs = box(FRAME)
sheet = im.copy()
sheet[ys, xs] = out
Image.fromarray(sheet, "RGBA").save(OUT)
print(f"{OUT}: frame {FRAME} := Farneback flow tween(frame {(FRAME-1)%N}, frame {(FRAME+1)%N}, t={T}) "
      f"win={WIN}  cell={cw}x{ch}")
