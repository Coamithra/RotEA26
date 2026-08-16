using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the INetEntity seam (card 25ad0659 step 2c-ii).
    // Invoke with eaNetEntity() / `eval NetEntityTest` -- best from the main menu.
    //
    // WHAT THE COMPILER ALREADY COVERS, so this does not. Unlike steps 2a/2b/2c-i, a call site
    // left behind by this migration does NOT compile: PuppetInfo.Comp, NetIdRegistry.Entry.Comp
    // and the kill-note table all changed TYPE, so nothing can still be reading
    // AlienDrawableGameComponent off them. Exhaustiveness is not the risk here.
    //
    // WHAT IT DOES NOT COVER, which is the whole of this suite:
    //
    //   1. A MIS-WIRED FORWARD. AlienDrawableGameComponent implements the seam explicitly, so
    //      `float INetEntity.NetScale => rotation;` compiles perfectly and silently swaps two
    //      values that are both floats. Same hazard NetHostTest calls out for the impairment
    //      triple, and the answer is the same: drive every member to a DISTINCT value and
    //      compare against the member it claims to front, so a swap cannot pass.
    //   2. A MISSING DISCRIMINANT. The layer's `is KillableAlien` (4 sites) and `is Powerup`
    //      (3 sites) became NetKillable / NetPickup. If Powerup had not overridden its half,
    //      every remote pickup would silently take the generic death-burst branch -- an
    //      explosion where the other player collected something -- and nothing would say so.
    //      So the discriminants are asserted to agree EXACTLY with the type tests they
    //      replaced, on entities of all four shapes, with the type test run as the control.
    //
    // Leave-no-trace: every entity here is CONSTRUCTED and never added to the bin or to
    // Game.Components, so nothing enters the world, the recycle pool or the watcher multiset.
    // (Two of the four do decode their sprite in their constructor -- LoadAnimation calls
    // content.Load -- which is a cache hit for anything already warm and otherwise one decode
    // into the shared manager. That is why this is a menu suite, not a probe leg.)
    internal static class NetEntityTest
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

            sb.Append("[netentity] the INetEntity seam (card 25ad0659 step 2c-ii)\n");

            // ---- 1. the field-backed forwards, driven to DISTINCT values -------------------
            //
            // NetRotation / NetScale / NetCurFrame exist ONLY because `rotation`, `scale` and
            // `curframe` are public FIELDS and an interface cannot expose a field. They are
            // therefore the three most likely to be mis-wired, and the three whose mis-wiring
            // a compiler cannot see. Every value below is distinct from every other AND from
            // the field's own starting value, so neither a swapped write nor a swapped read
            // can pass.
            ProbeEntity probe = new ProbeEntity(game);
            INetEntity e = probe;

            probe.rotation = 1.5f;
            probe.scale = 2.25f;
            probe.curframe = 3.75f;
            Check("NetRotation reads `rotation` (1.5)", e.NetRotation == 1.5f);
            Check("NetScale reads `scale` (2.25)", e.NetScale == 2.25f);
            Check("NetCurFrame reads `curframe` (3.75)", e.NetCurFrame == 3.75f);

            e.NetRotation = 4.5f;
            e.NetScale = 5.75f;
            Check("NetRotation writes `rotation` (4.5)", probe.rotation == 4.5f);
            Check("NetScale writes `scale` (5.75)", probe.scale == 5.75f);
            // The two above already catch a write SWAPPED BETWEEN THEM (each would leave the
            // other field at its section-1 value). What they cannot catch is a write that
            // landed in the THIRD field of the trio -- nothing here writes `curframe` through
            // the seam, so it must still hold what section 1 put in it.
            Check("neither write touched `curframe`", probe.curframe == 3.75f);

            probe.Position = new Vector2(11f, 13f);
            Check("Position is the seam's own property", e.Position == new Vector2(11f, 13f));
            e.Position = new Vector2(17f, 19f);
            Check("Position writes through", probe.Position == new Vector2(17f, 19f));

            // Compared against the class's own internal member rather than the protected
            // backing property: that IS the claim ("the forward fronts THIS member"), and
            // SpeedVector's setter derives speed+direction, so a raw round-trip would be
            // asserting float reconstruction rather than the wiring.
            e.NetSpeedVector = new Vector2(0.25f, -0.5f);
            Check("NetSpeedVector fronts the class member",
                e.NetSpeedVector == probe.NetSpeedVector);
            Check("NetSpeedVector really reached the class",
                probe.NetSpeedVector != Vector2.Zero);

            probe.Enabled = false;
            Check("Enabled reads through (false)", !e.Enabled);
            e.Enabled = true;
            Check("Enabled writes through (true)", probe.Enabled);

            probe.NetProbePointValue = 1234f;
            Check("NetPointValue fronts the class member (1234)", e.NetPointValue == 1234f);
            // NO-FORWARD MEMBER, and this leg says so rather than implying coverage it has not:
            // IsDead (like Position and Enabled above) is already public, so it satisfies the
            // interface IMPLICITLY and both sides of this comparison resolve to the same member.
            // There is no wiring here to get wrong, so this cannot fail -- it is a sentinel for
            // someone later adding an explicit forward, not evidence about today. (Position and
            // Enabled at least round-trip a value; this one does not even do that.) Same
            // honesty NetHostTest applies to its equal-to-source boolean legs.
            Check("IsDead needs no forward -- implicit, so this cannot fail", e.IsDead == probe.IsDead);

            // ---- 2. the frame methods route to the real wrapping implementations ----------
            //
            // NetSetFrame and NetAdvanceFrame both wrap into the type's ACTIVE frame range;
            // a forward that quietly assigned curframe instead would pass any "it changed"
            // test. So each is compared against the class's own method from the same start.
            probe.curframe = 0f;
            e.NetSetFrame(2.5f);
            float viaSeam = probe.curframe;
            probe.curframe = 0f;
            probe.NetSetFrame(2.5f);
            Check("NetSetFrame is the class's own (wrapped) implementation",
                viaSeam == probe.curframe);

            probe.curframe = 0f;
            e.NetAdvanceFrame(0.5f);
            viaSeam = probe.curframe;
            probe.curframe = 0f;
            probe.NetAdvanceFrame(0.5f);
            Check("NetAdvanceFrame is the class's own (wrapped) implementation",
                viaSeam == probe.curframe);
            // Positive control: both of the above compare two results, so they would agree
            // vacuously if neither call moved curframe at all.
            Check("NetAdvanceFrame actually advanced the frame", probe.curframe != 0f);

            // ---- 3. the two void forwards that CAN be swapped for each other ---------------
            //
            // NetTickTimers and NetDriveExtras both return void and both take a GameTime, so
            // an explicit forward bound to the wrong one compiles and is invisible in play:
            // puppets would keep animating (the driver calls both) while hit-blink decay or a
            // boss's charge glow silently stopped. Each leg therefore asserts the OTHER did
            // not move, which is what tells the swap from a working pair.
            //
            // They are observed differently because only one of them can be: NetDriveExtras is
            // virtual, so the probe counts it; NetTickTimers is not, so the probe watches a
            // real Timer it owns actually advance -- which is the better observation anyway,
            // since it is the effect the puppet layer is buying.
            GameTime gt = new GameTime(System.TimeSpan.Zero, System.TimeSpan.FromMilliseconds(100f));
            float before = probe.ProbeTimerLeft;
            e.NetTickTimers(gt);
            Check("NetTickTimers ticked the entity's own timers",
                probe.ProbeTimerLeft < before && before > 0f);
            Check("... and did NOT run NetDriveExtras", probe.DriveExtrasCalls == 0);

            float held = probe.ProbeTimerLeft;
            e.NetDriveExtras(gt);
            Check("NetDriveExtras reached the class's override", probe.DriveExtrasCalls == 1);
            Check("... and did NOT tick the timers", probe.ProbeTimerLeft == held);

            // (NetSuppressAward's reachability leg lived here until card af96bcc2 deleted the
            // member outright -- one writer per slot needs no award suppression.)

            // ---- 4. the VIRTUAL forwards dispatch to the override, not to the base --------
            //
            // NetSpinPerMs and NetCosmeticOnly are the two members whose whole point is that a
            // SUBTYPE answers differently -- Asteroid spins its own puppets locally, and a
            // belt-decoration Asteroid opts out of replication entirely (card 9a3175d0). The
            // base answers 0 and false, so an explicit forward accidentally bound to the base
            // would read exactly like the shipped default on every OTHER type: this is the one
            // leg that can tell them apart, and it needs an Asteroid to do it.
            Check("a plain entity's NetSpinPerMs is the base 0", e.NetSpinPerMs == 0f);
            Check("a plain entity's NetCosmeticOnly is the base false", !e.NetCosmeticOnly);

            // A fixed override rather than a real Asteroid for the SPIN half: Asteroid's
            // rotationspeed is rolled inside Setup off the SHARED game RNG, so leaning on it
            // would both consume that RNG from a menu suite and leave the assertion able to
            // roll ~0. What is under test is that the explicit forward dispatches to an
            // override at all, which any override proves.
            INetEntity variant = new ProbeVariant(game);
            Check("a NetSpinPerMs override reaches the seam (0.125)", variant.NetSpinPerMs == 0.125f);
            Check("a NetCosmeticOnly override reaches the seam (true)", variant.NetCosmeticOnly);

            // NetFrameLocal is the third virtual of this shape (the puppet animation opt-out,
            // cards c92f3817 / 0dfc4495 / d3add86f) and it DEFAULTS TRUE, unlike the two above.
            // That inversion is exactly why it needs its own pair of checks: a forward
            // accidentally bound to the base would answer `true` everywhere, which is the
            // shipped answer for almost every type -- so only a type that opts OUT can tell the
            // two apart, and getting it wrong silently reintroduces the animation kick on the
            // handful of types whose frame is host-gated.
            Check("a plain entity's NetFrameLocal is the base true", e.NetFrameLocal);
            Check("a NetFrameLocal override reaches the seam (false)", !variant.NetFrameLocal);

            // ... and the REAL shipped opt-outs, which is what the audit actually rests on.
            // Spider writes curframe from its rear-up/land state machine; MarsBoss re-derives
            // `fps` from HitPointsNormalized every Update, so a puppet would free-run at the
            // wrong RATE. Both must answer false; a UFO -- the plain free-running case the
            // cards are about -- must answer true beside them, or a predicate hard-wired to
            // false would pass this leg.
            Check("Spider opts OUT of local frames (host-gated pose)",
                !((INetEntity)new Spider(game)).NetFrameLocal);
            Check("MarsBoss opts OUT of local frames (Update mutates fps)",
                !((INetEntity)new MarsBoss(game)).NetFrameLocal);
            Check("UFO keeps local frames (a plain free-running loop)",
                ((INetEntity)new UFO(game)).NetFrameLocal);

            // NetScaleLocal (cards 4392bd30 / 80749dc4) is the fourth virtual of this shape, and
            // it needs the pair for the same reason NetFrameLocal does, INVERTED: it defaults
            // FALSE, so a forward accidentally bound to NetFrameLocal -- the member immediately
            // above it in every one of these files -- would answer TRUE for almost every type,
            // and every entity in the game would silently stop taking the replicated scale. The
            // shipped opt-out is Wall (it derives its scale from the replicated grid variation);
            // the UFO beside it is the control, since its scale is caller-chosen per spawn.
            Check("a plain entity's NetScaleLocal is the base false", !e.NetScaleLocal);
            Check("a NetScaleLocal override reaches the seam (true)", variant.NetScaleLocal);
            Check("Wall opts OUT of replicated scale (it derives its own)",
                ((INetEntity)new Wall(game)).NetScaleLocal);
            Check("UFO keeps the replicated scale (caller-chosen per spawn)",
                !((INetEntity)new UFO(game)).NetScaleLocal);

            // ... and then the REAL shipped case for the cosmetic half, which is what the
            // production opt-out actually looks like (card 9a3175d0): a belt-decoration
            // Asteroid, whose answer flips on SetBackground rather than on its type.
            Asteroid rock = new Asteroid(game);
            INetEntity rockSeam = rock;
            Check("a foreground Asteroid is not cosmetic", !rockSeam.NetCosmeticOnly);
            rock.SetBackground();
            Check("SetBackground's NetCosmeticOnly override reaches the seam (true)",
                rockSeam.NetCosmeticOnly && rock.NetCosmeticOnly);

            // ---- 5. the two discriminants, against the type tests they replaced -----------
            //
            // `x is KillableAlien` and `x is Powerup` are what the seven call sites used to
            // ask. The control is the type test itself, run beside the discriminant on the
            // same instance: agreement on all four shapes is the 1:1 mapping, and requiring
            // both answers to occur is what stops a discriminant hard-wired to null (or to
            // `this`) passing everything.
            // ProbeKillable rather than a shipped boss: every KillableAlien sets its hit points
            // through SetHitPoints, which only records `initialhitpoints` -- the live
            // `hitpoints` is not seeded until Initialize(), and Initialize on a real boss means
            // LoadContent. The probe seeds it directly, which is what NetApplyHp reads.
            ProbeKillable killable = new ProbeKillable(game);
            killable.SetProbeHitPoints(200);
            Powerup pickup = new Powerup(game);
            pickup.type = Powerup.PowerupType.OneUp; // NOT the default 0, so a zeroed read fails
            AlienDrawableGameComponent[] shapes = { probe, rock, killable, pickup };
            int killableSeen = 0;
            int pickupSeen = 0;
            bool killableAgrees = true;
            bool pickupAgrees = true;
            bool identityHolds = true;
            foreach (AlienDrawableGameComponent shape in shapes)
            {
                INetEntity seam = shape;
                bool isKillable = shape is KillableAlien;
                bool isPickup = shape is Powerup;
                killableAgrees &= (seam.NetKillable != null) == isKillable;
                pickupAgrees &= (seam.NetPickup != null) == isPickup;
                // The discriminant must be the entity ITSELF, not some other object: a
                // NetKill routed at a different instance would kill the wrong thing.
                identityHolds &= (seam.NetKillable == null || ReferenceEquals(seam.NetKillable, shape))
                    && (seam.NetPickup == null || ReferenceEquals(seam.NetPickup, shape));
                if (isKillable) { killableSeen++; }
                if (isPickup) { pickupSeen++; }
            }
            Check("NetKillable agrees with `is KillableAlien` on all four shapes", killableAgrees);
            Check("NetPickup agrees with `is Powerup` on all four shapes", pickupAgrees);
            Check("each discriminant returns the entity itself", identityHolds);
            Check("the shape set is non-degenerate (one of each, and two of neither)",
                killableSeen == 1 && pickupSeen == 1 && shapes.Length == 4);

            // The pickup surface itself. NetPickupType is what decides the OneUp branch in
            // both settle paths (an extra life is host-authoritative), so reading the wrong
            // field there would cost a player a life with nothing logged.
            //
            // NULL-GUARDED, and that is not defensive padding -- it is what the mutation test
            // required. Dereferencing a missing discriminant throws out of Run(), which prints
            // a stack trace and NO tally line, so the suite reads as "did not run" instead of
            // "failed": exactly the outcome an absence assertion is supposed to prevent.
            INetPickup pick = ((INetEntity)pickup).NetPickup;
            Check("NetPickupType fronts `type` (OneUp)",
                pick != null && pick.NetPickupType == Powerup.PowerupType.OneUp);
            Check("the pickup starts untaken", !pickup.taken);
            pick?.NetMarkTaken();
            Check("NetMarkTaken sets `taken`", pickup.taken);

            // The killable surface. NetApplyHp takes the host's value in BOTH directions since
            // card 87310afa (it used to refuse every raise, which left a client's local
            // over-predictions uncorrectable) and floors at 1 -- deaths still arrive as events,
            // never by snapshot -- so all three legs are worth pinning here rather than trusting
            // the forward alone.
            INetKillable kill = ((INetEntity)killable).NetKillable;
            int hp0 = killable.NetHitPoints;
            Check("NetHitPoints fronts the class member", kill != null && kill.NetHitPoints == hp0 && hp0 > 1);
            kill?.NetApplyHp(hp0 - 1);
            Check("NetApplyHp lowers through the seam", kill != null && kill.NetHitPoints == hp0 - 1);
            kill?.NetApplyHp(hp0 + 100);
            Check("NetApplyHp RAISES through the seam (want " + (hp0 + 100) + ", was "
                + (kill != null ? kill.NetHitPoints : -1) + ")",
                kill != null && kill.NetHitPoints == hp0 + 100);
            // The floor is a guard of its own -- it is what keeps a death off the snapshot path.
            // It had no leg of its own until this card (nothing was shadowing it; it was simply
            // never asserted), and it gets one here so the direction change cannot take it along
            // unnoticed.
            kill?.NetApplyHp(0);
            Check("NetApplyHp still floors at 1 (was "
                + (kill != null ? kill.NetHitPoints : -1) + ")", kill != null && kill.NetHitPoints == 1);

            sb.Append("[netentity] ").Append(fail == 0 ? "PASS" : "FAIL")
                .Append(" (").Append(pass).Append(" passed, ").Append(fail).Append(" failed)\n");
            return sb.ToString();
        }

        // A minimal replicable entity that exists only for this suite. It loads no sprite (so
        // it costs no decode and needs no warm content), owns one real Timer so NetTickTimers
        // has an observable effect, and counts NetDriveExtras -- the one of the pair that is
        // virtual and therefore countable at all. Never added to the bin or to Game.Components.
        private sealed class ProbeEntity : AlienDrawableGameComponent
        {
            public int DriveExtrasCalls;

            private readonly Timer probeTimer = new Timer(5000f, repeating: false);

            public ProbeEntity(Game game)
                : base(game)
            {
                // No LoadAnimation: nothing here is ever drawn, which is also why it needs no
                // warm content. FirstFrame stays 0 and LastFrame is set so the frame methods
                // have a real (non-degenerate) active range to wrap into.
                LastFrame = 8;
                fps = 10f;
                AddTimer(probeTimer);
            }

            public override ICollisionType CollisionType => null;

            public float ProbeTimerLeft => probeTimer.TimeLeft;

            // PointValue is protected on the base, so the suite sets it from in here.
            public float NetProbePointValue
            {
                set
                {
                    PointValue = value;
                }
            }

            internal override void NetDriveExtras(GameTime gameTime)
            {
                DriveExtrasCalls++;
                base.NetDriveExtras(gameTime);
            }
        }

        // Overrides the two VIRTUAL seam members to fixed, non-default answers, so the leg
        // that checks the explicit forwards dispatch to an override does not depend on a real
        // type's runtime state (Asteroid's spin comes off the shared RNG inside Setup).
        private sealed class ProbeVariant : AlienDrawableGameComponent
        {
            public ProbeVariant(Game game)
                : base(game)
            {
            }

            public override ICollisionType CollisionType => null;

            internal override float NetSpinPerMs => 0.125f;

            internal override bool NetCosmeticOnly => true;

            // Opposite polarity to the two above, deliberately: NetFrameLocal DEFAULTS to true,
            // so an override has to answer false to be distinguishable from the base at all.
            internal override bool NetFrameLocal => false;

            // Same polarity as NetSpinPerMs/NetCosmeticOnly: NetScaleLocal DEFAULTS to false, so
            // an override answers true.
            internal override bool NetScaleLocal => true;
        }

        // A content-free KillableAlien, for the same reason ProbeEntity is a content-free
        // entity. `hitpoints` is protected, so seeding it is done from in here.
        private sealed class ProbeKillable : KillableAlien
        {
            public ProbeKillable(Game game)
                : base(game)
            {
            }

            public override ICollisionType CollisionType => null;

            // Never reached: this suite only ever reaches hp through NetApplyHp, which FLOORS AT
            // 1, so the death path is unreachable from here by construction. That is the floor
            // doing the work, not the clamp direction -- NetApplyHp raises as well since card
            // 87310afa, and the leg above drives it to 0 to prove the floor still holds.
            protected override void KilledBy(ICollidable other, bool comboGenerator)
            {
            }

            public void SetProbeHitPoints(int hp)
            {
                HitPoints = hp;
            }
        }
    }
}
