#!/usr/bin/env python
"""Make the Level-3 collidable-wall texture (`GFX/Base/756-v1`) seamlessly tileable.

Why this exists
---------------
`Wall.Draw` (Game/EvilAliens/Wall.cs) samples the wall texture as an **8x8 grid**: block
(i,j) draws source cell `(j%8, i%8)` at an adjacent on-screen position, wrapping every 8
cells. So a run of blocks scans the image left-to-right / top-to-bottom and **wraps around
its edges** -- which means the WHOLE texture has to tile seamlessly (all four edges wrap)
or a hard seam shows every 8 blocks. On-screen size is dynamic (`scale = 800 /
(texture.Width * width)`), and the 8x8 split is `texture.Width/8` (integer), so the only
hard constraint on a replacement is: **dimensions a multiple of 8**. Any resolution works.

The flow (mirrors the other tools/ asset steps: raw art in, shipped artifact out):
  1. You upscale `756-v1.png` with ChatGPT / an image upscaler (art step -- see README).
     An AI upscale hallucinates detail and BREAKS the wrap seam, so it can't ship as-is.
  2. Drop it at `tools/walls/source/756-v1.png` (gitignored raw source).
  3. Run this: it re-makes the upscale seamlessly tileable and installs it at the content
     path, then reports the before/after wrap-seam discontinuity.

How it makes a texture tileable (the "similar toolchain as mars" the card asks for)
-----------------------------------------------------------------------------------
Classic **offset-and-heal**, with the seam healed by the *same* Laplacian multiband blend
the mars-ground stitcher uses (`tools/mars/stitch_lib.py:pyr_blend`):
  - `A` = the image as-is: continuous in the CENTER, discontinuous at the edges (the seam).
  - `B` = the image rolled by half (`np.roll` W/2, H/2): its EDGES are now interior-adjacent
    columns/rows of A, so B wraps seamlessly; its only discontinuity is the cross where A's
    old edges landed, through B's centre.
  - Blend `pyr_blend(A, B, mask)` with a mask that is 1 (use B) everywhere EXCEPT a feathered
    cross-band through the centre, where it is 0 (use A). Result = B (fully-detailed, seamless
    edges) everywhere, with B's centre-cross discontinuity patched by A's continuous centre;
    the multiband blend cross-fades only the low frequencies at the patch boundary so each
    side keeps its own detail. The output's outer edges are pure B -> they wrap -> seamless.

No game code change is needed -- `Wall` loads `GFX/Base/756-v1` and scales dynamically.

Usage
-----
  python tools/walls/build_wall_tileable.py                 # source/756-v1.png -> content path
  python tools/walls/build_wall_tileable.py --size 1024     # force square 1024x1024 first
  python tools/walls/build_wall_tileable.py --in foo.png --out bar.png
  python tools/walls/build_wall_tileable.py --check-only     # just report seams + preview, no write
  python tools/walls/build_wall_tileable.py --dry-run        # process but don't write the content file

Every run writes a `tools/walls/preview_756-v1.png` (2x2 tiling of the result) so you can
eyeball seamlessness. Offline (numpy + Pillow; cv2 optional, sharper pyramid). Don't
hand-edit the shipped `756-v1.png`; re-run this after a new upscale.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
# reuse the mars ground stitcher's Laplacian multiband blend
sys.path.insert(0, os.path.join(REPO, "tools", "mars"))
from stitch_lib import pyr_blend  # type: ignore  # noqa: E402

RESAMPLE_LANCZOS = getattr(Image, "Resampling", Image).LANCZOS  # type: ignore

DEFAULT_IN = os.path.join(HERE, "source", "756-v1.png")
DEFAULT_OUT = os.path.join(
    REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "base", "756-v1.png"
)
PREVIEW = os.path.join(HERE, "preview_756-v1.png")


def wrap_seam(rgb):
    """Wrap discontinuity as a RATIO to the texture's own interior adjacency.

    Mean abs RGB diff across the wrap edges (right<->left, bottom<->top), divided by the
    mean abs diff of interior-adjacent columns/rows. A perfectly tiling texture has a wrap
    seam no worse than any interior adjacency -> ratio ~1.0; a broken seam is >> 1.
    Returns (H_ratio, V_ratio, H_abs, V_abs).
    """
    h_abs = np.abs(rgb[:, -1, :] - rgb[:, 0, :]).mean()
    v_abs = np.abs(rgb[-1, :, :] - rgb[0, :, :]).mean()
    h_base = np.abs(rgb[:, 1:, :] - rgb[:, :-1, :]).mean() + 1e-6
    v_base = np.abs(rgb[1:, :, :] - rgb[:-1, :, :]).mean() + 1e-6
    return float(h_abs / h_base), float(v_abs / v_base), float(h_abs), float(v_abs)


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def crop_mult8(im):
    """Center-crop to the largest width/height that is a multiple of 8."""
    w, h = im.size
    w8, h8 = w - w % 8, h - h % 8
    if (w8, h8) == (w, h):
        return im
    left, top = (w - w8) // 2, (h - h8) // 2
    return im.crop((left, top, left + w8, top + h8))


def make_tileable(rgb, band_frac=0.14, levels=None):
    """Offset-and-heal with a multiband blend. `rgb` float32 HxWx3.

    `B` = the image rolled by half. B is seamless at its OUTER edges (they are
    interior-adjacent rows/cols of the original) and only discontinuous along its centre
    cross. So: use B in a frame around ALL four edges (guarantees a seamless border) and
    the original A in the centre (keeps original detail, and hides B's centre cross); the
    Laplacian pyramid cross-fades the frame<->centre transition so each side keeps its
    high-freq detail. Output border is pure B -> tiles seamlessly.
    """
    H, W = rgb.shape[:2]
    cy, cx = H // 2, W // 2
    A = rgb
    B = np.roll(np.roll(rgb, cy, axis=0), cx, axis=1)  # half-offset copy (seamless edges)

    if levels is None:
        levels = int(np.clip(np.floor(np.log2(min(W, H))) - 3, 3, 6))

    # mask = 1 (use B) near ANY outer edge, 0 (use A) in the central plateau. The border
    # must stay a CONSTANT 1 for a margin wider than the pyramid's coarsest support (~2**levels)
    # or the multiband reconstruction there isn't pure B and a faint seam survives; hence `flat`.
    ramp = max(8.0, band_frac * min(W, H))
    flat = max(2.0 ** (levels + 1), 0.5 * ramp)
    x = np.arange(W, dtype=np.float32)
    y = np.arange(H, dtype=np.float32)
    dx = np.minimum(x, W - 1 - x)  # distance to nearest vertical edge
    dy = np.minimum(y, H - 1 - y)  # distance to nearest horizontal edge
    d = np.minimum(dx[None, :], dy[:, None])  # distance to nearest edge
    mask = 1.0 - smoothstep((d - flat) / ramp)  # 1 in a `flat`-wide border frame -> 0 in the centre

    out = pyr_blend(A, B, mask, levels=levels)
    return np.clip(out, 0, 255)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--in", dest="inp", default=DEFAULT_IN, help="source PNG (upscaled art)")
    ap.add_argument("--out", default=DEFAULT_OUT, help="destination content PNG")
    ap.add_argument("--size", type=int, default=0,
                    help="resize (Lanczos) to SIZE x SIZE before healing; 0 = keep source dims")
    ap.add_argument("--band-frac", type=float, default=0.14,
                    help="heal cross-band width as a fraction of min(W,H) (default 0.14)")
    ap.add_argument("--keep-alpha", action="store_true",
                    help="preserve source alpha (default: force fully opaque, walls are solid)")
    ap.add_argument("--check-only", action="store_true",
                    help="report seams + write preview, do NOT write the content file")
    ap.add_argument("--dry-run", action="store_true", help="process but skip writing the content file")
    args = ap.parse_args()

    if not os.path.exists(args.inp):
        sys.exit(
            f"source not found: {args.inp}\n"
            "Drop your upscaled 756-v1.png there first (see tools/walls/README.md), "
            "or pass --in <path>."
        )

    im = Image.open(args.inp).convert("RGBA")
    if args.size:
        im = im.resize((args.size, args.size), RESAMPLE_LANCZOS)
    im = crop_mult8(im)
    W, H = im.size
    if W % 8 or H % 8:
        sys.exit(f"dims {W}x{H} not a multiple of 8 after crop -- unexpected")

    rgba = np.asarray(im).astype(np.float32)
    rgb, alpha = rgba[..., :3], rgba[..., 3]

    hr0, vr0, ha0, va0 = wrap_seam(rgb)
    out_rgb = make_tileable(rgb, band_frac=args.band_frac)
    hr1, vr1, ha1, va1 = wrap_seam(out_rgb)

    out_a = alpha if args.keep_alpha else np.full((H, W), 255.0, np.float32)
    out = np.dstack([out_rgb, out_a]).astype(np.uint8)
    out_im = Image.fromarray(out, "RGBA")

    # 2x2 tiled preview so seams (if any) are obvious to the eye
    prev = Image.new("RGBA", (W * 2, H * 2))
    for py in (0, H):
        for px in (0, W):
            prev.paste(out_im, (px, py))
    prev.save(PREVIEW)

    print(f"in  : {args.inp}  ({Image.open(args.inp).size[0]}x{Image.open(args.inp).size[1]})")
    print(f"proc: {W}x{H}  (mult-of-8 ok; 8x8 cell = {W // 8}x{H // 8})")
    print("wrap seam vs interior adjacency (1.0 = seamless; abs meandiff 0..255 in parens):")
    print(f"    H  {hr0:5.2f}x ({ha0:4.1f}) -> {hr1:5.2f}x ({ha1:4.1f})"
          f"     V  {vr0:5.2f}x ({va0:4.1f}) -> {vr1:5.2f}x ({va1:4.1f})")
    ok = max(hr1, vr1) < 1.6
    print(f"    {'OK: tiles seamlessly' if ok else 'WARN: residual seam -- try a wider --band-frac'}")
    print(f"preview: {PREVIEW}  (2x2 tiling)")

    if args.check_only or args.dry_run:
        print("(no content file written)")
        return
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    out_im.save(args.out)
    print(f"wrote: {args.out}")


if __name__ == "__main__":
    main()
