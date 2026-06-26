"""Compare NON-AI 2x upscalers on the mothership sprite, so you can eyeball which
crisps best before committing to a full 32-frame repack.

The flying mothership (`mothershipA`/`mothershipB`, 4x4 grids of 456px cells) was left
NATIVE in UPSCALING.md because a generative redraw wrecks animation consistency. A
deterministic non-AI upscaler is per-frame identical, so it sidesteps that entirely --
its only job is to pre-bake the Stage-10 presenter's ~2.4x magnification with a smarter
kernel than the runtime's bilinear, so edges stay crisp.

Methods (all 2x, all on PREMULTIPLIED alpha then un-premult, per the straight-alpha trap):
  bilinear      -- approximates what the GPU does at runtime (the soft baseline to beat)
  bicubic       -- standard cubic
  lanczos       -- Lanczos (sharper, can ring on the rim-vs-space edge)
  lanczos+rcas  -- Lanczos then AMD FSR Robust Contrast Adaptive Sharpening (ring-controlled)
  dcci          -- Directional Cubic Convolution Interpolation (edge-directed, jaggy-free)

Outputs (to wwwroot/_flowtest/ so the dev server serves them):
  upscale_compare.png  -- labeled montage: per cell, a full-sprite strip + a 2x-NEAREST
                          detail-crop strip (the dome+rim, where crispness shows).
  upscale_<method>_cell<N>.png -- the individual straight-alpha 2x results, drop one into
                          the harness/game to try it for real.

  python compare_upscale_noai.py [cells=0,5] [sharp=0.2]
"""
import sys, numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont

SHEET = "web/EvilAliensWeb/wwwroot/Content/gfx/sprites/mothershipa.png"
OUTDIR = "web/EvilAliensWeb/wwwroot/_flowtest"
COLS = ROWS = 4
SEP = 1
BG = np.array([60, 60, 70], np.float32)        # neutral-ish gray so BOTH bright+dark halos show
CROP = (0.14, 0.42, 0.56, 0.70)                # x0,y0,x1,y1 (norm of cell): cracked rim + a spike
ZOOM = 3                                        # NEAREST zoom of the detail crop

CELLS = [int(x) for x in sys.argv[1].split(",")] if len(sys.argv) > 1 else [0, 5]
SHARP = float(sys.argv[2]) if len(sys.argv) > 2 else 0.2   # FSR sharpness (0=strongest)


# ---- straight-alpha-safe premultiply / un-premultiply (same as flow_interp_sheet.py) ----
def premult(a):
    al = a[:, :, 3:4] / 255.0
    return np.dstack([a[:, :, :3] * al, a[:, :, 3]]).astype(np.float32)


def unpremult(p):
    al = p[:, :, 3:4] / 255.0
    rgb = np.where(al > 1e-4, p[:, :, :3] / np.maximum(al, 1e-4), 0)
    return np.clip(np.dstack([rgb, p[:, :, 3]]), 0, 255).astype(np.uint8)


# ---- classic resamplers (cv2, on the premult float image) ----
def resize_cv(pm, interp):
    h, w = pm.shape[:2]
    return cv2.resize(pm, (w * 2, h * 2), interpolation=interp)


# ---- AMD FSR 1.0 RCAS (Robust Contrast Adaptive Sharpening), sharpen-only ----
# Faithful to ffx_fsr1.h: 3x3 cross {b=up,d=left,f=right,h=down}, e=center; per-channel
# limiters keep it from over-sharpening near clipping; lobe (negative) is the sharpen weight.
RCAS_LIMIT = 0.25 - (1.0 / 16.0)   # 0.1875


def rcas(pm, sharpness):
    con = 2.0 ** (-sharpness)
    rgb = pm[:, :, :3] / 255.0
    P = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), mode="edge")
    e = rgb
    b = P[:-2, 1:-1]; h = P[2:, 1:-1]; d = P[1:-1, :-2]; f = P[1:-1, 2:]
    mn4 = np.minimum(np.minimum(b, d), np.minimum(f, h))
    mx4 = np.maximum(np.maximum(b, d), np.maximum(f, h))
    eps = 1e-4
    hitMin = mn4 / (4.0 * mx4 + eps)
    hitMax = (1.0 - mx4) / (4.0 * mn4 - 4.0 - eps)
    lobe_c = np.maximum(-hitMin, hitMax)                       # per channel
    lobe = np.max(lobe_c, axis=2, keepdims=True)               # combine channels
    lobe = np.maximum(-RCAS_LIMIT, np.minimum(lobe, 0.0)) * con
    out = (e + lobe * (b + d + f + h)) / (1.0 + 4.0 * lobe)
    out = np.clip(out, 0.0, 1.0) * 255.0
    return np.dstack([out, pm[:, :, 3]]).astype(np.float32)    # sharpen RGB, keep upscaled alpha


