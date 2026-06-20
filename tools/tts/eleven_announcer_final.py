#!/usr/bin/env python
"""FINAL announcer set -- Brian, [robotic announcer] persona, Natural stability.

Locked recipe from the bake-off: voice=Brian (Deep, Resonant), persona tag
"[robotic announcer]", per-line emotion tag, stability 0.5. Files are named after
the exact XACT cue names in SoundManager.PlayText (ttf_*), so Stage 6 audio can
drop them straight in. The two defeat lines (missionFailed / gameOver) are NEW --
the original defeat screen was silent.

Reads key from ../../.env (ELEVENLABSTOKEN); never prints it.
Run: PYTHONIOENCODING=utf-8 python tools/tts/eleven_announcer_final.py
"""
import json
import os
import sys
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "announcer_final")
MODEL_ID = "eleven_v3"
STABILITY = 0.5                       # Natural -- keeps tags responsive
VOICE = ("brian", "nPczCjzI2devNBz1zQrb")
PERSONA = "[robotic announcer]"       # the locked meta game-voice layer

# (cue_name, emotion_tag, spoken_words) -- cue_name == SoundManager ttf_* cue
LINES = [
    ("ttf_warning",            "[urgent]",     "WARNING!"),
    ("ttf_danger",             "[urgent]",     "DANGER!"),
    ("ttf_powerup",            "[triumphant]", "POWER UP!"),
    ("ttf_waveCompleted",      "[proud]",      "WAVE COMPLETED!"),
    ("ttf_getReady",           "[hyped]",      "GET READY!"),
    ("ttf_challengeUnlocked",  "[triumphant]", "CHALLENGE UNLOCKED!"),
    ("ttf_cheatUnlocked",      "[amused]",     "CHEAT UNLOCKED!"),
    ("ttf_levelUnlocked",      "[triumphant]", "LEVEL UNLOCKED!"),
    ("ttf_difficultyUnlocked", "[triumphant]", "DIFFICULTY UNLOCKED!"),
    ("ttf_awardmentUnlocked",  "[proud]",      "AWARDMENT UNLOCKED!"),
    ("ttf_missionFailed",      "[grave]",      "MISSION FAILED!"),   # NEW
    ("ttf_gameOver",           "[grave]",      "GAME OVER!"),        # NEW
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
    only = set(sys.argv[1:])  # optional: regenerate only the named cue(s)
    lines = [ln for ln in LINES if not only or ln[0] in only]
    ok = 0
    for cue, emotion, words in lines:
        text = f"{PERSONA}{emotion} {words}"
        path = os.path.join(OUT_DIR, cue + ".mp3")
        try:
            n = synth(token, VOICE[1], text, path)
            print(f"OK   {cue + '.mp3':28} {n:>7}b   {text!r}")
            ok += 1
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:200]
            print(f"FAIL {cue + '.mp3':28} HTTP {e.code} {e.reason}  {detail}")
        except Exception as e:
            print(f"FAIL {cue + '.mp3':28} {e}")
    print(f"\n{ok}/{len(lines)} generated ({VOICE[0]}) -> {OUT_DIR}")


if __name__ == "__main__":
    main()
