# Tracker: fix/spawn-death-queues

Card: `02d9ad67` — Spawn/death queue shenanigans

## Phase 1: Pick Up the Card
- [x] Claim the top card with `trello grab`
- [x] Pull latest main
- [x] Read the card
- [x] Create worktree (wt4) and branch `fix/spawn-death-queues`
- [ ] Push branch upstream

## Phase 2: Research
- [x] Find the spawn/destroy queue system in Game/ (ComponentBin: birthList/deathList/idleList/inactive)
- [x] Trace frame order (base.Update -> flush -> collisions) + KNI journal internals (decompiled)
- [x] Catalog bug classes: H1 zombie frame, H2 purge-then-late-add, H3 pause hole
- [x] Check net/co-op interactions (SuppressWorldSpawn, frozen puppets, NetIdRegistry seams)
- [x] Summarize findings

## Phase 3: Design
- [x] Draft hardening approach in plans/spawn-death-queues.md
- [x] Present recommendation to user, get approval (deaths stay queued per user)
- [x] Post short approach comment on card

## Phase 4: Implement
- [x] tools/audit_add_order.py + run it, fix the 8 config-after-Add sites (reorder Add last)
- [x] ComponentBin: instant births (drop birthList)
- [x] ComponentBin: top-of-tick death flush (Game1.UpdateInner call)
- [x] ComponentBin: standing purge filter (pendingPurges until next flush)
- [x] Purge standing:false opt-out for GameScene.UpdateStartup clear-and-respawn (regression
      caught live by ?binlog: filter ate the PlayerShip respawn)
- [x] ComponentBin: pause-aware Add (world objects join the freeze)
- [x] ?binlog DebugFlags + eaBinTest() console scenario suite
- [x] Update web CLAUDE.md lifecycle contract + tools CLAUDE.md audit script entry

## Phase 5: Verify (done pre-commit)
- [x] Clean Debug build, no new warnings; audit script 0 suspects
- [x] eaBinTest: 10/10 PASS
- [x] ClassicAliens smoke: waves, kills, death->reset->respawn, pause frozen (spawn racing the
      pause parked + thawed on resume), zero console errors; filter caught a real stray
      EvilBullet during a wipe
- [x] Spider boss fast-boot smoke: fight machinery, respawns, zero console errors

## Phase 5: Verify
- [ ] Clean Debug build
- [ ] Tool-driven verification (isolation sim if needed)
- [ ] Real Chrome smoke: boots, zero console errors

## Phase 6: Ship
- [ ] Commit, /review, fix findings
- [ ] Pull main into branch, re-verify
- [ ] PR + self-merge, cleanup worktree/branch
- [ ] Delete plan+tracker, move card to Done, comment summary
- [ ] Follow-up cards if needed

## Phase 7: Clean up
- [ ] Kill dev servers, close tabs
