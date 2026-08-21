# 3 & 4 player online co-op — feasibility answer + N-peer design

Card `2e0f908b`: *"Can the networking be extended to allow up to 4 players to play together
online?"*

> **STATUS 2026-08-21 — the epic is ACTIVE (11.7 and 11.8 SHIPPED; next up Stage 11.9, card `87242257`), and this doc was written 2026-07-24 against a codebase that has since moved. The architecture below STANDS (star topology, `PeerChannel`, unified ship paths, negotiated signaling capacity, the listed-session match-end shape); the following specifics are corrected here rather than rewritten in place:**
>
> - **Stage 11.6 is RETIRED — its work shipped as card `25ad0659`.** The in-process wire exists (`NetWire`/`InMemoryTransport`, N ≤ 8 endpoints, `NetWireTest` runs an N=4 leg), the scenario harness exists (ONE real session + scripted wire peers — that card measured that two independent WORLDS in one process are impossible, and unnecessary), and the de-static `NetContext` was measured and DECLINED as optional. The N-peer regression net is therefore "extend the scenario-harness pattern + the multi-process eahl rigs", not a de-static refactor. The epic starts at 11.7.
> - **Protocol is v22 now, not v5** — §B's "bump 5 → 6" reads as "bump whatever is current". The per-version changelog lives at `NetSession.ProtocolVersion`.
> - **`NetSession` is 4,472 + 388 lines (`NetSession.Friends.cs`) with ~20 peer-scoped singletons**, not 2.6k/16 — the JIP/listing/game-browser/host-menu/metrics machinery (cards `2001fbd8`, `0b8a300b`, `0d6ffe70`, …) all post-dates this doc and is enumerated in `Compat/Net/CLAUDE.md`. Two additions to §A's singleton list that matter: `CaptureBaseState` read-and-clears the per-entity teleport latch, so a snapshot turn must encode ONCE and send the same bytes to every client (never re-capture per recipient); and `ReplayLive()`/`NetReplayCatchUp()` are unaddressed broadcasts, so a late joiner's catch-up needs the ADDRESSED sends or it re-blasts every already-caught-up peer.
> - **TURN is decided: STUN-only stays** (owner decision, 2026-08-21). §"Topology"'s go/no-go is resolved as a deferred follow-up card, gated on real-world lobby-formation failure reports — 11.11 loses that item.
> - **A two-process eahl rig exists** (`tools/headless/LocalSocketNet.cs` + `tools/sim/net_jip_sync.py`) and is the thing the multi-process N-peer rigs extend; `?netpeers=` still does not exist and 11.7 deliberately adds no flag (a >2 capacity on a live session is knowingly broken until 11.9 — the transport-layer N rig is console-driven `eaRtc` on plain boots).

## Answer, in one line

**Four players online already ships today** — as **two consoles with a couch partner each**.
What does *not* exist is **four separate machines**: the session layer is hard-wired to exactly
one remote peer. Extending it is a contained, well-shaped refactor (the ship-replication layer is
already N-wide), and the topology choice is forced by the no-TURN reality.

## Evidence that 4 players already work (measured, not cited)

Two peers on today's `main`, each with one synthetic couch join
(`?level=Level2&net=host|join&aiplayer&invuln&netlocal=1&room=4p1`), print mirror-image
four-seat rosters with four live ships:

```
host  roster=0:Keyboard*,1:Remote,2:Generic*,3:RemoteFriend  pri=0/1
      ships=0:Keyboard,2:Generic,1:Remote,3:RemoteFriend
join  roster=0:Remote,1:Keyboard*,2:RemoteFriend,3:Generic*  pri=1/0
      ships=1:Keyboard,0:Remote,2:RemoteFriend,3:Generic
```

(`*` = locally owned.) All four seats are filled, all four ships are simulated and replicated, and
each console's view is the exact mirror of the other's. That is 4-player online co-op — card
`4d904410` delivered it as a side effect of "local co-op AND online co-op at once".

The real-world requirement is the catch: **players 3 and 4 must be sitting next to players 1 and
2** with a gamepad each (a couch join is a pad Start press). Four people in four houses cannot
play together.

> Caveat on the run above: both tabs were **hidden** (background tabs of a non-foreground window),
> so its `pops`/`pupPops` numbers are not a health verdict. The claim being made is the
> roster/ship *structure*, which is not timing-derived — a stalled peer's seats are held, not
> freed. See the structural-check note in `web/EvilAliensWeb/Compat/Net/CLAUDE.md` for why that survives
> hidden-tab throttling and what it does not license.

## Already N-wide — no work needed

