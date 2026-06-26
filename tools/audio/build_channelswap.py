#!/usr/bin/env python
"""Synthesize the "static channel swap" SFX for the splash channel-flip.

The "I made this!" splash (SplashScene index 1) does an analog-TV channel-flip
glitch (channelflip.fx) that crossfades the old meme into the revenged image.
The original XACT banks have no such cue (it's a port-era reskin gag), so this
generates the punctuating sound offline -- a bright burst of TV static with a
snappy detent/de-sync snap on the onset that decays into a settling hiss as the
new picture resolves crisp. Deterministic (fixed seed) so the committed WAV is
reproducible; like tools/shaders & tools/audio, CI just ships the output.

  python tools/audio/build_channelswap.py        # -> wwwroot/Content/sfx/channelswap.wav

Re-run after changing the knobs below; don't hand-edit the WAV. Output is mono
16-bit PCM (SoundEffect.FromStream); 22050 Hz for bright hiss (the cracked XACT
SFX are 8 kHz, but static wants the bandwidth). Needs numpy only.
"""

import os
import wave
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
WWW = os.path.normpath(os.path.join(HERE, "..", "..", "web", "EvilAliensWeb", "wwwroot"))
OUT = os.path.join(WWW, "Content", "sfx", "channelswap.wav")

SR = 22050
DUR = 0.62          # ~ the channelflip.fx glitch (FLIP_MS = 650 ms)
SEED = 20260626


def movavg(x, k):
    """Cheap boxcar low-pass (vectorized) for brightening by subtraction."""
    return np.convolve(x, np.ones(k) / k, mode="same")


def build():
    rng = np.random.default_rng(SEED)
    n = int(SR * DUR)   # floor to whole frames
    t = np.arange(n) / SR

    # Broadband static bed, brightened toward hiss: subtract a short moving
    # average (the low end) so it reads as TV "snow", not rumble.
    white = rng.standard_normal(n)
    bright = white - 0.85 * movavg(white, 8)
    static = 0.7 * bright + 0.3 * white

    # Amplitude envelope: instant attack, an initial punch that settles into a
    # quieter hiss bed, then an overall decay so the burst fades as the new
    # picture emerges crisp.
    env = np.ones(n)
    attack = max(1, int(0.003 * SR))
    env[:attack] = np.linspace(0.0, 1.0, attack)
    overall = np.exp(-t * 6.0)
    settle = 0.35 + 0.65 * np.exp(-t * 22.0)
    env *= overall * settle
    rel = max(1, int(0.04 * SR))
    env[-rel:] *= np.linspace(1.0, 0.0, rel)   # zero the tail (no end click)

    # Vertical-roll flutter on the tail: a low tremolo evoking the rolling
    # picture / sync loss while the channel settles.
    roll = 1.0 + 0.18 * np.sin(2 * np.pi * 15.0 * t) * np.clip((t - 0.06) / 0.1, 0.0, 1.0)

    sig = static * env * roll

    # Onset snaps: a couple of sharp broadband clicks -- the channel detent and
    # the de-sync pop as the signal breaks up.
    def click(at, amp, width):
        i = int(at * SR)
        w = int(width * SR)
        if i + w > n:
            w = n - i
        if w <= 0:
            return
        burst = rng.standard_normal(w) * np.exp(-np.arange(w) / (w * 0.3))
        sig[i:i + w] += burst * amp

    click(0.0, 0.9, 0.012)
    click(0.02, 0.5, 0.008)

    # Normalize with a little headroom; SoundManager applies the cue volume.
    sig = sig / (np.max(np.abs(sig)) + 1e-9) * 0.92
    pcm = np.clip(sig, -1.0, 1.0)
    ints = (pcm * 32767.0).astype("<i2")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with wave.open(OUT, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(ints.tobytes())
    print(f"wrote {OUT}  {n / SR:.2f}s  {SR}Hz mono")


if __name__ == "__main__":
    build()
