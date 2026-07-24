# Tracker: refactor/rename-decompiled-locals

Card `d26f0681` — "Rename decompiled local variables (num/val/flag/text/array)"
Worktree: `.claude/worktrees/wt7` · dev port `5287`

## Phase 1: Pick Up the Card
- [x] Claim the card atomically (`grab` → In Progress)
- [x] Pull latest `main`
- [x] Read the card (description, comments — none)
- [x] Create worktree `wt7` + branch `refactor/rename-decompiled-locals`
- [x] Push branch upstream

## Phase 2: Research
- [x] Census the actual damage — 124 files, 3032 refs, ~727 decl sites
- [x] Found the real verification answer: an IL-identity oracle beats any harness here
- [x] Proved it (rename → identical hash; `128`→`129` → different hash; path-independent)
- [x] Pick a scoped slice — 5 core files

## Phase 3: Design
- [x] Write `plans/rename-decompiled-locals.md`
- [x] Present plan, user approved (5 files; document oracle in root CLAUDE.md)
- [x] Post short TLDR comment on the card

## Phase 4: Implement
- [x] `tools/verify_il_identical.py` (the oracle, ships)
- [x] MyMath.cs (16), CollisionHandler.cs (186), BackgroundImage.cs (107),
      InputHandler.cs (56 of 96), PlayerShip.cs (260) = **625 renamed**
- [x] Root `CLAUDE.md` — new verification-rule bullet for provable no-op refactors

## Phase 5: Verify
- [x] Oracle IDENTICAL after every file
- [x] Fixed a real tool bug found mid-verify: MSBuild skips recompiles on a property-only
      change, so a preceding normal build left a stale DLL → added `-t:Rebuild`
- [x] Re-ran the negative control against the FIXED tool in the exact bug scenario → caught
- [x] Clean `dotnet build -c Debug`: 0 errors, 38 warnings (same as baseline)
- [x] Real Chrome on 5287: menu renders, attract demo AI flies+blasts, Level1 boots and
      plays (`level=Level1 invuln=True`), **0 console errors** across 72 messages

## Phase 6: Review & Ship
- [x] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook rules
- [ ] Re-verify after merge
- [ ] Back to root checkout, `gh pr create --fill` + `gh pr merge --merge`
- [ ] Clean up worktree, branch, plan + tracker docs
- [ ] Move card to Done + comment summary (real newlines)
- [ ] Open follow-up cards (|= temporaries, duplicated sub-expressions, remaining 119 files)
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server on 5287
- [ ] Close verification browser tabs
