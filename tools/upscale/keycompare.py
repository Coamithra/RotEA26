"""Key out magenta from the GPT/Gemini UFO sheets and compare frame sequences."""
import numpy as np
from PIL import Image

ORIG = "../../web/EvilAliensWeb/wwwroot/Content/gfx/sprites/ufosheet.png"
GPT  = "ufo chatgpt upscaled.png"
GEM  = "ufo gemini upscaled.png"
ROWS, COLS = 4, 8

def key_magenta(rgb):
    """rgb HxWx3 uint8 on magenta -> RGBA straight alpha, despilled."""
    f = rgb.astype(np.float32)
    R, G, B = f[:, :, 0], f[:, :, 1], f[:, :, 2]
    m = (R + B) * 0.5 - G                     # high on magenta, ~0 on grey, <0 on teal
    T = 120.0
    a = np.clip(1.0 - m / T, 0.0, 1.0)
    # despill: remove magenta excess (min(R,B) above G) from R and B
    excess = np.clip(np.minimum(R, B) - G, 0, None)
    R2 = R - excess; B2 = B - excess
    out = np.zeros((*rgb.shape[:2], 4), np.uint8)
    out[:, :, 0] = np.clip(R2, 0, 255); out[:, :, 1] = np.clip(G, 0, 255)
    out[:, :, 2] = np.clip(B2, 0, 255); out[:, :, 3] = (a * 255 + 0.5).astype(np.uint8)
    return out

def cell(img, r, c):
    H, W = img.shape[:2]
    x0 = round(c * W / COLS); x1 = round((c + 1) * W / COLS)
    y0 = round(r * H / ROWS); y1 = round((r + 1) * H / ROWS)
    return img[y0:y1, x0:x1]

def orig_cell(img, r, c):
    return img[r*49:r*49+48, c*49:c*49+48]

def checker(rgba, Z):
    im = np.asarray(Image.fromarray(rgba, "RGBA").resize((Z, Z), Image.LANCZOS)).astype(float)
    a = im[:, :, 3:4] / 255.0
    bg = np.zeros((Z, Z, 3)); s = 10
    for y in range(0, Z, s):
        for x in range(0, Z, s):
            bg[y:y+s, x:x+s] = 90 if ((x//s + y//s) % 2) else 140
    return (im[:, :, :3] * a + bg * (1 - a)).astype(np.uint8)

orig = np.asarray(Image.open(ORIG).convert("RGBA"))
gpt  = key_magenta(np.asarray(Image.open(GPT).convert("RGB")))
gem  = key_magenta(np.asarray(Image.open(GEM).convert("RGB")))

Z = 240; gap = 8; row = 0; PICK = [0, 2, 4, 6]  # every-other frame, top row
def strip(getter, src, is_orig=False):
    cells = []
    for c in PICK:
        rgba = orig_cell(src, row, c) if is_orig else getter(src, row, c)
        cells.append(checker(rgba, Z))
        cells.append(np.full((Z, gap, 3), 30, np.uint8))
    return np.hstack(cells[:-1])

s_orig = strip(None, orig, True)
s_gpt  = strip(cell, gpt)
s_gem  = strip(cell, gem)
vgap = np.full((gap, s_orig.shape[1], 3), 30, np.uint8)
full = np.vstack([s_orig, vgap, s_gpt, vgap, s_gem])
Image.fromarray(full, "RGB").save("out/_rotation_check.png")
print("saved out/_rotation_check.png  (rows: ORIGINAL | CHATGPT | GEMINI, frames 0-7)")

# also full keyed sheets for the record
Image.fromarray(gpt, "RGBA").save("out/_ufo_chatgpt_keyed.png")
Image.fromarray(gem, "RGBA").save("out/_ufo_gemini_keyed.png")
print("saved keyed sheets")
