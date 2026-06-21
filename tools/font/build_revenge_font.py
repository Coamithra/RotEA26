#!/usr/bin/env python
# ---------------------------------------------------------------------------
# build_revenge_font.py  --  bake the user's hand-drawn alphabet sheets into
# the game's SpriteFont (GFX/Menu/menufont): atlas <name>.fnt.png + binary
# metrics <name>.fnt, in the exact format WebContentManager.LoadFont reads.
#
# Sources (white glyphs on dark, with a frame + header strip), under
# tools/font/sources/:
#   revenge_font_caps.png   rows: A-J / K-T / U-Z / 0-9 (+ a corner diamond)
#   revenge_punctuation.png rows: a-j / k-t / u-z / [. , ! ' : ( ) - ? " % &]
#
# Targets (measured from the original menufont so layouts don't move):
#   lineSpacing 45, baseline 28px (line-top -> baseline), cap height 21px,
#   x-height 15px.
#
# Stage-12 fixes vs. the first pass:
#  (1) RESOLUTION: the atlas is supersampled SS x. Each glyph's BoundsInTexture
#      is SS x its design size, but Cropping / kerning / lineSpacing stay in
#      DESIGN units (so SpriteFont.MeasureString -- called directly all over the
#      game for layout -- is unchanged). SpriteBatchWrapper.DrawStringScaled draws
#      each SS x source into a design-size quad (per-glyph scale = Cropping.Size /
#      BoundsInTexture.Size = 1/SS for redrawn glyphs, 1 for the merged originals),
#      so text stays crisp after the Stage-10 RenderScale upscale.
#  (2) BASELINE + SLIVERS: per-row baselines come from flat-bottomed reference
#      glyphs (not a whole-row density median), and each glyph's bbox is trimmed to
#      its own connected component(s) inside a baseline-anchored row window, so no
#      stray neighbour-row / adjacent-glyph ink or hairline rides along.
#      Punctuation marks are scaled to CAP height (not x-height), fixing the tiny
#      "%, &, ?, ( )".
#  (3) KERNING: per-glyph side bearings + a tuned global spacing (SpriteFont has no
#      pair-kern table; tracking + bearings is the lever we have).
#
# Custom glyphs replace A-Z a-z 0-9 and the 12 punctuation marks; every other
# original glyph (space + debug symbols) is carried over unchanged from the
# *.orig backup; U+00B4 (the game's acute-accent "apostrophe") is aliased to the
# drawn apostrophe.
# Re-run after editing any sheet. Writes a preview PNG (+ a --debug montage);
# only overwrites the live font when run with --commit.
# ---------------------------------------------------------------------------
import os, sys, struct, json
import numpy as np
from PIL import Image
from scipy import ndimage

LANCZOS = Image.Resampling.LANCZOS
NEAREST = Image.Resampling.NEAREST

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MENU = os.path.join(ROOT, 'web/EvilAliensWeb/wwwroot/Content/gfx/menu')
SRC  = os.path.join(ROOT, 'tools/font/sources')   # hand-drawn sheets live here

CAPS_SHEET  = os.path.join(SRC, 'revenge_font_caps.png')
PUNC_SHEET  = os.path.join(SRC, 'revenge_punctuation.png')
FNT_PNG     = os.path.join(MENU, 'menufont.fnt.png')
FNT_META    = os.path.join(MENU, 'menufont.fnt')
PREVIEW     = os.path.join(ROOT, 'tools/font/_preview.png')
DEBUG_PNG   = os.path.join(ROOT, 'tools/font/_debug_rows.png')
OVERRIDES   = os.path.join(ROOT, 'tools/font/overrides.json')   # written by the live editor
EDITOR_DATA = os.path.join(ROOT, 'tools/font/editor/data.json') # auto-extraction for the editor

# sample lines the editor renders (kept short so the editor preview fits)
SAMPLE_LINES = [
    'The quick brown fox jumps over the lazy dog.',
    'Pack my box with five dozen liquor jugs.',
    'ABCDEFGHIJKLM  NOPQRSTUVWXYZ',
    'abcdefghijklm  nopqrstuvwxyz',
    '0123456789  .,!\'":()-?%&',
    'Score: 1234567890   50% off!',
    'REVENGE of the EVIL ALIENS!',
    '"Awardment" Unlocked - (Don\'t stop)',
]

