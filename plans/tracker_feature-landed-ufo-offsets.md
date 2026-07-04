# Tracker: feature/landed-ufo-offsets

Card `f44ca8e2` — Landed Mars UFOs need well-centered shadows + a seamless landed→flying
takeoff (feet offset). Build an HTML tool to author per-sprite offsets + game plumbing to
consume them. Create a "For me" card for the user to tune the values.

## Phase 1: Pick Up
- [x] Grab top backlog card (f44ca8e2 → In Progress)
- [x] Pull latest main
- [x] Read the card + research code
- [x] Create worktree wt11 + branch feature/landed-ufo-offsets

## Phase 2/3: Research + Design (done)
- Landed sprites: ufometpootjes, Smallship_landed, Mediumship_landed, Mothership_landed.
- UFO.Draw stationary branch draws still centered on Position; takeoff flips stationary=false.
- Floor.cs draws generic ground shadows from each colliding component's collision box.
- Design: JSON `Content/data/landed_offsets.json` (per-sprite landed/takeoff/shadow/shadowScale,
  design px, +y down, ships all-zero so behavior unchanged). Loader `Compat/LandedOffsets.cs`
  (TitleContainer.OpenStream + tiny manual parse). Wire UFO + StationaryBoss. Suppress generic
  floor shadow for landed things (CastsFloorShadow flag) and draw own tool-controlled shadow.
- Tool: self-contained `wwwroot/landed-editor.html` (fetches real Content assets, drag sprite +
  shadow + preview flying overlay on mars ground, export JSON).

## Phase 4: Implement
- [ ] Content/data/landed_offsets.json (defaults)
- [ ] Compat/LandedOffsets.cs (loader)
- [ ] AlienDrawableGameComponent.CastsFloorShadow + Floor suppression
- [ ] UFO.cs: landed draw offset, own shadow, takeoff shift
- [ ] StationaryBoss.cs: landed draw offset + own shadow
- [ ] wwwroot/landed-editor.html tool
- [ ] Preload landed_offsets.json where landed sprites preload (optional)
- [ ] CLAUDE.md doc bullet

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on :5291, ?level=Level2 — landed ufos + shadows, console clean
- [ ] Open /landed-editor.html — drag, export works
- [ ] Confirm defaults = no behavior change

## Phase 6: Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] pull main, PR, self-merge
- [ ] worktree cleanup, delete tracker
- [ ] card → Done + comment
- [ ] Create "For me" card
- [ ] User overview
