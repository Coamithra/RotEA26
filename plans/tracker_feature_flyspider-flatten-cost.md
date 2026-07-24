# Tracker: feature/flyspider-flatten-cost

Card `9c92962e` — "Background FlyingSpider group-flatten: measure it properly, then decide"
Worktree slot: `.claude/worktrees/wt12` · dev port **5304** (served manually, launch.json untouched)

Picked back up 2026-07-24 after the original session died mid-flight (card comment has the
handoff state; the rig + GL-call numbers were already committed as 99094b5).

## Phase 1: Pick Up the Card
- [x] Claim the card (moved 9c92962e → In Progress; re-claimed on pickup)
- [x] Fetch latest origin/main
- [x] Read the card + linked notes
- [x] Create worktree + branch (wt12, `feature/flyspider-flatten-cost`; recreated on pickup)
- [x] Push branch upstream

## Phase 2: Research
- [x] Read `SpriteBatchWrapper.BeginGroupFlatten/EndGroupFlatten`
- [x] Read `FlyingSpider` draw path + the background/foreground split
- [x] Read the `?flyspiders` fast-boot + the FPS HUD GL-call counter
- [x] Find why background spiders accumulate (Collides=false) and how to pin population

## Phase 3: Design
- [x] Write `plans/flyspider-flatten-cost.md`
- [x] Get user approval
- [x] Post TLDR comment on card

## Phase 4: Implement
- [x] Pinned-population A/B harness knob (`?flyspidercount=`, `?flyspiderflatten=`,
      `?flyspiderbox=`, `eaFlySpiders()`, `?harness=flyingspiderbg` — commit 99094b5)
- [x] Measurement: GL-call matrix (per +1.97 calls/spider; swarm ~1 call total; the ms matrix
      was deliberately DROPPED — rationale in the plan's Results)
- [x] Decision applied: `DebugFlags.FlySpiderFlatten` defaults to `Swarm`; `Level2` constructs
      the driver on every normal boot; `per|0` remain as A/B overrides
- [x] Doc update in `web/EvilAliensWeb/CLAUDE.md` next to the flatten bullet (+ the flying-spider
      feature bullet + DebugFlags/Level2/FlyingSpiderSwarm comments)
- [x] Merge origin/main into the branch (was ~100 commits behind; conflicts in root CLAUDE.md +
      index.html resolved keep-both)

## Phase 5: Verify
- [x] Clean Debug build
- [ ] Default actually moved: fresh `?flyspiders&flyspidercount=40` boot with no flatten flag
      reports `flatten=Swarm` + swarm-level GL calls (~102, not ~180)  ← NEEDS BROWSER
- [ ] Frozen-bench visual: swarm vs per silhouette unchanged per spider  ← NEEDS BROWSER
- [ ] Final smoke: plain `?level=Level2&invuln`, zero console exceptions  ← NEEDS BROWSER
- [ ] Close browser tabs

**BLOCKED (2026-07-24): user is gaming — no live browser testing until they're done.**

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
