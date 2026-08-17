using System.Text;
using EvilAliens;
using EvilAliensWeb.Compat.Net.Descriptors;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for ANCHORED MOTION -- the motion-parameter lane (card c1a38ef9).
    // Invoke with eaNetMotion() / `eval NetMotionTest`; menu-runnable and leave-no-trace.
    //
    // WHY IT EXISTS. Every defect this lane can develop is SILENT and looks like the pre-card
    // build, which shipped, works, and is merely rougher:
    //
    //   * a lost NetPathAnchored override -- the host goes back to finite-differencing the wasp's
    //     swivel into the base velocity and the client stops integrating. Nothing throws, no
    //     counter moves, and pupPops stays at the contented 0 it already reads (the whole point
    //     of the smoothness family: these stutters never approach SnapThresholdPx);
    //   * a spawn anchor that stops being sent or stops being applied -- the client's wasp bobs
    //     in an unrelated phase about an unrelated height, which the position correction then
    //     fights every turn, i.e. it looks exactly like ordinary jitter;
    //   * a Lazer rate that arrives at the wrong scale or the wrong sign -- the beam grows or
    //     sweeps at the wrong speed between turns, on a COLLIDABLE hitbox.
    //
    // None of those is visible in a frame, and a timed screenshot of a beam that is supposed to
    // be moving proves nothing either way. So the observables here are the DATA: the predicate,
    // the bytes the real descriptors produce, and what the real per-tick drive does to a real
    // entity over a chosen dt.
    //
    // The SHAPE of the smoothing -- whether anchored motion is actually smoother than the
    // estimator it replaced -- is NOT asserted here and cannot be: it is a property of a whole
    // run against a host control, which is
    // `python tools/sim/net_puppet_drive_sim.py --smoothness`'s job. This suite asserts that the
    // mechanism is wired and exact; that one asserts it is worth having.
    //
    // Leave-no-trace: almost every entity is CONSTRUCTED and never added to the bin or to
    // Game.Components, exactly as NetEntityTest does, and Initialize() is called by hand on the
    // ones that need their spawn rolls to have happened -- it is what ComponentBin.Add would have
    // run synchronously, and running it on a detached instance touches no collection. The
    // exceptions are the pooled types: anything reached through New*/CreatePuppet has been taken
    // OUT of the recycle pool, and section 4's sweep-order leg genuinely Adds one (that is its
    // whole subject). All of them are bin.Remove()d on the way out.
    internal static class NetMotionTest
    {
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

            sb.Append("[netmotion] anchored motion -- sent rates + path anchors (card c1a38ef9)\n");

            sb.Append(" 1. the NetPathAnchored predicate\n");
            SectionPredicate(game, Check);

            sb.Append(" 2. FlyingSpider path anchor through the real descriptor\n");
            SectionSpiderAnchor(bin, game, Check);

            sb.Append(" 3. FlyingSpider drifting parameters are EASED, not snapped\n");
            SectionSpiderEase(bin, game, Check);

            sb.Append(" 4. Lazer sent rates\n");
            SectionLazerRates(bin, game, Check);

            sb.Append(" 5. the HOST's velocity decision (NetSession.ResolveBaseVelocity)\n");
            SectionHostVelocity(Check);

            sb.Append(" 6. Ball local rotation -- the junkboss rocks (card 566474ae)\n");
            SectionBallSpin(bin, game, Check);

            sb.Append(" 7. Ball hit-test radius -- the same rocks (card 1210e14e)\n");
            SectionBallRadius(bin, game, Check);

            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[netmotion] {0} passed, {1} failed\n", pass, fail));
            return sb.ToString();
        }

        // ---- 1. the predicate --------------------------------------------------------------
        //
        // A per-type constant, and the two users are the whole set. The UFO is the CONTROL and is
        // not decoration: a predicate hard-wired to true would satisfy both positives, and it
        // would be actively wrong -- a UFO's flight is a scripted position curve, so its declared
        // SpeedVector lies and anchoring it would dead-reckon it at a stale velocity.
        private static void SectionPredicate(Game game, System.Action<string, bool> check)
        {
            check("FlyingSpider is anchored (linear drift + its own swivel)",
                ((INetEntity)new FlyingSpider(game)).NetPathAnchored);
            check("Asteroid is anchored (constant velocity)",
                ((INetEntity)new Asteroid(game)).NetPathAnchored);
            check("a UFO is NOT anchored -- control, and its Position curve is why",
                !((INetEntity)new UFO(game)).NetPathAnchored);

            // An anchored type with no periodic component must return the base's ZERO, or the
            // driver would difference a stale value into the puppet's position every tick.
            check("Asteroid's path offset is zero (no periodic component)",
                ((INetEntity)new Asteroid(game)).NetPathOffset == Vector2.Zero);
        }

        // ---- 2. the spawn anchor -----------------------------------------------------------

        private static void SectionSpiderAnchor(ComponentBin bin, Game game,
            System.Action<string, bool> check)
        {
            FlyingSpiderDescriptor desc = new FlyingSpiderDescriptor();

            // A "host" spider: constructed and Initialized, so it has ROLLED its own entry height
            // and swivel phase exactly as a real spawn does.
            FlyingSpider host = new FlyingSpider(game);
            host.Setup(isbackground: false);
            host.Initialize();

            byte[] extra = new byte[64];
            int len = desc.EncodeSpawnExtra(host, extra, 0);
            check("the spawn extras are the anchored layout (flags + height + phase)", len == 5);

            NetBaseState state = default;
            state.Pos = new Vector2(850f, host.NetStartHeight);
            FlyingSpider puppet = (FlyingSpider)desc.CreatePuppet(bin, game, state, extra, 0, len);
            // CreatePuppet stores the anchor; ComponentBin.Add would run Initialize, which is
            // what applies it (and what would otherwise clobber it with its own rolls). Running
            // it by hand here is that step, without entering the world.
            puppet.Initialize();

            check("the puppet adopts the host's start height",
                Near(puppet.NetStartHeight, host.NetStartHeight, 1.5f));
            check("the puppet adopts the host's swivel phase",
                Near(puppet.NetSwivelPhase, host.NetSwivelPhase, 0.002f));

            // NEGATIVE CONTROL, and it is what makes the two legs above mean something: a puppet
            // built from the PRE-CARD one-byte extras block keeps Initialize's own rolls. Without
            // it, a rig whose "host" and "puppet" happened to roll alike would pass. The height is
            // the discriminator -- it is uniform over 0..475 -- while two phases can agree far
            // more often.
            //
            // ASSERTED OVER N INDEPENDENT PUPPETS, and that is the deflake (card c41a89a2). One
            // puppet agreeing with the host inside 1.5px by chance is a ~0.6% event, which is a
            // spurious FAILURE roughly once in 160 verification runs -- measured once in-suite.
            // The guarded regression (CreatePuppet applying the anchor from a short extras block)
            // is deterministic, so EVERY puppet would adopt the host's height; requiring only
            // that ONE kept its own roll therefore loses no sensitivity while the coincidence
            // collapses to 0.006^N. Seeding the roll instead was rejected: RandomHelper is
            // process-global and this suite is leave-no-trace, and host and puppet draw from the
            // same stream, so a seed does not by itself make them differ.
            const int UnanchoredSamples = 4;
            FlyingSpider[] unanchored = new FlyingSpider[UnanchoredSamples];
            int ownRoll = 0;
            int distinctHeights = 0;
            for (int i = 0; i < UnanchoredSamples; i++)
            {
                unanchored[i] = (FlyingSpider)desc.CreatePuppet(bin, game, state, extra, 0, 1);
                unanchored[i].Initialize();
                if (!Near(unanchored[i].NetStartHeight, host.NetStartHeight, 1.5f))
                {
                    ownRoll++;
                }
                if (i > 0 && !Near(unanchored[i].NetStartHeight, unanchored[0].NetStartHeight, 1.5f))
                {
                    distinctHeights++;
                }
            }
            check(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "...and a pre-card 1-byte extras block leaves the puppet on its OWN roll"
                        + " ({0}/{1} kept theirs)", ownRoll, UnanchoredSamples),
                ownRoll > 0);
            // SENTINEL for the deflake above, not for the wire: the 0.006^N claim holds only
            // while those N rolls are INDEPENDENT. Nothing correlates them today (Initialize's
            // height roll is unconditional and not PosePinned-gated), so this cannot realistically
            // fail now -- it has the same coincidence shape it guards, at 0.006^3 -- and it exists
            // so that a future change which DID correlate them (a pin reaching that roll, a
            // shared-seed refactor) fails HERE rather than silently reverting the leg above to its
            // ~0.6% coincidence with every assertion still green. It reads a puppet per i > 0, so
            // it says nothing at UnanchoredSamples < 2.
            check("...and those rolls are INDEPENDENT (sentinel: the deflake's own premise)",
                distinctHeights > 0);

            // The offset really is the swivel: two phases a quarter cycle apart must differ by
            // about the full amplitude, and the shape must be the sine Update draws.
            puppet.NetApplySwivel(50f, 0.25f);
            DriveOnce(puppet, 1000f); // one long tick spends the whole phase ease
            float atQuarter = ((INetEntity)puppet).NetPathOffset.Y;
            puppet.NetApplySwivel(50f, 0.75f);
            DriveOnce(puppet, 1000f);
            float atThreeQuarter = ((INetEntity)puppet).NetPathOffset.Y;
            check("NetPathOffset tracks the swivel (quarter and three-quarter cycle oppose)",
                atQuarter * atThreeQuarter < 0f
                    && System.Math.Abs(atQuarter - atThreeQuarter) > 10f);

            // All of them came out of the recycle pool via CreatePuppet -> NewFlyingSpider, so
            // they are Remove()d on the way out -- section 4 does the same with its beams. Note
            // what that does NOT do: nothing here ever entered `collection`, so no
            // ComponentRemoved fires and none of them lands back in the bin's idleList. They are
            // dropped rather than returned, and the next run re-`new`s what it needs.
            bin.Remove(puppet);
            for (int i = 0; i < unanchored.Length; i++)
            {
                bin.Remove(unanchored[i]);
            }
        }

        // ---- 3. easing ---------------------------------------------------------------------
        //
        // THE POINT OF THE WHOLE LANE IS THAT A CORRECTION IS A NUDGE. Both drifting parameters
        // are recorded by ApplyStateExtra and SPENT by NetDriveExtras over real time, because
        // NetPuppets.Drive DIFFERENCES NetPathOffset across the tick -- so anything applied in
        // one step moves the puppet by that much in one frame, which is the artefact this card
        // exists to remove. A regression that "simplified" the ease into a straight write would
        // change no output but this.
        private static void SectionSpiderEase(ComponentBin bin, Game game,
            System.Action<string, bool> check)
        {
            FlyingSpiderDescriptor desc = new FlyingSpiderDescriptor();

            FlyingSpider host = new FlyingSpider(game);
            host.Setup(isbackground: false);
            host.Initialize();
            byte[] extra = new byte[64];
            int len = desc.EncodeStateExtra(host, extra, 0);
            check("the state extras are the anchored layout (amplitude + phase)", len == 4);

            FlyingSpider p = new FlyingSpider(game);
            p.Setup(isbackground: false);
            p.NetForceAnchor(300f, 0f);
            p.Initialize();

            // A large amplitude step: 50 (the default a puppet starts on) -> 200, delivered as a
            // REAL state-extra frame through the REAL descriptor. Driving NetApplySwivel directly
            // would be the wrong seam and was measured to be: a mutation that spent the whole
            // correction inside ApplyStateExtra -- which is exactly the "simplify the ease into a
            // straight write" regression this section exists to catch -- passed a version of
            // these legs that called the entity by hand.
            byte[] step = new byte[4];
            step[0] = 200;
            step[1] = 0;
            step[2] = extra[2];
            step[3] = extra[3];

            float before = p.NetLocalSwivelAmplitude;
            desc.ApplyStateExtra(p, step, 0, 4);
            DriveOnce(p, 16.7f);
            float afterOne = p.NetLocalSwivelAmplitude;
            for (int i = 0; i < 200; i++)
            {
                DriveOnce(p, 16.7f);
            }
            float afterMany = p.NetLocalSwivelAmplitude;

            check("the amplitude starts at the un-modified default", Near(before, 50f, 0.01f));
            check("one tick moves it only PART of the way (a nudge, not a step)",
                afterOne > before && afterOne < before + (200f - before) * 0.5f);
            check("...and it does converge, so the ease is not a leak",
                Near(afterMany, 200f, 1f));

            // THE WRAPPED SHORTEST ARC. A phase 0.95 -> 0.05 correction is +0.1 of a cycle, not
            // -0.9: a naive difference walks the wasp almost a whole period the wrong way, which
            // for a 2.7s swivel is a full visible dive. Asserted on the recorded ERROR rather
            // than on the phase itself, because the timer also advances on its own during the
            // tick and would blur the sign this leg is about.
            check("a phase correction across the 1 -> 0 wrap takes the SHORT arc",
                ShortestArc(0.95f, 0.05f) > 0f && ShortestArc(0.95f, 0.05f) < 0.2f);
            check("...and the other way round too", ShortestArc(0.05f, 0.95f) < 0f);
        }

        // Drives the real FlyingSpider.NetApplySwivel and reads back the arc it recorded, by
        // measuring how far ONE full ease moves the phase. The type keeps the error private, so
        // this is the observable: a naive (target - current) would report ~-0.9 where the short
        // arc is +0.1.
        private static float ShortestArc(float from, float to)
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            FlyingSpider f = new FlyingSpider(bin.Game);
            f.Setup(isbackground: false);
            f.NetForceAnchor(300f, from);
            f.Initialize();
            f.NetApplySwivel(50f, to);
            float start = f.NetSwivelPhase;
            // A tick long enough to spend the whole correction, but the timer advances too --
            // so subtract the free-running part, which is dt / Duration of a cycle.
            const float dtMs = 250f;
            DriveOnce(f, dtMs);
            float moved = f.NetSwivelPhase - start + dtMs / 2700f;
            if (moved > 0.5f)
            {
                moved -= 1f;
            }
            else if (moved < -0.5f)
            {
                moved += 1f;
            }
            return moved;
        }

        // ---- 4. Lazer rates ----------------------------------------------------------------

        private static void SectionLazerRates(ComponentBin bin, Game game,
            System.Action<string, bool> check)
        {
            LazerDescriptor desc = new LazerDescriptor();

            Lazer host = Lazer.NewLazer(bin, game);
            host.Initialize();
            host.SetupSingleShot(new Vector2(400f, 300f), 1.0f, 50f, playSound: false);

            float modifier = Settings.GetInstance().DifficultyModifier;
            check("the host reports its REAL growth rate (growthspeed x DifficultyModifier)",
                Near(host.NetLenRate, 0.4f * modifier, 0.0005f));
            // The lead rate is gated on `freed` and the length rate on `stopped`, so a beam that
            // has just been fired reports growth and no lead catch-up. Asserting the gate rather
            // than just the number is what stops a readback that ignored Update's own conditions.
            check("...and no lead rate until the emitter lets go", host.NetLeadRate == 0f);

            // Nothing sweeps a single shot, so the angular rate is honestly zero -- and the
            // miniboss' constant is what a swept beam reports.
            //
            // THESE TWO USED TO SIT AFTER host.Free() AND NOW SIT BEFORE IT (card d6645119): the
            // sweep readback is gated on `freed` from this card on, so the old order was asserting
            // the constant on a beam that has been released, which is exactly the state that must
            // now report zero. The legs are MOVED rather than deleted -- their intent (a swept
            // beam declares Boss.LazerSweepRadPerMs) is the pin, and `host` stays un-freed for the
            // rest of the section so the frame it encodes below is a LIVE beam's.
            check("an unswept beam reports no angular rate", host.NetAngleRate == 0f);
            host.SetSweepRate(Boss.LazerSweepRadPerMs);
            check("a swept beam reports the sweeper's constant",
                Near(host.NetAngleRate, Boss.LazerSweepRadPerMs, 0.000001f));

            // ---- the RELEASED beam (card d6645119) -----------------------------------------
            //
            // A SECOND beam, so the live one above keeps its rates for the encode further down.
            // Boss.Update sweeps only the beams still in `lazors`, and its 3-beam eviction drops
            // one from that list in the SAME statement pair that calls Free() -- so `freed` is the
            // host's own "I have stopped turning this beam" and the rate has to follow it. The
            // reported defect is what happens when it does not: the client keeps integrating a
            // sweep the host abandoned, the next snapshot's aim snaps it back, and it does that
            // every turn for the beam's remaining seconds of life.
            Lazer released = Lazer.NewLazer(bin, game);
            released.Initialize();
            released.SetupSingleShot(new Vector2(400f, 300f), 1.0f, 50f, playSound: false);
            released.SetSweepRate(Boss.LazerSweepRadPerMs);
            // THE PRECONDITION IS PINNED, not assumed: a beam that was never sweeping in the first
            // place would satisfy the zero below for the wrong reason.
            check("a beam about to be released IS sweeping first (the precondition)",
                Near(released.NetAngleRate, Boss.LazerSweepRadPerMs, 0.000001f));
            check("...and no lead rate yet either", released.NetLeadRate == 0f);
            released.Free();
            check("the lead rate STARTS when the emitter lets go",
                Near(released.NetLeadRate, 0.4f * modifier, 0.0005f));
            check("...and the SWEEP rate goes to ZERO on the SAME event (card d6645119)",
                released.NetAngleRate == 0f);
            // The gate is `freed`, not "the beam has been re-Setup": a released beam that is still
            // in the world must keep reporting zero for the rest of its life, which is the whole
            // ~2.9s the abandoned beam takes to catch its own tail.
            released.SetSweepRate(Boss.LazerSweepRadPerMs);
            check("...and it STAYS zero even if the sweep constant is handed over again",
                released.NetAngleRate == 0f);

            // THE REAL SPAWN ORDER, and this leg is not optional: Boss.Update does
            // Setup -> SetSweepRate -> collection.Add, and ComponentBin.Add runs Initialize
            // SYNCHRONOUSLY. A first cut cleared the sweep rate from Initialize, which zeroed it
            // for every miniboss beam a frame after it was set -- the angular half of the wire
            // went permanently dead, and every leg above still passed because they never Added
            // the beam. So this one drives the ordering rather than the values.
            Lazer swept = Lazer.NewLazer(bin, game);
            swept.Setup(new Vector2(400f, 300f), 1.0f, null, 50f);
            swept.SetSweepRate(Boss.LazerSweepRadPerMs);
            bin.Add(swept);
            check("...and SURVIVES the Initialize that ComponentBin.Add runs on it",
                Near(swept.NetAngleRate, Boss.LazerSweepRadPerMs, 0.000001f));
            // The recycle half of the same rule: a pooled beam whose next owner does not sweep
            // must not inherit this one's rate. Setup is where that is cleared.
            swept.Setup(new Vector2(400f, 300f), 1.0f, null, 50f);
            check("...and a re-Setup beam does NOT inherit it (the recycle trap)",
                swept.NetAngleRate == 0f);
            bin.Remove(swept);

            byte[] extra = new byte[64];
            int len = desc.EncodeStateExtra(host, extra, 0);
            check("the state extras carry the values AND the three rates", len == 12);

            // THE WIRE FIELD ITSELF, both arms (card d6645119). The readback legs above prove the
            // property; these prove the byte the joining peer actually receives, which is the only
            // thing the defect ever showed up in. Offset 10 is the angleRate i16 at the rad scale.
            byte[] freedExtra = new byte[64];
            int freedLen = desc.EncodeStateExtra(released, freedExtra, 0);
            check("...and the released beam encodes the same 12-byte block", freedLen == 12);
            check("the wire's angleRate field carries the sweep for a LIVE beam",
                Near(NetProtocol.ReadScaledI16(extra, 10, NetProtocol.RateRadPerMsScale),
                    Boss.LazerSweepRadPerMs, 0.00002f));
            check("...and exactly ZERO for a RELEASED one (the card's stop-rotating event)",
                NetProtocol.ReadScaledI16(freedExtra, 10, NetProtocol.RateRadPerMsScale) == 0f);

            // The puppet: built through the real descriptor, fed the real frame, then DRIVEN.
            NetBaseState state = default;
            state.Pos = new Vector2(400f, 300f);
            Lazer puppet = (Lazer)desc.CreatePuppet(bin, game, state, extra, 0, 0);
            puppet.Initialize();
            desc.ApplyStateExtra(puppet, extra, 0, len);
            float lenBefore = puppet.NetLen;
            float aimBefore = puppet.NetAngle;
            DriveOnce(puppet, 100f);
            check("a driven puppet GROWS at the sent rate between turns",
                Near(puppet.NetLen - lenBefore, 0.4f * modifier * 100f, 1f));
            check("...and SWEEPS at the sent angular rate",
                Near(puppet.NetAngle - aimBefore, Boss.LazerSweepRadPerMs * 100f, 0.0005f));

            // ...and the released frame's puppet does NOT, on the identical drive. Same
            // descriptor, same driver, same dt -- the only difference is the byte above.
            Lazer stoppedPuppet = (Lazer)desc.CreatePuppet(bin, game, state, freedExtra, 0, 0);
            stoppedPuppet.Initialize();
            desc.ApplyStateExtra(stoppedPuppet, freedExtra, 0, freedLen);
            float stoppedAimBefore = stoppedPuppet.NetAngle;
            DriveOnce(stoppedPuppet, 100f);
            check("a puppet fed the RELEASED frame holds its aim on the same drive",
                stoppedPuppet.NetAngle == stoppedAimBefore);
            // ...and it is not simply frozen: the LENGTH still grows, because the host is still
            // extending that beam. A gate that killed all three rates would pass the leg above.
            check("...while still growing, so the gate is angular and nothing else",
                stoppedPuppet.NetLen > 0f
                    && Near(stoppedPuppet.NetLen - stoppedPuppet.NetLead,
                        // len - lead is what CollisionType draws its line from; both grew by the
                        // same 0.4*mod*100, so their DIFFERENCE is unchanged from the applied frame.
                        released.NetLen - released.NetLead, 1.5f));

            // ---- THE SAWTOOTH, measured (card d6645119) ------------------------------------
            //
            // The legs above say the mechanism is wired. This one says what it is WORTH, and it is
            // the shape of the reported defect rather than a property: the host has stopped
            // turning the beam, so its aim is constant, and every snapshot turn the client
            // integrates a stale rate away from it and then gets snapped back by NetApplyBeam.
            // Read as the worst aim error reached WITHIN a turn, over several turns.
            //
            // The pre-card arm is not a mutation -- it is the LIVE frame (a still-sweeping beam's
            // rate) applied against a host aim that no longer moves, which is precisely the state
            // the ungated readback produced for an evicted beam.
            // An EXACT tick count, not a `t < turnMs` accumulation: 240/16.7 leaves a part tick,
            // and rounding it up drove 250.5 ms -- over Lazer's 250 ms extrapolation cap, so the
            // arm below was measuring the cap as much as the sweep.
            const float TickMs = 16f;
            const int TicksPerTurn = 15; // 240 ms, comfortably inside the 250 ms cap
            const float TurnMs = TickMs * TicksPerTurn;
            float stale = WorstAimDrift(bin, game, desc, state, extra, len, TickMs, TicksPerTurn, 5);
            float fixedArm = WorstAimDrift(bin, game, desc, state, freedExtra, freedLen, TickMs, TicksPerTurn, 5);
            check("the PRE-CARD arm drifts a whole turn's worth of sweep, every turn ("
                    + stale.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + " rad)",
                Near(stale, System.Math.Abs(Boss.LazerSweepRadPerMs) * TurnMs, 0.005f));
            check("...and the gated one does not drift at all ("
                    + fixedArm.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + " rad)",
                fixedArm == 0f);

            // NEGATIVE CONTROL: the same puppet given the PRE-CARD six-byte block has no rates,
            // so it holds its beam between turns -- which is the reported chop, and is what every
            // leg above is measured against. Without it, a driver that grew the beam off some
            // other state would pass.
            Lazer noRates = (Lazer)desc.CreatePuppet(bin, game, state, extra, 0, 0);
            noRates.Initialize();
            desc.ApplyStateExtra(noRates, extra, 0, 6);
            float heldLen = noRates.NetLen;
            float heldAim = noRates.NetAngle;
            DriveOnce(noRates, 100f);
            check("a pre-card 6-byte block leaves the beam HOLDING (the reported chop)",
                noRates.NetLen == heldLen && noRates.NetAngle == heldAim);

            // The 250ms integration cap: a puppet whose peer went quiet must not grow without
            // limit across the screen, because the beam is collidable and can kill the local
            // player. Drive far past the cap and require the growth to have stopped at it.
            Lazer capped = (Lazer)desc.CreatePuppet(bin, game, state, extra, 0, 0);
            capped.Initialize();
            desc.ApplyStateExtra(capped, extra, 0, len);
            float capBefore = capped.NetLen;
            DriveOnce(capped, 5000f);
            check("integration is CAPPED at 250ms of silence (a collidable beam)",
                Near(capped.NetLen - capBefore, 0.4f * modifier * 250f, 1f));

            bin.Remove(host);
            bin.Remove(released);
            bin.Remove(puppet);
            bin.Remove(stoppedPuppet);
            bin.Remove(noRates);
            bin.Remove(capped);
        }

        // One puppet, `turns` snapshot turns of a host whose beam aim is STANDING STILL: apply the
        // frame, drive the turn out in real 16.7 ms ticks, and record how far the aim wandered from
        // the one the frame carried before the next apply pulls it back. Returns the worst such
        // excursion in radians -- 0 when the client is told the beam has stopped sweeping.
        //
        // The turn is 240 ms, comfortably under Lazer's 250 ms extrapolation cap (NetApplyRates
        // resets that budget on every apply), so the cap is not what this measures.
        private static float WorstAimDrift(ComponentBin bin, Game game, LazerDescriptor desc,
            in NetBaseState state, byte[] frame, int frameLen, float tickMs, int ticksPerTurn,
            int turns)
        {
            Lazer p = (Lazer)desc.CreatePuppet(bin, game, state, frame, 0, 0);
            p.Initialize();
            float worst = 0f;
            for (int turn = 0; turn < turns; turn++)
            {
                desc.ApplyStateExtra(p, frame, 0, frameLen);
                float hostAim = p.NetAngle; // what the host just said, quantisation included
                for (int t = 0; t < ticksPerTurn; t++)
                {
                    DriveOnce(p, tickMs);
                    float d = System.Math.Abs(p.NetAngle - hostAim);
                    if (d > worst)
                    {
                        worst = d;
                    }
                }
            }
            bin.Remove(p);
            return worst;
        }

        // ---- 5. the host's velocity decision -----------------------------------------------
        //
        // THE OTHER HALF OF THE LANE, and the half that was uncovered until it was split out of
        // CaptureBaseState. An anchored client integrates its own periodic component ON TOP of
        // the velocity the host sends, so if the host goes back to differentiating, that periodic
        // part is counted TWICE -- the wasp bobs at double amplitude and the correction fights it
        // every turn. Measured: a mutation dropping the anchored branch passed the whole probe
        // suite and every other leg here, which is why this section exists.
        //
        // It shares the decision with card e79bb994's teleport marker, so the two are asserted
        // TOGETHER -- they are separate reasons to refuse a finite difference and each must keep
        // working when the other is off.
        private static void SectionHostVelocity(System.Action<string, bool> check)
        {
            // One entity, sampled 100 ms apart, with a DECLARED velocity that deliberately
            // disagrees with the observed one -- which is the wasp's real situation: it drifts at
            // -0.12 px/ms in X and its Y displacement is the swivel, not travel.
            Vector2 declared = new Vector2(-0.12f, 0f);
            Vector2 last = new Vector2(400f, 300f);
            Vector2 now = new Vector2(388f, 320f); // -12 px of X drift, +20 px of swivel

            Vector2 anchored = NetSession.ResolveBaseVelocity(
                declared, anchored: true, teleported: false, now, true, last, 0L, 100L, scripted: false, announced: default);
            Vector2 observed = NetSession.ResolveBaseVelocity(
                declared, anchored: false, teleported: false, now, true, last, 0L, 100L, scripted: false, announced: default);

            check("an ANCHORED entity's velocity is its declared vector, never differenced",
                anchored == declared);
            // The CONTROL, and it is what makes the line above mean something: on the identical
            // inputs the ordinary path really does differ, and specifically it picks the swivel
            // up as Y travel -- the pollution the anchor exists to remove.
            check("...while an ordinary entity IS differenced on the same inputs",
                observed != declared && Near(observed.Y, 0.2f, 0.001f));

            // The first-observation fallback is the declared vector for BOTH, which is what makes
            // an anchored entity's first snapshot correct rather than a special case.
            check("with no history both paths fall back to the declared vector",
                NetSession.ResolveBaseVelocity(
                        declared, anchored: true, teleported: false, now, false, last, 0L, 100L, scripted: false, announced: default)
                    == declared
                    && NetSession.ResolveBaseVelocity(
                        declared, anchored: false, teleported: false, now, false, last, 0L, 100L, scripted: false, announced: default)
                    == declared);

            // The teleport marker (card e79bb994) is the OTHER reason to refuse a difference, and
            // it still stands on the un-anchored path -- these two branches share one decision, so
            // a change to either must leave the other working.
            Vector2 far = new Vector2(1200f, 300f);
            check("a MARKED teleport is not differenced either",
                NetSession.ResolveBaseVelocity(
                    declared, anchored: false, teleported: true, far, true, last, 0L, 100L, scripted: false, announced: default)
                    == declared);
            // ...with its own control: the identical jump UNMARKED really is differenced, so
            // neither leg can be passing because the function refuses everything.
            check("...while the same jump UNMARKED is (the control for both branches)",
                NetSession.ResolveBaseVelocity(
                    declared, anchored: false, teleported: false, far, true, last, 0L, 100L, scripted: false, announced: default)
                    != declared);
        }

        // ---- 6. Ball local rotation ---------------------------------------------------------
        //
        // The junkboss rocks stepped to the replicated angle once per snapshot turn -- up to ~13.7
        // degrees every 240 ms -- because `rotation` advances only in a frozen puppet's Update.
        // The card asks for "the same system as the asteroids", and that is what ships:
        // Ball.NetSpinPerMs, unconditional, exactly as Asteroid's.
        //
        // THE LEG THAT MATTERS MOST HERE IS 6a's, AND IT IS NOT ABOUT THE NET LAYER AT ALL. The
        // design turns on one fact about `Ball.Update` that reads the other way round on a careless
        // pass: `connected` picks the SIGN of its step to chase the bearing to its owner, which
        // looks like settling, but both branches step by the same fixed `rotationspeed * dt`. It is
        // bang-bang, not proportional, so it cannot settle -- a connected ball turns at FULL rolled
        // speed and merely wobbles in direction. A first cut of this card read it as a lock and
        // built a wire bit, a new INetEntity member and a protocol bump on top of that reading.
        // 6a is what stops the next reader repeating it.
        private static void SectionBallSpin(ComponentBin bin, Game game,
            System.Action<string, bool> check)
        {
            // ---- 6a. what the real state machine does to the angle ---------------------------
            //
            // GROUND TRUTH FROM THE GAME, NOT FROM A READER (NetScriptedMotionTest section 2's
            // shape): a real Ball with a real JunkBoss owner is driven through its own Update
            // until it genuinely reaches `connected`, and the angle it produces is measured --
            // rather than a table written from the same reading of Ball.cs the override was.
            JunkBoss boss = JunkBoss.NewJunkBoss(bin, game);
            boss.Setup(false);
            boss.Initialize();
            boss.Position = new Vector2(400f, 300f);

            Ball ball = Ball.NewBall(bin, game);
            ball.Setup(boss);
            ball.Initialize();

            float rolled = ((INetEntity)ball).NetSpinPerMs;
            check("a Ball spins locally at its OWN rolled rate (the Asteroid seam)", rolled != 0f);
            // The seam is UNCONDITIONAL, so the driver never re-snaps a Ball puppet's angle in any
            // state -- the default NetSpinPerMs != 0 owner test in ApplySnapshotState. Asserted
            // through the interface, which is what the driver reads.
            check("...and it is the interface's answer too, so the driver sees it",
                ((INetEntity)ball).NetSpinPerMs == rolled);

            // THE FREE-SPIN BASELINE IS OBSERVED, NOT READ OFF THE SEAM. Differencing `rotation`
            // over a few `startup` ticks measures what Update actually does; taking |NetSpinPerMs|
            // instead would couple this measurement to the very override it is supposed to be
            // independent of, and a build that zeroed the seam would divide by zero rather than
            // report a wrong rate. Section 6a is a statement about Ball.Update and nothing else.
            float spinFrom = ((INetEntity)ball).NetRotation;
            for (int i = 0; i < 10; i++)
            {
                ball.Update(Tick(16.7f));
            }
            float freeRatePerMs = System.Math.Abs(((INetEntity)ball).NetRotation - spinFrom)
                / (10f * 16.7f);
            check("a ball in `startup` free-spins, and at the rate the seam declares",
                freeRatePerMs > 0f && Near(freeRatePerMs, System.Math.Abs(rolled),
                    System.Math.Abs(rolled) * 0.01f));

            // Drive it. CollidesWith is safe to call every tick: `startup` has no case in that
            // switch, and `attracted` is the one that transitions. ~8.4 s of sim is needed (a
            // 5 s starttimer, then the speed decaying under 0.01), so this is generous.
            bool connected = false;
            int ticks = 0;
            for (; ticks < 800 && !connected; ticks++)
            {
                ball.Update(Tick(50f));
                ball.CollidesWith(boss);
                connected = ball.IsConnected();
            }
            // THE PRECONDITION IS PINNED, NOT WAITED OUT (card af4c3694). The measurement below is
            // about a CONNECTED ball and passes vacuously on a run that never got there.
            check("a real Ball reaches `connected` against a real JunkBoss (" + ticks + " ticks)",
                connected);
            if (!connected)
            {
                bin.Remove(ball);
                bin.Remove(boss);
                return;
            }

            // THE FACT THE WHOLE DESIGN RESTS ON, measured rather than trusted: over 10 s of
            // connected ticks the mean |per-tick turn| comes out at 1.00x the ball's own free
            // roll, with a handful to a hundred-odd direction reversals. Measured at 1.00x for
            // 16 of 16 balls across two runs while this card was being designed.
            float sumAbs = 0f;
            int reversals = 0;
            float lastSign = 0f;
            float prev = ((INetEntity)ball).NetRotation;
            const int LockTicks = 600;
            for (int i = 0; i < LockTicks; i++)
            {
                ball.Update(Tick(16.7f));
                ball.CollidesWith(boss);
                float now = ((INetEntity)ball).NetRotation;
                float d = now - prev;
                prev = now;
                sumAbs += System.Math.Abs(d);
                float sign = d > 0f ? 1f : (d < 0f ? -1f : 0f);
                if (sign != 0f && lastSign != 0f && sign != lastSign) { reversals++; }
                if (sign != 0f) { lastSign = sign; }
            }
            float meanPerMs = sumAbs / (LockTicks * 16.7f);
            check("a CONNECTED ball turns at its FULL free-spin speed, only the sign wobbles ("
                    + (meanPerMs / freeRatePerMs).ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture) + "x, "
                    + reversals + " reversals)",
                Near(meanPerMs, freeRatePerMs, freeRatePerMs * 0.01f));
            // ...and the reversals are what make it a wobble rather than a spin. Both halves are
            // needed: a build where the sign stopped flipping would pass the rate leg alone, and a
            // build where the STEP became proportional would pass the reversal leg alone.
            check("...and it really does reverse (not a constant spin in disguise)", reversals > 0);
            bin.Remove(ball);
            bin.Remove(boss);

            // ---- 6b. the real driver, on real puppets --------------------------------------
            //
            // NetPuppets.OnSpawn / OnSnapshotEntry / Drive with no session, NetStaleTest's shape.
            // Nothing below reads a COPY of a driver line: the rotation these puppets end up with
            // is the one NetPuppets actually gave them.
            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                check("SKIPPED the driver legs (a session, level or attract demo is up)", false);
                return;
            }

            const ushort IdBall = 60421;
            const ushort IdControl = 60422;
            const byte TypeBall = 5;
            const byte TypeEvilBullet = 0;
            check("registry index " + TypeBall + " is the Ball descriptor",
                NetTypeRegistry.Get(TypeBall) is BallDescriptor);

            NetPuppets.Enable(game);
            try
            {
                NetBaseState st = default;
                st.Pos = new Vector2(-600f, -600f);
                st.Scale = 1f;
                st.Rotation = 0f;
                byte[] variant = new byte[1] { 1 };

                if (NetPuppets.OnSpawn(IdBall, TypeBall, st, variant, 0, 1) != SpawnRejectKind.None
                    || !(NetPuppets.FindPuppet(IdBall) is Ball pup))
                {
                    check("a Ball puppet was built for the driver legs", false);
                    return;
                }
                check("a Ball puppet was built for the driver legs", true);

                // The host's angle is deliberately PI away from the puppet's, so an assignment
                // would be unmistakable -- and the puppet must ignore it in favour of its own spin.
                float pupRate = ((INetEntity)pup).NetSpinPerMs;
                ((INetEntity)pup).NetRotation = 0f;
                st.Rotation = 3.1415927f;
                // NOT `variant` (card 1210e14e): these are STATE extras, and since that card
                // BallDescriptor HAS some -- a flags byte whose bit0 is "connected". Handing it the
                // spawn-extra array {1} would decode as a CONNECTED ball and quietly change what
                // this rotation leg is measuring. Zero = unconnected, which is what it measured
                // before the state extras existed.
                byte[] ballState = new byte[1] { 0 };
                Apply(IdBall, TypeBall, st, ballState, 0, 1);
                check("a Ball puppet is NOT snapped to the wire's angle",
                    ((INetEntity)pup).NetRotation == 0f);
                float before = ((INetEntity)pup).NetRotation;
                NetPuppets.Drive(16.7f);
                float step = ((INetEntity)pup).NetRotation - before;
                check("...it advances every tick at its own rate instead, not once a turn",
                    pupRate != 0f && Near(step, pupRate * 16.7f, 0.0005f));

                // THE PRE-CARD CONTROL, and it is what makes the two legs above mean something:
                // the driver's per-turn assignment is still LIVE for a type that has not opted
                // out. EvilBullet does not, which is exactly where Ball was before this card --
                // same driver, same PI-away frame, and it steps the whole way in one go. That is
                // the reported chop, at its most extreme.
                if (NetPuppets.OnSpawn(IdControl, TypeEvilBullet, st, variant, 0, 0)
                    != SpawnRejectKind.None
                    || !(NetPuppets.FindPuppet(IdControl) is INetEntity ctrl))
                {
                    check("a control puppet was built", false);
                    return;
                }
                ctrl.NetRotation = 0f;
                check("CONTROL an EvilBullet declares no local spin (the pre-card Ball)",
                    ctrl.NetSpinPerMs == 0f);
                Apply(IdControl, TypeEvilBullet, st, variant, 0, 3);
                check("...so the identical frame SNAPS it -- the stepping Ball no longer does",
                    Near(ctrl.NetRotation, 3.1415927f, 0.01f));
            }
            finally
            {
                // BY HAND: Disable() clears the id maps but leaves the components it built in
                // Game.Components, drawn and in the Oracle scans. NetStaleTest's shape, and
                // collected BEFORE Disable since FindPuppet reads the maps it clears.
                foreach (ushort id in new ushort[] { IdBall, IdControl })
                {
                    INetEntity p = NetPuppets.FindPuppet(id);
                    if (p != null)
                    {
                        bin.Remove((GameComponent)(object)p);
                    }
                }
                NetPuppets.Disable();
            }
        }

        // ---- 7. Ball hit-test RADIUS (card 1210e14e) ----------------------------------------
        //
        // Section 6's rocks, a different property of them, and a defect that is SILENT in every
        // frame: `Ball.CollisionType` tests a connected ball at full radius and every other state at
        // 0.8, and a frozen puppet can reach neither `connected` nor any state of its own -- Update
        // never runs, and CheckOwner parks the null-owner puppet at `freed`. So the joining player
        // hit-tested the whole junkboss body 20% small: their bullets flew through rocks they had
        // visibly touched, those hits did not sustain their combo, and their ship survived a band
        // the host's screen called a collision. Nothing throws and no counter moves either way,
        // which is why this is a probe and not something anyone noticed.
        //
        // The fix replicates ONE BIT (BallDescriptor's first state extras, protocol v22) into a
        // field that answers the RADIUS question only. `state` still owns the gameplay arm, and
        // 7b's last leg is the assertion that keeps those two apart.
        private static void SectionBallRadius(ComponentBin bin, Game game,
            System.Action<string, bool> check)
        {
            // ---- 7a. the HOST's two radii, measured off a real connected ball ----------------
            //
            // 6a's shape and 6a's doctrine: the 1.25x is OBSERVED by reading CollisionType before
            // and after the real state machine reaches `connected`, not read off the 0.8f/1f
            // constants the fix itself uses. A build that broke both arms together would still have
            // to break this ratio to pass.
            JunkBoss boss = JunkBoss.NewJunkBoss(bin, game);
            boss.Setup(false);
            boss.Initialize();
            boss.Position = new Vector2(400f, 300f);

            Ball hostBall = Ball.NewBall(bin, game);
            hostBall.Setup(boss);
            hostBall.Initialize();

            float startupRadius = Radius(hostBall);
            check("a host Ball in `startup` has a radius at all (" + F2(startupRadius) + "px)",
                startupRadius > 0f);
            check("...and it does not claim to be connected", !hostBall.IsConnected());

            bool connected = false;
            int ticks = 0;
            for (; ticks < 800 && !connected; ticks++)
            {
                hostBall.Update(Tick(50f));
                hostBall.CollidesWith(boss);
                connected = hostBall.IsConnected();
            }
            // PINNED, NOT WAITED OUT (card af4c3694): every leg below is about a CONNECTED ball and
            // passes vacuously on a run that never got there.
            check("a real Ball reaches `connected` against a real JunkBoss (" + ticks + " ticks)",
                connected);
            if (!connected)
            {
                bin.Remove(hostBall);
                bin.Remove(boss);
                return;
            }

            float connectedRadius = Radius(hostBall);
            check("a CONNECTED host Ball is hit-tested 1.25x larger ("
                    + F2(connectedRadius) + "px vs " + F2(startupRadius) + "px, "
                    + F2(connectedRadius / startupRadius) + "x)",
                Near(connectedRadius, startupRadius * 1.25f, startupRadius * 0.001f));

            // The HOST ENCODE half, which nothing else in the suite reaches: the descriptor has to
            // put that answer on the wire, and a build whose EncodeStateExtra always wrote 0 would
            // pass every puppet leg below (they drive the bytes directly).
            INetTypeDescriptor desc = NetTypeRegistry.Get(5);
            byte[] enc = new byte[8];
            int encLen = desc.EncodeStateExtra(hostBall, enc, 0);
            check("the descriptor encodes the connected bit for the wire (len=" + encLen
                    + ", flags=" + enc[0] + ")",
                encLen == 1 && enc[0] == 1);
            bin.Remove(hostBall);
            bin.Remove(boss);

            Ball looseBall = Ball.NewBall(bin, game);
            looseBall.Setup(null);
            looseBall.Initialize();
            encLen = desc.EncodeStateExtra(looseBall, enc, 0);
            check("CONTROL an unconnected Ball encodes it CLEAR (len=" + encLen
                    + ", flags=" + enc[0] + ")",
                encLen == 1 && enc[0] == 0);
            bin.Remove(looseBall);

            // ---- 7b. the real descriptor and the real puppet layer ---------------------------
            //
            // 6b's shape: NetPuppets.OnSpawn / OnSnapshotEntry with no session. The radius these
            // puppets report is the one the production apply path actually gave them.
            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                check("SKIPPED the puppet legs (a session, level or attract demo is up)", false);
                return;
            }

            const ushort IdBall = 60431;
            const ushort IdControl = 60432;
            const byte TypeBall = 5;

            NetPuppets.Enable(game);
            try
            {
                NetBaseState st = default;
                st.Pos = new Vector2(-600f, -600f);
                st.Scale = 1f;
                st.Rotation = 0f;
                byte[] variant = new byte[1] { 1 };
                byte[] flagsClear = new byte[1] { 0 };
                byte[] flagsConnected = new byte[1] { 1 };

                if (NetPuppets.OnSpawn(IdBall, TypeBall, st, variant, 0, 1) != SpawnRejectKind.None
                    || !(NetPuppets.FindPuppet(IdBall) is Ball pup))
                {
                    check("a Ball puppet was built for the radius legs", false);
                    return;
                }
                check("a Ball puppet was built for the radius legs", true);

                // THE PRE-CARD STATE, and the leg that must NOT be the only one: a puppet that has
                // heard nothing keeps the small radius. That is correct (a just-spawned ball really
                // is in `startup`) AND it is exactly the whole defect, so it proves nothing alone.
                float smallRadius = Radius(pup);
                check("a fresh Ball puppet reads the SMALL radius (" + F2(smallRadius) + "px)",
                    smallRadius > 0f && !pup.IsConnected());

                Apply(IdBall, TypeBall, st, flagsClear, 1, 1);
                check("...and a CLEAR state extra leaves it there",
                    Near(Radius(pup), smallRadius, 0.001f) && !pup.IsConnected());

                // THE FIX. The host says connected; the puppet must now hit-test at the host's
                // radius, which is 7a's measured 1.25x and not a constant copied from Ball.cs.
                Apply(IdBall, TypeBall, st, flagsConnected, 1, 2);
                check("a CONNECTED state extra grows the puppet to the host's radius ("
                        + F2(Radius(pup)) + "px, " + F2(Radius(pup) / smallRadius) + "x)",
                    Near(Radius(pup), smallRadius * 1.25f, smallRadius * 0.001f));
                // The other three local readers (Bullet's combo sustain, PlayerShip's IsAiShootable)
                // go through IsConnected, so it takes the host's answer too.
                check("...and IsConnected() reports the host's answer to the joiner's own bullets",
                    pup.IsConnected());

                // THE CONTRACT LEG: a puppet must never run gameplay. The host's answer reaches the
                // RADIUS and nothing else -- CollidesWith still dispatches on the puppet's own
                // `state`, which CheckOwner parks at `freed`, so the connected arm (hp, the 35 ms
                // blink, owner.RemoveChild() on a null owner) is not entered. This is what fails if
                // anyone ever "simplifies" the fix by replicating into `state` itself.
                Bullet slug = Bullet.NewBullet(bin, game);
                slug.Setup(st.Pos, 0f, 500f, 0);
                bool threw = false;
                try { pup.CollidesWith(slug); }
                catch (System.Exception) { threw = true; }
                check("a hit on a CONNECTED puppet runs no gameplay arm (no throw, no blink)",
                    !threw && !pup.NetHitBlinking);
                check("...and the puppet still reads the host's radius afterwards",
                    Near(Radius(pup), smallRadius * 1.25f, smallRadius * 0.001f));
                bin.Remove(slug);

                // ...and it comes back DOWN. A fix that latched "connected" for the rest of the
                // ball's life would pass every leg above.
                Apply(IdBall, TypeBall, st, flagsClear, 1, 3);
                check("a ball that breaks away shrinks back on the next turn",
                    Near(Radius(pup), smallRadius, 0.001f) && !pup.IsConnected());

                // CONTROL a second puppet that is never told anything keeps its radius for its
                // whole life -- so the legs above are reading the WIRE, not a global.
                // Against its OWN baseline, deliberately: Ball.Initialize rolls
                // `scale = 0.45 * rand(0.42, 0.85)` per instance and `r` follows it, so two puppets
                // never share a radius and comparing one against the other's is meaningless.
                if (NetPuppets.OnSpawn(IdControl, TypeBall, st, variant, 0, 1)
                        != SpawnRejectKind.None
                    || !(NetPuppets.FindPuppet(IdControl) is Ball ctrl))
                {
                    check("a control Ball puppet was built", false);
                    return;
                }
                float ctrlSmall = Radius(ctrl);
                Apply(IdBall, TypeBall, st, flagsConnected, 1, 4);
                check("CONTROL an unaddressed Ball puppet is unaffected by another's state extra",
                    Near(Radius(ctrl), ctrlSmall, 0.001f) && !ctrl.IsConnected()
                        && Near(Radius(pup), smallRadius * 1.25f, smallRadius * 0.001f));
            }
            finally
            {
                // BY HAND, 6b's shape: Disable() clears the id maps but leaves the components it
                // built in Game.Components. Collected BEFORE Disable, since FindPuppet reads the
                // maps it clears.
                foreach (ushort id in new ushort[] { IdBall, IdControl })
                {
                    INetEntity p = NetPuppets.FindPuppet(id);
                    if (p != null)
                    {
                        bin.Remove((GameComponent)(object)p);
                    }
                }
                NetPuppets.Disable();
            }
        }

        private static float Radius(Ball b)
        {
            return ((CollisionSimpleCircle)b.CollisionType).Radius;
        }

        private static string F2(float v)
        {
            return v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Apply(ushort id, byte typeIdx, in NetBaseState st, byte[] extra,
            int extraLen, ushort seq)
        {
            NetPuppets.OnSnapshotEntry(id, typeIdx, NetProtocol.NetSnapshotFlags.None, st,
                extra, 0, extraLen, seq, out _, out _, out _);
        }

        private static float Wrapped(float radians)
        {
            float d = System.Math.Abs(radians) % 6.2831855f;
            return System.Math.Min(d, 6.2831855f - d);
        }

        private static GameTime Tick(float dtMs)
        {
            return new GameTime(System.TimeSpan.Zero, System.TimeSpan.FromMilliseconds(dtMs));
        }

        // ---- helpers -----------------------------------------------------------------------

        // One tick of the per-puppet drive hook, exactly as NetPuppets.Drive calls it.
        private static void DriveOnce(AlienDrawableGameComponent c, float dtMs)
        {
            GameTime t = new GameTime(System.TimeSpan.Zero,
                System.TimeSpan.FromMilliseconds(dtMs));
            ((INetEntity)c).NetTickTimers(t);
            ((INetEntity)c).NetDriveExtras(t);
        }

        private static bool Near(float a, float b, float tol)
        {
            return System.Math.Abs(a - b) < tol;
        }
    }
}
