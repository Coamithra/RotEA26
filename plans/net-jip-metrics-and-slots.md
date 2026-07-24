# Card 48ab9b2f — JIP joiner metrics (pupPops/snapUnk) + the browser's `1/2` players column

## Context

Card `c0398370`'s two-window `?netjip` JIP pass left two "look into it" observations:

1. The joiner logged `pupPops 207` and `snapUnk 344` over ~25s of steady state, against web
   CLAUDE.md's stated healthy targets (`pupPops` near 0, `snapUnk` small and not climbing). The
   docs' own heuristic — *"flat `clTx` with climbing `snapUnk` is the shape that means trouble"* —
   **could not be applied**, because `clTx` was 0 (the joiner cannot pass `?aiplayer`, so its ship
   idled and never claimed a kill). The rig also ran both peers at ~40fps against the dense
   `?flyspiders` swarm, a plausible innocent explanation.
2. The "Join Online Game" carousel prints `Players: N/2` although card `4d904410` relaxed the
   roster to 4 slots.

Observation 2 is a confirmed, trivial bug. Observation 1 is **not decidable from the counters the
game currently prints** — that is the actual defect this card fixes.

## Findings

### `snapUnk` conflates three unrelated situations (`NetPuppets.OnSnapshotEntry:206`)

```csharp
if (!byId.TryGetValue(netId, out PuppetInfo info))
{
    if (!IsRecentlyRemoved(netId)) { OnSpawn(netId, typeIdx, state, buf, extraOff, 0); }
    return false;                       // <-- caller counts snapUnk in BOTH branches
}
```

`NetSession.HandleWorldSnapshot:2071` increments one counter for all of:

* **rebuilt** — an id we never had; the self-heal *successfully constructed a puppet from the
  snapshot*. Ordinary and benign: the unreliable stream lane routinely outruns the ordered
  reliable lane, so a fresh spawn's first snapshot entry can beat its `EvSpawn`. In a swarm that
  spawns continuously this happens at roughly the spawn rate, forever, at steady state.
* **left dead** — an id removed *here* < 3s ago (`RecentRemovalWindowMs`): a claim or an
  `EvDeath` still settling. This is the one the docs' heuristic describes.
* **rebuild refused** — `OnSpawn` declining to register (unbuildable type). This one *does* climb
  per-entity per-snapshot-turn forever, and is the only genuinely pathological shape.

Crucially the docs tie `snapUnk` to `clTx` (client claims) alone. `MarkRemoved` fires on **every**
local puppet removal, including host-authoritative `EvDeath`s the client never claimed — so an
idle joiner watching a host AI clear a field produces "left dead" counts with `clTx` pinned at 0.
The heuristic is wrong as written, which is exactly why it could not be applied.

### `pupPops` scales with population, via the round-robin snapshot cursor

`SendWorldSnapshot` (`NetSession.cs:1055`) round-robins `SnapshotMaxEntries` = 16 entries per
`SnapshotIntervalMs` = 60ms packet. So an entity's **snapshot turn interval** is

    T = ceil(N / 16) * 60ms

with `N` = live replicated entity count. Between turns the puppet dead-reckons at the last
observed velocity; `ApplySnapshotState` hard-snaps (and counts `pupPops`) when the accumulated
error exceeds `SnapThresholdPx` = 100px.

`?flyspiders` is a pathological `N`: `FlyingSpiderEvent(.., 5.5f, isbackground:true)` spawns
continuously and the **background** variant has `Collides=false`, so nothing kills them — they
accumulate until they exit at `Position.X < -100`. Each one is replicated
(`FlyingSpiderDescriptor`, typeIdx 16) and moves on a *sinusoid*
(`Position.Y = startheight + 50*diffMod*scale*sin(2*pi*t/4000ms)`, `FlyingSpider.cs:224`) — the
worst possible shape for constant-velocity dead reckoning, and one whose extrapolation error grows
without bound in `T`.

**The sim then refuted this as the explanation** (`--population`, added by this card). Read the
data, not the arithmetic: on a healthy client the swarm logs **0 pops/s at every N from 16 to
2048** (bar one resonance cell at N=512 on Very_Hard). And a live boot of the rig measures its
world at only **17–19 live entities, `snapTurn=120ms`** — they spawn at 5.5/s but die off-screen,
so the swarm never accumulates in the first place. The swivel is only ±25px, so a
straight-line prediction almost never gets 100px wrong however long `T` grows, and the X drift is
exactly linear. What *does* produce hundreds of pops is **client tick starvation**:
`NetPuppetDriver` clamps its dt to 200ms, so a client ticking slower than 5Hz silently loses
`gap - 200ms` of real motion every tick. At N=128 the sweep logs 0 pops/s at 60/40/30/10/5 Hz and
**128 pops/s at 1Hz** — which is an *occluded* window (rAF paused, timers ~1Hz: JIP trap 1), not
the ~40fps the card recorded.

