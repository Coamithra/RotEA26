#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_texviewer.py - prep step for the in-game ?texviewer texture-format viewer.

The viewer (Compat/TexViewerScene.cs, reached at ?texviewer) shows the RAW (PNG-decoded,
lossless) and the DXT (BC3/DXT5, lossy) version of each sprite side by side, both drawn through
the REAL game GPU pipeline, so you can flip between them, scrutinise the artifacts, and lock a
per-sprite format decision into tools/textures/textures.config. See plans/texviewer.md + the Trello
card "Revisit per-sprite texture format".

To compare against, the scene needs an actual .dds for each candidate. We do NOT want to drop those
next to the PNGs (WebContentManager would start LOADING them in the real game). So this tool writes
throwaway DXT previews into a SEPARATE scratch dir the game never ships or auto-loads:

    wwwroot/Content/texviewer/<asset>.dds        BC3/DXT5 preview (real GPU decode in the scene)
    wwwroot/Content/texviewer/manifest.json      the asset list + sizes + current decisions

Both are GITIGNORED (local dev only). Production ships the real siblings that build_textures.py
emits from textures.config; this tool is only the comparison harness. Kept separate from
build_textures.py precisely so these previews can never leak into the shipped Content siblings /
PrecompiledTextures.cs.

"raw" == the PNG decode: an .rtex is uncompressed straight-alpha RGBA8, i.e. pixel-identical to the
PNG's StbImageSharp decode. So the scene compares the original .png (the raw reference) against this
.dds; we only need to build the .dds here.

Grid (cols/rows) only matters for the mult-of-4 crop that preserves a sheet's cell pitch. BC3 is a
per-4x4-block codec, so the artifacts a 1x1 whole-image preview shows are representative of any grid;
we seed the real grid from textures.config where it's known (so already-configured sheets preview
exactly), default the rest to 1x1, and let the viewer's panel set cols/rows when you save a DXT line.

Usage:
  python tools/textures/build_texviewer.py [--only GLOB] [--dry-run] [--manifest-only]

