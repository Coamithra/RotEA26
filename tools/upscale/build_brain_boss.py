# Final boss assets, split into two aligned 1448x1086 layers (straight alpha):
#   brainbosshd.png   = keyed brain + cables, NO glow      (drawn normal, pulses w/ boss)
#   brainbossaura.png = blue glow halo ONLY (brain-body gated, cables de-glowed)
#                       (drawn additively BEHIND, animated by the BrainAura component)
# Boss-specific name so the small Braineroids keep the old brainlargetransglow.
import numpy as np
from PIL import Image, ImageFilter

RAW = r"C:\Programming\RotEA26\new_assets_raw\brainboss.png"
DST = r"C:\Programming\RotEA26\web\EvilAliensWeb\wwwroot\Content\gfx\sprites"

im = Image.open(RAW).convert("RGB")
a = np.asarray(im).astype(np.float32)
R, G, B = a[..., 0], a[..., 1], a[..., 2]
H, W = R.shape

def blur01(x, s):
    return np.asarray(Image.fromarray((np.clip(x, 0, 1) * 255).astype(np.uint8), "L")
                      .filter(ImageFilter.GaussianBlur(s))).astype(np.float32) / 255
def smooth(x, e0, e1):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

# --- green key (straight alpha) ----------------------------------------------
ge = G - np.maximum(R, B)
alpha = 1.0 - np.clip((ge - 35) / (110 - 35), 0, 1)
G2 = G - np.maximum(0, G - np.maximum(R, B)) * 0.85
brain_rgba = np.dstack([np.stack([R, np.clip(G2, 0, 255), B], -1), alpha * 255]).astype(np.uint8)
Image.fromarray(brain_rgba, "RGBA").save(DST + r"\brainbosshd.png")

# --- aura: brain-body-gated blue halo (no brain pixels) ----------------------
brain_blob = smooth(blur01(alpha, W * 0.075), 0.42, 0.58)
gate = np.clip(blur01(brain_blob, W * 0.05) * 1.15, 0, 1)
glow = np.clip(blur01(alpha, W * 0.045) * 0.85 + blur01(alpha, W * 0.015) * 0.9, 0, 1)
# NOTE: do NOT punch the brain out (no *(1-alpha)). The aura draws BEHIND the brain, which
# occludes the filled interior anyway; a filled blob has no inner edge, so when the aura
# scales/shimmers independently there's no misaligned "hole" ringing the brain.
glow *= gate
GC = np.array([70, 130, 255], np.float32)
aura_rgba = np.dstack([np.broadcast_to(GC, (H, W, 3)), np.clip(glow * 255, 0, 255)]).astype(np.uint8)
Image.fromarray(aura_rgba, "RGBA").save(DST + r"\brainbossaura.png")

# --- measure brain core (cables removed) to choose the design width ----------
ys, xs = np.nonzero(brain_blob > 0.5)
core_w = xs.max() - xs.min() + 1
core_cx = (xs.max() + xs.min()) / 2
core_cy = (ys.max() + ys.min()) / 2
frac = core_w / W
for target_core in (520, 560, 600):
    print(f"target core {target_core} design px -> DesignFrameWidth D = {round(target_core / frac)}")
print(f"brain core: {core_w}px wide = {frac:.3f} of {W}; core center=({core_cx:.0f},{core_cy:.0f}) frame center=({W/2:.0f},{H/2:.0f})")
print("wrote brainbosshd.png + brainbossaura.png to Content/gfx/sprites/")
