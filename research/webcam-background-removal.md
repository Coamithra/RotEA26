# Research: High-quality real-time browser background removal (closing the Zoom gap)

*2026-07-05 — for the "I Made This!" webcam challenge (`wwwroot/webcam.js` + `webcam-worker.js`).
Implemented outcome: commit "Refine the webcam background-removal mask (Meet-style post-processing)"
on `human_tweaks`.*

## Questions

- **Q1:** What is the 2025–2026 state of the art for real-time, in-browser person
  segmentation / background removal (models + runtimes)?
- **Q2:** What is publicly known about how Zoom/Teams/Google Meet achieve their edge quality?
- **Q3:** Which post-processing tricks apply to our MediaPipe tasks-vision worker pipeline?

**Out of scope:** server-side/native SDKs, green-screen workflows, licensing deep-dives.

## Findings

### 1. The quality gap is mostly post-processing, not the model

Google Meet's published pipeline runs a small MobileNetV3-class segmenter — the same class as
our vendored `selfie_segmenter.tflite` — and gets its look from the refinement stages:

- "Modified MobileNetV3-small as the encoder, which has been tuned by network architecture
  search for the best performance with low resource requirements." [S1]
- "In the refinement stage, we apply a joint bilateral filter to smooth the low resolution
  mask." [S1]
- "For background replacement, we adopt a compositing technique, known as light wrapping, for
  blending segmented persons and customized background images." [S1]
- MediaPipe's own docs recommend the same refinement for our exact model: "To improve
  segmentation around boundaries, consider applying a joint bilateral filter to
  'results.segmentation_mask' with 'image'." [S3]

Flicker is a known artifact with a known cheap fix:

- "Small variations in inference (body movements, due to lighting, or model uncertainty)
  manifest as visible jitter when composited" — and "implementation of lightweight temporal
  smoothing by blending the current frame's alpha mask with the previous frame's mask would
  reduce the flickering". [S5c]
- "A low-overhead approach to include temporal information is to compute an exponential moving
  average (EMA) between the current and past predictions." [S8]

Other production pipelines confirm the same recipe:

- Slack Clips: "we apply a weighted blur to the mask itself, where the weight is an inverted
  parabola centered around 0.5" (feather only the uncertain band); "we also weight each sample
  by the mask value at the sampled location. This prevents pixels outside the mask from
  sampling pixels inside the mask" (halo prevention). [S5b]
- The open-source Volcomix `virtual-background` project reimplements the Meet pipeline:
  "Joint bilateral filter to smooth the segmentation mask and to preserve edges from the
  original input frame", plus "blending background image with light wrapping", all as WebGL2
  shaders. [S5a]

Meet's 2022 upgrade additionally moved to an HD-input model with an MLP decoder — "crisper
boundaries around hair and fingers", "+3% mean IoU" — running on WebGL with an
EfficientNet-Lite backbone. [S2] That model is **not public**; the public `selfie_segmenter`
stays 256×256 internally, and feeding larger frames only returns an internally-upsampled mask
(output mask matches input frame size via upsampling). [S9]

### 2. Alternative models are not a practical win for us today

- **RVM (Robust Video Matting)** — true alpha matting with recurrent temporal state; "4K 76FPS
  and HD 104FPS on an Nvidia GTX 1080 Ti GPU"; TF.js/ONNX exports and a browser demo exist,
  but **no published browser FPS**, and it is a much heavier vendored payload. [S10]
- **MODNet** — "real-time portrait matting with only RGB image input", ~7 MB ONNX; browser
  video FPS unverified. [S11]
- **RMBG-2.0 / BiRefNet** — "The model is not currently supported in-browser due to a bug in
  onnxruntime-web" (OOM); not real-time video. [S12]
- **BEN2-ONNX** — runs via Transformers.js but is ~223 MB; real-time video unconfirmed. [S13]
- **selfie_multiclass_256x256** (hair/skin/clothes channels) — ~2× GPU latency vs the selfie
  segmenter (71.24 ms vs 35.15 ms GPU on Pixel 6) and an open iOS-Safari GPU bug where
  "segmentation output categories become scrambled". [S14][S15]
