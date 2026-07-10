#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_brain_overlays.py - triage the Wan i2v takes from gen_brain_anims.py and pack
the chosen ones into feathered sprite sheets + a manifest the Brain boss draws.

For each named region it:
  1. loads the extracted frames (new_assets_raw/brainanim/<name>/frames),
  2. TRIAGES motion (mean inter-frame diff + a border-drift metric) so a static-image
     dud can be spotted/rejected,
  2b. STABILIZES: fits and undoes the whole-frame camera move (zoom + drift) Wan invents
     no matter what the prompt says, locking every frame to frame 0 = the untouched crop,
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
  ... --drop <name>          # remove an overlay from the manifest + delete its sheet
  ... --sync                 # only re-sync playback knobs from regions.json (no repack)

PLAYBACK knobs (fps, blend, triggerAvgSeconds) live in regions.json and are re-synced into
every EXISTING manifest entry on each run, so they retune without re-rendering the raw
frames -- which are gitignored, so a later session usually no longer has them.
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
PRELOAD = REPO / "web/EvilAliensWeb/wwwroot/Content/preload/manifest.txt"
BRAIN_W, BRAIN_H = 1448, 1086            # brainbosshd.png dims (texture space)

# --- packing / look knobs ---
KEEP_FRAMES = 17        # decimate to ~this many (the interpolation shader smooths low fps)
CELL_W = 224            # cell width in texels (on-screen size is pinned by texW, not this)
FEATHER = 0.20          # edge feather as a fraction of the smaller cell dim (dissolve into art)
DEFAULT_FPS = 10.0      # playback fps (ping-ponged in game); mech faster, flesh slower via regions
SEP = 1                 # transparent cell gutter so interpolation can't bleed across cells
STAB_BAND = 0.15        # outer band fraction the camera fit is scored on (see stabilize)
STAB_DEADZONE_ZOOM = 0.003   # |s-1| below this AND shift below the px deadzone -> don't warp
STAB_DEADZONE_PX = 0.4       # a fit this small is noise; warping would only soften the frame


def load_regions():
    return {r["name"]: r for r in
            json.loads((HERE / "regions.json").read_text(encoding="utf-8"))["regions"]}


def apply_knobs(entry, region):
    """Copy the PLAYBACK knobs from a region onto a manifest entry. These don't depend on
    the packed pixels, so they stay retunable long after the raw frames are gone. Each knob
    is popped then re-inserted so the key ORDER is identical whether the entry came from a
    fresh build() or a --sync of an old one -- otherwise the two paths churn the committed
    JSON diff."""
    entry.pop("fps", None)
    entry["fps"] = region.get("fps", DEFAULT_FPS)
    entry.pop("blend", None)
    entry["blend"] = region.get("blend", "alpha")
    entry.pop("triggerAvgSeconds", None)
    trigger = region.get("triggerAvgSeconds")
    if trigger:
        entry["triggerAvgSeconds"] = float(trigger)   # rest on frame 0, play ~every N s
    entry.pop("interpolate", None)
    if region.get("interpolate") is False:
        entry["interpolate"] = False                  # draw nearest frame, no cross-fade
    return entry


def load_manifest():
    if not MANIFEST.exists():
        return {}
    return {e["name"]: e for e in json.loads(MANIFEST.read_text(encoding="utf-8"))["overlays"]}


def drop_from_preload(name):
    """Strip every `<Level>|gfx/sprites/brainov_<name>` line from the level preload manifest
    so --drop leaves no dangling texture load (ApplyManifest would log a failed load each
    boss level otherwise). Returns how many lines were removed."""
    if not PRELOAD.exists():
        return 0
    asset = f"gfx/sprites/brainov_{name}"
    lines = PRELOAD.read_text(encoding="utf-8").splitlines(keepends=True)
    kept = [ln for ln in lines if ln.rstrip("\r\n").split("|")[-1] != asset]
    if len(kept) != len(lines):
        PRELOAD.write_text("".join(kept), encoding="utf-8", newline="")
    return len(lines) - len(kept)


