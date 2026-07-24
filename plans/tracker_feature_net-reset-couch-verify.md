# Tracker: feature/net-reset-couch-verify

Card: `af0eb00a` — "Net: verify a full reset with couch players aboard"
Worktree: `.claude/worktrees/net-reset-couch-verify` (all wt1..wt8 slots were taken) · dev port **5289**

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (got `af0eb00a`)
- [x] Pull latest main (root checkout has user's uncommitted work — branched off `origin/main` instead)
- [x] Read the card (description + comments)
- [x] Create worktree and branch
- [ ] Read the linked prior card `4d904410` / PR #153

## Phase 2: Research
- [ ] Read `NetSession.Friends` adopt logic (`SpawnAllPlayers`, `DriveFriendShip`, `SpawnFriend`)
- [ ] Trace the reset/checkpoint path that respawns all seated slots
- [ ] Read the existing `?netlocal` two-tab rig + `[net]` line fields (`resets`, `roster=`)
- [ ] Read `ExpireUnclaimedGrants` and the `RejectFull` path
- [ ] Identify blast radius

## Phase 3: Design
- [ ] Settle the approach (forced-reset debug hook + assertions)
- [ ] Write `plans/net-reset-couch-verify.md`
- [ ] Present plan, get user approval
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Add the deterministic reset trigger (debug flag / `eaNet` console hook)
- [ ] Cover granted-seat expiry (`ExpireUnclaimedGrants`)
- [ ] Cover roster-full `RejectFull`
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` (+ root `CLAUDE.md` if a new URL flag)

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Tool-driven verification (NOT timed live screenshots)
- [ ] Assert: `resets` increments; `roster=` unchanged across reset on BOTH peers
- [ ] Assert: no puppet frozen on spawn pose, none duplicated
- [ ] Zero console exceptions in real Chrome
- [ ] Spot-check diff (lowercase `content/`, `BlendState.AlphaBlend`, codegen re-run)
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook rules
- [ ] Re-verify after merge
- [ ] Return to root checkout
- [ ] PR + self-merge (`gh pr create --fill`, `gh pr merge --merge`)
- [ ] Remove worktree + branch (local & remote)
- [ ] Delete plan + tracker docs
- [ ] Move card to Done + summary comment (real newlines)
- [ ] Create follow-up cards
- [ ] Write closing overview

## Phase 7: Clean up
- [ ] Stop dev server on 5289
- [ ] Close any remaining browser tabs

## Notes / gotchas encountered
- Root checkout is behind `origin/main` and holds the user's uncommitted work
  (netsim doc tweaks, 3 replaced `.wav`s, `index.html`). Left untouched; do NOT pull it.
- All slots wt1..wt8 are live worktrees from parallel agents — used a named dir + port 5289.
