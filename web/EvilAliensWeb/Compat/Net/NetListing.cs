using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Host-side public-game listing (card 2001fbd8). While an eligible single-player game is
    // running, this keeps ONE lightweight signaling WebSocket open (via eaRtc, reusing the
    // 11.4 host machinery) so strangers can find it in the browser and join it -- WITHOUT
    // constructing a NetSession. Listing is pure metadata on the server; the replication
    // layer only spins up when a stranger actually arrives (join-in-progress).
    //
    // The eligibility predicate here is the SINGLE source of truth: it drives the listing,
    // the ScoreVisualiser room-code beacon, and the pause-menu "Listed online" indicator, so
    // they can never disagree. Listable == ANY empty player slot (oracle.Players <
    // Oracle.MaxPlayers -- card 4d904410 relaxed this from "exactly one player" once couch
    // players could coexist with an online peer) + the setting on + no cheats/debug flags + a
    // net-eligible level + no session already up.
    //
    // On pairing (a browser joins our code -> eaRtc drives the host handshake -> the "connected"
    // phase), NetSession.StartListedSession attaches a real host session to the running level.
    //
    // Ticked once per game tick from Game1.UpdateInner (right after NetSession.Update); a plain
    // menu/attract boot has no GameScene up, so it is a single early return there.
    public static class NetListing
    {
        // The room is currently advertised in the browser (list sent, code in hand, not
        // unlisted). Read by the beacon + pause indicator.
        public static bool Listed { get; private set; }

        // The room code (set on the 'code' phase). Kept while a room exists even if
        // transiently unlisted, so a re-list reuses the same code.
        public static string RoomCode { get; private set; } = "";

        // Whether the running game *could* be listed right now (setting + slot + level +
        // no cheats). The pause indicator distinguishes "listed" from "not listed" with this.
        public static bool Eligible { get; private set; }

        // Eligible EXCEPT for the Settings.AllowOnlineJoins switch -- i.e. "this game is one
        // toggle away from being joinable". Card 0d6ffe70: the pause menu's Online Play entry
        // needs to offer the room toggle precisely when flipping it would change something, and
        // Eligible cannot answer that (it is already false whenever the setting is off, so it
        // cannot tell "the player closed the room" from "this level can never be listed").
        public static bool CouldList { get; private set; }

        private static Game game;
        private static bool subscribed;
        private static readonly Queue<(string phase, string detail)> phaseQueue = new Queue<(string, string)>();

        private static bool open;            // we have an outstanding listing WS (opening/open)
        private static bool waitingForCode;  // List() sent, room code not yet received
        private static int curLevel = -1, curDiff = -1, curPlayers = -1;
        private static long retryAfter;      // backoff so a down server isn't hammered each tick
        private const long RetryBackoffMs = 5000;

        private static long NowMs => Environment.TickCount64;

        public static void Tick(Game g)
        {
            game = g;
            // ?netfakelisted=<code>: pretend to be listed, with no socket and no server (card
            // d1a0559b). Placed first so nothing below can open a listing behind it; the two
            // consumers of Listed/RoomCode (the pause line, the corner beacon) then render
            // exactly as they do for a real listing, which is the whole point of the rig.
            if (!string.IsNullOrEmpty(DebugFlags.NetFakeListed))
            {
                // Eligible is deliberately left alone. It answers "could the RUNNING GAME be
                // listed", which has no meaning here (there may be no GameScene at all), and
                // faking it would be a claim about a predicate this flag bypasses.
                //
                // CouldList and the AllowOnlineJoins test on Listed, however, are the point of
                // the flag since card 0d6ffe70: the fake listing is the only offline way to
                // reach the pause menu's room toggle, and a Listed pinned to true would make
                // that toggle a dead control in the one rig that can drive it. So the fake
                // room obeys the setting exactly as a real one does -- close it and the pause
                // line + corner beacon go away; re-open it and the same code comes back.
                CouldList = true;
                Listed = Settings.GetInstance().AllowOnlineJoins;
                RoomCode = DebugFlags.NetFakeListed;
                return;
            }
            if (!subscribed)
            {
                subscribed = true;
                WebRtcInterop.OnPhase += (p, d) => phaseQueue.Enqueue((p, d));
            }
            DrainPhases();

            GameScene scene = GameScene.NetActiveScene;
            CouldList = ComputeEligibleIgnoringSetting(scene);
            Eligible = CouldList && Settings.GetInstance().AllowOnlineJoins;

            if (Eligible)
            {
                int lvl = (int)scene.Level;
                int diff = (int)Settings.GetInstance().CurrentDifficulty;
                int players = Players();
                if (!open)
                {
                    if (NowMs < retryAfter)
                    {
                        return; // still backing off from a failed attempt
                    }
                    open = true;
                    waitingForCode = true;
                    Listed = false;
                    RoomCode = "";
                    curLevel = lvl; curDiff = diff; curPlayers = players;
                    WebRtcInterop.List(DebugFlags.NetSignal, lvl, diff, players, NetSession.ProtocolVersion);
                }
                else if (!waitingForCode && !Listed && RoomCode != "")
                {
                    // Was unlisted while transiently ineligible; re-advertise the same code.
                    WebRtcInterop.Relist(lvl, diff, players);
                    Listed = true;
                    curLevel = lvl; curDiff = diff; curPlayers = players;
                }
                else if (Listed && (lvl != curLevel || diff != curDiff || players != curPlayers))
                {
                    WebRtcInterop.Relist(lvl, diff, players);
                    curLevel = lvl; curDiff = diff; curPlayers = players;
                }
            }
            else if (open)
            {
                if (scene == null || NetSession.Active)
                {
                    // Level exited, or a session took over: drop the room entirely.
                    WebRtcInterop.EndListing();
                    ResetListing();
                }
                else if (Listed)
                {
                    // In-level but no longer eligible (the roster filled up, the option was
                    // turned off, a cheat was enabled): hide it but keep the code + beat so
                    // re-eligibility re-lists the same room.
                    WebRtcInterop.Unlist();
                    Listed = false;
                }
            }
        }

        // The LEVEL half of the eligibility predicate, split out of ComputeEligible so it can be
        // verified as data: it is pure (no ServiceHelper, no GameScene, no clock), which is what
        // lets tools/sim/logic_probe sweep the whole Levels enum through the REAL method
        // (ProbeListingLevels). The rest of ComputeEligible reaches the live world and cannot go
        // there. Keep it pure.
        //
        // Tutorial is refused since card df8f1ef7: it is a scripted teaching level whose whole
        // point is walking ONE player through the controls, so advertising it to strangers in the
        // public browser is never what a player meant. This is about the PUBLIC LISTING only --
        // it does not stop a host deliberately picking the tutorial for a join-by-code game.
        internal static bool IsNetEligibleLevel(Levels lvl)
        {
            if (lvl == Levels.WebcamAliens || lvl == Levels.TeamChallenge)
            {
                return false;                              // camera-is-the-controller / needs two seats
            }
            if (lvl == Levels.Demo1 || lvl == Levels.Demo2 || lvl == Levels.Demo3)
            {
                return false;                              // the idle attract demo is an AI playthrough
            }
            if (lvl == Levels.Tutorial)
            {
                return false;                              // a solo scripted walkthrough (card df8f1ef7)
            }
            return true;
        }

        // Single eligibility predicate, MINUS the AllowOnlineJoins switch -- Tick ANDs that in
        // one line above, and CouldList is this half on its own (card 0d6ffe70). Splitting it
        // this way rather than adding a second predicate keeps the one-source-of-truth property
        // the header claims: there is still exactly one place that decides what is listable.
        // Excludes scene==null and an active session up front, so the caller can use it verbatim.
        private static bool ComputeEligibleIgnoringSetting(GameScene scene)
        {
            if (scene == null)
            {
                return false;                              // only while a level is up
            }
            if (NetSession.Active)
            {
                return false;                              // already in a session (JIP done / lobby / URL)
            }
            if (!IsNetEligibleLevel(scene.Level))
            {
                return false;
            }
            // A cheating or debug-flagged host would change the joiner's game (Turbo is forced
            // to 100 in a session; cheats alter the shared run), and Friends>0 means mechanical
            // friend ships that aren't replicated -- CheckForCheats() covers Friends>0 + Turbo.
            // ?netjip bypasses this so a ?level= JIP-test host (DebugFlags.Active) can still list.
            if (!DebugFlags.NetJip)
            {
                if (DebugFlags.Active || Settings.GetInstance().CheckForCheats())
                {
                    return false;
                }
            }
            // Any free seat will do (card 4d904410). This used to demand exactly one player
            // because the roster was hard-wired to one local ship per peer; now the host
            // allocates slots, so a couch game can advertise its spare seat too. The browser's
            // players column consequently varies 1..3 instead of always reading 1.
            return Players() < Oracle.MaxPlayers;
        }

        private static int Players()
        {
            IOracleService svc = ServiceHelper.Get<IOracleService>();
            return svc?.Oracle?.Players ?? 0;
        }

        private static void DrainPhases()
        {
            while (phaseQueue.Count > 0)
            {
                (string p, string detail) = phaseQueue.Dequeue();
                if (!open)
                {
                    continue; // not our listing (a NetLobby menu-flow phase) -- ignore
                }
                switch (p)
                {
                case "code":
                    RoomCode = detail;
                    waitingForCode = false;
                    Listed = true; // JS advertises (sends list) the moment it has the code
                    break;
                case "connected":
                    // A stranger paired with our listed game. The signaling WS has closed
                    // (channels up); start the host session attached to the running level.
                    if (!NetSession.Active && game != null)
                    {
                        open = false;
                        Listed = false;
                        waitingForCode = false;
                        NetSession.StartListedSession(game, new WebRtcTransport(attachOnly: true), RoomCode);
                    }
                    break;
                case "failed":
                case "closed":
                    // The listing socket died before anyone joined; back off, then re-list.
                    ResetListing();
                    retryAfter = NowMs + RetryBackoffMs;
                    break;
                }
            }
        }

        private static void ResetListing()
        {
            open = false;
            waitingForCode = false;
            Listed = false;
            RoomCode = "";
            curLevel = curDiff = curPlayers = -1;
        }
    }
}
