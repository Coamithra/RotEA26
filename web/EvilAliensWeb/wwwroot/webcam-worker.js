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
// ---------------------------------------------------------------------------
"use strict";

// MUST match webcam.js / Compat/WebcamInterop.cs.
var PROC_W = 320, PROC_H = 240;
var GRID_W = 40, GRID_H = 30;
var CELL = PROC_W / GRID_W;
var CONF = 0.5;
var CELL_MIN = 10;

var segmenter = null;

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

function frame(bitmap, ts) {
    if (!segmenter || !bitmap) {
        if (bitmap && bitmap.close) bitmap.close();
        postMessage({ type: "mask", ok: false });
        return;
    }
    var sent = false;
    try {
        // tasks-vision invokes the callback synchronously; the result (and its
        // GPU-backed masks) is only valid inside it.
        segmenter.segmentForVideo(bitmap, ts, function (result) {
            try {
                var masks = result.confidenceMasks;
                if (masks && masks.length) {
                    // selfie_segmenter: the last channel is the person confidence
                    var m = masks[masks.length - 1].getAsFloat32Array();
                    var out = buildMask(m);
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

function buildMask(m) {
    var n = PROC_W * PROC_H;
    var alpha = new Uint8ClampedArray(n);
    for (var i = 0; i < n; i++) {
        // soft edge: 0 below 0.35 confidence, 255 above 0.65
        var a = (m[i] - 0.35) / 0.3;
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
                    if (m[row + x] > CONF) count++;
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
