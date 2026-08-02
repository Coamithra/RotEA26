#!/usr/bin/env python
"""Run the committed eahl regression probes.

    python tools/headless/probes/run_probes.py            # all probes
    python tools/headless/probes/run_probes.py --list
    python tools/headless/probes/run_probes.py --only preload_*
    python tools/headless/probes/run_probes.py --build    # dotnet build eahl first

Exit 0 = every probe passed, 1 = at least one failed, 2 = the runner itself could not run
(eahl missing, THE BINARY IS STALE, no probes matched, ...).

A STALE eahl.exe is the failure this runner exists to make impossible (card 74998f22): the
probes link the game sources into eahl, so a probe run after a source edit -- or after a
`dotnet build` that FAILED -- silently exercises the PREVIOUS binary and reports a green suite
for code that does not even compile. That happened (card 4a3b22b7) and was caught only by
grepping the build output separately. So before the first probe the runner compares eahl's
build time against the newest source it is built from and REFUSES to run (exit 2, distinct from
a probe failing) when the sources are newer. `--build` builds first and cures it; `--allow-stale`
prints the same block as a warning and runs anyway.

Each probe is a plain `eahl --script` file, so any single one can also be run by hand:

    tools/headless/bin/Debug/net8.0/eahl.exe --script tools/headless/probes/silence.txt \
        --flags "?level=Level1&invuln"

The runner exists to supply those per-probe flags (from the `# eahl:` directive in the file)
and to run the set. Every probe gets its OWN PROCESS: a fresh boot, wiped saves, and no state
inherited from the probe before it -- which is the same reason eaAiBench.matrix reloads the
page per run.
"""
import argparse
import fnmatch
import os
import re
import shlex
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
EAHL_DIR = os.path.join(REPO, "tools", "headless", "bin", "Debug", "net8.0")
EAHL = os.path.join(EAHL_DIR, "eahl.exe" if os.name == "nt" else "eahl")

# What eahl is BUILT FROM -- the csproj's own `Compile Include` set (the two linked game trees)
# plus the host's own sources and the project file. Content under wwwroot is deliberately absent:
# HeadlessTitleContainer reads it live off disk, so a regenerated .dds/.mgfxo/manifest needs no
# rebuild and must not read as stale. Probe .txt files are read live too.
SOURCE_TREES = [
    os.path.join(REPO, "web", "EvilAliensWeb", "Game"),
    os.path.join(REPO, "web", "EvilAliensWeb", "Compat"),
    os.path.join(REPO, "tools", "headless"),
]
SOURCE_SUFFIXES = (".cs", ".csproj")

# Build outputs whose mtimes date the binary. Both are checked because MSBuild writes the
# compiler's eahl.dll and copies the eahl.exe apphost on separate steps; the NEWEST of the two
# is the honest "when was this built".
BINARY_PARTS = ["eahl.dll", os.path.basename(EAHL)]

# `# eahl: --flags "?menu&loadlog"` -- extra argv for this probe. eahl itself treats the line
# as an ordinary comment, so a probe file stays runnable by hand with no runner involved.
DIRECTIVE = re.compile(r"^#\s*eahl:\s*(.+?)\s*$")

# `# PROBE: <text>` -- the probe's one-line summary, shown by --list and on a failure.
SUMMARY = re.compile(r"^#\s*PROBE:\s*(.+?)\s*$")


class MissingSourceTree(Exception):
    """A tree in SOURCE_TREES is not there -- see newest_source."""


def newest_source(trees=None):
    """(mtime, path) of the newest file eahl is compiled from, or (0.0, None) if there are none.

    Skips bin/obj -- they hold the OUTPUT (and its intermediate copies), which is always newer
    than the sources it was built from, so counting them would make every tree look stale.

    A tree that is not THERE raises, and that is the point: os.walk on a missing path yields
    nothing without error, so a renamed or moved source tree would leave this scanning fewer
    files (or none), answer "fresh" forever, and silently restore the exact hole this check
    exists to close -- in the safe-looking direction, which is the one nobody notices.
    """
    newest, where = 0.0, None
    for tree in (SOURCE_TREES if trees is None else trees):
        if not os.path.isdir(tree):
            raise MissingSourceTree(rel(tree))
        for root, dirs, files in os.walk(tree):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
            for f in files:
                if not f.endswith(SOURCE_SUFFIXES):
                    continue
                path = os.path.join(root, f)
                try:
                    mtime = os.path.getmtime(path)
                except OSError:
                    continue          # raced away mid-walk; it cannot date the binary either
                if mtime > newest:
                    newest, where = mtime, path
    return newest, where


