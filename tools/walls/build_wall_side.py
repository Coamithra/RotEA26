"""Build the Level-3 wall TOWER SIDE sheet from the collidable wall texture.

Card d59266cc / plans/walls-3d-towers.md step 6. `Wall.Draw` extrudes each collidable block
into a stacked-slice shaft. Slicing the block's OWN full-resolution 8x8 cell for every slice
produces a corduroy comb: consecutive slices are the same high-frequency cell at slightly
different scales, so the slivers each slice leaves exposed repeat its detail instead of
smearing into a wall face. So the slices sample this low-frequency companion instead.

The sheet is ONE CONTIGUOUS, SEAMLESS image: `756-v1.png` area-averaged down to GRID*CELL square,
then wrap-padded by CELL on the right and bottom. Two properties fall out of that, and both are
load-bearing -- do not "simplify" this into per-cell tiles:

1. NO SEAMS BETWEEN ADJACENT BLOCKS. Block (i, j) samples a CELL-sized window at
   `((j%8)*CELL, (i%8)*CELL)`, so its neighbour's window ABUTS it in the source. Each block
   stretches its window across its own footprint, and those footprints are edge-to-edge on screen,
   so at a shared edge both blocks sample the identical texel and the texture runs straight
   through. (Build the sheet as 64 isolated per-cell tiles and every block boundary hard-edges --
   which is exactly what an earlier revision did.) This is the same trick the TOP faces have always
   used: they sample the seamless `756-v1` 8x8 grid directly.

2. THE SCAN CAN WRAP. `Wall.DrawTowerShafts` slides that window DIAGONALLY across the sheet as the
   shaft descends (its `sidescan` knob), taking the origin mod `GRID*CELL`. Because `756-v1` tiles
   seamlessly on all four edges, the wrap is invisible, and the CELL of padding means a window whose
   origin lands anywhere in [0, GRID*CELL) stays inside the image. As a shaft descends it walks into
   the content of the NEIGHBOURING cells -- i.e. the very texture those neighbouring blocks show on
   their caps -- so the walk stays coherent with the wall it belongs to.

Area-averaging (not a centre crop) is what keeps the comb gone AND keeps each cell's true mean
colour: the centre texel of some cells is a bright highlight (the brightest is RGB(121,194,240) vs
a cell-average luminance range of only 72..116), which as a slice tint would render that block as a
glowing white slab.

Output: (GRID*CELL + CELL)^2 = 576x576. Decodes instantly, so no textures.config entry.
Offline + deterministic (Pillow only), like the other tools/ asset steps; CI just ships the PNG.

    python tools/walls/build_wall_side.py            # rebuild from the committed 756-v1.png
    python tools/walls/build_wall_side.py --cell 32  # smaller sheet, softer faces

Re-run after changing 756-v1.png. Don't hand-edit the output.

CELL IS A CONTRACT: it is the sampling-window size, and `Wall.SideWindow` must match it. The game
derives `scanSpan = side.Width - SideWindow` (= GRID*CELL), which is the wrap period.

`?wallsidescan=` is measured in TEXELS OF TRAVEL PER SLICE, so the natural value is simply 1 and it
does not move when this sheet is resized. Below 1 slices repeat a window and smear; above it they
skip texels and the shaft corrugates into ridges.
"""
import argparse
import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = ROOT / "web" / "EvilAliensWeb" / "wwwroot" / "Content" / "gfx" / "base"
SRC = BASE / "756-v1.png"
DST = BASE / "756-v1-side.png"

GRID = 8  # Wall.Draw samples 756-v1 as an 8x8 grid of cells; the side sheet must match.
CELL = 64  # sampling-window size, and the per-cell pitch. MUST equal Wall.SideWindow.
# 64 keeps roughly one texel per on-screen pixel at a typical block size (~67 design px), so the
# half-texel CLAMP at a window edge is sub-pixel. At 16 the window magnified ~4x and every block
# boundary showed a soft seam band -- adjacent atlas windows do not filter across their shared
# edge, they each clamp, so continuity of CONTENT is not enough on its own.


def build(cell: int) -> Image.Image:
    src = Image.open(SRC).convert("RGBA")
    if src.width % GRID or src.height % GRID:
        sys.exit(f"756-v1.png is {src.size}; both dims must be a multiple of {GRID}")
    span = GRID * cell
    # BOX == area average. Cell boundaries land exactly on multiples of `cell`, so cell (i, j) of
    # the result is precisely the average of cell (i, j) of the source -- no bleed across cells.
    flat = src.resize((span, span), Image.BOX)
    out = Image.new("RGBA", (span + cell, span + cell))
    out.paste(flat, (0, 0))
    # Wrap padding. 756-v1 tiles seamlessly on all four edges (Wall.Draw's 8x8 wrap depends on it),
    # so copying the leading rows/cols to the trailing edge is a true wrap, not a mirror.
    out.paste(flat.crop((0, 0, cell, span)), (span, 0))
    out.paste(flat.crop((0, 0, span, cell)), (0, span))
    out.paste(flat.crop((0, 0, cell, cell)), (span, span))
    return out


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cell", type=int, default=CELL, help=f"sampling window; must match Wall.SideWindow (default {CELL})")
    ap.add_argument("--dry-run", action="store_true", help="report what would be written, write nothing")
    args = ap.parse_args()

    if not SRC.exists():
        sys.exit(f"missing {SRC}")
    out = build(args.cell)
    if args.dry_run:
        print(f"[dry-run] would write {DST} ({out.width}x{out.height})")
        return
    out.save(DST)
    span = GRID * args.cell
    print(f"wrote {DST.relative_to(ROOT)} ({out.width}x{out.height}, {DST.stat().st_size} B)")
    print(f"  window={args.cell}  scanSpan={span}  (wallsidescan is in texels/slice; natural = 1)")


if __name__ == "__main__":
    main()
