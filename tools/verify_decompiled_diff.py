"""Show the SEMANTIC diff of a refactor, by decompiling both assemblies and diffing the C#.

The companion to verify_il_identical.py, for the changes that oracle cannot cover.

verify_il_identical.py proves a change is invisible to the compiler. That covers renames, and
(with --optimize) a deleted dead temporary -- but it is all-or-nothing, and some genuinely
behaviour-preserving refactors DO change the emitted code:

  * collapsing `bool num = held; held = num | X;` to `held |= X` reads `held` at a different
    point, so the ldloc moves across the neighbouring struct-property read;
  * collapsing four `powerup.Position - base.Position` recomputations into one local removes
    three get_Position calls -- Roslyn cannot prove a property getter is pure, so it never CSEs
    them away by itself.

For those, the question is not "is the assembly identical" but "is the difference CONFINED to
the methods I edited, and is it the difference I intended". This script answers that.

Why decompiled C# rather than raw IL: deleting a local RENUMBERS every later local slot in that
method, so a raw IL diff of a 360-line method reports hundreds of changed lines and drags in
code you never touched (measured: 317 changed IL lines in PlayerShip.DoAIMove, most of it
wall-navigation code that was untouched -- the diff aligner simply mispaired `ldloc.s 53` with
`ldloc.s 51`). Decompiling first makes slot numbers vanish, because ILSpy re-derives names from
structure. The same change reads as 82 lines of exactly the intended edits.

Read the output as a REVIEW AID, not a proof. It shows what moved; a human decides whether that
was intended. Prefer verify_il_identical.py whenever the change is supposed to be invisible --
a byte-identical hash is strictly stronger than reading a diff.

CAVEAT, and it is a sharp one: ILSpy NORMALISES. Its transform pipeline rewrites recognised
patterns back to idiomatic C#, so a difference the compiler really did emit can decompile to
identical source and silently vanish from this diff. Measured on card 0c624f9d: collapsing
`bool num = held; held = num | X;` to `held |= X` genuinely moves the `ldloc` in the IL, yet
both render as `held |= X` here and the method does not appear in the report at all. Treat an
absent method as "ILSpy considers these the same construct", never as "the IL is identical" --
verify_il_identical.py is the only tool that answers the latter. Corollary: this decompiles the
assembly IN PLACE, in its build output directory, so ILSpy can resolve its references. Copying
the dll somewhere isolated first makes ILSpy fall back to unresolved-type output
(`((GamePadState)(ref state)).Buttons`) with its transforms disabled -- a different, noisier
answer that is easy to mistake for a real finding.

Both sides build optimized, for the same reason --optimize exists over there: unoptimized output
keeps every dead store, which buries the real change in noise.

Needs `ilspycmd` on PATH (see root CLAUDE.md -- run as `DOTNET_ROLL_FORWARD=LatestMajor`; this
script sets that itself).

Usage, from anywhere in the repo:

    python tools/verify_decompiled_diff.py --ref main     # a whole branch vs its branch point
    python tools/verify_decompiled_diff.py                # uncommitted edits vs HEAD
    python tools/verify_decompiled_diff.py --ref main --full   # print the whole diff, not a summary

Exits 0 when the decompiled C# is identical, 1 when it differs (expected for this class of
change -- a 1 is "now go read it", not a failure), 2 on a build or plumbing failure.
"""
import argparse
import difflib
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import verify_il_identical as ili

# ILSpy stamps every method with the source IL offsets it could not type. They shift whenever
# anything above them in the method changes size, so they are pure noise for this comparison.
IL_COMMENT_RE = re.compile(r'^\s*//IL_[0-9a-fA-F]+')
# `} // end of method Class::Name` does not exist in C# output; types/methods are found instead
# by the decompiler's own declaration lines. Attribution has to cover CONSTRUCTORS, PROPERTIES
# and EXPLICIT INTERFACE IMPLEMENTATIONS as well as plain methods: a `modifiers returnType Name(`
# pattern alone matches none of those, so a changed line inside one gets blamed on whichever
# method happened to be declared above it -- which turns the report's headline rule ("a member
# you did not touch appearing here is the finding") into either a false alarm or, worse, a real
# change hiding behind a familiar name.
MODIFIERS = (r'(?:public|private|protected|internal|static|virtual|override|sealed|abstract|'
             r'partial|readonly|unsafe|extern|async|new|const|volatile)')
# At least ONE modifier is REQUIRED, and that is load-bearing, not incidental: it is the only
# thing separating a member declaration from an ordinary statement. Relaxing it to zero-or-more
# makes `else if (...)` parse as a member named `if`, and every hunk downstream gets filed under
# it. ILSpy writes an explicit access modifier on virtually everything, so requiring one costs
# almost no coverage.
MEMBER_RE = re.compile(
    r'^(?P<indent>\s*)(?:' + MODIFIERS + r'\s+)+'
    r'(?:'
    # method / property / expression-bodied member: returnType Name
    r'[\w<>\[\],.?]+\s+(?P<name>[\w<>]+)\s*(?:\(|\{|=>|$)'
    r'|'
    # constructor / finalizer: no return type, name is the enclosing type
    r'~?(?P<ctor>[\w<>]+)\s*\('
    r')')
