#!/usr/bin/env python
"""Build the browser tab favicon from the in-game player-UFO sprite.

The site's tab icon used to be a hand-drawn green "grey alien" head
(`wwwroot/favicon.svg`). This regenerates it from THE actual game art: one
frame of the player's flying-saucer sheet (`GFX/Sprites/ufosheet`), on a
TRANSPARENT background so the saucer sits directly on the tab bar and adapts
to any browser theme (no black tile).

Outputs (committed; the loader/host page reference them):
  wwwroot/favicon.ico        multi-res 16/32/48/64 (classic tab icon)
  wwwroot/favicon-180.png    180px (apple-touch-icon / PWA)

Re-run after changing the source sheet or the knobs below; don't hand-edit the
outputs. Offline asset step (Pillow only), like the other tools/ pipelines.

    python tools/favicon/build_favicon.py
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHEET = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot",
                     "Content", "gfx", "sprites", "ufosheet.png")
OUT_DIR = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot")

# ufosheet is an 8-col x 4-row spin sheet (no .dat sidecar; the player UFO
# slices it as a grid via AlienDrawableGameComponent) with a 1px separator
# between cells -- mirror the engine's slicing so we extract the exact frame it
# draws (AlienDrawableGameComponent.cs ~L560). Frame 28 is the top-3/4 "hero"
# pose: full elliptical disc, bright teal dome, the alien silhouette just
# visible inside -- the most legible UFO at 16px.
COLS, ROWS, SEP = 8, 4, 1
FRAME = 28

MARGIN_FRAC = 0.10       # padding between the saucer and the icon edge
ICO_SIZES = [16, 32, 48, 64]
TOUCH_SIZE = 180


def extract_frame(sheet, idx):
    w, h = sheet.size
    cw = (w - (COLS - 1) * SEP) // COLS      # engine cell pitch (separator-aware)
    ch = (h - (ROWS - 1) * SEP) // ROWS
    r, c = divmod(idx, COLS)
    x, y = c * (cw + SEP), r * (ch + SEP)
    cell = sheet.crop((x, y, x + cw, y + ch))
    bbox = cell.getbbox()           # tight-crop to the saucer's alpha bounds
    return cell.crop(bbox) if bbox else cell


def render(sprite, size):
    # Transparent canvas -- the saucer sits directly on the tab bar (no tile),
    # so the icon adapts to any browser theme instead of a black square.
    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    avail = size - 2 * int(size * MARGIN_FRAC)
    spr = sprite.copy()
    spr.thumbnail((avail, avail), Image.LANCZOS)
    icon.alpha_composite(spr, ((size - spr.width) // 2, (size - spr.height) // 2))
    return icon


def main():
    sheet = Image.open(SHEET).convert("RGBA")
    sprite = extract_frame(sheet, FRAME)

    ico_path = os.path.join(OUT_DIR, "favicon.ico")
    # Render each .ico member from the hi-res sprite (sharper than letting PIL
    # downscale one large frame), largest first.
    members = [render(sprite, s) for s in sorted(ICO_SIZES, reverse=True)]
    members[0].save(ico_path, format="ICO",
                    sizes=[(s, s) for s in sorted(ICO_SIZES, reverse=True)],
                    append_images=members[1:])
    print("wrote", ico_path, "sizes", sorted(ICO_SIZES))

    touch_path = os.path.join(OUT_DIR, "favicon-180.png")
    render(sprite, TOUCH_SIZE).save(touch_path)
    print("wrote", touch_path, f"({TOUCH_SIZE}px)")


if __name__ == "__main__":
    main()
