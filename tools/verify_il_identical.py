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

Deliberately NOT codegen: unlike its fix_*.py / fix_ctors.py neighbours (which regenerated
Game/ from src_decompiled/ and must never be re-run), this only ever builds and hashes. It is
safe to run any number of times, and it never writes inside the repo -- the reference worktree
and the hash cache both live in the system temp dir.

The reference build happens in a throwaway git worktree at --ref, which is sound because the
deterministic hash is path-independent (verified: the same commit built under .claude/worktrees/wt7
and under a temp dir produced the same SHA-256). So you can baseline at any time, including
after you have already started editing.

Usage, from anywhere in the repo:

    python tools/verify_il_identical.py                # working tree vs HEAD
    python tools/verify_il_identical.py --ref main     # working tree vs main
    python tools/verify_il_identical.py --no-cache     # rebuild the reference too

Exits 0 when identical (refactor proven behaviour-preserving), 1 when it differs, 2 on a
build or plumbing failure.
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
DLL = 'web/EvilAliensWeb/bin/Debug/net8.0/EvilAliensWeb.dll'
# -t:Rebuild is load-bearing, not belt-and-braces: MSBuild's incremental check does NOT
# re-run the compiler when only a PROPERTY changes, so a plain `dotnet build` run beforehand
# (say, to count warnings) leaves a PDB-bearing assembly that this script would otherwise hash
# as-is. That first showed up as a false DIFFERENT; the dangerous direction is the opposite --
# a stale DLL that happens to match would report IDENTICAL and prove nothing. Forcing a clean
# compile makes the verdict independent of whatever was built here last.
BUILD_FLAGS = ['-c', 'Debug', '-p:DebugType=none', '-p:Deterministic=true', '-t:Rebuild']


def run(args, cwd=None, check=True):
    proc = subprocess.run(args, cwd=cwd, capture_output=True, text=True)
    if check and proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        sys.exit(2)
    return proc


def repo_root():
    return run(['git', 'rev-parse', '--show-toplevel']).stdout.strip()


def build_and_hash(tree, label):
    """Build `tree` with the no-PDB deterministic flags and return the assembly's SHA-256."""
    print(f'  building {label} ...', flush=True)
    proc = subprocess.run(
        ['dotnet', 'build', os.path.join(tree, *PROJECT.split('/')), *BUILD_FLAGS,
         '-v', 'q', '--nologo'],
        capture_output=True, text=True)
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        sys.exit(f'BUILD FAILED for {label} -- fix the build before verifying.')
    dll = os.path.join(tree, *DLL.split('/'))
    if not os.path.isfile(dll):
        sys.exit(f'no assembly at {dll}')
    with open(dll, 'rb') as fh:
        return hashlib.sha256(fh.read()).hexdigest()


def cache_path(root, commit):
    key = hashlib.sha256((root + '|' + commit).encode()).hexdigest()[:16]
    return os.path.join(tempfile.gettempdir(), f'rotea_il_baseline_{key}.json')


def reference_hash(root, ref, use_cache):
    commit = run(['git', 'rev-parse', ref], cwd=root).stdout.strip()
    cache = cache_path(root, commit)
    if use_cache and os.path.isfile(cache):
        with open(cache) as fh:
            print(f'  reference {ref} ({commit[:8]}) from cache')
            return json.load(fh)['sha256']

    tree = tempfile.mkdtemp(prefix='rotea_ilbase_')
    worktree = os.path.join(tree, 'src')
    run(['git', 'worktree', 'add', '--detach', worktree, commit], cwd=root)
    try:
        digest = build_and_hash(worktree, f'reference {ref} ({commit[:8]})')
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
    args = ap.parse_args()

    root = repo_root()
    print(f'IL-identity check  (repo {root})')
    reference = reference_hash(root, args.ref, use_cache=not args.no_cache)
    current = build_and_hash(root, 'working tree')

    print()
    print(f'  reference ({args.ref}) : {reference}')
    print(f'  working tree      : {current}')
    print()
    if reference == current:
        print('IDENTICAL -- the change is provably behaviour-preserving.')
        print('(Name QUALITY is not checked; only that nothing semantic moved.)')
        return 0
    print('DIFFERENT -- the working tree does NOT compile to the same assembly.')
    print('Something semantic changed. Usual causes, in order of likelihood:')
    print('  * a rename collided with an existing identifier in the same scope, so a read')
    print('    now resolves to a different variable/field/parameter;')
    print('  * a substitution escaped its method and hit a field or another method;')
    print('  * a real edit (constant, operator, control flow) rode along with the rename.')
    print('Bisect by reverting files until it goes identical again.')
    return 1


if __name__ == '__main__':
    sys.exit(main())
