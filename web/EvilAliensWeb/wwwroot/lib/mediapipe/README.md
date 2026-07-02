# Bundled MediaPipe selfie segmentation (webcam challenge level)

Vendored copy of Google MediaPipe's tasks-vision runtime + the selfie-segmenter
model, used by `wwwroot/webcam.js` (the "I Made This!" webcam level) to remove
the player's room background in-browser. Bundled locally because the site must
stay fully self-hosted (GitHub Pages, no CDN dependency) — and it is
**lazy-loaded** only when the webcam level starts, so the normal boot payload is
unchanged.

| file | origin |
|---|---|
| `vision_bundle.mjs`, `wasm/vision_wasm_internal.{js,wasm}` | npm `@mediapipe/tasks-vision@0.10.14` (Apache-2.0) |
| `selfie_segmenter.tflite` | https://storage.googleapis.com/mediapipe-models/image_segmenter/selfie_segmenter/float16/latest/selfie_segmenter.tflite (Apache-2.0) |

Only the SIMD wasm variant is shipped (every current browser has wasm SIMD); if
the loader can't bring the runtime up, `webcam.js` falls back to a no-model
"simple oval" mode, so the level still works.

To update: bump the npm tarball / model URL above, copy the same four files in,
and re-test the level (`?level=WebcamAliens`).
