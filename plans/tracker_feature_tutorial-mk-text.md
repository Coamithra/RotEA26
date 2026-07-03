# Tracker: feature/tutorial-mk-text

Card 70e30aa7 — "Tutorial mode for M&K users": based on detected input device, show
mouse&keyboard text rather than joystick in the tutorial mode.

## Phase 1: Pick Up
- [x] Move card Backlog -> In Progress
- [x] Pull main
- [x] Read card
- [x] Worktree wt6 + branch feature/tutorial-mk-text

## Phase 2: Research
- [x] TutorialMessage.cs / TutorialMessageEvent.cs / TutorialLevel.cs (strings)
- [x] Gamepad-specific strings live ONLY in TutorialLevel.PopulateEventList:
      - "Use Left Stick to Move"
      - "Use Right Stick to Fire"
      - "Press Left or Right Trigger to activate a\nbomb"
- [x] Detection: oracle.DeviceIsPlaying(ControlDevice) — safe, non-throwing, used
      everywhere in GameScene for keyboard detection.
- [x] Player 0's device = the starter that launched the level (MenuFinished ->
      oracle.AddPlayer(starter)); keyboard OR mouse-click => ControlDevice.Keyboard;
      real gamepad => PadOne..PadFour. Touch injects keyboard keys (DebugInput), and
      Generic has no PlayerShip movement case, so keyboard is the correct default.
- [x] Keyboard bindings (keysToCheck): move = arrows/WASD; PlayerShip Keyboard case:
      fire = hold Mouse1 aim at cursor; bomb = Mouse2 (right-click).

## Phase 3: Design
- [x] Add helper in TutorialLevel: bool UsingGamepad => any Pad device playing.
- [x] Swap the 3 device strings conditionally; keep gamepad phrasing for gamepad users.

## Phase 4: Implement
- [ ] Edit TutorialLevel.cs

## Phase 5: Verify
- [ ] dotnet build -c Debug (no live browser per orchestrator override)
- [ ] Re-read full diff

## Phase 6: Ship
- [ ] Commit + push
- [ ] Self-review (rtk git diff main...HEAD)
- [ ] PR --fill (DO NOT MERGE — pause for orchestrator)