# ---- Directional Cubic Convolution Interpolation (2x), vectorized per channel ----
# Zhou/Shen/Zhang 2012. Pass 1 fills (odd,odd) center holes from the 4x4 diagonal window
# (interpolate along the SMOOTHER diagonal); pass 2 fills the remaining (odd,even)/(even,odd)
# holes along the smoother axis. Catmull-Rom (a=-0.5) midpoint = (9(P1+P2)-(P0+P3))/16.
K = 5
T = 1.15


def _cubic_mid(p0, p1, p2, p3):
    return (9.0 * (p1 + p2) - (p0 + p3)) / 16.0


def _blend(grad_a, grad_b, val_a, val_b):
    # interpolate along the direction with the SMALLER gradient (val_a goes with grad_a, etc.)
    wa = 1.0 / (1.0 + grad_a ** K)
    wb = 1.0 / (1.0 + grad_b ** K)
    soft = (wa * val_a + wb * val_b) / (wa + wb)
    ra = (1.0 + grad_a) / (1.0 + grad_b)
    rb = (1.0 + grad_b) / (1.0 + grad_a)
    out = np.where(ra > T, val_b, np.where(rb > T, val_a, soft))   # grad_a >> grad_b -> use val_b
    return out


def dcci_channel(I):
    H, W = I.shape
    out = np.zeros((2 * H, 2 * W), np.float32)
    out[0::2, 0::2] = I

    # --- pass 1: center holes out[2i+1,2j+1] from 4x4 known window W[m,n]=I[i-1+m,j-1+n] ---
    P = np.pad(I, 2, mode="edge")
    Wm = [[P[1 + m:1 + m + H, 1 + n:1 + n + W] for n in range(4)] for m in range(4)]
    # gradient along "\" (m-n const) and "/" (m+n const)
    d1 = (np.abs(Wm[0][0] - Wm[1][1]) + np.abs(Wm[1][1] - Wm[2][2]) + np.abs(Wm[2][2] - Wm[3][3])
          + np.abs(Wm[0][1] - Wm[1][2]) + np.abs(Wm[1][2] - Wm[2][3])
          + np.abs(Wm[1][0] - Wm[2][1]) + np.abs(Wm[2][1] - Wm[3][2]))
    d2 = (np.abs(Wm[0][3] - Wm[1][2]) + np.abs(Wm[1][2] - Wm[2][1]) + np.abs(Wm[2][1] - Wm[3][0])
          + np.abs(Wm[0][2] - Wm[1][1]) + np.abs(Wm[1][1] - Wm[2][0])
          + np.abs(Wm[1][3] - Wm[2][2]) + np.abs(Wm[2][2] - Wm[3][1]))
    p135 = _cubic_mid(Wm[0][0], Wm[1][1], Wm[2][2], Wm[3][3])     # along "\"
    p45 = _cubic_mid(Wm[0][3], Wm[1][2], Wm[2][1], Wm[3][0])      # along "/"
    out[1::2, 1::2] = _blend(d1, d2, p135, p45)

    # --- pass 2: remaining holes (r+c odd) along smoother AXIS, taps at dist 1 & 3 ---
    Q = np.pad(out, 3, mode="edge")
    OH, OW = 2 * H, 2 * W

    def s(dr, dc):                       # out[r+dr, c+dc] as a full (OH,OW) array
        return Q[3 + dr:3 + dr + OH, 3 + dc:3 + dc + OW]
    L1, L3, R1, R3 = s(0, -1), s(0, -3), s(0, 1), s(0, 3)
    U1, U3, D1, D3 = s(-1, 0), s(-3, 0), s(1, 0), s(3, 0)
    dh = np.abs(L3 - L1) + np.abs(L1 - R1) + np.abs(R1 - R3)
    dv = np.abs(U3 - U1) + np.abs(U1 - D1) + np.abs(D1 - D3)
    ph = _cubic_mid(L3, L1, R1, R3)
    pv = _cubic_mid(U3, U1, D1, D3)
    cand = _blend(dh, dv, ph, pv)
    rr, cc = np.meshgrid(np.arange(OH), np.arange(OW), indexing="ij")
    mask = ((rr + cc) & 1).astype(bool)
    out[mask] = cand[mask]
    return out


