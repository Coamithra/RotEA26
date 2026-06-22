"""Repack an upscaled+keyed animated sheet using the DIFFERENCE-MINIMISATION stabiliser
(registration tier 3) instead of overlap-to-original. For sprites whose silhouette is
NOT a rigid body across the animation (a face/skull whose eyes+mouth morph, the player's
pulsing cross) — overlap-to-original wobbles, so each frame is instead sub-pixel aligned
(scale + dx + dy) to MINIMISE the SAD vs the previous frame, then the whole loop is
de-trended (mean scale/translation removed) so it stays centred. Frames are footprint-
matched to the original * FACTOR and bbox-centred for the initial placement; the diff-min
only does the fine per-frame correction. Premultiplied during the warp, un-premultiplied
to STRAIGHT alpha for the engine.

This is the generalised cousin of repack_player_engine.py (which is hardcoded to the
player sheet + its dark-panel feature-lock). Use repack_for_engine.py for rigid single-
blob sprites (overlap-to-original is simpler and exact there).

usage: python repack_diffmin.py <gen.png> <orig.png> <factor> <crop> <out.png> [cols] [rows]
cols/rows default to 8/4 (landscape grid). Frame count (cols*rows) must be 32.
"""
import sys, numpy as np, cv2
from PIL import Image
from keycompare import key_magenta

GEN, ORIG = sys.argv[1], sys.argv[2]
FACTOR = int(sys.argv[3]); CROP = int(sys.argv[4]); OUT = sys.argv[5]
COLS = int(sys.argv[6]) if len(sys.argv) > 6 else 8
ROWS = int(sys.argv[7]) if len(sys.argv) > 7 else 4
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


def bbmax(a, thr):
    m = a > thr
    if not m.any(): return 1
    ys, xs = np.where(m); return max(xs.max() - xs.min() + 1, ys.max() - ys.min() + 1)


o_med = np.median([bbmax(ocell(i)[:, :, 3].astype(float), 40) * FACTOR for i in range(N)])
g_med = np.median([bbmax(gcell(i)[:, :, 3].astype(float), 60) for i in range(N)])
SG = o_med / g_med
print(f"footprint scale SG={SG:.3f}  (orig~{o_med:.0f}px target / gem~{g_med:.0f}px)")


def stamp(i):
    cell = gcell(i)
    im = Image.fromarray(cell, "RGBA").resize(
        (max(1, round(cell.shape[1] * SG)), max(1, round(cell.shape[0] * SG))), Image.LANCZOS)
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.where(a > 40)
    cx = (xs.min() + xs.max()) / 2 if len(xs) else im.size[0] / 2  # bbox-centre (NOT centroid)
    cy = (ys.min() + ys.max()) / 2 if len(ys) else im.size[1] / 2
    cv = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    cv.alpha_composite(im, (round(FRAME / 2 - cx), round(FRAME / 2 - cy)))
    arr = np.asarray(cv).astype(np.float32); al = arr[:, :, 3:4] / 255.0
    return np.dstack([arr[:, :, :3] * al, arr[:, :, 3]]).astype(np.float32)  # premultiplied


raw = [stamp(i) for i in range(N)]


def warp(img, s, dx, dy):
    h, w = img.shape[:2]
    M = np.array([[s, 0, (1 - s) * w / 2 + dx], [0, s, (1 - s) * h / 2 + dy]], np.float32)
    return cv2.warpAffine(img, M, (w, h), flags=cv2.INTER_LINEAR, borderValue=0)


def sad(a, b): return float(np.abs(a - b).sum())


def best_fit(mov, ref, s0, dx0, dy0):
    best = (s0, dx0, dy0); bc = sad(warp(mov, s0, dx0, dy0), ref)
    for s in np.arange(s0 - 0.008, s0 + 0.0081, 0.004):
        for dx in np.arange(dx0 - 1.5, dx0 + 1.51, 0.25):
            for dy in np.arange(dy0 - 1.5, dy0 + 1.51, 0.25):
                c = sad(warp(mov, s, dx, dy), ref)
                if c < bc: bc, best = c, (s, dx, dy)
    return best


ff_before = sum(sad(raw[i], raw[i - 1]) for i in range(1, N))
T = [(1.0, 0.0, 0.0)]; pos = [raw[0]]
for i in range(1, N):
    s, dx, dy = best_fit(raw[i], pos[i - 1], *T[i - 1])
    T.append((s, dx, dy)); pos.append(warp(raw[i], s, dx, dy))
S = np.array([t[0] for t in T]); DX = np.array([t[1] for t in T]); DY = np.array([t[2] for t in T])
S /= np.exp(np.mean(np.log(S))); DX -= DX.mean(); DY -= DY.mean()  # de-trend: keep centred over the loop
final = [warp(raw[i], S[i], DX[i], DY[i]) for i in range(N)]
ff_after = sum(sad(final[i], final[i - 1]) for i in range(1, N))
print(f"frame-to-frame wobble: before={ff_before/1e6:.2f}M after={ff_after/1e6:.2f}M "
      f"({100*(1-ff_after/ff_before):.0f}% smoother)")

SW = COLS * FRAME + (COLS - 1) * SEP
SH = ROWS * FRAME + (ROWS - 1) * SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i, f in enumerate(final):
    a = np.clip(f[:, :, 3:4] / 255.0, 0, 1)
    rgb = np.where(a > 1e-4, f[:, :, :3] / np.maximum(a, 1e-4), 0)  # un-premultiply -> straight alpha
    straight = np.dstack([rgb.clip(0, 255), f[:, :, 3].clip(0, 255)]).astype(np.uint8)
    r, c = i // COLS, i % COLS
    sheet.paste(Image.fromarray(straight, "RGBA"), (c * (FRAME + SEP), r * (FRAME + SEP)))
sheet.save(OUT)
chk = (SW - (COLS - 1) * SEP) // COLS
print(f"packed {OUT}  {SW}x{SH}  frame={FRAME}px x{FACTOR}  engine-recompute={chk} {'OK' if chk == FRAME else 'MISMATCH'}")
