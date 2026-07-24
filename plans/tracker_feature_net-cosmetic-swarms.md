# Tracker: feature/net-cosmetic-swarms

Card `9a3175d0` — *Net: replicate background COSMETIC swarms as one 'effect on/off' event,
not per-entity*. Worktree `.claude/worktrees/wt4` (port 5284).

**User constraint this session: NO live browser testing (user is overwatching the screen).**
Verification must be offline: build + isolation sims + decompiled/IL diff + code reading.
Anything that genuinely needs real Chrome gets flagged to the user, not faked.

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (got 9a3175d0)
- [x] Pull latest main
- [x] Read the card (no comments; description is the spec sketch)
- [x] Create worktree + branch

## Phase 2: Research
- [x] Read NetIdRegistry / ComponentAdded seam + NetTypeRegistry
- [x] Read FlyingSpiderDescriptor + FlyingSpider background form
- [x] Read the NetBackgroundOp beat path (send, apply, JIP catch-up)
- [x] Find the background swarm spawner(s) (?flyspiders rig + Level2 real swarm)
- [x] Confirm nothing gameplay-visible reads background spiders (every AI consumer gates on Collides)
- [x] Check whether any other pure scenery is replicated + non-collidable -> background asteroids, IN SCOPE

## Phase 3: Design
- [x] Write plans/net-cosmetic-swarms.md
- [x] Present plan, get user approval BEFORE coding (both kinds; ship with the browser run flagged)
- [x] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Instance-level NetCosmeticOnly opt-out
- [ ] Spawner-level replication beat (+ JIP replay)
- [ ] Docs: web/EvilAliensWeb/CLAUDE.md (+ root CLAUDE.md if a flag/convention lands)

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Offline proof (sim / harness / data) that cosmetics are no longer registered and the beat replays
- [ ] Diff spot-check (case-sensitive paths, no BlendState.AlphaBlend, no codegen re-run)
- [ ] Flag to the user whatever still needs a live two-window check

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] Pull main into branch, resolve conflicts by the runbook rules
- [ ] Re-verify
- [ ] PR + self-merge (`--merge`)
- [ ] Remove worktree, prune, delete branch (local + remote)
- [ ] Delete plan + tracker docs
- [ ] Move card to Done + summary comment (real newlines)
- [ ] Follow-up cards for anything out of scope
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] No dev servers started (no live testing this session) — confirm nothing left running
