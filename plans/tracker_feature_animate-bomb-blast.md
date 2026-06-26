# Tracker: feature/animate-bomb-blast

Card: a305affa0e24d90b4a363171 — "the bomb explosion looks quite static, can we animate it,
perhaps just a rotation will make it look better, or we layer 2 together (each half the alpha
of before) and rotate both in different directions."

## Phase 1: Pick Up the Card
- [x] Atomic grab top Backlog card (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree wt4 + branch feature/animate-bomb-blast

## Phase 2: Research
- [x] Found bomb explosion = Blast.cs (GFX/Sprites/blast, single-frame, no rotation)
- [x] Traced Draw path (AlienDrawableGameComponent.Draw -> single-frame branch, rotation never set)
- [x] Viewed blast sprite: blue plasma ball with crackly textured rim (rotation will show)

## Phase 3: Design
- [x] Counter-rotating two half-alpha layers + time-based spin (the card's richer option)
- [x] Blast already registered in HarnessRegistry; gate a scale baseline so harness shows it

## Phase 4: Implement
- [x] Override Draw in Blast: two layers, half alpha each, opposite time-based spin
- [x] Random initial rotation per blast (desync); harness-gated visible scale baseline

## Phase 5: Verify
- [x] dotnet build -c Debug clean (0 errors)
- [x] Run on port 5284, Chrome, harness=blast: renders, rim rotates between captures, console clean

## Phase 6: Ship
- [ ] Commit, /review, pull main, PR + self-merge
- [ ] Clean up worktree, delete tracker
- [ ] Move card to Done + comment
