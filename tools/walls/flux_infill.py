#!/usr/bin/env python
"""One-shot INFILL make-tileable for `GFX/Base/756-v1` using a LOCAL Flux Fill model.

This is the model-driven alternative to `build_wall_tileable.py`'s offline BLEND: it offsets
the upscale so the wrap seam is a centre cross, has **Flux Fill** regenerate coherent detail
across that seam, then composites the fill INSIDE THE MASK ONLY over the original offset so the
wrap borders stay pixel-exact (guaranteed tiling). Produces a candidate you can A/B against the
blend (`preview_infill_756-v1.png` vs `preview_blend_756-v1.png`).

  emit seam+mask  ->  FluxFillPipeline(image, mask_image, prompt)  ->  composite in-mask  ->  install

NOT RUN / NOT VERIFIED in this repo -- it needs a GPU + the gated `black-forest-labs/FLUX.1-Fill-dev`
weights, which aren't available here. The seam/composite/install plumbing it calls IS verified
(shared with `build_wall_tileable.py`); only the FluxFillPipeline call is unproven. Treat the
pipeline params (steps/guidance/dtype/offload) as a starting point and tune for your box. Any
other local inpainter works too -- use `build_wall_tileable.py --emit-seam` / `--reimport` and
run your model in between; this file is just the Flux convenience wrapper.

Setup (one time):
    pip install "diffusers>=0.32" transformers accelerate torch sentencepiece protobuf
    # FLUX.1-Fill-dev is gated: accept the licence on HF, then `huggingface-cli login`.

Usage:
    python tools/walls/flux_infill.py                      # source/756-v1.png -> content path
    python tools/walls/flux_infill.py --size 1024 --steps 50 --guidance 30 --seed 0
    python tools/walls/flux_infill.py --prompt "seamless alien base metal wall, same material"
    python tools/walls/flux_infill.py --check-only         # don't write the content file
"""
import argparse
import importlib.util
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
# reuse the verified seam/composite/install helpers
_spec = importlib.util.spec_from_file_location("bwt", os.path.join(HERE, "build_wall_tileable.py"))
bwt = importlib.util.module_from_spec(_spec)  # type: ignore
_spec.loader.exec_module(bwt)  # type: ignore

DEFAULT_PROMPT = ("seamless tileable alien-base wall texture, continuous surface, same material "
                  "and lighting as the surroundings, no seams, no borders")


def crop_mult(im, n):
    """Center-crop to the largest w/h that is a multiple of n (Flux latents want mult-of-16)."""
    w, h = im.size
    w2, h2 = w - w % n, h - h % n
    if (w2, h2) == (w, h):
        return im
    left, top = (w - w2) // 2, (h - h2) // 2
    return im.crop((left, top, left + w2, top + h2))


def run_flux(offset_u8, mask_u8, prompt, steps, guidance, seed, cpu_offload):
    try:
        import torch
        from diffusers import FluxFillPipeline  # type: ignore[attr-defined]
    except Exception as e:  # noqa: BLE001
        sys.exit(f"Flux deps not available ({e}).\n"
                 'Install: pip install "diffusers>=0.32" transformers accelerate torch '
                 "sentencepiece protobuf ; then accept FLUX.1-Fill-dev on HF + huggingface-cli login.")
    H, W = offset_u8.shape[:2]
    pipe = FluxFillPipeline.from_pretrained(
        "black-forest-labs/FLUX.1-Fill-dev", torch_dtype=torch.bfloat16)
    if cpu_offload:
        pipe.enable_model_cpu_offload()
    else:
        pipe.to("cuda")
    out = pipe(  # type: ignore[operator]
        prompt=prompt,
        image=Image.fromarray(offset_u8, "RGB"),
        mask_image=Image.fromarray(mask_u8, "L"),
        height=H, width=W,
        guidance_scale=guidance,
        num_inference_steps=steps,
        max_sequence_length=512,
        generator=torch.Generator("cpu").manual_seed(seed),
    )
    return np.asarray(out.images[0].convert("RGB")).astype(np.float32)  # type: ignore[union-attr]


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--in", dest="inp", default=bwt.DEFAULT_IN, help="source PNG (upscaled art)")
    ap.add_argument("--out", default=bwt.DEFAULT_OUT, help="destination content PNG")
    ap.add_argument("--size", type=int, default=0, help="resize to SIZE x SIZE first (0 = keep)")
    ap.add_argument("--seam-frac", type=float, default=0.16, help="seam-band width frac of min(W,H)")
    ap.add_argument("--feather", type=float, default=6.0, help="fill<->original handoff feather px")
    ap.add_argument("--prompt", default=DEFAULT_PROMPT)
    ap.add_argument("--steps", type=int, default=50)
    ap.add_argument("--guidance", type=float, default=30.0, help="Flux Fill wants a high guidance (~30)")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--cpu-offload", action="store_true",
                    help="enable_model_cpu_offload (lower VRAM, slower) instead of .to('cuda')")
    ap.add_argument("--check-only", action="store_true", help="don't write the content file")
    args = ap.parse_args()

    if not os.path.exists(args.inp):
        sys.exit(f"source not found: {args.inp}\nDrop your upscaled 756-v1.png there, or --in <path>.")

    im = Image.open(args.inp).convert("RGB")
    ow, oh = im.size
    if args.size:
        im = im.resize((args.size, args.size), bwt.RESAMPLE_LANCZOS)
    im = crop_mult(im, 16)  # Flux latents want mult-of-16 (also a valid mult-of-8 for the game)
    W, H = im.size
    rgb = np.asarray(im).astype(np.float32)

    offset, mask = bwt.seam_offset_and_mask(rgb, seam_frac=args.seam_frac)
    print(f"in  : {args.inp} ({ow}x{oh}) -> proc {W}x{H}; running Flux Fill ({args.steps} steps)...")
    result = run_flux(offset.astype(np.uint8), mask, args.prompt, args.steps,
                      args.guidance, args.seed, args.cpu_offload)

    out_rgb = bwt.composite_infill(offset, result, mask, feather_px=args.feather)
    bwt.report_seam(offset, out_rgb, "flux")

    if args.check_only:
        out_im = Image.fromarray(
            np.dstack([out_rgb, np.full((H, W), 255.0, np.float32)]).astype(np.uint8), "RGBA")
        bwt.write_preview(out_im, bwt.PREVIEW_INFILL)
        print(f"preview: {bwt.PREVIEW_INFILL}  (2x2 tiling)\n(no content file written)")
        return
    out_im = bwt.install(out_rgb, np.full((H, W), 255.0, np.float32), args.out, keep_alpha=False)
    bwt.write_preview(out_im, bwt.PREVIEW_INFILL)
    print(f"preview: {bwt.PREVIEW_INFILL}  (2x2 tiling)\nwrote: {args.out}")


if __name__ == "__main__":
    main()
