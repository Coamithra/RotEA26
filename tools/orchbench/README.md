# orchbench — comparing agent-orchestration strategies

An experiment harness for answering: *given a batch of AI-tuning tickets,
which division of labor between Fable ($10/$50 per MTok) and Opus ($5/$25)
gives the best result per dollar and per hour?* A run is one
(strategy × rep) pass over the whole ticket batch; cost lands in
`runs.csv`, per-ticket quality in `scores.csv`.

## How spend is measured

Claude Code logs every API call's `usage` block (input/output/cache tokens,
per model) to local transcripts under `~/.claude/projects/`, subagents
included. `usage_snap.py` sums them — **deduplicated by `requestId`**, because
streamed responses rewrite the same record several times and a naive sum
double-counts. Snapshot before, run the strategy, snapshot/record after:

```sh
python tools/orchbench/usage_snap.py snap -o start.json
# ... run the whole batch under one strategy ...
python tools/orchbench/usage_snap.py record start.json \
    --strategy fable-oracle --rep 1 --tickets T1,T2
```

`record` appends one batch row to `runs.csv` with wall seconds, per-class
token counts, and a `$` figure at list prices (cache reads 0.1× input rate,
cache writes 1.25× for 5-minute entries / 2× for 1-hour, split per record).
Both `diff` and `record` also report **prefix re-writes** — turns that
re-paid for an already-cached prefix because it expired (subagent caches
live 5 minutes; one that idles longer waiting on feedback re-writes its
whole conversation on resume) or was rebuilt. That waste is real cost but
not strategy intelligence — see `strategies.md` for how to avoid it.
`--selftest` pins the detector. The `$` is notional on a subscription, but it is the
right normalizer here, since the strategies deliberately mix Fable and Opus
tokens — raw token counts are not comparable across tiers.

**Scope caveats:**
- The meter sees only this machine's transcripts — run every strategy of a
  comparison from the same session/container.
- The diff includes the orchestrator's own turns (writing agent prompts,
  answering questions, reviewing). That is intentional: Fable's overhead is
  precisely what the strategies vary.
- Cost is attributed to the **batch**, not to individual tickets — when
  Opus agents run in parallel and Fable's turns serve several tickets at
  once, per-ticket attribution would be fiction. The per-model rows in the
  `diff` output do show the Fable-vs-Opus split, which is the interesting
  decomposition here.
- Don't run unrelated work in the session between `snap` and `record`.

## Resetting between runs — `reset_run.py`

A strategy run dirties the second shared surface: the Trello board (cards
claimed/moved/edited, and any **new** cards the run raises). The next rep
must start from the frozen-batch state, but nothing may be lost — new
tickets in particular are real findings. The local backend is file-backed,
so the tool snapshots the board's raw files and restores them
byte-identically, archiving every change first:

```sh
python tools/orchbench/reset_run.py snap -o board_start.json   # beside usage snap
# ... run the strategy (normal flows: worktrees, PRs, merges into main) ...
python tools/orchbench/reset_run.py diff  board_start.json     # preview
python tools/orchbench/reset_run.py reset board_start.json \
    --label fable-oracle-rep1 --git
```

`reset` writes `archive/<ts>-<label>.json` (full before/after text of every
changed card, new cards verbatim) plus a `.md` summary, then restores the
board. With `--git` it also archives the run's `main` tip as the keeper
branch `orchbench/run-<label>` (pushed; the scoring pass checks it out —
per-ticket diffs are its merge commits) and moves `main` back to the
snapshot's baseline commit with `--force-with-lease`. The batch baseline is
also an annotated tag (`orchbench-base-A` = batch A) so the state main
returns to has a name that survives any snapshot file. The archive dir is
committed — it is run output, like `runs.csv`; re-raise archived tickets on
the board after the *last* strategy run, so no strategy sees another's
findings. Deliberately untouched: the board's `activity.log` (append-only
history of what the run did), and `orchbench/*` branches are never deleted.
`--selftest` covers the board cycle against a synthetic store and the git
cycle against a temp repo.

## Ledgers

- **`runs.csv`** — one row per (strategy × rep) batch run: wall time, token
  counts, `$`. Written by `usage_snap.py record`.
- **`scores.csv`** — one row per (ticket × strategy × rep), filled by the
  scoring pass: `sweep_delta`, `probes_pass`, rubric score, run branch,
  notes. Joined to `runs.csv` on (strategy, rep).

## Protocol

1. **Tickets** are supplied at run time (they live on the project's Trello
   board, not in the repo). Before the first strategy runs, fix the batch:
   each ticket gets an id (T1, T2, …), its text verbatim, a target metric,
   an objective gate, and a regression definition, all pinned to one base
   commit — every strategy then runs that same frozen batch.
2. **Paired design.** Compare strategies on the same batch; compare ticket
   quality *within* a ticket only — ticket difficulty varies far more than
   strategy quality.
3. **Isolation.** Each ticket's change lands on
   `orchbench/<ticket>-<strategy>-<rep>`, cut from the ticket's pinned base
   commit, in a fresh worktree. No run sees another run's output, and the
   orchestrating session should not carry one strategy's discussion into the
   next (start a fresh session per strategy run if context has accumulated).
4. **Reps.** Agent runs are high-variance: minimum 2 reps per strategy
   before trusting any ranking. One run per cell is anecdote, not data.
5. **Scoring** (after all strategies have run the batch):
   - *Objective:* check out each run branch, run the ticket's stated gate
     (typically `tools/sim/ai_sweep.py` at N=60 paired-by-seed plus
     `tools/headless/probes/run_probes.py` and a clean build). Record the
     target-metric delta and pass/fail in `scores.csv`.
   - *Subjective (blind):* per ticket, the strategies' diffs are relabeled
     A/B/C with strategy names stripped, then scored by a judge agent — and
     by you — on the rubric in `strategies.md` § Rubric. Blinding matters;
     knowing which diff came from which strategy biases the grade.
6. **Reporting.** Wall time is reported as measured, but the verification
   gate (an N=60 sweep) can dwarf orchestration differences — the gate runs
   in the scoring pass, outside the metered window, unless a ticket says
   otherwise (note deviations in `notes`).

## Strategies

Defined as procedures in `strategies.md` — they vary how the labor splits
between Fable and Opus:

- **`fable-solo`** — Fable follows CONTRIBUTING.md itself, but grabs the
  whole batch and solves it in one sweep. No delegation.
- **`fable-oracle`** — the `/farm` skill as shipped, scoped to the batch's
  cards: Opus agents design their own solutions, Fable reviews plans, rules
  on questions, and runs the ship-gate diff review.
- **`fable-architect`** — `/farm` with the design moved up front: Fable
  fully architects each ticket's solution first, Opus agents implement to
  Fable's spec (farm's own-plan checkpoint replaced by design-note
  conformance).

All run on the plain Agent tool underneath (`model: "opus"` for the fleet,
worktree isolation) — no workflow runtime, so every strategy is measured by
the same meter. Runs use the normal flows, PR merges into `main` included;
isolation comes from the reset (below), not from withholding merges.

## Running a batch

Tell the orchestrating session, e.g.:

> run orchbench: strategy fable-oracle, rep 1

It will snapshot (usage **and** board), execute the strategy over all
tickets per `strategies.md`, commit each ticket's result to its run branch,
`record` the batch row, and finish with
`reset_run.py reset --label <strategy>-rep<n>` so the next run starts from
the frozen board.
