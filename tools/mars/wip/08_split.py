#!/usr/bin/env python
"""
08_split.py -- cut the final looping strip into GPU-sized tiles for the game.

`strip_horizon.png` (07) is 19048px wide -- past the WebGL max texture size -- so
it ships as N contiguous tiles. The strip is SEAMLESS and natively loopable, so
the cut location is arbitrary: we cut N near-equal tiles that re-abut pixel-exact
(no overlap, no blend). Default N=12.

Tiles -> wwwroot/Content/GFX/MarsBG/marsloop{1..N}.png (lowercase under capital
`Content/`; the live host is case-sensitive). They REPLACE the old mars1..6 +
mirror in Background.SetMars (drawn at size 1/3.238, position.Y=300, no mirror --
see STITCH_ALGORITHM.md "Output + finish").
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SRC = os.path.join(HERE, "strip_horizon.png")
DST = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "GFX", "MarsBG")
N = 12


def main():
    im = Image.open(SRC).convert("RGBA")
    W, H = im.size
    arr = np.asarray(im)
    edges = [round(i * W / N) for i in range(N + 1)]     # contiguous, sum == W
    assert edges[0] == 0 and edges[-1] == W
    widths = []
    for i in range(N):
        tile = arr[:, edges[i]:edges[i + 1]]
        Image.fromarray(tile, "RGBA").save(os.path.join(DST, f"marsloop{i + 1}.png"))
        widths.append(edges[i + 1] - edges[i])
    assert sum(widths) == W
    print(f"strip {W}x{H} -> {N} tiles {widths} (sum {sum(widths)}, max {max(widths)})")
    print(f"  -> {DST}/marsloop1..{N}.png")
    print(f"  on-screen design width = {W / 3.238:.1f} at size 1/3.238 (no mirror)")


if __name__ == "__main__":
    main()
