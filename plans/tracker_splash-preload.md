# Tracker: fix/splash-preload

Card: "Do all the assets etc of the game (or at least main menu) actually preload during the splash screens?"
id `b92f957e4574513792f25388`

## Phase 1: Pick Up the Card
- [x] Claim the top card (Backlog -> In Progress) via atomic grab
- [x] Pull latest main
- [x] Read the card (no description — investigation card)
- [ ] Create worktree (slot wt1) + branch

## Phase 2: Research
- [ ] Find splash screen code + timing
- [ ] Find menu content warming (Game1.WarmMenuContent)
- [ ] Find level preload system (BeginPreload/manifest)
- [ ] Determine: does preload happen DURING splash, or after?
- [ ] Summarize findings

## Phase 3: Design
- [ ] Draft approach
- [ ] Align with user

## Phase 4: Implement
- [ ] Make changes

## Phase 5: Verify
- [ ] Build clean
- [ ] Run + look (real Chrome, console)
- [ ] Spot-check diff

## Phase 6: Review & Ship
- [ ] Commit
- [ ] /review
- [ ] Pull main into branch
- [ ] PR + self-merge
- [ ] Clean up worktree
- [ ] Delete tracker
- [ ] Move card to Done + comment
- [ ] Overview for user
