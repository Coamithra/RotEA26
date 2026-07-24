# Tracker: feature/wall-sheet-mips

Card `110153c7` — "Mip chain for the wall sheet 756-v1 (tower shafts alias at high side tiling)"
Worktree: `.claude/worktrees/wt1` · port 5281 (`eaweb-wt1`)

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (first)
- [x] Fetch latest `main` (origin/main = 84a95f7)
- [x] Read the card (desc; no comments)
- [x] Create worktree + branch off origin/main
- [x] Push branch

## Phase 2: Research
- [x] Read `tools/textures/build_textures.py` (pad, edge_gutter, texconv invocation)
- [x] Read `tools/textures/check_pad_bleed.py` (what it guards, level 0 only?)
- [x] Read the wall sheet build (`tools/walls/build_wall_tileable.py`) + `textures.config` entry
- [x] Read the CPU cell walk / UV emission (Game side, wall 3D) + `Wall.DefaultSideTile`
- [x] Read `tools/walls/preview_wall3d.py --ladder`
- [x] Read the doc note from 84a95f7 (over-pad is deliberate)
- [x] Determine: does the runtime even use mips? (sampler state / KNI DDS loader)

## Phase 3: Design
- [x] Settle approach, write `plans/wall-sheet-mips.md`
- [x] Get user approval
- [x] Post short TLDR comment on the card

## Phase 4: Implement
- [x] Pipeline change (mip generation)
- [x] Pad/gutter interaction fix
- [x] `check_pad_bleed.py` extended across mip levels
- [x] Runtime sampler (if needed)
- [x] Docs (`tools/CLAUDE.md` / `web/.../CLAUDE.md`)

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug`
- [x] Tool-driven proof (mip-aware sampler in preview_wall3d ladder / pad-bleed check across levels)
- [x] Live Level 3 smoke in real Chrome, zero console exceptions
- [x] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` and fix every finding
- [ ] Pull `main`, resolve, re-verify
- [ ] PR + self-merge
- [ ] Worktree/branch cleanup, delete plan + tracker
- [ ] Card → Done + summary comment
- [ ] Follow-up cards
- [ ] User overview

## Phase 7: Clean up
- [ ] Stop dev server, close tabs
