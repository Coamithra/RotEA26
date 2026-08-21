using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // eaNetFire() -- the verification for the co-op fire replication: the bullets a peer sees must
    // be exactly the bullets their owner fired, one for one, however the packets fall. Run it
    // inside a level, or `eval NetFire` under eahl. Committed as
    // tools/headless/probes/net_single_tap.txt.
    //
    // THE ORIGINAL BUG (card a5c2a39b) was arithmetic: the wire carried `firing` as a LEVEL, the
    // peer re-fired from it through a cadence gate it set from the SAME packet, and a flat 150 ms
    // hold in front of a 125 ms period spawned 1 + floor(150/125) = TWO bullets for one tap (three
    // at the maxed 18/s). Those are real bullets in the peer's world and damage what they hit,
    // which is the card's other symptom -- "P1 can kill an enemy on P2's screen that is alive on
    // P1's". That card bounded the hold at P/2 and left two residuals it could not close.
    //
    // THIS SUITE NOW COVERS THE REPLACEMENT (card a45b78f6): a CUMULATIVE u8 shot count in
    // MsgShipState, incremented inside PlayerShip.FireAt's cadence gate beside the Bullet it
    // counts, spent by the receiver as a wrapped DELTA through the same shot construction with no
    // second gate. Both residuals fall out of that rather than being traded against each other:
    //   * a dropped or reordered stream packet costs nothing -- the count is cumulative, so the
    //     next packet carries the total (leg 5);
    //   * two taps inside one cadence period are ONE increment, because the increment happens
    //     where the bullet does, not where the intent does (leg 2).
    //
    // WHAT EACH LEG IS FOR.
    //   1  the delta arithmetic as a PURE DECISION, swept over the whole 0..255 wrap domain plus
    //      the resync bound. Phase-independent, so it is the rigorous half: legs 3-6 script one
    //      packet cadence and therefore sample one phase of it.
    //   2  THE SENDER, which the pre-card design could not test at all (its stamp read
    //      Environment.TickCount64, so driving it needed a clock seam on FireAt whose only reader
    //      would have been this suite). The counter has no clock, so the real local ship is driven
    //      through the real input path and its increments are compared against the bullets it
    //      really spawned -- including the two-taps-in-one-period case, which must be ONE of each.
    //   3  the symptom end to end over a real NetWire: a scripted peer's count goes up by one, the
    //      real remote puppet is driven at 60 Hz, and the bullets it spawns are COUNTED. Exactly 1.
    //   4  the same at the maxed fire rate, plus a count that arrives +2 at once (two taps a full
    //      period apart, i.e. two real bullets) which must produce exactly 2.
    //   5  PACKET LOSS mid-burst -- the leg the old design could not have passed. Four of the
    //      burst's packets never reach the wire and the count is still exact.
    //   6  sustained fire runs at the OWNER'S cadence, now as an EXACT number rather than a band:
    //      the peer fires what the counter says and nothing else.
    //
    // THE NEGATIVE CONTROL IS A REFERENCE IMPLEMENTATION, not the old code (which is deleted).
    // `PreCardTapBullets` / `PreCardLossBullets` are pure mirrors of the firing-LEVEL rule, run on
    // the same inputs as legs 3 and 5, and asserted to give the WRONG answer -- 2 bullets for one
    // tap and a short count under loss. Without them a green run says nothing: a rig that had
    // stopped exercising the fire path at all would report 1 bullet everywhere. (The
    // CollisionBench.ReferencePass idiom.)
    //
    // *** DESTRUCTIVE, like eaNetPickup / eaNetResetSpawn. *** It pairs a real session onto the
    // live level, seats a Remote puppet, fires real Bullets into the live world and drives the
    // LOCAL player's ship through scripted input. Run it in a throwaway ?level=Level2&invuln boot,
    // never in a game you care about. Teardown stops the session, sweeps the bullets it spawned,
    // releases the scripted input and frees the Remote seat.
    internal static class NetFireTest
    {
        private const string Room = "netfire";
        private const ulong PeerToken = 0x1A5E27C0UL;

        // The pre-card sender: a flat 150 ms hold regardless of fire rate, in front of a
        // 1000/shotsPerSec re-fire gate. The reference implementation's input.
        private const float PreCardHoldMs = 150f;

        // PlayerShip's catch-up bound, restated for the messages. Kept as a literal rather than a
        // second internal accessor: leg 1 asserts the real predicate's behaviour AT this value, so
        // a drift between the two fails the leg rather than hiding in it.
        private const int MaxCatchUp = 6;

        // One game tick at 60 Hz. The rig drives the ships by hand at exactly this rate so the
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
            StringBuilder sb = new StringBuilder("[netfire] replicated shot count (cards a5c2a39b / a45b78f6)\n");
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            // Leg 1 needs no world at all, so it runs before the gate -- the arithmetic is still
            // worth reporting from a menu boot even though the live legs are not reachable.
            LegDelta(sb, Check);

            if (GameScene.NetActiveScene == null)
            {
                sb.Append("  SKIP legs 2-6 (need a live level -- boot ?level=Level2&invuln and run it there)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }
            if (NetSession.Active)
            {
                sb.Append("  SKIP legs 2-6 (a co-op session is already up -- this suite would tear it down)\n");
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
                LegSender(sb, Check, oracle, bin, game);
                RunLegs(sb, Check, oracle, bin, game, clock);
            }
            catch (Exception ex)
            {
                Check("the legs ran (" + Describe(ex) + ")", false);
            }
            finally
            {
                sb.Append(" 8. teardown\n");
                Teardown(sb, Check, oracle, bin, playersBefore);
                NetHost.Current = hostBefore;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the delta arithmetic, as a pure decision --------------------------------------

        private static void LegDelta(StringBuilder sb, Action<string, bool> Check)
        {
            sb.Append(" 1. the wrapped-delta decision over the whole u8 domain\n");
            // Every (last, received) pair in the domain: a delta the receiver will SPEND must
            // equal the unsigned distance the counter really moved, and a step past the catch-up
            // bound must be refused outright. Sweeping it is the point -- the live legs below only
            // ever produce the handful of deltas their scripted bursts happen to contain.
            int wrong = 0;
            int spendable = 0;
            string firstWrong = null;
            for (int last = 0; last < 256; last++)
            {
                for (int step = 0; step < 256; step++)
                {
                    byte received = (byte)(last + step);
                    int delta = PlayerShip.NetShotDelta(received, (byte)last, out bool resync);
                    bool ok = resync ? (step > MaxCatchUp && delta == 0) : (delta == step);
                    if (!ok)
                    {
                        wrong++;
                        firstWrong ??= "last=" + last + " received=" + received;
                    }
                    if (!resync) { spendable++; }
                }
            }
            Check("every counter step in 0..255 is spent exactly once, or refused as a resync"
                + (wrong == 0 ? "" : " -- first wrong at " + firstWrong), wrong == 0);
            // The wrap itself, named rather than left inside the sweep -- it is the property a
            // plain subtraction would get wrong (255 -> 2 as a signed difference is -253), and
            // the one a reader comes here to check. Both cases are inside the catch-up bound, so
            // they are really SPENT rather than refused.
            Check("the counter WRAPS: 255 -> 2 is 3 shots, 255 -> 0 is 1",
                PlayerShip.NetShotDelta(2, 255, out _) == 3
                && PlayerShip.NetShotDelta(0, 255, out _) == 1);
            // The bound, both sides of it. A resync must fire nothing: a peer whose ship respawned
            // restarts its counter at 0, which as a raw delta is a magazine we do not owe.
            Check("a step of " + MaxCatchUp + " is still catch-up and is fired in full",
                PlayerShip.NetShotDelta((byte)MaxCatchUp, 0, out bool atBound) == MaxCatchUp && !atBound);
            Check("a step of " + (MaxCatchUp + 1) + " is a resync: adopted, nothing fired",
                PlayerShip.NetShotDelta((byte)(MaxCatchUp + 1), 0, out bool overBound) == 0 && overBound);
            // Non-degeneracy AND the bound's independence from where the counter happens to
            // stand: exactly steps 0..MaxCatchUp are spendable, from EVERY one of the 256
            // starting values. Without this the sweep above would also pass on a predicate that
            // called everything a resync -- which would silently stop replicating fire entirely.
            Check("exactly the first " + (MaxCatchUp + 1) + " steps are spendable, from every"
                + " starting count (spendable=" + spendable + ")",
                spendable == 256 * (MaxCatchUp + 1));
            // The TX side of the same arithmetic: the count on the wire belongs to the SLOT, so a
            // ship swap must contribute nothing and the sequence must stay monotone across a
            // respawn. The case that matters is a ship dying near the top of the byte range --
            // 252 -> 0 is a wrapped delta of 4, INSIDE the catch-up bound, so a ship-local count
            // put straight on the wire would spawn four bullets nobody fired.
            byte lastShipShots = 0;
            byte wire = 0;
            NetSession.AdvanceTxShotCount(sameShip: false, shipShots: 0, ref lastShipShots, ref wire);
            NetSession.AdvanceTxShotCount(sameShip: true, shipShots: 252, ref lastShipShots, ref wire);
            byte afterLife1 = wire;
            // The ship dies at 252 and its replacement starts at 0, then fires three shots.
            NetSession.AdvanceTxShotCount(sameShip: false, shipShots: 0, ref lastShipShots, ref wire);
            Check("a ship SWAP contributes no shots to the slot's wire count (" + afterLife1
                + " -> " + wire + ")", wire == afterLife1);
            NetSession.AdvanceTxShotCount(sameShip: true, shipShots: 3, ref lastShipShots, ref wire);
            Check("the new ship's shots continue the slot's sequence (" + afterLife1 + " -> "
                + wire + ", i.e. +" + (byte)(wire - afterLife1) + ", want +3)",
                (byte)(wire - afterLife1) == 3);
            // NEGATIVE: the ship-local count put straight on the wire is what would have looked
            // like a real, spendable burst to the receiver.
            Check("NEGATIVE the raw ship count would have read as "
                + PlayerShip.NetShotDelta(0, 252, out _) + " spendable shots across that respawn",
                PlayerShip.NetShotDelta(0, 252, out bool rawResync) > 0 && !rawResync);

            // The reference implementation, on the reported case. It is the control legs 3 and 5
            // lean on, so it is asserted here too: a control that had stopped modelling the bug
            // would make their disagreement meaningless.
            int preCardTap = PreCardTapBullets(PreCardHoldMs, SendIntervalMs, 8);
            int preCardMaxed = PreCardTapBullets(PreCardHoldMs, SendIntervalMs, 18);
            Check("NEGATIVE the pre-card firing-LEVEL rule spawns " + preCardTap + " bullets for ONE"
                + " tap at 8 shots/sec (the reported bug)", preCardTap == 2);
            Check("... and " + preCardMaxed + " at the maxed 18 shots/sec", preCardMaxed == 3);
        }

        // ---- the reference implementation (the deleted firing-LEVEL rule) ---------------------

        // What the PRE-CARD sender+receiver pair produced for ONE tap: the sender marked
        // ceil(hold / interval) packets, the peer held each marked sample for one whole send
        // interval, and re-fired through a 1000/shotsPerSec gate -- so it spawned
        // 1 + floor(window / period) bullets. This is the arithmetic card a5c2a39b measured; it is
        // here as the thing the counter design has to beat, not as live code.
        private static int PreCardTapBullets(float holdMs, float intervalMs, int shotsPerSec)
        {
            float period = 1000f / Math.Max(shotsPerSec, 1);
            int markedPackets = Math.Max(1, (int)Math.Ceiling(holdMs / intervalMs - 1e-4f));
            float window = markedPackets * intervalMs;
            return 1 + (int)Math.Floor(window / period);
        }

        // What the same pair produced for a sustained burst under LOSS. A level says only "firing
        // now", so a packet that never arrives is a firing interval that never happened and the
        // peer simply fires fewer times: residual (a) of card a5c2a39b, and the reason the counter
        // design exists.
        private static int PreCardLossBullets(int ownerShots, int shotsInDroppedPackets)
        {
            return ownerShots - shotsInDroppedPackets;
        }

        // ---- 2. the sender ---------------------------------------------------------------------

        // The local ship, driven through the REAL input path (DebugInput -> InputHandler ->
        // PlayerShip.Update -> FireAt) so the counter is exercised exactly as play exercises it.
        // The subject is the pairing of the two statements inside FireAt's cadence gate: a bullet
        // and an increment, never one without the other.
        private static void LegSender(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game)
        {
            sb.Append(" 2. the SENDER -- the local ship's counter against the bullets it spawned\n");
            PlayerShip local = FindShip(oracle, 0);
            InputHandler input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
            bool ready = local != null && input != null;
            Check("PRECONDITION a live local ship at slot 0 and a reachable InputHandler", ready);
            if (!ready)
            {
                return;
            }
            try
            {
                // A single tap: one tick of held fire. One bullet, one increment.
                SweepBullets(bin, game, 0);
                byte before = local.NetShotCount;
                int fired = SenderTap(input, local, bin, game, gapTicks: 12);
                byte after = local.NetShotCount;
                Check("one tap spawns one bullet and moves the counter by one (bullets=" + fired
                    + " counter+" + (byte)(after - before) + ")",
                    fired == 1 && (byte)(after - before) == 1);

                // TWO taps inside ONE cadence period. The owner's gate swallows the second, so it
                // is one bullet -- and it must be ONE increment, or the peer fires a bullet that
                // never existed here. This is card a5c2a39b's second residual: the pre-card stamp
                // sat on the INTENT, before the gate, which is what made it two on the peer.
                SweepBullets(bin, game, 0);
                before = local.NetShotCount;
                fired = SenderDoubleTap(input, local, bin, game, gapTicks: 3);
                after = local.NetShotCount;
                Check("two taps INSIDE one cadence period are one bullet AND one increment"
                    + " (bullets=" + fired + " counter+" + (byte)(after - before) + ")",
                    fired == 1 && (byte)(after - before) == 1);

                // The control for it: the same two taps a full period apart really are two of
                // each. Without this a counter wired never to increment would pass the leg above.
                SweepBullets(bin, game, 0);
                before = local.NetShotCount;
                fired = SenderDoubleTap(input, local, bin, game, gapTicks: 12);
                after = local.NetShotCount;
                Check("CONTROL two taps a full period APART are two bullets and two increments"
                    + " (bullets=" + fired + " counter+" + (byte)(after - before) + ")",
                    fired == 2 && (byte)(after - before) == 2);

                // A held trigger: one increment per bullet over a run long enough that an
                // off-by-one per shot would show.
                SweepBullets(bin, game, 0);
                before = local.NetShotCount;
                fired = SenderHold(input, local, bin, game, ticks: 60);
                after = local.NetShotCount;
                Check("a held trigger increments once per bullet over a full second (bullets="
                    + fired + " counter+" + (byte)(after - before) + ")",
                    fired > 1 && (byte)(after - before) == fired);

                // The roll RING records each outcome beside the bullet it belongs to (card
                // 950bb70a). Range level 2 makes the bounce roll DETERMINISTIC (100%), and
                // FirePower level 3 gives the asplode roll a 60% mix, so thirty taps see both
                // outcomes (P of a one-sided run ~ 2e-7) while every tap's ring bit 0 must equal
                // its own bullet's flag. This buffs the live ship's loadout for the rest of its
                // life -- the suite is destructive-throwaway, and a respawn's Setup resets it.
                local.PowerUp(Powerup.PowerupType.FirePower, 3, doEffect: false);
                local.PowerUp(Powerup.PowerupType.Range, 2, doEffect: false);
                bool paired = true;
                bool sawAsplode = false;
                bool sawPlain = false;
                bool bounceAll = true;
                for (int i = 0; i < 30 && paired; i++)
                {
                    SweepBullets(bin, game, 0);
                    DebugInput.Hold("Mouse1", down: true);
                    SenderTicks(input, local, bin, 1);
                    DebugInput.Hold("Mouse1", down: false);
                    Bullet b = FindBullet(game, 0);
                    bool ringA = (local.NetAsplodeBits & 1) != 0;
                    bool ringB = (local.NetBounceBits & 1) != 0;
                    paired = Census(game, 0) == 1 && b != null
                        && b.NetAsploding == ringA && b.NetBouncing == ringB;
                    sawAsplode |= ringA;
                    sawPlain |= !ringA;
                    bounceAll &= ringB;
                    SenderTicks(input, local, bin, 12);
                }
                Check("every tap's ring bit 0 equals its own bullet's flags over 30 mixed rolls",
                    paired);
                Check("the deterministic 100% bounce roll reads 1 on every tap", bounceAll);
                Check("both asplode outcomes appeared at 60% (asplode=" + sawAsplode
                    + " plain=" + sawPlain + ")", sawAsplode && sawPlain);
            }
            finally
            {
                DebugInput.Hold("Mouse1", down: false);
                SweepBullets(bin, game, 0);
            }
        }

        private static int SenderTap(InputHandler input, PlayerShip ship, ComponentBin bin,
            Game game, int gapTicks)
        {
            DebugInput.Hold("Mouse1", down: true);
            SenderTicks(input, ship, bin, 1);
            DebugInput.Hold("Mouse1", down: false);
            SenderTicks(input, ship, bin, gapTicks);
            return Census(game, 0);
        }

        private static int SenderDoubleTap(InputHandler input, PlayerShip ship, ComponentBin bin,
            Game game, int gapTicks)
        {
            DebugInput.Hold("Mouse1", down: true);
            SenderTicks(input, ship, bin, 1);
            DebugInput.Hold("Mouse1", down: false);
            SenderTicks(input, ship, bin, gapTicks);
            DebugInput.Hold("Mouse1", down: true);
            SenderTicks(input, ship, bin, 1);
            DebugInput.Hold("Mouse1", down: false);
            SenderTicks(input, ship, bin, 12);
            return Census(game, 0);
        }

        private static int SenderHold(InputHandler input, PlayerShip ship, ComponentBin bin,
            Game game, int ticks)
        {
            DebugInput.Hold("Mouse1", down: true);
            SenderTicks(input, ship, bin, ticks);
            DebugInput.Hold("Mouse1", down: false);
            SenderTicks(input, ship, bin, 2);
            return Census(game, 0);
        }

        // One tick of the sender: the real InputHandler poll (which is what drains the scripted
        // hold) followed by the ship's own Update, which is where FireAt lives.
        private static void SenderTicks(InputHandler input, PlayerShip ship, ComponentBin bin, int ticks)
        {
            GameTime gt = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            for (int i = 0; i < ticks; i++)
            {
                input.Update();
                ship.Update(gt);
                bin.TopOfTickFlush();
            }
        }

        // ---- 3-6. the end-to-end legs ---------------------------------------------------------

        private static void RunLegs(StringBuilder sb, Action<string, bool> Check, Oracle oracle,
            ComponentBin bin, Game game, PinnedNetHost clock)
        {
            sb.Append(" 3. rig -- a real HOST session, a scripted peer and its ship puppet\n");
            bool rosterOk = oracle.IsSeated(0) && oracle.IsAlive(0);
            Check("PRECONDITION a local player at slot 0 with a live ship (players="
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
            byte shotCount = 0;

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
            Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, 8);
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

            // ---- 3. one shot at the default fire rate ---------------------------------------
            int fired = Burst(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq,
                ref shipMs, ref clockCarry, ref shotCount, shotsPerSec: 8,
                increments: new int[] { 1 }, drops: null);
            Check("one shot on the counter spawns exactly ONE bullet on the peer (got "
                + fired + "), where the pre-card firing LEVEL spawned "
                + PreCardTapBullets(PreCardHoldMs, SendIntervalMs, 8), fired == 1);

            // ---- 4. the tightest period, and a +2 step --------------------------------------
            sb.Append(" 4. the maxed fire rate (18/s), and two shots arriving as ONE step\n");
            fired = Burst(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq,
                ref shipMs, ref clockCarry, ref shotCount, shotsPerSec: 18,
                increments: new int[] { 1 }, drops: null);
            Check("one shot at 18 shots/sec spawns exactly ONE bullet (got " + fired + ")",
                fired == 1);
            fired = Burst(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq,
                ref shipMs, ref clockCarry, ref shotCount, shotsPerSec: 8,
                increments: new int[] { 2 }, drops: null);
            Check("a counter that moves by TWO in one packet spawns exactly two bullets (got "
                + fired + ")", fired == 2);

            // ---- 5. packet loss --------------------------------------------------------------
            sb.Append(" 5. PACKET LOSS mid-burst -- the count stays exact\n");
            // Ten packets, one shot each; four of them never reach the wire, in two runs of two so
            // the largest delta the receiver ever sees is 3 -- inside the catch-up bound, which is
            // the regime the claim is about (past it a resync is the correct answer, leg 1).
            int[] ones = new int[10];
            for (int i = 0; i < ones.Length; i++) { ones[i] = 1; }
            bool[] drops = new bool[10];
            drops[2] = drops[3] = drops[6] = drops[7] = true;
            int dropped = 0;
            foreach (bool d in drops) { if (d) { dropped++; } }
            Check("PRECONDITION the rig really drops packets (" + dropped + " of 10)", dropped == 4);
            fired = Burst(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq,
                ref shipMs, ref clockCarry, ref shotCount, shotsPerSec: 8,
                increments: ones, drops: drops);
            Check("10 owner shots over a lossy link are 10 bullets on the peer (got " + fired + ")",
                fired == 10);
            int preCardLoss = PreCardLossBullets(10, dropped);
            Check("NEGATIVE the pre-card firing LEVEL would have spawned only " + preCardLoss
                + " over the same loss", preCardLoss < 10);

            // ---- 6. sustained fire -----------------------------------------------------------
            sb.Append(" 6. sustained fire runs at the OWNER'S cadence, exactly\n");
            // A held trigger at 8 shots/sec over 32 packets (~1.07 s), spread the way a real
            // sender spreads it: a shot every 125 ms, i.e. every ~3.75 send intervals.
            int[] sustained = new int[32];
            int shotsSoFar = 0;
            for (int i = 0; i < sustained.Length; i++)
            {
                int want = (int)((i + 1) * SendIntervalMs / 125f);
                sustained[i] = want - shotsSoFar;
                shotsSoFar = want;
            }
            fired = Burst(peer, wire, bin, game, clock, puppet, peerSlot, ref shipSeq,
                ref shipMs, ref clockCarry, ref shotCount, shotsPerSec: 8,
                increments: sustained, drops: null);
            Check("~1 s of held fire spawns the owner's " + shotsSoFar + " shots exactly, not one"
                + " per packet (got " + fired + " over " + sustained.Length + " packets)",
                fired == shotsSoFar && shotsSoFar > 1);

            // ---- 7. the roll rings (card 950bb70a) --------------------------------------------
            sb.Append(" 7. the ROLL RINGS -- the puppet spends the owner's outcomes, not its own dice\n");
            // The puppet's own percentages are ZERO here (nothing set them), so before this card
            // NO bullet it spawned could ever asplode or bounce -- an asploding puppet bullet
            // below can only have come off the wire, which is what makes these legs the
            // discriminator against the pre-card re-roll (a pre-card build fails every "carries
            // the asplode" assertion and passes every "does not").
            List<RollFlags> flags = BurstRolls(peer, wire, bin, game, clock, puppet, peerSlot,
                ref shipSeq, ref shipMs, ref clockCarry, ref shotCount, inc: 1,
                asplodeBits: 0x01, bounceBits: 0x00, dropFirst: false);
            Check("a shot the owner rolled ASPLODE spawns an asploding, non-bouncing bullet",
                flags.Count == 1 && flags[0].Asplode && !flags[0].Bounce);
            flags = BurstRolls(peer, wire, bin, game, clock, puppet, peerSlot,
                ref shipSeq, ref shipMs, ref clockCarry, ref shotCount, inc: 1,
                asplodeBits: 0x00, bounceBits: 0x01, dropFirst: false);
            Check("a shot the owner rolled BOUNCE spawns a bouncing, non-asploding bullet",
                flags.Count == 1 && !flags[0].Asplode && flags[0].Bounce);
            // A +3 step in ONE packet: the ring is POSITIONAL (bit i = shot ShotCount-i), so the
            // three bullets must come out oldest-first carrying bits 2,1,0 -- a ring read off the
            // wrong end, or off the packet's arrival rather than the shot's distance from the
            // newest, scrambles this pattern.
            flags = BurstRolls(peer, wire, bin, game, clock, puppet, peerSlot,
                ref shipSeq, ref shipMs, ref clockCarry, ref shotCount, inc: 3,
                asplodeBits: 0x05, bounceBits: 0x02, dropFirst: false);
            Check("a +3 step spends bits 2/1/0 onto the three bullets in spawn order"
                + " (asplode " + Pattern(flags, asplode: true) + " want 101,"
                + " bounce " + Pattern(flags, asplode: false) + " want 010)",
                flags.Count == 3
                && flags[0].Asplode && !flags[1].Asplode && flags[2].Asplode
                && !flags[0].Bounce && flags[1].Bounce && !flags[2].Bounce);
            // LOSS: the packet that announced the first shot never arrives, and the next one's
            // ring still hands BOTH bullets their own roll -- the cumulative-count property,
            // extended to the outcomes.
            flags = BurstRolls(peer, wire, bin, game, clock, puppet, peerSlot,
                ref shipSeq, ref shipMs, ref clockCarry, ref shotCount, inc: 1,
                asplodeBits: 0x02, bounceBits: 0x01, dropFirst: true);
            Check("under LOSS the surviving packet's ring covers the dropped shot too"
                + " (older asplodes, newest bounces)",
                flags.Count == 2
                && flags[0].Asplode && !flags[0].Bounce
                && !flags[1].Asplode && flags[1].Bounce);
        }

        private struct RollFlags
        {
            public bool Asplode;
            public bool Bounce;
        }

        private static string Pattern(List<RollFlags> flags, bool asplode)
        {
            StringBuilder p = new StringBuilder();
            // Printed newest-first so it reads like the ring's own bit order.
            for (int i = flags.Count - 1; i >= 0; i--)
            {
                RollFlags f = flags[i];
                p.Append((asplode ? f.Asplode : f.Bounce) ? '1' : '0');
            }
            return p.ToString();
        }

        // Script one roll burst and return the puppet's new bullets' flags IN SPAWN ORDER --
        // the puppet spends one owed shot per tick, so ticking one send interval at a time and
        // scanning for the newcomer after each is what recovers the order the ring is keyed on.
        // The quiet drain packets repeat the SAME count and rings: the count has not moved, so
        // the bit positions of the still-owed shots are unchanged -- exactly what a real owner's
        // stream does between shots (the ring only shifts when a shot is counted).
        private static List<RollFlags> BurstRolls(InMemoryTransport peer, NetWire wire,
            ComponentBin bin, Game game, PinnedNetHost clock, PlayerShip puppet, int peerSlot,
            ref ushort shipSeq, ref uint shipMs, ref float clockCarry, ref byte shotCount,
            int inc, byte asplodeBits, byte bounceBits, bool dropFirst)
        {
            for (int i = 0; i < 4; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, 8);
                DriveTicks(puppet, bin);
            }
            SweepBullets(bin, game, peerSlot);
            List<RollFlags> flags = new List<RollFlags>();
            HashSet<Bullet> seen = new HashSet<Bullet>();
            int expected = inc + (dropFirst ? 1 : 0);
            if (dropFirst)
            {
                shotCount = (byte)(shotCount + 1);
                Skip(clock, ref shipSeq, ref shipMs, ref clockCarry);
            }
            shotCount = (byte)(shotCount + inc);
            Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, 8,
                asplodeBits, bounceBits);
            GameTime gt = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            for (int p = 0; p < 10 && flags.Count < expected; p++)
            {
                for (int t = 0; t < TicksPerSend; t++)
                {
                    puppet.Update(gt);
                    bin.TopOfTickFlush();
                    foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
                    {
                        if (item is Bullet b && b.Player() == peerSlot && seen.Add(b))
                        {
                            flags.Add(new RollFlags { Asplode = b.NetAsploding, Bounce = b.NetBouncing });
                        }
                    }
                }
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, 8,
                    asplodeBits, bounceBits);
            }
            SweepBullets(bin, game, peerSlot);
            return flags;
        }

        // Script a burst: one packet per entry of `increments`, each advancing the peer's
        // cumulative counter by that many shots, with `drops[i]` suppressing the SEND -- the
        // counter still moves, which is the whole point. Then enough quiet packets for the puppet
        // to have spent everything it owes. Returns the bullets the PUPPET spawned.
        private static int Burst(InMemoryTransport peer, NetWire wire, ComponentBin bin, Game game,
            PinnedNetHost clock, PlayerShip puppet, int peerSlot, ref ushort shipSeq,
            ref uint shipMs, ref float clockCarry, ref byte shotCount, int shotsPerSec,
            int[] increments, bool[] drops)
        {
            // Settle: a few packets with the counter unchanged, so the puppet enters the burst
            // owing nothing from the previous leg.
            for (int i = 0; i < 4; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, shotsPerSec);
                DriveTicks(puppet, bin);
            }
            SweepBullets(bin, game, peerSlot);

            for (int i = 0; i < increments.Length; i++)
            {
                shotCount = (byte)(shotCount + increments[i]);
                if (drops == null || !drops[i])
                {
                    Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, shotsPerSec);
                }
                else
                {
                    // A dropped packet still costs its interval of wall clock and its ticks -- the
                    // sender sent it, the wire ate it.
                    Skip(clock, ref shipSeq, ref shipMs, ref clockCarry);
                }
                DriveTicks(puppet, bin);
            }
            // Let the puppet finish: it spends at most one owed shot per tick, so a burst that
            // arrived bunched needs a few quiet packets to drain.
            for (int i = 0; i < 8; i++)
            {
                Deliver(peer, wire, clock, ref shipSeq, ref shipMs, ref clockCarry, shotCount, shotsPerSec);
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
        // TicksPerSend ticks = 33.33 ms at 60 Hz -- NOT the nominal 33. The clock is whole ms, so
        // the fraction is carried rather than truncated per packet.
        private static void Deliver(InMemoryTransport peer, NetWire wire, PinnedNetHost clock,
            ref ushort shipSeq, ref uint shipMs, ref float clockCarry, byte shotCount, int shotsPerSec,
            byte asplodeBits = 0, byte bounceBits = 0)
        {
            long step = AdvanceClock(ref shipMs, ref clockCarry);
            peer.SendStream(NetProtocol.EncodeShipState(1, primary: true, shipSeq++, shipMs, PeerAt, Vector2.Zero,
                4.712389f, alive: true, shotCount: shotCount, shotsPerSec: shotsPerSec, bulletLife: 450f,
                scriptGate: false, asplodeBits: asplodeBits, bounceBits: bounceBits));
            wire.Pump();
            clock.Advance(step);
            NetSession.Update();
        }

        // A packet the sender sent and the stream lane lost: time passes, the sequence and the
        // sample clock move on, nothing arrives.
        private static void Skip(PinnedNetHost clock, ref ushort shipSeq, ref uint shipMs,
            ref float clockCarry)
        {
            long step = AdvanceClock(ref shipMs, ref clockCarry);
            shipSeq++;
            clock.Advance(step);
            NetSession.Update();
        }

        private static long AdvanceClock(ref uint shipMs, ref float clockCarry)
        {
            clockCarry += SendIntervalMs;
            long step = (long)clockCarry;
            clockCarry -= step;
            shipMs += (uint)step;
            return step;
        }

        // The two game ticks that fill one send interval. DriveRemoteShip runs from the puppet's
        // own Update, so this is the real per-tick apply path and not a stand-in for it.
        private static void DriveTicks(PlayerShip puppet, ComponentBin bin)
        {
            GameTime gt = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            for (int i = 0; i < TicksPerSend; i++)
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

        private static Bullet FindBullet(Game game, int slot)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is Bullet b && b.Player() == slot)
                {
                    return b;
                }
            }
            return null;
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
                DebugInput.Hold("Mouse1", down: false);
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
                // The LOCAL ship's bullets too -- leg 2 fires real ones out of the player's ship.
                SweepBullets(bin, bin.Game, 0);
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
