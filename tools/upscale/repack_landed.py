"""Repack an upscaled+keyed SINGLE-FRAME sprite (a "landed" UFO still) into a texture that
is exactly FACTOR x the original's canvas, footprint-matched so it overlays the original's
on-screen position/size when drawn `center: true` with the draw scale divided by FACTOR.

Unlike the animated sheets (repack_for_engine.py), the landed sprite is drawn directly with
raw `scale` at UFO.cs, so the size-trap is undone at the draw site via SuperSampleFactor,
which needs the texture to be a clean FACTOR x the original (design width * FACTOR).

usage: python repack_landed.py <gen.png> <orig.png> <factor> <out.png>
"""
import sys, numpy as np
from PIL import Image
from keycompare import key_magenta

GEN, ORIG = sys.argv[1], sys.argv[2]
FACTOR = int(sys.argv[3]); OUT = sys.argv[4]

orig = np.asarray(Image.open(ORIG).convert("RGBA"))
OH, OW = orig.shape[:2]
gen = key_magenta(np.asarray(Image.open(GEN).convert("RGB")))


def bbox(alpha, thr):
    m = alpha > thr
    ys, xs = np.where(m)
    return xs.min(), ys.min(), xs.max(), ys.max()


ox0, oy0, ox1, oy1 = bbox(orig[:, :, 3].astype(float), 40)
gx0, gy0, gx1, gy1 = bbox(gen[:, :, 3].astype(float), 60)

# match the saucer's WIDTH (the disc is the stable size cue for these top-down-ish saucers)
ow = (ox1 - ox0 + 1) * FACTOR          # target sprite width in the FACTOR-canvas
SG = ow / (gx1 - gx0 + 1)

crop = Image.fromarray(gen[gy0:gy1 + 1, gx0:gx1 + 1], "RGBA")
sw, sh = max(1, round(crop.size[0] * SG)), max(1, round(crop.size[1] * SG))
crop = crop.resize((sw, sh), Image.LANCZOS)

# output canvas = FACTOR x original; place the scaled sprite so its bbox CENTER sits at the
# original bbox center * FACTOR (preserves the original's centering / vertical bias)
FW, FH = OW * FACTOR, OH * FACTOR
ocx, ocy = ((ox0 + ox1 + 1) / 2) * FACTOR, ((oy0 + oy1 + 1) / 2) * FACTOR
cv = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
cv.alpha_composite(crop, (round(ocx - sw / 2), round(ocy - sh / 2)))
cv.save(OUT)

print(f"packed {OUT}  {FW}x{FH} (orig {OW}x{OH} x{FACTOR})  sprite {sw}x{sh}  "
      f"design-width-recompute={FW // FACTOR} {'OK' if FW // FACTOR == OW else 'MISMATCH'}")
