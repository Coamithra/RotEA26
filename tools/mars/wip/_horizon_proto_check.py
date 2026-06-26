"""
Sanity check on horizon_out/combined.png (carry-the-taller result):

  INVARIANT: any pixel that has a RED component must be backed by an OPAQUE
  red-source pixel at that same (x,y); likewise BLUE. And no terrain-coloured
  pixel should be semi-transparent except the legitimate 1px antialiased
  silhouette top edge.

We don't trust the compositor's bookkeeping -- we decompose the OUTPUT image
against the two source colours and cross-check against the source mattes.
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, "horizon_out")


def load(name, key):
    im = np.asarray(Image.open(os.path.join(HERE, f"{name}.png")).convert("RGB")).astype(float)
    R, G, B = im[:, :, 0], im[:, :, 1], im[:, :, 2]
    m = (B > 120) & (R < 120) & (G < 120) if key == "blue" else (R > 120) & (G < 120) & (B < 120)
    return im, m


rgb_l, m_l = load("horizon2", "red")    # red source  (left)
rgb_r, m_r = load("horizon1", "blue")   # blue source (right)
comb = np.asarray(Image.open(os.path.join(OUT, "combined.png")).convert("RGBA")).astype(float)
H, W = m_l.shape
C = comb[:, :, :3]
A = comb[:, :, 3] / 255.0

RED = np.median(rgb_l[m_l], axis=0)
BLUE = np.median(rgb_r[m_r], axis=0)
print(f"pure RED  source colour ~ {tuple(RED.astype(int))}")
print(f"pure BLUE source colour ~ {tuple(BLUE.astype(int))}")

# decompose each opaque pixel C = (1-t)*RED + t*BLUE  -> least squares for t,
# then red weight = 1-t, blue weight = t (clipped to [0,1]).
d = BLUE - RED
t = ((C - RED) @ d) / (d @ d)
t = np.clip(t, 0.0, 1.0)
pred = RED[None, None, :] * (1 - t)[:, :, None] + BLUE[None, None, :] * t[:, :, None]
resid = np.linalg.norm(C - pred, axis=2)

EPS = 0.02
opaque = A > 0.004                       # any terrain coverage at all
has_red = opaque & ((1 - t) > EPS)
has_blue = opaque & (t > EPS)

# --- violation 1/2: colour drawn from a TRANSPARENT source pixel -------------
red_from_transparent = has_red & (~m_l)
blue_from_transparent = has_blue & (~m_r)

# allow a 1px tolerance: the hue-threshold matte can sit 1px inside the true
# edge, so count a "violation" only if NO opaque source pixel is within 1px.
def dilate(mask):
    out = mask.copy()
    out[1:, :] |= mask[:-1, :]; out[:-1, :] |= mask[1:, :]
    out[:, 1:] |= mask[:, :-1]; out[:, :-1] |= mask[:, 1:]
    return out

red_viol = red_from_transparent & (~dilate(m_l))
blue_viol = blue_from_transparent & (~dilate(m_r))

# --- violation 3: semi-transparent coloured pixel NOT on the silhouette edge -
# (legit AA = a transparent pixel directly above it; interior partial = bug)
partial = opaque & (A < 0.996)
edge_above = np.zeros_like(partial)
edge_above[1:, :] = comb[:-1, :, 3] < 10        # transparent pixel directly above
interior_partial = partial & (~edge_above)

# residual sanity: a big residual means the colour is NEITHER red nor blue
# (e.g. white background bled in) -- the smoking gun for sampling transparent.
white_contam = opaque & (resid > 40)

print(f"\nopaque terrain pixels checked : {int(opaque.sum())}")
print(f"  have a red  component        : {int(has_red.sum())}")
print(f"  have a blue component        : {int(has_blue.sum())}")
print(f"\nVIOLATIONS")
print(f"  red component, red source transparent  : {int(red_viol.sum())}")
print(f"  blue component, blue source transparent: {int(blue_viol.sum())}")
print(f"  interior semi-transparent (non-edge)   : {int(interior_partial.sum())}")
print(f"  off-gamut colour (white/other bled in) : {int(white_contam.sum())}")
print(f"\nsilhouette-edge AA pixels (expected, OK)  : {int((partial & edge_above).sum())}")
print(f"max colour residual over all opaque px    : {resid[opaque].max():.1f}")

ok = (red_viol.sum() == 0 and blue_viol.sum() == 0
      and interior_partial.sum() == 0 and white_contam.sum() == 0)
print("\nRESULT:", "PASS -- every colour pixel is backed by an opaque source, no interior translucency"
      if ok else "FAIL -- see violation map")

if not ok:
    vis = (C * (A[:, :, None])).astype(np.uint8).copy()
    vis[red_viol] = [255, 255, 0]
    vis[blue_viol] = [0, 255, 0]
    vis[interior_partial] = [255, 0, 255]
    Image.fromarray(vis).save(os.path.join(OUT, "check_violations.png"))
    print("wrote", os.path.join(OUT, "check_violations.png"))
