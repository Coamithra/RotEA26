#!/usr/bin/env python
"""Dump the full ElevenLabs shared-library record for a voice (default: Victor),
so we can read its category, creator, terms, and usage limits before shipping.

Run: PYTHONIOENCODING=utf-8 python tools/tts/eleven_voiceinfo.py [voice_id]
"""
import json
import os
import sys
import urllib.parse
import urllib.request

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")
TARGET = sys.argv[1] if len(sys.argv) > 1 else "U1nBX3lzKSM937PaYYfk"  # Victor


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


def find_in_shared(token):
    # try a few queries; Victor is British/male/narrative
    for params in (
        {"gender": "male", "accent": "british", "page_size": 100},
        {"search": "Victor", "page_size": 100},
        {"page_size": 100},
    ):
        data = api_get("/v1/shared-voices?" + urllib.parse.urlencode(params), token)
        for v in data.get("voices", []):
            if v.get("voice_id") == TARGET:
                return v
    return None


def main():
    token = load_token()
    v = find_in_shared(token)
    if not v:
        sys.exit(f"voice {TARGET} not found in shared library queries")

    # the fields that actually matter for crediting / licensing
    keys = ["name", "voice_id", "public_owner_id", "category", "accent", "gender",
            "age", "descriptive", "language", "free_users_allowed",
            "live_moderation_enabled", "notice_period", "cloned_by_count",
            "usage_character_count_1y", "featured", "date_unix"]
    print("=== KEY FIELDS ===")
    for k in keys:
        if k in v:
            print(f"  {k:26} {v[k]}")

    desc = v.get("description")
    if desc:
        print("\n=== DESCRIPTION (creator's own words / any terms) ===")
        print("  " + desc.replace("\n", "\n  "))

    # surface any other keys we didn't explicitly list, so nothing hides
    extra = {k: val for k, val in v.items()
             if k not in keys and k not in ("description", "preview_url")}
    if extra:
        print("\n=== OTHER FIELDS ===")
        print(json.dumps(extra, indent=2))


if __name__ == "__main__":
    main()
