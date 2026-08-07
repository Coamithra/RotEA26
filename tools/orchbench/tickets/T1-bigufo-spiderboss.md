# T1 — AI: leave big UFOs alive as laser support against the spider boss

**Base commit:** `96c1073` (pin all runs of this ticket to the same base;
re-pin here if the ticket is re-run after main moves).

## Task (verbatim)

> AI: AI could be more careful to leave some big UFOs alive to shoot lasers
> at the spider boss, perhaps reduce radius where it shoots at big UFOs?

## Context for the agent

- The AI's tuning surface is the `?ai*` seam family and
  `PlayerShip.AiSkillByDifficulty[]` (see web CLAUDE.md → AI bench). Do not
  invent parallel knobs; extend the existing pattern if a new seam is needed.
- The AI bench (`?aibench`) already reports `killers=<Type>:<n>` with
  `SpiderBoss(standing)` split out, plus `boss=<px> bossfar=<pct>` — these
  are the observables that turned boss-related complaints into numbers.
- Every AI number predating merge f6b6504 is a hypothesis; the measurement
  floor is N=60 paired by seed via `python tools/sim/ai_sweep.py`, which
  reports time-to-victory as well as deaths.
- Whether "shoots at big UFOs" is aim selection, a firing radius, or threat
  weighting is part of the research: find where the bot chooses targets and
  whether big-UFO lasers actually damage the spider boss before designing
  the change.

## Target metric

Primary: on the spider-boss rig, deaths and/or time-to-victory improve at
N=60 paired-by-seed (the mechanism being that surviving big UFOs contribute
laser damage / distraction during the boss). `killers=SpiderBoss(standing)`
should not worsen.

## Gate (objective scoring pass)

1. `dotnet build web/EvilAliensWeb -c Debug` clean.
2. `python tools/headless/probes/run_probes.py` green (rebuild first — the
   runner refuses a stale binary).
3. `python tools/sim/ai_sweep.py` — spider-boss rig N=60 paired: primary
   metric moved in the right direction; the other rigs' victory verdicts
   unchanged (the reference table in web CLAUDE.md is the baseline).

## Regression definition

Any other rig's victory verdict flipping, or a statistically clear
worsening of deaths/time-to-victory on a non-target rig, fails the gate
even if the target metric improved.
