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

        // Unreliable-class lane (~30 Hz ship stream, later world snapshots).
        void SendStream(byte[] payload);

        // Ordered + guaranteed lane (handshake, spawn/death/blast events).
        void SendReliable(byte[] payload);

        void Close();

        // (payload, arrivedOnReliableLane, senderId). Fired from JS callbacks -- consumers
        // should queue and drain on the game tick rather than mutating game state directly.
        event Action<byte[], bool, string> OnData;

        // Best-effort "peer is going away" (pagehide). A silent drop must still be caught
        // by a stream timeout above this layer.
        event Action<string> OnPeerBye;
    }
}
