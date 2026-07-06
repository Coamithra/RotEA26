#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_brain_overlays.py - triage the Wan i2v takes from gen_brain_anims.py and pack
the chosen ones into feathered sprite sheets + a manifest the Brain boss draws.

For each named region it:
  1. loads the extracted frames (new_assets_raw/brainanim/<name>/frames),
  2. TRIAGES motion (mean inter-frame diff + a border-drift metric) so a static-image
     dud or a whole-frame camera drift can be spotted/rejected,
  3. colour-matches every frame's border to the ORIGINAL crop (undo VAE colour drift)
     and multiplies in an edge FEATHER so the patch dissolves into the static art,
  4. resizes to cells + packs a squarest grid -> wwwroot/Content/gfx/sprites/brainov_<name>.png,
  5. appends a manifest entry (texture-space anchor + size, so the game pins the on-screen
     footprint to the brain-texel crop and it pulses with the boss) to
     wwwroot/Content/data/brainoverlays.json.
  6. writes a diagnostic _<name>.gif + _<name>_contact.png (gitignored) to eyeball.

The manifest is the ONLY thing the game reads; a region NOT passed here is simply not
drawn. Straight (non-premultiplied) alpha throughout. Run with the AnimGen venv (PIL+numpy):

  C:/Programming/animgen/.venv/Scripts/python.exe tools/brainanim/build_brain_overlays.py <name> [<name> ...]
  ... --list                 # just print motion triage for every generated region
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
WORK = REPO / "new_assets_raw/brainanim"
SPRITES = REPO / "web/EvilAliensWeb/wwwroot/Content/gfx/sprites"
DATA = REPO / "web/EvilAliensWeb/wwwroot/Content/data"
MANIFEST = DATA / "brainoverlays.json"
BRAIN_W, BRAIN_H = 1448, 1086            # brainbosshd.png dims (texture space)

# --- packing / look knobs ---
KEEP_FRAMES = 17        # decimate to ~this many (the interpolation shader smooths low fps)
CELL_W = 224            # cell width in texels (on-screen size is pinned by texW, not this)
FEATHER = 0.20          # edge feather as a fraction of the smaller cell dim (dissolve into art)
DEFAULT_FPS = 10.0      # playback fps (ping-ponged in game); mech faster, flesh slower via regions
SEP = 1                 # transparent cell gutter so interpolation can't bleed across cells


def load_regions():
    return {r["name"]: r for r in
            json.loads((HERE / "regions.json").read_text(encoding="utf-8"))["regions"]}


def frames_of(name):
    fdir = WORK / name / "frames"
    files = sorted(fdir.glob("frame_*.png")) if fdir.exists() else []
    return [np.asarray(Image.open(f).convert("RGB")).astype(np.float32) for f in files]


def triage(frames):
    """(mean_motion, border_drift) in 0..255 grey levels."""
    if len(frames) < 2:
        return 0.0, 0.0
    g = [f.mean(axis=2) for f in frames]
    diffs = [np.abs(g[i + 1] - g[i]) for i in range(len(g) - 1)]
    motion = float(np.mean([d.mean() for d in diffs]))
    h, w = g[0].shape
    b = max(2, int(min(h, w) * 0.10))
    mask = np.ones((h, w), bool)
    mask[b:-b, b:-b] = False               # outer border band only
    drift = float(np.mean([d[mask].mean() for d in diffs]))
    return motion, drift


def decimate(frames, keep):
    if len(frames) <= keep:
        return frames
    idx = np.linspace(0, len(frames) - 1, keep).round().astype(int)
    return [frames[i] for i in idx]


def feather_mask(w, h):
    """1 in the interior, smooth ramp to 0 at the border (fraction FEATHER)."""
    fx = max(1.0, FEATHER * min(w, h))
    xx = np.minimum(np.arange(w), np.arange(w)[::-1])[None, :]
    yy = np.minimum(np.arange(h), np.arange(h)[::-1])[:, None]
    d = np.minimum(xx, yy).astype(np.float32)
    m = np.clip(d / fx, 0.0, 1.0)
    return m * m * (3 - 2 * m)              # smoothstep


def colour_match(frames, crop_rgb, crop_alpha):
    """Shift every frame by (crop_border_mean - frame0_border_mean) so the VAE's global
    colour drift doesn't make even the rest frame mismatch the static art it overlays.
    Only OPAQUE border pixels are sampled (crop_alpha), so a crop that overhangs the ball
    doesn't average in the sprite's transparent green backdrop and cast the patch green."""
    h, w, _ = frames[0].shape
    b = max(2, int(min(h, w) * 0.12))
    border = np.zeros((h, w), bool)
    border[:b, :] = border[-b:, :] = border[:, :b] = border[:, -b:] = True
    alpha = np.asarray(Image.fromarray(crop_alpha).resize((w, h), Image.LANCZOS))
    band = border & (alpha > 128)
    if not band.any():
        return [np.clip(f, 0, 255) for f in frames]
    crop = np.asarray(Image.fromarray(crop_rgb).resize((w, h), Image.LANCZOS)).astype(np.float32)
    off = crop[band].mean(0) - frames[0][band].mean(0)
    return [np.clip(f + off, 0, 255) for f in frames]


