#!/usr/bin/env python
# per-glyph extraction diagnostics: where does each glyph's top/bottom land
# relative to the baseline (design 28)?  bottom_off > 0 = sits below baseline
# (descender/overshoot ok); < 0 = FLOATS above baseline; top = design cropY.
import numpy as np
import build_revenge_font as B

caps_g, _ = B.extract_sheet(B.CAPS_SHEET, B.CAPS_ROWS)
punc_g, _ = B.extract_sheet(B.PUNC_SHEET, B.PUNC_ROWS)

def med_h(g, chars):
    hs = [e['ib'] - e['it'] for e in g if e['ch'] in chars]
    return float(np.median(hs)) if hs else 1.0

cap_med = med_h(caps_g, set('ABCDEFGHIJKLMNOPQRSTUVWXYZ'))
x_med   = med_h(punc_g, B.LOW_REF)
asc_med = med_h(punc_g, B.ASCENDERS)
sc_caps = B.TGT_CAP / cap_med
sc_low  = B.TGT_XHEIGHT / x_med
sc_punc = B.TGT_CAP / asc_med

def report(entry, scale, tag):
    it, ib, bl = entry['it'], entry['ib'], entry['baseline']
    designH = round((ib - it) * scale)
    ascent  = (bl - it) * scale
    cropY   = round(B.TGT_BASELINE - ascent)
    bottom  = cropY + designH
    boff    = bottom - B.TGT_BASELINE
    return (entry['ch'], tag, it, ib, round(bl,1), cropY, designH, round(boff,1))

rows = []
for e in caps_g:
    rows.append(report(e, sc_caps, 'cap'))
for e in punc_g:
    rows.append(report(e, sc_low if e['ch'].isalpha() else sc_punc,
                       'low' if e['ch'].isalpha() else 'pun'))

print(f"{'ch':>3} {'cls':>3} {'it':>4} {'ib':>4} {'base':>6} {'cropY':>5} {'dH':>3} {'botOff':>6}")
print('-'*44)
for r in rows:
    flag = '   <-- FLOATS' if r[7] < -0.6 else ('  (below)' if r[7] > 2.5 else '')
    print(f"{r[0]!r:>3} {r[1]:>3} {r[2]:>4} {r[3]:>4} {r[4]:>6} {r[5]:>5} {r[6]:>3} {r[7]:>6}{flag}")