# ---- targets locked onto the original menufont's metrics --------------------
TGT_BASELINE = 28      # line-top -> baseline, design px
TGT_CAP      = 21      # cap height, design px
TGT_XHEIGHT  = 15      # x-height, design px
LINE_SPACING = 45
SS           = 3       # atlas supersample factor (see fix 1 above)

# ---- spacing / kerning (fix 3) ---------------------------------------------
SPACING      = -1.0    # global tracking between glyphs (design px); italic tucks
SIDE_BEARING = 1.0     # per-glyph left/right bearing (design px)
SPACE_W      = 12.0    # space advance (design px) -- the merged original is too thin

# ---- extraction tuning ------------------------------------------------------
FRAME_MARGIN = 26      # crop the rounded border frame
THRESH       = 150     # luminance > THRESH = ink
MIN_ROW_H    = 70      # glyph rows are >=76px tall; headers are <=63px -> drop
ROW_ON_FRAC  = 0.05    # >5% of width lit = inside a glyph row (separates header)
ALPHA_LO     = 40.0    # luminance -> alpha ramp (outline/bg -> 0)
ALPHA_HI     = 205.0   # fill -> 1, AA preserved between
HAIRLINE     = 4       # components <= this tall are slivers (drop)
ATLAS_W      = 1024

# left-to-right glyph order per row, per sheet
CAPS_ROWS = ['ABCDEFGHIJ', 'KLMNOPQRST', 'UVWXYZ', '0123456789']
PUNC_ROWS = ['abcdefghij', 'klmnopqrst', 'uvwxyz', '.,!\':()-?"%&']

# reference glyphs whose ink bottoms define a row's baseline (flat-bottomed;
# round-overshoot + descenders excluded)
CAP_REF   = set('ABDEFHIKLMNPRTVWXYZ')
DIGIT_REF = set('0123456789')
LOW_REF   = set('acemnorsuvwxz')
PUNC_REF  = set('.!?:%&')
ASCENDERS = set('bdfhklt')      # lowercase ascenders ~= cap height (punc-sheet proxy)
DESCEND   = set('gjpqy,')       # extend below baseline


def load_gray(path):
    return np.asarray(Image.open(path).convert('L'), dtype=np.float32)


def find_rows(mask):
    """Glyph-row bands: runs where >ROW_ON_FRAC of the width is lit and the run
    is >= MIN_ROW_H tall (drops the shorter/sparser title header)."""
    on = mask.sum(axis=1) > (ROW_ON_FRAC * mask.shape[1])
    bands, s = [], None
    for y, v in enumerate(on):
        if v and s is None:
            s = y
        elif not v and s is not None:
            bands.append((s, y)); s = None
    if s is not None:
        bands.append((s, len(on)))
    return [b for b in bands if b[1] - b[0] >= MIN_ROW_H]


def seg_glyphs(mask, y0, y1, nchars):
    """Column-segment a row into exactly nchars glyph x-spans. When there are too
    many runs, resolve in this order: (1) drop a SHORT stray fragment -- a column
    whose tallest ink is far less than the row's glyphs, e.g. a flag/tail from a
    neighbouring row poking into this band; (2) drop a trailing decorative diamond
    (far-right, big gap); (3) merge the tightest split (a multi-part glyph like the
    two marks of " or the pieces of %)."""
    sub = mask[y0:y1]
    colcount = sub.sum(axis=0)          # lit band-rows per source column
    on = colcount > 0
    runs, s = [], None
    for x, v in enumerate(on):
        if v and s is None:
            s = x
        elif not v and s is not None:
            runs.append([s, x]); s = None
    if s is not None:
        runs.append([s, len(on)])
    runs = [r for r in runs if r[1] - r[0] >= 2]

    def gaps(rs):
        return [rs[i + 1][0] - rs[i][1] for i in range(len(rs) - 1)]

    def runheight(r):
        return int(colcount[r[0]:r[1]].max())   # tallest ink column in the run

    while len(runs) > nchars:
        gs = gaps(runs)
        med = float(np.median([runheight(r) for r in runs]))
        # Drop the TRAILING run when it's a decoration / neighbour-row flag: either a
        # SHORT fragment (e.g. a glyph's flag/tail poking in from the next row, like the
        # moved t's serif intruding on the a-j row) or far-separated (the corner diamond).
        # Trailing-only keeps interior short marks safe -- the period/hyphen in the
        # punctuation row are NOT trailing, so they're never dropped.
        if runheight(runs[-1]) < 0.5 * med or (gs and gs[-1] == max(gs)
                                               and gs[-1] >= max(np.median(gs) * 1.4, 8)):
            runs.pop(); continue
        i = int(np.argmin(gs))           # else merge the tightest split (multi-part glyph)
        runs[i] = [runs[i][0], runs[i + 1][1]]; del runs[i + 1]
    return [tuple(r) for r in runs]


