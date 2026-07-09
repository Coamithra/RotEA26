# Tracker: fix/invuln-spinner-screensize

Three misc bugfix cards bundled into one branch/PR (orchestrator instructions): 5e608dba
(invulnerability persists), fdbe3be0 (menu spinner goes haywire after a level), 993db245
(remove dead "Modify Screen Size" option).

## Phase 1: Pick Up the Cards
- [x] Claim all three cards (Backlog -> In Progress)
- [x] Pull latest main
- [x] Read each card
- [x] Create worktree wt7 + branch fix/invuln-spinner-screensize

## Phase 2-4: Research / Design / Implement

### Card 5e608dba — invulnerability persists
- [x] Root cause: `Game1.startScreen_OnFinished` wrote
      `Settings.GetInstance().Invulnerability = true` directly on `?invuln`, which then
      PERSISTED via any later `Settings.SaveThreaded()` (options exit, difficulty pick, cheats
      menu exit, gamma/screen-resize exit) -- one test session with `?invuln` left every LATER
      plain boot invulnerable forever.
- [x] Found `optionsMenu_HaxSelected` (the only would-be UI toggle for this field) is DEAD CODE
      -- never wired to an `AddEntryEvent`, so there is currently NO shipped way to legitimately
      set `Settings.Invulnerability = true`. Confirmed by diffing against
      `src_decompiled/EvilAliens/MenuScene.cs`'s `playtestMenu` (a debug-only menu that was never
      ported).
- [x] Fix: `DebugFlags.Invuln` is now a pure session-only runtime override (like `?unlockall`),
      never written into Settings. `PlayerShip.CollidesWith` and `WebcamLevel.PlayerHit` (the
      two consumers) now OR in `DebugFlags.Invuln` directly.
- [x] Self-heal: `Settings.loadData` forces a deserialized `Invulnerability` back to `false` --
      safe because no code path can legitimately produce `true` in a save file except the now
      fixed bug.

### Card fdbe3be0 — menu spinner haywire after a level
- [x] Root cause: the HUD ring "autofocus hunt" (`UpdateRing` in MenuScene.cs) times its dart
      state machine (`ringMoveStart`/`ringHoldUntil`) against `timer.TotalSeconds`. `menuScene`
      is a single long-lived instance re-added to the component bin after every level/credits
      finish, and `ComponentBin.Add` calls `Initialize()` on every re-add -- which zeroes
      `timer` but never reset the ring's dart timestamps. Leaving the menu mid-dart
      (`ringHolding == false`) stores a stale `ringMoveStart` that ends up far AHEAD of the
      freshly-zeroed `timer`; `UpdateRing`'s smoothstep+Lerp extrapolates wildly for the
      deeply-negative `u` this produces, reading as the ring spinning at absurd speed for a few
      seconds until real time catches back up.
- [x] Confirmed via `git log -S ringDriftVel`: the 2026-07-03 fix (6b2c2a7) capped a DIFFERENT
      failure mode (ambient coast drift growing unbounded over a long IDLE), not this one (which
      only manifests on a menu RE-ENTRY) -- explains "we fixed this at one point... can still go
      haywire".
- [x] Fix: reset the ring's dart-machine fields (`ringAngle`, `ringFrom`, `ringTo`,
      `ringMoveStart`, `ringMoveDur`, `ringHoldUntil`, `ringHolding`, `ringDirAccumDeg`,
      `ringDrift`, `ringDriftVel`) to their field-initializer values inside `Initialize()`,
      right alongside the existing `timer = TimeSpan.Zero`. Byte-identical to first boot;
      re-entry now starts from the same calm state.

### Card 993db245 — remove "Modify Screen Size"
- [x] Confirmed `Settings.Scale` (the field this menu edits) is never read by any draw path
      (RenderScale replaced it post Stage-10) -- the option is genuinely dead, matching the
      card title.
- [x] Removed the "Modify Screen Size" entry + its `AddEntryEvent`, the `screenResizeMenu`
      field/construction/wiring, and both handler methods
      (`optionsMenu_ScreenSizeSelected`/`screenResizeMenu_OnFinished`) from MenuScene.cs.
- [x] Deleted the now fully-unreferenced `ScreenResizeMenu.cs`.
- [x] Left `Settings.Scale` itself untouched (XML-serialized field; must not remove/rename).
- [x] Confirmed no other reference (attract demo, help text, harness.html) mentions "Modify
      Screen Size" / `ScreenResizeMenu`.

## Phase 5: Verify (ORCHESTRATOR OVERRIDE: no live browser -- build clean + diff re-read only)
- [x] `dotnet build -c Debug` in `web/EvilAliensWeb` -- 0 errors, only pre-existing warnings
- [x] Re-read every diff hunk end to end
- [ ] Orchestrator live-tests post-hoc (see PR body / card comments for the checklist)

## Phase 6: Review & Ship (PAUSED per orchestrator override)
- [x] Commit(s) -- one per card, descriptive messages
- [ ] `/review` -- SKIPPED per orchestrator override (batched review later)
- [x] `rtk gh pr create --fill`
- [ ] STOP HERE -- awaiting orchestrator approval before merge/deploy/cleanup
