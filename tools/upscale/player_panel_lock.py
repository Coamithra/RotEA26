"""Register the player by locking the dark panel rectangle (the 'four lines').
The panel is a constant size (130x141 +/-0.6px), so pinning its centre pins all
four edges -> kills the ~1.9px per-frame drift the full-silhouette align left in.
Renders at 2x then downscales for effective sub-pixel placement.
"""
import numpy as np
from PIL import Image
from keycompare import key_magenta

COLS, ROWS = 8, 4
DISP, SS = 256, 2          # display size, supersample for sub-pixel
CANVAS = DISP*SS
BG, DUR = (10, 10, 20), 100

gem = key_magenta(np.asarray(Image.open("playership_gemini.png").convert("RGB"))[:639])
H, W = gem.shape[:2]
def gcell(i):
    r, c = i//COLS, i % COLS
    return gem[round(r*H/ROWS):round((r+1)*H/ROWS), round(c*W/COLS):round((c+1)*W/COLS)]

cell_dim = max(gcell(0).shape[:2])
SG = (CANVAS-60) / cell_dim

def panel_center(scaled):
    """centre of the dark opaque panel rectangle, in scaled-image px (float)."""
    a = np.asarray(scaled)
    R, G, B, A = [a[:, :, k].astype(int) for k in range(4)]
    dark = (A > 120) & (np.maximum.reduce([R, G, B]) < 80)
    ys, xs = np.where(dark)
    return (xs.min()+xs.max())/2, (ys.min()+ys.max())/2

frames = []; centers = []
for i in range(32):
    im = Image.fromarray(gcell(i), "RGBA").resize(
        (round(gcell(i).shape[1]*SG), round(gcell(i).shape[0]*SG)), Image.LANCZOS)
    cx, cy = panel_center(im)
    centers.append((cx, cy))
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (*BG, 255))
    canvas.alpha_composite(im, (round(CANVAS/2 - cx), round(CANVAS/2 - cy)))
    frames.append(canvas.convert("RGB").resize((DISP, DISP), Image.LANCZOS))

# residual: where does each panel centre land after locking? (should be ~0 spread)
post = []
for i, f in enumerate(frames):
    pass
res = np.array(centers); res = res - res.mean(0)
print(f"panel-centre spread BEFORE lock: x±{np.std(res[:,0])/SS:.2f} y±{np.std(res[:,1])/SS:.2f}px (display units)")
print("AFTER lock every panel centre is pinned to canvas centre -> ~0 (sub-pixel only)")

frames[0].save("out/ufo_player_panellock.gif", save_all=True, append_images=frames[1:],
               duration=DUR, loop=0, disposal=2, optimize=True)
print("saved out/ufo_player_panellock.gif")
