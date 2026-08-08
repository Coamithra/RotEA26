# orchbench strategies

The strategies vary one axis: **how the labor is divided between Fable
(the orchestrating session's model, $10/$50 per MTok) and Opus agents
($5/$25)**. Each strategy takes the whole ticket batch in one run — the
measured unit is (strategy × rep), with quality scored per ticket
afterwards (see README → Ledgers).

Constants for every strategy:

- Every ticket's change lands on its own branch
  `orchbench/<ticket>-<strategy>-<rep>`, cut from the ticket's pinned base
  commit. Agents working in parallel get worktree isolation.
- Every implementing agent's prompt includes the ticket file verbatim plus
  the pointer to `CLAUDE.md` / `web/EvilAliensWeb/CLAUDE.md` conventions
  (verification tools first, `?ai*` seams, measurement bar).
- Cheap verification (logic probe, eahl, short bench soaks) happens inside
  the run; the N=60 sweep gate is the scoring pass unless a ticket says
  otherwise.
- The strategy defines who is allowed to do what. Fable writing code in an
  Opus-implements strategy contaminates the cell — if it happens (e.g. an
  agent dies), note it in `notes` or discard the rep.
- **Subagent caches live 5 minutes** (root: 1h — measured 2026-08-08). An
  agent idling past that (waiting on a SendMessage answer, a review) pays a
  full prefix re-write on resume: ~$0.58 extra per stall at 100k Opus
  context. So the oracle answers agent questions PROMPTLY, and feedback
  loops prefer a fresh reviewer reading the diff over an implementer idling
  for verdicts. `usage_snap diff` reports `prefix re-writes` (and `record`
  writes `rw_events`/`rw_tok`), so stall waste is visible per run rather
  than masquerading as strategy cost.

## fable-solo

Fable does all the tickets itself, sequentially, in one session. No
subagents (beyond trivial read-only searches). One ticket at a time:
research → implement → cheap-verify → commit → next ticket.

The quality ceiling / cost ceiling reference point: maximum capability on
every step, zero parallelism, every token at Fable rates.

## fable-oracle

Opus does the work; Fable dispatches and unblocks.

1. Fable spawns one Opus agent per ticket (`model: "opus"`, worktree
   isolation), in parallel, each with the full ticket brief and this
   standing instruction: *"If you hit a decision you cannot resolve from
   the ticket, the code, or repo conventions — a design fork, an ambiguous
   requirement, a suspicious measurement — STOP and end your report with
   the question(s) instead of guessing."*
2. When an agent reports back with questions, Fable answers via
   `SendMessage` (the agent continues with its context intact). Fable may
   read code to answer well, but writes no implementation itself.
3. Repeat until each agent has committed; Fable sanity-reads each final
   report (not the full diff — that would drift into architect/reviewer).

Hypothesis: near-Opus cost, shorter wall than solo (parallel tickets),
quality depends on whether agents ask the right questions.

## fable-architect

Fable designs; Opus implements to spec.

1. **Design phase (Fable, batch):** for each ticket, Fable researches the
   code enough to write a concrete design note — mechanism, files/seams to
   touch, tuning values or how to derive them, verification plan, risks and
   explicit don'ts. Depth bar: an implementer should need no design
   decisions of their own.
2. **Implementation phase (Opus, parallel):** one Opus agent per ticket,
   each given ticket + design note, in its own worktree. Deviating from the
   design requires reporting back first (Fable either amends the design or
   holds the line).
3. Fable reviews each diff against its own design note (this strategy
   *does* include Fable review — the design is the contract) and requests
   fixes; agents apply them.

Hypothesis: Fable tokens concentrated where they matter (design/review),
implementation at Opus rates; wall = design phase (serial-ish) +
implementation (parallel).

## Rubric (blind subjective scoring, 1–5 each)

Per ticket. Judges see only the ticket text and the unlabeled diff (plus
the run's stated verification evidence for criterion 4) — strategy labels
stripped, diffs relabeled A/B/C across strategies.

1. **Fitness** — does the change plausibly achieve the ticket's target
   behavior, mechanism-wise?
2. **Convention compliance** — existing seams reused, tuning routed through
   the established knobs, measurement bar respected, no forbidden areas
   touched.
3. **Diff quality** — minimal, readable, matches surrounding idiom; no
   scope creep.
4. **Verification evidence** — the run produced evidence a reviewer could
   accept (probe/bench/sim output), not just assertions.
