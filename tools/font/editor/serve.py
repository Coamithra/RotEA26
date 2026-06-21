#!/usr/bin/env python
# ---------------------------------------------------------------------------
# serve.py -- tiny local server for the Revenge-font live editor.
#   1) python tools/font/build_revenge_font.py --emit-editor   (writes data.json)
#   2) python tools/font/editor/serve.py                        (opens the editor)
#   3) tweak glyphs, click "Save overrides"  -> tools/font/overrides.json
#   4) python tools/font/build_revenge_font.py --commit         (bakes them in)
# Serves the app + the source sheets, and accepts POST /save to write the
# overrides JSON. Localhost only.
# ---------------------------------------------------------------------------
import http.server, socketserver, json, os, sys, webbrowser, urllib.parse

HERE      = os.path.dirname(os.path.abspath(__file__))   # tools/font/editor
FONT      = os.path.dirname(HERE)                         # tools/font
SOURCES   = os.path.join(FONT, 'sources')
OVERRIDES = os.path.join(FONT, 'overrides.json')
PORT      = int(sys.argv[1]) if len(sys.argv) > 1 else 8009


class Handler(http.server.BaseHTTPRequestHandler):
    def _send(self, code, body, ctype='application/json'):
        if isinstance(body, str):
            body = body.encode('utf-8')
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(body)

    def _sendfile(self, fp, ctype):
        try:
            with open(fp, 'rb') as f:
                self._send(200, f.read(), ctype)
        except Exception as e:
            self._send(404, json.dumps({'error': str(e)}))

    def do_GET(self):
        path = urllib.parse.urlparse(self.path).path
        if path in ('/', '/index.html'):
            return self._sendfile(os.path.join(HERE, 'index.html'), 'text/html; charset=utf-8')
        if path == '/data.json':
            return self._sendfile(os.path.join(HERE, 'data.json'), 'application/json')
        if path == '/overrides.json':
            if os.path.exists(OVERRIDES):
                return self._sendfile(OVERRIDES, 'application/json')
            return self._send(200, '{}')
        if path.startswith('/sources/'):
            fp = os.path.join(SOURCES, os.path.basename(path))
            if os.path.exists(fp):
                return self._sendfile(fp, 'image/png')
        self._send(404, json.dumps({'error': 'not found', 'path': path}))

    def do_POST(self):
        path = urllib.parse.urlparse(self.path).path
        if path == '/save':
            n = int(self.headers.get('Content-Length', 0))
            raw = self.rfile.read(n)
            try:
                obj = json.loads(raw)
                if not isinstance(obj, dict):
                    raise ValueError('expected a JSON object')
                with open(OVERRIDES, 'w', encoding='utf-8') as f:
                    json.dump(obj, f, indent=1, ensure_ascii=False, sort_keys=True)
                return self._send(200, json.dumps({'ok': True, 'count': len(obj),
                                                   'path': OVERRIDES}))
            except Exception as e:
                return self._send(400, json.dumps({'ok': False, 'error': str(e)}))
        self._send(404, json.dumps({'error': 'not found'}))

    def log_message(self, *a):
        pass


if __name__ == '__main__':
    os.chdir(HERE)
    if not os.path.exists(os.path.join(HERE, 'data.json')):
        print('!! editor/data.json missing -- run first:\n'
              '   python tools/font/build_revenge_font.py --emit-editor', file=sys.stderr)
    with socketserver.TCPServer(('127.0.0.1', PORT), Handler) as httpd:
        url = f'http://127.0.0.1:{PORT}/'
        print(f'font editor -> {url}   (Ctrl-C to stop)')
        try:
            webbrowser.open(url)
        except Exception:
            pass
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print('\nstopped.')
