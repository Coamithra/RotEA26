# Tracker: fix/net-coop-bin-sanity

Card `9009a1c4` — "Net: two-tab co-op sanity vs new ComponentBin lifecycle"
Worktree: `.claude/worktrees/wt4` · port `5284`

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (Backlog -> In Progress)
- [x] Fetch/verify latest `main`
- [x] Read the card (description; no comments)
- [x] Create worktree + branch `fix/net-coop-bin-sanity`
- [ ] Push branch upstream

## Phase 2: Research
- [ ] Read PR #141 / card 02d9ad67 ComponentBin rework diff
- [ ] Read the net-layer seams: `SuppressWorldSpawn` divert, Pop frozen-puppet exception,
      `NetIdRegistry` on ComponentAdded/Removed
- [ ] Read the net metrics surface (`[net]` log: pops/pupPops/snapUnk/claims)
- [ ] Read `?binlog` instrumentation
- [ ] Identify blast radius / what a failure would look like

## Phase 3: Design
- [ ] Write `plans/net-coop-bin-sanity.md` (verification protocol, pass/fail criteria)
- [ ] Get user approval
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Run the verification passes; fix anything found
- [ ] Update CLAUDE.md if a new convention/flag/gotcha emerges

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Two-window co-op session: `?level=Level1&net=host&aiplayer&invuln&room=<fresh>` + `net=join`
- [ ] `[net]` metrics healthy (pops/pupPops ~0, snapUnk non-climbing, claims settle)
- [ ] Mid-level pause/resume from each side
- [ ] Host death/reset: no stray puppets, no diverted respawns
- [ ] `?netjip` join-in-progress pass
- [ ] Zero console exceptions in real Chrome

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff; fix every finding
- [ ] `git pull origin main`, resolve conflicts per rules
- [ ] Re-verify after merge
- [ ] `cd` back to root checkout
- [ ] `gh pr create --fill` + `gh pr merge --merge` + pull main
- [ ] Remove worktree, prune, delete branch (local + remote)
- [ ] Delete plan + tracker docs
- [ ] Move card to Done (`9c204b80`)
- [ ] Comment summary on card (real newlines)
- [ ] Open follow-up cards for out-of-scope findings
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server(s) on port 5284
- [ ] Close verification Chrome tabs
