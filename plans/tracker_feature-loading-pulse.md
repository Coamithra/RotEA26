# Tracker: feature/loading-pulse

Card 02a96ff6 — "Loading pulse indicator during the pre-launch level warm"
Optional polish follow-up to fe25712a: show subtle loading feedback while
Game1.WarmThenLaunch/PumpLevelWarm decodes textures one-per-tick.

## Phase 1: Pick up
- [x] Grab top backlog card (atomic) -> In Progress (02a96ff6)
- [x] Pull latest main
- [x] Read card + fe25712a context in CLAUDE.md
- [x] Create worktree wt3 + branch feature/loading-pulse (port 5283)

## Phase 2/3: Research + Design
- [x] Read Game1 warm plumbing (pendingLevelLaunch, PumpLevelWarm, DrawInner seam)
- [x] Read SpriteBatchWrapper DrawString/DrawStringScaled + blackPixel
- Design: option (a) — draw in C# from DrawInner, gated on `pendingLevelLaunch != null`.
  Subtle centered "LOADING" (menu font, breathing alpha) + a row of 3 marching
  pulse dots (blackPixel squares). Design space 800x600, on the black fade frame.
  No new content, no interop, no debug flag (only shows during the warm).

## Phase 4: Implement
- [ ] Add DrawLevelWarmIndicator(gameTime) to Game1
- [ ] Call it in DrawInner when pendingLevelLaunch != null
- [ ] Update CLAUDE.md fe25712a bullet if warranted

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on 5283, ?level=Level3 (biggest warm) in real Chrome, screenshot warm frame, 0 console errors

## Phase 6: Ship
- [ ] Commit, /review, fix findings
- [ ] Pull main, PR, self-merge, cleanup worktree/branch
- [ ] Delete tracker, move card to Done, comment
