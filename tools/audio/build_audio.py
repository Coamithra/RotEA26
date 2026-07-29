# ---------------------------------------------------------------------------
# build_audio.py — Stage 6 audio asset build (offline, committed outputs).
#
# Mirrors the Stage 3/5 philosophy: derive web assets from the recovered files
# with a reproducible Python script and commit the outputs.
#
#   SFX     : crack the XACT banks -> wwwroot/Content/sfx/<cue>.wav   (PCM_16)
#   Speech  : ElevenLabs "Brian" renders (mp3) -> wwwroot/Content/sfx/ttf_*.wav
#   Narrate : ElevenLabs "Victor" renders (mp3) -> wwwroot/Content/vo/*.wav
#   Music   : crack the banks -> wwwroot/Content/music/<cue>.ogg (Vorbis) +
#             music.json loop manifest. Loop points come straight from XACT: the
#             .xsb play-wave events loop count 255 (= infinite) the whole wave;
#             the wave LoopRegions are all (0,0), so there are NO partial loops.
#             A 2-wave music cue is an authored intro (loop count 0, plays once)
#             followed by a body wave (loop count 255, loops whole).
#
# Run from the repo root:  PYTHONIOENCODING=utf-8 python tools/audio/build_audio.py
# Re-run after changing the source banks or the TTS renders.
# ---------------------------------------------------------------------------
import json
import os
import sys

import numpy as np
import soundfile as sf

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xact  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
WB = os.path.join(ROOT, "extracted", "584E07D1", "Content", "SFX", "Wave Bank.xwb")
SB = os.path.join(ROOT, "extracted", "584E07D1", "Content", "SFX", "Sound Bank.xsb")
WWW = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot", "Content")
TTS = os.path.join(ROOT, "tools", "tts", "out")

SFX_DIR = os.path.join(WWW, "sfx")
MUSIC_DIR = os.path.join(WWW, "music")
VO_DIR = os.path.join(WWW, "vo")

# Non-speech, non-music cues the game actually plays (PlayCue / Play call sites).
# A cue with >1 wave (e.g. lazershot=[5,7]) plays its first wave as the body.
SFX_CUES = [
    "expl1", "expl2", "fire", "head asplode", "small head asplode",
    "lazershot", "lazercharge", "lazershotnoloop", "newwave", "blast",
    "powerup", "targetacquired", "hit_boss", "bugdies", "bees", "wasp",
    "spiderbossdeath", "evillaugh", "usepowerup",
]

# Cues whose shipped .wav is HAND-MADE and must never be re-derived from the bank.
# These three were re-recorded by hand in Reaper to strip the static background noise
# the originals carry, and were committed over the bank-derived files (24-bit stereo
# where the bank gives 16-bit). build_sfx SKIPS them: a rebuild would otherwise silently
# restore the noisy originals, and nothing would complain at runtime -- SoundManager
# .GetEffect swallows every load exception and caches null, so a broken or regressed sfx
# never announces itself, it just stops sounding right. Same rule as channelswap.wav,
# which pick_channelswap.py owns (tools/CLAUDE.md, Audio).
# To genuinely re-derive one from the bank, drop it from this set for that run --
# deliberately not a CLI flag, because casual use is exactly what this guards against.
HAND_OWNED_SFX = frozenset({
    "head asplode", "small head asplode", "spiderbossdeath",
})

# Music cues -> the SongInstance.songFiles ids. Cues with two waves are an
# authored intro + loop body (the intro plays once, then the body loops).
# "classic"/"classicclean"/"lastsignal" are NOT here: each was replaced with a
# bespoke external track and is installed by install_external.py (called from
# main); build_music preserves their music.json entries so a rebuild never drops
# or clobbers them. The bank's own "sjaakslow" wave (a pitched-down cut of the
# menu theme) is dead — "lastsignal" took over the CreditsScene crawl.
MUSIC_CUES = ["stage1", "stage2", "stage3", "bach",
              "sjaak", "kylikova"]


def sanitize(name):
    return name.lower().replace(" ", "_")


def to_unit(a):
    """Clamp WMA decode overshoot (peaks can hit ~1.3) without hard-clipping:
    attenuate the whole buffer if it exceeds unity."""
    peak = float(np.abs(a).max()) if a.size else 0.0
    if peak > 1.0:
        a = a * (0.99 / peak)
    return a


