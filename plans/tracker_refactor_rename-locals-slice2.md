# Tracker: refactor/rename-locals-slice2

Card `ace0b261` — "Rename decompiled locals — slice 2 (119 files, ~2367 refs left)"
Continuation of `d26f0681` (slice 1, shipped: 625/3032 refs + `tools/verify_il_identical.py`).

## Phase 1: Pick Up the Card
- [x] Claim card atomically (`grab` → ace0b261, Backlog → In Progress)
- [x] Read project CLAUDE.md + CONTRIBUTING.md (root + global)
- [x] Read the card, its comments, and slice 1's card d26f0681
- [x] Fetch latest main (root pull aborted on user's uncommitted files; branched off origin/main = ae3bac5)
- [x] Create worktree + branch (`.claude/worktrees/rename-locals-slice2`; wt1..wt8 all claimed by parallel agents, and this card needs no dev server)
- [x] Push branch upstream

## Phase 2: Research
- [x] Read `tools/verify_il_identical.py` — understand the oracle's contract and flags
- [x] Re-census the remaining `num`/`val`/`flag`/`text`/`array` refs on current main
- [x] Read slice 1's plan/commits for the substitution rules that held up
- [x] Pick the slice-2 file set (densest + most-read first)

## Phase 3: Design
- [x] Write `plans/rename-locals-slice2.md` (context / design / verification / out of scope)
- [x] Present the plan and get user approval BEFORE writing code
- [x] Post short TLDR comment on card ace0b261

## Phase 4: Implement
- [x] Rename locals file by file, one method body at a time
- [x] Run the IL oracle after each file
- [x] Re-scan every touched comment line by eye (the oracle cannot see comments)
- [x] Update docs if the change adds a convention/gotcha

## Phase 5: Verify
- [x] `python tools/verify_il_identical.py --ref main` → IDENTICAL
- [x] Clean `dotnet build -c Debug` (0 errors, warning count unchanged)
- [x] Spot-check the full diff by eye (comments, prose corruption, no behaviour/format change)
- [x] No browser/dev-server needed for this card class (established by slice 1)

## Phase 6: Review & Ship
- [x] Commit + push
- [x] `/review` the branch diff; fix every finding
- [ ] Pull main into branch, resolve conflicts per runbook rules
- [ ] Re-verify after merge (oracle + build)
- [ ] Return to root checkout
- [ ] `gh pr create --fill` + `gh pr merge --merge`
- [ ] Clean up worktree + branch (local and remote)
- [ ] Delete plan + tracker docs
- [ ] Move card ace0b261 to Done
- [ ] Comment summary on card (real newlines)
- [ ] Open follow-up card for the remaining slices
- [ ] Write closing overview for the user

## Phase 7: Clean up
- [ ] No dev servers started; no browser tabs opened (nothing to stop)

## Rules carried forward from slice 1
- Scope every substitution to ONE method body, longest name first (num18 before num1 before num), on word boundaries.
- NEVER rename a parameter — parameter names came from metadata and are the originals. Only locals are invented.
- Comments referencing a renamed identifier must be updated with it — but the IL oracle cannot see comments, and slice 1 corrupted the English word "flag" in prose. Re-scan every renamed comment line by eye.
- Prefer naming precedent already in the tree over invention.
- No behaviour, formatting or dead-code changes — the oracle enforces this literally.
