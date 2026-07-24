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

This script checks that property directly on the committed bytes: for every .dds, along the right
column, the bottom row and the corner, the texel one step outside the logical edge must still look
like the edge texel it replicates.

It is a TOLERANCE CHECK, not a proof. BC3 is lossy and the gutter lands in a different 4x4 block
from the edge it copies, so the two never decode bit-identical; the bar each texel is held to is
derived from the image itself (see FLOOR/SLACK/HARD/WINDOW below). A pass means no texel on a
logical edge steps away from its replica by more than that image's own local variation there --
i.e. nothing that would read as a seam. It is not a guarantee of pixel equality.

Sensitivity, measured against the pre-fix assets: of the 103 .dds the gutter actually changed, it
flags 85. The rest have logical edges that are already (near-)transparent, where a transparent pad
is within noise of a replica and there is genuinely nothing to see -- so a clean run does NOT mean
"every texture was rebuilt", only "no texture has a visible gap at its logical edge". Whether the
gutter is present at all is build_textures.py's business, not this script's.

EVERY MIP LEVEL IS CHECKED, not just level 0 (Trello 110153c7). A mipped .dds has the same
property to keep at each level, and it is easy to lose: a chain built by handing texconv the
already-padded canvas filters the pad along WITH the content, so the levels blend real pixels
into transparent pad near the logical edge. Measured on 756-v1, a GUTTER of 4 px survives
log2(GUTTER) = 2 levels and then fails hard -- alpha delta 0/0/0 at levels 0-2, then
127/191/223 at 3/4/5. Trilinear samples two levels at once, so one bad level is enough to show.
The level's own logical size is `logical >> level` (KNI's GetSizeForLevel), and levels where the
pad has shrunk away entirely are skipped as unpadded.

It is a pure read of the shipped assets -- no texconv, no GPU, no game. build_textures.py runs it
automatically at the end of a real build; run it by hand after anything else touches the .dds.

Usage:
  python tools/textures/check_pad_bleed.py [--verbose]
  python tools/textures/check_pad_bleed.py --selftest

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

# A fixed tolerance would either miss real gaps on noisy art or cry wolf on clean art, so each
# texel is judged against the image's own behaviour AT THAT POSITION: the step from the edge texel
# to its gutter replica is compared to the step between the last two content texels there, under
# the same compressor. A replica sits at or below that; a transparent pad blows through it.
# The reference is the worst intrinsic step within WINDOW texels either side -- per-texel alone is
# too twitchy where BC3 quantises a flat run to exactly 0, and a whole-edge maximum is far too lax
# (one high-contrast spot on a 1000-texel edge would license a real gap everywhere else). Keep
# WINDOW small: it smooths quantisation, but it is variation ALONG the edge being used to excuse a
# step ACROSS it, so widening it re-opens exactly that hole.
# FLOOR keeps a flat edge from tripping on a few levels of noise; SLACK leaves room for the gutter
# block being a harder fit than the content block on a high-contrast edge; HARD is the backstop
# neither may explain away, since a replica can never legitimately differ from its source by half
# of full scale -- so a re-transparent pad is caught even on a violently noisy edge.
# FLOOR is SWEPT, not guessed, and it has to absorb CROSS-BLOCK error: adjacent content texels
# usually share BC3 endpoints, so the intrinsic reference systematically under-reads the error the
# gutter takes from landing in a different 4x4 block. Mip levels widen that gap -- downsampling
# flattens the content (so the intrinsic reference shrinks toward 0 and the allowance collapses to
# FLOOR) while the compressor's absolute error does not shrink with it. Measured over all 124
# shipped .dds at every level: worst legitimate step 41 (756-v1 level 2), and nothing within 3x of
# HARD. 64 clears that by 1.6x while staying 1.8x under the SMALLEST real bleed on record (the
# 116/255 alpha discontinuity on pre-fix eye_idle); a transparent pad reads the full 255.
#
# BE HONEST ABOUT WHAT THE PER-TEXEL REFERENCE BUYS AT THIS FLOOR. It is not the upward escape:
# exactly ONE shipped edge steps past a flat 64 (controls_keyboard level 0, by 2/255), so for
# 123 of 124 assets this is a flat threshold. What it buys is the DOWNWARD half. The superseded
# rule had this same shape but took its reference from the whole edge's maximum, so one busy spot
# handed EVERY texel on that edge the full HARD -- 128 where this rule gives a quiet stretch 64.
# That factor of two is the entire 64..128 band, which is exactly where a real gap on an otherwise
# noisy edge hides. Do not "simplify" this to a constant.
FLOOR = 64
SLACK = 2
HARD = 128
WINDOW = 1
CHANNELS = ("R", "G", "B", "A")