def write_ogg(path, audio, rate):
    """libsndfile's Vorbis encoder aborts on a single multi-MB write, so stream
    the buffer in blocks. The data must be C-contiguous (the xWMA decode returns
    a transposed/F-contiguous view)."""
    audio = np.ascontiguousarray(audio, dtype=np.float32)
    with sf.SoundFile(path, "w", samplerate=rate, channels=audio.shape[1],
                      format="OGG", subtype="VORBIS") as f:
        for i in range(0, len(audio), 1 << 16):
            f.write(audio[i:i + (1 << 16)])


def build_sfx(entries, cues):
    os.makedirs(SFX_DIR, exist_ok=True)
    for cue in SFX_CUES:
        if cue in HAND_OWNED_SFX:
            print(f"  SKIP {cue:20} hand-recorded replacement, NOT rebuilt from the bank "
                  f"({sanitize(cue)}.wav)")
            continue
        waves = cues[cue]
        a, rate = xact.decode(entries[waves[0]])
        a = to_unit(a)
        out = os.path.join(SFX_DIR, sanitize(cue) + ".wav")
        sf.write(out, a, rate, subtype="PCM_16")
        print(f"  sfx  {cue:20} wave{waves[0]:2} {a.shape[0]/rate:5.2f}s {rate}Hz -> {os.path.basename(out)}")


def _mp3_to_wav(src, dst):
    a, rate = sf.read(src, dtype="float32", always_2d=True)
    a = to_unit(a)
    sf.write(dst, a, rate, subtype="PCM_16")
    return a.shape[0] / rate, rate


def build_speech():
    os.makedirs(SFX_DIR, exist_ok=True)
    src_dir = os.path.join(TTS, "announcer_final")
    for fn in sorted(os.listdir(src_dir)):
        if not fn.endswith(".mp3"):
            continue
        dur, rate = _mp3_to_wav(os.path.join(src_dir, fn),
                                os.path.join(SFX_DIR, sanitize(fn[:-4]) + ".wav"))
        print(f"  vox  {fn[:-4]:24} {dur:5.2f}s {rate}Hz")


def build_narration():
    os.makedirs(VO_DIR, exist_ok=True)
    src_dir = os.path.join(TTS, "narrator")
    for fn in sorted(os.listdir(src_dir)):
        if not fn.endswith(".mp3"):
            continue
        dur, rate = _mp3_to_wav(os.path.join(src_dir, fn),
                                os.path.join(VO_DIR, sanitize(fn[:-4]) + ".wav"))
        print(f"  narr {fn[:-4]:24} {dur:5.2f}s {rate}Hz")