def rough_bbox(mask, y0, y1, cx0, cx1):
    cm = mask[y0:y1, cx0:cx1]
    ys = np.where(cm.any(axis=1))[0]
    xs = np.where(cm.any(axis=0))[0]
    if len(ys) == 0:
        return None
    return (y0 + int(ys[0]), y0 + int(ys[-1]) + 1, cx0 + int(xs[0]), cx0 + int(xs[-1]) + 1)


def clean_bbox(mask, y0, y1, cx0, cx1, capH):
    """Tight bbox of the glyph's own connected component(s). The CC search is
    EXTENDED above/below the dense row band so connected descenders / below-
    baseline spurs (q p y g, and the K/R legs) are captured -- the band itself
    stops at the dense body, which used to clip them. The main (largest)
    component is kept WHOLE (so its connected descender/spur rides along); other
    components are kept only if their centre falls inside the original band
    (i, j dots, the colon, the two marks of ") -- which drops the neighbour-row
    ink the extended slice now reaches into. Hairline + edge slivers dropped."""
    asc_pad  = int(0.35 * capH)
    desc_pad = int(0.70 * capH)
    sy0 = max(0, y0 - asc_pad)
    sy1 = min(mask.shape[0], y1 + desc_pad)
    sub = mask[sy0:sy1, cx0:cx1]
    struct = ndimage.generate_binary_structure(2, 2)   # 8-connectivity
    lbl, n = ndimage.label(sub, structure=struct)
    if n == 0:
        return None
    objs = ndimage.find_objects(lbl)
    sizes = ndimage.sum(np.ones_like(sub, dtype=np.float32), lbl, range(1, n + 1))
    main = int(np.argmax(sizes))
    sw = sub.shape[1]
    keep = (lbl == main + 1)
    for k in range(n):
        if k == main:
            continue
        sl = objs[k]
        ct = sy0 + sl[0].start; cb = sy0 + sl[0].stop
        cl = sl[1].start;       cr = sl[1].stop
        if cb - ct <= HAIRLINE:                          # stray hairline
            continue
        if not (y0 <= 0.5 * (ct + cb) <= y1):            # neighbour-row ink
            continue
        if sizes[k] < 0.08 * sizes[main] and (cl <= 0 or cr >= sw):
            continue                                     # adjacent-glyph lean sliver
        keep |= (lbl == k + 1)
    ys, xs = np.where(keep)
    if len(ys) == 0:
        return None
    return (sy0 + int(ys.min()), sy0 + int(ys.max()) + 1,
            cx0 + int(xs.min()), cx0 + int(xs.max()) + 1)


