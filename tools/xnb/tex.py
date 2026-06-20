"""
Decode XNA 3.1 texture surfaces (Color / Dxt1 / Dxt5) to straight RGBA bytes.

Xbox 360 is big-endian, so the on-disk byte order differs from PC. The exact
swap is determined empirically (see scratch test) and pinned via SWAP16 /
COLOR_ORDER. DXT blocks are stored as 16-bit words; on Xbox those words are
byte-swapped, so we swap every 2 bytes before decoding as little-endian DXT.
"""


def swap16(data):
    b = bytearray(data)
    b[0::2], b[1::2] = bytes(b[1::2]), bytes(b[0::2])
    return bytes(b)


def _color565(c):
    r = (c >> 11) & 0x1F
    g = (c >> 5) & 0x3F
    b = c & 0x1F
    return (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2)


def decode_dxt1(data, w, h, swap=True):
    if swap:
        data = swap16(data)
    out = bytearray(w * h * 4)
    bw = (w + 3) // 4
    bh = (h + 3) // 4
    i = 0
    for by in range(bh):
        for bx in range(bw):
            c0 = data[i] | (data[i + 1] << 8)
            c1 = data[i + 2] | (data[i + 3] << 8)
            bits = data[i + 4] | (data[i + 5] << 8) | (data[i + 6] << 16) | (data[i + 7] << 24)
            i += 8
            r0, g0, b0 = _color565(c0)
            r1, g1, b1 = _color565(c1)
            if c0 > c1:
                palette = [
                    (r0, g0, b0, 255), (r1, g1, b1, 255),
                    ((2 * r0 + r1) // 3, (2 * g0 + g1) // 3, (2 * b0 + b1) // 3, 255),
                    ((r0 + 2 * r1) // 3, (g0 + 2 * g1) // 3, (b0 + 2 * b1) // 3, 255),
                ]
            else:
                palette = [
                    (r0, g0, b0, 255), (r1, g1, b1, 255),
                    ((r0 + r1) // 2, (g0 + g1) // 2, (b0 + b1) // 2, 255),
                    (0, 0, 0, 0),
                ]
            for py in range(4):
                for px in range(4):
                    x = bx * 4 + px
                    y = by * 4 + py
                    if x >= w or y >= h:
                        continue
                    idx = (bits >> (2 * (4 * py + px))) & 3
                    r, g, b, a = palette[idx]
                    o = (y * w + x) * 4
                    out[o] = r
                    out[o + 1] = g
                    out[o + 2] = b
                    out[o + 3] = a
    return bytes(out)


def decode_dxt5(data, w, h, swap=True):
    if swap:
        data = swap16(data)
    out = bytearray(w * h * 4)
    bw = (w + 3) // 4
    bh = (h + 3) // 4
    i = 0
    for by in range(bh):
        for bx in range(bw):
            a0 = data[i]
            a1 = data[i + 1]
            abits = int.from_bytes(data[i + 2:i + 8], "little")
            c0 = data[i + 8] | (data[i + 9] << 8)
            c1 = data[i + 10] | (data[i + 11] << 8)
            bits = data[i + 12] | (data[i + 13] << 8) | (data[i + 14] << 16) | (data[i + 15] << 24)
            i += 16
            if a0 > a1:
                alpha = [a0, a1,
                         (6 * a0 + 1 * a1) // 7, (5 * a0 + 2 * a1) // 7,
                         (4 * a0 + 3 * a1) // 7, (3 * a0 + 4 * a1) // 7,
                         (2 * a0 + 5 * a1) // 7, (1 * a0 + 6 * a1) // 7]
            else:
                alpha = [a0, a1,
                         (4 * a0 + 1 * a1) // 5, (3 * a0 + 2 * a1) // 5,
                         (2 * a0 + 3 * a1) // 5, (1 * a0 + 4 * a1) // 5, 0, 255]
            r0, g0, b0 = _color565(c0)
            r1, g1, b1 = _color565(c1)
            palette = [
                (r0, g0, b0), (r1, g1, b1),
                ((2 * r0 + r1) // 3, (2 * g0 + g1) // 3, (2 * b0 + b1) // 3),
                ((r0 + 2 * r1) // 3, (g0 + 2 * g1) // 3, (b0 + 2 * b1) // 3),
            ]
            for py in range(4):
                for px in range(4):
                    x = bx * 4 + px
                    y = by * 4 + py
                    if x >= w or y >= h:
                        continue
                    pix = 4 * py + px
                    idx = (bits >> (2 * pix)) & 3
                    aidx = (abits >> (3 * pix)) & 7
                    r, g, b = palette[idx]
                    o = (y * w + x) * 4
                    out[o] = r
                    out[o + 1] = g
                    out[o + 2] = b
                    out[o + 3] = alpha[aidx]
    return bytes(out)


def decode_dxt3(data, w, h, swap=True):
    if swap:
        data = swap16(data)
    out = bytearray(w * h * 4)
    bw = (w + 3) // 4
    bh = (h + 3) // 4
    i = 0
    for by in range(bh):
        for bx in range(bw):
            abits = int.from_bytes(data[i:i + 8], "little")  # 4 bits/pixel explicit alpha
            c0 = data[i + 8] | (data[i + 9] << 8)
            c1 = data[i + 10] | (data[i + 11] << 8)
            bits = data[i + 12] | (data[i + 13] << 8) | (data[i + 14] << 16) | (data[i + 15] << 24)
            i += 16
            r0, g0, b0 = _color565(c0)
            r1, g1, b1 = _color565(c1)
            # DXT2/3 always use the 4-colour (opaque) interpolation
            palette = [
                (r0, g0, b0), (r1, g1, b1),
                ((2 * r0 + r1) // 3, (2 * g0 + g1) // 3, (2 * b0 + b1) // 3),
                ((r0 + 2 * r1) // 3, (g0 + 2 * g1) // 3, (b0 + 2 * b1) // 3),
            ]
            for py in range(4):
                for px in range(4):
                    x = bx * 4 + px
                    y = by * 4 + py
                    if x >= w or y >= h:
                        continue
                    pix = 4 * py + px
                    idx = (bits >> (2 * pix)) & 3
                    a = (abits >> (4 * pix)) & 0xF
                    r, g, b = palette[idx]
                    o = (y * w + x) * 4
                    out[o] = r
                    out[o + 1] = g
                    out[o + 2] = b
                    out[o + 3] = (a << 4) | a
    return bytes(out)


def decode_color(data, w, h, order="ARGB"):
    """32-bit Color surface -> RGBA. `order` is the on-disk byte order.

    Xbox 360 is big-endian, so a 32-bit Color (0xAARRGGBB) is stored as the
    byte sequence [A, R, G, B] = ARGB. (Reading it as BGRA gives a blue cast and
    a wrong alpha mask — byte 0 is alpha, not blue.)"""
    out = bytearray(w * h * 4)
    ri, gi, bi, ai = (order.index("R"), order.index("G"),
                      order.index("B"), order.index("A"))
    for p in range(w * h):
        s = p * 4
        d = p * 4
        out[d] = data[s + ri]
        out[d + 1] = data[s + gi]
        out[d + 2] = data[s + bi]
        out[d + 3] = data[s + ai]
    return bytes(out)


def decode_texture(fmt, data, w, h, swap=True, color_order="ARGB"):
    if fmt == 1:
        return decode_color(data, w, h, color_order)
    if fmt == 28:
        return decode_dxt1(data, w, h, swap)
    if fmt == 32:
        return decode_dxt5(data, w, h, swap)
    if fmt == 30:
        return decode_dxt3(data, w, h, swap)
    raise ValueError("unsupported surface format %d" % fmt)
