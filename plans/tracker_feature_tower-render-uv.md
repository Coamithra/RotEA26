# Tracker: feature/tower-render-uv

Card: `0f7fc977` — "improve tower rendering" (In Progress)
Worktree: `.claude/worktrees/wt9` · dev port **5289**

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (got `0f7fc977`)
- [x] Pull latest `main`
- [x] Read the card + attachment (`tmp.png`) + comments (none)
- [x] Create worktree `wt9` + branch `feature/tower-render-uv`
- [x] Push branch

## Phase 2: Research
- [x] Find the tower/wall rendering code (geometry + UVs)
- [x] Understand how the top (xy) face and side (z) faces get their UVs
- [x] Confirm the "mirrored" side texture claim
- [x] Identify tower height vs top-width in world units
- [x] Check tiling/wrap sampler state and texture atlas constraints
- [x] Blast radius: what else uses the same mesh builder

## Phase 3: Design
- [x] Write `plans/tower-render-uv.md`
- [x] Get user approval
- [x] Post short TLDR comment on the card

## Phase 4: Implement
- [x] Side-face UVs continue the tile instead of mirroring
- [x] Scale side-face V by tower height so towers read as tall
- [x] Update docs if a convention/flag is added

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug`
- [x] Tool-driven visual verification (harness / wallsonly flag)
- [x] Zero console exceptions in real Chrome
- [x] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`)

## Phase 6: Review & Ship
- [x] Commit + push
- [x] `/review` the branch diff, fix every finding
- [ ] Pull `main`, resolve conflicts, re-verify
- [ ] PR + self-merge
- [ ] Clean up worktree/branch/plan/tracker
- [ ] Card -> Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server, close verification tabs
