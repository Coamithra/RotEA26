#!/usr/bin/env python
"""Protagonist VO for the office "Quick Sync" boss call.

THE JOKE: Alex narrated his own game. So he is voiced by the SAME actor as the
between-level interstitials (Victor, the cinematic elder) -- but here we have to
UN-cinematic that grandiose voice into just a tired, faintly neurotic normal guy
on a work call. Same actor; opposite performance.

How we de-grand it (vs the narrator recipe):
  * stability 1.0 (Robust) -- the LEAST theatrical of v3's three modes; kills the
    swooping, performed delivery. (Narrator used 0.0 Creative for max drama; 0.5
    Natural still let him perform -- the "rough/too grand" bake-off.)
  * NO [British accent] tag -- Victor is natively British; the tag just re-triggers
    "BBC narrator". Drop it.
  * casual / nervous / muttering tags instead of [cinematic][reverent], and plain
    punctuation (no [dramatic pause]).
Override per run with env vars: ALEX_STABILITY (0.0/0.5/1.0) and ALEX_SUFFIX (adds
to the filename so you can A/B without clobbering). Boss is intentionally NOT voiced.

Slugs are <node>_<choiceindex> so office.js maps a reply to its clip
(vo/alex_<slug>.mp3). Reads key from ../../.env (ELEVENLABSTOKEN); never prints it.
Writes mp3s into wwwroot/office/vo/.

Bake-off:   PYTHONIOENCODING=utf-8 python tools/tts/eleven_alex.py start_1 chair_2 resign3_1
Full set:   PYTHONIOENCODING=utf-8 python tools/tts/eleven_alex.py
"""
import json
import os
import sys
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
OUT_DIR = os.path.join(REPO_ROOT, "web", "EvilAliensWeb", "wwwroot", "office", "vo")
MODEL_ID = "eleven_v3"
STABILITY = float(os.environ.get("ALEX_STABILITY", "0.5"))   # Natural -- Creative (0.0) drifted the accent too hard (Indian/American/British roulette)
LANG = os.environ.get("ALEX_LANG", "en")                     # ISO 639-1 language_code: pin English language + normalization (curbs non-English drift)
SUFFIX = os.environ.get("ALEX_SUFFIX", "")                   # A/B without overwriting
VOICE_ID = "U1nBX3lzKSM937PaYYfk"                            # Victor -- the interstitial narrator, same actor

