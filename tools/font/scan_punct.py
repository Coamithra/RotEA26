import re, glob, collections

files = glob.glob('web/EvilAliensWeb/Game/**/*.cs', recursive=True) + \
        glob.glob('web/EvilAliensWeb/Compat/**/*.cs', recursive=True)
lit = re.compile(r'"((?:[^"\\]|\\.)*)"')
counter = collections.Counter()
examples = collections.defaultdict(list)

for fp in files:
    try:
        txt = open(fp, encoding='utf-8', errors='ignore').read()
    except Exception:
        continue
    for m in lit.finditer(txt):
        s = m.group(1)
        low = s.lower()
        # skip obvious asset paths / non-text
        if low.startswith(('gfx/', 'content/', 'sfx/', 'vo/', 'music/', 'fx/')):
            continue
        if s.endswith(('.png', '.wav', '.ogg', '.fnt', '.mgfxo', '.xml', '.curve')):
            continue
        if '/' in s and ' ' not in s:
            continue
        for ch in s:
            if ch.isalnum() or ch == ' ':
                continue
            if 32 <= ord(ch) < 127:
                counter[ch] += 1
                if len(examples[ch]) < 3 and s.strip():
                    examples[ch].append(s if len(s) < 40 else s[:37] + '...')

print('Punctuation/symbols in game text literals (char  count  examples):')
for ch, c in sorted(counter.items(), key=lambda kv: -kv[1]):
    print(f'  [{ch}]  x{c:<4} {examples[ch]}')