DXT_BLOCK_BYTES = {b"DXT1": 8, b"DXT3": 16, b"DXT5": 16}
DDSD_MIPMAPCOUNT = 0x00020000
DDSCAPS_COMPLEX = 0x00000008
DDSCAPS_MIPMAP = 0x00400000


def read_dds(path):
    """(data, padded_w, padded_h, logical_w, logical_h, mip_count, block_bytes).

    Unstamped .dds were never padded, so their logical size IS their padded size.
    Raises ValueError rather than exiting -- build_textures.py imports run() as a library, and one
    malformed file must fail the guard, not kill the build mid-`main()`."""
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < 128 or data[:4] != b"DDS ":
        raise ValueError(f"not a DDS file (bad magic or truncated header): {path}")
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


def weighted(strip):
    """Raw RGBA bytes -> alpha-WEIGHTED (r*a, g*a, b*a, a) samples.

    The assets are straight alpha and stay that way -- this is only the comparison metric, chosen
    because it measures what the renderer can actually show. RGB under a=0 never reaches the
    framebuffer, so texconv rewriting a fully transparent texel's colour (it collapses uniform
    transparent blocks to 0,0,0,0) is not a gap; weighting by alpha discounts it exactly as much as
    the blend does, while a genuinely transparent PAD next to opaque content still reads as the
    full 255 it is.
    """
    return [(strip[i] * strip[i + 3] // 255, strip[i + 1] * strip[i + 3] // 255,
             strip[i + 2] * strip[i + 3] // 255, strip[i + 3])
            for i in range(0, len(strip), 4)]


def edge_margin(edge, gutter, inner):
    """Tightest margin along one edge: (slack, label). Negative slack == a violation.

    `inner` is the texel one further inside, so |inner-edge| is the image's own step at that spot.
    Each texel is allowed the worst such step within WINDOW either side, so a locally busy patch
    stays permissive without licensing a gap on the quiet stretches around it.
    """
    intrinsic = [[abs(inner[i][k] - edge[i][k]) for k in range(4)] for i in range(len(edge))]
    worst = None
    for i in range(len(edge)):
        lo, hi = max(0, i - WINDOW), min(len(edge), i + WINDOW + 1)
        for k in range(4):
            step = abs(edge[i][k] - gutter[i][k])
            allowed = min(HARD, max(SLACK * max(intrinsic[j][k] for j in range(lo, hi)), FLOOR))
            if worst is None or allowed - step < worst[0]:
                worst = (allowed - step, f"{CHANNELS[k]}{step}/{allowed}@{i}")
    return worst or (0, "-")


def check_edges(im, lw, lh, pw, ph):
    """The gutter margins for ONE surface: [(edge name, (slack, label))], tightest per edge."""
    strip = lambda box: weighted(im.crop(box).tobytes())
    edges = []
    if pw > lw:   # right gutter vs the last content column, scaled by that edge's own steps
        edges.append(("right column", edge_margin(
            strip((lw - 1, 0, lw, lh)), strip((lw, 0, lw + 1, lh)), strip((lw - 2, 0, lw - 1, lh)))))
    if ph > lh:   # bottom gutter vs the last content row
        edges.append(("bottom row", edge_margin(
            strip((0, lh - 1, lw, lh)), strip((0, lh, lw, lh + 1)), strip((0, lh - 2, lw, lh - 1)))))
    if pw > lw and ph > lh:
        edges.append(("corner", edge_margin(   # calibrated against the diagonal inner neighbour
            strip((lw - 1, lh - 1, lw, lh)), strip((lw, lh, lw + 1, lh + 1)),
            strip((lw - 2, lh - 2, lw - 1, lh - 1)))))
    return edges


def report(edges):
    return "  ".join(f"{name} {label}" for name, (_, label) in edges)


def check(path, verbose):
    rel = os.path.relpath(path, CONTENT).replace(os.sep, "/")
    try:
        data, pw0, ph0, lw0, lh0, mips, block = read_dds(path)
    except ValueError as e:
        print(f"  BAD   {rel}: {e}")
        return False
    mipnote = f", {mips} mip levels" if mips > 1 else ""
    if (pw0, ph0) == (lw0, lh0):
        if verbose:
            print(f"  ok    {rel}  {lw0}x{lh0}  (unpadded, clamp applies{mipnote})")
        return True
    ok = True
    checked = 0
    tightest = {}
    for level in range(mips):
        im, pw, ph = mip_level_image(data, pw0, ph0, level, block)
        lw, lh = max(1, lw0 >> level), max(1, lh0 >> level)
        # A per-texel reference needs a texel one further in, and once the pad has shrunk away the
        # level is unpadded and clamp applies to it as-is.
        if (pw, ph) == (lw, lh) or lw < 2 or lh < 2:
            continue
        checked += 1
        edges = check_edges(im, lw, lh, pw, ph)
        for name, margin in edges:
            if name not in tightest or margin[0] < tightest[name][0]:
                tightest[name] = margin
        if any(slack < 0 for _, (slack, _) in edges):
            print(f"  BLEED {rel}  level {level}  {lw}x{lh} -> {pw}x{ph}:  " + report(edges))
            ok = False
    if ok and verbose:
        print(f"  ok    {rel}  {lw0}x{lh0} -> {pw0}x{ph0}  ({checked} padded level"
              f"{'' if checked == 1 else 's'} checked{mipnote}):  " + report(tightest.items()))
    return ok


def run(verbose=False):
    """Check every shipped .dds; True if all clean. Imported by build_textures.py's final gate."""
    # texviewer/ holds throwaway comparison previews (gitignored, never loaded by the game).
    paths = sorted(p for p in glob.glob(os.path.join(CONTENT, "**", "*.dds"), recursive=True)
                   if "texviewer" not in p.replace(os.sep, "/"))
    print(f"check_pad_bleed: {len(paths)} shipped .dds under "
          f"{os.path.relpath(CONTENT, REPO)}  (local calibration, floor {FLOOR}/255, cap {HARD}, "
          f"every mip level)")
    failed = [p for p in paths if not check(p, verbose)]
    if failed:
        print(f"FAIL: {len(failed)} of {len(paths)} step away from the replicated edge by more "
              f"than the image's own local variation there.")
        print("  Rebuild: python tools/textures/build_textures.py [--only GLOB] [--padtest N]")
        return False
    print(f"ok: all {len(paths)} replicate their logical edge into the pad, at every mip level.")
    return True


def _synthetic(rows, lw=63, lh=64):
    """An RGBA image (lw+1)x lh whose last three columns are `rows` = [(inner, edge, gutter)].

    Only the right gutter exists (height stays a mult-of-4), so a case exercises one edge alone.
    Values are opaque grey levels unless a 4-tuple RGBA is given."""
    from PIL import Image
    px = lambda v: v if isinstance(v, tuple) else (v, v, v, 255)
    im = Image.new("RGBA", (lw + 1, lh), (0, 0, 0, 255))
    for y, (inner, edge, gutter) in enumerate(rows):
        im.putpixel((lw - 2, y), px(inner))
        im.putpixel((lw - 1, y), px(edge))
        im.putpixel((lw, y), px(gutter))
    return im, lw, lh


def _whole_edge_max_verdict(im, lw, lh):
    """The SUPERSEDED rule, kept only so the selftest can show what it let through.

    It compared the whole edge's max gutter step against the whole edge's max intrinsic step --
    see the calibration comment above for why that is too lax."""
    strip = lambda box: weighted(im.crop(box).tobytes())
    inner, edge, gutter = (strip((lw - 2, 0, lw - 1, lh)), strip((lw - 1, 0, lw, lh)),
                           strip((lw, 0, lw + 1, lh)))
    worst = lambda a, b: [max(abs(a[i][k] - b[i][k]) for i in range(len(a))) for k in range(4)]
    step, intrinsic = worst(edge, gutter), worst(inner, edge)
    return not any(step[k] > HARD or step[k] > max(SLACK * intrinsic[k], FLOOR) for k in range(4))


def selftest():
    """Pin the three properties the tolerance rule is supposed to have. No .dds, no texconv."""
    lh = 64
    # A replicated gutter passes even where the content itself is busy across the edge.
    clean = [((y * 37) % 256, (y * 91) % 256, (y * 91) % 256) for y in range(lh)]
    # A transparent pad next to opaque content is the original bug.
    transparent = [(120, 200, (0, 0, 0, 0))] * lh
    # The finding the per-texel reference exists for: a quiet edge with one high-contrast spot, and
    # a real gap far away from it. The whole-edge maximum takes its licence from row 8 and waves
    # row 40 through -- the last case below asserts exactly that difference.
    licensed = [(200, 200, 200)] * lh
    licensed[8] = (0, 255, 255)     # intrinsic step 255 here, and no gap
    licensed[40] = (200, 200, 100)  # gap of 100 here, and no intrinsic step

    # The other direction, which a flat threshold could not do: a step of 90 is ABOVE FLOOR, so it
    # passes only where the image's own across-edge step vouches for it. Rows 20-21 are busy
    # (intrinsic 60 -> allowed 120); the identical step is legitimate at row 20 and a violation at
    # row 23, which is WINDOW+1 clear of the busy patch. The pair pins SLACK and WINDOW together --
    # widen WINDOW and the second case silently starts passing.
    def busy(gap_row):
        rows = [(200, 200, 200)] * lh
        rows[20] = rows[21] = (140, 200, 200)
        rows[gap_row] = (rows[gap_row][0], 200, 110)
        return rows

    cases = [("replicated gutter", clean, True), ("transparent pad", transparent, False),
             ("gap licensed by a distant hot spot", licensed, False),
             ("step vouched for by the local content", busy(20), True),
             ("same step one texel outside the window", busy(23), False)]
    ok = True
    for name, rows, want_pass in cases:
        im, lw, _ = _synthetic(rows, lh=lh)
        edges = check_edges(im, lw, lh, lw + 1, lh)
        got_pass = all(slack >= 0 for _, (slack, _) in edges)
        ok &= got_pass == want_pass
        print(f"  {'ok  ' if got_pass == want_pass else 'FAIL'}  {name}: "
              f"{'passes' if got_pass else 'flagged'} (want {'passes' if want_pass else 'flagged'})"
              f"  [{report(edges)}]")
    # The discrimination is the point of the change, so assert it, not just the new verdict.
    im, lw, _ = _synthetic(licensed, lh=lh)
    if not _whole_edge_max_verdict(im, lw, lh):
        print("  FAIL  the superseded whole-edge rule was expected to MISS the licensed gap")
        ok = False
    else:
        print("  ok    the superseded whole-edge rule misses that gap; per-texel catches it")
    print("selftest: " + ("ok" if ok else "FAILED"))
    return ok


def main():
    ap = argparse.ArgumentParser(description="Assert every padded .dds replicates its logical edge")
    ap.add_argument("--verbose", action="store_true",
                    help="list every asset with its tightest margin, not just failures")
    ap.add_argument("--selftest", action="store_true",
                    help="check the tolerance rule itself against synthetic edges; no .dds needed")
    args = ap.parse_args()
    if args.selftest:
        sys.exit(0 if selftest() else 1)
    sys.exit(0 if run(args.verbose) else 1)


if __name__ == "__main__":
    main()
