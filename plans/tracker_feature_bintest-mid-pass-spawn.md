# Tracker: feature/bintest-mid-pass-spawn

Card `bcdc7430` — "Bin: pin the mid-pass-spawn collision contract in eaBinTest"

## Phase 1: Pick Up the Card
- [x] Claim card atomically (`grab` → bcdc7430, Backlog → In Progress)
- [x] Read the card (desc; no comments)
- [x] Base off latest origin/main (ae3bac5)
- [x] Create worktree `.claude/worktrees/wt4` + branch `feature/bintest-mid-pass-spawn`
- [ ] Push branch upstream

## Phase 2: Research
- [ ] Read `CollisionHandler.DetectCollisions` + the PR #149 fix
- [ ] Read `eaBinTest` scenario harness (card 02d9ad67 pattern), `ComponentBin`
- [ ] Read `CollisionBox` geometry + an existing collidable (Alien) for the scratch pair
- [ ] Trace how `Game1` exposes internals to BinTest
- [ ] Blast radius

## Phase 3: Design
- [ ] Write `plans/bintest-mid-pass-spawn.md`
- [ ] Get user approval
- [ ] Post short TLDR comment on card

## Phase 4: Implement
- [ ] Game1 CollisionHandler accessor
- [ ] TestAlien scratch collidable pair + scenario
- [ ] Assertions: no throw + newborn not visited in-pass
- [ ] Self-clean via PruneIdle
- [ ] Docs (web CLAUDE.md) if a convention/flag changes

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Run `eaBinTest` in real Chrome, foreground, read console
- [ ] Negative control: prove the assertion FAILS against the reverted (buggy) DetectCollisions
- [ ] Diff spot-check (case-sensitive paths, no AlphaBlend, no codegen re-run)
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` branch diff, fix every finding
- [ ] Pull origin/main, resolve conflicts, re-verify
- [ ] PR + self-merge
- [ ] Clean up worktree/branch/plan/tracker
- [ ] Card → Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview for user

## Phase 7: Clean up
- [ ] Stop dev server, close tabs
