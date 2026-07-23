# Fake lag + packet loss dev knobs (card 40334a8f)

## Context

The net layer (cards 11.1–11.3) is built on an explicit two-lane contract
(`Compat/Net/INetTransport`): the STREAM lane is unreliable-class — consumers *must* tolerate
drops and reorder — and the RELIABLE lane is ordered + guaranteed. But the only transport that
exists today is `BroadcastChannelTransport`, and its own header admits the problem:

> BroadcastChannel is inherently reliable + ordered on one machine … the interface contract only
> promises LESS for the stream lane, never more.

So every drop-tolerance path written in 11.1–11.3 — the `ShipStateBuffer` interpolation window,
extrapolation on underrun, the `snapUnk` self-heal that rebuilds a puppet from a snapshot, the
generous claim ledgers, the 3s peer timeout — has **never actually been exercised**. They are
dead code until a packet goes missing. This card makes packets go missing on demand.

It also de-risks 11.4: when WebRTC lands, "it broke over the real network" needs to be
reproducible on one machine, and a real bad link can't be summoned to order.

## Design

### Where it sits

A `NetImpairment` decorator implementing `INetTransport`, wrapping the real transport:

```
NetSession.transport  ->  NetImpairment  ->  BroadcastChannelTransport   (today)
                                          -> WebRtcTransport             (after 11.4, unchanged)
```

Because it sits *behind the interface*, it impairs both transports identically and needs zero
knowledge of either. `NetSession.Start` changes from constructing the transport directly to
wrapping it.

**Impairment is rx-only.** Delaying/dropping our own inbound is equivalent to the peer's outbound
being bad, and it keeps the whole thing on one side of the wire with no protocol change. Each peer
sets its own knobs, so an asymmetric link (good host → bad client) is just two different tabs.

### The wrapper

- Subscribes to `inner.OnData`; instead of re-raising immediately, stamps each packet with a
  release time and parks it in a lane queue.
- **`Pump(long nowMs)`** drains everything due and re-raises `OnData` in release order *within each
  lane* (the reliable lane is drained first, so with equal lag a reliable event can land ahead of a
  stream packet that arrived before it — real transports don't order across channels either).
  Ties inside the stream lane break on arrival order. Called from
  the top of `NetSession.Update`, **before `DrainRx()`**, off the same `Environment.TickCount64`
  real-time clock the rest of the session cadence uses (so turbo / slow-mo / hit-stop never skew
  impairment, matching `StreamIntervalMs` and friends).
- **Pass-through fast path:** `lag == 0 && loss == 0` re-raises inline with no queue, no
  allocation, one bool test. So an unimpaired net session is behaviourally what it is today.
- `Open`/`SendStream`/`SendReliable`/`Close` forward verbatim — tx is untouched.
- `OnPeerBye` forwards **immediately, never delayed**. It is a lifecycle signal, not traffic;
  delaying a `pagehide` bye behind a 500ms queue would just muddy the disconnect path 11.5 owns.

### Per-lane policy

| Lane | Delay | Loss | Order |
|---|---|---|---|
| Stream (`reliable == false`) | yes | yes | FIFO (see jitter below) |
| Reliable (`reliable == true`) | yes | **never** | **strictly preserved** |

The reliable lane models a *slow* link, not a broken one — dropping or reordering it would violate
the `INetTransport` contract itself and would only ever produce fake bugs. Everything above the
interface is entitled to assume that lane is sound.

Loss is drawn from a **private `Random`** owned by the wrapper — never the shared game RNG, per
the house rule that anything stochastic and cosmetic keeps its own generator so it can't desync a
co-op session (same reason `Quad` and `ShipConnector` hold private FX RNGs).

### Jitter (panel-only)

Constant delay preserves stream order perfectly, which means **reorder tolerance still never gets
tested** and `ordViol` / `sgap` stay cosmetic counters. Jitter randomises each stream packet's
release by ±jitter, and is the only way to exercise it.

**Panel/console only — deliberately NOT a URL flag** (user call): it lives in the `eaNetSim`
panel next to lag and loss so all three impairment knobs are in one place, rather than two of them
being bootable and the third not.

Two consequences that fall out of that:

- The queue that jitter feeds cannot be drained head-first. A FIFO that only releases its head
  would make a late-stamped packet block a later packet that came due earlier — silently
  converting jitter back into pure delay, i.e. the exact thing it exists to defeat. The stream lane
  is therefore a **list scanned for all due packets, released in release-time order**; the reorder
  is the point.
- The reliable lane's release times are clamped monotone (`release = max(release,
  lastReliableRelease)`) so jitter can never reorder that lane, and it stays a plain FIFO queue.

The panel's bake-ready readout prints only `?netlag=`/`?netloss=`, since jitter has no flag to bake
to; it is labelled as session-only in the panel.

### Knobs

URL flags, parsed in `DebugFlags.Parse` next to the existing `?net*` cases:

- `?netlag=<ms>` — 0–500, clamped
- `?netloss=<0-100>` — percent, clamped

Jitter has no URL flag; it is set from the panel / `eaNetSim(lag, loss, jitter)` only.

Live panel `eaNetSim`, built in `index.html` **outside `#app`**, constructed only on a `?net` boot
(house pattern; a normal boot gains no DOM). Sliders + an orange bake-ready readout printing the
equivalent query string, exactly like `eaWalls` / `eaWcTune`. `autocomplete='off'` on every range
input — the documented Chrome form-restoration gotcha.

