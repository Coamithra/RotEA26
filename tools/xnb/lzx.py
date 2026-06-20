"""
LZX decompressor for XNA .xnb content.

A faithful, line-by-line Python port of KNI's
Microsoft.Xna.Framework.Content.LzxDecoder (decompiled from
nkast.Xna.Framework.Content 4.1.9001 -> tools/xnb/LzxDecoder.decompiled.cs),
which is itself the libmspack LZX decoder. Porting KNI's exact code guarantees
we decompress XNB byte-for-byte the same way the engine would.

Window size for XNB is always 16 bits (64 KiB). The decoder is stateful across
"frames"; the caller (xnb.py) drives the frame loop the same way MonoGame/KNI's
ContentManager.DecompressLzx does.
"""

# BLOCKTYPE
BT_INVALID = 0
BT_VERBATIM = 1
BT_ALIGNED = 2
BT_UNCOMPRESSED = 3

# Static tables, built once (mirrors the static ctor logic in LzxDecoder).
_extra_bits = bytearray(52)
_j = 0
_n = 0
while _j <= 50:
    _extra_bits[_j] = _n
    _extra_bits[_j + 1] = _n
    if _j != 0 and _n < 17:
        _n += 1
    _j += 2

_position_base = [0] * 51
_n = 0
for _k in range(51):
    _position_base[_k] = _n
    _n += 1 << _extra_bits[_k]

MASK32 = 0xFFFFFFFF


class _BitBuffer:
    __slots__ = ("buffer", "bitsleft", "stream")

    def __init__(self, stream):
        self.stream = stream
        self.buffer = 0
        self.bitsleft = 0

    def init(self):
        self.buffer = 0
        self.bitsleft = 0

    def _rb(self):
        b = self.stream.read(1)
        # C# (byte)Stream.ReadByte(): -1 at EOF -> 0xFF
        return b[0] if b else 0xFF

    def ensure(self, bits):
        while self.bitsleft < bits:
            n1 = self._rb()
            n2 = self._rb()
            self.buffer = (self.buffer | (((n2 << 8) | n1) << (16 - self.bitsleft))) & MASK32
            self.bitsleft += 16

    def peek(self, bits):
        return (self.buffer >> (32 - bits)) & MASK32

    def remove(self, bits):
        self.buffer = (self.buffer << bits) & MASK32
        self.bitsleft -= bits

    def read(self, bits):
        if bits <= 0:
            return 0
        self.ensure(bits)
        r = self.peek(bits)
        self.remove(bits)
        return r


