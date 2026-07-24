# Spawn/death queue hardening (card 02d9ad67)

## Context

`ComponentBin` defers all world mutations: `Add()` queues into `birthList`, `Remove()` queues
into `deathList`, and both flush once per tick in `ComponentBin.Update()` (called from
`Game1.UpdateInner` AFTER `base.Update` runs every component, BEFORE `DetectCollisions`).
This was needed in 2008 because XNA's `GameComponentCollection` couldn't be safely mutated
mid-update. The deferral creates a "pending world" that the rest of the game can't see, which
is the root of the card's bug reports (stray enemy after a clear-all, an object moving while
paused, a laser that never despawns).

### Verified frame order (per tick, `Game1.UpdateInner`)

1. `base.Update` — every enabled component updates (GameScene first — it's added before the
   enemies — then enemies/projectiles in add order).
2. `collectionHelper.Update()` — flush: births enter `Game.Components`, then deaths leave.
3. `collisionHandler.DetectCollisions()` — kills happen here (`KillableAlien.HitBy` →
   `KilledBy` → `Die()` → `collection.Remove` → deathList).
4. Draw.

### The concrete bug classes

- **H1 — the zombie frame.** An enemy killed in step 3 of tick N stays in `Game.Components`,
  fully Enabled, through ALL of tick N+1's step 1 — it moves, its AI runs, and it can fire /
  spawn children — before the flush finally removes it. Children spawned from the grave
  outlive every cleanup that already ran ("laser keeps hanging around": `Lazer.owner` fires
  one last beam / an `EvilBullet` volley lands after the world was cleared).
- **H2 — purge-then-late-add in the same tick.** `Purge<T>` sweeps `Game.Components` +
  `birthList` at CALL time (it runs inside `GameScene.Update`, early in step 1). Any
  component updating later in step 1 (all enemies do — they were added after GameScene), or
  any kill side effect in step 3 (asteroid splits, powerup drops, `OnDeath` handlers), can
  `Add()` a fresh world object the purge never saw. Single-shot purges
  (`UpdateResetting`, `Terminate`) leave that stray alive indefinitely — the "stray enemy
  left over" report. (`UpdateWin` re-purges every tick, which masks it there.)
