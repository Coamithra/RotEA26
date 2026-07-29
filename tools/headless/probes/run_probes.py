#!/usr/bin/env python
"""Run the committed eahl regression probes.

    python tools/headless/probes/run_probes.py            # all probes
    python tools/headless/probes/run_probes.py --list
    python tools/headless/probes/run_probes.py --only preload_*
    python tools/headless/probes/run_probes.py --build    # dotnet build eahl first

Exit 0 = every probe passed, 1 = at least one failed, 2 = the runner itself could not run
(eahl missing, no probes matched, ...).

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

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
EAHL = os.path.join(REPO, "tools", "headless", "bin", "Debug", "net8.0",
                    "eahl.exe" if os.name == "nt" else "eahl")

# `# eahl: --flags "?menu&loadlog"` -- extra argv for this probe. eahl itself treats the line
# as an ordinary comment, so a probe file stays runnable by hand with no runner involved.
DIRECTIVE = re.compile(r"^#\s*eahl:\s*(.+?)\s*$")

# `# PROBE: <text>` -- the probe's one-line summary, shown by --list and on a failure.
SUMMARY = re.compile(r"^#\s*PROBE:\s*(.+?)\s*$")


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
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, errors="replace",
                              timeout=TIMEOUT_S)
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


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", metavar="GLOB", help="run only probes whose name matches")
    ap.add_argument("--list", action="store_true", help="list probes and exit")
    ap.add_argument("--build", action="store_true", help="dotnet build eahl first")
    ap.add_argument("--verbose", action="store_true", help="stream each probe's output")
    args = ap.parse_args()

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
