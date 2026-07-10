#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
gen_brain_anims.py - crop regions of the Brain final boss and animate each with
the LOCAL Wan 2.2 14B Lightning i2v model, driving the ../animgen ComfyUI plumbing.

For every region in regions.json this:
  1. crops brainbosshd.png to the region box (texture pixels),
  2. runs it through comfy_client.generate() as an OPEN-ENDED i2v continuation
     (start frame = the crop, no end frame -> the FLF template degrades to I2V),
  3. extracts the produced mp4 to individual frames.

Raw work (crop, mp4, frames) lands in new_assets_raw/brainanim/<name>/ (gitignored).
Triage + packing into game sheets is build_brain_overlays.py.

Run with the AnimGen venv (has PyAV + the backends):
  C:/Programming/animgen/.venv/Scripts/python.exe tools/brainanim/gen_brain_anims.py [name ...]

ComfyUI is auto-launched (ensure_server) with the safe TDR flags; the Wan lightning
LoRAs must be present under comfyui/models/loras (they are, per model_library notes).
Pass region names to render a subset; no args = all regions.
"""
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent


def _find_animgen():
    if os.environ.get("ANIMGEN_ROOT"):
        return Path(os.environ["ANIMGEN_ROOT"])
    # Search up from the repo for a sibling 'animgen' (worktrees nest the checkout
    # deep, so REPO.parent isn't C:/Programming); fall back to the known sibling.
    for base in [REPO, *REPO.parents]:
        cand = base.parent / "animgen"
        if (cand / "model_library.json").exists():
            return cand
    return Path("C:/Programming/animgen")


ANIMGEN = _find_animgen()
sys.path.insert(0, str(ANIMGEN))

from backends import comfy_client  # noqa: E402
from pipeline.extract import extract_frames  # noqa: E402

BRAIN_PNG = REPO / "web/EvilAliensWeb/wwwroot/Content/gfx/sprites/brainbosshd.png"
WORK = REPO / "new_assets_raw/brainanim"
MODEL_ID = "local-wan14b-lightning"
LENGTH = 33          # i2v frame count (4k+1); interpolated/ping-ponged in game
LONG_SIDE = 448      # gen long-side px (mult of 16); Wan sweet spot, fast enough

# Every patch is composited over the STATIC brain sprite, so whole-frame camera motion the
# model invents (drift, a slow push-in) reads as the patch sliding against the art around
# it -- the one artifact this pipeline cannot tolerate.
#
# We MUST pass a negative, because the shared workflow template's baked-in one was written
# for animgen's fighting-game character and ends with "frozen, still image, static pose".
# On a locked-off shot of a barely-moving pod cluster that term fights the whole point: the
# cheapest way for the model to avoid "still image" is to move the entire frame, so it
# invents a zoom. The positive prompt's "locked static camera" loses that argument -- which
# is why saying it louder there never worked. Note what is deliberately ABSENT below.
#
# Per-region "negative" overrides this; build_brain_overlays.py's border-drift triage (and
# a scale fit against frame 0) is what confirms it took.
DEFAULT_NEGATIVE = (
    "camera movement, camera motion, camera pan, camera zoom, zoom in, zoom out, dolly, "
    "push in, pull out, tilt, tracking shot, handheld, camera shake, drifting frame, "
    "rotating camera, scale change, the whole frame moving, cropping, letterboxing, "
    "scene change, background change, "
    "realistic, photorealistic, 3D render, motion blur, blurry, low quality, text, watermark"
)


def load_model():
    lib = json.loads((ANIMGEN / "model_library.json").read_text(encoding="utf-8"))
    def walk(o):
        if isinstance(o, dict):
            if o.get("id") == MODEL_ID:
                return o
            for v in o.values():
                r = walk(v)
                if r:
                    return r
        if isinstance(o, list):
            for v in o:
                r = walk(v)
                if r:
                    return r
    m = walk(lib)
    if not m:
        sys.exit(f"model {MODEL_ID} not in model_library.json")
    return m


def gen_size(w, h):
    """Crop (w,h) -> generation (gw,gh): long side LONG_SIDE, both mult of 16."""
    if w >= h:
        gw = LONG_SIDE
        gh = max(256, round(LONG_SIDE * h / w / 16) * 16)
    else:
        gh = LONG_SIDE
        gw = max(256, round(LONG_SIDE * w / h / 16) * 16)
    return gw, gh


def main():
    from PIL import Image
    model = load_model()
    template = str(ANIMGEN / model["workflow_template"])
    roles = model["comfy_nodes"]
    regions = json.loads((HERE / "regions.json").read_text(encoding="utf-8"))["regions"]
    want = set(sys.argv[1:])
    if want:
        regions = [r for r in regions if r["name"] in want]
        if not regions:
            sys.exit(f"no regions match {want}")

    brain = Image.open(BRAIN_PNG).convert("RGBA")
    print(f"brain {brain.size}; rendering {len(regions)} region(s) with {MODEL_ID} "
          f"(len {LENGTH}); launching ComfyUI if needed...")
    comfy_client.ensure_server(progress_cb=lambda m: print("  [comfy]", m))

    for r in regions:
        name = r["name"]
        x0, y0, x1, y1 = r["box"]
        # 373 = the HARD cutoff (rows above it are off the top of the screen at fight scale
        # 1.0); regions.json keeps ty0 >= ~400 for a safety margin against pulsation.
        assert y0 >= 373, f"{name}: box top {y0} is above the on-screen cutoff (373)!"
        outdir = WORK / name
        outdir.mkdir(parents=True, exist_ok=True)
        crop = brain.crop((x0, y0, x1, y1))
        crop_path = outdir / "crop.png"
        crop.convert("RGB").save(crop_path)   # solid RGB start frame for i2v
        gw, gh = gen_size(*crop.size)
        mp4 = outdir / "anim.mp4"
        print(f"\n=== {name}  box {r['box']}  crop {crop.size} -> gen {gw}x{gh} ===")
        comfy_client.generate(
            template, mp4,
            start=str(crop_path),
            prompt=r["prompt"],
            negative=r.get("negative", DEFAULT_NEGATIVE),
            seed=r["seed"],
            node_roles=roles,
            sets={"11.width": gw, "11.height": gh, "11.length": LENGTH},
            text_encoder_cpu=True,
            progress_cb=lambda m: print("   ", m if isinstance(m, str) else ""),
        )
        frames = extract_frames(mp4, outdir / "frames")
        print(f"    -> {mp4.name} ({len(frames)} frames) in {outdir}")

    print("\nall done. Next: triage + pack with build_brain_overlays.py")


if __name__ == "__main__":
    main()
