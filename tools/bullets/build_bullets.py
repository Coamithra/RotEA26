#!/usr/bin/env python
# ---------------------------------------------------------------------------
# build_bullets.py - process the HD bullet render into the two game sprites.
#
# WHY: GFX/Sprites/bulletgood.png (player shot) + bulletevil.png (enemy shot)
# shipped as flat 16x16 pixel orbs (the recovered ROM art) that a later procedural
# pass reinvented as plain shaded spheres. This tool takes ONE HD source image that
# holds BOTH bullets side-by-side on a black background (image-gen output; see the
# "For me" Trello card 00f1dac6) and splits/keys/crops it into the two straight-alpha
# game sprites -- crisp at any window size.
#
# HOW IT FITS: on-screen size is pinned by AlienDrawableGameComponent.DesignFrameWidth
# ('GFX/Sprites/bulletgood' / 'bulletevil' -> 16), so a supersampled source keeps the
# same on-screen footprint; more texels only means more crispness. So we emit a big
# square texture with NO game code change. The hitbox is sized off the full frame x
# DrawScale x 0.6 (design width x 0.6 = 9.6px) INDEPENDENT of painted alpha, so the
# soft glow margin this tool leaves does not change collision.
#
# BLEND: bullets draw straight (non-premultiplied) alpha -- SpriteBlendMode maps to
# BlendState.NonPremultiplied (see CLAUDE.md) and bloom is applied downstream, so a
# bright white-hot core blooms nicely. Output is STRAIGHT-alpha RGBA (no premultiply).
# The bullets never set `rotation` (stay radially symmetric), so the art must read
# round -- this tool centres each orb in a square frame, no directional shaping.
#
# AUTO-DETECT: the two orbs are found by projection (no fixed midline split), so
# image-gen drift (off-centre / unequal orbs) is handled; each blob is classified as
# player-vs-evil by which colour channel dominates (green -> good, else -> evil),
# NOT by left/right position, so a swapped source still lands in the right file.
#
# SOURCE (gitignored raw, you provide it):  new_assets_raw/bulletshd.png
#   Both bullets on a (near-)black opaque background; alpha is derived from luminance
#   so black becomes transparent and the glow edges feather out.
#
# OUTPUT (committed, offline build like tools/nebula, tools/earth, tools/textures):
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/bulletgood.png   (straight-alpha RGBA)
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/bulletevil.png
#
# SAFE NO-OP: if the source is missing, the shipped PNGs are left untouched (so CI /
# a fresh clone with no raw source never regresses the committed art). Same pattern
# as tools/nebula/build_nebula.py.
#
# Verify in-context with the ?bulletshot showcase scene (Compat/BulletShowcaseScene.cs);
# aggressive cache-bust (DevTools -> Disable cache) when swapping the PNGs -- textures
# load late so a plain reload serves stale (see CLAUDE.md graphics gotcha).
#
# Re-run after swapping the source or a knob:  python tools/bullets/build_bullets.py
# Don't hand-edit the PNGs. Flags: --source PATH --size N --black F --white F
#   --gamma F --margin F --dry-run
# ---------------------------------------------------------------------------
import argparse
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_DEFAULT = os.path.normpath(os.path.join(HERE, "..", "..", "new_assets_raw", "bulletshd.png"))
# Content paths are CASE-SENSITIVE on the live host: capital "Content/", lowercase
# under it (see CLAUDE.md). Write to .../Content/gfx/sprites, never GFX/Sprites.
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites"))

# --- knobs (CLI-overridable) ------------------------------------------------
SIZE = 128            # output square side in px (16 design px x 8 supersample); tiny file
# luma->alpha mapping: max-channel "value" below BLACK is transparent, above WHITE
# opaque, smoothstepped between (^GAMMA). Background max-channel measures ~0.075, so
# BLACK sits above it to kill the backdrop without eating the orb's dim glow limb.
BLACK_POINT = 0.11
WHITE_POINT = 0.55
ALPHA_GAMMA = 0.85    # <1 lifts the mid glow so the corona stays visible
# Blob detection: a pixel is "solid orb core" when its max channel exceeds this; the
# core bbox drives the square crop (glow lives in the MARGIN beyond it).
DETECT_THR = 0.30
MARGIN = 1.30         # square side = core diameter x MARGIN (leaves ~15%/side glow)

LANCZOS = getattr(Image, "Resampling", Image).LANCZOS  # Pillow <9.1 vs >=9.1


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def find_runs(mask_1d):
    """Contiguous nonzero runs in a 1D bool profile -> list of (start, end) inclusive."""
    runs, s = [], None
    for i, v in enumerate(mask_1d):
        if v and s is None:
            s = i
        elif not v and s is not None:
            runs.append((s, i - 1)); s = None
    if s is not None:
        runs.append((s, len(mask_1d) - 1))
    return runs


def detect_blobs(val):
    """Find the orbs by projection (no fixed midline). Returns core bboxes
    (x0, y0, x1, y1) left-to-right. Robust to image-gen off-centre drift."""
    strong = val > DETECT_THR
    col = strong.any(axis=0)
    xruns = find_runs(col)
    boxes = []
    for (x0, x1) in xruns:
        if x1 - x0 < 8:                       # ignore speckle columns
            continue
        rows = strong[:, x0:x1 + 1].any(axis=1)
        yruns = find_runs(rows)
        if not yruns:
            continue
        y0 = min(a for a, _ in yruns)
        y1 = max(b for _, b in yruns)
        boxes.append((x0, y0, x1, y1))
    return boxes


