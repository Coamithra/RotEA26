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

--population (card 48ab9b2f) asks a SECOND, independent question of the same model: what ELSE
can drive pupPops on a client whose clock is behaving? The JIP two-window pass logged 207 pops
in ~25s and guessed the dense `?flyspiders` swarm was to blame. The sweep weighs both candidate
explanations against the real background-FlyingSpider motion and answers, in short: the swarm
CANNOT (the swivel is only +/-25px, so a straight-line prediction almost never gets 100px wrong
however long the round-robin turn interval grows), while client TICK STARVATION can and does,
enormously -- but only below ~5Hz, where NetPuppetDriver's 200ms dt clamp starts silently
dropping real motion every tick. That is an occluded/hidden window (JIP trap 1), not the ~40fps
the pass recorded. So neither of the pass' own hypotheses survives, which is why the card put
its effort into making the counters decidable instead.

The swarm hypothesis fails twice over, in fact: the sweep below shows it popping nothing at ANY
size, and a live `?level=Level2&flyspiders&net=host` boot measures the rig's actual world at
only 17-19 live entities (`snapTurn=120ms`) -- they spawn at 5.5/s but die off-screen at
`Position.X < -100`, so it reaches a small steady state rather than accumulating. The table
still sweeps to N=2048 because the question "how big would a world have to be" is worth an
answer, not because that rig ever got there.

Run:  python tools/sim/net_puppet_drive_sim.py               (assert mode; exit 0 = fix holds)
      python tools/sim/net_puppet_drive_sim.py --sweep       (pops vs window depth/length)
      python tools/sim/net_puppet_drive_sim.py --population  (pops vs live count / client tick)
