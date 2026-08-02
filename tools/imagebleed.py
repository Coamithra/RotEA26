#!/usr/bin/env python
# ---------------------------------------------------------------------------
# imagebleed.py -- the project's ONE implementation of the straight-alpha edge
# bleed, shared by every asset pipeline that writes an RGBA image.
#
# Alpha is STRAIGHT (non-premultiplied) project-wide, so bilinear filtering
# averages RGB *ignoring* alpha: a sample straddling a sprite edge mixes the
# ink's colour with whatever colour the fully-transparent side happens to
# carry. Every producer here leaves that at (0,0,0) -- a fresh RGBA canvas,
# PIL's alpha_composite, an XNB decode -- so the edge sample comes back dragged
# toward black. That is a dark halo on every sprite, worst under minification.
#
# Lifted out of tools/font/build_revenge_font.py, where it was written for the
# menufont atlas (card 5d8becc2) and measured pixel-exact: 5921 pixels changed
# on ?textshot, 100% of them brighter, none darker. Card 5d75b700 found the same
# all-black transparent field on 72 of the 127 shipped RGBA PNGs, so it lives
# here now and the font tool imports it.
#
# Same class of bug as build_textures.py's edge_gutter(), one level down: that
# one heals the sampler reaching past the LOGICAL edge into the mult-of-4 pad,
# this one heals it reaching across the ALPHA edge inside the image.
# ---------------------------------------------------------------------------
import sys

# numpy / PIL / scipy are imported INSIDE the functions on purpose: build_textures.py
# keeps its own PIL import lazy so `--selftest` runs with nothing installed, and this
# module must not spoil that.


def bleed_transparent_rgb(img):
    """Dilate an RGBA image's RGB into its fully-transparent texels.

    Each alpha==0 texel takes the RGB of its nearest alpha>0 texel. ALPHA IS
    NEVER WRITTEN, so shapes, coverage and antialiasing are bit-for-bit
    unchanged and only texels that are invisible on their own are touched.

    Returns a NEW image, never the caller's -- including on the no-op paths
    (nothing to bleed from, or nothing to bleed into).
    """
    import numpy as np
    from PIL import Image
    from scipy import ndimage

    a = np.array(img, dtype=np.uint8)        # (h,w,4) copy
    ink = a[..., 3] > 0
    if not ink.any() or ink.all():
        return Image.fromarray(a, 'RGBA')
    idx = ndimage.distance_transform_edt(~ink, return_distances=False,
                                         return_indices=True)
    rgb = a[..., 0:3]
    rgb[~ink] = rgb[idx[0], idx[1]][~ink]
    return Image.fromarray(a, 'RGBA')


def count_unbled(img):
    """How many texels the bleed would still MOVE -- 0 iff `img` is a fixed point
    of bleed_transparent_rgb().

    "Is any transparent texel black" is the WRONG question and was the guard's
    first formulation: it works for the menufont, whose ink is white, but 52% of
    gfx/sprites/spider_sheet2's edge ink is itself pure black, so a correctly bled
    dark sprite keeps millions of black transparent texels and the black count
    cries wolf (measured: 6763990 on a perfectly bled sheet). Being a fixed point
    is the property that actually matters -- every transparent texel already
    carries its nearest ink's RGB, whatever colour that is -- and it needs no
    exemption for art that is legitimately black.
    """
    import numpy as np

    rgba = img.convert('RGBA')
    a = np.asarray(rgba)
    b = np.asarray(bleed_transparent_rgb(rgba))
    return int((a[..., 0:3] != b[..., 0:3]).any(axis=2).sum())


def check_bled(img, what='image'):
    """Guard the ship: the bleed is one unconditional call and losing it fails
    SILENTLY -- a slightly dark sprite edge, no error and no metrics diff. Call
    it on whatever a pipeline is about to WRITE."""
    n = count_unbled(img)
    if n:
        sys.exit(f'ABORT: {what}: {n} transparent texels do not carry their nearest '
                 f'ink RGB -- the bleed_transparent_rgb() pass did not run. '
                 f'Straight-alpha bilinear would interpolate the leftover '
                 f'transparent-field RGB into every edge (cards 5d8becc2 / 5d75b700).')
