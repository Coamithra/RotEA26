// ---------------------------------------------------------------------------
// eaWebcam's segmentation worker (see webcam.js). MediaPipe MUST run in a
// worker here: its Emscripten loader assigns the GLOBAL `Module` object, and
// Blazor's Mono runtime on the main thread uses the same global — loading
// tasks-vision on the main thread clobbers it and kills the .NET runtime
// ("_malloc is not a function", frozen game loop; reproduced). A dedicated
// worker gets its own global scope, so the two wasm runtimes can't collide —
// and segmentation stops competing with the interpreted game for the main
// thread. This is a CLASSIC worker (not a module worker) on purpose:
// tasks-vision loads its wasm via importScripts(), which module workers forbid;
// dynamic import() is available in classic dedicated workers everywhere current.
//
// Protocol (all messages are {type, ...}):
//   in :  {type:'init',  base}                    absolute URL of lib/mediapipe/
//   out:  {type:'ready'} | {type:'failed', error}
//   in :  {type:'frame', bitmap, ts}              mirrored 320x240 ImageBitmap
//   out:  {type:'mask', ok:true, alpha, grid, occupied}   (buffers transferred)
//         alpha: Uint8ClampedArray PROC_W*PROC_H person-alpha (soft edge)
//         grid : Uint8Array packed 40x30 occupancy bits (LSB-first)
//       | {type:'mask', ok:false}                 (frame skipped/errored)
//
// MASK REFINEMENT (the Zoom-quality gap turned out to be post-processing, not
// the model — Google Meet's public pipeline runs the same class of segmenter
// and then refines; see the research in the card/PR):
//   1. ADAPTIVE TEMPORAL EMA over the raw confidence: raw per-frame masks
//      shimmer ("small variations in inference manifest as visible jitter");
//      blending with the previous frame's mask kills the flicker. The blend is
//      confidence-DELTA adaptive so real motion snaps (no ghost trails) while
//      stable regions smooth hard.
//   2. JOINT BILATERAL FILTER on the uncertain edge band, guided by the camera
//      frame — MediaPipe's own guidance ("apply a joint bilateral filter to
//      results.segmentation_mask with image") and the documented Google Meet
//      refinement stage. Band-limited (only ~mid-confidence pixels), so it
//      costs ~1-2ms at 320x240 instead of a full-frame pass.
//   The occupancy grid is built from the REFINED confidence, so the gameplay
//   hitbox stops flickering along with the visual.
// ---------------------------------------------------------------------------
"use strict";

// MUST match webcam.js / Compat/WebcamInterop.cs.
var PROC_W = 320, PROC_H = 240;
var GRID_W = 40, GRID_H = 30;
var CELL = PROC_W / GRID_W;
var CONF = 0.5;
var CELL_MIN = 10;

// Temporal EMA: blend weight for the NEW frame ranges MIN..MAX with the
// per-pixel |delta| (stable pixel -> heavy smoothing, moving pixel -> snappy).
var EMA_MIN = 0.35, EMA_MAX = 0.95;

// Joint bilateral filter: 5x5 window, guided by the camera frame's RGB.
var JBF_RADIUS = 2;
var JBF_SIGMA_SPATIAL = 1.6;         // px
var JBF_SIGMA_COLOR = 18;            // 0..255 rgb distance
// Only refine genuinely uncertain pixels (the edge band); if a weird frame
// makes the band huge, skip the filter rather than blow the frame budget.
var JBF_BAND_LO = 0.03, JBF_BAND_HI = 0.97;
var JBF_MAX_BAND = (PROC_W * PROC_H * 0.30) | 0;

var segmenter = null;

// Scratch buffers (persistent — no per-frame allocation beyond the transfers).
var prevConf = null;                  // Float32Array: last frame's EMA state
var confBuf = new Float32Array(PROC_W * PROC_H);   // this frame's blended conf
var jbfBuf = new Float32Array(PROC_W * PROC_H);    // refined conf
var guideCanvas = null, guideCtx = null;           // camera frame at proc res

