"""Render WHERE the AI bot dies, over the 800x600 design field.

`killers=` says WHAT killed it and `deaths=` says how often; neither can tell
edge-hugging from a mid-field lane collision, and those two want opposite fixes.
AiBench emits `deathpos=x,y;x,y;...` (design space) on its report line; this reads
that straight off an eahl transcript and plots it, aggregated across seeds.

    eahl.exe --flags "?level=SpaceDodge&aiplayer&aibench&seed=1" --repl --nodraw \
        <<< "step 150 nodraw\neval AiBenchRun 60\neval AiBench\nquit" > run1.txt
    python tools/sim/ai_death_heatmap.py run*.txt --out deaths.png --title SpaceDodge

Takes any number of transcripts. Reads only the LAST deathpos= per file, since the
bench report is cumulative and an interval line would double-count.
"""
import argparse
import re
import sys

DESIGN_W, DESIGN_H = 800, 600
POS = re.compile(r"deathpos=([0-9.,;-]+)")


def parse(paths):
    pts = []
    for p in paths:
        with open(p, "r", encoding="utf-8", errors="replace") as fh:
            found = POS.findall(fh.read())
        if not found:
            print("  %s: no deathpos= (no deaths, or ?aibench was off)" % p)
            continue
        n = 0
        for pair in found[-1].split(";"):
            if not pair.strip():
                continue
            x, _, y = pair.partition(",")
            try:
                pts.append((float(x), float(y)))
                n += 1
            except ValueError:
                pass
        print("  %s: %d deaths" % (p, n))
    return pts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--out", default="ai_deaths.png")
    ap.add_argument("--title", default="AI death positions")
    ap.add_argument("--bins", type=int, default=24)
    args = ap.parse_args()

    pts = parse(args.files)
    if not pts:
        print("no death positions found -- nothing to plot")
        return 1
    print("total %d deaths across %d file(s)" % (len(pts), len(args.files)))

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    fig, ax = plt.subplots(figsize=(8, 6))
    h = ax.hist2d(xs, ys, bins=[args.bins, int(args.bins * DESIGN_H / DESIGN_W)],
                  range=[[0, DESIGN_W], [0, DESIGN_H]], cmap="inferno")
    fig.colorbar(h[3], ax=ax, label="deaths per cell")
    ax.scatter(xs, ys, s=6, c="cyan", alpha=0.35, linewidths=0)
    # Design space has +Y DOWN, so invert to match what the player sees.
    ax.set_xlim(0, DESIGN_W)
    ax.set_ylim(DESIGN_H, 0)
    ax.set_xlabel("design X")
    ax.set_ylabel("design Y (0 = top of screen)")
    ax.set_title("%s -- %d deaths" % (args.title, len(pts)))
    fig.tight_layout()
    fig.savefig(args.out, dpi=110)
    print("wrote %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
