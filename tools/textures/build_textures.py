#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_textures.py - the web port's offline texture-precompile step.

Reads tools/textures/textures.config and, for each listed sprite, writes a
GPU-ready sibling next to its PNG under wwwroot/Content so WebContentManager can
skip the costly managed PNG decode (StbImageSharp runs on the WASM main thread, so
a cold multi-megapixel PNG is a multi-hundred-ms to multi-second frame hitch):

  dxt  -> <name>.dds   BC3/DXT5 block-compressed. Lossy, ~2.4x the PNG on disk,
          tiny in VRAM, zero decode. Needs texconv.exe (DirectXTex). Chrome/ANGLE
          maps to D3D11, which requires block textures be multiples of 4, so the
          image is PADDED up to a mult-of-4 (transparent, bottom/right only, so the
          content keeps its top-left pixel coords). The original ("logical") size is
          stamped into the .dds header's reserved dwords; WebContentManager reads it
          back (see TextureDims.cs) and every consumer uses the LOGICAL size for
          pixel-space math + clamps whole-texture draws to it, so the pad is never
          sampled and nothing shifts. cols/rows are no longer needed for the build.
          The first GUTTER px of the pad are NOT transparent -- they replicate the
          logical edge, because a clamped source rect does not stop bilinear from
          reaching one texel past it (see edge_gutter). A trailing "mip" on the config
          line adds a full mip chain, built PER LEVEL so the pad never filters into the
          content (see build_mip_chain); without one a minified draw gets bilinear and
          nothing else, and aliases.

  raw  -> <name>.rtex  Uncompressed straight-alpha RGBA8 with a 16-byte header.
          Lossless, large on disk, zero decode, NO dimension constraint. For sheets
          where DXT artifacts are unacceptable (smooth gradients, soft glows).

Assets NOT listed stay PNG (smallest download, slow decode). Outputs are committed
and ship under wwwroot/Content; WebContentManager.LoadTexture prefers .dds, then
.rtex, then .png. This is an OFFLINE step -- texconv is Windows-only and the dev
box differs from the Linux CI, which just consumes the committed outputs (same
model as tools/shaders/build_shaders.py and tools/audio/build_audio.py).

The committed .dds are built at --padtest 100 (a deliberate over-pad canary; see check_canary and
web CLAUDE.md) even though --padtest defaults to 0, so a run that would pad an asset LESS than the
file it is replacing is refused before anything is written. --drop-canary is the opt-out.

Usage:
  python tools/textures/build_textures.py [--config FILE] [--dry-run] [--only GLOB]
                                          [--padtest PX] [--drop-canary]
  python tools/textures/build_textures.py --selftest

