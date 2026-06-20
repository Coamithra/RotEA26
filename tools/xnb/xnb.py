"""
XNB container parser for XNA 3.1 Xbox 360 content.

- Decompresses the LZX payload (driving lzx.LzxDecoder frame-by-frame exactly
  like MonoGame/KNI's ContentManager.DecompressLzx).
- Reads the type-reader manifest and the primary object header.
- Knows how to read Texture2D, SpriteFont and Curve enough to extract assets.

Run directly to dump info:  python xnb.py <file-or-dir> [...]
"""
import io
import os
import struct
import sys

from lzx import LzxDecoder


# XNA 3.1 SurfaceFormat enum (mirrors D3D9 era). Only the values we expect to
# meet in this game's content are named; unknown ints are reported raw.
XNA31_SURFACE = {
    1: "Color",        # 32-bit A8R8G8B8, 4 bpp
    28: "Dxt1",        # 0.5 bpp
    29: "Dxt2",
    30: "Dxt3",        # 1 bpp
    31: "Dxt4",
    32: "Dxt5",        # 1 bpp
    15: "Alpha8",
}


def read_file(path):
    with open(path, "rb") as f:
        return f.read()


def decompress_xnb(data):
    """Return (header, payload_bytes) where payload is the uncompressed content
    body (everything after the type-reader-manifest start, i.e. the bytes the
    ContentReader would see)."""
    if data[:3] != b"XNB":
        raise ValueError("not an XNB file")
    platform = chr(data[3])
    version = data[4]
    flags = data[5]
    compressed = bool(flags & 0x80)
    file_size = struct.unpack_from("<I", data, 6)[0]
    header = {
        "platform": platform,
        "version": version,
        "flags": flags,
        "compressed": compressed,
        "file_size": file_size,
    }
    if not compressed:
        return header, data[10:file_size]

    decompressed_size = struct.unpack_from("<I", data, 10)[0]
    header["decompressed_size"] = decompressed_size
    comp = io.BytesIO(data[14:file_size])
    comp_len = file_size - 14
    out = io.BytesIO()
    dec = LzxDecoder(16)
    pos = 0
    while out.tell() < decompressed_size and pos < comp_len:
        comp.seek(pos)
        b0 = comp.read(1)
        b1 = comp.read(1)
        if not b0 or not b1:
            break
        hi = b0[0]
        lo = b1[0]
        block_size = (hi << 8) | lo
        frame_size = 0x8000
        if hi == 0xFF:
            hi = lo
            lo = comp.read(1)[0]
            frame_size = (hi << 8) | lo
            hi = comp.read(1)[0]
            lo = comp.read(1)[0]
            block_size = (hi << 8) | lo
            pos += 5
        else:
            pos += 2
        if block_size == 0 or frame_size == 0:
            break
        comp.seek(pos)
        dec.decompress(comp, block_size, out, frame_size)
        pos += block_size
    payload = out.getvalue()[:decompressed_size]
    return header, payload


class Reader:
    def __init__(self, data):
        self.d = data
        self.p = 0

    def byte(self):
        v = self.d[self.p]
        self.p += 1
        return v

    def bytes(self, n):
        v = self.d[self.p:self.p + n]
        self.p += n
        return v

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def u32(self):
        v = struct.unpack_from("<I", self.d, self.p)[0]
        self.p += 4
        return v

    def f32(self):
        v = struct.unpack_from("<f", self.d, self.p)[0]
        self.p += 4
        return v

    def f64(self):
        v = struct.unpack_from("<d", self.d, self.p)[0]
        self.p += 8
        return v

    def u7(self):
        """7-bit encoded int (BinaryReader.Read7BitEncodedInt)."""
        result = 0
        shift = 0
        while True:
            b = self.byte()
            result |= (b & 0x7F) << shift
            if (b & 0x80) == 0:
                break
            shift += 7
        return result

    def string(self):
        n = self.u7()
        return self.bytes(n).decode("utf-8")


def parse_manifest(payload):
    r = Reader(payload)
    num_readers = r.u7()
    readers = []
    for _ in range(num_readers):
        name = r.string()
        ver = r.i32()
        readers.append((name, ver))
    num_shared = r.u7()
    type_id = r.u7()  # 1-based index of primary object's reader; 0 = null
    return r, readers, num_shared, type_id


def short_reader_name(name):
    # "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna..., ..."
    base = name.split(",")[0].strip()
    return base.rsplit(".", 1)[-1]


