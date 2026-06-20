#!/usr/bin/env python
"""Probe the ElevenLabs API with the key in ../../.env (ELEVENLABSTOKEN).

Prints ONLY non-secret info: whether the key authenticates, the subscription
tier, which models are available (so we know if v3 / audio-tag support is
reachable), and a short list of voices. The token itself is never printed.

Run:  PYTHONIOENCODING=utf-8 python tools/tts/eleven_probe.py
"""
import json
import os
import sys
import urllib.request
import urllib.error

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV_PATH = os.path.join(REPO_ROOT, ".env")


def load_token():
    if not os.path.exists(ENV_PATH):
        sys.exit(f"No .env at {ENV_PATH}")
    with open(ENV_PATH, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            if k.strip() == "ELEVENLABSTOKEN":
                return v.strip().strip('"').strip("'")
    sys.exit("ELEVENLABSTOKEN not found in .env")


def api_get(path, token):
    req = urllib.request.Request(
        "https://api.elevenlabs.io" + path,
        headers={"xi-api-key": token, "accept": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def main():
    token = load_token()
    print(f"token loaded: len={len(token)} (value not shown)\n")

    try:
        sub = api_get("/v1/user/subscription", token)
    except urllib.error.HTTPError as e:
        sys.exit(f"AUTH FAILED: HTTP {e.code} {e.reason} — key may be invalid/expired")
    except Exception as e:
        sys.exit(f"request error: {e}")

    print("=== SUBSCRIPTION ===")
    print(f"  tier:            {sub.get('tier')}")
    print(f"  char_count:      {sub.get('character_count')}")
    print(f"  char_limit:      {sub.get('character_limit')}")
    remaining = (sub.get("character_limit") or 0) - (sub.get("character_count") or 0)
    print(f"  chars remaining: {remaining}")

    print("\n=== MODELS (can_do_text_to_speech) ===")
    try:
        models = api_get("/v1/models", token)
        for m in models:
            if m.get("can_do_text_to_speech"):
                print(f"  {m.get('model_id'):32} {m.get('name')}")
    except Exception as e:
        print(f"  (could not list models: {e})")

    print("\n=== VOICES (first 25) ===")
    try:
        voices = api_get("/v1/voices", token).get("voices", [])
        for v in voices[:25]:
            labels = v.get("labels") or {}
            desc = ", ".join(f"{k}={v2}" for k, v2 in labels.items())
            print(f"  {v.get('voice_id'):24} {v.get('name'):20} {desc}")
        print(f"  ...total voices: {len(voices)}")
    except Exception as e:
        print(f"  (could not list voices: {e})")


if __name__ == "__main__":
    main()
