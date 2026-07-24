# Replicate background COSMETIC swarms as one "effect on/off" beat

Card `9a3175d0`. Branch `feature/net-cosmetic-swarms`, worktree `wt4`.

## Context

Purely decorative entities are replicated per entity today, at full cost, for no gameplay
reason. Background `FlyingSpider`s (`?flyspiders`, and Level 2's real fog swarm) each get a
`NetId`, an `EvSpawn`, an `EvDeath` and a share of the 16-entries-per-60ms world-snapshot
round robin — while `Collides=false` means they can never be shot, never hurt anyone, and are
invisible to the AI (`PlayerShip.IsAiShootable` gates FlyingSpider on `baddy.Collides`; the
threat scan gates on `baddy.Collides` too). Their whole visible state is a sinusoid plus a
constant X drift, which every peer can compute for itself.

Measured in the `?flyspiders` rig: `liveIds` settles at 17-19, i.e. essentially the WHOLE
snapshot budget is scenery, and `snapTurn` (the mean blind dead-reckoning window of every real
enemy) is averaged over that count.

**Background `Asteroid`s are the same shape and are also replicated.** `AsteroidSpawner.DoEvent`
spawns two `SetBackground()` asteroids (grey, `Collides=false`, `DrawOrder 1`) per event
alongside one real one, at 4/s in Level 1's belt and 5/s in `AsteroidChase` — a bigger per-second
cosmetic load than the spiders. Verified: `SetBackground` is reachable only from
`AsteroidSpawner`, `Collides` never flips back on afterwards, and nothing gameplay-facing reads
them (`Asteroid` is in `Oracle.GetBaddies` but every AI consumer gates on `Collides`).

So both are in scope.

## Design

Two halves: an **instance-level opt-out** so a cosmetic instance is never replicated, and a
**spawner-level beat** so the joiner runs its own copy of the effect.

### 1. Instance-level opt-out

`AlienDrawableGameComponent.NetCosmeticOnly` — `internal virtual bool`, default `false`,
exactly the `NetSpinPerMs` / `NetDriveExtras` idiom (an instance-level net seam that lives on
the type).

- `FlyingSpider` overrides it to `isbackground` (pinned in `Setup`, before `bin.Add`).
- `Asteroid` overrides it to its background flag (set by `SetBackground()`, also before
  `bin.Add`).

Read at the `ComponentAdded` seam, so the value must be final before `Add` — which it is at
both sites, and is already the repo-wide rule (`tools/audit_add_order.py`).

One predicate, `NetTypeRegistry.IsReplicableInstance(c)` = `IsReplicable(c) && !NetCosmeticOnly`,
consulted at all three places the type-level test is used today:

| Site | Why it must consult the instance |
|---|---|
| `NetIdRegistry.Components_ComponentAdded` | the point of the card: no NetId, no EvSpawn, no snapshot turn |
| `NetSession.SuppressWorldSpawn` | **load-bearing** — otherwise the client's OWN cosmetic spawns get diverted into the recycle pool and the joiner sees no scenery at all |
| `NetSession.NoteKill` | a cosmetic entity has no id to attribute a kill to |

**Rule (documented at the property):** only for an instance that can never become collidable and
that nothing gameplay-visible reads.

### 2. Spawner-level beat

New reliable event `EvCosmeticSwarm = 21`, payload `[kind:1][on:1][rate:f32]`. Protocol
**v9 -> v10**. `NetCosmeticKind` is APPEND-ONLY, wire value = enum value:

```
FlyingSpiderBackground = 0   // FlyingSpiderEvent(isbackground: true)
BackgroundAsteroids    = 1   // AsteroidSpawner's SetBackground() pair
```

**Host announce** — at the primitive, the same rule the script beats follow:

- `FlyingSpiderEvent` (when `isbackground`) announces `on` on its first `Update` after becoming
  live, and `off` from its own `OnFinished` (Level 2 terminates it via `LinkWith`). `Reset()`
  clears the announced flag, so a checkpoint revert re-announces.
- `AsteroidSpawner` announces `on`/`off` the same way (its background pair is unconditional).

**Latch + JIP catch-up** — `GameScene` holds the live `(kind, rate)` latch, updated by the
announce and replayed from `NetReplayCatchUp` next to `Background.NetReplayCatchUp` (card
45a4e48d's seam, the `EvReady` handler). Latching happens at the announce, NOT off the send
path — a listed single-player game announces with no peer connected and must still remember.

**Client apply** — `GameScene.NetApplyCosmeticSwarm(kind, on, rate)` builds the matching spawner
locally and ticks it in `UpdateNormal`, in the branch that currently just skips
`eventList.Update` for a client:

```csharp
if (!NetSession.SuppressLevelScript) { eventList.Update(gameTime); }
else { /* tick the cosmetic spawners */ }
```

Putting them there rather than in a component of their own is deliberate: the whole scene is
already disabled under a pause `Push`, and `UpdateNormal` only runs in `GameState.Normal`, so
pause / victory / resetting are handled by the existing state machine for free.

The client's copies are built through the real constructors:

- `new FlyingSpiderEvent(game, 0f, rate, isbackground: true)`
- `new AsteroidSpawner(game, 0f, rate, startWithBig: false)` + `SetBackGroundOnly()` — which is
  what that (currently dead) seam is for; `startWithBig:false` is what keeps the client from
  spawning the real, collidable big asteroid.

**Reset** — a checkpoint revert drops the host's active events without terminating them, so
both sides clear the cosmetic set on reset (`RevertToCheckpoint` host-side, `NetApplyReset`
client-side). The host's re-activated event re-announces on its next tick; one that was NOT
re-activated correctly stays off.

**Idempotence** — a second `on` for a kind replaces the spawner (rate may differ); an `off` for
a kind that isn't running is a no-op. Spiders/asteroids already in flight when an `off` arrives
are left to fly off screen, which is what the host does.

### Divergence

Accepted by definition — the two screens' scenery need not be in the same places. Confirmed
nothing gameplay-visible reads them (above). Note the client now consumes the shared
`RandomHelper` RNG for its own cosmetic spawns; this is not a lockstep design and the client
already consumes it during puppet construction, so nothing depends on the streams agreeing.

## Files

| File | Change |
|---|---|
| `Game/EvilAliens/AlienDrawableGameComponent.cs` | `NetCosmeticOnly` virtual + the contract comment |
| `Game/EvilAliens/FlyingSpider.cs` | override; announce seam accessor |
| `Game/EvilAliens/Asteroid.cs` | override + explicit background flag |
| `Game/EvilAliens/FlyingSpiderEvent.cs` | announce on/off |
| `Game/EvilAliens/AsteroidSpawner.cs` | announce on/off |
| `Game/EvilAliens/GameScene.cs` | latch, catch-up replay, client apply + tick, reset clear, state line |
| `Compat/Net/NetTypeRegistry.cs` | `IsReplicableInstance` |
| `Compat/Net/NetIdRegistry.cs` | consult it |
| `Compat/Net/NetSession.cs` | `SuppressWorldSpawn` / `NoteKill` / `OnCosmeticSwarm` / rx case |
| `Compat/Net/NetProtocol.cs` | `EvCosmeticSwarm`, `NetCosmeticKind`, codec, v10 |
| `Compat/Net/NetCosmeticTest.cs` | new — the self-test |
| `Compat/DebugInput.cs` + `wwwroot/index.html` | `eaNetCosmetic()` console entry |
| `web/EvilAliensWeb/CLAUDE.md` | the net-layer bullets |

## Verification

**The user has asked for no live browser testing this session.** So the gate splits:

Offline, done by me:
- clean `dotnet build -c Debug`;
- `python tools/verify_decompiled_diff.py --ref main` to prove the change is confined to the
  files above (a stray edit elsewhere is exactly what this catches);
- diff spot-check against the repo's specials (no lowercase `content/`, no
  `BlendState.AlphaBlend`, no codegen re-run).

Delivered as tooling, needs ONE browser run (flagged to the user, not skipped):
- **`eaNetCosmetic()`** (`Compat/Net/NetCosmeticTest.cs`), leave-no-trace, main-menu runnable:
  1. **codec** — every kind/rate round-trips through the real `EncodeCosmeticSwarmEvent` ->
     decode, at the real wire offsets;
  2. **predicate** — a background `FlyingSpider`/`Asteroid` built through its REAL factory +
     `Setup`/`SetBackground` reads `IsReplicableInstance == false`, and the foreground form of
     the same type reads `true` (the positive control — a green tick means nothing without it,
     per the `eaNetScore.test()` rule);
  3. **apply** — `NetApplyCosmeticSwarm(on)` / `(off)` drives the live set, skipped with a
     printed SKIP when no `GameScene` is up.
- **`eaNetBgTest()`** gains the cosmetic leg (latch -> encode -> apply -> compare), and
  `eaNetBg()`'s state line gains `cosmetic=` so two peers can be diffed.
- A two-window JIP smoke check (`?level=Level2&flyspiders&netjip...`) would show `liveIds` and
  `snapTurn` collapsing on the host — the headline number. Not run this session.

A screenshot proves nothing here: the joiner's scenery is supposed to be in DIFFERENT places
than the host's after this change, so the only honest checks are the data ones above.

## Out of scope

- Any change to how foreground (collidable) spiders/asteroids replicate.
- The `NetSpinPerMs` / descriptor entries for the now-cosmetic forms: left in place, harmless,
  and still exercised by the foreground forms (the wire table is append-only anyway).
- Other scenery: nothing else in the replicable set is non-collidable by construction.
- Music-rate replication, mid-boss puppet fidelity, and the other known net gaps.
