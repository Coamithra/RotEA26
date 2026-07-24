"""Prove a refactor changed NO behaviour, by compiling it to a byte-identical assembly.

A local-variable rename (card d26f0681) is invisible to the compiler's output: local names
live only in the PDB, and the recovered game was a Release build with no PDB in the first
place. So build with `-p:DebugType=none` and a CORRECT rename must produce a bit-for-bit
identical EvilAliensWeb.dll. That is a total oracle -- strictly stronger than any screenshot,
and the reason this class of card needs no harness/sim despite touching Game/.

Measured when this was written (see plans/rename-decompiled-locals.md):
  * 19 locals renamed across CollisionHandler.FillCollisionMatrixLine -> identical hash
  * negative control, maxLineSteps 128 -> 129 (one constant)      -> different hash
so it is both sound and sensitive to a single-token slip. It hashes the WHOLE assembly, so a
stray edit in some other file is caught too.

What it does NOT check: whether a new name is a GOOD name. A misleading-but-compiling rename
hashes identically. Name quality stays a human judgement -- this only removes the risk that a
rename silently broke the game.

--optimize is for a refactor that DELETES a local rather than renaming it. The default build has
no Optimize property, so Roslyn keeps every local for the debugger and a deleted one changes the
IL -- the oracle would report DIFFERENT for a change that is provably behaviour-preserving.
Optimizing folds the dead temporary away, which is what makes the two shapes converge WHEN THEY
STAND ALONE. Measured on a scratch assembly (card 0c624f9d, ILSpy's `held |= X` artifact):

                                        default        --optimize
  bool num = held; held = num | a;      3378ea0f...    0d3f9784...
  held |= a;                            d5fb8cde...    0d3f9784...

It is opt-in because it is a strictly WEAKER oracle: it also folds away dead stores and other
differences a rename could never introduce, so use the default for a pure rename and reach for
this only when the change removes a local. Still sensitive to real edits -- its own negative
control (a 0.58f threshold nudged to 0.59f, on an otherwise-IDENTICAL tree) reports DIFFERENT
under --optimize.

TWO classes it does NOT cover, both hit by the very card that added it -- do not read the table
above as a promise that any `|=` collapse hashes equal:

  * an intervening evaluation between the temp and its use. The table's shapes are adjacent; the
    REAL InputHandler.UpdateKeyPads reads `held` into the temp BEFORE the neighbouring
    `GamePadButtons b = state.Buttons;` line, whereas `held |= X` reads it AFTER. The optimizer
    will not reorder a local read across a property call it cannot prove pure, so the `ldloc`
    moves and the hash differs -- measured, and benign (a method-local `bool` that
    GamePadState.get_Buttons() cannot observe).
  * a change to how many times a PROPERTY is read (e.g. collapsing four `x.Position - y.Position`
    recomputations into one local): Roslyn cannot prove a getter is pure, so it never CSEs the
    calls away.

For both, the question stops being "is it identical" and becomes "is the difference confined to
the methods I edited" -- that is tools/verify_decompiled_diff.py, not this script.

Deliberately NOT codegen: unlike its fix_*.py / fix_ctors.py neighbours (which regenerated
Game/ from src_decompiled/ and must never be re-run), this only ever builds and hashes. It is
safe to run any number of times, and it never writes inside the repo -- the reference worktree
and the hash cache both live in the system temp dir.

The reference build happens in a throwaway git worktree, which is sound because the
deterministic hash is path-independent (verified: the same commit built under .claude/worktrees/wt7
and under a temp dir produced the same SHA-256). So you can baseline at any time, including
after you have already started editing. --ref resolves to the MERGE-BASE with HEAD, not the
ref's tip, so a shared branch moving under you cannot drag someone else's work into the diff.

Usage, from anywhere in the repo:

    python tools/verify_il_identical.py                # uncommitted edits vs HEAD
    python tools/verify_il_identical.py --ref main     # a whole branch vs its branch point
    python tools/verify_il_identical.py --no-cache     # rebuild the reference too
    python tools/verify_il_identical.py --optimize     # for a refactor that DELETES a local

Use the first form while editing and the second once the work is committed -- after a commit,
"vs HEAD" would compare a commit against itself, so the script refuses that instead of
handing back a meaningless green tick.

Exits 0 when identical (refactor proven behaviour-preserving), 1 when it differs, 2 on a
build or plumbing failure. Note the asymmetry: a 1 is a definite finding, while IDENTICAL is
only as good as the assumption that nothing but names moved -- it says nothing about whether
the new names are any good.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile

PROJECT = 'web/EvilAliensWeb'
DLL_TAIL = ('bin', 'Debug', 'net8.0', 'EvilAliensWeb.dll')
BUILD_FLAGS = [
    '-c', 'Debug',
    # No PDB: local variable names live only there, which is the whole premise.
    '-p:DebugType=none',
    '-p:Deterministic=true',
    # WITHOUT this the SDK stamps the git commit into AssemblyInformationalVersion
    # ("1.0.0+<sha>"), so two DIFFERENT commits can never hash equal no matter how cosmetic
    # the change -- which silently broke the most useful mode, comparing a branch to its
    # merge-base, into a permanent false DIFFERENT.
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    # -t:Rebuild is load-bearing, not belt-and-braces: MSBuild's incremental check does NOT
    # re-run the compiler when only a PROPERTY changes, so a build run beforehand with other
    # flags leaves an assembly this script would otherwise hash as-is. The dangerous direction
    # is a stale DLL that happens to match, reporting IDENTICAL while proving nothing.
    '-t:Rebuild',
]

EXIT_IDENTICAL = 0
EXIT_DIFFERENT = 1
EXIT_ERROR = 2


def die(message):
    sys.stderr.write(message + '\n')
    sys.exit(EXIT_ERROR)


def run(args, cwd=None, check=True):
    proc = subprocess.run(args, cwd=cwd, capture_output=True, text=True)
    if check and proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        die(f'command failed: {" ".join(args)}')
    return proc


def repo_root():
    return run(['git', 'rev-parse', '--show-toplevel']).stdout.strip()


def sdk_version():
    return run(['dotnet', '--version']).stdout.strip()


def msbuild(tree, extra):
    return subprocess.run(
        ['dotnet', 'build', os.path.join(tree, *PROJECT.split('/')), *BUILD_FLAGS, *extra,
         '-v', 'q', '--nologo'],
        capture_output=True, text=True)


def build_and_hash(tree, label, clean_after=False):
    """Build `tree` with the no-PDB deterministic flags and return the assembly's SHA-256.

    `clean_after` matters for the working tree (the reference builds in a throwaway worktree,
    so it cannot contaminate anything). This build deliberately emits NO PDB, and by the
    MSBuild quirk noted above an ordinary `dotnet build` afterwards would consider the sources
    unchanged and leave that assembly in place -- handing the dev server a binary with no
    stack traces, in the one place WASM errors actually surface (the browser console). Cleaning
    up forces the developer's next build to compile properly.

    Redirecting BaseIntermediateOutputPath instead looks tidier but does not work: it drops
    the project's real obj/ out of DefaultItemExcludes, so the old generated AssemblyInfo.cs
    gets compiled alongside the new one and every assembly-level attribute collides (CS0579).
    """
    print(f'  building {label} ...', flush=True)
    proc = msbuild(tree, [])
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        die(f'BUILD FAILED for {label} -- fix the build before verifying.')
    dll = os.path.join(tree, *PROJECT.split('/'), *DLL_TAIL)
    if not os.path.isfile(dll):
        die(f'no assembly at {dll}')
    with open(dll, 'rb') as fh:
        digest = hashlib.sha256(fh.read()).hexdigest()
    if clean_after:
        msbuild(tree, ['-t:Clean'])
    return digest


def cache_path(root, commit, toolchain):
    # The key covers the toolchain and the build flags, not just the commit: KNI floats every
    # package version (4.1.9001.*) and an SDK bump or a BUILD_FLAGS edit changes the bytes, so
    # a commit-only key would keep serving a confidently wrong baseline.
    material = '|'.join([root, commit, toolchain, ' '.join(BUILD_FLAGS)])
    key = hashlib.sha256(material.encode()).hexdigest()[:16]
    return os.path.join(tempfile.gettempdir(), f'rotea_il_baseline_{key}.json')


def resolve_ref(root, ref):
    """Resolve `ref` to the MERGE-BASE with HEAD, not to its tip.

    Comparing against the tip of a shared branch is a trap on this repo: several worktrees
    merge into main concurrently, so `--ref main` would silently drag in other people's work
    and report a difference that has nothing to do with your edits. The merge-base is the
    commit your change actually departed from.
    """
    base = run(['git', 'merge-base', ref, 'HEAD'], cwd=root).stdout.strip()
    tip = run(['git', 'rev-parse', ref], cwd=root).stdout.strip()
    if base != tip:
        print(f'  note: {ref} ({tip[:8]}) is not an ancestor of HEAD; '
              f'comparing against merge-base {base[:8]}')
    return base


def reference_hash(root, commit, label, toolchain, use_cache):
    cache = cache_path(root, commit, toolchain)
    if use_cache and os.path.isfile(cache):
        with open(cache) as fh:
            print(f'  reference {label} from cache')
            return json.load(fh)['sha256']

    tree = tempfile.mkdtemp(prefix='rotea_ilbase_')
    worktree = os.path.join(tree, 'src')
    run(['git', 'worktree', 'add', '--detach', worktree, commit], cwd=root)
    try:
        digest = build_and_hash(worktree, f'reference {label}')
    finally:
        # Windows holds directory locks (Defender scanning a fresh bin/) -- --force still
        # unregisters the worktree from git even when the folder itself survives, so a
        # leftover temp dir is harmless cruft rather than a corrupted repo.
        run(['git', 'worktree', 'remove', '--force', worktree], cwd=root, check=False)
        run(['git', 'worktree', 'prune'], cwd=root, check=False)
        shutil.rmtree(tree, ignore_errors=True)

    with open(cache, 'w') as fh:
        json.dump({'commit': commit, 'sha256': digest}, fh)
    return digest


def main():
    ap = argparse.ArgumentParser(
        description='Prove a refactor compiles to a byte-identical assembly.')
    ap.add_argument('--ref', default='HEAD', help='git ref to compare against (default HEAD)')
    ap.add_argument('--no-cache', action='store_true', help='rebuild the reference too')
    ap.add_argument('--optimize', action='store_true',
                    help='build optimized, so a DELETED local still hashes equal (weaker oracle)')
    args = ap.parse_args()

    # Both sides of the comparison must build the same way, and the cache key already hashes
    # BUILD_FLAGS -- so appending here is enough to keep an optimized baseline from ever being
    # served to a default run, or vice versa.
    if args.optimize:
        BUILD_FLAGS.append('-p:Optimize=true')

    root = repo_root()
    print(f'IL-identity check  (repo {root})'
          f'{"  [optimized -- weaker oracle, see module docstring]" if args.optimize else ""}',
          flush=True)
    commit = resolve_ref(root, args.ref)
    label = f'{args.ref} ({commit[:8]})'

    # Working tree == the reference commit with nothing uncommitted is a vacuous check: it
    # would print IDENTICAL while proving nothing at all. Say so rather than hand back a
    # green tick. After committing, compare against the merge-base instead (--ref main).
    head = run(['git', 'rev-parse', 'HEAD'], cwd=root).stdout.strip()
    dirty = bool(run(['git', 'status', '--porcelain'], cwd=root).stdout.strip())
    if commit == head and not dirty:
        die('nothing to compare: the working tree is clean and --ref resolves to HEAD.\n'
            'Pass the branch point instead, e.g. --ref main.')

    toolchain = sdk_version()
    reference = reference_hash(root, commit, label, toolchain,
                               use_cache=not args.no_cache)
    current = build_and_hash(root, 'working tree', clean_after=True)

    print()
    print(f'  reference {label} : {reference}')
    print(f'  working tree{" " * len(label)} : {current}')
    print()
    if reference == current:
        print('IDENTICAL -- the change is provably behaviour-preserving.')
        print('(Name QUALITY is not checked; only that nothing semantic moved.)')
        if args.optimize:
            print('(Optimized: dead stores and unused locals were folded away before hashing,')
            print(' so this says nothing about a change that only moved one of those.)')
        return EXIT_IDENTICAL
    print('DIFFERENT -- the working tree does NOT compile to the same assembly.')
    print('Something semantic changed. Usual causes, in order of likelihood:')
    print('  * a rename collided with an existing identifier in the same scope, so a read')
    print('    now resolves to a different variable/field/parameter;')
    print('  * a substitution escaped its method and hit a field or another method;')
    print('  * a real edit (constant, operator, control flow) rode along with the rename.')
    if not args.optimize:
        print('  * the change DELETED a local -- this build keeps every local for the')
        print('    debugger, so try --optimize (see the module docstring).')
    else:
        print('  * the change is one --optimize still cannot hide: a temp with an intervening')
        print('    evaluation, or a changed number of property reads. Both are expected to')
        print('    differ -- bound them with tools/verify_decompiled_diff.py instead.')
    print('Bisect by reverting files until it goes identical again.')
    return EXIT_DIFFERENT


if __name__ == '__main__':
    sys.exit(main())
