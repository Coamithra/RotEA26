# Tracker: fix/bg-tile-cull

Card: `5216412d` — "Fix BackgroundImage tile-cull: LogicalWidth in Y test, missing *size in mirrorX"
Worktree: `.claude/worktrees/bg-tile-cull` (all wt1..wt8 slots were taken) — dev port 5293

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (5216412d, Backlog -> In Progress)
- [x] Fetch latest main (root checkout is dirty + behind; branched off `origin/main` = ae3bac5)
- [x] Read the card (desc is detailed; no comments, no linked plan)
- [x] Create worktree + branch (`.claude/worktrees/bg-tile-cull`, `fix/bg-tile-cull`)

## Phase 2: Research
- [x] Read `BackgroundImage.DrawBackground` in full — confirm both defects verbatim
- [x] Establish what `LogicalWidth()` / `LogicalHeight()` return and where `size` comes from
- [x] Map every cull test — there are **4**, not 2 (main, mirrorY-in-main, mirrorX, mirrorX+mirrorY);
      bug 1 is in all four, bug 2 in the two mirrorX ones (both their X *and* Y terms)
- [x] Find real callers: `mirrorX`/`mirrorY` are NEVER set true (only one explicit `= false`),
      so both mirror blocks are dead code today and bug 2 is unreachable
- [x] Confirm "not visibly broken today": every live tile is square or WIDE (W >= H), so bug 1
      only ever over-draws. Measured all: 756* 512x512, 2331-v5 512x512, Starfield2 1024x768,
      grid3 30x30, clouds/hills 1024x600 & 1000x600, marsloop* 1587x971
- [x] Blast radius as data (cull sim over every real config): **no layer loses a visible tile**;
      the ONLY delta is the Mars ground `[12,1]` layer, 6 -> 3 tile draws/frame, the 3 removed
      being entirely off-screen (bottom edge y = -0.124)
- [x] Decide the verification tool — predicate property test + live cull counters (see plan)

## Phase 3: Design
- [ ] Write `plans/bg-tile-cull.md` (context, design, verification, out of scope)
- [ ] Present to user, get approval before coding
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Fix per approved plan
- [ ] Update the right CLAUDE.md if a convention/flag/gotcha is added

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Tool-driven verification (NOT the IL oracle — this is a real behaviour change)
- [ ] Zero console exceptions in real Chrome
- [ ] Spot-check the diff (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)
- [ ] Close verification browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook
- [ ] Re-verify after merge
- [ ] `gh pr create --fill` + `gh pr merge --merge`
- [ ] Remove worktree, prune, delete branches
- [ ] Delete plan + this tracker
- [ ] Move card to Done + summary comment (real newlines)
- [ ] Follow-up cards
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev servers, close tabs
