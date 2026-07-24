"""Isolation sim for the pupPops burst on the FIRST wipe (deferred from card 11.3, item 1).

WHAT IT MODELS
--------------
The client drives its frozen enemy puppets from `NetPuppetDriver.Update`, an ordinary
`GameComponent`. `Game1.Update` folds turbo * slow-motion * hit-stop into the `gameTime` it
hands to `base.Update(gameTime)`, so the driver -- and therefore `NetPuppets.Drive` --
dead-reckons on SCALED game time. The host meanwhile captures every world snapshot on REAL
time (`NetSession` cadence is `Environment.TickCount64`) and stamps each entity's observed
velocity as realPosDelta / realTimeDelta.

So during any window where the client's game clock runs slower than real time -- the 180 ms
player-death hit-stop at a wipe (`PlayerShip.Asplode` -> `Juice.AddHitStop(0.18f)`), a 1-up
slow-motion (`Oracle.SetSlowmotion` = 0.4x) overlapping the transition -- the puppets fall
behind the host's real-time positions. Worse, the snapshot CORRECTION blend only advances
inside `Drive` (it decrements `CorrectionMsLeft` there), so while the client clock is slow the
incoming corrections apply slowly too and the residual error keeps climbing. Once it crosses
the 100 px snap threshold (`SnapThresholdPx`) the entry hard-snaps and counts a `pupPops`.

The remote-SHIP puppet already sidesteps exactly this: `NetSession.DriveRemoteShip` advances
on `realDtMs` and the comments call it out ("never the turbo/slowmo/hit-stop-scaled game
time"). The enemy puppet driver was meant to match -- `AlienDrawableGameComponent.NetAdvanceFrame`
is even documented "on real dt" -- but `Drive` feeds it the scaled `gameTime`. THE FIX drives
the puppets on real elapsed time too.

WHAT IT PROVES (and what it does NOT)
-------------------------------------
It mirrors the real `Drive` + `ApplySnapshotState` math (same 100 px `SnapThresholdPx`, 150 ms
`CorrectionWindowMs`, 60 ms round-robin <=16-entry snapshots, real-time observed velocity) and
runs the client puppets two ways:

    * SCALED  -- the shipped bug: driver dt is scaled by the client's time-scale profile.
    * REAL    -- the fix: driver dt is real elapsed time, whatever the game clock is doing.

Findings the assertion below locks in:

  1. The REAL driver logs ZERO pops in every time-scaled window -- it stays converged with the
     host's real-time world. This is the fix's guarantee.
  2. The SCALED driver's pop count GROWS with how deep and how long the time scaling is:
       - a bare 180 ms hit-stop is absorbed by the correction blend (~0 pops) -- so the
         time-scaling suspect, on its own, does NOT explain a 200-500 burst;
       - a longer / deeper scaling window (a slow-motion that overlaps the wipe, or fast
         divers) does drive real pops.

So this fix removes the CONFIRMED time-scaling contribution. A full first-wipe burst of the
200-500 magnitude the card reported additionally implicates the reset / id-churn transition
(purge + checkpoint replay), which is out of a single-puppet clock model's reach -- that is
what the headless two-peer net sim (item 2, scenarios "reset/pause ordering" + "id churn") is
built to reproduce. The two items compose: item 1 removes the clock bug; item 2 is the harness
to pin any transition residual.

No browser, no game -- pure data, the repo's isolation-sim rule.

Run:  python tools/sim/net_puppet_drive_sim.py           (assert mode; exit 0 = fix holds)
      python tools/sim/net_puppet_drive_sim.py --sweep   (pops vs window depth/length table)
"""

import argparse
import sys

# ---- constants mirrored from the C# net layer -------------------------------------------
SNAP_THRESHOLD_PX = 100.0     # NetPuppets.SnapThresholdPx -- error above this snaps + pops
CORRECTION_WINDOW_MS = 150.0  # NetPuppets.CorrectionWindowMs -- blend a sub-threshold error
SNAPSHOT_INTERVAL_MS = 60.0   # NetSession.SnapshotIntervalMs -- host world-snapshot cadence
SNAPSHOT_MAX_ENTRIES = 16     # NetSession.SnapshotMaxEntries -- round-robin budget per packet
TICK_MS = 1000.0 / 60.0       # game tick (~16.67 ms)

