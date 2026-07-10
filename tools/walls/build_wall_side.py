"""Build the Level-3 wall TOWER SIDE sheet from the collidable wall texture.

Card d59266cc / plans/walls-3d-towers.md step 6. `Wall.Draw` extrudes each collidable block
into a stacked-slice shaft. Slicing the block's OWN full-resolution 8x8 cell for every slice
produces a corduroy comb: consecutive slices are the same high-frequency cell at slightly
different scales, so the slivers each slice leaves exposed repeat its detail instead of
smearing into a wall face. (Measured: the shaft's exposed sliver is ~2 design px wide, the
same order as the cell's detail.)

The fix is a low-frequency companion sheet: the same 8x8 grid, each cell AREA-AVERAGED down to
CELL px square. Stretched back over a block it reads as one smooth, softly-shaded side face,
while keeping that cell's true average colour -- so each block's shaft still matches the block
it belongs to. Area-averaging (not a centre crop) is what makes this safe: the centre texel of
some cells is a bright highlight (the brightest is RGB(121,194,240) vs a cell-average range of
only luminance 72..116), which as a slice tint would render that block as a glowing white slab.

Output is tiny (8*CELL square, a few KB) and decodes instantly, so it needs no textures.config
entry. Offline + deterministic (Pillow only), like the other tools/ asset steps; CI just ships
the committed PNG.

    python tools/walls/build_wall_side.py            # rebuild from the committed 756-v1.png
    python tools/walls/build_wall_side.py --cell 8   # coarser (smoother, less structure)

Re-run after changing 756-v1.png. Don't hand-edit the output.
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
CELL = 16  # side-sheet texels per cell. 16 keeps a hint of surface structure; 8 is flatter.


def build(cell: int) -> Image.Image:
    src = Image.open(SRC).convert("RGBA")
    if src.width % GRID or src.height % GRID:
        sys.exit(f"756-v1.png is {src.size}; both dims must be a multiple of {GRID}")
    cw, ch = src.width // GRID, src.height // GRID
    out = Image.new("RGBA", (GRID * cell, GRID * cell))
    for i in range(GRID):
        for j in range(GRID):
            box = (j * cw, i * ch, (j + 1) * cw, (i + 1) * ch)
            # BOX == area average: every source texel contributes, so the cell keeps its true
            # mean colour and no single highlight can dominate.
            out.paste(src.resize((cell, cell), Image.BOX, box=box), (j * cell, i * cell))
    return out


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cell", type=int, default=CELL, help=f"side-sheet texels per cell (default {CELL})")
    ap.add_argument("--dry-run", action="store_true", help="report what would be written, write nothing")
    args = ap.parse_args()

    if not SRC.exists():
        sys.exit(f"missing {SRC}")
    out = build(args.cell)
    if args.dry_run:
        print(f"[dry-run] would write {DST} ({out.width}x{out.height})")
        return
    out.save(DST)
    print(f"wrote {DST.relative_to(ROOT)} ({out.width}x{out.height}, {DST.stat().st_size} B)")


if __name__ == "__main__":
    main()
