#!/usr/bin/env python
# ---------------------------------------------------------------------------
# render_sample.py -- render a scrutiny sheet of the COMMITTED menufont, laid
# out exactly like the in-game SpriteBatchWrapper.DrawStringScaled path (KNI's
# SpriteFont layout + the 3x-atlas size ratio). What you see here is what the
# game draws. Writes tools/font/_sample.png.
#   python tools/font/render_sample.py [--zoom N]
# ---------------------------------------------------------------------------
import os, sys, struct
import numpy as np
from PIL import Image

LANCZOS = Image.Resampling.LANCZOS
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MENU = os.path.join(ROOT, 'web/EvilAliensWeb/wwwroot/Content/gfx/menu')
OUT  = os.path.join(ROOT, 'tools/font/_sample.png')


def load_font():
    d = open(os.path.join(MENU, 'menufont.fnt'), 'rb').read(); off = 0
    def i():
        nonlocal off; v = struct.unpack_from('<i', d, off)[0]; off += 4; return v
    def f():
        nonlocal off; v = struct.unpack_from('<f', d, off)[0]; off += 4; return v
    line = i(); sp = f(); i(); i(); n = i()
    chars  = [i() for _ in range(n)]
    bounds = [(i(), i(), i(), i()) for _ in range(n)]
    crop   = [(i(), i(), i(), i()) for _ in range(n)]
    kern   = [(f(), f(), f()) for _ in range(n)]
    atlas = Image.open(os.path.join(MENU, 'menufont.fnt.png')).convert('RGBA')
    g = {chr(c): dict(b=bounds[k], c=crop[k], k=kern[k]) for k, c in enumerate(chars)}
    return dict(line=line, spacing=sp, glyphs=g, atlas=atlas)


def tinted(tile, color):
    if color == (255, 255, 255):
        return tile
    a = np.asarray(tile, dtype=np.uint16)
    a[..., 0] = a[..., 0] * color[0] // 255
    a[..., 1] = a[..., 1] * color[1] // 255
    a[..., 2] = a[..., 2] * color[2] // 255
    return Image.fromarray(a.astype(np.uint8), 'RGBA')


def measure(font, text):
    g = font['glyphs']; sp = font['spacing']
    pen = 0.0; first = True
    for ch in text:
        gd = g.get(ch) or g.get(' ')
        lsb, w, rsb = gd['k']
        pen += (max(lsb, 0.0) if first else sp + lsb); first = False
        pen += w + rsb
    return pen


def draw_line(canvas, font, text, top, zoom, color):
    g = font['glyphs']; sp = font['spacing']; atlas = font['atlas']
    pen = 0.0; first = True
    for ch in text:
        gd = g.get(ch)
        if gd is None:
            gd = g.get(' ');  ch = ' '
        bx, by, bw, bh = gd['b']
        cx, cy, cw, chh = gd['c']
        lsb, w, rsb = gd['k']
        pen += (max(lsb, 0.0) if first else sp + lsb); first = False
        if bw > 0 and cw > 0 and chh > 0:
            tile = atlas.crop((bx, by, bx + bw, by + bh)).resize(
                (max(1, round(cw * zoom)), max(1, round(chh * zoom))), LANCZOS)
            tile = tinted(tile, color)
            canvas.alpha_composite(tile, (round((pen + cx) * zoom), round((top + cy) * zoom)))
        pen += w + rsb


def main():
    zoom = 4
    if '--zoom' in sys.argv:
        zoom = float(sys.argv[sys.argv.index('--zoom') + 1])
    font = load_font()

    WHITE = (255, 255, 255)
    BLUE  = (120, 150, 255)      # player-1 HUD score colour
    GREY  = (150, 150, 150)      # unselected menu entry
    GOLD  = (255, 210, 90)

    lines = [
        ('The quick brown fox jumps over the lazy dog.', WHITE),
        ('THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG', WHITE),
        ('Lorem ipsum dolor sit amet, consectetur', WHITE),
        ('adipiscing elit, sed do eiusmod tempor.', GREY),
        ('ABCDEFGHIJKLMNOPQRSTUVWXYZ', WHITE),
        ('abcdefghijklmnopqrstuvwxyz', WHITE),
        ('0123456789  .,!\'":()-?%&', WHITE),
        ('Score: 1234567890', BLUE),
        ('REVENGE of the EVIL ALIENS!  50% off?', GOLD),
        ('"Awardment" Unlocked - Press Start (Don\'t stop)', WHITE),
    ]

    pad = 12
    W = int(max(measure(font, t) for t, _ in lines)) + pad * 2
    H = font['line'] * len(lines) + pad * 2
    canvas = Image.new('RGBA', (int(W * zoom), int(H * zoom)), (16, 16, 20, 255))
    for li, (text, color) in enumerate(lines):
        draw_line(canvas, font, text, pad + li * font['line'], zoom, color)
    canvas.convert('RGB').save(OUT)
    print(f'wrote {OUT}  ({canvas.size}, zoom {zoom}, lineSpacing {font["line"]}, '
          f'spacing {font["spacing"]:.2f}, {len(font["glyphs"])} glyphs)')


if __name__ == '__main__':
    main()
