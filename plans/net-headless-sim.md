# Net: headless two-peer sim + de-static refactor (deferred from card 11.3, item 2)

The 11.2 retro ask, deferred through 11.3/11.4: run **two `NetSession` instances in one
process** over an in-memory transport, drive scripted scenarios, and assert on ledger / metric
state — no browser, no `Game`, no WASM. The prize is a fast, deterministic regression net for
the replication layer's trickiest logic (generous claims, id churn, reset/pause ordering) that
today can only be exercised by two hand-driven Chrome windows reading `[net]` log lines.

It is also the tool that would close out **item 1's residual**: the first-wipe `pupPops` burst
has a confirmed, now-fixed time-scaling half (`tools/sim/net_puppet_drive_sim.py`), but a full
200-500 burst additionally implicates the **reset / id-churn transition** (host purge +
checkpoint replay racing the client's own purge). That is a two-peer, event-ordering scenario —
exactly what this sim asserts on — and a single-puppet clock model can't reach it.

> Status: **in implementation** (card `25ad0659`: steps 1, 1b, 2a-2c, 3, 4). Step 1 shipped in
> PR #231 (`dce0772`) -- the in-process wire, its self-test, a `logic_probe` case set and two
> committed probes.
>
> **THIS DOC WAS WRITTEN 2026-07-24 AND TWO OF ITS PREMISES WERE WRONG.** Both are corrected in
> place below, but read this first, because each changed the plan materially:
>
> 1. **No graphics-free core ASSEMBLY is needed** (see "Verification"). `tools/sim/logic_probe`
>    already `AssemblyLoadContext`-loads the built `EvilAliensWeb.dll` into the desktop CLR and
>    calls real statics -- its ninth case set is already `NetProtocol`'s decoders -- and
>    `tools/headless` (`eahl`) source-links `Game/**` + `Compat/**` into a desktop exe. So the
>    harness lives IN the game assembly under `Compat/Net/` (the house `eaNetScore` / `eaSlotTest`
>    pattern) and ONE implementation gets three runners: browser console, `eahl` probe, and
>    `logic_probe` for a browserless exit code. Splitting the shipped csproj buys nothing and puts
>    the publish/trim path at risk. **Do not do it.**
> 2. **A large slice of the value needs NO de-static refactor at all.** `NetSession`'s session
>    entry points already take an arbitrary `INetTransport`, so ONE real live session plus a
>    scripted in-memory wire peer reaches most of the scenarios below -- including the ship-puppet
>    `TryAdd` one, which needs the REAL `ComponentBin` and a real `PlayerShip` and so could never
>    have been reached by the `FakeNetHost` design at all. The scenarios sat in step 4 because this
>    doc believed they needed the refactor; those that do not have moved to step 1b.

## Why it's blocked — two layers, not one

The card framed the blocker as "static singletons wired to `ServiceHelper` globals." That's the
*first* layer. Surveying the code surfaced a *second*, deeper one.

**Layer 1 — static singletons.** `NetSession`, `NetPuppets`, `NetIdRegistry` are `static`
classes holding all per-session state in static fields and reaching services through
`ServiceHelper.Get<IOracleService>().Oracle` (and `IComponentBinService` / `IScoreService` /
`ISoundManagerService`). `ServiceHelper` is a process-global registry, so two peers can't hold
distinct Oracles/Bins/Scores at once. Every external call site goes through the public static API,
so a **static-facade-over-instance-core** move keeps all of them unchanged.

**The seam surface, MEASURED on `main` @ `513f3fd` (2026-07-29).** This doc originally claimed "55
external call sites". That count predates cards `4d904410`, `0b8a300b`, `c0229c57`, `ee96ea61`,
`9a3175d0`, `1a3ad45a`, `74403f83` and `88f87ba2`, and it never counted the sibling test suites.
Re-derive rather than trusting either figure if much time has passed:

| Measure | Count |
|---|---|
| Reference tokens to `NetSession.` / `NetPuppets.` / `NetIdRegistry.`, all files | **227** over 209 lines in 24 files |
| ... SHIPPING code only (excluding the `*Test.cs` suites and `DebugInput`) | **92** in 18 files |
| ... in `Game/**` alone | **79** real code sites (95 incl. comments) |
| ... in `Compat/Net/Net{Slot,Kick,Snapshot,Combo}Test.cs` | **112** |
| Distinct external members the facades must forward | **70** |
| Static declarations across the three classes | **154**, of which **108 must move** |

The 112 test-suite sites are the cost this doc never counted: they reach `internal` members, so
they FOLLOW the state into the cores instead of being insulated by the facade. They are also what
makes the refactor defensible -- see the regression-net note under "Verification".

**Two sequencing findings from the same census:**

- **`NetIdRegistry` has ZERO external references outside its own file.** Much the cheapest of the
  three to convert, and a clean rehearsal of the facade pattern -- do it first in step 3.
- **`NetProtocol` needs NO change**: pure static, 44 `const`s, no mutable static state. Likewise
  `NetMetrics`, `NetBaseState`, `NetImpairment` and `ShipStateBuffer` (already instance or pure).

**Layer 2 — KNI `Game` / `AlienDrawableGameComponent` entanglement.** De-static-ing alone does
NOT make the code headless. `NetSession` / `NetPuppets` operate on real engine objects:

- `NetPuppetDriver : GameComponent` (needs a `Game`), added to `game.Components`.
- `NetSession` holds `Game game`; `HandleClaim` does `Explosion.NewExplosion(bin, game)`,
  `new Bullet(game)`, `killable.NetKill(...)`, `bin.Recycle<PlayerShip>()`, `new PlayerShip(game)`.
- `NetPuppets` stores `AlienDrawableGameComponent Comp`; reads `Position`, `rotation`, `scale`,
  `curframe`, `IsDead`, `NetPointValue`, `NetHitPoints`; calls `NetKill` / `NetAdvanceFrame` /
  `NetSetFrame` / `NetTickTimers`.
- `NetIdRegistry.Entry.Comp` is an `AlienDrawableGameComponent`; the descriptor table
  (`NetTypeRegistry` + `Descriptors/*`) *constructs real puppets* (`UFO`, `Asteroid`, …) via their
  `New*+Setup` factories.

`AlienDrawableGameComponent` is a `DrawableGameComponent`; constructing one needs a `Game`, and
KNI's only referenced platform here is `nkast.Kni.Platform.Blazor.GL` — **there is no headless
platform**, so a console process can't `new Game()` or build a real puppet. The pure-math bits
(`Vector2`, `MathHelper`, `GameTime`) run fine on console; the object graph does not.

So the sim needs the core abstracted **off `Game` and off the concrete entity type**, not just
off `ServiceHelper`.

## Target architecture

Three moves, each independently landable and byte-identical in production.

### 1. Instance core + static facade

Per singleton, split into an instance `*Core` and a thin `static` facade that forwards to the
one production instance:

```
NetSession        (static facade)  ->  NetSessionCore     (instance: all former static state)
NetPuppets        (static facade)  ->  NetPuppetsCore
NetIdRegistry     (static facade)  ->  NetIdRegistryCore
```

The three cores co-reference (puppets → session for claims, session → registry for the live set,
registry → session for spawn/death). Bundle them in a **`NetContext`** hub that owns one of each
plus the host seam (below) and the transport chain (`NetImpairment` → the real transport). The
facades read `NetContext.Current` (the game's single context, created in `Start`/`StartMenuSession`,
cleared in `Stop`). The sim constructs **two** `NetContext`s directly and never touches
`NetContext.Current`.

`NetProtocol`, `NetMetrics`, `NetBaseState`, `NetImpairment`, `ShipStateBuffer` are already pure /
instance-friendly — land these under the sim's reach first (they need no change) so protocol and
impairment round-trips can be tested before the bigger move.

### 2. The host seam (`INetHost`) — off `ServiceHelper`, off `Game`, off the concrete entity

One injected interface carries *everything* the cores reach outside their own state. Two impls:
`ServiceHelperNetHost` (production, byte-identical — literally today's `ServiceHelper.Get<>` /
`new Explosion` calls) and `FakeNetHost` (the sim, plain data objects).

The entity abstraction is the crux. Introduce **`INetEntity`** — the union of the members the net
layer reads/writes on a replicable component. **The sketch below is INCOMPLETE.** The measured set
is **18 members**; the ones this doc missed are `Enabled`, `NetSpeedVector`, `NetSpinPerMs`,
`NetCosmeticOnly`, `NetDriveExtras`, `NetSuppressAward` and `GetType()` (descriptor lookup).
`INetHost` is likewise **~45 members**, not the ~30 sketched under "The seam surface" (16
`oracle.*`, 5 `bin.*`, 8 `score.*`, 2 `sound.*`, 12 `DebugFlags.*`, 2 `WebRtcInterop.*`, `NowMs`,
plus entity creation), and `INetScene` is **14**. That is why step 2 is split three ways below.

**Implement `INetEntity` DIRECTLY on `AlienDrawableGameComponent`**, never via an adapter object:
the existing `Net*` accessors at the bottom of that class already ARE the implementation, and an
adapter would allocate per entity on a path that runs per puppet per tick.

```
Vector2 Position { get; set; }     float Rotation { get; set; }
float Scale { get; set; }          float CurFrame  { get; set; }   // NetSetFrame/NetAdvanceFrame
bool IsDead { get; }               float PointValue { get; }
int  NetHitPoints { get; }         void NetApplyHp(int hp);
void NetKill(INetKiller killer, bool comboGenerator);
void AdvanceFrame(float dtSeconds); void TickTimers(float dtMs);
```

Production wraps `AlienDrawableGameComponent` in an adapter that forwards to the existing `Net*`
accessors (already the seam — see `AlienDrawableGameComponent.cs` bottom, `UFO.cs`). The sim's
`FakeEntity` is a struct-ish class with fields + a scripted `IsDead`/`NetKill`. The host owns
entity lifecycle so the sim never constructs a `Game`:

```
INetEntity CreatePuppet(byte typeIdx, in NetBaseState s, ReadOnlySpan<byte> extra); // via descriptor
void AddPuppet(INetEntity e);  void Remove(INetEntity e);  void SpawnExplosion(Vector2 at, float scale);
INetKiller ScratchKiller(byte slot, Vector2 at);          // was NetPuppets.KillerAgent(new Bullet)
// score
void AddScore(float pts, Vector2 at, byte slot);  void AddLife();  sbyte Lives { get; set; }
float PointScore(byte slot);  void AdoptScore(byte slot, float v);
// oracle / ships (remote-ship puppet + hues)
IReadOnlyList<INetShip> Ships { get; }  bool DeviceIsPlaying(...);  int AddPlayer(...);  void SetHue(...);
// sound / scene beats
void PlayCue(string);  void ApplyMusic(int song);  INetScene Scene { get; }  // GameScene.NetActiveScene facade
```

`INetScene` fronts the `GameScene.NetActive*` reset/pause/victory/background/checkpoint hooks so
the sim can assert the *ordering* of state transitions without a real scene. Descriptors: the sim
registers a **single generic test descriptor** that round-trips `NetBaseState` into a `FakeEntity`
(the wire `typeIdx` table stays the production one for encode/decode, but puppet *construction* in
the sim is the fake — enough for ledger/protocol/ordering; per-type extras get their own tiny
fakes only where a scenario needs them).

### 3. Driver-as-`Tick`, and the in-memory transport

`NetPuppetDriver` shrinks to a thin `GameComponent` that computes the real-time dt (as it does
now after item 1) and calls `core.Tick(dtMs)`. The sim calls `core.Tick(dtMs)` directly on a
virtual clock — no `GameComponent`, no `Game`. Same for `NetSession.Update()` → `core.Update(nowMs)`
with an injected clock (replace the four `Environment.TickCount64` reads with `host.NowMs`).

**`InMemoryTransport : INetTransport`** — a paired in-process transport: two endpoints, each
`SendStream`/`SendReliable` enqueues onto the peer's inbound queue, delivered on the peer's next
`Update`. It already composes with `NetImpairment` (which decorates `INetTransport`), so the sim
gets loss/reorder/jitter for the stream lane for free, on the same virtual clock. Sketch:

```
sealed class InMemoryTransport : INetTransport {              // OnData is (payload, reliable, senderId:string)
    InMemoryTransport peer; string id;                        // paired by the factory
    public event Action<byte[],bool,string> OnData;  public event Action<string> OnPeerBye;
    public void Open(string room) {}  public void Close() => peer?.OnPeerBye?.Invoke(id);
    public void SendStream(byte[] d)   => peer.Deliver((byte[])d.Clone(), reliable:false); // copy: no aliasing
    public void SendReliable(byte[] d) => peer.Deliver((byte[])d.Clone(), reliable:true);
    void Deliver(byte[] d, bool reliable) => inbound.Enqueue((d, reliable));
    public void Pump() { while (inbound.Count>0){ var (d,r)=inbound.Dequeue(); OnData?.Invoke(d,r,id);} }
}
```

## The seam surface (exhaustive — from the grep)

Everything the cores touch outside their own state, and where it lands:

| Today | `INetHost` member | Notes |
|---|---|---|
| `ServiceHelper.Get<IOracleService>().Oracle` | `Ships`, `AddPlayer`, `DeviceIsPlaying`, `GetPlayerIndex`, `Players`, `SetHue`, `Hue` | remote-ship puppet + join hues |
| `…IComponentBinService…ComponentBin` | `AddPuppet`, `Remove`, `Recycle<PlayerShip>` | puppet + explosion lifecycle |
| `…IScoreService….Score` | `AddScore`×4, `AddLife`×2, `Lives`, `PointScore`, `AdoptScore` | generous credit + host sync |
| `…ISoundManagerService…` | `PlayCue`, `ApplyMusic` | remote ship death, EvMusic |
| `Explosion.NewExplosion` ×4, `new Bullet`, `new PlayerShip` | `SpawnExplosion`, `ScratchKiller`, spawn-remote-ship | entity creation |
| `is KillableAlien` / `.NetKill` ×2, `is Powerup` | `INetEntity.NetKill` / a `Kind` discriminant | claim honoring |
| `GameScene.NetActiveScene.Net*` | `INetScene` | reset/pause/victory/background/checkpoint/tether |
| `Environment.TickCount64` ×4 | `host.NowMs` | injected clock (virtual in sim) |
| `DebugFlags.*`, `WebRtcInterop.BuildHash()` | host consts | fixed in sim |

## Scenarios (assert on ledger + `NetMetrics`)

Each spins up two `NetContext`s (host + join) paired by an `InMemoryTransport`, pumps the virtual
clock, and asserts. All are today's generous-claim invariants made executable:

1. **Kill claim (happy path).** Client hit-tests a live puppet → `EvClaim`. Assert host
   `ClaimsHonored++`, `ClaimsHonoredLive`, entity removed, killer slot credited **once** on both
   sides (per-`(netId,slot)` ledger). `pupPops`/`SnapUnknownIds` unchanged.
2. **Double claim (both peers, same target, inside RTT).** Two `EvClaim`s for one `netId`,
   distinct slots. Assert **both** slots paid exactly once (`clPaid`/`ClaimsPaidDead` nonzero =
   the generous-pay proof), never double-credited, entity removed once.
3. **Late claim (host already reaped it).** Host death broadcast, then a client claim arrives.
   Assert the claimant is paid from `recentDeaths` once, and a *second* late claim for the same
   `(netId,slot)` is a no-op (`PaidMask`).
4. **OneUp overlap.** Two collectors grab a `OneUp` powerup inside the RTT window. Assert `AddLife`
   fires **once per distinct collector** (host-authoritative lives), and the next `EvScoreSync`
   doesn't revert it.
5. **Id churn (purge + replay).** Host removes N replicables (bulk `EvDeath`) and spawns M fresh
   ids (`EvSpawn`) in the same tick, with the stream lane (snapshots) reordered ahead of the
   reliable lane via `NetImpairment`. Assert the client self-heals (`SnapUnknownIds` bounded,
   `DupSpawns` bounded, no leaked puppets) — **and this is the item-1 residual probe**: watch
   `pupPops` across the churn.
6. **Reset / pause ordering.** Interleave `EvReset`(reset/respawn/gameover), `EvPause`(on/off from
   either peer), and `EvCheckpoint` and assert the `INetScene` sees the exact transition sequence,
   `RemotePaused` resolves only when both are clear, and a reset mid-pause doesn't strand the world
   frozen.

Bonus (cheap, high-value): **protocol round-trips** (every `Encode*`/`TryDecode*` is pure) and a
**seq/dedup** table (`SeqGaps`, `DupSpawns`, `StreamSeqGaps`) driven by the impairment wrapper —
these need only Layer-1 + `NetProtocol`, so they can land before the full seam.

## Verification

- **Compile** (both): CI `dotnet build web/EvilAliensWeb -c Debug` (the WASM project, unchanged
  behavior) + `dotnet build` the sim.
- **Run** (the point), CORRECTED -- no separate assembly, no `dotnet test`, and **no CI job**:
  - the harness lives in the game assembly under `Compat/Net/`, reached from `DebugInput` like
    every other suite here. Keep the parts that need no `Game` **Game-free and clock-free**: that
    is what lets `tools/sim/logic_probe` run them for real with no browser and no GL, and what
    keeps them non-flaky. `ProbeNetWire` is the pattern -- it INVOKES the suite rather than
    restating it, and guards a green run with a section-header check plus an assertion-count floor.
  - **A GitHub Actions workflow is OUT OF SCOPE -- a SETTLED USER DECISION, not deferred work.**
    The user pushes manually and does not want a push-triggered workflow (it would spend Actions
    minutes on every push and change the workflow for every future card). Do not file a card for it
    and do not add a workflow file. This doc's original "wire CI" clause is satisfied **in
    substance** by a runnable exit-code gate: `python tools/headless/probes/run_probes.py` and
    `dotnet run --project tools/sim/logic_probe -- web/EvilAliensWeb/bin/Debug/net8.0`.
- **The regression net for steps 2-3 is `tools/headless/probes/net_selftests.txt`** (landed in
  step 1, green before step 2 began). It runs all ten menu-runnable net self-tests as one exit
  code. A facade refactor does not fail by not compiling -- it fails by one of the net layer's
  decisions quietly changing -- and these suites already cover those decisions. Asserted as
  TALLIES with their counts, never `expect-not FAIL`: an absence assertion passes on a run where
  the `eval` never happened, and several suites SKIP legs they cannot reach.
  The argument for it in one fact: when it was written, `eaNetCombo.test()` had been failing on
  `main` and nobody knew.

## Migration — byte-identical production, incremental

1. **DONE (PR #231, `dce0772`).** `InMemoryTransport` + `NetWire` -- an **N-endpoint switch** with
   per-`(src,dst)` queues. The two-field sketch under "Driver-as-`Tick`" below IS the hardcoded 2
   the N-peer stages would have had to undo; do not reintroduce it. Plus `NetWireTest` (transport
   contract at N=2 and N=4, `NetImpairment` composed over a real endpoint, every codec
   round-tripped THROUGH the wire), `ProbeNetWire`, `net_wire.txt` and `net_selftests.txt`.
   **`NetWire.Pump()` captures EVERY endpoint's budget before draining any of them** -- capturing
   per endpoint as its turn came round still let a send to a HIGHER-indexed endpoint arrive in the
   same `Pump`, a same-tick round trip no real transport can do, which silently satisfied an
   ordering assertion. Only the upward direction discriminates, so test both.
2. **Step 1b -- the ship-puppet `TryAdd` scenario** (this card's second comment, from PR #160).
   Moved here from step 4: it needs the REAL `ComponentBin`, a real `PlayerShip` and a real
   standing `Purge<PlayerShip>`, so a `FakeNetHost` could never reach it -- yet one live session
   plus a scripted wire peer reaches it with no production change. Add one `internal StartForTest`
   forwarding to `StartWith(..., asMenuSession: false, asListedSession: false)`; **not**
   `StartMenuSession`, whose `menuSession` flag makes a `?level=` boot reject its own pairing on
   `DebugFlags.Active`. Assert the puppet is NOT adopted while the filter is live, IS adopted on
   the next tick once `TopOfTickFlush` has expired it, and that the oracle seat is REUSED via
   `DeviceIsPlaying` rather than re-allocated; same for a `RemoteFriend` via `SpawnFriend`.
   **Encode the NEGATIVE half too:** only `NetApplyReset`'s purge can reach this, because the
   `LoseLife` / `UpdateWin` / `UpdateResetting` purges run in `base.Update` and
   `collectionHelper.Update()` flushes them before the drain, leaving `FindLocalShip()` null and
   both callers' gates shut. The reachable ordering, traced: `Game1.UpdateInner` runs
   `TopOfTickFlush` -> `base.Update` -> `collectionHelper.Update` -> `DetectCollisions` ->
   `NetSession.Update` -> `DrainRx` (where `NetApplyReset` arms the purge) -> `ManagePuppet` ->
   `SpawnPuppet`; the local ship's purge death is still only QUEUED there, which is exactly why
   the caller's gate is open.
   **It runs on `Environment.TickCount64`, so prove it is not flaky before committing it as a
   probe** (~10 runs + a mutation test). If it is flaky, ship it console-only and re-home it as a
   probe once 2a has landed the injected clock.
3. **Steps 2a/2b/2c** -- `INetHost` + `ServiceHelperNetHost`, split three ways because the seam is
   45 + 18 + 14 members rather than the ~30 sketched: **2a** the injected clock (`NowMs`, replacing
   9 `Environment.TickCount64` reads) plus the `DebugFlags` / `WebRtcInterop` consts -- FIRST,
   because the clock buys determinism for everything downstream, 1b's probe included; **2b** the
   four `ServiceHelper` services; **2c** `INetEntity` + `INetScene` + entity creation. Still
   static, still one instance. Diff-review that every mapped call is 1:1.
   At 2c, **MEASURE before choosing the entity representation.** `FrameSection.UpdNet` already
   brackets `NetSession.Update()` in `Game1.UpdateInner`, so take the ABSOLUTE mean ms at a pinned
   replicable population (uncapped/headless, or focused -- a vsync-capped frame rate cannot see
   this) and judge the delta against the 16.7ms frame budget, plus whether p99 frame time moved.
   Never as a percentage of a small phase: 10% of 0.3ms is nothing and would trigger a fallback to
   real added complexity for no gain. The simple direct-interface design is the DEFAULT; the
   generic-core fallback has to earn it.
4. **Step 3** -- move state into `*Core` instances behind the static facades; `NetContext.Current`
   is the single production instance. Every external call site unchanged. **Start with
   `NetIdRegistry`**: zero external references, so it is the cheapest rehearsal of the pattern.
5. **Step 4** -- `FakeNetHost` + the N-peer scenario harness + the remaining scenarios below + the
   id-churn `pupPops` probe. **No CI job** (see "Verification").

Each step is separately shippable and separately verifiable. Steps 2 and 3 each get an OCCLUDED
two-window loopback run with `?fpsuncapped` on both peers, asserting the STRUCTURAL set (roster,
`pri=`, adopt, `resets`, mirror-image seat maps, `drop`/`sgap`/`ordViol`/`seqGap`): those stay
valid occluded, and this refactor changes no timing, so none of it needs the two genuinely visible
windows that would seize the user's screen. **The one real-WebRTC visible two-window smoke is the
USER'S, once, after step 3** -- write the procedure down for them (exact URLs and flags for both
windows, the local signaling steps, the key sequence, and the console lines and values that
constitute a pass) rather than running it, and rehearse it headlessly first so they are replaying
a known-good script. The production path never stops going through `ServiceHelper` --
`ServiceHelperNetHost` *is* those calls.

## Risks / sizing

- **Regression risk to shipped co-op** (steps 2–3 touch hot paths). Mitigation: the facade keeps
  call sites identical; the byte-identical adapter; a two-tab smoke after each step.
- **`INetEntity` on the snapshot hot path.** Interface dispatch per puppet per snapshot — measure;
  if it bites, keep the production path on the concrete type via a generic core specialized to
  `AlienDrawableGameComponent`, and only the sim pays the interface. (Design allows either.)
- **Descriptor fidelity in the sim** — the generic fake descriptor tests ledger/ordering, not
  per-type puppet looks; per-type extras get fakes only as scenarios demand. Full descriptor
  fidelity is explicitly out of scope (that's the harness/browser's job).
- **Size:** ~medium-large. Step 1 small (shipped); 1b moderate; step 2 the bulk (the seam +
  adapters, hence the 2a/2b/2c split); step 3 mechanical but WIDE (108 statics under 227 reference
  sites); step 4 moderate. A real card, not a 11.x tail — which is why 11.3 deferred it.
- **`verify_il_identical.py` will NOT hash identically for steps 2-3, and that is EXPECTED** -- a
  seam and an instance move are real refactors, not cosmetic ones. Do not iterate trying to force
  a green tick. The instrument is `verify_decompiled_diff.py --ref main` ("is the difference
  confined to the members I edited"), which is trustworthy for these files as of PR #230; before
  that it mis-attributed members to nested types, and `Compat/Net/` has nine of them
  (`NetIdRegistry.Entry`, `NetPuppets.PuppetInfo`, `NetSession.DeathRecord`,
  `NetSession.SlotAdopt`, `FriendChannel`, ...). Member NAMES in any pre-`bb41b78` verdict may be
  wrong; hunk counts never were. Note also that several `Compat/Net/` types LOOK nested because
  they sit at indent 4 in a file whose first type is the big one (`NetPuppetDriver`,
  `SnapUnknownKind`, `NetBaseState`, `NetBackgroundOp`, `NetCosmeticKind`, `ShipSample`,
  `INetTypeDescriptor`) but are namespace-level siblings.
