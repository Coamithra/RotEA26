#!/usr/bin/env python
"""
shrink_sheet.py - downscale an already-HD sprite (sheet or single still) to a
LOWER supersample factor, trimming download payload while staying crisp at the
1440 render-target cap.

WHY: the upscale push built these at 4x (eff ~0.60 at the cap - oversampled: more
texels than the worst-case on-screen footprint ever resolves, so the extra texels
are pure payload). Dropping 4x -> 3x lands eff ~0.80 (still minified at the cap =
supersampled AA + rotation/sub-pixel headroom) and cuts each file ~40-45%.

HOW IT STAYS 1:1 IN-GAME: AlienDrawableGameComponent computes
  textureScale = SuperSampleFactor(name, actualFrameWidth) = actualFrameWidth/designWidth
  DrawScale    = scale / textureScale
so shrinking the texels lowers the factor and RAISES the draw scale to match -
on-screen size + collision are unchanged. Direct-draw sites (wing1, the landed
stills via UFO.Draw) recompute the factor at runtime the same way. The design-width
registry is NOT touched, so nothing else needs to change. (See tools/upscale/UPSCALING.md
"Choosing the factor".)

FAITHFUL RE-PACK: the engine reads a cell as
  cellW = (texW-(cols-1)*sep)/cols ; rect = (col*(cellW+sep), row*(cellH+sep), cellW, cellH)
(getFrameRectangle), sep=1. We reproduce that exact layout at 0.75x cell size, so
every frame still indexes correctly. Single frames (cols=rows=1) are a plain resize.

ALPHA: content is STRAIGHT (non-premultiplied). We premultiply -> resize -> un-premultiply
so edge pixels don't bleed toward transparent-pixel RGB (halo) - the OUTPUT stays
straight alpha (we divide back), so this does NOT violate "don't premultiply on export".

Backs the current HD sheet up to <name>.png.hd4x (never clobbers an existing backup;
the pre-upscale 48px original stays safe in <name>.png.orig). Re-runnable.

Usage:  python tools/upscale/shrink_sheet.py            # process the committed set below
        python tools/upscale/shrink_sheet.py --ratio 0.75
"""
import argparse
import os
import sys
import numpy as np
from PIL import Image

SPRITES = "web/EvilAliensWeb/wwwroot/Content/gfx/sprites"
SEP = 1  # separatingspace (1px between cells), matches every HD sheet

# name -> (cols, rows).  cols=rows=1 => single-frame still (plain resize).
# cols/rows are the LoadAnimation (columns, rows) for that sheet; AnimationData's
# arg order is (name, ROWS, COLS, ...), so e.g. ufosheet AnimationData(...,4,8,...)
# is rows=4, cols=8.
TARGETS = {
    # grid sheets (all 192px cells today, design width 48 -> factor 4 -> 3)
    "ufosheet":               (8, 4),
    "smallship":              (4, 8),
    "deathstarsheet2":        (8, 4),
    "faceofdeathspritesheet": (8, 4),
    "playersheet":            (8, 4),
    # single-frame stills (whole-image resize)
    "wing1":                  (1, 1),
    "ufometpootjes":          (1, 1),
    "smallship_landed":       (1, 1),
    "mediumship_landed":      (1, 1),
}


def resize_straight_rgba(im, w, h):
    """Downscale a straight-alpha RGBA PIL image to (w,h) via premultiplied
    filtering, returning straight-alpha RGBA (no edge halos)."""
    a = np.asarray(im, dtype=np.float64) / 255.0          # H,W,4 in 0..1
    rgb, al = a[..., :3], a[..., 3:4]
    pm = np.concatenate([rgb * al, al], axis=2)           # premultiplied
    pim = Image.fromarray((pm * 255.0 + 0.5).astype(np.uint8), "RGBA")
    pim = pim.resize((w, h), Image.Resampling.LANCZOS)
    p = np.asarray(pim, dtype=np.float64) / 255.0
    prgb, pal = p[..., :3], p[..., 3:4]
    out_rgb = np.where(pal > 0, prgb / np.clip(pal, 1e-6, None), 0.0)  # un-premultiply
    out = np.concatenate([np.clip(out_rgb, 0, 1), pal], axis=2)
    return Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGBA")


def shrink(name, cols, rows, ratio):
    path = os.path.join(SPRITES, name + ".png")
    if not os.path.isfile(path):
        print(f"  SKIP {name}: not found", file=sys.stderr)
        return None
    im = Image.open(path).convert("RGBA")
    W, H = im.size

    if cols == 1 and rows == 1:
        nw, nh = round(W * ratio), round(H * ratio)
        out = resize_straight_rgba(im, nw, nh)
        layout = f"{W}x{H} -> {nw}x{nh}"
    else:
        cw = (W - (cols - 1) * SEP) // cols
        ch = (H - (rows - 1) * SEP) // rows
        ncw, nch = round(cw * ratio), round(ch * ratio)
        nW = cols * ncw + (cols - 1) * SEP
        nH = rows * nch + (rows - 1) * SEP
        out = Image.new("RGBA", (nW, nH), (0, 0, 0, 0))   # transparent separators
        for r in range(rows):
            for c in range(cols):
                cell = im.crop((c * (cw + SEP), r * (ch + SEP),
                                c * (cw + SEP) + cw, r * (ch + SEP) + ch))
                out.paste(resize_straight_rgba(cell, ncw, nch),
                          (c * (ncw + SEP), r * (nch + SEP)))
        layout = f"{W}x{H} cell {cw} -> {nW}x{nH} cell {ncw} ({cols}x{rows})"

    # back up the current HD sheet (don't clobber; .orig already holds the 48px original)
    bak = path + ".hd4x"
    if not os.path.exists(bak):
        Image.open(path).save(bak, format="PNG")
    old = os.path.getsize(path)
    out.save(path, optimize=True)
    new = os.path.getsize(path)
    print(f"  {name:24s} {layout:42s} {old//1024:5d}K -> {new//1024:5d}K "
          f"({100*new//old:3d}%)")
    return old, new


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ratio", type=float, default=0.75,
                    help="cell scale factor (0.75 = 4x->3x). default 0.75")
    args = ap.parse_args()
    print(f"shrink_sheet.py  ratio={args.ratio}  (4x -> {4*args.ratio:.2g}x)\n")
    to, tn = 0, 0
    for name, (cols, rows) in TARGETS.items():
        r = shrink(name, cols, rows, args.ratio)
        if r:
            to += r[0]
            tn += r[1]
    if to:
        print(f"\n  TOTAL {to//1024}K -> {tn//1024}K  "
              f"(saved {(to-tn)//1024}K, {100*(to-tn)//to}%)")


if __name__ == "__main__":
    main()
