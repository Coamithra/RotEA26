#!/usr/bin/env python
"""Local audition page for Alex's office VO -- Play + Reroll each line until happy.

  python tools/tts/audition.py        ->  open http://localhost:7878

Reuses the lines/tags/voice/synth from eleven_alex.py. Each Reroll calls ElevenLabs
(spends credits) and OVERWRITES wwwroot/office/vo/alex_<slug>.mp3 in place, so the
game picks up the new take on its next reload. A stability dropdown (Creative/Natural/
Robust) lets you experiment per reroll. To change the wording or tags, edit the LINES
in eleven_alex.py (this just reads them).
"""
import html
import json
import os
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import eleven_alex as ea  # noqa: E402  (lines, synth, load_token, OUT_DIR, VOICE_ID)

PORT = 7878
TEXT = dict(ea.LINES)                       # slug -> tagged TTS text
ORDER = [slug for slug, _ in ea.LINES]      # dialogue order
TOKEN = ea.load_token()
_gen_lock = threading.Lock()                # serialize generations (shared module STABILITY)

# slug -> the menu line the player reads, for readability
LABELS = {
    "start_1": "I'm resigning.",
    "start_2": "My cat did it.",
    "start_3": "Everything's aggressively fine.",
    "start_4": "I think I'd like to make video games.",
    "games1_1": "A progress bar is not joy, Jordan.",
    "games1_2": "...achievements?",
    "games2_1": "Forty-first feels like a great final score.",
    "games2_2": "...who's in first?",
    "resign1_1": "Leave Emma out of this.",
    "resign1_2": "It's not about Emma.",
    "resign1_3": "...maybe you're right.",
    "resign2_1": "The lumbar chair?",
    "resign2_2": "I don't want a better chair. I want out.",
    "chair_1": "It remembers me. That's the problem.",
    "chair_2": "Okay, the chair is genuinely--",
    "resign3_1": "Yes. Exactly that.",
    "resign3_2": "They're not little.",
    "resign3_3": "Goodbye, Jordan.",
}

HEAD = """<!doctype html><html><head><meta charset="utf-8"><title>Alex VO audition</title>
<style>
 :root { color-scheme: dark; }
 body { background:#0f1115; color:#e6e9ef; font:14px/1.5 system-ui,'Segoe UI',Arial,sans-serif; margin:0; padding:24px; }
 h1 { font-size:18px; margin:0 0 4px; }
 .sub { color:#8a93a6; font-size:12px; margin-bottom:16px; max-width:760px; }
 .bar { display:flex; gap:14px; align-items:center; margin-bottom:18px; flex-wrap:wrap; }
 select,button { font:inherit; }
 button { background:#222838; color:#e6e9ef; border:1px solid #38415a; border-radius:8px; padding:7px 12px; cursor:pointer; }
 button:hover { background:#2c3346; border-color:#5b80c0; }
 button:disabled { opacity:.45; cursor:default; }
 select { background:#222838; color:#e6e9ef; border:1px solid #38415a; border-radius:8px; padding:6px 8px; }
 .row { display:flex; align-items:center; gap:14px; padding:10px 12px; border:1px solid #232a3a; border-radius:10px; margin-bottom:8px; background:#151925; }
 .row.playing { border-color:#5b80c0; background:#1a2030; }
 .meta { flex:1; min-width:0; }
 .slug { color:#7fc6ff; font-family:ui-monospace,Consolas,monospace; font-size:12px; }
 .line { font-weight:600; }
 .tags { color:#8a93a6; font-size:12px; margin-top:2px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
 .ctl { display:flex; align-items:center; gap:8px; flex:none; }
 .status { color:#8a93a6; font-size:12px; min-width:96px; text-align:right; }
 .ok { color:#34d27b; } .err { color:#ff6b6b; } .busy { color:#ffce6e; }
</style></head><body>
<h1>Alex VO audition</h1>
<div class="sub">Play each line; reroll the duds until you're happy. Each reroll calls ElevenLabs (spends credits) and overwrites the clip on disk &mdash; the game picks it up on reload. Voice: Victor (the narrator). Edit wording/tags in <code>tools/tts/eleven_alex.py</code>.</div>
<div class="bar">
  <label>Reroll stability:
    <select id="stab"><option value="0.0">Creative (0.0)</option><option value="0.5" selected>Natural (0.5)</option><option value="1.0">Robust (1.0)</option></select>
  </label>
  <button id="playall">&#9654; Play all</button>
  <button id="stopall">&#9209; Stop</button>
</div>
<div id="list">
"""

