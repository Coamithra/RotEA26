# Tracker: fix/menu-ring-drift  (card f0f2636a — "main menu spinner speed drift")

## Root cause (Phase 2 — DONE)
The main-menu "spinner" is the **HUD autofocus reticle** (`hudring`) in `MenuScene.cs`,
drawn at rotation `ringAngle + ringDrift` (DrawHudDecor, ~line 1281). `ringAngle` is a
bounded dart-lerp; `ringDrift` is an **unbounded integrator**: `ringDrift += ringDriftVel * dt`
runs every frame forever (UpdateRing, ~line 1158) and is NEVER reset, decayed, or clamped.
`ringDriftVel` (the ambient "coast" momentum) is (over)written each dart to ~5% of the dart's
angular speed and is likewise never damped back toward 0.

Consequences / the "going ham" class of bug:
- `ringDrift` grows without bound for the life of the (persistent) MenuScene instance.
- The coast momentum persists indefinitely instead of bleeding off, so the ring keeps a
  net spin, and any tweak to the 0.05 factor / durations makes the coast visibly run past
  the intended slow max. `ringDriftVel` has no hard ceiling — it's only implicitly bounded
  by the dart params, so a param change (or the user's "idk what I did") pushes it over max.

## Fix (Phase 3/4 — conservative, at the source)
In `MenuScene.UpdateRing`:
1. Hard-clamp `ringDriftVel` to `RingDriftVelMax` so the coast can NEVER exceed the intended
   slow max regardless of dart params (the "over its max speed" guarantee).
2. Exponentially decay `ringDriftVel` toward 0 each frame so inherited momentum bleeds off
   (coast is meant to be brief, not permanent).
3. Wrap `ringDrift` into (-pi, pi] each frame so the accumulator can't grow unbounded /
   lose float precision (rotation is mod-2pi identical).

No feel change intended: the darts, hold logic, and the ~5% momentum seed are untouched; the
clamp only bites values that were already out of spec, and the decay is gentle.

## Steps
- [x] Move card Backlog -> In Progress
- [x] Pull main, create wt4 + branch, push
- [x] Research: identify spinner + root cause
- [x] Write tracker
- [x] Implement fix in MenuScene.UpdateRing
- [x] dotnet build -c Debug clean (also after merging main)
- [x] Re-read full diff
- [x] Commit 6b2c2a7, push, pull main into branch (clean merge)
- [ ] /review (reviewer agent running; first attempt lost to outage)
- [ ] PR (--fill). PAUSE before merge (orchestrator override).

## Needs live/manual testing (no browser verification this session)
- Boot `?menu`, idle on the main menu, confirm the reticle rotates at a gentle, steady max
  and does not accelerate/run away over a long idle or after entering/leaving submenus.
- Confirm the darts (twitch/adjust/sweep) still read as before (feel unchanged).
