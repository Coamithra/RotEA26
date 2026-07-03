# ---------------------------------------------------------------------------
# install_classic.py — install the "classic" music cue from an EXTERNAL track
# instead of the XACT banks.
#
# WHY: the "classic" song (Songs.Classic -> songFiles[5] -> Content/music/
# classic.ogg; played by the retro minigames AsteroidChase / BraineroidsLevel /
# CrazyGame) was replaced with a bespoke "Evil Aliens Revenged" track the user
# authored to be loopable. It is NOT in the recovered banks, so build_audio.py
# no longer cracks "classic" — this tool installs it, the same way
# build_channelswap.py owns the one port-era SFX cue that isn't in the banks.
#
# What it does (offline, committed output — mirrors the tools/audio philosophy):
#   1. copy the source OGG straight to wwwroot/Content/music/classic.ogg
#      (the source is already OGG Vorbis 44100 stereo, so a copy avoids a
#      decode->re-encode generation loss; no need to round-trip through PCM).
#   2. run pymusiclooper on it and take the tool's top-ranked loop pair — the
#      track has a ~75s intro then a seamless body, which is exactly what
#      pymusiclooper is for (the card said "use the python music looping tool").
#   3. write those loop points into music.json's "classic" entry, preserving
#      every other cue's entry.
#
# Re-run after replacing the source track; don't hand-edit classic.ogg /
# music.json. The source lives in new_assets_raw/ (gitignored raw assets); the
# committed classic.ogg is the shipped artifact. build_audio.py calls this at
# the end of a full rebuild when the source is present (else the committed
# classic is left untouched).
#
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py --dry-run
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py --source <path>
# ---------------------------------------------------------------------------
import argparse
import json
import os
import shutil
import warnings

import soundfile as sf

warnings.filterwarnings("ignore")  # librosa/numpy log10 divide-by-zero spam

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MUSIC_DIR = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot", "Content", "music")
MANIFEST = os.path.join(MUSIC_DIR, "music.json")

CUE = "classic"
DEFAULT_SOURCE = os.path.join(ROOT, "new_assets_raw", "EvilAliensRevengedLoopable.ogg")


def find_loop(path):
    """Return (loopStart, loopEnd, score, click) for the source track, using
    pymusiclooper's own top-ranked loop pair. Import is late (heavy + optional)."""
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from refine_loops import splice_click  # shared splice-click metric
    from pymusiclooper.core import MusicLooper

    audio, rate = sf.read(path, dtype="float32", always_2d=True)
    ml = MusicLooper(path)
    pairs = ml.find_loop_pairs()
    if not pairs:
        raise RuntimeError(f"pymusiclooper found no loop in {path}")
    # pymusiclooper ranks pairs by loop quality; [0] is its best pick. Print a
    # few so a re-run's choice is auditable.
    print("  pymusiclooper candidates (top 5):")
    for pr in pairs[:5]:
        s = ml.samples_to_seconds(pr.loop_start)
        e = ml.samples_to_seconds(pr.loop_end)
        print(f"    {s:8.3f} -> {e:8.3f}  len={e - s:7.2f}s  "
              f"score={pr.score:.4f}  click={splice_click(audio, rate, s, e):.2f}")
    best = pairs[0]
    s = ml.samples_to_seconds(best.loop_start)
    e = ml.samples_to_seconds(best.loop_end)
    return round(s, 4), round(e, 4), best.score, splice_click(audio, rate, s, e)


def install(source=DEFAULT_SOURCE, dry_run=False):
    if not os.path.exists(source):
        print(f"  source missing: {source} (skip — committed classic.ogg left as-is)")
        return False
    out = os.path.join(MUSIC_DIR, CUE + ".ogg")

    info = sf.info(source)
    if info.format != "OGG" or info.subtype != "VORBIS":
        raise SystemExit(f"expected OGG/VORBIS source, got {info.format}/{info.subtype}")
    duration = round(info.frames / info.samplerate, 4)

    loop_start, loop_end, score, click = find_loop(source)
    entry = {
        "file": f"Content/music/{CUE}.ogg",
        "loopStart": loop_start,
        "loopEnd": loop_end,
        "duration": duration,
        # End of the once-only intro = the loop start, so refine_loops.py won't
        # later pull loopStart in front of the intro.
        "introEnd": loop_start,
    }
    print(f"  {CUE:10} {info.samplerate}Hz ch={info.channels} {duration:.3f}s"
          f"  loop[{loop_start:.3f}..{loop_end:.3f}] len={loop_end - loop_start:.2f}s"
          f"  score={score:.3f} click={click:.2f}")

    if dry_run:
        print("  dry-run: classic.ogg not copied, music.json not written.")
        return True

    shutil.copyfile(source, out)
    manifest = json.load(open(MANIFEST)) if os.path.exists(MANIFEST) else {}
    manifest[CUE] = entry
    with open(MANIFEST, "w") as f:
        json.dump(manifest, f, indent=2)
    size = os.path.getsize(out) / 1024
    print(f"  -> {os.path.relpath(out, ROOT)} ({size:.0f}KB) + music.json[{CUE}] updated")
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source", default=DEFAULT_SOURCE,
                    help="source OGG (default: new_assets_raw/EvilAliensRevengedLoopable.ogg)")
    ap.add_argument("--dry-run", action="store_true",
                    help="print the chosen loop without writing classic.ogg / music.json")
    args = ap.parse_args()
    install(source=args.source, dry_run=args.dry_run)


if __name__ == "__main__":
    main()
