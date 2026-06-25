#!/usr/bin/env python
# ---------------------------------------------------------------------------
# build_earth.py - rebuild the Earth sprites from a high-res NASA globe render.
#
# WHY: the shipped GFX/Sprites/earth.png was a 735px disk that, drawn at the
# Level 1 fly-by size (~1168 design px -> up to ~2800 actual px on a big
# window), was upscaled ~3.8x => washed-out and blurry. Against the new vivid
# procedural nebula starfield it read as a flat, hard-edged sticker. This swaps
# in NASA's "Blue Marble: Next Generation" Western-hemisphere globe (the same
# Americas-centered view, much higher res) and masks it to a clean, feathered
# alpha disk whose limb DISSOLVES into space (so there's no dark occluding ring
# over the nebula).
#
# SOURCE (public domain, NASA - credit "NASA Earth Observatory / Reto Stockli"):
#   sources/globe_west_2048.jpg
#   https://eoimages.gsfc.nasa.gov/images/imagerecords/57000/57723/globe_west_2048.jpg
#   2048x2048, globe disk ~1842px (incl. atmospheric limb) on black,
#   centred at (1021, 1026).
#
# OUTPUTS (committed; offline build like tools/textures, tools/audio, tools/font):
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/earth.png        (HERO fly-by, full-res
#       central vertical STRIP, ~1392 x ~1822 -- crisp, sides cropped; see below)
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/earth_small.png  (minor appearances, 256px)
#
# COMPOSITION INVARIANT (keep the on-screen size identical to the old asset):
#   The HERO disk is now the FULL source crop (no downscale) -> on-screen size is
#   kept the same by lowering doodadscale, NOT by resizing the texture. The solid
#   disk (R_IN) must still land at ~1168 design px, so doodadscale = 1168/solid
#   where solid = DISK_HERO * R_IN/R_OUT (~1806). The script PRINTS this value;
#   set Background.QueueEarth AND QueueEarthSim doodadscale to it.
#   new SMALL: disk DISK_SMALL(243) in FRAME_SMALL(256), so QueueSmallEarth must
#             use scale 0.45 (243*0.45 ~= 109.5, == old 730*0.15).
#   STRIP NOTE: the hero is cropped to a central vertical band (STRIP_W wide). This
#   is ONLY valid because the hero earth stays horizontally centred -- QueueEarth/
#   QueueEarthSim zero its X scroll so it can't drift sideways into the cropped edge.
#
# Straight (non-premultiplied) alpha out, to match the rest of the content pipeline
# (SpriteBatchWrapper maps AlphaBlend -> NonPremultiplied). The HERO strip is emitted
# straight from the masked source (no resize). The SMALL disk is downscaled in
# premultiplied space then un-premultiplied so the limb doesn't bleed black.
#
# Re-run after changing the source or knobs:  python tools/earth/build_earth.py
# Don't hand-edit the output PNGs.
# ---------------------------------------------------------------------------
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "sources", "globe_west_2048.jpg")
# Content paths are CASE-SENSITIVE on the live host: capital "Content/", lowercase
# under it (see CLAUDE.md). Write to .../Content/gfx/sprites, never GFX/Sprites.
OUT_DIR = os.path.normpath(os.path.join(
    HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites"))

# --- source disk geometry (measured) ---------------------------------------
CX, CY = 1021.0, 1026.0     # disk centre in the source
# alpha = 1 for r <= R_IN, smoothstep down to 0 at r = R_OUT. A TIGHT 8px band
# right at the globe's limb (lum ~28 -> ~10) gives a crisp, anti-aliased edge
# that still reads as a planet limb -- NOT the old wide 35px fade, which looked
# like a hazy halo and made the disk read smaller. The globe's own limb darkening
# means even this thin cut dissolves cleanly into the starfield (no dark ring).
R_OUT = 911.0               # outer radius: alpha hits 0 just past the bright limb
R_IN = 903.0                # inner radius: solid disk extends to here

# --- output framing --------------------------------------------------------
# HERO: keep the FULL source resolution (NO downscale) so the Level-1 fly-by is
# crisp (1 texel ~= 1 pixel on a typical window) instead of the old ~1.3-1.9x
# bilinear upscale. DISK_HERO is the masked source crop at its native ~1822 px.
#
# The hero earth is WIDER than the screen and stays HORIZONTALLY CENTRED -- it
# only scrolls vertically (Background.QueueEarth/QueueEarthSim zero the doodad's
# X scroll), so its left/right limb NEVER reaches the screen edge. We therefore
# crop to a central VERTICAL STRIP (STRIP_W wide, full disk height) and don't
# store the never-seen sides. STRIP_W must cover the 800-design visible band at
# the earth's on-screen scale (~0.647): 800/0.647 ~= 1237 px, + ~50px margin each
# side -> 1392. On-screen disk size is UNCHANGED (doodadscale falls 0.8 -> ~0.647).
DISK_HERO = int(round(R_OUT * 2))      # full-res disk, no downscale (~1822 px)
STRIP_W = 1392                         # central band width (>= visible 800 design px + margin)
DISK_SMALL, FRAME_SMALL = 243, 256     # -> doodadscale 0.45 (243*0.45 = 109.4)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def make_masked_rgba():
    """Load the source, build a feathered circular straight-alpha disk, crop to
    the disk bbox. Returns an RGBA float array (0..1) centred on the disk."""
    rgb = np.asarray(Image.open(SRC).convert("RGB"), dtype=np.float32) / 255.0
    h, w, _ = rgb.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.hypot(xx - CX, yy - CY)
    alpha = 1.0 - smoothstep(R_IN, R_OUT, r)        # 1 inside, 0 past the limb
    alpha[r > R_OUT] = 0.0
    rgba = np.dstack([rgb, alpha.astype(np.float32)])
    # crop to the disk bbox (square, centred on the disk)
    x0, y0 = int(round(CX - R_OUT)), int(round(CY - R_OUT))
    side = int(round(R_OUT * 2))
    return rgba[y0:y0 + side, x0:x0 + side, :]


def resize_straight(rgba, disk, frame):
    """Resize the cropped disk to `disk` px (premultiplied, to avoid black bleed
    at the limb), un-premultiply back to straight alpha, and centre it in a
    transparent `frame`x`frame` canvas."""
    rgb = rgba[:, :, :3]
    a = rgba[:, :, 3:4]
    pm = np.clip(np.dstack([rgb * a, a]) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    lanczos = getattr(Image, "Resampling", Image).LANCZOS  # type: ignore  # Pillow <9.1 vs >=9.1
    pm_img = Image.fromarray(pm, "RGBA").resize((disk, disk), lanczos)
    pm = np.asarray(pm_img, dtype=np.float32) / 255.0
    a2 = pm[:, :, 3:4]
    rgb2 = np.where(a2 > 1e-4, pm[:, :, :3] / np.maximum(a2, 1e-4), 0.0)
    straight = np.clip(np.dstack([rgb2, a2[:, :, 0]]) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    disk_img = Image.fromarray(straight, "RGBA")
    canvas = Image.new("RGBA", (frame, frame), (0, 0, 0, 0))
    off = (frame - disk) // 2
    canvas.paste(disk_img, (off, off), disk_img)
    return canvas


def crop_strip_straight(rgba, strip_w):
    """Full-res straight-alpha PNG, cropped to a central vertical strip of width
    `strip_w` (full disk height). No resize -> native crispness. The float rgba
    is already straight (non-premultiplied) alpha from make_masked_rgba."""
    straight = np.clip(rgba * 255.0 + 0.5, 0, 255).astype(np.uint8)
    w = straight.shape[1]
    x0 = max(0, (w - strip_w) // 2)
    return Image.fromarray(straight[:, x0:x0 + strip_w, :], "RGBA")


def main():
    rgba = make_masked_rgba()                       # ~1822x1822 straight-alpha float disk
    hero = crop_strip_straight(rgba, STRIP_W)       # full-res central strip (no downscale)
    small = resize_straight(rgba, DISK_SMALL, FRAME_SMALL)
    os.makedirs(OUT_DIR, exist_ok=True)
    hero.save(os.path.join(OUT_DIR, "earth.png"))
    small.save(os.path.join(OUT_DIR, "earth_small.png"))
    disk = rgba.shape[0]                             # full disk diameter (alpha-0)
    solid = disk * R_IN / R_OUT                      # solid disk diameter (R_IN)
    print(f"wrote earth.png       {hero.size} (strip of {disk}px disk)  -> "
          f"Background.QueueEarth/QueueEarthSim doodadscale = {1168 / solid:.4f}")
    print(f"wrote earth_small.png {small.size}  -> Background.QueueSmallEarth doodadscale = {109.5 / DISK_SMALL:.4f}")


if __name__ == "__main__":
    main()
