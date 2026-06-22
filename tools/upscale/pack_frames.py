"""Pack a folder of individual animation frames (e.g. an AnimGen / WAN video export on a
magenta background) into the engine's grid sheet: key the magenta, footprint-match to an
original sprite * FACTOR, bbox-centre each frame in a uniform cell, pack COLS x ROWS with
1px separators and STRAIGHT alpha.

This is the cousin of repack_for_engine.py for the case where the source is N separate
frame_XXX.png files instead of one pre-laid-out magenta grid. Frames are selected EVENLY
across the export (np.linspace) so you can thin a 45-frame take down to a lean COLS*ROWS.

usage: python pack_frames.py <frames_dir> <orig.png> <factor> <cols> <rows> <out.png>
prints the cell (frame) size so you know design-frame-width = cellW / factor for the registry.
"""
import sys, glob, os, numpy as np
from PIL import Image
from keycompare import key_magenta

FRAMES_DIR, ORIG = sys.argv[1], sys.argv[2]
FACTOR = int(sys.argv[3]); COLS = int(sys.argv[4]); ROWS = int(sys.argv[5]); OUT = sys.argv[6]
SEP = 1
N = COLS * ROWS

files = sorted(glob.glob(os.path.join(FRAMES_DIR, "frame_*.png")))
if not files:
    files = sorted(glob.glob(os.path.join(FRAMES_DIR, "*.png")))
pick = [files[int(round(i))] for i in np.linspace(0, len(files) - 1, N)]
print(f"selected {N} of {len(files)} frames")

keyed = [key_magenta(np.asarray(Image.open(p).convert("RGB"))) for p in pick]


def bbox(a, thr=40):
    m = a[:, :, 3] > thr
    ys, xs = np.where(m)
    return xs.min(), ys.min(), xs.max(), ys.max()


bxs = [bbox(k) for k in keyed]
# footprint-match: median sprite WIDTH -> original sprite width * FACTOR
o = np.asarray(Image.open(ORIG).convert("RGBA"))
oys, oxs = np.where(o[:, :, 3] > 40)
ow = oxs.max() - oxs.min() + 1
SG = (ow * FACTOR) / np.median([b[2] - b[0] + 1 for b in bxs])

# uniform cell big enough for the largest scaled bbox + small pad, rounded to a multiple of FACTOR
maxw = max(b[2] - b[0] + 1 for b in bxs) * SG
maxh = max(b[3] - b[1] + 1 for b in bxs) * SG
pad = 0.06
cellW = int(np.ceil((maxw * (1 + pad)) / FACTOR) * FACTOR)
cellH = int(np.ceil((maxh * (1 + pad)) / FACTOR) * FACTOR)
print(f"footprint SG={SG:.3f}  cell={cellW}x{cellH}  design-frame-width={cellW // FACTOR}")

cells = []
for k, (x0, y0, x1, y1) in zip(keyed, bxs):
    crop = Image.fromarray(k[y0:y1 + 1, x0:x1 + 1], "RGBA")
    sw, sh = max(1, round(crop.size[0] * SG)), max(1, round(crop.size[1] * SG))
    crop = crop.resize((sw, sh), Image.LANCZOS)
    cv = Image.new("RGBA", (cellW, cellH), (0, 0, 0, 0))
    cv.alpha_composite(crop, (round((cellW - sw) / 2), round((cellH - sh) / 2)))  # bbox-centre
    cells.append(cv)

SW = COLS * cellW + (COLS - 1) * SEP
SH = ROWS * cellH + (ROWS - 1) * SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i, c in enumerate(cells):
    r, col = i // COLS, i % COLS
    sheet.paste(c, (col * (cellW + SEP), r * (cellH + SEP)))
sheet.save(OUT)
chkW = (SW - (COLS - 1) * SEP) // COLS
print(f"packed {OUT}  {SW}x{SH}  {COLS}x{ROWS}={N} frames  cell={cellW}x{cellH}  "
      f"recompute={chkW} {'OK' if chkW == cellW else 'MISMATCH'}  (<=4096? {max(SW,SH)<=4096})")
