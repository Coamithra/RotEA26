// ---------------------------------------------------------------------------
// eaWebcam — the browser half of the "I Made This!" webcam challenge level
// (the remake of the 2004 webcam game the "I made this!" splash meme is from).
//
// What it owns (all DOM, all OUTSIDE #app so Blazor's mount can't wipe it):
//   * A Teams-style CAMERA SETUP dialog: pick a webcam from a dropdown, see a
//     live preview (with background removal applied once the model is up), then
//     Join or Cancel. Shown by C# via WebcamInterop.BeginSetup() -> eaWebcam.begin().
//   * The PERSON OVERLAY: a canvas positioned exactly over the game's 4:3
//     letterbox (same math as RenderScale.WindowDestRect) that shows the
//     player's segmented, MIRRORED camera image with the room background
//     removed — so the player appears standing in the game's starfield. It sits
//     above the game canvas (the aliens fly "behind" the player) and below the
//     touch/fullscreen UI; pointer-events pass through.
//   * Background removal: MediaPipe selfie segmentation, bundled OFFLINE under
//     lib/mediapipe/ (see the README there) and lazy-loaded only when this level
//     starts, so the normal boot payload is untouched. It runs in a DEDICATED
//     WORKER (webcam-worker.js) — MediaPipe's Emscripten loader assigns the
//     global `Module`, which collides with Blazor's Mono runtime on the main
//     thread and kills the game loop (reproduced), and a worker also keeps
//     segmentation off the interpreted game's thread. If the worker/model can't
//     come up (ancient browser, missing files) it falls back to a fixed oval
//     "simple mode" so the level stays playable.
//   * The COLLISION FEED: every processed frame the person mask is downsampled
//     to a 40x30 occupancy grid in the game's 800x600 design space (mirrored,
//     i.e. matching exactly what is drawn) and pushed to C# as ~200 bytes of
//     base64 via DotNet.invokeMethod('EvilAliensWeb','webcamMask',...). The game
//     does all hit-testing against that grid (Compat/WebcamInterop.cs).
//
// Nothing here runs unless the level asks for it; stop() tears everything down
// (tracks, loops, DOM) and is idempotent.
// ---------------------------------------------------------------------------
window.eaWebcam = (function () {
    "use strict";

    // Design space + grid (MUST match Compat/WebcamInterop.cs).
    var DESIGN_W = 800, DESIGN_H = 600;
    var GRID_W = 40, GRID_H = 30;

    // Segmentation processing size (4:3, small = fast; the mask is upscaled for
    // the visual and downsampled for the grid from this same buffer). The
    // mask/grid math itself lives in webcam-worker.js — keep sizes in sync.
    var PROC_W = 320, PROC_H = 240;

    var MEDIAPIPE_BASE = "lib/mediapipe/";        // relative to <base href>

    // --- state -------------------------------------------------------------
    var video = null;              // <video> playing the chosen camera
    var stream = null;
    var currentDeviceId = null;
    var worker = null;             // webcam-worker.js (owns MediaPipe)
    var workerBusy = false;        // a frame is in flight to the worker
    var workerErrors = 0;          // consecutive failed frames -> simple mode
    var segmenterState = "off";    // off | loading | ready | failed
    var mode = "setup";            // setup | play | off
    var rafId = 0;
    var rafIsTimeout = false;      // rafId is a setTimeout id (hidden tab), not a rAF id
    var lastProcT = 0;

    var dlg = null;                // setup dialog root
    var previewCanvas = null;      // canvas inside the dialog
    var overlayCanvas = null;      // the in-game person layer
    var statusEl = null, joinBtn = null, selectEl = null;

    var procCanvas = null, procCtx = null;    // mirrored cover-cropped camera frame
    var maskCanvas = null, maskCtx = null;    // alpha mask (from segmentation)
    var maskData = null;
    var gridBytes = new Uint8Array((GRID_W * GRID_H + 7) >> 3);

    function invoke(method) {
        var args = Array.prototype.slice.call(arguments, 1);
        try { return DotNet.invokeMethod.apply(DotNet, ["EvilAliensWeb", method].concat(args)); }
        catch (e) { /* game not booted / level already gone — non-fatal */ }
    }

    // --- letterbox geometry (mirror of RenderScale.WindowDestRect) ----------
    function destRect() {
        var ww = window.innerWidth || DESIGN_W, wh = window.innerHeight || DESIGN_H;
        var s = Math.min(ww / DESIGN_W, wh / DESIGN_H);
        if (!(s > 0) || !isFinite(s)) s = 1;
        var dw = Math.round(DESIGN_W * s), dh = Math.round(DESIGN_H * s);
        return { x: (ww - dw) / 2, y: (wh - dh) / 2, w: Math.max(1, dw), h: Math.max(1, dh) };
    }

    // Cap on the overlay's backing-store width. A 1280x720 camera cover-cropped
    // to 4:3 yields a 960px-wide source, so pixels beyond ~that are upscale-only;
    // a little headroom keeps a higher-res camera crisp without a huge canvas.
    var OVERLAY_MAX_W = 1280;

    function placeOverlay() {
        if (!overlayCanvas) return;
        var r = destRect();
        overlayCanvas.style.left = r.x + "px";
        overlayCanvas.style.top = r.y + "px";
        overlayCanvas.style.width = r.w + "px";
        overlayCanvas.style.height = r.h + "px";
        // Size the BACKING STORE to the letterbox's device pixels (capped) —
        // a fixed 800x600 store CSS-stretched to e.g. 1440x1080 softens the
        // player image before the mask even matters. Only touch width/height
        // when they actually change (assigning them clears the canvas).
        var dpr = window.devicePixelRatio || 1;
        var bw = Math.min(OVERLAY_MAX_W, Math.max(DESIGN_W, Math.round(r.w * dpr)));
        var bh = Math.round(bw * 3 / 4);
        if (overlayCanvas.width !== bw || overlayCanvas.height !== bh) {
            overlayCanvas.width = bw;
            overlayCanvas.height = bh;
        }
    }

    // --- camera ------------------------------------------------------------
    function stopTracks() {
        if (stream) {
            try { stream.getTracks().forEach(function (t) { t.stop(); }); } catch (e) { }
            stream = null;
        }
    }

    function openCamera(deviceId) {
        stopTracks();
        setStatus("Requesting camera…", "wait");
        var vc = deviceId
            ? { deviceId: { exact: deviceId }, width: { ideal: 1280 }, height: { ideal: 720 } }
            : { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } };
        return navigator.mediaDevices.getUserMedia({ video: vc, audio: false }).then(function (s) {
            if (mode === "off") { s.getTracks().forEach(function (t) { t.stop(); }); return; }
            stream = s;
            var track = s.getVideoTracks()[0];
            currentDeviceId = (track && track.getSettings && track.getSettings().deviceId) || deviceId || null;
            if (!video) {
                video = document.createElement("video");
                video.setAttribute("playsinline", "");
                video.muted = true;
            }
            video.srcObject = s;
            return video.play().catch(function () { });
        }).then(function () {
            refreshDeviceList();
            updateReadiness();
        }).catch(function (err) {
            console.warn("[webcam] getUserMedia failed:", err && err.name, err && err.message);
            setStatus(err && (err.name === "NotAllowedError" || err.name === "PermissionDeniedError")
                ? "Camera access was denied. Allow camera access and try again."
                : "No usable camera found.", "err");
            if (joinBtn) joinBtn.disabled = true;
        });
    }

    function refreshDeviceList() {
        if (!selectEl || !navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;
        navigator.mediaDevices.enumerateDevices().then(function (devs) {
            if (!selectEl) return;                 // torn down meanwhile
            var cams = devs.filter(function (d) { return d.kind === "videoinput"; });
            selectEl.innerHTML = "";
            cams.forEach(function (d, i) {
                var o = document.createElement("option");
                o.value = d.deviceId;
                o.textContent = d.label || ("Camera " + (i + 1));
                if (d.deviceId && d.deviceId === currentDeviceId) o.selected = true;
                selectEl.appendChild(o);
            });
            selectEl.disabled = cams.length <= 1;
        }).catch(function () { });
    }

    // --- segmentation model (in the worker — see webcam-worker.js) ----------
    function loadSegmenter() {
        if (segmenterState === "loading" || segmenterState === "ready") return;
        segmenterState = "loading";
        updateReadiness();
        try {
            worker = new Worker(new URL("webcam-worker.js", document.baseURI).href);
        } catch (e) {
            console.warn("[webcam] worker creation failed — falling back to simple mode:", e);
            segmenterFailed();
            return;
        }
        worker.onerror = function (e) {
            console.warn("[webcam] segmentation worker error — falling back to simple mode:", e && e.message);
            segmenterFailed();
        };
        worker.onmessage = function (ev) {
            var d = ev.data || {};
            if (d.type === "ready") {
                if (mode === "off") return;
                segmenterState = "ready";
                updateReadiness();
            } else if (d.type === "failed") {
                console.warn("[webcam] segmentation model failed to load — falling back to simple mode:", d.error);
                segmenterFailed();
            } else if (d.type === "mask") {
                workerBusy = false;
                if (d.ok) {
                    workerErrors = 0;
                    onWorkerMask(d);
                } else if (++workerErrors > 30) {
                    console.warn("[webcam] segmentation keeps failing — switching to simple mode");
                    segmenterFailed();
                }
            }
        };
        worker.postMessage({ type: "init", base: new URL(MEDIAPIPE_BASE, document.baseURI).href });
    }

    function segmenterFailed() {
        segmenterState = "failed";
        workerBusy = false;
        if (worker) { try { worker.terminate(); } catch (e) { } worker = null; }
        updateReadiness();
    }

    // A segmented mask came back from the worker: paint its alpha into the mask
    // canvas, adopt its occupancy grid, and finish the frame (composite + feed).
    function onWorkerMask(d) {
        if (mode === "off") return;
        ensureBuffers();
        var alpha = new Uint8ClampedArray(d.alpha.buffer || d.alpha);
        var px = maskData.data;
        var n = Math.min(alpha.length, PROC_W * PROC_H);
        for (var i = 0; i < n; i++) {
            px[i * 4 + 3] = alpha[i];
        }
        maskCtx.putImageData(maskData, 0, 0);
        gridBytes = new Uint8Array(d.grid.buffer || d.grid);
        finishFrame(d.occupied | 0);
    }

    // --- per-frame processing ------------------------------------------------
    function ensureBuffers() {
        if (procCanvas) return;
        procCanvas = document.createElement("canvas");
        procCanvas.width = PROC_W; procCanvas.height = PROC_H;
        procCtx = procCanvas.getContext("2d", { willReadFrequently: false });
        maskCanvas = document.createElement("canvas");
        maskCanvas.width = PROC_W; maskCanvas.height = PROC_H;
        maskCtx = maskCanvas.getContext("2d");
        maskData = maskCtx.createImageData(PROC_W, PROC_H);
        // mask pixels are white; only alpha varies per frame
        var d = maskData.data;
        for (var i = 0; i < d.length; i += 4) { d[i] = d[i + 1] = d[i + 2] = 255; d[i + 3] = 0; }
    }

    // Centered 4:3 cover-crop source rect of the live video frame.
    function coverRect() {
        var vw = video.videoWidth || 4, vh = video.videoHeight || 3;
        var sw = vw, sh = vh;
        if (vw / vh > 4 / 3) sw = vh * 4 / 3;      // wide camera: crop the sides
        else sh = vw * 3 / 4;                      // tall camera: crop top/bottom
        return { x: (vw - sw) / 2, y: (vh - sh) / 2, w: sw, h: sh };
    }

    // Draw the current camera frame MIRRORED + cover-cropped into ctx (w x h).
    function drawCamera(ctx, w, h) {
        var s = coverRect();
        ctx.save();
        ctx.scale(-1, 1);
        ctx.drawImage(video, s.x, s.y, s.w, s.h, -w, 0, w, h);
        ctx.restore();
    }

    // Fallback "simple mode": no segmentation — an oval where a seated player
    // usually is. The raw feed shows inside the same oval so what you see is
    // still exactly the hitbox.
    function applyOvalFallback() {
        maskCtx.clearRect(0, 0, PROC_W, PROC_H);
        maskCtx.fillStyle = "#fff";
        maskCtx.beginPath();
        maskCtx.ellipse(PROC_W / 2, PROC_H * 0.62, PROC_W * 0.23, PROC_H * 0.5, 0, 0, Math.PI * 2);
        maskCtx.fill();
        gridBytes.fill(0);
        var occupied = 0;
        for (var gy = 0; gy < GRID_H; gy++) {
            for (var gx = 0; gx < GRID_W; gx++) {
                var dx = (gx + 0.5) / GRID_W - 0.5, dy = (gy + 0.5) / GRID_H - 0.62;
                if ((dx * dx) / (0.23 * 0.23) + (dy * dy) / (0.5 * 0.5) <= 1) {
                    var bit = gy * GRID_W + gx;
                    gridBytes[bit >> 3] |= (1 << (bit & 7));
                    occupied++;
                }
            }
        }
        return occupied;
    }

    function b64(bytes) {
        var s = "";
        for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
        return btoa(s);
    }

    function composite(target) {
        var ctx = target.getContext("2d");
        var w = target.width, h = target.height;
        // "high" = bicubic-ish resampling: the camera upscales crisper and the
        // 320x240 mask's soft edge stretches smoothly instead of stair-stepping.
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = "high";
        ctx.clearRect(0, 0, w, h);
        drawCamera(ctx, w, h);
        ctx.globalCompositeOperation = "destination-in";
        ctx.drawImage(maskCanvas, 0, 0, w, h);
        ctx.globalCompositeOperation = "source-over";
    }

    // Composite the current masked frame to the live target and, in play mode,
    // feed the occupancy grid to the game. Called synchronously for the oval
    // fallback and from onWorkerMask when a segmented mask arrives.
    function finishFrame(occupied) {
        var target = (mode === "play") ? overlayCanvas : previewCanvas;
        if (target) composite(target);
        if (mode === "play") {
            invoke("webcamMask", b64(gridBytes), occupied / (GRID_W * GRID_H));
        }
    }

    function processFrame(now) {
        rafId = 0;
        if (mode === "off") return;
        scheduleFrame();
        if (!video || video.readyState < 2) return;
        if (now - lastProcT < 30) return;          // ~30fps cap
        lastProcT = now;
        ensureBuffers();
        drawCamera(procCtx, PROC_W, PROC_H);

        if (segmenterState === "ready" && worker) {
            // Ship the frame to the segmentation worker (one in flight at a time;
            // the composite + game feed happen in onWorkerMask when it returns).
            if (!workerBusy && typeof createImageBitmap === "function") {
                workerBusy = true;
                var ts = now;
                createImageBitmap(procCanvas).then(function (bmp) {
                    if (mode === "off" || !worker) { bmp.close(); workerBusy = false; return; }
                    worker.postMessage({ type: "frame", bitmap: bmp, ts: ts }, [bmp]);
                }).catch(function () { workerBusy = false; });
            }
        }
        else if (segmenterState === "failed") {
            finishFrame(applyOvalFallback());
        }
        // while the model is still loading, the preview shows the raw feed
        else if (mode === "setup" && previewCanvas) {
            var ctx = previewCanvas.getContext("2d");
            ctx.clearRect(0, 0, previewCanvas.width, previewCanvas.height);
            drawCamera(ctx, previewCanvas.width, previewCanvas.height);
        }
    }

    function scheduleFrame() {
        if (mode === "off" || rafId) return;
        // Keep processing in a hidden tab (rAF pauses there; the game loop itself
        // falls back to setTimeout — see index.html scheduleTick).
        rafIsTimeout = !!document.hidden;
        if (rafIsTimeout) rafId = setTimeout(function () { processFrame(performance.now()); }, 66);
        else rafId = requestAnimationFrame(processFrame);
    }

    function cancelFrame() {
        if (!rafId) return;
        if (rafIsTimeout) clearTimeout(rafId); else cancelAnimationFrame(rafId);
        rafId = 0;
    }

    // --- setup dialog --------------------------------------------------------
    function setStatus(text, kind) {
        if (!statusEl) return;
        statusEl.textContent = text;
        statusEl.className = "ea-wc-status" + (kind ? " ea-wc-" + kind : "");
    }

    function updateReadiness() {
        if (mode !== "setup" || !dlg) return;
        var camOk = !!(stream && video);
        // Join only once the model load has settled (ready or failed->simple mode):
        // joining mid-load would drop the player into an empty overlay.
        if (joinBtn) joinBtn.disabled = !camOk || (segmenterState !== "ready" && segmenterState !== "failed");
        if (!camOk) return;
        if (segmenterState === "ready")
            setStatus("Background removal ready. You're in the starfield — jump in!", "ok");
        else if (segmenterState === "loading")
            setStatus("Camera ready. Loading background removal… (one-time ~10 MB)", "wait");
        else if (segmenterState === "failed")
            setStatus("Background removal unavailable — using simple oval mode.", "warn");
    }

    // While the dialog is up, the level is already running underneath: swallow the
    // game keys (same trick as the eaTrailer overlay) so Enter/arrows can't
    // blind-drive the pause menu behind it. Esc cancels, Enter joins.
    var GAME_KEYS = { 38: 1, 40: 1, 37: 1, 39: 1, 13: 1, 32: 1, 87: 1, 65: 1, 83: 1, 68: 1 };
    function onKey(e) {
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        if (e.key === "Escape" || e.keyCode === 27) {
            e.preventDefault(); e.stopPropagation();
            if (e.stopImmediatePropagation) e.stopImmediatePropagation();
            cancel();
            return;
        }
        if (e.keyCode === 13 && joinBtn && !joinBtn.disabled) {
            e.preventDefault(); e.stopPropagation();
            if (e.stopImmediatePropagation) e.stopImmediatePropagation();
            join();
            return;
        }
        if (GAME_KEYS[e.keyCode]) {
            e.preventDefault(); e.stopPropagation();
            if (e.stopImmediatePropagation) e.stopImmediatePropagation();
        }
    }

    function buildDialog() {
        var d = document.createElement("div");
        d.id = "ea-webcam-setup";
        d.innerHTML =
            '<div class="ea-wc-card">' +
                '<div class="ea-wc-title">CAMERA SETUP</div>' +
                '<div class="ea-wc-sub">The aliens are coming for YOU this time.<br>' +
                    'Check yourself out before you jump into the starfield.</div>' +
                '<div class="ea-wc-previewwrap"><canvas class="ea-wc-preview" width="400" height="300"></canvas></div>' +
                '<label class="ea-wc-devrow">Camera' +
                    '<select class="ea-wc-device"></select>' +
                '</label>' +
                '<div class="ea-wc-status">Requesting camera…</div>' +
                '<div class="ea-wc-buttons">' +
                    '<button type="button" class="ea-wc-join" disabled>JOIN THE INVASION</button>' +
                    '<button type="button" class="ea-wc-cancel">Back</button>' +
                '</div>' +
            '</div>';
        document.body.appendChild(d);
        dlg = d;
        previewCanvas = d.querySelector(".ea-wc-preview");
        statusEl = d.querySelector(".ea-wc-status");
        joinBtn = d.querySelector(".ea-wc-join");
        selectEl = d.querySelector(".ea-wc-device");
        joinBtn.addEventListener("click", function (e) { e.preventDefault(); join(); });
        d.querySelector(".ea-wc-cancel").addEventListener("click", function (e) { e.preventDefault(); cancel(); });
        selectEl.addEventListener("change", function () {
            if (selectEl.value) openCamera(selectEl.value);
        });
        window.addEventListener("keydown", onKey, true);
    }

    function removeDialog() {
        window.removeEventListener("keydown", onKey, true);
        if (dlg && dlg.parentNode) dlg.parentNode.removeChild(dlg);
        dlg = null; previewCanvas = null; statusEl = null; joinBtn = null; selectEl = null;
    }

    function buildOverlay() {
        overlayCanvas = document.createElement("canvas");
        overlayCanvas.id = "ea-webcam-layer";
        overlayCanvas.width = DESIGN_W;
        overlayCanvas.height = DESIGN_H;
        document.body.appendChild(overlayCanvas);
        placeOverlay();
        window.addEventListener("resize", placeOverlay);
    }

    function removeOverlay() {
        window.removeEventListener("resize", placeOverlay);
        if (overlayCanvas && overlayCanvas.parentNode) overlayCanvas.parentNode.removeChild(overlayCanvas);
        overlayCanvas = null;
    }

    // --- public flow -----------------------------------------------------------
    function begin() {
        stop();                                   // clean slate (idempotent)
        mode = "setup";
        buildDialog();
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            setStatus("This browser can't access cameras (needs HTTPS or localhost).", "err");
            return;
        }
        openCamera(null);
        loadSegmenter();
        scheduleFrame();
        var canvas = document.getElementById("theCanvas");
        if (canvas && canvas.focus) { try { canvas.focus(); } catch (e) { } }
    }

    function join() {
        if (mode !== "setup") return;
        mode = "play";
        removeDialog();
        buildOverlay();
        invoke("webcamJoined", segmenterState === "ready" ? "segmented" : "simple");
        var canvas = document.getElementById("theCanvas");
        if (canvas && canvas.focus) { try { canvas.focus(); } catch (e) { } }
    }

    function cancel() {
        if (mode !== "setup") return;
        stop();
        invoke("webcamCancelled");
    }

    function stop() {
        if (mode === "off" && !dlg && !overlayCanvas && !stream) return;
        mode = "off";
        cancelFrame();
        removeDialog();
        removeOverlay();
        stopTracks();
        if (video) { try { video.srcObject = null; } catch (e) { } }
        if (worker) { try { worker.terminate(); } catch (e) { } worker = null; }
        workerBusy = false;
        workerErrors = 0;
        segmenterState = "off";
    }

    // Grab the in-game person overlay as straight-alpha RGBA for the level-select
    // thumbnail (the opt-in Settings.WebcamScreenshot capture; C# side is
    // WebcamInterop.GetOverlayPixels). Renders the overlay canvas — which exactly
    // covers the game's 4:3 letterbox — into a reqW x reqH offscreen (the thumbnail is
    // also 4:3, so it fills), reads it back, and returns {w,h,px:base64}. null unless
    // actively playing. Called rarely (once per capture), so the readback cost is fine.
    function overlayPixels(reqW, reqH) {
        try {
            if (mode !== "play" || !overlayCanvas || !overlayCanvas.width || !overlayCanvas.height) return null;
            var w = Math.max(1, reqW | 0), h = Math.max(1, reqH | 0);
            var off = document.createElement("canvas");
            off.width = w; off.height = h;
            var ctx = off.getContext("2d");
            ctx.imageSmoothingEnabled = true;
            ctx.imageSmoothingQuality = "high";
            ctx.clearRect(0, 0, w, h);
            ctx.drawImage(overlayCanvas, 0, 0, overlayCanvas.width, overlayCanvas.height, 0, 0, w, h);
            var data = ctx.getImageData(0, 0, w, h).data; // straight (unpremultiplied) RGBA
            // Chunked base64 — btoa/String.fromCharCode.apply choke on a whole big buffer.
            var CH = 0x8000, parts = [];
            for (var i = 0; i < data.length; i += CH) {
                parts.push(String.fromCharCode.apply(null, data.subarray(i, Math.min(i + CH, data.length))));
            }
            return JSON.stringify({ w: w, h: h, px: btoa(parts.join("")) });
        } catch (e) {
            console.warn("[webcam] overlayPixels failed:", e);
            return null;
        }
    }

    return { begin: begin, stop: stop, overlayPixels: overlayPixels };
})();