def binary_built(directory=None):
    """(mtime, path) of the newest eahl build output, or (0.0, None) if it is not built."""
    newest, where = 0.0, None
    for name in BINARY_PARTS:
        path = os.path.join(EAHL_DIR if directory is None else directory, name)
        try:
            mtime = os.path.getmtime(path)
        except OSError:
            continue
        if mtime > newest:
            newest, where = mtime, path
    return newest, where


def when(mtime):
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(mtime))


def staleness(directory=None, trees=None):
    """None when the binary is at least as new as every source, else the report block.

    Sound in the direction that matters: MSBuild's own up-to-date check is timestamp-based, so a
    successful build always rewrites the outputs and leaves them newer than every source (that is
    measured, not assumed -- touch a Game/*.cs, rebuild, and both eahl.dll and eahl.exe are
    rewritten). A build that FAILED leaves them older than the source that broke it, which is the
    case this exists to catch.
    """
    built, binary = binary_built(directory)
    src, path = newest_source(trees)
    if binary is None or path is None or built >= src:
        return None
    return ("  eahl built     %s   %s\n"
            "  newest source  %s   %s\n"
            % (when(built), rel(binary), when(src), rel(path)))


def rel(path):
    """Repo-relative and forward-slashed when possible -- absolute when it is not.

    os.path.relpath RAISES across Windows drives, which --selftest's temp tree can sit on.
    """
    try:
        return os.path.relpath(path, REPO).replace("\\", "/")
    except ValueError:
        return path.replace("\\", "/")


def probe_files(pattern):
    names = sorted(f for f in os.listdir(HERE) if f.endswith(".txt"))
    if pattern:
        names = [f for f in names if fnmatch.fnmatch(f, pattern)
                 or fnmatch.fnmatch(os.path.splitext(f)[0], pattern)]
    return [os.path.join(HERE, f) for f in names]


def read_meta(path):
    """(extra argv, one-line summary) from the probe's header comments."""
    argv, summary = [], ""
    with open(path, "r", encoding="utf-8") as fh:
        for line in fh:
            # lstrip first, matching eahl's own comment handling (it Trim()s before
            # testing for '#'), or an indented comment ends the header scan here while
            # staying a comment there -- and the probe silently runs with no flags.
            line = line.lstrip()
            if not line.startswith("#"):
                if line.strip():
                    break          # header is over once real commands start
                continue
            m = DIRECTIVE.match(line)
            if m:
                argv = shlex.split(m.group(1))
            m = SUMMARY.match(line)
            if m and not summary:
                summary = m.group(1)
    return argv, summary


# Generous: the slowest committed probe (preload_insanebossi) plays a level out over 720
# simulated seconds and takes ~3.5s. This is not a performance budget, it is a deadlock
# guard -- a wedged eahl (a scene
# that never advances, a modal pause) would otherwise hang the runner forever with
# capture_output swallowing every clue, which is the worst outcome for something an agent
# leaves running unattended.
TIMEOUT_S = 300


def run(path, extra_argv, verbose):
    cmd = [EAHL, "--script", path] + extra_argv
    # cwd=REPO, not the caller's: a probe calling `eval PreloadExport` drops its
    # preload_manifest.txt next to --out, or in the cwd when a --script run has none. Pinning
    # it keeps that litter in the one place .gitignore covers, wherever the runner was invoked.
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, errors="replace",
                              cwd=REPO, timeout=TIMEOUT_S)
    except subprocess.TimeoutExpired as ex:
        out = (ex.stdout or "") + (ex.stderr or "")
        if isinstance(out, bytes):
            out = out.decode("utf-8", "replace")
        return "timeout", out + "\nerr eahl did not exit within %ds\n" % TIMEOUT_S, cmd
    out = (proc.stdout or "") + (proc.stderr or "")
    if verbose:
        sys.stdout.write(out)
    return proc.returncode, out, cmd


def failure_tail(out, limit=12):
    """The `err ...` line eahl stopped on, plus a little context around it."""
    lines = out.splitlines()
    for i, ln in enumerate(lines):
        if ln.startswith("err "):
            return lines[max(0, i - 3):i + 2]
    return lines[-limit:]