"""

import argparse
import math
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


# ---- population mode (card 48ab9b2f) ------------------------------------------------------
# The snapshot cursor round-robins SNAPSHOT_MAX_ENTRIES entries per packet, so an entity's
# correction interval is ceil(N/16)*60ms -- it grows with the live entity count, and between
# turns the puppet dead-reckons on a straight line. So pupPops is partly a function of how BIG
# the world is, with nothing wrong with the link. That is the alternative explanation for the
# JIP pass' pupPops=207/25s, whose host fight was ?flyspiders: the BACKGROUND flying spiders
# have Collides=false, so nothing can kill them and they accumulate for the whole run.
#
# This sweeps N against the REAL background-FlyingSpider motion to find out whether population
# actually accounts for the observed rate, or whether it falls short and something else is going
# on. It is deliberately the honest question, not a confirmation: read the printed verdict.
SWIVEL_PERIOD_MS = 4000.0     # FlyingSpider.Setup: swiveltimer.Duration for isbackground
SWIVEL_BASE_PX = 50.0         # FlyingSpider.Update: 50f * DifficultyModifier * scale
BG_SCALE = 0.67 * 0.75        # Setup: scale = 0.67f * SizeFactor (DefaultSizeFactor 0.75)
DIFFICULTY = {"Easy": 0.35, "Medium": 0.6, "Hard": 0.8, "Very_Hard": 1.0}
OBSERVED_POPS_PER_S = 207 / 25.0   # the JIP pass' reading, for scale

# Horizontal drift, which is where the SECOND candidate explanation lives. Level2.slowdown sets
# the background to Vector2(-3, 0) / 16.667 = -0.18 px/ms, and FlyingSpider.Setup takes
# Speed = |BackgroundSpeed| * 1.11 for the background variant. It is perfectly LINEAR, so it
# costs a well-fed client nothing at all -- but see DRIVER_MAX_DT_MS.
FLYSPIDER_X_SPEED = (3.0 / 16.666666) * 1.11

# NetPuppetDriver.Update clamps its real-time delta to this. The clamp is deliberate (a pause
# Pop or a tab refocus must advance the world by at most one over-long frame, never a fling),
# but it means a client ticking SLOWER than 5Hz silently loses every millisecond past the clamp:
# the puppets under-advance by (gap - 200ms) of real motion on every tick, the error integrates,
# and the next snapshot for that entity snaps. Chrome pauses rAF outright in an occluded window
# and throttles a hidden tab to ~1Hz, which is JIP-pass trap 1.
DRIVER_MAX_DT_MS = 200.0


def swivel_amplitude_px(diff_mod):
    return SWIVEL_BASE_PX * diff_mod * BG_SCALE


class FlySpider:
    """A background FlyingSpider, in 2D this time -- the 1D Enemy above cannot show the effect
    the tick-starvation table needs, because the two axes fail for opposite reasons.

      Y: pure sinusoid, FlyingSpider.Update --
         Position.Y = startheight + 50 * DifficultyModifier * scale * sin(2pi * swivel.Normalized)
         Hard to dead-reckon (a straight line never fits a sine) but SMALL in amplitude.
      X: constant Speed from Setup. Trivially dead-reckoned and free... unless the client's
         driver dt is being clamped, at which point the lost motion is all horizontal.

    observe() mirrors NetSession.CaptureBaseState: a FINITE-DIFFERENCE velocity measured between
    this entity's own snapshot turns, which for a sinusoid is an average over the whole interval
    rather than the instantaneous velocity -- a second reason a long turn interval hurts.
    """

    def __init__(self, amplitude_px, phase_ms, x_speed):
        self.amp = amplitude_px
        self.phase = phase_ms
        self.x_speed = x_speed
        self.t = 0.0
        self.x = 0.0
        self.y = self._y()
        self.have_last = False
        self.last = (0.0, 0.0)
        self.last_ms = 0.0

    def _y(self):
        return self.amp * math.sin(2.0 * math.pi * (self.t + self.phase) / SWIVEL_PERIOD_MS)

    def advance(self, dt_ms):
        self.t += dt_ms
        self.x -= self.x_speed * dt_ms
        self.y = self._y()

    def observe(self, now_ms):
        vx, vy = -self.x_speed, 0.0
        if self.have_last and now_ms > self.last_ms:
            dt = now_ms - self.last_ms
            vx = (self.x - self.last[0]) / dt
            vy = (self.y - self.last[1]) / dt
        self.last = (self.x, self.y)
        self.last_ms = now_ms
        self.have_last = True
        return (self.x, self.y), (vx, vy)


class Puppet2D:
    """Mirror of NetPuppets.ApplySnapshotState + Drive, in 2D. The pop test is on the error
    VECTOR's length, exactly as the C# does (err.Length() > SnapThresholdPx)."""

    def __init__(self, pos):
        self.x, self.y = pos
        self.vx = self.vy = 0.0
        self.cx = self.cy = 0.0
        self.correction_ms_left = 0.0

    def apply_snapshot(self, pos, vel):
        ex, ey = pos[0] - self.x, pos[1] - self.y
        popped = math.hypot(ex, ey) > SNAP_THRESHOLD_PX
        if popped:
            self.x, self.y = pos
            self.cx = self.cy = 0.0
            self.correction_ms_left = 0.0
        else:
            self.cx, self.cy = ex, ey
            self.correction_ms_left = CORRECTION_WINDOW_MS
        self.vx, self.vy = vel
        return popped

    def drive(self, dt_ms):
        sx, sy = self.vx * dt_ms, self.vy * dt_ms
        if self.correction_ms_left > 0.0:
            take = min(dt_ms, self.correction_ms_left)
            sx += self.cx * (take / CORRECTION_WINDOW_MS)
            sy += self.cy * (take / CORRECTION_WINDOW_MS)
            self.correction_ms_left -= take
        self.x += sx
        self.y += sy


