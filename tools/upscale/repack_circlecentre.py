"""Repack a CLEAN, already-registered animated sheet by footprint-match + CIRCLE-FIT centre,
WITHOUT the difference-min pass. For a radially-symmetric spinning sprite (the death star)
whose silhouette is a featureless circle: the source frames are already centred, and the
geometric centre of the circle is stable even as the interior core animates. So neither
SAD-vs-previous-frame diff-min (it chases the rotating surface -> ~5.6px wobble) nor bbox-
centre (pulled by the faint asymmetric glow -> ~2.3px wobble) is right; a least-squares
circle fit on the alpha silhouette edge pins the centre to <1px. Packs straight to the engine
grid (1px sep, straight alpha, footprint-matched to orig*FACTOR so on-screen size is identical).

usage: python repack_circlecentre.py <gen.png> <orig.png> <factor> <crop> <out.png> [cols] [rows] [circle|bbox]
"""
import sys, numpy as np
from PIL import Image
from keycompare import key_magenta


def circle_fit(a, default):
    """least-squares circle fit on the alpha>0.4 silhouette boundary -> (cx, cy, radius)."""
    m = a > 0.4
    if m.sum() < 30:
        return default
    b = np.zeros(m.shape, bool)
    b[1:-1, 1:-1] = m[1:-1, 1:-1] & ~(m[:-2, 1:-1] & m[2:, 1:-1] & m[1:-1, :-2] & m[1:-1, 2:])
    ys, xs = np.where(b)
    xs = xs.astype(float); ys = ys.astype(float)
    A = np.c_[2 * xs, 2 * ys, np.ones(len(xs))]
    sol, *_ = np.linalg.lstsq(A, xs ** 2 + ys ** 2, rcond=None)
    return sol[0], sol[1], np.sqrt(sol[2] + sol[0] ** 2 + sol[1] ** 2)

GEN, ORIG = sys.argv[1], sys.argv[2]
FACTOR = int(sys.argv[3]); CROP = int(sys.argv[4]); OUT = sys.argv[5]
COLS = int(sys.argv[6]) if len(sys.argv) > 6 else 8
ROWS = int(sys.argv[7]) if len(sys.argv) > 7 else 4
METHOD = sys.argv[8] if len(sys.argv) > 8 else "circle"   # circle (default) | bbox
DESIGN, SEP = 48, 1
FRAME = DESIGN * FACTOR
N = COLS * ROWS

gem = key_magenta(np.asarray(Image.open(GEN).convert("RGB"))[:CROP])
orig = np.asarray(Image.open(ORIG).convert("RGBA"))
H, W = gem.shape[:2]

def gcell(i):
    r, c = i // COLS, i % COLS
    return gem[round(r * H / ROWS):round((r + 1) * H / ROWS), round(c * W / COLS):round((c + 1) * W / COLS)]

def ocell(i):
    r, c = i // COLS, i % COLS
    return orig[r * 49:r * 49 + 48, c * 49:c * 49 + 48]

# Footprint match by CIRCLE-FIT DIAMETER (not bbox): for a sphere this is the true
# on-screen-size driver. Scaling the gen so its sphere diameter == orig diameter * FACTOR
# makes the new sphere's fraction-of-frame == the original's by construction, so the engine
# (textureScale = frameW/48) draws it at the identical on-screen size AND collision.
def diam(a):
    fit = circle_fit(a.astype(float) / a.max() if a.max() else a, None)
    return fit[2] * 2 if fit else 1.0

o_med = np.median([diam(ocell(i)[:, :, 3]) * FACTOR for i in range(N)])
g_med = np.median([diam(gcell(i)[:, :, 3]) for i in range(N)])
SG = o_med / g_med
print(f"footprint scale SG={SG:.4f}  (orig diam*{FACTOR}={o_med:.1f}px target / gem diam={g_med:.1f}px)")

SW = COLS * FRAME + (COLS - 1) * SEP
SH = ROWS * FRAME + (ROWS - 1) * SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i in range(N):
    cell = gcell(i)
    im = Image.fromarray(cell, "RGBA").resize(
        (max(1, round(cell.shape[1] * SG)), max(1, round(cell.shape[0] * SG))), Image.LANCZOS)
    a = np.asarray(im)[:, :, 3].astype(float) / 255.0
    if METHOD == "bbox":
        ys, xs = np.where(a > 40 / 255.0)                            # bbox centre (glow-sensitive)
        cx = (xs.min() + xs.max()) / 2 if len(xs) else im.size[0] / 2
        cy = (ys.min() + ys.max()) / 2 if len(ys) else im.size[1] / 2
    else:
        cx, cy, _ = circle_fit(a, (im.size[0] / 2, im.size[1] / 2, 0))   # circle-fit centre
    frame = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    frame.alpha_composite(im, (round(FRAME / 2 - cx), round(FRAME / 2 - cy)))
    r, c = i // COLS, i % COLS
    sheet.paste(frame, (c * (FRAME + SEP), r * (FRAME + SEP)))
sheet.save(OUT)
chk = (SW - (COLS - 1) * SEP) // COLS
print(f"packed {OUT}  {SW}x{SH}  frame={FRAME}px x{FACTOR}  engine-recompute={chk} {'OK' if chk == FRAME else 'MISMATCH'}")
