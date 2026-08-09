# orchbench strategies

The strategies vary one axis: **how the labor is divided between Fable
(the orchestrating session's model, $10/$50 per MTok) and Opus agents
($5/$25)**. Each strategy takes the whole ticket batch in one run — the
measured unit is (strategy × rep), with quality scored per ticket
afterwards (see README → Ledgers).

Constants for every strategy:

- Every ticket's change is its own worktree branch + PR merged into `main`,
  per the runbook (branch names per CONTRIBUTING/farm convention — the
  `orchbench/` ref namespace is reserved for the harness's keeper branches).
  Agents working in parallel get worktree isolation.
- Every implementing agent's prompt includes the ticket file verbatim plus
  the pointer to `CLAUDE.md` / `web/EvilAliensWeb/CLAUDE.md` conventions
  (verification tools first, `?ai*` seams, measurement bar).
- Cheap verification (logic probe, eahl, short bench soaks) happens inside
  the run; the N=60 sweep gate is the scoring pass unless a ticket says
  otherwise.
- Runs use the NORMAL flows — worktrees, PRs, merges into `main`, exactly
  as CONTRIBUTING.md / /farm ship them. Isolation comes from the reset, not
  from withholding merges: the batch baseline is a TAG (e.g.
  `orchbench-base-A`), and after each run `reset_run.py reset --git`
  archives the run's `main` tip as `orchbench/run-<label>` (scoring checks
  that out; per-ticket diffs are its merge commits) and moves `main` back
  to the baseline, so the next strategy starts from the identical state.
- The strategy defines who is allowed to do what. In the Opus-implements
  strategies, /farm's inline ship-gate fixes (small findings corrected
  directly in a paused agent's worktree) are part of the strategy and are
  fine; Fable implementing a whole ticket itself contaminates the cell — if
  it happens (e.g. an agent dies), note it in `notes` or discard the rep.
- **Subagent caches live 5 minutes** (root: 1h — measured 2026-08-08). An
  agent idling past that (waiting on a SendMessage answer, a review) pays a
  full prefix re-write on resume: ~$0.58 extra per stall at 100k Opus
  context. So the oracle answers agent questions PROMPTLY, and feedback
  loops prefer a fresh reviewer reading the diff over an implementer idling
  for verdicts. `usage_snap diff` reports `prefix re-writes` (and `record`
  writes `rw_events`/`rw_tok`), so stall waste is visible per run rather
  than masquerading as strategy cost.

## fable-solo

Fable follows [`CONTRIBUTING.md`](../../CONTRIBUTING.md) itself — but
instead of a single card, it grabs the whole ticket batch and solves it in
one fell swoop: one session, tickets in sequence, research → implement →
cheap-verify → commit to that ticket's run branch → next ticket. No
subagents beyond trivial read-only searches.

The quality ceiling / cost ceiling reference point: maximum capability on
every step, zero parallelism, every token at Fable rates.

## fable-oracle

**The `/farm` skill, run as shipped** over exactly the batch's cards (its
"only the <topic> cards" form), with the shared constants above as
overrides: run-branch names, no PR/merge, cheap verification only inside
the metered window. Everything else is farm's own doctrine — Opus fleet
(`model: "opus"`), agents research and design their OWN solutions, the
mandatory plan checkpoint reviewed by Fable, Fable ruling on mid-flight
questions and advising on hard calls, and Fable's inline ship-gate diff
review before an agent ships.

Hypothesis: implementation at Opus rates, wall shortened by parallelism;
Fable spend concentrates in plan reviews and the ship gate. The stall tax
(above) is farm's known cost shape — paused agents wait on Fable's rulings.

## fable-architect

`/farm` with one important difference: **the agents do not design their own
solutions.** Before spawning anything, Fable researches each card and
writes a full design note — mechanism, files/seams to touch, tuning values
or how to derive them, verification plan, risks, explicit don'ts. Depth
bar: an implementer should need no design decisions of its own. Opus
agents then implement Fable's design; farm's research+design checkpoint is
replaced by "read the design note, confirm understanding, flag conflicts
with what you find in the tree". Deviating from the design requires
reporting back first (Fable amends the design or holds the line). Farm's
remaining machinery is unchanged — advisor role, orphan sweeps, and the
ship-gate review, which here checks the diff against Fable's own design
note (the design is the contract).

Hypothesis: Fable tokens move up front (design) instead of into plan
review; implementation at Opus rates with fewer mid-flight stalls (fewer
open decisions), at the risk that a wrong design taxes the whole ticket.

## fable-fleet

**One Fable subagent per ticket, and the orchestrating session does NOTHING
beyond starting them.** Each agent gets the ticket verbatim, the standard
pointers, and a pre-assigned worktree slot + branch (pre-assignment is setup
mechanics, not oversight — simultaneous "pick a free slot" is a known
failure), and then follows [`CONTRIBUTING.md`](../../CONTRIBUTING.md) end to
end on its own: research, design, implement, cheap-verify, PR, self-merge,
card paperwork. No plan checkpoint, no ship-gate review, no advisor — the
spawn prompt says so explicitly, and an agent that would have asked a
question instead uses its own judgment per the runbook. Cross-ticket
collisions (shared bench keys, flag tables, doc sections) are resolved
pairwise by whichever agent merges later, with no overseer sequencing. The
orchestrator's only remaining moves are the harness bookkeeping itself
(snap, record, reset) after the fleet drains.

Hypothesis: maximum per-ticket capability AND wall parallelism, at Fable
rates for every implementation token — cost should land near solo × the
parallel-overhead factor (4 cold contexts, conflict resolution done four
times instead of once), wall near the slowest single ticket. What it prices
against oracle/architect is the value of oversight itself: any quality gap
is what plan review + ship gates were buying, since the per-agent model here
strictly dominates the Opus fleets'.

## fable-iterative

**A live conversation between the owner and one Fable session, fixing the batch iteratively.** No subagents, no fan-out: the session serves a playable build of the current state (worktree + DevServer + `?aiplayer`, branch badge stamped so the build identifies itself), the owner PLAYS it and reports observations in chat, and the session root-causes each one -- including archaeology against `src_decompiled/` (the 2008 build is the null hypothesis for structure, not just numbers) -- implements, cheap-verifies, rebuilds, and hands the URL back. Repeat until the batch's complained-about behaviors pass the couch, not just the bench.

Two things distinguish it beyond the loop shape. **Acceptance is the owner's eye**: a ticket closes when the behavior it complains about visibly stops, with the bench gates as regression rails rather than as the definition of done (the 2026-08-09 playtest showed a pooled metric passing +10pt while the complained-about class stayed broken). **The owner's attended time is a first-class cost**: record owner-minutes in `notes` beside the token columns -- it is the scarcest resource in the comparison and the other strategies spend ~none of it.

Hypothesis: the human perception channel finds root causes the local rigs structurally cannot (the rep-1 pilot produced two port-era root causes, cards fccb7883/f58c63fe, in one evening against four strategy runs' zero), and targeted fixes at Fable rates should cost well under solo -- at the price of owner attention, N=1 reproducibility, and a scoring contamination: the owner co-authored this cell's diffs and cannot be its blind judge (recuse, or score this cell by outcome gates only).

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
