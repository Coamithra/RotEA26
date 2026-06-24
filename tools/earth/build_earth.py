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
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/earth.png        (HERO fly-by, 1480px)
#   web/EvilAliensWeb/wwwroot/Content/gfx/sprites/earth_small.png  (minor appearances, 256px)
#
# COMPOSITION INVARIANT (keep the on-screen size identical to the old asset):
#   old: 735px frame, disk ~730px, drawn at doodadscale 1.6 -> ~1168 design px.
#   new HERO: disk DISK_HERO(1460) in FRAME_HERO(1480), so Background.QueueEarth
#             must use scale 0.8  (1460*0.8 = 1168, unchanged).
#   new SMALL: disk DISK_SMALL(243) in FRAME_SMALL(256), so QueueSmallEarth must
#             use scale 0.45 (243*0.45 ~= 109.5, == old 730*0.15).
#   The script PRINTS the scales to use; if you change the frame sizes here,
#   update the doodadscale constants in Game/EvilAliens/Background.cs to match.
#
# Straight (non-premultiplied) alpha out, to match the rest of the content
# pipeline (SpriteBatchWrapper maps AlphaBlend -> NonPremultiplied). The resize
# is done in premultiplied space then un-premultiplied so the downscale doesn't
# bleed the black background into the limb.
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
# DISK_HERO is sized so the SOLID disk (the R_IN boundary) lands at on-screen
# radius 730 at doodadscale 0.8 -> 1168 design px == the old 730px disk at 1.6.
DISK_HERO, FRAME_HERO = 1473, 1480     # solid edge -> 1168 design px at scale 0.8
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


def main():
    rgba = make_masked_rgba()
    hero = resize_straight(rgba, DISK_HERO, FRAME_HERO)
    small = resize_straight(rgba, DISK_SMALL, FRAME_SMALL)
    os.makedirs(OUT_DIR, exist_ok=True)
    hero.save(os.path.join(OUT_DIR, "earth.png"))
    small.save(os.path.join(OUT_DIR, "earth_small.png"))
    print(f"wrote earth.png       {hero.size}  -> Background.QueueEarth/QueueEarthSim doodadscale = {1168/DISK_HERO:.4f}")
    print(f"wrote earth_small.png {small.size}  -> Background.QueueSmallEarth doodadscale = {109.5/DISK_SMALL:.4f}")


if __name__ == "__main__":
    main()
