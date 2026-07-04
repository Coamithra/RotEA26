#!/usr/bin/env python
"""Make the Level-3 collidable-wall texture (`GFX/Base/756-v1`) seamlessly tileable.

Why this exists
---------------
`Wall.Draw` (Game/EvilAliens/Wall.cs) samples the wall texture as an **8x8 grid**: block
(i,j) draws source cell `(j%8, i%8)` at an adjacent on-screen position, wrapping every 8
cells. So a run of blocks scans the image and **wraps around its edges** -- the WHOLE texture
has to tile seamlessly (all four edges wrap) or a hard seam shows every 8 blocks. On-screen
size is dynamic (`scale = 800 / (texture.Width * width)`) and the 8x8 split is `texture.Width/8`
(integer), so the only hard constraint on a replacement is **dims a multiple of 8**.

You upscale `756-v1.png` (ChatGPT / an upscaler) -> it hallucinates detail and BREAKS the wrap
seam -> this tool makes it seamlessly tileable again. TWO methods are provided so you can A/B them:

  BLEND (default, offline, no model) -- `python build_wall_tileable.py`
      Offset-and-heal, healed with the mars-ground stitcher's Laplacian multiband blend
      (`tools/mars/stitch_lib.py:pyr_blend` -- the "similar toolchain as mars" the card asked
      for). Roll the image by half so the wrap seam is a cross through the centre, keep a
      seamless pure-`B` frame around all four edges, and cross-fade the frame<->centre
      transition per frequency. Deterministic; but it RELOCATES edge content (from the opposite
      half) and can faintly ghost near the seam -- it blends existing pixels, it doesn't invent.

  INFILL (needs a local inpainting model -- Flux Fill / SD-inpaint) -- `--emit-seam` + `--reimport`
      Offset so the seam runs through the centre, mask that seam cross, and let an inpainting
      model REGENERATE coherent new detail across it. No ghosting, no relocation -- the modern
      "make seamless" method. ChatGPT can't do this (it regenerates the whole frame and breaks
      the unmasked borders); you need a real inpainter that preserves unmasked pixels. `--reimport`
      composites the model's fill INSIDE THE MASK ONLY over the original offset, so the wrap
      borders stay pixel-exact and the result is guaranteed to tile. See `flux_infill.py` for a
      one-shot Flux Fill runner, or `tools/walls/README.md` for the manual recipe.

No game code change is needed -- `Wall` loads `GFX/Base/756-v1` and scales dynamically.

Usage
-----
  # BLEND (offline):
  python tools/walls/build_wall_tileable.py                 # source/756-v1.png -> content path
  python tools/walls/build_wall_tileable.py --size 1024     # Lanczos-resize to 1024x1024 first
  python tools/walls/build_wall_tileable.py --check-only     # report seam + preview, no write

  # INFILL (local model):
  python tools/walls/build_wall_tileable.py --emit-seam      # -> seam/756-v1_offset.png + _mask.png
  # ...run your inpainter on those two (or use flux_infill.py)...
  python tools/walls/build_wall_tileable.py --reimport out.png   # composite + install

Both methods write a 2x2 tiling preview (`preview_blend_756-v1.png` / `preview_infill_756-v1.png`)
so you can eyeball seamlessness side by side. Offline (numpy + Pillow; cv2 optional, sharper
pyramid). Don't hand-edit the shipped `756-v1.png`; re-run this after a new upscale.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

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
SEAM_DIR = os.path.join(HERE, "seam")
OFFSET_PNG = os.path.join(SEAM_DIR, "756-v1_offset.png")   # the image to inpaint (seam at centre)
MASK_PNG = os.path.join(SEAM_DIR, "756-v1_mask.png")       # white = the seam band to fill
PREVIEW_BLEND = os.path.join(HERE, "preview_blend_756-v1.png")
PREVIEW_INFILL = os.path.join(HERE, "preview_infill_756-v1.png")


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


def load_prepared(path, size=0):
    """Open RGBA, optional square resize, crop to a multiple of 8. Returns (im, orig_w, orig_h)."""
    im = Image.open(path).convert("RGBA")
    orig_w, orig_h = im.size
    if size:
        im = im.resize((size, size), RESAMPLE_LANCZOS)
    im = crop_mult8(im)
    if im.size[0] % 8 or im.size[1] % 8:
        sys.exit(f"dims {im.size[0]}x{im.size[1]} not a multiple of 8 after crop -- unexpected")
    return im, orig_w, orig_h


def write_preview(out_im, path):
    """2x2 tiling of the result so any wrap seam is obvious to the eye."""
    W, H = out_im.size
    prev = Image.new("RGBA", (W * 2, H * 2))
    for py in (0, H):
        for px in (0, W):
            prev.paste(out_im, (px, py))
    prev.save(path)


def report_seam(rgb0, rgb1, tag):
    hr0, vr0, ha0, va0 = wrap_seam(rgb0)
    hr1, vr1, ha1, va1 = wrap_seam(rgb1)
    print(f"[{tag}] wrap seam vs interior adjacency (1.0 = seamless; abs meandiff in parens):")
    print(f"    H  {hr0:5.2f}x ({ha0:4.1f}) -> {hr1:5.2f}x ({ha1:4.1f})"
          f"     V  {vr0:5.2f}x ({va0:4.1f}) -> {vr1:5.2f}x ({va1:4.1f})")
    ok = max(hr1, vr1) < 1.6
    print(f"    {'OK: tiles seamlessly' if ok else 'WARN: residual seam'}")
    return ok


def make_tileable(rgb, band_frac=0.14, levels=None):
    """BLEND method: offset-and-heal with a multiband blend. `rgb` float32 HxWx3.

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
    # keep a real central plateau (mask reaches 0 -> A's detail survives): flat+ramp must clear
    # the half-extent, else an over-wide band silently leaves the output as all rolled-B.
    if flat + ramp > 0.45 * min(W, H):
        scale_down = (0.45 * min(W, H)) / (flat + ramp)
        flat, ramp = flat * scale_down, ramp * scale_down
    x = np.arange(W, dtype=np.float32)
    y = np.arange(H, dtype=np.float32)
    dx = np.minimum(x, W - 1 - x)  # distance to nearest vertical edge
    dy = np.minimum(y, H - 1 - y)  # distance to nearest horizontal edge
    d = np.minimum(dx[None, :], dy[:, None])  # distance to nearest edge
    mask = 1.0 - smoothstep((d - flat) / ramp)  # 1 in a `flat`-wide border frame -> 0 in the centre

    out = pyr_blend(A, B, mask, levels=levels)
    return np.clip(out, 0, 255)


