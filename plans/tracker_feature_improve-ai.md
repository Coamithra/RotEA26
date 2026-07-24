# Tracker: feature/improve-ai (card f4d1721f "Improve AI")

Worktree: `.claude/worktrees/wt7` · dev port **5287** · branch `feature/improve-ai`

Card ask: AI bot plays OK to ~halfway level 2, no idea how to fight the spider boss,
gives up somewhere in level 3; **and is bad at flying through the walls — jittery,
collides a lot**.

## Phase 1: Pick Up the Card
- [x] Claim card atomically (`grab` → f4d1721f "Improve AI")
- [x] Fetch latest origin/main (ae3bac5)
- [x] Read the card (no comments, desc only)
- [x] Create worktree wt7 + branch feature/improve-ai
- [ ] Push branch

## Phase 2: Research
- [ ] Find the AI/bot implementation in `web/EvilAliensWeb/Game/`
- [ ] Trace how the AI is driven (input injection? per-frame decision?)
- [ ] Understand the wall section (Level2 walls) + how the AI navigates it
- [ ] Understand the spider boss fight + brain boss (Level3)
- [ ] Identify existing debug/verification tooling for AI (flags, sims)

## Phase 3: Design
- [ ] Write `plans/improve-ai.md`
- [ ] Present plan, get user approval
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Wall navigation smoothing / collision avoidance
- [ ] Spider boss handling
- [ ] Level 3 handling
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` if new flags/conventions

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Tool-driven verification (isolation sim / headless AI run), NOT timed screenshots
- [ ] Real-game smoke check in Chrome, zero console errors
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per rules
- [ ] Re-verify
- [ ] PR + self-merge (`gh pr create --fill`, `gh pr merge --merge`)
- [ ] Clean up worktree + branch
- [ ] Delete plan + tracker
- [ ] Move card to Done (9c204b80) + summary comment
- [ ] Follow-up cards
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server on 5287
- [ ] Close any remaining browser tabs