def write_manifest(manifest, regions):
    for name, entry in manifest.items():
        if name in regions:
            apply_knobs(entry, regions[name])
    DATA.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps(
        {"_doc": "Live animated overlay patches drawn on the Brain final boss by BrainBoss.Draw. "
                 "texCenter/texW/texH are in brainbosshd texture pixels; the game pins the on-screen "
                 "footprint to that crop so patches pulse with the boss. triggerAvgSeconds (optional) "
                 "makes a patch rest on frame 0 and play one cycle every ~N seconds instead of looping. "
                 "interpolate:false (optional) draws the nearest frame with no cross-fade shader. "
                 "Built by tools/brainanim/build_brain_overlays.py; don't hand-edit.",
         "overlays": list(manifest.values())}, indent=1))
    print(f"-> {MANIFEST.relative_to(REPO)} ({len(manifest)} overlays)")


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


def _bilinear(img, sy, sx):
    """Sample img (HxW or HxWxC) at float coords, clamped at the edges."""
    h, w = img.shape[:2]
    x0 = np.clip(np.floor(sx), 0, w - 2).astype(int)
    y0 = np.clip(np.floor(sy), 0, h - 2).astype(int)
    fx = np.clip(sx - x0, 0, 1)
    fy = np.clip(sy - y0, 0, 1)
    if img.ndim == 3:
        fx, fy = fx[..., None], fy[..., None]
    return (img[y0, x0] * (1 - fx) * (1 - fy) + img[y0, x0 + 1] * fx * (1 - fy)
            + img[y0 + 1, x0] * (1 - fx) * fy + img[y0 + 1, x0 + 1] * fx * fy)


def _warp(img, s, dx, dy):
    """Undo a uniform zoom s + translation (dx,dy) about the frame centre."""
    h, w = img.shape[:2]
    cy, cx = (h - 1) / 2.0, (w - 1) / 2.0
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return _bilinear(img, (yy - cy) / s + cy + dy, (xx - cx) / s + cx + dx)


