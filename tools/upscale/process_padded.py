"""Process a padded Gemini sheet: auto-crop banner, key magenta, slice 8x4,
keep only the saucer (largest component) per frame -> auto-strip strays.
Builds an indexed contact sheet, a centered 10fps GIF, and a dome-consistency
strip (the pilot across all 32 frames).

usage: python process_padded.py "<sheet.png>" <tag>
"""
import sys, numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont
from keycompare import key_magenta

SRC = sys.argv[1] if len(sys.argv) > 1 else "ufo gemini upscaled_padded.png"
TAG = sys.argv[2] if len(sys.argv) > 2 else "gemini_padded"
# "strip" (default) keeps only the largest component per frame (single-blob sprites
# like the UFO). "nostrip" leaves all components (multi-part sprites like the player
# ship = 4 corner orbs + centre, which keep-largest would destroy).
STRIP = (sys.argv[3] if len(sys.argv) > 3 else "strip") != "nostrip"
# registration anchor: "centroid" (alpha center of mass — fine for single rigid
# blobs like the UFO) or "bbox" (center of the opaque bounding box — steadier for
# sprites with an animating interior like the player, where the centroid drifts).
CENTER = sys.argv[4] if len(sys.argv) > 4 else "centroid"
COLS, ROWS = 8, 4

full = np.asarray(Image.open(SRC).convert("RGB")).astype(int)
R, G, B = full[:, :, 0], full[:, :, 1], full[:, :, 2]
mag = (R > 180) & (G < 90) & (B > 180)
blk = (R < 45) & (G < 45) & (B < 45)
# grid region = magenta band at top; banner = black band at bottom. find the cut.
GRID_H = full.shape[0]
for y in range(full.shape[0] - 1, -1, -1):
    if mag[y].mean() > 0.2:
        GRID_H = y + 1
        break
print(f"{SRC}: {full.shape[1]}x{full.shape[0]} -> grid region top {GRID_H}px (banner cropped)")

keyed = key_magenta(np.asarray(Image.open(SRC).convert("RGB"))[:GRID_H])
H, W = keyed.shape[:2]

def slice_cell(r, c):
    return keyed[round(r*H/ROWS):round((r+1)*H/ROWS), round(c*W/COLS):round((c+1)*W/COLS)].copy()

def keep_saucer(cell):
    mask = (cell[:, :, 3] > 60).astype(np.uint8)
    n, lbl, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    if n <= 1:
        return cell, 0
    big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    strays = 0
    for k in range(1, n):
        if k == big:
            continue
        if stats[k, cv2.CC_STAT_AREA] >= 6:
            strays += 1
        cell[:, :, 3][lbl == k] = 0
    return cell, strays

cells = []
total = 0
for r in range(ROWS):
    for c in range(COLS):
        cell, s = keep_saucer(slice_cell(r, c)) if STRIP else (slice_cell(r, c), 0)
        if s:
            print(f"  frame {r*COLS+c:2d}: stripped {s} stray(s)"); total += s
        cells.append(cell)
print(f"total strays stripped: {total}")

try: font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 22)
except: font = ImageFont.load_default()

def comp(rgba, Z, bg=16):
    im = np.asarray(Image.fromarray(rgba, "RGBA").resize((Z, Z), Image.LANCZOS)).astype(float)
    a = im[:, :, 3:4]/255
    return (im[:, :, :3]*a + bg*(1-a)).astype(np.uint8)

# indexed contact sheet
Z = 150; gap = 4
sheet = Image.new("RGB", (COLS*(Z+gap), ROWS*(Z+gap)), (38, 38, 46)); d = ImageDraw.Draw(sheet)
for i, cell in enumerate(cells):
    sheet.paste(Image.fromarray(comp(cell, Z)), ((i % COLS)*(Z+gap), (i//COLS)*(Z+gap)))
    d.text(((i % COLS)*(Z+gap)+5, (i//COLS)*(Z+gap)+3), str(i), fill=(255, 220, 40), font=font)
sheet.save(f"out/_{TAG}_indexed.png"); print(f"saved out/_{TAG}_indexed.png")

# dome-consistency strip: crop central dome region of each frame, tile 8x4
DZ = 120
dome = Image.new("RGB", (COLS*(DZ+3), ROWS*(DZ+3)), (38, 38, 46)); dd = ImageDraw.Draw(dome)
for i, cell in enumerate(cells):
    h, w = cell.shape[:2]
    crop = cell[int(h*0.20):int(h*0.62), int(w*0.30):int(w*0.70)]   # dome area
    dome.paste(Image.fromarray(comp(crop, DZ, bg=8)), ((i % COLS)*(DZ+3), (i//COLS)*(DZ+3)))
    dd.text(((i % COLS)*(DZ+3)+3, (i//COLS)*(DZ+3)+1), str(i), fill=(255, 220, 40), font=font)
dome.save(f"out/_{TAG}_domes.png"); print(f"saved out/_{TAG}_domes.png")

# centered 10fps GIF
CANVAS, BG, DUR = 240, (10, 10, 20), 100
def to_frame(rgba, scale):
    im = Image.fromarray(rgba, "RGBA").resize((round(rgba.shape[1]*scale), round(rgba.shape[0]*scale)), Image.LANCZOS)
    a = np.asarray(im)[:, :, 3].astype(float)
    if CENTER == "bbox" and (a > 100).any():
        ys, xs = np.where(a > 100); cx, cy = (xs.min()+xs.max())/2, (ys.min()+ys.max())/2
    elif a.sum() > 0:
        ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]; cx, cy = (xs*a).sum()/a.sum(), (ys*a).sum()/a.sum()
    else: cx, cy = im.size[0]/2, im.size[1]/2
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (*BG, 255))
    canvas.alpha_composite(im, (round(CANVAS/2-cx), round(CANVAS/2-cy)))
    return canvas.convert("RGB")
scale = (CANVAS-30)/max(cells[0].shape[0], cells[0].shape[1])
frames = [to_frame(c, scale) for c in cells]
frames[0].save(f"out/ufo_{TAG}.gif", save_all=True, append_images=frames[1:], duration=DUR, loop=0, disposal=2, optimize=True)
print(f"saved out/ufo_{TAG}.gif")
