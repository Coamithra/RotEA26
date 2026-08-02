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
essentially cannot -- 0 pops/s at every size swept except ONE cell, N=512, where the turn
interval lands near half the spiders' 4000ms swivel period and a finite-difference velocity is
maximally wrong. That resonance is real and worth knowing about (7.2/s at Very_Hard, 92.6/s at
Inzane's 20%-larger swivel), but it is not what the rig hit: a live boot measures that world at
17-19 entities, three orders of N away, where the answer is a flat zero. Meanwhile client TICK
STARVATION can produce the observed rate and then some. The starvation cliff is between 3Hz and 1Hz: the 200ms dt
clamp in NetPuppetDriver starts DROPPING real motion at 3Hz (133ms lost per tick) but still pops
nothing, and by 1Hz it is 128 pops/s. That is an occluded/hidden window (JIP trap 1), not the
~40fps the pass recorded -- 40Hz is nowhere near it. So neither of the pass' own hypotheses
survives, which is why the card put its effort into making the counters decidable instead.

The swarm hypothesis fails twice over, in fact: the sweep below shows it popping nothing at ANY
size, and a live `?level=Level2&flyspiders&net=host` boot measures the rig's actual world at
only 17-19 live entities (`snapTurn=120ms`) -- they spawn at 5.5/s but die off-screen at
`Position.X < -100`, so it reaches a small steady state rather than accumulating. The table
still sweeps to N=2048 because the question "how big would a world have to be" is worth an
answer, not because that rig ever got there.

--hoststall (card 68f62e92) asks the model's RECIPROCAL question, and it is the one the
assert mode above cannot: what happens when the HOST's world stops and the client is perfectly
healthy? A player death arms `Juice.AddHitStop(0.18f)` and `Game1.UpdateScaled` folds
`TimeScale` into the gameTime every component gets, so the dying peer's WHOLE world halts --
while `NetSession.Update` sits outside that scaled path and keeps streaming snapshots of the
frozen positions on the real clock. The other peer's puppets keep dead-reckoning forward (as
they must -- see above), so the corrections that follow walk every replicated enemy BACKWARD
at once, over a background that never stopped scrolling. That is the "when P1 dies, the whole
game rewinds a bit" report, and the fix is on the OTHER side of this model: no hit-stop while a
session is active, so the host never stalls. Measured: 23 px of backward glide at a mid-sized
world (N=64) and a typical 0.15 px/ms enemy, 45 px for a fast diver, against a stall=0 control
that never steps backward at all. It saturates in stall length but scales with POPULATION,
because the round robin corrects a small world several times inside a 180 ms freeze.

