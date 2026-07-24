# Tracker: fix/net-score-combo-replication

Card: `b0ab09ec` — "Net: score tally diverges one-way between peers"
Worktree: `.claude/worktrees/wt11` · dev port **5303** (wt9/wt10 lost to races; wt1-wt8 all owned)

## Phase 1: Pick Up the Card
- [x] Claim the card (moved `b0ab09ec` Backlog -> In Progress)
- [x] Pull latest main (branched off `origin/main` = ae3bac5; root checkout was 35 behind and dirty with other agents' work -- left alone)
- [x] Read the card description
- [x] Create worktree + branch (`wt11` / `fix/net-score-combo-replication`)
- [ ] Read card comments / linked plans

## Phase 2: Research
- [ ] Read `NetPuppets.OnRemoteDeath` and the score-credit path
- [ ] Read `score.AddScore` + `comboModify` (combo multiplier state)
- [ ] Read `EvScoreSync` (the max() adoption)
- [ ] Trace how the host's combo state is maintained vs the client's
- [ ] Scope blast radius (couch co-op, single player, JIP)

## Phase 3: Design
- [ ] Settle on option (a)/(b)/(c) from the card
- [ ] Write `plans/net-score-combo-replication.md`
- [ ] Get user approval before coding
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Changes per plan
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` (it currently claims "self-corrects upward")

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Build/drive a purpose-built verification tool (isolation sim for the score/combo divergence)
- [ ] Real-Chrome smoke on port 5303, zero console exceptions
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] Pull origin/main, resolve conflicts, re-verify
- [ ] PR + self-merge
- [ ] Remove worktree + branch, delete plan + tracker
- [ ] Move card to Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Kill dev server on 5303
