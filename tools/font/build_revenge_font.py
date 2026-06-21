#!/usr/bin/env python
# ---------------------------------------------------------------------------
# build_revenge_font.py  --  bake the user's hand-drawn alphabet sheets into
# the game's SpriteFont (GFX/Menu/menufont): atlas <name>.fnt.png + binary
# metrics <name>.fnt, in the exact format WebContentManager.LoadFont reads.
#
# Sources (white glyphs on dark, with a frame + header strip):
#   revenge_font_caps.png   rows: A-J / K-T / U-Z / 0-9
#   revenge_punctuation.png rows: a-j / k-t / u-z / [. , ! ' : ( ) - ? " % &]
#
# Targets (measured from the original menufont so layouts don't move):
#   lineSpacing 45, spacing 2.0, baseline 28px, cap height 21px, x-height 15px
#
# Custom glyphs replace A-Z a-z 0-9 and the 12 punctuation marks; every other
# original glyph (space + debug symbols) is carried over unchanged; U+00B4 (the
# game's acute-accent "apostrophe") is aliased to the drawn apostrophe.
# Re-run after editing any sheet. Writes a preview PNG; only overwrites the live
# font when run with --commit.
# ---------------------------------------------------------------------------
import os, sys, struct
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MENU = os.path.join(ROOT, 'web/EvilAliensWeb/wwwroot/Content/gfx/menu')

CAPS_SHEET  = os.path.join(MENU, 'revenge_font_caps.png')
PUNC_SHEET  = os.path.join(MENU, 'revenge_punctuation.png')
FNT_PNG     = os.path.join(MENU, 'menufont.fnt.png')
FNT_META    = os.path.join(MENU, 'menufont.fnt')
PREVIEW     = os.path.join(ROOT, 'tools/font/_preview.png')

# original metrics we lock onto
TGT_BASELINE = 28      # line-top -> baseline, px
TGT_CAP      = 21      # cap height, px
TGT_XHEIGHT  = 15      # x-height, px
LINE_SPACING = 45
SPACING      = 2.0
SIDE_BEARING = 0       # per-glyph left/right bearing; spacing handled globally

FRAME_MARGIN = 26      # crop the rounded border frame
GLYPH_ALPHA  = 40      # alpha floor for ink bbox
DENSE_FRAC   = 0.22    # fraction-of-max column-count that counts as glyph "body"
ROW_ON_FRAC  = 0.05    # >5% of width lit = inside a glyph row (separates header)
MIN_ROW_H    = 70      # glyph rows are >=76px tall; headers are <=63px -> drop

# left-to-right glyph order per row, per sheet
CAPS_ROWS = ['ABCDEFGHIJ', 'KLMNOPQRST', 'UVWXYZ', '0123456789']
PUNC_ROWS = ['abcdefghij', 'klmnopqrst', 'uvwxyz', '.,!\':()-?"%&']


def load_gray(path):
    return np.asarray(Image.open(path).convert('L'), dtype=np.float32)


def load_rgba(path):
    return Image.open(path).convert('L')   # we rebuild RGBA from luminance


def find_rows(mask):
    """Return the glyph-row bands of a frame-cropped mask. Bands are runs where
    >ROW_ON_FRAC of the width is lit; the (sparser/shorter) title header falls
    below MIN_ROW_H and is dropped, leaving exactly the glyph rows."""
    rowsum = mask.sum(axis=1)
    on = rowsum > (ROW_ON_FRAC * mask.shape[1])
    bands = []
    s = None
    for y, v in enumerate(on):
        if v and s is None:
            s = y
        elif not v and s is not None:
            bands.append([s, y]); s = None
    if s is not None:
        bands.append([s, len(on)])
    return [b for b in bands if b[1] - b[0] >= MIN_ROW_H]


def seg_columns(mask, y0, y1):
    sub = mask[y0:y1]
    colsum = sub.sum(axis=0)
    on = colsum > 0
    cols = []
    s = None
    for x, v in enumerate(on):
        if v and s is None:
            s = x
        elif not v and s is not None:
            cols.append([s, x]); s = None
    if s is not None:
        cols.append([s, len(on)])
    return [c for c in cols if c[1] - c[0] >= 3]


def dense_band(mask, y0, y1, x0, x1):
    """Vertical extent of the glyph 'body' across [x0:x1], ignoring thin spikes:
    scanlines whose lit-count exceeds DENSE_FRAC*max."""
    sub = mask[y0:y1, x0:x1]
    rc = sub.sum(axis=1)
    if rc.max() == 0:
        return y0, y1
    thr = DENSE_FRAC * rc.max()
    ys = np.where(rc > thr)[0]
    return y0 + int(ys[0]), y0 + int(ys[-1]) + 1


