#!/usr/bin/env python
# ---------------------------------------------------------------------------
# make_cutout.py - turn a raw galaxy photo (galaxy on a dense starfield) into a
# soft ELLIPTICAL CUTOUT source for build_nebula.py, matching the original
# andromeda asset's style: just the galaxy blob in a tilted oval, with the
# surrounding starfield cut away (transparent) so it doesn't lay a second field
# of stars over the game's own starfield during the Level-1 fly-by.
#
# WHY a separate step: build_nebula.py's luma->alpha keeps EVERY bright pixel,
# so a galaxy photographed on a rich starfield (e.g. the Adam Evans M31 mosaic)
# would carry its whole surrounding star field as opaque specks. The old shipped
# andromeda.png was a cutout (a soft tilted oval around the galaxy); this
# reproduces that, then hands a pre-cut transparent-background PNG to
# build_nebula.py (which auto-picks --alpha source).
#
# HOW: the galaxy's centre + tilt + size are found automatically from image
# moments of a heavily-blurred luma (individual stars dissolve into the diffuse
# glow), a tilted elliptical soft mask is built from that, and it's gated by the
# per-pixel brightness so dark gaps/dust inside the oval stay transparent (the
# game starfield twinkles through) while the galaxy body + its own stars stay.
# The frame is cropped to the oval's bounding box + a small margin so the galaxy
# fills the texture (keeps build_nebula's 840-design-px footprint meaningful).
#
# IN  (gitignored):  tools/nebula/source/andromeda_raw.png   (the raw photo)
# OUT (gitignored):  tools/nebula/source/andromeda.png       (elliptical cutout)
# then run:          python tools/nebula/build_nebula.py     (-> committed PNG)
#
# Tunables below. --show writes a preview over a synthetic starfield.
# ---------------------------------------------------------------------------
import argparse
import os
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RAW_DEFAULT = os.path.join(HERE, "source", "andromeda_raw.png")
OUT_DEFAULT = os.path.join(HERE, "source", "andromeda.png")