def extract_sheet(path, rows):
    """Per glyph: dict(ch,row, it,ib,jl,jr [clean, core px], baseline [core px])."""
    g = load_gray(path)
    H, W = g.shape
    core = g[FRAME_MARGIN:H - FRAME_MARGIN, FRAME_MARGIN:W - FRAME_MARGIN]
    mask = core > THRESH
    bands = find_rows(mask)
    if len(bands) != len(rows):
        print(f'  WARN {os.path.basename(path)}: {len(bands)} rows, expected {len(rows)}',
              file=sys.stderr)

    # ---- pass 1: rough bboxes + segmentation -------------------------------
    rough = []   # per row: list of (ch, it,ib,jl,jr)
    for (y0, y1), chars in zip(bands, rows):
        spans = seg_glyphs(mask, y0, y1, len(chars))
        if len(spans) != len(chars):
            print(f'  WARN {os.path.basename(path)} "{chars}": {len(spans)} spans',
                  file=sys.stderr)
        row = []
        for (cx0, cx1), ch in zip(spans, chars):
            bb = rough_bbox(mask, y0, y1, cx0, cx1)
            if bb:
                row.append((ch, *bb))
        rough.append(((y0, y1), row))

    # cap-height proxy in this sheet's pixels (caps body, or ascenders on the
    # punctuation sheet) -> the row window half-heights
    cap_src = []
    for _, row in rough:
        for ch, it, ib, jl, jr in row:
            if ch.isupper() or ch in ASCENDERS:
                cap_src.append(ib - it)
    capH = float(np.median(cap_src)) if cap_src else 90.0

    # ---- pass 2: clean bboxes (CC) FIRST, then baseline from those bottoms --
    # (computing the baseline from the same clean bottoms the glyphs use removes
    #  the rough-vs-clean mismatch that floated round letters like o s t ~1px.)
    out = []
    for (y0, y1), row in rough:
        cg = []
        for (ch, it, ib, jl, jr) in row:
            cb = clean_bbox(mask, y0, y1, jl, jr, capH)
            if cb is None:
                cb = (it, ib, jl, jr)
            cg.append(dict(ch=ch, it=cb[0], ib=cb[1], jl=cb[2], jr=cb[3]))
        chars = [d['ch'] for d in cg]
        if any(c.isupper() for c in chars):
            refset = CAP_REF
        elif any(c.isdigit() for c in chars):
            refset = DIGIT_REF
        elif any(c in ('.,!\'":()-?%&') for c in chars):
            refset = PUNC_REF
        else:
            refset = LOW_REF
        ref_ib = [d['ib'] for d in cg if d['ch'] in refset]
        if len(ref_ib) >= 3:
            baseline = float(np.median(ref_ib))
        else:   # flat bottoms dominate; spurs/overshoot sit below (higher ib) -> low pct
            nd = [d['ib'] for d in cg if d['ch'] not in DESCEND] or [d['ib'] for d in cg]
            baseline = float(np.percentile(nd, 35))
        for d in cg:
            out.append(dict(ch=d['ch'], row_y=(y0, y1), baseline=baseline,
                            it=d['it'], ib=d['ib'], jl=d['jl'], jr=d['jr']))
    return out, g


