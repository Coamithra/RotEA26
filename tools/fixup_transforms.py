"""Re-derive the KNI Game/ source from the pristine decompiled output, applying
the mechanical fixes for ILSpy artifacts:
  1. (TYPE)(ref EXPR)            -> EXPR        (invalid 'cast of a ref' artifact)
  2. ((BaseClass)this).Member    -> base.Member (base access / base virtual calls)
  3. ((BaseClass)this)           -> this        (leftover value uses)
Reads from src_decompiled (never modified); writes to web/EvilAliensWeb/Game.
"""
import re, os, glob

SRC = r'C:\Programming\RotEA26\src_decompiled'
DST = r'C:\Programming\RotEA26\web\EvilAliensWeb\Game'

ref_prefix = re.compile(r'\([\w.]+\)\(ref ')

def fix_ref(text):
    count = 0
    while True:
        m = ref_prefix.search(text)
        if not m:
            break
        start = m.start()
        i = m.end()
        depth = 1
        while i < len(text) and depth > 0:
            c = text[i]
            if c == '(':
                depth += 1
            elif c == ')':
                depth -= 1
            i += 1
        inner = text[m.end():i - 1]
        text = text[:start] + inner + text[i:]
        count += 1
    return text, count

ref_tot = base_tot = this_tot = nfiles = 0
for sub in ['EvilAliens', 'BloomPostprocess', 'EvilAliens.Constants']:
    for path in glob.glob(os.path.join(SRC, sub, '**', '*.cs'), recursive=True):
        rel = os.path.relpath(path, SRC)
        if rel.replace(os.sep, '/').endswith('EvilAliens/Program.cs'):
            continue  # conflicts with our Blazor Program.cs entry point
        src = open(path, encoding='utf-8').read()
        src, n = fix_ref(src)
        ref_tot += n
        for c in ['((GameComponent)this).', '((DrawableGameComponent)this).', '((Game)this).']:
            base_tot += src.count(c)
            src = src.replace(c, 'base.')
        for c in ['((GameComponent)this)', '((DrawableGameComponent)this)', '((Game)this)']:
            this_tot += src.count(c)
            src = src.replace(c, 'this')
        out = os.path.join(DST, rel)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        open(out, 'w', encoding='utf-8').write(src)
        nfiles += 1

print("files written           :", nfiles)
print("ref-cast artifacts fixed:", ref_tot)
print("((Base)this). -> base.  :", base_tot)
print("((Base)this)  -> this    :", this_tot)
