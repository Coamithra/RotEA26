#!/usr/bin/env python
"""Offline **3D model -> AnimatedSprite sheet** pipeline for the big UFO / mothership /
spider boss (Trello card 6a376549: "redo ... as 3D models for effectively infinite
resolution").

The game draws these bosses as `AnimatedSprite`s: a packed atlas PNG + a binary `.dat`
(see `datfmt.py`). The sheets ship at their *original render resolution* (e.g. 640x480
per frame), which is the resolution ceiling. This tool re-renders a supplied 3D model at
an arbitrary supersample factor, trims + packs the frames into a fresh atlas, and emits a
byte-compatible `.dat` -- so a much crisper sheet drops straight into the existing draw
path. Pair it with `AnimatedSprite`'s `supersample` constructor arg (default 1 = no-op)
so the higher-res sheet keeps its original on-screen size.

Scope (per the card's decision): **static hero pose or an N-angle turntable**. An online
image-to-3D tool (Meshy / Tripo / Rodin / ...) gives ONE static mesh with no rig, so the
multi-pose gameplay animations (spider fly/jump/land) stay on their hand-made sheets until
an animated model exists -- see `tools/models/README.md`.

Renderer = **Blender headless** (`blender -b -P`). Blender is a heavy, Windows-friendly,
dev-box-only dependency (like `texconv` for `tools/textures`); CI just ships the committed
PNGs. `bpy` has no Python 3.12 wheel, so we shell out to a `blender` executable found via
$BLENDER, the config's `blender_exe`, PATH, or the usual install dirs.

Usage:
    python tools/models/build_models.py                # build every object with a source model
    python tools/models/build_models.py --only bigufo  # just one
    python tools/models/build_models.py --dry-run      # print what would happen
    python tools/models/build_models.py --selftest     # exercise pack + .dat round-trip, no Blender

An object whose `source` model is missing is SKIPPED (inert) -- safe in CI / fresh clones,
same pattern as tools/audio/install_external.py.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from datfmt import Animation, Frame, read_dat, write_dat  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(HERE, "models.config")
PAD = 1  # transparent gutter between packed frames (avoids bilinear bleed)


# ---------------------------------------------------------------------------
# Blender discovery + render
# ---------------------------------------------------------------------------

def find_blender(cfg: dict) -> str | None:
    for cand in (os.environ.get("BLENDER"), cfg.get("blender_exe")):
        if cand and os.path.isfile(cand):
            return cand
    on_path = shutil.which("blender")
    if on_path:
        return on_path
    def ver_key(name):  # "Blender 4.10" -> (4, 10) so it sorts above "Blender 4.9"
        return tuple(int(n) for n in re.findall(r"\d+", name)) or (0,)
    for root in (r"C:\Program Files\Blender Foundation",
                 r"C:\Program Files (x86)\Blender Foundation"):
        if os.path.isdir(root):
            for name in sorted(os.listdir(root), key=ver_key, reverse=True):  # newest first
                exe = os.path.join(root, name, "blender.exe")
                if os.path.isfile(exe):
                    return exe
    return None


def render_frames(blender: str, obj: dict, out_dir: str) -> list[str]:
    """Render each frame to out_dir/frame_###.png via Blender headless. Returns paths."""
    job = {
        "model": obj["_abs_source"],
        "out_dir": out_dir,
        "width": obj["_render_w"],
        "height": obj["_render_h"],
        "frames": obj["_frame_count"],
        "mode": obj.get("mode", "hero"),
        "turntable_axis": obj.get("turntable_axis", "Z"),
        "camera": obj.get("camera", {}),
        "light": obj.get("light", {}),
        "samples": obj.get("samples", 64),
        "view_transform": obj.get("view_transform", "Standard"),
    }
    job_path = os.path.join(out_dir, "_job.json")
    with open(job_path, "w", encoding="utf-8") as fh:
        json.dump(job, fh)
    script = os.path.join(HERE, "blender_render.py")
    cmd = [blender, "-b", "-P", script, "--", job_path]
    print(f"    blender: {' '.join(cmd)}")
    subprocess.run(cmd, check=True)
    frames = [os.path.join(out_dir, f"frame_{i:03d}.png") for i in range(job["frames"])]
    missing = [f for f in frames if not os.path.isfile(f)]
    if missing:
        raise RuntimeError(f"Blender did not produce {len(missing)} expected frame(s); "
                           f"first missing: {missing[0]}")
    return frames


