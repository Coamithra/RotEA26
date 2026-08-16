"""Tether isolation sim -- picks/validates the ShipConnector net-pull constants.

Models the online connector tether as implemented in ShipConnector.NetPullOwnShip: each peer
applies a FIRST-ORDER positional pull to its OWN ship toward the remote puppet's on-screen
(i.e. stale) position whenever the gap exceeds the rest length:

    if dist > REST: step = PULL_SPEED(dist) * dt   (toward the anchor)

First-order (no velocity state) cannot self-oscillate; the one instability channel is the
MUTUAL STALE-ANCHOR loop: each side chases a delayed image of the other (stream cadence +
interp delay + network one-way). This sim runs the two coupled peers with realistic delays
and asserts the loop stays overdamped.

*** WARNING TO WHOEVER READS THIS NEXT (card 2cfab019) ***
Until that card this file drove the players for 1000ms and then STOPPED, and only ever asked
whether the release rang. That is structurally blind to the defect it existed to guard: the
runaway is a STEADY-STATE gain problem that needs SUSTAINED thrust to appear at all, so every
constant here passed green while the shipped tether separated without bound. If you add a
scenario, ask what it can never see.

Card 11.3 picked K / MAX_PULL / REST; card 2cfab019 added the hard cap (HARD_PX / HARD_K /
MAX_HARD_PULL) after measuring that runaway, and its negative leg -- reproducing the unbounded
separation with the cap off -- is the load-bearing half of this file.

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
MAX_PULL = 0.22      # px/ms soft pull-speed cap (< ship MaxSpeed 0.33 -> players can fight it)
HARD_PX = 200.0      # px, the knee: REST + MAX_PULL/K = 200.2, where the soft cap saturates
HARD_K = 0.0055      # per ms, the hard band's added stiffness above the knee
MAX_HARD_PULL = 0.55  # px/ms absolute ceiling (1.67x ship speed; under the 2x "no real ship" bar)

# ---- fixed environment -------------------------------------------------------------------
TICK = 16.6667       # ms, game tick
STREAM = 33.0        # ms, ship-stream cadence
INTERP = 100.0       # ms, interpolation delay behind newest sample
SHIP_SPEED = 0.33    # px/ms, PlayerShip.ShipMaxSpeed
EXTRAP_CAP = 250.0   # ms, ShipStateBuffer.ExtrapolateCapMs -- past it the puppet FREEZES


def pull_speed(dist, wall=True):
    """ShipConnector.NetPullSpeedPxPerMs. wall=False is ?nettetherwall=0, the pre-card law."""
    soft = min(K * (dist - REST), MAX_PULL)
    if not wall or dist <= HARD_PX:
        return soft
    return min(soft + HARD_K * (dist - HARD_PX), MAX_HARD_PULL)


class Peer:
    """One peer's view: own ship position + a delayed sample tape of the other ship."""

    def __init__(self, x):
        self.x = x
        self.tape = []  # (t_visible, pos, vel) samples of the REMOTE ship

    def anchor(self, t):
        vis = None
        for tv, pos, vel in self.tape:
            if tv <= t:
                vis = (tv, pos, vel)
            else:
                break
        if vis is None:
            return None
        tv, pos, vel = vis
        # The jitter buffer extrapolates along the last velocity, capped -- then holds still.
        return pos + vel * min(t - tv, EXTRAP_CAP)


