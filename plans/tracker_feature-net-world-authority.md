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
      timers tick), spawn/death/claim application + snapshot self-heal resurrect
- [x] NetSession: snapshot scheduler (host ~16.7Hz), claims, score sync, metrics,
      host-relative wire-slot translation (TranslateSlot)
- [x] Game seams: CollisionHandler puppet-collidable, ComponentBin add-gate +
      Pop guard, GameScene client suppression, KillableAlien NetKill/notes,
      AlienDrawableGameComponent internal accessors, PlayerShip powerup claim hook
- [x] FARM OUT ~20 per-type descriptors to parallel Opus agents (batches A-D)
- [x] Review + integrate descriptor batches (all 21 types; builds clean)
- [x] Docs: web CLAUDE.md net section update (drafted; re-check before ship)

## Phase 5: Verify -- IN PROGRESS (see "Session 1 verification state" below)
- [x] Clean Debug build (all descriptors in)
- [~] Two-window gate: infrastructure PROVEN end-to-end on the pre-descriptor build
      (see below); the post-descriptor + post-fix run NOT yet done
- [ ] Client kills honored on host (clKill > 0) -- blocked on the re-run
- [ ] Powerup collection replicates
- [ ] Double-claim test (clPaid > 0 while enemy dies once)
- [ ] Metrics healthy both sides, zero console errors
- [ ] Plain no-flag boot smoke check

## Phase 6: Review & Ship -- NOT STARTED
- [ ] Commit + push (WIP commits exist; see below)
- [ ] /review + fix all findings
- [ ] Pull main, re-verify
- [ ] PR + self-merge, fast-forward main
- [ ] Remove worktree + branch
- [ ] Delete tracker; card -> Done + summary comment; follow-up cards

---

## Session 1 verification state (2026-07-13, handoff)

What RAN and what it proved (two-window BroadcastChannel loopback, rooms g1-g3):
- Two Chrome WINDOWS are REQUIRED: two tabs in one window throttle the hidden tab
  to ~1Hz (the setTimeout fallback is not enough) -> mutual 3s peer timeout.
  WORKING RECIPE: host tab in the MCP tab group; inject a button whose pointerdown
  handler window.open()s the join URL as a popup window, then click it via
  claude-in-chrome `computer` (a trusted click carries user activation; a plain
  javascript_tool window.open is popup-blocked). Read the popup's console by
  wrapping p.console FROM the host tab (same origin) into a window._joinLog array.
  Clicks sometimes don't register right after a (re)load -- screenshot, re-click
  the button center, verify via the button label flipping to OK.
- On the PRE-descriptor build (UFO-only puppets): session start v2 both sides,
  peer up, ship streams healthy (0 drops/gaps/pops), snapTx ~16.7Hz, snapshots
  applied (snapEnt >> snapUnk), UFO puppets live on the join side, killer-
  attributed EvDeaths applied, claims flowing client->host (clTx == clRx), ZERO
  console errors/exceptions on either side over ~10 min of AI-vs-AI Level1.
  Also confirmed the sim-split: host liveIds tracked the wave; the join world had
  no real enemies (only puppets); the level script stayed host-only.
- Two REAL BUGS found from those metrics, FIXED in the WIP commit:
  1. Echo-claims: puppet removal is DEFERRED a tick (ComponentBin deathList), so
     the applyingRemoteDeath bool missed the removal seam and every host-initiated
     death echoed back as a claim (killer=None -> clKill/clPaid stayed 0). Fix:
     `remoteDeaths` HashSet membership consumed at the removal seam (NetPuppets).
  2. Slot numbering: each side seats its LOCAL ship in slot 0, so client claims
     credited the wrong slot and EvScoreSync was cross-wired. Fix:
     NetSession.TranslateSlot (wire slots are host-relative; the join side swaps
     0<->1) applied at SendClaim tx, EvDeath rx, EvScoreSync rx.

NOT yet verified (the remaining gate, all on the current full build):
1. Fresh-room two-window run: expect join liveIds ~= host liveIds (all 21 types
   now puppet), snapUnk low and flat, dup ~0, pupPops ~0.
2. Client kill honored: host clKill > 0; enemy dies both sides with FX + score.
3. Powerup pickup claim (bonus UFO drop): collected one side -> despawns both;
   overlapping pickups inside the RTT window both keep it.
4. Double-claim: both AIs focus-fire -> host clPaid > 0 (that metric IS the
   proof) while the enemy dies once.
5. Zero console errors both sides on the FULL build -- the 20 new descriptors'
   CreatePuppet/ApplyStateExtra paths have NEVER run in a browser yet.
6. Plain no-flag boot smoke check (net never constructed).
7. THEN Phase 6: /review + fix, pull main, re-verify, PR self-merge, worktree +
   branch cleanup, tracker deletion, card aa58cde3 -> Done + summary comment,
   follow-up cards.

Known issues / notes for the next session:
- An orphaned popup window (rooms g2/g3 rigs) may still be open on the desktop
  showing an error page -- close manually if present.
- The wt5 dev server must run DETACHED (tracked background tasks were being
  stopped externally): powershell Start-Process -FilePath dotnet -ArgumentList
  'run','--project','web/DevServer','-c','Debug','--urls','http://localhost:5285'
  -WorkingDirectory <wt5>. Kill via the PID listening on 5285 when done.
- Braineroid pulsate caveat (batch B): the host encoder samples comp.scale on the
  game tick; if Braineroid applies pulsate inside Draw (restore-after), puppet
  brains won't breathe -- check during the gate run; if steady, encode the
  effective (pulsated) scale or add a state extra.
- SweepUFO/MarsBoss puppets don't show the LazerGenerator charge-up glow (child
  component not replicated; the fired beam replicates as Lazer) -- follow-up card.
- AI friends are disabled in ALL net sessions (their ships aren't replicated) --
  a deviation from the design doc's "host runs AI friends"; follow-up card.
- Follow-up card candidates: types outside the 11.1 replicable set (PlasmaBall,
  paratroopers, SpiderBoss, BrainBoss, FakeBoss, SpiderHelperMothership);
  LazerGenerator charge-glow replication; AI-friend ship replication.

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
