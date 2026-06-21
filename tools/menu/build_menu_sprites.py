# Procedural menu UI sprites for the Stage-13 menu reskin.
#
# Emits crisp, white (tint-in-engine), transparent PNGs into
# wwwroot/Content/GFX/Menu. The menu draws them through SpriteBatchWrapper and
# tints/bloons them, so everything here is pure white-on-transparent geometry,
# supersampled then LANCZOS-downsampled for clean anti-aliasing.
#
#   pointer.png   right-pointing arcade triangle (selection cursor)
#   hudring.png   techy reticle / targeting ring (rotates behind the menu list)
#
# Re-run after tweaking; the menu loads them as GFX/Menu/pointer + GFX/Menu/hudring.
import math
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "..",
                   "web", "EvilAliensWeb", "wwwroot", "Content", "GFX", "Menu")
OUT = os.path.abspath(OUT)


def save(img, name, size):
    img = img.resize((size, size), Image.LANCZOS)
    path = os.path.join(OUT, name)
    img.save(path)
    print("wrote", path, img.size)


def build_pointer():
    base, S = 128, 4
    W = base * S
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # A solid right-pointing triangle (the ► selection cursor in the mockups),
    # slightly inset so the AA edge isn't clipped.
    pts = [(34, 20), (34, 108), (110, 64)]
    d.polygon([(x * S, y * S) for x, y in pts], fill=(255, 255, 255, 255))
    save(img, "pointer.png", base)


def build_hudring():
    base, S = 768, 3
    W = base * S
    C = W / 2.0
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def arc(r, a0, a1, width, a):
        r *= S
        d.arc([C - r, C - r, C + r, C + r], a0, a1,
              fill=(255, 255, 255, a), width=max(1, int(width * S)))

    def circ(r, width, a):
        arc(r, 0, 360, width, a)

    # Main outer ring, broken into four arcs with small gaps at the cardinals
    # for a HUD / targeting feel.
    for k in range(4):
        arc(350, k * 90 + 6, k * 90 + 84, 4, 235)
    # Faint full inner ring.
    circ(300, 2, 150)
    # Radial tick marks (skip near the cardinal gaps).
    for i in range(72):
        deg = i * 5
        if min(deg % 90, 90 - (deg % 90)) < 8:
            continue
        ang = math.radians(deg)
        r0, r1 = 356 * S, 368 * S
        d.line([C + math.cos(ang) * r0, C + math.sin(ang) * r0,
                C + math.cos(ang) * r1, C + math.sin(ang) * r1],
               fill=(255, 255, 255, 175), width=int(2 * S))
    # Four bright node accents on the diagonals.
    for k in range(4):
        ang = math.radians(45 + k * 90)
        cx, cy = C + math.cos(ang) * 350 * S, C + math.sin(ang) * 350 * S
        rad = 9 * S
        d.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=(255, 255, 255, 255))
    save(img, "hudring.png", base)


def build_vignette():
    # A soft radial darkener: transparent in the centre, ramping to ~70% black at
    # the corners. Drawn full-screen over the menu backdrop to tame the bright
    # nebula edges, focus the eye on the menu, and lift text contrast. Black RGB,
    # varying alpha (straight). Smooth gradient, so no supersampling needed.
    import numpy as np
    W, H = 1024, 768
    yy, xx = np.mgrid[0:H, 0:W]
    dx = (xx - W / 2.0) / (W / 2.0)
    dy = (yy - H / 2.0) / (H / 2.0)
    r = np.sqrt(dx * dx + dy * dy)            # 0 centre, 1 edge-mid, ~1.41 corner
    a = np.clip((r - 0.50) / (1.25 - 0.50), 0.0, 1.0) ** 1.3
    img = np.zeros((H, W, 4), dtype="uint8")
    img[..., 3] = (a * 175).astype("uint8")
    Image.fromarray(img, "RGBA").save(os.path.join(OUT, "vignette.png"))
    print("wrote", os.path.join(OUT, "vignette.png"), (W, H))


def build_star():
    # Menu starfield sprite. Kept at 236x236 so Star.cs's baked size math is unchanged.
    # A tight bright core + soft glow + faint 4-point diffraction spikes: the tiny distant
    # stars read as clean points, and the ones that grow as they warp outward twinkle like
    # real stars instead of blurring into blobs. White (tinted in-engine), straight alpha.
    import numpy as np
    base, S = 236, 2
    W = base * S
    C = W / 2.0
    yy, xx = np.mgrid[0:W, 0:W]
    dx = xx - C
    dy = yy - C
    r2 = dx * dx + dy * dy

    def gauss(sig):
        return np.exp(-r2 / (2.0 * (sig * S) ** 2))

    core = gauss(5.5)                 # tight bright point
    glow = 0.42 * gauss(18.0)         # soft inner halo
    outer = 0.10 * gauss(52.0)        # faint outer bloom
    tw = (2.1 * S) ** 2               # spike thickness (var)
    tl = (40.0 * S) ** 2              # spike length (var)
    spike_h = 0.5 * np.exp(-(dy * dy) / (2 * tw)) * np.exp(-(dx * dx) / (2 * tl))
    spike_v = 0.5 * np.exp(-(dx * dx) / (2 * tw)) * np.exp(-(dy * dy) / (2 * tl))
    a = np.clip(core + glow + outer + spike_h + spike_v, 0.0, 1.0)

    img = np.zeros((W, W, 4), dtype="uint8")
    img[..., 0:3] = 255
    img[..., 3] = (a * 255).astype("uint8")
    Image.fromarray(img, "RGBA").resize((base, base), Image.LANCZOS).save(os.path.join(OUT, "star.png"))
    print("wrote", os.path.join(OUT, "star.png"), (base, base))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    build_pointer()
    build_hudring()
    build_vignette()
    build_star()
