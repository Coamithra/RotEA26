#!/usr/bin/env python
"""Generate arcade-announcer test barks via ElevenLabs v3 audio tags.

Reads the key from ../../.env (ELEVENLABSTOKEN), never prints it. Saves MP3s
to tools/tts/out/ and prints only HTTP status + filename + byte size.

Run:  PYTHONIOENCODING=utf-8 python tools/tts/eleven_generate.py
"""
import json
import os
import sys
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
MODEL_ID = "eleven_v3"

# (voice label, voice_id) — chosen for announcer energy
VOICES = [
    ("charlie", "IKne3meq5aSn9XLyUdCD"),  # Deep, Confident, Energetic (hyped)
    ("adam",    "pNInz6obpgDQGcFmaJgB"),  # Dominant, Firm
    ("harry",   "SOYHLrjzK2X1ezoPC6cr"),  # Fierce Warrior (rough)
]

# (file slug, text with v3 audio tags)
LINES = [
    ("powerup",   "[shouts] POWER UP!"),
    ("danger",    "[shouts] DANGER!"),
    ("getready",  "[excited] GET READY!"),
    ("gameover",  "[shouts] GAME OVER!"),
]


def load_token():
    with open(ENV_PATH, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("ELEVENLABSTOKEN="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("ELEVENLABSTOKEN not found in .env")


def synth(token, voice_id, text, out_path):
    body = json.dumps({
        "text": text,
        "model_id": MODEL_ID,
        "voice_settings": {"stability": 0.5, "similarity_boost": 0.75, "use_speaker_boost": True},
    }).encode("utf-8")
    req = urllib.request.Request(
        f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}?output_format=mp3_44100_128",
        data=body,
        headers={"xi-api-key": token, "accept": "audio/mpeg", "content-type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=60) as r:
        audio = r.read()
    with open(out_path, "wb") as f:
        f.write(audio)
    return len(audio)


def main():
    token = load_token()
    os.makedirs(OUT_DIR, exist_ok=True)
    ok = 0
    for vlabel, vid in VOICES:
        for slug, text in LINES:
            name = f"{vlabel}_{slug}.mp3"
            path = os.path.join(OUT_DIR, name)
            try:
                n = synth(token, vid, text, path)
                print(f"OK   {name:24} {n:>7} bytes   {text!r}")
                ok += 1
            except urllib.error.HTTPError as e:
                detail = e.read().decode("utf-8", "replace")[:300]
                print(f"FAIL {name:24} HTTP {e.code} {e.reason}  {detail}")
            except Exception as e:
                print(f"FAIL {name:24} {e}")
    print(f"\n{ok}/{len(VOICES) * len(LINES)} generated -> {OUT_DIR}")


if __name__ == "__main__":
    main()
