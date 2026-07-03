# Tracker: feature/asteroid-belt-star-slowdown

Card b9169230: "same as earth animation but for the asteroid field! some stars are faster than
asteroids, that's weird." Slow the starfield during the Level 1 sideways asteroid-belt phase so
the fastest star is comfortably slower than the slowest asteroid.

## Phase 1: Pick up
- [x] Move card to In Progress
- [x] Pull main
- [x] Read card + CLAUDE.md/CONTRIBUTING
- [x] Worktree wt2 + branch feature/asteroid-belt-star-slowdown + push

## Phase 2: Research (DONE)
- [x] Background.cs — DoodadStarSlowdownFactor + effectiveModifier fold-in
- [x] DriftingStars.cs — parallax 1.3..3.8, near stars move baseDelta*parallax
- [x] Level1.cs — belt phase: earthFlybyGate -> spawner_OnFinished sets speed (0.25,0.6)/16.666;
      AsteroidSpawner(42s); later waitevent_OnFinished resets to (0,0.2)/16.666
- [x] AsteroidSpawner/Asteroid — fg speed 0.38, bg (SetBackground) 0.152 px/ms; big opener 0.3
- [x] AlienDrawableGameComponent — _speed is design px/ms
- [x] Confirmed earth & belt CANNOT overlap (WaitForDoodadEvent gate)

### Speed derivation (design px/ms, gameplay scrollspeedmodifier settles to 1.0)
- belt scrollspeed vec (0.015, 0.036), mag 0.039
- fastest near star = mag * 3.8 = 0.148 px/ms  (this is the offender)
- slowest star     = mag * 1.3 = 0.051 px/ms
- foreground (collidable) asteroid = 0.38 (-10% -> 0.342)
- background (decor) asteroid       = 0.152 (-10% -> 0.137)  <- SLOWEST asteroid class
- => fastest star 0.148 ~= slowest asteroid 0.137: some stars edge past the decor asteroids. BUG confirmed.

Mirror earth's "N x" reasoning: want slowest asteroid = N x fastest star.
Slowdown factor f applied to star field: fastest star -> 0.148*f.
Pick N=2.5 (conservative, comfortably slower without freezing): f = (0.137/2.5)/0.148 = 0.37.
=> BeltStarSlowdown = 0.37. Fastest star becomes ~0.055 px/ms; decor asteroid ~0.137 = 2.5x; fg 5x+.

## Phase 3: Design (DONE — proceeding, no interactive approval per orchestrator)
Reuse the slowdown machinery: add a SECOND factor `BeltStarSlowdownFactor()` multiplied into
`effectiveModifier` alongside the doodad factor. Belt phase is a WAVE, not a doodad crossing, so it
uses an explicit engage/disengage with a wall-clock ramp (same shape + ramp durations as the doodad
envelope, no per-doodad position hook).
- Background: `BeltStarSlowdown` const (0.37), `beltSlowActive` bool, `beltRampTimer` state, methods
  `EngageBeltSlowdown()`/`DisengageBeltSlowdown()`, `BeltStarSlowdownFactor()` returning <=1.
  Multiply into effectiveModifier: `scrollspeedmodifier * DoodadStarSlowdownFactor() * BeltStarSlowdownFactor()`.
- Level1: `spawner_OnFinished` -> also `Background.EngageBeltSlowdown()`; AsteroidSpawner.OnFinished
  -> `Background.DisengageBeltSlowdown()`.
- Ramps: reuse RampIn 1200ms / RampOut 1600ms feel via a 0..1 eased envelope over wall-clock.

## Phase 4: Implement
- [ ] Background.cs changes
- [ ] Level1.cs wiring
- [ ] CLAUDE.md doc update (extend earth bullet / add belt note)

## Phase 5: Verify (NO live browser per orchestrator)
- [ ] dotnet build -c Debug clean
- [ ] Re-read full diff
- [ ] List manual-test items

## Phase 6: Ship (PAUSE before merge)
- [ ] Commit + push
- [ ] /review or self-review
- [ ] gh pr create --fill
- [ ] STOP; report back