The player dimension is done. Card `4d904410` generalised it and nothing is 2-shaped:

| Thing | State |
|---|---|
| `Oracle.MaxPlayers` | 4 |
| `ScoreVisualiser.SlotCount` | 4 |
| Slot allocation | host-allocated, **identity-mapped** on both peers, sparse rosters legal |
| `MsgFriendState` | bidirectional, **slot-keyed**, one jitter buffer + interp clock *per slot* |
| `EvScoreSync` | widened from 2 slots to 4 (`NetProtocol.MaxSlots`) |
| `EvBlast` | carries a slot byte |
| Couch join on a client | `EvJoinRequest` → host `EvSlotGrant(slot)` |
| Claim ledgers | per-`(netId, slot)`, `PaidMask` is a byte = 8 slots |
| Listing eligibility | already `Players < Oracle.MaxPlayers`, players column varies 1..3 |

`FriendChannel` (buffer + render clock + fire state + puppet, keyed by slot) is **already the
per-remote-ship abstraction an N-peer session needs**. It exists, it ships, it is exercised.

## Actually 2-wide — the work

Five layers, outermost first:

1. **`wwwroot/webrtc.js`** — module singletons `pc`, `chS`, `chR`. One `RTCPeerConnection`, two
   DataChannels, period.
2. **`WebRtcTransport`** — `Forward` hard-codes `senderId` to the literal `"peer"`; `SendStream` /
   `SendReliable` have no destination. (`INetTransport.OnData` *already* carries a `senderId`
   parameter — the seam was anticipated; `NetSession` currently discards it.)
3. **`server/signal/main.py`** — a room is `host` + a single `joiner`; a third peer is answered
   `{"t":"error","reason":"full"}`.
4. **`NetSession`** (~2.6k lines across `NetSession.cs` + `NetSession.Friends.cs`, `static`) —
   16 singleton members scoped to *the* peer: `buffer`, `puppet`, `renderMs`, `remoteAlive`,
   `remoteShotsPerSec`, `remoteBulletLife`, `lastRxSeq`, `haveRxSeq`, `lastRxEventSeq`,
   `lastRxStreamAt`, `peerPrimarySlot`, `peerStalled`, `PeerUp`, `RemotePaused`, `peerByeQueued`,
   `lastPuppetPos` — plus one hello loop, one timeout/stall verdict, and one `PeerLost` →
   match-end path.
5. **`NetLobby` / `NetGameBrowser`** — one code, one joiner, "connected" is a boolean.

Layers 1–3 are small and mechanical. Layer 4 is the real work. Layer 5 is UX.

## Topology: star (host relay), not mesh — and the reason is NAT, not taste

There is **no TURN server** (11.4 shipped STUN-only; ~10–15 % of NAT pairs fail outright). The
number of pairs that must *all* succeed grows quadratically with a mesh and linearly with a star:

| Topology | Connections for 4 peers | All-connect @ 85 %/pair | @ 90 %/pair |
|---|---|---|---|
| Full mesh | 6 | **≈ 38 %** | ≈ 53 % |
| Star (host hub) | 3 | **≈ 61 %** | ≈ 73 % |

> These figures assume pairs fail **independently**, which they do not — STUN-only failure is
> mostly a property of one peer's NAT (symmetric NAT fails *all* of that peer's pairs at once).
> So treat the table as an illustrative bound: mesh is somewhat less bad than 38 % suggests, and
> the star concentrates the risk on one machine — **a symmetric-NAT host takes the whole lobby
> down**, where in a mesh the other players could still see each other.

At the pessimistic end a 4-player mesh fails to form more often than it forms (38 %), and the star
lifts that by roughly half again (38 → 61 %, 53 → 73 %). The star also matches the authority model
already in place (the host is *already* authoritative for the world, score, lives, resets and
level script), so the hub is a role that exists rather than one being invented.

**Cost of the star:** client→client ship state is relayed through the host, adding ~½(RTT_A+RTT_B)
to the *other clients'* puppets. Remote ships already render `InterpDelayMs` (100 ms) behind, so
relayed channels need a larger per-channel delay (~150 ms, or derived from observed arrival
jitter) or they will extrapolate constantly.

**Host bandwidth at N=4** (ship state 31 B @ 30 Hz ≈ 0.93 KB/s; world snapshot ~500 B @ ~16.7 Hz
≈ 8.3 KB/s): per client ≈ 8.3 + 3 × 0.93 ≈ 11 KB/s, × 3 clients ≈ **33 KB/s up**. That is
**payload only** — at these packet sizes SCTP/DTLS/UDP/IP headers add roughly 2–3×, so budget
~70–100 KB/s. Still comfortable on any home connection; worth an eye, not a redesign.

