"""
Procedurally generate the sprites that are better drawn than AI-upscaled:
  - bulletevil / bulletgood : simple red / green shaded spheres with a specular
    highlight (the originals are 16px blurry blobs; a generative redraw adds junk
    detail a clean glossy ball shouldn't have).
  - arrow : the Warning!/Danger! directional arrow (original 49x40: a wide short
    triangular head + a rectangular shaft, drawn at AnimatedMessage.cs:285 rotating
    round screen-centre at scale 1, so it blurs when the presenter magnifies it).
    A crisp filled vector arrow at the same silhouette.

All straight-alpha RGBA, supersampled then box-down for clean edges. Output ->
tools/upscale/gen_out/<name>.png + a gen_preview.png contact strip. These are the
SOURCE; swapping into Content/gfx/sprites + the engine wiring is a separate step.

    python tools/upscale/gen_sprites.py
"""
# PIL.Image.LANCZOS resolves at runtime but Pyright's Pillow stubs miss it:
# pyright: reportAttributeAccessIssue=false
import os

import cv2
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITES = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites")
OUT = os.path.join(HERE, "gen_out")
SS = 4  # supersample factor


def sphere(size, base, *, ambient=0.22, gloss=0.85, shininess=55.0,
           light=(-0.45, -0.55, 0.70), outline=0.5, outline_start=0.58):
    """A shaded sphere: lambert + blinn-phong specular, a soft anti-aliased circular
    alpha edge, and a subtle darkened OUTLINE toward the silhouette (radial, light-
    independent) so the ball reads with definition against any background -- like the
    original 16px bullets' darker rim. `base` is the 0..1 RGB ball colour."""
    n = size * SS
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    nx = (xx - (n - 1) / 2) / (n / 2)
    ny = (yy - (n - 1) / 2) / (n / 2)
    r2 = nx * nx + ny * ny
    nz = np.sqrt(np.clip(1.0 - r2, 0.0, 1.0))

    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    V = np.array([0.0, 0.0, 1.0])
    H = L + V
    H /= np.linalg.norm(H)

    ndotl = np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0.0, 1.0)
    ndoth = np.clip(nx * H[0] + ny * H[1] + nz * H[2], 0.0, 1.0)
    spec = ndoth ** shininess

    base = np.asarray(base, float)
    col = base * (ambient + gloss * ndotl)[..., None] + np.array([1.0, 1.0, 1.0]) * spec[..., None]

    r = np.sqrt(r2)
    # radial darkened outline (light-independent) -> definition, like the OG bullets
    t = np.clip((r - outline_start) / (1.0 - outline_start), 0.0, 1.0)
    edged = t * t * (3.0 - 2.0 * t)                 # smoothstep
    col = col * (1.0 - outline * edged)[..., None]
    col = np.clip(col, 0.0, 1.0)

    edge = 1.6 / (n / 2)                       # ~1.6 hi-res px of feather
    alpha = np.clip((1.0 - r) / edge, 0.0, 1.0)

    rgba = np.zeros((n, n, 4), np.float64)
    rgba[..., :3] = col
    rgba[..., 3] = alpha
    img = Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA")
    return img.resize((size, size), Image.LANCZOS)


def arrow(orig_path, factor=8, *, color=(255, 255, 255)):
    """Trace the EXACT original arrow.png silhouette and re-render it crisp at
    `factor`x. Find the alpha contour, simplify the 1px staircase to clean straight
    edges (Douglas-Peucker), scale the corners by `factor`, fill anti-aliased
    (supersample then box-down). Output is (49 x 40) * factor; design width = 49."""
    a = np.asarray(Image.open(orig_path).convert("RGBA"))[:, :, 3]
    h, w = a.shape
    mask = ((a > 64).astype(np.uint8)) * 255
    cnts, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    cnt = max(cnts, key=cv2.contourArea)
    approx = cv2.approxPolyDP(cnt, 1.5, True).reshape(-1, 2).astype(np.float64)
    print("    arrow contour: %d corners" % len(approx))

    s = factor * SS
    W, H = w * factor, h * factor
    im = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
    ImageDraw.Draw(im).polygon([(float(x * s), float(y * s)) for x, y in approx],
                               fill=(*color, 255))
    return im.resize((W, H), Image.LANCZOS)


