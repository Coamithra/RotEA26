using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Pre-session orchestration for the menu-driven online co-op flow (card 11.4): drives
    // eaRtc (host / join via the code-entry overlay), exposes a phase + room code for the
    // menu to draw, and starts the NetSession once the DataChannels are up. JS callbacks
    // are queued and drained by Tick() -- MenuScene calls it from its Update, so all state
    // (including session start) mutates on the game tick.
    public static class NetLobby
    {
        public enum LobbyPhase
        {
            Idle,
            Contacting, // signaling WS opening / host request in flight
            Hosting,    // have a room code, waiting for a friend
            Prompting,  // join: the code-entry overlay is up
            Connecting, // paired, ICE in progress
            Connected,  // channels up, session started
            Failed,     // FailText says why
        }

        public static LobbyPhase Phase { get; private set; } = LobbyPhase.Idle;

        public static string RoomCode { get; private set; } = "";

        public static string FailText { get; private set; } = "";

        public static bool IsHosting => weHost;

        private static Game game;
        private static bool weHost;
        private static bool subscribed;
        private static readonly Queue<(string phase, string detail)> phaseQueue = new Queue<(string, string)>();
        private static readonly Queue<string> codeQueue = new Queue<string>();

        public static void HostGame(Game g)
        {
            Begin(g, host: true);
            WebRtcInterop.Host(DebugFlags.NetSignal);
            Phase = LobbyPhase.Contacting;
        }

        public static void JoinGame(Game g)
        {
            Begin(g, host: false);
            WebRtcInterop.PromptCode();
            Phase = LobbyPhase.Prompting;
        }

        // Join a specific room code directly (card 2001fbd8: the game browser picked it),
        // skipping the code-entry overlay. Identical to the overlay path once a code is in
        // hand -- Contacting -> Connecting -> Connected -> the client waits for the host's
        // EvLaunch. The host is mid-level, so this becomes join-in-progress on the host side.
        public static void JoinWithCode(Game g, string code)
        {
            Begin(g, host: false);
            RoomCode = code;
            WebRtcInterop.Join(DebugFlags.NetSignal, code);
            Phase = LobbyPhase.Contacting;
        }

        private static void Begin(Game g, bool host)
        {
            game = g;
            weHost = host;
            RoomCode = "";
            FailText = "";
            phaseQueue.Clear();
            codeQueue.Clear();
            if (!subscribed)
            {
                subscribed = true;
                WebRtcInterop.OnPhase += (p, d) => phaseQueue.Enqueue((p, d));
                WebRtcInterop.OnCodeEntry += c => codeQueue.Enqueue(c);
            }
        }

        // Back out of whatever phase we're in (menu Cancel/Back). Also ends a session that
        // already started (client cancelling on the "host is choosing" screen).
        public static void Cancel()
        {
            WebRtcInterop.ClosePrompt();
            if (NetSession.Active)
            {
                NetSession.Stop("lobby cancelled");
            }
            else
            {
                WebRtcInterop.Close();
            }
            Phase = LobbyPhase.Idle;
            RoomCode = "";
        }

        // Drained on the game tick (MenuScene.Update). Keeps running after Connected so a
        // pre-launch drop still lands here if the session didn't catch it first.
        public static void Tick()
        {
            while (codeQueue.Count > 0)
            {
                string code = codeQueue.Dequeue();
                if (Phase != LobbyPhase.Prompting)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(code))
                {
                    Phase = LobbyPhase.Idle; // overlay cancelled
                }
                else
                {
                    RoomCode = code;
                    WebRtcInterop.Join(DebugFlags.NetSignal, code);
                    Phase = LobbyPhase.Contacting;
                }
            }
            while (phaseQueue.Count > 0)
            {
                (string p, string detail) = phaseQueue.Dequeue();
                // eaRtc's phase channel is shared with the in-level game LISTING flow
                // (card 2001fbd8: NetListing drives eaRtc.list on the same OnPhase). When
                // this lobby isn't in an active flow, any phase it sees belongs to the
                // listing (or is stale) -- discard it so a listed game's 'connected' can't
                // start a spurious menu session here. NetListing owns those.
                if (Phase == LobbyPhase.Idle)
                {
                    continue;
                }
                switch (p)
                {
                case "code":
                    RoomCode = detail;
                    Phase = LobbyPhase.Hosting;
                    break;
                case "peer":
                    Phase = LobbyPhase.Connecting;
                    break;
                case "connected":
                    if (Phase != LobbyPhase.Connected)
                    {
                        Phase = LobbyPhase.Connected;
                        NetSession.StartMenuSession(game, weHost, new WebRtcTransport(attachOnly: true), RoomCode);
                    }
                    break;
                case "failed":
                case "closed":
                    // Post-session drops are the session's job (transport OnPeerBye); the
                    // lobby only owns failures before the session exists.
                    if (!NetSession.Active && Phase != LobbyPhase.Idle && Phase != LobbyPhase.Failed)
                    {
                        FailText = FailureText(detail);
                        Phase = LobbyPhase.Failed;
                    }
                    break;
                }
            }
        }

        // A session that started from this lobby ended (peer left, reject, cancel) --
        // called by NetSession.Stop so the menu flow resets cleanly.
        internal static void OnSessionEnded()
        {
            Phase = LobbyPhase.Idle;
            RoomCode = "";
        }

        private static string FailureText(string reason)
        {
            switch (reason)
            {
            case "nocode":
                return "ROOM NOT FOUND\nCheck the code and try again";
            case "full":
                return "That game is already full";
            case "expired":
                return "The room expired\nHost again for a fresh code";
            case "busy":
                return "The server is busy\nTry again in a minute";
            case "signal":
                return "Could not reach the server";
            case "gone":
                return "The other player left";
            case "ice":
            case "timeout":
                return "Could not connect to the other player\nOne of the networks is too restrictive";
            default:
                return "Connection failed (" + reason + ")";
            }
        }
    }
}
