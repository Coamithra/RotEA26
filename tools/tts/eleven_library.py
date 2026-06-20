#!/usr/bin/env python
"""Confirm subscription tier, then search the ElevenLabs shared voice library for
natively-British cinematic narrator voices (male + female). Prints candidates with
voice_id + preview_url; never prints the token.

Run: PYTHONIOENCODING=utf-8 python tools/tts/eleven_library.py
"""
import json
import os
import sys
import urllib.parse
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")

# narration-flavored signals to surface the right voices from a big result set
GOOD_USE = {"narrative_story", "audiobook", "characters_animation", "informative_educational"}
GOOD_DESC = {"deep", "mature", "calm", "meditative", "authoritative", "gravelly",
             "intense", "classy", "formal", "wise", "crisp", "hoarse", "raspy", "pleasant"}


def load_token():
    with open(ENV_PATH, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("ELEVENLABSTOKEN="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("ELEVENLABSTOKEN not found in .env")


def api_get(path, token):
    req = urllib.request.Request(
        "https://api.elevenlabs.io" + path,
        headers={"xi-api-key": token, "accept": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def score(v):
    s = 0
    uc = (v.get("use_case") or "")
    if uc in GOOD_USE:
        s += 2
    if (v.get("descriptive") or "") in GOOD_DESC:
        s += 2
    name = (v.get("name") or "").lower()
    for kw in ("narrat", "cinemat", "story", "epic", "deep", "trailer", "documentary", "british", "lord", "sir"):
        if kw in name:
            s += 1
    return s


def search(token, gender, term=None, n=100):
    q = {"gender": gender, "accent": "british", "language": "en", "page_size": n}
    if term:
        q["search"] = term
    params = urllib.parse.urlencode(q)
    data = api_get("/v1/shared-voices?" + params, token)
    return data.get("voices", [])


def show(label, voices, cap=12):
    print(f"\n=== {label} ({len(voices)} found, top {cap}) ===")
    ranked = sorted(voices, key=score, reverse=True)
    for v in ranked[:cap]:
        print(f"  {v.get('voice_id'):24} {(v.get('name') or '')[:22]:22} "
              f"age={v.get('age')} desc={v.get('descriptive')} use={v.get('use_case')}")
        purl = v.get("preview_url")
        if purl:
            print(f"      preview: {purl}")


def main():
    token = load_token()
    try:
        sub = api_get("/v1/user/subscription", token)
    except urllib.error.HTTPError as e:
        sys.exit(f"AUTH FAILED: HTTP {e.code} {e.reason}")
    rem = (sub.get("character_limit") or 0) - (sub.get("character_count") or 0)
    print(f"=== SUBSCRIPTION ===\n  tier={sub.get('tier')}  used={sub.get('character_count')}"
          f"  limit={sub.get('character_limit')}  remaining={rem}")

    try:
        show("BRITISH MALE -- cinematic/epic/deep", search(token, "male", term="cinematic epic deep trailer"), cap=14)
        show("BRITISH MALE -- villain/aristocratic", search(token, "male", term="villain aristocratic sinister"), cap=10)
        show("BRITISH FEMALE -- ethereal/elegant", search(token, "female", term="ethereal elegant cinematic"), cap=8)
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")[:300]
        sys.exit(f"library search failed: HTTP {e.code} {e.reason}  {detail}")


if __name__ == "__main__":
    main()