def run(one_way_ms, drive_ms: "float | None" = 1000.0, total_ms=8000.0, mode="drag", wall=True,
        pin_b=False, dark=None):
    """1-D scenario. A drives away for drive_ms (None = for the whole run); mode 'both' has B
    drive the other way too. pin_b freezes B in place (the screen-clamp corner). dark=(t0,t1)
    silences the stream both ways over that window (a peer stall). Returns [(t, signed gap)]."""
    a, b = Peer(-REST / 2), Peer(REST / 2)
    delay = one_way_ms + INTERP  # what each side effectively sees
    t, next_stream, gaps = 0.0, 0.0, []
    va = vb = 0.0
    while t < total_ms:
        quiet = dark is not None and dark[0] <= t < dark[1]
        if t >= next_stream:
            if not quiet:
                a.tape.append((t + delay, b.x, vb))
                b.tape.append((t + delay, a.x, va))
            next_stream += STREAM
        anch_a, anch_b = a.anchor(t), b.anchor(t)
        # input phase
        driving = drive_ms is None or t < drive_ms
        va = -SHIP_SPEED if driving else 0.0
        vb = SHIP_SPEED if (driving and mode == "both") else 0.0
        a.x += va * TICK
        b.x += vb * TICK
        # tether phase (positional pull on OWN ship only)
        for peer, anch in ((a, anch_a), (b, anch_b)):
            if anch is None or (peer is b and pin_b):
                continue
            d = anch - peer.x
            dist = abs(d)
            if dist <= REST:
                continue
            peer.x += pull_speed(dist, wall) * TICK * (d / dist)
        gaps.append((t, b.x - a.x))
        t += TICK
    return gaps


def analyze(gaps, after_ms):
    """After input stops: peak stretch, direction reversals of the gap (ringing), final gap.

    The reversal count is taken on the SIGNED gap, not on max(0, gap - REST): the stale-anchor
    settle routinely takes the pair BELOW the rest length, and an excess-only signal is
    identically zero down there and so cannot see a ring at all."""
    post = [(t, g) for t, g in gaps if t >= after_ms]
    peak = max(g for _, g in post)
    final = post[-1][1]
    rings, direction = 0, 0
    for i in range(1, len(post)):
        dv = post[i][1] - post[i - 1][1]
        nd = 1 if dv > 1e-9 else (-1 if dv < -1e-9 else direction)
        if direction != 0 and nd != 0 and nd != direction and abs(dv) > 0.05:
            rings += 1
        direction = nd
    return peak, rings, final


# ---- the assertion legs ------------------------------------------------------------------
# Sustained thrust: the scenarios card 2cfab019 was reported for. A bound here is the fix.
SUSTAINED = (
    ("both", dict(mode="both")),                  # both players thrusting apart
    ("pinnedB", dict(mode="drag", pin_b=True)),   # one thrusting, partner on the screen clamp
    ("dragB", dict(mode="drag")),                 # one thrusting, partner free -- never broken
)
ONE_WAYS = (0.0, 50.0, 100.0, 200.0, 300.0)
# The band a real co-op session lives in. Up to here the cap is required to be INVISIBLE in
# ordinary play; above it the stale-anchor displacement legitimately reaches the knee (see
# leg_bounded's second block) and the requirement weakens to "may only tighten".
REALISTIC_ONE_WAY = 100.0

# A lone thruster is held by the idle partner's own pull at REST + (SHIP_SPEED/2)/K = 169.7px;
# allow slack around it for the discretisation.
DRAG_BOUND = 185.0
# Sustained equilibrium with the cap: HARD_PX + (SHIP_SPEED - MAX_PULL)/HARD_K = 220px
# perceived, ~214.5px true. Bound generously -- the assertion is BOUNDEDNESS, not a value.
WALL_BOUND = 240.0
# What "runaway" has to clear for the negative leg to mean anything. The shipped law reaches
# 2300-4600px over 20s; a floor near the wall bound would make that leg vacuous.
RUNAWAY_FLOOR = 1500.0


def leg_runaway():
    """NEGATIVE leg: with the cap OFF (?nettetherwall=0) the two runaway scenarios must really
    run away, and the never-broken one must not. Without this, every green tick below could be
    coming from a rig that simply never separates."""
    ok = True
    print("negative leg -- cap OFF (?nettetherwall=0), sustained thrust, 20s")
    for name, kw in SUSTAINED:
        final = run(100.0, drive_ms=None, total_ms=20000.0, wall=False, **kw)[-1][1]
        want_runaway = name != "dragB"
        good = (final > RUNAWAY_FLOOR) if want_runaway else (final <= DRAG_BOUND)
        ok &= good
        print(f"  {name:>8}  gap@20s {final:9.1f}  "
              f"{'runs away (expected)' if want_runaway else 'bounded (expected)'}"
              f"{'' if good else '   <-- FAIL'}")
    return ok


