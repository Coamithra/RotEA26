"""Register each upscaled UFO frame to its low-res ORIGINAL counterpart by
maximizing silhouette overlap (FFT cross-correlation of the alpha channels).
The original sheet is the ground-truth registration, so this inherits the exact
authored motion and removes image-gen drift/wobble.

usage: python align_to_original.py "<upscaled_sheet.png>" <tag> [grid_crop]
"""
import sys, numpy as np
from PIL import Image
from keycompare import key_magenta

UP   = sys.argv[1] if len(sys.argv) > 1 else "ufo gemini upscaled_padded_with_alien.png"
TAG  = sys.argv[2] if len(sys.argv) > 2 else "gemini_aligned"
CROP = int(sys.argv[3]) if len(sys.argv) > 3 else 636
ORIG = sys.argv[4] if len(sys.argv) > 4 else "../../web/EvilAliensWeb/wwwroot/Content/gfx/sprites/ufosheet.png"
COLS, ROWS = 8, 4
CANVAS, BG, DUR = 256, (10, 10, 20), 100

orig = np.asarray(Image.open(ORIG).convert("RGBA"))
gem  = key_magenta(np.asarray(Image.open(UP).convert("RGB"))[:CROP])
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

# global scale so the upscaled saucer ~ matches the original's apparent size
o_med = np.median([bbmax(ocell(i)[:, :, 3].astype(float), 40)*CANVAS/48 for i in range(32)])
g_med = np.median([bbmax(gcell(i)[:, :, 3].astype(float), 60) for i in range(32)])
SG = o_med / g_med
print(f"global scale SG={SG:.3f}  (orig~{o_med:.0f}px / gem~{g_med:.0f}px on a {CANVAS}px canvas)")

def place(rgba, dx, dy):
    im = Image.fromarray(rgba, "RGBA").resize(
        (max(1, round(rgba.shape[1]*SG)), max(1, round(rgba.shape[0]*SG))), Image.LANCZOS)
    cv = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    cv.alpha_composite(im, (round((CANVAS-im.size[0])/2+dx), round((CANVAS-im.size[1])/2+dy)))
    return cv

def alpha_of(rgba_img):
    return np.asarray(rgba_img)[:, :, 3].astype(np.float32)/255.0

def best_shift(target, src):
    """integer (dx,dy) to shift src so it best overlaps target (FFT xcorr)."""
    F = np.fft.rfft2(target); G = np.fft.rfft2(src)
    cc = np.fft.irfft2(F*np.conj(G), s=target.shape)
    py, px = np.unravel_index(int(np.argmax(cc)), cc.shape)
    if py > target.shape[0]//2: py -= target.shape[0]
    if px > target.shape[1]//2: px -= target.shape[1]
    return px, py

def overlap(a, b):
    return float((np.minimum(a, b)).sum() / (np.maximum(a, b).sum() + 1e-6))

frames = []; ov_before = []; ov_after = []; shifts = []
for i in range(32):
    O = np.asarray(Image.fromarray(ocell(i), "RGBA").resize((CANVAS, CANVAS), Image.LANCZOS))
    Oa = O[:, :, 3].astype(np.float32)/255.0
    G0 = place(gcell(i), 0, 0); Ga = alpha_of(G0)
    dx, dy = best_shift(Oa, Ga)
    # guard: keep the shift only if it actually improves overlap (sign safety)
    Ga_sh = alpha_of(place(gcell(i), dx, dy))
    if overlap(Oa, Ga_sh) < overlap(Oa, Ga):
        dx, dy = 0, 0; Ga_sh = Ga
    shifts.append((dx, dy)); ov_before.append(overlap(Oa, Ga)); ov_after.append(overlap(Oa, Ga_sh))
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (*BG, 255))
    canvas.alpha_composite(place(gcell(i), dx, dy))
    frames.append(canvas.convert("RGB"))

print(f"mean silhouette overlap (IoU):  before={np.mean(ov_before):.3f}  after={np.mean(ov_after):.3f}")
print(f"shift magnitude: mean={np.mean([abs(x)+abs(y) for x,y in shifts]):.1f}px  max={max(abs(x)+abs(y) for x,y in shifts)}px")

frames[0].save(f"out/ufo_{TAG}.gif", save_all=True, append_images=frames[1:],
               duration=DUR, loop=0, disposal=2, optimize=True)
print(f"saved out/ufo_{TAG}.gif")

# diagnostic: original silhouette (green) vs aligned-gem silhouette (red), overlap=yellow
import numpy as _np
tiles = []
for i in [0, 8, 16, 24]:
    O = _np.asarray(Image.fromarray(ocell(i), "RGBA").resize((CANVAS, CANVAS), Image.LANCZOS))[:, :, 3]/255.0
    G = alpha_of(place(gcell(i), *shifts[i]))
    img = _np.zeros((CANVAS, CANVAS, 3), _np.uint8)
    img[:, :, 1] = (O*255).astype(_np.uint8)   # original -> green
    img[:, :, 0] = (G*255).astype(_np.uint8)   # aligned  -> red  (overlap=yellow)
    tiles.append(img)
strip = _np.hstack([_np.pad(t, ((0, 0), (0, 4), (0, 0))) for t in tiles])
Image.fromarray(strip, "RGB").save(f"out/_{TAG}_overlap.png")
print(f"saved out/_{TAG}_overlap.png  (green=orig, red=aligned-gem, yellow=overlap; frames 0/8/16/24)")
