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
DDS_LOGICAL_MAGIC = b"LOGD"   # written at reserved1[2] to flag that reserved1[0..1] carry (w,h)


def pad4(x):
    return ((x + 3) // 4) * 4


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


def build_dxt(asset, dry, pad_extra=0):
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
    print(f"  dxt  {asset}  {w}x{h} -> {tw}x{th}{note}")
    if dry:
        return
    os.makedirs(SCRATCH, exist_ok=True)
    tmp = os.path.join(SCRATCH, base + ".png")
    canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    canvas.paste(im, (0, 0))                              # pad bottom/right, transparent
    canvas.save(tmp)
    r = subprocess.run([TEXCONV, "-nologo", "-y", "-m", "1", "-f", "BC3_UNORM",
                        "-o", os.path.dirname(png), tmp],
                       capture_output=True, text=True)
    # texconv writes <base>.DDS/.dds in -o; normalise to lowercase <base>.dds.
    produced = None
    for ext in (".dds", ".DDS"):
        p = os.path.join(os.path.dirname(png), base + ext)
        if os.path.isfile(p):
            produced = p
            break
    if produced is None:
        fail("texconv produced no .dds for " + asset + "\n" + r.stdout + r.stderr)
    if produced != out_dds:
        if os.path.exists(out_dds):
            os.remove(out_dds)
        os.replace(produced, out_dds)
    os.remove(tmp)
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
                # Optional trailing "cols rows" are parsed but now IGNORED by the build (pad4 no
                # longer needs the grid — the game owns frame layout via its own AnimationData).
                # Still accepted so existing config lines and the ?texviewer Save path don't error.
                cols = int(parts[2]) if len(parts) > 2 else 1
                rows = int(parts[3]) if len(parts) > 3 else 1
                entries.append(("dxt", asset, cols, rows))
            elif fmt == "raw":
                entries.append(("raw", asset, None, None))
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
    for e in entries:
        if e[0] == "dxt":
            build_dxt(e[1], args.dry_run, args.padtest)
        else:
            build_raw(e[1], args.dry_run)
    print("done.")


if __name__ == "__main__":
    main()
