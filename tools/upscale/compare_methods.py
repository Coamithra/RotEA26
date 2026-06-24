"""Render a side-by-side animated comparison of repack/registration methods so the owner
can eyeball which one to ship (the "make a bunch of GIFs and let me decide" workflow).

Give it 2+ engine sheets (same grid) produced by different methods; it emits:
  <out>.gif         animated. Each panel shows the engine FRAME box (the real cell bounds)
                    with dark margin around it, so wobble shows as drift against a fixed
                    crosshair AND any clipping shows as the sprite flat-cut at the SAME box
                    in every panel. Labelled with circle-fit centre std + frames-clipped.
  <out>_centres.png static proof: per-frame centres scattered as dots (tight dot = stable,
                    spread cloud = wobbly) over the frame-mean (ghosted edge = wobble).

usage: python compare_methods.py <out_basename> [cols rows fps] <label=sheet.png> [label=sheet.png ...]
e.g.   python compare_methods.py ../../deathstar_methods diffmin=a.png bbox=b.png circle-fit=c.png
"""
import sys
import numpy as np
from PIL import Image, ImageDraw

BG = (12, 14, 24)
INNER = 200          # px the engine frame is drawn at inside a panel
MARGIN = 22          # dark margin around the frame box (so clipping is visible)
PANEL = INNER + 2 * MARGIN

args = sys.argv[1:]
OUT = args.pop(0)
COLS, ROWS, FPS = 8, 4, 25
nums = []
while args and "=" not in args[0]:
    nums.append(int(args.pop(0)))
if len(nums) >= 2:
    COLS, ROWS = nums[0], nums[1]
if len(nums) >= 3:
    FPS = nums[2]
pairs = [(a.split("=", 1)[0], a.split("=", 1)[1]) for a in args]
N = COLS * ROWS


def cfit(al):
    m = al > 0.4
    b = np.zeros(m.shape, bool)
    b[1:-1, 1:-1] = m[1:-1, 1:-1] & ~(m[:-2, 1:-1] & m[2:, 1:-1] & m[1:-1, :-2] & m[1:-1, 2:])
    ys, xs = np.where(b)
    if len(xs) < 10:
        return (np.nan, np.nan)
    xs = xs.astype(float); ys = ys.astype(float)
    A = np.c_[2 * xs, 2 * ys, np.ones(len(xs))]
    sol, *_ = np.linalg.lstsq(A, xs ** 2 + ys ** 2, rcond=None)
    return (sol[0], sol[1])


def load(path):
    sh = Image.open(path).convert("RGBA")
    W, H = sh.size
    fw = (W - (COLS - 1)) // COLS
    fh = (H - (ROWS - 1)) // ROWS
    cells = [sh.crop(((f % COLS) * (fw + 1), (f // COLS) * (fh + 1),
                      (f % COLS) * (fw + 1) + fw, (f // COLS) * (fh + 1) + fh)) for f in range(N)]
    cs = np.array([cfit(np.asarray(c)[:, :, 3].astype(float) / 255.0) for c in cells])
    std = (np.nanstd(cs[:, 0]) + np.nanstd(cs[:, 1])) / 2
    clip = 0
    for c in cells:
        al = np.asarray(c)[:, :, 3] > 100
        if al.any():
            ys, xs = np.where(al)
            if xs.min() <= 0 or ys.min() <= 0 or xs.max() >= fw - 1 or ys.max() >= fh - 1:
                clip += 1
    return cells, fw, fh, cs, float(std), clip


data = [(name,) + load(p) for name, p in pairs]


def panel(cells, fw, fh, f):
    """one method's frame f: engine cell on dark, framed with margin + box + crosshair."""
    cell = Image.new("RGBA", (fw, fh), BG + (255,))
    cell.alpha_composite(cells[f])
    cell = cell.convert("RGB").resize((INNER, INNER), Image.LANCZOS)
    pan = Image.new("RGB", (PANEL, PANEL), (18, 18, 26))
    pan.paste(cell, (MARGIN, MARGIN))
    d = ImageDraw.Draw(pan)
    d.rectangle([MARGIN, MARGIN, MARGIN + INNER - 1, MARGIN + INNER - 1], outline=(120, 110, 80))  # frame box
    h = MARGIN + INNER // 2
    d.line([(h, MARGIN), (h, MARGIN + INNER)], fill=(70, 80, 110))
    d.line([(MARGIN, h), (MARGIN + INNER, h)], fill=(70, 80, 110))
    return pan


frames = []
for f in range(N):
    cv = Image.new("RGB", (len(data) * (PANEL + 14) + 14, PANEL + 36), (18, 18, 26))
    d = ImageDraw.Draw(cv)
    for i, (name, cells, fw, fh, cs, std, clip) in enumerate(data):
        x0 = 14 + i * (PANEL + 14)
        cv.paste(panel(cells, fw, fh, f), (x0, 6))
        d.text((x0 + 2, PANEL + 12), name, fill=(225, 225, 235))
        d.text((x0 + 2, PANEL + 23), f"wobble {std:.2f}px   clips {clip}/{N}",
               fill=(150, 200, 150) if (std < 1 and clip == 0) else (220, 150, 150))
    frames.append(cv)
src = Image.new("RGB", (frames[0].width, frames[0].height * N))
for i, fr in enumerate(frames):
    src.paste(fr, (0, i * fr.height))
pal = src.quantize(colors=256, method=Image.MEDIANCUT)
pf = [fr.quantize(palette=pal, dither=Image.FLOYDSTEINBERG) for fr in frames]
pf[0].save(OUT + ".gif", save_all=True, append_images=pf[1:], duration=round(1000 / FPS), loop=0, optimize=True, disposal=2)

# --- static centre-scatter proof ---
P2 = 230
proof = Image.new("RGB", (len(data) * (P2 + 14) + 14, P2 + 24), (18, 18, 26))
pdraw = ImageDraw.Draw(proof)
for i, (name, cells, fw, fh, cs, std, clip) in enumerate(data):
    acc = np.zeros((fh, fw, 3))
    for c in cells:
        pc = Image.new("RGBA", (fw, fh), BG + (255,)); pc.alpha_composite(c); acc += np.asarray(pc.convert("RGB"))
    mean = Image.fromarray((acc / N).astype("uint8")).resize((P2, P2), Image.LANCZOS)
    md = ImageDraw.Draw(mean); s = P2 / fw
    for cx, cy in cs:
        if not np.isnan(cx):
            md.ellipse([cx * s - 2, cy * s - 2, cx * s + 2, cy * s + 2], fill=(255, 80, 80))
    span = (np.nanmax(cs, 0) - np.nanmin(cs, 0)).max()
    proof.paste(mean, (14 + i * (P2 + 14), 6))
    pdraw.text((14 + i * (P2 + 14) + 2, P2 + 10), f"{name}: centres span {span:.1f}px", fill=(225, 225, 235))
proof.save(OUT + "_centres.png")
print(f"wrote {OUT}.gif + {OUT}_centres.png  ",
      [f"{n}: wobble={s:.2f} clips={c}/{N}" for n, _, _, _, _, s, c in data])
