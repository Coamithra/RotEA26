using System;

namespace EvilAliensWeb.Compat.Net
{
    // The seam card 11.4 swaps WebRTC in behind. Two lanes mirror the eventual pair of
    // RTCDataChannels: the STREAM lane is unreliable-class (may drop / arrive out of order --
    // consumers must tolerate both, see ShipStateBuffer), the RELIABLE lane is ordered and
    // guaranteed. The dev BroadcastChannelTransport happens to deliver both lanes reliably
    // (same machine, in-order), but nothing above this interface may rely on that.
    //
    // JS owns the platform half (the house webcam.js/eaMusic pattern): a transport
    // implementation talks to a window.eaXxx facade via a [JSInvokable] shim (NetInterop for
    // BroadcastChannel; 11.4 adds a webrtc.js + its own shim behind this same interface).
    public interface INetTransport
    {
        // Join the named room. Peers discover each other above this layer (NetSession's
        // Hello/Welcome), so Open just attaches to the medium.
        void Open(string room);

        // Unreliable-class lane (~30 Hz ship stream, later world snapshots). The unaddressed
        // forms FAN OUT to every connected peer -- with one peer up (today's whole protocol)
        // they are indistinguishable from unicast.
        void SendStream(byte[] payload);

        // Ordered + guaranteed lane (handshake, spawn/death/blast events).
        void SendReliable(byte[] payload);

        // Addressed sends (card 583a3ef8, the N-peer stages). `peerId` is the same opaque,
        // session-stable token OnData reports as senderId -- that identity IS the address;
        // there is no separate naming scheme. A send to an unknown or departed peer is a
        // SILENT DROP (the closed-DataChannel semantic the unaddressed forms already have),
        // never a throw.
        void SendStreamTo(string peerId, byte[] payload);

        void SendReliableTo(string peerId, byte[] payload);

        void Close();

        // (payload, arrivedOnReliableLane, senderId). Fired from JS callbacks -- consumers
        // should queue and drain on the game tick rather than mutating game state directly.
        // The senderId is real on every implementation since card 583a3ef8 (WebRtcTransport
        // used to hard-code a literal), and it is NetSession's peer-channel KEY since card
        // b2828be8 -- the address a hub routes by (card 87242257).
        event Action<byte[], bool, string> OnData;

        // Best-effort "peer is going away" (pagehide). A silent drop must still be caught
        // by a stream timeout above this layer. The string names the DEPARTING PEER (its
        // OnData senderId) on every per-peer departure -- with one exception: WebRtcTransport's
        // TERMINAL whole-link failure still reports its legacy "phase:reason" string, and in
        // that case every peer is gone at once. NetSession routes byes by id since card
        // 87242257 and treats an unrecognized string as "all peers", per this contract.
        event Action<string> OnPeerBye;
    }
}
