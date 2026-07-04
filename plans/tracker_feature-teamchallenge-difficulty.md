# Tracker: feature/teamchallenge-difficulty

Card: 7329fcd4 — Add difficulty levels to TeamChallenge (co-op)

## Phase 1: Pick Up
- [x] Grab top backlog card (atomic) -> In Progress
- [x] Pull latest main
- [x] Read card + traced code
- [x] Create worktree wt1 + branch

## Phase 2/3: Research + Design
- [x] TeamChallenge already routed via challengeSelector -> difficultyMenu (LevelType.Challenge).
      Real issue: Initialize() calls LockDifficulty(Medium) + PlayMusic(Songs.Classic), which
      throws away the menu-selected difficulty and always plays the lyric cut.
- [x] Fix = match AsteroidChase/ClassicAliens: LockDifficulty() (respect selection) +
      PlayMusic(SoundManager.ClassicForDifficulty()).

## Phase 4: Implement
- [ ] Edit TeamChallenge.cs Initialize()
- [ ] Update CLAUDE.md note (TeamChallenge no longer hard-locks Songs.Classic)

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on port 5281, boot ?level=TeamChallenge, check console clean
- [ ] Confirm difficulty menu selection applies (music variant switches on Hard+)

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main, PR, self-merge
- [ ] Cleanup worktree, delete tracker
- [ ] Card -> Done + comment
