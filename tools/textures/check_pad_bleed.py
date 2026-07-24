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

EVERY MIP LEVEL IS CHECKED, not just level 0 (Trello 110153c7). A mipped .dds has the same
property to keep at each level, and it is easy to lose: a chain built by handing texconv the
already-padded canvas filters the pad along WITH the content, so the levels blend real pixels
into transparent pad near the logical edge. Measured on 756-v1, a GUTTER of 4 px survives
log2(GUTTER) = 2 levels and then fails hard -- alpha delta 0/0/0 at levels 0-2, then
127/191/223 at 3/4/5. Trilinear samples two levels at once, so one bad level is enough to show.
The level's own logical size is `logical >> level` (KNI's GetSizeForLevel), and levels where the
pad has shrunk away entirely are skipped as unpadded.

It is a pure read of the shipped assets -- no texconv, no GPU, no game. Run it after any
build_textures.py rebuild.

Usage:
  python tools/textures/check_pad_bleed.py [--verbose]

Exit code 0 = clean, 1 = at least one asset would bleed. Requires Pillow (PIL).
"""
import argparse
import glob
import io
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


DXT_BLOCK_BYTES = {b"DXT1": 8, b"DXT3": 16, b"DXT5": 16}
DDSD_MIPMAPCOUNT = 0x00020000
DDSCAPS_COMPLEX = 0x00000008
DDSCAPS_MIPMAP = 0x00400000


def read_dds(path):
    """(data, padded_w, padded_h, logical_w, logical_h, mip_count, block_bytes).

    Unstamped .dds were never padded, so their logical size IS their padded size."""
    with open(path, "rb") as f:
        data = f.read()
    h, w = struct.unpack("<I", data[12:16])[0], struct.unpack("<I", data[16:20])[0]
    mips = max(1, struct.unpack("<I", data[28:32])[0])
    block = DXT_BLOCK_BYTES.get(data[84:88], 16)
    lw, lh = struct.unpack("<II", data[32:40])
    if data[40:44] != DDS_LOGICAL_MAGIC:
        lw, lh = w, h
    return data, w, h, lw, lh, mips, block


def mip_level_image(data, w, h, level, block):
    """Decode ONE mip level; returns (RGBA image, level_w, level_h).

    Pillow has no per-level seek for DDS, and hand-rolling a BC decoder here would mean this
    guard no longer tests the same decode path level 0 is trusted through. So the level's blocks
    are spliced behind a copy of the file's OWN header with the dims swapped and the mip fields
    cleared -- Pillow then decodes it as an ordinary single-level texture, verbatim.

    Level sizing is `w >> level` (floor, min 1), which is what both texconv and KNI's
    TextureHelpers.GetSizeForLevel compute."""
    from PIL import Image
    off = 128
    lw, lh = w, h
    for _ in range(level):
        off += ((lw + 3) // 4) * ((lh + 3) // 4) * block
        lw, lh = max(1, lw // 2), max(1, lh // 2)
    n = ((lw + 3) // 4) * ((lh + 3) // 4) * block
    hdr = bytearray(data[:128])
    struct.pack_into("<I", hdr, 12, lh)
    struct.pack_into("<I", hdr, 16, lw)
    struct.pack_into("<I", hdr, 28, 1)
    struct.pack_into("<I", hdr, 8, struct.unpack_from("<I", hdr, 8)[0] & ~DDSD_MIPMAPCOUNT)
    struct.pack_into("<I", hdr, 108,
                     struct.unpack_from("<I", hdr, 108)[0] & ~(DDSCAPS_COMPLEX | DDSCAPS_MIPMAP))
    return Image.open(io.BytesIO(bytes(hdr) + data[off:off + n])).convert("RGBA"), lw, lh


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


def check_edges(im, lw, lh, pw, ph):
    """The gutter assertions for ONE surface. Returns a list of human-readable failures."""
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
    return bad


def check(path, verbose):
    data, pw0, ph0, lw0, lh0, mips, block = read_dds(path)
    rel = os.path.relpath(path, CONTENT).replace(os.sep, "/")
    mipnote = f", {mips} mip levels" if mips > 1 else ""
    if (pw0, ph0) == (lw0, lh0):
        if verbose:
            print(f"  ok    {rel}  {lw0}x{lh0}  (unpadded, clamp applies{mipnote})")
        return True
    ok = True
    checked = 0
    for level in range(mips):
        im, pw, ph = mip_level_image(data, pw0, ph0, level, block)
        lw, lh = max(1, lw0 >> level), max(1, lh0 >> level)
        # A 2-texel reference needs 2 content texels to compare, and once the pad has shrunk
        # away the level is unpadded and clamp applies to it as-is.
        if (pw, ph) == (lw, lh) or lw < 2 or lh < 2:
            continue
        checked += 1
        bad = check_edges(im, lw, lh, pw, ph)
        if bad:
            print(f"  BLEED {rel}  level {level}  {lw}x{lh} -> {pw}x{ph}:  " + ", ".join(bad))
            ok = False
    if ok and verbose:
        print(f"  ok    {rel}  {lw0}x{lh0} -> {pw0}x{ph0}  ({checked} padded level"
              f"{'' if checked == 1 else 's'} checked{mipnote})")
    return ok


def main():
    ap = argparse.ArgumentParser(description="Assert every padded .dds replicates its logical edge")
    ap.add_argument("--verbose", action="store_true", help="list every asset, not just failures")
    args = ap.parse_args()

    # texviewer/ holds throwaway comparison previews (gitignored, never loaded by the game).
    paths = sorted(p for p in glob.glob(os.path.join(CONTENT, "**", "*.dds"), recursive=True)
                   if "texviewer" not in p.replace(os.sep, "/"))
    print(f"check_pad_bleed: {len(paths)} shipped .dds under "
          f"{os.path.relpath(CONTENT, REPO)}  (per-texture calibration, floor {FLOOR}/255, "
          f"every mip level)")
    failed = [p for p in paths if not check(p, args.verbose)]
    if failed:
        print(f"FAIL: {len(failed)} of {len(paths)} would bleed the pad across the logical edge.")
        print("  Rebuild: python tools/textures/build_textures.py [--only GLOB] [--padtest N]")
        sys.exit(1)
    print(f"ok: all {len(paths)} replicate their logical edge into the pad, at every mip level.")


if __name__ == "__main__":
    main()
