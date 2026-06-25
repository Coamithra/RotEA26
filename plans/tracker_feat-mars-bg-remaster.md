# Tracker: feat/mars-bg-remaster

Card: `51de2925` — Remaster Level 2 (Mars) background — re-source parallax layers
Board: RotEA26 local `10989a3d` (In Progress)

## Phase 1: Pick up
- [x] Create + claim card (In Progress)
- [x] Pull latest main
- [x] Worktree `.claude/worktrees/mars-bg-remaster` + branch `feat/mars-bg-remaster`, pushed
- [x] Tracker doc

## Phase 2: Research (DONE in main session)
- [x] Map Level 2 assets; confirm BG is the only un-remastered piece
- [x] Read `Background.SetMars()` + `BackgroundImage` draw/scroll/mirror
- [x] Alpha-profile each layer (horizon framing)
- [x] Read `tools/earth/build_earth.py` as pipeline template

## Phase 3: Design
- [x] Settle approach (re-source NASA panorama → 4 layers, supersample + size-compensation)
- [x] Write `plans/mars-bg-remaster.md`
- [x] Source = Mars Pathfinder Presidential Pan (user: "Sojourner 1997 is what the originals are based on")
- [x] Downloaded tools/mars/sources/pathfinder_presidential_pan.jpg (6230x1079, public domain)
- [x] User confirmed construction: mars1-6 = horizontal slices of the HORIZON band (distant plain+Twin Peaks), above deck/rover
- [ ] Comment approach TLDR on card

## Phase 4: Implement
- [ ] `tools/mars/sources/` — sourced panorama(s)
- [ ] `tools/mars/build_mars.py` — decompose → clouds-background / marshills / mars1-6 / clouds-foreground2
- [ ] Regenerate `wwwroot/Content/GFX/MarsBG/*` (supersampled, straight alpha)
- [ ] `Background.SetMars()` — size-compensation so realsize/scroll/mirror unchanged
- [ ] Update CLAUDE.md gotchas (new tools/mars step) if a new convention lands

## Phase 5: Verify
- [ ] `dotnet build` clean
- [ ] Real Chrome `?level=Level2` — crisp, no seam, framing/parallax matches original; console clean
- [ ] Cache-bust hard reload when swapping textures
- [ ] Check Level-2 preload payload delta (background only, not boot payload)

## Phase 6: Ship
- [ ] `/review`, fix findings
- [ ] Pull main into branch, re-verify
- [ ] PR + self-merge (--merge), fast-forward root main
- [ ] Remove worktree + branch
- [ ] Delete tracker + plan
- [ ] Card → Done + comment
- [ ] User overview

## Phase 7: Clean up
- [ ] Stop worktree dev server, close Chrome tab
