using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // A LEVEL THAT PLAYS ITSELF OUT KEEPS A MENU-LOBBY PAIRING ALIVE -- WON (card 3b6c12e7) OR
    // LOST (card c600c55a) -- and the remote ship leaves in the SAME DIRECTION as every local
    // one (card b4a9fe60).
    //
    // FOUR ARMS, TWO CHECKS. The victory half drives EvVictory; the defeat half drives
    // EvReset(ResetModeGameOver) on the client and a real lives-exhausted death on the host. The
    // legs that both halves share -- the scene terminated, the pairing survived, NO EvLeave went
    // out, the per-match state was dropped -- live in CheckLevelEndSurvival so the two cannot
    // drift: the whole claim of card c600c55a is that losing ends exactly the way winning does.
    //
    // Run it in a throwaway `?level=Level2&invuln&netallowdebug&noattract` boot -- Level 2 because
    // it is the one shipped level with a non-default `spawnType` (West), which is what makes the
    // direction legs discriminate at all, and `?netallowdebug` because this is a real MENU
    // session and `?level=` sets DebugFlags.Active, which such a session otherwise refuses.
    //
    // DESTRUCTIVE, like eaNetSceneOrder and eaNetResetSpawn -- more so: it drives the level's real
    // victory choreography to its end, so the scene TERMINATES and the run lands back at the menus.
    //
    // TWO PHASES, because the subject is a SEVEN-SECOND real-time sequence. `Arm()` pairs a real
    // session onto the live level and applies a real EvVictory; the caller then steps the game
    // ~7.5 s so GameScene.UpdateWin reaches its own Terminate; `Check()` reads the result. Faking
    // the wait (calling Terminate by hand, or latching the flag directly) would assert about the
    // rig -- the whole claim is that the REAL victory path leaves the session standing.
    //
    // WHAT DISCRIMINATES. "The session is still up" passes trivially on a build that never got as
    // far as terminating the scene, so every leg has its precondition asserted beside it: the
    // scene really went down, the pairing really was live first, and -- the leg that fails on the
    // pre-card build -- NO EvLeave reached the scripted peer. Pre-card, the scene-down edge sent
    // one and called Stop("match ended").
    //
    // The clock is pinned for the whole run (PinnedNetHost) so none of the session's real-time
    // deadlines can fire across the seven seconds of game time the caller steps: the drop verdict
    // lands at PeerTimeoutMs + PeerGraceMs = 8 s of silence, which a scripted peer that stops
    // sending would otherwise be inside by the end of the wait.
    internal static class NetLevelEndTest
    {
        private const string Room = "levelend";
        private const byte GrantedSlot = 1;
        private const ulong PeerToken = 0x1E7E1E4DUL;

        // Held ACROSS the caller's step, which is why they are static: the peer endpoint's inbound
        // queue is where the EvLeave would land, and the pinned clock must not be handed back
        // until Check() has read it.
        private static NetWire wire;
        private static InMemoryTransport peer;
        private static INetHost hostBefore;
        private static bool armed;
        // WHICH arm ran. The two phase-2 entry points read DIFFERENT captured state (Check reads
        // the direction samples, MenuCheck reads none of them), so without this a Check() after
        // an ArmHost() sails past the `armed` guard and reports FAILs about a puppet that was
        // never spawned -- garbage where a SKIP belongs.
        private static bool armedHost;
        // WHICH ENDING the arm set up. Same reason as armedHost: Check() reads the direction
        // samples a defeat arm never takes (a defeat purges every ship instead of flying it off),
        // so without this a Check() after an ArmDefeat() would report FAILs about a fly-off that
        // was never sampled.
        private static bool armedDefeat;
        // WHICH host arm ran (card 51566427). The listed arm reuses MenuCheck -- the menu facts a
        // lobby return has to satisfy are the same whichever session kind reached it -- but it
        // adds legs no menu-session arm needs: that no EvLeave went out, and that the session
        // CONVERTED to a menu-lobby one. Without this flag those would either be missing from the
        // listed run or asserted about menu-session arms that never claimed them.
        private static bool armedListed;
        private static bool armedPaired;
        private static bool armedSceneUp;
        private static float armedLocalDir;
        private static float armedSceneDir;
        private static float armedPuppetDir;
        private static bool armedHadPuppet;

        public static string Arm()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[netlevelend] phase 1 -- arm (card 3b6c12e7 / b4a9fe60)\n");

            if (armed)
            {
                sb.Append("  SKIP (already armed -- step the game, then run the check phase)\n");
                return sb.ToString();
            }
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot"
                    + " ?level=Level2&invuln&netallowdebug&noattract and run it there)\n");
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up -- this suite would tear it down)\n");
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;

            hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            armed = true;
            armedHost = false;
            armedDefeat = false;
            try
            {
                wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                peer = wire[1];
                ushort seq = 1;

                // A real MENU session -- the whole subject. StartForTest's asMenuSession inherits
                // HandleHello's debug refusal, so the pairing assertion below is what tells a
                // missing ?netallowdebug from a broken build.
                NetSession.StartForTest(game, host: false, ours, Room, asMenuSession: true);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                    NetSession.LocalBuildHash, 0, GrantedSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                armedPaired = NetSession.IsClient && NetSession.PeerUp;
                sb.Append("  paired=").Append(armedPaired ? "yes" : "NO").Append('\n');

                // The scene's own answer, and the local ship's, sampled BEFORE the victory --
                // Terminate purges every ship, so after the wait there is nothing to compare to.
                armedSceneUp = true;
                armedSceneDir = NetScene.Current.PlayerSpawnDirection;
                PlayerShip local = FindAnyLocalShip(oracle);
                armedLocalDir = local != null ? local.NetStartDirection : float.NaN;

                // A real remote ship puppet, built by the real SpawnPuppet off a scripted ship
                // stream -- the site that used to hard-code South.
                for (int i = 0; i < 3 && !NetSession.HasRemotePuppet; i++)
                {
                    peer.SendStream(ScriptedShipState(300f + i * 4f, 300f));
                    wire.Pump();
                    NetSession.Update();
                }
                armedHadPuppet = NetSession.HasRemotePuppet;
                PlayerShip pup = oracle.GetPlayerShip(oracle.GetPlayerIndex(ControlDevice.Remote));
                armedPuppetDir = pup != null ? pup.NetStartDirection : float.NaN;
                sb.Append("  sceneDir=").Append(F(armedSceneDir))
                  .Append(" localDir=").Append(F(armedLocalDir))
                  .Append(" puppetDir=").Append(F(armedPuppetDir))
                  .Append(" puppet=").Append(armedHadPuppet ? "yes" : "no").Append('\n');

                // The real host-broadcast victory. Both peers run their own Victory() from it, so
                // this is exactly what a client sees at the end of a co-op level.
                peer.SendReliable(NetProtocol.EncodeEmptyEvent(seq++, NetProtocol.EvVictory));
                wire.Pump();
                NetSession.Update();
                sb.Append("  EvVictory applied -- step the game ~450 frames, then run the check\n");
            }
            catch (Exception ex)
            {
                sb.Append("  FAIL the arm phase ran (").Append(Describe(ex)).Append(")\n");
                sb.Append(Frames(ex));
            }
            return sb.ToString();
        }

        // PHASE 1', THE HOST HALF. The client suite above cannot reach it: EnterNetLobby shows
        // the HOST netPickMenu and the CLIENT status panel, and only the host's row is the thing
        // the card is for ("so the host can select the next level"). So this arms a real HOST
        // session and injects NO victory -- the level's own `?win` script produces one, which is
        // the genuine article rather than a wire beat, and is the only route to a host victory a
        // rig has. Pair it with ArmedHost/MenuCheck below.
        public static string ArmHost()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[netlevelend] phase 1' -- arm (host) (card 3b6c12e7)\n");

            if (armed)
            {
                sb.Append("  SKIP (already armed)\n");
                return sb.ToString();
            }
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot"
                    + " ?level=Level2&invuln&netallowdebug&noattract&win and run it there)\n");
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up)\n");
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            armed = true;
            armedHost = true;
            armedDefeat = false;
            try
            {
                wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                peer = wire[1];

                NetSession.StartForTest(game, host: true, ours, Room, asMenuSession: true);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                armedPaired = NetSession.IsHost && NetSession.PeerUp;
                sb.Append("  paired=").Append(armedPaired ? "yes" : "NO")
                  .Append(" -- now let the level's own ?win script reach Victory, then step past"
                      + " the credits crawl and run the menu check\n");
            }
            catch (Exception ex)
            {
                sb.Append("  FAIL the arm phase ran (").Append(Describe(ex)).Append(")\n");
                sb.Append(Frames(ex));
            }
            return sb.ToString();
        }

        // PHASE 1'''', THE JOIN-IN-PROGRESS HALF (card 51566427). ArmHost()'s twin with ONE flag
        // changed, and that flag is the whole card: a LISTED session -- a stranger who found our
        // single-player game in the browser and joined it mid-level -- used to be excluded from
        // the survival branch, so finishing the level together tore the match down and ejected
        // them while the host was still sitting right there.
        //
        // A HOST arm because `listedSession` only ever exists host-side (StartListedSession never
        // builds a client), and so, like ArmHost, its victory has to come from the level's own
        // ?win script rather than an injected beat.
        //
        // NO ?netallowdebug NEEDED, unlike every menu-session arm here: HandleHello's debug
        // refusal is menuSession-only. That asymmetry is production's own -- a debug-flagged game
        // is stopped from being LISTED by NetListing's eligibility predicate, not from pairing
        // once it has been -- so the arm pairs under a bare ?level= exactly as the real thing
        // would. The pairing is still asserted, so a build that changed that reads as a FAIL.
        public static string ArmListed()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[netlevelendjip] phase 1 -- arm (listed / join-in-progress host)"
                + " (card 51566427)\n");

            if (armed)
            {
                sb.Append("  SKIP (already armed)\n");
                return sb.ToString();
            }
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot"
                    + " ?level=Level2&invuln&noattract&win and run it there)\n");
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up)\n");
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            armed = true;
            armedHost = true;
            armedListed = true;
            armedDefeat = false;
            try
            {
                wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                peer = wire[1];

                NetSession.StartForTest(game, host: true, ours, Room,
                    asMenuSession: false, asListedSession: true);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                armedPaired = NetSession.IsHost && NetSession.PeerUp;
                // THE KIND IS ASSERTED AT BOTH ENDS OF THE RUN: here, so a StartForTest that
                // quietly dropped the opt-in cannot turn the whole probe into a menu-session
                // re-run wearing this card's name; and in MenuCheck, so the conversion the fix
                // performs is seen to happen rather than inferred.
                sb.Append("  paired=").Append(armedPaired ? "yes" : "NO")
                  .Append(" listed=").Append(NetSession.IsListedSession ? "yes" : "NO")
                  .Append(" menu=").Append(NetSession.IsMenuSession ? "yes" : "no")
                  .Append(" -- now let the level's own ?win script reach Victory, then step past"
                      + " the credits crawl and run the menu check\n");
                if (!NetSession.IsListedSession || NetSession.IsMenuSession)
                {
                    sb.Append("  FAIL this session is not a LISTED one, so the arm proves"
                        + " nothing about the card\n");
                }
            }
            catch (Exception ex)
            {
                sb.Append("  FAIL the arm phase ran (").Append(Describe(ex)).Append(")\n");
                sb.Append(Frames(ex));
            }
            return sb.ToString();
        }

        // PHASE 1'', THE DEFEAT HALF, CLIENT SIDE (card c600c55a). Same shape as Arm() and for
        // the same reason -- only a CLIENT applies a host's end-of-level broadcast off the wire
        // -- but the broadcast is EvReset(ResetModeGameOver) instead of EvVictory. That is what
        // puts GameScene into GameState.GameOver, and so through the real Mission Failed
        // choreography (UpdateGameOver's 4 s wait, then the AnimatedMessage.defeat 1.5/6/3 s
        // states) to Defeat() -> Terminate(FinishedMode.lostlevel).
        //
        // NO DIRECTION LEGS, unlike Arm(): card b4a9fe60's fly-off is a `hasWon` behaviour and a
        // defeat purges every ship instead, so nothing here samples one and CheckDefeat asserts
        // none. A real puppet IS built first, though -- it is what the per-match-state leg reads.
        public static string ArmDefeat()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[netlevellost] phase 1 -- arm (card c600c55a)\n");

            if (armed)
            {
                sb.Append("  SKIP (already armed -- step the game, then run the check phase)\n");
                return sb.ToString();
            }
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot"
                    + " ?level=Level2&invuln&netallowdebug&noattract and run it there)\n");
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up -- this suite would tear it down)\n");
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            armed = true;
            armedHost = false;
            armedDefeat = true;
            try
            {
                wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                peer = wire[1];
                ushort seq = 1;

                NetSession.StartForTest(game, host: false, ours, Room, asMenuSession: true);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                    NetSession.LocalBuildHash, 0, GrantedSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                armedPaired = NetSession.IsClient && NetSession.PeerUp;
                sb.Append("  paired=").Append(armedPaired ? "yes" : "NO").Append('\n');

                for (int i = 0; i < 3 && !NetSession.HasRemotePuppet; i++)
                {
                    peer.SendStream(ScriptedShipState(300f + i * 4f, 300f));
                    wire.Pump();
                    NetSession.Update();
                }
                armedHadPuppet = NetSession.HasRemotePuppet;
                armedSceneUp = true;
                sb.Append("  puppet=").Append(armedHadPuppet ? "yes" : "no").Append('\n');

                // The real host-broadcast game over. A client never decides one itself
                // (GameScene.LoseLife returns early on IsClient), so this is exactly what the
                // far end of a co-op Mission Failed sees.
                peer.SendReliable(NetProtocol.EncodeByteEvent(seq++, NetProtocol.EvReset,
                    NetSession.ResetModeGameOver));
                wire.Pump();
                NetSession.Update();
                sb.Append("  EvReset(GameOver) applied -- step the game ~900 frames, then run"
                    + " the check\n");
            }
            catch (Exception ex)
            {
                sb.Append("  FAIL the arm phase ran (").Append(Describe(ex)).Append(")\n");
                sb.Append(Frames(ex));
            }
            return sb.ToString();
        }

        // PHASE 1''', THE DEFEAT HALF, HOST SIDE (card c600c55a). Pairs with MenuCheck() below,
        // exactly as ArmHost() does -- the menu facts a lobby return has to satisfy are the same
        // whether the level was won or lost, so that check is reused rather than copied.
        //
        // WHY IT SETS THREE FIELDS AND KILLS THE SHIPS. There is no `?lose` counterpart to the
        // `?win` script flag, and the host's game over is produced by the level itself:
        // UpdateNormal -> AllShipsDead -> LoseLife -> the lives-exhausted branch. So the arm puts
        // the level in the state a Hard+ run reaches on its last life (Lives 0, no InfiniteLives,
        // no DirectRespawn -- Easy's respawn-in-place branch returns from LoseLife before the
        // game over) and then asplodes every locally-owned ship through the real Asplode()->Die()
        // path, which is what eaKillShips does. Everything after that -- the broadcast, the
        // choreography, Terminate -- is the game's own code.
        public static string ArmDefeatHost()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[netlevellost] phase 1' -- arm (host) (card c600c55a)\n");

            if (armed)
            {
                sb.Append("  SKIP (already armed)\n");
                return sb.ToString();
            }
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot"
                    + " ?level=Level2&invuln&netallowdebug&noattract and run it there)\n");
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up)\n");
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            armed = true;
            armedHost = true;
            armedDefeat = true;
            try
            {
                wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                peer = wire[1];

                NetSession.StartForTest(game, host: true, ours, Room, asMenuSession: true);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                armedPaired = NetSession.IsHost && NetSession.PeerUp;
                sb.Append("  paired=").Append(armedPaired ? "yes" : "NO").Append('\n');

                Settings settings = Settings.GetInstance();
                settings.InfiniteLives = false;
                settings.DirectRespawn = false;
                ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
                score.Lives = 0;

                int killed = 0;
                List<PlayerShip> targets = new List<PlayerShip>();
                Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
                foreach (PlayerShip s in oracle.GetShips())
                {
                    ControlDevice d = oracle.Controller(s.Owner);
                    if (d != ControlDevice.Remote && d != ControlDevice.RemoteFriend)
                    {
                        targets.Add(s);
                    }
                }
                // Collected first: Asplode() adds Explosions and bin adds are instant, so
                // killing inside the enumeration would mutate the collection mid-foreach.
                foreach (PlayerShip s in targets)
                {
                    s.Asplode();
                    killed++;
                }
                sb.Append("  lives=0 shipsKilled=").Append(killed)
                  .Append(" -- now let the level's own LoseLife reach GameOver, step ~900 frames"
                      + " for the Mission Failed choreography, then run the menu check\n");
                if (killed == 0)
                {
                    sb.Append("  FAIL no locally-owned ship to kill -- the level never spawned"
                        + " one, so no game over can follow\n");
                }
            }
            catch (Exception ex)
            {
                sb.Append("  FAIL the arm phase ran (").Append(Describe(ex)).Append(")\n");
                sb.Append(Frames(ex));
            }
            return sb.ToString();
        }

        // PHASE 2', THE LOBBY. Asserted on the MENU CENSUS rather than a screenshot, for card
        // 72143c11's reason: NetStatusMenu draws its own 50% darken at DrawOrder 2000, so a menu
        // still live underneath the lobby merely looks dim while eating every keypress -- and
        // MenuSub1 has no modality at all, so two live menus move two selections per arrow.
        public static string MenuCheck()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check1(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netlevelendmenu] phase 2' -- the host is in its lobby"
                + (armedListed ? " (card 51566427)" : " (cards 3b6c12e7 / c600c55a)") + "\n");
            if (!armed || !armedHost)
            {
                sb.Append(armed
                    ? "  SKIP (the CLIENT arm phase is up -- its phase 2 is the session check)\n"
                    : "  SKIP (not armed -- run the host arm phase first)\n");
                sb.Append(Tally2(pass, fail));
                return sb.ToString();
            }

            try
            {
                Check1("PRECONDITION the scripted peer paired", armedPaired);
                Check1("PRECONDITION the level TERMINATED (NetScene.Current="
                    + (NetScene.Current == null ? "null" : "still up") + ")", NetScene.Current == null);
                Check1("the session outlived the level", NetSession.Active);
                Check1("... and the peer is still up", NetSession.PeerUp);

                if (armedListed)
                {
                    // THE DISCRIMINATOR FOR CARD 51566427, and the reason the two legs above are
                    // not enough on their own: pre-card the scene-down edge sent an EvLeave and
                    // THEN stopped, so a build that merely forgot the Stop would still eject the
                    // joiner from its own side while both legs above passed. Same shape as
                    // CheckLevelEndSurvival's -- drained straight off the peer's queue, since
                    // dispatch is inline on the send and wire.Pump() would deliver into nothing.
                    List<byte> types = DrainEventTypes(peer);
                    Check1("NO EvLeave was sent to the joiner (event types seen: ["
                        + string.Join(",", types) + "])", !types.Contains(NetProtocol.EvLeave));
                    // ...and that absence means something. A listed host's PeerConnected sends
                    // the joiner an EvLaunch into its running level, so this wire has carried
                    // real addressed traffic -- an assertion that nothing arrived would otherwise
                    // pass just as well on a wire that was never connected at all.
                    Check1("... on a wire that was genuinely carrying our traffic (joiner"
                        + " received " + peer.RxDelivered + " packets)", peer.RxDelivered > 0);
                    // The conversion itself. It has no behavioural observable until a peer
                    // departs at the MENUS -- one step past what this probe can drive -- and it
                    // is what keeps ReleaseDepartedPeer's tail from throwing this host to the
                    // main menu with a notice the moment the joiner disconnects from the lobby.
                    Check1("the listed session became a menu-LOBBY one (menu="
                        + (NetSession.IsMenuSession ? "yes" : "NO") + " listed="
                        + (NetSession.IsListedSession ? "STILL" : "no") + ")",
                        NetSession.IsMenuSession && !NetSession.IsListedSession);
                }

                List<MenuSub1> live = ServiceHelper.Get<IComponentBinService>()
                    .ComponentBin.InCollection<MenuSub1>();
                List<string> names = new List<string>();
                bool mainUp = false;
                bool pickUp = false;
                foreach (MenuSub1 m in live)
                {
                    string n = ((object)m).GetType().Name;
                    names.Add(n);
                    if (n == "MenuSubWithSkull") { mainUp = true; }
                    if (n == "MenuSub1") { pickUp = true; }
                }
                string census = "[" + string.Join(",", names) + "]";

                // PRECONDITION, and it has to be separate: everything below is about WHICH menu
                // is live, so a run that never reached the menus at all would satisfy the
                // absence assertion and nothing else.
                Check1("PRECONDITION the menus are up at all " + census, live.Count > 0);
                Check1("the host is on its LOBBY pick menu, not the main menu " + census, pickUp);
                // THE 72143c11 LEG. RemoveInstantly on a menu that is not shown is a no-op, so
                // this is the one that says EnterNetLobby really closed the main menu Initialize
                // had just re-added -- and a main menu left live under the lobby takes every
                // arrow and every Enter alongside it.
                Check1("... and the main menu is NOT still live underneath it " + census, !mainUp);
                Check1("exactly one menu is live " + census, live.Count == 1);
            }
            catch (Exception ex)
            {
                Check1("the menu check ran (" + Describe(ex) + ")", false);
                sb.Append(Frames(ex));
            }
            finally
            {
                sb.Append(" teardown\n");
                try
                {
                    NetSession.Stop("level-end lobby scenario finished");
                    Check1("the session is stopped", !NetSession.Active);
                }
                catch (Exception ex)
                {
                    Check1("teardown ran (" + Describe(ex) + ")", false);
                }
                NetHost.Current = hostBefore;
                Check1("the clock seam is handed back",
                    ReferenceEquals(NetHost.Current, hostBefore));
                wire = null;
                peer = null;
                armed = false;
                armedDefeat = false;
                armedListed = false;
            }

            sb.Append(Tally2(pass, fail));
            return sb.ToString();
        }

        private static string Tally2(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netlevelendmenu] {0} passed, {1} failed\n", pass, fail);
        }

        // PHASE 2 OF THE DEFEAT HALF (card c600c55a). Only the shared survival legs: a defeat
        // has no fly-off to assert, and the pairing/EvLeave/per-match legs ARE the card.
        public static string CheckDefeat()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check1(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netlevellost] phase 2 -- check (card c600c55a)\n");
            if (!armed || armedHost || !armedDefeat)
            {
                sb.Append(!armed
                    ? "  SKIP (not armed -- run the defeat arm phase first)\n"
                    : armedHost
                        ? "  SKIP (the HOST arm phase is up -- its phase 2 is the menu check)\n"
                        : "  SKIP (the VICTORY arm phase is up -- its phase 2 is the level-end"
                            + " check)\n");
                sb.Append(TallyLost(pass, fail));
                return sb.ToString();
            }

            try
            {
                Check1("PRECONDITION the scripted host paired", armedPaired);
                Check1("PRECONDITION a level was up when the suite armed", armedSceneUp);
                Check1("PRECONDITION a real remote puppet was built", armedHadPuppet);
                CheckLevelEndSurvival(sb, Check1, "the Mission Failed choreography");
            }
            catch (Exception ex)
            {
                Check1("the check phase ran (" + Describe(ex) + ")", false);
                sb.Append(Frames(ex));
            }
            finally
            {
                sb.Append(" 5. teardown\n");
                try
                {
                    NetSession.Stop("level-lost scenario finished");
                    Check1("the session is stopped", !NetSession.Active);
                    // No "...and the lobby return went with it" leg here, unlike Check(): the
                    // menus already SPENT that latch when Terminate handed them the scene, so
                    // asserting it is clear would pass on any build, working or not. The
                    // probe's `eval MenuNetState` line is the positive control instead.
                }
                catch (Exception ex)
                {
                    Check1("teardown ran (" + Describe(ex) + ")", false);
                }
                NetHost.Current = hostBefore;
                Check1("the clock seam is handed back",
                    ReferenceEquals(NetHost.Current, hostBefore));
                wire = null;
                peer = null;
                armed = false;
                armedDefeat = false;
            }

            sb.Append(TallyLost(pass, fail));
            return sb.ToString();
        }

        public static string Check()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check1(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netlevelend] phase 2 -- check (card 3b6c12e7 / b4a9fe60)\n");
            if (!armed || armedHost || armedDefeat)
            {
                sb.Append(!armed
                    ? "  SKIP (not armed -- run the arm phase first)\n"
                    : armedHost
                        ? "  SKIP (the HOST arm phase is up -- its phase 2 is the menu check)\n"
                        : "  SKIP (the DEFEAT arm phase is up -- its phase 2 is the defeat"
                            + " check)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            try
            {
                Check1("PRECONDITION the scripted host paired", armedPaired);
                Check1("PRECONDITION a level was up when the suite armed", armedSceneUp);

                // ---- 1. the fly-off DIRECTION (card b4a9fe60) ---------------------------------
                // Level 2 is spawnType West, i.e. 0 rad = RIGHT. The pre-card constant is South
                // (4.712389f = up), so it sits beside every leg as the negative control -- without
                // it "the puppet agrees with the scene" would also pass on a build where BOTH read
                // South, which is exactly the shipped bug on a South level.
                sb.Append(" 1. every ship leaves the way the SCENE says (card b4a9fe60)\n");
                Check1("the scene's spawn direction is not the hard-coded South this level is not"
                    + " (sceneDir=" + F(armedSceneDir) + ", pre-card constant="
                    + F(PreCardSouth) + ")", !Near(armedSceneDir, PreCardSouth));
                Check1("the LOCAL ship flies off on the scene's direction (localDir="
                    + F(armedLocalDir) + ")", Near(armedLocalDir, armedSceneDir));
                Check1("PRECONDITION a real remote puppet was built", armedHadPuppet);
                Check1("the REMOTE puppet flies off on the same one (puppetDir="
                    + F(armedPuppetDir) + ")", Near(armedPuppetDir, armedSceneDir));
                Check1("... which is the assertion the pre-card build fails -- it would read "
                    + F(PreCardSouth), !Near(armedPuppetDir, PreCardSouth));

                CheckLevelEndSurvival(sb, Check1, "the victory choreography");
                // NOT in the shared helper: it is only still READABLE here. A story level
                // returns through CreditsScene, so the menus have not initialized yet and the
                // take-once latch is untouched -- whereas a DEFEAT hands straight back to
                // Game1's OnFinished, which adds MenuScene inside Terminate, so by the time
                // CheckDefeat runs the menus have already spent it (which is the point).
                Check1("the menus have a lobby return pending", NetSession.PendingLobbyReturn);
            }
            catch (Exception ex)
            {
                Check1("the check phase ran (" + Describe(ex) + ")", false);
                sb.Append(Frames(ex));
            }
            finally
            {
                sb.Append(" 5. teardown\n");
                try
                {
                    NetSession.Stop("level-end scenario finished");
                    Check1("the session is stopped", !NetSession.Active);
                    Check1("... and the lobby return went with it -- the menus must not enter a"
                        + " lobby for a pairing that no longer exists",
                        !NetSession.PendingLobbyReturn);
                }
                catch (Exception ex)
                {
                    Check1("teardown ran (" + Describe(ex) + ")", false);
                }
                NetHost.Current = hostBefore;
                Check1("the clock seam is handed back",
                    ReferenceEquals(NetHost.Current, hostBefore));
                wire = null;
                peer = null;
                armed = false;
                armedDefeat = false;
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // GameScene.PlayerSpawnType.South, i.e. what BOTH puppet spawn sites used to hard-code.
        private const float PreCardSouth = 4.712389f;

        private static bool Near(float a, float b)
        {
            return !float.IsNaN(a) && !float.IsNaN(b) && Math.Abs(a - b) < 0.0005f;
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static PlayerShip FindAnyLocalShip(Oracle oracle)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                ControlDevice d = oracle.Controller(s.Owner);
                if (d != ControlDevice.Remote && d != ControlDevice.RemoteFriend)
                {
                    return s;
                }
            }
            return null;
        }

        // A minimal alive ship-state frame -- enough for ManagePuppet to build the puppet, which
        // is all these legs need. Position only; no shots, no bomb.
        private static ushort shipSeq = 1;

        private static byte[] ScriptedShipState(float x, float y)
        {
            return NetProtocol.EncodeShipState(0, primary: true, shipSeq++, 0u,
                new Vector2(x, y), Vector2.Zero,
                0f, alive: true, shotCount: 0, shotsPerSec: 5, bulletLife: 1000f);
        }

        // Everything the peer received since the last pump, reduced to MsgEvent type bytes.
        private static List<byte> DrainEventTypes(InMemoryTransport endpoint)
        {
            List<byte> types = new List<byte>();
            endpoint.OnData += Collect;
            endpoint.Pump(int.MaxValue);
            endpoint.OnData -= Collect;
            return types;

            void Collect(byte[] data, bool reliable, string from)
            {
                if (data != null && data.Length >= 4 && data[0] == NetProtocol.MsgEvent)
                {
                    types.Add(data[1]);
                }
            }
        }

        private static string Describe(Exception ex)
        {
            string s = ((object)ex).GetType().Name + ": " + ex.Message;
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                s += " <- " + ((object)inner).GetType().Name + ": " + inner.Message;
            }
            return s;
        }

        private static string Frames(Exception ex)
        {
            string trace = ex.StackTrace;
            if (string.IsNullOrEmpty(trace))
            {
                return "  (no stack trace)\n";
            }
            const int MaxFrames = 8;
            string[] lines = trace.Split('\n');
            StringBuilder frames = new StringBuilder();
            for (int i = 0; i < lines.Length && i < MaxFrames; i++)
            {
                frames.Append("  ").Append(lines[i].Trim()).Append('\n');
            }
            return frames.ToString();
        }

        // THE LEGS A WIN AND A LOSS SHARE, so the two halves cannot drift: the whole claim of
        // card c600c55a is that a Mission Failed ends exactly the way a victory does. Both
        // callers reach here having driven the REAL wind-down to its end; `windDown` is only the
        // label for which one it was.
        private static void CheckLevelEndSurvival(StringBuilder sb, Action<string, bool> check1,
            string windDown)
        {
            // ---- 2. the level really ended -------------------------------------------------
            // Every leg below is about what survived a scene going down, so a run that never
            // got there would pass them all vacuously.
            sb.Append(" 2. ").Append(windDown).Append(" really ran to its end\n");
            check1("the scene has TERMINATED (NetScene.Current="
                + (NetScene.Current == null ? "null" : "still up") + ")",
                NetScene.Current == null);

            // ---- 3. the session SURVIVED (cards 3b6c12e7 / c600c55a) ------------------------
            sb.Append(" 3. the pairing outlived the level\n");
            check1("the session is still Active", NetSession.Active);
            check1("... and the peer is still up", NetSession.PeerUp);

            // THE DISCRIMINATOR. Pre-card the scene-down edge sent EvLeave and stopped; a
            // build that merely forgot to Stop would still send it, and the peer would end the
            // match from its own side.
            // NOT wire.Pump() -- that drains every endpoint with no collector attached, and
            // the frame this leg is looking for would be delivered into nothing. Dispatch is
            // inline on the SEND, so the peer's queue already holds whatever we sent it; the
            // collector drains it directly. (Nor does Stop() destroy the evidence: Close()
            // clears the closing endpoint's OWN inbound queue, not its peers'.)
            List<byte> types = DrainEventTypes(peer);
            check1("NO EvLeave was sent to the peer (event types seen: ["
                + string.Join(",", types) + "])", !types.Contains(NetProtocol.EvLeave));
            // ...and the absence means something: an assertion that nothing arrived also
            // passes on a run where the wire was dead, or where this endpoint was never
            // wired to that one at all. So the leg above carries a positive control.
            // NOTE the count is SMALL and that is the pinned clock, not a fault: the ship
            // stream is gated on `now - lastStreamTx >= StreamIntervalMs`, and pinning the
            // clock -- which is what keeps the 8 s drop verdict from firing across the wait
            // -- freezes that cadence too. What it proves is delivery, not throughput.
            check1("... on a wire that was genuinely carrying our traffic (peer received "
                + peer.RxDelivered + " packets)", peer.RxDelivered > 0);

            // ---- 4. the per-MATCH state was cleared -----------------------------------------
            // The reason a level-end reset exists at all: a stale interpolation buffer would
            // place the NEXT level's puppet at this level's final position.
            sb.Append(" 4. the level's own state did not carry over\n");
            check1("the remote ship puppet was dropped", !NetSession.HasRemotePuppet);
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netlevelend] {0} passed, {1} failed\n", pass, fail);
        }

        private static string TallyLost(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netlevellost] {0} passed, {1} failed\n", pass, fail);
        }
    }
}