def classify(rgb, box):
    """green core -> 'good' (player), otherwise -> 'evil'. Keyed on colour, not
    position, so a swapped source still routes correctly."""
    x0, y0, x1, y1 = box
    core = rgb[y0:y1 + 1, x0:x1 + 1].reshape(-1, 3)
    r, g, b = core.mean(axis=0)
    return "good" if (g > r and g > b) else "evil"


def crop_square(rgb, box, margin, src_shape):
    """Square crop centred on the core bbox, side = max(core dims) x margin, so the
    orb sits centred with a symmetric glow margin. Out-of-bounds is padded with the
    black backdrop (keyed to transparent later)."""
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    side = max(x1 - x0 + 1, y1 - y0 + 1) * margin
    half = side / 2.0
    ix0, iy0 = int(round(cx - half)), int(round(cy - half))
    n = int(round(side))
    h, w, _ = src_shape
    out = np.zeros((n, n, 3), dtype=np.float32)
    sx0, sy0 = max(ix0, 0), max(iy0, 0)
    sx1, sy1 = min(ix0 + n, w), min(iy0 + n, h)
    out[sy0 - iy0:sy1 - iy0, sx0 - ix0:sx1 - ix0] = rgb[sy0:sy1, sx0:sx1]
    return out


def to_straight_sprite(crop_rgb, size, black, white, gamma):
    """RGB crop (float 0..1, black-bg) -> straight-alpha RGBA sprite resized to size."""
    val = crop_rgb.max(axis=2)
    alpha = smoothstep(black, white, val)
    if gamma != 1.0:
        alpha = np.power(alpha, gamma)
    alpha = alpha.astype(np.float32)
    # Resize in PREMULTIPLIED space so the transparent limb doesn't drag the black
    # backdrop into the glow (dark-halo), then un-premultiply back to straight.
    pm = np.clip(np.dstack([crop_rgb * alpha[..., None], alpha]) * 255.0 + 0.5,
                 0, 255).astype(np.uint8)
    pm = np.asarray(Image.fromarray(pm, "RGBA").resize((size, size), LANCZOS),
                    dtype=np.float32) / 255.0
    a2 = pm[:, :, 3:4]
    rgb2 = np.where(a2 > 1e-4, pm[:, :, :3] / np.maximum(a2, 1e-4), 0.0)
    straight = np.clip(np.dstack([rgb2, a2[:, :, 0]]) * 255.0 + 0.5,
                       0, 255).astype(np.uint8)
    return Image.fromarray(straight, "RGBA")


def build(src, size, black, white, gamma, margin, dry_run):
    if not os.path.exists(src):
        print(f"source missing ({src}) -- leaving committed bullet PNGs untouched (no-op).")
        return
    rgb = np.asarray(Image.open(src).convert("RGB"), dtype=np.float32) / 255.0
    val = rgb.max(axis=2)
    boxes = detect_blobs(val)
    print(f"source {rgb.shape[1]}x{rgb.shape[0]}  detected {len(boxes)} blob(s)")
    if len(boxes) != 2:
        raise SystemExit(f"expected 2 bullet blobs, found {len(boxes)}: {boxes} "
                         f"(tune --black / DETECT_THR)")

    targets = {}
    for box in boxes:
        kind = classify(rgb, box)
        x0, y0, x1, y1 = box
        print(f"  blob x[{x0}..{x1}] y[{y0}..{y1}]  core {x1-x0+1}x{y1-y0+1}  -> {kind}")
        if kind in targets:
            raise SystemExit(f"both blobs classified as '{kind}' -- source colours unexpected")
        crop = crop_square(rgb, box, margin, rgb.shape)
        targets[kind] = to_straight_sprite(crop, size, black, white, gamma)

    if targets.keys() != {"good", "evil"}:
        raise SystemExit(f"missing a bullet kind: got {sorted(targets)}")

    for kind, img in targets.items():
        out = os.path.join(OUT_DIR, f"bullet{kind}.png")
        a = np.asarray(img)[:, :, 3]
        print(f"  bullet{kind}.png  {size}x{size}  mean-alpha={a.mean():.1f}  "
              f"opaque-frac={(a > 250).mean():.2f}" + ("  [dry-run]" if dry_run else ""))
        if not dry_run:
            img.save(out)
    print("done." if not dry_run else "dry-run: nothing written.")


def main():
    ap = argparse.ArgumentParser(description="Split/key the HD bullet render into the two game sprites.")
    ap.add_argument("--source", default=SRC_DEFAULT)
    ap.add_argument("--size", type=int, default=SIZE, help="output square side (px)")
    ap.add_argument("--black", type=float, default=BLACK_POINT, help="luma->alpha black point")
    ap.add_argument("--white", type=float, default=WHITE_POINT, help="luma->alpha white point")
    ap.add_argument("--gamma", type=float, default=ALPHA_GAMMA, help="luma->alpha falloff gamma")
    ap.add_argument("--margin", type=float, default=MARGIN, help="square side = core x margin")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    build(a.source, a.size, a.black, a.white, a.gamma, a.margin, a.dry_run)


if __name__ == "__main__":
    main()
