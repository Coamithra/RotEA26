# Tracker: fix/net-purge-filter-races

Card `74403f83` - "Net: close the standing-purge-filter races against the puppet layer (R1-R3)"
Worktree: `.claude/worktrees/wt6`, dev port `5286`, base `ae3bac5`

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (before anything else)
- [x] Fetch latest main (root checkout is dirty from another session - branched off `origin/main`)
- [x] Read the card (description; no comments)
- [x] Create worktree + branch
- [x] Push branch

## Phase 2: Research
- [x] Read `ComponentBin` purge filter (`Purge<T>` :127, `IsPendingPurged` :415, `TopOfTickFlush` :272)
- [x] Read `Game1.UpdateInner` ordering - `TopOfTickFlush` :1017 -> `base.Update` :1028 ->
      `NetSession.Update()` :1038 **(window confirmed)**
- [x] Read `NetPuppets.OnSpawn` (R1) - `bin.Add` :156, unconditional registration :171-173 **(confirmed)**
- [x] Read `NetSession.SpawnPuppet` (R2) - `bin.Add` :2160, `puppet = ship` :2161 **(confirmed)**
- [x] Read `NetSession` `bin.Add(msg)` :1854 / `bin.Add(banner)` :1889 (R3) - both already gated on
      `NetActiveScene != null`
- [x] Read `NetApplyReset` purge from inside the rx drain (GameScene :317-320)
- [x] **R2b found (card missed it):** `NetSession.Friends.cs` :219-220 has the identical shape
- [x] Wider blast radius: `BrainBoss` :533-539 purges 7 replicable enemy types standing;
      `ClassicBoss` :195; `InsaneBossI` :269 - all covered free by R1's exemption
- [x] Checked `plans/net-headless-sim.md` - design-only, medium-large card of its own; NOT the rig here
- [x] `Compat/BinTest.cs` (`eaBinTest()`) is the right rig precedent; `NetPuppets.Enable(g)` needs
      no transport/session

## Phase 3: Design
- [x] Write plan doc `plans/net-purge-filter-races.md`
- [x] Present plan, get user approval BEFORE coding (R3 = document-not-change; R2b = in scope)
- [x] Post short TLDR comment on the card

## Phase 4: Implement
- [x] R1 - `ComponentBin.Add` exempts `NetPuppets.Constructing`; `OnSpawn` verifies + `MarkRemoved`
- [x] R2 - new `ComponentBin.TryAdd`; `SpawnPuppet` adopts only on true
- [x] R2b - same for `NetSession.Friends.SpawnFriend`
- [x] R3 - documented as correct-by-symmetry at both sites (no behaviour change, per approval)
- [x] Updated `web/EvilAliensWeb/CLAUDE.md` (3 lifecycle bullets + the `eaBinTest` diagnostics bullet)

## Phase 5: Verify
- [x] Built the rig: 3 new `eaBinTest()` scenarios (`Compat/BinTest.cs`)
- [x] **Pre-fix probe reproduces R1**: 13/15, `FAIL puppet survives the standing purge filter` +
      `FAIL registry agrees with the world` while `puppet spawn reports success` PASSes = the ghost
- [x] Post-fix 15/15; 5 back-to-back runs all 15/15 (leave-no-trace, after fixing a leak my own
      scenario introduced - it left the `AlienDrawableGameComponent` filter armed)
- [x] Clean `dotnet build -c Debug` (0 errors; 38 pre-existing warnings)
- [x] Real-Chrome smoke: `?level=Level1` boots, renders, zero console exceptions
- [x] Two-tab loopback `?net=host`/`?net=join` on Level2: mirror rosters
      (`0:Keyboard*,1:Remote pri=0/1` vs `0:Remote,1:Keyboard* pri=1/0`), `remote ship joined
      slot=0` (the `TryAdd` path live), drop/sgap/ordViol/seqGap/pops all 0, zero exceptions both tabs
- [x] KNOWN HARNESS LIMIT: `pupPops` climbs + "remote ship died" flaps because MCP tabs are
      backgrounded (throttled rAF) - the documented two-visible-windows requirement, not a regression
- [x] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)
- [x] Closed browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook rules
- [ ] Re-verify after merge
- [ ] Return to root checkout
- [ ] PR + self-merge (`gh pr create --fill`, `gh pr merge --merge`)
- [ ] Clean up worktree + branch
- [ ] Delete plan + tracker docs
- [ ] Move card to Done + summary comment (real newlines)
- [ ] Follow-up cards for out-of-scope findings
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server, close tabs
