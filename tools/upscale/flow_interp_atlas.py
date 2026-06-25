"""Like flow_interp_sheet.py, but the INPUT is an AnimatedSprite atlas (.png + .dat)
rather than a uniform grid -- the spider-boss animations (GFX/Spider/{spiderfly,
spiderstand,spiderjump,spiderland}) are packed atlases. Reconstruct each frame into
its authored canvas (already registered), crop all to the shared union bbox, then
insert a flow / blend tween between consecutive frames (cyclic) -> 2x sheet for the
wwwroot/skullframes.html viewer.

  python flow_interp_atlas.py <out.png> <flow|blend|none> <out_cols> <png> <dat> [anim] [win]

.dat = .NET BinaryReader: int32, string, int32 numAnims; per anim: string, 2 bool,
int32 rate, byte nframes; per frame: 8 int16 (ow, oh, minX, minY, maxX, maxY, xPos, yPos).
"""
import sys, io, math, struct, numpy as np, cv2
from PIL import Image

OUT, METHOD, OC = sys.argv[1], sys.argv[2], int(sys.argv[3])
PNG, DAT = sys.argv[4], sys.argv[5]
ANIM = int(sys.argv[6]) if len(sys.argv) > 6 else 0
WIN = int(sys.argv[7]) if len(sys.argv) > 7 else 25


def rdstr(f):
    n, sh = 0, 0
    while True:
        b = f.read(1)[0]; n |= (b & 0x7f) << sh
        if not (b & 0x80): break
        sh += 7
    return f.read(n).decode('utf-8', 'replace')


def parse(dat):
    f = io.BytesIO(open(dat, 'rb').read())
    struct.unpack('<i', f.read(4)); rdstr(f)
    nanims = struct.unpack('<i', f.read(4))[0]
    anims = []
    for _ in range(nanims):
        rdstr(f); f.read(2); struct.unpack('<i', f.read(4))
        nf = f.read(1)[0]
        fr = [struct.unpack('<8h', f.read(16)) for _ in range(nf)]   # ow,oh,minX,minY,maxX,maxY,xPos,yPos
        anims.append(fr)
    return anims


atlas = np.asarray(Image.open(PNG).convert('RGBA'))
fr = parse(DAT)[ANIM]
OW, OH = fr[0][0], fr[0][1]
# union bbox across frames (in the authored OWxOH canvas) -> tight common crop
minx = min(r[2] for r in fr); miny = min(r[3] for r in fr)
maxx = max(r[4] for r in fr); maxy = max(r[5] for r in fr)
CW, CH = maxx - minx, maxy - miny
print(f"{len(fr)} frames, canvas {OW}x{OH}, union bbox {CW}x{CH} at ({minx},{miny})")


def frame_canvas(r):
    ow, oh, mnx, mny, mxx, mxy, xp, yp = r
    crop = atlas[yp:yp + (mxy - mny), xp:xp + (mxx - mnx)]
    cv = np.zeros((CH, CW, 4), np.float32)
    y0, x0 = mny - miny, mnx - minx
    cv[y0:y0 + crop.shape[0], x0:x0 + crop.shape[1]] = crop
    return cv


frames = [frame_canvas(r) for r in fr]
F = len(frames)


def premult(a):
    al = a[:, :, 3:4] / 255.0
    return np.dstack([a[:, :, :3] * al, a[:, :, 3]])


def gray(p):
    return np.clip(0.299 * p[:, :, 0] + 0.587 * p[:, :, 1] + 0.114 * p[:, :, 2], 0, 255).astype(np.uint8)


def flow(g0, g1):
    return cv2.calcOpticalFlowFarneback(g0, g1, None, 0.5, 5, WIN, 5, 7, 1.5, 0)  # type: ignore[call-overload]


def warp(img, fl, t):
    h, w = img.shape[:2]
    gx, gy = np.meshgrid(np.arange(w, dtype=np.float32), np.arange(h, dtype=np.float32))
    return cv2.remap(img, (gx + t * fl[..., 0]).astype(np.float32), (gy + t * fl[..., 1]).astype(np.float32),
                     cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)


def unpre(p):
    al = p[:, :, 3:4] / 255.0
    rgb = np.where(al > 1e-4, p[:, :, :3] / np.maximum(al, 1e-4), 0)
    return np.clip(np.dstack([rgb, p[:, :, 3]]), 0, 255).astype(np.uint8)


def tween(a, b, t=0.5):
    pa, pb = premult(a), premult(b)
    if METHOD == "blend":
        return unpre((1 - t) * pa + t * pb)
    fAB, fBA = flow(gray(pa), gray(pb)), flow(gray(pb), gray(pa))
    return unpre((1 - t) * warp(pa, fBA, t) + t * warp(pb, fAB, 1 - t))


if METHOD == "none":
    seq = [f.astype(np.uint8) for f in frames]
else:
    seq = []
    for i in range(F):
        seq.append(frames[i].astype(np.uint8))
        seq.append(tween(frames[i], frames[(i + 1) % F]))
        print(f"  tween {i}->{(i+1)%F}")

T = len(seq); OR = math.ceil(T / OC); SEP = 1
SW, SH = OC * CW + (OC - 1) * SEP, OR * CH + (OR - 1) * SEP
sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
for i, fimg in enumerate(seq):
    r, c = i // OC, i % OC
    sheet.paste(Image.fromarray(fimg, "RGBA"), (c * (CW + SEP), r * (CH + SEP)))
sheet.save(OUT)
print(f"saved {OUT}  {SW}x{SH}  {T} frames  grid {OC}x{OR}  cell {CW}x{CH}  (method={METHOD}, win={WIN})")
