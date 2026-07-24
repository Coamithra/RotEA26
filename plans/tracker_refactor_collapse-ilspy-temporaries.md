# Tracker: refactor/collapse-ilspy-temporaries

Card `0c624f9d` — "Collapse ILSpy `|=` and duplicated-subexpression temporaries"
Worktree: `.claude/worktrees/wt14` · dev port **5306** (self-served, `launch.json` untouched)
Follow-up from `d26f0681` (rename decompiled locals, slice 1).

## Phase 1: Pick Up the Card
- [x] Claim the card (moved `0c624f9d` → In Progress `3b43cba3`)
- [x] Start from latest code (root checkout blocked on an untracked file → branched off `origin/main` @ ae3bac5)
- [x] Read the card description (no comments on the card)
- [x] Create worktree + branch
- [ ] Read the slice-1 commit `d26f0681` for established conventions

## Phase 2: Research
- [ ] Read `InputHandler.UpdateKeyPads` — the 20 `|=` temporaries (40 refs)
- [ ] Read `InputHandler.Update` keyboard path (the same-file style precedent)
- [ ] Read `PlayerShip.DoAIMove` — 4x `powerup.Position - base.Position`, plus `position` → `target`
- [ ] Read `PlayerShip.DoAIFire` — `toBaddy`/`toNearest`, `alienDrawableGameComponent`
- [ ] Read `CollisionHandler.DetectCollisions` — `item` / `item2`
- [ ] Read `tools/verify_il_identical.py` — how the byte-identical gate is invoked
- [ ] Confirm CS0136 scope constraints for each collapse site

## Phase 3: Design
- [ ] Write `plans/collapse-ilspy-temporaries.md`
- [ ] Get user approval before coding
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] 1. Collapse `UpdateKeyPads` `|=` temporaries (also drops the dead first read per case)
- [ ] 2. Collapse duplicated sub-expression temporaries (`DoAIMove`, `DoAIFire`)
- [ ] 3. Rename the out-of-vocabulary ILSpy names (`position`→`target`, `alienDrawableGameComponent`, `item`/`item2`)
- [ ] Do NOT touch any real property-setter `value` parameter
- [ ] Update docs only if a new convention/gotcha is introduced

## Phase 5: Verify
- [ ] `tools/verify_il_identical.py --ref main` → byte-identical assembly (the primary gate)
- [ ] Clean `dotnet build web/EvilAliensWeb -c Debug`
- [ ] Final smoke: boot in real Chrome, zero console exceptions
- [ ] Spot-check the diff (no `content/`, no `BlendState.AlphaBlend`, no codegen re-run)
- [ ] Close verification browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff; fix every finding
- [ ] Pull `main` into the branch, resolve conflicts per the runbook
- [ ] Re-verify after the merge
- [ ] Return to root checkout; `gh pr create --fill` + `gh pr merge --merge`
- [ ] Remove worktree + branch (kill dev server first)
- [ ] Delete plan + this tracker
- [ ] Move card `0c624f9d` → Done `9c204b80`
- [ ] Comment the summary on the card (real newlines)
- [ ] Open follow-up cards (e.g. the wider `value`/`item`/`list`/`result` vocabulary census)
- [ ] Write the closing overview for the user

## Phase 7: Clean up
- [ ] Stop the dev server on 5306; close any remaining tabs

## Notes / snags
- Collided with another agent on the first random pick (`ca4fd94f`, JIP scene switches) — they
  had already created wt9 + tracker, so I ceded it and re-rolled. Card left with them in In Progress.
- wt1–wt8 all had dirty trees / unmerged commits → no reclaim. wt9–wt13 were taken in-flight.
