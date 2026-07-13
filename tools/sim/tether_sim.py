"""Stage 11.3 tether isolation sim -- picks/validates the ShipConnector net-pull constants.

Models the TeamChallenge tether as implemented in ShipConnector.NetPullOwnShip: each peer
applies a FIRST-ORDER positional pull to its OWN ship toward the remote puppet's on-screen
(i.e. stale) position whenever the gap exceeds the rest length:

    if dist > REST: step = min(K * (dist - REST), MAX_PULL) * dt  (toward the anchor)

First-order (no velocity state) cannot self-oscillate; the one instability channel is the
MUTUAL STALE-ANCHOR loop: each side chases a delayed image of the other (stream cadence +
interp delay + network one-way). This sim runs the two coupled peers with realistic delays
and asserts the loop stays overdamped: once input stops, the stretch envelope decays and
never rings.

Run:  python tools/sim/tether_sim.py            (assert mode; exit 0 = constants are safe)
      python tools/sim/tether_sim.py --sweep    (table over K x delay to eyeball margins)

No browser, no game -- pure data, the repo's isolation-sim testing rule.
"""

import argparse
import math
import sys

# ---- constants under test (mirror ShipConnector.cs Net* consts) --------------------------
REST = 78.0          # px, the 2 x 39 docking separation
K = 0.0018           # per ms: fraction of excess stretch recovered per ms
MAX_PULL = 0.22      # px/ms pull speed clamp (< ship MaxSpeed 0.33 -> players can fight it)

# ---- fixed environment -------------------------------------------------------------------
TICK = 16.6667       # ms, game tick
STREAM = 33.0        # ms, ship-stream cadence
INTERP = 100.0       # ms, interpolation delay behind newest sample
SHIP_SPEED = 0.33    # px/ms, PlayerShip max speed


class Peer:
    """One peer's view: own ship position + a delayed sample tape of the other ship."""

    def __init__(self, x):
        self.x = x
        self.tape = []  # (t_visible, pos) samples of the REMOTE ship

    def anchor(self, t):
        vis = None
        for tv, pos in self.tape:
            if tv <= t:
                vis = pos
            else:
                break
        return vis


def pull(own, anchor, dt):
    d = anchor - own
    dist = abs(d)
    if dist <= REST:
        return own
    step = min(K * (dist - REST), MAX_PULL) * dt
    return own + step * (d / dist)


def run(one_way_ms, drive_ms=1000.0, total_ms=8000.0, mode="drag"):
    """1-D scenario: A drives away at full speed for drive_ms, then stops. Returns the
    per-tick gap timeline (list of (t, gap))."""
    a, b = Peer(-REST / 2), Peer(REST / 2)
    delay = one_way_ms + INTERP  # what each side effectively sees
    t, next_stream, gaps = 0.0, 0.0, []
    while t < total_ms:
        if t >= next_stream:
            a.tape.append((t + delay, b.x))
            b.tape.append((t + delay, a.x))
            next_stream += STREAM
        anch_a, anch_b = a.anchor(t), b.anchor(t)
        # input phase
        if t < drive_ms:
            if mode == "drag":
                a.x -= SHIP_SPEED * TICK
            elif mode == "both":
                a.x -= SHIP_SPEED * TICK
                b.x += SHIP_SPEED * TICK
        # tether phase (positional pull on OWN ship only)
        if anch_a is not None:
            a.x = pull(a.x, anch_a, TICK)
        if anch_b is not None:
            b.x = pull(b.x, anch_b, TICK)
        gaps.append((t, abs(b.x - a.x)))
        t += TICK
    return gaps


def analyze(gaps, drive_ms):
    """After input stops: peak stretch, ring count (rises of the excess after it started
    decaying), and the final gap."""
    post = [(t, g) for t, g in gaps if t >= drive_ms]
    peak = max(g for _, g in post)
    final = post[-1][1]
    # count local minima->rise events in the excess-over-rest signal (ringing)
    exc = [max(0.0, g - REST) for _, g in post]
    rings = 0
    falling = False
    for i in range(1, len(exc)):
        if exc[i] < exc[i - 1] - 1e-6:
            falling = True
        elif falling and exc[i] > exc[i - 1] + 0.5:  # >0.5px rebound = a ring
            rings += 1
            falling = False
    return peak, rings, final


def scenario_table(sweep=False):
    global K
    ks = [K] if not sweep else [0.0009, 0.0018, 0.0035, 0.007, 0.014]
    k_saved = K
    ok = True
    print(f"{'K/ms':>7} {'oneway':>7} {'mode':>5} {'peak px':>8} {'rings':>5} {'final px':>9}")
    for k in ks:
        K = k
        for one_way in (0.0, 50.0, 100.0, 200.0, 300.0):
            for mode in ("drag", "both"):
                gaps = run(one_way, mode=mode)
                peak, rings, final = analyze(gaps, 1000.0)
                good = rings == 0 and final <= REST + 1.0 and peak < 900.0
                ok &= (not math.isclose(k, k_saved)) or good
                flag = "" if good else "  <-- FAIL"
                print(f"{k:7.4f} {one_way:7.0f} {mode:>5} {peak:8.1f} {rings:5d} {final:9.1f}{flag}")
    K = k_saved
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sweep", action="store_true", help="table over K values")
    args = ap.parse_args()
    ok = scenario_table(sweep=args.sweep)
    if args.sweep:
        # Sweep is an EYEBALL table only -- it always exits 0. Only the no-arg assert
        # mode validates the shipped K row and gates on it.
        return 0
    if ok:
        print(f"\nOK: K={K}/ms MAX_PULL={MAX_PULL}px/ms REST={REST}px is overdamped up to "
              f"300ms one-way (+{INTERP:.0f}ms interp) -- no ringing, converges to rest.")
        return 0
    print("\nFAIL: constants ring or diverge -- SOFTEN K (never stiffen).")
    return 1


if __name__ == "__main__":
    sys.exit(main())
