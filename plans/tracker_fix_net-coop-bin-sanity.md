# Tracker: fix/net-coop-bin-sanity

Card `9009a1c4` — "Net: two-tab co-op sanity vs new ComponentBin lifecycle"
Worktree: `.claude/worktrees/wt4` · port `5284`

Results live in `plans/net-coop-bin-sanity.md` ("Results"). Both docs are deleted at card close.

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (Backlog -> In Progress)
- [x] Fetch/verify latest `main`
- [x] Read the card (description; no comments)
- [x] Create worktree + branch `fix/net-coop-bin-sanity`
- [x] Push branch upstream

## Phase 2: Research
- [x] Read PR #141 / card 02d9ad67 ComponentBin rework diff
- [x] Read the net-layer seams (only net change in #141 was a comment)
- [x] Read the net metrics surface + `?binlog` instrumentation
- [x] Derive the risk model from the `Game1.UpdateInner` tick order (R1-R5)

## Phase 3: Design
- [x] Write `plans/net-coop-bin-sanity.md`
- [x] User approval (fix inline + re-verify; user fronts the two windows)
- [x] Post TLDR comment on the card

## Phase 4: Implement
- [x] Fix `CollisionHandler.DetectCollisions` frozen count (the regression found)
- [x] Add the `?binlog` mid-pass-growth diagnostic
- [x] Add `eaKillShips()` so a co-op death/reset is reachable on demand
- [x] Update `web/EvilAliensWeb/CLAUDE.md` (lifecycle rule, diagnostics, recipe, snapUnk nuance)

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug`
- [x] Pass A steady state (~60s, >=3 `[net]` reports, both windows visible)
- [x] Pass B pause/resume from BOTH sides
- [x] Pass C host death/reset — `resets=1`, both ships back, zero diverts
- [x] Pass E `eaBinTest()` 10/10 on both peers against a live session
- [~] Pass D JIP — bin-relevant half (mid-world `ReplayLive`) covered by every pass;
      `?netjip` listing/signaling half NOT run (needs the live VPS; card 2001fbd8's gate)
- [x] Zero console exceptions after the fix (was 19 across two tabs before)

## Phase 6: Review & Ship
- [x] Commit + push
- [x] `/review` the branch diff; triage + fix findings
- [ ] `git pull origin main`, resolve conflicts per rules
- [ ] Re-verify after merge
- [ ] `cd` back to root checkout
- [ ] `gh pr create --fill` + `gh pr merge --merge` + pull main
- [ ] Remove worktree, prune, delete branch (local + remote)
- [ ] Delete plan + tracker docs
- [ ] Move card to Done (`9c204b80`)
- [ ] Comment summary on card (real newlines)
- [ ] Open follow-up cards (BinTest collision guard; `?netjip` pass; R1-R3 races)
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server on port 5284
- [ ] Close verification Chrome tabs
