"""Export the 32 player frames as individual transparent PNGs for manual
alignment. Each frame is the cleanly magenta-keyed sprite (straight alpha) at
full upscaled resolution, placed on a common 256x256 canvas and pre-centered by
panel-lock as a starting point. Also writes faint ground-truth reference frames
(the low-res original, upscaled) so you have an alignment target to nudge against.

Edit the frame_NN.png positions however you like (keep the 256x256 canvas +
transparency) and I'll repack them into the engine sheet.
"""
import os, numpy as np
from PIL import Image
from keycompare import key_magenta

COLS, ROWS = 8, 4
CANVAS = 256
OUT = "player_frames"
os.makedirs(OUT, exist_ok=True)

gem = key_magenta(np.asarray(Image.open("playership_gemini.png").convert("RGB"))[:639])
orig = np.asarray(Image.open("../../web/EvilAliensWeb/wwwroot/Content/gfx/sprites/playersheet.png").convert("RGBA"))
H, W = gem.shape[:2]
def gcell(i):
    r, c = i//COLS, i % COLS
    return gem[round(r*H/ROWS):round((r+1)*H/ROWS), round(c*W/COLS):round((c+1)*W/COLS)]
def ocell(i):
    r, c = i//COLS, i % COLS
    return orig[r*49:r*49+48, c*49:c*49+48]

cell_dim = max(gcell(0).shape[:2])
SG = (CANVAS-70) / cell_dim            # sprite ~70% of canvas, nudge room around it

def panel_center(im):
    a = np.asarray(im); R, G, B, A = [a[:, :, k].astype(int) for k in range(4)]
    dark = (A > 120) & (np.maximum.reduce([R, G, B]) < 80)
    if dark.sum() < 30:
        return im.size[0]/2, im.size[1]/2
    ys, xs = np.where(dark); return (xs.min()+xs.max())/2, (ys.min()+ys.max())/2

for i in range(32):
    im = Image.fromarray(gcell(i), "RGBA").resize(
        (round(gcell(i).shape[1]*SG), round(gcell(i).shape[0]*SG)), Image.LANCZOS)
    cx, cy = panel_center(im)
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas.alpha_composite(im, (round(CANVAS/2 - cx), round(CANVAS/2 - cy)))
    canvas.save(f"{OUT}/frame_{i:02d}.png")

    # ground-truth reference: original frame, upscaled + panel-locked, dimmed
    o = Image.fromarray(ocell(i), "RGBA").resize((CANVAS, CANVAS), Image.NEAREST)
    oa = np.asarray(o).copy(); oa[:, :, 3] = (oa[:, :, 3]*0.5).astype(np.uint8)  # 50% so it reads as a guide
    Image.fromarray(oa, "RGBA").save(f"{OUT}/ref_{i:02d}.png")

print(f"wrote 32 frame_NN.png + 32 ref_NN.png to {OUT}/  (canvas {CANVAS}x{CANVAS}, transparent)")
print(f"scale SG={SG:.3f}, panel-lock pre-centered")
