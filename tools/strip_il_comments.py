"""Strip ILSpy's '//IL_<hex>: ...' warning comments out of web/EvilAliensWeb/Game/.

The decompile ran without the XNA 3.1 reference assemblies, so ILSpy tagged nearly every
Vector2/Color/Rectangle expression with a per-IL-offset "type came back Unknown" comment.
4020 of them rode into Game/ and say nothing about the port.

Only lines that are ENTIRELY such a comment are dropped; three lines have one glued onto real
code ("}//IL_0002: ...") and keep the code. Runs of blank lines left behind are collapsed to
one. src_decompiled/ is deliberately untouched -- it stays verbatim as the reference copy.

Applied once (2026-07-24). Preserves CRLF and UTF-8-without-BOM. Run from the repo root:
    python tools/strip_il_comments.py [--dry-run]
"""
import glob
import re
import sys

ROOT = 'web/EvilAliensWeb/Game'
ONLY = re.compile(r'^[ \t]*//IL_[0-9a-fA-F]+:.*$')
TRAILING = re.compile(r'//IL_[0-9a-fA-F]+:.*$')

dry = '--dry-run' in sys.argv
dropped = trimmed = collapsed = files = 0

for path in sorted(glob.glob(f'{ROOT}/**/*.cs', recursive=True)):
    with open(path, encoding='utf-8', newline='') as fh:
        src = fh.read()
    if '//IL_' not in src:
        continue

    out = []
    for line in src.split('\n'):
        body, eol = (line[:-1], '\r') if line.endswith('\r') else (line, '')
        if ONLY.match(body):
            dropped += 1
            continue
        if '//IL_' in body:
            body = TRAILING.sub('', body).rstrip()
            trimmed += 1
        out.append(body + eol)

    # Collapse blank runs left behind by the trimmed lines.
    squashed = []
    for line in out:
        if not line.strip() and squashed and not squashed[-1].strip():
            collapsed += 1
            continue
        squashed.append(line)

    new = '\n'.join(squashed)
    if new != src:
        files += 1
        if not dry:
            with open(path, 'w', encoding='utf-8', newline='') as fh:
                fh.write(new)

print(f'{"would touch" if dry else "touched"} {files} files: '
      f'{dropped} comment lines dropped, {trimmed} trailing comments trimmed, '
      f'{collapsed} blank lines collapsed')