- **onnxruntime-web WebGPU** is production-ready in general ("20x speedup over multi-threaded
  ... CPU", "consecutive runs will be ~100 ms") [S16][S17] — i.e. ~10 FPS for the big matting
  models: fine for photos, not our 30 fps game overlay.
- Our vendored `@mediapipe/tasks-vision` 0.10.14 is ~40 versions behind (0.10.35, Apr 2026),
  which includes "Add missing GL memory barrier in TensorsToSegmentationGlBufferConverter"
  (GPU-mask sync fix). WebGPU inference for vision tasks is still **not shipped** (issue #5826
  open, "awaiting googler"). [S18][S19]

**Alpha matting vs segmentation** is the real ceiling: "Unlike segmentation masks, alpha
mattes are usually extremely precise, preserving strand-level hair details and accurate
foreground boundaries." [S20] Getting there in-browser today means RVM-class models with the
caveats above — a follow-up experiment, not a drop-in.

### 3. Our pipeline's specific losses (local code audit, pre-change)

1. **No temporal smoothing** — raw per-frame confidence → edge shimmer/flicker (visual AND the
   40×30 gameplay hitbox grid).
2. **No edge refinement** — fixed 0.35–0.65 alpha ramp on a 320×240 mask, bilinearly stretched.
3. **Resolution waste** — the overlay canvas was a fixed 800×600 backing store CSS-stretched to
   the letterbox: the camera image (960×720 usable after 4:3 cover-crop of 720p) was downscaled
   then re-upscaled before the mask even mattered.

## Implemented (commit on `human_tweaks`)

1. **Adaptive temporal EMA** on the confidence mask in the worker — per-pixel blend weight
   scales with |delta| (`EMA_MIN 0.35` … `EMA_MAX 0.95`): stable pixels smooth hard, moving
   pixels snap (no ghost trails). Grid/hitbox built from the smoothed mask.
2. **Band-limited joint bilateral filter** on the uncertain edge pixels (`0.03 < conf < 0.97`),
   guided by the camera frame RGB at proc res (OffscreenCanvas readback; gracefully skipped if
   unavailable). 5×5 window, precomputed spatial kernel + colour-distance LUT (~1–2 ms at
   320×240). Skipped wholesale if the band exceeds 30% of the frame (degenerate frames).
3. **Native-resolution overlay** — backing store sized to the letterbox's device pixels,
   capped `OVERLAY_MAX_W = 1280` (~a 720p camera's 4:3 crop), `imageSmoothingQuality = "high"`
   on the composite.

**Deliberately not done:**

- **Light wrapping** — needs to sample the background behind the person; our "background" is
  the live game framebuffer under a transparent DOM canvas, which JS can't sample cheaply.
- **Model swap** (RVM/MODNet/multiclass) — unverified browser FPS / heavier payload / iOS bug,
  per §2.
- **tasks-vision 0.10.14 → 0.10.35 vendor bump** — worthwhile mechanical follow-up (GL
  memory-barrier fix for GPU masks), separate change: bump the npm tarball + re-copy the four
  vendored files per `lib/mediapipe/README.md`, then re-test the level.

## Gaps and uncertainties

- Browser FPS for RVM/MODNet is undocumented — would need an actual spike to rule in/out.
- Whether 0.10.35's smoothing-calculator changes extend to segmentation (landmarks are the
  documented case) is unconfirmed.
- Zoom's and Teams' exact pipelines are proprietary (Teams: CNN + teacher/student distillation
  is documented only at a high level; Zoom: no public engineering material found).
- The implemented EMA/JBF constants are first-pass values, tuned by reasoning not by eye —
  webcam eyeball pass pending.

## Sources

- [S1] https://research.google/blog/background-features-in-google-meet-powered-by-web-ml/ — Google Meet 2020 pipeline (model, WASM SIMD, joint bilateral refinement, light wrapping)
- [S2] https://research.google/blog/high-definition-segmentation-in-google-meet/ — Meet 2022 HD segmentation (MLP decoder, WebGL, EfficientNet-Lite)
- [S3] https://github.com/google-ai-edge/mediapipe/blob/master/docs/solutions/selfie_segmentation.md — official model docs + joint-bilateral recommendation
- [S5a] https://github.com/Volcomix/virtual-background — open-source Meet-style pipeline (WebGL2 JBF + light wrap)
- [S5b] https://slack.engineering/building-background-effects-for-clips/ — Slack Clips mask refinement (weighted blur, halo prevention)
- [S5c] https://github.com/jitsi/jitsi-meet/issues/17080 — mask flicker + temporal smoothing fix
- [S8] https://arxiv.org/pdf/2403.03120v1 — EMA / motion-corrected moving average for video segmentation
- [S9] https://developers.google.com/edge/mediapipe/solutions/vision/image_segmenter — model cards (resolutions, classes, Pixel 6 latencies)
- [S10] https://github.com/PeterL1n/RobustVideoMatting — RVM claims + exports
- [S11] https://github.com/ZHKKKe/MODNet — MODNet claims
- [S12] https://huggingface.co/briaai/RMBG-2.0 — in-browser blocker
- [S13] https://huggingface.co/onnx-community/BEN2-ONNX — BEN2 browser variant
- [S14] MediaPipe image_segmenter model card latencies (see S9)
- [S15] https://github.com/google-ai-edge/mediapipe/issues/6142 — selfie_multiclass iOS-Safari GPU category scrambling
- [S16] https://img.ly/blog/browser-background-removal-using-onnx-runtime-webgpu/ — onnxruntime-web WebGPU perf
- [S17] https://opensource.microsoft.com/blog/2024/02/29/onnx-runtime-web-unleashes-generative-ai-in-the-browser-using-webgpu/ — WebGPU EP availability
- [S18] https://github.com/google-ai-edge/mediapipe/releases — 0.10.32 / 0.10.35 notes
- [S19] https://github.com/google-ai-edge/mediapipe/issues/5826 — WebGPU for vision tasks (open)
- [S20] https://research.google/blog/accurate-alpha-matting-for-portrait-mode-selfies-on-pixel-6/ — matting vs segmentation
- Teams (high level only): https://www.microsoft.com/en-us/microsoft-teams/virtual-meeting-backgrounds
