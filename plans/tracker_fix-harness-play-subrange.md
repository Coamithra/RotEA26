# Tracker: fix/harness-play-subrange

Card 00d8dd37 — Harness ?play should respect FirstFrame/LastFrame sub-ranges

## Phase 1: Pick Up
- [x] Claim card (Backlog -> In Progress, atomic grab)
- [x] Pull main
- [x] Read card + referenced code
- [x] Worktree wt1 + branch fix/harness-play-subrange

## Phase 2/3: Research + Design
- [x] HarnessScene.cs ~315 wraps curframe over rows*columns
- [x] Engine AlienDrawableGameComponent.Update ~502-508 wraps over [FirstFrame, ActiveLastFrame)
- [x] FirstFrame/LastFrame public; ActiveLastFrame private -> replicate (LastFrame>FirstFrame?LastFrame:rows*columns)

## Phase 4: Implement
- [ ] Rewrite the ?play branch in HarnessScene.Update to match engine wrap

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] ?harness=flyingspider&play -> only reared sub-range (22..30), not whole sheet
- [ ] ?harness=eyeattract&play&fps=2 still correct (whole sheet -> unchanged)
- [ ] Console clean

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main, PR, self-merge
- [ ] Clean worktree, delete tracker
- [ ] Card -> Done, comment
