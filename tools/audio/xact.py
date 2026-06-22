# ---------------------------------------------------------------------------
# xact.py — parse the recovered XACT audio banks (Stage 6).
#
# The shipped audio is XACT: a Wave Bank (.xwb) holding the raw audio and a
# Sound Bank (.xsb) mapping cue names -> waves. Both are the **Xbox 360 build**,
# so they are BIG-ENDIAN, and the long music tracks are **xWMA** (WMA-in-a-stream)
# while SFX/speech are plain PCM.
#
# KNI's BlazorGL backend has no XACT runtime, so Stage 6 doesn't "port XACT" — it
# cracks the banks offline (here) to web-friendly WAV/OGG that the rewritten
# SoundManager / JS music layer play natively. This module is the parser +
# decoders; build_audio.py is the driver that writes wwwroot/Content.
#
# Format references: the XACT WaveBank/SoundBank layout (vgmstream xwb.c, the
# MonoGame XACT readers) — adapted to the 2-bytes-shorter big-endian Xbox header
# verified against this game's banks (see plans/plan.md "Stage 6").
# ---------------------------------------------------------------------------
import io
import struct
from dataclasses import dataclass

# xWMA: XACT packs WMA avg-bytes/sec and block-align as small indices into these
# tables (the wBlockAlign mini-format byte: high 3 bits -> bps, low 5 bits -> align).
_WMA_AVG_BYTES_PER_SEC = [12000, 24000, 4000, 6000, 8000, 20000, 2500]
_WMA_BLOCK_ALIGN = [929, 1487, 1280, 2230, 8917, 8192, 4459, 5945, 2304,
                    1536, 1485, 1008, 2731, 4096, 6827, 5462, 1280]

CODEC_PCM, CODEC_XMA, CODEC_ADPCM, CODEC_WMA = 0, 1, 2, 3
_CODEC_NAME = {0: "PCM", 1: "XMA", 2: "ADPCM", 3: "xWMA"}


@dataclass
class WaveEntry:
    index: int
    codec: int
    channels: int
    rate: int
    bits: int            # 0 = 8-bit, 1 = 16-bit (PCM only)
    align_field: int     # raw wBlockAlign mini-format byte (xWMA index)
    play_offset: int     # into the wave-data segment
    play_length: int
    duration: int        # samples
    data: bytes = b""

    @property
    def codec_name(self):
        return _CODEC_NAME[self.codec]

    @property
    def seconds(self):
        return self.duration / self.rate if self.rate else 0.0


def parse_wavebank(path):
    """Parse a .xwb -> list[WaveEntry] (with raw .data filled in)."""
    d = open(path, "rb").read()
    if d[:4] != b"DNBW":
        raise ValueError(f"{path}: not a big-endian Xbox wave bank (sig={d[:4]!r})")
    be = ">"
    # header: sig(4) version(4) headerversion(4) then 5 regions {offset,length}
    regions = [struct.unpack_from(be + "II", d, 12 + i * 8) for i in range(5)]
    bankdata_off = regions[0][0]
    meta_off = regions[1][0]
    wave_base = regions[4][0]
    count = struct.unpack_from(be + "I", d, bankdata_off + 4)[0]

    entries = []
    for i in range(count):
        o = meta_off + i * 24
        fd, fmt, poff, plen, _ls, _lt = struct.unpack_from(be + "IIIIII", d, o)
        codec = fmt & 0x3
        channels = (fmt >> 2) & 0x7
        rate = (fmt >> 5) & 0x3FFFF
        align = (fmt >> 23) & 0xFF
        bits = (fmt >> 31) & 0x1
        e = WaveEntry(i, codec, channels, rate, bits, align, poff, plen, fd >> 4)
        e.data = d[wave_base + poff: wave_base + poff + plen]
        entries.append(e)
    return entries


def _read_cstr(d, o):
    z = d.index(b"\0", o)
    return d[o:z].decode("latin1"), z + 1


