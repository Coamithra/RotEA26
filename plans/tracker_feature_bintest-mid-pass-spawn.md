# Tracker: feature/bintest-mid-pass-spawn

Card `bcdc7430` — "Bin: pin the mid-pass-spawn collision contract in eaBinTest"

## Phase 1: Pick Up the Card
- [x] Claim card atomically (`grab` → bcdc7430, Backlog → In Progress)
- [x] Read the card (desc; no comments)
- [x] Base off latest origin/main (ae3bac5)
- [x] Create worktree `.claude/worktrees/wt4` + branch `feature/bintest-mid-pass-spawn`
- [x] Push branch upstream

## Phase 2: Research
- [x] Read `CollisionHandler.DetectCollisions` + the PR #149 fix (`8e3f4ef`)
- [x] Read `eaBinTest` scenario harness, `ComponentBin`, `DebugInput` bridge, `index.html`
- [x] Read `CollisionBox` / `CollisionMultibox` geometry + `AlienDrawableGameComponent`
- [x] Trace how `Game1` exposes internals to BinTest (`ComponentBin.Game` pattern)
- [x] Blast radius: test-only; one internal accessor added, no behaviour change

## Phase 3: Design
- [x] Write `plans/bintest-mid-pass-spawn.md`
- [x] User approval (chose "both 5a and 5b")
- [x] Post short TLDR comment on card

## Phase 4: Implement
- [x] Game1 `internal CollisionHandler` accessor
- [x] `CollidingAlien` scratch type + the two scenarios
- [x] Assertions: no throw + newborn not visited in-pass + joins the next pass
- [x] Self-clean via Remove/flush/PruneIdle
- [x] Docs: `web/EvilAliensWeb/CLAUDE.md` lifecycle + diagnostics bullets

## Phase 5: Verify
- [x] Clean `dotnet build -c Debug` (0 errors, no new warnings)
- [x] `eaBinTest` in real Chrome, foreground, `?menu` → 20/20
- [x] Repeat runs identical (no scratch accumulation); 20/20 mid-level too
- [x] Negative control: reverted `DetectCollisions` to pre-#149 → scenario 5 reports
      `InvalidOperationException`, scenario 6 reports the newborn participating. Stable over
      4 repeats, and reproduced from a busy level (proving the plant does not depend on the
      `boxes` high-water mark). Restored afterwards.
- [x] `?binlog` independently agrees (mid-pass growth counter fires)
- [x] Zero console exceptions
- [x] `python tools/audit_add_order.py` clean (0 suspects)
- [x] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)
- [x] Close browser tabs

## Phase 6: Review & Ship
- [x] Commit + push (`6ee2012`)
- [x] `/review` branch diff vs `origin/main`; all 13 findings actioned, none dismissed
- [x] Re-verify after review fixes
- [ ] Pull origin/main, resolve conflicts, re-verify
- [ ] PR + self-merge
- [ ] Clean up worktree/branch; DELETE this tracker + `plans/bintest-mid-pass-spawn.md`
- [ ] Card → Done + summary comment
- [ ] Follow-up cards
- [ ] Closing overview for user

## Phase 7: Clean up
- [ ] Stop dev server (port 5284), close tabs
