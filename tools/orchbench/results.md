# orchbench — running results

The prose ledger for the experiment [`README.md`](README.md) defines: which division of labor between Fable and Opus gives the best result per dollar and per hour on frozen batch A (T1–T4, base tag `orchbench-base-A`, card `1d685a77`). Raw cost rows: [`runs.csv`](runs.csv); per-ticket quality: [`scores.csv`](scores.csv) (empty until the scoring pass); per-run board/git archives: `archive/`.

**Status 2026-08-08: rep 1 of all four strategies is done. Nothing here ranks the strategies yet — quality is unscored (the N=60 gates and blind rubric run in the scoring pass), reps are N=1, and one run per cell is anecdote, not data.**

## Rep 1 — cost and wall

| | fable-solo r1 | fable-oracle r1 | fable-architect r1 | fable-fleet r1 |
|---|---|---|---|---|
| PRs | #315–#318 | #319–#322 | #323–#326 | #327–#330 |
| run branch | `orchbench/run-fable-solo-rep1` | `…-fable-oracle-rep1` | `…-fable-architect-rep1` | `…-fable-fleet-rep1` |
| cost | $91.12 | $141.37 | $101.24 | $184.53 |
| wall | 2122s | 4860s | 3324s | 3178s |
| Fable $ | $91.12 (all) | $20.66 | $37.94 | $176.48 |
| Opus $ | — | $120.71 | $63.30 | $8.05 |
| prefix re-writes | 0 | 11 ev / 1.91M tok | 11 ev / 1.41M tok | 7 ev / 1.61M tok |
| rewrite waste | $0 | ~$10.95 | ~$8.10 | ~$18.50 |
| cost, no-rewrite counterfactual | $91.12 | ~$130.42 | ~$93.14 | ~$166.03 |

- **Per-model splits are NOT in `runs.csv`** (its columns are aggregate). The oracle split above was recomputed from the local transcripts by re-running `usage_snap`'s scan filtered to the run's `started_at + wall_s` window — method validated by reproducing the architect run's recorded table digit-for-digit first. Rewrite waste = `rw_tok` × the model's 5-minute cache-write rate minus cache-read rate ($5.75/MTok Opus, $11.50/MTok Fable; the farm runs' rewrite events sat in Opus subagents, the fleet's in Fable subagents).
- **The counterfactual column is optimistic for the two farm strategies** — some agent idle is intrinsic to their checkpoint design, so $0 rewrite waste is not reachable; the honest floor is between the two cost rows. Oracle's figure also carries a known harness artifact (~1 of its 11 events was a dead-watcher stall, ~30min idle — see its `runs.csv` note). The fleet's rewrite waste is a different animal: no checkpoints exist, so all 7 events are agents ending their turn while their own background sweep/probe ran and re-paying the prefix on resume — avoidable in principle (poll in-turn), and the biggest single-run rewrite bill so far.
- **Fleet's Opus rows are the agents' own doing** — each spawned a fresh-context reviewer (the `/review` pattern) before shipping; the strategy spec doesn't forbid subagents of subagents, and it's the same runbook step solo skipped.

## Rep-1 observations (hypotheses until rep 2 + scoring)

- **Where the Fable money goes inverts between the farm strategies.** Architect nearly doubles oracle's Fable spend ($20.66 → $37.94: up-front research incl. diagnostic eahl soaks, four full design notes, design-amendment rulings) and Opus spend HALVES ($120.71 → $63.30) with wall −32% — consistent with the strategies.md hypothesis: implementation to a spec stalls less and flails less than design-your-own.
- **With rewrites removed, architect ≈ solo on cost** (~$93 vs $91) while finishing in 63% of solo's wall via parallelism. Whether its quality differs is exactly what the scoring pass exists to answer — do not rank on this table alone.
- **Architect's stated risk showed up in miniature**: two of four design notes needed mid-run amendment when the implementers' measurements refuted a design detail (T2's unscoped correction braked long-range dodges; T3's exponent claim failed arithmetic the agent caught). Both were caught at checkpoints and cost one extra measure–rule round trip each, not a wrong ship.
- **Fleet is the most expensive cell so far ($184.53, 2.0× solo) and did NOT beat solo on wall (3178s vs 2122s).** Wall = the slowest agent (T2 ran 3092s of its own); the parallelism bought nothing over solo because each fleet agent did far more per ticket than solo's per-ticket pass — deep root-cause work (T2 derived the orbit mechanism from the steering low-pass constants; T3 found both range tests measured to the boss's *centre*, not its collision-box face), new mutation-tested probe pairs and logic_probe case sets per ticket, and a fresh-context review each. Whether that extra work is quality or gold-plating is precisely the scoring pass's question — the strategy's hypothesis ("cost ≈ solo × overhead factor") underpriced how much more work an unsupervised Fable *chooses* to do per ticket than a batch-mode one.
- **"Spawn-only" wasn't literally achievable**: 5 content-free liveness nudges ("continue — your card is not done") were needed because agents ended their turn while a background sweep/probe ran, which also produced the rewrite bill. Harness friction, not oversight — no substantive input was given — but a rep-2 spawn prompt should say "poll background jobs in-turn; don't end your turn to wait" (the T3 agent alone parked 3×).
- **Cross-ticket conflict resolution worked leaderless**: T4, T3, T2 each merged `origin/main` mid-flight and resolved conflicts pairwise keeping both tickets' intent, re-running their gates on the merged tree; T2 (last) additionally audited every sibling-added repellent site into its new `threatPressure` coverage. One fleet-specific hazard surfaced: T4's branch name collided with the *oracle* rep's remote branch for the same ticket (PR #321, preserved on the oracle keeper branch) — handled with a disclosed force-with-lease, but pre-assigned branch names should be rep-unique going forward.
- Same-batch caveat for scoring: T4's in-run sweep predates T1's constant change on the architect run branch (flagged in PR #324) — re-capture, don't quote forward.

## Still to run

1. Rep 2 for each strategy (fresh orchestrating session per run; protocol minimum before trusting any ranking). For fleet rep 2: rep-unique branch names, and the poll-in-turn line in the spawn prompt (see above).
2. The scoring pass over the run branches: per-ticket N=60 gates (`ai_sweep.py`) + blind A/B/C/D rubric → `scores.csv`.
3. After the LAST strategy run: re-raise the archived follow-up cards from `archive/` (marsboss preset `30fe3de4`, tutorial timeout `fa5d98d8`, plus oracle's three AI finds — note its orbit-instead-of-parking card may already be resolved by fleet's T2/T3, so check the winning branch before re-raising; solo and fleet filed none) onto the board.
