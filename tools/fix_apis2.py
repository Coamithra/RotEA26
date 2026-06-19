"""Second batch of unambiguous literal XNA 3.x -> 4.0 fixes."""
import glob

DST = r'C:\Programming\RotEA26\web\EvilAliensWeb\Game'

REPLACEMENTS = [
    # PlayerIndex used as an int index -> explicit cast
    ('power[VibrationTimers[num].player]', 'power[(int)VibrationTimers[num].player]'),
    ('Gamer.SignedInGamers[activePlayer]', 'Gamer.SignedInGamers[(int)activePlayer]'),
    ('return OtherPlayersSettings[val];', 'return OtherPlayersSettings[(int)val];'),
    # Texture3D 3.x ctor (numberLevels:int, TextureUsage) -> 4.0 (mipMap:bool)
    (', 1, (TextureUsage)0, (SurfaceFormat)2)', ', false, (SurfaceFormat)2)'),
    # Xbox-360-only / removed APIs -> commented out
    ('graphics.MinimumPixelShaderProfile = (ShaderProfile)4;',
     '// graphics.MinimumPixelShaderProfile = (ShaderProfile)4; // removed in XNA 4.0'),
    ('Thread.CurrentThread.SetProcessorAffinity(new int[1] { 3 });',
     '// Thread.CurrentThread.SetProcessorAffinity(new int[1] { 3 }); // Xbox 360 only'),
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
    print(f"{n:3d}  {old[:55]!r}")