Requires: Pillow (PIL); texconv.exe in tools/textures/ for any 'dxt' entries
(download: https://github.com/microsoft/DirectXTex/releases/latest/download/texconv.exe).
"""
import argparse
import fnmatch
import os
import struct
import subprocess
import sys
from typing import NoReturn

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
CONTENT = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content")
TEXCONV = os.path.join(HERE, "texconv.exe")
SCRATCH = os.path.join(HERE, "_build")
DEFAULT_CONFIG = os.path.join(HERE, "textures.config")
# Generated C# lookup consumed by WebContentManager.LoadTexture so it probes a precompiled
# sibling ONLY for assets that actually have one (unlisted keys skip straight to the .png,
# avoiding two guaranteed-failing OpenStream probes + exceptions per PNG-only texture).
MANIFEST_CS = os.path.join(REPO, "web", "EvilAliensWeb", "Compat", "PrecompiledTextures.cs")

RTEX_MAGIC = b"RTEX"
RTEX_VERSION = 1
RTEX_FMT_RGBA8 = 0  # straight (non-premultiplied) alpha, matching the unpacked content

# BC3 blocks are 4x4 and Chrome/ANGLE->D3D11 rejects a block texture whose W or H isn't a
# multiple of 4 (renders black). So every dxt sibling is PADDED up to a mult-of-4 (transparent,
# bottom/right only, so the original content keeps its exact top-left pixel coords). The original
# ("logical") size is stamped into the DDS header's unused reserved1 dwords; WebContentManager
# reads it back and every consumer (frame slicing, source rects, whole-texture draws) uses the
# LOGICAL size, so the padded strip is never sampled and nothing shifts. See TextureDims.cs.
# The one exception is the GUTTER-px edge replication in edge_gutter(): bilinear reaches a texel
# past even a correctly clamped source rect, so that texel must mirror the edge, not be transparent.
DDS_LOGICAL_MAGIC = b"LOGD"   # written at reserved1[2] to flag that reserved1[0..1] carry (w,h)


def pad4(x):
    return ((x + 3) // 4) * 4


def target_dims(w, h, pad_extra=0):
    """The padded (mult-of-4) size a dxt build writes for a logical w x h image.

    The ONE place that size is computed. build_dxt and the canary preflight below must not grow
    two copies of it: the preflight decides whether to abort by predicting what the build will
    write, so a second formula that drifted would silently predict the wrong size."""
    return pad4(w + pad_extra), pad4(h + pad_extra)


# How far the logical edge is replicated into the pad (see edge_gutter). ONE texel is all
# correctness needs -- bilinear can never reach further past the source rect. 4 rounds that up to a
# full BC3 block so that under a large pad no block the sampler can touch also holds transparent
# pad -- which is the case that ships, since the committed .dds are --padtest 100 builds (web
# CLAUDE.md, "The canary is LEFT ON"). At a minimal mult-of-4 pad (0-3 px) the whole pad is filled
# and the rounding is moot.
GUTTER = 4


def mip_sizes(w, h):
    """The (w, h) of every level of a full chain, D3D/texconv convention.

    Successive integer halving with a floor of 1, which is exactly KNI's
    TextureHelpers.GetSizeForLevel -- so a level built here lands at the size the runtime
    computes for it. The chain length matches TextureHelpers.CalculateMipLevels, and it must
    be the FULL chain: KNI allocates that many levels and GL only samples a mipmap-COMPLETE
    texture, so a short chain renders black rather than degrading."""
    sizes = [(w, h)]
    while sizes[-1] != (1, 1):
        pw, ph = sizes[-1]
        sizes.append((max(1, pw // 2), max(1, ph // 2)))
    return sizes


def edge_gutter(canvas, w, h, tw, th):
    """Replicate the logical edge into the first GUTTER px of the transparent pad.

    Draw sites clamp their source rect to the LOGICAL bounds, but SamplerState.LinearClamp only
    clamps at the TEXTURE border -- so a destination pixel landing in the last half texel blends
    the last content texel with texel [w] / row [h]. While the pad was transparent black that cost
    the tile's final ~1px up to 50% of its RGB and alpha: a visible seam at every tile boundary
    (dark on the opaque Mars sky, bright where the marshills silhouettes sit over it). Copying the
    edge outward makes the filtered result identical to a true clamp, so the seam cannot exist at
    any pad size.

    Only GUTTER px are filled -- the rest of the pad stays transparent so the --padtest canary
    (Trello f2621e52) still shows an obvious hole for code that uses the padded size by mistake.
    """
    # max(0, ...) because build_texviewer sizes with mult4_preserving_pitch, which can CROP one
    # axis while the other pads; a negative gutter is truthy and would reach resize() as a
    # negative dimension. No shipped asset hits that today -- the ship path only ever pad4()s.
    gw = max(0, min(GUTTER, tw - w))
    gh = max(0, min(GUTTER, th - h))
    if gw:
        col = canvas.crop((w - 1, 0, w, h))
        for x in range(w, w + gw):
            canvas.paste(col, (x, 0))
    if gh:
        row = canvas.crop((0, h - 1, w, h))
        for y in range(h, h + gh):
            canvas.paste(row, (0, y))
    if gw and gh:                     # corner: the one texel diagonally past the logical edge
        canvas.paste(canvas.crop((w - 1, h - 1, w, h)).resize((gw, gh)), (w, h))


def poke_dds_logical(path, w, h):
    """Stamp the logical (pre-pad) size into the DDS header's reserved1[0..2] dwords.
    dwWidth/dwHeight (offsets 16/12) keep the PADDED size the GPU uploads; reserved1 starts at
    file offset 32 and is otherwise unused by texconv, so [0]=w [1]=h [2]=magic is safe."""
    with open(path, "r+b") as f:
        f.seek(32)
        f.write(struct.pack("<II", w, h) + DDS_LOGICAL_MAGIC)


def fail(msg) -> NoReturn:
    # Flush stdout FIRST: it is block-buffered under a redirect/pipe while stderr is not, so an
    # error that says "listed above" would otherwise print before the lines it refers to.
    sys.stdout.flush()
    print("ERROR: " + msg, file=sys.stderr)
    sys.exit(1)


def src_png(asset):
    # asset is Content-relative, lowercase, no extension (e.g. gfx/sprites/x).
    # The on-disk root is capital "Content"; everything under it is lowercase.
    return os.path.join(CONTENT, asset.replace("/", os.sep) + ".png")


# ---------------------------------------------------------------------------------------------
# The --padtest canary gate (Trello 06c6c741).
#
# The committed .dds are deliberately built at --padtest 100 (web CLAUDE.md, "The canary is LEFT
# ON") while --padtest DEFAULTS TO 0 -- so a plain `python tools/textures/build_textures.py` used
# to silently strip the canary off every texture it touched, and the diff read as a harmless size
# win. So before anything is written, every SELECTED dxt asset is compared against the .dds already
# on disk and the build ABORTS if this run would pad it LESS than the file it is about to replace.
#
# Firing BEFORE the build rather than inspecting the output is deliberate: nothing bad ever reaches
# the working tree (so there is no revert dance), and check_pad_bleed's reassuring "ok: all 124
# replicate their logical edge" is never printed under a run that just dropped the canary -- that
# reassuring-all-clear-after-the-footgun is what motivated this gate.
#
# The comparison is over the OVER-PAD (padded minus the minimal mult-of-4), never over the padded
# dims: padded dims also move when the SOURCE PNG is resized, so a padded-dims rule flags a
# legitimate rebuild of a shrunken sprite whose canary is perfectly intact. selftest() pins that
# distinction against the naive rule.
# ---------------------------------------------------------------------------------------------


def pad_bleed_module():
    """Import the sibling guard module, whichever way this file was invoked."""
    if HERE not in sys.path:
        sys.path.insert(0, HERE)
    import check_pad_bleed
    return check_pad_bleed


def read_dds_dims(path):
    """(padded_w, padded_h, logical_w, logical_h) of an existing .dds, or None if there is none.

    None means "no prior build to compare against", which is exactly what exempts the FIRST build
    of a NEW asset from the gate. Reuses check_pad_bleed's header parse rather than growing a
    second one; a file it cannot parse is reported and treated as no-info (a malformed .dds is
    that guard's business, not this gate's)."""
    if not os.path.isfile(path):
        return None
    try:
        pw, ph, lw, lh, _mips, _block = pad_bleed_module().read_dds_header(path)
    except (ValueError, OSError) as e:
        print(f"  WARN  cannot read {os.path.relpath(path, REPO)} for the canary check: {e}")
        return None
    return pw, ph, lw, lh


def over_pad(pw, ph, lw, lh):
    """How far a built .dds is padded PAST the minimal mult-of-4 -- i.e. the canary it carries.

    (0, 0) for a ship-minimal (--padtest 0) build and for an unpadded/unstamped file; exactly
    (N, N) for a --padtest N build whenever N is a multiple of 4 -- all 124 committed .dds read
    +100/+100. Clamped at 0 so a hand-made .dds with non-mult-of-4 dims cannot read negative."""
    return max(0, pw - pad4(lw)), max(0, ph - pad4(lh))


def canary_shrink(disk, w, h, pad_extra):
    """THE RULE. None if this build is fine, else (over-pad on disk, over-pad this run would write).

    `disk` is read_dds_dims()'s tuple, or None for an asset that has no .dds yet -- exempt, since a
    new asset has no canary to lose. GROWING the pad is always allowed; only a shrink on either
    axis is a finding."""
    if disk is None:
        return None
    old = over_pad(*disk)
    new = over_pad(*target_dims(w, h, pad_extra), w, h)
    return (old, new) if new[0] < old[0] or new[1] < old[1] else None


def dds_path_for(asset):
    """Where build_dxt writes this asset's .dds -- the gate must inspect the file the build
    replaces, so both sides resolve the path through here."""
    png = src_png(asset)
    return os.path.join(os.path.dirname(png), os.path.basename(asset) + ".dds")


def asset_probe(asset):
    """(logical w, h, dims of the .dds already on disk) -- ALL the I/O the canary gate does.

    None when the source PNG is missing, which build_dxt reports properly; the gate must not
    pre-empt that error. Isolated behind one function so selftest() can drive check_canary with a
    table instead of a Content tree."""
    from PIL import Image
    png = src_png(asset)
    if not os.path.isfile(png):
        return None
    with Image.open(png) as im:
        w, h = im.size
    return w, h, read_dds_dims(dds_path_for(asset))


def fleet_canary(entries, known, probe):
    """The over-pad every already-built dxt asset agrees on, or None if they disagree / none exist.

    `known` is the {asset: disk} check_canary has already read, so nothing is read twice -- on a
    full build that covers every entry and this costs no I/O at all. Only consulted when a NEW
    asset is in the build, so an ordinary run never pays for the rest of the sweep either."""
    overs = set()
    for e in entries:
        if e[0] != "dxt":
            continue
        if e[1] in known:
            disk = known[e[1]]
        else:
            probed = probe(e[1])
            disk = probed[2] if probed else None
        if disk is not None:
            overs.add(over_pad(*disk))
    return overs.pop() if len(overs) == 1 else None


def check_canary(selected, entries, pad_extra, drop_canary, probe=asset_probe):
    """Preflight the SELECTED dxt entries: refuse to shrink the shipped over-pad canary.

    Runs before ANY write, and under --dry-run too -- a dry run that prints a plan for a command
    which would abort has predicted the wrong outcome. Zero selected dxt entries (e.g. a raw-only
    --only, or --manifest-only, which never reaches here) means zero checks and no output."""
    findings, fresh, known = [], [], {}
    for e in selected:
        if e[0] != "dxt":
            continue
        probed = probe(e[1])
        if probed is None:
            continue           # build_dxt owns the "source not found" error; don't pre-empt it
        w, h, disk = probed
        known[e[1]] = disk
        hit = canary_shrink(disk, w, h, pad_extra)
        if hit:
            findings.append((e[1], target_dims(w, h, pad_extra), disk, hit[0], hit[1]))
        elif disk is None:
            fresh.append((e[1], over_pad(*target_dims(w, h, pad_extra), w, h)))
    # A brand-new asset has no canary to lose, so the rule above cannot judge it -- but building the
    # one asset that ships WITHOUT the fleet's canary is the same mistake by another route. Say so.
    # Deliberately NOT fatal: the card requires a legitimate first build of a new asset to pass.
    if fresh:
        fleet = fleet_canary(entries, known, probe)
        for asset, new in fresh:
            if fleet is not None and new != fleet:
                print(f"  NOTE  {asset} has no .dds yet, so the canary gate cannot judge it: this "
                      f"run would build it +{new[0]}x{new[1]}px while the other assets carry "
                      f"+{fleet[0]}x{fleet[1]}px.  --padtest {fleet[0]} matches the shipped set.")
    if not findings:
        return
    verb = "strip" if pad_extra == 0 else "shrink"
    print(f"  [canary] {len(findings)} selected asset(s) carry an over-pad canary this run would "
          f"{verb}:")
    for asset, (tw, th), disk, old, new in findings:
        print(f"    {asset}  on disk {disk[0]}x{disk[1]} (logical {disk[2]}x{disk[3]}, "
              f"+{old[0]}x{old[1]}px)  ->  would build {tw}x{th} (+{new[0]}x{new[1]}px)")
    if drop_canary:
        print(f"  [drop-canary] this run is meant to {verb} it -- proceeding.")
        return
    keep = max(o for f in findings for o in f[3])
    fail(f"this build would {verb} the over-pad canary from {len(findings)} of the "
         f"{len(known)} selected dxt asset(s) (listed above).\n"
         "  The shipped .dds deliberately carry it -- tools/CLAUDE.md (Textures) and web\n"
         '  CLAUDE.md ("The canary is LEFT ON"). It is what makes a padded-vs-logical size bug\n'
         "  visible in play, so dropping it by accident costs that coverage silently.\n"
         f"  Keep it:                 --padtest {keep}\n"
         "  Remove it deliberately:  --drop-canary   (see tools/CLAUDE.md before you do)")


def mult4_preserving_pitch(total, divs):
    """NOTE: build_dxt no longer calls this (it pad4()s instead); kept because build_texviewer.py
    still imports it to size its preview crops. Largest multiple of 4 in [divs*cell, total] keeping
    floor(total/divs)==floor(v/divs).

    Returns (target, padded). target<=total => crop the unused edge; target>total =>
    pad up (pitch changes; caller warns)."""
    cell = total // divs
    lo = divs * cell  # smallest size that still covers all `divs` cells of `cell` px
    candidates = [v for v in range(lo, total + 1) if v % 4 == 0]
    if candidates:
        return max(candidates), False
    # No mult-of-4 keeps the pitch; pad up to the next mult-of-4 (pitch will shift).
    return ((total + 3) // 4) * 4, True


def compress_one(img, base, out_dir):
    """BC3-compress ONE image with no mip chain; return the path of the produced .dds.

    texconv names its output after the input's basename and may spell the extension either
    way, so normalise to lowercase <base>.dds here rather than at each call site."""
    os.makedirs(SCRATCH, exist_ok=True)
    tmp = os.path.join(SCRATCH, base + ".png")
    img.save(tmp)
    r = subprocess.run([TEXCONV, "-nologo", "-y", "-m", "1", "-f", "BC3_UNORM",
                        "-o", out_dir, tmp],
                       capture_output=True, text=True)
    # Check the exit code BEFORE probing for output: out_dir is usually the Content dir, where the
    # previously committed <base>.dds already exists, so a failed texconv would otherwise resolve
    # to that stale file and get re-stamped and reported as a fresh build.
    if r.returncode != 0:
        fail(f"texconv failed for {base} (exit {r.returncode})\n" + r.stdout + r.stderr)
    produced = None
    for ext in (".dds", ".DDS"):
        p = os.path.join(out_dir, base + ext)
        if os.path.isfile(p):
            produced = p
            break
    if produced is None:
        fail("texconv produced no .dds for " + base + "\n" + r.stdout + r.stderr)
    os.remove(tmp)
    return produced


# DDS header fields the mip splice has to set (offsets into the 128-byte legacy header).
DDSD_MIPMAPCOUNT = 0x00020000   # dwFlags bit saying dwMipMapCount is meaningful
DDSCAPS_COMPLEX = 0x00000008    # dwCaps: more than one surface
DDSCAPS_MIPMAP = 0x00400000     # dwCaps: this surface is a mip level


def build_mip_chain(im, w, h, tw, th, base, out_dds):
    """Write a full-mip-chain .dds, re-deriving the PAD AT EVERY LEVEL.

    The pad is metadata, not content. Handing texconv the padded canvas and asking for `-m 0`
    would filter the pad along WITH the content, so each level blends real pixels into
    transparent pad near the logical edge -- reintroducing exactly the bleed check_pad_bleed.py
    exists to prevent. Measured on 756-v1: a GUTTER of 4 px keeps that clean for log2(GUTTER)=2
    levels and then fails hard (alpha delta 0/0/0 at levels 0-2, then 127/191/223 at 3/4/5).

    So each level is built from the LOGICAL image alone -- downsample content, pad THAT to the
    level's padded size, run edge_gutter() on it -- and the levels are compressed separately and
    spliced. Every level then satisfies the same clamp property level 0 does, and the pad stays
    transparent at every level, so the --padtest canary survives the whole chain.
    """
    from PIL import Image
    pads = mip_sizes(tw, th)
    logs = mip_sizes(w, h)
    blobs = []
    content = im
    for lv, (pw, ph) in enumerate(pads):
        lw, lh = logs[min(lv, len(logs) - 1)]
        if lw > pw or lh > ph:
            fail(f"{base}: level {lv} content {lw}x{lh} does not fit its pad {pw}x{ph}")
        if content.size != (lw, lh):
            # Successive halving (BOX = area average) matches the D3D/texconv chain and KNI's
            # GetSizeForLevel, so a level lands at exactly the size the runtime computes for it.
            content = content.resize((lw, lh), Image.Resampling.BOX)
        canvas = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
        canvas.paste(content, (0, 0))
        edge_gutter(canvas, lw, lh, pw, ph)
        produced = compress_one(canvas, base, SCRATCH)
        with open(produced, "rb") as f:
            blobs.append(f.read())
        os.remove(produced)
    # Level 0's own header describes the whole surface; only the mip fields need setting.
    header = bytearray(blobs[0][:128])
    struct.pack_into("<I", header, 8, struct.unpack_from("<I", header, 8)[0] | DDSD_MIPMAPCOUNT)
    struct.pack_into("<I", header, 28, len(blobs))
    struct.pack_into("<I", header, 108,
                     struct.unpack_from("<I", header, 108)[0] | DDSCAPS_COMPLEX | DDSCAPS_MIPMAP)
    with open(out_dds, "wb") as f:
        f.write(bytes(header))
        for blob in blobs:
            f.write(blob[128:])
    return len(blobs)


def build_dxt(asset, dry, pad_extra=0, mip=False):
    from PIL import Image
    png = src_png(asset)
    if not os.path.isfile(png):
        fail("source not found: " + png)
    if not os.path.isfile(TEXCONV):
        fail("texconv.exe not found at " + TEXCONV + "\n  download: "
             "https://github.com/microsoft/DirectXTex/releases/latest/download/texconv.exe")
    im = Image.open(png).convert("RGBA")
    w, h = im.size
    # Pad up to a mult-of-4 (never crop). pad_extra (>0 only in --padtest) grossly over-pads so
    # any code that still reads the PADDED size instead of the logical size shows an obvious
    # artifact. Content stays at (0,0); the logical (w,h) is stamped into the .dds for the runtime.
    tw, th = target_dims(w, h, pad_extra)
    padded = (tw != w or th != h)
    base = os.path.basename(asset)
    out_dds = dds_path_for(asset)   # same helper the canary gate inspects, so they cannot diverge
    note = f"  (logical {w}x{h}, +{pad_extra}px test-pad)" if pad_extra else (
        f"  (logical {w}x{h})" if padded else "")
    levels = len(mip_sizes(tw, th)) if mip else 1
    note += f"  [{levels} mip levels]" if mip else ""
    print(f"  dxt  {asset}  {w}x{h} -> {tw}x{th}{note}")
    if dry:
        return
    if mip:
        build_mip_chain(im, w, h, tw, th, base, out_dds)
    else:
        canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
        canvas.paste(im, (0, 0))                          # pad bottom/right, transparent
        edge_gutter(canvas, w, h, tw, th)                 # ...except the first GUTTER px
        produced = compress_one(canvas, base, os.path.dirname(png))
        if produced != out_dds:
            if os.path.exists(out_dds):
                os.remove(out_dds)
            os.replace(produced, out_dds)
    if padded:
        poke_dds_logical(out_dds, w, h)   # stamp logical (pre-pad) size for the runtime
    print(f"       wrote {os.path.relpath(out_dds, REPO)}  ({os.path.getsize(out_dds)//1024} KB)")


def build_raw(asset, dry):
    from PIL import Image
    png = src_png(asset)
    if not os.path.isfile(png):
        fail("source not found: " + png)
    im = Image.open(png).convert("RGBA")
    w, h = im.size
    out = os.path.join(os.path.dirname(png), os.path.basename(asset) + ".rtex")
    print(f"  raw  {asset}  {w}x{h} -> {os.path.basename(out)}  ({w*h*4//1024} KB payload)")
    if dry:
        return
    header = RTEX_MAGIC + bytes([RTEX_VERSION, RTEX_FMT_RGBA8, 0, 0]) + struct.pack("<II", w, h)
    with open(out, "wb") as f:
        f.write(header)
        f.write(im.tobytes())  # RGBA, row-major top-to-bottom (matches SurfaceFormat.Color)
    print(f"       wrote {os.path.relpath(out, REPO)}  ({os.path.getsize(out)//1024} KB)")


def parse_config(path):
    entries = []
    with open(path, encoding="utf-8") as f:
        for ln, raw in enumerate(f, 1):
            line = raw.split("#", 1)[0].strip()
            if not line:
                continue
            parts = line.split()
            asset, fmt = parts[0], parts[1].lower()
            if fmt == "dxt":
                # A trailing "mip" opts this asset into a full mip chain (see build_mip_chain).
                # It is opt-in per asset: mipping everything would cost ~33% more bytes across all
                # ~124 .dds and soften every minified sprite, for the benefit of the one sheet that
                # is tiled far past its native size.
                # Reject anything we don't recognise rather than ignoring it: a typo ("mips",
                # "Mipmap") would otherwise build an unmipped sheet with no diagnostic, and the
                # only symptom is a shimmering wall in game. Loud failure is the convention here
                # (unknown format fails; a stale asset line aborts the whole run).
                for p in parts[2:]:
                    if not p.isdigit() and p != "mip":
                        fail(f"{path}:{ln}: unknown dxt option '{p}' (expected 'mip' or cols/rows)")
                mip = "mip" in parts[2:]
                # Optional trailing "cols rows" are parsed but now IGNORED by the build (pad4 no
                # longer needs the grid — the game owns frame layout via its own AnimationData).
                # Still accepted so existing config lines and the ?texviewer Save path don't error.
                nums = [p for p in parts[2:] if p.isdigit()]
                cols = int(nums[0]) if len(nums) > 0 else 1
                rows = int(nums[1]) if len(nums) > 1 else 1
                entries.append(("dxt", asset, cols, rows, mip))
            elif fmt == "raw":
                entries.append(("raw", asset, None, None, False))
            else:
                fail(f"{path}:{ln}: unknown format '{fmt}' (expected dxt|raw)")
    return entries


def write_generated(path, text, dry):
    """Write `text` only if it would actually change the file, in the file's OWN line endings.

    Both halves matter, and neither works alone (Trello 06c6c741). This repo is checked out with
    core.autocrlf=true and no .gitattributes rule for .cs (the root .gitattributes pins .razor and
    .cshtml only, because their whitespace reaches the assembly), so a generated .cs is CRLF while
    `text` is built with \\n: writing it verbatim rewrote every line ending on every run and left
    the file MODIFIED in git status with an EMPTY content diff, which looks like the build changed
    something and invites a pointless commit. Preserving the endings alone would still rewrite the
    file (bumping its mtime, so MSBuild rebuilds for nothing) when nothing changed; skipping on
    equal content alone would never match, because the rendered LF text never equals the CRLF file.
    A file that does not exist yet is written LF -- git's checkout filter owns the local flavour.

    Returns True if the content differs (i.e. it wrote, or under --dry-run would have)."""
    existing = None
    if os.path.isfile(path):
        with open(path, "rb") as f:
            existing = f.read()
    newline = "\r\n" if existing and b"\r\n" in existing else "\n"
    # Normalise first: `text` is LF-only today, but a caller that ever hands this CRLF would
    # otherwise get \r\r\n out.
    data = text.replace("\r\n", "\n").replace("\n", newline).encode("utf-8")
    if data == existing:
        return False
    if not dry:
        with open(path, "wb") as f:
            f.write(data)
    return True


def write_manifest_cs(entries, dry):
    # WebContentManager keys assets as ResolvePath does: "Content/" + the lowercase,
    # Content-relative path (ResolvePath calls ToLowerInvariant on every lookup key). So lowercase
    # here too — an uppercase config segment would emit a key that never matches the runtime key,
    # silently skipping the sibling and falling back to the slow PNG with no error. Dedupe while
    # we're at it: a repeated asset would emit a duplicate C# dictionary key, which throws at
    # PrecompiledTextures static-init (white-screen boot). dxt -> ".dds", raw -> ".rtex".
    siblings = {}  # ordered (py3.7+): asset(lower) -> ext
    for e in entries:
        asset = e[1].lower()
        ext = ".dds" if e[0] == "dxt" else ".rtex"
        if asset in siblings and siblings[asset] != ext:
            fail(f"textures.config lists '{e[1]}' twice with conflicting formats "
                 f"({siblings[asset]} vs {ext})")
        siblings[asset] = ext
    lines = [f'            {{ "Content/{a}", "{ext}" }},' for a, ext in siblings.items()]
    body = "\n".join(lines)
    text = (
        "// <auto-generated>\n"
        "// GENERATED by tools/textures/build_textures.py from tools/textures/textures.config.\n"
        "// Maps a WebContentManager texture key (ResolvePath output, e.g. \"Content/gfx/sprites/x\")\n"
        "// to the precompiled sibling extension shipped for it. WebContentManager.LoadTexture probes\n"
        "// ONLY the listed sibling; an unlisted key skips straight to the .png (no failing .dds/.rtex\n"
        "// OpenStream probes + thrown exceptions). Re-run build_textures.py after editing textures.config;\n"
        "// do NOT hand-edit this file.\n"
        "// </auto-generated>\n"
        "using System.Collections.Generic;\n"
        "\n"
        "namespace EvilAliensWeb.Compat\n"
        "{\n"
        "    internal static class PrecompiledTextures\n"
        "    {\n"
        "        public static readonly Dictionary<string, string> Siblings = new Dictionary<string, string>\n"
        "        {\n"
        f"{body}\n"
        "        };\n"
        "    }\n"
        "}\n"
    )
    changed = write_generated(MANIFEST_CS, text, dry)
    state = ("would rewrite" if dry else "rewritten") if changed else "unchanged"
    print(f"  manifest  {len(siblings)} entr{'y' if len(siblings)==1 else 'ies'} -> "
          f"{os.path.relpath(MANIFEST_CS, REPO)}  ({state})")


def _padded_dims_verdict(disk, w, h, pad_extra):
    """The SUPERSEDED rule, kept only so selftest() can show what it gets wrong. True == flagged.

    It compares the PADDED DIMS instead of the over-pad -- the obvious first guess, and wrong,
    because padded dims also shrink when the SOURCE PNG does."""
    if disk is None:
        return False
    tw, th = target_dims(w, h, pad_extra)
    return tw < disk[0] or th < disk[1]


def selftest():
    """Pin the canary rule and the generated-file write policy. No config, no .dds, no texconv."""
    canary = (1348, 1348, 1248, 1248)    # a shipped --padtest 100 build (756-v1; logical mult-of-4)
    minimal = (1252, 1252, 1250, 1250)   # the same asset as it would ship at --padtest 0
    oneaxis = (1348, 600, 1248, 600)     # a canary on the width only
    cases = [
        ("rebuild at --padtest 100 keeps the canary", canary, (1248, 1248), 100, False),
        ("the bare default strips the canary", canary, (1248, 1248), 0, True),
        ("a weaker --padtest 50 still shrinks it", canary, (1248, 1248), 50, True),
        ("minimal-pad asset rebuilt minimally", minimal, (1250, 1250), 0, False),
        ("minimal-pad asset gaining a canary", minimal, (1250, 1250), 100, False),
        ("NEW asset with no .dds on disk is exempt", None, (1000, 1000), 0, False),
        ("unpadded, unstamped .dds", (1024, 1024, 1024, 1024), (1024, 1024), 0, False),
        ("resized source PNG, canary kept", canary, (900, 900), 100, False),
        ("resized source PNG, canary dropped", canary, (900, 900), 0, True),
        ("shrink on one axis only", oneaxis, (1248, 600), 0, True),
        ("growth on the other axis is not a shrink", oneaxis, (1248, 600), 100, False),
    ]
    ok = True
    for name, disk, (w, h), extra, want_flag in cases:
        hit = canary_shrink(disk, w, h, extra)
        got_flag = hit is not None
        ok &= got_flag == want_flag
        detail = f"{hit[0]} -> {hit[1]}" if hit else "-"
        print(f"  {'ok  ' if got_flag == want_flag else 'FAIL'}  {name}: "
              f"{'flagged' if got_flag else 'passes'} (want "
              f"{'flagged' if want_flag else 'passes'})  [{detail}]")
    # The discrimination the over-pad measure exists for, asserted rather than assumed: the naive
    # padded-dims rule flags a rebuild of a shrunken source whose canary is intact.
    resized = (canary, 900, 900, 100)
    if not _padded_dims_verdict(*resized):
        print("  FAIL  the superseded padded-dims rule was expected to MISS-fire on a resized source")
        ok = False
    else:
        print("  ok    the superseded padded-dims rule flags a resized source whose canary is "
              "intact; the over-pad rule passes it")

    # The RULE is pinned above; this pins the GATE built on it -- above all that --drop-canary
    # actually bypasses a real finding, since a --drop-canary that silently did nothing would let
    # exactly the footgun this card exists to close back through. `probe` stands in for the whole
    # Content tree, so no .dds and no PNG are needed.
    tree = {"keeps": (1248, 1248, canary), "strips": (1248, 1248, canary),
            "brand-new": (1000, 1000, None)}
    entries = [("dxt", a, 1, 1, False) for a in tree] + [("raw", "a/glow", None, None, False)]
    probe = lambda asset: tree[asset]
    def gate(assets, pad_extra, drop=False):
        """(aborted?, everything it printed) for a check_canary run over `assets`.

        stderr is merged in because fail()'s message goes there while the findings it refers to go
        to stdout -- and the message is part of what these cases assert."""
        import contextlib
        import io as _io
        buf = _io.StringIO()
        aborted = False
        try:
            with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
                check_canary([e for e in entries if e[1] in assets], entries, pad_extra, drop,
                             probe=probe)
        except SystemExit:
            aborted = True
        return aborted, buf.getvalue()

    gate_cases = [
        ("a canary-stripping build aborts", gate(["strips"], 0)[0] is True),
        ("--drop-canary lets the SAME build through", gate(["strips"], 0, drop=True)[0] is False),
        ("...and says so out loud", "[drop-canary]" in gate(["strips"], 0, drop=True)[1]),
        ("a --padtest 100 build is silent", gate(["keeps", "strips"], 100) == (False, "")),
        ("a raw-only selection checks nothing", gate(["a/glow"], 0) == (False, "")),
        ("a new asset does not abort", gate(["brand-new"], 0)[0] is False),
        ("...but is reported against the fleet", "NOTE" in gate(["brand-new"], 0)[1]),
        ("...and is silent when it matches the fleet", gate(["brand-new"], 100) == (False, "")),
        ("the abort names the padtest that would keep it",
         "--padtest 100" in gate(["strips"], 0)[1]),
    ]
    for name, good in gate_cases:
        ok &= good
        print(f"  {'ok  ' if good else 'FAIL'}  check_canary: {name}")

    import tempfile
    text = "line one\nline two\n"
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "Generated.cs")

        def read():
            with open(p, "rb") as f:      # `with` matters: a leaked handle makes the
                return f.read()           # TemporaryDirectory cleanup flaky on Windows
        writes = [("first write creates the file", write_generated(p, text, False) is True),
                  ("...as LF", read() == b"line one\nline two\n"),
                  ("re-writing identical text is a no-op", write_generated(p, text, False) is False)]
        with open(p, "wb") as f:
            f.write(b"line one\r\nline two\r\n")     # the repo's own checked-out flavour
        writes += [("identical text over a CRLF file is a no-op",
                    write_generated(p, text, False) is False),
                   ("changed text does rewrite",
                    write_generated(p, "line one\nline three\n", False) is True),
                   ("...keeping the file's CRLF", read() == b"line one\r\nline three\r\n"),
                   ("--dry-run reports the change without writing",
                    write_generated(p, "line four\n", True) is True
                    and read() == b"line one\r\nline three\r\n")]
    for name, good in writes:
        ok &= good
        print(f"  {'ok  ' if good else 'FAIL'}  write_generated: {name}")
    print("selftest: " + ("ok" if ok else "FAILED"))
    return ok


def main():
    ap = argparse.ArgumentParser(description="Precompile sprites to .dds/.rtex per textures.config")
    ap.add_argument("--config", default=DEFAULT_CONFIG)
    ap.add_argument("--dry-run", action="store_true", help="print the plan, write nothing")
    ap.add_argument("--only", metavar="GLOB",
                    help="rebuild only assets whose Content-relative name matches GLOB (e.g. "
                         "'gfx/base/756-v1'). The manifest still covers the whole config. Keeps a "
                         "one-texture change from rewriting all ~124 committed .dds. Matching "
                         "nothing is an error, not a silent no-op.")
    ap.add_argument("--manifest-only", action="store_true",
                    help="regenerate Compat/PrecompiledTextures.cs only; skip the texture builds "
                         "(no texconv/Pillow needed)")
    ap.add_argument("--padtest", type=int, default=0, metavar="PX",
                    help="TEST MODE: over-pad every dxt by ~PX px (still rounded to mult-of-4) so "
                         "any code path that uses the padded size instead of the logical size shows "
                         "an obvious PX-sized artifact. Ship builds use 0 (minimal mult-of-4 pad), "
                         "but the COMMITTED .dds are --padtest 100 builds and the canary gate "
                         "refuses to shrink that without --drop-canary.")
    ap.add_argument("--drop-canary", action="store_true",
                    help="allow this run to strip/shrink the over-pad canary the committed .dds "
                         "carry. The deliberate opt-out from the canary gate, and what the eventual "
                         "ship rebuild at --padtest 0 needs. See tools/CLAUDE.md first.")
    ap.add_argument("--selftest", action="store_true",
                    help="check the canary rule and the generated-file write policy against a case "
                         "table; no config, no .dds, no texconv needed")
    args = ap.parse_args()

    if args.selftest:
        sys.exit(0 if selftest() else 1)

    entries = parse_config(args.config)
    print(f"build_textures: {len(entries)} asset(s) from {os.path.relpath(args.config, REPO)}"
          + ("  [dry-run]" if args.dry_run else "")
          + ("  [manifest-only]" if args.manifest_only else ""))
    if args.manifest_only:
        # Derived from the config alone, so it needs neither texconv nor Pillow -- and there is no
        # texture build for the canary gate to have an opinion about.
        write_manifest_cs(entries, args.dry_run)
        print("done.")
        return
    if args.padtest:
        print(f"  [padtest] over-padding every dxt by ~{args.padtest}px to surface any "
              f"padded-vs-logical size bug")
    selected = entries
    if args.only:
        selected = [e for e in entries if fnmatch.fnmatch(e[1], args.only)]
        if not selected:
            fail(f"--only {args.only!r} matched none of the {len(entries)} config entries")
        print(f"  [only] {len(selected)} of {len(entries)} asset(s) match {args.only!r}")
    # Refuse to quietly strip the shipped over-pad canary. This runs before ANY write -- including
    # the manifest below -- so a refused run leaves the working tree byte-for-byte untouched, and
    # the post-build all-clear can never end up vouching for one.
    check_canary(selected, entries, args.padtest, args.drop_canary)
    # The manifest is derived from the config alone, so emit it before the textures: it then stays
    # in sync even if a later texture build fails.
    write_manifest_cs(entries, args.dry_run)
    for e in selected:
        if e[0] == "dxt":
            build_dxt(e[1], args.dry_run, args.padtest, mip=e[4])
        else:
            build_raw(e[1], args.dry_run)
    # Gate the build on the pad gutter surviving compression -- a silent regression here is a
    # hairline seam on every tiled sprite, which is exactly the class of bug nobody re-checks by
    # hand. Nothing was written on --dry-run/--manifest-only, so there is nothing to verify there.
    # The guard sweeps EVERY shipped .dds, not just what this run wrote, so under --only the
    # failure need not be in the asset you just rebuilt -- read the BLEED lines for which it is.
    if not args.dry_run:
        if not pad_bleed_module().run():
            fail("a shipped .dds does not replicate its logical edge (see above)")
    print("done.")


if __name__ == "__main__":
    main()
