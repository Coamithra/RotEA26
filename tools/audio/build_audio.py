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

# Music cues -> the SongInstance.songFiles ids. Cues with two waves are an
# authored intro + loop body (the intro plays once, then the body loops).
MUSIC_CUES = ["stage1", "stage2", "stage3", "bach", "classic",
              "sjaak", "kylikova", "sjaakslow"]


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
    manifest = {}
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
        }
        size = os.path.getsize(out) / 1024
        print(f"  mus  {cue:10} wave{waves} {total:6.1f}s {kind:10} "
              f"loop[{loop_start:6.1f}..{loop_end:6.1f}] {size:6.0f}KB")
    with open(os.path.join(MUSIC_DIR, "music.json"), "w") as f:
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
    print("\ndone.")


if __name__ == "__main__":
    main()
