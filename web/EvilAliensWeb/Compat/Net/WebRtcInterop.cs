using System;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat.Net
{
    // C# shim for the eaRtc JS facade (wwwroot/webrtc.js) -- the real WebRTC transport
    // (card 11.4). Mirrors NetInterop/WebcamInterop: JS owns the platform objects
    // (RTCPeerConnection, signaling WebSocket, code-entry overlay), C# sees Send/Host/
    // Join/Close plus [JSInvokable] static callbacks. Kept separate from NetInterop so
    // the two transports stay independent (the BroadcastChannel dev rig is untouched).
    //
    // Callbacks fire from JS event handlers -- consumers (WebRtcTransport/NetLobby) must
    // queue and drain on the game tick rather than mutating game state directly.
    public static class WebRtcInterop
    {
        private static IJSInProcessRuntime _js;

        internal static event Action<byte[], bool> OnData;
        internal static event Action<string, string> OnPhase; // (phase, detail)
        internal static event Action<string> OnCodeEntry;     // "" = cancelled

        // Public game browser (card 2001fbd8). OnRooms carries the JSON rooms array from a
        // browse; OnPing carries a per-host measured RTT (code, ms) as each pong lands;
        // OnBrowseFail carries a browse-socket failure reason. All fire from JS callbacks --
        // NetGameBrowser queues + drains them on the game tick (the NetLobby pattern).
        internal static event Action<string> OnRooms;
        internal static event Action<string, int> OnPing;
        internal static event Action<string> OnBrowseFail;

        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        internal static void Host(string signalUrl)
        {
            _js?.InvokeVoid("eaRtc.host", signalUrl);
        }

        internal static void Join(string signalUrl, string code)
        {
            _js?.InvokeVoid("eaRtc.join", signalUrl, code);
        }

        internal static void Send(byte[] payload, bool reliable)
        {
            _js?.InvokeVoid("eaRtc.send", Convert.ToBase64String(payload), reliable);
        }

        internal static void Close()
        {
            _js?.InvokeVoid("eaRtc.close");
        }

        // ---- public game browser (card 2001fbd8) ----------------------------------------

        // Host side: list / update / hide / drop this game's room. `proto` is
        // NetSession.ProtocolVersion; the JS pairs it with the build hash for the
        // server-side compatibility filter.
        internal static void List(string signalUrl, int level, int difficulty, int players, int proto)
        {
            _js?.InvokeVoid("eaRtc.list", signalUrl, level, difficulty, players, proto);
        }

        internal static void Relist(int level, int difficulty, int players)
        {
            _js?.InvokeVoid("eaRtc.relist", level, difficulty, players);
        }

        internal static void Unlist()
        {
            _js?.InvokeVoid("eaRtc.unlist");
        }

        internal static void EndListing()
        {
            _js?.InvokeVoid("eaRtc.endListing");
        }

        // Joiner side: open / close the browse socket.
        internal static void Browse(string signalUrl, int proto)
        {
            _js?.InvokeVoid("eaRtc.browse", signalUrl, proto);
        }

        internal static void EndBrowse()
        {
            _js?.InvokeVoid("eaRtc.endBrowse");
        }

        internal static void PromptCode()
        {
            _js?.InvokeVoid("eaRtc.promptCode");
        }

        internal static void ClosePrompt()
        {
            _js?.InvokeVoid("eaRtc.closePrompt");
        }

        // The published binary's fingerprint (deploy.yml stamps window.eaBuildHash;
        // local builds read 'dev'). Compared in the hello handshake -- peers must run
        // the identical build.
        internal static string BuildHash()
        {
            try
            {
                return _js?.Invoke<string>("eaRtc.buildHash") ?? "dev";
            }
            catch (Exception)
            {
                return "dev";
            }
        }

        [JSInvokable("rtcData")]
        public static void Data(string b64, bool reliable)
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
            OnData?.Invoke(bytes, reliable);
        }

        // phases: contacting, code (detail = the room code), peer, connected,
        // failed (detail = reason), closed (peer channel closed / bye).
        [JSInvokable("rtcPhase")]
        public static void Phase(string phase, string detail)
        {
            OnPhase?.Invoke(phase ?? "", detail ?? "");
        }

        [JSInvokable("rtcCodeEntry")]
        public static void CodeEntry(string code)
        {
            OnCodeEntry?.Invoke(code ?? "");
        }

        // browse -> the JSON rooms array (see server main.py listing_entry()).
        [JSInvokable("rtcRooms")]
        public static void Rooms(string json)
        {
            OnRooms?.Invoke(json ?? "[]");
        }

        // A host's pong landed: (room code, measured round-trip ms).
        [JSInvokable("rtcPing")]
        public static void Ping(string code, int rttMs)
        {
            OnPing?.Invoke(code ?? "", rttMs);
        }

        [JSInvokable("rtcBrowseFailed")]
        public static void BrowseFailed(string reason)
        {
            OnBrowseFail?.Invoke(reason ?? "");
        }
    }
}
