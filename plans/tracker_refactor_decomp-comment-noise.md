# Tracker: refactor/decomp-comment-noise

Card `432a31e9` — "Remove comment noise from decompilation pass"

## Phase 1: Pick Up the Card
- [x] Claim the top card with `trello grab`
- [x] Pull latest `main`
- [x] Read the card
- [x] Create worktree `wt7` + branch `refactor/decomp-comment-noise`

## Phase 2: Research
- [x] Enumerate every decompiler-artifact comment shape in `Game/`
- [x] Confirm blast radius (130 files, 4020 lines, `Game/` only — `Compat/` clean)
- [x] Confirm bare `//` lines are hand-written paragraph separators, NOT noise
- [x] Confirm no multi-line verbatim strings (line-based strip is safe)
- [x] Survey encodings / line endings (no BOM, CRLF, 43 files non-ASCII)
- [x] Answer the card's second question: what else did decompilation wreck?

## Phase 3: Design
- [x] Write `plans/decomp-comment-noise.md`
- [x] Post approach TLDR on the card

## Phase 4: Implement
- [x] `tools/strip_il_comments.py` (record of the derivation, like `fix_apis*.py`)
- [x] Run it over `web/EvilAliensWeb/Game/`
- [x] Handle the 3 mangled `}//IL_...` lines (keep the `}`)
- [x] Collapse the resulting double blank lines

## Phase 5: Verify
- [x] `git diff --stat` sanity: deletions only, no code lines touched
- [x] Assert zero `//IL_` remaining in `Game/`
- [x] Byte-identical check: file with all comment lines removed == before, modulo comments
- [x] Clean `dotnet build -c Debug`
- [x] Boot smoke check in real Chrome, zero console exceptions

## Phase 6: Ship
- [x] Commit + push
- [x] `/review`, fix findings
- [ ] Pull `main`, re-verify
- [ ] PR + self-merge
- [ ] Worktree/branch cleanup
- [ ] Delete plan + tracker
- [ ] Card → Done + summary comment
- [ ] Follow-up card for decompiled local-variable names

## Phase 7: Clean up
- [ ] Kill dev server, close Chrome tabs