TURN becomes materially more attractive at N > 2 (it converts the compounding failure, and the
host single-point-of-failure above, into a per-pair fallback) — the standing TURN go/no-go should
be re-decided as part of this, not after.

## Design

**A. `PeerChannel` — the layer-4 refactor.** *(SHIPPED -- card `b2828be8`, Stage 11.8, protocol
v23: `PeerChannel` + `ShipChannel` in `Compat/Net/PeerChannel.cs`, keyed by the 11.7 senderIds,
static facade unchanged; details in `Compat/Net/CLAUDE.md`.)* Lift the 16 singleton members into a
`PeerChannel` keyed by peer id, held in a `Dictionary<string, PeerChannel>` the way
`friendChannels` already is. Per peer: handshake state (hello/welcome, build hash, flags),
liveness (`lastRxStreamAt`, stall, timeout), `peerPrimarySlot`, pause flag, event seq. The static
public API (~60 external call sites across `GameScene`, `PlayerShip`, `KillableAlien`,
`ComponentBin`, `MenuScene`, …) stays untouched — a static facade over a per-peer core.

> Related but **not the same decomposition** as `plans/net-headless-sim.md`: that one splits per
> *session* (`NetContext` bundling core objects, so two whole peers coexist in one process); this
> one splits per *peer* inside a single session. They share only the static-facade-over-instance
> shape — and the de-static half lands first (card 11.6 below), which is what makes (A) tractable.

**B. Converge the two ship paths.** *(SHIPPED -- same card: one slot-keyed `MsgShipState` with a
PRIMARY flag, `MsgFriendState` retired, one receive/drive path; the a5b1e941 combo-timer byte rode
the same bump.)* Before it, `MsgShipState` meant "the sender's primary" (identity
implicit — there is only one sender) and `MsgFriendState` is the slot-tagged general case. With
N peers "implicit sender" stops being meaningful, so fold the primary into the slot-keyed form:
**every ship on the wire carries its slot**, and one code path drives every remote ship. The
friend path was deliberately isolated from the primary path so it could not regress it; this is
the moment to unify them, and it wants the headless sim as its safety net.

**This is a wire-format change and must bump `ProtocolVersion` (5 → 6)**, per the standing rule in
`NetProtocol`. Three consequences to carry: the handshake hard-rejects a version mismatch, so
mid-upgrade peers cannot pair; `ProtocolVersion` is *also* the public game-browser compatibility
filter (`NetListing` / `NetGameBrowser`), so a bumped build stops seeing already-deployed games;
and the site deploys manually, so that split is a deliberate act, not an accident.

**C. Transport addressing.** *(SHIPPED -- card `583a3ef8`, Stage 11.7.)* `INetTransport` grows `SendStreamTo(peerId, …)` /
`SendReliableTo(peerId, …)` (existing broadcast methods become fan-out over the peer set), and
`OnData`'s `senderId` starts carrying a real id. `webrtc.js` holds a map of peer id →
`{pc, chS, chR}`. `BroadcastChannelTransport` gets the same treatment — the loopback rig must
support 3–4 tabs, since that is where this gets developed. *(As shipped: senderIds are `"1".."3"`
host-side / `"h"` joiner-side on WebRTC, the per-tab random id on BroadcastChannel; `OnPeerBye`
carries the departing peer's id except WebRtcTransport's terminal whole-link failure, which keeps
its legacy "phase:reason" string -- a bye router must treat an unrecognized string as "every
peer". `tools/headless/LocalSocketNet` got the same treatment behind `--net-peers <1..3>`,
default 1. Details: `Compat/Net/CLAUDE.md` → "Transport & artificial impairment".)*

**D. Signaling.** *(SHIPPED -- card `583a3ef8`, Stage 11.7; the deployed server still needs the
manual `server/signal/README.md` update flow.)* A room becomes host + up to 3 joiners: N−1 offer/answer exchanges, each joiner
signalling only with the host (star). The room is full at 4. `test_signal.py` extends to cover a
3rd/4th join, a mid-session leave, and the full-room refusal. *(As shipped: optional `max` in
`{t:host}` clamped 2..4 default 2, monotone never-reused joiner ids, `{t:peer,id}` to the host,
`from`/`to` tagging only in max>2 rooms with max-2 relay kept byte-verbatim, `{t:gone,id}`
seat-free vs whole-room `gone`, capacity-aware `listable()`; 50 test cases, mutation-tested.)*

