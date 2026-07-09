# Tracker: fix/juice-hitstop-shake-tuning

Two bundled Trello cards (both in `Compat/Juice.cs` territory):
- **bd5efd9d** — kill the per-kill hit-stop micro-pause (reads as stutter, not juice)
- **8e439865** — L3 miniboss procession shakes too much: no shake for the death-explosion
  series (only the final blast), + halve max screenshake generally

Orchestrator overrides: no live browser verification (build + diff-read only; orchestrator
live-tests post-hoc), skip per-card /review, PAUSE after `gh pr create` — no merge/deploy
until approval.

## Phase 1: Pick Up
- [x] Claim both cards (Backlog -> In Progress)
- [x] Worktree slot wt5 + branch `fix/juice-hitstop-shake-tuning` off main
- [x] Read both card descriptions

## Phase 2: Research
- [x] Read Compat/Juice.cs, DebugFlags.cs (Hitstop/ShakeAmount seams)
- [x] Trace KillPunch callers (KillableAlien.HitBy) + AddHitStop direct callers
      (PlayerShip.Asplode/AsplodeWall, DebugInput.Hitstop)
- [x] Find the L3 miniboss procession: BattleSkullEvent -> BattleSkull; its death =
      KilledBy pop + Update dying-state flicker series + DeathTimer.Finished finale
- [x] Confirm Explosion.Initialize is the single AddTrauma site for explosion shake

## Phase 3: Design
- [x] bd5efd9d: flip DebugFlags.Hitstop default to false; gate KillPunch's freeze on it
      (shake trauma unaffected); AddHitStop itself UNgated so player-death + eaHitstop()
      still fire (player death isn't "destroying something" — noted for user veto)
- [x] 8e439865a: halve Juice.MaxOffsetDesignPx 14->7, MaxRollDegrees 2->1
- [x] 8e439865b: opt-out `noShake` param on Explosion.Setup (default false = all existing
      call sites unchanged); BattleSkull passes noShake:true for the opening pop + flicker
      series, finale keeps shake

## Phase 4: Implement
- [x] Compat/Juice.cs (defaults halved, KillPunch gated, comments)
- [x] Compat/DebugFlags.cs (Hitstop default false, flag docs)
- [x] Game/EvilAliens/Explosion.cs (noShake field + Setup param + Initialize gate)
- [x] Game/EvilAliens/BattleSkull.cs (noShake:true on series explosions)
- [x] CLAUDE.md juice bullet minimal update

## Phase 5: Verify (per overrides: build + diff read, NO browser)
- [x] dotnet build -c Debug clean (0 errors)
- [ ] Full diff re-read (gotchas: hit-stop thaw on unscaled dt — untouched; no
      content/ paths, no blend changes)
- [ ] Live-test checklist written (card comments + report)

## Phase 6: Ship (PAUSED at PR)
- [ ] Commit per card, push
- [ ] `rtk gh pr create --fill` then STOP + report to orchestrator
- [ ] (post-approval) pull main, re-build, merge, cards -> Done + comments,
      delete tracker, remove worktree
