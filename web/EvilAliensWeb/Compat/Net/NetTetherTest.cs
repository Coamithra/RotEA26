using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for card 2cfab019 -- the online connector tether's HARD CAP. Invoke with
    // eaNetTether() / `eval NetTether`; the eaNetPickup shape, i.e. DESTRUCTIVE (it pairs a real
    // HOST session onto the live level and adopts a real ship puppet), so run it in a throwaway
    // ?level=Level2&invuln boot.
    //
    // THE REPORT was "in online multiplayer, due to latency etc, this pulling back isnt working
    // and ships can fly further and further away from each other". The latency half is wrong and
    // this suite is not about latency: the runaway is a GAIN problem, measured identical at every
    // one-way delay (tools/sim/tether_sim.py). The soft pull saturates at NetMaxPullPxPerMs 0.22
    // while a ship thrusts at ShipMaxSpeed 0.33, so any ONE-SIDED pull budget separates without
    // bound -- both players thrusting apart, or one thrusting while the other is pinned on the
    // screen clamp. A LONE thruster was never broken (the idle partner's own pull covers the
    // shortfall at ~167px), which is why the card's guessed cause -- "only the host moves itself
    // back towards the client" -- is not it: both peers always ran NetPullOwnShip.
    //
    // DIVISION OF LABOUR, because three instruments cover this card and each sees something the
    // others cannot:
    //   * tools/sim/tether_sim.py  -- the SCENARIOS and the stability argument: two coupled peers,
    //     real stale anchors, the latency sweep, the release/ringing test, the stall.
    //   * tools/sim/logic_probe    -- the pure LAW (ShipConnector.NetPullSpeedPxPerMs): continuity
    //     at the knee, monotonicity, the equilibrium, the ceiling.
    //   * THIS SUITE               -- the WIRING, which neither of those can see: that
    //     ShipConnector.Update actually routes a real connector between a real local ship and a
    //     real puppet into that law, that it moves the ship we OWN and never the puppet, and that
    //     two locally-owned endpoints take the rigid branch instead.
    //
    // It runs on BOTH arms of ?nettetherwall and passes on both -- it reports the verdict rather
    // than assuming one, because the flag is exactly what the probe pair
    // (tools/headless/probes/net_tether_wall.txt + _absent.txt) discriminates on.
    internal static class NetTetherTest
    {
        private const string Room = "nettether";
        private const ulong PeerToken = 0x2CFAB019UL;

        // THE DRIVE, and why it is this scenario and not "both players thrusting apart". This
        // process is ONE peer. The puppet's position is whatever the wire says, and on the real
        // other peer that position has already had ITS tether pull applied to it -- so a rig that
        // walks the puppet away at full ShipMaxSpeed is not modelling the system, it is modelling
        // a peer that never tethers at all, and it measures a gap rate no session produces. (It
        // was written that way first and reported a runaway on a build whose cap works.)
        //
        // So the drive is the OTHER reported scenario, which needs no assumption about the remote
        // peer whatsoever: our ship thrusts directly away while the partner does not close --
        // pinned against the 800x600 clamp in PlayerShip.Update, which is the everyday trigger for
        // this card. Here the gap rate is exactly ShipMaxSpeed - pull, so the cap's equilibrium
        // (pull == thrust, ~220px) is reached, and with the cap off the pull saturates at 0.22 and
        // the gap grows at 0.11px/ms forever -- ~960px over the 8s below.
        // Two coupled peers with real stale anchors are tether_sim.py's job, not this one's.
        private const float TickMs = 16.6667f;
        private const int DriveTicks = 480;         // 8 seconds
        private const float BoundedMaxPx = 260f;
        private const float RunawayMinPx = 700f;

        public static string Run()
        {
            StringBuilder sb = new StringBuilder("[nettether] connector tether hard cap (card 2cfab019)\n");
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            // The eaNetPickup gate: this needs a live world, and must never tear down a session a
            // player is actually in.
            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP (needs a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP (a co-op session is already up -- this suite would tear it down)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            int playersBefore = oracle.Players;
            List<GameComponent> planted = new List<GameComponent>();
            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                RunLegs(sb, Check, oracle, bin, game, planted);
            }
            catch (Exception ex)
            {
                Check("the legs ran (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }
            finally
            {
                sb.Append(" 5. teardown\n");
                Teardown(sb, Check, oracle, bin, planted, playersBefore);
                NetHost.Current = hostBefore;
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        private static void RunLegs(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            // ---- 0. rig ---------------------------------------------------------------------
            sb.Append(" 0. rig -- a real HOST session, a scripted client, and a real connector\n");
            bool rosterOk = oracle.Players == 1 && oracle.IsSeated(0) && oracle.IsAlive(0);
            Check("PRECONDITION one local player at slot 0 with a live ship (players="
                + oracle.Players + ")", rosterOk);
            if (!rosterOk)
            {
                return;
            }
            PlayerShip own = oracle.GetShips()[0];

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort shipSeq = 1;
            uint shipMs = 100;

            NetSession.StartForTest(game, host: true, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("the scripted client paired", NetSession.PeerUp);

            peer.SendStream(NetProtocol.EncodeShipState(shipSeq++, shipMs += 33,
                new Vector2(400f, 300f), Vector2.Zero, 4.712389f, alive: true, shotCount: 0, 8, 450f));
            wire.Pump();
            NetSession.Update();
            int peerSlot = oracle.GetPlayerIndex(ControlDevice.Remote);
            PlayerShip puppet = (peerSlot >= 0) ? FindShip(oracle, peerSlot) : null;
            Check("the peer's ship puppet was adopted into a Remote seat (slot=" + peerSlot + ")",
                puppet != null && NetSession.HasRemotePuppet);
            if (puppet == null)
            {
                return;
            }

            // The connector is built the way TeamChallenge builds its scripted tether
            // (ShipConnector.Setup + bin.Add). The Linker-COLLISION formation path is
            // eaNetPickup's subject; what is under test here is the physics, which is the same
            // component either way.
            ShipConnector tether = ShipConnector.NewAlien(bin, game);
            tether.Setup(own, puppet);
            bin.Add((GameComponent)(object)tether);
            bin.TopOfTickFlush();
            planted.Add((GameComponent)(object)tether);
            Check("a real ShipConnector is live between our ship and the puppet",
                tether.A != null && tether.B != null);

            ControlDevice puppetControllerBefore = puppet.Controller;
            try
            {
                // ---- 1. the pull moves OUR ship and never the puppet ------------------------
                sb.Append(" 1. endpoint pick -- the tether may only move the ship we own\n");
                Place(own, new Vector2(200f, 300f));
                Place(puppet, new Vector2(500f, 300f));
                Vector2 puppetBefore = puppet.Position;
                Vector2 ownBefore = own.Position;
                tether.Update(Tick());
                Check("the puppet was NOT moved (its owner decides where it is)",
                    Vector2.Distance(puppet.Position, puppetBefore) < 0.001f);
                Check("our own ship was pulled toward it",
                    Vector2.Distance(own.Position, puppet.Position)
                        < Vector2.Distance(ownBefore, puppetBefore) - 0.001f);

                // A RemoteFriend is a host-driven puppet too. Pre-card the endpoint pick asked
                // `Controller != Remote`, which a RemoteFriend PASSES -- so it would be chosen as
                // "ours" and this peer would move a ship the wire is authoritative for.
                //
                // THE PUPPET MUST BE ENDPOINT A HERE, and that is the whole leg. The pick examines
                // A first and takes it if it is ours, so with our own ship in A the pre-card bug
                // is unreachable -- our ship is chosen before the puppet is ever looked at, and
                // the leg passes on the broken build. Found by mutation-testing this file, not by
                // reading it.
                tether.Setup(puppet, own);
                puppet.AdoptController(ControlDevice.RemoteFriend);
                Place(own, new Vector2(200f, 300f));
                Place(puppet, new Vector2(500f, 300f));
                puppetBefore = puppet.Position;
                tether.Update(Tick());
                Check("a RemoteFriend in endpoint A is still a puppet, not ours to move",
                    Vector2.Distance(puppet.Position, puppetBefore) < 0.001f
                        && Vector2.Distance(own.Position, new Vector2(200f, 300f)) > 0.001f);
                puppet.AdoptController(puppetControllerBefore);
                tether.Setup(own, puppet);

                // ---- 2. THE CARD: sustained separation --------------------------------------
                sb.Append(" 2. sustained separation -- we thrust away, partner pinned, 8s\n");
                bool wall = DebugFlags.NetTetherWall;
                sb.Append("    arm: ?nettetherwall is ")
                  .Append(wall ? "ON (shipped)" : "OFF (the pre-card runaway)").Append('\n');
                float peak = DriveApart(tether, own, puppet, out float start, out float end);
                sb.Append("    gap start=").Append(F(start))
                  .Append(" peak=").Append(F(peak))
                  .Append(" end=").Append(F(end))
                  .Append(" verdict=").Append(end <= BoundedMaxPx ? "BOUNDED" : "RUNAWAY")
                  .Append('\n');
                if (wall)
                {
                    Check("BOUNDED -- the separation plateaus instead of growing (end="
                        + F(end) + "px <= " + F(BoundedMaxPx) + ")", end <= BoundedMaxPx);
                    // Boundedness alone would also pass on a build that welded the ships
                    // together, which would be a different bug: the tether must still STRETCH.
                    Check("... and it is a cap, not a weld (the pair really did stretch past rest)",
                        peak > 150f);
                }
                else
                {
                    Check("RUNAWAY -- with the cap off the pre-card separation is unbounded (end="
                        + F(end) + "px >= " + F(RunawayMinPx) + ")", end >= RunawayMinPx);
                }

                // ---- 3. two LOCALLY-owned endpoints take the rigid branch -------------------
                sb.Append(" 3. couch pair -- two locally-owned ships inside a session are RIGID\n");
                puppet.AdoptController(ControlDevice.PadOne);
                Place(own, new Vector2(300f, 300f));
                Place(puppet, new Vector2(500f, 300f));
                tether.Update(Tick());
                float rigidGap = Vector2.Distance(own.Position, puppet.Position);
                Check("one tick locks them at the rigid 78px docking separation (gap="
                    + F(rigidGap) + ")", Math.Abs(rigidGap - 78f) < 0.5f);
                // The soft law moved only ONE of two locally-owned ships, so this pair ran away
                // on its own -- with no staleness anywhere to justify being soft about it.
                float rigidPeak = DriveApart(tether, own, puppet, out _, out float rigidEnd);
                Check("... and sustained thrust cannot separate them at all (end="
                    + F(rigidEnd) + "px, peak=" + F(rigidPeak) + "px)",
                    Math.Abs(rigidEnd - 78f) < 1f && rigidPeak < 90f);
            }
            finally
            {
                // Never leave the roster believing a Remote seat is a pad -- Teardown's
                // ReleasePlayer(Remote) and the puppet sweep both key off the controller.
                puppet.AdoptController(puppetControllerBefore);
            }

            // ---- 4. the tether is inert once an endpoint goes ------------------------------
            sb.Append(" 4. break -- a tether with no endpoints moves nothing\n");
            tether.NetBreakSilently();
            Place(own, new Vector2(200f, 300f));
            Vector2 parked = own.Position;
            tether.Update(Tick());
            Check("a broken tether does not keep pulling", Vector2.Distance(own.Position, parked) < 0.001f);
        }

        // Our ship thrusts directly away at ShipMaxSpeed for DriveTicks while the partner holds
        // station, with the real ShipConnector.Update between each step -- see the DriveTicks
        // block for why this scenario and not a two-thruster one.
        //
        // OUR OWN screen clamp is deliberately not run either (it lives in PlayerShip.Update):
        // this leg measures the tether, and a clamp on the thrusting ship would bound the gap for
        // a reason that has nothing to do with the tether -- which is precisely the trap that
        // makes the runaway hard to see in a real game.
        private static float DriveApart(ShipConnector tether, PlayerShip own, PlayerShip held,
            out float start, out float end)
        {
            Place(own, new Vector2(361f, 300f));
            Place(held, new Vector2(439f, 300f));   // the 78px rest separation
            start = Vector2.Distance(own.Position, held.Position);
            float peak = start;
            float step = PlayerShip.ShipMaxSpeed * TickMs;
            Vector2 station = held.Position;
            for (int i = 0; i < DriveTicks; i++)
            {
                Place(own, own.Position + new Vector2(-step, 0f));
                // The partner is pinned, so it is put back on its station every tick. On the rigid
                // branch (leg 3) the tether moves BOTH ships, and without this the pair would
                // simply drift together and the leg would assert nothing.
                Place(held, station);
                tether.Update(Tick());
                float gap = Vector2.Distance(own.Position, held.Position);
                if (gap > peak)
                {
                    peak = gap;
                }
            }
            end = Vector2.Distance(own.Position, held.Position);
            return peak;
        }

        private static void Place(PlayerShip ship, Vector2 where)
        {
            ship.SetPosition(where);
        }

        private static GameTime Tick()
        {
            return new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
        }

        private static string F(float v)
        {
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static PlayerShip FindShip(Oracle oracle, int slot)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.Owner == slot)
                {
                    return s;
                }
            }
            return null;
        }

        private static void Teardown(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, List<GameComponent> planted, int playersBefore)
        {
            try
            {
                if (NetSession.Active)
                {
                    NetSession.Stop("tether suite teardown");
                }
                Check("the session is stopped", !NetSession.Active);
                foreach (GameComponent comp in planted)
                {
                    bin.Remove(comp);
                }
                // The puppet ship and its seat, which Stop() does not unwind -- only the peer-loss
                // paths do, and nothing here goes through one. Left standing, the level plays on
                // with a frozen ghost ship in slot 1.
                foreach (PlayerShip s in new List<PlayerShip>(oracle.GetShips()))
                {
                    if (s.Controller == ControlDevice.Remote
                        || s.Controller == ControlDevice.RemoteFriend)
                    {
                        bin.Remove((GameComponent)(object)s);
                    }
                }
                oracle.ReleasePlayer(ControlDevice.Remote);
                oracle.ReleasePlayer(ControlDevice.RemoteFriend);
                bin.TopOfTickFlush();
                planted.Clear();
                Check("no Remote seat is left squatting the roster (players=" + oracle.Players
                    + ", was " + playersBefore + ")", oracle.Players == playersBefore);
            }
            catch (Exception ex)
            {
                Check("teardown ran (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }
        }

        private static string Tally(int pass, int fail)
        {
            return "[nettether] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
