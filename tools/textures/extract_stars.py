#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
extract_stars.py - pull a few isolated star sprites out of the space_near tiles for
the discrete drifting-star near layer (Game/EvilAliens/DriftingStars.cs).

The old near layer was a full scrolling tile sheet: every star moved at one uniform
speed, which read as a flat moving wall. Instead we now place a HANDFUL of individual
stars on top of the nebula, each with its own speed / scale / twinkle. This script cuts
those individual stars out of the existing art so they match the style.

Each output is a small RGB patch (glow on black) with a soft radial vignette, so it
blends additively with no hard edge (black adds nothing). Picks the K brightest, well-
separated stars per source tile. Re-run to regenerate; outputs are committed.

  python tools/textures/extract_stars.py
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SPACE = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "game", "space")
# Source = the pristine near tiles. We replicate the near-tile content pipeline inline
# (resize to 1440x1080, then a 1.4x luminosity gain) so the extracted stars match the
# shipped art exactly, WITHOUT needing the (now-removed) space_near tiles under Content.
RAW = os.path.join(REPO, "new_assets_raw", "space")
SRCS = [os.path.join(RAW, "space_near%d.png" % n) for n in (1, 2, 3, 4)]
RESIZE = (1440, 1080)   # match the content near-tile size
GAIN = 1.4              # match the baked-in luminosity gain
LANCZOS = getattr(Image, "Resampling", Image).LANCZOS

PATCH = 176         # crop size (px) - tight enough to avoid catching neighbours
HALF = PATCH // 2
DEDUP = 24          # merge peaks closer than this into one star (px)
THRESH = 200        # min peak luminance to qualify as a star core
GLOW_R = 34         # radius the "glow energy" prominence is summed over (px)
PER_TILE = 2        # stars taken from each source tile -> 4*PER_TILE total
FULL_R = 0.40       # vignette: full strength within this fraction of the radius


def vignette():
    yy, xx = np.mgrid[0:PATCH, 0:PATCH]
    d = np.sqrt((xx - HALF) ** 2 + (yy - HALF) ** 2) / HALF      # 0 at centre, 1 at edge
    v = np.clip((1.0 - d) / (1.0 - FULL_R), 0.0, 1.0)
    v = v * v * (3.0 - 2.0 * v)                                  # smoothstep
    return v[..., None]


def candidates(lum):
    """Deduped star cores (local maxima > THRESH), each with a glow-energy score."""
    H, W = lum.shape
    ys, xs = np.where(lum > THRESH)
    order = np.argsort(lum[ys, xs])[::-1]
    cores = []
    for idx in order:
        y, x = int(ys[idx]), int(xs[idx])
        if any((y - cy) ** 2 + (x - cx) ** 2 < DEDUP * DEDUP for cy, cx, _ in cores):
            continue
        if y < HALF or y >= H - HALF or x < HALF or x >= W - HALF:
            continue
        glow = float(lum[y - GLOW_R:y + GLOW_R, x - GLOW_R:x + GLOW_R].sum())  # prominence
        cores.append((y, x, glow))
    return cores


def pick(lum, k):
    """The k most prominent stars that are ISOLATED - no other comparable star within
    the patch (rejects doubles) - ranked by glow energy (favours big glowy stars over
    tiny sharp pixels)."""
    cores = candidates(lum)
    cores.sort(key=lambda c: c[2], reverse=True)
    chosen = []
    for (y, x, glow) in cores:
        # reject if another core of >=40% this one's prominence sits inside the patch
        if any(cy != y and cx != x and (cy - y) ** 2 + (cx - x) ** 2 < HALF * HALF
               and g >= 0.4 * glow for (cy, cx, g) in cores):
            continue
        chosen.append((y, x))
        if len(chosen) >= k:
            break
    return chosen


def main():
    vig = vignette()
    out_i = 0
    for si, src in enumerate(SRCS):
        # Replicate the near-tile content pipeline EXACTLY: resize, 1.4x luminosity gain,
        # then quantize to uint8 (the content tiles were 8-bit PNGs) so peak picks match.
        pil = Image.open(src).convert("RGB").resize(RESIZE, LANCZOS)
        im8 = np.clip(np.asarray(pil).astype(np.float32) * GAIN, 0, 255).astype(np.uint8)
        im = im8.astype(np.float32)
        lum = im.mean(axis=2)
        for (y, x) in pick(lum, PER_TILE):
            patch = im[y - HALF:y + HALF, x - HALF:x + HALF].copy() * vig
            out = os.path.join(SPACE, "star%02d.png" % out_i)
            Image.fromarray(np.clip(patch, 0, 255).astype("uint8"), "RGB").save(out)
            print("star%02d <- near%d @ (%d,%d) peak=%.0f" % (out_i, si + 1, x, y, lum[y, x]))
            out_i += 1
    print("wrote %d star sprites to %s" % (out_i, os.path.relpath(SPACE, REPO)))


if __name__ == "__main__":
    main()