So **neither of the card's two hypotheses survives**: not the swarm, and not a steady 40fps. The
remaining candidates are intermittent occlusion during the pass, id churn, or a genuine fault —
and the honest answer is that the counters as they stood could not separate them. That is what
the split and `snapTurn` are for.

## Design

### 1. Split the counter so the next JIP pass is decidable (`NetMetrics`, `NetPuppets`, `NetSession`)

`NetPuppets.OnSnapshotEntry` gains an `out SnapUnknownKind kind` (`None` / `Rebuilt` / `LeftDead` /
`RebuildRefused`) reported at the exact branch it took. `HandleWorldSnapshot` keeps
`SnapUnknownIds` as the total (so the existing field and every doc reference stay meaningful) and
adds `SnapRebuilt` / `SnapLeftDead` / `SnapRefused`. The `[net]` line gains
`snapNew=`/`snapDead=`/`snapBad=` next to `snapUnk=`.

`snapBad` is the only one that means trouble on its own; `snapNew` should track the spawn rate and
`snapDead` the *total* removal rate (claims **and** `EvDeath`s).

### 2. Print the mechanism behind `pupPops` (`NetMetrics`)

Add `snapTurn=<ms>` — the round-robin turn interval derived from the live count the peer already
reports (`ceil(live/16)*60`). One number that turns "207 pops, is that bad?" into "each puppet only
hears from the host every 1.2s". Host and client both print it (host from `NetIdRegistry.LiveCount`,
client from `NetPuppets.LiveCount`); it needs no new plumbing, the count is already passed to
`NetMetrics.Report`.

### 3. Players column (`SubMenuOnlineGames.cs:201`)

`"/2"` → `"/" + Oracle.MaxPlayers`. Also make `NetGameBrowser.InjectFakeGames` vary its players
1..3 instead of all-1, so `?gamebrowser` — the screenshot flag that exists *to verify this
carousel* — actually exercises the column it is verifying.

### 4. Docs (`web/EvilAliensWeb/CLAUDE.md`)

* Correct the `snapUnk` heuristic bullet: name the three causes, tie "left dead" to **all**
  removals rather than `clTx`, and point at the new split counters.
* Add the population/`snapTurn` mechanism to the puppet bullet, so a swarm's `pupPops` is read as
  a population artifact and not a link fault.
* Note under the JIP-pass bullets that `?flyspiders` — the recommended never-ending host fight —
  is exactly the population that inflates `pupPops`, and that a fidelity verdict wants a bounded
  fight.

## Verification

No unit tests in this repo; per root CLAUDE.md, behaviour claims are proved with an isolation sim
or a headless self-test, never a live screenshot.

1. **`eaNetSnap()`** — new headless self-test (`NetPuppets.SnapshotAttributionSelfTest`, the
   `eaKickTest`/`eaNetScore.test` idiom). Drives the *real* `OnSnapshotEntry` over a scripted id
   stream and asserts each branch attributes to the right counter: a never-seen id → `Rebuilt` +
   a live puppet; a locally-removed id inside the window → `LeftDead` + still dead; a known id →
   `None`; an unbuildable typeIdx → `RebuildRefused`. Positive control included. Skips itself over
   a live session/GameScene like the other net self-tests.
2. **`tools/sim/net_puppet_drive_sim.py --population`** — extend the existing sim (which already
   models the 60ms/16-entry round robin and the real 100px/150ms correction math) with a sweep of
   pops/s vs `N` using the real background-`FlyingSpider` motion profile. Proves claim §2 as data
   and prints the `N` at which the swarm crosses into steady popping.
3. **`?gamebrowser`** in real Chrome — screenshot showing `Players: 2/4` etc. on the varied fake
   entries.
4. Clean `dotnet build -c Debug` + zero console exceptions on a normal boot and on `?gamebrowser`.

## Out of scope

* Re-running the two-window JIP pass. It needs the tiled-topmost two-OS-window rig (JIP traps 1-5)
  and, as this card shows, would produce another undecidable number until the counters are split.
  A follow-up card carries the re-measure now that the tooling can answer it.
* Changing the snapshot cadence / making the round robin population-aware. Real behaviour change
  on the wire, overlaps cards in flight (`25ad0659`, `1ec29347`); a follow-up card.
* The joiner's inability to pass `?aiplayer` — that is card `af63f958`, already in progress.
* Mid-boss puppet fidelity (`1ec29347`).
