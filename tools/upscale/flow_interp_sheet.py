"""Build a 2x-frame interpolated spritesheet by inserting an in-between frame between
every consecutive pair (cyclic) of an animation, via the same motion-compensated
Farneback flow tween as flow_tween_frame.py -- OR a naive alpha blend, for A/B
comparison. Input frames come from one or more packed sheets read in order (the
mothership's animation is split across mothershipA + mothershipB, 16 frames each).

This is an EXPLORATION tool (does runtime/offline frame interpolation actually help a
given animation?) -- it writes a temp sheet you eyeball in wwwroot/skullframes.html.

  python flow_interp_sheet.py <out.png> <flow|blend|none> <in_cols> <in_rows> <sep> <out_cols> <sheet...> [win]

method none = just repack the originals (baseline). flow/blend = interleave originals
with tweens -> 2x frames. out_cols sets the output grid width; rows = ceil(T/out_cols).
"""
import sys, math, numpy as np, cv2
from PIL import Image

OUT, METHOD = sys.argv[1], sys.argv[2]
IC, IR, SEP, OC = int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
rest = sys.argv[7:]
WIN = 35
if rest and rest[-1].isdigit():
    WIN = int(rest[-1]); rest = rest[:-1]
SHEETS = rest


def cells(path):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    H, W = im.shape[:2]
    cw = (W - (IC - 1) * SEP) // IC
    ch = (H - (IR - 1) * SEP) // IR
    out = []
    for i in range(IC * IR):
        r, c = i // IC, i % IC
        out.append(im[r * (ch + SEP):r * (ch + SEP) + ch, c * (cw + SEP):c * (cw + SEP) + cw].copy())
    return out, cw, ch


frames = []
CW = CH = None
for s in SHEETS:
    cs, CW, CH = cells(s)
    frames += cs
F = len(frames)
print(f"loaded {F} frames ({len(SHEETS)} sheet(s)), cell {CW}x{CH}")


def premult(a):
    al = a[:, :, 3:4] / 255.0
    return np.dstack([a[:, :, :3] * al, a[:, :, 3]])


def gray(p):
    g = 0.299 * p[:, :, 0] + 0.587 * p[:, :, 1] + 0.114 * p[:, :, 2]
    return np.clip(g, 0, 255).astype(np.uint8)


def flow(g0, g1):
    return cv2.calcOpticalFlowFarneback(g0, g1, None, 0.5, 5, WIN, 5, 7, 1.5, 0)  # type: ignore[call-overload]


def warp(img, fl, t):
    h, w = img.shape[:2]
    gx, gy = np.meshgrid(np.arange(w, dtype=np.float32), np.arange(h, dtype=np.float32))
    return cv2.remap(img, (gx + t * fl[..., 0]).astype(np.float32), (gy + t * fl[..., 1]).astype(np.float32),
                     cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)


def unpremult(p):
    al = p[:, :, 3:4] / 255.0
    rgb = np.where(al > 1e-4, p[:, :, :3] / np.maximum(al, 1e-4), 0)
    return np.clip(np.dstack([rgb, p[:, :, 3]]), 0, 255).astype(np.uint8)


def tween(a, b, t=0.5):
    pa, pb = premult(a), premult(b)
    if METHOD == "blend":
        return unpremult((1 - t) * pa + t * pb)
    fAB, fBA = flow(gray(pa), gray(pb)), flow(gray(pb), gray(pa))
    wa, wb = warp(pa, fBA, t), warp(pb, fAB, 1 - t)
    return unpremult((1 - t) * wa + t * wb)


if METHOD == "none":
    seq = [f.astype(np.uint8) for f in frames]
else:
    seq = []
    for i in range(F):
        seq.append(frames[i].astype(np.uint8))
        seq.append(tween(frames[i], frames[(i + 1) % F]))   # tween toward the next (cyclic)
        print(f"  tween {i}->{(i+1)%F}")

T = len(seq)
OR = math.ceil(T / OC)
SW = OC * CW + (OC - 1) * SEP
SH = OR * CH + (OR - 1) * SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i, f in enumerate(seq):
    r, c = i // OC, i % OC
    sheet.paste(Image.fromarray(f, "RGBA"), (c * (CW + SEP), r * (CH + SEP)))
sheet.save(OUT)
print(f"saved {OUT}  {SW}x{SH}  {T} frames  grid {OC}x{OR}  cell {CW}x{CH}  (method={METHOD}, win={WIN})")
