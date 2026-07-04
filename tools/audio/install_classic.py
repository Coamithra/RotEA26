# ---------------------------------------------------------------------------
# install_classic.py — install the two "classic" music cues from EXTERNAL
# tracks instead of the XACT banks.
#
# WHY: the retro tune has two bespoke external variants (neither in the recovered
# banks), so build_audio.py doesn't crack them — this tool installs both, the way
# build_channelswap.py owns the one port-era SFX cue:
#   * "classic"      (Songs.Classic)      — the full Japanese-vocal "Evil Aliens
#     Revenged" cut, served only as a reward on Hard+ challenges.
#   * "classicclean" (Songs.ClassicClean) — a lyric-free loopable instrumental,
#     the default for the tutorial + Easy/Medium challenges.
# SoundManager.ClassicForDifficulty() picks between them; both are played by the
# retro minigames (AsteroidChase / BraineroidsLevel / ClassicAliens / CrazyGame).
#
# What it does per cue (offline, committed output — mirrors the tools/audio
# philosophy):
#   1. copy the source OGG straight to wwwroot/Content/music/<cue>.ogg
#      (the sources are already OGG Vorbis 44100 stereo, so a copy avoids a
#      decode->re-encode generation loss; no need to round-trip through PCM).
#   2. run pymusiclooper on it and take the tool's top-ranked loop pair — each
#      track has an intro then a seamless body, which is exactly what
#      pymusiclooper is for.
#   3. write those loop points into music.json's "<cue>" entry, preserving
#      every other cue's entry.
#
# Re-run after replacing a source track; don't hand-edit the .ogg / music.json.
# The sources live in new_assets_raw/ (gitignored raw assets); the committed
# .ogg files are the shipped artifacts. build_audio.py calls install() at the end
# of a full rebuild — a missing source leaves that cue's committed artifact as-is.
#
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py            # both cues
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py --dry-run
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py --cue classicclean
#   PYTHONIOENCODING=utf-8 python tools/audio/install_classic.py --cue classic --source <path>
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

# Two variants of the retro tune, each a bespoke external track (not from the
# banks). "classic" = the full Japanese-vocal cut (the Hard+ challenge reward);
# "classicclean" = a lyric-free loopable instrumental (tutorial + Easy/Medium).
# Songs.Classic / Songs.ClassicClean pick between them via
# SoundManager.ClassicForDifficulty(). Each gets its own committed .ogg +
# music.json loop entry. A missing source leaves that cue's committed artifact
# untouched (safe in CI / fresh clones).
TRACKS = {
    "classic": os.path.join(ROOT, "new_assets_raw", "EvilAliensRevengedLoopable.ogg"),
    "classicclean": os.path.join(ROOT, "new_assets_raw", "classicaliensremixloopable_nolyrics.ogg"),
}
CUE = "classic"  # back-compat default for --source with no --cue
DEFAULT_SOURCE = TRACKS[CUE]


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


def install_one(cue, source, dry_run=False):
    if not os.path.exists(source):
        print(f"  {cue}: source missing: {source} (skip — committed {cue}.ogg left as-is)")
        return False
    out = os.path.join(MUSIC_DIR, cue + ".ogg")

    info = sf.info(source)
    if info.format != "OGG" or info.subtype != "VORBIS":
        raise SystemExit(f"expected OGG/VORBIS source, got {info.format}/{info.subtype}")
    duration = round(info.frames / info.samplerate, 4)

    loop_start, loop_end, score, click = find_loop(source)
    entry = {
        "file": f"Content/music/{cue}.ogg",
        "loopStart": loop_start,
        "loopEnd": loop_end,
        "duration": duration,
        # End of the once-only intro = the loop start, so refine_loops.py won't
        # later pull loopStart in front of the intro.
        "introEnd": loop_start,
    }
    print(f"  {cue:12} {info.samplerate}Hz ch={info.channels} {duration:.3f}s"
          f"  loop[{loop_start:.3f}..{loop_end:.3f}] len={loop_end - loop_start:.2f}s"
          f"  score={score:.3f} click={click:.2f}")

    if dry_run:
        print(f"  dry-run: {cue}.ogg not copied, music.json not written.")
        return True

    shutil.copyfile(source, out)
    manifest = json.load(open(MANIFEST)) if os.path.exists(MANIFEST) else {}
    manifest[cue] = entry
    with open(MANIFEST, "w") as f:
        json.dump(manifest, f, indent=2)
    size = os.path.getsize(out) / 1024
    print(f"  -> {os.path.relpath(out, ROOT)} ({size:.0f}KB) + music.json[{cue}] updated")
    return True


def install(dry_run=False):
    """Install every external classic-tune variant (classic + classicclean)."""
    ok = False
    for cue, source in TRACKS.items():
        ok = install_one(cue, source, dry_run=dry_run) or ok
    return ok


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--cue", choices=sorted(TRACKS),
                    help="install only this cue (default: all — classic + classicclean)")
    ap.add_argument("--source",
                    help="override the source OGG for --cue (default: the cue's TRACKS path)")
    ap.add_argument("--dry-run", action="store_true",
                    help="print the chosen loop without writing the .ogg / music.json")
    args = ap.parse_args()
    if args.cue or args.source:
        cue = args.cue or CUE
        source = args.source or TRACKS[cue]
        install_one(cue, source, dry_run=args.dry_run)
    else:
        install(dry_run=args.dry_run)


if __name__ == "__main__":
    main()
