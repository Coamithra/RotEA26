# orchbench — comparing agent-orchestration strategies

An experiment harness for answering: *given an AI-tuning ticket, which way of
orchestrating Claude (one agent? a pipeline? a contest?) gives the best
result per dollar and per hour?* Each run is one (ticket × strategy × rep)
cell; results accumulate in `runs.csv`.

## How spend is measured

Claude Code logs every API call's `usage` block (input/output/cache tokens,
per model) to local transcripts under `~/.claude/projects/`, subagents
included. `usage_snap.py` sums them — **deduplicated by `requestId`**, because
streamed responses rewrite the same record several times and a naive sum
double-counts. Snapshot before, run the strategy, snapshot/record after:

```sh
python tools/orchbench/usage_snap.py snap -o start.json
# ... run the strategy ...
python tools/orchbench/usage_snap.py record start.json \
    --ticket T1 --strategy contest --rep 1 --branch orchbench/T1-contest-1
```

`record` appends a row to `runs.csv` with wall seconds, per-class token
counts, and a `$` figure at list prices (cache reads 0.1× input rate, cache
writes 1.25×). The `$` is notional on a subscription, but it is the right
normalizer when runs mix models. Quality columns (`sweep_delta`,
`probes_pass`, `rubric`) are filled later by the scoring pass.

**Scope caveats:**
- The meter sees only this machine's transcripts — run every strategy of a
  comparison from the same session/container.
- The diff includes the orchestrator's own turns (writing agent prompts,
  reading reports). That is intentional: orchestration overhead is part of a
  strategy's cost.
- Don't run unrelated work in the session between `snap` and `record`.

## Protocol

1. **Tickets** live in `tickets/` — one markdown file each, stating the task
   verbatim, the target metric, the objective gate, and what counts as a
   regression. Tickets should be comparable in shape (behavioral AI tuning
   with a measurable target).
2. **Paired design.** Every strategy runs every ticket. Compare *within* a
   ticket only — ticket difficulty varies far more than strategy quality.
3. **Isolation.** Each run starts from the same base commit in a fresh
   worktree/branch (`orchbench/<ticket>-<strategy>-<rep>`) and never sees
   another run's output.
4. **Reps.** Agent runs are high-variance: minimum 2 reps per cell before
   trusting any ranking. One run per cell is anecdote, not data.
5. **Scoring** (after all runs for a ticket are in):
   - *Objective:* check out each run's branch, run the ticket's stated gate
     (typically `tools/sim/ai_sweep.py` at N=60 paired-by-seed plus
     `tools/headless/probes/run_probes.py` and a clean build). Record the
     target-metric delta and pass/fail.
   - *Subjective (blind):* diffs are relabeled A/B/C with strategy names
     stripped, then scored by a judge agent — and by you — on the rubric in
     `strategies.md` § Rubric. Blinding matters; knowing which diff came from
     the fancy strategy biases the grade.
6. **Reporting.** Wall time is reported as measured, but note that the
   verification gate (an N=60 sweep) can dwarf orchestration differences —
   when comparing, check whether a wall-time gap is agent time or sweep time
   (the `notes` column is the place to say a run included/excluded the sweep).

## Strategies

Defined as procedures in `strategies.md`: `solo`, `pipeline`, `contest`,
`verify`. They are executed by the orchestrating Claude session using its
plain Agent tool (worktree isolation for parallel implementers) — no workflow
runtime involved, so all strategies are measured by the same meter.

## Running a cell

Tell the orchestrating session, e.g.:

> run orchbench: ticket T1, strategy contest, rep 1

It will snapshot, create the branch from the ticket's stated base commit,
execute the strategy per `strategies.md`, commit the result to the run
branch, and `record` the row.