def leg_bounded():
    """POSITIVE leg: with the cap ON every scenario is bounded at every latency, and the
    never-broken drag case is UNCHANGED (the cap must not reach into ordinary play)."""
    ok = True
    print("\npositive leg -- cap ON, sustained thrust, 20s")
    for name, kw in SUSTAINED:
        for one_way in ONE_WAYS:
            final = run(one_way, drive_ms=None, total_ms=20000.0, wall=True, **kw)[-1][1]
            bound = DRAG_BOUND if name == "dragB" else WALL_BOUND
            good = final <= bound
            ok &= good
            print(f"  {name:>8} oneway {one_way:5.0f}  gap@20s {final:8.1f}  "
                  f"(bound {bound:.0f}){'' if good else '   <-- FAIL'}")
    # Ordinary play at realistic latency must be bit-for-bit the pre-card behaviour: below the
    # knee the law is identical, so the drag equilibrium may not move AT ALL.
    #
    # Past REALISTIC_ONE_WAY it legitimately does move, and the reason is worth knowing. In a
    # steady drag both ships travel at the same speed v, so each peer's stale anchor is displaced
    # by v * (one_way + INTERP) ALONG the direction of travel: the LEADING peer therefore
    # perceives true + v*delay and the trailing one true - v*delay. At 300ms one-way that is
    # ~+-64px, enough for the leader's perceived gap to cross the 200px knee while the TRUE gap
    # is only ~162px -- i.e. the cap engages on a stale reading. It is bounded, it does not ring
    # (see leg_release), and it acts in the TIGHTENING direction (181 -> 162px, toward the 78px
    # rest), so it is accepted rather than designed away; but it may only ever tighten.
    print("  the cap must not reach into ordinary play:")
    for one_way in ONE_WAYS:
        on = run(one_way, drive_ms=None, total_ms=20000.0, wall=True, mode="drag")[-1][1]
        off = run(one_way, drive_ms=None, total_ms=20000.0, wall=False, mode="drag")[-1][1]
        if one_way <= REALISTIC_ONE_WAY:
            good = math.isclose(on, off, abs_tol=1e-6)
            note = "== (must be identical)"
        else:
            good = on <= off + 1e-6
            note = "<= (stale-anchor engagement; may only tighten)"
        ok &= good
        print(f"    drag equilibrium oneway {one_way:5.0f}: cap ON {on:7.2f} {note} OFF {off:7.2f}"
              f"{'' if good else '   <-- FAIL (the cap changed ordinary play)'}")
    return ok


def leg_release():
    """STABILITY leg -- the named constraint. The mutual stale-anchor loop must stay overdamped
    WITH the cap in: drive apart, release, and require no direction reversal of the gap at any
    latency. (A cap built as a POSITION clamp would have loop gain exactly 1 and ring here
    forever; this one is a rate, so its per-tick loop gain is HARD_K * TICK = 0.09.)"""
    ok = True
    print("\nstability leg -- drive apart 4s then release, 20s, reversals must be 0")
    for wall in (False, True):
        for one_way in ONE_WAYS:
            gaps = run(one_way, drive_ms=4000.0, total_ms=20000.0, mode="both", wall=wall)
            peak, rings, final = analyze(gaps, 4000.0)
            good = rings == 0
            ok &= good
            print(f"  cap {'ON ' if wall else 'OFF'} oneway {one_way:5.0f}  peak {peak:8.1f}  "
                  f"reversals {rings}  final {final:7.1f}{'' if good else '   <-- FAIL'}")
    return ok