# (slug, tagged text). No [British accent]; casual/nervous tags; plain punctuation.
# Slug = <node>_<choiceindex> -- must match office.js's mapping.
LINES = [
    # Tag recipe (v3): [strong British accent] up front -- the DOCUMENTED v3 accent tag
    # (docs example: [strong French accent]); bare [british] still wandered. Then casual/
    # conversational/candid/wry mood tags, no pace/inline tags. NOTE: Victor is a
    # Professional Voice Clone, which v3 "does not fully optimize" (their words) -> some
    # accent variance is baked in; cherry-pick takes with audition.py. v3's only relevant
    # voice_setting is STABILITY (Natural here); similarity_boost/style do nothing on v3.
    # start_1 is the user's approved tagging; the rest follow it, varied per beat.
    ("start_1",   "[strong British accent][casual][conversational][candid][wry] Two weeks' notice, Jordan. I practised this in the shower. Was going to say something about personal growth. ...You can fill it in yourself."),
    ("start_2",   "[strong British accent][casual][nervous][rambling] Total misfire. You know how you type things just to get them out of your body, and then the cat walks across the keyboard and -- anyway, we're good. We're SO good."),
    ("start_3",   "[strong British accent][casual][deadpan][unconvincing] I'm fine. The quarter's fine. I just stopped sleeping. Frees up a lot of hours. Why are you writing that down? It's fine."),
    ("start_4",   "[strong British accent][casual][quiet][candid] Little ships, big explosions, a guy with a laser. I started building it at night. I don't know if that's a dream, Jordan, or just what happens when you stop sleeping."),
    ("games1_1",  "[strong British accent][casual][dry][wry] You can put lipstick on an expense report, but it's still an expense report. I want to build the one where the guy actually gets the laser. Where something's finished, for once."),
    ("games1_2",  "[strong British accent][casual][reluctant][quiet] ...I hate that I'm asking, but. What achievements?"),
    ("games2_1",  "[strong British accent][casual][dry][resigned] You know what? That's the perfect number to quit on. Tell Karen she can have my Bronze. I want it weighing on her."),
    ("games2_2",  "[strong British accent][casual][incredulous] You telling me forty people have been outfiling me? ...Tell me who's in first."),
    ("resign1_1", "[strong British accent][casual][candid][a bit irritated] You met her twice, Jordan. Once on Zoom, and once at the holiday party. When you called her my home stakeholder."),
    ("resign1_2", "[strong British accent][casual][candid][thinking out loud] It's about me. Probably. Maybe. I've spent six years making other people's quarters look good. I'd like to make one bad decision that's entirely my own."),
    ("resign1_3", "[strong British accent][casual][unsure][quiet] It is a big swing, isn't it. Maybe the headspace thing is real. Maybe I'm not -- maybe I just need to--"),
    ("resign2_1", "[strong British accent][casual][wary][quiet] The one Gary has? With the little wheel on the side?"),
    ("resign2_2", "[strong British accent][casual][candid][firm] I don't want Senior in front of a job I can't explain to my own mother, Jordan. I want out of the building. With the lights coming up behind me, like the end of a movie."),
    ("chair_1",   "[strong British accent][casual][uneasy][candid] That's not a perk, Jordan, that's a threat. I'd be haunted by Gary's lower back for years. I don't want to be remembered by you or this chair. I want to be forgotten."),
    ("chair_2",   "[strong British accent][casual][flustered][rambling] Okay -- okay, the chair is genuinely -- no. No. See, this is exactly what you do, you find the one thing -- put the chair away, Jordan. Put it away."),
    ("resign3_1", "[strong British accent][casual][quiet][resigned][candid] It sounds small when you say it. Which is annoying, because it is small. I still want to try."),
    ("resign3_2", "[strong British accent][casual][dry][knowing] There are bosses too, Jordan. There's a final boss, actually. Real piece of work, never lets anybody leave. You'd see a lot of yourself in him."),
    ("resign3_3", "[strong British accent][casual][quiet][candid] Tell Karen she can have my parking spot. Tell Gary the chair is all his. ...Goodbye, Jordan."),
]


def load_token():
    with open(ENV_PATH, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("ELEVENLABSTOKEN="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("ELEVENLABSTOKEN not found in .env")


def synth(token, text, out_path):
    body = json.dumps({
        "text": text,
        "model_id": MODEL_ID,
        "language_code": LANG,   # lock language + normalization (en); helps the non-English accent drift
        "voice_settings": {"stability": STABILITY, "use_speaker_boost": True},  # v3's reference-adherence lever is STABILITY; similarity_boost/style are v2-only (ignored on v3)
    }).encode("utf-8")
    req = urllib.request.Request(
        f"https://api.elevenlabs.io/v1/text-to-speech/{VOICE_ID}?output_format=mp3_44100_128",
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
    only = set(sys.argv[1:])               # optional: only these slugs (a bake-off)
    print(f"[stability {STABILITY}  suffix '{SUFFIX}']")
    ok = total = 0
    for slug, text in LINES:
        if only and slug not in only:
            continue
        total += 1
        name = f"alex_{slug}{SUFFIX}.mp3"
        path = os.path.join(OUT_DIR, name)
        try:
            n = synth(token, text, path)
            print(f"OK   {name:26} {n:>7}b")
            ok += 1
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:200]
            print(f"FAIL {name:26} HTTP {e.code} {e.reason}  {detail}")
        except Exception as e:
            print(f"FAIL {name:26} {e}")
    print(f"\n{ok}/{total} generated -> {OUT_DIR}")


if __name__ == "__main__":
    main()
