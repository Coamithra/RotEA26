# Card 13960838 — AI: screen-edge repulsion vs powerup pickup

## Context

The card reports the AI's screen-edge repelling forces as "too strong (AI wont pick up powerups, for instance)" and asks whether the values differ from the decompiled 2008 source.

## The audit (the card's own question)

Compared `Game/EvilAliens/PlayerShip.cs` `DoAIMove` against `src_decompiled/EvilAliens/PlayerShip.cs` line-by-line:

- **The four generic screen-edge repulsions are value-identical to 2008**: margin `steerRange` 150px, strengths 4 → 0, `MyMath.PowerCurve` exponent 2, bottom edge 600 (560 with a `Floor`). Confirms card 05a2b818's source-inspection strike ("2008 == port, nothing to audit").
- **The powerup pull is value-identical too**: reach 150px (2008 reused `steerRange`; the port names it `PowerupReachPx`, same value), magnitude 4 → 0 PowerCurve exp 2, seek 0.8.
- **The ONE edge force stronger than 2008 is the port-ADDED top-edge push** (`TopEdgeAvoidStrength` 20 over 170px; 2008 had no dedicated top-edge term). Card 2248e5eb measured it against the 2008 null (`?aitopedgestrength=0`, N=60 paired): removing it is WORSE on deaths on both rigs (level1 +0.88±0.52, spider +0.67±0.64) because a ceiling-pinned ship is exploded by spawning UFOs (deaths-by-UFO 132→356).

So the card's premise is three-quarters refuted by source: the 2008-era edge forces ship unchanged. The open question is whether the port-added top-edge push (20 vs the powerup pull's max 4 + seek 0.8) suppresses pickups — a powerup inside the top 170px band is mathematically unreachable while the push holds.

## Design

1. Measure baseline `pickups=` pct and the `?aitopedgestrength=0` arm on the powerup rigs (level1, spider) via `python tools/sim/ai_sweep.py` — short steering runs first (seeds 1-8 x2), honest N reported.
2. If the top-edge push measurably costs pickups: design a targeted change that keeps the safety term (deaths gate) while releasing the pickup deficit, measure it paired.
3. If it doesn't: no tuning change (the regression definition forbids changing a value without a paired measurement); the card is answered by the audit + numbers.

## Results (measured, eahl, Very_Hard, seeds 1-8 x2 paired, N=16 per arm-rig)

Step 1 (mechanism check, shipped vs `?aitopedgestrength=0`): spider pickups 48.0% vs 63.6% — the top-edge push IS suppressing pickups on the spider rig; level1 near-flat (85.5% vs 86.9%). Deaths worsen with the term off (spider +0.94 ± 1.15), consistent with 2248e5eb, so removal stays off the table.

Step 2 (the fix, yield-on default vs `?aitopedgeyield=0`):

| rig | pickups (yield) | pickups (pre-card) | deaths paired diff | win@ |
|---|---|---|---|---|
| spider | **66.8%** (173/259) | 48.0% (129/269) | 0.06 ± 0.97 (flat) | 147s vs 153s |
| level1 | 85.4% (794/930) | 85.5% (734/858) | 0.75 ± 0.90 (flat, yield better) | — |

The `noyield` arm reproduces the pre-change baseline digit for digit on the same seeds, so the seam is a faithful negative control. N=60 protocol run deferred to the scoring pass.

## Verification

- `ai_sweep.py` paired runs (N stated in the PR; full N=60 gate is the scoring pass's).
- The yield's observable is AiBench's `topyield=` counter (a suppressed push changes no pixel), pinned by the probe pair `tools/headless/probes/ai_topedge_yield.txt` + `ai_topedge_yield_absent.txt` (seed 3 on the spider rig fires it 18 ticks inside 60 sim-s, 5/5 runs). Mutation-tested both ways: dropping the `NoteTopEdgeYield` call turns the positive probe red with the absent one green; hard-wiring `TopEdgeYieldEnabled` to true turns the absent probe red with the positive green.
- `run_probes.py` green (60/60); logic_probe ALL PASS; clean Debug build; eahl smoke.

## Out of scope

- Re-tuning the four 2008-identical edge repulsions (audit says they are original).
- The radial asteroid field (doc: four axes swept, do not tune).
