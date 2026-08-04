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
    //    intact enemy, then one frame of removal, seconds late. The fix RELEASES the puppet from
    //    the freeze so it finishes dying locally -- which is what card 13aa596c's own note asked
    //    for ("animation doesn't need to be syncd and can be done locally").
    //
    //    The TRIGGER for that release is card f62116b5's, and it is what sections 2d/2e and 6
    //    are about: the host emits an explicit EvDying the moment its KilledBy returns without
    //    removing the component, so the release happens on that tick at any world size. The two
    //    inferences remain as fallbacks and keep their own sections -- hp==0 across TWO snapshot
    //    turns (4, the loss/join-in-progress path) and the late EvDeath (5).
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
        private const ushort IdSkull4 = 61007;
        private const ushort IdSkull5 = 61008;
        // Never built, never registered: the "a beat for an id we do not hold" negative.
        private const ushort IdUnknown = 61009;
        // Section 8/9's multi-phase boss legs (card ad9c8f8b).
        private const ushort IdBrainBoss = 61010;
        private const ushort IdFakeBoss = 61011;
        private const ushort IdJunkBoss = 61012;
        private const ushort IdSpiderBoss = 61013;
        private const ushort IdBullet2 = 61014;
        private const ushort IdSpiderBoss2 = 61015;

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

            // BrainBoss.KilledBy ends in sound.StopMusic(), so running its real death from the
            // menu stops the menu track. Captured here and restarted in the teardown -- the
            // suite's leave-no-trace contract (it restarts the track from the top, which is the
            // one residue; nothing else in the suite touches music).
            SoundManager sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
            int songBefore = sound.NetCurrentSong;

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
                Section6DeferredFromEvDying(sb, Check, bin, game, score, planted);
                Section8BossChoreography(sb, Check, bin, game, score, planted);
                Section9SpiderBoss(sb, Check, bin, game, score, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 10. teardown\n");
                Teardown(sb, Check, bin, game, score, scoreBefore, planted, sound, songBefore);
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
            sb.Append(" 2. HOST -- a self-destruct goes out as KillerSelf, a fly-off does not,"
                + " a deferred death announces itself\n");
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> deaths = new List<byte[]>();
            List<byte[]> dyings = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length < 2 || payload[0] != NetProtocol.MsgEvent)
                {
                    return;
                }
                if (payload[1] == NetProtocol.EvDeath && payload.Length >= NetProtocol.DeathEventBytes)
                {
                    deaths.Add(payload);
                }
                else if (payload[1] == NetProtocol.EvDying)
                {
                    dyings.Add(payload);
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

                // 2d. THE TRIGGER-LATENCY LEG (card f62116b5). A deferred death must announce
                // itself AT KilledBy TIME. Everything before this card had to wait for either the
                // entity's round-robin snapshot turn (up to ~1.2 s in a big world) or the EvDeath
                // at the END of the 2.5 s animation -- and the assertion that pins the difference
                // is the SECOND one: the beat is on the wire while NO EvDeath is, because on the
                // host the skull has not been removed and will not be for 2.5 s.
                deaths.Clear();
                dyings.Clear();
                BattleSkull skull = BattleSkull.NewBattleSkull(bin, game);
                skull.Setup(Nowhere); // configure-then-Add
                bin.Add((GameComponent)(object)skull);
                planted.Add((GameComponent)(object)skull);
                skull.Position = Nowhere; // Initialize ran inside Add and may have moved it
                bool gotId = NetIdRegistry.TryGetByComp((GameComponent)(object)skull,
                    out NetIdRegistry.Entry skullEntry);
                ushort skullId = gotId ? skullEntry.Id : (ushort)0;
                Check("PRECONDITION the planted BattleSkull got a netId", gotId);
                ((INetKillable)skull).NetKill(null, isComboGenerator: false);
                wire.Pump();
                Check("a DEFERRED death broadcast exactly one EvDying (" + dyings.Count + ")",
                    dyings.Count == 1);
                Check("...addressed to that entity's netId",
                    dyings.Count == 1
                    && NetProtocol.TryDecodeDyingEvent(dyings[0], out ushort dyingId)
                    && dyingId == skullId && skullId != 0);
                // THE LATENCY CLAIM ITSELF: the old trigger had nothing to work with at this
                // moment -- the entity is still alive on the host and its EvDeath is 2.5 s away.
                Check("...and NO EvDeath yet -- the animation has 2.5s to run (" + deaths.Count + ")",
                    deaths.Count == 0);
                Check("...and the host's own copy is still in the world, dying",
                    InWorld(game, (GameComponent)(object)skull) && !skull.IsDead);

                // 2e. NEGATIVE -- an ORDINARY kill announces nothing. Its KilledBy ends in Die(),
                // so there is no frozen puppet to release and the EvDeath a flush later says
                // everything. Without this leg a hook that fired on every kill would pass 2d.
                deaths.Clear();
                dyings.Clear();
                StarMine shot = PlantMine(bin, game, planted, Nowhere);
                ((INetKillable)shot).NetKill(null, isComboGenerator: false);
                wire.Pump();
                Check("NEGATIVE an ordinary (instant) kill broadcasts NO EvDying ("
                    + dyings.Count + ")", dyings.Count == 0);
                bin.Update();
                wire.Pump();
                Check("...and still settles as an ordinary EvDeath at the removal seam ("
                    + deaths.Count + ")", deaths.Count == 1);

                // 2f. THE JOIN-IN-PROGRESS CATCH-UP. A deferred death runs for 2.5-5 s, so a peer
                // arriving mid-animation missed the live beat entirely -- and it is the one peer
                // for which the snapshot fallback's two-turn rule is expensive. ReplayLive sends
                // the beat again beside the catch-up spawn. The skull from 2d is still dying.
                dyings.Clear();
                NetIdRegistry.ReplayLive();
                wire.Pump();
                Check("a catch-up replay re-announces the STILL-DYING entity (" + dyings.Count + ")",
                    dyings.Count == 1
                    && NetProtocol.TryDecodeDyingEvent(dyings[0], out ushort replayId)
                    && replayId == skullId);
                // NEGATIVE: and nothing else in the live set. Without this, a replay that
                // announced every entity would satisfy the positive and make every joiner run
                // the award-free death path on a world full of healthy enemies.
                StarMine healthyMine = PlantMine(bin, game, planted, Nowhere);
                dyings.Clear();
                NetIdRegistry.ReplayLive();
                wire.Pump();
                Check("NEGATIVE ...and only that one -- a healthy entity is not announced ("
                    + dyings.Count + ")",
                    dyings.Count == 1 && healthyMine != null);

                // 2g. THE SPIDERBOSS, which announces nothing through KillableAlien (card
                // ad9c8f8b). It is not one -- its death lives in CollidesWith, reachable only by
                // a Lazer -- so it is killed here through that REAL path rather than NetKill.
                // Pre-card this leg read zero EvDying and zero EvDeath: the host said nothing at
                // all for the whole 5 s fall, and then sent a KillerNone EvDeath.
                //
                // FIRST, because ReplayLive below re-announces EVERY dying entity and the three
                // killable bosses after it would make that count unreadable.
                deaths.Clear();
                dyings.Clear();
                SpiderBoss spider = SpiderBoss.NewSpiderBoss(bin, game);
                spider.Setup(false);
                spider.Position = Nowhere;
                bin.Add((GameComponent)(object)spider);
                planted.Add((GameComponent)(object)spider);
                spider.Position = Nowhere;
                bool gotSpiderId = NetIdRegistry.TryGetByComp((GameComponent)(object)spider,
                    out NetIdRegistry.Entry spiderEntry);
                ushort spiderId = gotSpiderId ? spiderEntry.Id : (ushort)0;
                Check("PRECONDITION the planted SpiderBoss got a netId", gotSpiderId);
                // Its hit points are difficulty-scaled, so feed beams until it turns rather than
                // assuming a count. Each Lazer must be a DISTINCT instance (it dedupes on
                // identity) and none is ever added to the world.
                int beams = 0;
                while (beams < 40 && !((INetEntity)spider).NetIsDying)
                {
                    spider.CollidesWith(new Lazer(game));
                    beams++;
                }
                Check("PRECONDITION the SpiderBoss died to its real Lazer path (" + beams
                    + " beams)", ((INetEntity)spider).NetIsDying);
                wire.Pump();
                Check("a SpiderBoss death broadcast exactly one EvDying (" + dyings.Count + ")",
                    dyings.Count == 1);
                Check("...addressed to that entity's netId",
                    dyings.Count == 1
                    && NetProtocol.TryDecodeDyingEvent(dyings[0], out ushort spiderDyingId)
                    && spiderDyingId == spiderId && spiderId != 0);
                Check("...and NO EvDeath yet -- the debris have 5s to fall (" + deaths.Count + ")",
                    deaths.Count == 0);
                Check("...and the host's own copy is still in the world, dying",
                    InWorld(game, (GameComponent)(object)spider) && !spider.IsDead);

                // 2h. The join-in-progress catch-up reads the same NetIsDying seam, so a peer
                // arriving mid-fall is re-told about a boss no hp field could describe.
                dyings.Clear();
                NetIdRegistry.ReplayLive();
                wire.Pump();
                // TWO, not one: 2d's BattleSkull is still dying as well, which 2f already
                // established. What is asserted is that the SpiderBoss is now AMONG them -- the
                // pre-card discriminant is a killable at zero hit points, so this boss was the
                // one live dying entity a catch-up could never mention.
                bool replayHasSpider = false;
                foreach (byte[] frame in dyings)
                {
                    replayHasSpider |= NetProtocol.TryDecodeDyingEvent(frame, out ushort rid)
                        && rid == spiderId;
                }
                Check("a catch-up replay re-announces the dying SpiderBoss (" + dyings.Count
                    + " dying entities, skull + spider)",
                    replayHasSpider && dyings.Count == 2);

                // 2i. The three KillableAlien bosses. They reach NoteDeathBegan like the
                // BattleSkull in 2d, so this is coverage rather than a new mechanism -- but the
                // net CLAUDE.md said in as many words that nobody had watched them, and a
                // KilledBy that stopped deferring would be silent everywhere else.
                // BrainBoss LAST: its KilledBy purges seven types out of the live bin and stops
                // the music, so anything this section still needs must already be done with.
                HostDeferredLeg(Check, bin, game, planted, deaths, dyings, wire, "FakeBoss",
                    () =>
                    {
                        FakeBoss b = FakeBoss.NewFakeBoss(bin, game);
                        b.Setup();
                        return (AlienDrawableGameComponent)(object)b;
                    });
                HostDeferredLeg(Check, bin, game, planted, deaths, dyings, wire, "JunkBoss",
                    () =>
                    {
                        JunkBoss b = JunkBoss.NewJunkBoss(bin, game);
                        b.Setup(isbase: false);
                        return (AlienDrawableGameComponent)(object)b;
                    });
                HostDeferredLeg(Check, bin, game, planted, deaths, dyings, wire, "BrainBoss",
                    () =>
                    {
                        BrainBoss b = BrainBoss.NewBrainBoss(bin, game);
                        b.Setup(challenge: false);
                        return (AlienDrawableGameComponent)(object)b;
                    });
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("death-FX harness finished");
                bin.TopOfTickFlush();
                Check("the host session was stopped and left nothing Active", !NetSession.Active);
            }
        }

        // One host leg for a KillableAlien boss whose KilledBy defers: plant it, kill it through
        // the real NetKill, and read the frames the peer actually received. Same shape as 2d's
        // BattleSkull, and the second assertion is again the one that carries it -- the beat is
        // on the wire while NO EvDeath is, because the host will not remove the component until
        // its own animation finishes seconds later.
        private static void HostDeferredLeg(Action<string, bool> Check, ComponentBin bin,
            Game game, List<GameComponent> planted, List<byte[]> deaths, List<byte[]> dyings,
            NetWire wire, string name, Func<AlienDrawableGameComponent> make)
        {
            deaths.Clear();
            dyings.Clear();
            AlienDrawableGameComponent boss = make(); // configure-then-Add
            boss.Position = Nowhere;
            bin.Add((GameComponent)(object)boss);
            planted.Add((GameComponent)(object)boss);
            boss.Position = Nowhere; // Initialize ran inside Add and moves every one of these
            bool gotId = NetIdRegistry.TryGetByComp((GameComponent)(object)boss,
                out NetIdRegistry.Entry entry);
            ushort id = gotId ? entry.Id : (ushort)0;
            Check("PRECONDITION the planted " + name + " got a netId", gotId);
            ((INetKillable)boss).NetKill(null, isComboGenerator: false);
            wire.Pump();
            Check(name + ": a deferred death broadcast exactly one EvDying (" + dyings.Count + ")",
                dyings.Count == 1);
            Check(name + ": ...addressed to that entity's netId",
                dyings.Count == 1
                && NetProtocol.TryDecodeDyingEvent(dyings[0], out ushort dyingId)
                && dyingId == id && id != 0);
            Check(name + ": ...and NO EvDeath yet -- its asplode has seconds to run ("
                + deaths.Count + ")", deaths.Count == 0);
            Check(name + ": ...and the host's own copy is still in the world, dying",
                InWorld(game, (GameComponent)(object)boss) && !boss.IsDead
                && ((INetEntity)boss).NetIsDying);
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
            sb.Append(" 4. CLIENT -- hp==0 in TWO snapshot turns releases the puppet (the fallback)\n");
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
            //
            // THE FIRST TURN MUST NOT FIRE (card f62116b5). The host's ComponentBin defers
            // removal, so an ORDINARY kill is still in the registry for the one tick between the
            // killing blow and the flush -- and a snapshot turn landing in that tick reads hp==0
            // for an entity whose attributed EvDeath is already on its way. That one-tick-early
            // residual was accepted while this was the only fast trigger; EvDying owns the live
            // case now, so the fallback can afford to want a second opinion.
            Snapshot(IdSkull, skullType, hp: 0);
            Check("NEGATIVE ONE hp==0 turn changes nothing -- no death, no release (+"
                + (CountType<Explosion>(game) - boom) + " explosions, enabled=" + skull.Enabled
                + ", live " + liveBefore + "->" + NetPuppets.LiveCount + ")",
                CountType<Explosion>(game) == boom && !skull.Enabled
                && NetPuppets.LiveCount == liveBefore);

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
                // The two turns must be CONSECUTIVE, which is the only thing that makes them
                // stronger than one: a stale zero followed by a healthy turn is a puppet that is
                // demonstrably still alive, and must not be able to team up with a later zero.
                Snapshot(IdSkull2, skullType, hp: 0);
                Snapshot(IdSkull2, skullType, hp: 25);
                Snapshot(IdSkull2, skullType, hp: 0);
                Check("NEGATIVE a zero, a healthy turn, then a zero does NOT release"
                    + " (the turns must be consecutive)",
                    CountType<Explosion>(game) == boom && !healthy.Enabled
                    && NetPuppets.LiveCount == liveBefore);
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
                // Twice, so it is the NetKillable discriminant being asserted and not the latch
                // -- a bullet's hp is 0 on EVERY turn it ever gets.
                Snapshot(IdBullet, bulletType, hp: 0);
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

        // ---- 6. the EvDying beat releases on the EVENT tick ----------------------------------
        //
        // The card's subject (f62116b5): sections 4 and 5 are both INFERENCES that cost time --
        // the snapshot needs the entity's round-robin turn (60 ms at best, ~1.2 s in a big world)
        // and the EvDeath does not arrive until the host's whole 2.5-5 s animation has finished.
        // The host now says so outright at KilledBy time, and the release happens on the tick the
        // beat lands.
        //
        // THE LATENCY CLAIM IS THE ABSENCE OF A SNAPSHOT, and that is what this section pins: no
        // snapshot entry is delivered for this puppet at all, ever. Under the pre-card code the
        // puppet would simply still be standing there, frozen and intact.
        private static void Section6DeferredFromEvDying(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted)
        {
            sb.Append(" 6. CLIENT -- an EvDying beat releases the puppet on the EVENT tick\n");
            byte skullType = TypeIdxOf(new BattleSkull(game));
            int boom = CountType<Explosion>(game);
            float[] before = Scores(score);
            BattleSkull skull = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkull4, skullType, planted);
            Check("PRECONDITION a BattleSkull puppet was built", skull != null);
            if (skull == null)
            {
                return;
            }
            Check("PRECONDITION the puppet starts FROZEN, as every puppet does", !skull.Enabled);
            int liveBefore = NetPuppets.LiveCount;

            NetPuppets.OnDeathBegan(IdSkull4);

            Check("the death opened with its own FX (+" + (CountType<Explosion>(game) - boom)
                + " explosions), with NO snapshot delivered", CountType<Explosion>(game) > boom);
            Check("the puppet is STILL IN THE WORLD -- its animation has 2.5s to run",
                InWorld(game, (GameComponent)(object)skull));
            Check("...and is UN-FROZEN, which is what lets that animation run at all",
                skull.Enabled);
            Check("...and can no longer collide with the local player",
                !((AlienDrawableGameComponent)(object)skull).Collides);
            Check("...and left the puppet registry (live " + liveBefore + " -> "
                + NetPuppets.LiveCount + ")", NetPuppets.LiveCount == liveBefore - 1);
            Check("nothing was credited -- the host's EvDeath is still the authority",
                SameScores(score, before));

            // The by-hand MarkRemoved, as in section 4: without it the host's next turn for this
            // id is an unknown id and the self-heal rebuilds a fresh intact collidable enemy on
            // top of the one that is visibly dying.
            int worldBefore = CountType<BattleSkull>(game);
            SnapshotKind(IdSkull4, skullType, hp: 32, out SnapUnknownKind kind);
            Check("a later snapshot for the released id reports LeftDead, not Rebuilt (was "
                + kind + ")", kind == SnapUnknownKind.LeftDead);
            Check("...and builds no replacement", CountType<BattleSkull>(game) == worldBefore);

            // NEGATIVE: a beat for an id we do not hold is a no-op, not a throw and not a stray
            // explosion. A JIP peer, or one that already released this puppet, gets these.
            boom = CountType<Explosion>(game);
            NetPuppets.OnDeathBegan(IdUnknown);
            NetPuppets.OnDeathBegan(IdSkull4); // already released -- the id is gone from byId
            Check("NEGATIVE an EvDying for an unknown or already-released id does nothing (+"
                + (CountType<Explosion>(game) - boom) + " explosions)",
                CountType<Explosion>(game) == boom);

            // NEGATIVE: the beat must not touch a puppet it was not addressed to. Without this a
            // handler that released everything would pass every positive above.
            BattleSkull bystander = (BattleSkull)BuildPuppet<BattleSkull>(game, IdSkull5, skullType, planted);
            Check("PRECONDITION a bystander BattleSkull puppet was built", bystander != null);
            if (bystander != null)
            {
                liveBefore = NetPuppets.LiveCount;
                NetPuppets.OnDeathBegan(IdSkull4);
                Check("NEGATIVE a bystander puppet stays frozen and registered",
                    !bystander.Enabled && NetPuppets.LiveCount == liveBefore);
            }
        }

        // ---- 8. the multi-phase BOSS deaths actually RUN on the released puppet ---------------
        //
        // Card ad9c8f8b. Sections 4-6 prove the release MECHANISM on a BattleSkull, and stop at
        // the opening frame: still in the world, un-frozen, out of the registry. What none of
        // them checks is that the animation the release exists to permit then RUNS -- so this
        // section releases each of the three KillableAlien bosses and TICKS the released
        // component's real Update forward on a fixed 60 Hz dt (the isolation-sim pattern), which
        // is the only way to observe a 3-to-20-second choreography without a frame.
        //
        // The three were "covered by construction" (the mechanism is type-agnostic) and the net
        // CLAUDE.md's known-limits bullet said outright that nobody had watched them. They are:
        //   BrainBoss  -- KilledBy -> BossState.asplode, a 20 s bombardment then a 300 ms fade
        //   FakeBoss   -- KilledBy -> FakeBossState.asplode, 4 s then a 75-burst finale
        //   JunkBoss   -- KilledBy -> JunkBossState.asplode, exactly 25 explosions (~3.1 s)
        //                 -- the "elongated death explosion" raised on card c146422f
        //
        // THREE THINGS ARE ASSERTED PER BOSS AND EACH IS A DIFFERENT WAY THE RELEASE COULD BE
        // USELESS: the tally keeps CLIMBING at an intermediate checkpoint (a released puppet
        // whose Update did nothing would fire the opening burst and then stand there), the boss
        // is STILL IN THE WORLD at that checkpoint (no premature removal), and it eventually
        // Die()s ON ITS OWN (an animation that never terminates leaves a corpse in every
        // client's world for the rest of the level).
        //
        // AWARDS EXACTLY ONCE is the fourth, and the score panels DO discriminate it: each of
        // these bosses ends its asplode in AwardScoreToAll, which a released puppet really does
        // reach, so a lost NetSuppressAward would re-derive the figure from this peer's own
        // combo on top of the host's. The host's late EvDeath paying its array is the positive
        // control beside it -- without a leg where a score DOES move, "nothing moved" would pass
        // just as well on a build where payment had stopped working altogether.
        private static void Section8BossChoreography(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted)
        {
            sb.Append(" 8. CLIENT -- the multi-phase BOSS deaths run to completion on the"
                + " released puppet (card ad9c8f8b)\n");
            // maxTicks is a generous CEILING, not the animation's length -- see TickUntilGone.
            BossLeg<BrainBoss>(sb, Check, bin, game, score, planted, "BrainBoss", IdBrainBoss,
                TypeIdxOf(new BrainBoss(game)), midTicks: 300, maxTicks: 2000, award: 900f,
                opensWithFx: true);
            BossLeg<FakeBoss>(sb, Check, bin, game, score, planted, "FakeBoss", IdFakeBoss,
                TypeIdxOf(new FakeBoss(game)), midTicks: 60, maxTicks: 900, award: 500f,
                opensWithFx: true);
            BossLeg<JunkBoss>(sb, Check, bin, game, score, planted, "JunkBoss", IdJunkBoss,
                TypeIdxOf(new JunkBoss(game)), midTicks: 60, maxTicks: 900, award: 700f,
                opensWithFx: false);
        }

        private static void BossLeg<T>(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted,
            string name, ushort netId, byte typeIdx, int midTicks, int maxTicks, float award,
            bool opensWithFx)
            where T : AlienDrawableGameComponent
        {
            sb.Append("    -- " + name + "\n");
            int boom = DeathFxCount(game);
            float[] before = Scores(score);
            T boss = (T)(object)BuildPuppet<T>(game, netId, typeIdx, planted);
            Check(name + ": PRECONDITION a puppet was built", boss != null);
            if (boss == null)
            {
                return;
            }
            Check(name + ": PRECONDITION the puppet starts FROZEN", !boss.Enabled);
            int liveBefore = NetPuppets.LiveCount;

            NetPuppets.OnDeathBegan(netId);

            int opened = DeathFxCount(game) - boom;
            // JunkBoss's KilledBy spawns NOTHING -- it only sets the state and starts a 125 ms
            // timer, so its entire death is Update-driven. That makes it the type the release
            // matters MOST for (a puppet left frozen would show no death at all, not a truncated
            // one), so the expectation is asserted in both directions rather than relaxed.
            Check(name + (opensWithFx ? ": the death opened with its own FX (+"
                    : ": NEGATIVE its KilledBy spawns NO FX -- the whole death is Update-driven (+")
                + opened + ")", opensWithFx ? opened > 0 : opened == 0);
            Check(name + ": the puppet was RELEASED, not deleted mid-animation",
                InWorld(game, (GameComponent)(object)boss) && boss.Enabled
                && !boss.Collides && NetPuppets.LiveCount == liveBefore - 1);

            // The host's EvDeath arrives while the animation is still running -- it finished on
            // the host seconds before it finishes here. The puppet has left the registry, so this
            // settles as an award-only reconciliation and must fire no second burst.
            int beforeLate = DeathFxCount(game);
            float[] lateBefore = Scores(score);
            float[] awards = new float[NetProtocol.MaxSlots];
            awards[PeerSlot] = award;
            NetPuppets.OnRemoteDeath(netId, PeerSlot, Nowhere, awards);
            Check(name + ": the host's late EvDeath pays its award once (+"
                + Round(score.PointScore(PeerSlot) - lateBefore[PeerSlot]) + ")",
                Math.Abs(score.PointScore(PeerSlot) - lateBefore[PeerSlot] - award) < 0.01f);
            Check(name + ": ...and fires NO second burst on a puppet already dying",
                DeathFxCount(game) == beforeLate);

            // THE MULTI-PHASE PART. Tick the released component's own Update forward; nothing
            // else in the world is ticked, so every effect counted below is this boss's.
            int atRelease = DeathFxCount(game);
            Tick((GameComponent)(object)boss, midTicks);
            int midway = DeathFxCount(game);
            Check(name + ": the choreography KEEPS GOING after the opening burst (+"
                + (midway - atRelease) + " over " + midTicks + " ticks)", midway > atRelease);
            Check(name + ": ...and the boss is still in the world at that point -- no premature"
                + " removal", InWorld(game, (GameComponent)(object)boss));

            int took = TickUntilGone((GameComponent)(object)boss, bin, game, maxTicks);
            Check(name + ": the animation ENDED on its own and the boss left the world (after "
                + took + " of at most " + maxTicks + " ticks)",
                !InWorld(game, (GameComponent)(object)boss));
            Check(name + ": ...having spawned strictly more FX on the way out (+"
                + (DeathFxCount(game) - midway) + ")", DeathFxCount(game) > midway);
            // The finale calls AwardScoreToAll. Suppressed, so the ONLY movement across this
            // whole leg is the host's figure above -- which is what "awards exactly once" means.
            Check(name + ": nothing beyond the host's award ever moved a score panel",
                Math.Abs(score.PointScore(PeerSlot) - before[PeerSlot] - award) < 0.01f
                && SameScoresExcept(score, before, PeerSlot));
        }

        // ---- 9. SpiderBoss -- the one that is NOT a KillableAlien ----------------------------
        //
        // Card ad9c8f8b, and the hole the card's coverage work actually found. This boss derives
        // from AlienDrawableGameComponent, and its death lives in CollidesWith (only a Lazer
        // hurts it), so HitBy / KilledBy / NoteDeathBegan never run: pre-card it announced no
        // EvDying, its NetKillable was null so the hp==0 snapshot fallback was structurally
        // unreachable, and the EvDeath at the far end of its 5 s debris fall carried KillerNone
        // (nothing had called NoteKill) -- i.e. the join peer saw an intact boss stand there for
        // five seconds and then vanish, with no debris, no explosions and no cues.
        //
        // The fix is the INetEntity.NetIsDying / NetBeginDeferredDeath seam: the boss announces
        // at its own death entry and runs its own choreography on the client. No protocol
        // change -- EvDying already existed and carries only a netId.
        //
        // Its FX are BloodExplosion, not Explosion, which is why this suite counts both.
        private static void Section9SpiderBoss(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, ScoreVisualiser score, List<GameComponent> planted)
        {
            sb.Append(" 9. CLIENT -- SpiderBoss, whose death does NOT run through KillableAlien"
                + " (card ad9c8f8b)\n");
            byte spiderType = TypeIdxOf(new SpiderBoss(game));
            int boom = DeathFxCount(game);
            float[] before = Scores(score);
            SpiderBoss boss = (SpiderBoss)BuildPuppet<SpiderBoss>(game, IdSpiderBoss, spiderType, planted);
            Check("SpiderBoss: PRECONDITION a puppet was built", boss != null);
            if (boss == null)
            {
                return;
            }
            Check("SpiderBoss: PRECONDITION the puppet starts FROZEN", !boss.Enabled);
            // The discriminant the pre-card code used, which is exactly what fails here: this is
            // the reason the beat and both fallbacks all skipped it.
            Check("SpiderBoss: PRECONDITION it is NOT a KillableAlien, so hp==0 can never mean"
                + " 'dying' for it", ((INetEntity)boss).NetKillable == null);
            Check("SpiderBoss: PRECONDITION it does not report itself dying while alive",
                !((INetEntity)boss).NetIsDying);
            int liveBefore = NetPuppets.LiveCount;

            NetPuppets.OnDeathBegan(IdSpiderBoss);

            int opened = DeathFxCount(game) - boom;
            Check("SpiderBoss: the debris death opened with its own FX (+" + opened
                + "; BeginDeathThroes bleeds 32 times)", opened >= 32);
            Check("SpiderBoss: the puppet was RELEASED, not silently despawned",
                InWorld(game, (GameComponent)(object)boss) && boss.Enabled
                && !boss.Collides && NetPuppets.LiveCount == liveBefore - 1);
            Check("SpiderBoss: ...and now reports itself DYING, which is what a join-in-progress"
                + " replay reads", ((INetEntity)boss).NetIsDying);

            // IDEMPOTENT, which the seam requires: this peer hit-tests puppets with its own
            // beams, so it may already have run the same death locally. A second beat must not
            // spawn a second debris burst or restart the 5 s fall.
            int afterOpen = DeathFxCount(game);
            ((INetEntity)boss).NetBeginDeferredDeath();
            Check("SpiderBoss: NEGATIVE a repeated death-began is a no-op, not a second burst (+"
                + (DeathFxCount(game) - afterOpen) + ")", DeathFxCount(game) == afterOpen);

            // The 5 s debris fall, then Die(). 340 ticks is 5.67 s at 60 Hz.
            Tick((GameComponent)(object)boss, 180);
            Check("SpiderBoss: still in the world 3s in -- the debris have 5s to fall",
                InWorld(game, (GameComponent)(object)boss));
            int fell = TickUntilGone((GameComponent)(object)boss, bin, game, 1200);
            Check("SpiderBoss: the fall ENDED on its own and the boss left the world (after "
                + (180 + fell) + " ticks)", !InWorld(game, (GameComponent)(object)boss));
            Check("SpiderBoss: nothing was credited -- the host's EvDeath is the authority",
                SameScores(score, before));

            // THE SNAPSHOT-CLOBBER LEG. A client hit-tests puppets with its own beams, so it can
            // run this boss's death entry locally BEFORE the host's beat arrives -- and the host
            // keeps snapshotting the id for the whole 5 s fall. Every one of those turns used to
            // clamp the local `dead` back to `standing`, which un-posed the falling boss AND
            // defeated the idempotence guard, so the beat landing after it restarted the entry:
            // a second debris burst, both cues again, and a fresh 5 s timer.
            SpiderBoss local = (SpiderBoss)BuildPuppet<SpiderBoss>(game, IdSpiderBoss2, spiderType, planted);
            Check("SpiderBoss: PRECONDITION a second puppet was built for the local-kill case",
                local != null);
            if (local != null)
            {
                int beams = 0;
                while (beams < 40 && !((INetEntity)local).NetIsDying)
                {
                    local.CollidesWith(new Lazer(game));
                    beams++;
                }
                Check("SpiderBoss: PRECONDITION this peer killed the puppet itself, off its own"
                    + " beam (" + beams + " beams)", ((INetEntity)local).NetIsDying);
                int afterLocal = DeathFxCount(game);
                // What the host really streams while its own copy is dying. It has to carry the
                // descriptor's REAL 3-byte state extra -- the bare Snapshot() helper sends a
                // zero-length block, which SpiderBossDescriptor.ApplyStateExtra drops, so a leg
                // written on it would never touch NetState at all and would pass either way.
                // Byte 0 is the host's own state, which for a dying boss is `dead` (6) -- the
                // value the setter clamps.
                SnapshotWithExtra(IdSpiderBoss2, spiderType, hp: 0, new byte[] { 6, 1, 0 });
                Check("SpiderBoss: a snapshot turn does NOT un-do the local death",
                    ((INetEntity)local).NetIsDying);
                NetPuppets.OnDeathBegan(IdSpiderBoss2);
                Check("SpiderBoss: ...so the host's beat after it fires NO second burst (+"
                    + (DeathFxCount(game) - afterLocal) + ")",
                    DeathFxCount(game) == afterLocal);
            }

            // NEGATIVE: the release is not unconditional. A non-killable type with no deferred
            // death of its own answers `false` and must be left frozen and registered -- without
            // this, the null-killable branch would un-freeze live enemies into the client's
            // world on any stray beat.
            byte bulletType = TypeIdxOf(new EvilBullet(game));
            EvilBullet bullet = (EvilBullet)BuildPuppet<EvilBullet>(game, IdBullet2, bulletType, planted);
            Check("SpiderBoss: PRECONDITION a second EvilBullet puppet was built", bullet != null);
            if (bullet != null)
            {
                liveBefore = NetPuppets.LiveCount;
                boom = DeathFxCount(game);
                Check("NEGATIVE PRECONDITION an EvilBullet declares no deferred death of its own",
                    !((INetEntity)bullet).NetBeginDeferredDeath());
                NetPuppets.OnDeathBegan(IdBullet2);
                Check("NEGATIVE a beat for a non-killable with no deferred death releases"
                    + " NOTHING (+" + (DeathFxCount(game) - boom) + " FX)",
                    !bullet.Enabled && NetPuppets.LiveCount == liveBefore
                    && DeathFxCount(game) == boom);
            }
        }

        // ---- 10. teardown ------------------------------------------------------------------------

        private static void Teardown(StringBuilder sb, Action<string, bool> Check, ComponentBin bin,
            Game game, ScoreVisualiser score, float[] scoreBefore, List<GameComponent> planted,
            SoundManager sound, int songBefore)
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
            // Three collateral types, none of which is ever in `planted`: the death paths spawn
            // Explosion AND BloodExplosion (a plain AlienDrawableGameComponent, NOT an Explosion
            // subclass -- sweeping only the latter left the boss legs' hundreds of blood bursts
            // in the world), and BrainBoss.Initialize adds its own BrainAura child.
            foreach (GameComponent comp in CollectType<Explosion>(game))
            {
                bin.Remove(comp);
            }
            foreach (GameComponent comp in CollectType<BloodExplosion>(game))
            {
                bin.Remove(comp);
            }
            foreach (GameComponent comp in CollectType<BrainAura>(game))
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
            Check("no death FX component was left in the world",
                CountType<Explosion>(game) == 0 && CountType<BloodExplosion>(game) == 0
                && CountType<BrainAura>(game) == 0);
            // Restart whatever was playing if a boss leg stopped it. `songBefore` < 0 means
            // nothing was playing, in which case a stopped state is already correct.
            if (songBefore >= 0 && sound.NetCurrentSong != songBefore)
            {
                sound.PlayMusic((Songs)songBefore);
            }
            Check("the music is back where it started (was " + songBefore + ", now "
                + sound.NetCurrentSong + ")", sound.NetCurrentSong == songBefore);
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
            NetPuppets.OnSnapshotEntryNextSeq(netId, typeIdx, NetProtocol.NetSnapshotFlags.None, state, new byte[1], 0, 0, out _, out _);
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

        // As Snapshot, but carrying a real per-type state-extra block. Everything else in this
        // suite only cares about the base block's hp, so the bare helper sends none.
        private static void SnapshotWithExtra(ushort netId, byte typeIdx, int hp, byte[] extra)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = hp;
            NetPuppets.OnSnapshotEntry(netId, typeIdx, NetProtocol.NetSnapshotFlags.None, state,
                extra, 0, extra.Length, out _, out _);
        }

        private static void SnapshotKind(ushort netId, byte typeIdx, int hp, out SnapUnknownKind kind)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = hp;
            NetPuppets.OnSnapshotEntryNextSeq(netId, typeIdx, NetProtocol.NetSnapshotFlags.None, state, new byte[1], 0, 0, out _, out kind);
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

        // Both death-FX families. `BloodExplosion` is an AlienDrawableGameComponent, NOT an
        // Explosion subclass, and the SpiderBoss's entire debris death is made of it -- so a
        // CountType<Explosion> alone reads zero for the one leg that matters most.
        private static int DeathFxCount(Game game)
        {
            return CountType<Explosion>(game) + CountType<BloodExplosion>(game);
        }

        // Drive ONE component's real Update at a fixed 60 Hz, the isolation-sim pattern: the
        // thing under test is a 3-to-20-second choreography, which no frame and no timed
        // screenshot can see. Nothing else in the world is ticked, so every effect that appears
        // during a Tick belongs to this component.
        private static void Tick(GameComponent comp, int ticks)
        {
            TimeSpan step = TimeSpan.FromTicks(166667); // 16.6667 ms
            TimeSpan total = TimeSpan.Zero;
            for (int i = 0; i < ticks; i++)
            {
                total += step;
                comp.Update(new GameTime(total, step));
            }
        }

        // Tick until the component removes ITSELF, up to a ceiling, and report how long it took.
        //
        // A FIXED tick count is not a safe budget here, and the SpiderBoss is why: its fall is
        // `ResetTimer(5f)`, i.e. 5000 ms divided by Settings.DifficultyFactorized(0.5f) -- so on
        // an Easy save the fall really does last longer than five seconds. eahl boots a clean
        // temp save (default difficulty) and a browser reads the player's own, so a fixed budget
        // passed headlessly and failed in Chrome -- measured 300 ticks under eahl against 376
        // in a browser reading a real save. This is the repo's "pin a probe's
        // precondition, never wait it out" rule in its other form: what is asserted is that the
        // animation TERMINATES on its own, so the ceiling is generous and the measured count is
        // reported for a reader to notice drift.
        private static int TickUntilGone(GameComponent comp, ComponentBin bin, Game game, int cap)
        {
            for (int i = 0; i < cap; i++)
            {
                Tick(comp, 1);
                bin.TopOfTickFlush(); // its own Die() only QUEUES the removal
                if (!InWorld(game, comp))
                {
                    return i + 1;
                }
            }
            return cap;
        }

        private static bool SameScoresExcept(ScoreVisualiser score, float[] before, int skipSlot)
        {
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                if (i != skipSlot && Math.Abs(score.PointScore(i) - before[i]) >= 0.01f)
                {
                    return false;
                }
            }
            return true;
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
