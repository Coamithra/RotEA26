# Tracker: feature/jip-replay-bg-state (card 45a4e48d)

## Phase 1: Pick Up the Card
- [x] Claim the top card with `trello grab` (got 45a4e48d, not the eyeballed top — expected)
- [x] Pull latest main
- [x] Read the card + `plans/net-game-browser-followups.md` (#1)
- [x] Create worktree `.claude/worktrees/wt5` + branch `feature/jip-replay-bg-state` (port 5285)

## Phase 2: Research
- [x] `NetSession.PeerConnected` listedSession branch + `EvReady` -> `ReplayLive`
- [x] `Background.cs` op hooks + the state each op latches
- [x] `SoundManager.PlayMusic/StopMusic/NetApplyMusic` + the cue dedupe
- [x] `GameScene.NetApplyBackgroundOp` client-side apply
- [x] Finding: the burst must fire at **EvReady**, not PeerConnected — the joiner's own
      scene Initialize (which sets the INITIAL background/music) runs after pairing and
      would clobber anything sent earlier.

## Phase 3: Design
- [x] Write `plans/jip-background-catchup.md`
- [x] Align with the user (doodad: replay op + position; music rate: out of scope)
- [x] Comment the approach on the card

## Phase 4: Implement
- [x] `Background`: track last-op state + `NetReplayCatchUp()`; `NetSetDoodadPos`
- [x] `NetProtocol`: append `NetBackgroundOp.SetDoodadPos`
- [x] `SoundManager`: `NetCurrentSong` accessor
- [x] `GameScene`: `NetReplayDeepState()` + `SetDoodadPos` apply case
- [x] `NetSession`: fire the burst from the EvReady handler
- [x] Verification seam: `eaNetBg()` dump + `eaNetBgTest()` round-trip self-test

## Phase 5: Verify
- [x] Clean Debug build (0 errors, no new warnings)
- [x] `eaNetBgTest()` round-trip: PASS (doodad + position + speed reproduced exactly)
- [x] Two-window loopback: joiner received a pre-join `speed` op over the real transport
- [x] Real `?netjip` run: SKIPPED — same EvReady seam as the loopback run; noted in the plan
- [x] Final smoke: plain boot reaches splash, zero console errors
- [ ] Spot-check the diff (lowercase `content/`, `BlendState.AlphaBlend`, codegen re-run)

## Phase 6: Ship
- [ ] Commit + push
- [ ] `/review`, fix every finding
- [ ] Pull main, re-verify
- [ ] PR + self-merge
- [ ] Worktree + branch cleanup
- [ ] Delete plan + this tracker
- [ ] Card -> Done + closing comment
- [ ] Follow-up cards for anything out of scope

## Phase 7: Clean up
- [ ] Kill the wt5 dev server, close verification tabs
