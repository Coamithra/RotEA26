# Tracker: fix/bg-tile-seams

Card: `4ddcd13f` — "got a bit of a regression in the gfx"
Symptoms (levels 2 & 3): seams between background tiles; a bright vertical line on the
right-hand side (suspected too-small fog/darkening texture). Suspected cause: the move to
DXT-compressed textures.

## Phase 1: Pick Up the Card
- [x] Claim top Backlog card with `trello grab`
- [x] Pull latest main
- [x] Read the card (desc + attached screenshot)
- [x] Create worktree `.claude/worktrees/wt1` + branch `fix/bg-tile-seams` (port 5281)

## Phase 2: Research
- [x] Find the background tile draw path (levels 2/3)
- [x] Find the fog / darkening overlay draw
- [x] Read `tools/CLAUDE.md` texture pipeline + the DXT conversion change
- [x] Identify root cause of the seams and of the right-hand bright line

## Phase 3: Design
- [x] Settle approach, write `plans/bg-tile-seams.md`
- [x] User approval
- [x] Post short approach comment on the card

## Phase 4: Implement
- [x] Apply fix

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug`
- [x] Tool-driven visual verification (harness / scrub scene — build one if none fits)
- [x] Real-Chrome smoke on port 5281, zero console errors
- [x] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review`, fix every finding
- [ ] `git pull origin main`, re-verify
- [ ] PR + self-merge from root checkout
- [ ] Remove worktree + branch
- [ ] Delete plan + tracker
- [ ] Card -> Done + summary comment
- [ ] Follow-up cards if needed
- [ ] User overview

## Phase 7: Clean up
- [ ] Kill dev server, close Chrome tabs