- **H3 — the pause hole.** `Push()` freezes what exists NOW (collection + birthList). `Add()`
  unconditionally sets `Enabled = true`, so anything added while the world is pushed flies
  around under the pause menu — the "moving while paused" report. (The net layer already
  papers over one instance of this: `NetSession.SuppressWorldSpawn`'s "spawner strays after
  a pause tick" comment.)

### KNI facts (decompiled `Xna.Framework.Game.dll` 4.1.9001)

- `Game.Components` mutations mid-update are SAFE: the updateable/drawable lists are
  journaled — `ProcessAdd/RemoveUpdateableJournal` run at the START of the next update pass.
  A component added mid-tick therefore does not Update until the next tick automatically
  (the "don't update the spawn tick" flag comes free), and it DOES draw the same tick
  (draw journal processes at draw start — one frame earlier than today, i.e. a kill's
  explosion appears the same frame; cosmetic improvement).
- `Components.Add` calls `component.Initialize()` SYNCHRONOUSLY (ComponentAdded handler).
  Today the flush guarantees Setup-before-Initialize regardless of call-site order; with
  instant adds, any site that calls `Add` BEFORE `Setup` would get Initialize-before-Setup
  (e.g. `KillableAlien.Initialize` would compute hitpoints from an unset
  `initialhitpoints` → 1-hp enemies). 297 `Add((GameComponent)` sites in 80 files must be
  audited (scriptable — the dominant pattern is `New*` → `Setup` → `Add`).

### Why deaths must STAY queued (user constraint, independently confirmed)

Instant removal would mutate `CollisionHandler.collidables` in the middle of
`DetectCollisions`, whose broad phase keeps parallel arrays indexed by position
(`boxes[i]` ↔ `collidables[i]`) — removal mid-pass shifts indices and corrupts pair
resolution. It would also change within-tick gameplay (a dying enemy stops eating bullets
for the rest of the pass). Deaths keep the queue.

## Design

Four independent fixes; each closes one hole. `ComponentBin`'s public API is unchanged —
no call-site churn outside the audit.

### 1. Births become instant (removes `birthList`)

`Add()` puts the component straight into `Game.Components` (KNI journaling makes this safe;
the component first Updates next tick, exactly like today). Membership truth is then always
`Game.Components` — `Purge`/`Push`/`ContainsType` lose their birthList special cases, and
"pending spawn nobody can see" ceases to exist as a category.

- Prereq: **Setup-before-Add audit** — `tools/audit_add_order.py`, a lint-style script that
  finds every `.Add((GameComponent)` site and verifies the added variable's `Setup(`/config
  precedes it in the same method (whitelist for the genuinely-configless types). Fix any
  outliers first, keep the script for future regressions.
- The net `SuppressWorldSpawn` divert-to-idle branch in `Add()` is unchanged.

### 2. Zombie frame dies without touching removal timing

Keep `deathList` and the existing flush point, and ALSO flush deaths at the TOP of the tick
(before `base.Update`). Deaths queued during the collision phase of tick N are then gone
before tick N+1's component updates — no post-death AI/fire/spawn — while deaths queued
during the update phase still flush at the existing mid-tick point, so within-tick collision
semantics are byte-identical (a dying enemy still soaks bullets for the rest of its kill
pass, off-screen self-removals still skip that tick's collisions, and the component is
removed before its next possible Draw either way).

### 3. Purges become a standing filter for the rest of the tick

`Purge<T>` additionally records `typeof(T)` in a `pendingPurges` set, cleared at the
mid-tick flush of the NEXT tick (i.e. the filter covers the purge tick's remaining updates
+ collision phase). `Add()` checks the set: a component assignable to a pending purge type
is diverted to the recycle pool instead (the exact shape of the proven
`SuppressWorldSpawn` branch). A clear-all followed by a late same-tick spawn now actually
clears all.

### 4. Adds during a pushed (paused) world join the freeze

`Add()` while `inactive.Count > 0`: if the component is a world object
(`AlienDrawableGameComponent`), it is added with `Enabled = false` and registered into the
newest `inactive` list, so `Pop()` thaws it with everything else. Non-world components
(menus, darkener, overlays) keep today's behaviour — the pause UI itself is added while
pushed and must run. The frozen-puppet exception in `Pop()` is unchanged.

### Diagnostics (cheap, permanent)

`?binlog` DebugFlags seam: log when (a) an Add is diverted by a pending purge, (b) an Add
is frozen by rule 4, (c) the death flush removes a component that was Enabled at top-of-tick
(would-have-been zombie). Plus wire the existing unused `ComponentBin.test()` duplicate
check behind it. Console `eaBinTest()` runs a scripted Add/Remove/Purge/Push/Pop scenario
suite against a scratch bin and prints PASS/FAIL (the `eaNetSim.test` precedent).

## Files

- `Game/EvilAliens/ComponentBin.cs` — all four mechanisms.
- `Game1.UpdateInner` — the top-of-tick death flush call.
- `Compat/DebugFlags.cs` + `Compat/DebugInput.cs` — `?binlog`, `eaBinTest`.
- `tools/audit_add_order.py` — new audit script (+ fix any Add-before-Setup sites it finds).
- `web/EvilAliensWeb/CLAUDE.md` — document the new lifecycle contract.

## Verification

1. `tools/audit_add_order.py` clean over the tree.
2. `eaBinTest()` scenario suite green (spawn-after-purge diverted; pause-add frozen +
   thawed by Pop; no zombie update after collision-phase kill; recycle round-trip).
3. Real-game smokes (fast-boots, `?invuln&aiplayer` where useful): ClassicAliens wave
   clear + wave-completed powerup; Level1 checkpoint death mid-swarm (reset leaves zero
   strays — count via `eaBinTest` audit line); pause mid-swarm (nothing moves); spider
   boss + helper mothership lazer sequence; a full Victory. Zero console errors.
4. Net two-tab sanity (`?level=Level1&net=host&aiplayer&invuln&room=X` + join): puppets/
   claims unaffected (`[net]` metrics healthy) — the bin seams are load-bearing for 11.2.

## Out of scope

- Instant removal (user-declined; also technically hazardous — collision pass corruption).
- Reworking `Recycle`'s type-scan or the idle pool structure.
- Game-logic-level despawn bugs in individual types (if a specific enemy mismanages its own
  lifetime beyond the four holes above, that's a follow-up card with a repro).