# Explicit interface implementations carry no access modifier, so they need their own pattern.
# The mandatory dotted qualifier is what keeps this from matching a statement.
EXPLICIT_IMPL_RE = re.compile(
    r'^(?P<indent>\s*)[\w<>\[\],.?]+\s+[\w<>]+(?:\.[\w<>]+)*\.(?P<name>[\w<>]+)\s*(?:\(|\{|=>)')
TYPE_RE = re.compile(r'^(?P<indent>\s*)(?:public|private|protected|internal|sealed|abstract|'
                     r'static|partial|\s)*(?:class|struct|record|interface|enum)\s+([\w<>]+)')
# A closing brace at or above the member's own indent ends it, so the next changed line is not
# still attributed to the member that happened to precede it.
CLOSE_RE = re.compile(r'^(?P<indent>\s*)\}')

EXIT_IDENTICAL = 0
EXIT_DIFFERENT = 1
EXIT_ERROR = 2


def build_dll(tree, label):
    print(f'  building {label} ...', flush=True)
    proc = ili.msbuild(tree, [])
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        ili.die(f'BUILD FAILED for {label} -- fix the build before verifying.')
    dll = os.path.join(tree, *ili.PROJECT.split('/'), *ili.DLL_TAIL)
    if not os.path.isfile(dll):
        ili.die(f'no assembly at {dll}')
    return dll


def decompile(dll, outdir, label):
    print(f'  decompiling {label} ...', flush=True)
    env = dict(os.environ, DOTNET_ROLL_FORWARD='LatestMajor')
    proc = subprocess.run(['ilspycmd', '-o', outdir, dll],
                          capture_output=True, text=True, env=env)
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        ili.die('ilspycmd failed -- is it installed and on PATH?')
    produced = [f for f in os.listdir(outdir) if f.endswith('.cs')]
    if not produced:
        ili.die(f'ilspycmd wrote no .cs into {outdir}')
    path = os.path.join(outdir, produced[0])
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        return [l for l in fh.read().split('\n') if not IL_COMMENT_RE.match(l)]


def enclosing(lines):
    """-> per-line 'Type.Member' attribution, so a hunk can be named."""
    out = []
    cur_type = '<none>'
    cur_member = '<declarations>'
    member_indent = None
    for line in lines:
        t = TYPE_RE.match(line)
        if t:
            cur_type, cur_member, member_indent = t.group(2), '<declarations>', None
            out.append(f'{cur_type}.{cur_member}')
            continue
        if member_indent is not None:
            c = CLOSE_RE.match(line)
            if c and len(c.group('indent')) <= len(member_indent):
                out.append(f'{cur_type}.{cur_member}')
                cur_member, member_indent = '<declarations>', None
                continue
        m = MEMBER_RE.match(line) or EXPLICIT_IMPL_RE.match(line)
        if m:
            cur_member = m.group('name') or m.groupdict().get('ctor')
            member_indent = m.group('indent')
        out.append(f'{cur_type}.{cur_member}')
    return out


SELFTEST_SOURCE = '''\
public class Widget
{
\tprivate int count;

\tpublic int Count => count;

\tpublic string Name
\t{
\t\tget
\t\t{
\t\t\treturn "w";
\t\t}
\t}

\tpublic Widget(int n)
\t{
\t\tcount = n;
\t}

\tpublic void Step()
\t{
\t\tif (count > 0)
\t\t{
\t\t\tcount--;
\t\t}
\t\telse if (count < 0)
\t\t{
\t\t\tcount++;
\t\t}
\t}

\tvoid IDisposable.Dispose()
\t{
\t\tcount = 0;
\t}
}
'''

SELFTEST_EXPECT = {
    'count = n;': 'Widget.Widget',
    'count--;': 'Widget.Step',
    'count++;': 'Widget.Step',
    'count = 0;': 'Widget.Dispose',
    'return "w";': 'Widget.Name',
}


