using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Death FX on the JOIN peer -- cards 4e406eba (space mines don't explode), 303bfb5b (the twin
    // motherships' final ship crashes silently) and 13aa596c (the congaline of big rulers loses
    // its explosions and death animation). Run `eaNetDeathFx()` from the MAIN MENU, or
    // `eval NetDeathFx` under eahl. Committed as tools/headless/probes/net_death_fx.txt.
    //
    // ---- the two defects, which share a file and nothing else ---------------------------------
    //
    // A. UNATTRIBUTED REAL DEATHS despawned silently. NetPuppets.OnRemoteDeath had a
    //    "live puppet + no killer -> bin.Remove" branch, and a space mine detonating on its own
    //    timer takes it: StarMine.Asplode() never runs KillableAlien.HitBy, so NoteKill never
    //    fires and the host broadcast KillerNone. But KillerNone is ALSO how an off-screen
    //    fly-off and a teardown purge arrive, which is why the fix needs a second value on the
    //    wire (NetProtocol.KillerSelf) and an OPT-IN hook at the death site
    //    (NetSession.NoteSelfDestruct) rather than an IsDead guess at the removal seam.
    //
    // B. DEFERRED DEATHS never played at all. BattleSkull and the surviving MarsBoss put their
    //    whole death in an Update-driven state machine (2.5s of shrink-and-flicker; a 5s crash to
    //    the ground) -- and a puppet is Enabled=false for life, so its Update never runs. The
    //    EvDeath does not even arrive until that animation ENDS on the host, so the peer saw an
    //    intact enemy, then one frame of removal, seconds late. The fix reads the hp ALREADY in
    //    every snapshot entry (0 on a killable == the host has killed it) and RELEASES the puppet
    //    from the freeze so it finishes dying locally -- which is what card 13aa596c's own note
    //    asked for ("animation doesn't need to be syncd and can be done locally").
    //
    // ---- why this shape of test -------------------------------------------------------------
    //
    // A screenshot cannot check either half. Defect A's symptom is the ABSENCE of a one-second
    // effect at an unpredictable moment, and defect B's is an animation arriving seconds late --
    // both are "never verify motion with timed live screenshots" in the root CLAUDE.md's sense.
    // Two windows cannot check them either: the deaths happen on the HOST and the FX belong to
    // the joiner, and a backgrounded joiner tab ticks at ~1 Hz.
    //
    // So the observable is the WORLD: how many Explosions exist, whether the entity is still in
    // Game.Components, whether it is Enabled, and what the score panels read. Every positive is
    // asserted with a negative beside it, because almost every failure mode here is silent:
    //  * "it explodes" is worthless without "and the despawn case still does NOT" -- a fix that
    //    exploded everything would satisfy the positives and put a bang on every fly-off.
    //  * "no score moved" is worthless without a leg where score DOES move -- an award path that
    //    had stopped working entirely would pass the KillerSelf legs perfectly.
    //  * the release legs assert the ENABLED flag, not just survival: a puppet left frozen is
    //    still in the world and still counts as "not removed", and that is precisely the bug.
    //
    // MENU-ONLY AND LEAVE-NO-TRACE, the eaNetSnap / eaNetScenarios shape. Sections 2 runs a real
    // HOST session over an in-process NetWire; sections 3-5 need no session at all, only
    // NetPuppets.Enable. Everything it plants is tracked and swept in a finally, the score panels
    // are restored and asserted restored, and it refuses to run with a session, a level or an
    // attract demo up -- it puts real entities into the live Game.Components and really kills
    // them. Every entity it builds sits at Nowhere (far off-screen), so nothing it does is ever
    // drawn, INCLUDING its explosions -- but it is NOT silent: the real death paths play their
    // real cues, so running it from the menu fires a handful of explosion SFX.
    //
    // GOTCHA IT WORKS AROUND: the off-screen position that keeps this invisible is exactly what
    // NetSession's own on-screen gate refuses, so section 2's POSITIVE leg has to put its mine
    // on-screen for one flush. It is removed in the same call, so it is never drawn.
    internal static class NetDeathFxTest
    {
        private const string Room = "deathfx";

        // Far outside the 800x600 design screen: never drawn, never collides, and outside
        // NetSession's DeathFxMarginPx so a death here is the "do not bother" case.
        private static readonly Vector2 Nowhere = new Vector2(-4000f, -4000f);

        // On-screen, for the one leg that has to be (see the header's gotcha).
        private static readonly Vector2 OnScreen = new Vector2(400f, 300f);

        private const byte PeerSlot = 1;
        private const ulong PeerToken = 0x0DEADFA11UL;

        // Ids far above anything a real session reaches (AllocId counts from 1 and wraps at
        // 65535), so nothing here can collide with a live entry.
        private const ushort IdMine = 61001;
        // A SECOND id for the KillerNone control, not a reuse of IdMine: ReleaseDyingPuppet /
        // the removal seam MarkRemoved it, so the self-heal correctly refuses to rebuild that id
        // for RecentRemovalWindowMs and the leg's own precondition would fail.
        private const ushort IdMine2 = 61006;
        private const ushort IdSkull = 61002;
        private const ushort IdSkull2 = 61003;
        private const ushort IdBullet = 61004;
        private const ushort IdSkull3 = 61005;

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

            sb.Append("[netdeathfx] unattributed + deferred death FX on the join peer"
                + " (cards 4e406eba / 303bfb5b / 13aa596c)\n");

            if (NetSession.Active || NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(0, 0));
                return sb.ToString();
            }

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            ScoreVisualiser score = ServiceHelper.Get<IScoreService>().Score;
            Game game = bin.Game;

            // Everything this suite puts in the world, so the finally can sweep it even on a path
            // that throws halfway through a section.
            List<GameComponent> planted = new List<GameComponent>();
            float[] scoreBefore = new float[NetProtocol.MaxSlots];
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                scoreBefore[i] = score.PointScore(i);
            }

            INetHost hostBefore = NetHost.Current;
            NetHost.Current = new PinnedNetHost();
            try
            {
                Section1Codec(sb, Check);
                Section2HostEmission(sb, Check, bin, game, planted);
                NetPuppets.Enable(game);
                Section3ClientUnattributed(sb, Check, bin, game, score, planted);
                Section4DeferredFromSnapshot(sb, Check, bin, game, score, planted);
                Section5DeferredFromEvDeath(sb, Check, bin, game, score, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 6. teardown\n");
                Teardown(sb, Check, bin, game, score, scoreBefore, planted);
                NetHost.Current = hostBefore;
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the wire byte ----------------------------------------------------------------
        //
        // KillerSelf rides EvDeath's EXISTING killerSlot byte, so the only thing that can go
        // wrong at the wire is the classification: a value that should mean "explode" read as
        // "despawn" (the bug, back) or vice versa (a bang on every fly-off). ClampKillerSlot is
        // the single decode-boundary reader, per NetProtocol's validation contract.
        //
        // The payable rows are the NEGATIVE CONTROL: a clamp hard-wired to KillerSelf, or one
        // that swallowed everything into KillerNone, would satisfy half of this section and fail
        // the other half.
        private static void Section1Codec(StringBuilder sb, Action<string, bool> Check)
        {
            sb.Append(" 1. the killerSlot byte's three-way meaning at the decode boundary\n");
            Check("KillerSelf survives the clamp",
                NetProtocol.ClampKillerSlot(NetProtocol.KillerSelf) == NetProtocol.KillerSelf);
            Check("KillerNone survives the clamp",
                NetProtocol.ClampKillerSlot(NetProtocol.KillerNone) == NetProtocol.KillerNone);
            bool slotsOk = true;
            for (int s = 0; s < 8; s++)
            {
                slotsOk &= NetProtocol.ClampKillerSlot(s) == s;
            }
            Check("every payable slot 0..7 survives the clamp unchanged", slotsOk);
            Check("KillerSelf and KillerNone are DIFFERENT values (the whole point)",
                NetProtocol.KillerSelf != NetProtocol.KillerNone);
            // Garbage degrades to the silent despawn rather than being credited or exploded.
            Check("an out-of-range slot (8) degrades to KillerNone",
                NetProtocol.ClampKillerSlot(8) == NetProtocol.KillerNone);
            Check("an arbitrary junk byte (0x42) degrades to KillerNone",
                NetProtocol.ClampKillerSlot(0x42) == NetProtocol.KillerNone);

            // ...and through a REAL frame, which an encode/decode pair alone cannot prove: a
            // matching pair of wrong offsets passes one (the NetWireTest rule).
            byte[] frame = NetProtocol.EncodeDeathEvent(1, IdMine, NetProtocol.KillerSelf,
                OnScreen, new float[NetProtocol.MaxSlots]);
            Check("a real EvDeath frame carries KillerSelf at the killer offset",
                frame.Length == NetProtocol.DeathEventBytes
                && NetProtocol.ClampKillerSlot(frame[6]) == NetProtocol.KillerSelf);
            byte[] attributed = NetProtocol.EncodeDeathEvent(1, IdMine, PeerSlot,
                OnScreen, new float[NetProtocol.MaxSlots]);
            Check("...and an ATTRIBUTED one still carries its slot (control)",
                NetProtocol.ClampKillerSlot(attributed[6]) == PeerSlot);
        }

        // ---- 2. the host puts KillerSelf on the wire, and only when it should ----------------
        //
        // A real HOST session with a scripted client on the other end of an in-process wire, and
        // a real StarMine planted into the live bin so NetIdRegistry allocates a real id through
        // the real ComponentAdded seam. The mine is then killed through its OWN self-destruct
        // (Asplode, reached via NetReplayUnattributedDeath -- the same entry point the client
        // uses, which is why one call covers both ends), and the frame the peer actually
        // RECEIVED is read off the wire.
        private static void Section2HostEmission(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 2. HOST -- a self-destruct goes out as KillerSelf, a fly-off does not\n");
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> deaths = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= NetProtocol.DeathEventBytes
                    && payload[0] == NetProtocol.MsgEvent && payload[1] == NetProtocol.EvDeath)
                {
                    deaths.Add(payload);
                }
            }

            try
            {
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
                    return; // OnHostDeath early-returns with no peer; every leg below would be vacuous
                }

                // 2a. POSITIVE -- an on-screen self-destruct.
                deaths.Clear();
                StarMine mine = PlantMine(bin, game, planted, OnScreen);
                Check("PRECONDITION the planted mine got a netId",
                    NetIdRegistry.TryGetByComp((GameComponent)(object)mine, out _));
                ((INetKillable)mine).NetReplayUnattributedDeath(null);
                bin.Update(); // the ComponentRemoved seam -> OnHostDeath -> the wire
                wire.Pump();
                Check("an on-screen self-destruct broadcast exactly one EvDeath (" + deaths.Count + ")",
                    deaths.Count == 1);
                // The RAW byte, not ClampKillerSlot(...): section 1 is what proves the clamp, so reading
                // the host through it would let a collapsed clamp mask a real wire regression.
                Check("...carrying KillerSelf, not KillerNone",
                    deaths.Count == 1 && deaths[0][6] == NetProtocol.KillerSelf);
                // Nobody earned it. The award array is what the client pays from, so a
                // KillerSelf death that carried a figure would credit a slot for a suicide.
                float[] awards = new float[NetProtocol.MaxSlots];
                if (deaths.Count == 1)
                {
                    NetProtocol.ReadDeathAwards(deaths[0], awards);
                }
                Check("...and an ALL-ZERO award array -- nobody is credited for a self-destruct",
                    AllZero(awards));

                // 2b. NEGATIVE -- the same self-destruct, off screen. This is the ruling's gate:
                // the host itself showed nothing there, so the peer must not hear a bang at the
                // edge of its screen. Identical call, only the position differs.
                deaths.Clear();
                StarMine offMine = PlantMine(bin, game, planted, Nowhere);
                ((INetKillable)offMine).NetReplayUnattributedDeath(null);
                bin.Update();
                wire.Pump();
                Check("an OFF-SCREEN self-destruct still broadcasts its EvDeath (" + deaths.Count + ")",
                    deaths.Count == 1);
                Check("...but downgraded to KillerNone -- no bang off the edge of the screen",
                    deaths.Count == 1 && deaths[0][6] == NetProtocol.KillerNone);

                // 2c. NEGATIVE -- an ordinary despawn ON SCREEN. Nothing noted the death, so it
                // is KillerNone whatever its position: this is what tells the gate in 2b from
                // "the hook is what does the work", and it is the shape every fly-off, purge and
                // FX-free Die() in the game takes.
                deaths.Clear();
                StarMine quiet = PlantMine(bin, game, planted, OnScreen);
                bin.Remove((GameComponent)(object)quiet);
                bin.Update();
                wire.Pump();
                Check("a plain removal with no self-destruct note is KillerNone even on screen ("
                    + deaths.Count + " EvDeath)",
                    deaths.Count == 1 && deaths[0][6] == NetProtocol.KillerNone);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("death-FX harness finished");
                bin.TopOfTickFlush();
                Check("the host session was stopped and left nothing Active", !NetSession.Active);
            }
        }

        // ---- 3. the client replays an unattributed death, and pays nobody --------------------
        private static void Section3ClientUnattributed(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted)
        {
            sb.Append(" 3. CLIENT -- EvDeath(KillerSelf) runs the mine's real self-destruct\n");
            byte mineType = TypeIdxOf(new StarMine(game));

            // POSITIVE: the puppet explodes and leaves.
            int boom = CountType<Explosion>(game);
            float[] before = Scores(score);
            StarMine puppet = (StarMine)BuildPuppet<StarMine>(game, IdMine, mineType, planted);
            Check("PRECONDITION a StarMine puppet was built from the snapshot", puppet != null);
            if (puppet == null)
            {
                return;
            }
            NetPuppets.OnRemoteDeath(IdMine, NetProtocol.KillerSelf, Nowhere,
                new float[NetProtocol.MaxSlots]);
            bin.Update();
            int made = CountType<Explosion>(game) - boom;
            // Asplode spawns TWO blue bursts; KilledBy would spawn one. Asserting ">= 2" is what
            // pins that the override ran rather than the generic path -- and it is the reason the
            // override exists at all (the mine's self-destruct does not look like being shot).
            Check("the mine's OWN self-destruct FX ran (+" + made + " explosions, Asplode makes 2)",
                made >= 2);
            Check("the puppet left the world", !InWorld(game, (GameComponent)(object)puppet));
            Check("no slot was credited for a suicide", SameScores(score, before));

            // NEGATIVE: KillerNone is still the silent despawn. Without this leg, a fix that
            // exploded every unattributed removal would pass everything above.
            boom = CountType<Explosion>(game);
            StarMine quiet = (StarMine)BuildPuppet<StarMine>(game, IdMine2, mineType, planted);
            Check("PRECONDITION a second StarMine puppet was built", quiet != null);
            if (quiet == null)
            {
                return;
            }
            NetPuppets.OnRemoteDeath(IdMine2, NetProtocol.KillerNone, Nowhere,
                new float[NetProtocol.MaxSlots]);
            bin.Update();
            Check("NEGATIVE KillerNone still despawns SILENTLY (+"
                + (CountType<Explosion>(game) - boom) + " explosions)",
                CountType<Explosion>(game) == boom);
            Check("...and still removes the puppet", !InWorld(game, (GameComponent)(object)quiet));
        }

        // ---- 4. a deferred death, detected from the snapshot's hp --------------------------
        private static void Section4DeferredFromSnapshot(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted)
        {
            sb.Append(" 4. CLIENT -- hp==0 in a snapshot releases the puppet to finish dying\n");
            byte skullType = TypeIdxOf(new BattleSkull(game));

            int boom = CountType<Explosion>(game);
            float[] before = Scores(score);
            BattleSkull skull = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkull, skullType, planted);
            Check("PRECONDITION a BattleSkull puppet was built", skull != null);
            if (skull == null)
            {
                return;
            }
            Check("PRECONDITION the puppet starts FROZEN, as every puppet does", !skull.Enabled);
            int liveBefore = NetPuppets.LiveCount;

            // A snapshot whose hp reads 0: the host has landed the killing blow and its copy is
            // in its 2.5s dying state.
            Snapshot(IdSkull, skullType, hp: 0);

            Check("the death opened with its own FX (+" + (CountType<Explosion>(game) - boom)
                + " explosions)", CountType<Explosion>(game) > boom);
            // The RELEASE. Each of these three is a separate way the fix could half-work, and
            // "still in the world" alone is exactly what the BUG looked like.
            Check("the puppet is STILL IN THE WORLD -- its animation has 2.5s to run",
                InWorld(game, (GameComponent)(object)skull));
            Check("...and is UN-FROZEN, which is what lets that animation run at all",
                skull.Enabled);
            Check("...and can no longer collide with the local player",
                !((AlienDrawableGameComponent)(object)skull).Collides);
            Check("...and left the puppet registry (live " + liveBefore + " -> "
                + NetPuppets.LiveCount + ")", NetPuppets.LiveCount == liveBefore - 1);
            Check("nothing was credited yet -- the host's EvDeath is the authority",
                SameScores(score, before));

            // THE MarkRemoved LEG. Without the by-hand MarkRemoved in ReleaseDyingPuppet the
            // next snapshot entry for this id is an unknown id, and the self-heal rebuilds a
            // fresh intact collidable enemy standing on top of the one that is visibly dying.
            int worldBefore = CountType<BattleSkull>(game);
            SnapshotKind(IdSkull, skullType, hp: 32, out SnapUnknownKind kind);
            Check("a later snapshot for the released id reports LeftDead, not Rebuilt (was "
                + kind + ")", kind == SnapUnknownKind.LeftDead);
            Check("...and builds no replacement", CountType<BattleSkull>(game) == worldBefore);

            // The host's EvDeath arrives seconds later, when ITS animation ends. The puppet is
            // gone from the registry by then, so it settles as an ordinary award-only
            // reconciliation -- and must not fire a second burst.
            boom = CountType<Explosion>(game);
            before = Scores(score);
            float[] awards = new float[NetProtocol.MaxSlots];
            awards[PeerSlot] = 250f;
            NetPuppets.OnRemoteDeath(IdSkull, PeerSlot, Nowhere, awards);
            Check("the host's late EvDeath pays its award (+"
                + Round(score.PointScore(PeerSlot) - before[PeerSlot]) + ")",
                Math.Abs(score.PointScore(PeerSlot) - before[PeerSlot] - 250f) < 0.01f);
            Check("...and fires NO second burst on a puppet already dying",
                CountType<Explosion>(game) == boom);

            // NEGATIVE 1: a healthy snapshot changes nothing. Without it, a trigger that fired on
            // EVERY snapshot entry would pass every positive above.
            boom = CountType<Explosion>(game);
            BattleSkull healthy = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkull2, skullType, planted);
            Check("PRECONDITION a second BattleSkull puppet was built", healthy != null);
            if (healthy != null)
            {
                liveBefore = NetPuppets.LiveCount;
                Snapshot(IdSkull2, skullType, hp: 25);
                Check("NEGATIVE a snapshot with hp>0 kills nothing (+"
                    + (CountType<Explosion>(game) - boom) + " explosions)",
                    CountType<Explosion>(game) == boom);
                Check("...and leaves the puppet frozen and registered",
                    !healthy.Enabled && NetPuppets.LiveCount == liveBefore);
            }

            // NEGATIVE 2: hp is 0 on the wire for every NON-killable too (NetBaseState.Hp's own
            // "0 = not killable / unknown"), so the NetKillable discriminant -- not the value --
            // is what makes zero readable. An EvilBullet must be untouched by all of this.
            boom = CountType<Explosion>(game);
            byte bulletType = TypeIdxOf(new EvilBullet(game));
            EvilBullet bullet = (EvilBullet)BuildPuppet<EvilBullet>(game, IdBullet, bulletType, planted);
            Check("PRECONDITION an EvilBullet puppet was built", bullet != null);
            if (bullet != null)
            {
                liveBefore = NetPuppets.LiveCount;
                Snapshot(IdBullet, bulletType, hp: 0);
                Check("NEGATIVE hp==0 on a NON-killable is 'unknown', not a death (+"
                    + (CountType<Explosion>(game) - boom) + " explosions)",
                    CountType<Explosion>(game) == boom);
                Check("...and leaves it frozen and registered",
                    !bullet.Enabled && NetPuppets.LiveCount == liveBefore);
            }
        }

        // ---- 5. the EvDeath fallback releases too -------------------------------------------
        //
        // The snapshot in section 4 is the FAST path, but a puppet's round-robin turn may simply
        // never come before the host's EvDeath lands (a big world's snapTurn runs to ~1.2s, and
        // these deaths are 2.5-5s). So the attributed branch of OnRemoteDeath has to make the
        // same decision: a KilledBy that deferred its own removal releases rather than being
        // deleted mid-animation.
        private static void Section5DeferredFromEvDeath(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted)
        {
            sb.Append(" 5. CLIENT -- an ATTRIBUTED EvDeath on a deferred-death type releases too\n");
            byte skullType = TypeIdxOf(new BattleSkull(game));
            int boom = CountType<Explosion>(game);
            float[] before = Scores(score);
            BattleSkull skull = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkull3, skullType, planted);
            Check("PRECONDITION a BattleSkull puppet was built", skull != null);
            if (skull == null)
            {
                return;
            }
            float[] awards = new float[NetProtocol.MaxSlots];
            awards[PeerSlot] = 400f;
            NetPuppets.OnRemoteDeath(IdSkull3, PeerSlot, Nowhere, awards);
            Check("the death FX ran (+" + (CountType<Explosion>(game) - boom) + " explosions)",
                CountType<Explosion>(game) > boom);
            Check("the puppet was RELEASED, not deleted mid-animation",
                InWorld(game, (GameComponent)(object)skull) && skull.Enabled);
            // The POSITIVE CONTROL for every "no slot was credited" assertion above: the award
            // machinery is demonstrably still working, so those zeroes mean "nobody earned it"
            // rather than "payment is broken".
            Check("...and the killer slot WAS paid, verbatim off the wire (+"
                + Round(score.PointScore(PeerSlot) - before[PeerSlot]) + ")",
                Math.Abs(score.PointScore(PeerSlot) - before[PeerSlot] - 400f) < 0.01f);
        }

        // ---- teardown ------------------------------------------------------------------------

        private static void Teardown(StringBuilder sb, Action<string, bool> Check, ComponentBin bin,
            Game game, ScoreVisualiser score, float[] scoreBefore, List<GameComponent> planted)
        {
            // Released puppets are live components with their own Update, so they are swept the
            // same way as everything else; they are all at Nowhere and were never drawn.
            foreach (GameComponent comp in planted)
            {
                if (InWorld(game, comp))
                {
                    bin.Remove(comp);
                }
            }
            NetPuppets.Disable();
            bin.TopOfTickFlush();
            foreach (GameComponent comp in CollectType<Explosion>(game))
            {
                bin.Remove(comp);
            }
            bin.TopOfTickFlush();
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                score.NetSetScore(i, scoreBefore[i], 0f);
            }
            bool anyLeft = false;
            foreach (GameComponent comp in planted)
            {
                anyLeft |= InWorld(game, comp);
            }
            Check("every entity this suite planted left the world", !anyLeft);
            Check("the puppet layer is disabled again", NetPuppets.LiveCount == 0);
            bool scoresBack = true;
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                scoresBack &= Math.Abs(score.PointScore(i) - scoreBefore[i]) < 0.01f;
            }
            Check("the score panels are back where they started", scoresBack);
        }

        // ---- helpers -------------------------------------------------------------------------

        // Build a puppet through the REAL snapshot self-heal, then identify it as "the T that was
        // not there before" -- a bare type scan would latch onto one the world already owns, and
        // the teardown would then evict it (the eaNetSnap rule).
        private static GameComponent BuildPuppet<T>(Game game, ushort netId, byte typeIdx,
            List<GameComponent> planted) where T : GameComponent
        {
            HashSet<GameComponent> before = new HashSet<GameComponent>(CollectType<T>(game));
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = 0; // a spawn carries no hp; the descriptor's own Initialize sets it
            NetPuppets.OnSnapshotEntry(netId, typeIdx, NetProtocol.NetSnapshotFlags.None, state, new byte[1], 0, 0, out _, out _);
            foreach (GameComponent item in CollectType<T>(game))
            {
                if (!before.Contains(item))
                {
                    planted.Add(item);
                    return item;
                }
            }
            return null;
        }

        private static void Snapshot(ushort netId, byte typeIdx, int hp)
        {
            SnapshotKind(netId, typeIdx, hp, out _);
        }

        private static void SnapshotKind(ushort netId, byte typeIdx, int hp, out SnapUnknownKind kind)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = hp;
            NetPuppets.OnSnapshotEntry(netId, typeIdx, NetProtocol.NetSnapshotFlags.None, state, new byte[1], 0, 0, out _, out kind);
        }

        private static StarMine PlantMine(ComponentBin bin, Game game, List<GameComponent> planted,
            Vector2 at)
        {
            StarMine mine = StarMine.NewStarMine(bin, game);
            mine.Setup();
            mine.Position = at; // configure-then-Add (tools/audit_add_order.py)
            bin.Add((GameComponent)(object)mine);
            planted.Add((GameComponent)(object)mine);
            mine.Position = at; // Initialize ran inside Add and may have moved it
            return mine;
        }

        // The registry is an exact-runtime-type map, so a throwaway instance answers for the
        // type. It is never added to the world.
        private static byte TypeIdxOf(GameComponent probe)
        {
            return NetTypeRegistry.TryGet(probe, out byte idx, out _) ? idx : (byte)0;
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

        private static bool InWorld(Game game, GameComponent comp)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (ReferenceEquals(item, comp))
                {
                    return true;
                }
            }
            return false;
        }

        private static float[] Scores(ScoreVisualiser score)
        {
            float[] s = new float[NetProtocol.MaxSlots];
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                s[i] = score.PointScore(i);
            }
            return s;
        }

        private static bool SameScores(ScoreVisualiser score, float[] before)
        {
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                if (Math.Abs(score.PointScore(i) - before[i]) >= 0.01f)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AllZero(float[] values)
        {
            foreach (float v in values)
            {
                if (v != 0f)
                {
                    return false;
                }
            }
            return true;
        }

        private static string Round(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Tally(int pass, int fail)
        {
            return "[netdeathfx] " + pass + " passed, " + fail + " failed\n";
        }
    }
}