def run_population(n_enemies, diff_mod, total_ms=25000.0, client_tick_ms=TICK_MS):
    """Steady state: N live entities, healthy link, no time scaling. `client_tick_ms` is how
    often the CLIENT gets a tick -- the driver's dt is that gap clamped to DRIVER_MAX_DT_MS,
    so anything slower than 5Hz loses real motion every tick. Returns (pops, peak_px).

    STEADY STATE is the point, so the first full round-robin cycle is a WARM-UP that is
    simulated but not scored, and each puppet is seeded with its entity's true velocity. Both
    matter: a puppet that has not had its first turn yet holds still (vel 0) while its entity
    drifts, so with the warm-up scored, every entity pops exactly once on its first turn and a
    big N reads as a huge pop RATE -- an artifact of starting the sim, not of the world being
    big. (Measured while writing this: it invented 36 pops/s at N=1024 and a 769px "peak",
    which is just 3840ms of un-driven X drift.) A real session's puppets arrive via EvSpawn or
    the self-heal, both of which seed position AND velocity together.
    """
    amp = swivel_amplitude_px(diff_mod)
    # Decorrelated phases: swiveltimer.Randomize() gives every spider its own, and a swarm
    # swinging in lockstep would be an unrepresentative best OR worst case.
    enemies = [FlySpider(amp, SWIVEL_PERIOD_MS * i / max(1, n_enemies), FLYSPIDER_X_SPEED)
               for i in range(n_enemies)]
    puppets = []
    for e in enemies:
        p = Puppet2D((e.x, e.y))
        p.vx = -e.x_speed
        p.vy = (e.amp * 2.0 * math.pi / SWIVEL_PERIOD_MS
                * math.cos(2.0 * math.pi * e.phase / SWIVEL_PERIOD_MS))
        puppets.append(p)
    cursor = 0
    next_snap = SNAPSHOT_INTERVAL_MS
    next_client_tick = client_tick_ms
    pops = 0
    peak = 0.0
    t = 0.0
    warmup_ms = snap_turn_ms(n_enemies) + SNAPSHOT_INTERVAL_MS
    # The host keeps its own real-time pace whatever the client is doing -- step it finely so a
    # slow client tick is modelled as the client missing frames, not as time itself stopping.
    while t < warmup_ms + total_ms:
        scoring = t >= warmup_ms
        for e in enemies:
            e.advance(TICK_MS)
        if scoring:
            # Sample the error on the HOST's clock, not the client's. Sampling it only on client
            # ticks undersamples exactly the runs that matter: at 1Hz the error is read once a
            # second, long after the intervening snapshots have already snapped it back to zero,
            # so a badly diverging run reports a reassuringly small peak.
            for i, p in enumerate(puppets):
                peak = max(peak, math.hypot(enemies[i].x - p.x, enemies[i].y - p.y))
        if t >= next_snap:
            for _ in range(min(SNAPSHOT_MAX_ENTRIES, len(enemies))):
                idx = cursor % len(enemies)
                cursor = (cursor + 1) % len(enemies)
                snap_pos, snap_vel = enemies[idx].observe(t)
                if puppets[idx].apply_snapshot(snap_pos, snap_vel) and scoring:
                    pops += 1
            next_snap += SNAPSHOT_INTERVAL_MS
        if t >= next_client_tick:
            dt = min(client_tick_ms, DRIVER_MAX_DT_MS)
            for p in puppets:
                p.drive(dt)
            next_client_tick += client_tick_ms
        t += TICK_MS
    return pops, peak


