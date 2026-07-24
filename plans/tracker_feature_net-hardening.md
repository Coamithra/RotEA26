# Tracker: feature/net-hardening (card 4717d3cf — Stage 11.5)

## Phase 1: Pick Up the Card
- [x] Claim top card with `trello grab` (got 4717d3cf, Stage 11.5)
- [x] Pull latest main
- [x] Read the card + `plans/stage11-online-coop.md`
- [x] Create worktree wt6 + branch `feature/net-hardening` (dev port 5286)

## Phase 2: Research
- [x] Read `Compat/Net/` (NetSession, NetProtocol, NetImpairment, NetPauseOverlay, NetListing)
- [x] Trace the leave/drop paths (`PeerLost`, `EvLeave`, `Stop`)
- [x] Trace the powerup claim path (user-reported bugs)
- [ ] Audit WebcamAliens exclusion coverage

## Phase 3: Design
- [x] Write `plans/net-hardening.md`
- [ ] Post approach TLDR on the card

## Phase 4: Implement
- [ ] A. Powerup pickup replicates to the claimant's HUD slot
- [ ] B. One match-end code path + reconnect grace window
- [ ] C. Waiting-for-peer UI
- [ ] D. WebcamAliens exclusion enforcement

## Phase 5: Verify
- [ ] Clean Debug build
- [ ] `eaNetSim.test(...)` impairment sanity
- [ ] Two-window AI-peer run + fake lag, comparative screenshots every ~20s
- [ ] `[net]` metrics healthy both sides
- [ ] Zero console exceptions

## Phase 6: Ship
- [ ] Commit + push
- [ ] `/review`, fix every finding
- [ ] Pull main, re-verify
- [ ] PR + self-merge
- [ ] Worktree + branch cleanup
- [ ] Delete plan + tracker
- [ ] Card → Done + comment
- [ ] Follow-up cards (TURN decision, interp feel sign-off)

## Phase 7: Clean up
- [ ] Kill dev server(s), close Chrome tabs
