#!/usr/bin/env python
"""
build_marshills.py -- procedurally generate the Mars far-hills background layer.

Replaces the hand-drawn `GFX/MarsBG/marshills` (a low, hazy tan hill silhouette
that had visible vertical seams and read as "just a repeating hazy hills
texture") with a synthesized, natively-SEAMLESS, multi-ridge silhouette that has
real atmospheric depth.

WHERE IT'S USED: `Background.SetMars()` adds `marshills` as the 2nd background
layer (behind the HD `marsloop` ground, in front of `clouds-background`), drawn
at size 1, `scrollspeedmodifier = 0.7`, `mirrorX = false`. Because mirrorX is
off the layer simply REPEATS every `realsize.X` (= texture width) pixels as it
scrolls -- so the texture MUST wrap seamlessly left<->right or a hard seam scrolls
through view (that was the old art's flaw). Every heightfield here is built with
a circular FFT so column 0 continues perfectly from column W-1.

HOW IT LOOKS RIGHT: distant Martian hills are sold by AERIAL PERSPECTIVE, not by
a crisp outline -- farther ridges are lighter, softer and higher; nearer ridges
are darker, rougher and lower; every ridge's crest DISSOLVES into the sky/haze
(alpha feather + colour lerp toward the haze tone) instead of being a hard cut.
The layer is drawn OVER `clouds-background` (the sky) and UNDER the `marsloop`
ground, so the crests melt into the real sky layer and the ridge bodies get
occluded by the near ground below the horizon.

OFFLINE asset step (numpy + Pillow), same family as tools/earth, tools/walls,
tools/favicon: CI ships the committed `marshills.png`; this only re-runs when a
human wants to retune. It is DETERMINISTIC (seeded) -- same knobs -> same PNG.

TUNE IT: every aesthetic knob is a constant in the CONFIG block below. Re-run
    python tools/mars/build_marshills.py
after changing them. `--seed N` overrides the RNG seed for a different hill
shape without editing the file; `--preview` also writes a 2x-wide tiled preview
(`_preview_marshills.png`, gitignored) so you can eyeball the wrap seam; `--show`
composites the hills over the real `clouds-background` sky for a quick in-context
look (`_context_marshills.png`). Don't hand-edit the output PNG -- re-run this.
"""

import argparse
import os

import numpy as np
from PIL import Image

# --------------------------------------------------------------------------- #
#  CONFIG -- the knobs a human tweaks (see module docstring / the "For me" card)
# --------------------------------------------------------------------------- #

WIDTH, HEIGHT = 1000, 600        # must match the layer's on-disk size (drawn at size 1)
SEED = 7                         # RNG seed; --seed overrides. Change for a new hill shape.

# Palette (straight/non-premultiplied RGBA, 0..255). Sampled from the old art +
# clouds-background so the new hills sit in the same Mars tone family.
#   HILL_RGB : the base dusty-brown rock tone of the NEAREST ridge.
#   HAZE_RGB : the atmospheric tone crests fade toward (== the dusty sky).
HILL_RGB = (136, 102, 76)
HAZE_RGB = (192, 162, 132)

# Ridges are listed FAR -> NEAR (painted back to front). Per ridge:
#   base    : crest centre-line y (design px, 0=top). Farther ridges sit HIGHER.
#   amp     : peak-to-trough height variation (px). Nearer ridges are taller.
#   beta    : spectral roughness of the FFT heightfield -- HIGHER = smoother,
#             rounder hills; LOWER = jaggeder. Far hills read smoother.
#   lowcut  : drop frequencies below this (cycles across the width) so a ridge
#             has a few broad hills, not one giant bump. 2-3 is a good range.
#   highcut : drop frequencies above this so far ridges stay soft (no fine spikes).
#   haze    : 0..1, how far this ridge's body lerps toward HAZE_RGB (aerial
#             perspective). Farther ridges are hazier (higher).
#   feather : px of alpha fade at the crest so the ridgeline melts into the sky.
# NOTE the tight vertical band: in Level 2 the `marsloop` ground is drawn ON TOP
# from design y~448 down, so ONLY the ~40px strip above the rocky horizon
# (design y ~405..450) is ever visible -- crests must land there, and each
# ridge's body just fills down to be occluded by the ground.
RIDGES = [
    dict(base=428, amp=20, beta=2.5, lowcut=2, highcut=28, haze=0.52, feather=16),
    dict(base=440, amp=26, beta=2.2, lowcut=2, highcut=46, haze=0.36, feather=13),
    dict(base=452, amp=30, beta=2.0, lowcut=2, highcut=70, haze=0.20, feather=10),
]

# Large-scale left-to-right brightness drift (dust density) so no ridge is a flat
# fill. Amplitude is a fraction of the ridge value; period is in screen-widths.
DUST_STRENGTH = 0.10
DUST_CYCLES = 1.5                # integer+.5 keeps it non-repetitive but still wraps via the ridge FFT

