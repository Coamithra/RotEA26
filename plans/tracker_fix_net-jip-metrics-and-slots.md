# Tracker: fix/net-jip-metrics-and-slots (card 48ab9b2f)

Worktree slot: `.claude/worktrees/wt8` · dev port 5288

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (48ab9b2f)
- [x] Fetch latest main (root checkout dirty — branched off `origin/main` d8e403f)
- [x] Read the card description
- [x] Create worktree + branch

## Phase 2: Research
- [x] Read `SubMenuOnlineGames.cs` players column + `NetListing`
- [x] Read the puppet correction path + the snapUnk/pupPops metric sources
- [x] Read web CLAUDE.md "JIP pass" bullets + metric definitions
- [x] Decide what is measurable without the throttled two-window rig

## Phase 3: Design
- [x] Write `plans/net-jip-metrics-and-slots.md`
- [x] Present plan, get user approval
- [x] Post TLDR comment on the card

## Phase 4: Implement
- [x] Players column denominator -> `Oracle.MaxPlayers`; `?gamebrowser` fakes vary 1..3
- [x] `SnapUnknownKind` + split `snapNew`/`snapDead`/`snapBad` counters
- [x] Derived `snapTurn` (`NetSession.SnapshotTurnMs`) on the `[net]` line
- [x] `eaNetSnap()` attribution self-test (`Compat/Net/NetSnapshotTest.cs`)
- [x] `--population` sweep on `tools/sim/net_puppet_drive_sim.py`
- [x] Docs: web CLAUDE.md metrics heuristic, pupPops/snapTurn, JIP trap 6, console list

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug`
- [x] `eaNetSnap()` 17/17 PASS in real Chrome, identical across 3 back-to-back runs
- [x] NEGATIVE CONTROL: misattributing the kind fails exactly 3 checks; reverted
- [x] `?gamebrowser` shows Players 1/4, 2/4, 3/4 across the varied entries
- [x] `[net]` line formats correctly in a live `?net=host` boot (all fields in place)
- [x] MEASURED the rig's live count: `?flyspiders` settles at liveIds 17-19, snapTurn 120ms
- [x] Both sim modes exit 0
- [x] Zero console exceptions across boot / menu / gamebrowser / net-host level
- [ ] Close browser tabs

## Phase 6: Ship
- [ ] Commit + push
- [ ] `/review` and fix every finding
- [ ] Pull main, resolve conflicts, re-verify
- [ ] PR + self-merge, clean up worktree/branch/plan/tracker
- [ ] Card -> Done + summary comment + follow-up cards
      (follow-ups: re-measure JIP now the counters are decidable; replicate background
       cosmetics as ONE "start this effect" event instead of per-entity — user's idea)
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server, close tabs
