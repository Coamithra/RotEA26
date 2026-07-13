# Tracker: feature/net-level-script (Stage 11.3, card 70c7aea2)

## Phase 1: Pick Up the Card
- [x] Claim card (moved to In Progress)
- [x] Pull latest main
- [x] Read card + plans/stage11-online-coop.md
- [x] Worktree wt1 + branch feature/net-level-script (port 5281)

## Phase 2: Research
- [x] Net layer (NetSession/NetProtocol/NetPuppets/NetMetrics)
- [x] GameScene state machine, LoseLife/reset/victory/pause seams
- [x] GameEventList + Level1/TeamChallenge scripts, MessageEvent/UnlockEvent
- [x] ShipConnector + TeamChallenge shared fate

## Phase 3: Design
- [x] plans/stage11.3-net-script-reset.md
- [x] TLDR comment on the card

## Phase 4: Implement
- [x] tools/sim/tether_sim.py (pick K/MaxPull first — constants feed ShipConnector)
- [x] Protocol v3: EvMessage/EvUnlock/EvBackground/EvMusic/EvCheckpoint/EvReset/EvVictory/EvPause/EvTetherBreak
- [x] Host hooks: MessageEvent, UnlockEvent, Background ops, SoundManager music, checkpoint
- [x] GameScene seams: NetActiveScene, NetApplyReset, victory broadcast, client LoseLife suppression
- [x] Pause: replicate push/resume, remote-pause freeze overlay, overlap handling
- [x] TeamChallenge net seating (local device only) + deferred connector creation
- [x] ShipConnector: net soft-pull mode + EvTetherBreak
- [x] Metrics: localShip/remoteShip, beatRx, resets, pauses, tetherBrk
- [x] ?netscript fast-boot verification script
- [x] Docs: web CLAUDE.md net section update

## Phase 5: Verify
- [x] dotnet build clean
- [x] tether_sim.py assertions pass
- [x] Two-tab ?netscript run (all beats on join tab, metrics clean)
- [x] Death/checkpoint reset two-tab
- [x] TeamChallenge two-tab (tether, break, shared fate)
- [x] Pause both directions + overlap
- [x] Plain boot unchanged, zero console errors

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] /review, fix findings
- [ ] Merge main in, re-verify
- [ ] PR create + self-merge, pull root main
- [ ] Remove worktree + branches
- [ ] Delete plan + tracker docs
- [ ] Card -> Done + closing comment
- [ ] Follow-up cards (de-static NetSession harness)

## Phase 7: Clean up
- [ ] Kill dev server, close Chrome tabs
