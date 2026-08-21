using System;

namespace EvilAliensWeb.Compat.Net
{
    // Dev-only loopback transport: two local tabs over a BroadcastChannel, zero network.
    // Both lanes ride the same channel (tagged with the lane bit) -- BroadcastChannel is
    // inherently reliable + ordered on one machine, which is fine: the interface contract
    // only promises LESS for the stream lane, never more.
    public sealed class BroadcastChannelTransport : INetTransport
    {
        public event Action<byte[], bool, string> OnData;
        public event Action<string> OnPeerBye;

        private bool open;

        public void Open(string room)
        {
            if (open)
            {
                return;
            }
            open = true;
            NetInterop.OnData += Forward;
            NetInterop.OnBye += ForwardBye;
            NetInterop.Open(room);
        }

        public void SendStream(byte[] payload)
        {
            NetInterop.Send(payload, reliable: false);
        }

        public void SendReliable(byte[] payload)
        {
            NetInterop.Send(payload, reliable: true);
        }

        // Addressed sends ride the same channel with a `to` field the receiving tabs filter on
        // (index.html eaNet). The senderId a tab reports IS its address, per the interface.
        public void SendStreamTo(string peerId, byte[] payload)
        {
            NetInterop.Send(payload, reliable: false, to: peerId);
        }

        public void SendReliableTo(string peerId, byte[] payload)
        {
            NetInterop.Send(payload, reliable: true, to: peerId);
        }

        public void Close()
        {
            if (!open)
            {
                return;
            }
            open = false;
            NetInterop.OnData -= Forward;
            NetInterop.OnBye -= ForwardBye;
            NetInterop.Close();
        }

        private void Forward(byte[] payload, bool reliable, string from)
        {
            OnData?.Invoke(payload, reliable, from);
        }

        private void ForwardBye(string from)
        {
            OnPeerBye?.Invoke(from);
        }
    }
}
