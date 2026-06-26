# ---------------------------------------------------------------------------
# refine_loops.py — replace SEAMING whole-wave loop points in music.json with
# WAVEFORM-MATCHED ones found by pymusiclooper, but ONLY for tracks that
# actually click.
#
# WHY: build_audio.py writes the loop points straight from XACT — single-wave
# cues loop the WHOLE wave (loopStart=0, loopEnd=duration) and multi-wave cues
# loop the whole body after the intro. WebAudio's native AudioBufferSourceNode
# loop (index.html `eaMusic`) is sample-accurate but does a HARD SPLICE at the
# boundary: if the sample at loopEnd doesn't connect to the sample at loopStart,
# you hear a click/seam every loop. The sister project Fighterproto loops
# flawlessly precisely because its loop points are pymusiclooper-derived,
# waveform-matched samples — same native loop, no seam. This tool brings RotEA26
# to parity. No playback-code change, no re-encode.
#
# NOT every track seams: most of these 2008 tracks already wrap cleanly as a
# whole wave (the composer made end ~= start). Re-looping those to a pymusiclooper
# sub-region would only DISCARD music for no gain — so we measure each track's
# actual splice CLICK first and refine only the ones above an audible threshold,
# choosing the candidate that minimises the click while staying long & musical.
# Measured on the committed banks, only stage1 / stage2 / classic click audibly
# (62.9 / 41.0 / 17.6, vs <6 for the rest, which matches "not so bad past lvl1").
#
# It only rewrites loopStart/loopEnd in music.json (file/duration/introEnd are
# preserved). build_audio.py calls this at the end of a full rebuild; it is also
# safe to run standalone after build_audio.py, and is idempotent (a refined
# low-click loop falls below the threshold and is left alone on re-run):
#
#   PYTHONIOENCODING=utf-8 python tools/audio/refine_loops.py            # apply
#   PYTHONIOENCODING=utf-8 python tools/audio/refine_loops.py --dry-run  # preview
#
# Don't hand-edit the loop points; tweak a track via OVERRIDES below.
# ---------------------------------------------------------------------------
import argparse
import json
import os
import warnings

import numpy as np
import soundfile as sf

warnings.filterwarnings("ignore")  # librosa/numpy log10 divide-by-zero spam

MUSIC_DIR = os.path.abspath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "web", "EvilAliensWeb", "wwwroot", "Content", "music"))
MANIFEST = os.path.join(MUSIC_DIR, "music.json")

# Intro length (seconds) for the authored intro+loop cues — the loop must not
# start before this or the once-only intro would be swallowed into the loop.
# Used only when the manifest entry has no "introEnd" (build_audio.py writes one
# on a full rebuild; these mirror the current values for a standalone run).
FLOORS = {"stage2": 13.7462, "stage3": 36.6875}

# Manual pins for any track whose auto-pick is musically wrong. Either give exact
# {"loopStart": s, "loopEnd": e} (seconds, used verbatim) or per-track policy
# tweaks {"min_len": secs, "floor": secs}. Empty = pure auto. (Mirrors the
# tools/font/overrides.json idea: code-derived default + a hand-tune escape hatch.)
OVERRIDES = {}

# --- selection policy ------------------------------------------------------
# Splice click metric: the per-sample STEP at the loop join (audio[loopEnd-1] ->
# audio[loopStart]) relative to the signal's normal local step size. ~1 = the
# jump is indistinguishable from ordinary waveform motion (seamless); a large
# value is an audible click. We only refine a track whose CURRENT loop clicks
# above AUDIBLE, pick the longest candidate that is itself below SEAMLESS (or the
# least-clicky if none clear it), and only commit it if it is at least IMPROVE x
# better than the current loop. pymusiclooper candidates already guarantee
# musical similarity; among those we minimise the raw click.
MULT_LADDER = (0.30, 0.20, 0.12)   # descending min-duration multipliers to find candidates
AUDIBLE = 10.0                     # only refine loops clicking louder than this
SEAMLESS = 3.0                     # a candidate at/below this is effectively click-free
IMPROVE = 0.6                      # chosen click must be <= AUDIBLE-track's old click * this
MIN_LEN_FRAC = 0.30                # a loop must cover at least this fraction of the track...
MIN_LEN_ABS = 20.0                 # ...or this many seconds, whichever is larger


