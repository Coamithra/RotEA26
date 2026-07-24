# Tracker: feature/local-plus-online-mp

Card `4d904410` — "Local multiplayer AND online multiplayer"
Worktree `.claude/worktrees/wt8` · port `5288` · branch `feature/local-plus-online-mp`

## Phase 1: Pick Up the Card
- [x] Claim the card atomically (`grab`)
- [x] Pull latest `main`
- [x] Read card (no comments, no linked plan)
- [x] Create worktree + branch (wt8)
- [ ] Push branch

## Phase 2: Research
- [ ] Read local-multiplayer (controller join / player slots) code
- [ ] Read online co-op net layer (peer/player id mapping, authority)
- [ ] Trace the call chain for a mid-game local join while online
- [ ] Identify blast radius + failure modes

## Phase 3: Design
- [ ] Write `plans/local-plus-online-mp.md`
- [ ] Get user approval
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Changes per plan
- [ ] Update CLAUDE.md docs if a new flag/convention lands

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Tool-driven verification (harness/sim/debug flag — NOT timed live screenshots)
- [ ] Real-game smoke in Chrome, zero console exceptions
- [ ] Diff spot-check (no `content/`, no `BlendState.AlphaBlend`, no codegen re-run)

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per rules, re-verify
- [ ] PR + self-merge (`--merge`)
- [ ] Clean up worktree/branch/plan/tracker
- [ ] Move card to Done + summary comment (real newlines)
- [ ] Follow-up cards
- [ ] Closing overview for user

## Phase 7: Clean up
- [ ] Stop dev server on 5288
- [ ] Close verification browser tabs
