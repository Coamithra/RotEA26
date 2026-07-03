# Tracker: fix/credits-narration-delay

Card 6d7a4b64: voice sfx in the post-level crawl should start ~2-3s later.

## Phase 1
- [x] Claim card (Backlog -> In Progress)
- [x] Pull latest main (fresh worktree off main)
- [x] Read card
- [x] Create worktree wt3 + branch

## Phase 2 Research
- [x] Post-level crawl = CreditsScene.cs; VO fired via SoundManager.PlayNarration("victor_levelN")
- [x] SetupLevelN() calls PlayNarration immediately, called in Game1.gameScene_OnFinished BEFORE scene added
- [x] Initialize() is the per-showing reset point (runs each re-add of the singleton)
- [x] Timer semantics understood (countdown, Finished at 0, Reset/Start)
- [x] Skip path: Update -> Terminate() -> StopNarration(); terminated guard idempotent

## Phase 3 Design
- [x] Store pending VO name in Setup; start a 2500ms delay timer in Initialize; fire once in Update after it finishes
- [x] Must not fire if skipped before delay elapses (terminated guard)

## Phase 4 Implement
- [ ] Add delay timer + pending narration field + played flag
- [ ] Route SetupLevelN PlayNarration through pending field
- [ ] Initialize resets timer + flags
- [ ] Update fires narration once when timer finishes (and not terminated)

## Phase 5 Verify
- [ ] dotnet build -c Debug clean
- [ ] Re-read full diff

## Phase 6 Ship
- [ ] Commit + push
- [ ] /review
- [ ] PR create (PAUSE before merge per orchestrator)