> **Capacity must be negotiated, not assumed.** The signal server is one shared deployment on the
> Hetzner box and the site deploys independently, so a server that accepts 4 members would let a
> third browser pair into a room whose occupants run a shipped 2-peer client. The server must keep
> refusing a 3rd member unless the host asks for the larger capacity (an explicit `max` in
> `{t:host}`, gated on the bumped protocol version).

**E. Liveness and match-end policy — a real decision, not a port.** Today, in a **menu session**,
any peer leaving ends the match for both (a `?net=` URL session survives peer loss, and a listed
session reverts the host to single-player). At N=4 the menu-session rule is hostile: one player's
wifi should not kill three other people's run. The `listedSession` semantic is already the right
shape and generalises:
- **host leaves → match ends** for everyone (no host migration).
- **client leaves → its slots free, play continues**, exactly as a listed host reverts today.

**F. World authority is unchanged.** Host→client snapshots/events simply fan out; claims arrive
from any client and the ledgers are already per-`(netId, slot)` with room for 8 slots.

**G. Lobby UX.** Host shows the room code plus a live roster of who has joined and starts when
ready (today "connected" is a boolean and launch is implicit). The browser's players column and
JIP already handle "any empty slot", so a 3rd/4th arrival mid-game largely falls out.

## Verification plan

- **Headless N-peer sim first.** `plans/net-headless-sim.md` already specs the two-peer,
  in-one-process sim and the de-static refactor it needs. Building it as an *N*-peer sim makes it
  the regression net for this whole epic, and it is the only sane way to assert on 4-peer event
  ordering, claim ledgers and reset/id-churn.
- **Loopback first, WebRTC second.** `BroadcastChannelTransport` with 3–4 tabs exercises every
  layer above the transport with no server and no NAT.
- Hidden tabs keep ticking (`index.html`'s `document.hidden → setTimeout(tickJS, 33)` fallback)
  and seats are held across a stall, so an N-tab **structural** roster check does not need N
  foreground windows — as used for the evidence above. Timing and feel numbers still require
  focused windows.
- `?netlocal=<1-3>` already covers couch seats on any peer; a new `?netpeers=<n>` seeds the rig.
- Existing gates unchanged: `[net]` metrics lines, `eaNetSim.test(...)`, `server/signal/test_signal.py`.

## Suggested card breakdown (sequential)

Numbered as a continuation of the Stage 11 net series (Stage 12 is the shipped font reskin —
do not reuse `12.x`).

| Card | Scope |
|---|---|
| ~~**11.6**~~ | RETIRED — shipped as card `25ad0659` (see the status banner) |
| **11.7** (`583a3ef8`) | Transport addressing + N-peer signaling room, capacity negotiated (webrtc.js, `INetTransport`, signal server, `LocalSocketNet` multi-client, 3-tab loopback rig). Behaviour-neutral for 2-peer; protocol version unchanged |
| ~~**11.8**~~ (`b2828be8`) | SHIPPED (protocol v22 → v23): `PeerChannel` + one slot-keyed ship path, behaviour-neutral at 2 peers; `net_jip_sync.py` stayed green |
| **11.9** (`87242257`) | N-peer session: per-peer hello/welcome + roster negotiation (grants serialized against races), per-peer liveness/drop ladder, pause as a set, host relay of client ship/HUD state (symmetric events re-emitted under the host's own event seq), ADDRESSED catch-up, the new match-end policy, per-peer `[net]` metrics |
| **11.10** (`0257f8ba`) | Lobby + game-browser UX for 3–4 (host roster, start-when-ready, capacity-aware listing — the `!NetSession.Active` term goes, JIP into slots 3/4, per-peer kick target) |
| **11.11** (`6fb406bc`) | Hardening: relayed-channel interp delay, bandwidth soak at N=4, multi-process eahl rigs, `bufferedAmount` back-pressure. TURN stays deferred (owner decision — see banner) |

## Out of scope

- **Host migration** — deferred as a stretch goal in stage 11; here the host leaving ends the match.
- **TeamChallenge above 2 players** — the tether is inherently pairwise (`ShipConnector`, the
  soft `NetPullOwnShip` model). A 3–4 player tether is a *game design* question, not a netcode
  one; TeamChallenge stays 2-player.
- **WebcamAliens** — already excluded from net sessions (the camera is the controller).
- Anything that would weaken the standing invariants: no `?net` flag ⇒ no net layer is constructed
  *until a stranger joins a publicly listed game* (`NetListing` → `StartListedSession`, card
  2001fbd8), and single-player contacts no server except that opt-out-able listing beacon.
