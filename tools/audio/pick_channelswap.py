#!/usr/bin/env python
"""Install a chosen ElevenLabs candidate as the shipped "change the channel" cue.

The splash channel-flip (SplashScene index 1) plays SoundManager cue "channelswap"
(Content/sfx/channelswap.wav). It used to be numpy-synthesized static
(build_channelswap.py); this replaces it with a picked ElevenLabs sound-effect render.

Pattern mirrors install_classic.py: the chosen MP3 is copied to a committed
source-of-record (tools/audio/channelswap_source.mp3) and DECODED to the game's
mono 16-bit PCM WAV. Re-running with no slug reconverts from that committed source,
so the shipped WAV is reproducible without the throwaway candidates folder.

  # first time: pick a candidate by slug (from tools/audio/channelswap_out/)
  PYTHONIOENCODING=utf-8 python tools/audio/pick_channelswap.py 02_channel_static
  # later: reconvert from the committed source (e.g. after tweaking knobs below)
  PYTHONIOENCODING=utf-8 python tools/audio/pick_channelswap.py

NOTE: this SUPERSEDES tools/audio/build_channelswap.py as the owner of
channelswap.wav — don't re-run that synth or it clobbers this render.
"""
import argparse
import os
import shutil
import sys
import wave

import numpy as np
import av
from av.audio.resampler import AudioResampler

HERE = os.path.dirname(os.path.abspath(__file__))
WWW = os.path.normpath(os.path.join(HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot"))
OUT = os.path.join(WWW, "Content", "sfx", "channelswap.wav")
CAND_DIR = os.path.join(HERE, "channelswap_out")
SOURCE = os.path.join(HERE, "channelswap_source.mp3")  # committed source-of-record

PEAK = 0.92   # match the old synth's headroom so the cue-volume calibration still holds


def decode_mono_float(path):
    """Decode an audio file to a 1-D float32 mono array at its native sample rate."""
    container = av.open(path)
    stream = container.streams.audio[0]
    rate = stream.rate
    resampler = AudioResampler(format="flt", layout="mono", rate=rate)  # packed float32 mono
    chunks = []

    def take(frame):
        res = resampler.resample(frame)
        if res is None:
            return
        frames = res if isinstance(res, list) else [res]
        for f in frames:
            chunks.append(f.to_ndarray().reshape(-1))

    for frame in container.decode(audio=0):
        take(frame)
    take(None)  # flush
    container.close()
    if not chunks:
        sys.exit("no audio decoded from " + path)
    return np.concatenate(chunks).astype(np.float32), rate


def write_wav(samples, rate, path):
    peak = float(np.max(np.abs(samples))) if samples.size else 0.0
    if peak > 1e-9:
        samples = samples / peak * PEAK
    pcm = np.clip(samples, -1.0, 1.0)
    ints = (pcm * 32767.0).astype("<i2")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(ints.tobytes())
    return len(ints) / rate


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("slug", nargs="?", default=None,
                    help="candidate slug in channelswap_out/ (e.g. 02_channel_static); "
                         "omit to reconvert from the committed source")
    ap.add_argument("--source", default=None, help="explicit source mp3/wav path (overrides slug)")
    args = ap.parse_args()

    if args.source:
        src = args.source
    elif args.slug:
        src = os.path.join(CAND_DIR, args.slug + ".mp3")
    else:
        src = SOURCE

    if not os.path.isfile(src):
        sys.exit("source not found: " + src)

    # Copy the picked candidate to the committed source-of-record (unless we ARE it).
    if os.path.abspath(src) != os.path.abspath(SOURCE):
        shutil.copyfile(src, SOURCE)
        print("source -> " + os.path.relpath(SOURCE, HERE))

    samples, rate = decode_mono_float(SOURCE)
    dur = write_wav(samples, rate, OUT)
    print("wrote %s  %.2fs  %dHz mono 16-bit  (peak-normalized %.2f)" % (OUT, dur, rate, PEAK))


if __name__ == "__main__":
    main()
