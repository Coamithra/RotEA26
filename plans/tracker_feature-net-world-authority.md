# Tracker: feature/net-world-authority (card aa58cde3 -- Stage 11.2 World authority)

## Phase 1: Pick Up the Card
- [x] Claim card aa58cde3 (moved to In Progress)
- [x] Pull latest main
- [x] Read card + plans/stage11-online-coop.md + web CLAUDE.md net section
- [x] Create worktree wt5 + branch feature/net-world-authority

## Phase 2: Research
- [x] Read Compat/Net/* (11.1 skeleton), GameScene, GameEventList, GenericSpawner,
      ComponentBin, CollisionHandler, KillableAlien, AlienDrawableGameComponent,
      Bullet, Powerup, ScoreVisualiser, Oracle, PlayerShip seams, HarnessRegistry
- [x] Key findings (design inputs):
  - Client sim-split seam = GameScene.UpdateNormal's `eventList.Update()` call
    (spawners/script only act in GameEvent.Update; Reset() arms timers only).
  - CollisionHandler gates every pair on `GameComponent.Enabled` -> a frozen
    (Enabled=false) puppet needs a collision-handler seam to stay hit-testable.
  - ComponentBin.Add is the single choke point to swallow client-side gameplay
    spawns (KilledBy side effects like Asteroid splits / powerup drops).
  - ComponentBin.Pop re-enables everything -> must not re-enable puppets.
  - Enemy death FX/score live in per-type KilledBy(other, combo); a recycled
    scratch Bullet with the claimant's slot is a cast-safe IAlienKiller agent.
  - Die() sets IsDead before the deferred ComponentRemoved fires -> IsDead
    distinguishes gameplay deaths from Purge/Terminate teardown (claim filter).
  - Many types move Position directly (not Speed/Direction) -> snapshot velocity
    must be OBSERVED (pos delta / dt at encode), not SpeedVector.

## Phase 3: Design (detail below)
- [x] Design settled
- [x] Card TLDR comment posted

## Phase 4: Implement
- [x] NetProtocol: MsgWorldSnapshot codec, EvSpawn v2 (full state), EvDeath v2
      (killer/pos/points), EvClaim, EvScoreSync; protocol v2
- [x] NetTypeRegistry + INetTypeDescriptor contract + worked example (UFO)
- [x] NetIdRegistry: live list + id lookups + kill notes + spawn-state replay
- [x] NetPuppets: client puppet map + driver component (DR, lerp-correct, freeze,
      timers tick), spawn/death/claim application
- [x] NetSession: snapshot scheduler (host ~16.7Hz), claims, score sync, metrics
- [x] Game seams: CollisionHandler puppet-collidable, ComponentBin add-gate +
      Pop guard, GameScene client suppression, KillableAlien NetKill/notes,
      AlienDrawableGameComponent internal accessors, PlayerShip powerup claim hook
- [x] FARM OUT ~20 per-type descriptors to parallel Opus agents (batches A-D)
- [x] Review + integrate descriptor batches
- [x] Docs: web CLAUDE.md net section update

## Phase 5: Verify
- [x] Clean Debug build
- [x] Two Chrome WINDOWS gate: host `?level=Level1&net=host&aiplayer&invuln&room=<r>`
      + join same with `net=join`; same wave visible both sides
- [x] Client kills honored (enemy dies both sides, client score/combo local + host ledger)
- [x] Powerup collection replicates (first claim wins, both honored inside RTT)
- [x] Double-claim test: focus-fire one enemy -> both peers credited, enemy dies once
- [x] Metrics lines healthy both sides (pops self-heal, no ordViol), zero console errors
- [x] Plain no-flag boot byte-identical (no net construction; smoke check)

## Phase 6: Review & Ship
- [x] Commit + push
- [x] /review + fix all findings
- [x] Pull main, re-verify
- [x] PR + self-merge, fast-forward main
- [x] Remove worktree + branch
- [ ] Delete tracker; card -> Done + summary comment; follow-up cards

---

## Design (agreed approach)

Everything below only runs when `?net=` is present (hard invariant preserved).

**Host/client split.** Host = 11.1 role. Client suppression, all keyed on
`NetSession.IsClient`:
- `GameScene.UpdateNormal`: skip `eventList.Update()` + AI-friend join (level
  script beats replicate in 11.4-era card; initial bg/music still local).
- `ComponentBin.Add` gate: replicable-type components constructed by GAME code
  on the client (KilledBy splits, powerup drops, spawner strays) are swallowed;
  only the puppet layer (construction guard flag) may add replicable types.
  Cosmetics (Explosion/FloatingText/player Bullets...) pass untouched.
- Client's own ship, own bullets, blasts, background, HUD run normally.

**Puppets.** On EvSpawn the client constructs the real enemy via its New*+Setup
factory (descriptor-chosen args), adds it through the bin, then freezes it
(Enabled=false, re-asserted; ComponentBin.Pop patched to not re-enable puppets).
CollisionHandler treats a puppet as collidable while the puppet driver is
enabled (so pause still freezes collisions). A single NetPuppetDriver
(UpdateOrder -1000) per tick: dead-reckons Position += vel*dt, advances
curframe by fps*dt, blends a correction offset from the latest snapshot
(~150ms, snap when > threshold => pop metric), lerps scale, re-applies hp
(min(local, snapshot), floored at 1 -- deaths only via events/local kills),
ticks the puppet's timers (blink decay etc).

**Snapshots.** Host, stream lane, every 60ms, round-robin cursor over the live
NetId set, <=16 entries/packet (~<=500B). Entry: [len][netId:2][typeIdx:1]
[pos:8][vel:8 observed px/ms][rot:2][curframe:2 x64][scale:2 x256][hp:2]
[per-type state extra]. Unknown netIds skipped via len.

**Descriptors.** `INetTypeDescriptor` per replicable type (21 types), fixed
ordered registry = wire typeIdx. Owns: spawn-extra encode + puppet construction
(New*+Setup), state-extra encode/apply (anim sheet swaps, phases, facing).
Base-only types = trivial descriptor + justification. Farmed out to Opus in 4
parallel batches, reviewed line-by-line, integrated by me.

**Claims (generous at-least-once).** Client-side, ANY puppet removal with
IsDead (gameplay death: local bullets, blasts, its own Die) => EvClaim(netId,
killerSlot) on the reliable lane + full LOCAL death already happened through
the real per-type KilledBy (explosion, sound, score, combo -- paid locally to
the killer slot, incl. re-fired remote bullets' kills). Host on EvClaim:
- enemy alive -> NetKill via a scratch Bullet with the claimant's slot => real
  KilledBy (FX + score + authoritative children spawns) -> EvDeath broadcast.
- enemy already dead -> recent-death ledger: pay score once per (netId, slot),
  ignore repeats. No rejection path anywhere.
Host EvDeath(netId, killerSlot, pos, points) on every replicable removal.
Client on EvDeath: live puppet + killer -> NetKill locally (FX + credit);
live puppet + no killer -> silent remove; already dead -> ledger-pay the
killer slot if not yet credited locally. Ledger = bounded ring (256), per-side.
Powerups: local pickup runs the real PlayerShip path (instant effects) then
claims; host removes its copy on first claim (idempotent via `taken`); both
peers inside the RTT window keep the pickup. EvDeath on a Powerup = silent
remove (pickup or fly-off).

**Score/lives.** Immediate local generous crediting (above) + host-
authoritative EvScoreSync at 1Hz: per-slot score (client adopts max(local,
host) -- monotone within a life) + lives (client adopts verbatim).

**Out of scope (documented):** level-script beats, pause/checkpoint-reset/
shared-fate death, tether (11.4-era cards); WebRTC (11.4/11.5); types outside
the 11.1 replicable set (PlasmaBall, paratroopers, SpiderBoss, BrainBoss,
FakeBoss, SpiderHelperMothership) -> follow-up card; host slowmo/hit-stop makes
clients lerp-correct (self-heals by design).