def selftest():
    """Drive the stale-binary rule over a synthetic tree -- no dotnet, no eahl, no probes.

    The rule is the one thing here that can fail SILENTLY in the direction that matters: a bug
    that makes staleness() always answer "fresh" restores exactly the hole card 74998f22 is
    about, and every suite still reports PASS. So it is tested rather than trusted.

    Mutation-tested: dropping the bin/obj skip from newest_source turns the two skip rows FAIL;
    flipping `built >= src` to `>` turns the equal-mtimes row; taking BINARY_PARTS[0] instead of
    the newest turns the apphost-only row; dropping the isdir guard turns the missing-tree row;
    and returning None unconditionally turns every STALE row.
    """
    import shutil
    import tempfile

    root = tempfile.mkdtemp(prefix="run_probes_selftest_")
    src = os.path.join(root, "src")
    out = os.path.join(root, "out")
    os.makedirs(os.path.join(src, "obj"))
    os.makedirs(os.path.join(src, "bin"))
    os.makedirs(out)

    def write(path, mtime):
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("x\n")
        os.utime(path, (mtime, mtime))
        return path

    T = 1_700_000_000.0          # a fixed epoch, so a slow selftest cannot drift into the rule
    old, mid, new = T, T + 100, T + 200

    def verdict(**kw):
        return staleness(directory=out, trees=[src], **kw)

    failures = []

    def check(label, ok, detail=""):
        print("%-4s %s%s" % ("PASS" if ok else "FAIL", label, "" if ok else "  -- " + detail))
        if not ok:
            failures.append(label)

    # 1. binary newer than every source -- the normal, freshly-built case. The fixture is built
    #    from BINARY_PARTS rather than literal names so this still tests what the runner reads
    #    on a posix box (where the apphost is `eahl`, not `eahl.exe`) or after a rename.
    write(os.path.join(src, "Game.cs"), old)
    for name in BINARY_PARTS:
        write(os.path.join(out, name), new)
    check("fresh binary is not stale", verdict() is None, repr(verdict()))

    # 2. a source edited after the build -- the whole point.
    edited = write(os.path.join(src, "Edited.cs"), new + 50)
    report = verdict()
    check("source newer than binary is STALE", report is not None, "no report")
    check("the report NAMES the offending source",
          report is not None and os.path.basename(edited) in report, repr(report))
    check("the report names the binary it compared against",
          report is not None and "eahl" in report, repr(report))

    # 3. it must name the NEWEST source, not merely a newer one -- otherwise the timestamps
    #    printed do not bound the staleness they claim to.
    newest = write(os.path.join(src, "Newest.cs"), new + 500)
    report = verdict()
    check("the report names the NEWEST source",
          report is not None and os.path.basename(newest) in report, repr(report))
    os.remove(edited)
    os.remove(newest)

    # 4. bin/ and obj/ hold build OUTPUT, which is always newer than the code it came from.
    #    Counting either would make every tree permanently stale.
    write(os.path.join(src, "obj", "Game.cs"), new + 900)
    check("obj/ is skipped", verdict() is None, repr(verdict()))
    write(os.path.join(src, "bin", "Game.cs"), new + 900)
    check("bin/ is skipped", verdict() is None, repr(verdict()))

    # 5. non-source files are read live by eahl (content, probe scripts) -- they date nothing.
    write(os.path.join(src, "manifest.txt"), new + 900)
    check("a non-source file does not make it stale", verdict() is None, repr(verdict()))

    # 6. equal mtimes: a build whose output is stamped the same second as the source it compiled
    #    is fresh, not stale. Strictly-greater here would flag ordinary builds.
    write(os.path.join(src, "Game.cs"), new)
    check("equal mtimes are not stale", verdict() is None, repr(verdict()))

    # 7. the two build outputs are written by separate MSBuild steps, so the NEWEST of them is
    #    the build time. Reading only the first would call a real build stale.
    first, second = [os.path.join(out, name) for name in BINARY_PARTS]
    write(os.path.join(src, "Game.cs"), mid)
    os.utime(first, (old, old))
    os.utime(second, (new, new))
    check("the newer of dll/exe dates the build", verdict() is None, repr(verdict()))
    os.utime(first, (new, new))
    os.utime(second, (old, old))
    check("...either way round", verdict() is None, repr(verdict()))

    # 8. a source tree that is not THERE must be loud. os.walk yields nothing for a missing path,
    #    so a renamed tree would otherwise scan less (or nothing), read "fresh" forever, and put
    #    the stale-binary hole back with every suite still printing green.
    missing = False
    try:
        newest_source(trees=[os.path.join(root, "gone")])
    except MissingSourceTree:
        missing = True
    check("a missing source tree raises", missing, "no MissingSourceTree")

    # 9. no binary at all is NOT this rule's business -- main() reports "eahl not built" first,
    #    and a stale report there would bury it under a confusing timestamp block.
    for name in BINARY_PARTS:
        os.remove(os.path.join(out, name))
    write(os.path.join(src, "Game.cs"), new)
    check("an unbuilt binary yields no stale report", verdict() is None, repr(verdict()))

    shutil.rmtree(root, ignore_errors=True)
    print()
    if failures:
        print("SELFTEST FAILED: %s" % ", ".join(failures))
        return 1
    print("selftest ok")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", metavar="GLOB", help="run only probes whose name matches")
    ap.add_argument("--list", action="store_true", help="list probes and exit")
    ap.add_argument("--build", action="store_true", help="dotnet build eahl first")
    ap.add_argument("--allow-stale", action="store_true",
                    help="run even when eahl is older than the sources (warns loudly)")
    ap.add_argument("--verbose", action="store_true", help="stream each probe's output")
    ap.add_argument("--selftest", action="store_true",
                    help="test the stale-binary rule itself and exit (no dotnet, no probes)")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    paths = probe_files(args.only)
    if not paths:
        # A typo'd glob silently running zero probes and reporting success is the same trap
        # build_textures.py's --only hit (card 06c6c741). Fail instead.
        print("no probes matched %r in %s" % (args.only, HERE), file=sys.stderr)
        return 2

    if args.list:
        for p in paths:
            _, summary = read_meta(p)
            print("%-24s %s" % (os.path.basename(p), summary))
        return 0

    if args.build:
        rc = subprocess.call(["dotnet", "build", os.path.join(REPO, "tools", "headless"),
                              "-c", "Debug", "-v", "q", "--nologo"])
        if rc != 0:
            print("build failed", file=sys.stderr)
            return 2

    if not os.path.exists(EAHL):
        print("eahl not built: %s\nrun `dotnet build tools/headless -c Debug` (or pass --build)"
              % EAHL, file=sys.stderr)
        return 2

    try:
        stale = staleness()
    except MissingSourceTree as ex:
        print("run_probes: a source tree eahl is built from is missing: %s\n"
              "SOURCE_TREES is out of date -- fix it rather than running, or the stale-binary\n"
              "check silently passes everything from here on." % ex, file=sys.stderr)
        return 2
    if stale:
        # `dotnet build tools/headless`, NOT web/EvilAliensWeb: eahl source-links those trees into
        # its own exe, so building the WASM project alone leaves this binary on the old code.
        head = ("eahl is OLDER than the sources it is built from -- these probes would test the\n"
                "PREVIOUS build, so a failing `dotnet build` reads as a green suite.\n")
        fix = ("Rebuild and re-run:\n"
               "  dotnet build tools/headless -c Debug\n"
               "(or pass --build; --allow-stale runs against the old binary deliberately)\n")
        if not args.allow_stale:
            print("STALE BINARY -- refusing to run the probes.\n" + head + stale + fix,
                  file=sys.stderr)
            return 2
        print("WARNING: STALE BINARY -- running anyway (--allow-stale).\n" + head + stale + fix,
              file=sys.stderr)

    failed = []
    for path in paths:
        name = os.path.basename(path)
        extra, summary = read_meta(path)
        sys.stdout.write("%-24s " % name)
        sys.stdout.flush()
        rc, out, cmd = run(path, extra, args.verbose)
        if rc == 0:
            print("PASS")
        else:
            print("FAIL (%s)" % ("timeout" if rc == "timeout" else "exit %d" % rc))
            failed.append(name)
            print("    %s" % summary)
            print("    %s" % " ".join(shlex.quote(c) for c in cmd))
            for ln in failure_tail(out):
                print("    | %s" % ln)

    print()
    print("%d/%d probes passed" % (len(paths) - len(failed), len(paths)))
    if failed:
        print("failed: %s" % ", ".join(failed))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
