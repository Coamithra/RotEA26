#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_shaders.py - compile the Stage-5 HLSL effects to web-loadable blobs.

The original XNA 3.x .fx sources were lost (only compiled DX9 .xnb survived,
which KNI can't read), so the shaders were rewritten in HLSL under src/. This
script compiles them, offline, with KNI's own content pipeline:

    src/<name>.fx
      --(MGCB, /platform:BlazorGL)-->  bin/.../<name>.xnb   (xnb-wrapped MGFX)
      --(strip xnb header)-->          <name>.mgfxo         (raw MGFX v10 GLSL)
      --(copy, lowercased)-->          wwwroot/Content/...

At runtime Compat/WebContentManager.LoadEffect reads <name>.mgfxo and hands the
bytes to `new Effect(gd, bytes)` - the exact ctor the stock EffectReader feeds.

The ~13 EffectHandler sprite-effect variants are all the ONE master src/sprite.fx
compiled with different #defines. MGCB dedupes by source path (it won't build the
same .fx twice), so we generate a tiny stub per variant under gen/ that #defines
its features and #includes the master (the preprocessor only searches the stub's
own directory, so a copy of sprite.fx is placed alongside the stubs).

MGCB ships in the matching-version nuget package
  nkast.Xna.Framework.Content.Pipeline.Builder.Windows 4.1.9001
(restore it with: dotnet restore on any project referencing it; it lands in the
global nuget cache). BlazorGL makes MojoShader emit WebGL GLSL and the MGFX
header is version 10 - matching the 4.1.9001 runtime.