OUT = os.path.join(os.path.dirname(__file__), "..", "..",
                   "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "marsbg", "marshills.png")

# --------------------------------------------------------------------------- #


def periodic_heightfield(width, rng, beta, lowcut, highcut):
    """A circular (seamless) 1-D fractal heightfield in [-1, 1], length `width`.

    White noise shaped by a 1/f^(beta/2) spectral falloff via rFFT/irFFT -- the
    circular transform makes it inherently periodic (h[0] continues from h[-1]),
    which is what guarantees the tiled layer has no vertical seam. lowcut/highcut
    band-limit it so a ridge is a few broad hills rather than one bump or a spiky
    mess.
    """
    noise = rng.standard_normal(width)
    spectrum = np.fft.rfft(noise)
    k = np.arange(spectrum.size)
    amp = np.zeros_like(k, dtype=float)
    amp[1:] = 1.0 / (k[1:] ** (beta / 2.0))
    amp[k < lowcut] = 0.0
    if highcut:
        amp[k > highcut] = 0.0
    spectrum *= amp
    h = np.fft.irfft(spectrum, n=width)
    peak = np.max(np.abs(h))
    if peak > 1e-9:
        h /= peak
    return h


def lerp_rgb(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def build(seed):
    rng = np.random.default_rng(seed)
    W, H = WIDTH, HEIGHT

    # RGBA float accumulator, straight alpha, transparent to start.
    canvas = np.zeros((H, W, 4), dtype=np.float64)

    ys = np.arange(H)[:, None]                      # (H,1) column of row indices

    # A single seamless dust-drift curve shared across ridges (broad, low-freq).
    dust = periodic_heightfield(W, np.random.default_rng(seed * 131 + 7),
                                beta=3.0, lowcut=1, highcut=max(1, int(DUST_CYCLES) + 1))

    haze = np.array(HAZE_RGB, dtype=np.float64)

    for ridge in RIDGES:                             # far -> near
        h = periodic_heightfield(W, rng, ridge["beta"], ridge["lowcut"], ridge["highcut"])
        ridgeline = ridge["base"] - ridge["amp"] * h          # (W,) y of the crest per column

        body = np.array(lerp_rgb(HILL_RGB, HAZE_RGB, ridge["haze"]), dtype=np.float64)

        # Per-column brightness drift (dust) -- keep it subtle, multiply the body.
        col_scale = 1.0 + DUST_STRENGTH * dust                # (W,)

        # Coverage below the (sub-pixel) ridgeline, ~1px anti-aliased edge.
        cover = np.clip(ys - ridgeline[None, :] + 0.5, 0.0, 1.0)   # (H,W)

        # Crest haze feather: within `feather` px under the ridgeline, fade alpha
        # AND lerp the colour extra-hard toward haze, so the top melts into sky.
        depth = ys - ridgeline[None, :]                       # px below the crest, (H,W)
        feather = max(1.0, ridge["feather"])
        crest_t = np.clip(depth / feather, 0.0, 1.0)          # 0 at crest -> 1 below the feather band
        alpha = cover * crest_t                               # transparent at the very crest

        # colour = body, pulled toward haze near the crest, dusted per column.
        near_haze = (1.0 - crest_t)[..., None] * 0.6          # (H,W,1); crests soften, don't vanish
        rgb = body[None, None, :] * col_scale[None, :, None]  # (H,W,3)
        rgb = rgb * (1.0 - near_haze) + haze[None, None, :] * near_haze
        rgb = np.clip(rgb, 0, 255)

        a = alpha[..., None]                                  # (H,W,1)
        # Straight-alpha OVER composite: near ridge over the far accumulation.
        canvas[..., :3] = rgb * a + canvas[..., :3] * (1.0 - a)
        canvas[..., 3:4] = a + canvas[..., 3:4] * (1.0 - a)

    # canvas RGB is 0..255, alpha is 0..1 -> scale alpha to 0..255 for the PNG.
    out = np.empty((H, W, 4), dtype=np.uint8)
    out[..., :3] = np.clip(canvas[..., :3] + 0.5, 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(canvas[..., 3] * 255.0 + 0.5, 0, 255).astype(np.uint8)
    return out


def main():
    ap = argparse.ArgumentParser(description="Generate the Mars far-hills layer (marshills.png).")
    ap.add_argument("--seed", type=int, default=SEED, help=f"RNG seed (default {SEED}).")
    ap.add_argument("--preview", action="store_true",
                    help="Also write a 2x-wide tiled preview to check the wrap seam.")
    ap.add_argument("--show", action="store_true",
                    help="Also composite over clouds-background for an in-context look.")
    ap.add_argument("--dry-run", action="store_true", help="Build but don't write marshills.png.")
    args = ap.parse_args()

    rgba = build(args.seed)
    img = Image.fromarray(rgba, "RGBA")
    out = os.path.normpath(OUT)

    if not args.dry_run:
        img.save(out)
        print(f"wrote {out}  ({img.width}x{img.height}, seed {args.seed})")
    else:
        print(f"[dry-run] built {img.width}x{img.height}, seed {args.seed} (not saved)")

    # Diagnostic previews live next to THIS script (gitignored), never in wwwroot.
    here = os.path.dirname(__file__)
    if args.preview:
        tiled = Image.new("RGBA", (img.width * 2, img.height))
        tiled.paste(img, (0, 0))
        tiled.paste(img, (img.width, 0))
        p = os.path.join(here, "_preview_marshills.png")
        tiled.save(p)
        print(f"wrote {p}  (2x tiled -- inspect the join at x={img.width})")

    if args.show:
        sky_path = os.path.join(os.path.dirname(out), "clouds-background.png")
        if os.path.exists(sky_path):
            sky = Image.open(sky_path).convert("RGBA").resize((img.width, img.height))
            comp = Image.alpha_composite(sky, img)
            c = os.path.join(here, "_context_marshills.png")
            comp.save(c)
            print(f"wrote {c}  (hills over clouds-background)")
        else:
            print(f"[--show] no {sky_path}; skipped")


if __name__ == "__main__":
    main()
