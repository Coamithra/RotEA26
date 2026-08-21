using System;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat.Net
{
    // C# shim for the eaNet JS facade (wwwroot/index.html) -- the BroadcastChannel loopback
    // medium. Mirrors the WebcamInterop pattern: JS owns the platform object (the channel),
    // C# sees only Send/Open/Close plus [JSInvokable] static callbacks. Payloads cross the
    // boundary as base64 (the established convention; ~30 Hz x ~30 bytes is trivial).
    //
    // Static events rather than per-instance because [JSInvokable] entry points must be
    // static; BroadcastChannelTransport subscribes and re-raises as instance events.
    public static class NetInterop
    {
        private static IJSInProcessRuntime _js;

        internal static event Action<byte[], bool, string> OnData;
        internal static event Action<string> OnBye;

        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        internal static void Open(string room)
        {
            _js?.InvokeVoid("eaNet.open", room);
        }

        // `to` null/empty = broadcast (every other tab in the room); a peer id = only the tab
        // whose eaNet id matches delivers it (the tabs filter, the medium still carries it --
        // BroadcastChannel has no unicast, and does not need one for a dev rig).
        internal static void Send(byte[] payload, bool reliable, string to = null)
        {
            _js?.InvokeVoid("eaNet.send", Convert.ToBase64String(payload), reliable, to);
        }

        internal static void Close()
        {
            _js?.InvokeVoid("eaNet.close");
        }

        // JS bridge: eaNet.onmessage -> DotNet.invokeMethod('EvilAliensWeb', 'netData', b64, rel, from).
        [JSInvokable("netData")]
        public static void Data(string b64, bool reliable, string from)
        {
            if (string.IsNullOrEmpty(b64))
            {
                return;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                return;
            }
            OnData?.Invoke(bytes, reliable, from);
        }

        // JS bridge: the sender tab's pagehide handler posts a 'bye' so a clean tab close
        // drops the peer immediately (the stream timeout still covers silent deaths).
        [JSInvokable("netPeerBye")]
        public static void PeerBye(string from)
        {
            OnBye?.Invoke(from);
        }
    }
}
