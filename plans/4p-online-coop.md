# 3 & 4 player online co-op — feasibility answer + N-peer design

Card `2e0f908b`: *"Can the networking be extended to allow up to 4 players to play together
online?"*

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

> Caveat on the run above: both tabs were unfocused, so its `pops`/`pupPops` numbers are not a
> health verdict (an unfocused tab throttles rAF — the HUD says so itself). The claim being made
> is the roster/ship *structure*, which is not timing-derived.

## Already N-wide — no work needed

The player dimension is done. Card `4d904410` generalised it and nothing is 2-shaped:

| Thing | State |
|---|---|
| `Oracle.MaxPlayers` | 4 |
| `ScoreVisualiser.SlotCount` | 4 |
| Slot allocation | host-allocated, **identity-mapped** on both peers, sparse rosters legal |
| `MsgFriendState` | bidirectional, **slot-keyed**, one jitter buffer + interp clock *per slot* |
| `EvScoreSync` | widened from 2 slots to 4 |
| `EvBlast` | carries a slot byte |
| Couch join on a client | `EvJoinRequest` → host `EvSlotGrant(slot)` |
| Claim ledgers | per-`(netId, slot)`, `PaidMask` is a byte = 8 slots |
| Listing eligibility | already "any empty slot", players column varies 1..3 |

`FriendChannel` (buffer + render clock + fire state + puppet, keyed by slot) is **already the
per-remote-ship abstraction an N-peer session needs**. It exists, it ships, it is exercised.

## Actually 2-wide — the work

Five layers, outermost first:

1. **`wwwroot/webrtc.js`** — module singletons `pc`, `chS`, `chR`. One `RTCPeerConnection`, two
   DataChannels, period.
2. **`WebRtcTransport`** — `Forward` hard-codes `senderId` to the literal `"peer"`; `SendStream` /
   `SendReliable` have no destination. (`INetTransport.OnData` *already* carries a `senderId`
   parameter — the seam was anticipated, just never fed.)
3. **`server/signal/main.py`** — a room is `host` + a single `joiner`; a third peer is answered
   `{"t":"error","reason":"full"}`.
4. **`NetSession`** (2266 lines, `static`) — ~15 singleton fields scoped to *the* peer: `buffer`,
   `puppet`, `renderMs`, `remoteAlive`, `remoteShotsPerSec`, `remoteBulletLife`, `lastRxSeq`,
   `haveRxSeq`, `lastRxEventSeq`, `lastRxStreamAt`, `peerPrimarySlot`, `peerStalled`, `PeerUp`,
   `RemotePaused`, `peerByeQueued`, `lastPuppetPos` — plus one hello loop, one timeout/stall
   verdict, and one `PeerLost` → match-end path.
5. **`NetLobby` / `NetGameBrowser`** — one code, one joiner, "connected" is a boolean.

Layers 1–3 are small and mechanical. Layer 4 is the real work. Layer 5 is UX.

## Topology: star (host relay), not mesh — and the reason is NAT, not taste

There is **no TURN server** (11.4 shipped STUN-only; ~10–15 % of NAT pairs fail outright). Peer
pairs are independent trials, so the probability that a *whole lobby* forms compounds:

| Topology | Connections for 4 peers | All-connect @ 85 %/pair | @ 90 %/pair |
|---|---|---|---|
| Full mesh | 6 | **≈ 38 %** | ≈ 53 % |
| Star (host hub) | 3 | **≈ 61 %** | ≈ 73 % |

A 4-player mesh lobby would fail to form more often than it formed. Star roughly doubles the odds
and matches the authority model already in place (the host is *already* authoritative for the
world, score, lives, resets and level script), so the hub is a role that exists rather than one
being invented.

**Cost of the star:** client→client ship state is relayed through the host, adding ~½(RTT_A+RTT_B)
to the *other clients'* puppets. Remote ships already render `InterpDelayMs` (100 ms) behind, so
relayed channels need a larger per-channel delay (~150 ms, or derived from observed arrival
jitter) or they will extrapolate constantly.

**Host bandwidth at N=4** (ship state 31 B @ 30 Hz ≈ 0.93 KB/s; world snapshot ~500 B @ ~16.7 Hz
≈ 8.3 KB/s): per client ≈ 8.3 + 3 × 0.93 ≈ 11 KB/s, × 3 clients ≈ **33 KB/s up**. Comfortable on
any home connection; worth an eye, not a redesign.

TURN becomes materially more attractive at N > 2 (it converts the compounding failure into a
per-pair fallback) — the standing TURN go/no-go should be re-decided as part of this, not after.

## Design

