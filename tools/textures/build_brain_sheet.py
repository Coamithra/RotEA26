#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
build_brain_sheet.py - assemble the animated Braineroid sprite sheet (+ its blue
glow) from an AnimGen brain export.

The AnimGen export is N individual frames of the cyborg "space brain" on a solid
magenta backdrop (the colour the art never visits, so the key is clean). This
script:

  1. chroma-keys the magenta backdrop to STRAIGHT (non-premultiplied) alpha, with
     decontamination + edge-bleed (same recipe as tools/chroma_key_title.py), so it
     matches the project's straight-alpha content convention,
  2. crops every frame to ONE fixed box (the union of all frames' content + margin)
     so the brain doesn't jitter from per-frame bbox drift,
  3. DECIMATES to ~half the frames (the card: "probably can use half the fps") -
     the in-game interpolation shader (AlienDrawableGameComponent.drawWithInterpolation
     -> interpolate.fx) cross-fades frame N->N+1, so half the frames still play smooth,
  4. downscales each frame to a cell and PACKS them into a COLSxROWS grid (no
     separator; each brain sits inside transparent cell margin so bilinear/interp
     sampling can't bleed across cells),
  5. builds a single cell-sized BLUE GLOW from the union silhouette (blurred, tinted)
     - drawn additively behind the brain with a subtle pulse, like the BrainBoss aura.

Outputs (committed; capital Content/ root, lowercase under it):
  wwwroot/Content/gfx/sprites/brainanimated.png       (COLSxROWS sheet)
  wwwroot/Content/gfx/sprites/brainanimatedglow.png   (one cell-sized blue glow)

Then run tools/textures/build_textures.py to emit the .rtex siblings (both are
listed 'raw' in textures.config - lossless, the soft glow/edges don't tolerate DXT).
Don't hand-edit the outputs; re-run this script after a new export.

Usage:
  python tools/textures/build_brain_sheet.py [SRC_DIR]
  (SRC_DIR defaults to the export baked in below.)

Requires: Pillow, numpy, scipy.
"""
import os
import sys
import glob
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
CONTENT = os.path.join(REPO, "web", "EvilAliensWeb", "wwwroot", "Content")
SPRITES = os.path.join(CONTENT, "gfx", "sprites")

DEFAULT_SRC = r"C:\Programming\AnimGen\data\exports\brain_20260625_005814_18720b"

# --- layout / sizing knobs (see tracker_new-brain-sprite.md for the scale math) ---
FRAME_STEP = 4          # keep every Nth source frame (4 = quarter the frames, ~20)
COLS, ROWS = 5, 4       # grid => COLS*ROWS cells; must be >= number of kept frames
CELL_W = 512            # cell width in texels. Near the source's native brain detail and
                        # ~1 texel:pixel at the 1440 render cap, so OG-size draw isn't upscaled.
MARGIN = 0.07           # fractional padding added around the union content bbox
# Chroma-key ramp endpoints in "keyness" units (art level -> opaque, bg -> clear).
# This export's magenta backdrop is noisy (keyness ~216-251) and well-separated from
# the art (keyness <= ~215, mostly < 0), so the ramp sits in the gap: keyness >= K_HI
# fully transparent, <= K_LO fully opaque, between = anti-aliased edge.
K_LO, K_HI = 15.0, 215.0
# Glow look. The card asks for a STEEP falloff so the glow reads as a tight blue
# OUTLINE hugging the brain's silhouette (like the original brainlargetransglow),
# not a wide diffuse blob. Two levers: a small blur sigma keeps the halo tight and
# lets it follow the silhouette CONTOUR (a large sigma smears it into a featureless
# ellipse), and a >1 gamma on the normalised alpha crushes the faint gaussian tail so
# the rim drops off sharply instead of hazing far out. The brain is drawn ON TOP of
# the glow, so only the rim that extends past the silhouette is visible = the outline.
GLOW_BLUR_FRAC = 0.03     # gaussian sigma as a fraction of cell width (~15px on a 512 cell)
GLOW_FALLOFF_GAMMA = 1.6  # >1 steepens the outer falloff (1.0 = pure gaussian)
GLOW_RGB = (90, 150, 255)


def detect_bg(a):
    b = np.concatenate([a[:5].reshape(-1, 4), a[-5:].reshape(-1, 4),
                        a[:, :5].reshape(-1, 4), a[:, -5:].reshape(-1, 4)])
    return np.median(b[:, :3], axis=0)


def chroma_key(arr, bg, k_lo, k_hi):
    """arr: HxWx4 uint8 (alpha ignored). Returns HxWx4 uint8 straight-alpha keyed.

    The brain (keyness <~18) and magenta backdrop (keyness >~226) are well separated,
    but this export's backdrop is noisy: flat corners dip into keyness ~170, the same
    band as the brain's anti-aliased edge. A keyness threshold alone can't tell them
    apart, so after the ramp we keep only the brain's connected blob (large solid
    components, dilated to retain the soft edge) and zero isolated backdrop speckles."""
    a = arr.astype(np.float32)
    rgb = a[..., :3]
    thr = bg.max() * 0.5
    hi_ch = [c for c in range(3) if bg[c] > thr]
    lo_ch = [c for c in range(3) if bg[c] <= thr]
    keyness = rgb[..., hi_ch].min(axis=-1) - rgb[..., lo_ch].max(axis=-1)
    bg_cov = np.clip((keyness - k_lo) / (k_hi - k_lo), 0.0, 1.0)
    alpha = 1.0 - bg_cov
    denom = np.maximum(1.0 - bg_cov, 1e-3)[..., None]
    art = (rgb - bg_cov[..., None] * bg) / denom
    visible = alpha[..., None] > 0.0
    rgb = np.where(visible, np.clip(art, 0, 255), rgb)
    a8 = np.clip(alpha * 255.0, 0, 255)

    # keep only the brain: large confidently-solid components, dilated to keep edges.
    solid = a8 > 128
    lbl, n = ndimage.label(solid)
    if n > 0:
        sizes = ndimage.sum(np.ones_like(lbl), lbl, range(1, n + 1))
        big = 1 + np.where(sizes >= 0.005 * a8.size)[0]      # >=0.5% of the frame
        keep = np.isin(lbl, big) if len(big) else (lbl == 1 + int(np.argmax(sizes)))
        keep = ndimage.binary_dilation(keep, iterations=8)   # recover the AA edge
        a8 = np.where(keep, a8, 0.0)

    # edge-bleed nearest art colour into transparent pixels (straight-alpha safe)
    opaque = a8 > 0
    _, (iy, ix) = ndimage.distance_transform_edt(~opaque, return_indices=True)
    rgb = rgb[iy, ix]
    return np.concatenate([rgb, a8[..., None]], axis=-1).astype(np.uint8)


def content_bbox(alpha, thresh=8):
    ys, xs = np.where(alpha > thresh)
    if len(xs) == 0:
        return None
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SRC
    files = sorted(glob.glob(os.path.join(src, "frame_*.png")))
    if not files:
        sys.exit(f"ERROR: no frame_*.png in {src}")
    kept = files[::FRAME_STEP]
    if len(kept) > COLS * ROWS:
        kept = kept[:COLS * ROWS]
    n = len(kept)
    print(f"src={src}\nframes: {len(files)} -> kept {n} (step {FRAME_STEP}); grid {COLS}x{ROWS}")

    # Pass 1: key every kept frame, find the union content bbox (so the crop is fixed).
    keyed = []
    bg = None
    union = None
    for f in kept:
        arr = np.asarray(Image.open(f).convert("RGBA"))
        if bg is None:
            bg = detect_bg(arr)
        k = chroma_key(arr, bg, K_LO, K_HI)
        keyed.append(k)
        bb = content_bbox(k[..., 3])
        if bb is None:
            continue
        if union is None:
            union = list(bb)
        else:
            union[0] = min(union[0], bb[0]); union[1] = min(union[1], bb[1])
            union[2] = max(union[2], bb[2]); union[3] = max(union[3], bb[3])
    if union is None:
        sys.exit("ERROR: no content found after keying (check K_LO/K_HI vs the backdrop)")
    H, W = keyed[0].shape[:2]
    x0, y0, x1, y1 = union
    bw, bh = x1 - x0, y1 - y0
    mx, my = int(bw * MARGIN), int(bh * MARGIN)
    x0 = max(0, x0 - mx); y0 = max(0, y0 - my)
    x1 = min(W, x1 + mx); y1 = min(H, y1 + my)
    cw, ch = x1 - x0, y1 - y0
    print(f"bg={bg.astype(int)}  union bbox={union} -> crop {x0,y0,x1,y1} = {cw}x{ch} (aspect {cw/ch:.3f})")

    cell_w = CELL_W
    cell_h = int(round(CELL_W * ch / cw))
    cell_h += cell_h % 2  # keep even
    print(f"cell {cell_w}x{cell_h}  ->  sheet {COLS*cell_w}x{ROWS*cell_h}")

    # Pass 2: crop -> resize -> paste into the grid; accumulate a union silhouette.
    sheet = Image.new("RGBA", (COLS * cell_w, ROWS * cell_h), (0, 0, 0, 0))
    silhouette = np.zeros((cell_h, cell_w), np.float32)
    for i, k in enumerate(keyed):
        cell = Image.fromarray(k[y0:y1, x0:x1], "RGBA").resize((cell_w, cell_h), Image.LANCZOS)
        r, c = divmod(i, COLS)
        sheet.paste(cell, (c * cell_w, r * cell_h))
        silhouette = np.maximum(silhouette, np.asarray(cell)[..., 3].astype(np.float32))

    os.makedirs(SPRITES, exist_ok=True)
    out_sheet = os.path.join(SPRITES, "brainanimated.png")
    sheet.save(out_sheet)
    print(f"-> {os.path.relpath(out_sheet, REPO)}  {sheet.size}")

    # Glow: PAD the silhouette before blurring so the soft falloff fully decays to
    # transparent before the texture edge (otherwise the blur is clipped to a hard
    # rectangle and the glow looks cut off). The silhouette stays cell-aligned and
    # centred, so in-game the glow drawn at the brain's DrawScale lines up, with the
    # padding becoming the halo that extends past the brain.
    sigma = cell_w * GLOW_BLUR_FRAC
    pad = int(np.ceil(sigma * 3.5))
    glow_a = ndimage.gaussian_filter(np.pad(silhouette, pad), sigma)
    if glow_a.max() > 0:
        glow_a = glow_a / glow_a.max()           # normalise to 0..1
        glow_a = np.power(glow_a, GLOW_FALLOFF_GAMMA) * 255.0  # steepen the falloff
    gh, gw = glow_a.shape
    glow = np.zeros((gh, gw, 4), np.uint8)
    glow[..., 0], glow[..., 1], glow[..., 2] = GLOW_RGB
    glow[..., 3] = np.clip(glow_a, 0, 255).astype(np.uint8)
    out_glow = os.path.join(SPRITES, "brainanimatedglow.png")
    Image.fromarray(glow, "RGBA").save(out_glow)
    print(f"-> {os.path.relpath(out_glow, REPO)}  ({gw}x{gh}, blue glow, pad {pad})")
    print("done. Now run: python tools/textures/build_textures.py")


if __name__ == "__main__":
    main()
