#!/usr/bin/env python
"""ONE-SHOT consistent VO: generate ALL of Alex's lines in a SINGLE v3 generation, so
the accent commits once and stays put across every line (the per-line approach let the
British accent roam region-to-region -- a known Professional-Voice-Clone-on-v3 problem).
Then slice the single take back into the 18 alex_<slug>.mp3 clips the game needs, using
the /with-timestamps endpoint's per-character timings (exact, tag-aware).

  python tools/tts/eleven_alex_oneshot.py

The accent is anchored ONCE at the top (default "[posh British accent]" for Queen's-
English diction; override with ALEX_ACCENT). Per-line mood tags are kept. Reuses the
lines, voice, token from eleven_alex.py. Overwrites the 18 clips in wwwroot/office/vo and
keeps the full take at tools/tts/_alex_oneshot_full.mp3 for a sanity listen.
"""
import base64
import json
import os
import subprocess
import sys
import urllib.request
import urllib.error

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import eleven_alex as ea  # noqa: E402

ACCENT = os.environ.get("ALEX_ACCENT", "[posh British accent]")   # one accent anchor for the whole take
STABILITY = float(os.environ.get("ALEX_STABILITY", "0.5"))
SEP = " ... "                                                     # audible gap between lines -> clean cut points
STRIP = ("[strong British accent]", "[british]")                 # drop any per-line accent tag; we anchor once up top
FULL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_alex_oneshot_full.mp3")


def ffmpeg_exe():
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return "ffmpeg"


def strip_accent(t):
    t = t.lstrip()
    for p in STRIP:
        if t.startswith(p):
            return t[len(p):]
    return t


def main():
    # 1. Build one combined prompt; remember each line's [start,end) char span in it.
    combined = ACCENT + " "
    spans = []                                  # (slug, start_idx, end_idx_exclusive)
    for i, (slug, text) in enumerate(ea.LINES):
        if i:
            combined += SEP
        start = len(combined)
        combined += strip_accent(text)
        spans.append((slug, start, len(combined)))
    print(f"combined prompt: {len(combined)} chars across {len(spans)} lines (v3 limit 5000)")
    if len(combined) > 5000:
        sys.exit("combined prompt exceeds the 5000-char v3 limit -- split into two takes.")

    # 2. One generation, with per-character timestamps.
    token = ea.load_token()
    body = json.dumps({
        "text": combined,
        "model_id": ea.MODEL_ID,
        "language_code": ea.LANG,
        "voice_settings": {"stability": STABILITY, "use_speaker_boost": True},
    }).encode("utf-8")
    req = urllib.request.Request(
        f"https://api.elevenlabs.io/v1/text-to-speech/{ea.VOICE_ID}/with-timestamps?output_format=mp3_44100_128",
        data=body,
        headers={"xi-api-key": token, "accept": "application/json", "content-type": "application/json"},
        method="POST",
    )
    print(f"[accent {ACCENT!r}  stability {STABILITY}]  generating one take...")
    try:
        with urllib.request.urlopen(req, timeout=240) as r:
            d = json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"HTTP {e.code} {e.reason}: {e.read().decode('utf-8', 'replace')[:300]}")

    audio = base64.b64decode(d["audio_base64"])
    al = d["alignment"]
    chars, starts, ends = al["characters"], al["character_start_times_seconds"], al["character_end_times_seconds"]
    if "".join(chars) != combined:
        sys.exit("alignment characters != input text -- can't map line boundaries safely. Aborting.")
    with open(FULL, "wb") as f:
        f.write(audio)
    print(f"full take: {len(audio)} bytes, {ends[-1]:.1f}s -> {FULL}")

    # 3. Slice each line's [start_time, end_time] (padded into the inter-line gaps) out of the take.
    ff = ffmpeg_exe()
    ok = 0
    for idx, (slug, s, e) in enumerate(spans):
        t0, t1 = starts[s], ends[e - 1]
        prev_end = ends[spans[idx - 1][2] - 1] if idx > 0 else 0.0
        next_start = starts[spans[idx + 1][1]] if idx + 1 < len(spans) else ends[-1] + 1.0
        a = max(prev_end, t0 - 0.06, 0.0)       # small pad, clamped so clips never overlap a neighbour
        b = min(next_start, t1 + 0.14)
        out = os.path.join(ea.OUT_DIR, f"alex_{slug}.mp3")
        try:
            subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-i", FULL,
                            "-ss", f"{a:.3f}", "-to", f"{b:.3f}", "-c:a", "libmp3lame", "-q:a", "3", out],
                           check=True, capture_output=True)
            print(f"OK   alex_{slug}.mp3   {a:6.2f}-{b:6.2f}s  ({os.path.getsize(out)}b)")
            ok += 1
        except subprocess.CalledProcessError as ex:
            print(f"FAIL alex_{slug}.mp3   {ex.stderr.decode('utf-8','replace')[:160]}")
    print(f"\n{ok}/{len(spans)} clips sliced -> {ea.OUT_DIR}")


if __name__ == "__main__":
    main()
