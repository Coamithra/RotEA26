using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // eaNetFire() -- the verification for card a5c2a39b: "if P1 taps once to shoot a single
    // bullet, P2 sees TWO bullets flying". Run it inside a level, or `eval NetFire` under eahl.
    // Committed as tools/headless/probes/net_single_tap.txt.
    //
    // THE BUG, MEASURED. The card guessed we replicate mousedown+location. We do not: the wire
    // carries firing STATE on MsgShipState and the peer re-fires through the REAL FireAt path
    // (NetSession.DriveRemoteShip -> PlayerShip.NetApplyRemoteState). The defect was arithmetic.
    //   * PlayerShip.FireAt stamps NetLastFireMs every tick the trigger is held, BEFORE its own
    //     cadence gate -- so one tap is one intent at one instant.
    //   * SendShipState streamed firing = (now - NetLastFireMs < 150 ms) -- a flat window.
    //   * The peer reads buffer.Newest.Firing EVERY tick and re-fires through a gate whose
    //     period it set from the SAME packet (1000/shotsPerSec = 125 ms at the default 8/s).
    // 150 ms of firing=true in front of a 125 ms gate spawns 1 + floor(150/125) = 2 bullets. The
    // second one is a real bullet in the peer's world and damages what it hits, which is exactly
    // the card's other symptom: "P1 can kill an enemy on P2's screen that is alive on P1's".
    //
    // THE FIX IS A BOUND ON THE HOLD, WHICH IS WHY LEG 1 IS SHAPED THE WAY IT IS. The peer holds
    // the newest sample until a newer one arrives, so a hold of H ms puts firing=true in front of
    // the re-fire gate for `ceil(H / I) * I` ms over there, where I is the REAL send interval --
    // not for H ms. `FiringHoldMsFor` returns P/2 (floored at one send interval, capped at the old
    // 150), because if I >= H that window is I and if I < H it is under H + I < 2H = P: safe at
    // EVERY send interval below the cadence period.
    //
    // TWO WRONG ANSWERS WERE MEASURED FIRST, and leg 1 exists to catch both. A plain
    // fraction-of-the-period hold (0.6 * the 62.5 ms period at 16 shots/s = 37.5 ms) still catches
    // TWO 33 ms-apart sends. Counting whole NOMINAL 33 ms packets is right at 60 Hz and wrong
    // everywhere else: the send gate is evaluated once per frame, so the real interval is 40 ms at
    // 100 Hz, and that over-fires at 7, 9, 10, 13, 14 and 15 shots/sec. So leg 1 sweeps the send
    // INTERVAL as well as the fire rate.
    //
    // WHAT EACH LEG IS FOR.
    //   1  the contract, as a pure decision: the marked-packet window must stay under one cadence
    //      period, for every reachable shotsPerSec CROSSED WITH every send interval a real frame
    //      rate produces. The PRE-CARD flat 150 ms hold is computed beside it as the negative
    //      control and must VIOLATE it -- so a predicate that had degenerated into "always true"
    //      cannot pass this leg.
    //   2  the symptom, end to end over a real NetWire: a scripted peer taps once, the real
    //      remote puppet is driven at 60 Hz, and the bullets it spawns are COUNTED. Exactly 1.
    //   3  the same at shotsPerSec 18 -- the tightest period (55.6 ms), where the fix has the
    //      least room and the residual lives.
    //   4  THE NEGATIVE CONTROL FOR LEG 2, and the reason a green run here means anything: the
    //      identical rig is fed the packet pattern the PRE-CARD sender produced for one tap
    //      (5 marked packets instead of 2) and must report 2 bullets. If leg 4 ever reports 1,
    //      the rig has stopped being able to see the bug and legs 2-3 are worthless.
    //   5  the overcorrection guard: a sustained hold must still produce the OWNER'S cadence,
    //      not one bullet per packet and not zero. Without it, "always 1 bullet" -- a re-fire
    //      that had stopped working entirely -- would pass legs 2-4.
    //
    // LEG 1 IS THE RIGOROUS HALF AND LEGS 2-4 ARE PHASE SAMPLES -- MEASURED, not asserted. The rig
    // sends at ONE cadence (60 Hz, SendIntervalMs) with each packet on a tick boundary, so the
    // end-to-end legs see one phase of the tap-vs-send-vs-cadence alignment and a hold that is
    // only marginally too long can land on the safe side of it. Mutation-tested: the 0.6-fraction
    // hold fails leg 1 at 16/s while legs 2-5 ALL PASS, the 18/s one included, because its second
    // bullet falls one tick outside the burst. So do not read a green leg 3 as covering the top of
    // the range, and do not read any of 2-5 as covering a non-60 Hz sender; leg 1 covers both.
    //
    // THE SENDER-SIDE STAMP IS COVERED BY LEG 1 ONLY, on purpose. PlayerShip.FireAt reads
    // Environment.TickCount64 directly while NetSession reads NetHost.Current, so driving the
    // send half end to end on a pinned clock would need a clock seam on FireAt whose only reader
    // is this suite -- the same call declined on card d53431b4's mute half (no cue counter in
    // SoundManager for one test). Leg 1 tests the decision the stamp feeds instead.
    //
    // *** DESTRUCTIVE, like eaNetPickup / eaNetResetSpawn. *** It pairs a real session onto the
    // live level, seats a Remote puppet and fires real Bullets into the live world. Run it in a
    // throwaway ?level=Level2&invuln boot, never in a game you care about. Teardown stops the
    // session, sweeps the bullets it spawned and frees the Remote seat.
    internal static class NetFireTest
    {
        private const string Room = "netfire";
        private const ulong PeerToken = 0x1A5E27C0UL;

        // The pre-card sender: a flat 150 ms hold regardless of fire rate. Leg 1's negative
        // control and leg 4's input, so it is named once rather than spelled twice.
        private const float PreCardHoldMs = 150f;

        // One game tick at 60 Hz. The rig drives the puppet by hand at exactly this rate so the
        // bullet counts are a function of the scripted packets and nothing else.
        private const float TickMs = 1000f / 60f;

        // Ticks per send, and the send interval that follows from it. The production gate fires on
        // the first FRAME at or past StreamIntervalMs, so at 60 Hz that is 2 ticks / 33.33 ms.
        private const int TicksPerSend = 2;
        private const float SendIntervalMs = TicksPerSend * TickMs;

        // Where the scripted peer's ship sits. Off to one side of the play area and away from the
        // local ship, so its bullets meet nothing on their way out.
        private static readonly Vector2 PeerAt = new Vector2(120f, 480f);

        public static string Run()
        {
            StringBuilder sb = new StringBuilder("[netfire] single-tap bullet count (card a5c2a39b)\n");
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            // Leg 1 needs no world at all, so it runs before the gate -- the contract is still
            // worth reporting from a menu boot even though the end-to-end legs are not reachable.
            LegContract(sb, Check);

            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP legs 2-5 (need a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP legs 2-5 (a co-op session is already up -- this suite would tear it down)\n");
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
                RunLegs(sb, Check, oracle, bin, game, clock);
            }
            catch (Exception ex)
            {
                Check("the legs ran (" + Describe(ex) + ")", false);
            }
            finally
            {
                sb.Append(" 6. teardown\n");
                Teardown(sb, Check, oracle, bin, playersBefore);
                NetHost.Current = hostBefore;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the contract, as a pure decision --------------------------------------------

        // The send intervals to hold the contract against. `SendShipState` runs off a
        // `now - lastStreamTx >= StreamIntervalMs` gate evaluated ONCE PER FRAME, so the real
        // interval is the smallest frame multiple >= 33 -- never the nominal 33.0. These are the
        // values that produces at the refresh rates this game is played at: 60 Hz (2 frames),
        // 100 Hz (4), 75 Hz (3 x 13.3), 50 Hz (2), 30 Hz (2) and a doubled 60 Hz frame.
        //
        // SWEEPING THIS IS THE POINT OF THE LEG. Asserting only at the nominal 33 would restate
        // FiringHoldMsFor's own arithmetic back at it, and the first version of this card did
        // exactly that: it counted whole 33 ms packets, passed at 60 Hz, and over-fired at 7, 9,
        // 10, 13, 14 and 15 shots/sec on a 100 Hz display.
        private static readonly float[] SendIntervalsMs = { 33.3333f, 40f, 39.9999f, 50f, 66.6667f, 55f };

        private static void LegContract(StringBuilder sb, Action<string, bool> Check)
        {
            sb.Append(" 1. the hold-window contract over every fire rate AND every send interval\n");
            int violations = 0;
            int preCardViolations = 0;
            string firstViolation = null;
            foreach (float interval in SendIntervalsMs)
            {
                for (int shots = 1; shots <= 18; shots++)
                {
                    float period = 1000f / shots;
                    // A send interval at or past the cadence period cannot represent that cadence
                    // at all (documented residual, below ~18 fps at the top fire rate) -- so the
                    // contract is only claimed where the sender can actually keep up.
                    if (interval >= period)
                    {
                        continue;
                    }
                    if (MarkedWindowMs(NetSession.FiringHoldMsFor(shots), interval) >= period)
                    {
                        violations++;
                        firstViolation ??= shots + "/s at " + interval.ToString("0.##") + "ms sends";
                    }
                    if (MarkedWindowMs(PreCardHoldMs, interval) >= period)
                    {
                        preCardViolations++;
                    }
                }
            }
            Check("no fire rate spans a cadence period at ANY send interval"
                + (violations == 0 ? "" : " -- first violation at " + firstViolation),
                violations == 0);
            // The negative control. Without it a FiringHoldMsFor that had collapsed to something
            // trivially small would sail through the leg above while dropping every tap.
            Check("NEGATIVE the pre-card flat " + PreCardHoldMs + " ms hold DOES span one"
                + " (violates in " + preCardViolations + " rate/interval combinations)",
                preCardViolations > 0);
            Check("... and it violates at the DEFAULT 8 shots/sec on a 60 Hz sender -- the"
                + " reported case", MarkedWindowMs(PreCardHoldMs, 33.3333f) >= 1000f / 8f);
            // The other half of the trade: a hold shorter than one send interval would expire
            // between two packets and lose the tap outright.
            int unsendable = 0;
            for (int shots = 1; shots <= 18; shots++)
            {
                if (NetSession.FiringHoldMsFor(shots) < NetSession.StreamIntervalMs)
                {
                    unsendable++;
                }
            }
            Check("every fire rate still holds for at least one nominal send interval, so no tap"
                + " goes unsent (unsendable=" + unsendable + ")", unsendable == 0);
            sb.Append("    holds: 8/s=" + NetSession.FiringHoldMsFor(8) + "ms, 15/s="
                + NetSession.FiringHoldMsFor(15) + "ms, 18/s=" + NetSession.FiringHoldMsFor(18)
                + "ms (floor " + NetSession.StreamIntervalMs + ", ceiling " + PreCardHoldMs + ")\n");
        }

        // Worst case (the tap landing the instant after a send): a hold of h ms marks
        // ceil(h / interval) sends, and the peer then sees firing=true for that many INTERVALS,
        // because it holds the newest sample until the next one lands. This is the quantity the
        // cadence period has to beat -- and the quantity a hold reasoned about purely in ms, or
        // against the nominal interval alone, gets wrong.
        private static float MarkedWindowMs(float holdMs, float intervalMs)
        {
            return MarkedPackets(holdMs, intervalMs) * intervalMs;
        }

        private static int MarkedPackets(float holdMs, float intervalMs)
        {
            return Math.Max(1, (int)Math.Ceiling(holdMs / intervalMs - 1e-4f));
        }

        // How many packets a hold marks at the cadence THIS RIG sends at -- what legs 2-4 have to
        // script to reproduce one tap.
        private static int RigPackets(float holdMs)
        {
            return MarkedPackets(holdMs, SendIntervalMs);
        }

        // ---- 2-5. the end-to-end legs -------------------------------------------------------

        private static void RunLegs(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game, PinnedNetHost clock)
        {
            sb.Append(" 2. rig -- a real HOST session, a scripted peer and its ship puppet\n");
            bool rosterOk = oracle.Players == 1 && oracle.IsSeated(0) && oracle.IsAlive(0);
            Check("PRECONDITION one local player at slot 0 with a live ship (players="
                + oracle.Players + ")", rosterOk);
            if (!rosterOk)
            {
                return;
            }

            // Carries the sub-millisecond remainder of the 33.33 ms send interval across packets,
            // so the pinned clock and the ticks stay in step over a whole burst.
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

            // The peer's ship stream is what makes SpawnPuppet seat a ControlDevice.Remote ship --
            // the real path, because the puppet's own Update is the code under test.
            Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry,
                firing: false, shotsPerSec: 8);
            int peerSlot = oracle.GetPlayerIndex(ControlDevice.Remote);
            bool puppetUp = NetSession.HasRemotePuppet && peerSlot >= 0;
            Check("the peer's ship puppet was adopted into a Remote seat (slot=" + peerSlot + ")",
                puppetUp);
            if (!puppetUp)
            {
                return;
            }
            PlayerShip puppet = FindShip(oracle, peerSlot);
            Check("PRECONDITION the puppet ship is reachable", puppet != null);
            if (puppet == null)
            {
                return;
            }

            // ---- 2. one tap at the default fire rate ----------------------------------------
            int fired = Tap(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq, ref shipMs, ref clockCarry,
                shotsPerSec: 8, markedPackets: RigPackets(NetSession.FiringHoldMsFor(8)));
            Check("a single tap at 8 shots/sec spawns exactly ONE bullet on the peer (got "
                + fired + ")", fired == 1);

            // ---- 3. the tightest period -----------------------------------------------------
            sb.Append(" 3. the same tap at the maxed fire rate (18/s, 55.6 ms period)\n");
            fired = Tap(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq, ref shipMs, ref clockCarry,
                shotsPerSec: 18, markedPackets: RigPackets(NetSession.FiringHoldMsFor(18)));
            Check("a single tap at 18 shots/sec spawns exactly ONE bullet (got " + fired + ")",
                fired == 1);

            // ---- 4. the negative control ----------------------------------------------------
            sb.Append(" 4. NEGATIVE -- the pre-card packet pattern for the SAME single tap\n");
            fired = Tap(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq, ref shipMs, ref clockCarry,
                shotsPerSec: 8, markedPackets: RigPackets(PreCardHoldMs));
            Check("the pre-card 150 ms hold spawns TWO bullets for one tap -- the reported bug,"
                + " and proof this rig can see it (got " + fired + ")", fired == 2);

            // ---- 5. the overcorrection guard ------------------------------------------------
            sb.Append(" 5. sustained fire still runs at the OWNER'S cadence\n");
            // One second of held trigger at 8 shots/sec. Every packet in the burst is marked,
            // exactly as the sender does while the trigger is genuinely down.
            const int burstPackets = 30;   // 30 x SendIntervalMs = 1000 ms of game time
            fired = Tap(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq, ref shipMs, ref clockCarry,
                shotsPerSec: 8, markedPackets: burstPackets);
            // 1000 ms of game time at 125 ms per shot -- the shoottimer the assertion is about
            // runs on the TICKS, not on the pinned net clock, so quote the game-time figure.
            // Bounded rather than pinned to a single number: the tick/packet phase decides whether
            // the last slot lands inside the burst.
            Check("~1 s of held fire spawns the owner's cadence, not one per packet (got "
                + fired + ", expected 7-9)", fired >= 7 && fired <= 9);
            Check("... which is far below one-bullet-per-packet (" + burstPackets + ")",
                fired < burstPackets / 2);
        }

        // Script one tap (or one burst): `markedPackets` packets carrying firing=true, then
        // enough clear packets for the peer to have stopped firing. Returns the bullets the
        // PUPPET spawned over the whole sequence.
        private static int Tap(InMemoryTransport peer, NetWire wire, ComponentBin bin, Game game,
            PinnedNetHost clock, PlayerShip puppet, int peerSlot, ref ushort shipSeq,
            ref uint shipMs, ref float clockCarry, int shotsPerSec, int markedPackets)
        {
            // Settle first: the previous leg's burst may have left the cadence gate mid-period,
            // which would move this leg's count. Six clear packets is ~200 ms -- enough for the
            // rates the legs actually use (8/s = 125 ms, 18/s = 55.6 ms), NOT for the whole 1..18
            // domain, whose slowest period is a full second. A future leg at a low fire rate must
            // raise this or it inherits a live cadence gate. The sweep below then starts the
            // count from a clean world.
            for (int i = 0; i < 6; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, false, shotsPerSec);
                DriveTicks(puppet, bin);
            }
            SweepBullets(bin, game, peerSlot);

            for (int i = 0; i < markedPackets; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, true, shotsPerSec);
                DriveTicks(puppet, bin);
            }
            // Let the re-fire finish: the peer keeps holding the last marked sample until a clear
            // one arrives, and the bullet it owes for it may land a tick later.
            for (int i = 0; i < 4; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, false, shotsPerSec);
                DriveTicks(puppet, bin);
            }

            int fired = Census(game, peerSlot);
            SweepBullets(bin, game, peerSlot);
            return fired;
        }

        // One ship-state packet, delivered and drained -- the real codec onto the real wire.
        //
        // THE CLOCK ADVANCE MUST MATCH THE TICKS, or the rig runs a cadence production cannot
        // emit. A send interval is the smallest whole number of frames >= StreamIntervalMs, i.e.
        // TicksPerSend ticks = 33.33 ms at 60 Hz -- NOT the nominal 33. Advancing the pinned
        // clock by 33 while DriveTicks advanced game time by 33.33 left the two 1% apart and the
        // packets landing where the sender never puts them. The clock is whole ms, so the
        // fraction is carried rather than truncated per packet.
        private static void Deliver(InMemoryTransport peer, NetWire wire, PinnedNetHost clock,
            ref ushort shipSeq, ref uint shipMs, ref float clockCarry, bool firing, int shotsPerSec)
        {
            clockCarry += SendIntervalMs;
            long step = (long)clockCarry;
            clockCarry -= step;
            shipMs += (uint)step;
            peer.SendStream(NetProtocol.EncodeShipState(shipSeq++, shipMs, PeerAt, Vector2.Zero,
                4.712389f, alive: true, firing: firing, shotsPerSec, 450f));
            wire.Pump();
            clock.Advance(step);
            NetSession.Update();
        }

        // The two game ticks that fill one send interval. DriveRemoteShip runs from the puppet's
        // own Update, so this is the real per-tick re-fire path and not a stand-in for it.
        private static void DriveTicks(PlayerShip puppet, ComponentBin bin)
        {
            GameTime gt = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            for (int i = 0; i < 2; i++)
            {
                puppet.Update(gt);
                bin.TopOfTickFlush();
            }
        }

        private static int Census(Game game, int slot)
        {
            int n = 0;
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is Bullet && ((Bullet)item).Player() == slot)
                {
                    n++;
                }
            }
            return n;
        }

        private static void SweepBullets(ComponentBin bin, Game game, int slot)
        {
            List<GameComponent> doomed = new List<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is Bullet && ((Bullet)item).Player() == slot)
                {
                    doomed.Add(item);
                }
            }
            foreach (GameComponent comp in doomed)
            {
                bin.Remove(comp);
            }
            bin.TopOfTickFlush();
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
                    NetSession.Stop("fire suite teardown");
                }
                Check("the session is stopped", !NetSession.Active);
                // The puppet ship and its seat, which Stop() does not unwind -- only the peer-loss
                // paths do, and nothing here goes through one. Its bullets go with it: they are
                // live, damaging components in the level this suite was run in.
                foreach (PlayerShip s in new List<PlayerShip>(oracle.GetShips()))
                {
                    if (s.Controller == ControlDevice.Remote
                        || s.Controller == ControlDevice.RemoteFriend)
                    {
                        SweepBullets(bin, bin.Game, s.Owner);
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
                Check("teardown ran (" + Describe(ex) + ")", false);
            }
        }

        private static string Describe(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        private static string Tally(int pass, int fail)
        {
            return "[netfire] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
