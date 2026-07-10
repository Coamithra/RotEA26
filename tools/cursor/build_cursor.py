#!/usr/bin/env python
"""Build the aiming-reticle art: the CSS hardware cursors + the in-game intro sprite.

During gameplay the aiming reticle is the ACTUAL OS cursor (set via
`canvas.style.cursor: url(reticle/<px>.png) ...` from index.html's eaCursor) so it is
zero-lag, instead of a game-loop-drawn sprite that trails the mouse. At the start of a
level `MousePointer` plays a scale+rotate intro with the SPRITE and then hands the
pointer off to that CSS cursor -- so the two must agree on size and shape.

A CSS cursor image is a FIXED pixel size, but the game letterbox-upscales its 800x600
design space to the window, so one fixed cursor reads correct at exactly one window
size (and small on a big monitor). So this emits a LADDER of cursors, one per
`SIZES` entry, and `MousePointer.ChooseCursorPx()` picks the bucket nearest
`ReticleDesignPx * windowPerDesign` -- the reticle then holds its design-space size at
any window size, and the intro's end scale is derived from the SAME number so the
sprite->cursor handoff never pops. The bucket rule is `round(target/8)*8` clamped to
[24,96]: keep SIZES in sync with MousePointer's CursorPxStep/Min/MaxCursorPx.

The reticle is four axis-aligned red bars with a gap in the middle, so each size is
DRAWN at its native resolution rather than resampled from one master. Two reasons:
  * Resampling a crosshair blurs it, and a blurry cursor is very visible. (The old tool
    tried to upscale the original 26px `cursor2.png` with `Image.thumbnail`, which only
    ever SHRINKS -- so the shipped cursor was 26px of art floating in a 48px canvas and
    the OS cursor came out ~half the size the intro ended at.)
  * The intro sprite is drawn at up to 4x the largest cursor, so it wants headroom.

Every output has its bars running edge to edge (alpha bbox == full canvas). That
invariant is load-bearing:
  * `MousePointer.CssHandoffScale()` divides by `texture.Width` to size the sprite, so
    any padding would shrink the drawn reticle below the cursor.
  * index.html's hotspot is the image centre (px/2), which is where the bars cross only
    when the art is centred and unpadded.

Outputs (committed):
  wwwroot/reticle/<px>.png            one per SIZES entry, for `cursor: url(...)`
  wwwroot/Content/gfx/cursor2.png     384x384 intro sprite (4x the largest cursor)

Re-run after changing a knob; don't hand-edit the outputs.
Offline asset step (Pillow only), like tools/favicon/build_favicon.py.

    python tools/cursor/build_cursor.py
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WWWROOT = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot")
CURSOR_DIR = os.path.join(WWWROOT, "reticle")
SPRITE_OUT = os.path.join(WWWROOT, "Content", "gfx", "cursor2.png")

# The cursor ladder, in window px. 24 is about the design-faithful size in an 800x600
# window; 96 covers a 4K fullscreen letterbox and stays well under the ~128px size at
# which browsers ignore a cursor image outright. The 8px step quantizes the reticle by
# at most 4px (<=8%), which is imperceptible, and every entry is drawn crisp.
SIZES = list(range(24, 97, 8))

# The intro sprite starts at 4x the cursor size (MousePointer's `1f + num * 3f`), so
# author the texture at 4x the LARGEST cursor: 1:1 texels at the animation's biggest
# frame, bilinear-downsampled the rest of the way. MousePointer divides by
# texture.Width, so this never affects on-screen size, only crispness.
SPRITE_PX = max(SIZES) * 4

# Bar thickness and central-gap radius as fractions of the image size -- the original
# 26px art's proportions (2/26 thick, gap radius 4/26), nudged to a clean 1/12 and 1/6
# so the bars read a touch heavier at the small end (they were getting spindly).
THICK_FRAC = 1.0 / 12.0
GAP_FRAC = 1.0 / 6.0

COLOR = (255, 0, 0, 255)


def even(x, minimum=2):
    """Round to a positive even int -- the bars must stay symmetric about the centre."""
    return max(minimum, int(round(x / 2.0)) * 2)


def draw_reticle(size):
    """Four red bars running from the gap radius out to each edge."""
    if size % 2:
        raise SystemExit(f"cursor size {size} must be even (bars centre on size/2)")
    thickness, gap = even(size * THICK_FRAC), even(size * GAP_FRAC)
    if gap + thickness >= size // 2:
        raise SystemExit(f"cursor size {size}: gap {gap} swallows the bars")
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    half = size // 2
    lo, hi = half - thickness // 2, half + thickness // 2 - 1  # inclusive bar span
    d.rectangle([lo, 0, hi, half - gap - 1], fill=COLOR)       # up
    d.rectangle([lo, half + gap, hi, size - 1], fill=COLOR)    # down
    d.rectangle([0, lo, half - gap - 1, hi], fill=COLOR)       # left
    d.rectangle([half + gap, lo, size - 1, hi], fill=COLOR)    # right
    if img.getbbox() != (0, 0, size, size):
        raise SystemExit(f"cursor size {size}: bars must reach every edge")
    return img, thickness


def main():
    os.makedirs(CURSOR_DIR, exist_ok=True)
    # Drop stale sizes so a shrunk SIZES list can't leave an orphan the game may request.
    for stale in os.listdir(CURSOR_DIR):
        if stale.endswith(".png") and stale[:-4].isdigit() and int(stale[:-4]) not in SIZES:
            os.remove(os.path.join(CURSOR_DIR, stale))
            print(f"removed stale {stale}")

    for size in SIZES:
        img, thickness = draw_reticle(size)
        img.save(os.path.join(CURSOR_DIR, f"{size}.png"))
        print(f"wrote reticle/{size}.png ({thickness}px bars, hotspot {size // 2},{size // 2})")

    sprite, thickness = draw_reticle(SPRITE_PX)
    sprite.save(SPRITE_OUT)
    print(f"wrote {SPRITE_OUT} ({SPRITE_PX}px, {thickness}px bars)")


if __name__ == "__main__":
    main()