def dcci(pm):
    chans = [dcci_channel(pm[:, :, c]) for c in range(4)]
    return np.clip(np.dstack(chans), 0, 255).astype(np.float32)


METHODS = [
    ("bilinear",     lambda pm: resize_cv(pm, cv2.INTER_LINEAR)),
    ("bicubic",      lambda pm: resize_cv(pm, cv2.INTER_CUBIC)),
    ("lanczos",      lambda pm: resize_cv(pm, cv2.INTER_LANCZOS4)),
    ("lanczos+rcas", lambda pm: rcas(resize_cv(pm, cv2.INTER_LANCZOS4), SHARP)),
    ("dcci",         lambda pm: dcci(pm)),
]


# ---------------- run ----------------
def extract_cell(sheet, idx):
    H, W = sheet.shape[:2]
    cw = (W - (COLS - 1) * SEP) // COLS
    ch = (H - (ROWS - 1) * SEP) // ROWS
    r, c = idx // COLS, idx % COLS
    return sheet[r * (ch + SEP):r * (ch + SEP) + ch, c * (cw + SEP):c * (cw + SEP) + cw].copy()


def comp_on_bg(straight_rgba, scale_to=None):
    a = straight_rgba[:, :, 3:4] / 255.0
    comp = straight_rgba[:, :, :3] * a + BG * (1 - a)
    img = Image.fromarray(np.clip(comp, 0, 255).astype(np.uint8))
    if scale_to:
        img = img.resize((scale_to, scale_to), Image.LANCZOS)
    return img


def crop_zoom(straight_rgba):
    h, w = straight_rgba.shape[:2]
    x0, y0, x1, y1 = CROP
    cr = straight_rgba[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)]
    img = comp_on_bg(cr)
    return img.resize((img.width * ZOOM, img.height * ZOOM), Image.NEAREST)


try:
    FONT = ImageFont.truetype("arial.ttf", 22)
except Exception:
    FONT = ImageFont.load_default()

sheet = np.asarray(Image.open(SHEET).convert("RGBA")).astype(np.float32)
print(f"sharpness={SHARP} (con={2.0**-SHARP:.3f}), cells={CELLS}")

blocks = []          # one (full_strip, zoom_strip) image per cell
for idx in CELLS:
    cell = extract_cell(sheet, idx)
    pm = premult(cell)
    fulls, zooms = [], []
    for name, fn in METHODS:
        res = unpremult(fn(pm))
        Image.fromarray(res).save(f"{OUTDIR}/upscale_{name.replace('+','_')}_cell{idx}.png")
        fulls.append((name, comp_on_bg(res, scale_to=300)))
        zooms.append((name, crop_zoom(res)))
        print(f"  cell{idx} {name:13s} -> {res.shape[1]}x{res.shape[0]}")
    blocks.append((idx, fulls, zooms))

# ---- montage ----
PAD, LABEL_H, TITLE_H = 12, 30, 34
tile_w = max(im.width for _, _, zs in blocks for _, im in zs)
zh = blocks[0][2][0][1].height
fw = blocks[0][1][0][1].height
n = len(METHODS)
strip_w = PAD + n * (max(300, tile_w) + PAD)
block_h = TITLE_H + (LABEL_H + fw + PAD) + (LABEL_H + zh + PAD * 2)
montage = Image.new("RGB", (strip_w, PAD + len(blocks) * block_h), (24, 24, 28))
dr = ImageDraw.Draw(montage)

y = PAD
for idx, fulls, zooms in blocks:
    dr.text((PAD, y), f"cell {idx}  -  full sprite (composited on gray)", font=FONT, fill=(235, 235, 245))
    y += TITLE_H
    x = PAD
    colw = max(300, tile_w) + PAD
    for name, im in fulls:
        dr.text((x, y), name, font=FONT, fill=(170, 210, 255))
        montage.paste(im, (x, y + LABEL_H))
        x += colw
    y += LABEL_H + fw + PAD
    dr.text((PAD, y), f"cell {idx}  -  detail crop (dome+rim), {ZOOM}x NEAREST", font=FONT, fill=(235, 235, 245))
    y += LABEL_H
    x = PAD
    for name, im in zooms:
        montage.paste(im, (x, y))
        x += colw
    y += zh + PAD * 2

out = f"{OUTDIR}/upscale_compare.png"
montage.save(out)
print(f"saved {out}  {montage.width}x{montage.height}")
