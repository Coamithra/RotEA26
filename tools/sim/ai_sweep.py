#!/usr/bin/env python
"""Paired-seed AI bench sweeps through `eahl` -- the audit kit for AI tuning A/Bs.

Runs one or more ARMS (a set of ?ai* overrides) over one or more RIGS (a level +
its cap), scores every run with AiBench's machine-readable row, and prints a
table per rig with the arms side by side.

    python tools/sim/ai_sweep.py --rig spacedodge --arm shipped= --arm og=aifieldpx=150&aifieldsize=0

THE PROTOCOL THIS ENFORCES (cards ada9e839 / c1d783ad -- read before quoting a number):

  * ?seed= is NEAR-deterministic, not deterministic: a same-seed run lands in one
    of a handful of discrete worlds, and a loaded box makes that more likely. So
    every seed is captured TWICE per arm (--captures) and the two captures are
    compared. **A seed whose own two captures disagree on deaths is UNSTABLE and
    is reported as such** -- the table marks it, and cross-arm differences resting
    on an unstable seed are not evidence. Same-side agreement first, then compare
    sides.
  * Arms are compared on the SAME seeds, never on run counts that differ.
  * Deaths alone is not a verdict: `victories` and `killers` are in the table
    because a bot that stops dodging scores beautifully on churn while losing.
  * Every AI figure predating merge f6b6504 (PR #298, the recycle-phantom fix) was
    measured with phantom teleport cones in the world. Re-capture your own
    baseline arm; never A/B against a table in the docs.

Rigs are named presets (--list-rigs); anything else can be passed as
`name:flags:seconds`. Arms are `label=query` where query is the bare ?ai*
overrides (no leading `?`), so `shipped=` is the baked configuration.
"""

import argparse
import concurrent.futures
import os
import re
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EAHL = os.path.join(REPO, "tools", "headless", "bin", "Debug", "net8.0", "eahl.exe")

# name -> (level flags, sim seconds). Durations are the ones the AI cards used, so
# a new number is comparable with the ones already recorded.
RIGS = {
    "spacedodge": ("level=SpaceDodge", 600),
    "crazygame": ("level=CrazyGame", 300),
    "spider": ("level=Level2&spiderboss", 300),
    # 600s, not the 300 the older cards used: Level 1 does not REACH a verdict in 300
    # sim-s, so at that cap it reads 0 victories on every arm and the victory column
    # carries no signal at all. Deaths-only comparisons are still valid there.
    "level1": ("level=Level1", 600),
    "brainboss": ("level=Level3&brainboss", 300),
    "level3": ("level=Level3", 300),
    "ownlevel": ("level=OwnLevel", 300),
}

# Every run is Very_Hard with the bot flying and no invulnerability -- the gate the
# AI cards measure on. ?noattract keeps the idle demo out of a debug boot.
BASE_FLAGS = "aiplayer&aibench&noattract&difficulty=Very_Hard"

ROW = re.compile(r"^ok eval (verdict=.*)$", re.M)


def run_one(rig_flags, seconds, arm_query, seed):
    """One eahl run. Returns the parsed AiBench row as a dict."""
    query = "?" + rig_flags + "&" + BASE_FLAGS + "&seed=" + str(seed)
    if arm_query:
        query += "&" + arm_query
    frames = int(seconds * 60)
    fd, path = tempfile.mkstemp(suffix=".txt", prefix="aisweep-")
    try:
        with os.fdopen(fd, "w") as f:
            f.write("step %d nodraw\neval AiBenchRow\nquit\n" % frames)
        out = subprocess.run([EAHL, "--flags", query, "--script", path],
                             capture_output=True, text=True, timeout=1800).stdout
    finally:
        os.unlink(path)
    m = ROW.search(out)
    if not m:
        raise RuntimeError("no AiBench row from: %s\n%s" % (query, out[-2000:]))
    row = {}
    for pair in m.group(1).split():
        if "=" in pair:
            k, v = pair.split("=", 1)
            row[k] = v
    return row


def sweep(seconds, rig_flags, arms, seeds, captures, workers):
    jobs = [(label, q, seed, cap)
            for label, q in arms for seed in seeds for cap in range(captures)]
    results = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        futs = {pool.submit(run_one, rig_flags, seconds, q, seed): (label, seed, cap)
                for (label, q, seed, cap) in jobs}
        done = 0
        for fut in concurrent.futures.as_completed(futs):
            label, seed, cap = futs[fut]
            results.setdefault((label, seed), []).append(fut.result())
            done += 1
            print("  %d/%d" % (done, len(jobs)), end="\r", file=sys.stderr)
    return results


