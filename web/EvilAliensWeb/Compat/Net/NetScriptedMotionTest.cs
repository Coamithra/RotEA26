using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // SCRIPTED MOTION -- a motion model for the scripted-position bosses (card 76ec8bdb).
    // Run `eaNetScriptedMotion()` from the MAIN MENU, or `eval NetScriptedMotion` under eahl.
    // Its probe is tools/headless/probes/net_scripted_motion.txt.
    //
    // WHAT IS BEING FIXED. A type that moves by writing Position directly never assigns
    // Speed/Direction, so the two velocities the net layer already had both fail for it:
    //
    //   * its DECLARED NetSpeedVector is a flat ZERO. That is what a marked teleport (card
    //     e79bb994) falls back to, so a parked SpiderBoss went out at velocity (0,0) and the
    //     joiner's puppet stood still until its next round-robin turn;
    //   * the FINITE DIFFERENCE is a whole snapshot turn LATE. A difference reported at turn T
    //     describes [T-1, T] while the client dead-reckons over [T, T+1], so every phase change
    //     of a scripted set-piece is driven on the PREVIOUS phase's velocity for up to a turn --
    //     `SnapshotTurnMs` is live*60/16, i.e. 60 ms at 16 live entities and 480 ms at 128.
    //
    // The second is the bigger half and the card's real subject: the boss steps from a standing
    // 0 to a 0.78 px/ms screen-crossing sweep, so at a 480 ms turn a COLLIDABLE puppet trails the
    // host by ~375 px before popping past SnapThresholdPx. The teleport case is the extreme of
    // the same defect, not a separate one.
    //
    // THE FIX IS AN ANNOUNCED VELOCITY -- AlienDrawableGameComponent.TryGetNetScriptedVelocity --
    // used on EVERY turn, not only the ones the fallbacks would have caught. It is FORWARD
    // looking where a difference is backward looking, which is the direction dead reckoning
    // needs. No wire bytes and no protocol change: it is a better number in a field that already
    // ships.
    //
    // SECTION 2 IS THE ONE THAT CANNOT BE FAKED, and it is why this suite exists rather than a
    // table of expected velocities. The override in SpiderBoss transcribes that class's own
    // Update switch -- so an expectation table transcribed from the same reading would prove only
    // that two copies of one misreading agree. Instead the REAL Update is driven through the
    // whole choreography and its ACTUAL displacement is finite-differenced to produce the
    // expected value. The pause reading zero falls out of that for free, and the leg doubles as a
    // standing tripwire: change Update's choreography without following it in the override and
    // this fails rather than shipping a wrong velocity onto the other player's screen.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE (the eaNetTeleport shape): the boss is driven far
    // off-screen, everything the choreography spawns on its own (warning banners, the helper
    // mothership) is collected and removed with it, and the finally hands back every seam.
    internal static class NetScriptedMotionTest
    {
        private const string Room = "netscript";

        private const ulong PeerToken = 0x5C819A7DUL;

        // Off-screen, so nothing this suite builds can be seen for the frame it exists. The boss
        // is driven relative to its own choreography, which uses absolute screen coordinates, so
        // this is where the PLANTED extras go rather than where the boss lives.
        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // One tick at the game's fixed step. The drive is a plain loop over the real Update --
        // the ApplyLifecycle / isolation-sim idiom, no rendering and no bin.
        private const float TickMs = 16.666666f;

        // A phase change is a velocity STEP of at least the background scroll (~0.05 px/ms) and
        // usually of the full 0.78 px/ms move speed. DifficultyModifier ramps continuously, which
        // moves the announced value by ~1e-5 px/ms per tick, so a boundary test has to sit
        // between the two rather than at zero -- or the drift reads as a phase change every tick
        // and section 2 excludes its entire sample, passing vacuously.
        private const float BoundaryStepPxPerMs = 0.01f;

        // What "the announced velocity matches the real displacement" means. Position is a float
        // accumulated per tick, so the difference carries ordinary single-precision noise.
        private const float VelEpsPxPerMs = 0.0005f;

        // WHAT COUNTS AS "SWEEPING", AS A FRACTION OF THE BOSS'S OWN TOP SPEED RATHER THAN A
        // px/ms LITERAL. `moveSpeed` is `0.78 * Settings.DifficultyModifier`, which is 0.27 on
        // Easy and ramps within a fight -- so a hard-coded 0.5 px/ms threshold quietly means "did
        // it move at all" on one tier and "did it sweep" on another, and the legs resting on it
        // then fail for a reason that has nothing to do with the code under test. Measured: an
        // early cut of this suite reported five spurious failures exactly that way.
        private const float SweepFraction = 0.5f;

        // Long enough for a full flyleft -> park -> flyright -> park -> land -> standing -> jump
        // -> flyup -> park cycle (~23 s of choreography), with margin. At 16.7 ms a tick this is
        // ~40 s of game time and costs a few ms of real time -- there is no Draw here.
        private const int DriveTicks = 2400;

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

            sb.Append("[netscript] scripted motion -- the announced velocity on the wire\n");

            // The eaNetTeleport / eaNetFx gate: this starts a REAL session and drives a REAL boss
            // in the LIVE bin, so a session, level or attract demo is a reason to SKIP rather
            // than let an unrun suite read as a pass.
            if (NetSession.Active || GameScene.NetActiveScene != null || NetPuppets.LiveCount > 0)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            List<GameComponent> planted = new List<GameComponent>();

            PinnedNetHost clock = new PinnedNetHost();
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                SectionPolicy(sb, Check, game);
                SectionGroundTruth(sb, Check, bin, game, planted);
                SectionDecision(sb, Check);
                SectionWire(sb, Check, bin, game, planted, clock);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + Describe(ex) + ")", ok: false);
            }
            finally
            {
                NetSession.Stop("netscript suite teardown");
                Teardown(sb, Check, game, bin, planted);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back", !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. WHO TAKES WHICH SEAM ----------------------------------------------------------
        //
        // The census the card asked for, and one invariant. Read by REFLECTION on the override's
        // DeclaringType rather than by calling it: the point is which types have TAKEN the seam,
        // and a constructed-but-not-Setup boss cannot safely be asked (its oracle is not wired
        // until Initialize). That also makes the census total -- every registered type appears,
        // including the ones no factory here could build.
        private static void SectionPolicy(StringBuilder sb, Action<string, bool> Check, Game game)
        {
            sb.Append(" 1. the seam census over all " + NetTypeRegistry.Count
                + " replicable types\n");

            List<string> scripted = new List<string>();
            List<string> anchored = new List<string>();
            List<string> both = new List<string>();
            for (int i = 0; i < NetTypeRegistry.Count; i++)
            {
                INetTypeDescriptor d = NetTypeRegistry.Get((byte)i);
                Type t = d?.ComponentType;
                if (t == null)
                {
                    continue;
                }
                bool s = Overrides(t, "TryGetNetScriptedVelocity");
                bool a = Overrides(t, "get_NetPathAnchored");
                if (s) { scripted.Add(t.Name); }
                if (a) { anchored.Add(t.Name); }
                if (s && a) { both.Add(t.Name); }
            }

            sb.Append("    scripted: ").Append(Join(scripted)).Append('\n');
            sb.Append("    anchored: ").Append(Join(anchored)).Append('\n');

            // The card shipped exactly one override on purpose. Every additional one is a
            // hand-transcribed script that can dead-reckon a collidable puppet wrong, so a new
            // name appearing here is a decision someone made and should have to re-make.
            Check("SpiderBoss takes the scripted seam", scripted.Contains("SpiderBoss"));
            Check("...and it is the only type that does (" + scripted.Count + ")",
                scripted.Count == 1);

            // THE CONTROL that makes the line above mean something: a predicate hard-wired true
            // would pass it. UFO is the standing choice for this in the net suites -- it is
            // replicable, it moves, and it is emphatically not scripted.
            Check("CONTROL an ordinary UFO does NOT take it", !Overrides(typeof(UFO),
                "TryGetNetScriptedVelocity"));

            // THE INVARIANT. The two seams answer the same question from opposite evidence -- a
            // declared vector that is honest, versus a script that makes the declared vector
            // irrelevant -- so holding both is a contradiction. ResolveBaseVelocity ranks anchored
            // first rather than blending, but the ranking is a tie-break of last resort and no
            // type should ever reach it.
            Check("no type takes BOTH seams (" + Join(both) + ")", both.Count == 0);

            // And the anchored set is untouched by this card -- if a name left this list, the
            // card broke someone else's fix rather than adding its own. THREE, not the two card
            // c1a38ef9 shipped: the Level-3 Wall took the seam afterwards (card 4392bd30), which
            // is why this reads the census rather than that card's prose.
            Check("the anchored set is unchanged (" + Join(anchored) + ")",
                anchored.Count == 3 && anchored.Contains("FlyingSpider")
                    && anchored.Contains("Asteroid") && anchored.Contains("Wall"));

            // Behavioural, not reflective: the seam has to ANSWER, not merely exist. A UFO
            // beside it, since the base implementation is what every other type in the census
            // relies on.
            //
            // These two are constructed and NOT Setup, which the census above deliberately avoids
            // -- it is safe here only because `SpiderBossState`'s first member is `flyleft`, whose
            // arm reads no `oracle`. Reorder that enum so 0 is `standing` or `jump` and this leg
            // NREs into the suite's catch-all; Setup(intro: true) pins `flyleft` if it ever needs
            // to stop depending on the default.
            SpiderBoss boss = new SpiderBoss(game);
            UFO ufo = new UFO(game);
            Check("the base implementation answers false and yields no velocity",
                !((INetEntity)ufo).TryGetNetScriptedVelocity(out Vector2 none)
                    && none == Vector2.Zero);
            Check("the SpiderBoss override answers true through the INetEntity forward",
                ((INetEntity)boss).TryGetNetScriptedVelocity(out _));
        }

        // ---- 2. GROUND TRUTH: the real Update, finite-differenced ------------------------------
        //
        // THE LEG THAT CANNOT BE FAKED. For every tick of a full choreography cycle, the velocity
        // the boss ANNOUNCES before the tick is compared against the displacement its own real
        // Update produces during that tick. Nothing here is transcribed from reading the source,
        // so this fails if the override and Update ever disagree -- including if they were both
        // written from the same misreading.
        //
        // TWO CLASSES OF TICK ARE EXCLUDED, and each is excluded on evidence rather than by
        // being listed:
        //   * a MARKED TELEPORT tick, where the displacement is a park and not motion. Detected
        //     by the entity's own latch, so a park that stopped announcing itself would take the
        //     exclusion away rather than keep it;
        //   * a PHASE BOUNDARY tick, where Update spends part of the tick in each of two phases.
        //     Detected two ways, because neither alone is enough: the STATE byte changing, and
        //     the announced velocity STEPPING by more than the continuous DifficultyModifier
        //     drift. The velocity test alone misses a transition whose two phases happen to
        //     announce the same vector (jump's climb into flyup, measured -- both are
        //     (0,-moveSpeed), yet the tick itself moves zero because Update re-rolls
        //     animationProgress before reading its own gate); the state test alone misses the
        //     boundary INSIDE `jump`, where the climb starts partway through the crouch with no
        //     state change to key on.
        // Both exclusions are then asserted to be RARE and non-empty: an exclusion rule that
        // swallowed the whole run would make this pass on any build at all.
        private static void SectionGroundTruth(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 2. the announced velocity against the REAL Update's displacement\n");

            // Everything the boss's own choreography adds to the world during the drive --
            // "Danger!" banners and, after enough cycles, a helper mothership. Collected by
            // diffing the bin, so a beat added later is cleaned up too.
            HashSet<GameComponent> before = SnapshotBin(game);

            SpiderBoss boss = SpiderBoss.NewSpiderBoss(bin, game);
            boss.Setup(intro: true);
            bin.Add((GameComponent)(object)boss);
            planted.Add((GameComponent)(object)boss);
            bin.TopOfTickFlush();

            GameTime tick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            INetEntity seam = boss;

            HashSet<byte> phasesSeen = new HashSet<byte>();
            int compared = 0;
            int teleportTicks = 0;
            int boundaryTicks = 0;
            int stillTicks = 0;
            // Every announced speed of the run, so "was it really sweeping" can be judged
            // RELATIVE to this boss's own top speed -- see SweepFraction.
            List<float> announcedSpeeds = new List<float>();
            float worst = 0f;
            string worstWhere = "none";

            for (int i = 0; i < DriveTicks; i++)
            {
                bool had = seam.TryGetNetScriptedVelocity(out Vector2 announced);
                if (!had)
                {
                    Check("the boss announces a velocity on every tick (failed at tick "
                        + i + ")", false);
                    break;
                }
                byte phaseBefore = boss.NetState;
                phasesSeen.Add(phaseBefore);
                if (announced == Vector2.Zero)
                {
                    stillTicks++;
                }
                announcedSpeeds.Add(announced.Length());

                Vector2 posBefore = boss.Position;
                boss.Update(tick);
                Vector2 moved = (boss.Position - posBefore) / TickMs;

                // The latch is read-and-CLEAR, which is exactly what CaptureBaseState would do to
                // it -- and nothing is capturing here, so consuming it is free.
                if (seam.NetTakeTeleport())
                {
                    teleportTicks++;
                    continue;
                }
                seam.TryGetNetScriptedVelocity(out Vector2 after);
                if (boss.NetState != phaseBefore
                    || (after - announced).Length() > BoundaryStepPxPerMs)
                {
                    boundaryTicks++;
                    continue;
                }

                compared++;
                float err = (moved - announced).Length();
                if (err > worst)
                {
                    worst = err;
                    worstWhere = "state " + phaseBefore + " at tick " + i
                        + " (announced " + Fmt(announced) + ", moved " + Fmt(moved) + ")";
                }
            }

            float peakAnnounced = 0f;
            foreach (float v in announcedSpeeds)
            {
                peakAnnounced = Math.Max(peakAnnounced, v);
            }
            int sweepTicks = 0;
            foreach (float v in announcedSpeeds)
            {
                if (v > SweepFraction * peakAnnounced)
                {
                    sweepTicks++;
                }
            }

            // PRECONDITIONS FIRST, because every claim below is about the run having HAPPENED.
            // Six live phases: flyleft/flyright/flyup/land/standing/jump. `dead` is not driven
            // here -- the boss is never damaged -- and its announced velocity is zero by
            // inspection of a case that moves only its debris list.
            Check("PRECONDITION the drive visited all six live phases (" + phasesSeen.Count
                + " of 6)", phasesSeen.Count == 6);
            Check("PRECONDITION the drive parked at least once, so the teleport exclusion is "
                + "real (" + teleportTicks + ")", teleportTicks >= 1);
            // STILL means "announces exactly zero", which is the warning hold AND the grounded
            // standing phase (whose only motion is a background scroll that does not run at the
            // menu). Both are cases where the pre-card wire carried zero for the right reason and
            // then kept carrying it for the wrong one.
            Check("PRECONDITION the drive held STILL ticks, the case the card is about ("
                + stillTicks + ")", stillTicks >= 30);
            Check("PRECONDITION the exclusions are rare, not the whole sample (" + compared
                + " compared, " + boundaryTicks + " boundary, " + teleportTicks + " teleport)",
                compared > DriveTicks * 3 / 4);

            // THE CLAIM.
            Check("the announced velocity matches the real displacement on every in-phase tick "
                + "(worst " + worst.ToString("0.00000", CultureInfo.InvariantCulture)
                + " px/ms, " + worstWhere + ")", worst < VelEpsPxPerMs);

            // AND ITS CONTROL. A zero-everywhere override agrees with a boss that never moves, so
            // the claim above is only worth something over a run that swept at full speed for a
            // real part of its life. Without this the whole section passes on a build whose
            // choreography silently stopped running.
            Check("...over a run that actually swept at full speed (" + sweepTicks + " of "
                + DriveTicks + " ticks, peak "
                + peakAnnounced.ToString("0.000", CultureInfo.InvariantCulture) + " px/ms)",
                peakAnnounced > 0f && sweepTicks > DriveTicks / 8);

            foreach (GameComponent extra in NewSince(game, before))
            {
                if (!planted.Contains(extra))
                {
                    planted.Add(extra);
                }
            }
        }

        // ---- 3. THE HOST'S DECISION, as a pure function ---------------------------------------
        //
        // ResolveBaseVelocity is split out for exactly this (the OwnsSlotCore precedent): all
        // four of its branches are chosen by something other than the numbers, so only a
        // table-driven test can cover them. Card c1a38ef9 measured that a mutation dropping its
        // anchored branch passed every other leg it had -- the scripted branch is one more of the
        // same shape.
        private static void SectionDecision(StringBuilder sb, Action<string, bool> Check)
        {
            sb.Append(" 3. NetSession.ResolveBaseVelocity ranks the scripted answer\n");

            // A declared vector that is ZERO, which is the whole situation: this is what a type
            // writing Position directly really reports.
            Vector2 declared = Vector2.Zero;
            Vector2 announced = new Vector2(-0.78f, 0f);   // the sweep
            Vector2 last = new Vector2(700f, 235f);
            Vector2 now = new Vector2(697f, 235f);          // 3px over 60ms -- a STALE phase
            Vector2 far = new Vector2(1145f, 70f);          // a park

            Vector2 sweeping = NetSession.ResolveBaseVelocity(declared, anchored: false,
                teleported: false, now, true, last, 0L, 60L, scripted: true, announced);
            Vector2 preCard = NetSession.ResolveBaseVelocity(declared, anchored: false,
                teleported: false, now, true, last, 0L, 60L, scripted: false, announced);

            Check("a SCRIPTED entity's velocity is the announced one (" + Fmt(sweeping) + ")",
                sweeping == announced);
            // THE CONTROL, and the card's magnitude claim in one line: on the identical samples
            // the pre-card path reports the previous phase's crawl -- 0.05 px/ms against a real
            // 0.78, i.e. the client dead-reckons at 6% of the truth for a whole turn.
            Check("CONTROL ...where the pre-card path reports the STALE difference instead ("
                + Fmt(preCard) + ")", preCard != announced && preCard.Length() < 0.1f);

            // The two fallbacks the card is named after. Both used to return the declared ZERO.
            Check("a MARKED teleport carries the announced velocity, not the declared zero",
                NetSession.ResolveBaseVelocity(declared, anchored: false, teleported: true, far,
                    true, last, 0L, 60L, scripted: true, announced) == announced);
            Check("CONTROL ...where the pre-card path froze the puppet at (0,0)",
                NetSession.ResolveBaseVelocity(declared, anchored: false, teleported: true, far,
                    true, last, 0L, 60L, scripted: false, announced) == Vector2.Zero);
            Check("a FIRST observation carries it too, so a fresh puppet is born moving",
                NetSession.ResolveBaseVelocity(declared, anchored: false, teleported: false, now,
                    false, last, 0L, 60L, scripted: true, announced) == announced);

            // ANCHORED STILL WINS. Not a preference: a type holding both is a contradiction
            // section 1 asserts nobody creates, and this is the tie-break of last resort.
            Vector2 honest = new Vector2(0f, 0.31f);
            Check("ANCHORED still outranks scripted on a type that somehow claimed both",
                NetSession.ResolveBaseVelocity(honest, anchored: true, teleported: false, now,
                    true, last, 0L, 60L, scripted: true, announced) == honest);

            // And an UNSCRIPTED entity is untouched by this card -- the ordinary path still
            // differentiates, which is what every one of the other 28 types gets.
            Check("an ordinary entity is still differenced (" + Fmt(preCard) + ")",
                preCard == (now - last) / 60f);
        }

        // ---- 4. END TO END: the frames a peer actually receives --------------------------------
        //
        // The wire is the only place the host's decision is observable -- a refused sample and an
        // entity standing still are byte-identical anywhere else. A REAL SpiderBoss is driven
        // through its choreography inside a REAL host session, and every snapshot the peer
        // receives is compared against the boss's true instantaneous velocity, with the pre-card
        // estimate recomputed from the same samples as the control.
        private static void SectionWire(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted, PinnedNetHost clock)
        {
            sb.Append(" 4. the velocity the peer receives, over a real fly-by\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];

            List<byte[]> snaps = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= NetProtocol.SnapshotHeaderBytes
                    && payload[0] == NetProtocol.MsgWorldSnapshot)
                {
                    snaps.Add(payload);
                }
            }

            NetScene.Current = new ScriptedScene();
            NetSession.StartForTest(game, host: true, ours, Room);
            peer.Open(Room);
            peer.OnData += Sniff;
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("PRECONDITION the scripted client paired with a real host session",
                NetSession.IsHost && NetSession.PeerUp);
            if (!NetSession.PeerUp)
            {
                return; // SendWorldSnapshot is peer-gated; every leg below would be vacuous
            }

            // NetSession.Metrics is a process-wide counter with no reset, and eaNetTeleport
            // deliberately raises this one as its own section-1c control -- so the leg at the end
            // of this section reads a DELTA, never the absolute (the house rule for every
            // scenario suite here).
            long unmarkedBefore = NetSession.Metrics.UnmarkedTeleports;

            HashSet<GameComponent> before = SnapshotBin(game);
            SpiderBoss boss = SpiderBoss.NewSpiderBoss(bin, game);
            boss.Setup(intro: true);
            bin.Add((GameComponent)(object)boss);
            planted.Add((GameComponent)(object)boss);
            bin.TopOfTickFlush();
            bool gotId = NetIdRegistry.TryGetByComp((GameComponent)(object)boss,
                out NetIdRegistry.Entry entry);
            Check("PRECONDITION the planted SpiderBoss got a netId", gotId);
            if (!gotId)
            {
                return;
            }
            ushort netId = entry.Id;

            GameTime tick = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickMs));
            INetEntity seam = boss;
            // AN ENTITY'S TURN IS NOT THE SNAPSHOT INTERVAL. The host sends a packet every 60 ms
            // but each packet carries at most SnapshotMaxEntries (16) of the live set, so a given
            // entity is sampled every interval * live/16 -- 60 ms at 16 live entities, 480 ms at
            // 128, ~1.2 s at 320. THAT is the blind window this card is about, so the drive
            // advances both the boss and the injected clock by 29 ticks (~483 ms) per snapshot,
            // which is the N=128 world modelled exactly with one entity in it. At the 60 ms floor
            // the same run reads a pre-card error of only ~0.195 px/ms (measured): the defect is
            // real there too, but it is the big world that makes it a pop.
            const int ticksPerTurn = 29;

            Vector2 prevSample = boss.Position;
            Vector2 prevTruth = Vector2.Zero;
            bool havePrev = false;
            int turns = 0;
            int marked = 0;
            ushort peerSeq = 1;
            List<Vector2> truths = new List<Vector2>();
            List<Vector2> wireVels = new List<Vector2>();
            List<Vector2> preCards = new List<Vector2>();

            for (int t = 0; t < DriveTicks / ticksPerTurn; t++)
            {
                for (int i = 0; i < ticksPerTurn; i++)
                {
                    boss.Update(tick);
                }
                // The boss's own truth for the turn just ended, read AFTER the ticks so it is the
                // velocity the client is about to dead-reckon with -- the same instant
                // CaptureBaseState reads it.
                seam.TryGetNetScriptedVelocity(out Vector2 truth);
                Vector2 sampled = boss.Position;

                snaps.Clear();
                // THE PEER MUST KEEP TALKING OR THE RUN QUIETLY ENDS AFTER ~8 SECONDS.
                // `SendWorldSnapshot` is peer-gated and `PeerLost("timeout")` fires after
                // PeerTimeoutMs + PeerGraceMs of stream silence -- so a drive long enough to
                // cover a whole fly-by cycle outlives the pairing unless the scripted peer
                // heartbeats. `alive: false` is the documented shape: the ship stream doubles as
                // the heartbeat and is sent even with no live ship, so this spawns no puppet.
                peer.SendStream(NetProtocol.EncodeShipState(peerSeq++,
                    (uint)(t * ticksPerTurn * (long)Math.Round(TickMs)), Nowhere, Vector2.Zero,
                    0f, alive: false, 0, 0, 0f));
                clock.Advance(ticksPerTurn * (long)Math.Round(TickMs));
                NetSession.Update();
                wire.Pump();
                if (!LastEntry(snaps, netId, out byte flags, out Vector2 wireVel))
                {
                    continue;
                }
                bool wasMarked = (flags & NetProtocol.NetSnapshotFlags.Teleported) != 0;
                if (wasMarked)
                {
                    marked++;
                }

                // The pre-card estimate, recomputed from the identical samples the host had.
                Vector2 preCard = havePrev && !wasMarked
                    ? (sampled - prevSample) / (ticksPerTurn * TickMs)
                    : Vector2.Zero;

                if (havePrev)
                {
                    turns++;
                    truths.Add(truth);
                    wireVels.Add(wireVel);
                    preCards.Add(preCard);
                }
                prevSample = sampled;
                havePrev = true;
            }

            // JUDGED AFTER THE FACT, AGAINST THE BOSS'S OWN TOP SPEED. See SweepFraction: a
            // literal px/ms threshold silently stops meaning "sweeping" at a lower difficulty.
            float peakTruth = 0f;
            foreach (Vector2 v in truths)
            {
                peakTruth = Math.Max(peakTruth, v.Length());
            }
            float worstWire = 0f;
            float worstPreCard = 0f;
            float wireAtSweepStart = 0f;
            float preCardAtSweepStart = 0f;
            int sweepStarts = 0;
            for (int i = 0; i < truths.Count; i++)
            {
                worstWire = Math.Max(worstWire, (wireVels[i] - truths[i]).Length());
                worstPreCard = Math.Max(worstPreCard, (preCards[i] - truths[i]).Length());
                // THE CARD'S SYMPTOM, caught at the instant it happens: a turn on which the boss
                // is genuinely sweeping after having been still -- the pause->sweep boundary the
                // whole card is about.
                //
                // MEASURED OVER EVERY SUCH TURN, NOT THE FIRST. How wrong the pre-card estimate
                // is depends on WHERE inside the turn the pause ended: a boundary landing early
                // in the turn leaves the difference nearly right, one landing late leaves it
                // nearly zero. The first boundary of a run is whichever the phase happens to
                // give (measured 0.296 of a 0.78 sweep), so pinning the magnitude to it would be
                // pinning an accident of alignment. The wire must be exact at ALL of them; the
                // pre-card estimate need only be badly wrong at one.
                if (truths[i].Length() > SweepFraction * peakTruth
                    && (i == 0 || truths[i - 1].Length() < 0.1f * peakTruth))
                {
                    sweepStarts++;
                    wireAtSweepStart = Math.Max(wireAtSweepStart,
                        (wireVels[i] - truths[i]).Length());
                    preCardAtSweepStart = Math.Max(preCardAtSweepStart,
                        (preCards[i] - truths[i]).Length());
                }
            }

            Check("PRECONDITION the run produced snapshot turns (" + turns + ")", turns > 50);
            Check("PRECONDITION at least one park went out MARKED (" + marked + ")", marked >= 1);
            Check("PRECONDITION the boss reached a real sweep speed ("
                + peakTruth.ToString("0.000", CultureInfo.InvariantCulture) + " px/ms)",
                peakTruth > 0.1f);

            Check("every frame the peer receives carries the boss's true velocity (worst "
                + worstWire.ToString("0.0000", CultureInfo.InvariantCulture) + " px/ms)",
                worstWire < 0.02f * peakTruth);
            // THE CONTROL, and the whole card in one number: over the same samples the pre-card
            // estimate is out by most of a sweep speed at some point in the cycle.
            Check("CONTROL ...where the pre-card estimate is out by a whole phase (worst "
                + worstPreCard.ToString("0.0000", CultureInfo.InvariantCulture) + " px/ms)",
                worstPreCard > SweepFraction * peakTruth);

            Check("PRECONDITION the run crossed a pause->sweep boundary (" + sweepStarts + ")",
                sweepStarts >= 1);
            Check("at EVERY turn a sweep begins the peer already has the sweep velocity (worst "
                + wireAtSweepStart.ToString("0.0000", CultureInfo.InvariantCulture) + ")",
                sweepStarts >= 1 && wireAtSweepStart < 0.02f * peakTruth);
            Check("CONTROL ...where the pre-card path was still driving the previous phase (worst "
                + preCardAtSweepStart.ToString("0.0000", CultureInfo.InvariantCulture) + ")",
                preCardAtSweepStart > SweepFraction * peakTruth);

            // THE SAFETY NET IS STILL ARMED (card 76ec8bdb's deliberate asymmetry with the
            // anchored branch). A scripted type's wire velocity is announced, so it cannot be
            // judged -- but the SpiderBoss holds three of the game's four reposition sites, so
            // CaptureBaseState recomputes the raw difference for the diagnostic alone. A whole
            // fly-by cycle with every park MARKED must produce no accusation.
            Check("a fully-marked cycle raises no unmarked-teleport report (+"
                + (NetSession.Metrics.UnmarkedTeleports - unmarkedBefore) + ")",
                NetSession.Metrics.UnmarkedTeleports == unmarkedBefore);

            peer.OnData -= Sniff;
            foreach (GameComponent extra in NewSince(game, before))
            {
                if (!planted.Contains(extra))
                {
                    planted.Add(extra);
                }
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        // Whether `t` (or an ancestor below AlienDrawableGameComponent) overrides the named
        // virtual. Pass a property's getter as "get_Name".
        private static bool Overrides(Type t, string member)
        {
            MethodInfo m = t.GetMethod(member,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return m != null && m.DeclaringType != typeof(AlienDrawableGameComponent);
        }

        private static HashSet<GameComponent> SnapshotBin(Game game)
        {
            HashSet<GameComponent> set = new HashSet<GameComponent>();
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is GameComponent gc)
                {
                    set.Add(gc);
                }
            }
            return set;
        }

        private static List<GameComponent> NewSince(Game game, HashSet<GameComponent> before)
        {
            List<GameComponent> added = new List<GameComponent>();
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is GameComponent gc && !before.Contains(gc))
                {
                    added.Add(gc);
                }
            }
            return added;
        }

        // The entry for `netId` in the most recent snapshot packet, decoded with the real reader.
        private static bool LastEntry(List<byte[]> snaps, ushort netId, out byte flags, out Vector2 vel)
        {
            flags = 0;
            vel = Vector2.Zero;
            for (int s = snaps.Count - 1; s >= 0; s--)
            {
                byte[] packet = snaps[s];
                int off = NetProtocol.SnapshotHeaderBytes;
                for (int i = 0; i < packet[1]; i++)
                {
                    if (!NetProtocol.TryReadSnapshotEntry(packet, ref off, out ushort id, out _,
                        out byte f, out NetBaseState st, out _, out _))
                    {
                        break;
                    }
                    if (id == netId)
                    {
                        flags = f;
                        vel = st.Vel;
                        return true;
                    }
                }
            }
            return false;
        }

        private static void Teardown(StringBuilder sb, Action<string, bool> Check,
            Game game, ComponentBin bin, List<GameComponent> planted)
        {
            sb.Append(" 5. teardown\n");
            foreach (GameComponent comp in planted)
            {
                bin.Remove(comp);
            }
            bin.TopOfTickFlush();
            int left = 0;
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is GameComponent gc && planted.Contains(gc))
                {
                    left++;
                }
            }
            Check("every entity this suite built or provoked is out of the world ("
                + planted.Count + " planted, " + left + " left)", left == 0);
            Check("no puppets are still registered (live=" + NetPuppets.LiveCount + ")",
                NetPuppets.LiveCount == 0);
        }

        private static string Join(List<string> names)
        {
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }

        private static string Fmt(Vector2 v)
        {
            return v.X.ToString("0.000", CultureInfo.InvariantCulture) + ","
                + v.Y.ToString("0.000", CultureInfo.InvariantCulture);
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

        private static string Tally(int pass, int fail)
        {
            return "[netscript] " + pass + "/" + (pass + fail) + " checks passed"
                + (fail == 0 ? "\n" : " -- " + fail + " FAILED\n");
        }

        private sealed class ScriptedScene : INetScene
        {
            public Levels Level => Levels.Level1;

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