Run:  python tools/sim/net_puppet_drive_sim.py               (assert mode; exit 0 = fix holds)
      python tools/sim/net_puppet_drive_sim.py --sweep       (pops vs window depth/length)
      python tools/sim/net_puppet_drive_sim.py --population  (pops vs live count / client tick)
      python tools/sim/net_puppet_drive_sim.py --hoststall   (card 68f62e92: the HOST stalling)
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
# correction interval averages N/16*60ms -- it grows with the live entity count, and between
# turns the puppet dead-reckons on a straight line. So pupPops COULD be partly a function of how
# BIG the world is, with nothing wrong with the link. That was the JIP pass' own explanation for
# its pupPops=207/25s: its host fight was ?flyspiders, whose BACKGROUND spiders have
# Collides=false and so cannot be shot down.
#
# This sweeps N against the REAL background-FlyingSpider motion to find out whether population
# actually accounts for the observed rate, or whether it falls short and something else is going
# on. It is deliberately the honest question, not a confirmation: read the printed verdict.
# (It falls short -- and note that "cannot be shot down" does not mean unbounded either: they
# die off-screen at Position.X < -100, and a live boot measures that rig at 17-19 entities.)
SWIVEL_PERIOD_MS = 4000.0     # FlyingSpider.Setup: swiveltimer.Duration for isbackground
SWIVEL_BASE_PX = 50.0         # FlyingSpider.Update: 50f * DifficultyModifier * scale
BG_SCALE = 0.67 * 0.75        # Setup: scale = 0.67f * SizeFactor (DefaultSizeFactor 0.75)
# Settings.GetDifficultyValue. Inzane is included because it has the largest swivel amplitude
# and so is the tier most likely to widen the one resonance cell that does pop.
DIFFICULTY = {"Easy": 0.35, "Medium": 0.6, "Hard": 0.8, "Very_Hard": 1.0, "Inzane": 1.2}
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
    """NetSession.SnapshotTurnMs -- the MEAN gap between an entity's turns, pinned C#-side by
    eaNetSnap(). Mean, not whole packets rounded up: the cursor wraps continuously, so a
    17-entity world averages ~63ms rather than the 120ms a second whole packet would imply."""
    if n <= 0:
        return 0
    return max(int(SNAPSHOT_INTERVAL_MS), n * int(SNAPSHOT_INTERVAL_MS) // SNAPSHOT_MAX_ENTRIES)


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
    print(f"\n  -> worst cell {best_pop:.1f} pops/s vs the pass' {OBSERVED_POPS_PER_S:.1f}/s.")
    print("     Note the shape: this is NOT monotone in N. Every cell is a flat 0 except the\n"
          "     N=512 column, where snapTurn (%dms) lands near HALF the %.0fms swivel period --\n"
          "     the phase at which a finite-difference velocity measured across the interval is\n"
          "     most wrong about where the entity goes next. Off that resonance the swivel is\n"
          "     only +/-%.0fpx (Very_Hard), too small to miss by 100px however long the turn\n"
          "     grows, and the X drift is exactly linear so it costs a well-fed client nothing.\n"
          "     Amplitude decides how hard the resonance bites: Very_Hard 7.2/s, Inzane 92.6/s\n"
          "     for a 20%% bigger swivel. So a long snapTurn is dangerous for PERIODIC motion\n"
          "     whose period it happens to straddle -- not for big worlds as such."
          % (snap_turn_ms(512), SWIVEL_PERIOD_MS, swivel_amplitude_px(1.0)))

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

    # Deliberately an order-of-magnitude bracket, not a `>=` threshold. A mechanism that peaks
    # at 87% of the observed rate in ONE resonance cell has not been cleanly excluded (the
    # repo's own rule is that differences under ~30% are noise), and one that overshoots 15x
    # has not been cleanly confirmed either -- it only says the mechanism is capable.
    def bracket(best):
        if best >= 3.0 * OBSERVED_POPS_PER_S:
            return "AMPLY capable"
        if best >= 0.5 * OBSERVED_POPS_PER_S:
            return "borderline -- same order, cannot be cleanly excluded"
        return "cannot get near it"

    print(f"\nVERDICT vs the pass' {OBSERVED_POPS_PER_S:.1f} pops/s over {secs:.0f}s:")
    print(f"  population alone   : peaks at {best_pop:>6.1f}/s  -- {bracket(best_pop)}")
    print(f"  tick starvation    : peaks at {best_starve:>6.1f}/s  -- {bracket(best_starve)}")
    print("\nRead those carefully. Population only bites in the single N=512 resonance cell and\n"
          "sits at a flat 0 everywhere else, including the 17-19 entities a live boot of that rig\n"
          "actually measures -- so it is not the explanation there, but a turn interval that\n"
          "straddles a periodic motion's half-period is a mechanism worth remembering.\n"
          "Starvation, meanwhile,\n"
          "is all-or-nothing: the cliff sits between 3Hz and 1Hz, and note that 3Hz is ALREADY\n"
          "losing 133ms of motion per tick to the clamp while still popping nothing. So a\n"
          "steady 40fps -- what the pass recorded -- is nowhere near it; only an OCCLUDED or\n"
          "hidden window gets there (rAF paused / ~1Hz timers, JIP-pass trap 1).\n"
          "Neither of the pass' hypotheses explains its reading, and the remaining candidates\n"
          "-- intermittent occlusion, id churn, a genuine fault -- are separated by the\n"
          "snapTurn + snapNew/snapDead/snapBad fields now in the [net] line, not by another\n"
          "undecidable two-window run.")



# ---- smoothness (cards 0dfc4495 / d3add86f / 8dabe812) ---------------------------------------
#
# A DIFFERENT question from pops. `--population` above asks "does the puppet diverge far enough to
# SNAP", and answers "essentially never on a healthy client". But the cards are not about snapping,
# they are about the motion being visibly ROUGH while never once crossing the 100px threshold --
# stutter, not teleporting. So this mode measures the SHAPE of the motion rather than its error:
#
#   jerk    = stddev of |step_n| - |step_n-1| over the run, i.e. how much the per-tick distance
#             keeps CHANGING. Smooth motion holds a near-constant step; the host control reads
#             ~0.0008 px/tick^2 and is the yardstick every client row is read against.
#   maxstep = the worst single-tick distance, which is what a lurch looks like.
#
# TWO FINDINGS, and the second one refuted the fix that was proposed first.
#
# 1. THE CORRECTION WINDOW WAS FIXED WHILE THE BLIND WINDOW WAS NOT. NetPuppets blended every
#    snapshot error over a constant 150ms, but an entity is only corrected every SnapshotTurnMs,
#    which scales with the world (60ms at 16 live entities, 480ms at 128). Draining faster than
#    corrections arrive means the puppet spends most of its life on a stale dead-reckon and then
#    lurches when the next one lands. Scaling the window to 2x the turn flattens the jerk across
#    every world size instead of letting it degrade 3.7x.
#
# 2. AN EXPONENTIAL / CRITICALLY-DAMPED DRAIN IS WORSE, AT EVERY SIZE. It was the obvious
#    alternative (no reset discontinuity when a new correction replaces an old one) and it loses
#    outright, because its tail keeps a velocity offset alive to be re-hit by the next correction.
#    Do not re-propose it without re-running this.
#
# The teleport section is card 8dabe812 and is the sharpest result here: a reposition
# differentiated as velocity (NetSession.CaptureBaseState) is dead-reckoned by the client at
# teleport speed until its next turn, and puppets are collidable, so the boss crosses the screen
# and kills the local player. The guard lives in CaptureBaseState; its CAP is measured separately
# and in the REAL GAME by eaNetVelScan, not here -- this only shows what an unguarded sample does.


class SmoothPuppet:
    """NetPuppets.Drive + ApplySnapshotState, with the correction window as a parameter."""

    def __init__(self, pos, window_ms, exponential=False):
        self.pos = pos
        self.vel = (0.0, 0.0)
        self.corr = (0.0, 0.0)
        self.corr_left = 0.0
        self.window = window_ms
        self.exponential = exponential
        self.has = False
        self.pops = 0

    def apply_snapshot(self, pos, vel):
        if not self.has:
            self.pos = pos
            self.has = True
        else:
            err = (pos[0] - self.pos[0], pos[1] - self.pos[1])
            if math.hypot(*err) > SNAP_THRESHOLD_PX:
                self.pos = pos
                self.corr = (0.0, 0.0)
                self.corr_left = 0.0
                self.pops += 1
            else:
                self.corr = err
                self.corr_left = self.window
        self.vel = vel

    def drive(self, dt_ms):
        sx = self.vel[0] * dt_ms
        sy = self.vel[1] * dt_ms
        if self.exponential:
            k = 1.0 - math.exp(-dt_ms / (self.window / 3.0))
            dx, dy = self.corr[0] * k, self.corr[1] * k
            sx += dx
            sy += dy
            self.corr = (self.corr[0] - dx, self.corr[1] - dy)
        elif self.corr_left > 0.0:
            take = min(dt_ms, self.corr_left)
            sx += self.corr[0] * (take / self.window)
            sy += self.corr[1] * (take / self.window)
            self.corr_left -= take
        self.pos = (self.pos[0] + sx, self.pos[1] + sy)
        return math.hypot(sx, sy)


def _stddev_of_deltas(series):
    if len(series) < 2:
        return 0.0
    d = [abs(series[i] - series[i - 1]) for i in range(1, len(series))]
    mean = sum(d) / len(d)
    return math.sqrt(sum((x - mean) ** 2 for x in d) / len(d))


def _flyspider_truth(t_ms, teleport_at=None, teleport_dx=0.0):
    """FlyingSpider-shaped: linear X drift plus the ~4s +-25px vertical swivel."""
    jump = teleport_dx if (teleport_at is not None and t_ms >= teleport_at) else 0.0
    return (-0.12 * t_ms + jump, 300.0 + 25.0 * math.sin(2 * math.pi * t_ms / 4000.0))


def run_smoothness(n_live, window_mode, total_ms=20000.0, exponential=False,
                   teleport_at=None, teleport_dx=0.0, vel_guard=None,
                   declared_vel=(-0.12, 0.0)):
    turn = snap_turn_ms(n_live)
    window = {"fixed150": CORRECTION_WINDOW_MS,
              "2xturn": max(CORRECTION_WINDOW_MS, 2.0 * turn)}[window_mode]
    pup = SmoothPuppet(_flyspider_truth(0.0), window, exponential)
    last_pos, last_ms, has_last = _flyspider_truth(0.0), 0.0, False
    next_turn, t = turn, 0.0
    steps, host_steps = [], []
    prev_host = _flyspider_truth(0.0)
    while t < total_ms:
        t += TICK_MS
        truth = _flyspider_truth(t, teleport_at, teleport_dx)
        if t >= next_turn:
            next_turn += turn
            vel = (0.0, 0.0)
            if has_last and t > last_ms:
                vx = (truth[0] - last_pos[0]) / (t - last_ms)
                vy = (truth[1] - last_pos[1]) / (t - last_ms)
                # NetSession.MaxObservedSpeedPxPerMs -- the teleport guard. Production does NOT
                # zero the velocity on a refusal, it falls back to the entity's DECLARED
                # NetSpeedVector, which is the honest answer (it describes what the entity will do
                # next). Modelling that rather than zero matters: zero would be strictly better
                # than what ships, and the assertion below would then be measured against a
                # fallback the game does not have.
                if vel_guard is not None and math.hypot(vx, vy) > vel_guard:
                    vx, vy = declared_vel
                vel = (vx, vy)
            last_pos, last_ms, has_last = truth, t, True
            pup.apply_snapshot(truth, vel)
        steps.append(pup.drive(TICK_MS))
        host_steps.append(math.hypot(truth[0] - prev_host[0], truth[1] - prev_host[1]))
        prev_host = truth
    return {
        "turn": turn, "window": window, "pops": pup.pops,
        "jerk": _stddev_of_deltas(steps), "maxstep": max(steps),
        "host_jerk": _stddev_of_deltas(host_steps), "host_maxstep": max(host_steps),
    }


def smoothness():
    print("SMOOTHNESS -- FlyingSpider-shaped motion, client puppet vs host truth")
    print("jerk = stddev of successive per-tick step deltas (px/tick^2); lower is smoother.\n")
    ctrl = run_smoothness(16, "fixed150")
    print("  host truth (the yardstick):  jerk %.4f  maxstep %.2f px\n"
          % (ctrl["host_jerk"], ctrl["host_maxstep"]))
    print("  %-5s %-8s %-10s %-10s %-10s" % ("N", "turn", "fixed150", "2xturn", "exp(150)"))
    fails = []
    for n in (16, 32, 64, 128):
        a = run_smoothness(n, "fixed150")
        b = run_smoothness(n, "2xturn")
        c = run_smoothness(n, "fixed150", exponential=True)
        print("  %-5d %-8.0f %-10.4f %-10.4f %-10.4f"
              % (n, a["turn"], a["jerk"], b["jerk"], c["jerk"]))
        # The shipped fix must be clearly better once the turn stretches past the 150ms floor.
        if n >= 32 and not b["jerk"] < a["jerk"] * 0.9:
            fails.append("N=%d: 2xturn (%.4f) is not clearly better than fixed150 (%.4f)"
                         % (n, b["jerk"], a["jerk"]))
        if not c["jerk"] > a["jerk"]:
            fails.append("N=%d: the exponential drain (%.4f) was expected to be WORSE than "
                         "fixed150 (%.4f) -- re-read the finding before changing the drain"
                         % (n, c["jerk"], a["jerk"]))
    print("\n  (2xturn is flat in N; fixed150 degrades with the world; the exponential drain is"
          "\n   worse than both at every size -- it was proposed first and measured out.)")

    print("\nTELEPORT (card 8dabe812) -- an 800px reposition differentiated as velocity")
    un = run_smoothness(24, "2xturn", total_ms=6000.0, teleport_at=3000.0, teleport_dx=800.0)
    gu = run_smoothness(24, "2xturn", total_ms=6000.0, teleport_at=3000.0, teleport_dx=800.0,
                        vel_guard=5.0)
    print("  unguarded: maxstep %.1f px/tick  pops %d" % (un["maxstep"], un["pops"]))
    print("  guarded  : maxstep %.1f px/tick  pops %d" % (gu["maxstep"], gu["pops"]))
    if not gu["maxstep"] < un["maxstep"] * 0.25:
        fails.append("the teleport guard did not cut the client's peak step by at least 4x "
                     "(%.1f -> %.1f)" % (un["maxstep"], gu["maxstep"]))
    # NEGATIVE LEG: the teleport must still be CORRECTED. A guard that swallowed the reposition
    # would leave the puppet in the wrong place, which is worse than the lurch it removes.
    if gu["pops"] < 1:
        fails.append("the guarded run never popped -- the reposition must still snap the puppet "
                     "to the host's position; a guard that hides it is not a fix")
    print("  (the guarded run still POPS: the reposition is applied as a snap, which is correct."
          "\n   It is only the VELOCITY that is refused, so the puppet stops being flung onward.)")

    if fails:
        print("\nFAIL:")
        for f in fails:
            print("  - " + f)
        return False
    print("\nOK: window scaling holds, the exponential alternative stays refuted, and the"
          "\n    teleport guard removes the fling while keeping the correction.")
    return True

def run_host_stall(stall_ms, n_enemies=16, speed=0.15, total_ms=6000.0):
    """THE RECIPROCAL of the run() above (card 68f62e92), and the whole point is which SIDE
    stops.

    run() models the CLIENT's clock being scaled while the host runs on real time -- the case
    the real-time driver fixed. This models the HOST's world halting for `stall_ms` while the
    client is perfectly healthy: a player death on the host arms Juice.AddHitStop(0.18f), and
    Game1.UpdateScaled folds TimeScale into the gameTime UpdateInner hands every component, so
    the host's whole world stops advancing. NetSession.Update is NOT in that scaled path -- it
    runs on TickCount64 -- so snapshots keep flowing at full cadence carrying the FROZEN
    positions, while the client's puppets keep dead-reckoning forward on real time exactly as
    designed.

    The puppet therefore runs ahead by vel * (stall + that entity's blind window), and the
    corrections that follow walk it BACKWARD. That backward motion is the report: "when P1
    dies, the whole game rewinds a bit" -- every replicated enemy at once, over a background
    that never stopped scrolling (the client scrolls its own).

    Returns (max_backward_px, overshoot_px, pops). max_backward_px is the largest single-frame
    backward step summed into a contiguous backward run, i.e. what the eye sees as a rewind.
    """
    enemies = [Enemy(pos=100.0 + 25.0 * i, vel=speed) for i in range(n_enemies)]
    puppets = [Puppet(pos=e.pos) for e in enemies]
    cursor = 0
    next_snap = SNAPSHOT_INTERVAL_MS
    stall_start = 2000.0          # steady state first, so nothing here is a spawn transient
    pops = 0
    overshoot = 0.0
    back_run = [0.0] * n_enemies  # px travelled backward in the current contiguous run
    max_back = 0.0
    t = 0.0
    while t < total_ms:
        frozen = stall_start <= t < stall_start + stall_ms
        if not frozen:
            for e in enemies:
                e.advance(TICK_MS)
        if t >= next_snap:
            # The host keeps SENDING through its own freeze -- that is the mechanism.
            count = min(SNAPSHOT_MAX_ENTRIES, len(enemies))
            for _ in range(count):
                idx = cursor % len(enemies)
                cursor = (cursor + 1) % len(enemies)
                snap_pos, snap_vel = enemies[idx].observe(t)
                if puppets[idx].apply_snapshot(snap_pos, snap_vel):
                    pops += 1
            next_snap += SNAPSHOT_INTERVAL_MS
        for i, p in enumerate(puppets):
            before = p.pos
            p.drive(TICK_MS)      # the client is healthy: real dt, always
            step = p.pos - before
            if step < 0.0:
                back_run[i] -= step
                max_back = max(max_back, back_run[i])
            else:
                back_run[i] = 0.0
            overshoot = max(overshoot, p.pos - enemies[i].pos)
        t += TICK_MS
    return max_back, overshoot, pops


def host_stall():
    print("Card 68f62e92 -- what does a HOST-side hit-stop do to the other peer's world?\n")
    print("The client is healthy throughout; only the HOST's world stops advancing, while its")
    print("snapshots keep flowing on the real clock. Enemies move at a constant speed, so every")
    print("px of backward motion below is the correction blend undoing dead reckoning the host")
    print("never earned -- there is no real reversal in the model to confuse it with.\n")
    print(f"Snapshot round robin {SNAPSHOT_MAX_ENTRIES}/{SNAPSHOT_INTERVAL_MS:.0f}ms, "
          f"correction window {CORRECTION_WINDOW_MS:.0f}ms, snap threshold {SNAP_THRESHOLD_PX:.0f}px.\n")

    speeds = (0.08, 0.15, 0.30)   # px/ms: a drifting enemy, a typical UFO, a fast diver
    print("A. AT A SMALL WORLD (N=16, snapTurn at its 60ms floor), vs stall length:\n")
    header = f"{'stall ms':>9} " + "".join(f"{('v=' + str(s)):>22}" for s in speeds)
    print(header)
    print(f"{'':>9} " + "".join(f"{'rewind px / ahead px':>22}" for _ in speeds))
    print("-" * len(header))
    for stall in (0.0, 60.0, 120.0, HITSTOP_MS, 360.0):
        row = f"{stall:>9.0f} "
        for sp in speeds:
            back, ahead, _ = run_host_stall(stall, speed=sp)
            row += f"{(f'{back:8.1f} / {ahead:8.1f}'):>22}"
        print(row)
    print("\nThe rewind SATURATES down that column, and the reason is the round robin: at N=16")
    print("every entity gets a turn every 60ms, so it is corrected 3 times INSIDE a 180ms")
    print("freeze and can only ever be one turn's worth of dead reckoning ahead. A longer")
    print("freeze buys no more error -- which is exactly why N is the variable that matters.\n")

    print("B. AT THE SHIPPED 180ms DEATH HIT-STOP, vs world POPULATION:\n")
    counts = (16, 32, 64, 128, 256)
    header = f"{'N live':>7} {'snapTurn':>9} " + "".join(f"{('v=' + str(s)):>22}" for s in speeds)
    print(header)
    print(f"{'':>7} {'':>9} " + "".join(f"{'rewind px / ahead px':>22}" for _ in speeds))
    print("-" * len(header))
    for n in counts:
        row = f"{n:>7} {snap_turn_ms(n):>9} "
        for sp in speeds:
            back, ahead, _ = run_host_stall(HITSTOP_MS, n_enemies=n, speed=sp)
            row += f"{(f'{back:8.1f} / {ahead:8.1f}'):>22}"
        print(row)
    back, ahead, pops = run_host_stall(HITSTOP_MS, n_enemies=64, speed=0.15)
    print(f"\nAt a mid-sized world (N=64, snapTurn {snap_turn_ms(64)}ms) and a typical 0.15 px/ms enemy:")
    print(f"the puppet runs {ahead:.1f}px AHEAD of the host's frozen truth and is then walked")
    print(f"{back:.1f}px BACKWARD over the {CORRECTION_WINDOW_MS:.0f}ms blend -- {pops} hard pops, so it is a")
    print("visible GLIDE the wrong way rather than a teleport, which is how the report")
    print("describes it. The stall=0 row in A is the control: a healthy pair never steps")
    print("backward at all, at any speed.")
    print("\nEvery replicated enemy does this at once, and the client's background keeps")
    print("scrolling throughout -- hence 'the whole game rewinds a bit'.")
    print("\nTHE FIX IS ON THE OTHER SIDE OF THIS MODEL: suppress the hit-stop while a session")
    print("is active (Juice.AddHitStop), so the host's world never stops and no row below the")
    print("control is ever reached. Nothing here changes the client's dead reckoning, which is")
    print("correct as it stands -- see run() above for why it must stay on real time.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sweep", action="store_true", help="pops vs window depth/length (eyeball)")
    ap.add_argument("--population", action="store_true",
                    help="pops vs live entity count (card 48ab9b2f: is pupPops a swarm artifact?)")
    ap.add_argument("--smoothness", action="store_true",
                    help="jerk/maxstep vs correction window + the teleport guard "
                         "(cards 0dfc4495 / d3add86f / 8dabe812)")

    ap.add_argument("--hoststall", action="store_true",
                    help="card 68f62e92: how far a HOST-side hit-stop rewinds the peer's world")
    args = ap.parse_args()
    if args.sweep:
        sweep()
        return 0
    if args.population:
        population()
        return 0
    if args.smoothness:
        return 0 if smoothness() else 1

    if args.hoststall:
        host_stall()
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