class LzxDecoder:
    def __init__(self, window=16):
        wsize = 1 << window
        if window < 15 or window > 21:
            raise ValueError("Unsupported window size range")
        self.window = bytearray([220]) * 1  # placeholder, replaced below
        self.window = bytearray(b"\xDC" * wsize)
        self.window_size = wsize
        self.window_posn = 0
        if window == 20:
            num4 = 42
        elif window == 21:
            num4 = 50
        else:
            num4 = window << 1
        self.R0 = 1
        self.R1 = 1
        self.R2 = 1
        self.main_elements = 256 + (num4 << 3)
        self.header_read = 0
        self.frames_read = 0
        self.block_remaining = 0
        self.block_length = 0
        self.block_type = BT_INVALID
        self.intel_filesize = 0
        self.intel_curpos = 0
        self.intel_started = 0
        self.PRETREE_table = [0] * 104
        self.PRETREE_len = bytearray(84)
        self.MAINTREE_table = [0] * 5408
        self.MAINTREE_len = bytearray(720)
        self.LENGTH_table = [0] * 4596
        self.LENGTH_len = bytearray(314)
        self.ALIGNED_table = [0] * 144
        self.ALIGNED_len = bytearray(72)

    def _make_decode_table(self, nsyms, nbits, length, table):
        bit = 1
        pos = 0
        table_mask = 1 << nbits
        bit_mask = table_mask >> 1
        next_symbol = bit_mask
        while bit <= nbits:
            for sym in range(nsyms):
                if length[sym] == bit:
                    leaf = pos
                    pos += bit_mask
                    if pos > table_mask:
                        return 1
                    fill = bit_mask
                    while fill != 0:
                        fill -= 1
                        table[leaf] = sym
                        leaf += 1
            bit_mask >>= 1
            bit += 1
        if pos != table_mask:
            for sym in range(pos, table_mask):
                table[sym] = 0
            pos <<= 16
            table_mask <<= 16
            bit_mask = 1 << 15
            while bit <= 16:
                for sym in range(nsyms):
                    if length[sym] == bit:
                        leaf = pos >> 16
                        for _ in range(bit - nbits):
                            if table[leaf] == 0:
                                table[next_symbol << 1] = 0
                                table[(next_symbol << 1) + 1] = 0
                                table[leaf] = next_symbol
                                next_symbol += 1
                            leaf = table[leaf] << 1
                            if (pos >> (15 - _)) & 1:
                                leaf += 1
                        table[leaf] = sym
                        pos += bit_mask
                        if pos > table_mask:
                            return 1
                bit_mask >>= 1
                bit += 1
        if pos == table_mask:
            return 0
        for sym in range(nsyms):
            if length[sym] != 0:
                return 1
        return 0

    def _read_lengths(self, lens, first, last, bitbuf):
        for i in range(20):
            self.PRETREE_len[i] = bitbuf.read(4)
        self._make_decode_table(20, 6, self.PRETREE_len, self.PRETREE_table)
        i = first
        while i < last:
            sym = self._read_huff_sym(self.PRETREE_table, self.PRETREE_len, 20, 6, bitbuf)
            if sym == 17:
                run = bitbuf.read(4) + 4
                while run != 0:
                    run -= 1
                    lens[i] = 0
                    i += 1
            elif sym == 18:
                run = bitbuf.read(5) + 20
                while run != 0:
                    run -= 1
                    lens[i] = 0
                    i += 1
            elif sym == 19:
                run = bitbuf.read(1) + 4
                sym = self._read_huff_sym(self.PRETREE_table, self.PRETREE_len, 20, 6, bitbuf)
                sym = lens[i] - sym
                if sym < 0:
                    sym += 17
                while run != 0:
                    run -= 1
                    lens[i] = sym
                    i += 1
            else:
                sym = lens[i] - sym
                if sym < 0:
                    sym += 17
                lens[i] = sym
                i += 1

    def _read_huff_sym(self, table, lengths, nsyms, nbits, bitbuf):
        bitbuf.ensure(16)
        sym = table[bitbuf.peek(nbits)]
        if sym >= nsyms:
            i = 1 << (32 - nbits)
            while True:
                i >>= 1
                sym <<= 1
                sym |= 1 if (bitbuf.buffer & i) != 0 else 0
                if i == 0:
                    return 0
                sym = table[sym]
                if sym < nsyms:
                    break
        length = lengths[sym]
        bitbuf.remove(length)
        return sym

    def decompress(self, in_stream, in_len, out_stream, out_len):
        """Decompress one frame. Mirrors LzxDecoder.Decompress; return value is
        intentionally ignored by the caller (KNI does the same)."""
        bitbuf = _BitBuffer(in_stream)
        start_pos = in_stream.tell()
        end_pos = start_pos + in_len
        window = self.window
        window_posn = self.window_posn
        window_size = self.window_size
        R0 = self.R0
        R1 = self.R1
        R2 = self.R2
        togo = out_len
        bitbuf.init()

        if self.header_read == 0:
            if bitbuf.read(1) != 0:
                hi = bitbuf.read(16)
                lo = bitbuf.read(16)
                self.intel_filesize = ((hi << 16) | lo)
            self.header_read = 1

        while togo > 0:
            if self.block_remaining == 0:
                if self.block_type == BT_UNCOMPRESSED:
                    if (self.block_length & 1) == 1:
                        in_stream.read(1)
                    bitbuf.init()
                self.block_type = bitbuf.read(3)
                hi = bitbuf.read(16)
                lo = bitbuf.read(8)
                self.block_length = (hi << 8) | lo
                self.block_remaining = self.block_length
                if self.block_type == BT_ALIGNED:
                    for i in range(8):
                        self.ALIGNED_len[i] = bitbuf.read(3)
                    self._make_decode_table(8, 7, self.ALIGNED_len, self.ALIGNED_table)
                    # fallthrough to VERBATIM
                    self._read_lengths(self.MAINTREE_len, 0, 256, bitbuf)
                    self._read_lengths(self.MAINTREE_len, 256, self.main_elements, bitbuf)
                    self._make_decode_table(656, 12, self.MAINTREE_len, self.MAINTREE_table)
                    if self.MAINTREE_len[232] != 0:
                        self.intel_started = 1
                    self._read_lengths(self.LENGTH_len, 0, 249, bitbuf)
                    self._make_decode_table(250, 12, self.LENGTH_len, self.LENGTH_table)
                elif self.block_type == BT_VERBATIM:
                    self._read_lengths(self.MAINTREE_len, 0, 256, bitbuf)
                    self._read_lengths(self.MAINTREE_len, 256, self.main_elements, bitbuf)
                    self._make_decode_table(656, 12, self.MAINTREE_len, self.MAINTREE_table)
                    if self.MAINTREE_len[232] != 0:
                        self.intel_started = 1
                    self._read_lengths(self.LENGTH_len, 0, 249, bitbuf)
                    self._make_decode_table(250, 12, self.LENGTH_len, self.LENGTH_table)
                elif self.block_type == BT_UNCOMPRESSED:
                    self.intel_started = 1
                    bitbuf.ensure(16)
                    if bitbuf.bitsleft > 16:
                        in_stream.seek(-2, 1)
                    R0 = _read_u32le(in_stream)
                    R1 = _read_u32le(in_stream)
                    R2 = _read_u32le(in_stream)
                else:
                    return -1

            if in_stream.tell() > end_pos and (
                in_stream.tell() > end_pos + 2 or bitbuf.bitsleft < 16
            ):
                return -1

            while True:
                this_run = self.block_remaining
                if not (this_run > 0 and togo > 0):
                    break
                if this_run > togo:
                    this_run = togo
                togo -= this_run
                self.block_remaining -= this_run
                window_posn &= window_size - 1
                if window_posn + this_run > window_size:
                    return -1

                if self.block_type == BT_VERBATIM:
                    while this_run > 0:
                        sym = self._read_huff_sym(self.MAINTREE_table, self.MAINTREE_len, 656, 12, bitbuf)
                        if sym < 256:
                            window[window_posn] = sym
                            window_posn += 1
                            this_run -= 1
                            continue
                        sym -= 256
                        length = sym & 7
                        if length == 7:
                            length += self._read_huff_sym(self.LENGTH_table, self.LENGTH_len, 250, 12, bitbuf)
                        length += 2
                        match_offset = sym >> 3
                        if match_offset > 2:
                            if match_offset != 3:
                                extra = _extra_bits[match_offset]
                                verbatim_bits = bitbuf.read(extra)
                                match_offset = (_position_base[match_offset] - 2) + verbatim_bits
                            else:
                                match_offset = 1
                            R2 = R1
                            R1 = R0
                            R0 = match_offset
                        else:
                            if match_offset == 0:
                                match_offset = R0
                            elif match_offset == 1:
                                match_offset = R1
                                R1 = R0
                                R0 = match_offset
                            else:
                                match_offset = R2
                                R2 = R0
                                R0 = match_offset
                        rundest = window_posn
                        this_run -= length
                        if window_posn >= match_offset:
                            runsrc = rundest - match_offset
                        else:
                            runsrc = rundest + (window_size - match_offset)
                            copy = match_offset - window_posn
                            if copy < length:
                                length -= copy
                                window_posn += copy
                                while copy > 0:
                                    copy -= 1
                                    window[rundest] = window[runsrc]
                                    rundest += 1
                                    runsrc += 1
                                runsrc = 0
                        window_posn += length
                        while length > 0:
                            length -= 1
                            window[rundest] = window[runsrc]
                            rundest += 1
                            runsrc += 1

                elif self.block_type == BT_ALIGNED:
                    while this_run > 0:
                        sym = self._read_huff_sym(self.MAINTREE_table, self.MAINTREE_len, 656, 12, bitbuf)
                        if sym < 256:
                            window[window_posn] = sym
                            window_posn += 1
                            this_run -= 1
                            continue
                        sym -= 256
                        length = sym & 7
                        if length == 7:
                            length += self._read_huff_sym(self.LENGTH_table, self.LENGTH_len, 250, 12, bitbuf)
                        length += 2
                        match_offset = sym >> 3
                        if match_offset > 2:
                            extra = _extra_bits[match_offset]
                            match_offset = _position_base[match_offset] - 2
                            if extra > 3:
                                extra -= 3
                                verbatim_bits = bitbuf.read(extra)
                                match_offset += verbatim_bits << 3
                                aligned_bits = self._read_huff_sym(self.ALIGNED_table, self.ALIGNED_len, 8, 7, bitbuf)
                                match_offset += aligned_bits
                            elif extra == 3:
                                aligned_bits = self._read_huff_sym(self.ALIGNED_table, self.ALIGNED_len, 8, 7, bitbuf)
                                match_offset += aligned_bits
                            elif extra > 0:
                                verbatim_bits = bitbuf.read(extra)
                                match_offset += verbatim_bits
                            else:
                                match_offset = 1
                            R2 = R1
                            R1 = R0
                            R0 = match_offset
                        else:
                            if match_offset == 0:
                                match_offset = R0
                            elif match_offset == 1:
                                match_offset = R1
                                R1 = R0
                                R0 = match_offset
                            else:
                                match_offset = R2
                                R2 = R0
                                R0 = match_offset
                        rundest = window_posn
                        this_run -= length
                        if window_posn >= match_offset:
                            runsrc = rundest - match_offset
                        else:
                            runsrc = rundest + (window_size - match_offset)
                            copy = match_offset - window_posn
                            if copy < length:
                                length -= copy
                                window_posn += copy
                                while copy > 0:
                                    copy -= 1
                                    window[rundest] = window[runsrc]
                                    rundest += 1
                                    runsrc += 1
                                runsrc = 0
                        window_posn += length
                        while length > 0:
                            length -= 1
                            window[rundest] = window[runsrc]
                            rundest += 1
                            runsrc += 1

                elif self.block_type == BT_UNCOMPRESSED:
                    if in_stream.tell() + this_run > end_pos:
                        return -1
                    chunk = in_stream.read(this_run)
                    window[window_posn:window_posn + this_run] = chunk
                    window_posn += this_run
                else:
                    return -1

        if togo != 0:
            return -1
        start = window_posn
        if start == 0:
            start = window_size
        start -= out_len
        out_stream.write(bytes(window[start:start + out_len]))
        self.window_posn = window_posn
        self.R0 = R0
        self.R1 = R1
        self.R2 = R2
        self.frames_read += 1
        # The intel-E8 postprocess in the reference never executes for XNB
        # (it gates on absolute out-stream position < out_len-10), so omitted.
        return 0


def _read_u32le(stream):
    b = stream.read(4)
    return b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)
