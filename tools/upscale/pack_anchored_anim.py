"""Pack a folder of magenta-background frames into ONE sprite sheet for a *fixed-camera*
animation (the subject stays anchored; only its parts move -- e.g. the spider rear-up,
AnimGen prompt: "Fixed camera, static shot").

Unlike pack_frames.py / repack_for_engine.py this does NOT footprint-match to an existing
sprite and does NOT bbox-centre per frame (that would make an anchored subject jitter).
Instead it crops EVERY frame to ONE shared window = the union of all silhouettes (+margin),
so motion is preserved exactly. Pipeline per frame:

  RGBA frame --composite over magenta--> key_magenta (despill, straight alpha)
            --crop to shared union box--> resize by one global factor --> place in cell

Compositing over magenta first is essential: some repaired frames carry real transparency
whose underlying RGB is black, which would otherwise key to opaque black.

usage:
  python pack_anchored_anim.py <frames_dir> <out.png> [--every N] [--maxdim 2048]
                               [--cols C --rows R] [--margin 8] [--sep 1] [--preview]

--every 2 halves the frame count (fps); grid auto-squares if cols/rows omitted.
Prints grid, cell size and frame count so you can wire it into the registry.
"""
from __future__ import annotations
import argparse, glob, math, os, sys
import numpy as np
from PIL import Image
from keycompare import key_magenta


def load_keyed(path: str) -> np.ndarray:
    """RGBA frame -> keyed RGBA (magenta -> transparent, straight alpha)."""
    im = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    a = im[:, :, 3:4] / 255.0
    mag = np.zeros_like(im[:, :, :3]); mag[:, :, 0] = 255; mag[:, :, 2] = 255
    comp = (im[:, :, :3] * a + mag * (1 - a)).astype(np.uint8)
    return key_magenta(comp)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("frames_dir")
    ap.add_argument("out")
    ap.add_argument("--every", type=int, default=1, help="keep every Nth frame (2 = halve fps)")
    ap.add_argument("--maxdim", type=int, default=2048, help="cap on sheet width/height (px)")
    ap.add_argument("--cellw", type=int, default=0, help="exact per-frame cell width in px "
                    "(overrides --maxdim scaling; size it = designWidth * window/800 for a 1:1 draw)")
    ap.add_argument("--cols", type=int, default=0)
    ap.add_argument("--rows", type=int, default=0)
    ap.add_argument("--margin", type=int, default=8, help="px added around the union bbox")
    ap.add_argument("--sep", type=int, default=1)
    ap.add_argument("--thr", type=int, default=40, help="alpha threshold for bbox")
    ap.add_argument("--preview", action="store_true", help="also write *_preview.png on a checker")
    args = ap.parse_args()

    files = sorted(glob.glob(os.path.join(args.frames_dir, "frame_*.png")))
    if not files:
        files = sorted(glob.glob(os.path.join(args.frames_dir, "*.png")))
    files = files[::args.every]
    n = len(files)
    if n == 0:
        print("no frames found", file=sys.stderr); return 1

    keyed = [load_keyed(f) for f in files]
    H, W = keyed[0].shape[:2]

    # shared (union) bbox across every kept frame
    gx0 = gy0 = 10 ** 9; gx1 = gy1 = -1
    for k in keyed:
        ys, xs = np.where(k[:, :, 3] > args.thr)
        gx0 = min(gx0, xs.min()); gy0 = min(gy0, ys.min())
        gx1 = max(gx1, xs.max()); gy1 = max(gy1, ys.max())
    x0 = max(0, gx0 - args.margin); y0 = max(0, gy0 - args.margin)
    x1 = min(W, gx1 + 1 + args.margin); y1 = min(H, gy1 + 1 + args.margin)
    cw0, ch0 = x1 - x0, y1 - y0

    # grid: explicit, else auto roughly-square accounting for the cell aspect
    cols, rows = args.cols, args.rows
    if not (cols and rows):
        cols = max(1, round(math.sqrt(n * ch0 / cw0)))
        rows = math.ceil(n / cols)
    if cols * rows < n:
        rows = math.ceil(n / cols)

    # global scale so the packed sheet fits maxdim. Separators don't scale, so size the
    # cells against the space left after them (else flooring still spills 1px over the cap).
    sep = args.sep
    if args.cellw:                                   # exact cell width (for a 1:1 in-game draw)
        sf = args.cellw / cw0
        cw = args.cellw; ch = max(1, round(ch0 * sf))
    else:
        avail_w = args.maxdim - (cols - 1) * sep
        avail_h = args.maxdim - (rows - 1) * sep
        sf = min(1.0, avail_w / (cols * cw0), avail_h / (rows * ch0))
        cw = max(1, int(cw0 * sf)); ch = max(1, int(ch0 * sf))

    SW = cols * cw + (cols - 1) * sep
    SH = rows * ch + (rows - 1) * sep
    sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
    for i, k in enumerate(keyed):
        crop = Image.fromarray(k[y0:y1, x0:x1], "RGBA").resize((cw, ch), Image.Resampling.LANCZOS)
        r, c = divmod(i, cols)
        sheet.paste(crop, (c * (cw + sep), r * (ch + sep)))
    sheet.save(args.out)

    print(f"packed {args.out}")
    print(f"  frames {n} (every {args.every} of {n * args.every if args.every>1 else n})  grid {cols}x{rows}")
    print(f"  union bbox {cw0}x{ch0} -> cell {cw}x{ch}  (scale {sf:.3f})")
    print(f"  sheet {SW}x{SH}  maxdim {max(SW,SH)} (<= {args.maxdim}: {max(SW,SH)<=args.maxdim})")

    if args.preview:
        Z = 12; bg = np.zeros((SH, SW, 3), np.uint8)
        yy, xx = np.mgrid[0:SH, 0:SW]
        bg[((xx // Z + yy // Z) % 2) == 0] = 70; bg[((xx // Z + yy // Z) % 2) == 1] = 120
        arr = np.asarray(sheet).astype(float); al = arr[:, :, 3:4] / 255
        comp = (arr[:, :, :3] * al + bg * (1 - al)).astype(np.uint8)
        pv = os.path.splitext(args.out)[0] + "_preview.png"
        Image.fromarray(comp, "RGB").save(pv)
        print(f"  preview {pv}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
