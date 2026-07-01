# Tracker: refactor/quality-cleanup-batch

Card: ddf604e591e10fefdba2d431 — "Quality cleanup: dead code, duplicated metal params, letterbox unify, dt-correct slowmo trail"

## Phase 1: Pick Up
- [x] Claim top card (Backlog -> In Progress) via atomic grab
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree wt4 + branch refactor/quality-cleanup-batch

## Phase 2/3: Research + Design
- [ ] Read all cited files/lines
- [ ] Confirm each cleanup is safe

## Phase 4: Implement (cleanup items)
- [ ] Delete dead ContentTestGame.cs + SpikeGame.cs
- [ ] Unwire/stub NewPreviewScene (banned Content.Load<Video> VFX), remove wiring in Game1.cs:297-299
- [ ] Remove unreachable playtestMenu (MenuScene.cs:323-330; rename 'Invincibility:' -> 'Invulnerability:')
- [ ] Hoist metal.fx param block into one SetMetalParams (SpriteBatchWrapper.cs:271-281 vs 439-449)
- [ ] Expose dest rect from RenderScale; use in present blit + mouse mapping (Game1.cs:846-849, RenderScale)
- [ ] dt-correct slowmo ghost-trail decay/mix (Game1.cs:886-914)
- [ ] eaTrailer onKey: only swallow game-relevant keys (index.html:343-349)
- [ ] Reset attract idle timer on mouse movement (MenuSub1.cs:332-394)
- [ ] AnimatedSprite.cs:34-60 loadData: wrap stream + BinaryReader in using
- [ ] Remove [trace] Console.WriteLines (GameScene.cs:489/675/769)
- [ ] Fix stale comments: Braineroid.cs:271 (20 frames 5x4), MenuSubWithSkull.cs:170 (RowsYOffset=96)

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run + real Chrome: menu, a level, score chrome, slowmo, trailer, attract idle
- [ ] Zero console exceptions

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main, resolve conflicts
- [ ] PR + self-merge, confirm Pages green
- [ ] Clean up worktree/branch
- [ ] Delete tracker
- [ ] Card -> Done + comment + follow-ups
- [ ] User overview
