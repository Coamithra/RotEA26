"""Repack the player sheet for the engine: footprint-matched to the original at 4x
AND smoothed with the difference-minimization alignment (per-frame sub-pixel scale +
translation that minimizes SAD vs the previous frame). Packs 192px frames, 1px
separators, STRAIGHT alpha (what the engine expects).
"""
import numpy as np, cv2
from PIL import Image
from keycompare import key_magenta

COLS, ROWS, DESIGN, SEP, FACTOR = 8, 4, 48, 1, 4
FRAME = DESIGN * FACTOR  # 192
gem = key_magenta(np.asarray(Image.open("playership_gemini.png").convert("RGB"))[:639])
orig = np.asarray(Image.open("../../web/EvilAliensWeb/wwwroot/Content/gfx/sprites/playersheet.png").convert("RGBA"))
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

# footprint-match scale: gemini sprite -> original sprite size * FACTOR
o_med = np.median([bbmax(ocell(i)[:, :, 3].astype(float), 40)*FACTOR for i in range(32)])
g_med = np.median([bbmax(gcell(i)[:, :, 3].astype(float), 60) for i in range(32)])
SG = o_med / g_med
print(f"footprint scale SG={SG:.3f}  (orig~{o_med:.0f}px target / gem~{g_med:.0f}px)")

def panel_center(im):
    a = np.asarray(im); R, G, B, A = [a[:, :, k].astype(int) for k in range(4)]
    dark = (A > 120) & (np.maximum.reduce([R, G, B]) < 80)
    if dark.sum() < 30: return im.size[0]/2, im.size[1]/2
    ys, xs = np.where(dark); return (xs.min()+xs.max())/2, (ys.min()+ys.max())/2

def stamp(i):
    im = Image.fromarray(gcell(i), "RGBA").resize(
        (round(gcell(i).shape[1]*SG), round(gcell(i).shape[0]*SG)), Image.LANCZOS)
    cx, cy = panel_center(im)
    cv = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    cv.alpha_composite(im, (round(FRAME/2-cx), round(FRAME/2-cy)))
    arr = np.asarray(cv).astype(np.float32); a = arr[:, :, 3:4]/255.0
    return np.dstack([arr[:, :, :3]*a, arr[:, :, 3]]).astype(np.float32)  # premultiplied

raw = [stamp(i) for i in range(32)]
def warp(img, s, dx, dy):
    h, w = img.shape[:2]
    M = np.array([[s, 0, (1-s)*w/2+dx], [0, s, (1-s)*h/2+dy]], np.float32)
    return cv2.warpAffine(img, M, (w, h), flags=cv2.INTER_LINEAR, borderValue=0)
def sad(a, b): return float(np.abs(a-b).sum())
def best_fit(mov, ref, s0, dx0, dy0):
    best = (s0, dx0, dy0); bc = sad(warp(mov, s0, dx0, dy0), ref)
    for s in np.arange(s0-0.008, s0+0.0081, 0.004):
        for dx in np.arange(dx0-1.5, dx0+1.51, 0.25):
            for dy in np.arange(dy0-1.5, dy0+1.51, 0.25):
                c = sad(warp(mov, s, dx, dy), ref)
                if c < bc: bc, best = c, (s, dx, dy)
    return best

ff_before = sum(sad(raw[i], raw[i-1]) for i in range(1, 32))
T = [(1.0, 0.0, 0.0)]; pos = [raw[0]]
for i in range(1, 32):
    s, dx, dy = best_fit(raw[i], pos[i-1], *T[i-1])
    T.append((s, dx, dy)); pos.append(warp(raw[i], s, dx, dy))
S = np.array([t[0] for t in T]); DX = np.array([t[1] for t in T]); DY = np.array([t[2] for t in T])
S /= np.exp(np.mean(np.log(S))); DX -= DX.mean(); DY -= DY.mean()
final = [warp(raw[i], S[i], DX[i], DY[i]) for i in range(32)]
ff_after = sum(sad(final[i], final[i-1]) for i in range(1, 32))
print(f"frame-to-frame wobble: before={ff_before/1e6:.2f}M after={ff_after/1e6:.2f}M ({100*(1-ff_after/ff_before):.0f}% smoother)")

# un-premultiply to STRAIGHT alpha and pack into the engine grid
SW = COLS*FRAME + (COLS-1)*SEP
SH = ROWS*FRAME + (ROWS-1)*SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i, f in enumerate(final):
    a = np.clip(f[:, :, 3:4]/255.0, 0, 1)
    rgb = np.where(a > 1e-4, f[:, :, :3]/np.maximum(a, 1e-4), 0)
    straight = np.dstack([rgb.clip(0, 255), f[:, :, 3].clip(0, 255)]).astype(np.uint8)
    r, c = i//COLS, i % COLS
    sheet.paste(Image.fromarray(straight, "RGBA"), (c*(FRAME+SEP), r*(FRAME+SEP)))
sheet.save("out/playersheet_HD.png")
chk = (SW-(COLS-1)*SEP)//COLS
print(f"packed out/playersheet_HD.png {SW}x{SH} frame={FRAME} engine-recompute={chk} {'OK' if chk==FRAME else 'MISMATCH'}")
