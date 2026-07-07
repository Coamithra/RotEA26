#!/usr/bin/env python
"""Generate candidate "change the channel" static SFX via ElevenLabs sound-effects.

The "I made this!" splash (SplashScene index 1) does an analog-TV channel-flip
glitch (channelflip.fx). We punctuate it with a static burst. The shipped cue is
numpy-synthesized (tools/audio/build_channelswap.py); this instead auditions REAL
sound-effect renders from ElevenLabs' text-to-sound-effects endpoint so a nicer
one can be picked by ear.

Reads the key from ../../.env (ELEVENLABSTOKEN), never prints it. Writes MP3
candidates + a self-contained index.html into tools/audio/channelswap_out/ so
you can open it (file://) and pick. Prints only HTTP status + filename + bytes.

  PYTHONIOENCODING=utf-8 python tools/audio/eleven_channelswap.py
  PYTHONIOENCODING=utf-8 python tools/audio/eleven_channelswap.py --only 3,5   # re-render just those

Each render spends credits. Once you pick a slug, convert it to the game's
mono-16bit-PCM WAV with:  python tools/audio/pick_channelswap.py <slug>
"""
import argparse
import html
import json
import os
import sys
import urllib.request
import urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
OUT_DIR = os.path.join(HERE, "channelswap_out")
API_URL = "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128"

# (slug, description, prompt, duration_seconds, prompt_influence)
# FUNCTIONAL prompts: name the real-world EVENT/SOURCE ("changing the channel on an
# old TV"), don't decompose the acoustics ("hiss/pop/zap"). ElevenLabs renders a much
# more convincing whole when it's told what's happening, not what frequencies to make.
# A spread of the same event phrased different ways, short enough to punctuate the flip.
CANDIDATES = [
    ("01_change_channel", "Changing the channel on an old TV",
     "Changing the channel on an old TV", 0.8, 0.45),
    ("02_channel_static", "TV static when switching channels",
     "Television static noise when switching channels", 0.9, 0.45),
    ("03_detune",         "Detuning an analog TV between channels",
     "Detuning an old analog television between two channels", 0.9, 0.45),
    ("04_channel_knob",   "Turning the channel knob on a CRT",
     "Turning the channel knob on a vintage CRT television", 0.8, 0.45),
    ("05_no_signal",      "Flipping to a dead no-signal channel",
     "An old TV flipping to a channel with no signal, just static", 0.9, 0.5),
    ("06_channel_surf",   "Channel surfing, quick static bursts",
     "Channel surfing on an old television, quick bursts of static", 1.0, 0.5),
    ("07_lose_signal",    "TV losing signal, cutting to snow",
     "An old television set losing signal and cutting to static snow", 1.0, 0.45),
    ("08_antenna_jump",   "Antenna TV jumping between channels",
     "Static crash as an old antenna TV jumps between channels", 0.8, 0.5),
]


def load_token():
    with open(ENV_PATH, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("ELEVENLABSTOKEN="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("ELEVENLABSTOKEN not found in .env")


def synth(token, prompt, duration, influence, out_path):
    body = json.dumps({
        "text": prompt,
        "duration_seconds": duration,
        "prompt_influence": influence,
    }).encode("utf-8")
    req = urllib.request.Request(
        API_URL,
        data=body,
        headers={"xi-api-key": token, "accept": "audio/mpeg", "content-type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=90) as r:
        audio = r.read()
    with open(out_path, "wb") as f:
        f.write(audio)
    return len(audio)


def write_index(results):
    rows = []
    for slug, desc, prompt, dur, infl, status in results:
        ok = status == "ok"
        badge = "%.1fs" % dur
        rows.append(
            '<div class="row">'
            '<div class="meta"><span class="slug">%s</span> <span class="dur">%s</span>'
            '<div class="desc">%s</div><div class="prompt">%s</div></div>'
            '<div class="ctl">%s</div></div>' % (
                html.escape(slug), badge, html.escape(desc), html.escape(prompt),
                ('<audio controls preload="none" src="%s.mp3"></audio>' % html.escape(slug))
                if ok else '<span class="err">failed</span>',
            )
        )
    page = (
        '<!doctype html><meta charset="utf-8"><title>Channel-swap SFX audition</title>'
        '<style>:root{color-scheme:dark}body{background:#0f1115;color:#e6e9ef;'
        "font:14px/1.5 system-ui,Segoe UI,Arial;margin:0;padding:24px}"
        'h1{font-size:18px;margin:0 0 4px}.sub{color:#8a93a6;font-size:12px;margin-bottom:16px;max-width:720px}'
        '.row{display:flex;gap:16px;align-items:center;padding:12px;border:1px solid #232a3a;'
        'border-radius:10px;margin-bottom:8px;background:#151925}.meta{flex:1;min-width:0}'
        '.slug{color:#7fc6ff;font-family:ui-monospace,Consolas,monospace;font-weight:600}'
        '.dur{color:#ffce6e;font-size:12px;margin-left:6px}.desc{font-weight:600;margin-top:2px}'
        '.prompt{color:#8a93a6;font-size:12px;margin-top:2px}.err{color:#ff6b6b}'
        "audio{height:34px}</style>"
        "<h1>Channel-swap SFX candidates</h1>"
        '<div class="sub">Play each; when you like one, tell Claude its <b>slug</b> (the blue name). '
        "Then it becomes the shipped splash cue. Re-render duds with "
        "<code>python tools/audio/eleven_channelswap.py --only N,N</code>.</div>"
        + "".join(rows)
    )
    idx = os.path.join(OUT_DIR, "index.html")
    with open(idx, "w", encoding="utf-8") as f:
        f.write(page)
    return idx


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="", help="comma list of candidate numbers to (re)render, e.g. 3,5")
    args = ap.parse_args()

    token = load_token()
    os.makedirs(OUT_DIR, exist_ok=True)

    only = None
    if args.only.strip():
        only = {int(x) for x in args.only.replace(" ", "").split(",") if x}

    results = []
    ok = 0
    for i, (slug, desc, prompt, dur, infl) in enumerate(CANDIDATES, start=1):
        path = os.path.join(OUT_DIR, slug + ".mp3")
        if only is not None and i not in only:
            # keep prior render if present; still list it
            results.append((slug, desc, prompt, dur, infl, "ok" if os.path.isfile(path) else "skip"))
            continue
        try:
            n = synth(token, prompt, dur, infl, path)
            print("OK   %-18s %7d bytes  %.1fs" % (slug, n, dur))
            results.append((slug, desc, prompt, dur, infl, "ok"))
            ok += 1
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:300]
            print("FAIL %-18s HTTP %s %s  %s" % (slug, e.code, e.reason, detail))
            results.append((slug, desc, prompt, dur, infl, "fail"))
        except Exception as e:
            print("FAIL %-18s %s" % (slug, e))
            results.append((slug, desc, prompt, dur, infl, "fail"))

    idx = write_index(results)
    print("\n%d rendered -> %s" % (ok, OUT_DIR))
    print("open %s" % idx)


if __name__ == "__main__":
    main()