def report(rig, arms, seeds, results):
    print("\n=== %s ===" % rig)
    header = "seed   " + "".join("%-22s" % label for label, _ in arms)
    print(header)
    unstable = []
    for seed in seeds:
        cells = []
        for label, _ in arms:
            rows = results[(label, seed)]
            deaths = [int(r["deaths"]) for r in rows]
            wins = sum(1 for r in rows if r["verdict"] == "VICTORY")
            flag = "" if len(set(deaths)) == 1 else " !"
            if flag:
                unstable.append((label, seed, deaths))
            cells.append("%-22s" % ("%s d  %d/%d v%s" %
                                    ("/".join(str(d) for d in deaths), wins, len(rows), flag)))
        print("%-7d%s" % (seed, "".join(cells)))
    print("-" * len(header))
    for label, _ in arms:
        allrows = [r for seed in seeds for r in results[(label, seed)]]
        deaths = [int(r["deaths"]) for r in allrows]
        wins = sum(1 for r in allrows if r["verdict"] == "VICTORY")
        killers = {}
        for r in allrows:
            if r.get("killers", "none") != "none":
                for k in r["killers"].split(","):
                    name, n = k.rsplit(":", 1)
                    killers[name] = killers.get(name, 0) + int(n)
        top = ", ".join("%s:%d" % kv for kv in
                        sorted(killers.items(), key=lambda kv: -kv[1])[:4])
        print("%-12s deaths %5.2f   victories %2d/%-3d  %s"
              % (label, sum(deaths) / float(len(deaths)), wins, len(allrows), top))
        # The secondary counters, which decide as many AI questions as deaths do:
        # idle% is "a shootable was on screen and it did not shoot", bossfar% is the
        # approach term measured where it acts, and pickups is the powerup rate.
        def mean(key):
            return sum(float(r.get(key, 0)) for r in allrows) / float(len(allrows))
        got = sum(int(r.get("pickups", 0)) for r in allrows)
        offered = sum(int(r.get("poffered", 0)) for r in allrows)
        # TIME TO VICTORY, over the runs that reached one. Deaths are an EXPOSURE
        # count, not a survival rate: a build that takes twice as long to finish the
        # same world dies about twice as often there while dodging exactly as well.
        # Card 05a2b818 found the whole of c1d783ad's handed-off SpaceDodge seed-4
        # "regression" to be this and nothing else (victory at 456s/17 deaths against
        # 165s/4 on the same seed), so read the two columns together, never deaths alone.
        won = [float(r["verdictAt"]) for r in allrows if r["verdict"] == "VICTORY"]
        vstr = ("%.0fs" % (sum(won) / len(won))) if won else "n/a"
        print("%-12s   win@%-5s idle %4.1f%%  bossfar %4.1f%%  boss %4.0fpx  turn %4.0fdeg/s"
              "  revs %4.2f  coast %4.1f%%  pickups %d/%d"
              % ("", vstr, mean("idle"), mean("bossfar"), mean("boss"), mean("turn"),
                 mean("revs"), mean("coast"), got, offered))
    # PAIRED comparison against the first arm, seed as the unit of analysis.
    #
    # This is the part that stops a sweep lying to you. Per-seed deaths on these rigs
    # range from 0 to 17, so the standard error of a 16-run mean is ~2 deaths -- big
    # enough that two arms differing by "46%" can be one SEM apart and mean nothing.
    # Seeds are the dominant variance source and every arm runs the SAME seeds, so
    # pairing by seed removes that variance and is far more powerful than comparing
    # two independent means. Read `diff` with its +-: an interval spanning 0 is NOT
    # evidence, however large the percentage looks.
    def seed_means(label):
        return [sum(int(r["deaths"]) for r in results[(label, s)])
                / float(len(results[(label, s)])) for s in seeds]

    def mean_sem(xs):
        n = len(xs)
        m = sum(xs) / float(n)
        if n < 2:
            return m, float("nan")
        var = sum((x - m) ** 2 for x in xs) / float(n - 1)
        return m, (var / n) ** 0.5

    base_label = arms[0][0]
    base = seed_means(base_label)
    print("paired vs %s (per-seed deaths, +- 1 SEM of the paired difference):" % base_label)
    for label, _ in arms:
        m, sem = mean_sem(seed_means(label))
        if label == base_label:
            print("   %-12s %5.2f +- %.2f" % (label, m, sem))
            continue
        d = [a - b for a, b in zip(seed_means(label), base)]
        dm, dsem = mean_sem(d)
        sig = "" if (dsem != dsem or abs(dm) <= 2 * dsem) else "   <- outside 2 SEM"
        print("   %-12s %5.2f +- %.2f   diff %+5.2f +- %.2f%s" % (label, m, sem, dm, dsem, sig))
    if unstable:
        print("UNSTABLE (same-arm captures disagree -- do not rest a conclusion on these):")
        for label, seed, deaths in unstable:
            print("   %s seed %d: %s" % (label, seed, deaths))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rig", action="append", default=[],
                    help="a preset name, or name:flags:seconds")
    ap.add_argument("--arm", action="append", default=[],
                    help="label=query -- e.g. og=aifieldpx=150&aifieldsize=0; `shipped=` is baked")
    ap.add_argument("--seeds", default="1-8")
    ap.add_argument("--captures", type=int, default=2,
                    help="captures per seed per arm (2 = the protocol; 1 proves nothing)")
    ap.add_argument("--workers", type=int, default=4,
                    help="parallel eahl processes -- this box is shared, keep it modest")
    ap.add_argument("--list-rigs", action="store_true")
    args = ap.parse_args()

    if args.list_rigs:
        for name, (flags, secs) in sorted(RIGS.items()):
            print("%-12s ?%s  %ds" % (name, flags, secs))
        return 0
    if not os.path.exists(EAHL):
        print("no eahl -- dotnet build tools/headless -c Debug", file=sys.stderr)
        return 2
    if not args.rig or not args.arm:
        ap.error("need at least one --rig and one --arm")

    seeds = []
    for part in args.seeds.split(","):
        if "-" in part:
            a, b = part.split("-")
            seeds.extend(range(int(a), int(b) + 1))
        else:
            seeds.append(int(part))

    arms = []
    for a in args.arm:
        label, _, query = a.partition("=")
        arms.append((label, query))

    for rigspec in args.rig:
        if rigspec in RIGS:
            rig, (flags, secs) = rigspec, RIGS[rigspec]
        else:
            rig, flags, secs = rigspec.split(":")
            secs = int(secs)
        print("running %s (%ds, %d arms, %d seeds x%d)"
              % (rig, secs, len(arms), len(seeds), args.captures), file=sys.stderr)
        report(rig, arms, seeds, sweep(secs, flags, arms, seeds, args.captures, args.workers))
    return 0


if __name__ == "__main__":
    sys.exit(main())
