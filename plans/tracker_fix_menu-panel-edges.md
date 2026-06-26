# Tracker: fix/menu-panel-edges

Card: "the main menu panels need redrawing (those square lines look funky at edges)"
Card id: 89afe57c3209a99581167b87 (In Progress)

## Phase 1: Pick Up the Card
- [x] Atomic grab (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card (empty desc; visual polish on main-menu panels)
- [x] Create worktree wt6 + branch fix/menu-panel-edges (port 5286)

## Phase 2: Research
- [x] Read MenuSubWithSkull.cs panel-drawing code (DrawFrameFill / DrawFrameOutline / DrawLine)
- [x] Read MenuTheme.cs palette
- [ ] Run + screenshot the live main menu to SEE the funky edges
- [ ] Diagnose root cause of the funky corners/edges

## Phase 3: Design
- [ ] Draft fix approach
- [ ] Align with user if scope is large

## Phase 4: Implement
- [ ] Fix the panel edge drawing

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on 5286, view main menu in real Chrome, screenshot before/after, console clean

## Phase 6: Ship
- [ ] Commit, /review, pull main, PR + self-merge
- [ ] Clean up worktree + branch, delete tracker
- [ ] Move card to Done, comment, follow-ups
- [ ] Overview for user
