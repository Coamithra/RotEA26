"""Build 25fps animated GIFs from the original / ChatGPT / Gemini UFO sheets
so the spin (or loss of it) can be judged in motion."""
import sys
import numpy as np
from PIL import Image
from keycompare import key_magenta, ORIG, GPT, GEM, ROWS, COLS

# in-game is 25fps (40ms); pass a slower ms/frame as argv[1] to inspect the spin
DUR = int(sys.argv[1]) if len(sys.argv) > 1 else 40   # ms per frame
FPS = round(1000 / DUR)
CANVAS = 240
BG = (10, 10, 20)        # dark space

def uniform_cells(img):
    H, W = img.shape[:2]
    out = []
    for r in range(ROWS):
        for c in range(COLS):
            x0 = round(c*W/COLS); x1 = round((c+1)*W/COLS)
            y0 = round(r*H/ROWS); y1 = round((r+1)*H/ROWS)
            out.append(img[y0:y1, x0:x1])
    return out

def orig_cells(img):
    out = []
    for r in range(ROWS):
        for c in range(COLS):
            out.append(img[r*49:r*49+48, c*49:c*49+48])
    return out

def to_frame(rgba, scale, center):
    """RGBA cell -> CANVAS x CANVAS RGB on dark bg.
    center=False: place by image center (keeps the cell's own registration).
    center=True : place by alpha centroid (removes image-gen position drift)."""
    im = Image.fromarray(rgba, "RGBA").resize(
        (max(1, round(rgba.shape[1]*scale)), max(1, round(rgba.shape[0]*scale))), Image.LANCZOS)
    arr = np.asarray(im); a = arr[:, :, 3].astype(float)
    if center and a.sum() > 0:
        ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]
        cx, cy = (xs*a).sum()/a.sum(), (ys*a).sum()/a.sum()   # alpha centroid
    else:
        cx, cy = im.size[0]/2, im.size[1]/2
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (*BG, 255))
    canvas.alpha_composite(im, (round(CANVAS/2 - cx), round(CANVAS/2 - cy)))
    return canvas.convert("RGB")

def build(name, cells, center):
    # common per-sheet scale from nominal cell size -> no scale jitter introduced
    cell_dim = max(cells[0].shape[0], cells[0].shape[1])
    scale = (CANVAS - 30) / cell_dim
    frames = [to_frame(c, scale, center) for c in cells]
    out = f"out/ufo_{name}.gif"
    frames[0].save(out, save_all=True, append_images=frames[1:],
                   duration=DUR, loop=0, disposal=2, optimize=True)
    print(f"  {out}  ({len(frames)} frames @ {FPS}fps, {DUR}ms each)")

orig = np.asarray(Image.open(ORIG).convert("RGBA"))
gpt  = key_magenta(np.asarray(Image.open(GPT).convert("RGB")))
import os
GEM_FIXED = "ufo gemini upscaled_fixed.png"
gem  = key_magenta(np.asarray(Image.open(GEM_FIXED if os.path.exists(GEM_FIXED) else GEM).convert("RGB")))

print("building GIFs:")
build("original", orig_cells(orig), center=False)   # cells already registered
build("chatgpt",  uniform_cells(gpt), center=True)  # de-jitter via centroid
build("gemini",   uniform_cells(gem), center=True)
