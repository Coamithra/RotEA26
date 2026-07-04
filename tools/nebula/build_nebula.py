#!/usr/bin/env python
# ---------------------------------------------------------------------------
# build_nebula.py - process a high-res source into the Level-1 Andromeda nebula.
#
# WHY: GFX/Sprites/andromeda.png (the galaxy fly-by that crosses during Level 1's
# "brains" section, Background.QueueAndromeda) shipped at 840x583. It is drawn at
# a fixed 840 design-px footprint, which RenderScale then upscales to the window,
# so on a 1080p+ window it is a ~2.4x bilinear blur. Against the Stage-13 reskin's
# vivid procedural nebula starfield the low-res, washed-out galaxy reads as a flat
# sticker. This tool takes a high-res source galaxy/nebula (generated separately --
# e.g. ChatGPT image gen; see the "For me" Trello card) and normalises it into the
# game's straight-alpha format so it stays crisp at any window size.
#
# HOW IT FITS: the draw is now RESOLUTION-INDEPENDENT -- Background.QueueAndromeda
# sets doodadscale = AndromedaDesignWidth(840) / texture.Width, so ANY output size
# keeps the same on-screen footprint; more texels just means more crispness, not a
# bigger galaxy. So this tool can emit a big HD texture with NO game code change.
#
# BLEND: the andromeda doodad uses (SpriteBlendMode)1 == AlphaBlend, which
# SpriteBatchWrapper maps to BlendState.NonPremultiplied (straight, NOT premultiplied
# -- see CLAUDE.md). So the output is STRAIGHT-alpha RGBA: the galaxy's own RGB with
# an alpha channel that fades it into transparent space. No premultiplied tints.
#
# SOURCE (gitignored raw, you provide it):  tools/nebula/source/andromeda.png
#   Two source shapes are both handled (auto-detected):
#     * a galaxy/nebula on a BLACK (opaque) background  -> alpha derived from
#       luminance so black becomes transparent (the typical image-gen output);
#     * a pre-cut galaxy on a TRANSPARENT background     -> its alpha is respected.
#   In BOTH cases a per-axis edge feather is applied so the frame edges are
#   always transparent (no hard rectangle over the starfield), and the long side
#   is capped at MAX_DIM.
#
# OUTPUT (committed, offline build like tools/earth, tools/textures, tools/audio):
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/andromeda.png   (straight-alpha RGBA)
#
# SAFE NO-OP: if the source is missing, the shipped PNG is left untouched (so CI /
# a fresh clone with no raw source never regresses the committed art). Same pattern
# as tools/audio/install_classic.py.
#
# Re-run after swapping the source or a knob:  python tools/nebula/build_nebula.py
# Don't hand-edit andromeda.png. Flags: --source PATH --out PATH --dry-run
#   --alpha {auto,luma,source}  --opacity F  --gamma F  --feather F  --max-dim N
# ---------------------------------------------------------------------------
import argparse
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_DEFAULT = os.path.join(HERE, "source", "andromeda.png")
# Content paths are CASE-SENSITIVE on the live host: capital "Content/", lowercase
# under it (see CLAUDE.md). Write to .../Content/gfx/sprites, never GFX/Sprites.
OUT_DEFAULT = os.path.normpath(os.path.join(
    HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites",
    "andromeda.png"))

# --- knobs (CLI-overridable) ------------------------------------------------
# On-screen footprint pinned by Background.QueueAndromeda. Kept here only to print
# the resulting doodadscale so a framing change is easy to sanity-check.
DESIGN_WIDTH = 840.0
# Cap the long side. The 840 design-px footprint is ~2016 actual px at the biggest
# supported window (RenderScale caps at 1440 tall -> ~1920 wide), so ~2048 gives a
# ~1:1 texel:pixel galaxy at max zoom without a wastefully huge PNG. Bigger sources
# are downscaled; smaller ones are NOT upscaled (no fake detail).
MAX_DIM = 2048
# luma->alpha mapping (only used when alpha is derived): pixels darker than BLACK
# are fully transparent, brighter than WHITE fully opaque, smoothstep between; GAMMA
# shapes the falloff (>1 = keep more of the faint outer wisps translucent).
BLACK_POINT = 0.04
WHITE_POINT = 0.90
ALPHA_GAMMA = 1.0
OPACITY = 1.0          # overall alpha multiplier (0..1); <1 = more translucent galaxy
# Per-axis edge feather: alpha is forced to 0 at every frame edge and smoothstepped
# up to full between FEATHER_START and 1.0 of each half-extent. Mild by default so a
# well-margined centred galaxy is untouched -- it only guarantees clean edges.
FEATHER_START = 0.85


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def derive_alpha_from_luma(rgb, black, white, gamma):
    # max-channel "value" so a saturated but not-bright coloured wisp (a deep red/blue
    # arm) still registers, not just luma-bright regions.
    val = rgb.max(axis=2)
    a = smoothstep(black, white, val)
    if gamma != 1.0:
        a = np.power(a, gamma)
    return a.astype(np.float32)


