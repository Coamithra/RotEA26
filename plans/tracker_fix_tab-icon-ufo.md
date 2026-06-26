# Tracker: fix/tab-icon-ufo

Card: "The tab icon needs improving (standard green alien atm, should be the ufo sprite)" (979fb6be35215c9de0256013)
Decision: favicon based on the **player ship** `ufosheet` (a single frame).

## Phase 1: Pick Up the Card
- [x] Atomic grab (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card (desc empty; title is the spec)
- [x] Create worktree (slot wt4) + branch + push

## Phase 2: Research
- [x] Find favicon refs (wwwroot/index.html line 22 -> favicon.svg)
- [x] Find ufo sprites (ufosheet 8x4 player ship; ufometpootjes hero shot)
- [x] Decide source: ufosheet (user-confirmed)

## Phase 3: Design
- [ ] Extract a clean frame from ufosheet.png.orig, crop, generate favicon.png (multi-size ico or png)
- [ ] Update index.html <link rel="icon">

## Phase 4: Implement
- [ ] Generate favicon asset
- [ ] Wire it in index.html
- [ ] Remove old alien-head favicon.svg if replaced

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on port 5284, real Chrome, check tab icon + zero console errors

## Phase 6: Ship
- [ ] Commit, /review, pull main, PR + self-merge
- [ ] Clean up worktree, delete tracker, move card to Done, comment
