# Tracker: feature/ai-difficulty-scaling

Card `c10e3e7f` — AI: difficulty-scaled skill (the bot plays identically on Easy and Inzane)

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (got c10e3e7f)
- [x] Fetch latest origin/main (root checkout dirty — branched off origin/main instead of pulling)
- [x] Read the card description
- [ ] Read linked prior card f4d1721f + any plan doc
- [x] Create worktree `.claude/worktrees/wt1` + branch `feature/ai-difficulty-scaling` (port 5281, `eaweb-wt1`)
- [ ] Push branch

## Phase 2: Research
- [ ] Read `PlayerShip` AI constants + `?ai*` override flags
- [ ] Read `?aibench` harness — how it scores, what it outputs
- [ ] Find where `Settings.CurrentDifficulty` lives + its tiers
- [ ] Identify every AI consumer (attract demo, Mechanical Friends, co-op bots)
- [ ] Blast radius: does difficulty change mid-session?

## Phase 3: Design
- [ ] Write `plans/ai-difficulty-scaling.md` (Context / Design / Verification / Out of scope)
- [ ] Get user approval before coding
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Per-tier constant table + lookup
- [ ] Keep `?ai*` overrides winning over the tier values
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` if a convention/flag changes

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] `?aibench` runs per tier showing a monotonic skill gradient
- [ ] Real-game smoke check, zero console exceptions (Chrome, port 5281)
- [ ] Spot-check the diff
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] Pull origin/main into the branch, resolve conflicts
- [ ] Re-verify
- [ ] PR + self-merge (`--merge`)
- [ ] Remove worktree, prune, delete branches
- [ ] Delete plan + tracker docs
- [ ] Move card to Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server on 5281
- [ ] Close remaining browser tabs