**A. `PeerChannel` — the layer-4 refactor.** Lift the ~15 singleton fields into a
`PeerChannel` keyed by peer id, held in a `Dictionary<string, PeerChannel>` the way
`friendChannels` already is. Per peer: handshake state (hello/welcome, build hash, flags),
liveness (`lastRxStreamAt`, stall, timeout), `peerPrimarySlot`, pause flag, event seq. The static
public API (55 external call sites across `GameScene`, `PlayerShip`, `KillableAlien`,
`ComponentBin`, `MenuScene`, …) stays untouched — a static facade over a per-peer core, exactly
the shape `plans/net-headless-sim.md` already specced for its own reasons.

**B. Converge the two ship paths.** Today `MsgShipState` means "the sender's primary" (identity
implicit — there is only one sender) and `MsgFriendState` is the slot-tagged general case. With
N peers "implicit sender" stops being meaningful, so fold the primary into the slot-keyed form:
**every ship on the wire carries its slot**, and one code path drives every remote ship. The
friend path was deliberately isolated from the primary path so it could not regress it; this is
the moment to unify them, and it wants the headless sim as its safety net.

**C. Transport addressing.** `INetTransport` grows `SendStreamTo(peerId, …)` /
`SendReliableTo(peerId, …)` (existing broadcast methods become fan-out over the peer set), and
`OnData`'s `senderId` starts carrying a real id. `webrtc.js` holds a map of peer id →
`{pc, chS, chR}`. `BroadcastChannelTransport` gets the same treatment — the loopback rig must
support 3–4 tabs, since that is where this gets developed.

**D. Signaling.** A room becomes host + up to 3 joiners: N−1 offer/answer exchanges, each joiner
signalling only with the host (star). The room is full at 4. `test_signal.py` extends to cover a
3rd/4th join, a mid-session leave, and the full-room refusal.

**E. Liveness and match-end policy — a real decision, not a port.** Today *any* peer leaving ends
the match for both. At N=4 that is hostile: one player's wifi should not kill three other people's
run. The `listedSession` semantic is already the right shape and generalises:
- **host leaves → match ends** for everyone (no host migration; stage 11 ruled it out).
- **client leaves → its slots free, play continues**, exactly as a listed host reverts today.

**F. World authority is unchanged.** Host→client snapshots/events simply fan out; claims arrive
from any client and the ledgers are already per-`(netId, slot)` with room for 8 slots.

**G. Lobby UX.** Host shows the room code plus a live roster of who has joined and starts when
ready (today "connected" is a boolean and launch is implicit). The browser's players column and
JIP already handle "any empty slot", so a 3rd/4th arrival mid-game largely falls out.

## Verification plan

- **Headless N-peer sim first.** `plans/net-headless-sim.md` already specs the two-peer,
  in-one-process sim and the de-static refactor it needs — the same refactor as (A). Building it
  as an *N*-peer sim makes it the regression net for this whole epic, and it is the only sane way
  to assert on 4-peer event ordering, claim ledgers and reset/id-churn.
- **Loopback first, WebRTC second.** `BroadcastChannelTransport` with 3–4 tabs exercises every
  layer above the transport with no server and no NAT.
- Hidden tabs tick at ~30 Hz (`index.html`'s `document.hidden → setTimeout(tickJS, 33)` fallback),
  so an N-tab roster/data check does **not** need N foreground windows — as used for the evidence
  above. Timing/feel numbers still require focus.
- `?netlocal=<1-3>` already covers couch seats on any peer; a new `?netpeers=<n>` seeds the rig.
- Existing gates unchanged: `[net]` metrics lines, `eaNetSim.test(...)`, `server/signal/test_signal.py`.

## Suggested card breakdown (sequential)

| Card | Scope |
|---|---|
| **12.0** | Headless N-peer sim + de-static core (subsumes the deferred `net-headless-sim` card) |
| **12.1** | Transport addressing + N-peer signaling room (webrtc.js, `INetTransport`, signal server, 3-tab loopback rig) |
| **12.2** | `PeerChannel` refactor + converge the primary/friend ship paths — **still 2 peers on the wire**, so it must be behaviour-neutral |
| **12.3** | N-peer session: per-peer hello/welcome + roster negotiation, per-peer liveness, the new match-end policy |
| **12.4** | Lobby + game-browser UX for 3–4 (host roster, start-when-ready, JIP into slots 3/4) |
| **12.5** | Hardening: relayed-channel interp delay, bandwidth soak, **TURN go/no-go re-decided** |

## Out of scope

- **Host migration** — ruled out in stage 11; the host leaving ends the match.
- **TeamChallenge above 2 players** — the tether is inherently pairwise (`ShipConnector`, the
  soft `NetPullOwnShip` model). A 3–4 player tether is a *game design* question, not a netcode
  one; TeamChallenge stays 2-player.
- **WebcamAliens** — already excluded from net sessions (the camera is the controller).
- Anything that would weaken the two hard invariants: no `?net` flag ⇒ the net layer is never
  constructed, and single-player never contacts a server (the public-listing beacon is the one
  knowing, opt-out-able exception).
