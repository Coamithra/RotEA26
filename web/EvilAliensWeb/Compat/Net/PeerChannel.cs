using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Card b2828be8 (Stage 11.8): everything NetSession knows about ONE remote peer, lifted out
    // of ~20 static singletons so the session can hold peers in a dictionary keyed by the
    // transport senderId (real on every transport since card 583a3ef8). Since card 87242257
    // (Stage 11.9) the session genuinely holds a SET of these -- a host hub carries up to three
    // (Oracle.MaxPlayers minus its own seat), a client exactly one (its host) -- and the
    // handshake, liveness ladder, pause bookkeeping and reliable-event sequencing below are all
    // per-channel for that reason.
    internal sealed class PeerChannel
    {
        // The transport senderId this peer's frames arrive under -- the address, per
        // INetTransport's contract. Re-keyed (not replaced) when a DOWN peer reconnects under a
        // fresh identity, so the granted seat and event-seq bookkeeping survive exactly as the
        // static singletons did; see NetSession.GetOrCreatePeer.
        public string Id;

        public bool Up;
        public long LastRxStreamAt;
        public bool Stalled;

        // The peer holds a pause (EvPause on). RemotePauseAt/KickOfferShown are the host's
        // kick-offer clock over that pause (card 0b8a300b).
        public bool RemotePaused;
        public long RemotePauseAt;
        public bool KickOfferShown;

        // The peer's PRIMARY roster slot -- the seat its primary-flagged ship stream drives.
        // Host: the slot it granted this peer. Client: the host's, i.e. 0. SlotNone until the
        // handshake settles.
        public byte PrimarySlot = NetProtocol.SlotNone;

        // The peer's self-reported identity token hash (v6, card 0b8a300b) -- the kick+block key.
        public ulong PeerId;

        // Card 8a7772d6: the newest ShipFlagScriptGate off the HOST's primary stream (see
        // NetSession.PeerHoldsShipSpawn, which is what the scene actually reads).
        public bool ScriptGate;

        // Reliable-event ordering bookkeeping (seq-gap metric). -1 = nothing received yet.
        public int LastRxEventSeq = -1;

        // The reliable-event seq WE stamp on frames sent TO this peer (card 87242257). Per
        // RECIPIENT rather than the old session-global counter, because addressed sends (a
        // welcome, a slot grant, a replay catch-up, a relayed event) would otherwise open a
        // false seqGap at every peer that was not the target. A broadcast is now N addressed
        // sends, each contiguous on its own channel.
        public ushort TxEventSeq;

        // Per-peer hello cadence clock (the handshake retries until THIS peer's slot settles;
        // another peer's being mid-negotiation must not retrigger ours).
        public long LastHelloTx;

        // HOST only: the last "someone besides you holds a pause" value sent to this peer --
        // the per-recipient aggregate the pause-as-a-set relay sends edges of (card 87242257).
        public bool PauseSentTo;

        // The channel was refused (per-peer reject, or a kick): its frames are dropped without
        // touching the session, and the channel itself is swept at RemoveAtMs or on its bye --
        // whichever comes first. A refused sender that keeps talking costs one dictionary probe
        // per frame and nothing else.
        public bool Refused;
        public long RemoveAtMs;

        // The peer's primary ship -- a DISTINGUISHED channel rather than an Extras entry, because
        // its slot is SlotNone until the grant settles and can be re-granted mid-handshake
        // (ReserveRemotePrimarySlot's re-allocate path), and the heartbeat/alive-edge semantics
        // belong to the channel whichever seat it is in. Routing is by ShipFlagPrimary.
        public readonly ShipChannel Primary = new ShipChannel(isPrimary: true);

        // Every OTHER ship the peer owns (couch players, AI friends), keyed by roster slot --
        // the shape FriendChannel always had.
        public readonly Dictionary<byte, ShipChannel> Extras = new Dictionary<byte, ShipChannel>();

        public PeerChannel(string id)
        {
            Id = id ?? "";
        }

        // The WORLD-scoped reset for a pairing that outlives its level (card 3b6c12e7): drop
        // everything describing the match, keep everything describing the PAIRING -- Up,
        // PrimarySlot, PeerId, and the event-seq bookkeeping (the tx/rx event sequences are
        // monotone per session by design; note the pre-card ResetPerMatchState left the ship
        // stream's HaveRxSeq alone too, so this does as well).
        public void ResetMatchState()
        {
            Primary.ClearSamples();
            Primary.Alive = false;
            Primary.Puppet = null;
            Primary.SeenAlive = false;
            Primary.HasLastPuppetPos = false;
            ScriptGate = false;
            RemotePaused = false;
            RemotePauseAt = 0;
            KickOfferShown = false;
            PauseSentTo = false;
            foreach (ShipChannel ch in Extras.Values)
            {
                ch.Puppet = null;
            }
            Extras.Clear();
        }
    }

    // One replicated ship: its own jitter buffer + interpolation clock + latest fire state + the
    // puppet. The unification of the old FriendChannel with the primary-remote singletons (card
    // b2828be8): the fields below the IsPrimary marker only mean anything on the primary channel
    // -- the alive EDGE (death explosion / respawn buffer clear), the stream seq-gap metric and
    // the pop-metric baselines -- and the metrics stay primary-gated so the [net] line reads
    // exactly as it did.
    internal sealed class ShipChannel
    {
        public readonly bool IsPrimary;

        public readonly ShipStateBuffer Buffer = new ShipStateBuffer();
        public double RenderMs = double.NaN;
        public int ShotsPerSec = 8;
        public float BulletLife = 450f;
        public PlayerShip Puppet;

        // Extras only: the resume-gap clear and the per-slot timeout read it
        // (NetSession.Friends.cs). The primary never stamps it -- its death is the alive EDGE
        // below and its liveness is the peer-level LastRxStreamAt, so on Primary this stays 0
        // ("infinitely stale") and must not be consulted.
        public long LastRxAt;

        // Extras only (card 6fb406bc): the newest sample's ShipFlagRelayed -- this channel's
        // frames took the host relay's second hop, so its render clock runs
        // NetSession.RelayedInterpDelayMs behind newest instead of InterpDelayMs. Latched in
        // HandleExtraShipFrame; the primary channel is one hop by construction and never
        // consults it.
        public bool Relayed;

        // Primary only: the alive LEVEL off the newest in-order sample (was remoteAlive), and
        // whether the peer has reported alive=true while we held THIS puppet -- only then does
        // losing alive mean a death worth showing (card b4d0ba1d).
        public bool Alive;
        public bool SeenAlive;

        // Primary only: stream seq-gap metric bookkeeping.
        public ushort LastRxSeq;
        public bool HaveRxSeq;

        // Primary only: the correction-pop metric's baseline.
        public Vector2 LastPuppetPos;
        public bool HasLastPuppetPos;

        public ShipChannel(bool isPrimary)
        {
            IsPrimary = isPrimary;
        }

        public void ClearSamples()
        {
            Buffer.Clear();
            RenderMs = double.NaN;
        }
    }
}
