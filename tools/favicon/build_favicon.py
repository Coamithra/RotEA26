#!/usr/bin/env python
"""Build the browser tab favicon from the in-game player-UFO sprite.

The site's tab icon used to be a hand-drawn green "grey alien" head
(`wwwroot/favicon.svg`). This regenerates it from THE actual game art: one
frame of the player's flying-saucer sheet (`GFX/Sprites/ufosheet`), composited
onto the menu's near-black rounded-rect tile so the grey saucer stays visible
on light browser tab bars.

Outputs (committed; the loader/host page reference them):
  wwwroot/favicon.ico        multi-res 16/32/48/64 (classic tab icon)
  wwwroot/favicon-180.png    180px (apple-touch-icon / PWA)

Re-run after changing the source sheet or the knobs below; don't hand-edit the
outputs. Offline asset step (Pillow only), like the other tools/ pipelines.

    python tools/favicon/build_favicon.py
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHEET = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot",
                     "Content", "gfx", "sprites", "ufosheet.png")
OUT_DIR = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot")

# ufosheet is a uniform 8-col x 4-row spin sheet (no .dat sidecar; the player
# UFO slices it as a grid via AlienDrawableGameComponent). Frame 28 is the
# top-3/4 "hero" pose: full elliptical disc, bright teal dome, the alien
# silhouette just visible inside -- the most legible UFO at 16px.
COLS, ROWS = 8, 4
FRAME = 28

BG = (5, 3, 10, 255)     # menu near-black (#05030a), matches the old favicon tile
MARGIN_FRAC = 0.10       # padding between the saucer and the tile edge
RADIUS_FRAC = 0.18       # rounded-rect corner radius
ICO_SIZES = [16, 32, 48, 64]
TOUCH_SIZE = 180


def extract_frame(sheet, idx):
    w, h = sheet.size
    cw, ch = w // COLS, h // ROWS
    r, c = divmod(idx, COLS)
    cell = sheet.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
    bbox = cell.getbbox()           # tight-crop to the saucer's alpha bounds
    return cell.crop(bbox) if bbox else cell


def render(sprite, size):
    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(icon).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * RADIUS_FRAC), fill=BG)
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
