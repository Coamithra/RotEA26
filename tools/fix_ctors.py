"""Fix ILSpy '_002Ector' artifacts: X._002Ector(args) -> X = new TYPE(args).
Uses the compiler's CS1061 errors (which carry the receiver TYPE + file/line/col).
Run from web/EvilAliensWeb after writing the errors to build_errors.txt.
"""
import re, collections

ERR = r'C:\Programming\RotEA26\web\EvilAliensWeb\build_errors.txt'
err_re = re.compile(r"^(.*?)\((\d+),(\d+)\): error CS1061: '([^']+)' does not contain a definition for '_002Ector'")

byfile = collections.defaultdict(list)
for ln in open(ERR, encoding='utf-8'):
    m = err_re.match(ln.strip())
    if m:
        byfile[m.group(1)].append((int(m.group(2)), int(m.group(3)), m.group(4)))

fixed = 0
for f, hits in byfile.items():
    lines = open(f, encoding='utf-8').read().split('\n')
    perline = collections.defaultdict(list)
    for line, col, typ in hits:
        perline[line].append((col, typ))
    for line, items in perline.items():
        items.sort()
        text = lines[line - 1]
        parts = text.split('._002Ector(')
        if len(parts) - 1 != len(items):
            types = [items[0][1]] * (len(parts) - 1)
        else:
            types = [t for _, t in items]
        rebuilt = parts[0]
        for i, seg in enumerate(parts[1:]):
            rebuilt += ' = new ' + types[i] + '(' + seg
            fixed += 1
        lines[line - 1] = rebuilt
    open(f, 'w', encoding='utf-8').write('\n'.join(lines))

print("constructors rewritten:", fixed)
