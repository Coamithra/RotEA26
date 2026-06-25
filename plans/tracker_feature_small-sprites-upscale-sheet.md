# Tracker: feature/small-sprites-upscale-sheet

Card: `5d1746bf` — "small sprites upscale effort"
Goal: gather all small in-game sprites NOT yet HD-ified (bullets, etc.) into one
labelled grid image so the user can run it through ChatGPT for an upscale pass,
then hand the result back for re-integration.

## Phase 1: Pick Up the Card
- [x] Claim the top card (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree (slot wt5) + branch

## Phase 2: Research
- [x] Inventory all sprite assets under wwwroot/Content/gfx (+ dims)
- [x] Determine HD-ified vs raw OG-size via current png dims vs original .xnb dims (tools/xnb/xnb.py)
- [x] Identify the "small" sprites still at original low res (15 small + 4 medium statics)
- [x] Confirm each is in-use in Game/ (grep) + found tools/upscale/UPSCALING.md prior workflow

## Phase 3: Design
- [x] Decide sheet layout (magenta #FF00FF, uniform grid, dark label strip, NN upscale, manifest)
- [x] Write tools/upscale/build_contact_sheet.py
- [x] Align with user: TWO sheets (smalls + mediums), magenta background

## Phase 4: Implement
- [x] Build smalls.png (5x3) + mediums.png (2x2) on magenta
- [x] Emit per-set JSON manifest (cell geometry + native dims + scale) for part-2 slicing
- [x] Document in UPSCALING.md (new "Bulk contact sheet" section + scripts row + paste prompt)

## Phase 5: Verify
- [x] Opened both sheets — every sprite present, labelled, on magenta grid, correctly scaled
- (no C# changed -> no dotnet build / browser verify needed)

## Phase 6: Ship (PART 1)
- [x] Commit + push the script, sheets, manifests, doc to the feature branch
- [ ] Hand sheets + ChatGPT prompt to user; keep card In Progress for part 2
- [ ] Comment on card (part 1 done, awaiting user's ChatGPT pass)

## PART 2 (later — when user returns the HD result)
- [ ] Slice each cell by manifest sprite_area, chroma-key magenta
- [ ] repack_landed.py footprint-match each to origW*factor at original bbox-centre
- [ ] Register names in AlienDrawableGameComponent.DesignFrameWidth + fix direct-draw sites
- [ ] Swap pngs in (back up *.png.orig), verify in-game (cache-bust!), then ship + close card

## Notes
- 2-part card: PART 1 (now) = produce the upscale-prep sheets. PART 2 (later, when user
  returns the HD result) = slice + re-integrate. Card stays In Progress between the two.
- Sets / cell geometry live in SETS at top of build_contact_sheet.py — edit there to
  add/move/drop a sprite. Glows (singleconnectorglow, connector, shadow, blast,
  awardmentblade) are gradients = weaker AI candidates; left in for the user to choose.