# ---------------------------------------------------------------------------
# Trim + pack
# ---------------------------------------------------------------------------

def _bbox(img: Image.Image) -> tuple[int, int, int, int]:
    """Alpha bounding box (left, top, right, bottom); right/bottom exclusive."""
    bb = img.getchannel("A").getbbox()
    if bb is None:  # fully transparent frame -> a 1x1 sentinel so the .dat stays valid
        return (0, 0, 1, 1)
    return bb


def pack(frame_images: list[Image.Image], render_w: int, render_h: int):
    """Trim each frame to its alpha bbox and shelf-pack into one atlas.

    Returns (atlas_image, [Frame, ...]). Frame coords are in RENDER pixels; on-screen
    size is restored by the AnimatedSprite `supersample` divisor.
    """
    trimmed = []
    for img in frame_images:
        if img.mode != "RGBA":
            img = img.convert("RGBA")
        l, t, r, b = _bbox(img)
        trimmed.append((img.crop((l, t, r, b)), l, t, r, b))

    total_area = sum((c.width + PAD) * (c.height + PAD) for c, *_ in trimmed)
    atlas_w = max((c.width + PAD for c, *_ in trimmed), default=1)
    atlas_w = max(atlas_w, int(math.sqrt(total_area) * 1.15))

    # Shelf pack, tallest first, preserving original frame order in the .dat.
    order = sorted(range(len(trimmed)), key=lambda i: trimmed[i][0].height, reverse=True)
    placements: dict[int, tuple[int, int]] = {}
    x = y = shelf_h = 0
    for i in order:
        c = trimmed[i][0]
        if x + c.width + PAD > atlas_w and x > 0:
            x = 0
            y += shelf_h
            shelf_h = 0
        placements[i] = (x, y)
        x += c.width + PAD
        shelf_h = max(shelf_h, c.height + PAD)
    atlas_h = y + shelf_h

    limit = 32767  # .dat stores frame coords as int16; WebGL/ANGLE caps texture size anyway
    if max(atlas_w, atlas_h, render_w, render_h) > limit:
        raise ValueError(
            f"packed atlas is {atlas_w}x{atlas_h} (render frame {render_w}x{render_h}); the "
            f".dat stores coordinates as int16 (max {limit}), and the GPU won't accept a "
            f"texture that large. Lower this object's 'supersample' or frame count in "
            f"models.config.")

    atlas = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
    frames: list[Frame] = []
    for i, (c, l, t, r, b) in enumerate(trimmed):
        px, py = placements[i]
        atlas.paste(c, (px, py))
        frames.append(Frame(render_w, render_h, l, t, r, b, px, py))
    return atlas, frames


# ---------------------------------------------------------------------------
# Build one object
# ---------------------------------------------------------------------------

