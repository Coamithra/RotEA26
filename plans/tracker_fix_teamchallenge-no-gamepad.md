# Tracker: fix/teamchallenge-no-gamepad

Card `e6927ef8` — BUG: TeamChallenge is unplayable without a gamepad.
User constraint this session: **no live browser testing** (user is overwatching the screen).
User decisions: AI partner now + P2-keyboard as a follow-up card; build the seam and defer the
live pass (ship the PR).

## Phase 1: Pick Up the Card
- [x] Claim the card (`grab`, Backlog 79158996 -> In Progress 3b43cba3)
- [x] Pull latest main
- [x] Read the card (description; no comments)
- [x] Create worktree `.claude/worktrees/wt2` + branch `fix/teamchallenge-no-gamepad`
- [x] Push branch upstream

## Phase 2: Research
- [x] `TeamChallenge.Initialize` seating (only unconditional pad seat in the game)
- [x] `GameScene.Update` disconnected-pad pause guard (leave it alone: correct mid-run)
- [x] `PlayerShip.Update` controller switch (no `Generic` case; `Setup` reads oracle.Controller)
- [x] Survey every other seat path -> all via `CheckPlayerJoins`, device present by construction;
      `MenuScene`'s old mouse-click `PadOne` default already fixed
- [x] `MyKeys.Generic_Start` is unbound on this port (`keysToCheck[8]` empty) -> Generic needs a
      key map, not just an input case
- [x] Gamepads work on web (KNI ships `Gamepad.8.0.5.js`)
- [x] `Settings.CheckForCheats()` unaffected by a directly-seated AI -> progress still saves

## Phase 3: Design
- [x] Write `plans/teamchallenge-no-gamepad.md`
- [x] Present options, user chose the approach (AskUserQuestion)
- [x] Post short TLDR comment on card

## Phase 4: Implement
- [x] `TeamChallenge.ResolvePartnerSeat` (pure) + the seating call
- [x] `?teampartner=ai|pad` replaces `?aiteam` (DebugFlags: prop, parse, Active, log line)
- [x] `index.html` matrix drops `&aiteam`
- [x] Carousel briefing mentions the auto-pilot partner
- [x] `Compat/TeamSeatTest.cs` + `DebugInput` bridge + `window.eaTeamSeat()`
- [x] `tools/sim/logic_probe/` — headless oracle for pure C# logic (new capability)
- [x] Docs: root `CLAUDE.md` (flag + verification rule), `web/…/CLAUDE.md` (Input bullet, console
      list, stale matrix row, aiteam removal), `tools/CLAUDE.md` (the probe)

## Phase 5: Verify (no live testing this session)
- [x] Clean `dotnet build -c Debug` (0 errors, 37 pre-existing warnings)
- [x] `logic_probe` -> ALL PASS (48 cases + negative control) on the REAL resolver
- [x] Mutation test: `padConnected(i)` -> `true` gives 4 FAIL; reverted, green again
- [x] Diff spot-check (no `content/` casing, no BlendState, no codegen, no csproj change)
- [x] Deferred-live list written into the plan

## Phase 6: Review & Ship
- [x] Commit + push
- [x] `/review` branch diff, fix every finding
- [x] `git pull origin main`, resolve per rules, re-verify
- [x] PR + self-merge
- [x] Remove worktree/branch, delete plan + tracker
- [x] Card -> Done, summary comment (real newlines)
- [x] Follow-up cards (P2 keyboard device; re-measure the matrix row)
- [x] Closing overview for user

## Phase 7: Clean up
- [x] No dev servers started, no browser tabs opened (no live testing)
