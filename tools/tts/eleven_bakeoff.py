#!/usr/bin/env python
"""Tag-recipe bake-off for the arcade-announcer persona.

Art direction: a META game-voice -- it knows it is the voice of the GAME,
addressing the player at the cabinet, not a character in the fiction. Detached
and authoritative, but INVESTED: when it says DANGER it means it.

That implies TWO tag layers:
  * PERSONA (constant)  -- the announcer/robotic framing, same on every line.
  * EMOTION (per line)  -- urgent / hyped / final ... so it is never flat.
Final text = "{persona}{emotion} {words}", e.g. "[robotic announcer][urgent] DANGER!"

Natural stability (0.5) so the tags actually register (Robust suppresses them).
Edit VOICES / PERSONAS / LINES and re-run. Reads key from ../../.env
(ELEVENLABSTOKEN); never prints it. Saves to tools/tts/out/bakeoff/.

Run:  PYTHONIOENCODING=utf-8 python tools/tts/eleven_bakeoff.py
"""
import json
import os
import sys
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "bakeoff2")
MODEL_ID = "eleven_v3"
STABILITY = 0.5  # Natural -- keeps audio tags responsive (Robust suppresses them)

# (label, voice_id) -- Round 2: Brian is the one to beat; try fresh deep/announcer voices
VOICES = [
    ("brian",  "nPczCjzI2devNBz1zQrb"),  # Deep, Resonant  -- ANCHOR (current winner)
    ("bill",   "pqHfZKP75CvOlQylNhV4"),  # Wise, Mature -- advertisement voice
    ("eric",   "cjVigY5qzO86Huf0OWal"),  # Smooth, Trustworthy
    ("callum", "N2lVS1w4EtoT3dr4eOWO"),  # Husky Trickster -- characters/animation
    ("harry",  "SOYHLrjzK2X1ezoPC6cr"),  # Fierce Warrior -- rough
    ("daniel", "onwK4e9ZLuTAKqWW03F9"),  # Steady Broadcaster -- British, authoritative
]

# (label, persona tag-prefix) -- locked to robo; that's the winning persona
PERSONAS = [
    ("robo", "[robotic announcer]"),
]

# (slug, emotion tag, spoken words) -- the per-line "but it means it" layer
LINES = [
    ("danger",   "[urgent]",  "DANGER!"),
    ("getready", "[hyped]",   "GET READY!"),
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
        "voice_settings": {"stability": STABILITY, "similarity_boost": 0.75, "use_speaker_boost": True},
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
    ok = total = 0
    for vlabel, vid in VOICES:
        for plabel, persona in PERSONAS:
            for slug, emotion, words in LINES:
                total += 1
                text = f"{persona}{emotion} {words}"
                name = f"{vlabel}_{plabel}_{slug}.mp3"
                path = os.path.join(OUT_DIR, name)
                try:
                    n = synth(token, vid, text, path)
                    print(f"OK   {name:30} {n:>7}b   {text!r}")
                    ok += 1
                except urllib.error.HTTPError as e:
                    detail = e.read().decode("utf-8", "replace")[:200]
                    print(f"FAIL {name:30} HTTP {e.code} {e.reason}  {detail}")
                except Exception as e:
                    print(f"FAIL {name:30} {e}")
    print(f"\n{ok}/{total} generated -> {OUT_DIR}")


if __name__ == "__main__":
    main()