# --- knobs ------------------------------------------------------------------
MOMENT_BLUR = 28          # px: glow blur for the moment fit (dissolves stars)
GLOW_FLOOR = 0.10         # subtract from blurred luma before weighting (drops sky)
K_SIGMA = 2.15            # oval semi-axis length, in sigma of the glow
FEATHER_INNER = 0.72      # oval alpha=1 inside this radius (r units, boundary=1)
FEATHER_OUTER = 1.08      # oval alpha=0 beyond this radius
CROP_MARGIN = 1.10        # crop box = oval bbox * this
GATE_BLACK = 0.045        # per-pixel brightness gate: transparent below this
GATE_WHITE = 0.42         # opaque above this (max-channel value, 0..1)
GATE_GAMMA = 0.85         # <1 lifts mid arms so faint spiral survives the gate


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def build(raw, out, show):
    if not os.path.isfile(raw):
        print(f"[skip] no raw at {raw} -- drop the raw galaxy photo there and re-run.")
        return
    im = Image.open(raw).convert("RGB")
    W, H = im.size
    arr = np.asarray(im, np.float32) / 255.0
    val = arr.max(axis=2)  # max-channel value keeps saturated arms, not just luma

    # pass 1: image moments on a heavily-blurred glow -> centre, tilt, size
    lum = Image.fromarray((val * 255).astype(np.uint8))
    blur = np.asarray(lum.filter(ImageFilter.GaussianBlur(radius=MOMENT_BLUR)), np.float32) / 255.0
    g = np.clip(blur - GLOW_FLOOR, 0.0, None) ** 1.5
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    m = g.sum()
    cx = (xx * g).sum() / m
    cy = (yy * g).sum() / m
    dx = xx - cx
    dy = yy - cy
    cxx = (g * dx * dx).sum() / m
    cyy = (g * dy * dy).sum() / m
    cxy = (g * dx * dy).sum() / m
    evals, evecs = np.linalg.eigh(np.array([[cxx, cxy], [cxy, cyy]]))
    sig_major, sig_minor = float(np.sqrt(evals[1])), float(np.sqrt(evals[0]))
    vmaj = evecs[:, 1]
    theta = float(np.arctan2(vmaj[1], vmaj[0]))
    a = K_SIGMA * sig_major
    b = K_SIGMA * sig_minor
    print(f"center=({cx:.0f},{cy:.0f}) sigma=({sig_major:.0f},{sig_minor:.0f}) "
          f"theta={np.degrees(theta):.1f}deg  oval a,b=({a:.0f},{b:.0f})")

    # crop to the oval's axis-aligned bbox + margin so the galaxy fills the frame
    ct, st = np.cos(theta), np.sin(theta)
    hx = CROP_MARGIN * np.hypot(a * ct, b * st)
    hy = CROP_MARGIN * np.hypot(a * st, b * ct)
    x0 = int(max(0, np.floor(cx - hx)))
    x1 = int(min(W, np.ceil(cx + hx)))
    y0 = int(max(0, np.floor(cy - hy)))
    y1 = int(min(H, np.ceil(cy + hy)))
    crop = arr[y0:y1, x0:x1]
    cH, cW = crop.shape[:2]
    print(f"crop=({x0},{y0})-({x1},{y1}) -> {cW}x{cH}")

    # tilted elliptical soft mask, in the cropped frame
    cval = crop.max(axis=2)
    lcx, lcy = cx - x0, cy - y0
    yy2, xx2 = np.mgrid[0:cH, 0:cW].astype(np.float32)
    ddx = xx2 - lcx
    ddy = yy2 - lcy
    ct2, st2 = np.cos(-theta), np.sin(-theta)
    u = ddx * ct2 - ddy * st2
    v = ddx * st2 + ddy * ct2
    r = np.sqrt((u / a) ** 2 + (v / b) ** 2)
    ell = 1.0 - smoothstep(FEATHER_INNER, FEATHER_OUTER, r)

    # per-pixel brightness gate (dark gaps -> transparent, galaxy -> opaque)
    vs = np.asarray(Image.fromarray((cval * 255).astype(np.uint8))
                    .filter(ImageFilter.GaussianBlur(radius=1)), np.float32) / 255.0
    gate = smoothstep(GATE_BLACK, GATE_WHITE, vs) ** GATE_GAMMA
    alpha = np.clip(ell * gate, 0.0, 1.0)

    rgba = (np.clip(np.dstack([crop, alpha]), 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(out)
    print(f"wrote cutout source {cW}x{cH}  (transparent {float((alpha < 0.06).mean())*100:.0f}%)  -> {out}")
    print("now run: python tools/nebula/build_nebula.py")

    if show:
        rng = np.random.default_rng(7)
        bg = np.zeros((cH, cW, 3), np.float32)
        bg[:] = np.array([6, 4, 16])
        n = int(cW * cH * 0.0022)
        ys = rng.integers(0, cH, n)
        xs = rng.integers(0, cW, n)
        bg[ys, xs] = np.minimum(bg[ys, xs] + rng.uniform(40, 200, n)[:, None]
                                * np.array([0.8, 0.85, 1.0]), 255)
        o = rgba.astype(np.float32)
        aa = o[..., 3:4] / 255.0
        prev = os.path.join(HERE, "_preview_cutout.png")
        Image.fromarray((o[..., :3] * aa + bg * (1 - aa)).astype(np.uint8)).save(prev)
        print(f"preview -> {prev}")


def main():
    ap = argparse.ArgumentParser(description="Make an elliptical galaxy cutout for build_nebula.py.")
    ap.add_argument("--raw", default=RAW_DEFAULT, help="raw galaxy photo (default source/andromeda_raw.png)")
    ap.add_argument("--out", default=OUT_DEFAULT, help="cutout source out (default source/andromeda.png)")
    ap.add_argument("--show", action="store_true", help="write a preview over a synthetic starfield")
    a = ap.parse_args()
    build(a.raw, a.out, a.show)


if __name__ == "__main__":
    main()