def emit_grid(imgs: list[Image.Image], cols: int, rows: int):
    """Uniform cols x rows sheet for AlienDrawableGameComponent (no trim, no .dat).

    Cells are the full render frame -- the component slices by cell position
    ((texture.Width/cols) etc), so every cell must be identical size. Returns the sheet.
    """
    cw, ch = imgs[0].width, imgs[0].height
    sheet = Image.new("RGBA", (cw * cols, ch * rows), (0, 0, 0, 0))
    for i, im in enumerate(imgs):
        if im.mode != "RGBA":
            im = im.convert("RGBA")
        assert (im.width, im.height) == (cw, ch), (
            f"grid frame {i} is {im.width}x{im.height}, expected {cw}x{ch} -- every rendered "
            "frame must be the same size for a uniform grid (Blender renders them uniform)")
        sheet.paste(im, ((i % cols) * cw, (i // cols) * ch))
    return sheet


def build_object(name: str, obj: dict, cfg: dict, dry_run: bool) -> bool:
    if obj.get("_template"):
        print(f"  [skip] {name}: template entry -- retarget it and set \"_template\": false "
              "(or drop the flag) before building.")
        return False
    src = obj.get("source")
    abs_src = os.path.normpath(os.path.join(HERE, src)) if src else None
    if not abs_src or not os.path.isfile(abs_src):
        print(f"  [skip] {name}: no source model at {src!r} (drop one in to build this).")
        return False

    layout = obj.get("layout", "atlas")  # "atlas" (AnimatedSprite+.dat) | "grid" (component)
    ss = int(obj.get("supersample", 4))
    dw, dh = int(obj["design_width"]), int(obj["design_height"])
    mode = obj.get("mode", "hero")
    cols = rows = 1
    if layout == "grid":
        cols, rows = int(obj.get("columns", 1)), int(obj.get("rows", 1))
        frame_count = cols * rows if mode == "turntable" else 1
        cols = cols if frame_count > 1 else 1
        rows = rows if frame_count > 1 else 1
    else:
        frame_count = int(obj.get("turntable_frames", 16)) if mode == "turntable" else 1
    obj.update(_abs_source=abs_src, _render_w=dw * ss, _render_h=dh * ss,
               _frame_count=frame_count)

    out_base = os.path.normpath(os.path.join(HERE, obj["output"]))
    ext = ".png" + ("+.dat" if layout == "atlas" else "")
    print(f"  [build] {name}: {layout}/{mode}, {frame_count} frame(s) @ {dw*ss}x{dh*ss} "
          f"(design {dw}x{dh} x{ss}) -> {os.path.relpath(out_base, HERE)}{ext}")
    if dry_run:
        return True

    blender = find_blender(cfg)
    if not blender:
        print("  [error] Blender not found. Install Blender and set $BLENDER, add it to "
              "PATH, or set \"blender_exe\" in models.config. See tools/models/README.md.")
        return False
    print(f"    using blender: {blender}")

    with tempfile.TemporaryDirectory(prefix="eamodels_") as tmp:
        frame_paths = render_frames(blender, obj, tmp)
        imgs = [Image.open(p).convert("RGBA") for p in frame_paths]
        os.makedirs(os.path.dirname(out_base), exist_ok=True)
        if layout == "grid":
            sheet = emit_grid(imgs, cols, rows)
            sheet.save(out_base + ".png")
            out_dim, out_frames = sheet.size, frame_count
            note = ("Grid sheet for AlienDrawableGameComponent. To keep on-screen size, "
                    "add DesignFrameWidth[\"%s\"] = %d (the CURRENT per-cell design px) so "
                    "textureScale divides the %dx cells back down." %
                    (obj.get("texture_key", "<GFX/...>"), dw, ss))
        else:
            sheet, frames = pack(imgs, obj["_render_w"], obj["_render_h"])
            sheet.save(out_base + ".png")
            anim = Animation(name=obj.get("anim_name", name), frames=frames,
                             fps=float(obj.get("fps", 0.0)))
            write_dat(out_base + ".dat", [anim], atlas_name=obj.get("atlas_name", "test"))
            out_dim, out_frames = sheet.size, len(frames)
            note = ("Atlas+.dat for AnimatedSprite. Construct it with supersample=%d to "
                    "keep the original on-screen size at %dx crispness." % (ss, ss))

    sidecar = {
        "layout": layout, "supersample": ss, "design_width": dw, "design_height": dh,
        "render_width": obj["_render_w"], "render_height": obj["_render_h"],
        "frames": out_frames, "mode": mode,
        "columns": cols if layout == "grid" else None,
        "rows": rows if layout == "grid" else None,
        "note": note,
    }
    with open(out_base + ".model.json", "w", encoding="utf-8") as fh:
        json.dump(sidecar, fh, indent=2)
    print(f"    wrote {out_dim[0]}x{out_dim[1]} {layout}, {out_frames} frame(s); "
          f"supersample={ss}\n    -> {note}")
    return True


# ---------------------------------------------------------------------------
# Self-test (no Blender): prove pack + .dat round-trip against the C# read order
# ---------------------------------------------------------------------------

def selftest() -> int:
    print("[selftest] synthetic 8-frame turntable -> pack -> .dat -> re-read")
    render_w, render_h = 256, 256
    imgs = []
    for i in range(8):
        im = Image.new("RGBA", (render_w, render_h), (0, 0, 0, 0))
        # a solid ellipse whose size varies per frame, offset from center -> varied bboxes
        from PIL import ImageDraw
        d = ImageDraw.Draw(im)
        w = 60 + i * 12
        cx, cy = 128 + (i - 4) * 6, 128
        d.ellipse((cx - w, cy - w // 2, cx + w, cy + w // 2), fill=(200, 40, 40, 255))
        imgs.append(im)

    atlas, frames = pack(imgs, render_w, render_h)
    assert len(frames) == 8, "frame count"
    # No packed rect may overlap another (basic packer sanity).
    rects = [(f.x_pos, f.y_pos, f.x_pos + (f.max_x - f.min_x), f.y_pos + (f.max_y - f.min_y))
             for f in frames]
    for a in range(len(rects)):
        for b in range(a + 1, len(rects)):
            ax0, ay0, ax1, ay1 = rects[a]
            bx0, by0, bx1, by1 = rects[b]
            assert ax0 >= bx1 or bx0 >= ax1 or ay0 >= by1 or by0 >= ay1, \
                f"frames {a} and {b} overlap in the atlas"
    # Every packed rect must lie inside the atlas.
    for f in frames:
        assert f.x_pos + (f.max_x - f.min_x) <= atlas.width, "rect past atlas width"
        assert f.y_pos + (f.max_y - f.min_y) <= atlas.height, "rect past atlas height"
        assert f.original_width == render_w and f.original_height == render_h, "orig dims"

    with tempfile.TemporaryDirectory() as tmp:
        dat = os.path.join(tmp, "t.dat")
        write_dat(dat, [Animation("selftest", frames, fps=12.0)], atlas_name="test")
        anims, atlas_name, header = read_dat(dat)
    assert header == 1 and atlas_name == "test", "header/name"
    assert len(anims) == 1 and len(anims[0].frames) == 8, "round-trip anim/frame count"
    for orig, got in zip(frames, anims[0].frames):
        assert orig.as_tuple() == got.as_tuple(), f"frame mismatch {orig} != {got}"
    assert abs(anims[0].fps - 12.0) < 1e-4, "fps fixed-point round-trip"
    print(f"[selftest] OK -- packed {atlas.width}x{atlas.height}, .dat round-trips exactly")
    return 0


# ---------------------------------------------------------------------------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", help="build just this object key from models.config")
    ap.add_argument("--dry-run", action="store_true", help="print plan, render nothing")
    ap.add_argument("--selftest", action="store_true",
                    help="exercise pack + .dat round-trip without Blender")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    with open(CONFIG_PATH, encoding="utf-8") as fh:
        cfg = json.load(fh)
    objects = cfg.get("objects", {})
    if args.only:
        if args.only not in objects:
            print(f"No object '{args.only}' in models.config. Have: {', '.join(objects)}")
            return 2
        objects = {args.only: objects[args.only]}

    built = 0
    for name, obj in objects.items():
        if build_object(name, obj, cfg, args.dry_run):
            built += 1
    print(f"\nDone. {built}/{len(objects)} object(s) built. "
          f"{'(dry run)' if args.dry_run else ''}")
    if built == 0 and not args.dry_run:
        print("Nothing built -- drop a model at tools/models/source/<name>.glb "
              "(see tools/models/README.md) or run --selftest.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
