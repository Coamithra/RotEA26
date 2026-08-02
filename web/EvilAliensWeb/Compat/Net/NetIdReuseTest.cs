using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // THE VANISHING LASER UFO, AND THE CLAIM THAT DELETED IT (cards 9ccfe295 + 54e9a590).
    // Run `eaNetIdReuse()` from the MAIN MENU, or `eval NetIdReuse` under eahl.
    // Own probe: tools/headless/probes/net_id_reuse.txt.
    //
    // THE CHAIN, because no single leg here is the bug -- the bug is their composition:
    //   1. `Lazer.owner` is written ONLY by `Setup`, which no puppet runs. `LazerDescriptor`
    //      builds its puppets through `SetupSingleShot`, so a client's beam had NO emitter.
    //   2. `UFO.CollidesWith` damages itself off any `Lazer` whose `owner != this`. With a null
    //      owner that is TRUE for the very ship that fired it -- so on the joiner a big laser
    //      UFO was hit by its own beam, 11 hit points at a 35 ms hittimer.
    //   3. `KillableAlien.HitBy` calls `NoteKill(this, other)`, and a `Lazer` is not an
    //      `IAlienKiller`, so the note is `KillerNone`.
    //   4. The removal seam sent `EvClaim(netId, KillerNone)` anyway, and `HandleClaim`'s live
    //      branch fell through to the non-killable arm's bare `bin.Remove`: the HOST's UFO
    //      vanished with no explosion, no cue and a KillerNone `EvDeath`, whose client branch is
    //      also a silent despawn. Reported as "large laser-firing UFOs randomly disappear" and
    //      "P2 shoots them but the explosion does not play on P1's screen".
    //
    // SO THERE ARE TWO FIXES AND THIS SUITE ASSERTS BOTH SEPARATELY. The emitter on the wire
    // (sections 1-3) removes the mis-simulation; the unattributed-claim guard (sections 4-5)
    // removes the destructive amplifier, and stands on its own -- Floorbottom, an asteroid and
    // any future puppet-vs-puppet mishap reach step 4 by other routes.
    //
    // EVERY POSITIVE HAS ITS NEGATIVE BESIDE IT, and here that matters more than usual: "the UFO
    // survived" passes on a build where the two never collided at all, so section 2 asserts the
    // pre-card configuration (owner null) DOES kill it over the identical geometry. Without that
    // control the whole suite would go green on a rig that proves nothing.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE (the eaNetFx / eaNetScenarios shape, not
    // eaNetResetSpawn's): no GameScene is needed -- the client rx paths gate on the INetScene
    // SEAM, which a stand-in satisfies -- and every entity it builds is taken back out of the
    // live bin in the finally. It plants far off screen, so nothing it does is drawn.
    internal static class NetIdReuseTest
    {
        private const string Room = "netidreuse";

        private const byte PeerSlot = 1;

        private const ulong PeerToken = 0x9CCFE295UL;

        // Far off screen, so nothing this suite builds is ever drawn -- and, for the host legs,
        // so `OnHostDeath`'s on-screen gate cannot turn a removal into a KillerSelf broadcast.
        private static readonly Vector2 Nowhere = new Vector2(-900f, -900f);

        private const ushort UfoId = 9400;
        private const ushort LazerId = 9401;
        private const ushort OwnlessLazerId = 9402;

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

            sb.Append("[netidreuse] the vanishing laser UFO (cards 9ccfe295 / 54e9a590)\n");

            // The eaNetScenarios / eaNetSnap gate: this starts REAL sessions and plants real
            // entities in the LIVE bin, so a session, level or attract demo is a reason to
            // report a SKIP rather than let an unrun suite read as a pass.
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
                Section1PoolReset(sb, Check, bin, game, planted);
                Section2SelfHit(sb, Check, bin, game, planted);
                Section3HostEncodesOwner(sb, Check, bin, game, planted);
                Section4HostKeepsUnattributed(sb, Check, bin, game, planted);
                Section5ClientSendsNoUnattributedClaim(sb, Check, bin, game, planted);
                Section6SameFrameReuse(sb, Check, bin, game, planted);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + Describe(ex) + ")", false);
            }
            finally
            {
                NetSession.Stop("netidreuse suite teardown");
                Teardown(game, bin, planted);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back", !NetScene.IsOverridden);
                Check("the suite left no live puppets", NetPuppets.LiveCount == 0);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- 1. the pooling half: SetupSingleShot must CLEAR a recycled beam's owner ---------
        //
        // `Lazer` is pooled (`NewLazer` -> `bin.Recycle<Lazer>`), and only `Setup` ever wrote
        // `owner`. So a recycled single-shot beam kept the PREVIOUS emitter and would spare the
        // WRONG enemy -- on BOTH peers, since `SetupSingleShot` is also how the JunkBoss and the
        // two motherships fire. Needs no session at all: it is a property of the two entry
        // points. The same recycle trap `netSweepRadPerMs` documents two lines above it.
        private static void Section1PoolReset(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 1. a recycled beam does not inherit the last emitter (pooling)\n");

            UFO emitter = PlantUfo(bin, game, planted, big: true);
            Lazer beam = Lazer.NewLazer(bin, game);
            beam.Setup(Nowhere, 0f, emitter, 75f);
            Check("PRECONDITION Setup assigns the emitter", ReferenceEquals(beam.NetOwner, emitter));

            // Drive the pool for real rather than re-calling Setup on the same instance: the
            // whole point is that the NEXT life of this object starts clean.
            bin.Add((GameComponent)(object)beam);
            planted.Add((GameComponent)(object)beam);
            bin.Remove((GameComponent)(object)beam);
            bin.TopOfTickFlush();
            Lazer recycled = Lazer.NewLazer(bin, game);
            Check("PRECONDITION the pool handed the same instance back",
                ReferenceEquals(recycled, beam));
            recycled.SetupSingleShot(Nowhere, 0f, 0f, playSound: false);
            Check("...and SetupSingleShot cleared its owner", recycled.NetOwner == null);
        }

        // ---- 2. the defect itself: an ownerless beam kills the ship that fired it ------------
        //
        // The DECISION (`UFO.CollidesWith`) and the GEOMETRY (`DetectCollision`) are asserted
        // separately, because they fail differently and only their conjunction is the bug. The
        // decision leg is exact and phase-independent; the geometry leg is what says the pair
        // can meet at all, i.e. that the defect is REACHABLE rather than theoretical.
        private static void Section2SelfHit(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 2. an OWNERLESS beam damages its own emitter; an owned one does not\n");

            UFO shooter = PlantUfo(bin, game, planted, big: true);
            int fullHp = ((INetEntity)shooter).NetKillable.NetHitPoints;
            Check("PRECONDITION a big UFO has hit points to lose (" + fullHp + ")", fullHp > 1);

            // The beam exactly as UFO.Update fires it: muzzle on the emitter, lead 75.
            Lazer ownless = Lazer.NewLazer(bin, game);
            ownless.SetupSingleShot(shooter.Position, MathHelper.PiOver2, 75f, playSound: false);
            ownless.NetApplyBeam(MathHelper.PiOver2, 400f, 75f);
            bin.Add((GameComponent)(object)ownless);
            planted.Add((GameComponent)(object)ownless);

            // GEOMETRY, MEASURED RATHER THAN ASSUMED -- and the number matters, because at the
            // EXACT fire pose the 75 px lead clears the emitter's hitbox and the two do NOT
            // meet. What closes the gap on a client is DRIFT: the beam and the UFO are separate
            // puppets, corrected on separate snapshot round-robin turns and dead-reckoned blind
            // in between, so their relative offset wanders. The honest claim is therefore "the
            // self-hit needs N px of relative drift along the beam", and this measures N.
            Vector2 muzzle = shooter.Position;
            float needed = -1f;
            for (float d = 0f; d <= 200f; d += 1f)
            {
                shooter.Position = muzzle + new Vector2(0f, d); // toward the beam, along +Y
                if (shooter.DetectCollision(ownless) || ownless.DetectCollision(shooter))
                {
                    needed = d;
                    break;
                }
            }
            shooter.Position = muzzle;
            sb.Append("    (geometry: the emitter meets its OWN beam after "
                + (needed < 0f ? ">200" : needed.ToString("0")) + " px of relative drift along"
                + " it; SnapThresholdPx is 100 and a correction blends over >=150 ms, so this"
                + " sits well inside what two separately-corrected puppets wander)\n");
            Check("the self-hit is REACHABLE by ordinary puppet drift (<= 100 px, the snap"
                + " threshold) rather than needing an implausible offset",
                needed >= 0f && needed <= 100f);

            // THE NEGATIVE CONTROL, and it is what carries the card: with no owner the emitter
            // damages itself. If this stops failing the pre-card way, the suite below is vacuous.
            shooter.CollidesWith(ownless);
            int afterOwnless = ((INetEntity)shooter).NetKillable.NetHitPoints;
            Check("an ownerless beam DAMAGES the emitter (hp " + fullHp + " -> " + afterOwnless
                + ") -- the pre-card client, and the reason for the wire field",
                afterOwnless < fullHp);

            // THE POSITIVE: the same collision with the emitter adopted is refused outright.
            // A fresh UFO, because the one above is now inside its 35 ms hittimer and would
            // refuse a second hit whatever the owner said -- exactly the vacuity `eaNetFx`'s
            // second Ball exists to avoid.
            UFO owned = PlantUfo(bin, game, planted, big: true);
            Lazer mine = Lazer.NewLazer(bin, game);
            mine.SetupSingleShot(owned.Position, MathHelper.PiOver2, 75f, playSound: false);
            mine.NetSetOwner(owned);
            mine.NetApplyBeam(MathHelper.PiOver2, 400f, 75f);
            bin.Add((GameComponent)(object)mine);
            planted.Add((GameComponent)(object)mine);
            int ownedBefore = ((INetEntity)owned).NetKillable.NetHitPoints;
            owned.CollidesWith(mine);
            Check("...and an OWNED beam does not (hp " + ownedBefore + " -> "
                + ((INetEntity)owned).NetKillable.NetHitPoints + ")",
                ((INetEntity)owned).NetKillable.NetHitPoints == ownedBefore);

            // A beam belonging to SOMEONE ELSE must still hurt, or the fix would make every
            // enemy immune to every other enemy's laser -- the over-correction to watch for.
            UFO bystander = PlantUfo(bin, game, planted, big: true);
            int bystanderBefore = ((INetEntity)bystander).NetKillable.NetHitPoints;
            bystander.CollidesWith(mine);
            Check("...while ANOTHER ship's beam still hurts (hp " + bystanderBefore + " -> "
                + ((INetEntity)bystander).NetKillable.NetHitPoints + ")",
                ((INetEntity)bystander).NetKillable.NetHitPoints < bystanderBefore);
        }

        // ---- 3. the HOST puts the emitter on the wire ---------------------------------------
        //
        // Read off the frame the peer RECEIVED, through the real `TryDecodeSpawnEvent`, rather
        // than by calling `EncodeSpawnExtra` and decoding it again: a matching pair of wrong
        // offsets passes an encode/decode round trip (`eaNetWire.test`'s own rule).
        private static void Section3HostEncodesOwner(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 3. HOST -- the beam's EvSpawn carries its emitter's netId (protocol v18)\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> spawns = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= 2 && payload[0] == NetProtocol.MsgEvent
                    && payload[1] == NetProtocol.EvSpawn)
                {
                    spawns.Add(payload);
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
                    return; // OnHostSpawn early-returns with no peer; every leg below is vacuous
                }

                spawns.Clear();
                UFO emitter = PlantUfo(bin, game, planted, big: true);
                wire.Pump();
                Check("PRECONDITION the planted emitter got a netId and a spawn frame",
                    NetIdRegistry.TryGetByComp((GameComponent)(object)emitter,
                        out NetIdRegistry.Entry ufoEntry) && spawns.Count == 1);
                if (!NetIdRegistry.TryGetByComp((GameComponent)(object)emitter, out ufoEntry))
                {
                    return;
                }

                spawns.Clear();
                Lazer owned = Lazer.NewLazer(bin, game);
                owned.Setup(emitter.Position, MathHelper.PiOver2, emitter, 75f);
                bin.Add((GameComponent)(object)owned);
                planted.Add((GameComponent)(object)owned);
                wire.Pump();
                Check("the beam broadcast exactly one EvSpawn (" + spawns.Count + ")",
                    spawns.Count == 1);
                Check("...naming its emitter (" + OwnerIdIn(spawns) + " == the UFO's "
                    + ufoEntry.Id + ")", spawns.Count == 1 && OwnerIdIn(spawns) == ufoEntry.Id);

                // THE NEGATIVE, and it is not decoration: every SetupSingleShot emitter in the
                // game (JunkBoss, both motherships) genuinely has no owner, so 0 has to reach
                // the wire as 0 rather than as some stale entry.
                spawns.Clear();
                Lazer solo = Lazer.NewLazer(bin, game);
                solo.SetupSingleShot(Nowhere, MathHelper.PiOver2, 75f, playSound: false);
                bin.Add((GameComponent)(object)solo);
                planted.Add((GameComponent)(object)solo);
                wire.Pump();
                Check("an ownerless beam reports netId 0 (" + OwnerIdIn(spawns) + ")",
                    spawns.Count == 1 && OwnerIdIn(spawns) == 0);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("netidreuse section 3");
            }
        }

        // ---- 4. HOST -- an unattributed claim keeps the entity and re-announces it -----------
        private static void Section4HostKeepsUnattributed(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 4. HOST -- an unattributed claim never deletes a live entity\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> spawns = new List<byte[]>();
            List<byte[]> deaths = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length < 2 || payload[0] != NetProtocol.MsgEvent)
                {
                    return;
                }
                if (payload[1] == NetProtocol.EvSpawn) { spawns.Add(payload); }
                else if (payload[1] == NetProtocol.EvDeath) { deaths.Add(payload); }
            }

            ushort eventSeq = 1;
            try
            {
                NetSession.StartForTest(game, host: true, ours, Room);
                peer.Open(Room);
                peer.OnData += Sniff;
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, PeerSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                if (!NetSession.PeerUp)
                {
                    Check("PRECONDITION the scripted client paired (section 4)", false);
                    return;
                }

                // 4a. THE CARD. A live killable, claimed with no killer.
                UFO victim = PlantUfo(bin, game, planted, big: true);
                if (!NetIdRegistry.TryGetByComp((GameComponent)(object)victim,
                    out NetIdRegistry.Entry entry))
                {
                    Check("PRECONDITION the planted UFO got a netId", false);
                    return;
                }
                bin.TopOfTickFlush();
                // Drain the victim's OWN spawn frame before arming the window -- it is still
                // queued on the transport, and clearing the list without pumping first counts
                // it as the re-announce below (which is what it read as on the first run).
                wire.Pump();
                spawns.Clear();
                deaths.Clear();
                int explosionsBefore = CountType<Explosion>(game);
                long honoredBefore = NetSession.Metrics.ClaimsHonored;
                long unattributedBefore = NetSession.Metrics.ClaimsUnattributed;

                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, entry.Id,
                    NetProtocol.KillerNone));
                wire.Pump();
                NetSession.Update();
                bin.TopOfTickFlush();
                wire.Pump();

                Check("the entity is STILL in the world", InWorld(game, (GameComponent)(object)victim));
                Check("...and not dead", !victim.IsDead);
                // The FX assertion is the half the card reported: a silent removal and an
                // exploding removal are both "gone", so survival alone would not tell them apart
                // if the guard were ever loosened into "explode it instead".
                Check("...no explosion was spawned (" + (CountType<Explosion>(game) - explosionsBefore) + ")",
                    CountType<Explosion>(game) == explosionsBefore);
                Check("...no EvDeath went out (" + deaths.Count + ")", deaths.Count == 0);
                Check("...the claim was NOT honoured",
                    NetSession.Metrics.ClaimsHonored == honoredBefore);
                Check("...it counted as unattributed",
                    NetSession.Metrics.ClaimsUnattributed == unattributedBefore + 1);
                // The recovery half: the joiner has already dropped its puppet and MarkRemoved
                // the id, so without a re-announce it would blank for RecentRemovalWindowMs and
                // then self-heal into a GENERICALLY DRESSED puppet (card de4d5d65's provisional
                // shape, with no later EvSpawn to fix it).
                Check("...and the entity was RE-ANNOUNCED to the peer (" + spawns.Count
                    + " EvSpawn, id " + OwnerlessSpawnIdIn(spawns) + ")",
                    spawns.Count == 1 && OwnerlessSpawnIdIn(spawns) == entry.Id);

                // 4b. THE POSITIVE CONTROL. The identical claim, attributed, still kills --
                // otherwise the guard could have been "refuse every claim" and pass 4a.
                UFO shot = PlantUfo(bin, game, planted, big: true);
                if (!NetIdRegistry.TryGetByComp((GameComponent)(object)shot, out NetIdRegistry.Entry shotEntry))
                {
                    Check("PRECONDITION the second UFO got a netId", false);
                    return;
                }
                bin.TopOfTickFlush();
                deaths.Clear();
                honoredBefore = NetSession.Metrics.ClaimsHonored;
                peer.SendReliable(NetProtocol.EncodeClaimEvent(eventSeq++, shotEntry.Id, PeerSlot));
                wire.Pump();
                NetSession.Update();
                bin.TopOfTickFlush();
                wire.Pump();
                Check("an ATTRIBUTED claim still kills it", shot.IsDead
                    || !InWorld(game, (GameComponent)(object)shot));
                Check("...and is honoured", NetSession.Metrics.ClaimsHonored == honoredBefore + 1);
                Check("...and broadcasts its EvDeath (" + deaths.Count + ")", deaths.Count == 1);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("netidreuse section 4");
            }
        }

        // ---- 5. CLIENT -- an unattributed puppet death sends no claim -----------------------
        private static void Section5ClientSendsNoUnattributedClaim(StringBuilder sb,
            Action<string, bool> Check, ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 5. CLIENT -- an unattributed puppet death files no claim\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> claims = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= 2 && payload[0] == NetProtocol.MsgEvent
                    && payload[1] == NetProtocol.EvClaim)
                {
                    claims.Add(payload);
                }
            }

            ushort eventSeq = 1;
            try
            {
                NetScene.Current = new ReuseScene();
                NetSession.StartForTest(game, host: false, ours, Room);
                peer.Open(Room);
                peer.OnData += Sniff;
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                    NetSession.LocalBuildHash, 0, PeerSlot, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                if (!NetSession.IsClient || !NetSession.PeerUp)
                {
                    Check("PRECONDITION a real CLIENT session paired (section 5)", false);
                    return;
                }

                byte ufoIdx = TypeIdxOf(UFO.NewUFO(bin, game));
                byte lazerIdx = TypeIdxOf(Lazer.NewLazer(bin, game));
                NetBaseState state = default(NetBaseState);
                state.Pos = Nowhere;
                state.Scale = 1f;
                state.Hp = 11;

                // The emitter first, then its beam naming it -- the real ordering, since an
                // emitter always spawns before the beam it fires and both ride the ORDERED
                // reliable lane.
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, UfoId, ufoIdx, state,
                    UfoSpawnExtras(), 2));
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, LazerId, lazerIdx, state,
                    OwnerExtras(UfoId), 2));
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, OwnlessLazerId, lazerIdx,
                    state, OwnerExtras(0), 2));
                wire.Pump();
                NetSession.Update();
                TrackPuppets(game, planted);

                UFO ufo = NetPuppets.FindPuppet(UfoId) as UFO;
                Lazer owned = NetPuppets.FindPuppet(LazerId) as Lazer;
                Lazer ownless = NetPuppets.FindPuppet(OwnlessLazerId) as Lazer;
                Check("the scripted host's EvSpawns built all three puppets",
                    ufo != null && owned != null && ownless != null);
                if (ufo == null || owned == null || ownless == null)
                {
                    return;
                }
                // The DECODE half of section 3: the spawn extra really reaches `owner`.
                Check("the beam puppet adopted the emitter named on the wire",
                    ReferenceEquals(owned.NetOwner, ufo));
                Check("...and a netId-0 beam adopted nobody (the negative)", ownless.NetOwner == null);

                // 5a. An unattributed gameplay death of a puppet. `Die()` is what every such
                // path ends in, and it is what the removal seam reads as `IsDead`.
                claims.Clear();
                long claimsTxBefore = NetSession.Metrics.ClaimsTx;
                // A real gameplay death that nobody landed -- the shared entry point
                // OnRemoteDeath uses for exactly this case, so it runs the type's own death
                // path and leaves no kill note, which is what makes it unattributed.
                ((INetKillable)(object)ufo).NetReplayUnattributedDeath(null);
                bin.Remove((GameComponent)(object)ufo);
                bin.TopOfTickFlush();
                wire.Pump();
                Check("an unattributed puppet death sends NO claim (" + claims.Count + ")",
                    claims.Count == 0);
                Check("...and moves no claim counter",
                    NetSession.Metrics.ClaimsTx == claimsTxBefore);

                // 5b. THE POSITIVE CONTROL -- an ATTRIBUTED death still claims, so 5a cannot be
                // "the seam stopped working". The note is written the way KillableAlien.HitBy
                // writes it, through the real hook.
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, UfoId, ufoIdx, state,
                    UfoSpawnExtras(), 2));
                wire.Pump();
                NetSession.Update();
                TrackPuppets(game, planted);
                UFO second = NetPuppets.FindPuppet(UfoId) as UFO;
                Check("PRECONDITION a second UFO puppet is up", second != null);
                if (second == null)
                {
                    return;
                }
                claims.Clear();
                NetSession.NoteKillSlot(second, PeerSlot);
                ((INetKillable)(object)second).NetReplayUnattributedDeath(null);
                bin.Remove((GameComponent)(object)second);
                bin.TopOfTickFlush();
                wire.Pump();
                Check("an ATTRIBUTED puppet death still sends its claim (" + claims.Count + ")",
                    claims.Count == 1);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("netidreuse section 5");
                NetScene.Current = null;
            }
        }

        // ---- 6. same-frame die-then-reuse (card 54e9a590) ------------------------------------
        //
        // `ComponentBin.Add` on a component whose removal is still QUEUED cancels the death,
        // re-runs `Initialize()` and returns -- firing NO collection events. `NetIdRegistry`
        // hangs entirely off those events, so the netId would survive across what is, on the
        // host, a different entity: the joiner's puppet would keep the old spawn extras and be
        // dragged to the new spawn position by the interpolation blend.
        //
        // THIS SECTION IS A FINDING, NOT A FIX. It asserts what the bin does today so the
        // hazard is pinned and greppable; whether any shipped call site reaches it is a
        // separate question (audited: none does -- every reuse in the game goes through
        // `bin.Recycle<T>()`, which draws from `idleList`, i.e. only components that have
        // already LEFT the collection and therefore already fired `ComponentRemoved`). If a
        // future call site re-adds a live-but-dying component, this is where it bites.
        private static void Section6SameFrameReuse(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 6. FINDING -- a same-frame resurrect keeps its netId silently (card 54e9a590)\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            int spawnFrames = 0;
            int deathFrames = 0;
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length < 2 || payload[0] != NetProtocol.MsgEvent) { return; }
                if (payload[1] == NetProtocol.EvSpawn) { spawnFrames++; }
                else if (payload[1] == NetProtocol.EvDeath) { deathFrames++; }
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
                if (!NetSession.PeerUp)
                {
                    Check("PRECONDITION the scripted client paired (section 6)", false);
                    return;
                }

                UFO reused = PlantUfo(bin, game, planted, big: false);
                if (!NetIdRegistry.TryGetByComp((GameComponent)(object)reused,
                    out NetIdRegistry.Entry before))
                {
                    Check("PRECONDITION the planted UFO got a netId", false);
                    return;
                }
                ushort idBefore = before.Id;
                bin.TopOfTickFlush();
                wire.Pump();
                spawnFrames = 0;
                deathFrames = 0;

                // Remove and re-Add in the SAME tick, with no flush between: the resurrect path.
                bin.Remove((GameComponent)(object)reused);
                bin.Add((GameComponent)(object)reused);
                bin.TopOfTickFlush();
                wire.Pump();

                bool sameId = NetIdRegistry.TryGetByComp((GameComponent)(object)reused,
                    out NetIdRegistry.Entry after) && after.Id == idBefore;
                Check("PRECONDITION the resurrect kept the component in the world",
                    InWorld(game, (GameComponent)(object)reused));
                Check("FINDING the netId survives the resurrect unchanged (" + idBefore
                    + ") -- no EvDeath, no EvSpawn (" + deathFrames + "/" + spawnFrames
                    + "), so a peer's puppet would keep the OLD spawn extras",
                    sameId && deathFrames == 0 && spawnFrames == 0);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("netidreuse section 6");
            }
        }

        // ---- helpers -------------------------------------------------------------------------

        // A real UFO through its own factory + Setup, planted off screen so nothing is drawn.
        // `big` picks the laser-firing variant the card is about (11 hit points, mediumship).
        private static UFO PlantUfo(ComponentBin bin, Game game, List<GameComponent> planted, bool big)
        {
            UFO u = UFO.NewUFO(bin, game);
            u.Setup(Nowhere, big, EnemyBehaviour.normal);
            u.Position = Nowhere; // configure-then-Add (tools/audit_add_order.py)
            bin.Add((GameComponent)(object)u);
            planted.Add((GameComponent)(object)u);
            u.Position = Nowhere; // Initialize ran inside Add and may have moved it
            return u;
        }

        // UfoDescriptor's spawn extras: [flags][bonusType]. FlagBig = 1.
        private static byte[] UfoSpawnExtras()
        {
            return new byte[] { 1, 0 };
        }

        // LazerDescriptor's spawn extras: [ownerNetId:2], little-endian.
        private static byte[] OwnerExtras(ushort ownerId)
        {
            return new byte[] { (byte)ownerId, (byte)(ownerId >> 8) };
        }

        // The owner netId carried by the ONE sniffed EvSpawn, or 0xFFFF if it cannot be read --
        // a value the registry never allocates, so a decode failure can never read as a pass.
        private static ushort OwnerIdIn(List<byte[]> spawns)
        {
            if (spawns.Count != 1
                || !NetProtocol.TryDecodeSpawnEvent(spawns[0], out _, out _, out _,
                    out int extraOff, out int extraLen)
                || extraLen < 2)
            {
                return 0xFFFF;
            }
            return (ushort)(spawns[0][extraOff] | (spawns[0][extraOff + 1] << 8));
        }

        private static ushort OwnerlessSpawnIdIn(List<byte[]> spawns)
        {
            if (spawns.Count != 1
                || !NetProtocol.TryDecodeSpawnEvent(spawns[0], out ushort netId, out _, out _,
                    out _, out _))
            {
                return 0xFFFF;
            }
            return netId;
        }

        // The registry is an exact-runtime-type map, so a throwaway instance answers for the
        // type. It is never added to the world.
        private static byte TypeIdxOf(GameComponent probe)
        {
            return NetTypeRegistry.TryGet(probe, out byte idx, out _) ? idx : (byte)0;
        }

        private static void TrackPuppets(Game game, List<GameComponent> planted)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (NetPuppets.IsPuppet(item) && !planted.Contains(item))
                {
                    planted.Add(item);
                }
            }
        }

        private static int CountType<T>(Game game)
        {
            int n = 0;
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is T) { n++; }
            }
            return n;
        }

        private static bool InWorld(Game game, GameComponent comp)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (ReferenceEquals(item, comp)) { return true; }
            }
            return false;
        }

        // Leave no trace: everything this suite planted leaves the world AND the recycle pool,
        // so a second run in the same process reads the same tally (the eaBinTest rule).
        private static void Teardown(Game game, ComponentBin bin, List<GameComponent> planted)
        {
            foreach (GameComponent c in planted)
            {
                bin.Remove(c);
            }
            bin.TopOfTickFlush();
            foreach (GameComponent c in planted)
            {
                bin.PruneIdle(c);
            }
        }

        private static string Describe(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        private static string Tally(int pass, int fail)
        {
            return "[netidreuse] " + pass + " passed, " + fail + " failed\n";
        }

        // The client rx paths gate on "is a scene up", and nothing in this suite is about what a
        // scene DOES -- the NetScenarioTest scenario-5 shape.
        private sealed class ReuseScene : INetScene
        {
            public Levels Level => Levels.Level1;

            public bool NetEndingNormally => false;

            public bool JoinWouldSpawnNow => false;

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