def blast_burst(size, *, rays=8):
    """The HUD bomb-count icon: a red energy shockwave/starburst (the bomb is the
    screen-clearing Blast, whose powerup theme colour is red). A white-hot detonation
    core fading orange->red, N sharp radiating rays (a long set + a shorter offset set
    for a jagged burst), and a thin glowing shockwave ring. Red is baked into the art
    (drawn with Color.White at the HUD site, not tinted). Straight-alpha RGBA,
    supersampled then box-down for clean edges. Design width 24 (matches the old
    `option` reuse, so the HUD layout/spacing is unchanged)."""
    n = size * SS
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    nx = (xx - (n - 1) / 2) / (n / 2)
    ny = (yy - (n - 1) / 2) / (n / 2)
    r = np.sqrt(nx * nx + ny * ny)
    theta = np.arctan2(ny, nx)

    glow = np.exp(-(r / 0.52) ** 2 * 1.8)                          # filled red fireball body
    core = np.exp(-(r / 0.16) ** 2 * 2.6)                          # white-hot detonation core

    ang_long = (0.5 + 0.5 * np.cos(rays * theta)) ** 5.0           # long spokes punching out
    rad_long = np.clip(1.0 - r / 0.99, 0.0, 1.0) ** 0.9
    rays_long = ang_long * rad_long

    ang_short = (0.5 + 0.5 * np.cos(rays * theta + np.pi)) ** 8.0  # short spokes, between
    rad_short = np.clip(1.0 - r / 0.62, 0.0, 1.0) ** 1.0
    rays_short = ang_short * rad_short * 0.7

    ring = np.exp(-((r - 0.82) / 0.07) ** 2) * 0.35                # faint shockwave ring

    inten = np.clip(glow * 0.9 + core * 1.3 + rays_long * 0.85
                    + rays_short * 0.5 + ring, 0.0, 1.0)

    # fire ramp by intensity: stays RED through the body, only the core goes white-hot
    stops = np.array([
        [0.00, 0.45, 0.02, 0.01],
        [0.40, 0.90, 0.05, 0.03],
        [0.66, 1.00, 0.22, 0.05],
        [0.85, 1.00, 0.58, 0.16],
        [1.00, 1.00, 0.98, 0.90],
    ])
    col = np.empty(inten.shape + (3,), np.float64)
    for c in range(3):
        col[..., c] = np.interp(inten, stops[:, 0], stops[:, c + 1])

    alpha = np.clip(inten * 1.25, 0.0, 1.0)
    alpha *= np.clip((1.0 - r) / (3.0 / (n / 2)), 0.0, 1.0)        # feather the canvas edge

    rgba = np.zeros((n, n, 4), np.float64)
    rgba[..., :3] = col
    rgba[..., 3] = alpha
    img = Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA")
    return img.resize((size, size), Image.LANCZOS)


def orig_ref(name):
    """Pristine original of a sprite -- the .png.orig backup if a swap already
    happened (so a re-run doesn't read our own upscaled output back in), else .png."""
    o = os.path.join(SPRITES, name + ".png.orig")
    return o if os.path.exists(o) else os.path.join(SPRITES, name + ".png")


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name + ".png"))
    print("  %-14s %dx%d" % (name, img.width, img.height))


def main():
    print("generating procedural sprites -> tools/upscale/gen_out/")
    # Sized for ~1:1 texel:pixel at the worst case (presenter caps at 2.4x), with a
    # little AA headroom -- NOT oversized. bullets draw at design 16 * scale 1 * 2.4
    # ~= 38px on screen -> 48px; arrow design 49 * 1 * 2.4 ~= 118px -> 49*3 = 147px.
    sprites = {
        "bulletevil": sphere(48, (0.90, 0.12, 0.12)),      # red energy ball
        "bulletgood": sphere(48, (0.18, 0.82, 0.24)),      # green energy ball
        "arrow": arrow(orig_ref("arrow"), factor=3),   # traced from the pristine 49x40, * 3
        "bombicon": blast_burst(96),                   # red blast/shockwave HUD bomb icon (design 24 * 4)
    }
    for name, img in sprites.items():
        save(img, name)

    # preview strip on a neutral grey so the highlight + edges read
    pad, cellh = 24, 220
    cells = []
    for name in ("bulletevil", "bulletgood", "arrow", "bombicon"):
        im = Image.open(os.path.join(OUT, name + ".png")).convert("RGBA")
        cell = Image.new("RGBA", (cellh, cellh), (90, 90, 96, 255))
        sc = (cellh - 2 * pad) / max(im.width, im.height)
        rs = im.resize((round(im.width * sc), round(im.height * sc)), Image.LANCZOS)
        cell.alpha_composite(rs, ((cellh - rs.width) // 2, (cellh - rs.height) // 2))
        cells.append(cell)
    strip = Image.new("RGBA", (cellh * len(cells), cellh), (90, 90, 96, 255))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * cellh, 0))
    strip.convert("RGB").save(os.path.join(OUT, "gen_preview.png"))
    print("  gen_preview.png")


if __name__ == "__main__":
    main()
