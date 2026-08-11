using System;
using System.Collections.Generic;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // eaNetRespawnPos() -- the verification for card df72b051: after a death and respawn, the
    // OTHER player's ship must appear where it respawned, never at the spot it died and then
    // slide across the screen to the spawn point. Run it inside a level, or `eval NetRespawnPos`
    // under eahl. Committed as tools/headless/probes/net_respawn_pos.txt.
    //
    // THE BUG. While a peer's ship is dead, SendShipState keeps streaming MsgShipState as the
    // heartbeat with alive=false and pos = lastTxPos -- the position the ship DIED at, repeated
    // every send interval for the whole death. Every one of those samples landed in the
    // interpolation buffer, and on the respawn the render clock (~InterpDelayMs behind the newest
    // sample) read them FIRST: the puppet materialised at the old death spot and lerped fast to
    // the real spawn point. A full both-players-die reset makes the gap seconds long and the two
    // points arbitrarily far apart, which is the reported "appears on the wrong position, then
    // gets sync'd".
    //
    // THE FIX (receiver-side only, no wire change): HandleShipState clears the buffer (+ render
    // clock) on the dead->alive RISING edge, before the first alive sample is added, so a new
    // life starts from its own samples and the interpolator can never bridge a death. Skipping
    // the dead Adds instead is NOT enough -- ShipStateBuffer's trim always keeps the last two
    // samples, so the bracketing pair straddling the death gap survives any gap length; section 1
    // demonstrates exactly that pair producing the bridge.
    //
    // WHAT EACH SECTION IS FOR.
    //   1  the bridge itself, as a PURE model on a scratch ShipStateBuffer fed through the real
    //      codec -- the NEGATIVE CONTROL (the PreCardTapBullets idiom): dead-period samples at the
    //      death spot plus fresh alive samples at the spawn point MUST read near the death spot at
    //      newest - InterpDelayMs, or the rest of the suite is asserting against a bridge that
    //      does not exist. The cleared buffer beside it must read the spawn point.
    //   2  the symptom END TO END over a real NetWire: a scripted peer lives at A, dies (heartbeat
    //      packets at A), respawns at B on the far side of the screen. The real puppet must
    //      explode on the falling edge, respawn on the rising edge, and every position it is
    //      driven to must stay near B -- it may never read near A again.
    //
    // *** DESTRUCTIVE, like eaNetFire / eaNetPickup. *** It pairs a real session onto the live
    // level and seats + explodes a Remote puppet (real explosions + the expl2 cue into the live
    // world). Run it in a throwaway ?level=Level2&invuln boot. Teardown stops the session,
    // removes the puppet ship and frees the Remote seat.
    internal static class NetRespawnPosTest
    {
        private const string Room = "netrespawnpos";
        private const ulong PeerToken = 0x2B7D31E5UL;

        private const float TickMs = 1000f / 60f;
        private const int TicksPerSend = 2;
        private const float SendIntervalMs = TicksPerSend * TickMs;

        // The two points of the story. |A - B| is ~665 design px -- most of the screen, so "near
        // one and far from the other" cannot be satisfied by accident.
        private static readonly Vector2 DeathAt = new Vector2(120f, 480f);
        private static readonly Vector2 SpawnAt = new Vector2(680f, 120f);

        // "Near": generous against interpolation jitter (the scripted ship never moves, so the
        // real slack is ~0). "Far": the bridge's signature is the puppet READING A, i.e. within a
        // few px of it -- 200 px leaves room for any partial lerp to still fail loudly.
        private const float NearPx = 60f;
        private const float FarPx = 200f;

        public static string Run()
        {
            StringBuilder sb = new StringBuilder("[netrespawnpos] respawn puppet position (card df72b051)\n");
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            // Section 1 needs no world, so it runs (and reports) even from a menu boot.
            LegBridge(sb, Check);

            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP section 2 (needs a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP section 2 (a co-op session is already up -- this suite would tear it down)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;

            int playersBefore = oracle.Players;
            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                LegEndToEnd(sb, Check, oracle, bin, game, clock);
            }
            catch (Exception ex)
            {
                Check("section 2 ran (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }
            finally
            {
                sb.Append(" 3. teardown\n");
                Teardown(sb, Check, oracle, bin, playersBefore);
                NetHost.Current = hostBefore;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the bridge, as a pure model (the negative control) ----------------------------

        private static void LegBridge(StringBuilder sb, Action<string, bool> Check)
        {
            sb.Append(" 1. the death-bridge on a scratch buffer -- the pre-card behaviour, modelled\n");
            // The exact sample stream a death produces, through the REAL codec so the model
            // cannot drift from the wire: a life at A, a dead stretch heartbeating A, one alive
            // sample at B. The buffer's 1s trim keeps at least the last two samples whatever the
            // gap, so the pair straddling the death survives.
            ShipStateBuffer buf = new ShipStateBuffer();
            uint t = 100;
            for (int i = 0; i < 8; i++)
            {
                buf.Add(Sample(t, DeathAt, alive: true));
                t += (uint)SendIntervalMs;
            }
            for (int i = 0; i < 60; i++) // ~2s of death heartbeats, all at the death position
            {
                buf.Add(Sample(t, DeathAt, alive: false));
                t += (uint)SendIntervalMs;
            }
            buf.Add(Sample(t, SpawnAt, alive: true));
            Vector2 read = buf.Sample(buf.NewestMs - NetSession.InterpDelayMs, out _);
            Check("NEGATIVE without the clear, the render point (newest - " + NetSession.InterpDelayMs
                + "ms) reads the DEATH position (" + Dist(read, DeathAt) + "px from it, "
                + Dist(read, SpawnAt) + "px from the spawn)",
                Dist(read, DeathAt) < NearPx && Dist(read, SpawnAt) > FarPx);

            // What the fix does: the rising edge clears the buffer before the first alive sample.
            buf.Clear();
            buf.Add(Sample(t, SpawnAt, alive: true));
            read = buf.Sample(buf.NewestMs - NetSession.InterpDelayMs, out _);
            Check("a buffer cleared on the rising edge reads the SPAWN position ("
                + Dist(read, SpawnAt) + "px from it)", Dist(read, SpawnAt) < NearPx);
        }

        // A ShipSample built through the real codec (encode -> decode), so this model and the
        // wire cannot disagree about layout or units.
        private static ShipSample Sample(uint ms, Vector2 pos, bool alive)
        {
            byte[] frame = NetProtocol.EncodeShipState(1, ms, pos, Vector2.Zero, 4.712389f, alive,
                shotCount: 0, shotsPerSec: 8, bulletLife: 450f);
            NetProtocol.TryDecodeShipState(frame, out _, out ShipSample s, out _, out _);
            return s;
        }

        // ---- 2. end to end ---------------------------------------------------------------------

        private static void LegEndToEnd(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game, PinnedNetHost clock)
        {
            sb.Append(" 2. end to end -- a scripted peer dies at A and respawns at B\n");
            bool rosterOk = oracle.IsSeated(0) && oracle.IsAlive(0);
            Check("PRECONDITION a local player at slot 0 with a live ship (players="
                + oracle.Players + ")", rosterOk);
            if (!rosterOk)
            {
                return;
            }

            float clockCarry = 0f;
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
            Check("the scripted peer paired (peer=" + (NetSession.PeerUp ? "up" : "down") + ")",
                NetSession.PeerUp);

            // ---- the first life, at A -------------------------------------------------------
            for (int i = 0; i < 8; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, DeathAt, alive: true);
            }
            int peerSlot = oracle.GetPlayerIndex(ControlDevice.Remote);
            PlayerShip puppet = peerSlot >= 0 ? FindShip(oracle, peerSlot) : null;
            Check("the peer's ship puppet was adopted (slot=" + peerSlot + ")",
                NetSession.HasRemotePuppet && puppet != null);
            if (puppet == null)
            {
                return;
            }
            DriveTicks(puppet, bin);
            Check("the living puppet is driven at A (" + Dist(puppet.GetPosition(), DeathAt)
                + "px from it)", Dist(puppet.GetPosition(), DeathAt) < NearPx);

            // ---- the death: alive=false heartbeats, still stamped with A --------------------
            long explosionsBefore = NetSession.Metrics.RemoteShipExplosions;
            long rxBefore = NetSession.Metrics.StreamRx;
            for (int i = 0; i < 60; i++) // ~2s of death, so the InterpDelay window is all-dead
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, DeathAt, alive: false);
            }
            Check("the falling edge exploded the puppet (RemoteShipExplosions +"
                + (NetSession.Metrics.RemoteShipExplosions - explosionsBefore) + ")",
                !NetSession.HasRemotePuppet
                && NetSession.Metrics.RemoteShipExplosions == explosionsBefore + 1);
            // The poison this card is about: the dead stretch really did keep streaming samples.
            Check("PRECONDITION the dead heartbeats kept flowing (StreamRx +"
                + (NetSession.Metrics.StreamRx - rxBefore) + ")",
                NetSession.Metrics.StreamRx - rxBefore == 60);

            // ---- the respawn, at B ----------------------------------------------------------
            Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, SpawnAt, alive: true);
            peerSlot = oracle.GetPlayerIndex(ControlDevice.Remote);
            puppet = peerSlot >= 0 ? FindShip(oracle, peerSlot) : null;
            Check("the respawned puppet was adopted again (slot=" + peerSlot + ")",
                NetSession.HasRemotePuppet && puppet != null);
            if (puppet == null)
            {
                return;
            }
            // Drive through the whole InterpDelay window and beyond, sampling every tick. The
            // bridge's signature is the FIRST driven ticks reading A (the render clock starts
            // ~InterpDelayMs behind the newest sample, inside the dead stretch) -- so the minimum
            // distance to A over the drive is the discriminating number, and the very first tick
            // is in the window that fails on the pre-card build.
            float minToDeath = float.MaxValue;
            float maxFromSpawn = 0f;
            for (int i = 0; i < 12; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, SpawnAt, alive: true);
                DriveTicks(puppet, bin);
                Vector2 at = puppet.GetPosition();
                minToDeath = Math.Min(minToDeath, Dist(at, DeathAt));
                maxFromSpawn = Math.Max(maxFromSpawn, Dist(at, SpawnAt));
            }
            Check("the respawned puppet NEVER reads near the death spot (min " + minToDeath
                + "px from A over the drive)", minToDeath > FarPx);
            Check("...and stays at the spawn point throughout (max " + maxFromSpawn
                + "px from B)", maxFromSpawn < NearPx);
        }

        // One ship-state packet, delivered and drained -- the real codec onto the real wire, the
        // clock advanced in step with the ticks (the NetFireTest.Deliver shape).
        private static void Deliver(InMemoryTransport peer, NetWire wire, PinnedNetHost clock,
            ref ushort shipSeq, ref uint shipMs, ref float clockCarry, Vector2 pos, bool alive)
        {
            clockCarry += SendIntervalMs;
            long step = (long)clockCarry;
            clockCarry -= step;
            shipMs += (uint)step;
            peer.SendStream(NetProtocol.EncodeShipState(shipSeq++, shipMs, pos, Vector2.Zero,
                4.712389f, alive, shotCount: 0, shotsPerSec: 8, bulletLife: 450f));
            wire.Pump();
            clock.Advance(step);
            NetSession.Update();
        }

        private static void DriveTicks(PlayerShip puppet, ComponentBin bin)
        {
            GameTime gt = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            for (int i = 0; i < TicksPerSend; i++)
            {
                puppet.Update(gt);
                bin.TopOfTickFlush();
            }
        }

        private static float Dist(Vector2 a, Vector2 b)
        {
            return (float)Math.Round(Vector2.Distance(a, b), 1);
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
            ComponentBin bin, int playersBefore)
        {
            try
            {
                if (NetSession.Active)
                {
                    NetSession.Stop("respawn-pos suite teardown");
                }
                Check("the session is stopped", !NetSession.Active);
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
                Check("no Remote seat is left squatting the roster (players=" + oracle.Players
                    + ", was " + playersBefore + ")",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote)
                    && oracle.Players == playersBefore);
            }
            catch (Exception ex)
            {
                Check("teardown ran (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }
        }

        private static string Tally(int pass, int fail)
        {
            return "[netrespawnpos] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