def grid_for(n):
    best = None
    for cols in range(1, n + 1):
        rows = math.ceil(n / cols)
        score = abs(cols - rows) + (cols * rows - n)  # squarish, few empty cells
        if best is None or score < best[0]:
            best = (score, cols, rows)
    return best[1], best[2]


def build(name, region):
    frames = frames_of(name)
    if not frames:
        print(f"  {name}: NO FRAMES (run gen_brain_anims.py first) - skipped")
        return None
    motion, drift = triage(frames)
    x0, y0, x1, y1 = region["box"]
    brain = np.asarray(Image.open(
        REPO / "web/EvilAliensWeb/wwwroot/Content/gfx/sprites/brainbosshd.png"
    ).convert("RGBA"))[y0:y1, x0:x1]
    crop_rgb = brain[..., :3]
    crop_alpha = brain[..., 3]           # brain silhouette in this crop

    frames = decimate(frames, KEEP_FRAMES)
    frames = colour_match(frames, crop_rgb, crop_alpha)
    n = len(frames)
    tw, th = x1 - x0, y1 - y0
    cw = CELL_W
    ch = max(1, round(CELL_W * th / tw))
    ch += ch % 2
    # Feather = rectangular edge ramp * the brain's own alpha, so the patch dissolves
    # into the static art AND never draws where the brain is transparent (no green
    # backdrop leaking over the space background where a crop overhangs the ball).
    alpha_cell = np.asarray(Image.fromarray(crop_alpha).resize((cw, ch), Image.LANCZOS)).astype(np.float32) / 255.0
    fm = feather_mask(cw, ch) * alpha_cell
    cols, rows = grid_for(n)
    SW = cols * cw + (cols - 1) * SEP
    SH = rows * ch + (rows - 1) * SEP
    sheet = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
    cells = []
    for i, f in enumerate(frames):
        rgb = np.asarray(Image.fromarray(f.astype(np.uint8)).resize((cw, ch), Image.LANCZOS)).astype(np.float32)
        rgba = np.dstack([rgb, fm * 255.0]).astype(np.uint8)
        cell = Image.fromarray(rgba, "RGBA")
        cells.append(cell)
        r, c = divmod(i, cols)
        sheet.paste(cell, (c * (cw + SEP), r * (ch + SEP)))
    SPRITES.mkdir(parents=True, exist_ok=True)
    out = SPRITES / f"brainov_{name}.png"
    sheet.save(out)

    # diagnostics (gitignored): a gif + a horizontal contact strip of the feathered cells
    gif = [Image.alpha_composite(Image.new("RGBA", c.size, (20, 20, 24, 255)), c).convert("RGB") for c in cells]
    gif[0].save(HERE / f"_{name}.gif", save_all=True, append_images=gif[1:], duration=90, loop=0)
    entry = {
        "name": name, "sheet": f"GFX/Sprites/brainov_{name}",
        "cols": cols, "rows": rows, "frames": n,
        "fps": region.get("fps", DEFAULT_FPS),
        "texCenterX": (x0 + x1) / 2.0, "texCenterY": (y0 + y1) / 2.0,
        "texW": tw, "texH": th, "cellW": cw, "cellH": ch, "sep": SEP,
        "blend": region.get("blend", "alpha"), "pingpong": True,
    }
    print(f"  {name}: motion {motion:5.2f}  border-drift {drift:5.2f}  "
          f"{'<-- STATIC DUD?' if motion < 0.6 else ('<-- DRIFTS?' if drift > motion * 0.8 else 'ok')}"
          f"  | {n}f grid {cols}x{rows} cell {cw}x{ch} sheet {SW}x{SH} -> {out.name}")
    return entry


def main():
    regions = load_regions()
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--list" in sys.argv:
        print("motion triage (mean inter-frame diff / border drift, 0..255 grey):")
        for name in regions:
            fr = frames_of(name)
            if fr:
                m, d = triage(fr)
                print(f"  {name:16s} frames {len(fr):3d}  motion {m:5.2f}  border-drift {d:5.2f}")
            else:
                print(f"  {name:16s} (no frames)")
        return
    if not args:
        sys.exit("pass region name(s) to pack, or --list to triage. e.g. flesh_breathe pods_flicker")
    DATA.mkdir(parents=True, exist_ok=True)
    manifest = {}
    if MANIFEST.exists():
        manifest = {e["name"]: e for e in json.loads(MANIFEST.read_text(encoding="utf-8"))["overlays"]}
    for name in args:
        if name not in regions:
            print(f"  {name}: not in regions.json - skipped")
            continue
        e = build(name, regions[name])
        if e:
            manifest[name] = e
    MANIFEST.write_text(json.dumps(
        {"_doc": "Live animated overlay patches drawn on the Brain final boss by BrainBoss.Draw. "
                 "texCenter/texW/texH are in brainbosshd texture pixels; the game pins the on-screen "
                 "footprint to that crop so patches pulse with the boss. Built by "
                 "tools/brainanim/build_brain_overlays.py; don't hand-edit.",
         "overlays": list(manifest.values())}, indent=1))
    print(f"-> {MANIFEST.relative_to(REPO)} ({len(manifest)} overlays)")


if __name__ == "__main__":
    main()
