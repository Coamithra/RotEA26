# Tracker: fix/ownlevel-wall-nav-churn (card b4972696)

Constraint from the user this session: **NO live browser testing** (they're using the
machine). Verification must be offline/headless — build + isolation sim + IL/decompile
oracles. Anything that genuinely needs the in-browser `eaAiBench` gets flagged for them.

## Phase 1: Pick Up the Card
- [x] Claim card `b4972696` with `trello grab`
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree `.claude/worktrees/wt5` + branch `fix/ownlevel-wall-nav-churn`
- [ ] Push branch upstream

## Phase 2: Research
- [ ] Read the AI wall-nav code (ColumnScore, gap hysteresis, steering low-pass) from card f4d1721f
- [ ] Read `Walls(game, 2)` (OwnLevel) vs Level 3's grid variation — gap widths, column counts
- [ ] Trace the call chain: level setup -> Walls -> AI nav query
- [ ] Check `tools/sim/` for an existing offline harness to extend
- [ ] Summarize root cause hypothesis

## Phase 3: Design
- [ ] Write `plans/ownlevel-wall-nav-churn.md` (context / design / verification / out of scope)
- [ ] Present plan, get user approval BEFORE coding
- [ ] Post short TLDR comment on card b4972696

## Phase 4: Implement
- [ ] Build the offline sim harness (if none reusable)
- [ ] Apply the fix
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` if a new knob/flag/gotcha lands

## Phase 5: Verify (offline only)
- [ ] `dotnet build web/EvilAliensWeb -c Debug` clean
- [ ] Offline sim: before/after turn deg/s + reversals/s on OwnLevel grid
- [ ] Regression check: Level 3 grid must NOT get worse
- [ ] Spot-check the diff (no lowercase `content/`, no BlendState.AlphaBlend, no codegen re-run)
- [ ] Note for user: live `eaAiBench.matrix(['OwnLevel'], 1800, 3)` confirmation left to them

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook rules
- [ ] Re-verify after merge
- [ ] Return to root checkout
- [ ] `gh pr create --fill` + `gh pr merge --merge`
- [ ] Remove worktree, prune, delete branch (local + remote)
- [ ] Delete plan + this tracker
- [ ] Move card b4972696 to Done (`9c204b80`)
- [ ] Comment summary on the card (real newlines)
- [ ] Open follow-up cards (incl. orphaned worktree dirs `.claude/worktrees/wt2`? no - wt7 stale, 1 entry)
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] No dev servers started (no live testing) — confirm nothing left running
