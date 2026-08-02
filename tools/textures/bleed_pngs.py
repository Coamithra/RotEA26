#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
bleed_pngs.py - the shipping-layer straight-alpha edge bleed for the PNGs that have
no precompile step (card 5d75b700).

WHY THIS EXISTS. Alpha is STRAIGHT project-wide, so bilinear filtering averages RGB
*ignoring* alpha and mixes whatever the transparent side of a sprite edge carries into
that edge. Every producer of an RGBA image here leaves that at (0,0,0), so a scaled or
minified sprite draws with a dark halo. tools/imagebleed.py fixes an image; the question
is WHERE each asset gets fixed:

  * An asset listed in textures.config is compiled to a .dds/.rtex sibling, and
    build_textures.py bleeds its source on the way in (see load_source there). Its PNG
    is only an input, so it is left exactly as its producer wrote it. THIS SCRIPT SKIPS
    THOSE -- one owner per asset, no double ownership.

  * A PNG that is neither listed nor precompiled is loaded verbatim by the game. There is
    no compile step to hook, so the bleed has to be in the file. Their nominal producers are ~8 scattered scripts
    (build_powerbar.py, build_bullets.py, build_lazer_glow.py, tools/upscale/*), several
    of which cannot be re-run at all (gitignored raw sources, AI models), and four assets
    -- gfx/menu/star, gfx/preview/small_face_a|b, gfx/tutorial/grid3 -- have no producer
    beyond tools/xnb/unpack.py's verbatim decode. So the bleed is owned HERE, as a
    separate layer over whatever wrote the file, rather than smeared across scripts that
    cannot be executed to prove it.

RE-RUN THIS after any tool regenerates an unlisted PNG (an upscale pipeline, an unpack,
a hand re-export). It is idempotent -- a second run writes nothing -- and `--check`
exits 1 on any asset that has drifted back, which is the lint for exactly that.

Alpha is NEVER written: only the RGB of fully-transparent texels moves, so coverage,
shapes and antialiasing are bit-for-bit unchanged and nothing visible is touched.

Usage:
  python tools/textures/bleed_pngs.py                 # bleed every unlisted PNG in place
  python tools/textures/bleed_pngs.py --check         # report only, exit 1 if any unbled
  python tools/textures/bleed_pngs.py --dry-run       # same as --check but exit 0
  python tools/textures/bleed_pngs.py --only GLOB     # restrict to matching assets

Requires: Pillow, numpy, scipy.
"""
import argparse
import fnmatch
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.dirname(HERE))
CONTENT = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content")

import build_textures as bt                                  # noqa: E402
from imagebleed import bleed_transparent_rgb, count_unbled   # noqa: E402


def asset_pngs(root=CONTENT):
    """Every PNG under Content, as (abs path, Content-relative slash-separated key)."""
    out = []
    for dirpath, _dirs, files in os.walk(root):
        for name in sorted(files):
            if name.lower().endswith(".png"):
                p = os.path.join(dirpath, name)
                out.append((p, os.path.relpath(p, root).replace(os.sep, "/")))
    return sorted(out, key=lambda t: t[1])


def configured_assets():
    """Content-relative keys (no extension) that textures.config precompiles."""
    return {e[0] for e in bt.parse_config(bt.DEFAULT_CONFIG)}


def is_precompiled(png, configured):
    """True when build_textures.py owns this asset, so this script must not touch it.

    The UNION of two signals, because each misses the other's case. A .dds/.rtex sibling
    on disk covers an asset whose config line was removed but whose artifact still ships.
    A textures.config entry covers the reverse and more dangerous window: an asset listed
    but not yet BUILT (or only --dry-run'd) has no sibling yet, and bleeding its PNG then
    would rewrite the source of record that build_textures.py deliberately leaves alone.
    """
    stem = png[:-4]
    if os.path.exists(stem + ".dds") or os.path.exists(stem + ".rtex"):
        return True
    return os.path.relpath(stem, CONTENT).replace(os.sep, "/") in configured


def process(png, key, write):
    """-> (unbled_texels, wrote). Opens once, bleeds, writes only if it changed."""
    from PIL import Image
    with Image.open(png) as im:
        if im.mode in ("RGB", "L"):
            return 0, False        # no alpha channel at all -> no transparent field
        if im.mode != "RGBA":
            # P+tRNS and LA DO carry transparency, so skipping them silently would let
            # exactly the regression this script lints for pass --check unnoticed. None
            # ship today; say so loudly rather than guess at a conversion round-trip.
            sys.exit(f"ERROR: {key}: unsupported mode {im.mode!r}. If it carries alpha "
                     f"(P+tRNS, LA) it needs bleeding -- convert it to RGBA at source, "
                     f"or teach this script how to write its mode back.")
        rgba = im.convert("RGBA")
    n = count_unbled(rgba)
    if not n or not write:
        return n, False
    # optimize=True: these ship to the browser, and PIL's default compress_level leaves
    # a few percent on an asset that just gained entropy in its transparent field.
    bleed_transparent_rgb(rgba).save(png, optimize=True)
    return n, True


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true",
                    help="report unbled assets and exit 1 if any (the regression lint)")
    ap.add_argument("--dry-run", action="store_true", help="report only, always exit 0")
    ap.add_argument("--only", metavar="GLOB",
                    help="restrict to assets matching GLOB (Content-relative, no extension)")
    args = ap.parse_args()

    write = not (args.check or args.dry_run)
    pngs = asset_pngs()
    if not pngs:
        sys.exit(f"no PNGs found under {CONTENT} -- wrong repo root?")

    configured = configured_assets()
    selected, skipped_precompiled = [], 0
    for png, key in pngs:
        if is_precompiled(png, configured):
            skipped_precompiled += 1
            continue
        if args.only and not fnmatch.fnmatch(key[:-4], args.only):
            continue
        selected.append((png, key))
    if args.only and not selected:
        # Same hard fail as build_textures.py's --only (card 06c6c741): a typo'd glob
        # that silently processes nothing must not report success.
        sys.exit(f"ERROR: --only {args.only!r} matched none of the "
                 f"{len(pngs) - skipped_precompiled} candidate assets")

    print(f"bleed_pngs: {len(selected)} PNG(s) under {os.path.relpath(CONTENT, REPO)}"
          f"  ({skipped_precompiled} precompiled, owned by build_textures.py)")

    unbled, wrote = [], 0
    for png, key in selected:
        n, did = process(png, key, write)
        if n:
            unbled.append((key, n))
            print(f"  {'bled ' if did else 'UNBLED'} {key}  ({n} texels)")
            wrote += did

    if not unbled:
        print("ok: every selected PNG already carries its nearest-ink RGB in the "
              "transparent field.")
        return 0
    if write:
        print(f"done. rewrote {wrote} PNG(s); alpha untouched in all of them.")
        return 0
    print(f"{len(unbled)} PNG(s) are unbled -- run "
          f"`python tools/textures/bleed_pngs.py` to fix.")
    return 1 if args.check else 0


if __name__ == "__main__":
    sys.exit(main())
