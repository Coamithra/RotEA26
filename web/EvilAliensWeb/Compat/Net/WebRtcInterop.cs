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

        // (payload, reliableLane, peerId). The peerId is JS's per-connection key ("1".."3"
        // on the host side, "h" on a joiner) -- the same string SendTo takes as the address.
        internal static event Action<byte[], bool, string> OnData;
        internal static event Action<string, string> OnPhase; // (phase, detail)
        internal static event Action<string> OnCodeEntry;     // "" = cancelled

        // Public game browser (card 2001fbd8). OnRooms carries the JSON rooms array from a
        // browse; OnPing carries a per-host measured RTT (code, ms) as each pong lands;
        // OnBrowseFail carries a browse-socket failure reason. All fire from JS callbacks --
        // NetGameBrowser queues + drains them on the game tick (the NetLobby pattern).
        internal static event Action<string> OnRooms;
        internal static event Action<string, int> OnPing;
        internal static event Action<string> OnBrowseFail;

        // Room thumbnails (card e7404647). OnShotRequest = the server is pulling a picture of
        // our listed game (NetRoomShot arms a capture); OnShot = a fetched thumbnail arrived,
        // already decoded to raw RGBA by JS (code, seq, pixels, w, h) -- seq 0 with an empty
        // buffer is "the server has nothing for that code", which retires the request.
        internal static event Action OnShotRequest;
        internal static event Action<string, int, byte[], int, int> OnShot;

        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // maxPeers = total machines including this one, clamped 2..4 in JS; 2 (the default)
        // is every shipped flow. >2 is inert until the session layer is N-peer (card 87242257).
        internal static void Host(string signalUrl, int maxPeers = 2)
        {
            _js?.InvokeVoid("eaRtc.host", signalUrl, maxPeers);
        }

        internal static void Join(string signalUrl, string code)
        {
            _js?.InvokeVoid("eaRtc.join", signalUrl, code);
        }

        internal static void Send(byte[] payload, bool reliable)
        {
            _js?.InvokeVoid("eaRtc.send", Convert.ToBase64String(payload), reliable);
        }

        internal static void SendTo(string peerId, byte[] payload, bool reliable)
        {
            _js?.InvokeVoid("eaRtc.sendTo", peerId, Convert.ToBase64String(payload), reliable);
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

        // ---- room thumbnails (card e7404647) --------------------------------------------

        // Host side: answer a pull with a raw RGBA capture. JS does the JPEG encoding -- C#
        // never handles compressed image bytes in either direction.
        internal static void SendShot(byte[] rgba, int width, int height)
        {
            _js?.InvokeVoid("eaRtc.sendShot", Convert.ToBase64String(rgba), width, height);
        }

        // Joiner side: fetch one listed room's stored thumbnail.
        internal static void ShotGet(string code)
        {
            _js?.InvokeVoid("eaRtc.shotGet", code);
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

        // This browser's own identity token (card 0b8a300b) -- a random localStorage value,
        // hashed into the hello so a host can refuse a peer it kicked+blocked. Self-reported
        // (see eaRtc.peerId): a speed bump against casual re-joining, not authentication.
        // "" means JS could not produce one; NetSession treats that as "unblockable".
        internal static string PeerId()
        {
            try
            {
                return _js?.Invoke<string>("eaRtc.peerId") ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        [JSInvokable("rtcData")]
        public static void Data(string b64, bool reliable, string peerId)
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
            OnData?.Invoke(bytes, reliable, peerId ?? "");
        }

        // phases: contacting, code (detail = the room code), peer, connected,
        // failed (detail = reason), closed (peer channel closed / bye),
        // peergone (detail = the departed peer's id -- N-peer hosts only, card 583a3ef8).
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

        // The server is asking our listed game for a fresh thumbnail (card e7404647).
        [JSInvokable("rtcShotRequest")]
        public static void ShotRequest()
        {
            OnShotRequest?.Invoke();
        }

        // A fetched thumbnail, decoded to raw RGBA by JS. Base64 rather than a byte[] parameter
        // for the house reason (the eaRtc/eaNet boundary is base64 throughout); a malformed
        // payload retires the request instead of throwing across the interop boundary.
        [JSInvokable("rtcShot")]
        public static void Shot(string code, int seq, string b64, int width, int height)
        {
            byte[] rgba;
            if (string.IsNullOrEmpty(b64))
            {
                rgba = null;
            }
            else
            {
                try
                {
                    rgba = Convert.FromBase64String(b64);
                }
                catch (FormatException)
                {
                    rgba = null;
                }
            }
            OnShot?.Invoke(code ?? "", seq, rgba, width, height);
        }

        [JSInvokable("rtcBrowseFailed")]
        public static void BrowseFailed(string reason)
        {
            OnBrowseFail?.Invoke(reason ?? "");
        }
    }
}
