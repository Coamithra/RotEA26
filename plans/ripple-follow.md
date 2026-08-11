# Bomb ripple follows the ship/explosion (card 03c379f2)

## Context

The bomb-detonation ripple (`Compat/BombRipple.cs`, card 5f38ed35) fires one ring at the detonation point with a fixed 0.75 s life. But the explosion it decorates is neither stationary nor 0.75 s long: `PlayerShip.Update` drags the live `Blast` with the ship every tick (`blast.SetPosition(base.Position)`, PlayerShip.cs:1155), and a blast lives `1000 ms * (power+1)` = 1..5 s (`Blast.Setup`). So the ring is left behind at the point of detonation and dies long before the blast does. The card: the ripple should follow the ship/explosion "in both duration and location".

## Design

Token-based push from the blast into the ripple layer — no Compat->game type reference, no dangling delegate over a pooled `Blast`:

- **`BombRipple.Fire(pos, power, mini, durationSeconds)` returns an int token** (0 = no ring fired: master off, mini gated). Token = slot | generation, so a stale token (ring evicted by a fifth bomb, or a recycled Blast) no-ops instead of dragging someone else's ring.
- **`BombRipple.MoveRing(token, pos)`**: re-centre the ring if the token still matches. Called from `Blast.Update` every tick, so the ring rides the blast exactly as the blast rides the ship — local bomb, remote peer's bomb (`NetDoBlast` sets the same `blast` field) and the respawn pop (stationary — push is a no-op in effect) all covered with no per-caller code.
- **Per-ring duration**: `Ring.Duration` is seeded at `Fire` from the blast's real `lifetime.Duration`; minis pass their own lifetime. Resolution stays live per frame: `?rippleduration=` (and the eaRipple slider) still overrides everything — the tuner and the committed probe `bomb_ripple.txt` (which pins the expiry window via that flag) keep working unchanged. No override -> the ring expires exactly when its blast does.
- **Clock unchanged**: rings keep advancing on raw Draw time, freeze-gated (card d79a2f48's ruling — travels through hit-stop, freezes under pause). Under slow-mo the ring can finish before the blast; accepted, it is a Draw-time cosmetic.
- **`EnsureParked`** (the `?ripplephase=` scrub rig) parks the ring with the duration a real bomb of `?ripplepower=` would have (`(1+power)` s), so the scrub maps phase like a real detonation.

## Verification

- **`logic_probe` case set `ProbeBombRippleFollow`**: `BombRipple` is pure statics over consts, so the decision layer is probed for real — fire-with-duration, follow, stale-token no-op (negative control), eviction reuse, per-ring expiry, `?rippleduration=` override winning.
- **New eval seams** `RippleBlast(x,y,power)` / `RippleBlastMove(x,y)` in `DebugInput` spawn and move a REAL `Blast` through `ComponentBin`, so the wiring (`Blast.Initialize` -> `Fire`, `Blast.Update` -> `MoveRing`) is probed live; `RippleState` now reports each live ring's centre + elapsed/duration as data.
- **New headless probe `tools/headless/probes/bomb_ripple_follow.txt`**: real blast fired, moved, ring centre follows, ring expires with the blast's own duration. Existing `bomb_ripple.txt` unaffected.
- Final Chrome smoke: boot a level, `eaRipple` panel still live, zero console exceptions.

## Out of scope

- Radius/amplitude retuning for long-lived rings (all knobs remain live; taste pass is the owner's).
- Emitting repeated rings over a blast's life (different look; not what the card asks).