def seam_offset_and_mask(rgb, seam_frac=0.16):
    """INFILL method, step 1: roll so the wrap seam becomes a centre cross, and build the
    fill mask over that cross. Returns (offset_rgb float32 HxWx3, mask_u8 HxW: 255 = fill).

    The offset's OUTER edges are interior-adjacent rows/cols of the upscale, so they wrap
    seamlessly and must NOT be repainted -- the mask is a centre-only cross, kept clear of the
    borders. Widen the band a bit past the raw seam so the model has context on both sides.
    """
    H, W = rgb.shape[:2]
    cy, cx = H // 2, W // 2
    off = np.roll(np.roll(rgb, cy, axis=0), cx, axis=1)
    half = min(W, H) / 2.0
    bw = float(np.clip(seam_frac * min(W, H) / 2.0, 24.0, 0.4 * half))  # band half-width, kept off borders
    x = np.arange(W)
    y = np.arange(H)
    cross = (np.abs(x - cx)[None, :] < bw) | (np.abs(y - cy)[:, None] < bw)
    return off, (cross.astype(np.uint8) * 255)


def composite_infill(offset_rgb, result_rgb, mask_u8, feather_px=6.0):
    """INFILL method, step 3: paste the model's fill INSIDE THE MASK ONLY over the original
    offset, so the seamless wrap borders (mask=0) stay pixel-exact -> tiling is guaranteed even
    if the model altered unmasked pixels. A small feather smooths the fill<->original handoff.
    """
    m = Image.fromarray(mask_u8, "L").filter(ImageFilter.GaussianBlur(feather_px))
    w = (np.asarray(m).astype(np.float32) / 255.0)[..., None]
    return np.clip(offset_rgb * (1.0 - w) + result_rgb * w, 0, 255)


def install(out_rgb, keep_alpha_from, out_path, keep_alpha=False):
    """Write the shipped content PNG (opaque by default -- walls are solid)."""
    H, W = out_rgb.shape[:2]
    a = keep_alpha_from if keep_alpha else np.full((H, W), 255.0, np.float32)
    out = np.dstack([out_rgb, a]).astype(np.uint8)
    out_im = Image.fromarray(out, "RGBA")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    out_im.save(out_path)
    return out_im


def do_blend(args):
    if not os.path.exists(args.inp):
        sys.exit(f"source not found: {args.inp}\n"
                 "Drop your upscaled 756-v1.png there first (see tools/walls/README.md), "
                 "or pass --in <path>.")
    im, ow, oh = load_prepared(args.inp, args.size)
    W, H = im.size
    rgba = np.asarray(im).astype(np.float32)
    rgb = rgba[..., :3]
    out_rgb = make_tileable(rgb, band_frac=args.band_frac)

    print(f"in  : {args.inp}  ({ow}x{oh})")
    print(f"proc: {W}x{H}  (mult-of-8 ok; 8x8 cell = {W // 8}x{H // 8})")
    report_seam(rgb, out_rgb, "blend")

    out_im = install(out_rgb, rgba[..., 3], args.out, args.keep_alpha) if not (
        args.check_only or args.dry_run) else Image.fromarray(
        np.dstack([out_rgb, np.full((H, W), 255.0, np.float32)]).astype(np.uint8), "RGBA")
    write_preview(out_im, PREVIEW_BLEND)
    print(f"preview: {PREVIEW_BLEND}  (2x2 tiling)")
    if args.check_only or args.dry_run:
        print("(no content file written)")
    else:
        print(f"wrote: {args.out}")


