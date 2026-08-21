# Stage 11.9 — N-peer session: roster, liveness, match-end policy (card 87242257)

## Context

Cards 11.7 (`583a3ef8`) and 11.8 (`b2828be8`) left the stack one layer short of "more than two machines": the transport is N-peer (addressed sends, real senderIds, N-capable signaling + LocalSocketNet `--net-peers`), and the per-peer state lives on `PeerChannel` — but `NetSession` still pairs EXACTLY ONE peer (`GetOrCreatePeer` drops a third identity by design). This card makes the session hold a peer SET on the star topology `plans/4p-online-coop.md` fixed: the host is the hub, clients connect only to the host, and client↔client state flows through host relay. World authority is untouched — host→client snapshots/events fan out, claims arrive from any client, the ledgers are already per-(netId, slot).

Scope, from the design doc's card table: per-peer hello/welcome + roster negotiation (grants serialized against races), per-peer liveness/drop ladder, pause as a set, host relay of client ship/HUD state (symmetric events re-emitted under the host's own event seq), ADDRESSED catch-up, the new match-end policy, per-peer `[net]` metrics. Lobby/browser UX and capacity>2 signaling stay card `0257f8ba` (11.10); relayed-channel interp delay, N=4 soak and TURN stay `6fb406bc` (11.11). The menu lobby's real WebRTC rooms therefore stay capacity 2 until 11.10 — this card is exercised on the N-capable dev transports (BroadcastChannel tabs, `NetWire`, LocalSocketNet `--net-peers`).

## THE DESIGN DECISION (the card's explicit ask)

Today any peer leaving a MENU session ends the match for both. At N=4 that is hostile — one
player's wifi should not kill three other people's run. **Decision (adopting the card's proposal,
uniformly, N=2 included):**

- **Host leaves → the match ends for every client** (no host migration). Host scene-down
  (quit / game over) sends `EvLeave` to all + `Stop()`; each client takes the existing
  `EndMatchPeerGone` path. A client losing its host (leave / bye / timeout / kick) is
  likewise still a match end for that client.
- **A client leaving → its seats free and play continues for everyone else** — exactly the
  `listedSession` semantic generalised. The host releases the departed peer's primary +
  couch seats, tells the remaining clients with a new `EvPeerLeft` beat (slot mask) so their
  seats free too, and keeps playing. When the LAST client goes: mid-level the host reverts to
  plain single-player (`RevertToSinglePlayer` — a listed game re-lists); at the menus (lobby,
  no scene) the session Stops with the "player left" notice, since a pre-11.10 lobby with
  zero peers is a dead end.
- A finished level keeps ALL pairings alive and everyone returns to the lobby (card 3b6c12e7,
  unchanged — `ResetPerMatchState` now loops the channels).

Consequence at N=2: a menu-session host whose partner drops now keeps playing solo instead of
being thrown to the menu. That is the deliberate behaviour change this card ships.

## Design

**Protocol v24.** One new event, `EvPeerLeft = 27` (`EncodeByteEvent`, payload `[slotMask:1]`),
host→clients: "these roster seats' owner left — free them". Client apply: for each masked slot
not owned locally: drop the host-channel extras entry (explode its puppet if one is up), free
the `RemoteFriend` seat. No other layout changes. `EvPause`'s host→client meaning becomes "at
least one participant besides you holds a pause" (aggregate — see below); payload unchanged.

**Peer set (`NetSession`).** `peers` becomes a real set. Host cap: 3 up channels (host + 3 =
`Oracle.MaxPlayers`); an over-cap sender gets an addressed `RejectFull` + a once-per-id console
note. Client cap: 1, and a client only CREATES a channel from a Hello/Welcome whose role byte
says host — on a bus medium (BroadcastChannel) clients see each other's frames directly, and
binding to a fellow client was previously possible; all other unknown-sender frames are
dropped client-side. A client addresses its post-pairing traffic to its host channel (stream +
events), so a 3-tab loopback rig carries no client↔client noise. Host-side channel creation
semantics (any frame; stream-first reconnect) are unchanged.