def extract_sheet(path, rows):
    """Return list of (char, PIL 'L' glyph crop, ink_top, ink_bottom, baseline,
    body_top, body_bottom) in source pixels, per row metadata attached."""
    g = load_gray(path)
    H, W = g.shape
    core = g[FRAME_MARGIN:H - FRAME_MARGIN, FRAME_MARGIN:W - FRAME_MARGIN]
    mask = core > 150
    glyph_bands = find_rows(mask)
    if len(glyph_bands) != len(rows):
        print(f'  WARN {os.path.basename(path)}: found {len(glyph_bands)} glyph '
              f'rows, expected {len(rows)}', file=sys.stderr)
    src = Image.open(path).convert('L')
    out = []
    for ri, (band, chars) in enumerate(zip(glyph_bands, rows)):
        y0, y1 = band
        cols = seg_columns(mask, y0, y1)
        # drop the decorative corner diamond: keep leftmost len(chars)
        cols = sorted(cols, key=lambda c: c[0])[:len(chars)]
        if len(cols) != len(chars):
            print(f'  WARN {os.path.basename(path)} row {ri}: {len(cols)} cols '
                  f'for {len(chars)} chars "{chars}" -> {[c[1]-c[0] for c in cols]}',
                  file=sys.stderr)
        # per-row baseline = bottom of the dense body across whole row
        bt, bb = dense_band(mask, y0, y1, 0, mask.shape[1])
        row_baseline = bb
        for (cx0, cx1), ch in zip(cols, chars):
            # tight ink bbox (incl. spikes) within this column slice
            colmask = mask[y0:y1, cx0:cx1]
            ys = np.where(colmask.any(axis=1))[0]
            it, ib = y0 + int(ys[0]), y0 + int(ys[-1]) + 1
            # glyph body band (for reporting; baseline already from row)
            gbt, gbb = dense_band(mask, y0, y1, cx0, cx1)
            crop = src.crop((cx0 + FRAME_MARGIN, it + FRAME_MARGIN,
                             cx1 + FRAME_MARGIN, ib + FRAME_MARGIN))
            out.append(dict(ch=ch, img=crop, top=it, bot=ib,
                            baseline=row_baseline, bodytop=gbt, bodybot=gbb,
                            row=ri))
    return out, glyph_bands


