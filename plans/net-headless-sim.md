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

> Status: **design only** (this doc). Implementation is its own card; it needs a dotnet build
> environment (the headless sim is a console app CI can build *and run*, but the authoring loop
> needs local `dotnet`). Sized below.

## Why it's blocked — two layers, not one

The card framed the blocker as "static singletons wired to `ServiceHelper` globals." That's the
*first* layer. Surveying the code surfaced a *second*, deeper one.

**Layer 1 — static singletons.** `NetSession`, `NetPuppets`, `NetIdRegistry` are `static`
classes holding all per-session state in static fields and reaching services through
`ServiceHelper.Get<IOracleService>().Oracle` (and `IComponentBinService` / `IScoreService` /
`ISoundManagerService`). `ServiceHelper` is a process-global registry, so two peers can't hold
distinct Oracles/Bins/Scores at once. The 55 external call sites (across `GameScene`,
`PlayerShip`, `KillableAlien`, `ComponentBin`, `MenuScene`, `TeamChallenge`, `Background`,
`SoundManager`, `MessageEvent`, `UnlockEvent`, `ControlDevice`, `ShipConnector`, `CollisionHandler`,
`Game1`) all go through the public static API, so a **static-facade-over-instance-core** move
keeps every one of them unchanged.

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
layer reads/writes on a replicable component:

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
- **Run** (the point): the sim is a plain `net8.0` console app referencing a **KNI-graphics-free**
  core assembly (depends only on `nkast.Xna.Framework` core math + the `INetHost` seam — NOT
  `.Game`/`.Graphics`/`Blazor.GL`). It runs under `dotnet run` / `dotnet test` in CI with no browser.
  Add a CI job (`.github/workflows/ci.yml`, on push + `workflow_dispatch`) that builds the WASM
  project and builds+runs the sim, failing the job on any scenario assertion. This is the first
  runtime CI the repo has; deploy stays manual and untouched.
- The core must not gain a `Game`/graphics dependency — enforce with an assembly-ref check in the
  sim's csproj (or a tiny reflection guard test) so a future edit can't quietly re-entangle it.

## Migration — byte-identical production, incremental

1. Land `InMemoryTransport` + protocol/seq round-trip tests (Layer-1 only; no game change).
2. Introduce `INetHost` + `ServiceHelperNetHost`; route the cores' service/entity/`Game` touches
   through it, still static, still one instance. Diff-review every mapped call is 1:1.
3. Move state into `*Core` instances behind the static facades; `NetContext.Current` is the single
   production instance. All 55 external call sites unchanged.
4. Add `FakeNetHost` + the scenario harness; wire CI.

Each step is separately shippable and separately verifiable (steps 1–3 by the existing two-tab
Chrome recipe once, step 4 by the sim itself). The production path never stops going through
`ServiceHelper` — `ServiceHelperNetHost` *is* those calls.

## Risks / sizing

- **Regression risk to shipped co-op** (steps 2–3 touch hot paths). Mitigation: the facade keeps
  call sites identical; the byte-identical adapter; a two-tab smoke after each step.
- **`INetEntity` on the snapshot hot path.** Interface dispatch per puppet per snapshot — measure;
  if it bites, keep the production path on the concrete type via a generic core specialized to
  `AlienDrawableGameComponent`, and only the sim pays the interface. (Design allows either.)
- **Descriptor fidelity in the sim** — the generic fake descriptor tests ledger/ordering, not
  per-type puppet looks; per-type extras get fakes only as scenarios demand. Full descriptor
  fidelity is explicitly out of scope (that's the harness/browser's job).
- **Size:** ~medium-large. Step 1 small; step 2 the bulk (the seam + adapters); step 3 mechanical;
  step 4 moderate. A real card, not a 11.x tail — which is why 11.3 deferred it.