Console entry point `eaNetSim(lag, loss)` for scripted runs, routed the standard way:
`eaNetSim` → `DebugInput.SetNetSim` (`[JSInvokable("debugSetNetSim")]`) → `DebugFlags.SetNetSimOverride`
→ read by the wrapper each `Pump`.

### Metrics

Four counters on `NetMetrics`, appended to the `[net]` line (append-only, like every other field):

- `impDrop` — stream packets dropped by the wrapper
- `impHeld` — packets currently parked in the delay queues
- `impLag` / `impLoss` — the settings in force, so a captured log line is self-describing

Without these, a `[net]` line from an impaired run is indistinguishable from a genuinely broken
one — which would make the logs actively misleading six months from now.

### The 100%-loss case is a feature

`?netloss=100` starves the stream lane, so `lastRxStreamAt` goes stale and the existing 3s
`PeerTimeoutMs` fires — a simulated silent disconnect, with the handshake still alive on the
reliable lane. That is the peer-lost failsafe path 11.5 has to harden, on demand, without pulling
a network cable. Worth calling out because it *looks* like a bug the first time you see it.

## Verification

Per the repo rule (behaviour/timing over time → isolation sim, read the DATA):

1. **Primary — in-runtime self-test.** `eaNetSim.test(lag, loss, n)` pushes N synthetic packets
   through the real C# wrapper and prints measured mean delay, delay spread, drop rate, and an
   ordering check per lane. Verifies the shipped implementation in the real WASM runtime as
   numbers, not a python mirror that can drift out of sync with the C# (the failure mode of a
   `tools/sim/` re-implementation here).
   Expected: mean delay ≈ lag ± one tick, stream drop rate ≈ loss within sampling error, reliable
   lane drop 0 and reorder 0 at every setting including `loss=100`.
2. **Delay granularity is one tick (~16ms)** — `Pump` runs on the game tick, so `?netlag=5` is
   indistinguishable from `?netlag=0`. Documented, not fixed; sub-frame precision would need a JS
   timer and buys nothing.
3. **Integration — two-tab run** (house recipe: two Chrome *windows*, both visible, fresh
   `?room=`): `?level=Level1&net=host&aiplayer&invuln&room=<r>` + the `net=join` twin with
   `&netlag=150&netloss=10`. Read both consoles. Expected: client `extrap` and `sgap` climb,
   `pupPops` rises above its normal ~0, `buf` stays near 100ms, `impDrop` climbs — and all of it
   returns to healthy when the knobs go to 0/0 live via the panel, with no reload.
4. Reliable-lane integrity under `?netloss=100`: `dup` and `ordViol` must stay 0 while the stream
   lane is fully starved.
5. Clean Debug build + zero console exceptions in real Chrome on port 5283.

## Out of scope

- **Tx-side impairment** — rx-only is equivalent and half the surface.
- **Bandwidth caps / MTU fragmentation** — a different failure class; WebRTC owns it after 11.4.
- **Duplicate injection** — the dedup paths are already exercised by the generous claim ledgers.
- **Impairing `OnPeerBye`** — lifecycle, not traffic (above).
- **Anything in 11.5's disconnect/reconnect UX** — this card only provides the tool that makes
  that card testable.

## Risk

`wt1` is mid-flight on 11.4 with uncommitted edits to `NetSession.cs`, `NetProtocol.cs` and a new
`WebRtcTransport.cs`. The overlap here is small and mechanical — one construction site in
`NetSession.Start` and one `Pump` call in `NetSession.Update` — but the Phase 6 merge will
conflict on `NetSession.Start` where 11.4 chooses between BroadcastChannel and WebRTC. Resolution
per the runbook: take 11.4's transport-selection logic, wrap its result.
