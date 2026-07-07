#!/usr/bin/env python
"""Isolation sim of WebcamMothership's choreography (F1) — mirrors the pure PoseAt + phase
timeline + geometry in web/EvilAliensWeb/Game/EvilAliens/WebcamMothership.cs so the MOVEMENT is
verified as DATA (trajectory over time), not screenshot-timed frames.

Keep the constants below in sync with WebcamMothership.cs. Run:
  python tools/sim/webcam_mothership_sim.py
Prints, per case, the position/beam trajectory sampled over the whole choreography + explicit
invariant checks (entrance horizontal, vertical beam sits at its chosen third, sideways ship
shows ~40%, exits pass/retreat the right way).
"""
import math

# --- constants (mirror WebcamMothership.cs) ---
EnterMs, WindupMs, BeamSweepMs, BeamHoldMs, LeaveMs = 1400, 1800, 500, 1300, 1200
FireMs = BeamSweepMs + BeamHoldMs
ChargeStart, FireStart, FireEnd, LeaveEnd = EnterMs, EnterMs + WindupMs, EnterMs + WindupMs + FireMs, EnterMs + WindupMs + FireMs + LeaveMs

FullSpan, FireLead = 900.0, 55.0
VertRestY, BisectY, OffLeft, OffRight = 5.0, 200.0, -280.0, 1080.0
ArtHalfWidth, HorizVisibleFrac = 208.0, 0.40
ThirdLeft, ThirdRight = 133.0, 667.0
HORIZ_PEEK_X = HorizVisibleFrac * (2 * ArtHalfWidth) - ArtHalfWidth   # ~ -41.6


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def pose_at(es, rp, xp, t):                       # mirror of WebcamMothership.PoseAt
    if t <= ChargeStart:
        p = max(0.0, min(1.0, t / EnterMs))
        return lerp(es, rp, 1.0 - (1.0 - p) * (1.0 - p))
    if t < FireEnd:
        return rp
    q = max(0.0, min(1.0, (t - FireEnd) / LeaveMs))
    return lerp(rp, xp, q * q)


def phase_at(t):
    if t < ChargeStart: return "enter"
    if t < FireStart:   return "charge"
    if t < FireEnd:     return "fire"
    if t < LeaveEnd:    return "leave"
    return "done"


def geometry(orientation, enter_left=True, bx=400.0):
    if orientation == "VerticalDown":
        es = (OffLeft if enter_left else OffRight, VertRestY)
        rp = (bx, VertRestY)
        xp = (OffRight if enter_left else OffLeft, VertRestY)
        fd = math.pi / 2
    elif orientation == "HorizontalFromLeft":
        es, rp, xp, fd = (OffLeft, BisectY), (HORIZ_PEEK_X, BisectY), (OffLeft, BisectY), 0.0
    else:  # HorizontalFromRight
        es, rp, xp, fd = (OffRight, BisectY), (800.0 - HORIZ_PEEK_X, BisectY), (OffRight, BisectY), math.pi
    return es, rp, xp, fd


def beam_origin(rp, fd):
    return (rp[0] + FireLead * math.cos(fd), rp[1] + FireLead * math.sin(fd))


def run(title, orientation, enter_left=True, bx=400.0):
    es, rp, xp, fd = geometry(orientation, enter_left, bx)
    bo = beam_origin(rp, fd)
    print("\n=== %s ===" % title)
    print("  enterStart=%s rest=%s exit=%s beamOrigin=(%.1f,%.1f)" % (fmt(es), fmt(rp), fmt(xp), bo[0], bo[1]))
    entrance_ys, positions = [], []
    for t in range(0, int(LeaveEnd) + 1, 200):
        x, y = pose_at(es, rp, xp, float(t))
        positions.append((t, phase_at(float(t)), x, y))
        if t <= ChargeStart:
            entrance_ys.append(y)
    # compact: show enter end, rest, fire start, exit
    for t, ph, x, y in positions:
        if t in (0, int(ChargeStart), int(FireStart), int(FireEnd), int(LeaveEnd)) or t == 3400:
            print("   t=%-5d %-7s (%.1f, %.1f)" % (t, ph, x, y))
    check(orientation, es, rp, xp, fd, bo, entrance_ys, bx)


def fmt(p): return "(%.0f,%.0f)" % (p[0], p[1])


def check(orientation, es, rp, xp, fd, bo, entrance_ys, bx):
    ok = True
    horiz = max(entrance_ys) - min(entrance_ys) < 0.01
    ok &= horiz
    print("   [%s] entrance horizontal (y const during slide-in)" % P(horiz))
    if orientation == "VerticalDown":
        at_third = abs(rp[0] - bx) < 0.01 and abs(bo[0] - bx) < 0.5   # beam sits at the chosen x
        passes = (es[0] < 0) != (xp[0] < 0)
        ok &= at_third and passes
        print("   [%s] beam at x=%.0f (rest.x=%.1f beamOrigin.x=%.1f) ; [%s] passes out far side"
              % (P(at_third), bx, rp[0], bo[0], P(passes)))
    else:
        # visible fraction of the art past the near edge
        if orientation == "HorizontalFromLeft":
            visible = rp[0] + ArtHalfWidth
        else:
            visible = 800.0 - (rp[0] - ArtHalfWidth)
        frac = visible / (2 * ArtHalfWidth)
        good = abs(frac - HorizVisibleFrac) < 0.02
        retreats = (es[0] == xp[0])
        ok &= good and retreats
        print("   [%s] shows %.0f%% on screen (target %.0f%%) ; [%s] retreats back the way it came"
              % (P(good), frac * 100, HorizVisibleFrac * 100, P(retreats)))
    print("   => %s" % ("ALL PASS" if ok else "*** CHECK FAILED ***"))


def P(b): return "PASS" if b else "FAIL"


if __name__ == "__main__":
    print("WebcamMothership sim — charge@%d fire@%d fireEnd@%d gone@%d ; horizPeekX=%.1f"
          % (ChargeStart, FireStart, FireEnd, LeaveEnd, HORIZ_PEEK_X))
    run("VerticalDown middle (x=400)", "VerticalDown", enter_left=True, bx=400.0)
    run("VerticalDown left third (x=133)", "VerticalDown", enter_left=False, bx=ThirdLeft)
    run("VerticalDown right third (x=667)", "VerticalDown", enter_left=True, bx=ThirdRight)
    run("HorizontalFromLeft", "HorizontalFromLeft")
    run("HorizontalFromRight", "HorizontalFromRight")