def _fit_camera(ref_g, frame_g):
    """Best (scale, dx, dy) taking frame -> ref, scored on the OUTER BAND only: the
    interior is where the intended animation lives (flickering lights), so it would
    dominate the error and mask the camera move we're hunting. Coarse pass on a 1/4
    pyramid level, then refine at full res."""
    h, w = ref_g.shape
    b = max(3, int(min(h, w) * STAB_BAND))
    mask = np.ones((h, w), bool)
    mask[b:-b, b:-b] = False

    small_ref = np.asarray(Image.fromarray(ref_g).resize((w // 4, h // 4), Image.BILINEAR))
    small_frm = np.asarray(Image.fromarray(frame_g).resize((w // 4, h // 4), Image.BILINEAR))
    sh, sw = small_ref.shape
    sb = max(2, int(min(sh, sw) * STAB_BAND))
    smask = np.ones((sh, sw), bool)
    smask[sb:-sb, sb:-sb] = False

    def score(img, s, dx, dy, ref, m):
        d = _warp(img, s, dx, dy)[m] - ref[m]
        return float(np.mean(d * d))

    best = (1e18, 1.0, 0.0, 0.0)
    for s in np.arange(0.86, 1.1401, 0.005):
        for dx in np.arange(-2.5, 2.51, 0.5):
            for dy in np.arange(-2.5, 2.51, 0.5):
                e = score(small_frm, s, dx, dy, small_ref, smask)
                if e < best[0]:
                    best = (e, s, dx, dy)
    _, s, dx, dy = best
    dx, dy = dx * 4, dy * 4                       # pyramid level -> full-res pixels
    best = (score(frame_g, s, dx, dy, ref_g, mask), s, dx, dy)
    for ss in np.arange(s - 0.006, s + 0.0061, 0.001):
        for ddx in np.arange(dx - 1.5, dx + 1.51, 0.25):
            for ddy in np.arange(dy - 1.5, dy + 1.51, 0.25):
                e = score(frame_g, ss, ddx, ddy, ref_g, mask)
                if e < best[0]:
                    best = (e, ss, ddx, ddy)
    return best[1:]


def stabilize(frames):
    """Lock every frame to frame 0's framing.

    Wan invents a slow camera move (a push-in, plus a little drift) no matter how hard the
    prompt forbids it -- and because each patch is composited over the STATIC brain, that
    whole-frame motion is the one artifact the pipeline can't tolerate: the patch visibly
    slides against the art around it. So we measure the move and undo it, exactly as
    colour_match undoes the VAE's colour drift. Frame 0 is the untouched crop, so locking
    to it also guarantees the resting pose matches the sprite underneath.

    A fit within the deadzone (essentially identity) leaves the frame UNTOUCHED: the SSD
    minimum on a genuinely locked-off take can sit at a spurious sub-pixel warp, and warping
    to it would only add bilinear softening where there was no camera move to remove.

    Returns (frames, max_zoom_pct, max_shift_px) so build() can report what it removed."""
    if len(frames) < 2:
        return frames, 0.0, 0.0
    ref_g = frames[0].mean(axis=2).astype(np.float32)
    out = [frames[0]]
    zooms, shifts = [], []
    for f in frames[1:]:
        s, dx, dy = _fit_camera(ref_g, f.mean(axis=2).astype(np.float32))
        zoom, shift = abs(s - 1.0), math.hypot(dx, dy)
        if zoom < STAB_DEADZONE_ZOOM and shift < STAB_DEADZONE_PX:
            out.append(f)                            # fit is noise; leave the frame alone
            continue
        zooms.append(zoom)
        shifts.append(shift)
        out.append(np.clip(_warp(f, s, dx, dy), 0, 255))
    return out, 100.0 * max(zooms, default=0.0), max(shifts, default=0.0)


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
    motion, _ = triage(frames)   # full-rate motion, for the STATIC-DUD screen only
    x0, y0, x1, y1 = region["box"]
    brain = np.asarray(Image.open(
        REPO / "web/EvilAliensWeb/wwwroot/Content/gfx/sprites/brainbosshd.png"
    ).convert("RGBA"))[y0:y1, x0:x1]
    crop_rgb = brain[..., :3]
    crop_alpha = brain[..., 3]           # brain silhouette in this crop

    frames = decimate(frames, KEEP_FRAMES)
    # Compare drift BEFORE vs AFTER on the same (decimated) frame set -- decimation alone
    # roughly doubles every inter-frame diff, so the full-rate `motion` above is not a
    # valid baseline for the post-stabilisation number.
    pre_motion, pre_drift = triage(frames)
    frames, zoom_pct, shift_px = stabilize(frames)
    post_motion, post_drift = triage(frames)
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
    entry = apply_knobs({
        "name": name, "sheet": f"GFX/Sprites/brainov_{name}",
        "cols": cols, "rows": rows, "frames": n,
        "texCenterX": (x0 + x1) / 2.0, "texCenterY": (y0 + y1) / 2.0,
        "texW": tw, "texH": th, "cellW": cw, "cellH": ch, "sep": SEP,
        "pingpong": True,
    }, region)
    print(f"  {name}: motion {motion:5.2f} {'<-- STATIC DUD?' if motion < 0.6 else 'ok'}"
          f"  | stabilised out {zoom_pct:.1f}% zoom / {shift_px:.1f}px shift:"
          f" border-drift {pre_drift:5.2f} -> {post_drift:5.2f}"
          f", motion {pre_motion:5.2f} -> {post_motion:5.2f}"
          f"  | {n}f grid {cols}x{rows} cell {cw}x{ch} sheet {SW}x{SH} -> {out.name}")
    return entry


def main():
    regions = load_regions()
    argv = sys.argv[1:]
    args = [a for a in argv if not a.startswith("--")]
    flags = {a for a in argv if a.startswith("--")}
    if "--list" in flags:
        print("motion triage (mean inter-frame diff / border drift, 0..255 grey):")
        for name in regions:
            fr = frames_of(name)
            if fr:
                m, d = triage(fr)
                print(f"  {name:16s} frames {len(fr):3d}  motion {m:5.2f}  border-drift {d:5.2f}")
            else:
                print(f"  {name:16s} (no frames)")
        return
    if "--drop" in flags:
        if not args:
            sys.exit("--drop needs the overlay name(s) to remove")
        manifest = load_manifest()
        for name in args:
            if manifest.pop(name, None) is None:
                print(f"  {name}: not in the manifest - nothing to drop")
                continue
            sheet = SPRITES / f"brainov_{name}.png"
            sheet.unlink(missing_ok=True)
            removed = drop_from_preload(name)
            print(f"  {name}: dropped (removed {sheet.name}"
                  f"{f', {removed} preload line(s)' if removed else ''})")
        write_manifest(manifest, regions)
        return
    if "--sync" in flags:
        write_manifest(load_manifest(), regions)
        return
    if not args:
        sys.exit("pass region name(s) to pack, or --list / --drop <name> / --sync. "
                 "e.g. flesh_breathe pods_flicker")
    manifest = load_manifest()
    for name in args:
        if name not in regions:
            print(f"  {name}: not in regions.json - skipped")
            continue
        e = build(name, regions[name])
        if e:
            manifest[name] = e
    write_manifest(manifest, regions)


if __name__ == "__main__":
    main()
