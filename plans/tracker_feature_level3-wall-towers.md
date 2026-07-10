# Tracker: feature/level3-wall-towers

Card: `d59266cc` — Level 3 walls as 3D towers emerging from the fog
Plan: `plans/walls-3d-towers.md`
Worktree: `.claude/worktrees/wt2` · dev port **5282**

## Phase 1: Pick Up the Card
- [x] Claim the card (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card + plan doc
- [x] Create worktree wt2 + branch `feature/level3-wall-towers`
- [x] Push branch

## Phase 2: Research
- [x] Read `Game/EvilAliens/Wall.cs` (Draw, blocks, Setup, 8x8 cell scheme)
- [x] Read `Background.SetAlienBase*` (ground layer, 0.66 modifier, fog layers, 2331-v5)
- [x] Read `Level3.cs` walls sections (variations, event list, swapBG)
- [x] Read `CollisionLevelMap` / how walls register collision
- [x] Read an existing live-panel wiring end to end (eaLazer: index.html + DebugInput + DebugFlags)
- [x] Read `Level2.PopulateSpiderBossOnly` for the fast-boot pattern
- [x] Summarize findings

## Phase 3: Design
- [x] Confirm/adjust the plan doc against the real code
- [x] Align with the user before writing code

## Phase 4: Implement
- [x] Step 1: projection + slice pass + tints in `Wall.Draw` + `?walltowers=0` kill switch
- [x] Step 2: fog dissolve (bottom-slice alpha) + wisp pass
- [x] Step 3: DebugFlags knobs + `eaWalls` panel + `?level=Level3&wallsonly` boot
- [x] Update CLAUDE.md (new flags, panel, fast boot)

## Phase 5: Verify
- [x] `dotnet build -c Debug` clean
- [x] Run on :5282, verify in real Chrome (claude-in-chrome), console clean
- [x] `?level=Level3&wallsonly&invuln` screenshots (entry / mid / near-VP)
- [x] `?hitboxes` identical before/after
- [x] `?walltowers=0` reproduces flat look
- [x] Hitch watchdog quiet
- [x] Spot-check diff

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` and fix findings
- [ ] Pull main into branch, rebuild
- [ ] PR + self-merge
- [ ] Clean up worktree + branch
- [ ] Delete this tracker
- [ ] Card -> Done + comment + follow-ups
- [ ] Overview for user

## Phase 7: Clean up
- [ ] Kill dev server, close Chrome tabs
