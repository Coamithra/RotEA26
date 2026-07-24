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

Usage:
  python tools/textures/build_textures.py [--config FILE] [--dry-run]

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
    print("ERROR: " + msg, file=sys.stderr)
    sys.exit(1)


def src_png(asset):
    # asset is Content-relative, lowercase, no extension (e.g. gfx/sprites/x).
    # The on-disk root is capital "Content"; everything under it is lowercase.
    return os.path.join(CONTENT, asset.replace("/", os.sep) + ".png")


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
    tw = pad4(w + pad_extra)
    th = pad4(h + pad_extra)
    padded = (tw != w or th != h)
    base = os.path.basename(asset)
    out_dds = os.path.join(os.path.dirname(png), base + ".dds")
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
    print(f"  manifest  {len(siblings)} entr{'y' if len(siblings)==1 else 'ies'} -> "
          f"{os.path.relpath(MANIFEST_CS, REPO)}")
    if not dry:
        with open(MANIFEST_CS, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)


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
                         "an obvious PX-sized artifact. Ship builds use 0 (minimal mult-of-4 pad).")
    args = ap.parse_args()

    entries = parse_config(args.config)
    print(f"build_textures: {len(entries)} asset(s) from {os.path.relpath(args.config, REPO)}"
          + ("  [dry-run]" if args.dry_run else "")
          + ("  [manifest-only]" if args.manifest_only else ""))
    # The manifest is derived from the config alone, so emit it first — it stays in sync even if a
    # later texture build fails, and --manifest-only regenerates it without texconv/Pillow.
    write_manifest_cs(entries, args.dry_run)
    if args.manifest_only:
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
        sys.path.insert(0, HERE)   # sibling module, whichever way this file was invoked
        import check_pad_bleed
        if not check_pad_bleed.run():
            fail("a shipped .dds does not replicate its logical edge (see above)")
    print("done.")


if __name__ == "__main__":
    main()