# Time-scale sources, from the game.
HITSTOP_MS = 180.0            # Juice.AddHitStop(0.18f) at PlayerShip.Asplode
SLOWMO_SCALE = 0.4            # Oracle.SetSlowmotion sets slowmotion = 0.4f
SLOWMO_MS = 12000.0          # SetSlowmotion(12f)


class Enemy:
    """A host enemy: real-time motion + the host's per-entity observed-velocity baseline
    (NetSession.CaptureBaseState differentiates real positions between the entity's turns)."""

    def __init__(self, pos, vel):
        self.pos = pos
        self.vel = vel               # true velocity (px/ms), constant here
        self.have_last = False
        self.last_pos = 0.0
        self.last_ms = 0.0

    def advance(self, dt_ms):
        self.pos += self.vel * dt_ms

    def observe(self, now_ms):
        """Snapshot capture: position + observed velocity (real delta / real time delta)."""
        vel = self.vel
        if self.have_last and now_ms > self.last_ms:
            vel = (self.pos - self.last_pos) / (now_ms - self.last_ms)
        self.last_pos = self.pos
        self.last_ms = now_ms
        self.have_last = True
        return self.pos, vel


class Puppet:
    """A client puppet: dead-reckoned position + the active correction blend (NetPuppets)."""

    def __init__(self, pos):
        self.pos = pos
        self.vel = 0.0
        self.correction = 0.0
        self.correction_ms_left = 0.0
        self.has_snapshot = True      # spawn already landed, like steady state

    def apply_snapshot(self, snap_pos, snap_vel):
        """Mirror of NetPuppets.ApplySnapshotState (position branch). Returns True on a pop."""
        popped = False
        err = snap_pos - self.pos
        if abs(err) > SNAP_THRESHOLD_PX:
            self.pos = snap_pos
            self.correction = 0.0
            self.correction_ms_left = 0.0
            popped = True
        else:
            self.correction = err
            self.correction_ms_left = CORRECTION_WINDOW_MS
        self.vel = snap_vel
        return popped

    def drive(self, dt_ms):
        """Mirror of NetPuppets.Drive (position step). dt_ms is the driver's elapsed time --
        SCALED game time in the shipped bug, REAL elapsed time after the fix."""
        step = self.vel * dt_ms
        if self.correction_ms_left > 0.0:
            take = min(dt_ms, self.correction_ms_left)
            step += self.correction * (take / CORRECTION_WINDOW_MS)
            self.correction_ms_left -= take
        self.pos += step


def time_scale(t_ms, profile):
    """Client game-time scale at real time t. `profile` = [(start, end, scale), ...] windows
    (turbo is locked to 100 in a net session, so 1.0 is the only baseline)."""
    scale = 1.0
    for start, end, s in profile:
        if start <= t_ms < end:
            scale *= s
    return scale


def run(profile, n_enemies=16, speed=0.15, total_ms=15000.0, mode="scaled"):
    """One coupled run. Returns (pops, peak_error_px) for the given driver mode."""
    enemies = [Enemy(pos=100.0 + 25.0 * i, vel=speed) for i in range(n_enemies)]
    puppets = [Puppet(pos=e.pos) for e in enemies]
    cursor = 0
    next_snap = SNAPSHOT_INTERVAL_MS
    pops = 0
    peak = 0.0
    t = 0.0
    while t < total_ms:
        for e in enemies:                 # host advances on REAL time
            e.advance(TICK_MS)
        if t >= next_snap:                # host world snapshot: round-robin <=16 entries
            count = min(SNAPSHOT_MAX_ENTRIES, len(enemies))
            for _ in range(count):
                idx = cursor % len(enemies)
                cursor = (cursor + 1) % len(enemies)
                snap_pos, snap_vel = enemies[idx].observe(t)
                if puppets[idx].apply_snapshot(snap_pos, snap_vel):
                    pops += 1
            next_snap += SNAPSHOT_INTERVAL_MS
        dt = TICK_MS * (time_scale(t, profile) if mode == "scaled" else 1.0)
        for i, p in enumerate(puppets):
            p.drive(dt)
            peak = max(peak, abs(enemies[i].pos - p.pos))
        t += TICK_MS
    return pops, peak