def selftest():
    """Attribution IS this tool's contract, so it gets a test that does not need a build.

    Guards the specific ways it has already been broken: a zero-or-more modifier prefix makes
    `else if (...)` parse as a member named `if`, and constructors / properties / explicit
    interface implementations match no `modifiers returnType Name(` pattern at all.
    """
    lines = SELFTEST_SOURCE.split('\n')
    where = enclosing(lines)
    failures = []
    for needle, expected in SELFTEST_EXPECT.items():
        hits = [w for l, w in zip(lines, where) if l.strip() == needle]
        if not hits:
            failures.append(f'  {needle!r}: never found in the fixture')
        elif hits[0] != expected:
            failures.append(f'  {needle!r}: attributed to {hits[0]}, expected {expected}')
    bogus = sorted({w for w in where if w.split('.')[-1] in ('if', 'else', 'return', 'get')})
    if bogus:
        failures.append(f'  a statement keyword parsed as a member: {bogus}')
    if failures:
        print('SELFTEST FAILED:')
        print('\n'.join(failures))
        return EXIT_DIFFERENT
    print(f'SELFTEST PASSED -- {len(SELFTEST_EXPECT)} member attributions correct, '
          f'no statement parsed as a member.')
    return EXIT_IDENTICAL


def main():
    ap = argparse.ArgumentParser(
        description='Diff the decompiled C# of a refactor against its branch point.')
    ap.add_argument('--selftest', action='store_true',
                    help='check member attribution against a fixture; no build, no git')
    ap.add_argument('--ref', default='HEAD', help='git ref to compare against (default HEAD)')
    ap.add_argument('--full', action='store_true',
                    help='print the entire unified diff, not just the per-method summary')
    ap.add_argument('--context', type=int, default=3, help='diff context lines (default 3)')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    # Same reason as verify_il_identical --optimize: unoptimized output keeps every dead store.
    ili.BUILD_FLAGS.append('-p:Optimize=true')

    root = ili.repo_root()
    # Same exposure as verify_il_identical: this also builds a FRESH checkout against the
    # working tree, so pinned-EOL drift would show up as a phantom BuildRenderTree diff.
    ili.check_pinned_eol(root)
    print(f'Decompiled-C# diff  (repo {root})', flush=True)
    commit = ili.resolve_ref(root, args.ref)
    label = f'{args.ref} ({commit[:8]})'

    head = ili.run(['git', 'rev-parse', 'HEAD'], cwd=root).stdout.strip()
    dirty = bool(ili.run(['git', 'status', '--porcelain'], cwd=root).stdout.strip())
    if commit == head and not dirty:
        ili.die('nothing to compare: the working tree is clean and --ref resolves to HEAD.\n'
                'Pass the branch point instead, e.g. --ref main.')

    scratch = tempfile.mkdtemp(prefix='rotea_csdiff_')
    ref_tree = os.path.join(scratch, 'src')
    try:
        ili.run(['git', 'worktree', 'add', '--detach', ref_tree, commit], cwd=root)
        try:
            ref_cs = decompile(build_dll(ref_tree, f'reference {label}'),
                               os.path.join(scratch, 'ref'), f'reference {label}')
        finally:
            ili.run(['git', 'worktree', 'remove', '--force', ref_tree], cwd=root, check=False)
            ili.run(['git', 'worktree', 'prune'], cwd=root, check=False)

        # The Clean must be in a finally: decompile() can die (ilspycmd missing is the documented
        # prerequisite, so it is the LIKELY path), and this build emitted no PDB. Leaving that
        # assembly behind hands the dev server a binary with no stack traces -- in the one place
        # WASM errors actually surface -- and MSBuild's incremental check will not recompile it,
        # because only a property changed. Same cleanup verify_il_identical does, same reason.
        try:
            work_dll = build_dll(root, 'working tree')
            work_cs = decompile(work_dll, os.path.join(scratch, 'work'), 'working tree')
        finally:
            ili.msbuild(root, ['-t:Clean'])

        if ref_cs == work_cs:
            print('\nIDENTICAL -- the decompiled C# is unchanged.')
            print('(Use verify_il_identical.py for the stronger byte-identical claim.)')
            return EXIT_IDENTICAL

        ref_where, work_where = enclosing(ref_cs), enclosing(work_cs)
        sm = difflib.SequenceMatcher(None, ref_cs, work_cs, autojunk=False)
        touched, removed, added = {}, 0, 0
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag == 'equal':
                continue
            removed += i2 - i1
            added += j2 - j1
            for i in range(i1, i2):
                touched.setdefault(ref_where[i], [0, 0])[0] += 1
            for j in range(j1, j2):
                touched.setdefault(work_where[j], [0, 0])[1] += 1

        print(f'\nDIFFERENT -- {removed} line(s) removed, {added} added, '
              f'across {len(touched)} member(s):')
        for name, (minus, plus) in sorted(touched.items()):
            print(f'   {name}:  -{minus} / +{plus}')
        print('\nEvery member listed above should be one you deliberately edited.')
        print('A member you did not touch appearing here is the finding.')

        if args.full:
            print()
            for line in difflib.unified_diff(ref_cs, work_cs, fromfile=f'ref {label}',
                                             tofile='working tree', lineterm='',
                                             n=args.context):
                print(line)
        else:
            print('\nRe-run with --full to read the diff itself.')
        return EXIT_DIFFERENT
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


if __name__ == '__main__':
    sys.exit(main())