def splice_click(audio, rate, ls, le):
    """Per-sample step at the loop join, normalised by the local step size."""
    n = audio.shape[0]
    a = int(round(ls * rate)) % n
    b = min(int(round(le * rate)), n - 1)
    pre = audio[max(0, b - 256):b]
    post = audio[a:a + 256]
    step = float(np.sqrt(np.mean((audio[b - 1] - audio[a]) ** 2)))
    base = float(np.median(np.concatenate(
        [np.abs(np.diff(pre, axis=0)), np.abs(np.diff(post, axis=0))]))) + 1e-9
    return step / base


def choose_loop(ml, audio, rate, floor, min_len, c_old):
    """Pick the best seamless loop for a clicking track, or None to keep current."""
    pairs = None
    for mult in MULT_LADDER:
        try:
            pairs = ml.find_loop_pairs(min_duration_multiplier=mult)
        except Exception:
            pairs = None
        if pairs:
            break
    cands = []
    for p in (pairs or []):
        s = ml.samples_to_seconds(p.loop_start)
        e = ml.samples_to_seconds(p.loop_end)
        if s < floor - 0.5 or e - s < min_len:
            continue
        cands.append((s, e, p.score, splice_click(audio, rate, s, e)))
    if not cands:
        return None
    seamless = [c for c in cands if c[3] < SEAMLESS]
    pick = max(seamless, key=lambda c: c[1] - c[0]) if seamless \
        else min(cands, key=lambda c: c[3])
    return pick if pick[3] <= c_old * IMPROVE else None


def run(dry_run=False):
    """Refine music.json's loop points in place. Importable so build_audio.py can
    call it as the last step of a full rebuild."""
    from pymusiclooper.core import MusicLooper   # imported late: heavy + optional

    manifest = json.load(open(MANIFEST))
    changed = 0
    for cue, info in manifest.items():
        path = os.path.join(MUSIC_DIR, cue + ".ogg")
        if not os.path.exists(path):
            print(f"  {cue:10} MISSING {path} (skip)")
            continue
        ov = OVERRIDES.get(cue, {})
        old_s, old_e = info["loopStart"], info["loopEnd"]

        if "loopStart" in ov and "loopEnd" in ov:        # exact manual pin
            new_s, new_e, score = ov["loopStart"], ov["loopEnd"], None
        else:
            audio, rate = sf.read(path, dtype="float32", always_2d=True)
            c_old = splice_click(audio, rate, old_s, old_e)
            if c_old < AUDIBLE:                          # already loops cleanly
                print(f"  {cue:10} keep {old_s:8.3f}->{old_e:8.3f}  click={c_old:5.1f} (< {AUDIBLE:g})")
                continue
            ml = MusicLooper(path)
            dur = ml.samples_to_seconds(ml.mlaudio.length)
            floor = ov.get("floor", info.get("introEnd", FLOORS.get(cue, 0.0)))
            min_len = ov.get("min_len", max(MIN_LEN_ABS, MIN_LEN_FRAC * dur))
            pick = choose_loop(ml, audio, rate, floor, min_len, c_old)
            if pick is None:
                print(f"  {cue:10} keep {old_s:8.3f}->{old_e:8.3f}  click={c_old:5.1f} (no better seamless loop)")
                continue
            new_s, new_e = round(pick[0], 4), round(pick[1], 4)
            score = f"click {c_old:.1f}->{pick[3]:.2f}, score {pick[2]:.3f}"

        info["loopStart"], info["loopEnd"] = new_s, new_e
        changed += 1
        tag = "pin" if score is None else score
        print(f"  {cue:10} {old_s:8.3f}->{old_e:8.3f}  =>  {new_s:8.3f}->{new_e:8.3f}"
              f"  len={new_e-new_s:7.2f}s  [{tag}]")

    if dry_run:
        print(f"\ndry-run: {changed} track(s) would change (music.json not written).")
        return
    with open(MANIFEST, "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"\n-> wrote {os.path.basename(MANIFEST)} ({changed} track(s) refined).")


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--dry-run", action="store_true",
                    help="print the chosen loop points without writing music.json")
    run(dry_run=ap.parse_args().dry_run)


if __name__ == "__main__":
    main()