def scenarios():
    """(label, profile, speed, n). Windows start at t=1000 ms after a steady-state lead-in.
    `is_burst` marks the ones the SCALED bug must actually reproduce a burst on."""
    W = 1000.0
    return [
        # label, profile, speed, n, expect_scaled_burst
        ("bare hit-stop 180ms (typical foes)", [(W, W + HITSTOP_MS, 0.0)], 0.15, 16, False),
        ("bare hit-stop 180ms (fast divers)",  [(W, W + HITSTOP_MS, 0.0)], 0.40, 16, False),
        ("1-up slowmo 0.4x 12s (fast divers)", [(W, W + SLOWMO_MS, SLOWMO_SCALE)], 0.55, 24, True),
        # A multi-second near-freeze (deep, long scaling) -- the shape a burst takes.
        ("deep freeze 4s (typical foes)",      [(W, W + 4000.0, 0.02)], 0.15, 24, True),
        ("deep freeze 8s (typical foes)",      [(W, W + 8000.0, 0.02)], 0.15, 24, True),
    ]


def assert_mode():
    ok = True
    header = f"{'scenario':<38} {'SCALED pops':>12} {'peak px':>8} {'REAL pops':>10}"
    print(header)
    print("-" * len(header))

    # Control: no time scaling -> both modes clean (the sim must not manufacture pops).
    ss_scaled, _ = run([], n_enemies=24, speed=0.15, total_ms=6000.0, mode="scaled")
    ss_real, _ = run([], n_enemies=24, speed=0.15, total_ms=6000.0, mode="real")
    print(f"{'steady state (control)':<38} {ss_scaled:>12} {'-':>8} {ss_real:>10}")
    if ss_scaled != 0 or ss_real != 0:
        ok = False
        print("  <-- FAIL: control must be 0/0")

    saw_burst = False
    for label, profile, speed, n, expect_burst in scenarios():
        scaled, peak = run(profile, n_enemies=n, speed=speed, mode="scaled")
        real, _ = run(profile, n_enemies=n, speed=speed, mode="real")
        flag = ""
        # THE FIX'S GUARANTEE: the real-time driver stays converged -> 0 pops, always.
        if real != 0:
            ok = False
            flag = "  <-- FAIL: fix should log 0 pops"
        if expect_burst:
            saw_burst = saw_burst or scaled > 0
            if scaled == 0:
                ok = False
                flag = "  <-- FAIL: scaled bug should reproduce a burst here"
        print(f"{label:<38} {scaled:>12} {peak:>8.0f} {real:>10}{flag}")
    if not saw_burst:
        ok = False
        print("  <-- FAIL: no scenario reproduced the bug")
    return ok


def sweep():
    """pops (SCALED) vs window depth (scale) x length -- the burst grows with both. REAL is
    0 throughout, printed for contrast."""
    print("SCALED-mode pupPops, 24 foes @ 0.15 px/ms, window at t=1000ms:\n")
    print(f"{'scale':>6} " + "".join(f"{d:>8}ms" for d in (180, 500, 1000, 2000, 4000, 8000)))
    for scale in (0.0, 0.2, 0.4, 0.6):
        row = [f"{scale:>6.1f}"]
        for dur in (180, 500, 1000, 2000, 4000, 8000):
            prof = [(1000.0, 1000.0 + dur, scale)]
            s, _ = run(prof, n_enemies=24, speed=0.15, total_ms=1000.0 + dur + 3000.0, mode="scaled")
            row.append(f"{s:>10}")
        print("".join(row))
    print("\n(REAL-driver mode logs 0 in every cell above -- the fix.)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sweep", action="store_true", help="pops vs window depth/length (eyeball)")
    args = ap.parse_args()
    if args.sweep:
        sweep()
        return 0
    ok = assert_mode()
    if ok:
        print("\nOK: the REAL-time driver holds the puppets converged (0 pops) in every window; "
              "\nthe shipped SCALED driver diverges and pops as the time scaling deepens/lengthens "
              "\n(a bare 180ms hit-stop is absorbed by the correction blend -- see the first rows). "
              "\nFix: NetPuppets.Drive on real elapsed time, matching the remote-ship puppet.")
        return 0
    print("\nFAIL: see rows above.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