Run:  PYTHONIOENCODING=utf-8 python tools/shaders/build_shaders.py
Idempotent: regenerates stubs, rebuilds every effect, overwrites shipped blobs.
"""
import glob
import os
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(HERE, "src")
GEN = os.path.join(HERE, "gen")
BIN = os.path.join(HERE, "bin")
WWWROOT = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content")

MGCB_VERSION = "4.1.9001"

# Bloom shaders -> Content/Bloom; everything else -> Content/GFX/Effects.
# All destination names lowercased (Pages is case-sensitive; WebContentManager
# lowercases every request).
BLOOM = {"bloomextract", "bloomcombine", "gaussianblur"}

# The master sprite.fx is never compiled directly; only via the stubs below.
MASTER = "sprite.fx"

# EffectHandler variant name -> feature defines fed to the master shader.
SPRITE_VARIANTS = {
    "colorize":                          ["COLORIZE"],
    "lighten":                           ["LIGHTEN"],
    "fade":                              ["FADE"],
    "interpolate":                       ["INTERPOLATE"],
    "colorize_lighten":                  ["COLORIZE", "LIGHTEN"],
    "colorize_fade":                     ["COLORIZE", "FADE"],
    "colorize_interpolate":              ["COLORIZE", "INTERPOLATE"],
    "fade_interpolate":                  ["FADE", "INTERPOLATE"],
    "lighten_interpolate":              ["LIGHTEN", "INTERPOLATE"],
    "colorize_fade_interpolate":         ["COLORIZE", "FADE", "INTERPOLATE"],
    "colorize_lighten_interpolate":      ["COLORIZE", "LIGHTEN", "INTERPOLATE"],
    "lighten_interpolate_fade":          ["LIGHTEN", "INTERPOLATE", "FADE"],
    "colorize_lighten_interpolate_fade": ["COLORIZE", "LIGHTEN", "INTERPOLATE", "FADE"],
}


def find_mgcb():
    nuget = os.path.join(os.path.expanduser("~"), ".nuget", "packages",
                         "nkast.xna.framework.content.pipeline.builder.windows")
    exact = os.path.join(nuget, MGCB_VERSION, "tools", "MGCB.exe")
    if os.path.isfile(exact):
        return exact
    cands = sorted(glob.glob(os.path.join(nuget, "*", "tools", "MGCB.exe")))
    if cands:
        print("WARNING: MGCB %s not found; using %s" % (MGCB_VERSION, cands[-1]))
        return cands[-1]
    sys.exit("MGCB.exe not found. Restore nkast.Xna.Framework.Content.Pipeline."
             "Builder.Windows %s (dotnet restore a project referencing it)." % MGCB_VERSION)


def win(path):
    try:
        out = subprocess.run(["cygpath", "-w", path], capture_output=True, text=True)
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()
    except OSError:
        pass
    return path


def gen_stubs():
    """Write the per-variant stubs (and a master copy) into gen/."""
    if not os.path.isdir(GEN):
        os.makedirs(GEN)
    # Copy the master beside the stubs so `#include "sprite.fx"` resolves (the
    # FX preprocessor only searches the including file's own directory).
    master_src = os.path.join(SRC, MASTER)
    with open(master_src, "r", encoding="utf-8") as f:
        master = f.read()
    with open(os.path.join(GEN, MASTER), "w", encoding="utf-8", newline="\n") as f:
        f.write(master)
    for name, defines in SPRITE_VARIANTS.items():
        body = "".join("#define %s\n" % d for d in defines) + '#include "%s"\n' % MASTER
        with open(os.path.join(GEN, name + ".fx"), "w", encoding="utf-8", newline="\n") as f:
            f.write(body)


def write_mgcb(builds):
    """builds: list of source paths relative to HERE."""
    lines = ["/outputDir:bin", "/intermediateDir:obj", "/rebuild", ""]
    for rel in builds:
        lines += ["/importer:EffectImporter", "/processor:EffectProcessor",
                  "/build:%s" % rel.replace("\\", "/"), ""]
    path = os.path.join(HERE, "effects.mgcb")
    with open(path, "w", encoding="ascii", newline="\n") as f:
        f.write("\n".join(lines))
    return path


def read_7bit(data, i):
    res = shift = 0
    while True:
        b = data[i]; i += 1
        res |= (b & 0x7F) << shift
        if not (b & 0x80):
            return res, i
        shift += 7


def strip_xnb(xnb_path):
    """Extract the raw MGFX blob from an effect .xnb (MGCB output)."""
    data = open(xnb_path, "rb").read()
    if data[:3] != b"XNB":
        sys.exit("not an xnb: %s" % xnb_path)
    i = 10                                   # 'XNB' + platform + ver + flags + uint32 size
    n_readers, i = read_7bit(data, i)
    for _ in range(n_readers):
        slen, i = read_7bit(data, i)
        i += slen + 4                        # reader type name + int32 version
    _shared, i = read_7bit(data, i)          # shared resource count (0 for effects)
    _typeid, i = read_7bit(data, i)          # object's type-reader id
    mlen = struct.unpack_from("<i", data, i)[0]; i += 4   # EffectReader: int32 length
    blob = data[i:i + mlen]
    if blob[:4] != b"MGFX":
        sys.exit("MGFX signature not found in %s" % xnb_path)
    if blob[4] != 10:
        print("WARNING: %s is MGFX v%d (runtime expects v10)" % (xnb_path, blob[4]))
    return blob


def ship(xnb_path, name):
    blob = strip_xnb(xnb_path)
    sub = "bloom" if name.lower() in BLOOM else os.path.join("gfx", "effects")
    dest_dir = os.path.join(WWWROOT, sub)
    os.makedirs(dest_dir, exist_ok=True)
    dest = os.path.join(dest_dir, name.lower() + ".mgfxo")
    open(dest, "wb").write(blob)
    print("  %-34s -> %s (%d bytes)" % (name, os.path.relpath(dest, REPO).replace("\\", "/"), len(blob)))


def main():
    standalone = [os.path.basename(p) for p in sorted(glob.glob(os.path.join(SRC, "*.fx")))
                  if os.path.basename(p) != MASTER]
    if not standalone:
        sys.exit("no standalone .fx in %s" % SRC)
    gen_stubs()

    builds = ["src/" + f for f in standalone] + \
             ["gen/" + n + ".fx" for n in SPRITE_VARIANTS]
    write_mgcb(builds)

    mgcb = win(find_mgcb())
    print("Compiling %d standalone + %d sprite variants for BlazorGL ..."
          % (len(standalone), len(SPRITE_VARIANTS)))
    res = subprocess.run([mgcb, "/platform:BlazorGL", "/@:effects.mgcb"],
                         cwd=HERE, capture_output=True, text=True)
    sys.stdout.write(res.stdout)
    if res.stderr.strip():
        sys.stderr.write(res.stderr)
    if "0 failed" not in res.stdout:
        sys.exit("MGCB build reported failures (see output above).")

    print("Shipping blobs:")
    for f in standalone:
        name = os.path.splitext(f)[0]
        ship(os.path.join(BIN, "src", name + ".xnb"), name)
    for name in SPRITE_VARIANTS:
        ship(os.path.join(BIN, "gen", name + ".xnb"), name)
    print("Done.")


if __name__ == "__main__":
    main()