def snap_turn_ms(n):
    """NetSession.SnapshotTurnMs -- pinned C#-side by eaNetSnap()."""
    if n <= 0:
        return 0
    return -(-n // SNAPSHOT_MAX_ENTRIES) * int(SNAPSHOT_INTERVAL_MS)


def population():
    secs = 25.0
    print("Does world POPULATION alone explain the JIP pass' pupPops?\n")
    print(f"Background FlyingSpider swivel: +/-{swivel_amplitude_px(1.0):.1f}px "
          f"(Very_Hard) over {SWIVEL_PERIOD_MS:.0f}ms; X is linear and contributes nothing.")
    print(f"Snap threshold {SNAP_THRESHOLD_PX:.0f}px, correction window {CORRECTION_WINDOW_MS:.0f}ms, "
          f"round robin {SNAPSHOT_MAX_ENTRIES}/{SNAPSHOT_INTERVAL_MS:.0f}ms.")
    print(f"Reference: the pass logged {OBSERVED_POPS_PER_S:.1f} pops/s (207 over {secs:.0f}s).\n")

    counts = (16, 32, 64, 128, 256, 512, 1024, 2048)
    header = f"{'N live':>7} {'snapTurn':>9} " + "".join(f"{d:>12}" for d in DIFFICULTY)
    print("A. POPULATION, with the client ticking normally (60Hz):\n")
    print(header)
    print("-" * len(header))
    best_pop = 0.0
    for n in counts:
        row = [f"{n:>7} {snap_turn_ms(n):>7}ms "]
        for diff in DIFFICULTY.values():
            pops, _ = run_population(n, diff, total_ms=secs * 1000.0)
            best_pop = max(best_pop, pops / secs)
            row.append(f"{pops / secs:>10.1f}/s")
        print("".join(row))
    print("\npeak dead-reckoning error, Very_Hard: " + ", ".join(
        f"N={n}: {run_population(n, 1.0, total_ms=secs * 1000.0)[1]:.0f}px" for n in (64, 256, 1024)))
    print(f"\n  -> worst cell {best_pop:.1f} pops/s vs the pass' {OBSERVED_POPS_PER_S:.1f}/s. "
          + ("REACHED." if best_pop >= OBSERVED_POPS_PER_S else "NOT reached."))
    print("     The swivel is only +/-%.0fpx, so a straight-line prediction rarely gets 100px\n"
          "     wrong however long the turn interval grows; the lone hot cell is a resonance\n"
          "     (turn ~= half the swivel period, where the finite-difference velocity is most\n"
          "     wrong). X drift costs a well-fed client nothing -- it is exactly linear."
          % swivel_amplitude_px(1.0))

    # B. The other candidate, and the one the JIP rig actually risks: a starved client.
    print("\n\nB. CLIENT TICK STARVATION, at a fixed N=128 (snapTurn %dms), Very_Hard:\n"
          % snap_turn_ms(128))
    rates = ((60, TICK_MS), (40, 25.0), (30, 33.3), (10, 100.0), (5, 200.0), (3, 333.0), (1, 1000.0))
    header = f"{'client':>8} {'tick gap':>10} {'driver dt':>11} {'lost/tick':>11} {'pops':>10} {'peak px':>9}"
    print(header)
    print("-" * len(header))
    best_starve = 0.0
    for hz, gap in rates:
        pops, peak = run_population(128, 1.0, total_ms=secs * 1000.0, client_tick_ms=gap)
        best_starve = max(best_starve, pops / secs)
        lost = max(0.0, gap - DRIVER_MAX_DT_MS)
        print(f"{hz:>6}Hz {gap:>8.0f}ms {min(gap, DRIVER_MAX_DT_MS):>9.0f}ms "
              f"{lost:>9.0f}ms {pops / secs:>8.1f}/s {peak:>8.0f}")

    print(f"\nVERDICT vs the pass' {OBSERVED_POPS_PER_S:.1f} pops/s over {secs:.0f}s:")
    print(f"  population alone   : up to {best_pop:.1f}/s  "
          + ("(sufficient)" if best_pop >= OBSERVED_POPS_PER_S else "(insufficient)"))
    print(f"  tick starvation    : up to {best_starve:.1f}/s  "
          + ("(sufficient)" if best_starve >= OBSERVED_POPS_PER_S else "(insufficient)"))
    print("\nA steady 40fps is NOT enough on its own -- the driver's dt clamp only bites below\n"
          "5Hz, which is what an OCCLUDED or hidden window does (rAF paused / ~1Hz timers,\n"
          "JIP-pass trap 1), not what a merely slow one does. So neither the swarm nor the\n"
          "frame rate the pass recorded explains its reading by itself, and the remaining\n"
          "candidates -- intermittent occlusion, id churn, a genuine fault -- are separated by\n"
          "the snapTurn + snapNew/snapDead/snapBad fields now in the [net] line, not by\n"
          "another undecidable two-window run.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sweep", action="store_true", help="pops vs window depth/length (eyeball)")
    ap.add_argument("--population", action="store_true",
                    help="pops vs live entity count (card 48ab9b2f: is pupPops a swarm artifact?)")
    args = ap.parse_args()
    if args.sweep:
        sweep()
        return 0
    if args.population:
        population()
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
