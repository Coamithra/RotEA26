"""Read/write the AnimatedSprite `.dat` format, byte-for-byte compatible with the
C# loader in `Game/EvilAliens/AnimatedSprite.cs` (`loadData`).

The `.dat` is the .NET `BinaryReader` layout the original XNA content pipeline emitted:

    int32   header            (== 1 in the shipped files; loadData discards it)
    string  atlasName         (.NET 7-bit-length-prefixed UTF-8; shipped value "test")
    int32   numAnimations
    repeat numAnimations:
        string  animName
        bool    flag1          (looping; discarded by loadData)
        bool    flag2          (discarded)
        int32   fpsFixed       (fps * 65536; discarded -- the game drives its own timing)
        byte    frameCount     (0..255)
        repeat frameCount:
            int16 originalWidth   } the full (untrimmed) render frame size, in atlas pixels
            int16 originalHeight  }
            int16 minX  } trimmed content bbox within the original frame (max exclusive:
            int16 minY  }   drawn width = maxX-minX, height = maxY-minY)
            int16 maxX
            int16 maxY
            int16 xPos  } top-left of the trimmed content in the packed atlas texture
            int16 yPos

`AnimatedSprite` keeps all animations' frames in one flat list and indexes it globally,
so a single-animation sheet (a turntable / hero pose) is `numAnimations == 1`.

Everything is little-endian: the game reads with .NET `BinaryReader`, which is
little-endian on every platform regardless of the original Xbox being big-endian.
"""

from __future__ import annotations

import io
import struct
from dataclasses import dataclass


@dataclass
class Frame:
    original_width: int
    original_height: int
    min_x: int
    min_y: int
    max_x: int
    max_y: int
    x_pos: int
    y_pos: int

    def as_tuple(self) -> tuple[int, ...]:
        return (
            self.original_width, self.original_height,
            self.min_x, self.min_y, self.max_x, self.max_y,
            self.x_pos, self.y_pos,
        )


@dataclass
class Animation:
    name: str
    frames: list[Frame]
    fps: float = 0.0
    flag1: bool = True   # matches the shipped files (bool1 == 1)
    flag2: bool = False  # (bool2 == 0)


def _write_7bit_string(buf: io.BytesIO, s: str) -> None:
    """.NET BinaryWriter.Write(string): 7-bit-encoded length prefix then UTF-8 bytes."""
    data = s.encode("utf-8")
    n = len(data)
    while n >= 0x80:
        buf.write(bytes([(n & 0x7F) | 0x80]))
        n >>= 7
    buf.write(bytes([n]))
    buf.write(data)


def _read_7bit_string(buf: io.BytesIO) -> str:
    n = 0
    shift = 0
    while True:
        b = buf.read(1)[0]
        n |= (b & 0x7F) << shift
        if b < 0x80:
            break
        shift += 7
    return buf.read(n).decode("utf-8")


def write_dat(path: str, animations: list[Animation], atlas_name: str = "test",
              header: int = 1) -> None:
    buf = io.BytesIO()
    buf.write(struct.pack("<i", header))
    _write_7bit_string(buf, atlas_name)
    buf.write(struct.pack("<i", len(animations)))
    for anim in animations:
        _write_7bit_string(buf, anim.name)
        buf.write(bytes([1 if anim.flag1 else 0]))
        buf.write(bytes([1 if anim.flag2 else 0]))
        buf.write(struct.pack("<i", int(round(anim.fps * 65536))))
        if len(anim.frames) > 255:
            raise ValueError(f"animation '{anim.name}' has {len(anim.frames)} frames; "
                             "the .dat frameCount is a single byte (max 255)")
        buf.write(bytes([len(anim.frames)]))
        for f in anim.frames:
            buf.write(struct.pack("<8h", *f.as_tuple()))
    with open(path, "wb") as fh:
        fh.write(buf.getvalue())


def read_dat(path: str) -> tuple[list[Animation], str, int]:
    """Mirror of the C# `loadData` reads. Returns (animations, atlasName, header).

    Used by the self-test to prove a written `.dat` round-trips through the exact
    same sequence of reads the game performs.
    """
    with open(path, "rb") as fh:
        buf = io.BytesIO(fh.read())
    header = struct.unpack("<i", buf.read(4))[0]
    atlas_name = _read_7bit_string(buf)
    num = struct.unpack("<i", buf.read(4))[0]
    animations: list[Animation] = []
    for _ in range(num):
        name = _read_7bit_string(buf)
        flag1 = buf.read(1)[0] != 0
        flag2 = buf.read(1)[0] != 0
        fps = struct.unpack("<i", buf.read(4))[0] / 65536.0
        count = buf.read(1)[0]
        frames = [Frame(*struct.unpack("<8h", buf.read(16))) for _ in range(count)]
        animations.append(Animation(name, frames, fps, flag1, flag2))
    return animations, atlas_name, header