**Per-recipient reliable event seq.** Addressed sends make one global `txEventSeq` produce
false `seqGap`s at non-recipients, so the tx seq moves onto `PeerChannel.TxEventSeq` and every
reliable event goes through two helpers: `SendEventToPeer(p, encoder)` and
`SendEventToSessionPeers(encoder)` (encoder = `seq => Encode*(seq, ...)`). A `replayTarget`
latch inside the session helper is the ADDRESSED CATCH-UP: the `EvReady` handler wraps
`NetIdRegistry.ReplayLive()` + `NetReplayCatchUp()` with `replayTarget = p`, so a late joiner's
catch-up burst no longer re-blasts every already-caught-up peer (the 4p plan's stated hazard).
`MsgWelcome`, the JIP `EvLaunch` and `EvSlotGrant` are addressed to their peer for the same
reason. Hellos: per-peer addressed (with THAT peer's granted slot) while any channel is
unsettled; the broadcast hello survives only while no channel exists (it is how a pairing is
initiated).

**Roster negotiation, N-wide + serialized.** Everything keys off `p.PrimarySlot`, never off a
`ControlDevice.Remote` scan — `GetPlayerIndex(Remote)`/`DeviceIsPlaying(Remote)`/
`ReleasePlayer(Remote)` are ambiguous with two remote peers. `ReserveRemotePrimarySlot(p)`
re-uses a leftover Remote seat only if no OTHER channel claims it; `SpawnPuppet`/`ManagePuppet`
adopt strictly by `Owner == p.PrimarySlot`; `ReleasePeerSeats(p)` frees `p.PrimarySlot` +
`p.Extras` seats by slot. Grants are serialized by construction: each hello's reservation
lands in the oracle before the next hello is drained, so two joiners in one tick get distinct
seats; `AllocateSeat` additionally excludes every channel's primary. Rejects become per-peer
whenever ≥1 OTHER peer is up (addressed `MsgReject`, channel marked `Refused`, frames dropped,
swept after a grace or on its bye); with nobody up the old whole-session wind-down + notice
stands (client role, empty lobby, listed first-joiner — identical UX to today).

**Per-peer liveness.** The stall/timeout ladder runs per channel: `p.Stalled` per peer, the
`NetWaitOverlay` banner driven by the AGGREGATE (any up peer stalled — the scene setter already
self-guards); the 120 s paused backstop widens on `localPaused || any peer paused` (a world
frozen by anyone's pause throttles everyone's tab). A timeout verdict is `PeerLost(p)` — for a
host in a menu/listed session that is now the client-departure path above, not a match end.

**Pause as a set.** `p.RemotePaused` per channel; the world freezes while the aggregate is
nonzero (scene calls on aggregate edges). The host RELAYS pause as a per-recipient aggregate:
for each client X it sends `EvPause` edges of `localPaused || anyOtherClientPaused(X)` (tracked
as `p.PauseSentTo`), which is exactly the semantic the client code already implements — and it
is what makes A-pauses/B-pauses/A-unpauses hold B's freeze everywhere. At N=2 the wire traffic
is byte-identical to today. Kick offers tick per paused channel; the offer latches its TARGET,
`KickPeer(block)` kicks that peer only: addressed `EvKick`, seats freed, `EvPeerLeft` to the
rest, session wound down (after the egress grace) only when no peers remain.

**Host relay of client state (the star's hub duty).**
- *Ship state:* on the 33 ms cadence the host re-encodes, for every up peer p, p's primary
  (only while its alive latch is set) and each fresh extras channel as a NON-primary
  slot-keyed `MsgShipState` (host clock, own relay seq) and `SendStreamTo`s every OTHER up
  peer. Death propagates as the extras semantic: the relay stops, the recipient's 500 ms
  timeout explodes the puppet; the respawn's fresh stream re-spawns it (the resume-gap clear
  keeps it from bridging). Recipients cannot tell a relayed client primary from a host couch
  ship — that is the point of v23's one ship path.
- *HUD:* a client's `MsgHudState` is relayed verbatim to the other clients (it has no seq and
  carries only the sender's owned slots; the receiver's own-slot guard already protects its
  own panels).
- *Symmetric events:* `EvBlast`, `EvRespawn`, `EvSlowmo`, `EvTetherBreak` from a client are
  re-emitted by the host to every other client under each recipient channel's own event seq.
- *Receiver hygiene:* `HandleExtraShipFrame` refuses a slot the receiver owns — nothing off
  the wire may drive a locally-owned ship, and on a bus medium a client can see frames that
  were not meant for it.

**Per-peer `[net]` metrics.** The existing `[net]` line is unchanged at ≤1 peer (every probe
that greps it stays valid); `pri=` grows `+slot` per extra peer. A second `[netpeers]` line
prints on the same 5 s cadence whenever the session holds >1 channel: per peer, id, up/stalled/
paused/refused, quiet ms, primary buffer depth, extras count, event seqs.

## Out of scope

- Lobby/game-browser UX for 3–4, capacity>2 signaling rooms, per-peer kick UI (11.10).
- Relayed-channel interp delay (~150 ms), N=4 bandwidth soak, `bufferedAmount` back-pressure,
  the full multi-process eahl matrix (11.11). TURN stays deferred (owner decision).
- Host migration (host leaving ends the match — the decision above).
- TeamChallenge above 2 players (pairwise tether by design).

## Verification

- **`eaNetNPeer()` / `eval NetNPeer` (`Compat/Net/NetNPeerTest.cs`)** — the scenario-harness
  pattern: ONE real HOST session on a `NetWire(4)` with two SCRIPTED joiner endpoints, menu-
  runnable and leave-no-trace. Legs: two hellos in one drain get DISTINCT slots and each
  welcome/grant reaches only its peer; ship/HUD relay reaches the other joiner and never echoes
  to the source, with the re-encoded frame slot-keyed + non-primary; per-recipient event seqs
  stay contiguous at each endpoint; the pause set (A on, B on, A off keeps B's aggregate up,
  per-recipient edges correct); per-peer timeout frees exactly the dead peer's seats, emits
  `EvPeerLeft` to the survivor, and leaves the survivor Up; the match-end policy (one client's
  EvLeave → session continues with seats freed; the LAST client's → Stop); the client-side
  `EvPeerLeft` apply (planted RemoteFriend seat + channel freed). Committed to
  `net_selftests.txt` with its tally.
- **`python tools/sim/net_npeer_smoke.py`** — three eahl PROCESSES over LocalSocketNet
  (`--net-peers 2`): one `?net=host` + two `?net=join` on Level 2, asserting the three consoles
  print mirror-image THREE-seat rosters (`pri` fields consistent), `dupBad=0`, and a `[netpeers]`
  line on the host — the "wire actually goes past 2 peers" acceptance evidence.
- **Regression:** `python tools/headless/probes/run_probes.py` (net_selftests + every committed
  net probe), `python tools/sim/net_jip_sync.py --level Level2` stays green, clean Debug build,
  and the Chrome smoke (2-tab BroadcastChannel pair unchanged + a 3-tab `?net=` roster check).