def leg_stall():
    """STALL leg: the cap is deliberately NOT gated on peer freshness. Against a frozen anchor
    the pull is a CONTRACTION with a fixed point at REST, not an integrator, so its total travel
    is bounded by the initial excess however long the stall runs -- and it must be the SAME
    travel with the cap and without, or the cap would be dragging a ship further toward a ghost
    than the pre-card law did. (This is what separates it from ShipStateBuffer.ExtrapolateCapMs
    and Lazer.NetExtrapolateCapMs, which bound `pos + vel*t` integrators and so need a TIME
    bound.) The second assertion is why gating it would be wrong: across a real stall the cap is
    the only thing still holding the pair."""
    ok = True
    print("\nstall leg -- the pull toward a frozen ghost is a contraction, not an integrator")
    for wall in (False, True):
        # own ship parked at the sustained equilibrium, anchor frozen, no thrust, 20s
        x, anchor, travel, max_step, t = 0.0, 214.5, 0.0, 0.0, 0.0
        while t < 20000.0:
            dist = anchor - x
            if dist > REST:
                step = pull_speed(dist, wall) * TICK
                max_step = max(max_step, step)
                x += step
                travel += step
            t += TICK
        settled = anchor - x
        good = (math.isclose(settled, REST, abs_tol=0.5)
                and math.isclose(travel, anchor - REST, abs_tol=0.5))
        ok &= good
        print(f"  cap {'ON ' if wall else 'OFF'}  travel toward ghost {travel:6.1f}px  "
              f"settles at {settled:5.1f}px (rest {REST:.0f})  max step {max_step:.2f}px/frame"
              f"{'' if good else '   <-- FAIL'}")
    stall_on = run(100.0, drive_ms=None, total_ms=14000.0, mode="both", wall=True,
                   dark=(4000.0, 5200.0))[-1][1]
    stall_off = run(100.0, drive_ms=None, total_ms=14000.0, mode="both", wall=False,
                    dark=(4000.0, 5200.0))[-1][1]
    good = stall_on <= WALL_BOUND < stall_off
    ok &= good
    print(f"  across a 1200ms PeerStallMs stall: cap ON {stall_on:8.1f}px  vs  OFF {stall_off:9.1f}px"
          f"{'' if good else '   <-- FAIL'}")
    return ok


def sweep_table():
    """EYEBALL table over K x delay (card 11.3's original view). Always exits 0."""
    global K
    k_saved = K
    print(f"{'K/ms':>7} {'oneway':>7} {'mode':>5} {'peak px':>8} {'rings':>5} {'final px':>9}")
    for k in (0.0009, 0.0018, 0.0035, 0.007, 0.014):
        K = k
        for one_way in ONE_WAYS:
            for mode in ("drag", "both"):
                gaps = run(one_way, drive_ms=1000.0, mode=mode)
                peak, rings, final = analyze(gaps, 1000.0)
                print(f"{k:7.4f} {one_way:7.0f} {mode:>5} {peak:8.1f} {rings:5d} {final:9.1f}")
    K = k_saved


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sweep", action="store_true", help="eyeball table over K values")
    args = ap.parse_args()
    if args.sweep:
        sweep_table()
        return 0
    ok = leg_runaway()
    ok &= leg_bounded()
    ok &= leg_release()
    ok &= leg_stall()
    if ok:
        print(f"\nOK: REST={REST} K={K}/ms MAX_PULL={MAX_PULL} + cap HARD_PX={HARD_PX} "
              f"HARD_K={HARD_K}/ms MAX_HARD_PULL={MAX_HARD_PULL}px/ms -- sustained separation is "
              f"bounded at every latency to 300ms one-way (+{INTERP:.0f}ms interp), the loop does "
              f"not ring, and ordinary drag play is unchanged.")
        return 0
    print("\nFAIL: see the marked rows. If the RELEASE leg rings, SOFTEN K (never stiffen); "
          "if a BOUND leg fails, the cap is not holding.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
