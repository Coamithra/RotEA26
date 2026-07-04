# Tracker: feature/spiderboss-mothership-helper

Card 8134a68b — "Spiderboss - mothership 'helper' addition"

## Phase 1: Pick Up the Card
- [x] Claim top card (grab -> In Progress)
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree wt8 + branch

## Phase 2: Research
- [x] Read SpiderBoss.cs / SpiderBossEvent.cs / Level2.cs
- [x] Read Boss.cs / MarsBoss.cs (mothership sprite draw + A/B swap)
- [x] Read KillableAlien / AlienDrawableGameComponent / Lazer / Bullet
- [x] Understand damage path: SpiderBoss only takes damage from Lazer; big-UFO lazers are the (confusing) normal source
- [x] Mothership sprite: mothershipA/B, 1827x1827, 4x4, ~456px frame

## Phase 3: Design
- [x] Design SpiderHelperMothership component + trigger in SpiderBoss + debug plumbing

## Phase 4: Implement
- [x] New Game/EvilAliens/SpiderHelperMothership.cs
- [x] SpiderBoss idle-timer trigger + spawn
- [x] Bullet.cs: bullets stop on the helper (no combo farm)
- [x] DebugFlags: ?spiderboss + ?spiderhelper* tuning flags
- [x] Harness registry + harness.html entry
- [x] Level2 ?spiderboss quick-boot path (PopulateSpiderBossOnly)
- [x] CLAUDE.md doc bullet
- [x] FIX: float32-DDA hang from perfectly-vertical laser -> tilt FireTilt=0.02 (reproduced in float32)

## Phase 5: Verify
- [x] dotnet build -c Debug clean
- [x] Harness view: bottom-half framing correct (hoverY 10)
- [x] ?level=Level2&spiderboss&invuln live: helper appears bottom-half, fires down, boss bleeds green, boss defeated -> Victory, NO HANG
- [x] Zero console exceptions
- [x] DDA hang reproduced + fixed deterministically (scratchpad/dda_repro2.py)

## Phase 6: Ship
- [ ] Commit, /review, fix findings
- [ ] pull main, PR, self-merge, Pages green
- [ ] worktree + branch cleanup, delete tracker
- [ ] card -> Done + comment
- [ ] "For me" ticket: tuning flags + harness
- [ ] Follow-up card: harden CollisionHandler DDA vs near-axis-aligned lines
- [ ] user overview
