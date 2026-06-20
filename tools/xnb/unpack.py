"""
Stage 3 content unpacker.

Walks the extracted Xbox 360 Content tree and produces web-loadable assets under
web/EvilAliensWeb/wwwroot/Content:

  Texture2D  -> <name>.png          (mip 0, RGBA)
  SpriteFont -> <name>.fnt.png      (glyph atlas, RGBA)
                <name>.fnt          (binary metrics, see write_fnt)
  Curve      -> <name>.curve        (binary)
  .dat/.txt  -> copied verbatim     (animation data / level text)

Effects (Stage 5), audio (Stage 6) and video (Stage 6) are skipped and logged.

All output paths are LOWERCASED so the case-sensitive GitHub Pages host serves
them regardless of the (inconsistent) casing the game asks for; the runtime
WebContentManager lowercases requests to match.

Usage:  python unpack.py
"""
import os
import shutil
import struct
import sys

import numpy as np
from PIL import Image

from xnb import (decompress_xnb, parse_manifest, parse_texture2d,
                 parse_spritefont, parse_curve, short_reader_name, read_file)
from tex import decode_texture

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, "..", "..", "extracted", "584E07D1", "Content"))
DST = os.path.normpath(os.path.join(HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot", "Content"))

COPY_EXT = {".dat", ".txt", ".png"}
SKIP_EXT = {".xwb", ".xsb", ".xgs", ".wmv"}

stats = {"texture": 0, "font": 0, "curve": 0, "copy": 0, "skip": 0, "error": 0}
skipped = []


def to_image(rgba_bytes, w, h):
    """RGBA bytes -> straight (non-premultiplied) alpha PIL image.

    The original Xbox 3.1 content is straight alpha (transparent pixels keep real
    RGB), and the renderer matches it with BlendState.NonPremultiplied
    (SrcAlpha/InvSrcAlpha), so we emit the decoded RGBA verbatim -- no premultiply.
    """
    arr = np.frombuffer(rgba_bytes, dtype=np.uint8).reshape(h, w, 4)
    return Image.fromarray(arr, "RGBA")


def out_path(rel, new_ext):
    rel = rel.replace("\\", "/").lower()
    base = os.path.splitext(rel)[0]
    p = os.path.join(DST, *(base + new_ext).split("/"))
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return p


def save_texture(rel, tex):
    rgba = decode_texture(tex["format"], tex["mips"][0], tex["width"], tex["height"])
    to_image(rgba, tex["width"], tex["height"]).save(out_path(rel, ".png"))
    stats["texture"] += 1


def write_fnt(path, sf):
    chars = [ord(c) for c in sf["chars"]]
    n = len(chars)
    with open(path, "wb") as f:
        f.write(struct.pack("<i", sf["line_spacing"]))
        f.write(struct.pack("<f", sf["spacing"]))
        has_def = 1 if sf["default_char"] is not None else 0
        f.write(struct.pack("<i", has_def))
        f.write(struct.pack("<i", ord(sf["default_char"]) if has_def else 0))
        f.write(struct.pack("<i", n))
        for c in chars:
            f.write(struct.pack("<i", c))
        for (x, y, w, h) in sf["glyphs"]:
            f.write(struct.pack("<4i", x, y, w, h))
        for (x, y, w, h) in sf["cropping"]:
            f.write(struct.pack("<4i", x, y, w, h))
        for (a, b, c) in sf["kerning"]:
            f.write(struct.pack("<3f", a, b, c))


def save_font(rel, sf):
    tex = sf["texture"]
    rgba = decode_texture(tex["format"], tex["mips"][0], tex["width"], tex["height"])
    to_image(rgba, tex["width"], tex["height"]).save(out_path(rel, ".fnt.png"))
    write_fnt(out_path(rel, ".fnt"), sf)
    stats["font"] += 1


def write_curve(path, cv):
    with open(path, "wb") as f:
        f.write(struct.pack("<i", cv["pre_loop"]))
        f.write(struct.pack("<i", cv["post_loop"]))
        f.write(struct.pack("<i", len(cv["keys"])))
        for (pos, val, ti, to, cont) in cv["keys"]:
            f.write(struct.pack("<ffffi", pos, val, ti, to, cont))


def save_curve(rel, cv):
    write_curve(out_path(rel, ".curve"), cv)
    stats["curve"] += 1


def copy_verbatim(rel, full):
    dst = out_path(rel, os.path.splitext(rel)[1].lower())
    shutil.copyfile(full, dst)
    stats["copy"] += 1


def handle_xnb(rel, full):
    header, payload = decompress_xnb(read_file(full))
    r, readers, ns, tid = parse_manifest(payload)
    kind = short_reader_name(readers[0][0]) if readers else "?"
    if kind == "Texture2DReader":
        save_texture(rel, parse_texture2d(r))
    elif kind == "SpriteFontReader":
        save_font(rel, parse_spritefont(r))
    elif kind == "CurveReader":
        save_curve(rel, parse_curve(r))
        if r.p != len(payload):
            print("  WARN curve %s consumed %d/%d" % (rel, r.p, len(payload)))
    elif kind in ("EffectReader", "SoundEffectReader", "SongReader", "VideoReader"):
        skipped.append((rel, kind))
        stats["skip"] += 1
    else:
        skipped.append((rel, "?" + kind))
        stats["skip"] += 1


def main():
    if os.path.isdir(DST):
        shutil.rmtree(DST)
    os.makedirs(DST, exist_ok=True)
    seen_lower = {}
    for root, _, files in os.walk(SRC):
        for fn in files:
            full = os.path.join(root, fn)
            rel = os.path.relpath(full, SRC).replace("\\", "/")
            low = rel.lower()
            if low in seen_lower and seen_lower[low] != rel:
                print("  COLLISION (case): %s vs %s" % (rel, seen_lower[low]))
            seen_lower[low] = rel
            ext = os.path.splitext(fn)[1].lower()
            try:
                if ext == ".xnb":
                    handle_xnb(rel, full)
                elif ext in COPY_EXT:
                    copy_verbatim(rel, full)
                elif ext in SKIP_EXT:
                    skipped.append((rel, "audio/video"))
                    stats["skip"] += 1
                else:
                    skipped.append((rel, "other"))
                    stats["skip"] += 1
            except Exception as e:
                stats["error"] += 1
                print("  ERROR %s: %s" % (rel, e))
    print("\n== summary ==")
    for k, v in stats.items():
        print("  %-8s %d" % (k, v))
    print("\n== skipped (later stages) ==")
    for rel, why in sorted(skipped):
        print("  %-45s %s" % (rel, why))


if __name__ == "__main__":
    sys.exit(main())
