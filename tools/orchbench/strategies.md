# orchbench strategies

Each strategy is a procedure the orchestrating Claude session executes with
its Agent tool. Constants for every strategy:

- Start from the ticket's stated base commit on a fresh branch
  `orchbench/<ticket>-<strategy>-<rep>`; parallel implementers get worktree
  isolation so they never share a checkout.
- Every agent prompt includes the ticket file verbatim plus the pointer to
  `CLAUDE.md` / `web/EvilAliensWeb/CLAUDE.md` conventions (verification
  tools first, `?ai*` seams, measurement bar).
- The run ends with the change committed to the run branch. The heavy
  objective gate (N=60 sweep) is run in the scoring pass, not inside the
  strategy, unless the ticket says otherwise — note deviations in `notes`.
- The orchestrator does not fix agents' work itself; steering happens by
  messaging the agent. (Otherwise every strategy degrades into "solo with
  extra steps" and the comparison is meaningless.)

## solo — the control

One `general-purpose` agent gets the ticket end-to-end: research, design,
implement, self-verify with the repo's cheap tools (logic probe, eahl,
short bench soaks), commit.

## pipeline — sequential specialists

Four agents in sequence, each receiving the previous stage's written report:

1. **Research** (read-only): where the relevant behavior lives, which seams
   and benches exist, what the 2008 original did. Output: a findings report.
2. **Design**: a concrete change proposal with tuning values, measurement
   plan, and risk list. Output: a design note.
3. **Implement**: apply the design, run cheap verification, commit.
4. **Verify** (read-only on the diff): check the diff against the design and
   the repo's conventions; report defects. Implementer fixes anything real.

## contest — parallel implementers + blind judge

1. Three implementer agents run **in parallel, each in its own worktree**,
   with identical prompts (the solo prompt). Each commits to its own branch.
2. A judge agent receives the three diffs **relabeled A/B/C** (strategy of
   generation is identical here, but the judge must not see which agent or
   any chat context) and scores them on the rubric below.
3. The winning diff becomes the run's branch. The judge's scores go in
   `notes`. The run's cost is all three implementers plus the judge — that's
   the point of measuring this strategy.

## verify — implement + adversarial verification

1. One implementer agent (the solo prompt).
2. Three verifier agents in parallel, each prompted to **refute** the change
   from a distinct lens:
   - *correctness*: does the change do what the ticket asks; edge cases;
     does it break the AI on other levels in principle?
   - *conventions*: CLAUDE.md compliance — used existing seams, respected
     the measurement bar, no landmines (e.g. tuning the radial asteroid
     field), probe added if the failure would be silent?
   - *evidence*: does the verification the implementer ran actually support
     the claim? What did it not test?
3. Confirmed findings go back to the implementer for a fix round; repeat
   once if needed, then commit.

## Rubric (blind subjective scoring, 1–5 each)

Judges see only the ticket text and the unlabeled diff (plus the run's
stated verification evidence for criterion 4).

1. **Fitness** — does the change plausibly achieve the ticket's target
   behavior, mechanism-wise?
2. **Convention compliance** — existing seams reused, tuning routed through
   the established knobs, measurement bar respected, no forbidden areas
   touched.
3. **Diff quality** — minimal, readable, matches surrounding idiom; no scope
   creep.
4. **Verification evidence** — the run produced evidence a reviewer could
   accept (probe/bench/sim output), not just assertions.
