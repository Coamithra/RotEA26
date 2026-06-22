"""Repack an upscaled+keyed sheet into the engine's grid, footprint-matched to the
ORIGINAL low-res sheet so it draws at the identical in-game size (with the engine
supersample factor = FACTOR). Frames are aligned to the original by max silhouette
overlap, packed FACTOR*48 px with 1px separators, straight alpha.

usage: python repack_for_engine.py <upscaled.png> <original.png> <factor> <crop> <out.png> [cols] [rows]

cols/rows default to 8/4 (ufosheet's landscape grid). For a portrait sheet like smallship
or mediumship (4 cols x 8 rows) pass `4 8`. Frame count (cols*rows) must stay 32.
"""
import sys, numpy as np
from PIL import Image
from keycompare import key_magenta

UP, ORIG = sys.argv[1], sys.argv[2]
FACTOR = int(sys.argv[3]); CROP = int(sys.argv[4]); OUT = sys.argv[5]
COLS = int(sys.argv[6]) if len(sys.argv) > 6 else 8
ROWS = int(sys.argv[7]) if len(sys.argv) > 7 else 4
DESIGN, SEP = 48, 1
FRAME = DESIGN * FACTOR

orig = np.asarray(Image.open(ORIG).convert("RGBA"))
gem = key_magenta(np.asarray(Image.open(UP).convert("RGB"))[:CROP])
H, W = gem.shape[:2]
def gcell(i):
    r, c = i//COLS, i % COLS
    return gem[round(r*H/ROWS):round((r+1)*H/ROWS), round(c*W/COLS):round((c+1)*W/COLS)]
def ocell(i):
    r, c = i//COLS, i % COLS
    return orig[r*49:r*49+48, c*49:c*49+48]

def bbmax(a, thr):
    m = a > thr
    if not m.any(): return 1
    ys, xs = np.where(m); return max(xs.max()-xs.min()+1, ys.max()-ys.min()+1)

o_med = np.median([bbmax(ocell(i)[:, :, 3].astype(float), 40)*FRAME/48 for i in range(32)])
g_med = np.median([bbmax(gcell(i)[:, :, 3].astype(float), 60) for i in range(32)])
SG = o_med / g_med

def place(rgba, dx, dy):
    im = Image.fromarray(rgba, "RGBA").resize(
        (max(1, round(rgba.shape[1]*SG)), max(1, round(rgba.shape[0]*SG))), Image.LANCZOS)
    cv = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    cv.alpha_composite(im, (round((FRAME-im.size[0])/2+dx), round((FRAME-im.size[1])/2+dy)))
    return cv

def best_shift(target, src):
    F = np.fft.rfft2(target); G = np.fft.rfft2(src)
    cc = np.fft.irfft2(F*np.conj(G), s=target.shape)
    py, px = np.unravel_index(int(np.argmax(cc)), cc.shape)
    if py > target.shape[0]//2: py -= target.shape[0]
    if px > target.shape[1]//2: px -= target.shape[1]
    return px, py
def overlap(a, b): return float(np.minimum(a, b).sum()/(np.maximum(a, b).sum()+1e-6))

frames = []
for i in range(32):
    Oa = np.asarray(Image.fromarray(ocell(i), "RGBA").resize((FRAME, FRAME), Image.LANCZOS))[:, :, 3].astype(np.float32)/255
    Ga = np.asarray(place(gcell(i), 0, 0))[:, :, 3].astype(np.float32)/255
    dx, dy = best_shift(Oa, Ga)
    sh = np.asarray(place(gcell(i), dx, dy))[:, :, 3].astype(np.float32)/255
    if overlap(Oa, sh) < overlap(Oa, Ga): dx, dy = 0, 0
    frames.append(place(gcell(i), dx, dy))

SW = COLS*FRAME + (COLS-1)*SEP
SH = ROWS*FRAME + (ROWS-1)*SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i, f in enumerate(frames):
    r, c = i//COLS, i % COLS
    sheet.paste(f, (c*(FRAME+SEP), r*(FRAME+SEP)))
sheet.save(OUT)

chk = (SW - (COLS-1)*SEP)//COLS
print(f"packed {OUT}  {SW}x{SH}  frame={FRAME}px x{FACTOR}  engine-recompute={chk} {'OK' if chk==FRAME else 'MISMATCH'}")