Requires: Pillow; texconv.exe in tools/textures/ (same as build_textures.py). Offline / Windows-only,
like build_textures.py -- CI never runs it.
"""
import argparse
import fnmatch
import json
import os
import subprocess
import sys

# Reuse build_textures' constants + the pitch-preserving mult-of-4 helper so the preview crop
# matches exactly what a shipped .dds would get.
import build_textures as bt

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
CONTENT = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content")
VIEWER_DIR = os.path.join(CONTENT, "texviewer")
MANIFEST = os.path.join(VIEWER_DIR, "manifest.json")
CONFIG = os.path.join(HERE, "textures.config")


def rel_asset(png_path):
    """Content-relative, lowercase, no extension key (matches WebContentManager.ResolvePath)."""
    rel = os.path.relpath(png_path, CONTENT).replace(os.sep, "/")
    return rel[:-4].lower() if rel.lower().endswith(".png") else rel.lower()


def load_config_decisions():
    """asset(lower) -> (fmt, cols, rows) from the current textures.config (fmt in dxt|raw)."""
    out = {}
    if not os.path.isfile(CONFIG):
        return out
    for entry in bt.parse_config(CONFIG):
        kind, asset = entry[0], entry[1].lower()
        if kind == "dxt":
            out[asset] = ("dxt", entry[2], entry[3])
        else:
            out[asset] = ("raw", 1, 1)
    return out


def enumerate_pngs():
    pngs = []
    for root, _dirs, files in os.walk(CONTENT):
        # Never enumerate our own scratch output.
        if os.path.commonpath([os.path.abspath(root), VIEWER_DIR]) == VIEWER_DIR:
            continue
        for fn in files:
            if fn.lower().endswith(".png"):
                pngs.append(os.path.join(root, fn))
    return pngs


def build_dds_preview(png_path, asset, cols, rows, dry):
    """texconv the PNG to a BC3 .dds under VIEWER_DIR/<asset>.dds. Returns byte size or None."""
    from PIL import Image
    im = Image.open(png_path).convert("RGBA")
    w, h = im.size
    tw, _ = bt.mult4_preserving_pitch(w, cols)
    th, _ = bt.mult4_preserving_pitch(h, rows)
    out_dds = os.path.join(VIEWER_DIR, asset.replace("/", os.sep) + ".dds")
    if dry:
        return None
    os.makedirs(os.path.dirname(out_dds), exist_ok=True)
    os.makedirs(bt.SCRATCH, exist_ok=True)
    tmp = os.path.join(bt.SCRATCH, os.path.basename(asset) + ".png")
    if tw <= w and th <= h:
        im.crop((0, 0, tw, th)).save(tmp)
    else:
        canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
        canvas.paste(im, (0, 0))
        canvas.save(tmp)
    r = subprocess.run([bt.TEXCONV, "-nologo", "-y", "-m", "1", "-f", "BC3_UNORM",
                        "-o", os.path.dirname(out_dds), tmp],
                       capture_output=True, text=True)
    produced = None
    base = os.path.basename(asset)
    for ext in (".dds", ".DDS"):
        p = os.path.join(os.path.dirname(out_dds), base + ext)
        if os.path.isfile(p):
            produced = p
            break
    os.remove(tmp)
    if produced is None:
        print(f"  WARN texconv produced no .dds for {asset}\n{r.stdout}{r.stderr}", file=sys.stderr)
        return None
    if produced != out_dds:
        if os.path.exists(out_dds):
            os.remove(out_dds)
        os.replace(produced, out_dds)
    return os.path.getsize(out_dds)


def main():
    ap = argparse.ArgumentParser(description="Build DXT previews + manifest for the ?texviewer scene")
    ap.add_argument("--only", help="glob over the Content-relative asset key (e.g. 'gfx/sprites/*')")
    ap.add_argument("--dry-run", action="store_true", help="print the plan, write nothing")
    ap.add_argument("--manifest-only", action="store_true",
                    help="rewrite manifest.json from existing previews; no texconv")
    args = ap.parse_args()

    if not os.path.isfile(bt.TEXCONV) and not args.manifest_only:
        bt.fail("texconv.exe not found at " + bt.TEXCONV + "\n  download: "
                "https://github.com/microsoft/DirectXTex/releases/latest/download/texconv.exe")

    decisions = load_config_decisions()
    pngs = enumerate_pngs()
    records = []
    for png in sorted(pngs):
        asset = rel_asset(png)
        if args.only and not fnmatch.fnmatch(asset, args.only.lower()):
            continue
        from PIL import Image
        with Image.open(png) as im:
            w, h = im.size
        fmt, cols, rows = decisions.get(asset, ("png", 1, 1))
        png_bytes = os.path.getsize(png)
        dds_path = os.path.join(VIEWER_DIR, asset.replace("/", os.sep) + ".dds")
        if args.manifest_only:
            dds_bytes = os.path.getsize(dds_path) if os.path.isfile(dds_path) else 0
        else:
            print(f"  dxt-preview  {asset}  {w}x{h} ({cols}x{rows})")
            dds_bytes = build_dds_preview(png, asset, cols, rows, args.dry_run) or 0
        records.append({
            "asset": asset,
            "w": w, "h": h,
            "cols": cols, "rows": rows,
            "pngBytes": png_bytes,
            "ddsBytes": dds_bytes,
            "rawBytes": w * h * 4 + 16,   # .rtex = 16-byte header + RGBA8
            "current": fmt,               # dxt|raw|png from textures.config
        })

    # Expensive (slow-to-decode) sprites first — that's what the viewer is for.
    records.sort(key=lambda r: r["pngBytes"], reverse=True)
    print(f"build_texviewer: {len(records)} asset(s)"
          + ("  [dry-run]" if args.dry_run else "")
          + ("  [manifest-only]" if args.manifest_only else ""))
    if not args.dry_run:
        os.makedirs(VIEWER_DIR, exist_ok=True)
        with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
            json.dump({"assets": records}, f, indent=1)
        print(f"  wrote {os.path.relpath(MANIFEST, REPO)}  ({len(records)} entries)")
    print("done.")


if __name__ == "__main__":
    main()