// Precomputed JBF kernels: spatial weights + a colour-distance LUT
// (exp() per sample would dominate the pass).
var jbfSpatial = (function () {
    var size = 2 * JBF_RADIUS + 1;
    var w = new Float32Array(size * size);
    var s2 = 2 * JBF_SIGMA_SPATIAL * JBF_SIGMA_SPATIAL;
    for (var dy = -JBF_RADIUS; dy <= JBF_RADIUS; dy++) {
        for (var dx = -JBF_RADIUS; dx <= JBF_RADIUS; dx++) {
            w[(dy + JBF_RADIUS) * size + (dx + JBF_RADIUS)] = Math.exp(-(dx * dx + dy * dy) / s2);
        }
    }
    return w;
})();
var jbfColorLUT = (function () {
    // index = squared rgb distance >> 6 (max 3*255^2 = 195075 -> 3049 entries)
    var lut = new Float32Array(3050);
    var c2 = 2 * JBF_SIGMA_COLOR * JBF_SIGMA_COLOR;
    for (var i = 0; i < lut.length; i++) {
        lut[i] = Math.exp(-(i << 6) / c2);
    }
    return lut;
})();

self.onmessage = function (ev) {
    var d = ev.data || {};
    if (d.type === "init") init(d.base);
    else if (d.type === "frame") frame(d.bitmap, d.ts);
};

function init(base) {
    import(base + "vision_bundle.mjs").then(function (vision) {
        return vision.FilesetResolver.forVisionTasks(base + "wasm").then(function (fileset) {
            var opts = function (delegate) {
                return {
                    baseOptions: { modelAssetPath: base + "selfie_segmenter.tflite", delegate: delegate },
                    runningMode: "VIDEO",
                    outputConfidenceMasks: true,
                    outputCategoryMask: false
                };
            };
            return vision.ImageSegmenter.createFromOptions(fileset, opts("GPU")).catch(function () {
                // GPU delegate needs OffscreenCanvas WebGL; retry on CPU before giving up.
                return vision.ImageSegmenter.createFromOptions(fileset, opts("CPU"));
            });
        });
    }).then(function (seg) {
        segmenter = seg;
        postMessage({ type: "ready" });
    }).catch(function (err) {
        postMessage({ type: "failed", error: String(err) });
    });
}

// Grab the camera frame's RGB at proc res to guide the bilateral filter.
// Returns null (filter skipped, everything else still works) if OffscreenCanvas
// isn't available — the same browsers that lack it are already on the fallback
// paths elsewhere.
function guidePixels(bitmap) {
    try {
        if (!guideCtx) {
            if (typeof OffscreenCanvas === "undefined") return null;
            guideCanvas = new OffscreenCanvas(PROC_W, PROC_H);
            guideCtx = guideCanvas.getContext("2d", { willReadFrequently: true });
            if (!guideCtx) return null;
        }
        guideCtx.drawImage(bitmap, 0, 0, PROC_W, PROC_H);
        return guideCtx.getImageData(0, 0, PROC_W, PROC_H).data;
    } catch (e) {
        return null;
    }
}

function frame(bitmap, ts) {
    if (!segmenter || !bitmap) {
        if (bitmap && bitmap.close) bitmap.close();
        postMessage({ type: "mask", ok: false });
        return;
    }
    var sent = false;
    try {
        // read the guide BEFORE segmentForVideo — the bitmap is consumed there
        var guide = guidePixels(bitmap);
        // tasks-vision invokes the callback synchronously; the result (and its
        // GPU-backed masks) is only valid inside it.
        segmenter.segmentForVideo(bitmap, ts, function (result) {
            try {
                var masks = result.confidenceMasks;
                if (masks && masks.length) {
                    // selfie_segmenter: the last channel is the person confidence
                    var m = masks[masks.length - 1].getAsFloat32Array();
                    var out = buildMask(m, guide);
                    postMessage({ type: "mask", ok: true, alpha: out.alpha, grid: out.grid, occupied: out.occupied },
                        [out.alpha.buffer, out.grid.buffer]);
                    sent = true;
                }
            } finally {
                if (result && result.close) result.close();
            }
        });
    } catch (e) {
        postMessage({ type: "mask", ok: false, error: String(e) });
        sent = true;
    } finally {
        if (bitmap.close) bitmap.close();
    }
    if (!sent) postMessage({ type: "mask", ok: false });
}

