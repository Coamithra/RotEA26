# Tracker: feature/ai-challenge-matrix

Card `9391f95a` — "AI: measure the challenge levels (completion matrix)".
Worktree `.claude/worktrees/wt4`, dev port `5284`.

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (got 9391f95a)
- [x] Fetch latest main (root checkout dirty from other agents — branched off `origin/main` d8e403f)
- [x] Read the card + parent card f4d1721f comments
- [x] Create worktree + branch (wt4 / feature/ai-challenge-matrix)
- [x] Push branch

## Phase 2: Research
- [x] Read `Compat/AiBench.cs` (soak harness, verdicts, telemetry)
- [x] Read the AI ship path (`PlayerShip` DoAI*, `Oracle`)
- [x] Read the 9 unmeasured levels' event scripts + control schemes
- [x] Identify which levels can even run headless — findings:
  - 8 of 9 run with `score.Lives = -1` (GameScene.cs:736) so GAME OVER is unreachable
  - TeamChallenge force-pauses forever (PadOne seated, no pad) → unmeasurable without a fix
  - Paratrooper is a turret (ship pinned at 400,500) → steering metrics vacuous
  - Braineroids/CrazyGame use the ordinary ship (the card's caution was speculative)
  - headless soak ≈ 60x realtime

## Phase 3: Design
- [x] Write `plans/ai-challenge-matrix.md`
- [x] Get user approval (defects only; follow-up cards for AI capability work)
- [x] Post TLDR comment on the card

## Phase 4: Implement
- [x] `AiBench.Row()` + `debugAiBenchRow` — machine-readable per-run counters
- [x] `eaAiBench.matrix()` sweep runner (sessionStorage plan, one fresh boot per run, TIMEOUT verdict)
- [x] `?aiteam` — seat TeamChallenge slot 1 as Generic so it is measurable
- [x] Run the sweep, record the matrix (9x3 at Very Hard; 6 pass, 3 fail)
- [x] Triage: no world-model/targeting defect found — `?invuln` control wins all 3 failures
- [x] Update docs (root CLAUDE.md + web CLAUDE.md)

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug`
- [x] `?aiteam` before/after: `ticks=0 noship=1 prog=2/52` → `ticks=1682 shots=1029 prog=6/52`
- [x] Runner smoke test (2 levels x 1 run) prints the matrix, resumes across reloads
- [x] Full sweep completes with zero console exceptions
- [x] Completion matrix written down (web CLAUDE.md)

## Phase 6: Review & Ship
- [x] Commit + push
- [x] `/review` and fix findings (17 found, all 17 fixed)
- [ ] Pull main, resolve, re-verify
- [ ] PR + self-merge
- [ ] Remove worktree/branch, delete plan + tracker
- [ ] Card → Done + summary comment
- [ ] Follow-up cards
- [ ] User overview

## Phase 7: Clean up
- [ ] Stop dev server, close browser tabs
