# Tracker: feature/tutorial-improvements

Card: `4aab0629` — Improve the tutorial · Worktree: `wt3` · Dev port: `5283`

## Phase 1: Pick Up the Card
- [x] Create + claim the card (Backlog -> In Progress) — no existing tutorial card, created 4aab0629
- [x] Pull latest main
- [x] Create worktree wt3 + branch feature/tutorial-improvements, push -u
- [ ] Read the card / firm up scope with the user

## Phase 2: Research
- [x] Read the tutorial code (TutorialLevel / TutorialMessage(Event) / GameEventList / spawners)
- [x] Trace how tutorial steps/messages are driven
- [x] Summarize findings + candidate improvements

### Findings
- Scope (user): (1) pacing punch-up — overlap text with action, kill dead waits;
  (2) visual punch-up — more holodeck/simulation feel, channel-flip-style glitch, fullscreen
  scanline/tint filter.
- `GameEventList`: events run CONCURRENTLY until an `AddHalt()` after a halting event.
  `TutorialLevel.message()` always adds halting+AddHalt => text and spawners are strictly
  serial today. Overlap = add the message event with `halting:false` (no AddHalt) and let the
  spawner be the halting event. NB: two TutorialMessages at once would draw on top of each
  other at y=85 — overlap text-with-ACTION, not text-with-text.
- Powerup lessons: `message` (6.5s halting) -> `bonusWave` (4s spawner, halting) -> `wait(9.5)`.
  ~16-20s per powerup, mostly reading/waiting.
- Background already has holodeck systems: `SetSimpleSpace()` cyan grid layers, `Jump()`
  glitch-slip, light-pulse sweep (`DrawHoloPulse`), `isHolodeck`.
- `channelflip.fx` = TV glitch (row jitter/skew + scanlines + static + contrast) as a
  two-texture crossfade; its distortion recipe is reusable for a fullscreen sim filter.
- Fullscreen post-process seam: `Game1.ApplySlowmoTrail` pattern — operate on `sceneTarget`
  before the gamma present blit, raw `spriteBatch` identity. New `.fx` => re-run
  `tools/shaders/build_shaders.py`.

## Phase 3: Design
- [x] Draft approach (file-by-file) in this tracker
- [ ] Align with the user before writing code

### Design (proposed)
**A. Pacing (`TutorialLevel.cs`)**
- Add non-halting message plumbing (`message(text, halting:false)` variant).
- Trim the cold open (wait(4) + two intro lines -> shorter, overlapping).
- Powerup section: text appears AND the bonus UFOs start streaming the same instant
  (message non-halting, spawner halting); cut the fixed 9.5s waits down / gate on pickup.
- Optional: WaitForPickupEvent (advance as soon as the player grabs the powerup).
**B. Visuals**
- New `holosim.fx` fullscreen filter (scanline + subtle cyan/green tint + vignette +
  occasional interlace roll; intensity param). Applied on `sceneTarget` at the
  ApplySlowmoTrail seam while the tutorial runs; bursts to high intensity on
  "Activating/Terminating Tutorial" + on Background.Jump() glitches (channel-flip feel).
- Debug knobs `?holofilter=` etc (null => baked defaults), verify via `?level=Tutorial`.

## Phase 4: Implement
- [ ] Make the changes
- [ ] Update CLAUDE.md if a new flag/convention is introduced

## Phase 5: Verify
- [ ] dotnet build -c Debug clean
- [ ] Run on :5283, verify in real Chrome (claude-in-chrome), zero console exceptions
- [ ] Spot-check the diff
- [ ] Flag anything needing manual testing

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Pull main into branch, re-build
- [ ] PR + self-merge, pull main at root
- [ ] Remove worktree wt3 + branches
- [ ] Delete this tracker
- [ ] Card -> Done + closing comment
- [ ] Follow-up cards if needed
- [ ] Overview for the user

## Phase 7: Clean up
- [ ] Kill dev server, close Chrome tabs
