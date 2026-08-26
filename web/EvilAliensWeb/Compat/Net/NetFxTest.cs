using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using EvilAliensWeb.Compat.Net.Descriptors;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // THE TRANSIENT-FEEDBACK BEATS (cards 43e85936 / 57ea30cd / ee939dd1 / 8d063d33 / c146422f).
    // Run `eaNetFx()` from the MAIN MENU, or `eval NetFx` under eahl. A leg of
    // tools/headless/probes/net_selftests.txt.
    //
    // WHAT IT COVERS, and why a codec test does not. eaNetWire.test() puts EvFx frames on a real
    // wire and reads them back, which proves the LAYOUT. What it cannot see is whether the frame
    // then does anything: the whole bug class these cards are about is a host-side effect with no
    // lane, so "the beat arrived" and "the puppet lit up" are exactly the two facts that used to
    // disagree. This suite drives real frames from a SCRIPTED HOST over a NetWire into a REAL
    // CLIENT NetSession and asserts the effect on the live puppet -- the NetScenarioTest shape.
    //
    // THE OBSERVABLES ARE DELIBERATELY THE PRIVATE ONES. A hit blink is a 35ms timer read only by
    // Draw; a detach burst is an Explosion entering the bin. Neither moves a metric, and neither
    // survives long enough to screenshot even if a headless frame could be timed to it -- which is
    // the same reason these effects needed a wire beat in the first place. So the entity types
    // expose narrow `Net*` readbacks (KillableAlien.NetHitBlinking, Ball.NetHitBlinking /
    // NetDetachedFx) and this suite reads those.
    //
    // EVERY POSITIVE HAS ITS NEGATIVE BESIDE IT. An apply path hard-wired to "always flash" would
    // pass a bare before/after, so each section also asserts the beat is REFUSED where it must be:
    // an unknown netId reaches nothing, and a second beat for an event already applied locally
    // does not re-fire. The idempotence legs are not decoration -- a client hit-tests puppets with
    // its own bullets, so for any hit BOTH peers saw, the beat lands on an effect already running.
    //
    // NOTE: section 4 builds a REAL charge glow, and enemy telegraphs are audible on a joiner
    // since these cards -- so running this suite makes a brief "lazercharge" blip. That is the
    // shipped path doing what it should; a silent back door for the test would make the suite stop
    // covering the thing the card changed.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE (the eaNetScenarios shape, not eaNetResetSpawn's): it needs
    // no GameScene -- the client rx paths gate on the INetScene SEAM, which a stand-in satisfies --
    // and every entity it builds is taken back out of the live bin in the finally.
    internal static class NetFxTest
    {
        private const string Room = "netfx";

        private const byte PeerSlot = 1;

        private const ulong PeerToken = 0x5CE7A5C0UL;

        // Off-screen, so nothing this suite builds can be seen for the frame it exists.
        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // A netId no EvSpawn in this suite ever uses -- the "beat with nothing to act on" control.
        private const ushort UnknownId = 60000;

        private const ushort UfoId = 9200;
        private const ushort BallId = 9201;
        // A SECOND ball, used only to make the post-detach refusal non-vacuous -- see there.
        private const ushort BallId2 = 9202;

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

            sb.Append("[netfx] transient-feedback beats (EvFx)\n");

            // Same gate as eaNetScenarios / eaNetSnap: this starts a REAL session and adds real
            // entities to the LIVE bin, so a session, level or attract demo is a reason to report
            // a SKIP rather than let an unrun suite read as a pass.
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
                RunBeats(sb, Check, bin, game, planted);
                // Its own session, so it starts from a clean one: RunBeats leaves a paired CLIENT
                // session up and this half needs a HOST. Stopped in the finally either way.
                NetSession.Stop("netfx host section");
                RunHostEmission(sb, Check, bin, game, planted);
            }
            catch (Exception ex)
            {
                Check("the beats ran (" + Describe(ex) + ")", ok: false);
            }
            finally
            {
                NetSession.Stop("netfx suite teardown");
                Teardown(sb, Check, game, bin, planted);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back", !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        private static void RunBeats(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            NetScene.Current = new FxScene();
            NetSession.StartForTest(game, host: false, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                NetSession.LocalBuildHash, 0, PeerSlot, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("session started as a CLIENT and paired", NetSession.IsClient && NetSession.PeerUp);

            // The two typeIdxs are LOOKED UP through the real registry rather than written down:
            // the wire typeIdx IS the registry order, so a literal here would silently spawn some
            // other enemy the day a descriptor is appended ahead of these.
            byte ufoIdx = TypeIdxOf(UFO.NewUFO(bin, game), Check, "UFO");
            byte ballIdx = TypeIdxOf(Ball.NewBall(bin, game), Check, "Ball");

            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = 10;
            byte[] noExtras = new byte[2];

            sb.Append(" 1. EnemyHitFlash on a KillableAlien puppet (cards 43e85936 / c146422f)\n");

            peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, UfoId, ufoIdx, state, noExtras, 0));
            peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, BallId, ballIdx, state, noExtras, 0));
            wire.Pump();
            NetSession.Update();
            TrackPuppets(game, planted);

            UFO ufo = NetPuppets.FindPuppet(UfoId) as UFO;
            Ball ball = NetPuppets.FindPuppet(BallId) as Ball;
            Check("the scripted host's EvSpawns built both puppets", ufo != null && ball != null);
            if (ufo == null || ball == null)
            {
                return;
            }

            // The NEGATIVE first, so the positive below cannot read as "the rig always flashes":
            // a frozen puppet does not blink by itself, and nothing so far has told it to.
            Check("a fresh puppet is NOT blinking (the pre-state)", !ufo.NetHitBlinking);

            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.EnemyHitFlash, UfoId, 0));
            wire.Pump();
            NetSession.Update();
            Check("an EnemyHitFlash beat lights the puppet up", ufo.NetHitBlinking);

            // The unknown-id control. Both halves matter: it must reach nothing AND it must not
            // throw or wedge the drain -- an FX beat naming a dead id is an ordinary production
            // case (the entity died while the beat was in flight).
            long beatsBefore = NetSession.Metrics.BeatsRx;
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.EnemyHitFlash, UnknownId, 0));
            wire.Pump();
            NetSession.Update();
            Check("a beat for an unknown netId is consumed harmlessly (BeatsRx +"
                + (NetSession.Metrics.BeatsRx - beatsBefore) + ")",
                NetSession.Metrics.BeatsRx == beatsBefore + 1);

            sb.Append(" 2. the Ball chip + detach beats (card c146422f)\n");

            Check("the ball is not blinking and has not detached (the pre-state)",
                !ball.NetHitBlinking && !ball.NetDetachedFx);
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.EnemyHitFlash, BallId, 0));
            wire.Pump();
            NetSession.Update();
            Check("a chip beat lights the ball up", ball.NetHitBlinking);

            // The detach burst is an Explosion entering the LIVE bin, which is the only thing it
            // leaves behind -- counted rather than looked at, since it lasts a few frames.
            bin.TopOfTickFlush();
            int explosionsBefore = CountExplosions(game);
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.BallDetach, BallId, 0));
            wire.Pump();
            NetSession.Update();
            int explosionsAfter = CountExplosions(game);
            Check("a BallDetach beat spawns the break-away burst (explosions +"
                + (explosionsAfter - explosionsBefore) + ")", explosionsAfter == explosionsBefore + 1);
            Check("... and latches the ball as detached", ball.NetDetachedFx);

            // IDEMPOTENCE. This is the leg the design rests on: the client hit-tests puppets with
            // its own bullets, so for a detach BOTH peers observed, the host's beat arrives after
            // the client has already run the real one. A second burst would be a visible double
            // explosion, and the latch is what stops it.
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.BallDetach, BallId, 0));
            wire.Pump();
            NetSession.Update();
            Check("a SECOND detach beat for the same ball fires nothing (explosions still +"
                + (CountExplosions(game) - explosionsBefore) + ")",
                CountExplosions(game) == explosionsAfter);
            // ...and the chip beat is refused once detached, for the same reason: a ball that has
            // broken away is not part of the boss any more and must not keep flashing.
            //
            // ON A SECOND BALL, and that is the whole point of it existing: the first one is
            // already blinking from the chip beat above, and `!hittimer.Active` would refuse a
            // further beat whatever the detach latch said -- so asserting it there would be
            // vacuous and would pass with the latch deleted. This ball is detached having never
            // been chipped, so only the latch can refuse it.
            peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, BallId2, ballIdx, state, noExtras, 0));
            wire.Pump();
            NetSession.Update();
            TrackPuppets(game, planted);
            Ball ball2 = NetPuppets.FindPuppet(BallId2) as Ball;
            Check("a second, un-chipped ball is up and not blinking",
                ball2 != null && !ball2.NetHitBlinking);
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.BallDetach, BallId2, 0));
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.EnemyHitFlash, BallId2, 0));
            wire.Pump();
            NetSession.Update();
            Check("a chip beat AFTER the detach is refused (the ball never lights up)",
                ball2 != null && ball2.NetDetachedFx && !ball2.NetHitBlinking);

            sb.Append(" 3. the beat is CLIENT-ONLY and DRAW-ONLY\n");

            // A beat must never move gameplay state. HP is the one piece of entity state that
            // rides the wire, and the hit flash sits right next to the code that spends it -- so
            // a NetPlayFx that "helpfully" also decremented would desync the two worlds silently.
            int hpBefore = ((INetEntity)ufo).NetKillable.NetHitPoints;
            peer.SendReliable(NetProtocol.EncodeFxEvent(eventSeq++,
                (byte)NetFxKind.EnemyHitFlash, UfoId, 0));
            wire.Pump();
            NetSession.Update();
            Check("a hit beat spends NO hitpoints (hp " + hpBefore + " -> "
                + ((INetEntity)ufo).NetKillable.NetHitPoints + ")",
                ((INetEntity)ufo).NetKillable.NetHitPoints == hpBefore);
            Check("... and the puppet is still alive", !ufo.IsDead);

            sb.Append(" 4. the charge glow, which is a STATE EXTRA rather than a beat\n");

            // Cards 57ea30cd / c146422f. Not everything in this group is an EVENT: a windup is a
            // STATE that lasts seconds, so it rides the snapshot's per-type extras and the child
            // is (re)built by the puppet driver, never by ApplyStateExtra. That split is the
            // design decision these cards rest on, so it is asserted rather than described.
            //
            // The extras are hand-built rather than encoded off a charging host UFO because there
            // is no host here -- but through the REAL NetChargeWire, so a layout change moves this
            // leg with it.
            byte[] chargeExtras = new byte[1 + NetChargeWire.Bytes];
            chargeExtras[0] = NetChargeWire.FlagChargingBit1;
            NetChargeWire.Encode(chargeExtras, 1, new Vector2(20f, 0f), 2.5f, 1f);

            Check("the puppet has no charge glow yet (the pre-state)", !ufo.NetCharging);
            peer.SendStream(SnapshotFor(UfoId, ufoIdx, state, chargeExtras, chargeExtras.Length));
            wire.Pump();
            NetSession.Update();
            // ApplyStateExtra only RECORDS -- the descriptor contract forbids spawning from it --
            // so the glow must still be absent until the driver runs. That ordering is the thing
            // most likely to be "simplified" away by someone spawning it in the apply.
            Check("...and ApplyStateExtra alone does NOT spawn it (it only records)",
                !ufo.NetCharging);
            NetPuppets.Drive(16f);
            Check("the driver builds the charge glow from the replicated state", ufo.NetCharging);

            // ...and takes it away again on the charge-off edge, which is what stops the swarm
            // (and its looped cue) outliving the beam on the joiner's screen.
            byte[] idleExtras = new byte[1];
            peer.SendStream(SnapshotFor(UfoId, ufoIdx, state, idleExtras, idleExtras.Length));
            wire.Pump();
            NetSession.Update();
            NetPuppets.Drive(16f);
            Check("...and frees it when the host stops charging", !ufo.NetCharging);
            TrackPuppets(game, planted);
        }

        // One world-snapshot packet carrying a single entry, so a scenario can drive a per-type
        // STATE EXTRA without a host to encode it. Built with the real WriteSnapshotEntry, so an
        // entry-layout change moves the callers with it rather than silently passing.
        private static ushort snapshotSeq;

        private static byte[] SnapshotFor(ushort id, byte typeIdx, in NetBaseState state,
            byte[] extras, int extrasLen)
        {
            byte[] scratch = new byte[NetProtocol.SnapshotHeaderBytes
                + NetProtocol.SnapshotEntryBaseBytes + extrasLen + 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            NetProtocol.WriteSnapshotEntry(scratch, ref off, id, typeIdx,
                NetProtocol.NetSnapshotFlags.None, state, extras, extrasLen);
            // A MONOTONE seq per packet (card f5cf7a5c): the receiver refuses an entry that is
            // not newer than the last it applied for that netId, so a hand-stamped header's
            // fixed zero would make every packet after the first stale and this suite would
            // silently stop delivering anything.
            NetProtocol.WriteSnapshotHeader(scratch, 1, ++snapshotSeq);
            byte[] packet = new byte[off];
            Array.Copy(scratch, packet, off);
            return packet;
        }

        // The registry index for a live instance of this type, asserted rather than assumed.
        // The instance is a throwaway -- it is never added to the bin, so it takes no NetId.
        private static byte TypeIdxOf(AlienDrawableGameComponent probe,
            Action<string, bool> Check, string name)
        {
            bool ok = NetTypeRegistry.TryGet((GameComponent)(object)probe, out byte idx, out _);
            Check(name + " is a replicable type (registry idx " + idx + ")", ok);
            return idx;
        }

        private static int CountExplosions(Game game)
        {
            int n = 0;
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is Explosion)
                {
                    n++;
                }
            }
            return n;
        }

        // ---- 5. HOST -- which hits put an EnemyHitFlash on the wire (card f6fc1d97) ----------
        //
        // Sections 1-4 are the RECEIVING half: a scripted host sends a beat and the puppet lights
        // up. This is the SENDING half, and it exists because the reported bug lived entirely
        // there: `KillableAlien.HitBy` announced the blink for EVERY hit, so a LETHAL one told the
        // peer "flash" and then, an EvDeath later, "explode". On a one-hit-point enemy that is
        // every kill -- "1 hp ufo's blink white before they blow up (the hit effect for enemies
        // with multiple hit points)".
        //
        // A REAL host session with a scripted client on an in-process wire, a REAL UFO planted
        // into the live bin (so NetIdRegistry allocates a real id through the real ComponentAdded
        // seam), and a REAL Bullet driven through the REAL `CollidesWith` -> `HitBy` path. What is
        // read is the frames the peer ACTUALLY RECEIVED -- the NetDeathFxTest section-2 shape.
        //
        // THE POSITIVE IS THE LOAD-BEARING ONE. "No beat on a lethal hit" is satisfied by a build
        // that stopped sending beats at all, which would silently delete the whole hit tell for
        // every multi-hit-point enemy in the game -- so the survivable hit is asserted first, on
        // the same entity, through the same call.
        private static void RunHostEmission(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted)
        {
            sb.Append(" 5. HOST -- a survivable hit announces a blink, a LETHAL one does not"
                + " (card f6fc1d97)\n");
            // Two things this section inherits or emits, stated because section 4's own blip is:
            // it kills a real UFO, so UFO.KilledBy plays "expl1" -- an explosion noise at the main
            // menu -- and it needs NetScene.Current non-null (OnGameFx's third gate), which it
            // takes from the FxScene RunBeats installed. Reorder the two and 5a fails with a
            // message about the fix rather than about the missing scene.
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            List<byte[]> flashes = new List<byte[]>();
            List<byte[]> deaths = new List<byte[]>();
            int grantedSlot = 0;
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length < 2 || payload[0] != NetProtocol.MsgEvent)
                {
                    return;
                }
                if (payload[1] == NetProtocol.EvFx && payload.Length >= 5
                    && payload[4] == (byte)NetFxKind.EnemyHitFlash)
                {
                    flashes.Add(payload);
                }
                else if (payload[1] == NetProtocol.EvDeath)
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
                Check("PRECONDITION the scripted client paired with a real HOST session",
                    NetSession.IsHost && NetSession.PeerUp);
                if (!NetSession.PeerUp)
                {
                    return; // OnGameFx early-returns with no peer; every leg below would be vacuous
                }
                // The hello asks for SlotNone, so the HOST allocates -- read the grant back rather
                // than assuming it, and release exactly that seat in the finally.
                grantedSlot = NetSession.UpPeerPrimarySlotsMask();

                // isBig: the real MakeBig() path gives 11 hit points, so 5a's survivable hit needs
                // no hp seam at all (see there).
                UFO victim = UFO.NewUFO(bin, game);
                victim.Setup(Nowhere, isBig: true, EnemyBehaviour.normal);
                bin.Add((GameComponent)(object)victim);
                planted.Add((GameComponent)(object)victim);
                bin.TopOfTickFlush();
                Check("PRECONDITION the planted UFO got a netId",
                    NetIdRegistry.TryGetByComp((GameComponent)(object)victim, out _));

                // The killer: a real Bullet, because HitBy is only reachable through
                // CollidesWith's `other is IAlienKiller` test and the beat's own call site sits
                // inside it. Never added to the bin -- it is an argument, not a world entity.
                Bullet shot = Bullet.NewBullet(bin, game);

                // 5a. POSITIVE -- a hit the UFO SURVIVES still announces the blink.
                // The hp comes from the real MakeBig() path (isBig above -> SetHitPoints(11)),
                // NOT from NetApplyHp: that is a CLIENT-PUPPET seam by its own header, and under
                // ?nethpraise=0 -- card 87310afa's own reproduction flag -- its downward-only
                // clamp refuses the raise, so the "survivable" hit becomes lethal and this
                // section fails pointing at the fix instead of at its unmet precondition
                // (measured: 2 FAILs). Asserted before the hit, so the precondition can never be
                // read off the result.
                Check("PRECONDITION the UFO has hit points to spare (hp "
                    + ((INetEntity)victim).NetKillable.NetHitPoints + ")",
                    ((INetEntity)victim).NetKillable.NetHitPoints > 1);
                flashes.Clear();
                victim.CollidesWith((ICollidable)shot);
                wire.Pump();
                Check("a survivable hit put exactly one EnemyHitFlash on the wire ("
                    + flashes.Count + ")", flashes.Count == 1);
                Check("...and the UFO really did survive it (hp "
                    + ((INetEntity)victim).NetKillable.NetHitPoints + ")",
                    ((INetEntity)victim).NetKillable.NetHitPoints > 0 && !victim.IsDead);

                // 5b. THE CARD -- the same entity, the same call, one hit point left.
                // The 35 ms hittimer HitBy opens with would swallow the next hit outright, so the
                // blink is run down first with FOUR hand-built 16.7 ms ticks (66.8 ms). Three is
                // the minimum -- Timer only expires once its remaining time goes NEGATIVE, so two
                // ticks (33.4 ms) leave a 35 ms timer running. (Nothing advances the injected
                // clock here; these GameTimes are the tick, and they are safe on a UFO only
                // because `Nowhere` is off-screen, which is what makes UFO.Update's two
                // !OffScreen() fire branches unreachable.)
                RunDownBlink(victim);
                Check("PRECONDITION the blink has expired, so the lethal hit is not swallowed",
                    !victim.NetHitBlinking);
                ((INetEntity)victim).NetKillable.NetApplyHp(1);
                Check("PRECONDITION the UFO is down to its last hit point (hp "
                    + ((INetEntity)victim).NetKillable.NetHitPoints + ")",
                    ((INetEntity)victim).NetKillable.NetHitPoints == 1);
                flashes.Clear();
                deaths.Clear();
                victim.CollidesWith((ICollidable)shot);
                // PUMP BEFORE ASSERTING. Without it `flashes` is empty whatever the send did, and
                // this leg -- and 5c below -- pass on the pre-card build. Caught by the mutation
                // test, which is the whole reason to run one on a leg whose subject is an ABSENCE.
                wire.Pump();
                Check("a LETHAL hit put NO EnemyHitFlash on the wire (" + flashes.Count + ")",
                    flashes.Count == 0);
                Check("...and the UFO really is dead", victim.IsDead);

                // 5c. WHY THE PREDICATE IS `hitpoints > 0` AND NOT `(hitpoints <= 0) & !dead`.
                // A hit landing on something ALREADY dying must announce nothing either -- the
                // host draws no blink there (isBlinking carries the same hp term) and the live
                // case is SpiderHelperMothership, whose KilledBy flags `dying` without clearing
                // Collides, so the host really does keep hitting it for seconds. Reproduced here
                // on the cheapest entity that reaches the same state: the UFO is `dead` and its
                // removal is still QUEUED, so it is hittable for one more pass. This leg is what
                // separates the two predicates -- the `& !dead` form SENDS here.
                RunDownBlink(victim);
                flashes.Clear();
                victim.CollidesWith((ICollidable)shot);
                wire.Pump();
                Check("a hit on an ALREADY-DEAD entity announces nothing either ("
                    + flashes.Count + ")", flashes.Count == 0);

                bin.Update(); // the ComponentRemoved seam -> OnHostDeath -> the wire
                wire.Pump();
                // The kill itself still crossed. Without this, every "no flash" leg above is
                // satisfied by a hit that never landed at all.
                Check("...while the kill itself still announced an EvDeath (" + deaths.Count + ")",
                    deaths.Count == 1);
            }
            finally
            {
                peer.OnData -= Sniff;
                NetSession.Stop("netfx host section finished");
                bin.TopOfTickFlush();
                // The scripted peer's GRANTED SEAT is a live trace, not just an untidy one: this
                // is the only section here that hosts, so it is the only one that allocates a
                // roster slot for its peer, and Stop does not release it. Left behind it fails a
                // LATER suite in the same boot (measured: netrespawn's "the roster is empty again"
                // teardown leg). Released SEAT BY SEAT off the grant mask rather than with
                // Oracle.ResetPlayers(), which is a blanket wipe and would silently unseat a couch
                // player who was already sitting there before the run.
                bool seatsLeft = false;
                for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
                {
                    if ((grantedSlot & (1 << slot)) == 0)
                    {
                        continue;
                    }
                    NetHost.Current.Oracle.RemovePlayerAt(slot, ControlDevice.Remote);
                    seatsLeft |= NetHost.Current.Oracle.IsSeated(slot);
                }
                Check("the host session was stopped and its peer's granted seats released"
                    + " (mask " + grantedSlot + ", leave-no-trace)",
                    !NetSession.Active && !seatsLeft);
            }
        }

        // Tick the 35 ms hit blink out. FOUR hand-built 16.7 ms ticks (66.8 ms); THREE is the
        // minimum, because Timer only expires once its remaining time goes NEGATIVE -- two ticks
        // is 33.4 ms and leaves a 35 ms timer running.
        private static void RunDownBlink(AlienDrawableGameComponent victim)
        {
            for (int i = 0; i < 4; i++)
            {
                ((GameComponent)(object)victim).Update(new GameTime(TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(16.7)));
            }
        }

        private static void TrackPuppets(Game game, List<GameComponent> planted)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                // LazerGenerator too: section 4's charge glow is a CHILD the driver builds into
                // the live bin. Free() only self-removes on the child's next Update, a frame this
                // suite does not run -- so without tracking it, a run at the menu leaves one behind.
                if ((item is UFO || item is Ball || item is LazerGenerator) && !planted.Contains(item))
                {
                    planted.Add(item);
                }
            }
        }

        // Hand the world back exactly as it was found. The bin is the LIVE one, so a puppet left
        // behind would sit frozen at the main menu for the rest of the process -- and the next run
        // of this suite would then SKIP on its own leftovers rather than report a failure.
        private static void Teardown(StringBuilder sb, Action<string, bool> Check,
            Game game, ComponentBin bin, List<GameComponent> planted)
        {
            sb.Append(" 9. teardown\n");
            foreach (GameComponent comp in planted)
            {
                bin.Remove(comp);
            }
            bin.TopOfTickFlush();
            int left = 0;
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (planted.Contains(item))
                {
                    left++;
                }
            }
            Check("every entity this suite built is out of the world (" + planted.Count
                + " planted, " + left + " left)", left == 0);
            // The DEATH FX are not in `planted` -- nothing here builds them; section 5's UFO kill
            // does, through the real KilledBy. They self-clear once the world ticks, so this is a
            // transient rather than a leak, but the header promises leave-no-trace and the
            // NetDeathFxTest teardown sweeps the same types. Swept by TYPE for that reason.
            int swept = 0;
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is Explosion || item is SmokeDrawer)
                {
                    bin.Remove((GameComponent)item);
                    swept++;
                }
            }
            bin.TopOfTickFlush();
            int fxLeft = 0;
            foreach (IGameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is Explosion || item is SmokeDrawer)
                {
                    fxLeft++;
                }
            }
            Check("the death FX the suite's kills spawned are swept too (" + swept
                + " swept, " + fxLeft + " left)", fxLeft == 0);
            Check("no puppets are still registered (live=" + NetPuppets.LiveCount + ")",
                NetPuppets.LiveCount == 0);
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
            return string.Format(CultureInfo.InvariantCulture,
                "[netfx] {0} passed, {1} failed\n", pass, fail);
        }

        // The minimum INetScene: something non-null, so the client rx paths' "is a scene up" gate
        // opens. Nothing here is about what a scene DOES -- every beat this suite drives is
        // applied to an ENTITY, so a stand-in is honest (the NetScenarioTest scenario-5 argument).
        private sealed class FxScene : INetScene
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
