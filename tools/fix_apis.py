"""Safe global XNA 3.x -> 4.0 textual edits that don't need per-site context."""
import glob

DST = r'C:\Programming\RotEA26\web\EvilAliensWeb\Game'

# (old, new) literal replacements
REPLACEMENTS = [
    ('.ElapsedRealTime', '.ElapsedGameTime'),   # 4.0 removed Real* time
    ('.TotalRealTime', '.TotalGameTime'),
    ('(RenderTarget)', '(Texture2D)'),          # 3.x base type -> Texture2D (has Width/Height/Dispose)
    (', (EffectPool)null', ''),                 # 4.0 BasicEffect/Effect dropped the EffectPool arg
]

counts = {old: 0 for old, _ in REPLACEMENTS}
for f in glob.glob(DST + r'\**\*.cs', recursive=True):
    src = open(f, encoding='utf-8').read()
    orig = src
    for old, new in REPLACEMENTS:
        c = src.count(old)
        if c:
            counts[old] += c
            src = src.replace(old, new)
    if src != orig:
        open(f, 'w', encoding='utf-8').write(src)

for old, n in counts.items():
    print(f"{n:4d}  {old!r}")
