#!/usr/bin/env python
"""
build_marshills.py -- procedurally generate the Mars far-hills background layer.

Replaces the hand-drawn `GFX/MarsBG/marshills` (a low, hazy tan hill silhouette
that had visible vertical seams and read as "just a repeating hazy hills
texture") with a synthesized, natively-SEAMLESS, multi-ridge silhouette that has
real atmospheric depth.

WHERE IT'S USED: `Background.SetMars()` adds the hills as THREE background
layers -- `marshills1` (far) / `marshills2` (mid) / `marshills3` (near), one
texture per RIDGES entry, emitted by this tool -- between `clouds-background`
and the HD `marsloop` ground, each drawn at size 1, `mirrorX = false`, with its
OWN `scrollspeedmodifier` (far 0.33 / mid 0.53 / near 0.85, between the sky's
0.3 and the ground's 1.0) so the ridges PARALLAX against each other. Because
mirrorX is off each layer simply REPEATS every `realsize.X` (= texture width)
pixels as it scrolls -- so every texture MUST wrap seamlessly left<->right or a
hard seam scrolls through view (that was the old art's flaw). Every heightfield
here is built with a circular FFT so column 0 continues perfectly from column
W-1.

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

TUNE IT (the fun way): run the LIVE EDITOR --
    python tools/mars/editor/serve.py
then open http://localhost:5299/ -- sliders for every knob below, re-rendered
by THIS generator per drag (pixel-exact) and composited over the real sky +
rocky ground, with a "Write into game" button and a paste-ready CONFIG block
to bake your values back into this file when done.

TUNE IT (by hand): every aesthetic knob is a constant in the CONFIG block below.
Re-run
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
#  CONFIG -- the knobs a human tweaks (see the module docstring's "TUNE IT")
# --------------------------------------------------------------------------- #

WIDTH, HEIGHT = 1000, 600        # must match the layer's on-disk size (drawn at size 1)
SEED = 30466                     # RNG seed; --seed overrides. Change for a new hill shape.

# Palette (straight/non-premultiplied RGBA, 0..255). MEASURED, not vibed:
#   - clouds-background at the horizon band (design y 400..450) is ~(188,154,116);
#   - the ORIGINAL hand-drawn marshills was ONE flat tone (177,144,107) -- a mere
#     ~11 levels below the sky. That near-invisibility is the whole OG look, so
#     the ridge bodies must stay within ~a dozen levels of the sky tone.
#   HILL_RGB : the base dusty-brown rock tone of the NEAREST ridge (>= OG tone,
#              since even the nearest ridge lerps `haze` of the way to HAZE_RGB).
#   HAZE_RGB : the atmospheric tone crests fade toward (== the measured sky).
HILL_RGB = (172, 138, 102)
HAZE_RGB = (188, 154, 116)

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
# Haze values are HIGH on purpose: with the near-sky palette above, the ridge
# bodies land only a handful of levels below the sky (far ridge fully hazed, near
# ridge at the full HILL_RGB) -- OG-adjacent subtlety (the OG sat ~11 below) with
# a whisper of layered depth, but still clearly THERE. Feathers melt the crests
# like the OG's soft alpha ramp. Values dialed by eye in the live editor
# (2026-07-06): a tall far ridge that all-but-fades into sky, a mid ridge, and a
# low near ridge at full rock tone that catches the eye above the horizon.
# NOTE: each ridge is emitted as its OWN texture (marshills1..3) and scrolls at
# its own depth -- the per-layer scroll modifiers live in Background.SetMars()
# (far 0.33 / mid 0.53 / near 0.85, between the sky 0.3 and the ground 1.0).
RIDGES = [
    dict(base=380, amp=47, beta=3.2, lowcut=2, highcut=28, haze=0.66, feather=14),
    dict(base=420, amp=28, beta=1.8, lowcut=2, highcut=31, haze=0.44, feather=12),
    dict(base=455, amp=11, beta=2.5, lowcut=2, highcut=25, haze=0.0, feather=10),
]

# Large-scale left-to-right brightness drift (dust density) -- a broad seamless
# curve shared by all ridges. DUST_STRENGTH is the drift as a fraction of the
# ridge value; DUST_HIGHCUT is the top FFT bin kept (higher = finer drift).
# Keep this SMALL: with the near-sky palette the hills' own contrast is only a
# handful of levels, so even a 5% swing (~9 levels) reads as a weird horizontal
# GRADIENT sliding across the layer, not as dust. 0.02 is a barely-there breakup
# dialed in the editor; 0 turns it off entirely.
DUST_STRENGTH = 0.02
DUST_HIGHCUT = 2

# One output PNG per ridge (far -> near), each its own parallax layer in
# Background.SetMars(). The old single `marshills.png` is superseded (main()
# removes a stale copy so the game can't half-load the old look).
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "..",
                       "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "marsbg")
OUT_TEMPLATE = os.path.join(OUT_DIR, "marshills{}.png")
LEGACY_OUT = os.path.join(OUT_DIR, "marshills.png")
# Kept for the editor server (it derives the marsbg dir from here).
OUT = OUT_TEMPLATE.format(1)

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


def build_layers(seed, cfg=None):
    """Build the hills as one straight-alpha RGBA uint8 array PER RIDGE (far ->
    near) -- each becomes its own parallax texture (marshills1..3). `cfg`
    (optional dict) overrides the CONFIG constants per call -- used by the live
    editor (tools/mars/editor/serve.py); None = the baked CONFIG, so the CLI
    path is unchanged. The shared rng is consumed one heightfield per ridge in
    order, so a given seed produces the same silhouettes it did when the ridges
    were composited into a single texture."""
    cfg = cfg or {}
    hill_rgb = tuple(cfg.get("hill_rgb", HILL_RGB))
    haze_rgb = tuple(cfg.get("haze_rgb", HAZE_RGB))
    ridges = cfg.get("ridges", RIDGES)
    dust_strength = cfg.get("dust_strength", DUST_STRENGTH)
    dust_highcut = int(cfg.get("dust_highcut", DUST_HIGHCUT))

    rng = np.random.default_rng(seed)
    W, H = WIDTH, HEIGHT

    ys = np.arange(H)[:, None]                      # (H,1) column of row indices

    # A single seamless dust-drift curve shared across ridges (broad, low-freq).
    dust = periodic_heightfield(W, np.random.default_rng(seed * 131 + 7),
                                beta=3.0, lowcut=1, highcut=max(1, dust_highcut))

    haze = np.array(haze_rgb, dtype=np.float64)

    layers = []
    for ridge in ridges:                             # far -> near
        h = periodic_heightfield(W, rng, ridge["beta"], ridge["lowcut"], ridge["highcut"])
        ridgeline = ridge["base"] - ridge["amp"] * h          # (W,) y of the crest per column

        body = np.array(lerp_rgb(hill_rgb, haze_rgb, ridge["haze"]), dtype=np.float64)

        # Per-column brightness drift (dust) -- keep it subtle, multiply the body.
        col_scale = 1.0 + dust_strength * dust                # (W,)

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

        # Each ridge is its own STRAIGHT-alpha texture (the game's layer stack
        # does the OVER compositing at draw time, one parallax layer per ridge).
        # Fully-transparent texels are filled with the haze tone so bilinear
        # filtering at the crest can't bleed black in.
        out = np.empty((H, W, 4), dtype=np.uint8)
        out[..., :3] = np.clip(rgb + 0.5, 0, 255).astype(np.uint8)
        out[..., 3] = np.clip(alpha * 255.0 + 0.5, 0, 255).astype(np.uint8)
        layers.append(out)
    return layers


def build(seed, cfg=None):
    """Composite the per-ridge layers (far -> near, straight-alpha OVER) into
    ONE flat RGBA array -- the previews (--preview/--show) want the combined
    look. The game itself loads the separate layers.

    GOTCHA kept from the single-texture era: an OVER accumulator holds
    PREMULTIPLIED colour (rgb*a), and the game/preview consumers expect STRAIGHT
    alpha -- exporting it verbatim turns every feathered crest into a DARK
    fringe. So un-premultiply on the way out (transparent texels -> haze tone)."""
    cfg = cfg or {}
    haze = np.array(tuple(cfg.get("haze_rgb", HAZE_RGB)), dtype=np.float64)
    W, H = WIDTH, HEIGHT

    canvas = np.zeros((H, W, 4), dtype=np.float64)
    for layer in build_layers(seed, cfg):
        rgb = layer[..., :3].astype(np.float64)
        a = (layer[..., 3:4].astype(np.float64)) / 255.0
        canvas[..., :3] = rgb * a + canvas[..., :3] * (1.0 - a)
        canvas[..., 3:4] = a + canvas[..., 3:4] * (1.0 - a)

    a = canvas[..., 3:4]
    straight = np.where(a > 1e-6, canvas[..., :3] / np.maximum(a, 1e-6),
                        haze[None, None, :])

    out = np.empty((H, W, 4), dtype=np.uint8)
    out[..., :3] = np.clip(straight + 0.5, 0, 255).astype(np.uint8)
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

    layers = build_layers(args.seed)
    img = Image.fromarray(build(args.seed), "RGBA")   # combined, for the previews

    if not args.dry_run:
        for i, layer in enumerate(layers, start=1):
            out = os.path.normpath(OUT_TEMPLATE.format(i))
            Image.fromarray(layer, "RGBA").save(out)
            print(f"wrote {out}  ({img.width}x{img.height}, seed {args.seed})")
        legacy = os.path.normpath(LEGACY_OUT)
        if os.path.exists(legacy):   # superseded single-texture output
            os.remove(legacy)
            print(f"removed stale {legacy} (now split into per-ridge layers)")
    else:
        print(f"[dry-run] built {len(layers)} layers {img.width}x{img.height}, "
              f"seed {args.seed} (not saved)")

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
        sky_path = os.path.join(os.path.normpath(OUT_DIR), "clouds-background.png")
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