def to_white_alpha(img_l):
    """L crop -> RGBA white-on-transparent (alpha = remapped luminance)."""
    a = np.asarray(img_l, dtype=np.float32)
    a = (a - 40.0) / (205.0 - 40.0)      # fill->1, outline/bg->0, keep AA
    a = np.clip(a, 0, 1)
    h, w = a.shape
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., 0:3] = 255
    rgba[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(rgba, 'RGBA')


def read_orig():
    d = open(FNT_META, 'rb').read()
    off = 0
    def i():
        nonlocal off; v = struct.unpack_from('<i', d, off)[0]; off += 4; return v
    def f():
        nonlocal off; v = struct.unpack_from('<f', d, off)[0]; off += 4; return v
    ls = i(); sp = f(); hd = i(); dc = i(); n = i()
    chars = [i() for _ in range(n)]
    glyphs = [(i(), i(), i(), i()) for _ in range(n)]
    crop = [(i(), i(), i(), i()) for _ in range(n)]
    kern = [(f(), f(), f()) for _ in range(n)]
    atlas = Image.open(FNT_PNG).convert('RGBA')
    by = {}
    for k, c in enumerate(chars):
        gb = glyphs[k]
        sub = atlas.crop((gb[0], gb[1], gb[0] + gb[2], gb[1] + gb[3]))
        by[chr(c)] = dict(img=sub, crop=crop[k], kern=kern[k])
    return by


def build():
    caps, _ = extract_sheet(CAPS_SHEET, CAPS_ROWS)
    punc, _ = extract_sheet(PUNC_SHEET, PUNC_ROWS)

    # ---- scales: caps body -> 21, lowercase x-height -> 15 ------------------
    cap_bodies = [g['bodybot'] - g['bodytop'] for g in caps
                  if g['ch'].isalpha()]
    cap_med = float(np.median(cap_bodies))
    scale_caps = TGT_CAP / cap_med

    low_x = [g['bodybot'] - g['bodytop'] for g in punc
             if g['ch'] in 'acemnorsuvwxz']      # x-height letters only
    xmed = float(np.median(low_x))
    scale_low = TGT_XHEIGHT / xmed

    print(f'caps body median {cap_med:.1f}px -> scale {scale_caps:.4f}')
    print(f'lowercase x median {xmed:.1f}px -> scale {scale_low:.4f}')

    # ---- assemble custom glyph dict ----------------------------------------
    glyphs = {}   # char -> dict(img RGBA scaled, cropY, w, h)

    def add(entry, scale):
        ch = entry['ch']
        baseline = entry['baseline']
        top, bot = entry['top'], entry['bot']
        gimg = to_white_alpha(entry['img'])
        nw = max(1, round(gimg.width * scale))
        nh = max(1, round(gimg.height * scale))
        gimg = gimg.resize((nw, nh), Image.LANCZOS)
        ascent = (baseline - top) * scale          # ink top above baseline
        cropY = round(TGT_BASELINE - ascent)
        glyphs[ch] = dict(img=gimg, cropY=cropY, w=nw, h=nh)

    for e in caps:
        add(e, scale_caps)
    for e in punc:
        # punctuation row uses the lowercase sheet scale; its baseline is the
        # extrapolated grid baseline computed below
        add(e, scale_low)

    # fix the punctuation row baseline: extrapolate from the 3 lowercase rows
    low_baselines = []
    for r in range(3):
        bs = [g['baseline'] for g in punc if g['row'] == r]
        if bs:
            low_baselines.append(bs[0])
    if len(low_baselines) == 3:
        pitch = (low_baselines[2] - low_baselines[0]) / 2.0
        punc_baseline = low_baselines[2] + pitch
        for e in punc:
            if e['row'] == 3:
                ascent = (punc_baseline - e['top']) * scale_low
                glyphs[e['ch']]['cropY'] = round(TGT_BASELINE - ascent)
        print(f'lowercase baselines {low_baselines} -> punct baseline '
              f'{punc_baseline:.0f}')

    # ---- merge original glyphs for everything we didn't draw ----------------
    orig = read_orig()
    for ch, o in orig.items():
        if ch in glyphs:
            continue
        glyphs[ch] = dict(img=o['img'], cropY=o['crop'][1],
                          w=o['img'].width, h=o['img'].height, _orig=True)
    # alias U+00B4 -> apostrophe
    if "'" in glyphs:
        ap = glyphs["'"]
        glyphs['´'] = dict(img=ap['img'], cropY=ap['cropY'],
                                w=ap['w'], h=ap['h'], _alias=True)

    # ---- shelf-pack atlas ---------------------------------------------------
    items = sorted(glyphs.items(), key=lambda kv: -kv[1]['h'])
    ATLAS_W = 512
    pad = 1
    x = pad; y = pad; rowh = 0
    placed = {}
    for ch, g in items:
        if x + g['w'] + pad > ATLAS_W:
            x = pad; y += rowh + pad; rowh = 0
        placed[ch] = (x, y)
        rowh = max(rowh, g['h'])
        x += g['w'] + pad
    atlas_h = y + rowh + pad
    atlas = Image.new('RGBA', (ATLAS_W, atlas_h), (0, 0, 0, 0))
    for ch, g in glyphs.items():
        atlas.alpha_composite(g['img'], placed[ch])

    # ---- character table (sorted by codepoint) -----------------------------
    chars = sorted(glyphs.keys(), key=ord)
    rec = []
    for ch in chars:
        g = glyphs[ch]
        px, py = placed[ch]
        bounds = (px, py, g['w'], g['h'])
        crop = (0, g['cropY'], g['w'], g['h'])
        kern = (float(SIDE_BEARING), float(g['w']), float(SIDE_BEARING))
        rec.append((ord(ch), bounds, crop, kern))

    return atlas, rec, glyphs, placed


def render_preview(atlas, glyphs, placed, text, scale=3):
    """Replicate the MonoGame SpriteFont layout to eyeball spacing/baseline."""
    W = 1400; H = LINE_SPACING * 2
    canvas = Image.new('RGBA', (W, H), (18, 18, 22, 255))
    penx = 6
    for i, ch in enumerate(text):
        if ch not in glyphs:
            ch = ' '
        g = glyphs[ch]
        px, py = placed[ch]
        sub = atlas.crop((px, py, px + g['w'], py + g['h']))
        if i > 0:
            penx += SPACING
        canvas.alpha_composite(sub, (round(penx), 4 + g['cropY']))
        penx += g['w']
    canvas = canvas.resize((W * scale // 2, H * scale // 2), Image.NEAREST)
    canvas.convert('RGB').save(PREVIEW)
    print(f'preview -> {PREVIEW}  (line width used {penx:.0f}px)')


def write_font(atlas, rec):
    # backup once
    for p in (FNT_PNG, FNT_META):
        bak = p + '.orig'
        if not os.path.exists(bak):
            import shutil; shutil.copy2(p, bak)
            print(f'backup -> {bak}')
    atlas.save(FNT_PNG)
    with open(FNT_META, 'wb') as f:
        f.write(struct.pack('<i', LINE_SPACING))
        f.write(struct.pack('<f', SPACING))
        f.write(struct.pack('<i', 0))     # hasDefault
        f.write(struct.pack('<i', 0))     # defaultCp (unused)
        f.write(struct.pack('<i', len(rec)))
        for cp, b, c, k in rec:
            f.write(struct.pack('<i', cp))
        for cp, b, c, k in rec:
            f.write(struct.pack('<iiii', *b))
        for cp, b, c, k in rec:
            f.write(struct.pack('<iiii', *c))
        for cp, b, c, k in rec:
            f.write(struct.pack('<fff', *k))
    print(f'wrote {FNT_PNG} ({atlas.size}) and {FNT_META} ({len(rec)} glyphs)')


if __name__ == '__main__':
    atlas, rec, glyphs, placed = build()
    render_preview(atlas, glyphs, placed,
                   'REVENGE of the EVIL ALIENS! (Don\'t stop) 50% 1234567890')
    if '--commit' in sys.argv:
        write_font(atlas, rec)
    else:
        print('\n[dry run] preview only. Re-run with --commit to write the font.')