def parse_soundbank(path):
    """Parse a .xsb -> dict cue_name -> [wave_index, ...].

    Every cue here is "simple" (one sound); a sound is either a simple sound
    (inline wave ref) or a complex sound with clips (each clip's play-wave event
    holds a wave index). Music cues are complex with 1-2 clips (intro + loop).
    Validated: the 44 waves are each referenced exactly once.
    """
    d = open(path, "rb").read()
    if d[:4] != b"KBDS":
        raise ValueError(f"{path}: not a big-endian Xbox sound bank (sig={d[:4]!r})")
    be = ">"
    u16 = lambda o: struct.unpack_from(be + "H", d, o)[0]
    u32 = lambda o: struct.unpack_from(be + "I", d, o)[0]

    num_simple = u16(0x13)
    num_complex = u16(0x15)
    if num_complex:
        raise ValueError("unexpected complex CUES (only simple cues handled)")
    # Xbox header is 2 bytes shorter than the PC layout; offset block starts 0x22.
    simple_cues_off = u32(0x22)
    cue_names_off = u32(0x2A)

    # cue names are concatenated null-terminated strings, in cue order
    names, o = [], cue_names_off
    while o < len(d):
        s, o = _read_cstr(d, o)
        if s:
            names.append(s)

    def parse_sound(so):
        flags = d[so]
        complex_ = flags & 0x01
        has_rpc = flags & 0x0E
        has_dsp = flags & 0x10
        # header: flags(1) cat(2) vol(1) pitch(2) priority(1) filter(2) = 9 bytes
        p = so + 9
        if not complex_:
            return [u16(p)]                       # inline: trackIndex(2) waveBank(1)
        num_clips = d[p]
        p += 1
        if has_rpc:                               # skip RPC block (len includes itself)
            p += u16(p)
        if has_dsp:
            p += u16(p)
        clip_offsets = []
        for _ in range(num_clips):
            clip_offsets.append(u32(p + 1))       # sig(1) clipOffset(4) extra(4)
            p += 9
        # each clip's play-wave event carries the wave index at clipOffset+9
        return [u16(c + 9) for c in clip_offsets]

    mapping = {}
    for i in range(num_simple):
        so = u32(simple_cues_off + i * 5 + 1)     # cue: flags(1) soundOffset(4)
        mapping[names[i]] = parse_sound(so)
    return mapping


# --- decoders -------------------------------------------------------------

def decode_pcm(entry):
    """PCM WaveEntry -> (float32 ndarray [n, channels], rate). Xbox PCM is
    big-endian; 16-bit is signed, 8-bit is unsigned (centred at 128)."""
    import numpy as np
    if entry.bits == 1:                            # 16-bit signed big-endian
        a = np.frombuffer(entry.data, dtype=">i2").astype(np.float32) / 32768.0
    else:                                          # 8-bit unsigned
        a = (np.frombuffer(entry.data, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    ch = max(1, entry.channels)
    a = a[: (len(a) // ch) * ch].reshape(-1, ch)
    return a, entry.rate


def _build_xwma_container(entry):
    """Wrap an xWMA WaveEntry's raw stream in the RIFF/XWMA container that
    FFmpeg's demuxer (via PyAV) reads. No 'dpds' seek table is needed for a
    straight decode-from-start."""
    bps = _WMA_AVG_BYTES_PER_SEC[(entry.align_field >> 5) & 0x7]
    block_align = _WMA_BLOCK_ALIGN[entry.align_field & 0x1F]

    def chunk(tag, payload):
        return tag + struct.pack("<I", len(payload)) + payload + (b"\x00" if len(payload) & 1 else b"")

    fmt = struct.pack("<HHIIHHH", 0x0161, entry.channels, entry.rate, bps, block_align, 16, 0)
    body = b"XWMA" + chunk(b"fmt ", fmt) + chunk(b"data", entry.data)
    return b"RIFF" + struct.pack("<I", len(body)) + body


def decode_xwma(entry):
    """xWMA WaveEntry -> (float32 ndarray [n, channels], rate) via PyAV."""
    import numpy as np
    import av
    container = av.open(io.BytesIO(_build_xwma_container(entry)), mode="r")
    chunks = []
    for frame in container.decode(audio=0):
        arr = frame.to_ndarray()                   # (channels, n) or (1, n*ch) packed
        chunks.append(arr)
    container.close()
    if not chunks:
        return np.zeros((0, entry.channels), np.float32), entry.rate
    # PyAV gives planar float (channels, n) for wma; stack and transpose.
    out = np.concatenate(chunks, axis=1) if chunks[0].ndim == 2 else np.concatenate(chunks)
    if out.ndim == 1:
        out = out.reshape(1, -1)
    a = out.T.astype(np.float32)                   # -> (n, channels)
    if a.shape[1] != entry.channels and a.shape[1] == 1:
        a = np.repeat(a, entry.channels, axis=1)
    return a, entry.rate


def decode(entry):
    if entry.codec == CODEC_PCM:
        return decode_pcm(entry)
    if entry.codec == CODEC_WMA:
        return decode_xwma(entry)
    raise NotImplementedError(f"codec {entry.codec_name} not supported (entry {entry.index})")