def build_music(entries, cues):
    os.makedirs(MUSIC_DIR, exist_ok=True)
    # Merge into the existing manifest so externally-installed cues (classic,
    # classicclean, lastsignal — via install_external.py) survive a rebuild
    # instead of being dropped.
    manifest_path = os.path.join(MUSIC_DIR, "music.json")
    manifest = json.load(open(manifest_path)) if os.path.exists(manifest_path) else {}
    for cue in MUSIC_CUES:
        waves = cues[cue]
        parts = [xact.decode(entries[w]) for w in waves]
        rate = parts[0][1]
        audio = np.concatenate([p[0] for p in parts], axis=0)
        audio = to_unit(audio)
        out = os.path.join(MUSIC_DIR, cue + ".ogg")
        write_ogg(out, audio, rate)
        total = audio.shape[0] / rate

        # XACT looped the whole wave (loop count 255). A multi-wave cue is an
        # authored intro (the leading waves, played once) + a body wave that
        # loops whole, so the loop starts after the intro; a single-wave cue
        # loops the whole track.
        if len(waves) >= 2:
            loop_start = sum(p[0].shape[0] for p in parts[:-1]) / rate
            kind = "intro+loop"
        else:
            loop_start = 0.0
            kind = "whole"
        loop_end = total
        manifest[cue] = {
            "file": f"Content/music/{cue}.ogg",
            "loopStart": round(loop_start, 4),
            "loopEnd": round(loop_end, 4),
            "duration": round(total, 4),
            # End of the once-only intro (0.0 for whole-wave cues). Records the
            # loop floor so refine_loops.py won't pull loopStart in front of it.
            "introEnd": round(loop_start, 4),
        }
        size = os.path.getsize(out) / 1024
        print(f"  mus  {cue:10} wave{waves} {total:6.1f}s {kind:10} "
              f"loop[{loop_start:6.1f}..{loop_end:6.1f}] {size:6.0f}KB")
    with open(manifest_path, "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"  -> {os.path.join('Content', 'music', 'music.json')} ({len(manifest)} tracks)")


def main():
    entries = xact.parse_wavebank(WB)
    cues = xact.parse_soundbank(SB)
    print(f"banks: {len(entries)} waves, {len(cues)} cues\n")
    print("SFX:")
    build_sfx(entries, cues)
    print("Speech (ElevenLabs Brian):")
    build_speech()
    print("Narration (ElevenLabs Victor):")
    build_narration()
    print("Music:")
    build_music(entries, cues)
    # classic / classicclean / lastsignal are bespoke external tracks, not bank
    # cues — install them (copy + pymusiclooper loop). A missing source leaves
    # that cue's committed .ogg in place.
    print("External music:")
    try:
        import install_external
        install_external.install()
    except ImportError:
        print("  (pymusiclooper not installed — skipped; run install_external.py later)")
    # The loop points written above are the raw XACT whole-wave points, which
    # seam under WebAudio's hard-splice loop. Refine them to waveform-matched
    # points (pymusiclooper). Optional — a missing pymusiclooper just leaves the
    # whole-wave points in place (re-run tools/audio/refine_loops.py later).
    print("Refining music loop points:")
    import importlib.util
    if importlib.util.find_spec("pymusiclooper") is None:
        print("  (pymusiclooper not installed — skipped; run refine_loops.py later)")
    else:
        import refine_loops
        refine_loops.run(dry_run=False)
    print("\ndone.")


def selftest():
    """Prove build_sfx never writes the hand-owned SFX -- without banks, PyAV, or
    touching a single file. Monkeypatches the two side-effecting calls (xact.decode,
    sf.write) and records which cues reach the writer.

    The negative control matters as much as the positive one: with HAND_OWNED_SFX
    emptied, every cue must write. Without it a build_sfx that wrote NOTHING (a
    typo'd loop, a stray early return) would pass the main assertion vacuously.
    """
    import unittest.mock as mock

    def run(hand_owned):
        written = []
        fake_entries = {0: object()}
        fake_cues = {cue: [0] for cue in SFX_CUES}
        with mock.patch.object(xact, "decode", return_value=(np.zeros((8, 1), np.float32), 44100)), \
             mock.patch.object(sf, "write", side_effect=lambda p, *a, **k: written.append(os.path.basename(p))), \
             mock.patch.object(os, "makedirs"), \
             mock.patch(__name__ + ".HAND_OWNED_SFX", hand_owned):
            build_sfx(fake_entries, fake_cues)
        return written

    ok = True
    protected = {sanitize(c) + ".wav" for c in HAND_OWNED_SFX}
    all_wavs = {sanitize(c) + ".wav" for c in SFX_CUES}

    print("Protected (must never be written by build_sfx):")
    for fn in sorted(protected):
        print(f"  {fn}")

    written = set(run(HAND_OWNED_SFX))
    leaked = written & protected
    missing = (all_wavs - protected) - written
    print(f"\n[1] real run: {len(written)} written, {len(protected)} skipped")
    if leaked:
        print(f"  FAIL: hand-owned file(s) rebuilt from the bank: {sorted(leaked)}")
        ok = False
    else:
        print(f"  PASS: none of the {len(protected)} hand-owned files was written")
    if missing:
        print(f"  FAIL: unprotected cue(s) not built: {sorted(missing)}")
        ok = False
    else:
        print(f"  PASS: all {len(all_wavs) - len(protected)} other cues still built")

    # Negative control: without the guard the very same call DOES clobber them.
    written = set(run(frozenset()))
    print(f"\n[2] negative control (HAND_OWNED_SFX emptied): {len(written)} written")
    if written == all_wavs:
        print(f"  PASS: all {len(all_wavs)} cues write, so [1] is a real guard, "
              f"not a no-op loop")
    else:
        print(f"  FAIL: expected all {len(all_wavs)}, got {len(written)}")
        ok = False

    print("\nSELFTEST", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(selftest())
    main()
