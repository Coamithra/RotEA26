#!/usr/bin/env python
"""Narrator bake-off: a melodramatic, almost-Shakespearean reader (Charles Dance
reading dumb novels) for the between-level / commentary prose.

The comedy is in playing silly content DEAD straight -- grand, solemn, sincere,
never winking. Long-form prose suits v3 (short prompts are its weak spot), so we
use Creative stability (0.0) for maximum theatrical range and let [dramatic
pause] do the timing.

Reads key from ../../.env (ELEVENLABSTOKEN); never prints it. Saves to
tools/tts/out/narrator/. Run: PYTHONIOENCODING=utf-8 python tools/tts/eleven_narrator.py
"""
import json
import os
import sys
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "narrator")
MODEL_ID = "eleven_v3"
STABILITY = 0.0  # Creative -- most expressive / most tag-responsive

# NARRATOR LOCKED: Victor -- deep, old, native British. The cinematic elder.
VOICES = [
    ("victor", "U1nBX3lzKSM937PaYYfk"),
]

# The REAL post-level story crawls from CreditsScene.SetupLevel1/2/3 -- verbatim game
# text, with ellipses + [dramatic pause]s carrying the LotR-opening cadence. Level 3
# has two endings in the code: Hard+ (the "true" victory) and normal (Lieutenant only).
CRAWL1 = (
    "As the debris of the destroyed Fleet Commander Drone rushes past your cockpit... "
    "the remaining alien ships scatter... and retreat. [dramatic pause] The Earth... is "
    "saved. For now. [dramatic pause] But you know that your home will never truly be "
    "safe... not while the aliens are allowed to fester, like a cancerous sore upon the "
    "solar system. [dramatic pause] [wistful] The threat must be stopped. It is time... "
    "to take the fight to them. [dramatic pause] To Mars."
)
CRAWL2 = (
    "Having fought your way past the alien defenses... as well as Martian wildlife... "
    "you approach the invaders' base. [dramatic pause] Cannons blazing, you make your "
    "way inside. [dramatic pause] Your mission is clear: to find, and dispatch, the alien "
    "Overmind... once and for all. [dramatic pause] As you enter the azure fortress "
    "through one of the many tunnels... a chill runs down your spine. [dramatic pause] "
    "It is quiet. [dramatic pause] Too quiet."
)
CRAWL3_HARD = (
    "You have done it. [dramatic pause] The Overmind... has been destroyed. "
    "[dramatic pause] Chaos is already spreading throughout the alien ranks as you make "
    "your way to the exit. Without their leader to sustain them, the aliens' empire will "
    "crumble... into oblivion. [dramatic pause] As you leave the planet's atmosphere, the "
    "steerless alien base self-destructs... engulfing you in the bright red glow of "
    "victory. [dramatic pause] The game is over. The Earth is safe. [dramatic pause] Well done."
)
CRAWL3_NORMAL = (
    "Congratulations. You are victorious. [dramatic pause] The Evil Aliens' base lies in "
    "ruins. Their fleet, decimated. Their leader, reduced to pulp. [dramatic pause] Yet "
    "you know... that it was only a Lieutenant that you have slain. [dramatic pause] The "
    "Overmind still lives. [dramatic pause] You know that one day the aliens will be "
    "back... and it will be up to you, to once again save the day. [dramatic pause] And "
    "it will be much... harder... this time."
)

# (slug, text) -- the clean cinematic recipe; Victor is natively British
LINES = [
    ("level1",        "[British accent][cinematic][reverent] " + CRAWL1),
    ("level2",        "[British accent][cinematic][reverent] " + CRAWL2),
    ("level3_hard",   "[British accent][cinematic][reverent] " + CRAWL3_HARD),
    ("level3_normal", "[British accent][cinematic][reverent] " + CRAWL3_NORMAL),
]


def load_token():
    with open(ENV_PATH, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("ELEVENLABSTOKEN="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("ELEVENLABSTOKEN not found in .env")


def synth(token, voice_id, text, out_path, stability=STABILITY):
    body = json.dumps({
        "text": text,
        "model_id": MODEL_ID,
        "voice_settings": {"stability": stability, "similarity_boost": 0.75, "use_speaker_boost": True},
    }).encode("utf-8")
    req = urllib.request.Request(
        f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}?output_format=mp3_44100_128",
        data=body,
        headers={"xi-api-key": token, "accept": "audio/mpeg", "content-type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=90) as r:
        audio = r.read()
    with open(out_path, "wb") as f:
        f.write(audio)
    return len(audio)


def main():
    token = load_token()
    os.makedirs(OUT_DIR, exist_ok=True)
    args = set(sys.argv[1:])
    natural = "natural" in args            # "natural" -> stability 0.5 + "_natural" suffix
    args.discard("natural")
    only = args                            # remaining args: regenerate only those slug(s)
    stability = 0.5 if natural else STABILITY
    suffix = "_natural" if natural else ""
    ok = total = 0
    for vlabel, vid in VOICES:
        for slug, text in LINES:
            if only and slug not in only:
                continue
            total += 1
            name = f"{vlabel}_{slug}{suffix}.mp3"
            path = os.path.join(OUT_DIR, name)
            try:
                n = synth(token, vid, text, path, stability)
                print(f"OK   {name:20} {n:>7}b")
                ok += 1
            except urllib.error.HTTPError as e:
                detail = e.read().decode("utf-8", "replace")[:200]
                print(f"FAIL {name:20} HTTP {e.code} {e.reason}  {detail}")
            except Exception as e:
                print(f"FAIL {name:20} {e}")
    print(f"\n{ok}/{total} generated -> {OUT_DIR}")


if __name__ == "__main__":
    main()
