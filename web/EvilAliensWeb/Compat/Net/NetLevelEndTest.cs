using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // FINISHING A LEVEL KEEPS A MENU-LOBBY PAIRING ALIVE (card 3b6c12e7), and the remote ship
    // leaves in the SAME DIRECTION as every local one (card b4a9fe60).
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

            sb.Append("[netlevelendmenu] phase 2' -- the host is in its lobby (card 3b6c12e7)\n");
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
            }

            sb.Append(Tally2(pass, fail));
            return sb.ToString();
        }

        private static string Tally2(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netlevelendmenu] {0} passed, {1} failed\n", pass, fail);
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
            if (!armed || armedHost)
            {
                sb.Append(armed
                    ? "  SKIP (the HOST arm phase is up -- its phase 2 is the menu check)\n"
                    : "  SKIP (not armed -- run the arm phase first)\n");
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

                // ---- 2. the level really finished ---------------------------------------------
                // Every leg below is about what survived a scene going down, so a run that never
                // got there would pass them all vacuously.
                sb.Append(" 2. the victory choreography really ran to its end\n");
                Check1("the scene has TERMINATED (NetScene.Current="
                    + (NetScene.Current == null ? "null" : "still up") + ")",
                    NetScene.Current == null);

                // ---- 3. the session SURVIVED (card 3b6c12e7) ----------------------------------
                sb.Append(" 3. the pairing outlived the level\n");
                Check1("the session is still Active", NetSession.Active);
                Check1("... and the peer is still up", NetSession.PeerUp);
                Check1("the menus have a lobby return pending", NetSession.PendingLobbyReturn);

                // THE DISCRIMINATOR. Pre-card the scene-down edge sent EvLeave and stopped; a
                // build that merely forgot to Stop would still send it, and the peer would end the
                // match from its own side.
                // NOT wire.Pump() -- that drains every endpoint with no collector attached, and
                // the frame this leg is looking for would be delivered into nothing. Dispatch is
                // inline on the SEND, so the peer's queue already holds whatever we sent it; the
                // collector drains it directly. (Nor does Stop() destroy the evidence: Close()
                // clears the closing endpoint's OWN inbound queue, not its peers'.)
                List<byte> types = DrainEventTypes(peer);
                Check1("NO EvLeave was sent to the peer (event types seen: ["
                    + string.Join(",", types) + "])", !types.Contains(NetProtocol.EvLeave));
                // ...and the absence means something: an assertion that nothing arrived also
                // passes on a run where the wire was dead, or where this endpoint was never
                // wired to that one at all. So the leg above carries a positive control.
                // NOTE the count is SMALL and that is the pinned clock, not a fault: the ship
                // stream is gated on `now - lastStreamTx >= StreamIntervalMs`, and pinning the
                // clock -- which is what keeps the 8 s drop verdict from firing across the wait
                // -- freezes that cadence too. What it proves is delivery, not throughput.
                Check1("... on a wire that was genuinely carrying our traffic (peer received "
                    + peer.RxDelivered + " packets)", peer.RxDelivered > 0);

                // ---- 4. the per-MATCH state was cleared ---------------------------------------
                // The reason a level-end reset exists at all: a stale interpolation buffer would
                // place the NEXT level's puppet at this level's final position.
                sb.Append(" 4. the level's own state did not carry over\n");
                Check1("the remote ship puppet was dropped", !NetSession.HasRemotePuppet);
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
            return NetProtocol.EncodeShipState(shipSeq++, 0u,
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

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netlevelend] {0} passed, {1} failed\n", pass, fail);
        }
    }
}