def edge_feather(h, w, start):
    """Separable per-axis vignette: alpha is forced to 0 at EVERY frame edge and
    smoothstepped up to 1 between `start` and 1.0 of each half-extent. Multiplying
    the two axes zeroes all four edges (and corners), so no hard rectangle survives
    over the starfield even if the source content reaches an edge. Mild `start`
    (~0.85) leaves a well-margined centred galaxy untouched."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    hx = max((w - 1) / 2.0, 1e-6)                       # guard a 1px axis (no div-by-zero)
    hy = max((h - 1) / 2.0, 1e-6)
    nx = np.abs(xx - (w - 1) / 2.0) / hx                # 0 centre -> 1 left/right edge
    ny = np.abs(yy - (h - 1) / 2.0) / hy                # 0 centre -> 1 top/bottom edge
    wx = 1.0 - smoothstep(start, 1.0, nx)
    wy = 1.0 - smoothstep(start, 1.0, ny)
    return (wx * wy).astype(np.float32)


def cap_size(img, max_dim):
    """Downscale (never upscale) so the long side <= max_dim, in premultiplied
    space so the transparent limb doesn't bleed dark. img is straight-alpha uint8."""
    w, h = img.size
    if max(w, h) <= max_dim:
        return img
    scale = max_dim / float(max(w, h))
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    arr = np.asarray(img, dtype=np.float32) / 255.0
    rgb, a = arr[:, :, :3], arr[:, :, 3:4]
    pm = np.clip(np.dstack([rgb * a, a]) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    lanczos = getattr(Image, "Resampling", Image).LANCZOS  # type: ignore  # Pillow <9.1 vs >=9.1
    pm = np.asarray(Image.fromarray(pm, "RGBA").resize((nw, nh), lanczos),
                    dtype=np.float32) / 255.0
    a2 = pm[:, :, 3:4]
    rgb2 = np.where(a2 > 1e-4, pm[:, :, :3] / np.maximum(a2, 1e-4), 0.0)
    straight = np.clip(np.dstack([rgb2, a2[:, :, 0]]) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    return Image.fromarray(straight, "RGBA")


def build(src, out, alpha_mode, opacity, gamma, feather, max_dim, dry_run):
    if not os.path.isfile(src):
        print(f"[skip] no source at {src} -- shipped andromeda.png left untouched.")
        print("       generate an HD nebula (see tools/nebula/README.md) and re-run.")
        return
    im = np.asarray(Image.open(src).convert("RGBA"), dtype=np.float32) / 255.0
    rgb = im[:, :, :3]
    src_a = im[:, :, 3]

    # auto: if the source already carries a real cutout (a meaningful fraction of
    # near-transparent pixels), trust its alpha; else it's opaque-on-black -> derive.
    mode = alpha_mode
    transparent_frac = None
    if mode == "auto":
        transparent_frac = float((src_a < 0.06).mean())
        mode = "source" if transparent_frac > 0.02 else "luma"
    if mode == "source":
        alpha = src_a.copy()
    else:
        alpha = derive_alpha_from_luma(rgb, BLACK_POINT, WHITE_POINT, gamma)

    h, w = alpha.shape
    alpha *= edge_feather(h, w, feather)
    alpha = np.clip(alpha * opacity, 0.0, 1.0)

    straight = np.clip(np.dstack([rgb, alpha]) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    img = cap_size(Image.fromarray(straight, "RGBA"), max_dim)

    ow, oh = img.size
    scale = DESIGN_WIDTH / float(ow)
    detect = "" if transparent_frac is None else f" (auto: {transparent_frac*100:.1f}% transparent px)"
    print(f"source {w}x{h}  alpha-mode={mode}{detect}  mean-alpha={alpha.mean()*255:.1f}")
    print(f"output {ow}x{oh}  -> on-screen doodadscale = {scale:.4f} (footprint {DESIGN_WIDTH:.0f} design px)")
    if dry_run:
        print(f"[dry-run] would write {out}")
        return
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.save(out)
    print(f"wrote {out}")


def main():
    ap = argparse.ArgumentParser(description="Build the Level-1 Andromeda nebula sprite.")
    ap.add_argument("--source", default=SRC_DEFAULT, help="raw HD source PNG (default tools/nebula/source/andromeda.png)")
    ap.add_argument("--out", default=OUT_DEFAULT, help="output andromeda.png")
    ap.add_argument("--alpha", choices=["auto", "luma", "source"], default="auto",
                    help="how to get alpha: auto-detect (default), from luminance, or use source alpha")
    ap.add_argument("--opacity", type=float, default=OPACITY, help="overall alpha multiplier 0..1")
    ap.add_argument("--gamma", type=float, default=ALPHA_GAMMA, help="luma->alpha falloff gamma")
    ap.add_argument("--feather", type=float, default=FEATHER_START, help="edge-feather start (0..1 of half-extent)")
    ap.add_argument("--max-dim", type=int, default=MAX_DIM, help="cap the long side (downscale only)")
    ap.add_argument("--dry-run", action="store_true", help="report without writing")
    a = ap.parse_args()
    build(a.source, a.out, a.alpha, a.opacity, a.gamma, a.feather, a.max_dim, a.dry_run)


if __name__ == "__main__":
    main()
