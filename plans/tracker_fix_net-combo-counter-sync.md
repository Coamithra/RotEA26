# Tracker: fix/net-combo-counter-sync

Card `1a3ad45a` — "Net: combo COUNTER still differs between peers (cosmetic)"
Worktree: `.claude/worktrees/wt15` · dev port `5295`

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (before anything else)
- [x] Read the card (description, comments, linked plan)
- [x] Branch off latest origin/main
- [x] Create worktree + branch
- [ ] Push branch

## Phase 2: Research
- [ ] Read `ScoreVisualiser` (combo lifecycle, `SustainCombo`, `increasecombo`)
- [ ] Trace `Bullet.CollidesWith` -> combo raise, local-only path
- [ ] Check `EvScoreSync` / ship stream for a place to fold a per-slot byte
- [ ] **Check whether remote-slot POWERUP LEVELS drift for the same reason**
      (card note: `increasecombo` -> `powerupDatas[..].AddExp(combo)`) — not cosmetic if so
- [ ] Cross-check card 4717d3cf (powerup indicator mirroring for remote collector)

## Phase 3: Design
- [ ] Pick option (a) document / (b) replicate for display / (c) hide remote combos
- [ ] Write `plans/net-combo-counter-sync.md`
- [ ] Present plan, get user approval
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Implement per plan
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` if a convention/flag/gotcha is added

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Tool-driven verification (isolation sim / harness — build one if none exists)
- [ ] Real Chrome smoke: zero console exceptions
- [ ] Diff spot-check (lowercase `content/`, `BlendState.AlphaBlend`, codegen re-run)
- [ ] Close verification browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff; fix every finding
- [ ] `git pull origin main`; resolve conflicts per runbook rules
- [ ] Re-verify after merge
- [ ] PR + self-merge (`gh pr create --fill` / `gh pr merge --merge`)
- [ ] Clean up worktree, branch, plan + tracker files
- [ ] Move card to Done + comment summary (real newlines)
- [ ] Open follow-up cards
- [ ] Write closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev servers
- [ ] Close any remaining browser tabs
