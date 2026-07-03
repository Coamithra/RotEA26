# Tracker: fix/mars-shadow-sprite-size (card 225c041e)

## Phase 1: Pick Up
- [x] Move card 225c041e -> In Progress
- [x] Pull latest main
- [x] Read card + CONTRIBUTING + CLAUDE
- [x] Create worktree wt6 + branch, push

## Phase 2: Research
- [x] Locate shadow asset: wwwroot/Content/gfx/sprites/shadow.png
- [x] git log --follow: 82x40 (d8753c2, orig) -> 328x160 (c5fad9c)
- [x] Draw chain: Floor.cs Draw (line 63) + CollidesWith item.size (line 124)
- [x] Registry: AlienDrawableGameComponent DesignFrameWidth "GFX/Sprites/shadow"=82 (added c5fad9c)
- [x] Root cause: DOUBLE compensation (item.size already /shadowimage.Width, draw ALSO /SuperSampleFactor=4) => 1/4 size
- [x] Also: current 328x160 content is BROKEN (mean alpha 3.5, scattered scribble, not a gradient)
- [x] Confirmed original 82x40 is clean black radial-gradient ellipse

## Phase 3: Design
- [x] Decision: revert asset to exact original 82x40 (option a) + remove SuperSampleFactor double-divide + drop unused registry entry
- [x] Restores exact original on-screen size (matches decompiled Floor.cs)

## Phase 4: Implement
- [x] Revert shadow.png to d8753c2 version (82x40)
- [x] Floor.cs: remove / SuperSampleFactor(...) from draw
- [x] AlienDrawableGameComponent.cs: drop shadow registry entry + comment ref

## Phase 5: Verify
- [x] dotnet build -c Debug clean
- [x] Verify shadow.png dims 82x40, gradient content (Pillow)
- [x] Re-read diff
- [ ] Note: needs live Mars/Level2 visual check post-hoc

## Phase 6: Ship (PAUSE before merge per orchestrator)
- [x] Commit + push
- [ ] /review
- [ ] pull main
- [ ] PR create --fill (NO merge)
- [ ] Report back
