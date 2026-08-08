"""Per-tick trace of the AI's deliberate destination, and the arrival oscillation in it.

WHAT IT MEASURES (card fd126847)
--------------------------------
The instrument that found the reported pingpong. It runs `eahl` with `?aiseeklog`, parses the
`[aiseek]` line the game prints once per tick per AI ship, and reduces a run to the numbers an
arrival is judged on -- all SCOPED to the station seek, so a busy level's other steering does not
drown them out:

  * `reexits`  -- deadzone LEAVINGS: ticks where the ship was inside `dz` and is now outside it.
                  A clean arrive has none; the pre-card gate scored one per swing.
  * `uturns`   -- travel-direction reversals (>115 deg) while the station owns the steer, i.e. the
                  jitter pair restricted to the term under test.
  * `restDist` -- mean distance to the station over the ticks the ship is actually stopped.
                  It RISES slightly with a predictive gate (the pull is released earlier) and that
                  is the intended trade, not a regression -- read it beside the two above.

Both counts are reported per 1000 station ticks, because the two arms do not walk identical
worlds: a behavioural change reroutes the level from the first tick, so run TOTALS are not
comparable and only rates are.

WHY IT EXISTS
-------------
No frame shows any of this, and `?aibench`'s `turn`/`revs` are the whole steering sum -- a station
oscillation and a busy dodge look the same there. The `[aiseek]` line is the seek's only
observable, and the same trace is what ATTRIBUTED the "spinning circles" on
`?level=Level3&brainboss` to the boss approach rather than to the station.

USAGE
-----
    dotnet build web/EvilAliensWeb -c Debug && dotnet build tools/headless -c Debug
    python tools/sim/ai_seek_trace.py --rig Braineroids --seeds 1,2,3,4

    # the A/B: `?aiseekarrive=0` is the pre-card position-only gate
    python tools/sim/ai_seek_trace.py --arms aiseekarrive=1,aiseekarrive=0

`--dump` prints the raw per-tick rows instead, which is how the limit cycle was first seen.
Rebuild eahl after any Game/ or Compat/ edit -- like `ai_sweep.py`, this benches the last-built
binary and will silently measure the previous one otherwise.
"""

import argparse
import math
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE = os.path.join(REPO, "tools", "headless", "bin", "Debug", "net8.0", "eahl.exe")
ROW = re.compile(r"kind=(\w+) tgt=\S+ pos=([\d.-]+),([\d.-]+) dist=([\d.-]+) v=([\d.]+) "
                 r"gate=(on|off) dz=([\d.]+) arrive=(\w+)")
# A travel-direction change beyond this is a reversal, not a curve. 115 deg matches the spirit of
# AiBench's own reversal deadband: it is the "the ship turned round" case, not "the ship turned".
UTURN_RAD = 2.0


def trace(rig, seed, arm, frames, difficulty):
    flags = (f"?level={rig}&aiplayer&aibench&invuln&noattract"
             f"&difficulty={difficulty}&seed={seed}&aiseeklog&{arm}")
    out = subprocess.run([EXE, "--flags", flags, "--frames", str(frames), "--nodraw"],
                         capture_output=True, text=True).stdout
    rows = []
    for line in out.splitlines():
        if "[aiseek]" not in line:
            continue
        m = ROW.search(line)
        if m and m.group(1) == "station":
            rows.append(dict(x=float(m.group(2)), y=float(m.group(3)), dist=float(m.group(4)),
                             v=float(m.group(5)), gate=m.group(6), dz=float(m.group(7)),
                             arrive=m.group(8)))
    return rows


def reduce_rows(rows):
    reexits = uturns = 0
    was_inside = None
    prev_dir = None
    rest = []
    for i, r in enumerate(rows):
        inside = r["dist"] <= r["dz"]
        if was_inside and not inside:
            reexits += 1
        was_inside = inside
        if r["v"] < 0.02:
            rest.append(r["dist"])
        if i:
            dx, dy = r["x"] - rows[i - 1]["x"], r["y"] - rows[i - 1]["y"]
            # Ignore sub-pixel steps: their direction is rounding, not travel.
            if dx * dx + dy * dy > 0.04:
                a = math.atan2(dy, dx)
                if prev_dir is not None:
                    d = abs((a - prev_dir + math.pi) % (2 * math.pi) - math.pi)
                    if d > UTURN_RAD:
                        uturns += 1
                prev_dir = a
    n = max(len(rows), 1)
    return dict(n=len(rows), reexits=reexits, uturns=uturns,
                reexit_kt=reexits / n * 1000.0, uturn_kt=uturns / n * 1000.0,
                rest=(sum(rest) / len(rest)) if rest else float("nan"), restn=len(rest),
                arrive=rows[-1]["arrive"] if rows else "?")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rig", default="Braineroids",
                    help="a Levels value; the default is where the idle station owns the steer")
    ap.add_argument("--seeds", default="1,2,3,4")
    ap.add_argument("--arms", default="aiseekarrive=1,aiseekarrive=0",
                    help="bare query fragments, compared on the same seeds")
    ap.add_argument("--frames", type=int, default=3600)
    ap.add_argument("--difficulty", default="Very_Hard")
    ap.add_argument("--dump", action="store_true", help="print the raw per-tick rows and exit")
    args = ap.parse_args()

    if not os.path.exists(EXE):
        print("no eahl at " + EXE + " -- dotnet build tools/headless -c Debug", file=sys.stderr)
        return 2
    seeds = [int(s) for s in args.seeds.split(",")]
    arms = args.arms.split(",")

    if args.dump:
        for r in trace(args.rig, seeds[0], arms[0], args.frames, args.difficulty):
            print(f"pos=({r['x']:6.1f},{r['y']:6.1f}) dist={r['dist']:6.2f} v={r['v']:.3f} "
                  f"gate={r['gate']:3} arrive={r['arrive']}")
        return 0

    totals = {a: [0, 0, 0, 0.0, 0] for a in arms}
    for seed in seeds:
        for arm in arms:
            s = reduce_rows(trace(args.rig, seed, arm, args.frames, args.difficulty))
            t = totals[arm]
            t[0] += s["n"]
            t[1] += s["reexits"]
            t[2] += s["uturns"]
            t[3] += s["rest"] * s["restn"] if s["restn"] else 0.0
            t[4] += s["restn"]
            print(f"{args.rig} seed{seed} {arm:16} arrive={s['arrive']:10} "
                  f"stationTicks={s['n']:5d} reexits={s['reexits']:3d} ({s['reexit_kt']:5.2f}/kt) "
                  f"uturns={s['uturns']:4d} ({s['uturn_kt']:5.2f}/kt) "
                  f"restDist={s['rest']:5.2f}")
    print()
    for arm in arms:
        n, re_, ut, restsum, restn = totals[arm]
        n = max(n, 1)
        print(f"{arm:16} MEAN over {len(seeds)} seeds: reexits {re_ / n * 1000:5.2f}/kt   "
              f"uturns {ut / n * 1000:5.2f}/kt   restDist "
              f"{(restsum / restn) if restn else float('nan'):5.2f}px")
    return 0


if __name__ == "__main__":
    sys.exit(main())
