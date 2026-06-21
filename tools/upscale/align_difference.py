"""Difference-minimization registration (the user's algorithm).

For each frame, find the (scale, dx, dy) that MINIMIZES the summed absolute
difference (== 'Difference blend then sum the lightness') against the previous
positioned frame. Sub-pixel translation + a tiny scale term (the drift is
sub-pixel scaling). Greedy chain, then de-trend (remove mean translation/scale)
so it stays centred and doesn't slowly wander over the 32-frame loop.
"""
import numpy as np, cv2
from PIL import Image
from keycompare import key_magenta

COLS, ROWS = 8, 4
CANVAS, BG, DUR = 256, (10, 10, 20), 100

gem = key_magenta(np.asarray(Image.open("playership_gemini.png").convert("RGB"))[:639])
H, W = gem.shape[:2]
def gcell(i):
    r, c = i//COLS, i % COLS
    return gem[round(r*H/ROWS):round((r+1)*H/ROWS), round(c*W/COLS):round((c+1)*W/COLS)]
SG = (CANVAS-70) / max(gcell(0).shape[:2])

def panel_center(im):
    a = np.asarray(im); R, G, B, A = [a[:, :, k].astype(int) for k in range(4)]
    dark = (A > 120) & (np.maximum.reduce([R, G, B]) < 80)
    if dark.sum() < 30: return im.size[0]/2, im.size[1]/2
    ys, xs = np.where(dark); return (xs.min()+xs.max())/2, (ys.min()+ys.max())/2

def stamp(i):
    """keyed cell -> premultiplied RGBA float32 on CANVAS, panel-lock pre-centered."""
    im = Image.fromarray(gcell(i), "RGBA").resize(
        (round(gcell(i).shape[1]*SG), round(gcell(i).shape[0]*SG)), Image.LANCZOS)
    cx, cy = panel_center(im)
    cv = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    cv.alpha_composite(im, (round(CANVAS/2-cx), round(CANVAS/2-cy)))
    arr = np.asarray(cv).astype(np.float32)
    a = arr[:, :, 3:4]/255.0
    return np.dstack([arr[:, :, :3]*a, arr[:, :, 3]]).astype(np.float32)  # premultiplied + alpha

raw = [stamp(i) for i in range(32)]

def warp(img, s, dx, dy):
    h, w = img.shape[:2]; c = (w/2, h/2)
    M = np.array([[s, 0, (1-s)*c[0]+dx], [0, s, (1-s)*c[1]+dy]], np.float32)
    return cv2.warpAffine(img, M, (w, h), flags=cv2.INTER_LINEAR, borderValue=0)

def sad(a, b):
    return float(np.abs(a-b).sum())

def best_fit(mov, ref, s0, dx0, dy0):
    """local sub-pixel + scale search minimizing SAD(warp(mov), ref) near (s0,dx0,dy0)."""
    best = (s0, dx0, dy0); bc = sad(warp(mov, s0, dx0, dy0), ref)
    for s in np.arange(s0-0.008, s0+0.0081, 0.004):
        for dx in np.arange(dx0-1.5, dx0+1.51, 0.25):
            for dy in np.arange(dy0-1.5, dy0+1.51, 0.25):
                c = sad(warp(mov, s, dx, dy), ref)
                if c < bc: bc, best = c, (s, dx, dy)
    return best

# frame-to-frame wobble BEFORE (panel-lock only)
ff_before = sum(sad(raw[i], raw[i-1]) for i in range(1, 32))

# greedy chain: align each frame to the previous positioned one
T = [(1.0, 0.0, 0.0)]; pos = [raw[0]]
for i in range(1, 32):
    s, dx, dy = best_fit(raw[i], pos[i-1], *T[i-1])
    T.append((s, dx, dy)); pos.append(warp(raw[i], s, dx, dy))

# de-trend: keep centred / no scale wander (subtract mean translation, normalise mean scale)
S = np.array([t[0] for t in T]); DX = np.array([t[1] for t in T]); DY = np.array([t[2] for t in T])
S /= np.exp(np.mean(np.log(S))); DX -= DX.mean(); DY -= DY.mean()
final = [warp(raw[i], S[i], DX[i], DY[i]) for i in range(32)]

ff_after = sum(sad(final[i], final[i-1]) for i in range(1, 32))
print(f"frame-to-frame difference (wobble metric):  before={ff_before/1e6:.2f}M  after={ff_after/1e6:.2f}M"
      f"  ({100*(1-ff_after/ff_before):.0f}% smoother)")
print(f"scale range applied: {S.min():.4f}..{S.max():.4f}   translation: dx±{DX.std():.2f} dy±{DY.std():.2f}px")

# render GIF (un-premultiply onto dark bg)
frames = []
for f in final:
    a = np.clip(f[:, :, 3:4]/255.0, 0, 1)
    rgb = np.where(a > 1e-4, f[:, :, :3]/np.maximum(a, 1e-4), 0)
    out = (rgb*a + np.array(BG)*(1-a)).clip(0, 255).astype(np.uint8)
    frames.append(Image.fromarray(out, "RGB"))
frames[0].save("out/ufo_player_diffalign.gif", save_all=True, append_images=frames[1:],
               duration=DUR, loop=0, disposal=2, optimize=True)
print("saved out/ufo_player_diffalign.gif")
