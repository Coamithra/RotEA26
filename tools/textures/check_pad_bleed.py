#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
check_pad_bleed.py - regression guard for the DXT pad gutter (Trello 4ddcd13f).

Every shipped .dds is padded up to the mult-of-4 BC3/ANGLE needs. Draw sites clamp their source
rect to the stamped LOGICAL bounds, but SamplerState.LinearClamp only clamps at the TEXTURE
border, not at the source rect: a destination pixel whose centre lands in the last half texel
bilinearly blends the last content texel with the FIRST PAD texel. While that pad texel was
transparent black, the final ~1px of every tile lost up to 50% of its RGB and alpha -- the
background tile seams (dark on the opaque Mars sky, bright where the marshills silhouettes sit
over it). build_textures.py now replicates the logical edge into the pad gutter, which makes the
filtered result identical to a true clamp.

This script proves that property directly on the committed bytes: for every .dds it checks that
the texel one step outside the logical edge EQUALS the edge texel, along the right column, the
bottom row, and the corner. All zero deltas => bilinear at the logical edge cannot differ from
clamp => no seam is possible, at any pad size.

It is a pure read of the shipped assets -- no texconv, no GPU, no game. Run it after any
build_textures.py rebuild.

Usage:
  python tools/textures/check_pad_bleed.py [--verbose]

Exit code 0 = clean, 1 = at least one asset would bleed. Requires Pillow (PIL).
"""
import argparse
import glob
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
CONTENT = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content")
DDS_LOGICAL_MAGIC = b"LOGD"

# BC3 is lossy and the gutter lands in a DIFFERENT 4x4 block from the edge it copies, so it never
# decodes bit-identical -- a fixed tolerance would either miss real gaps on noisy art or cry wolf
# on clean art. Instead each texture calibrates against ITSELF: the gutter step is compared to the
# step between the last two content columns, i.e. the image's own column-to-column variation at
# that same edge, under the same compressor. A replicated gutter sits at or below that; a
# transparent pad blows through it (delta ~255 against a backdrop of single digits).
# FLOOR keeps a perfectly flat edge (intrinsic step 0) from tripping on a few levels of noise, and
# SLACK leaves room for the gutter block being a harder fit than the content block on a
# high-contrast edge. HARD is the backstop those two must not explain away: a replica can never
# legitimately differ from its source by half of full scale, so a re-transparent pad (which reads
# as the full 255) is caught even where the edge itself is violently noisy.
FLOOR = 16
SLACK = 2
HARD = 128
CHANNELS = ("R", "G", "B", "A")


def read_logical(path):
    """(padded_w, padded_h, logical_w, logical_h). Unstamped .dds were never padded."""
    with open(path, "rb") as f:
        hdr = f.read(44)
    h, w = struct.unpack("<I", hdr[12:16])[0], struct.unpack("<I", hdr[16:20])[0]
    lw, lh = struct.unpack("<II", hdr[32:40])
    if hdr[40:44] != DDS_LOGICAL_MAGIC:
        return w, h, w, h
    return w, h, lw, lh


def deltas(a, b):
    """Per-channel (R,G,B,A) worst difference between two equal-length raw RGBA byte strips.

    Compared alpha-WEIGHTED (r*a, g*a, b*a, a). The assets are straight alpha and stay that way --
    this is only the comparison metric, chosen because it measures what the renderer can actually
    show. RGB under a=0 never reaches the framebuffer, so texconv rewriting a fully transparent
    texel's colour (it collapses uniform transparent blocks to 0,0,0,0) is not a gap; weighting by
    alpha discounts it exactly as much as the blend does, while a genuinely transparent PAD next to
    opaque content still reads as the full 255 it is.
    """
    def w(s, i):
        al = s[i + 3]
        return (s[i] * al // 255, s[i + 1] * al // 255, s[i + 2] * al // 255, al)
    return [max((abs(w(a, i)[k] - w(b, i)[k]) for i in range(0, len(a), 4)), default=0)
            for k in range(4)]


def exceeds(step, intrinsic):
    """The channels where the gutter step outruns the image's own step at that edge."""
    return [CHANNELS[k] + f"+{step[k]}/{intrinsic[k]}" for k in range(4)
            if step[k] > HARD or step[k] > max(SLACK * intrinsic[k], FLOOR)]


def check(path, verbose):
    from PIL import Image
    pw, ph, lw, lh = read_logical(path)
    rel = os.path.relpath(path, CONTENT).replace(os.sep, "/")
    if (pw, ph) == (lw, lh):
        if verbose:
            print(f"  ok    {rel}  {lw}x{lh}  (unpadded, clamp applies)")
        return True
    im = Image.open(path).convert("RGBA")
    strip = lambda box: im.crop(box).tobytes()   # raw RGBA, 4 bytes per texel
    bad = []
    col_ref = row_ref = [0, 0, 0, 0]
    if pw > lw:   # right gutter vs the last content column, scaled by that edge's own step
        col_ref = deltas(strip((lw - 2, 0, lw - 1, lh)), strip((lw - 1, 0, lw, lh)))
        over = exceeds(deltas(strip((lw - 1, 0, lw, lh)), strip((lw, 0, lw + 1, lh))), col_ref)
        if over:
            bad.append("right column " + " ".join(over))
    if ph > lh:   # bottom gutter vs the last content row
        row_ref = deltas(strip((0, lh - 2, lw, lh - 1)), strip((0, lh - 1, lw, lh)))
        over = exceeds(deltas(strip((0, lh - 1, lw, lh)), strip((0, lh, lw, lh + 1))), row_ref)
        if over:
            bad.append("bottom row " + " ".join(over))
    if pw > lw and ph > lh:
        # One texel, so it is the noisiest sample -- calibrate it against the looser of the two
        # edges rather than against a single-pixel reference of its own.
        ref = [max(c, r) for c, r in zip(col_ref, row_ref)]
        over = exceeds(deltas(strip((lw - 1, lh - 1, lw, lh)), strip((lw, lh, lw + 1, lh + 1))), ref)
        if over:
            bad.append("corner " + " ".join(over))
    if bad:
        print(f"  BLEED {rel}  {lw}x{lh} -> {pw}x{ph}:  " + ", ".join(bad))
        return False
    if verbose:
        print(f"  ok    {rel}  {lw}x{lh} -> {pw}x{ph}")
    return True


def main():
    ap = argparse.ArgumentParser(description="Assert every padded .dds replicates its logical edge")
    ap.add_argument("--verbose", action="store_true", help="list every asset, not just failures")
    args = ap.parse_args()

    # texviewer/ holds throwaway comparison previews (gitignored, never loaded by the game).
    paths = sorted(p for p in glob.glob(os.path.join(CONTENT, "**", "*.dds"), recursive=True)
                   if "texviewer" not in p.replace(os.sep, "/"))
    print(f"check_pad_bleed: {len(paths)} shipped .dds under "
          f"{os.path.relpath(CONTENT, REPO)}  (per-texture calibration, floor {FLOOR}/255)")
    failed = [p for p in paths if not check(p, args.verbose)]
    if failed:
        print(f"FAIL: {len(failed)} of {len(paths)} would bleed the pad across the logical edge.")
        print("  Rebuild: python tools/textures/build_textures.py [--padtest N]")
        sys.exit(1)
    print(f"ok: all {len(paths)} replicate their logical edge into the pad.")


if __name__ == "__main__":
    main()
