# Tracker: feature/flyspider-flatten-cost

Card `9c92962e` — "Background FlyingSpider group-flatten: measure it properly, then decide"
Worktree slot: `.claude/worktrees/wt12` · dev port **5304** (served manually, launch.json untouched)

## Phase 1: Pick Up the Card
- [x] Claim the card (moved 9c92962e → In Progress)
- [x] Fetch latest origin/main
- [x] Read the card + linked notes
- [x] Create worktree + branch (wt12, `feature/flyspider-flatten-cost`)
- [ ] Push branch upstream

## Phase 2: Research
- [ ] Read `SpriteBatchWrapper.BeginGroupFlatten/EndGroupFlatten`
- [ ] Read `FlyingSpider` draw path + the background/foreground split
- [ ] Read the `?flyspiders` fast-boot + the FPS HUD GL-call counter
- [ ] Find why background spiders accumulate (Collides=false) and how to pin population

## Phase 3: Design
- [ ] Write `plans/flyspider-flatten-cost.md`
- [ ] Get user approval
- [ ] Post TLDR comment on card

## Phase 4: Implement
- [ ] Pinned-population A/B harness knob
- [ ] Whatever the measurement decides (keep / swarm-wide RT / drop)
- [ ] Doc update in `web/EvilAliensWeb/CLAUDE.md` next to the flatten bullet

## Phase 5: Verify
- [ ] Clean Debug build
- [ ] Measurement run, focused Chrome, pinned N, background vs foreground
- [ ] Zero console exceptions
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` and fix every finding
- [ ] Pull origin/main, resolve conflicts, re-verify
- [ ] PR + self-merge
- [ ] Clean up worktree/branch/plan/tracker
- [ ] Card → Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview

## Phase 7: Clean up
- [ ] Stop dev server on 5304
