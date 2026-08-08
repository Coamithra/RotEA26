# orchbench — running results

The prose ledger for the experiment [`README.md`](README.md) defines: which division of labor between Fable and Opus gives the best result per dollar and per hour on frozen batch A (T1–T4, base tag `orchbench-base-A`, card `1d685a77`). Raw cost rows: [`runs.csv`](runs.csv); per-ticket quality: [`scores.csv`](scores.csv) (empty until the scoring pass); per-run board/git archives: `archive/`.

**Status 2026-08-08: rep 1 of all three strategies is done. Nothing here ranks the strategies yet — quality is unscored (the N=60 gates and blind rubric run in the scoring pass), reps are N=1, and one run per cell is anecdote, not data.**

## Rep 1 — cost and wall

| | fable-solo r1 | fable-oracle r1 | fable-architect r1 |
|---|---|---|---|
| PRs | #315–#318 | #319–#322 | #323–#326 |
| run branch | `orchbench/run-fable-solo-rep1` | `…-fable-oracle-rep1` | `…-fable-architect-rep1` |
| cost | $91.12 | $141.37 | $101.24 |
| wall | 2122s | 4860s | 3324s |
| Fable $ | $91.12 (all) | $20.66 | $37.94 |
| Opus $ | — | $120.71 | $63.30 |
| prefix re-writes | 0 | 11 ev / 1.91M tok | 11 ev / 1.41M tok |
| rewrite waste | $0 | ~$10.95 | ~$8.10 |
| cost, no-rewrite counterfactual | $91.12 | ~$130.42 | ~$93.14 |

- **Per-model splits are NOT in `runs.csv`** (its columns are aggregate). The oracle split above was recomputed from the local transcripts by re-running `usage_snap`'s scan filtered to the run's `started_at + wall_s` window — method validated by reproducing the architect run's recorded table digit-for-digit first. Rewrite waste = `rw_tok × $5.75/MTok` (Opus 5-minute cache-write rate minus cache-read rate; both farm runs' rewrite events sat entirely in Opus subagents).
- **The counterfactual column is optimistic for the two farm strategies** — some agent idle is intrinsic to their checkpoint design, so $0 rewrite waste is not reachable; the honest floor is between the two cost rows. Oracle's figure also carries a known harness artifact (~1 of its 11 events was a dead-watcher stall, ~30min idle — see its `runs.csv` note).

## Rep-1 observations (hypotheses until rep 2 + scoring)

- **Where the Fable money goes inverts between the farm strategies.** Architect nearly doubles oracle's Fable spend ($20.66 → $37.94: up-front research incl. diagnostic eahl soaks, four full design notes, design-amendment rulings) and Opus spend HALVES ($120.71 → $63.30) with wall −32% — consistent with the strategies.md hypothesis: implementation to a spec stalls less and flails less than design-your-own.
- **With rewrites removed, architect ≈ solo on cost** (~$93 vs $91) while finishing in 63% of solo's wall via parallelism. Whether its quality differs is exactly what the scoring pass exists to answer — do not rank on this table alone.
- **Architect's stated risk showed up in miniature**: two of four design notes needed mid-run amendment when the implementers' measurements refuted a design detail (T2's unscoped correction braked long-range dodges; T3's exponent claim failed arithmetic the agent caught). Both were caught at checkpoints and cost one extra measure–rule round trip each, not a wrong ship.
- Same-batch caveat for scoring: T4's in-run sweep predates T1's constant change on the architect run branch (flagged in PR #324) — re-capture, don't quote forward.

## Still to run

1. `fable-fleet` rep 1 (strategy added 2026-08-08: one Fable subagent per ticket on the normal CONTRIBUTING workflow, orchestrator spawns-only — prices oversight itself; see strategies.md). Note the strategy set has grown since the first three reps ran; the batch and rubric are unchanged, so rows stay comparable.
2. Rep 2 for each strategy (fresh orchestrating session per run; protocol minimum before trusting any ranking).
3. The scoring pass over the run branches: per-ticket N=60 gates (`ai_sweep.py`) + blind A/B/C/D rubric → `scores.csv`.
4. After the LAST strategy run: re-raise the archived follow-up cards from `archive/` (marsboss preset `30fe3de4`, tutorial timeout `fa5d98d8`, plus the solo/oracle runs' finds) onto the board.
