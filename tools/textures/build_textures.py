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
          sheet is cropped to the largest mult-of-4 that PRESERVES the integer cell
          pitch floor(W/cols) x floor(H/rows) -- only the never-sampled edge pixels
          (beyond cols*cellW / rows*cellH) are dropped, so frame rects are unchanged.
          If no mult-of-4 exists in that window the image is padded up instead and a
          warning is printed (the pitch shifts; prefer raw for that sheet).

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

RTEX_MAGIC = b"RTEX"
RTEX_VERSION = 1
RTEX_FMT_RGBA8 = 0  # straight (non-premultiplied) alpha, matching the unpacked content


def fail(msg) -> NoReturn:
    print("ERROR: " + msg, file=sys.stderr)
    sys.exit(1)


def src_png(asset):
    # asset is Content-relative, lowercase, no extension (e.g. gfx/sprites/x).
    # The on-disk root is capital "Content"; everything under it is lowercase.
    return os.path.join(CONTENT, asset.replace("/", os.sep) + ".png")


def mult4_preserving_pitch(total, divs):
    """Largest multiple of 4 in [divs*cell, total] keeping floor(total/divs)==floor(v/divs).

    Returns (target, padded). target<=total => crop the unused edge; target>total =>
    pad up (pitch changes; caller warns)."""
    cell = total // divs
    lo = divs * cell  # smallest size that still covers all `divs` cells of `cell` px
    candidates = [v for v in range(lo, total + 1) if v % 4 == 0]
    if candidates:
        return max(candidates), False
    # No mult-of-4 keeps the pitch; pad up to the next mult-of-4 (pitch will shift).
    return ((total + 3) // 4) * 4, True


def build_dxt(asset, cols, rows, dry):
    from PIL import Image
    png = src_png(asset)
    if not os.path.isfile(png):
        fail("source not found: " + png)
    if not os.path.isfile(TEXCONV):
        fail("texconv.exe not found at " + TEXCONV + "\n  download: "
             "https://github.com/microsoft/DirectXTex/releases/latest/download/texconv.exe")
    im = Image.open(png).convert("RGBA")
    w, h = im.size
    tw, tw_pad = mult4_preserving_pitch(w, cols)
    th, th_pad = mult4_preserving_pitch(h, rows)
    note = ""
    if tw_pad or th_pad:
        note = "  WARNING: padded (no pitch-preserving mult-of-4) -> frame pitch shifts; consider raw"
    base = os.path.basename(asset)
    out_dds = os.path.join(os.path.dirname(png), base + ".dds")
    print(f"  dxt  {asset}  {w}x{h} ({cols}x{rows}) -> {tw}x{th}  "
          f"cell {tw // cols}x{th // rows}{note}")
    if dry:
        return
    os.makedirs(SCRATCH, exist_ok=True)
    tmp = os.path.join(SCRATCH, base + ".png")
    if tw <= w and th <= h:
        im.crop((0, 0, tw, th)).save(tmp)               # crop unused edge
    else:
        canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
        canvas.paste(im, (0, 0))                         # pad bottom/right, transparent
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
                cols = int(parts[2]) if len(parts) > 2 else 1
                rows = int(parts[3]) if len(parts) > 3 else 1
                entries.append(("dxt", asset, cols, rows))
            elif fmt == "raw":
                entries.append(("raw", asset, None, None))
            else:
                fail(f"{path}:{ln}: unknown format '{fmt}' (expected dxt|raw)")
    return entries


def main():
    ap = argparse.ArgumentParser(description="Precompile sprites to .dds/.rtex per textures.config")
    ap.add_argument("--config", default=DEFAULT_CONFIG)
    ap.add_argument("--dry-run", action="store_true", help="print the plan, write nothing")
    args = ap.parse_args()

    entries = parse_config(args.config)
    print(f"build_textures: {len(entries)} asset(s) from {os.path.relpath(args.config, REPO)}"
          + ("  [dry-run]" if args.dry_run else ""))
    for e in entries:
        if e[0] == "dxt":
            build_dxt(e[1], e[2], e[3], args.dry_run)
        else:
            build_raw(e[1], args.dry_run)
    print("done.")


if __name__ == "__main__":
    main()
