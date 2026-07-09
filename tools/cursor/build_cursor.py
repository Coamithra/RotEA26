#!/usr/bin/env python
"""Build the CSS hardware-cursor reticle from the in-game reticle sprite.

During gameplay the aiming reticle is now the ACTUAL OS cursor (set via
`canvas.style.cursor: url(reticle.png) ...` from index.html's eaCursor) so it is
zero-lag, instead of a game-loop-drawn sprite that trails the mouse. A CSS cursor
image must be a small standalone file (<=128px; browsers commonly cap around
32-48), so this exports the reticle art (`Content/gfx/cursor2.png`, 26px) as a
dedicated, slightly upscaled `wwwroot/reticle.png` (48px, transparent).

The reticle is drawn CENTRED on the pointer in-game, so the CSS hotspot is the
image centre (24,24 for 48px) -- index.html encodes that.

Outputs (committed; index.html references it):
  wwwroot/reticle.png     48x48 straight-alpha reticle for `cursor: url(...)`

Re-run after changing the source sprite or SIZE; don't hand-edit the output.
Offline asset step (Pillow only), like tools/favicon/build_favicon.py.

    python tools/cursor/build_cursor.py
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot",
                   "Content", "gfx", "cursor2.png")
OUT = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot", "reticle.png")

# 48px matches the reticle's on-screen size at a typical window scale (the in-game
# 26px design sprite letterboxes up to ~42px on a 1280-wide window) while staying
# well under the browser cursor-size cap.
SIZE = 48


def main():
    src = Image.open(SRC).convert("RGBA")
    # Tight-crop to the reticle's alpha bounds so the centre of the exported image
    # is the centre of the visible reticle (== the in-game centred hotspot).
    bbox = src.getbbox()
    if bbox:
        src = src.crop(bbox)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    spr = src.copy()
    spr.thumbnail((SIZE, SIZE), Image.LANCZOS)
    out.alpha_composite(spr, ((SIZE - spr.width) // 2, (SIZE - spr.height) // 2))
    out.save(OUT)
    print("wrote", OUT, f"({SIZE}px, hotspot {SIZE // 2},{SIZE // 2})")


if __name__ == "__main__":
    main()
