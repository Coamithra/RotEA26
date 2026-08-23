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
            // Card 0257f8ba: the room holds up to four MACHINES (the session layer has been
            // N-peer since card 87242257), so friends keep arriving on the same code until
            // every seat is taken -- launch is no longer implicit on the first pairing.
            WebRtcInterop.Host(DebugFlags.NetSignal, EvilAliens.Oracle.MaxPlayers);
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
                // Only enqueue while this lobby is in an active flow. eaRtc's OnPhase is shared
                // with the in-level game-listing flow (card 2001fbd8: NetListing drives eaRtc
                // on the same channel), so a listed game's phases arrive here while Phase==Idle;
                // dropping them at enqueue keeps the queue from growing between lobby visits and
                // stops a listed game's 'connected' from ever starting a spurious menu session.
                WebRtcInterop.OnPhase += (p, d) =>
                {
                    if (Phase != LobbyPhase.Idle)
                    {
                        phaseQueue.Enqueue((p, d));
                    }
                };
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
                // Belt-and-braces alongside the enqueue gate above: a phase enqueued during an
                // active flow that then went Idle (Cancel) -- or any phase while a session is
                // already up (the transport owns byes then) -- is not this lobby's to act on.
                if (Phase == LobbyPhase.Idle || NetSession.Active)
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

        // ---- lobby roster text (card 0257f8ba) ------------------------------------------

        // PURE, deliberately -- the panels these feed are ordinary re-texted ConfirmationMenus,
        // so the only thing that can rot silently is the TEXT decision itself: which seat reads
        // "you", whether an open seat is named, when the host is told it can start. logic_probe's
        // ProbeLobbyText sweeps these with no Game, no session and no browser; MenuScene only
        // passes live values in (the host derives the mask from its own channels, a client reads
        // the EvLobbyRoster beat).

        // One line per roster seat. `mask` bit i = oracle slot i is taken (the host is always
        // slot 0 -- NetSession.HostPrimarySlot); `youSlot` is the reader's own seat.
        internal static string RosterLines(int mask, int youSlot)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < EvilAliens.Oracle.MaxPlayers; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }
                sb.Append("Player ").Append(i + 1).Append(":  ");
                if ((mask & (1 << i)) == 0)
                {
                    sb.Append("open");
                }
                else if (i == youSlot)
                {
                    sb.Append(i == 0 ? "you (host)" : "you");
                }
                else
                {
                    sb.Append(i == 0 ? "host" : "joined");
                }
            }
            return sb.ToString();
        }

        internal static int CountSeats(int mask)
        {
            int n = 0;
            for (int i = 0; i < EvilAliens.Oracle.MaxPlayers; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    n++;
                }
            }
            return n;
        }

        // The HOST's lobby panel: room code + live roster + what to do next. The panel's two
        // entries (Start Game / Cancel) are MenuScene's; this is only the message above them.
        internal static string HostLobbyText(string code, int mask)
        {
            return "Room code:  " + code + "\n\n"
                + RosterLines(mask, youSlot: 0) + "\n\n"
                + (CountSeats(mask) >= 2
                    ? "Start when your crew is aboard!"
                    : "Tell your friends!\nWaiting for players to join...");
        }

        // The JOIN side's waiting panel: who else is already in. `mask` < 0 means the roster
        // beat has not arrived yet (a lost beat degrades to the pre-card text, never to a lie).
        // Slot 0 is always the HOST's seat, so a client whose own grant has not settled (its
        // LocalPrimarySlot still reads the default 0) must not be marked "you" there.
        internal static string ClientLobbyText(int mask, int youSlot)
        {
            if (mask < 0)
            {
                return "Connected!\nThe host is choosing a mission...";
            }
            if (youSlot <= 0 || youSlot >= EvilAliens.Oracle.MaxPlayers)
            {
                youSlot = -1;
            }
            return "Connected!\n\n" + RosterLines(mask, youSlot)
                + "\n\nWaiting for the host to start...";
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