def do_emit_seam(args):
    if not os.path.exists(args.inp):
        sys.exit(f"source not found: {args.inp}\n"
                 "Drop your upscaled 756-v1.png there first, or pass --in <path>.")
    im, ow, oh = load_prepared(args.inp, args.size)
    W, H = im.size
    rgb = np.asarray(im).astype(np.float32)[..., :3]
    off, mask = seam_offset_and_mask(rgb, seam_frac=args.seam_frac)
    os.makedirs(SEAM_DIR, exist_ok=True)
    Image.fromarray(off.astype(np.uint8), "RGB").save(OFFSET_PNG)
    Image.fromarray(mask, "L").save(MASK_PNG)
    print(f"in   : {args.inp}  ({ow}x{oh}) -> proc {W}x{H}")
    print(f"wrote: {OFFSET_PNG}   (the image to inpaint; wrap seam is now the centre cross)")
    print(f"wrote: {MASK_PNG}     (white = the seam band to regenerate)")
    print("\nNext: run a LOCAL inpainting model (Flux Fill / SD-inpaint -- NOT ChatGPT, which")
    print("regenerates the whole frame and breaks the borders) on those two, then:")
    print(f"    python tools/walls/build_wall_tileable.py --reimport <model_output.png>")
    print("Or one-shot it with:  python tools/walls/flux_infill.py")


def do_reimport(args):
    for p in (OFFSET_PNG, MASK_PNG):
        if not os.path.exists(p):
            sys.exit(f"missing {p} -- run `--emit-seam` first (the reimport composites the "
                     "model's fill over that offset so the borders stay seamless).")
    if not os.path.exists(args.reimport):
        sys.exit(f"inpainted result not found: {args.reimport}")
    offset = np.asarray(Image.open(OFFSET_PNG).convert("RGB")).astype(np.float32)
    mask = np.asarray(Image.open(MASK_PNG).convert("L"))
    H, W = offset.shape[:2]
    res_im = Image.open(args.reimport).convert("RGB")
    if res_im.size != (W, H):
        print(f"note: resizing model output {res_im.size} -> {(W, H)} to match the offset")
        res_im = res_im.resize((W, H), RESAMPLE_LANCZOS)
    result = np.asarray(res_im).astype(np.float32)

    out_rgb = composite_infill(offset, result, mask, feather_px=args.feather)
    print(f"proc: {W}x{H}  (composited model fill inside the mask; borders kept from the offset)")
    report_seam(offset, out_rgb, "infill")

    if args.check_only or args.dry_run:
        out_im = Image.fromarray(
            np.dstack([out_rgb, np.full((H, W), 255.0, np.float32)]).astype(np.uint8), "RGBA")
        write_preview(out_im, PREVIEW_INFILL)
        print(f"preview: {PREVIEW_INFILL}  (2x2 tiling)")
        print("(no content file written)")
        return
    out_im = install(out_rgb, np.full((H, W), 255.0, np.float32), args.out, keep_alpha=False)
    write_preview(out_im, PREVIEW_INFILL)
    print(f"preview: {PREVIEW_INFILL}  (2x2 tiling)")
    print(f"wrote: {args.out}")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--in", dest="inp", default=DEFAULT_IN, help="source PNG (upscaled art)")
    ap.add_argument("--out", default=DEFAULT_OUT, help="destination content PNG")
    ap.add_argument("--size", type=int, default=0,
                    help="resize (Lanczos) to SIZE x SIZE before processing; 0 = keep source dims")
    ap.add_argument("--band-frac", type=float, default=0.14,
                    help="BLEND: heal frame width as a fraction of min(W,H) (default 0.14)")
    ap.add_argument("--keep-alpha", action="store_true",
                    help="preserve source alpha (default: force fully opaque, walls are solid)")
    ap.add_argument("--check-only", action="store_true",
                    help="report seam + write preview, do NOT write the content file")
    ap.add_argument("--dry-run", action="store_true", help="process but skip writing the content file")
    # INFILL method
    ap.add_argument("--emit-seam", action="store_true",
                    help="INFILL step 1: write the offset image + seam mask for a local inpainter")
    ap.add_argument("--seam-frac", type=float, default=0.16,
                    help="INFILL: seam-band width as a fraction of min(W,H) (default 0.16)")
    ap.add_argument("--reimport", metavar="RESULT.png", default=None,
                    help="INFILL step 3: composite the inpainted RESULT over the offset + install")
    ap.add_argument("--feather", type=float, default=6.0,
                    help="INFILL: fill<->original handoff feather in px (default 6)")
    args = ap.parse_args()

    if args.emit_seam and args.reimport:
        sys.exit("--emit-seam and --reimport are separate steps; run them one at a time.")
    if args.emit_seam:
        do_emit_seam(args)
    elif args.reimport:
        do_reimport(args)
    else:
        do_blend(args)


if __name__ == "__main__":
    main()
