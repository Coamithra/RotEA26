"""Build the JunkBoss "fleet commander drone" idle + attract sprite sheets from two
AnimGen takes, normalised so the drone CORE renders at the OLD eye's on-screen size
(360 * 0.13 = 47 design px -> 47 * 2.4 = ~112 texels for 1:1 at the 1440 cap), and
CENTRED on the drone (so center:true draws the body at Position in both states; the
attract's lightning just flares symmetrically beyond it).

Robust relative magenta key (handles the veo take's brightness-varying / dark-corner
magenta that key_magenta mis-keys): bg-ness = min(R,B) - G, soft ramp 25..60.

Run from tools/upscale/.  Prints the registry designFrameWidth + grid/fps to wire in.
"""
import glob, math, os
import numpy as np
from PIL import Image

EXP = "C:/Programming/AnimGen/data/exports/"
IDLE_DIR = EXP + "fleet_commander_20260622_215534_9443f8"
ATTRACT_DIR = EXP + "fleet_commander_20260622_215542_c32127"
OUT = "../../web/EvilAliensWeb/wwwroot/Content/gfx/sprites/"
RAW = "../../new_assets_raw/"

CORE_TEXELS = 112.0      # old eye on-screen 47px * 2.4 cap = 1:1 core
CAP = 2.4
SEP = 1
LO, HI = 25.0, 60.0      # relative-key soft ramp on (min(R,B)-G)


def key(path):
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    R, G, B = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mness = np.minimum(R, B) - G                       # high on magenta of ANY brightness
    a = np.clip((HI - mness) / (HI - LO), 0.0, 1.0)    # <=25 ->1 sprite, >=60 ->0 bg
    excess = np.clip(np.minimum(R, B) - G, 0, None)    # despill magenta from R,B
    out = np.zeros((*rgb.shape[:2], 4), np.uint8)
    out[:, :, 0] = np.clip(R - excess, 0, 255)
    out[:, :, 1] = np.clip(G, 0, 255)
    out[:, :, 2] = np.clip(B - excess, 0, 255)
    out[:, :, 3] = (a * 255 + 0.5).astype(np.uint8)
    return out


def bbox(k, thr=40):
    ys, xs = np.where(k[:, :, 3] > thr)
    return xs.min(), ys.min(), xs.max(), ys.max()


def build(name, frames, core_src_w, fps):
    keyed = [key(f) for f in frames]
    # drone core centre = centre of frame-0 (rest) bbox
    bx0, by0, bx1, by1 = bbox(keyed[0])
    ccx, ccy = (bx0 + bx1 + 1) / 2.0, (by0 + by1 + 1) / 2.0
    # symmetric window around the core centre that contains every frame's sprite pixels
    H, W = keyed[0].shape[:2]
    hw = hh = 0
    for k in keyed:
        ux0, uy0, ux1, uy1 = bbox(k)
        hw = max(hw, ccx - ux0, ux1 + 1 - ccx)
        hh = max(hh, ccy - uy0, uy1 + 1 - ccy)
    hw = int(math.ceil(hw)) + 3
    hh = int(math.ceil(hh)) + 3
    x0, y0 = int(round(ccx - hw)), int(round(ccy - hh))
    winW, winH = hw * 2, hh * 2
    # global scale so the drone core (core_src_w) -> CORE_TEXELS
    sf = CORE_TEXELS / core_src_w
    cw, ch = max(1, round(winW * sf)), max(1, round(winH * sf))
    n = len(keyed)
    # exact divisor grid (rows*cols == n, no empty cells -> no blank-frame flicker),
    # squarest packed sheet that stays <= 4096
    best = None
    for cols in range(1, n + 1):
        if n % cols:
            continue
        rows = n // cols
        sw, sh = cols * cw + (cols - 1) * SEP, rows * ch + (rows - 1) * SEP
        if max(sw, sh) > 4096:
            continue
        score = max(sw, sh) / min(sw, sh)
        if best is None or score < best[0]:
            best = (score, cols, rows)
    _, cols, rows = best
    SW = cols * cw + (cols - 1) * SEP
    SH = rows * ch + (rows - 1) * SEP
    sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
    for i, k in enumerate(keyed):
        # pad-crop to the window (frames are all same canvas, window may exceed edges)
        canvas = np.zeros((winH, winW, 4), np.uint8)
        sx0, sy0 = max(0, x0), max(0, y0)
        sx1, sy1 = min(W, x0 + winW), min(H, y0 + winH)
        canvas[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = k[sy0:sy1, sx0:sx1]
        crop = Image.fromarray(canvas, "RGBA").resize((cw, ch), Image.LANCZOS)
        r, c = divmod(i, cols)
        sheet.paste(crop, (c * (cw + SEP), r * (ch + SEP)))
    out_png = OUT + name + ".png"
    sheet.save(out_png)
    sheet.save(RAW + name + ".png")
    dfw = round(cw / CAP)
    print(f"{name}: {n} frames, grid {cols}x{rows}, cell {cw}x{ch}, sheet {SW}x{SH} "
          f"(<=4096: {max(SW,SH)<=4096})")
    print(f"   window {winW}x{winH} src, core {core_src_w}->{CORE_TEXELS:.0f}px (sf {sf:.3f})")
    print(f"   REGISTRY designFrameWidth = {dfw}  |  AnimationData rows={rows} cols={cols} fps={fps}")
    return cw, dfw


# --- idle: one on/off cycle (frames 9..16), full fps (8 frames, tiny sheet) ---
idle_files = [f"{IDLE_DIR}/frame_{i:03d}.png" for i in range(9, 17)]
build("eye_idle", idle_files, core_src_w=514, fps=12)

# --- attract: spin+lightning, halved fps (every 2nd of 144 = 72 frames) ---
attract_files = sorted(glob.glob(ATTRACT_DIR + "/frame_*.png"))[::2]
build("eye_attract", attract_files, core_src_w=578, fps=12)
