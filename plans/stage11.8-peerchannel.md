# Stage 11.8 — PeerChannel refactor + converge the ship paths (protocol v23)

Card `b2828be8`. Epic: 3-4 MACHINE online co-op (`plans/4p-online-coop.md` §A + §B). Follows 11.7 (transport addressing, shipped). **Still exactly 2 peers on the wire, and behaviour-neutral** — the acceptance bar is the existing two-tab metrics recipe reading the same, with `net_jip_sync.py` and the scenario harness as the safety net.

## Context

`NetSession` is a static session orchestrator hard-wired to ONE remote peer: ~20 singleton members hold *the* peer's handshake, liveness, pause, primary slot, ship buffer and puppet. The extra-ship path (`FriendChannel`, slot-keyed, per-slot buffer + clock + puppet) is already the shape an N-peer session needs. Two wire messages describe ships: `MsgShipState` ("the sender's primary", identity implicit) and `MsgFriendState` (slot-keyed general case). With N senders "the sender's primary" stops meaning anything, so the primary folds into the slot-keyed form.

Also folded in (card a5b1e941, archived): the replicated combo TIMER. `NetSetHudState` refreshes the observer's combo timer to FULL on every live-combo packet, so the fade-out is up to ~1 s late and the alpha ramp never tracks the owner's. One byte of remaining-time in the `MsgHudState` entry fixes it; this is the card where the wire format is already open.

## Design

**A. `PeerChannel` (new file `Compat/Net/PeerChannel.cs`)** — `internal sealed class`, held in `Dictionary<string, PeerChannel>` keyed by the transport senderId (real on every transport since 11.7). Per peer: `Up`, `LastRxStreamAt`, `Stalled`, `RemotePaused` + kick-offer clock, `PrimarySlot` (was `peerPrimarySlot`), `PeerId` (identity token), `ScriptGate`, `LastRxEventSeq`, last-hello clock, and the ship channels. The session keeps exactly ONE entry (2 peers on the wire is an acceptance criterion): a frame from a second senderId while a live channel exists is dropped with a console note; a channel whose peer is DOWN is re-keyed to a new senderId (the `?net=` reconnect flow — the singletons served whoever talked, so this preserves it).

**`ShipChannel`** unifies `FriendChannel` and the primary singletons: buffer, render clock, shots/bulletLife, puppet, `SeenAlive`, `Alive` latch, stream seq + pop-metric baselines (primary-only, gated on `IsPrimary` so metrics read identically). One `PeerChannel.Primary` + `Dictionary<byte, ShipChannel> Extras` — the primary is a distinguished field, not a dictionary entry, because its slot is `SlotNone` pre-settle and can be re-granted mid-handshake.

The static public API (~115 externally referenced members) is unchanged — facades over the channel (`PeerUp`, `RemotePaused`, `PeerStalled`, `PeerHoldsShipSpawn`, `HasRemotePuppet`, `HasFriendPuppet`, `KickPeer`, ...). Hard constraints kept: `ResolveBaseVelocity` stays a static method and `MaxObservedSpeedPxPerMs` a const on `NetSession` (reflection-bound by `logic_probe`), `SlotAdopt` stays nested.

**B. One ship message.** `MsgShipState` (0x10) becomes the slot-keyed form, 34 bytes:
`[0x10][slot:1][flags:1][shotsPerSec:1][bulletLife:1][seq:2][t:4][pos:8][vel:8][aim:4][shotCount:1][asplodeBits:1][bounceBits:1]`
Flags: `Alive` (bit 0), `ScriptGate` (bit 1), **`Primary` (bit 2, new)** — set on the sender's primary-slot frame, which is the heartbeat carrier (streamed even shipless, alive=false). The flag rather than a slot comparison makes routing self-describing across the slot-settle race and any mid-session re-grant. `MsgFriendState` (0x11) is RETIRED (id reserved, never reused). One encoder/decoder; one receive path (`HandleShipFrame` routes by the flag: primary → alive-edge death/respawn-clear/scriptGate semantics, extras → timeout death/resume-gap-clear semantics — the deliberate asymmetries stay, per-channel rather than per-message-type); one drive path (`DriveRemoteShip`/`DriveFriendShip` stay as the public entry points and share one implementation). Seq counters stay TWO (primary contiguous, extras shared) so `seqGap` keeps meaning what it means.

**C. Combo remaining-time byte.** `MsgHudState` entry grows `[comboLeft:1]` after the combo (HudSlotBytes 16 → 17): the owner's `combotimer.TimeLeft/1000` quantised to a byte. `NetSetHudState` parks the observer's timer with `Timer.SetNormalized` instead of `Reset()`, so the two screens' combo readouts fade in phase and lapse together. Cosmetic; strictly better than the full-refresh.

**ProtocolVersion 22 → 23.** Forced bump, not a courtesy: both ship layouts moved (a v22 peer mis-parses every ship frame) and the HUD entry is fixed-width (mis-parses every entry after the first). Consequences carried per the card: the handshake hard-rejects mid-upgrade pairs, the bumped build stops seeing already-deployed games in the browser (`ProtocolVersion` is the compat filter), and the site deploys manually so the split is a deliberate act.

## Out of scope (11.9+)

N-peer session semantics: per-peer hello/welcome roster negotiation, per-peer drop ladder, pause-as-a-set, host relay of client ship state, addressed catch-up, match-end policy. Everything here must behave identically at 2 peers.

## Verification

- `dotnet build` clean; `tools/sim/logic_probe` (ProbeNetWire + reflection-bound cases must still bind).
- `python tools/headless/probes/run_probes.py` — the committed probe suite (net_selftests, net_single_tap, net_reset_spawn, net_respawn_pos, net_motion, net_stale, ...) after rebuilding eahl.
- `python tools/sim/net_jip_sync.py --level Level2` (+ a longer pass) — the card-named safety net; must stay GREEN.
- Final Chrome smoke: boots, zero console errors.
