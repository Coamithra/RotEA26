using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using EvilAliensWeb.Compat.Net.Descriptors;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for card eb057163 -- the enemy laser-charge glow's AIM on a join peer.
    // Invoke with eaNetChargeAim() / `eval NetChargeAim`; MENU-runnable and leave-no-trace, the
    // eaNetStale / eaNetWalls shape.
    //
    // THE REPORT was "the twin motherships in level 2 do not change where they are aiming visually
    // as their target moves while they charge their laser", seen on P2. Two mechanisms produce
    // that sentence and they need OPPOSITE fixes, so this suite's first job is to tell them apart
    // rather than to assert a fix:
    //
    //   FREEZE     -- the client's aim is written once and never again. Something in the
    //                 encode/apply/drive chain is latching, and the fix is wherever it latches.
    //   STALENESS  -- the aim is re-applied correctly but only on that boss's round-robin
    //                 SNAPSHOT TURN, so it is a staircase: dead still for a turn, then a jump.
    //                 Nothing is broken per entity; the fix is to make the client SWEEP between
    //                 the values it is told rather than teleport between them.
    //
    // Section 1 is that measurement and it stays here permanently as the control -- it is what
    // stops "the fixed aim moves smoothly" passing on a build where the aim never moves at all.
    //
    // WHAT IT MEASURED (2026-08-04, on `main` f2c95ae): STALENESS. See the section.
    //
    // WHY IT IS A REAL SESSION and not a direct NetPuppets poke: the aim's whole problem is the
    // CADENCE it arrives at, so the rig has to carry a real turn structure -- a scripted host
    // writing real MsgWorldSnapshot packets over a NetWire into a real client NetSession, with
    // NetPuppets.Drive ticked at frame rate BETWEEN the packets. That is the NetStaleTest
    // section-2 / NetScenarioTest scenario-5 shape.
    //
    // MarsBoss is the reported emitter and the one used here, but the aim lives in the shared
    // Compat/Net/NetChargeGlow that all five charge emitters call (MarsBoss, SweepUFO,
    // SpiderHelperMothership, UFO, JunkBoss), so section 4 checks the fix is a no-op on an
    // emitter whose offset does NOT track anything.
    internal static class NetChargeAimTest
    {
        private const string Room = "chargeaim";

        // Far above any id a live session reaches (AllocId counts from 1), and disjoint from
        // NetStaleTest's 604xx / NetWallTest's 603xx blocks so the suites can run back to back.
        private const ushort IdBoss = 60501;

        private const ulong PeerToken = 0xEB057163UL;

        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // The host's charge window (MarsBoss.Update: BossState.hover -> charge sets 2500ms) and a
        // representative round-robin turn. The turn is NOT 60ms: SnapshotMaxEntries is 16 per
        // 60ms packet, so a ~40-entity Level 2 puts a given boss's turn at ~150ms. Held here as
        // the conditions the card's numbers are quoted against.
        private const float ChargeMs = 2500f;
        private const float TurnMs = 150f;
        private const float TickMs = 16.7f;

        // The aim sweep the scripted host plays: a target crossing in front of the boss, so the
        // 100px-magnitude offset (MarsBoss aims `normalize(target - Position) * 100`) rotates
        // +/-30 degrees over the charge. A straight-line offset would make "did it move" trivially
        // true in a way a real chase is not.
        private const float SweepHalfAngle = 0.5235988f; // 30 degrees
        private const float AimRadius = 100f;

        // The MarsBoss windup + swarm size the host would be streaming (LazerGenerator.Setup's
        // args at MarsBoss.CreateGenerator), so the client's rebuilt copy ramps identically.
        private const float Windup = 2.5f;
        private const float SwarmSize = 2f;

        public static string Run()
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netaim] enemy charge-glow AIM on a join peer (card eb057163)\n");

            // The eaNetStale / eaBinTest gate: this starts a real session and builds real puppets
            // into the live bin, so a live session, level or attract demo is a reason to report a
            // SKIP rather than let an unrun suite read as a pass.
            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            List<GameComponent> planted = new List<GameComponent>();
            INetHost hostBefore = NetHost.Current;
            try
            {
                // Sections 1 and 2 share ONE session and ONE puppet on purpose: they are the
                // same frames with the fix off and on, so anything else differing between them
                // would be a confound rather than a control.
                SectionSweeps(bin, game, planted, sb, Check);

                // THE SESSION GOES DOWN HERE, and it is not tidiness. Sections 3-5 add REAL
                // replicable-type entities to the live bin, and while a CLIENT session is up
                // `NetSession.SuppressWorldSpawn` diverts exactly those into the recycle pool
                // (that is the world-authority split doing its job) -- so every one of them
                // would have been a component the world never held, quietly, with each section
                // still green. Found by review, not by a failing assertion, which is why the
                // sections below now ASSERT that what they planted really landed.
                NetSession.Stop("netaim sweeps done");
                Check("the session is down before anything else is planted (or the client's own"
                    + " world-spawn suppression would divert it)", !NetSession.Active);

                sb.Append(" 3. the HOST re-reads its live aim every encode\n");
                SectionHostEncode(bin, game, Check);

                sb.Append(" 4. an emitter whose offset does NOT track is left alone\n");
                SectionFixedOffset(Check);

                sb.Append(" 5. the charge-off edge, and the recycle trap behind it\n");
                SectionEdges(Check);

                sb.Append(" 6. the dt the driver really hands it\n");
                SectionTiming(Check);
            }
            finally
            {
                NetHost.Current = hostBefore;
                NetSession.Stop("netaim done");
                NetScene.Current = null;
                // BY HAND: NetPuppets.Disable() clears the id maps but does not remove the
                // components the layer built. Collected BEFORE Disable, since FindPuppet reads
                // the maps it clears. NetStaleTest's shape.
                foreach (ushort id in new ushort[] { IdBoss })
                {
                    INetEntity puppet = NetPuppets.FindPuppet(id);
                    if (puppet != null)
                    {
                        bin.Remove((GameComponent)(object)puppet);
                    }
                }
                NetPuppets.Disable();
                foreach (GameComponent c in planted)
                {
                    bin.Remove(c);
                }
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the mechanism ------------------------------------------------------------------
        //
        // A scripted host charges a MarsBoss puppet for one full 2500ms windup, re-aiming on every
        // 150ms snapshot turn while the client ticks at 16.7ms. The observable is
        // MarsBoss.NetChargeOffset -- the emitter's OWN readback of `lazerGenerator.Position -
        // Position`, i.e. exactly the vector the glow is drawn at, so this measures the drawn
        // thing and not a copy of the input.
        //
        // The three outcomes are disjoint and are reported as a verdict rather than inferred by
        // the reader:
        //   distinct == 1                -> FREEZE
        //   moved on turns only          -> STALENESS
        //   moved on (nearly) every tick -> SWEEPING
        private static void SectionSweeps(ComponentBin bin, Game game,
            List<GameComponent> planted, StringBuilder sb, Action<string, bool> check)
        {
            sb.Append(" 1. the mechanism: FREEZE or STALENESS, measured\n");
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            NetScene.Current = new AimScene();
            // A PINNED host, so the pre-card path is reached by the FLAG rather than by a reboot
            // -- the ?netstaleguard=0 idiom (NetStaleTest section 3). It also freezes the clock,
            // which this suite wants anyway: 16 scripted turns must not trip a peer timeout.
            PinnedNetHost host = new PinnedNetHost();
            NetHost.Current = host;

            NetSession.StartForTest(game, host: false, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                NetSession.LocalBuildHash, 0, 1, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            check("session started as a CLIENT and paired", NetSession.IsClient && NetSession.PeerUp);

            byte? typeIdxOrNull = TypeIdxOfMarsBoss(bin, game, check);
            if (!typeIdxOrNull.HasValue)
            {
                return;
            }
            byte typeIdx = typeIdxOrNull.Value;

            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            byte[] spawnExtras = new byte[1]; // bossPosition 0 = left
            peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq, IdBoss, typeIdx,
                state, spawnExtras, 1));
            wire.Pump();
            NetSession.Update();
            MarsBoss boss = NetPuppets.FindPuppet(IdBoss) as MarsBoss;
            check("the scripted host's EvSpawn built a MarsBoss puppet", boss != null);
            if (boss == null)
            {
                return;
            }

            // THE PRE-CARD PATH FIRST. Without it, "the fixed aim sweeps smoothly" would pass on
            // a build where the aim never moved at all -- one of the two faults under test.
            host.AimEase = false;
            AimTrace stepped = SweepAim(wire, peer, IdBoss, typeIdx, state, boss);

            check("the puppet is charging at all (the glow is up -- otherwise this is card 57ea30cd)",
                stepped.Samples > 0 && boss.NetCharging);
            sb.Append("    pre-card  ").Append(stepped.Describe()).Append('\n');

            // THE DISCRIMINATOR, stated as assertions so the verdict is on the record either way
            // rather than left to whoever reads the three numbers.
            check("NOT a freeze -- the client's aim really is re-applied (" + stepped.Distinct
                + " distinct aims over " + stepped.Turns + " turns)", stepped.Distinct > 1);
            check("...and every aim the host sent arrived (" + stepped.Distinct + " of "
                + stepped.Turns + ")", stepped.Distinct == stepped.Turns);
            check("STALENESS, not a freeze: a STAIRCASE -- still between turns, then a jump"
                + " (moved on " + stepped.MovedTicks + " of " + stepped.Samples + " ticks)",
                stepped.Verdict == VerdictStale);

            sb.Append(" 2. the EASE -- the same frames, swept instead of stepped\n");

            // The charge-off edge between the two sweeps also hands section 5's recycle case its
            // precondition for free: the second sweep starts from a FRESH child.
            EndCharge(wire, peer, IdBoss, typeIdx, state);
            host.AimEase = true;
            AimTrace swept = SweepAim(wire, peer, IdBoss, typeIdx, state, boss);
            sb.Append("    eased     ").Append(swept.Describe()).Append('\n');

            check("the glow SWEEPS -- it moves on (nearly) every tick, not one per turn ("
                + swept.MovedTicks + " of " + swept.Samples + ")", swept.Verdict == VerdictSweep);
            check("...which is many more moving ticks than the pre-card path ("
                + swept.MovedTicks + " vs " + stepped.MovedTicks + ")",
                swept.MovedTicks > stepped.MovedTicks * 4);
            // A sweep that merely JITTERED would also move on every tick. What makes it a sweep is
            // that no single tick jumps the way a whole turn used to.
            check("...in steps far smaller than the pre-card jump (max " + Px(swept.MaxStepPx)
                + " vs " + Px(stepped.MaxStepPx) + ")", swept.MaxStepPx < stepped.MaxStepPx * 0.5f);
            // It CHASES rather than overshoots: no tick may take it past the value it is heading
            // for. That is the property the extrapolation alternative could not offer, and an
            // overshooting telegraph points where the host never aimed.
            check("...never overshooting the aim it is heading for", !swept.Overshot);

            // ...and it ARRIVES. A chase converging on something else would satisfy every
            // assertion above while pointing the telegraph in the wrong direction.
            Vector2 lastAim = swept.LastWireAim;
            for (int i = 0; i < 200; i++)
            {
                NetPuppets.Drive(TickMs);
            }
            // EXACTLY on it, not near it: the target is the DECODED aim, which is what the client
            // was told, so a tolerance here would hide a chase that stopped fractionally short.
            check("...and settles ON the last aim the host sent once it stops moving ("
                + Show(boss.NetChargeOffset) + " vs " + Show(lastAim) + ")",
                Near(boss.NetChargeOffset, lastAim, 0.001f));
        }

        // ---- 4. a non-tracking emitter ---------------------------------------------------------
        //
        // All five charge emitters share NetChargeGlow, but only three of them AIM: the big UFO's
        // lazor and the JunkBoss' suck swarm sit at a FIXED offset from the emitter. One rule for
        // five emitters must therefore not pay for itself with a wobble on two of them.
        //
        // BE CLEAR WHAT THIS LEG CAN AND CANNOT FAIL. With target == current the update is
        // `Offset += (Target - Offset) * f`, which is identically Offset for ANY f -- so the
        // no-op is ALGEBRAIC and no test can discriminate a good ease from a bad one here. What
        // it does catch, and did catch under mutation, is the charge-on RESET going missing:
        // without it the eased offset starts at default (0,0) and this emitter sweeps in from
        // the emitter's own centre over its first window (measured 26.66px of drift).
        private static void SectionFixedOffset(Action<string, bool> check)
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            UFO ufo = UFO.NewUFO(bin, bin.Game);
            ufo.Setup(Nowhere, isBig: true, EnemyBehaviour.normal);
            bin.Add((GameComponent)(object)ufo);
            try
            {
                ufo.Position = Nowhere;
                check("the planted UFO really landed in the world (not diverted)",
                    InWorld(bin.Game, (GameComponent)(object)ufo));
                Vector2 fixedOffset = new Vector2(0f, 30f);
                GameTime tick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));

                ufo.NetApplyCharge(true, fixedOffset, Windup, SwarmSize);
                ufo.NetDriveExtras(tick);
                check("the UFO's charge glow is up", ufo.NetCharging);

                float worst = 0f;
                for (int i = 0; i < 200; i++)
                {
                    ufo.NetDriveExtras(tick);
                    float err = (ufo.NetChargeOffset - fixedOffset).Length();
                    if (err > worst) { worst = err; }
                }
                check("...and a FIXED offset is left exactly alone (worst " + Px(worst)
                    + " over 200 ticks -- see the section: this catches a missing charge-on"
                    + " reset, not a bad ease)", worst < 0.001f);

                ufo.NetApplyCharge(false, Vector2.Zero, Windup, SwarmSize);
                ufo.NetDriveExtras(tick);
            }
            finally
            {
                bin.Remove((GameComponent)(object)ufo);
            }
        }

        // ---- 5. the edges ----------------------------------------------------------------------
        //
        // The charge-off edge still frees the child, and a SECOND charge starts at the aim it is
        // told rather than sweeping in from the last one. The emitters are pooled and the eased
        // value lives on the EMITTER, so without the reset a boss winding up again would swing its
        // telegraph across the screen from wherever its previous beam pointed -- the recycle trap
        // Lazer.SetupSingleShot's owner clear and FlyingSpider's anchor reset both document.
        private static void SectionEdges(Action<string, bool> check)
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            MarsBoss boss = MarsBoss.NewMarsBoss(bin, bin.Game);
            boss.Setup(MarsBoss.BossPosition.left);
            bin.Add((GameComponent)(object)boss);
            try
            {
                boss.Position = Nowhere;
                check("the planted boss really landed in the world (not diverted)",
                    InWorld(bin.Game, (GameComponent)(object)boss));
                GameTime tick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));

                Vector2 aimA = AimAt(-SweepHalfAngle);
                boss.NetApplyCharge(true, aimA, Windup, SwarmSize);
                boss.NetDriveExtras(tick);
                check("a first charge starts AT the aim it was given (" + Show(boss.NetChargeOffset)
                    + ")", Near(boss.NetChargeOffset, aimA, 0.001f));

                boss.NetApplyCharge(false, Vector2.Zero, Windup, SwarmSize);
                boss.NetDriveExtras(tick);
                check("...the charge-off edge still frees the glow", !boss.NetCharging);

                Vector2 aimB = AimAt(SweepHalfAngle);
                boss.NetApplyCharge(true, aimB, Windup, SwarmSize);
                boss.NetDriveExtras(tick);
                check("...and a SECOND charge starts at ITS aim, not swept in from the first ("
                    + Show(boss.NetChargeOffset) + " vs " + Show(aimB) + ")",
                    Near(boss.NetChargeOffset, aimB, 0.001f));

                boss.NetApplyCharge(false, Vector2.Zero, Windup, SwarmSize);
                boss.NetDriveExtras(tick);
            }
            finally
            {
                bin.Remove((GameComponent)(object)boss);
            }
        }

        // ---- 6. the dt ---------------------------------------------------------------------------
        //
        // Every other section ticks at a constant 16.7ms, which is the ONE dt the driver is least
        // likely to hand this code. Two real cases, both found by review rather than by a run:
        //
        //   dt == 0    NetPuppetDriver derives dt from TickCount64, an INTEGER-millisecond clock,
        //              so two ticks inside one millisecond -- routine on a high-refresh display or
        //              under ?fpsuncapped -- produce exactly 0. A zero tick must HOLD. Treating it
        //              as "the window is over" would teleport the glow on those frames, i.e. put
        //              the staircase back on a subset of them, which no constant-dt rig can see.
        //   coarse dt  The sweep must not depend on the frame rate. A fraction of the WHOLE window
        //              per tick (the obvious one-liner, and the first cut) fails this: it decays
        //              exponentially, so a 30Hz client and a 60Hz client sweep at different speeds
        //              and neither ever lands.
        private static void SectionTiming(Action<string, bool> check)
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Vector2 aimA = AimAt(-SweepHalfAngle);
            Vector2 aimB = AimAt(SweepHalfAngle);

            MarsBoss zero = Charged(bin, aimA);
            try
            {
                zero.NetApplyCharge(true, aimB, Windup, SwarmSize);
                zero.NetDriveExtras(new GameTime(TimeSpan.Zero, TimeSpan.Zero));
                check("a ZERO-length tick HOLDS the aim rather than teleporting it ("
                    + Show(zero.NetChargeOffset) + ")", Near(zero.NetChargeOffset, aimA, 0.001f));

                // ...and the very next real tick moves, so the hold above is not just a dead ease.
                zero.NetDriveExtras(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs)));
                check("...and the next real tick resumes the sweep",
                    (zero.NetChargeOffset - aimA).Length() > 0.001f);
            }
            finally
            {
                Discard(bin, zero);
            }

            // The SAME elapsed time in different-sized ticks must land in the same place. The
            // window is >= 150ms, so 300ms of either cadence is comfortably past it and both must
            // have ARRIVED -- which is also the property an exponential drain cannot have.
            MarsBoss fast = Charged(bin, aimA);
            MarsBoss slow = Charged(bin, aimA);
            try
            {
                fast.NetApplyCharge(true, aimB, Windup, SwarmSize);
                slow.NetApplyCharge(true, aimB, Windup, SwarmSize);
                GameTime fastTick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(5f));
                GameTime slowTick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(30f));
                for (int i = 0; i < 60; i++) { fast.NetDriveExtras(fastTick); }
                for (int i = 0; i < 10; i++) { slow.NetDriveExtras(slowTick); }
                check("300ms at 5ms/tick and at 30ms/tick land in the SAME place ("
                    + Show(fast.NetChargeOffset) + " vs " + Show(slow.NetChargeOffset) + ")",
                    Near(fast.NetChargeOffset, slow.NetChargeOffset, 0.01f));
                check("...and both have ARRIVED, which an exponential drain never does",
                    Near(fast.NetChargeOffset, aimB, 0.001f)
                    && Near(slow.NetChargeOffset, aimB, 0.001f));
            }
            finally
            {
                Discard(bin, fast);
                Discard(bin, slow);
            }
        }

        // A real MarsBoss in the world with its charge glow up and settled at `aim`.
        private static MarsBoss Charged(ComponentBin bin, Vector2 aim)
        {
            MarsBoss boss = MarsBoss.NewMarsBoss(bin, bin.Game);
            boss.Setup(MarsBoss.BossPosition.left);
            bin.Add((GameComponent)(object)boss);
            boss.Position = Nowhere;
            boss.NetApplyCharge(true, aim, Windup, SwarmSize);
            boss.NetDriveExtras(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs)));
            return boss;
        }

        private static void Discard(ComponentBin bin, MarsBoss boss)
        {
            boss.NetApplyCharge(false, Vector2.Zero, Windup, SwarmSize);
            boss.NetDriveExtras(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs)));
            bin.Remove((GameComponent)(object)boss);
        }

        // Did ComponentBin.Add actually seat this component, or divert it? The suppression that
        // diverts a replicable type on a client is silent by design, so a section that plants one
        // has to look rather than assume -- the whole of sections 3-6 used to be about entities
        // the world did not hold.
        private static bool InWorld(Game game, GameComponent c)
        {
            foreach (IGameComponent item in game.Components)
            {
                if (ReferenceEquals(item, c)) { return true; }
            }
            return false;
        }

        // Tells the puppet the host has stopped charging, so the next sweep builds a fresh child.
        private static void EndCharge(NetWire wire, InMemoryTransport peer,
            ushort id, byte typeIdx, NetBaseState state)
        {
            peer.SendStream(SnapshotFor(id, typeIdx, state, new byte[1], 1));
            wire.Pump();
            NetSession.Update();
            NetPuppets.Drive(TickMs);
        }

        // ---- 3. the host half ------------------------------------------------------------------
        //
        // Section 1 measures the CLIENT. If the host were latching its aim at charge-on instead
        // of re-reading it, the client would be faithfully replicating a frozen value and no
        // client-side change could fix it -- so the encoder is checked too, and cheaply: a real
        // MarsBoss whose child generator is moved between two real EncodeStateExtra calls.
        //
        // It uses only shipped seams (NetApplyCharge + NetDriveExtras build and move the child;
        // MarsBossDescriptor.EncodeStateExtra + NetChargeWire.Decode read it back), so it needs
        // no live boss and none of the ~8 sim-minutes of Level 2 that reaching one costs.
        private static void SectionHostEncode(ComponentBin bin, Game game,
            Action<string, bool> check)
        {
            MarsBoss boss = MarsBoss.NewMarsBoss(bin, game);
            boss.Setup(MarsBoss.BossPosition.left);
            bin.Add((GameComponent)(object)boss);
            try
            {
            boss.Position = Nowhere;
            check("the planted boss really landed in the world (not diverted)",
                InWorld(game, (GameComponent)(object)boss));

            Descriptors.MarsBossDescriptor desc = new Descriptors.MarsBossDescriptor();
            GameTime tick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));

            Vector2 aimA = AimAt(-SweepHalfAngle);
            Vector2 aimB = AimAt(SweepHalfAngle);

            boss.NetApplyCharge(true, aimA, Windup, SwarmSize);
            SettleGlow(boss, tick);
            Vector2 encodedA = EncodedAim(desc, boss);
            check("the encoder reads the live generator (aim A " + Show(encodedA) + ")",
                Near(encodedA, aimA, 1.5f));

            boss.NetApplyCharge(true, aimB, Windup, SwarmSize);
            SettleGlow(boss, tick);
            Vector2 encodedB = EncodedAim(desc, boss);
            check("...and re-reads it after the aim MOVES, rather than latching charge-on (aim B "
                + Show(encodedB) + ")", Near(encodedB, aimB, 1.5f));

            boss.NetApplyCharge(false, Vector2.Zero, Windup, SwarmSize);
            boss.NetDriveExtras(tick);
            check("...and stops claiming to charge once the glow is gone", !boss.NetCharging);
            }
            finally
            {
                bin.Remove((GameComponent)(object)boss);
            }
        }

        // Drives the glow until the aim ease has converged, so section 3 reads the ENCODER rather
        // than one tick of the sweep. (Without it the second leg fails on a correct build, which
        // is a fine way to notice the ease is live and a poor way to state what section 3 is for.)
        private static void SettleGlow(MarsBoss boss, GameTime tick)
        {
            for (int i = 0; i < 200; i++)
            {
                boss.NetDriveExtras(tick);
            }
        }

        // ---- the rig ---------------------------------------------------------------------------

        // Plays one full host charge over the wire and samples the client's drawn aim every tick.
        private static AimTrace SweepAim(NetWire wire, InMemoryTransport peer,
            ushort id, byte typeIdx, NetBaseState state, MarsBoss boss)
        {
            AimTrace trace = new AimTrace();
            trace.LastWireAim = Vector2.Zero;
            int turns = (int)(ChargeMs / TurnMs);
            int ticksPerTurn = (int)Math.Round(TurnMs / TickMs);

            for (int t = 0; t < turns; t++)
            {
                // The host's aim at this turn: the target crossing from -30 to +30 degrees.
                float u = (turns > 1) ? (float)t / (turns - 1) : 0f;
                Vector2 aim = AimAt(MathHelper.Lerp(-SweepHalfAngle, SweepHalfAngle, u));
                byte[] extras = new byte[1 + NetChargeWire.Bytes];
                extras[0] = NetChargeWire.FlagChargingBit1;
                NetChargeWire.Encode(extras, 1, aim, Windup, SwarmSize);
                // THE TRACE FOLLOWS WHAT THE CLIENT WILL ACTUALLY BE TOLD, not what was sent.
                // NetChargeWire quantises the offset to whole px, so the eased sweep converges on
                // the DECODED aim -- and truncation can put that a fraction PAST the exact one
                // along the direction of travel, which an overshoot assertion measured against
                // the exact value reads as an overshoot that never happened.
                NetChargeWire.Decode(extras, 1, out Vector2 wireAim, out _, out _);
                trace.Sent(wireAim);
                peer.SendStream(SnapshotFor(id, typeIdx, state, extras, extras.Length));
                wire.Pump();
                NetSession.Update();

                for (int i = 0; i < ticksPerTurn; i++)
                {
                    NetPuppets.Drive(TickMs);
                    trace.Sample(boss.NetChargeOffset);
                }
            }
            return trace;
        }

        // What the client's glow is drawn at, tick by tick, reduced to the three numbers that
        // separate a freeze from a staircase from a sweep.
        private sealed class AimTrace
        {
            private Vector2 last;
            private bool has;
            private readonly List<Vector2> distinct = new List<Vector2>();

            public Vector2 LastWireAim;

            public int Samples;
            public int Turns;
            public int MovedTicks;
            public float MaxStepPx;
            public float TotalPx;

            public int Distinct => distinct.Count;

            private Vector2 target;
            private bool hasTarget;

            public bool Overshot;

            public void Sent(Vector2 aim)
            {
                Turns++;
                target = aim;
                hasTarget = true;
                LastWireAim = aim;
            }

            public void Sample(Vector2 aim)
            {
                Samples++;
                // An overshoot is a sample PAST the value it is heading for. Measured along the
                // approach direction so it cannot be confused with the residual error a chase
                // legitimately carries: only a step that lands on the far side counts.
                if (has && hasTarget)
                {
                    Vector2 toTarget = target - last;
                    if (toTarget.LengthSquared() > 0.000001f
                        && Vector2.Dot(aim - target, toTarget) > 0.001f)
                    {
                        Overshot = true;
                    }
                }
                if (has)
                {
                    float step = (aim - last).Length();
                    if (step > 0.001f)
                    {
                        MovedTicks++;
                        TotalPx += step;
                        if (step > MaxStepPx) { MaxStepPx = step; }
                    }
                }
                has = true;
                last = aim;
                bool seen = false;
                foreach (Vector2 v in distinct)
                {
                    if (Near(v, aim, 0.001f)) { seen = true; break; }
                }
                if (!seen) { distinct.Add(aim); }
            }

            // The verdict, spelled out rather than left to the reader of three numbers.
            public string Verdict
            {
                get
                {
                    if (Distinct <= 1) { return VerdictFreeze; }
                    // A staircase moves on roughly one tick per TURN (here 1 in 9); a sweep moves
                    // on nearly all of them. The halfway mark is nowhere near either, so the
                    // threshold implies no tuning and needs none.
                    return (MovedTicks * 2 <= Samples) ? VerdictStale : VerdictSweep;
                }
            }

            public string Describe()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "verdict={0}  distinct={1} turns={2}  moved on {3}/{4} ticks  "
                    + "maxStep={5:0.00}px  travelled={6:0.0}px",
                    Verdict, Distinct, Turns, MovedTicks, Samples, MaxStepPx, TotalPx);
            }
        }

        // One world-snapshot packet carrying a single entry, through the real WriteSnapshotEntry
        // so an entry-layout change moves this with it. The header seq is MONOTONE (card
        // f5cf7a5c): a fixed zero would make every packet after the first stale and the whole
        // sweep would read as a freeze that is really the rig's fault.
        private static ushort snapshotSeq;

        private static byte[] SnapshotFor(ushort id, byte typeIdx, in NetBaseState state,
            byte[] extras, int extrasLen)
        {
            byte[] scratch = new byte[NetProtocol.SnapshotHeaderBytes
                + NetProtocol.SnapshotEntryBaseBytes + extrasLen + 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            NetProtocol.WriteSnapshotEntry(scratch, ref off, id, typeIdx,
                NetProtocol.NetSnapshotFlags.None, state, extras, extrasLen);
            NetProtocol.WriteSnapshotHeader(scratch, 1, ++snapshotSeq);
            byte[] packet = new byte[off];
            Array.Copy(scratch, packet, off);
            return packet;
        }

        // The registry index for MarsBoss, asserted against the live table rather than hard-coded
        // -- the wire typeIdx IS the registry order, so a reordering would otherwise silently
        // spawn some other enemy and every aim assertion would be about the wrong thing.
        // The instance is a THROWAWAY -- never added to the bin, so it takes no NetId and needs
        // no cleanup (NetFxTest's TypeIdxOf, same shape). A failure returns null rather than 0:
        // 0 is a real registry index, so falling through would spawn some OTHER enemy and every
        // aim assertion after it would be about the wrong entity while still reading green.
        private static byte? TypeIdxOfMarsBoss(ComponentBin bin, Game game,
            Action<string, bool> check)
        {
            MarsBoss probe = MarsBoss.NewMarsBoss(bin, game);
            bool ok = NetTypeRegistry.TryGet((GameComponent)(object)probe, out byte idx, out _);
            check("MarsBoss is a replicable type (registry idx " + idx + ")", ok);
            return ok ? idx : (byte?)null;
        }

        // The aim vector a MarsBoss would hold at this bearing: a NORMALIZED direction scaled by
        // the flat 100px the host uses (MarsBoss.Update's `val * 100f`).
        private static Vector2 AimAt(float angle)
        {
            return new Vector2((float)Math.Sin(angle), -(float)Math.Cos(angle)) * AimRadius;
        }

        // The aim as it comes back off a real EncodeStateExtra -- through NetChargeWire, so a
        // wire-layout change moves this with it.
        private static Vector2 EncodedAim(Descriptors.MarsBossDescriptor desc, MarsBoss boss)
        {
            byte[] buf = new byte[1 + NetChargeWire.Bytes];
            desc.EncodeStateExtra(boss, buf, 0);
            NetChargeWire.Decode(buf, 1, out Vector2 offset, out _, out _);
            return offset;
        }

        // The three verdicts, named rather than spelled out at each comparison -- section 1 both
        // PRINTS and ASSERTS one, and a typo between the two would be silent.
        private const string VerdictFreeze = "FREEZE";
        private const string VerdictStale = "STALENESS (staircase)";
        private const string VerdictSweep = "SWEEPING";

        private static string Px(float v)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.000}px", v);
        }

        private static string Show(Vector2 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0}", v.X, v.Y);
        }

        private static bool Near(Vector2 a, Vector2 b, float tol)
        {
            return (a - b).Length() < tol;
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netaim] {0} passed, {1} failed\n", pass, fail);
        }

        // The client rx paths gate on "is a scene up", which the seam answers -- nothing in this
        // suite is about what a scene DOES. NetStaleTest's StaleScene, same reasoning.
        private sealed class AimScene : INetScene
        {
            public Levels Level => Levels.Level2;

            public bool NetEndingNormally => false;

            public bool JoinWouldSpawnNow => false;

            public float PlayerSpawnDirection => 4.712389f;

            public bool NetScriptHoldsShipSpawn => false;

            public void NetApplyIntroVolley(int seed) { }

            public void NetApplyReset(byte mode) { }

            public void NetApplyVictory() { }

            public void NetApplyCheckpoint() { }

            public void NetApplyBackgroundOp(NetBackgroundOp op, Vector2 v) { }

            public void NetApplyCosmeticSwarm(NetCosmeticKind kind, bool on, float rate) { }

            public void NetApplyTetherBreak() { }

            public void NetApplyPeerLeft() { }

            public void NetSetRemotePaused(bool on) { }

            public void NetSetPeerStalled(bool on) { }

            public void NetReplayCatchUp() { }

            public bool NetShowKickMenu() => false;

            public void SpawnPlayer(ControlDevice controlDevice, int slot) { }
        }
    }
}
