# Tracker: fix/bg-tile-seams-recovery

Card `51bdd4a9` — "Recover the unmerged bg-tile-seams review fixes (missed PR #142)".
Worktree slot **wt3** (port 5283). **Constraint this session: NO live browser testing**
(user is overwatching) — verification must be build + offline-tool driven.

## Phase 1: Pick Up the Card
- [x] Claim card atomically (`grab`, 51bdd4a9)
- [x] Pull latest main (31b8cc7, up to date)
- [x] Read the card (no comments on it)
- [x] Create worktree wt3 + branch `fix/bg-tile-seams-recovery`
- [ ] Push branch

## Phase 2: Research
- [ ] Read f029d30 in full (8 files)
- [ ] Diff each hunk against current main — what already landed, what conflicts
- [ ] Check the ~145 commits since PR #142 for semantic overlap (bgfreeze, build_textures, texviewer)
- [ ] Verdict per file: apply / already-superseded / needs-rework

## Phase 3: Design
- [ ] Write plan doc
- [ ] Get user approval
- [ ] Post TLDR comment on the card

## Phase 4: Implement
- [ ] Apply the recovered changes onto main
- [ ] Update docs (web/CLAUDE.md, tools/CLAUDE.md) as the commit intended

## Phase 5: Verify
- [ ] `dotnet build web/EvilAliensWeb -c Debug` clean
- [ ] `python tools/textures/check_pad_bleed.py` against shipped assets
- [ ] `verify_il_identical.py` / decompiled diff where the change should be inert
- [ ] Spot-check the diff (lowercase `content/`, BlendState.AlphaBlend, codegen re-run)
- [ ] NOTE for user: live-Chrome smoke check deferred (no live testing this session)

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook
- [ ] Re-verify
- [ ] PR + self-merge
- [ ] Remove worktree, delete branches (incl. the stale local `fix/bg-tile-seams`)
- [ ] Delete plan + tracker
- [ ] Card → Done, summary comment, follow-up cards

## Phase 7: Clean up
- [ ] No dev servers started (none needed)
