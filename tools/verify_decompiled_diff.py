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
# by the decompiler's own declaration lines.
MEMBER_RE = re.compile(
    r'^\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed|abstract|'
    r'partial|readonly|unsafe|extern|async|new)\s+)+[\w<>\[\],.?]+\s+([\w<>]+)\s*\(')
TYPE_RE = re.compile(r'^\s*(?:public|private|protected|internal|sealed|abstract|static|partial|'
                     r'\s)*(?:class|struct|record|interface|enum)\s+([\w<>]+)')

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
    for line in lines:
        t = TYPE_RE.match(line)
        if t:
            cur_type, cur_member = t.group(1), '<declarations>'
        else:
            m = MEMBER_RE.match(line)
            if m:
                cur_member = m.group(1)
        out.append(f'{cur_type}.{cur_member}')
    return out


def main():
    ap = argparse.ArgumentParser(
        description='Diff the decompiled C# of a refactor against its branch point.')
    ap.add_argument('--ref', default='HEAD', help='git ref to compare against (default HEAD)')
    ap.add_argument('--full', action='store_true',
                    help='print the entire unified diff, not just the per-method summary')
    ap.add_argument('--context', type=int, default=3, help='diff context lines (default 3)')
    args = ap.parse_args()

    # Same reason as verify_il_identical --optimize: unoptimized output keeps every dead store.
    ili.BUILD_FLAGS.append('-p:Optimize=true')

    root = ili.repo_root()
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

        work_dll = build_dll(root, 'working tree')
        work_cs = decompile(work_dll, os.path.join(scratch, 'work'), 'working tree')
        # This build emitted no PDB; leaving it in place would hand the dev server a binary with
        # no stack traces. Same cleanup verify_il_identical does, and same reason.
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