def to_white_alpha(crop_l):
    """L crop -> RGBA white-on-transparent (alpha = remapped luminance, so the
    drawn dark outline becomes the transparent edge and AA / spiky terminals
    survive)."""
    a = np.asarray(crop_l, dtype=np.float32)
    a = np.clip((a - ALPHA_LO) / (ALPHA_HI - ALPHA_LO), 0, 1)
    h, w = a.shape
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., 0:3] = 255
    rgba[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(rgba, 'RGBA')


def read_orig():
    """Original menufont glyphs (from the *.orig backup if present, else live)."""
    meta = FNT_META + '.orig' if os.path.exists(FNT_META + '.orig') else FNT_META
    png  = FNT_PNG + '.orig' if os.path.exists(FNT_PNG + '.orig') else FNT_PNG
    d = open(meta, 'rb').read(); off = 0
    def i():
        nonlocal off; v = struct.unpack_from('<i', d, off)[0]; off += 4; return v
    def f():
        nonlocal off; v = struct.unpack_from('<f', d, off)[0]; off += 4; return v
    i(); f(); i(); i(); n = i()        # lineSpacing, spacing, hasDefault, defaultCp, n
    chars = [i() for _ in range(n)]
    glyphs = [(i(), i(), i(), i()) for _ in range(n)]
    crop = [(i(), i(), i(), i()) for _ in range(n)]
    kern = [(f(), f(), f()) for _ in range(n)]
    atlas = Image.open(png).convert('RGBA')
    by = {}
    for k, c in enumerate(chars):
        gb = glyphs[k]
        sub = atlas.crop((gb[0], gb[1], gb[0] + gb[2], gb[1] + gb[3]))
        by[chr(c)] = dict(img=sub, crop=crop[k], kern=kern[k])
    return by


def load_overrides():
    """Per-glyph manual tweaks from the live editor (tools/font/overrides.json):
    char -> {dTop,dBot,dLeft,dRight (capture-box edge deltas, source px),
             voff (vertical nudge, design px), dlsb,drsb (side bearings, design px)}."""
    if not os.path.exists(OVERRIDES):
        return {}
    try:
        with open(OVERRIDES, encoding='utf-8') as f:
            return json.load(f)
    except Exception as e:
        print(f'  WARN could not read {OVERRIDES}: {e}', file=sys.stderr)
        return {}


def emit_editor_data(meta):
    """Write the auto-extraction the live editor renders from (editor/data.json)."""
    os.makedirs(os.path.dirname(EDITOR_DATA), exist_ok=True)
    with open(EDITOR_DATA, 'w', encoding='utf-8') as f:
        json.dump(meta, f, indent=1)
    print(f'editor  -> {EDITOR_DATA}  ({len(meta["glyphs"])} glyphs)')


def build(debug=False):
    caps_g, caps_src = extract_sheet(CAPS_SHEET, CAPS_ROWS)
    punc_g, punc_src = extract_sheet(PUNC_SHEET, PUNC_ROWS)

    def med_h(glyphs, chars):
        hs = [g['ib'] - g['it'] for g in glyphs if g['ch'] in chars]
        return float(np.median(hs)) if hs else 1.0

    cap_med  = med_h(caps_g, set('ABCDEFGHIJKLMNOPQRSTUVWXYZ'))
    x_med    = med_h(punc_g, LOW_REF)
    asc_med  = med_h(punc_g, ASCENDERS)
    scale_caps = TGT_CAP / cap_med               # caps + digits
    scale_low  = TGT_XHEIGHT / x_med             # lowercase (layout-stable x-height)
    scale_punc = TGT_CAP / asc_med               # punctuation marks (cap-anchored)
    print(f'caps body  {cap_med:.1f}px -> x{scale_caps:.4f}  (cap {cap_med*scale_caps:.1f})')
    print(f'x-height   {x_med:.1f}px -> x{scale_low:.4f}  (x {x_med*scale_low:.1f})')
    print(f'ascenders  {asc_med:.1f}px -> punct x{scale_punc:.4f}  '
          f'(asc {asc_med*scale_punc:.1f})')

    glyphs = {}   # char -> dict(img RGBA at SSx, cropY, w,h design, lsb, rsb, adv)
    ov = load_overrides()          # per-glyph manual tweaks from the live editor
    editor_glyphs = []             # auto extraction data the editor renders from

    def add(entry, scale, sheet, cls):
        ch = entry['ch']
        src = caps_src if sheet == 'caps' else punc_src
        coreH = src.shape[0] - 2 * FRAME_MARGIN
        coreW = src.shape[1] - 2 * FRAME_MARGIN
        # record the AUTO box (full-source px) for the editor before overrides
        editor_glyphs.append(dict(ch=ch, sheet=sheet, cls=cls,
                                  x=entry['jl'] + FRAME_MARGIN, y=entry['it'] + FRAME_MARGIN,
                                  w=entry['jr'] - entry['jl'], h=entry['ib'] - entry['it'],
                                  baseline=round(entry['baseline'] + FRAME_MARGIN, 1),
                                  scale=round(scale, 6)))
        # apply per-glyph overrides (capture-box deltas + vertical nudge + bearings)
        o = ov.get(ch, {})
        it = max(0, min(coreH - 1, entry['it'] + int(o.get('dTop', 0))))
        ib = max(it + 1, min(coreH, entry['ib'] + int(o.get('dBot', 0))))
        jl = max(0, min(coreW - 1, entry['jl'] + int(o.get('dLeft', 0))))
        jr = max(jl + 1, min(coreW, entry['jr'] + int(o.get('dRight', 0))))
        designW = max(1, round((jr - jl) * scale))
        designH = max(1, round((ib - it) * scale))
        ascent  = (entry['baseline'] - it) * scale
        voff    = float(o.get('voff', 0))
        cropY   = round(TGT_BASELINE - ascent) + int(np.floor(voff))
        vf      = voff - float(np.floor(voff))         # fractional design px -> sub-pixel
        crop = Image.fromarray(src[it + FRAME_MARGIN:ib + FRAME_MARGIN,
                                   jl + FRAME_MARGIN:jr + FRAME_MARGIN].astype(np.uint8), 'L')
        gimg = to_white_alpha(crop).resize((designW * SS, designH * SS), LANCZOS)
        dh = designH
        if vf > 1e-6:   # bake the sub-pixel shift into a 1-design-px-taller tile (atlas density)
            gimg = gimg.transform((designW * SS, (designH + 1) * SS), Image.AFFINE,
                                  (1, 0, 0, 0, 1, -vf * SS), resample=Image.BILINEAR,
                                  fillcolor=(0, 0, 0, 0))
            dh = designH + 1
        glyphs[ch] = dict(img=gimg, cropY=cropY, w=designW, h=dh,
                          lsb=SIDE_BEARING + float(o.get('dlsb', 0)), adv=float(designW),
                          rsb=SIDE_BEARING + float(o.get('drsb', 0)))

    for e in caps_g:
        add(e, scale_caps, 'caps', 'cap')
    for e in punc_g:
        is_low = e['ch'].isalpha()
        add(e, scale_low if is_low else scale_punc, 'punc', 'low' if is_low else 'pun')

    # ---- merge the original glyphs we didn't draw (space + debug symbols) ----
    orig = read_orig()
    for ch, o in orig.items():
        if ch in glyphs:
            continue
        glyphs[ch] = dict(img=o['img'], cropY=o['crop'][1],
                          w=o['img'].width, h=o['img'].height,
                          lsb=o['kern'][0], adv=o['kern'][1], rsb=o['kern'][2],
                          _orig=True)
    if ' ' in glyphs:                       # widen the (thin) original space
        glyphs[' ']['adv'] = SPACE_W
        glyphs[' ']['lsb'] = 0.0; glyphs[' ']['rsb'] = 0.0
    if "'" in glyphs:                       # alias U+00B4 -> drawn apostrophe
        ap = glyphs["'"]
        glyphs['´'] = dict(ap, _alias=True)

    # ---- shelf-pack the atlas ----------------------------------------------
    items = sorted(glyphs.items(), key=lambda kv: -kv[1]['img'].height)
    pad = 1; x = pad; y = pad; rowh = 0
    placed = {}
    for ch, g in items:
        w, h = g['img'].size
        if x + w + pad > ATLAS_W:
            x = pad; y += rowh + pad; rowh = 0
        placed[ch] = (x, y)
        rowh = max(rowh, h); x += w + pad
    atlas_h = y + rowh + pad
    atlas = Image.new('RGBA', (ATLAS_W, atlas_h), (0, 0, 0, 0))
    for ch, g in glyphs.items():
        atlas.alpha_composite(g['img'], placed[ch])

    # ---- character table (sorted by codepoint) -----------------------------
    rec = []
    for ch in sorted(glyphs.keys(), key=ord):
        g = glyphs[ch]
        px, py = placed[ch]
        bw, bh = g['img'].size                        # BoundsInTexture = SSx (or 1x)
        bounds = (px, py, bw, bh)
        crop = (0, g['cropY'], g['w'], g['h'])        # Cropping = DESIGN size
        kern = (float(g['lsb']), float(g['adv']), float(g['rsb']))
        rec.append((ord(ch), bounds, crop, kern))

    if debug:
        render_debug(caps_g + punc_g, caps_src, punc_src)
    editor_meta = dict(
        glyphs=editor_glyphs,
        globals=dict(
            tgtBaseline=TGT_BASELINE, lineSpacing=LINE_SPACING, ss=SS,
            spacing=SPACING, sideBearing=SIDE_BEARING, spaceW=SPACE_W,
            alphaLo=ALPHA_LO, alphaHi=ALPHA_HI, frame=FRAME_MARGIN,
            scales=dict(caps=round(scale_caps, 6), low=round(scale_low, 6),
                        punc=round(scale_punc, 6)),
            sheets=dict(
                caps=dict(file='revenge_font_caps.png', w=int(caps_src.shape[1]),
                          h=int(caps_src.shape[0])),
                punc=dict(file='revenge_punctuation.png', w=int(punc_src.shape[1]),
                          h=int(punc_src.shape[0]))),
            sampleLines=SAMPLE_LINES))
    return atlas, rec, glyphs, placed, editor_meta


def _advance(glyphs, text):
    penx = 4.0; first = True
    for ch in text:
        g = glyphs.get(ch, glyphs[' '])
        penx += (max(g['lsb'], 0.0) if first else SPACING + g['lsb'])
        first = False
        penx += g['adv'] + g['rsb']
    return penx


def render_preview(glyphs, placed, atlas, lines, zoom=2):
    """Replicate KNI's SpriteFont layout (DESIGN metrics) and draw each glyph by
    downscaling its SSx atlas tile to design*zoom -- mimics the on-screen upscale
    so spacing/baseline AND crispness are visible. A red line marks the baseline."""
    if isinstance(lines, str):
        lines = [lines]
    W = int(max(_advance(glyphs, t) for t in lines)) + 8
    H = LINE_SPACING * len(lines) + 8
    canvas = Image.new('RGBA', (W * zoom, H * zoom), (18, 18, 22, 255))
    for li, text in enumerate(lines):
        top = 4 + li * LINE_SPACING
        for x in range(0, W * zoom):       # baseline guide
            canvas.putpixel((x, (top + TGT_BASELINE) * zoom), (120, 40, 40, 255))
        penx = 4.0; first = True
        for ch in text:
            if ch not in glyphs:
                ch = ' '
            g = glyphs[ch]
            penx += (max(g['lsb'], 0.0) if first else SPACING + g['lsb'])
            first = False
            px, py = placed[ch]; bw, bh = g['img'].size
            if g['w'] > 0 and g['h'] > 0 and bw > 0:
                tile = atlas.crop((px, py, px + bw, py + bh)).resize(
                    (max(1, g['w'] * zoom), max(1, g['h'] * zoom)), LANCZOS)
                canvas.alpha_composite(tile, (round(penx * zoom), (top + g['cropY']) * zoom))
            penx += g['adv'] + g['rsb']
    canvas.convert('RGB').save(PREVIEW)
    print(f'preview -> {PREVIEW}  (max line width {W} design px)')


def render_debug(allglyphs, caps_src, punc_src):
    """Montage of every extracted glyph (clean bbox, white-on-transparent) to
    eyeball slivers / baselines."""
    tiles = []
    for e in allglyphs:
        src = caps_src if e['ch'].isupper() or e['ch'].isdigit() else punc_src
        crop = Image.fromarray(src[e['it'] + FRAME_MARGIN:e['ib'] + FRAME_MARGIN,
                                   e['jl'] + FRAME_MARGIN:e['jr'] + FRAME_MARGIN]
                               .astype(np.uint8), 'L')
        tiles.append(to_white_alpha(crop))
    cols = 16
    cw = max(t.width for t in tiles) + 6
    ch = max(t.height for t in tiles) + 6
    rows = (len(tiles) + cols - 1) // cols
    mont = Image.new('RGBA', (cols * cw, rows * ch), (30, 30, 36, 255))
    for i, t in enumerate(tiles):
        cx = (i % cols) * cw + 3; cy = (i // cols) * ch + 3
        mont.alpha_composite(t, (cx, cy))
    mont.convert('RGB').save(DEBUG_PNG)
    print(f'debug   -> {DEBUG_PNG}  ({len(tiles)} glyphs)')


def write_font(atlas, rec):
    for p in (FNT_PNG, FNT_META):           # back up the original once
        bak = p + '.orig'
        if not os.path.exists(bak):
            import shutil; shutil.copy2(p, bak); print(f'backup -> {bak}')
    atlas.save(FNT_PNG)
    with open(FNT_META, 'wb') as f:
        f.write(struct.pack('<i', LINE_SPACING))
        f.write(struct.pack('<f', float(SPACING)))
        f.write(struct.pack('<i', 0))       # hasDefault
        f.write(struct.pack('<i', 0))       # defaultCp (unused)
        f.write(struct.pack('<i', len(rec)))
        for cp, b, c, k in rec:
            f.write(struct.pack('<i', cp))
        for cp, b, c, k in rec:
            f.write(struct.pack('<iiii', *b))
        for cp, b, c, k in rec:
            f.write(struct.pack('<iiii', *c))
        for cp, b, c, k in rec:
            f.write(struct.pack('<fff', *k))
    print(f'wrote {FNT_PNG} ({atlas.size}) and {FNT_META} ({len(rec)} glyphs, SS={SS})')


if __name__ == '__main__':
    atlas, rec, glyphs, placed, editor_meta = build(debug='--debug' in sys.argv)
    render_preview(glyphs, placed, atlas, SAMPLE_LINES)
    if '--emit-editor' in sys.argv:
        emit_editor_data(editor_meta)
    if '--commit' in sys.argv:
        write_font(atlas, rec)
    else:
        print('\n[dry run] preview only. Re-run with --commit to write the font'
              + (' / --emit-editor to refresh the editor data.' if '--emit-editor' not in sys.argv else '.'))
