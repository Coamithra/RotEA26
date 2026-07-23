using System;

namespace EvilAliensWeb.Compat.Net
{
    // INetTransport impl #2 (card 11.4): real WebRTC DataChannels via the eaRtc JS facade.
    // The stream lane is genuinely unreliable+unordered here (maxRetransmits:0) -- the
    // contract consumers were built against from 11.1.
    //
    // Two construction modes:
    //  - attachOnly (menu lobby): NetLobby already drove signaling + ICE to 'connected'
    //    before the session starts; Open() just attaches the data handlers.
    //  - initiate (?net=host/join&rtc URL boot): Open() itself starts signaling using the
    //    DebugFlags signal URL (+ ?code= for the join side); data flows once ICE lands,
    //    and NetSession's hello loop keeps knocking until then.
    public sealed class WebRtcTransport : INetTransport
    {
        public event Action<byte[], bool, string> OnData;
        public event Action<string> OnPeerBye;

        private readonly bool attachOnly;
        private bool open;

        public WebRtcTransport(bool attachOnly)
        {
            this.attachOnly = attachOnly;
        }

        public void Open(string room)
        {
            if (open)
            {
                return;
            }
            open = true;
            WebRtcInterop.OnData += Forward;
            WebRtcInterop.OnPhase += ForwardPhase;
            if (!attachOnly)
            {
                if (DebugFlags.NetRole == NetRole.Host)
                {
                    WebRtcInterop.Host(DebugFlags.NetSignal);
                }
                else
                {
                    if (string.IsNullOrEmpty(DebugFlags.NetCode))
                    {
                        Console.WriteLine("[net] ?net=join&rtc needs ?code=<roomcode> (the host tab prints it)");
                    }
                    WebRtcInterop.Join(DebugFlags.NetSignal, DebugFlags.NetCode);
                }
            }
        }

        public void SendStream(byte[] payload)
        {
            WebRtcInterop.Send(payload, reliable: false);
        }

        public void SendReliable(byte[] payload)
        {
            WebRtcInterop.Send(payload, reliable: true);
        }

        public void Close()
        {
            if (!open)
            {
                return;
            }
            open = false;
            WebRtcInterop.OnData -= Forward;
            WebRtcInterop.OnPhase -= ForwardPhase;
            WebRtcInterop.Close();
        }

        private void Forward(byte[] payload, bool reliable)
        {
            OnData?.Invoke(payload, reliable, "peer");
        }

        private void ForwardPhase(string phase, string detail)
        {
            if (phase == "closed" || phase == "failed")
            {
                OnPeerBye?.Invoke(phase + ":" + detail);
            }
        }
    }
}