// Adaptive temporal EMA: blend the raw confidence with last frame's state.
// Weight for the NEW value scales with |delta| — a stable pixel (model noise)
// smooths hard, a genuinely moving pixel snaps to the new mask. Writes confBuf
// and updates prevConf in place.
function temporalBlend(m) {
    var n = PROC_W * PROC_H;
    if (!prevConf) {
        prevConf = new Float32Array(n);
        prevConf.set(m.subarray ? m.subarray(0, n) : m);
    }
    var range = EMA_MAX - EMA_MIN;
    for (var i = 0; i < n; i++) {
        var cur = m[i], old = prevConf[i];
        var d = cur - old;
        var ad = d < 0 ? -d : d;                   // 0..1
        var k = EMA_MIN + range * (ad > 1 ? 1 : ad);
        var v = old + d * k;
        confBuf[i] = v;
        prevConf[i] = v;
    }
}

// Joint bilateral filter over the uncertain band of confBuf, guided by the
// camera RGB: a mask pixel is re-estimated from neighbours that LOOK like it
// (spatial closeness x colour similarity), which snaps the mask edge onto the
// image edge — the Meet/MediaPipe-documented refinement. Full-confidence
// pixels are copied through untouched. Returns the buffer to read from.
function bilateralRefine(guide) {
    var n = PROC_W * PROC_H;
    if (!guide) return confBuf;
    // count the band first: skip the filter on degenerate frames
    var band = 0;
    for (var i = 0; i < n; i++) {
        var c = confBuf[i];
        if (c > JBF_BAND_LO && c < JBF_BAND_HI) band++;
    }
    if (band === 0 || band > JBF_MAX_BAND) return confBuf;
    var size = 2 * JBF_RADIUS + 1;
    for (var y = 0; y < PROC_H; y++) {
        var rowBase = y * PROC_W;
        for (var x = 0; x < PROC_W; x++) {
            var idx = rowBase + x;
            var c0 = confBuf[idx];
            if (c0 <= JBF_BAND_LO || c0 >= JBF_BAND_HI) { jbfBuf[idx] = c0; continue; }
            var g = idx << 2;
            var r0 = guide[g], g0 = guide[g + 1], b0 = guide[g + 2];
            var sum = 0, wsum = 0;
            var y0 = y - JBF_RADIUS < 0 ? -y : -JBF_RADIUS;
            var y1 = y + JBF_RADIUS >= PROC_H ? PROC_H - 1 - y : JBF_RADIUS;
            var x0 = x - JBF_RADIUS < 0 ? -x : -JBF_RADIUS;
            var x1 = x + JBF_RADIUS >= PROC_W ? PROC_W - 1 - x : JBF_RADIUS;
            for (var dy = y0; dy <= y1; dy++) {
                var nRow = idx + dy * PROC_W;
                var sRow = (dy + JBF_RADIUS) * size + JBF_RADIUS;
                for (var dx = x0; dx <= x1; dx++) {
                    var j = nRow + dx;
                    var gj = j << 2;
                    var dr = guide[gj] - r0, dg = guide[gj + 1] - g0, db = guide[gj + 2] - b0;
                    var w = jbfSpatial[sRow + dx] * jbfColorLUT[(dr * dr + dg * dg + db * db) >> 6];
                    sum += w * confBuf[j];
                    wsum += w;
                }
            }
            jbfBuf[idx] = wsum > 0 ? sum / wsum : c0;
        }
    }
    return jbfBuf;
}

function buildMask(m, guide) {
    var n = PROC_W * PROC_H;
    temporalBlend(m);
    var conf = bilateralRefine(guide);
    var alpha = new Uint8ClampedArray(n);
    for (var i = 0; i < n; i++) {
        // soft edge: 0 below 0.35 confidence, 255 above 0.65
        var a = (conf[i] - 0.35) / 0.3;
        alpha[i] = a <= 0 ? 0 : (a >= 1 ? 255 : (a * 255) | 0);
    }
    var grid = new Uint8Array((GRID_W * GRID_H + 7) >> 3);
    var occupied = 0;
    for (var gy = 0; gy < GRID_H; gy++) {
        for (var gx = 0; gx < GRID_W; gx++) {
            var count = 0;
            var baseIdx = gy * CELL * PROC_W + gx * CELL;
            for (var y = 0; y < CELL; y++) {
                var row = baseIdx + y * PROC_W;
                for (var x = 0; x < CELL; x++) {
                    if (conf[row + x] > CONF) count++;
                }
            }
            if (count >= CELL_MIN) {
                var bit = gy * GRID_W + gx;
                grid[bit >> 3] |= (1 << (bit & 7));
                occupied++;
            }
        }
    }
    return { alpha: alpha, grid: grid, occupied: occupied };
}
