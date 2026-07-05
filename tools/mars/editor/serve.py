#!/usr/bin/env python
"""Live tuning editor for the Mars far-hills layer (marshills.png).

The hills are an OFFLINE-baked PNG (build_marshills.py, numpy), so unlike the
in-game `?lazershot` / hue slider panels the game can't re-render them from
sliders at runtime. This is the next-best thing, same pattern as the font
editor (tools/font/editor/serve.py): a local page whose sliders re-run the
REAL generator per change (pixel-exact, no JS reimplementation drift) and
composite the result over the real sky + rocky ground, so what you see is what
Level 2 shows above the horizon.

RUN:    python tools/mars/editor/serve.py          (then open the printed URL)
        --port N to move it off 5299.

FLOW:   drag sliders -> the preview re-renders (~instantly; the build is ~0.1s)
        -> "Write into game" saves the PNG to wwwroot -> in the GAME tab do the
        content-cache bust (the page shows the console one-liner) and reload.
        When you settle, paste the printed CONFIG block back into
        build_marshills.py so the committed tool reproduces your values, and
        re-run it once (tool + PNG are what get committed; see the card).

Endpoints: /            editor page (editor.html, sibling of this file)
           /defaults    the CONFIG constants as JSON (seeds the sliders)
           /render      POST cfg JSON -> PNG bytes (build(seed, cfg))
           /apply       POST cfg JSON -> writes the real wwwroot marshills.png
           /assets/<f>  whitelisted marsbg art (sky + ground tiles) for the canvas
"""

import argparse
import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))          # tools/mars -> import the generator
import build_marshills as bm                        # noqa: E402

MARSBG = os.path.dirname(os.path.normpath(bm.OUT))  # wwwroot/Content/gfx/marsbg
ASSET_RE = re.compile(r"^(clouds-background|marsloop([1-9]|1[0-2]))\.png$")


def defaults_json():
    return json.dumps({
        "seed": bm.SEED,
        "hill_rgb": list(bm.HILL_RGB),
        "haze_rgb": list(bm.HAZE_RGB),
        "ridges": bm.RIDGES,
        "dust_strength": bm.DUST_STRENGTH,
        "dust_highcut": bm.DUST_HIGHCUT,
    }).encode()


def build_layer_arrays(cfg):
    return bm.build_layers(int(cfg.get("seed", bm.SEED)), cfg)


def render_png(cfg):
    """One PNG, the per-ridge layers stacked vertically (far -> near, each
    WIDTH x HEIGHT) -- the page slices them apart and parallax-animates them."""
    from PIL import Image
    import io
    import numpy as np
    layers = build_layer_arrays(cfg)
    stacked = np.concatenate(layers, axis=0)
    buf = io.BytesIO()
    Image.fromarray(stacked, "RGBA").save(buf, "PNG")
    return buf.getvalue()


class Handler(BaseHTTPRequestHandler):
    def _send(self, code, ctype, body, extra=None):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def _read_cfg(self):
        n = int(self.headers.get("Content-Length", 0))
        return json.loads(self.rfile.read(n) or b"{}")

    def do_GET(self):
        path = self.path.split("?")[0]
        if path in ("/", "/index.html"):
            with open(os.path.join(HERE, "editor.html"), "rb") as f:
                self._send(200, "text/html; charset=utf-8", f.read())
        elif path == "/defaults":
            self._send(200, "application/json", defaults_json())
        elif path.startswith("/assets/"):
            name = os.path.basename(path[len("/assets/"):])
            if not ASSET_RE.match(name):
                self._send(404, "text/plain", b"not an allowed asset")
                return
            fp = os.path.join(MARSBG, name)
            if not os.path.exists(fp):
                self._send(404, "text/plain", b"missing")
                return
            with open(fp, "rb") as f:
                self._send(200, "image/png", f.read())
        else:
            self._send(404, "text/plain", b"?")

    def do_POST(self):
        path = self.path.split("?")[0]
        if path == "/render":
            try:
                self._send(200, "image/png", render_png(self._read_cfg()))
            except Exception as e:  # bad slider combo shouldn't kill the server
                self._send(400, "text/plain", str(e).encode())
        elif path == "/apply":
            try:
                from PIL import Image
                cfg = self._read_cfg()
                written = []
                for i, layer in enumerate(build_layer_arrays(cfg), start=1):
                    out = os.path.normpath(bm.OUT_TEMPLATE.format(i))
                    Image.fromarray(layer, "RGBA").save(out)
                    written.append(out)
                    print(f"[apply] wrote {out}")
                legacy = os.path.normpath(bm.LEGACY_OUT)
                if os.path.exists(legacy):   # superseded single-texture output
                    os.remove(legacy)
                    print(f"[apply] removed stale {legacy}")
                self._send(200, "application/json",
                           json.dumps({"written": written}).encode())
            except Exception as e:
                self._send(400, "text/plain", str(e).encode())
        else:
            self._send(404, "text/plain", b"?")

    def log_message(self, format, *args):  # keep the console to apply-lines only
        pass


def main():
    ap = argparse.ArgumentParser(description="Live marshills tuning editor.")
    ap.add_argument("--port", type=int, default=5299)
    args = ap.parse_args()
    srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"marshills editor:  http://localhost:{args.port}/")
    print(f"writes into:       {os.path.normpath(bm.OUT)}")
    print("Ctrl+C to stop.")
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
