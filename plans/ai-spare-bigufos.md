# Card 2c74d5b7 — AI: leave big UFOs alive to laser the spider boss (engage radius)

## Context

Only a `Lazer` hurts the SpiderBoss (`SpiderBoss.CollidesWith`), and a big UFO is what fires one. The AI already spares exactly ONE big UFO (the one with the most room, suspended during a sweep fly-by — `DoAIFire`, commit a3e3b97), which caps the surviving beam platforms at one however many the fight has. The card asks the AI to be more careful about leaving "some" alive, suggesting a reduced engage radius. The dead const `SpiderBossLaserPlatforms = 2` (referenced by nothing; the code spares one) goes.

Prior art (previous benchmark reps of this same ticket, on run branches, not on main): a radius-only rule at 250px measured strongly positive on this baseline (deaths 2.44 vs 5.50, victories 12/16 vs 2/16 at N=16); a different baseline with other AI changes measured it as a loss. So the radius is measured fresh here, on current main.

## Design

- `PlayerShip`: new `DefaultBigUfoEngagePx` const + `BigUfoEngagePx` resolving property. In `DoAIFire`, while the spider boss is alive and NOT sweeping, a big UFO farther from this ship than the radius is spared (left alive and firing) **in addition to** the existing spare-one rule. Radius 0 = the rule off = today's shipped behaviour verbatim (the `?aicone=0` idiom); during a sweep nothing is spared (unchanged). The decision is a pure static (`AiSparesBigUfoAtRange`) so `logic_probe` can call it.
- `DebugFlags`: `?aibigufopx=<px>` (nullable override, RejectFlagValue else-branch per the value-carrying-flag convention; negative refused).
- `AiBench` (behind `?aibench`, zero work otherwise): event-driven counter hooked at `SpiderBoss.BeginDeathThroes` — big UFOs alive at the instant the boss dies. `Line()`/`Row()` gain append-only `bigufos=<mean|none> bossdeaths=<n>`.
- `tools/sim/ai_sweep.py`: print the two new fields in the per-arm summary.
- `logic_probe`: `?aibigufopx=` row in `ProbeAiFlagRejection`'s table + a small case set for the pure predicate (boss-alive / sweeping / radius-off / boundary).
- Docs: web CLAUDE.md AI section + flag lists.

## Verification

- Clean build (game + eahl), `logic_probe` ALL PASS, `run_probes.py` green.
- `ai_sweep.py --rig spider --seeds 1-8 --captures 2` (N=16, steering only — the N=60 gate runs in a later scoring pass): baseline (`aibigufopx=0`) vs 200/250/300. Bake the winner; deaths must stay within SEM of baseline, time-to-victory improve or stay flat, and the new counter must read higher on the baked arm.

## Out of scope

- Any change to how the ship POSITIONS itself around the boss or the spared UFOs (the standoff/evasion terms are other cards' territory; two siblings are touching them concurrently).
- The N=60 gate (scoring pass).