TAIL = """</div>
<script>
 const stab = document.getElementById('stab');
 const ver = {};
 const srcOf = (s) => '/vo/alex_' + s + '.mp3?v=' + (ver[s] || 0);
 let current = null, currentRow = null;
 function clearPlaying(){ if(currentRow){ currentRow.classList.remove('playing'); currentRow=null; } }
 function stopAll(){ if(current){ current.onended=null; current.pause(); current=null; } clearPlaying(); }
 function play(s){
   stopAll();
   const a = new Audio(srcOf(s)); current = a;
   currentRow = document.querySelector('.row[data-slug="'+s+'"]');
   if(currentRow) currentRow.classList.add('playing');
   a.onended = clearPlaying;
   a.play().catch(()=>{});
   return a;
 }
 async function reroll(s, btn, st){
   btn.disabled = true; const old = btn.textContent; btn.textContent = '...';
   st.className = 'status busy'; st.textContent = 'rerolling...';
   try {
     const r = await fetch('/reroll?slug=' + encodeURIComponent(s) + '&stability=' + encodeURIComponent(stab.value), {method:'POST'});
     const j = await r.json();
     if (j.ok) { ver[s] = Date.now(); st.className='status ok'; st.textContent = 'new! ' + Math.round(j.bytes/1024) + 'kb'; play(s); }
     else { st.className='status err'; st.textContent = 'failed: ' + (j.error||'?'); }
   } catch(e) { st.className='status err'; st.textContent = 'error: ' + e; }
   btn.disabled = false; btn.textContent = old;
 }
 document.querySelectorAll('.row').forEach(row => {
   const s = row.dataset.slug, st = row.querySelector('.status');
   row.querySelector('.play').onclick = () => play(s);
   row.querySelector('.reroll').onclick = (e) => reroll(s, e.currentTarget, st);
 });
 const order = [...document.querySelectorAll('.row')].map(r => r.dataset.slug);
 document.getElementById('playall').onclick = () => {
   let i = 0; const next = () => { if (i >= order.length) { clearPlaying(); return; } const a = play(order[i++]); a.onended = () => { clearPlaying(); next(); }; };
   next();
 };
 document.getElementById('stopall').onclick = stopAll;
</script></body></html>
"""


def row_html(slug):
    label = html.escape(LABELS.get(slug) or slug)
    tags = html.escape(TEXT.get(slug) or "")
    return (
        '<div class="row" data-slug="%s">'
        '<div class="meta"><span class="slug">%s</span> &nbsp; <span class="line">&ldquo;%s&rdquo;</span>'
        '<div class="tags">%s</div></div>'
        '<div class="ctl"><button class="play">&#9654; Play</button>'
        '<button class="reroll">&#10227; Reroll</button><span class="status"></span></div>'
        '</div>'
    ) % (html.escape(slug), html.escape(slug), label, tags)


def page():
    return HEAD + "".join(row_html(s) for s in ORDER) + TAIL


class Handler(BaseHTTPRequestHandler):
    def _send(self, code, body, ctype="text/html; charset=utf-8"):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        p = urlparse(self.path)
        if p.path in ("/", "/index.html"):
            self._send(200, page().encode("utf-8"))
        elif p.path.startswith("/vo/") and p.path.endswith(".mp3"):
            fp = os.path.join(ea.OUT_DIR, os.path.basename(p.path))
            if os.path.isfile(fp):
                with open(fp, "rb") as f:
                    self._send(200, f.read(), "audio/mpeg")
            else:
                self._send(404, b"not found", "text/plain")
        else:
            self._send(404, b"not found", "text/plain")

    def do_POST(self):
        p = urlparse(self.path)
        if p.path != "/reroll":
            self._send(404, b"", "text/plain")
            return
        q = parse_qs(p.query)
        slug = (q.get("slug") or [""])[0]
        stab = (q.get("stability") or ["0.5"])[0]
        if slug not in TEXT:
            self._send(400, json.dumps({"ok": False, "error": "unknown slug"}).encode(), "application/json")
            return
        try:
            with _gen_lock:
                ea.STABILITY = float(stab)                      # synth() reads this module global (v3's real lever)
                path = os.path.join(ea.OUT_DIR, "alex_%s.mp3" % slug)
                n = ea.synth(TOKEN, TEXT[slug], path)
            self._send(200, json.dumps({"ok": True, "bytes": n}).encode(), "application/json")
        except Exception as e:
            self._send(500, json.dumps({"ok": False, "error": str(e)}).encode(), "application/json")

    def log_message(self, format, *args):  # noqa: A002 -- match base signature, silence access log
        pass


if __name__ == "__main__":
    print("Alex VO audition  ->  http://localhost:%d   (Ctrl+C to stop)" % PORT)
    print("Serving clips from %s" % ea.OUT_DIR)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
