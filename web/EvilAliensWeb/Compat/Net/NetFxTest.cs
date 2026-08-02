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
        private static byte[] SnapshotFor(ushort id, byte typeIdx, in NetBaseState state,
            byte[] extras, int extrasLen)
        {
            byte[] scratch = new byte[NetProtocol.SnapshotHeaderBytes
                + NetProtocol.SnapshotEntryBaseBytes + extrasLen + 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            NetProtocol.WriteSnapshotEntry(scratch, ref off, id, typeIdx,
                NetProtocol.NetSnapshotFlags.None, state, extras, extrasLen);
            scratch[0] = NetProtocol.MsgWorldSnapshot;
            scratch[1] = 1;
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
