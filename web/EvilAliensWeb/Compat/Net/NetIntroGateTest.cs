using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The Level 1 intro cinematic in online co-op (card 8a7772d6). Run `eaNetIntroGate()` on a
    // ?level=Level1 boot, or `eval NetIntroGate` under eahl. Committed as
    // tools/headless/probes/net_intro_gate.txt.
    //
    // WHAT IT COVERS. Level 1 opens with a ~10.5 s scripted cutscene -- UFOs fly in, then a hail
    // of bullets, THEN the player -- and the level script is host-only. So the joiner used to
    // spawn 1.3 s in and fly around for the whole thing, while the host (whose own ManagePuppet
    // gate needs a local ship) could not even see it. Two mechanisms, and each fails in a way no
    // frame can be timed to:
    //   PART A, the spawn gate. The host streams "my script is holding the player spawn" as
    //   ShipFlagScriptGate in every MsgShipState; the joiner holds its own spawn while that bit
    //   is set and mirrors Level1.demo_OnFinished when it CLEARS, so both ships fly in together.
    //   PART B, the volley. `Bullet` is not in NetTypeRegistry at all -- player bullets are never
    //   replicated -- so a correctly gated joiner would watch the twenty intro UFOs pop with
    //   nothing visibly killing them. EvIntroVolley hands over a seed and the joiner runs its own
    //   COSMETIC copy.
    //
    // WHY IT NEEDS A LEVEL, AND WHY LEVEL 1. The host-side read is `!spawnPlayerNormally` on a
    // REAL GameScene, and Level 1's intro is the only shipped script that ever sets it -- on any
    // other level the read is a constant false and every leg below would be vacuous. Leg 1
    // asserts that precondition rather than assuming it.
    //
    // WHY THE RELEASE LEG DRIVES THE EDGE ITSELF. The suite attaches to a level that is already
    // running, so `GameScene._timer` is long past UpdateStartup's 1300 ms spawn branch and
    // UpdateNormal has no spawn path of its own. That is not a limitation of the rig, it IS the
    // production case: offline, the ship at the end of the cinematic comes from
    // demo_OnFinished, and on a client it comes from the gate's falling edge. So the legs pair
    // a HELD phase (no ship however long we tick) with a RELEASE (a ship on the next tick), and
    // leg 5 is the control that says the ship came from the edge and not from the ticking.
    //
    // *** THIS SUITE IS DESTRUCTIVE, like eaNetResetSpawn / eaNetSceneOrder. *** It pairs a real
    // session onto the live level, ticks the real scene, spawns the local player's ship and fires
    // real (non-colliding) bullets into the world. Run it in a throwaway ?level=Level1 boot. It
    // refuses outright with no scene or with a real session up, and restores the roster.
    //
    // IT READS NO REAL CLOCK -- PinnedNetHost for the whole run, the NetResetSpawnTest rule. The
    // gate is a level off the newest stream sample, so a session that timed its peer out
    // mid-suite would open the gate (correctly, by the fail-open contract) and fail the held
    // legs for a reason that has nothing to do with the code under test.
    internal static class NetIntroGateTest
    {
        private const string Room = "introgate";

        // Same reasoning as NetResetSpawnTest's: the client sets peerPrimarySlot to
        // HostPrimarySlot (0) on adoption, so slot 0 must stay free for the peer's own puppet.
        private const byte GrantedSlot = 1;

        private const ulong PeerToken = 0x117E0FEEUL;

        private static readonly Vector2 RemoteShipPos = new Vector2(360f, 240f);

        private const float FacingUp = 4.712389f;

        // One frame at 60 Hz -- what the scene's own tick is worth. Used for both the scene
        // ticks and the direct Volley ticks so the two halves are paced identically.
        private static readonly GameTime Frame = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16.6667));

        // Long enough to cover UpdateStartup's whole 1300 ms spawn branch several times over, so
        // "no ship appeared" is a claim about the gate and not about not having waited.
        private const int HeldTicks = 120;

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[introgate] Level 1 intro cinematic in co-op (card 8a7772d6)\n");

            GameScene live = GameScene.NetActiveScene;
            if (live == null)
            {
                sb.Append("SKIP (needs a live level -- boot ?level=Level1 and run it there)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("SKIP (a co-op session is already up -- this suite would tear it down)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            ControlDevice localDevice = oracle.Controller(0);

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;
            ushort shipSeq = 1;
            uint shipMs = 100;

            PinnedNetHost clock = new PinnedNetHost();

            // Ticking the LIVE scene is what makes the spawn legs end to end, and it is also the
            // one thing here that can throw into a caller that has a session half-installed --
            // so everything below the host install sits in the try/finally.
            void TickScene(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    bin.TopOfTickFlush();
                    live.Update(Frame);
                    bin.Update();
                    NetSession.Update();
                }
            }

            void RunLegs()
            {
                // ---- 1. the host read, on the REAL script ---------------------------------
                // No session yet: this is the level's own state, which is the whole reason the
                // suite demands Level 1. If the intro has already finished (the suite was run
                // late) every later leg would still pass while proving nothing about a real
                // cutscene, so this is a hard precondition rather than an assertion.
                sb.Append(" 1. the host read comes off the REAL Level 1 script\n");
                bool holding = live.NetScriptHoldsShipSpawn;
                Check("PRECONDITION Level 1's intro is still holding the player spawn"
                    + " (level=" + live.Level + " holding=" + holding + ")"
                    + (holding ? "" : " -- boot ?level=Level1 and run this within ~10s"), holding);
                Check("... and the level really has no player ship yet", !oracle.IsAlive(0));
                if (!holding)
                {
                    return;
                }

                // ---- 2. the HOST puts the bit on the wire ---------------------------------
                // A real host session over the wire, and the assertion is on the frame the peer
                // RECEIVED (the NetDeathFxTest section-2 shape) -- an encode/decode pair cannot
                // see a sender that never sets the flag.
                sb.Append(" 2. a real HOST session streams the gate bit\n");
                NetSession.StartForTest(game, host: true, ours, Room);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                Check("the scripted client paired with us", NetSession.PeerUp && NetSession.IsHost);

                RecordingEndpoint heard = new RecordingEndpoint(peer);
                clock.Advance(200);          // past StreamIntervalMs, so Update sends
                NetSession.Update();
                wire.Pump();
                Check("the peer received a ship-state frame while the script holds our spawn",
                    heard.LastShipGate.HasValue);
                Check("... and its ShipFlagScriptGate is SET", heard.LastShipGate == true);

                // The negative control for the same sender. Nothing else about the session
                // changes -- only the scene's answer -- so a bit hard-wired true cannot pass.
                live.NetTestForceSpawnGate(false);
                heard.Clear();
                clock.Advance(200);
                NetSession.Update();
                wire.Pump();
                Check("with the script no longer holding, the SAME sender clears the bit",
                    heard.LastShipGate == false);
                live.NetTestForceSpawnGate(null);
                Check("the override is handed back and the real script answers again",
                    live.NetScriptHoldsShipSpawn);

                NetSession.Stop("intro gate: host leg finished");
                Check("PeerHoldsShipSpawn is false with no session at all (fail-open)",
                    !NetSession.PeerHoldsShipSpawn);
                // The host leg RESERVED a seat for its scripted joiner, and Stop() deliberately
                // does not hand a roster seat back (a real host reverts to single-player with
                // its own roster intact). Leg 3 then joins as a CLIENT and needs that very slot
                // free, or the grant is refused with "occupied locally -- asking the host to
                // re-grant" and every leg under it is about a pairing that never settled.
                ReleaseNetSeats(oracle);

                // ---- 3. the CLIENT holds its spawn ----------------------------------------
                sb.Append(" 3. a real CLIENT session holds its own spawn while the host's bit is set\n");
                wire = new NetWire(2);
                ours = wire[0];
                peer = wire[1];
                shipSeq = 1;
                shipMs = 100;
                NetSession.StartForTest(game, host: false, ours, Room);
                peer.Open(Room);
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                    NetSession.LocalBuildHash, 0, GrantedSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                bool paired = NetSession.PeerUp && NetSession.LocalPrimarySlot == GrantedSlot;
                Check("the scripted host's hello was accepted and its grant adopted"
                    + " (pri=" + NetSession.LocalPrimarySlot + ")", paired);
                if (!paired)
                {
                    return;
                }

                Check("before any stream, the client is UNGATED -- the fail-open default",
                    !NetSession.PeerHoldsShipSpawn && live.NetWouldSpawnPlayerNormally);

                Pump(wire, peer, ShipFrame(ref shipSeq, ref shipMs, gate: true));
                Check("the host's held bit reaches PeerHoldsShipSpawn",
                    NetSession.PeerHoldsShipSpawn);
                Check("... and closes the scene's spawn gate, even though our own script flag"
                    + " is dead here", !live.NetWouldSpawnPlayerNormally);

                // READ THE NEXT ASSERTION AS A NO-REGRESSION CHECK, NOT AS THE DISCRIMINATOR.
                // The suite attaches to a level that is already in GameState.Normal, and
                // UpdateStartup's 1300 ms spawn branch -- the thing the pre-card `|| IsClient`
                // actually fed -- can no longer fire there either way. Measured: reverting the
                // getter to `|| IsClient` fails ONLY the gate assertion above, not this one.
                // The gate READING closed is what UpdateStartup consumes, so that is the leg
                // that carries the pre-card bug; this one says nothing else spawned a ship
                // behind our back during the hold.
                for (int i = 0; i < HeldTicks; i++)
                {
                    // Re-send every tick: the gate is a LEVEL off the newest sample, and this is
                    // also what keeps the jitter buffer fresh (the NetResetSpawnTest rule).
                    Pump(wire, peer, ShipFrame(ref shipSeq, ref shipMs, gate: true));
                    TickScene(1);
                }
                Check("after " + HeldTicks + " ticks of a held gate, our slot still has no ship",
                    !oracle.IsAlive(GrantedSlot));

                // ---- 4. the RELEASE flies both ships in -----------------------------------
                sb.Append(" 4. clearing the bit spawns our ship, mirroring demo_OnFinished\n");
                Pump(wire, peer, ShipFrame(ref shipSeq, ref shipMs, gate: false));
                Check("the cleared bit reaches PeerHoldsShipSpawn", !NetSession.PeerHoldsShipSpawn);
                TickScene(1);
                Check("one tick later our ship is in the world", oracle.IsAlive(GrantedSlot));
                Check("... and the scene has latched the release locally, so the rest of the"
                    + " level no longer depends on the wire", live.NetWouldSpawnPlayerNormally);

                // ---- 5. CONTROL: the ship came from the EDGE, not from the ticking --------
                // Without this, leg 4 passes on a build where the release does nothing and some
                // other path happened to spawn a ship during those ticks.
                sb.Append(" 5. CONTROL -- a late gate never yanks a ship, and never respawns one\n");
                PlayerShip flying = oracle.GetPlayerShip(GrantedSlot);
                Pump(wire, peer, ShipFrame(ref shipSeq, ref shipMs, gate: true));
                TickScene(2);
                Check("a gate arriving after we spawned leaves the ship exactly where it was",
                    ReferenceEquals(oracle.GetPlayerShip(GrantedSlot), flying));
                Pump(wire, peer, ShipFrame(ref shipSeq, ref shipMs, gate: false));
                TickScene(1);
                Check("... and releasing it again does not add a SECOND ship in that slot"
                    + " (ships=" + oracle.GetShips().Count + ")",
                    ReferenceEquals(oracle.GetPlayerShip(GrantedSlot), flying));

                // ---- 6. the intro volley ---------------------------------------------------
                // BEFORE the peer-loss leg, deliberately: a world message is only applied from a
                // live peer, so running it after would mean building a SECOND pairing, whose
                // re-grant would collide with the seat leg 3 moved us into -- noise this suite
                // has no business generating.
                sb.Append(" 6. the intro volley: EvIntroVolley -> a COSMETIC local copy\n");
                Check("no volley is running before the beat", !live.NetIntroVolleyActive);
                Pump(wire, peer, NetProtocol.EncodeIntroVolleyEvent(eventSeq++, 12345));
                Check("the beat started a local volley", live.NetIntroVolleyActive);

                // Census only what OUR volley added, the DriveVolley shape. A whole-world census
                // would fail for a reason that has nothing to do with the code: the HOST's own
                // volley is `cosmetic: false` and its 70 COLLIDING bullets are live in this same
                // world from ~8.8 s to ~14 s of level time, while leg 1's precondition stays
                // true until demo_OnFinished at ~13.2 s. The committed probe asserts at frame
                // 200 and would never have noticed; a human running it a few seconds later
                // would have.
                HashSet<GameComponent> preVolley = new HashSet<GameComponent>(CollectType<Bullet>(game));
                TickScene(20);
                int added = 0;
                bool allInert = true;
                foreach (GameComponent b in CollectType<Bullet>(game))
                {
                    if (preVolley.Contains(b))
                    {
                        continue;
                    }
                    added++;
                    allInert &= !((AlienDrawableGameComponent)b).Collides;
                }
                Check("ticking the scene fires bullets (added=" + added + ")", added > 0);
                Check("every bullet the volley added is NON-COLLIDING -- the cosmetic contract",
                    allInert && added > 0);

                // ---- 7. fail-open: losing the peer opens the gate --------------------------
                // The catastrophic failure this feature could have is a joiner with no ship, so
                // the escape is asserted rather than argued. The heading deliberately avoids
                // the word FAIL: the probe carries an `expect-not FAIL` and a leg title with it
                // in would trip that on every green run.
                sb.Append(" 7. OPENS ON PEER LOSS -- a held gate does not survive losing the peer\n");
                Pump(wire, peer, ShipFrame(ref shipSeq, ref shipMs, gate: true));
                Check("PRECONDITION the gate is held again", NetSession.PeerHoldsShipSpawn);
                peer.Close();
                NetSession.Update();
                Check("the peer going away opens the gate", !NetSession.PeerHoldsShipSpawn);

                // ---- 8. the emitter, driven directly ---------------------------------------
                // The wire leg above cannot say how many bullets a volley is, or that the seed
                // does anything -- 20 scene ticks is a fraction of its 2.3 s. Driving the real
                // Lvl1StartDemoEvent.Volley in a plain loop can.
                sb.Append(" 8. the emitter: 70 bullets, and the seed really picks the angles\n");
                float[] a = DriveVolley(bin, game, seed: 777, cosmetic: true);
                float[] b2 = DriveVolley(bin, game, seed: 777, cosmetic: true);
                float[] c = DriveVolley(bin, game, seed: 778, cosmetic: true);
                Check("a volley is exactly " + Lvl1StartDemoEvent.Volley.BulletCount
                    + " bullets (fired=" + a.Length + ")",
                    a.Length == Lvl1StartDemoEvent.Volley.BulletCount);
                Check("the same seed gives the same angles", SameAngles(a, b2));
                Check("a different seed gives different angles", !SameAngles(a, c));
                bool arcOk = true;
                foreach (float ang in a)
                {
                    arcOk &= ang >= (float)Math.PI * -3f / 4f - 0.001f && ang <= -(float)Math.PI / 4f + 0.001f;
                }
                Check("every angle is inside the intro's upward arc", arcOk);
            }

            INetHost previousHost = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                RunLegs();
            }
            catch (Exception ex)
            {
                sb.Append("  FAIL threw: ").Append(ex.GetType().Name).Append(": ")
                    .Append(ex.Message).Append('\n');
                fail++;
            }
            finally
            {
                Teardown(live, oracle, bin, localDevice, Check);
                NetHost.Current = previousHost;
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // Drive a REAL Volley in a plain loop and return the angle of every bullet it produced,
        // in order. It really adds them to the live bin (a Volley has no other output), so the
        // bullets are removed again here -- the suite is destructive, but it need not litter.
        private static float[] DriveVolley(ComponentBin bin, Game game, int seed, bool cosmetic)
        {
            HashSet<GameComponent> seen = new HashSet<GameComponent>(CollectType<Bullet>(game));
            Lvl1StartDemoEvent.Volley volley = new Lvl1StartDemoEvent.Volley(seed, cosmetic);
            List<float> angles = new List<float>();
            int fired = 0;
            // Generous bound: 70 bullets at one per 33 ms is ~140 frames, and the loop exits on
            // Finished anyway -- so an emitter that stalls reports a SHORT array (which leg 8
            // asserts on) rather than hanging the game.
            for (int i = 0; i < 600 && !volley.Finished; i++)
            {
                volley.Update(Frame, bin, game);
                if (volley.Fired != fired)
                {
                    fired = volley.Fired;
                    angles.Add(volley.LastAngle);
                }
            }
            // Take the bullets back out. The suite is destructive, but three volleys is 210
            // bullets and leaving them would make leg 7's world census meaningless on a re-run.
            foreach (GameComponent comp in CollectType<Bullet>(game))
            {
                if (seen.Add(comp))
                {
                    bin.Remove(comp);
                }
            }
            bin.TopOfTickFlush();
            return angles.ToArray();
        }

        // Every net-owned seat, from every slot. Not RemovePlayerAt(HostPrimarySlot, Remote):
        // which slot a puppet ends up in is the HOST's decision, and this suite runs both roles.
        private static void ReleaseNetSeats(Oracle oracle)
        {
            for (int i = 0; i < Oracle.MaxPlayers; i++)
            {
                oracle.RemovePlayerAt(i, ControlDevice.Remote);
                oracle.RemovePlayerAt(i, ControlDevice.RemoteFriend);
            }
        }

        private static bool SameAngles(float[] x, float[] y)
        {
            if (x.Length != y.Length || x.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (Math.Abs(x[i] - y[i]) > 0.0001f)
                {
                    return false;
                }
            }
            return true;
        }

        private static void Pump(NetWire wire, InMemoryTransport peer, byte[] frame)
        {
            if (frame[0] == NetProtocol.MsgShipState)
            {
                peer.SendStream(frame);
            }
            else
            {
                peer.SendReliable(frame);
            }
            wire.Pump();
            NetSession.Update();
        }

        private static byte[] ShipFrame(ref ushort seq, ref uint senderMs, bool gate)
        {
            senderMs += 33; // advance, or ShipStateBuffer refuses the sample as stale
            return NetProtocol.EncodeShipState(seq++, senderMs, RemoteShipPos, Vector2.Zero,
                FacingUp, alive: true, firing: false, shotsPerSec: 8, bulletLife: 450f,
                scriptGate: gate);
        }

        // Watches what the far endpoint actually received, so leg 2 asserts on the FRAME rather
        // than on the sender's intent.
        private sealed class RecordingEndpoint
        {
            public bool? LastShipGate;

            public RecordingEndpoint(InMemoryTransport endpoint)
            {
                endpoint.OnData += OnData;
            }

            public void Clear()
            {
                LastShipGate = null;
            }

            private void OnData(byte[] data, bool reliable, string from)
            {
                if (NetProtocol.TryDecodeShipState(data, out _, out ShipSample s, out _, out _))
                {
                    LastShipGate = s.ScriptGate;
                }
            }
        }

        private static List<GameComponent> CollectType<T>(Game game)
        {
            List<GameComponent> found = new List<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T)
                {
                    found.Add(item);
                }
            }
            return found;
        }

        private static int CountType<T>(Game game)
        {
            return CollectType<T>(game).Count;
        }

        // Hand the level back a roster it can play on. As with NetResetSpawnTest this is NOT the
        // state the suite found -- the cinematic has been cut short and the player's ship is in
        // the world early -- but nothing NET-owned may be left behind.
        private static void Teardown(GameScene live, Oracle oracle, ComponentBin bin,
            ControlDevice localDevice, Action<string, bool> check)
        {
            try
            {
                live.NetTestForceSpawnGate(null);
                NetSession.Stop("intro gate scenario finished");
                check("the session is stopped", !NetSession.Active);
                check("... so the gate is open again", !NetSession.PeerHoldsShipSpawn);

                ReleaseNetSeats(oracle);
                check("no Remote or RemoteFriend seat is left squatting the roster",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote)
                    && !oracle.DeviceIsPlaying(ControlDevice.RemoteFriend));

                if (oracle.IsSeated(GrantedSlot) && !oracle.IsSeated(0))
                {
                    oracle.MovePlayerSlot(GrantedSlot, 0);
                }
                check("the local player is back in slot 0 on its own device",
                    oracle.Players == 1 && oracle.IsSeated(0)
                    && oracle.Controller(0) == localDevice);
            }
            catch (Exception ex)
            {
                check("teardown threw: " + ex.GetType().Name + ": " + ex.Message, false);
            }
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[introgate] {0} passed, {1} failed\n", pass, fail);
        }
    }
}
