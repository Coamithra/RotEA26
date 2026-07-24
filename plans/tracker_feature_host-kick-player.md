# Tracker: feature/host-kick-player

Card: `0b8a300b` — "Kick option" (Backlog → In Progress)
Worktree: `.claude/worktrees/wt3` · dev port **5283**

> If a remote player joins and pauses the game, the host should have the option to kick that
> player and resume, or even a kick+ban option (for that session only) so the player also can't
> rejoin. Basic anti-griefing stuff :)

## Phase 1: Pick Up the Card
- [x] Claim the card with `trello grab` (got `0b8a300b`)
- [x] Fetch latest main (root checkout is dirty with user's local changes — branched off `origin/main` instead of pulling)
- [x] Read the card (no comments, no linked plan beyond `plans/stage11-online-coop.md`)
- [x] Create worktree `wt3` + branch `feature/host-kick-player`
- [ ] Push branch

## Phase 2: Research
- [ ] Read `plans/stage11-online-coop.md` (co-op design: net roles, channels, message types)
- [ ] Find the net session / peer bookkeeping (host authority, peer list, disconnect path)
- [ ] Find the pause flow (who can pause, how pause propagates, how resume works)
- [ ] Find the join path (game browser / JIP) — where a ban list must be enforced
- [ ] Identify blast radius + existing UI conventions for in-game menus

## Phase 3: Design
- [ ] Write `plans/host-kick-player.md` (Context / Design / Verification / Out of scope)
- [ ] Present the plan and get user approval BEFORE coding
- [ ] Post short TLDR comment on the card

## Phase 4: Implement
- [ ] Kick (host-authoritative disconnect of a peer, resume)
- [ ] Kick+ban (session-scoped ban list, enforced on rejoin)
- [ ] Pause-menu UI entry for the host
- [ ] Update `web/EvilAliensWeb/CLAUDE.md` if a new convention/flag/gotcha lands

## Phase 5: Verify
- [ ] Clean `dotnet build -c Debug`
- [ ] Tool-driven verification (build a harness/sim if none fits — no timed live screenshots)
- [ ] Real Chrome, foreground, zero console exceptions
- [ ] Diff spot-check (no lowercase `content/`, no `BlendState.AlphaBlend`, no codegen re-run)
- [ ] Close browser tabs

## Phase 6: Review & Ship
- [ ] Commit + push
- [ ] `/review` the branch diff, fix every finding
- [ ] `git pull origin main`, resolve conflicts per runbook rules
- [ ] Re-verify after merge
- [ ] `cd` back to root checkout
- [ ] PR + self-merge (`gh pr create --fill`, `gh pr merge --merge`)
- [ ] Remove worktree, prune, delete branch (local + remote)
- [ ] Delete plan + tracker docs
- [ ] Move card to Done (`9c204b80`) + summary comment (real newlines)
- [ ] Follow-up cards for anything out of scope
- [ ] Closing overview for the user

## Phase 7: Clean up
- [ ] Stop dev server on 5283
- [ ] Close any remaining browser tabs
