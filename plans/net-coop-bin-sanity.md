# Net: two-tab co-op sanity vs the new ComponentBin lifecycle

Card `9009a1c4`. Branch `fix/net-coop-bin-sanity`, worktree `wt4` (port 5284).

## Context

Card 02d9ad67 (PR #141, commits `5f3f1ce` + `3d9aec8`) reworked `ComponentBin`:

- **births are INSTANT** — `Add` puts the component into `Game.Components` inside the call
  (was: queued in a `birthList`, applied at an end-of-tick flush);
- **deaths flush twice** — the existing mid-tick point plus a new `TopOfTickFlush` in
  `Game1.UpdateInner`, so a collision-phase kill never gets one more "zombie" Update;
- **standing purge filter** — `Purge<T>()` records `T` in `pendingPurges`; every `Add` of a
  matching type until the next `TopOfTickFlush` is **diverted to the recycle pool**;
- **pause-aware adds** — a world object (`AlienDrawableGameComponent`) added while the world
  is pushed joins the freeze and the newest pause layer.

The net layer's three seams were preserved contract-for-contract, and the ONLY net-layer
change in the PR was a comment (`git show 3d9aec8 -- Compat/Net/NetIdRegistry.cs`). But the
seams now execute under different **timing**, and `eaBinTest` covers the bin in isolation with
no session up. No live two-window session has been driven against it. That is this card.

### The three seams and what moved under them

| Seam | Where | What the rework changed |
|---|---|---|
| `SuppressWorldSpawn` divert | `ComponentBin.Add`, first branch | Now runs synchronously at the call site instead of at the flush; sits **before** the new purge-filter branch |
| `Pop`'s frozen-puppet exception | `ComponentBin.Pop` | Unchanged, but `Add` can now enrol a puppet into a pause layer that `Pop` later walks |
| `NetIdRegistry` on `ComponentAdded/Removed` | `Compat/Net/NetIdRegistry` | The event now fires **inside `Add`**, so `OnHostSpawn` encodes the entity's base state at Add time, not at end-of-tick |

## Risk model — what a failure would look like

Derived by reading the tick order in `Game1.UpdateInner`:

```
TopOfTickFlush()          <- expires the standing purge filter
input / vibrator / sound / storage
base.Update()             <- all components, incl. GameScene.UpdateResetting / UpdateWin / Terminate
collectionHelper.Update() <- mid-tick death flush
collisionHandler.DetectCollisions()
NetSession.Update()       <- drains rx, builds puppets, adopts the remote ship  <-- SAME tick, AFTER the purges
```

**R1 — purge filter eats a puppet (client).** `GameScene` purges
`AlienDrawableGameComponent` / `AnimatedMessage` / `TutorialMessage` with the default
`standing: true` at `UpdateResetting` (:1015), `UpdateWin` (:984) and `Terminate` (:1104).
`NetSession.Update()` runs later in that same tick. `NetPuppets.OnSpawn` sets `constructing`
(so it clears `SuppressWorldSpawn`) but is **not** exempt from `IsPendingPurged` — a puppet
built in that window is diverted to idle, yet `OnSpawn` still registers it in
`byId`/`idByComp`/`live` unconditionally. Result: a **ghost puppet** — never drawn, never
collidable, and because the id IS known, `OnSnapshotEntry`'s self-heal never rebuilds it and
`snapUnk` does not climb. Silent.

**R2 — purge filter eats the remote ship.** `NetApplyReset` (GameScene:313, called from
inside the rx drain at `NetSession.cs:1446`) and host `LoseLife` (:291) both
`Purge<PlayerShip>()` / `Purge<PlayerShipSummon>()` standing. `NetSession.SpawnPuppet`
(:1652) does `bin.Add(ship)` then assigns `puppet = ship` **unconditionally**; a diverted add
leaves `puppet` non-null pointing at a ship outside the world, and the retry guard is
`puppet == null` — so the remote player would be permanently invisible after that reset.

**R3 — replicated banners eaten.** Same shape for `NetSession`'s `bin.Add(msg)` (:1369) and
`bin.Add(banner)` (:1404) against the standing `AnimatedMessage`/`TutorialMessage` purges.

**R4 — EvSpawn encodes pre-Setup state.** `NetIdRegistry.Components_ComponentAdded` →
`OnHostSpawn` now reads position/rotation/frame/hp/scale **inside `Add`**. Any spawn site
that configures after `Add` would replicate garbage. `tools/audit_add_order.py` covers exactly
this shape and reports `318 Add sites scanned, 0 config-after-Add suspects` — so this is
expected clean; the live pass is the confirmation.

**R5 — pause layering vs puppets.** A puppet spawned while the client holds a remote-pause
freeze now gets `Enabled=false` + enrolment in the newest pause layer; `Pop` then keeps it
frozen via `IsFrozenPuppet`. Expected correct (and better than before), but it is a new code
path that has never run live.

All of R1–R3 print a `[bin] purge-filter diverted <Type>` line under `?binlog` — that flag is
the detector, so it goes on **both** tabs from the start rather than being kept in reserve.

## Verification protocol

Two Chrome **windows** side by side, both visible (a backgrounded tab drops to ~1 Hz rAF and
invalidates the run). Dev server = wt4's own `web/DevServer` on **5284**. `?room=` fresh per
pair. `?binlog` on both.

| Pass | Host URL | Join URL | Watching for |
|---|---|---|---|
| **A** steady state | `?level=Level1&net=host&aiplayer&invuln&room=binsan1&binlog` | same, `net=join` | `[net]`: `pops`/`pupPops` ~0, `snapUnk` small + non-climbing, `drop`/`dup`/`ordViol`/`seqGap` 0, `clTx`(join) ≈ `clRx`(host), `liveIds` sane. Zero `[bin] purge-filter diverted`. Zero console exceptions. |
| **B** pause/resume | pause from host mid-level, resume; then repeat from join | | Both worlds freeze/unfreeze together, `pauses` counter increments both sides, no `[bin] pause-froze` for anything that should have kept running, puppets stay frozen after `Pop` (no puppet suddenly self-driving), metrics resume healthy |
| **C** host death / reset | let the host die (drop `?invuln` on the host, or fly into a hazard) | | `resets` increments both sides, `[bin] purge-filter diverted` lines around the reset are the **R1/R2/R3 signal**, remote ship still visible on both screens afterwards, no stray/invisible puppets, `snapUnk` does not step up |
| **D** JIP | `?level=Level1&netjip&aiplayer&invuln&binlog` host, join via the room code | | `ReplayLive` builds the already-alive world on the joiner, same metric bar as A |
| **E** bin unit gate | `eaBinTest()` in both consoles | | `[bin] N passed, 0 failed` (regression guard that the session itself didn't disturb the contract) |

Pass A runs long enough (≥3 `[net]` reports ≈ 15 s, target ~60 s) that "non-climbing" is a
real observation and not a single sample.

## Results (2026-07-24)

**One real regression found and fixed — and it was not in the net layer.**

`CollisionHandler.DetectCollisions` threw an intermittent `IndexOutOfRangeException`
(13 occurrences on the host in the first ~2 min, 6 more on the join side), swallowed by
`index.html`'s `tickJS` guard as a dropped frame. Stack captured by wrapping
`theInstance.invokeMethod` from the console:

```
List<List<CollisionHandler.BoxInfo>>.get_Item(Int32 index)
  at CollisionHandler.DetectCollisions()  CollisionHandler.cs:147
  at Game1.UpdateInner                    Game1.cs:990
```

Root cause: `boxes` is sized to a `count` snapshot taken at entry, but the fill and
resolution loops re-read the live `collidables.Count`. Instant births (card 02d9ad67) let a
collision callback's spawn grow `collidables` mid-pass via `Components_ComponentAdded`, so
`boxes[m]` ran past what the pass had sized. Only reachable once the live count exceeds the
`boxes` high-water mark, which is why it clusters in busy moments. Under the old deferred
`birthList` the collection could not grow mid-pass at all. Two more latent faults on the same
root: entries between the old and new count still held the previous frame's cells, and the
inner all-pairs `foreach (… in collidables)` was one mid-pass spawn from
`InvalidOperationException`. Fixed by freezing the count for the whole pass.

Proof it is fixed AND that the path is exercised (absence of an intermittent crash proves
little on its own): a `?binlog` counter reports mid-pass growths. Both peers log
`[bin] collision pass held its frozen count through N mid-pass collidable add(s)` with N
climbing into the hundreds (host 171→213, join 252→276) while tick exceptions stay at **0**.

| Pass | Result |
|---|---|
| **A** steady state | PASS — `drop/sgap/dup/ordViol/seqGap` 0 both sides, `buf` 79–122 ms, host `pupPops` 0, `clPaid` 19 (generous-claim path proven), 0 exceptions |
| **B** pause/resume | PASS — "OTHER PLAYER PAUSED" overlay + frozen world on the opposite tab in BOTH directions, clean resume, `pauses=2` both sides |
| **C** death/reset | PASS — `resets=1`, `localShip=1 remoteShip=1` after the reset on both peers (no lost remote ship), world repopulated, 0 exceptions |
| **D** JIP | PARTIAL — see below |
| **E** `eaBinTest` | PASS — 10/10 on both peers, run against a LIVE session, so `pop thaws it` exercised the real frozen-puppet exception |

**R1/R2/R3 did not fire.** `?binlog` was armed on both tabs for every pass; the only
`purge-filter diverted` / `pause-froze` lines in the entire session came from `eaBinTest`'s own
`TestAlien`. A dedicated reset with both consoles freshly cleared produced **zero** diverts on
either side. The races remain real but unobserved — the window is one tick, and the host's
checkpoint replay lands its spawns on later ticks. Left as a documented follow-up rather than
a speculative fix.

**`snapUnk` is not a leak indicator.** It climbed steadily in pass A (673→703) but tracks
`clTx` at ~1.1–1.4 per claim — the host still snapshotting an entity while a client claim is
in flight, which the design deliberately leaves dead. Confirmed by the converse: in pass C,
with `clTx` flat at 107, `snapUnk` froze at 639 across three samples. Correlation over 4+3
samples, not a proof.

**Pass D is partial and deliberately so.** The bin-relevant half of join-in-progress ran in
*every* pass — the joiner always connected to an already-running mid-level host, so
`ReplayLive` + the snapshot self-heal built the whole live world through
`NetPuppets.OnSpawn` → `bin.Add`. The untested half is the `?netjip` listing/signaling
handshake, which needs the live VPS signaling server, does not touch `ComponentBin`, and is
card 2001fbd8's own gate. Flagged as a follow-up rather than silently claimed.

## Outcome handling

- **Nothing found** → the card closes as a verification pass: the `[net]`/`[bin]` transcripts
  go in the card comment, and the two-window recipe's bin-lifecycle caveats get a line in
  `web/EvilAliensWeb/CLAUDE.md` so the next person knows this was checked and how.
- **Small, clearly-scoped regression** (R1/R2/R3 are all one-line-ish: exempt the puppet layer
  from the purge filter the way it is already exempt from `SuppressWorldSpawn`, and/or make
  `SpawnPuppet` verify the add landed) → fix inside this card, since it is the same lifecycle
  contract the card is auditing, and re-run the failing pass.
- **Structural** (protocol change, ordering rework, anything touching the tick order) →
  follow-up card, with the repro recorded.

## Out of scope

- The headless two-peer sim (`plans/net-headless-sim.md`) — design-only, its own card.
- Real WebRTC / signaling-server paths (`?rtc`): this card is the BroadcastChannel rig.
- Impairment sweeps (`?netlag`/`?netloss`) — card 40334a8f's own gate; a clean link is the
  right baseline for isolating a bin-lifecycle regression.
- Boss-puppet fidelity, and every other known limit already documented in web `CLAUDE.md`.