def parse_texture2d(r):
    fmt = r.i32()
    width = r.u32()
    height = r.u32()
    mip_count = r.u32()
    mips = []
    for _ in range(mip_count):
        size = r.u32()
        mips.append(r.bytes(size))
    return {
        "format": fmt,
        "format_name": XNA31_SURFACE.get(fmt, "?%d" % fmt),
        "width": width,
        "height": height,
        "mip_count": mip_count,
        "mips": mips,
    }


def read_char_utf8(r):
    """.NET BinaryReader.ReadChar() with the default UTF-8 decoder: one Unicode
    char, 1-4 bytes."""
    b0 = r.byte()
    if b0 < 0x80:
        return chr(b0)
    if (b0 >> 5) == 0b110:
        b1 = r.byte()
        return chr(((b0 & 0x1F) << 6) | (b1 & 0x3F))
    if (b0 >> 4) == 0b1110:
        b1 = r.byte()
        b2 = r.byte()
        return chr(((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F))
    b1 = r.byte()
    b2 = r.byte()
    b3 = r.byte()
    return chr(((b0 & 0x07) << 18) | ((b1 & 0x3F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F))


def parse_spritefont(r):
    """Parse a SpriteFontReader body. `r` is positioned right after the primary
    object's type id. Value-type list elements carry no per-element type id."""
    r.u7()                       # Texture2D reader id
    texture = parse_texture2d(r)

    def read_rect_list():
        r.u7()                   # ListReader<Rectangle> id
        n = r.i32()
        return [(r.i32(), r.i32(), r.i32(), r.i32()) for _ in range(n)]

    glyphs = read_rect_list()
    cropping = read_rect_list()
    r.u7()                       # ListReader<char> id
    n = r.i32()
    chars = [read_char_utf8(r) for _ in range(n)]
    line_spacing = r.i32()
    spacing = r.f32()
    r.u7()                       # ListReader<Vector3> id
    n = r.i32()
    kerning = [(r.f32(), r.f32(), r.f32()) for _ in range(n)]
    has_default = r.byte()
    default_char = read_char_utf8(r) if has_default else None
    return {
        "texture": texture,
        "glyphs": glyphs,
        "cropping": cropping,
        "chars": chars,
        "line_spacing": line_spacing,
        "spacing": spacing,
        "kerning": kerning,
        "default_char": default_char,
    }


def parse_curve(r):
    """CurveReader: PreLoop(i32), PostLoop(i32), count(i32), then per key
    position/value/tangentIn/tangentOut (float) + continuity(i32)."""
    pre_loop = r.i32()
    post_loop = r.i32()
    n = r.i32()
    keys = []
    for _ in range(n):
        pos = r.f32()
        val = r.f32()
        t_in = r.f32()
        t_out = r.f32()
        cont = r.i32()
        keys.append((pos, val, t_in, t_out, cont))
    return {"pre_loop": pre_loop, "post_loop": post_loop, "keys": keys}


def dump(path):
    data = read_file(path)
    try:
        header, payload = decompress_xnb(data)
    except Exception as e:
        print("%-60s  ERROR decompress: %s" % (path, e))
        return
    try:
        r, readers, num_shared, type_id = parse_manifest(payload)
    except Exception as e:
        print("%-60s  ERROR manifest: %s (plat=%s ver=%d flags=0x%02x)" %
              (path, e, header["platform"], header["version"], header["flags"]))
        return
    rn = short_reader_name(readers[0][0]) if readers else "?"
    info = "%-50s plat=%s ver=%d cmp=%d dsz=%s reader=%s n=%d" % (
        os.path.relpath(path),
        header["platform"], header["version"], int(header["compressed"]),
        header.get("decompressed_size", "-"), rn, len(readers))
    if rn == "Texture2DReader":
        try:
            t = parse_texture2d(r)
            sizes = ",".join(str(len(m)) for m in t["mips"])
            print("%s | fmt=%s(%d) %dx%d mips=%d sizes=[%s]" % (
                info, t["format_name"], t["format"], t["width"], t["height"],
                t["mip_count"], sizes))
        except Exception as e:
            print("%s | TEX PARSE ERROR %s" % (info, e))
    else:
        print(info)


def iter_xnb(paths):
    for p in paths:
        if os.path.isdir(p):
            for root, _, files in os.walk(p):
                for fn in files:
                    if fn.lower().endswith(".xnb"):
                        yield os.path.join(root, fn)
        else:
            yield p


if __name__ == "__main__":
    for p in iter_xnb(sys.argv[1:]):
        dump(p)
