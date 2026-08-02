using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // THE TELEPORT MARKER (card e79bb994, replacing card 8dabe812's observed-velocity cap).
    // Run `eaNetTeleport()` from the MAIN MENU, or `eval NetTeleport` under eahl. A leg of
    // tools/headless/probes/net_selftests.txt.
    //
    // WHAT IS BEING FIXED. The host stamps each replicated entity's wire velocity as a finite
    // difference between its snapshot turns, because half the replicable set writes Position
    // directly and Speed/Direction reads zero for those. That estimator cannot tell motion from a
    // REPOSITION -- and the SpiderBoss is parked at the far screen edge to start each fly-by, so
    // differentiating an ~800px jump put 42-57 px/ms on the wire and the joiner's puppet flew
    // across the screen on it, collidably, killing the local player. Card 8dabe812 refused any
    // sample above 5.0 px/ms, which worked but was an estimator with a threshold; the host KNOWS
    // when it teleports something, so it now says so (NetNoteTeleport -> a per-sample flags byte).
    //
    // TWO HALVES, AND EACH HAS ITS OWN NEGATIVE. Section 1 runs a real HOST session over a NetWire
    // and reads the frames the peer RECEIVED -- the wire is the only place the host's decision is
    // observable, since a refused sample looks exactly like an entity standing still. Section 2
    // runs a real CLIENT session and asserts what a marked entry does to a live puppet.
    //
    // IN BOTH, THE PRE-CARD PATH RUNS BESIDE IT OVER THE SAME INPUT (the eaNetScore.test rule).
    // That matters more than usual here: "the entity ended up in the right place" is true of the
    // broken code too -- the position was always snapped, it was the VELOCITY that poisoned the
    // dead reckoning, and the sub-threshold case was silently BLENDED. So every positive leg is
    // paired with the identical jump left UNMARKED, which must still produce the old behaviour.
    //
    // MENU-RUNNABLE AND LEAVE-NO-TRACE (the eaNetFx / eaNetScenarios shape, not eaNetResetSpawn's):
    // it needs no GameScene -- the rx paths gate on the INetScene SEAM, which a stand-in satisfies
    // -- everything it plants sits far off-screen, and the finally takes it all back out.
    internal static class NetTeleportTest
    {
        private const string Room = "nettp";

        private const byte PeerSlot = 1;

        private const ulong PeerToken = 0x7E1E9012UL;

        // Off-screen, so nothing this suite builds can be seen for the frame it exists.
        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // Comfortably past the puppet layer's SnapThresholdPx (100), i.e. the case the pre-card
        // code did snap -- and the one whose velocity it then dead-reckoned on.
        private const float BigJumpPx = 800f;

        // Comfortably UNDER it. This is the case the marker newly fixes: EvilSkull respawns at a
        // random point, so plenty of its repositions are short, and a short one was BLENDED --
        // the skull slid across the screen on the joiner instead of reappearing.
        private const float SmallJumpPx = 50f;

        // A distinctive declared speed, so "fell back to NetSpeedVector" cannot be confused with
        // "happened to observe about the same thing".
        private static readonly Vector2 Declared = new Vector2(0.125f, -0.0625f);

        private const ushort IdBlendCtl = 9400;
        private const ushort IdSmallMarked = 9401;
        private const ushort IdBigMarked = 9402;
        private const ushort IdBigUnmarked = 9403;

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

            sb.Append("[nettp] teleport marker on the wire\n");

            // Same gate as eaNetFx / eaNetScenarios: this starts a REAL session and adds real
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
                RunHostSide(sb, Check, bin, game, planted, clock);
                NetSession.Stop("nettp host section done");
                RunClientSide(sb, Check, bin, game, planted, clock);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + Describe(ex) + ")", ok: false);
            }
            finally
            {
                NetSession.Stop("nettp suite teardown");
                Teardown(sb, Check, game, bin, planted);
                NetHost.Current = hostBefore;
                NetScene.Current = null;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
                Check("the scene seam is handed back", !NetScene.IsOverridden);
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- section 1: the HOST's decision, read off the wire --------------------------------

        private static void RunHostSide(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted, PinnedNetHost clock)
        {
            sb.Append(" 1. the host marks a reposition and refuses to differentiate it\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];

            // Sniff the SNAPSHOT frames the peer receives. The host's decision leaves no other
            // trace -- a refused sample and an entity standing still are byte-identical anywhere
            // else -- so the wire is the observable.
            List<byte[]> snaps = new List<byte[]>();
            void Sniff(byte[] payload, bool reliable, string from)
            {
                if (payload.Length >= NetProtocol.SnapshotHeaderBytes
                    && payload[0] == NetProtocol.MsgWorldSnapshot)
                {
                    snaps.Add(payload);
                }
            }

            NetScene.Current = new TeleportScene();
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

            // A real replicable entity in the LIVE bin, so NetIdRegistry allocates it a netId
            // through the real ComponentAdded seam -- the same path production takes.
            UFO ufo = UFO.NewUFO(bin, game);
            ufo.Setup(Nowhere, isBig: false, EnemyBehaviour.normal);
            bin.Add((GameComponent)(object)ufo);
            planted.Add((GameComponent)(object)ufo);
            bin.TopOfTickFlush();
            bool gotId = NetIdRegistry.TryGetByComp((GameComponent)(object)ufo,
                out NetIdRegistry.Entry entry);
            Check("PRECONDITION the planted UFO got a netId", gotId);
            if (!gotId)
            {
                return;
            }
            ushort netId = entry.Id;
            ufo.NetSpeedVector = Declared;

            // Turn 1 establishes the baseline (HasLastPos was false), so nothing here is a claim
            // about the marker yet -- it is what makes turns 2..5 differences rather than firsts.
            Snapshot(clock, wire, snaps);

            // 1a. ORDINARY MOTION, the control for everything below: the observed velocity is
            // what goes out, and no flag is set. Without this leg a build that marked EVERY
            // sample -- i.e. that never differentiated at all -- would pass the whole section.
            ufo.Position = Nowhere + new Vector2(12f, 0f);
            ufo.NetSpeedVector = Declared;
            Snapshot(clock, wire, snaps);
            bool gotMove = LastEntry(snaps, netId, out byte moveFlags, out Vector2 moveVel);
            Check("ordinary motion goes out UNMARKED", gotMove
                && (moveFlags & NetProtocol.NetSnapshotFlags.Teleported) == 0);
            Check("...carrying the OBSERVED velocity, not the declared one (obs "
                + Fmt(moveVel) + " vs decl " + Fmt(Declared) + ")",
                gotMove && moveVel.X > 0.15f && !Near(moveVel, Declared));

            // 1b. THE MARKED REPOSITION. Same shape of write, one extra call.
            ufo.Position = ufo.Position + new Vector2(BigJumpPx, 0f);
            ufo.NetNoteTeleport();
            Snapshot(clock, wire, snaps);
            bool gotTp = LastEntry(snaps, netId, out byte tpFlags, out Vector2 tpVel);
            Check("a MARKED reposition goes out with the teleport flag set", gotTp
                && (tpFlags & NetProtocol.NetSnapshotFlags.Teleported) != 0);
            Check("...carrying the DECLARED velocity, not the jump's finite difference ("
                + Fmt(tpVel) + ")", gotTp && Near(tpVel, Declared));

            // 1c. THE NEGATIVE CONTROL AND THE POINT OF THE CARD -- the identical jump with the
            // marker left off. It must still produce the pre-card wire: no flag, and a velocity
            // an order of magnitude past anything in the game. If this leg ever goes quiet, the
            // suite has stopped discriminating and 1b means nothing.
            long unmarkedBefore = NetSession.Metrics.UnmarkedTeleports;
            ufo.Position = ufo.Position + new Vector2(BigJumpPx, 0f);
            Snapshot(clock, wire, snaps);
            bool gotRaw = LastEntry(snaps, netId, out byte rawFlags, out Vector2 rawVel);
            Check("CONTROL an UNMARKED jump is not flagged", gotRaw
                && (rawFlags & NetProtocol.NetSnapshotFlags.Teleported) == 0);
            Check("CONTROL ...and still poisons the wire with the jump's speed ("
                + rawVel.Length().ToString("0.0", CultureInfo.InvariantCulture) + " px/ms)",
                gotRaw && rawVel.Length() > NetSession.MaxObservedSpeedPxPerMs);
            Check("...which the unmarked-teleport diagnostic counts (+"
                + (NetSession.Metrics.UnmarkedTeleports - unmarkedBefore) + ")",
                NetSession.Metrics.UnmarkedTeleports == unmarkedBefore + 1);

            // 1d. THE LATCH IS SPENT, NOT STICKY. A marker that survived its turn would refuse
            // every following turn's velocity too, freezing the puppet's dead reckoning -- which
            // is a worse bug than the one being fixed, and invisible on the wire without this.
            ufo.NetNoteTeleport();
            ufo.Position = ufo.Position + new Vector2(BigJumpPx, 0f);
            Snapshot(clock, wire, snaps);
            ufo.Position = ufo.Position + new Vector2(12f, 0f);
            Snapshot(clock, wire, snaps);
            bool gotAfter = LastEntry(snaps, netId, out byte afterFlags, out Vector2 afterVel);
            Check("the marker is SPENT: the turn after a teleport is unflagged again", gotAfter
                && (afterFlags & NetProtocol.NetSnapshotFlags.Teleported) == 0);
            Check("...and differentiates normally again (" + Fmt(afterVel) + ")",
                gotAfter && afterVel.X > 0.15f && !Near(afterVel, Declared));

            peer.OnData -= Sniff;
        }

        // ---- section 2: what a marked entry does to a live puppet ------------------------------

        private static void RunClientSide(StringBuilder sb, Action<string, bool> Check,
            ComponentBin bin, Game game, List<GameComponent> planted, PinnedNetHost clock)
        {
            sb.Append(" 2. the client SNAPS a marked entry instead of blending it\n");

            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            NetScene.Current = new TeleportScene();
            NetSession.StartForTest(game, host: false, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                NetSession.LocalBuildHash, 0, PeerSlot, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            Check("PRECONDITION session started as a CLIENT and paired",
                NetSession.IsClient && NetSession.PeerUp);
            if (!NetSession.IsClient || !NetSession.PeerUp)
            {
                return;
            }

            // The typeIdx is LOOKED UP through the real registry rather than written down: the
            // wire typeIdx IS the registry order, so a literal would silently spawn some other
            // enemy the day a descriptor is appended ahead of this one.
            UFO probe = UFO.NewUFO(bin, game);
            bool haveIdx = NetTypeRegistry.TryGet((GameComponent)(object)probe, out byte ufoIdx, out _);
            Check("PRECONDITION UFO is a replicable type (registry idx " + ufoIdx + ")", haveIdx);

            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            state.Hp = 10;
            byte[] noExtras = new byte[2];

            // Four puppets, one per leg -- a blended leg leaves a correction on its puppet, and
            // reusing it would make the next leg's reading a function of the previous one.
            ushort[] ids = { IdBlendCtl, IdSmallMarked, IdBigMarked, IdBigUnmarked };
            foreach (ushort id in ids)
            {
                peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, id, ufoIdx, state, noExtras, 0));
            }
            wire.Pump();
            NetSession.Update();
            TrackPuppets(game, planted);
            bool built = true;
            foreach (ushort id in ids)
            {
                built &= NetPuppets.FindPuppet(id) != null;
            }
            Check("the scripted host's EvSpawns built all four puppets", built);
            if (!built)
            {
                return;
            }

            // A plain snapshot first: the FIRST entry after a spawn hard-writes the position
            // whatever the flags say (`!info.HasSnapshot`), so without this every leg below would
            // be reading that branch instead of the one under test.
            foreach (ushort id in ids)
            {
                peer.SendStream(SnapshotFor(id, ufoIdx, NetProtocol.NetSnapshotFlags.None, state));
            }
            wire.Pump();
            NetSession.Update();

            // 2a. THE PRE-CARD BEHAVIOUR, as the control: an UNMARKED sub-threshold move is an
            // error to be blended, so ApplySnapshotState stores it and does NOT write Position.
            // This is what a short reposition used to get -- the entity SLID.
            NetBaseState small = state;
            small.Pos = Nowhere + new Vector2(SmallJumpPx, 0f);
            Vector2 beforeBlend = NetPuppets.FindPuppet(IdBlendCtl).Position;
            peer.SendStream(SnapshotFor(IdBlendCtl, ufoIdx, NetProtocol.NetSnapshotFlags.None, small));
            wire.Pump();
            NetSession.Update();
            Check("CONTROL an unmarked sub-threshold move is BLENDED (position unchanged)",
                Near(NetPuppets.FindPuppet(IdBlendCtl).Position, beforeBlend));

            // 2b. The same jump, MARKED. THE CASE THE OLD CAP COULD NOT REACH AT ALL: it lived on
            // the host and only ever refused a velocity, so a jump under SnapThresholdPx was
            // blended exactly as above however implausible its speed.
            peer.SendStream(SnapshotFor(IdSmallMarked, ufoIdx, NetProtocol.NetSnapshotFlags.Teleported, small));
            wire.Pump();
            NetSession.Update();
            Check("a MARKED sub-threshold reposition snaps immediately",
                Near(NetPuppets.FindPuppet(IdSmallMarked).Position, small.Pos));

            // 2c. A marked jump PAST the threshold snaps -- and does NOT count a pop. `pupPops`
            // means "an error the layer could not account for"; a reposition the host announced is
            // accounted for, and every SpiderBoss fly-by used to inflate that counter.
            NetBaseState big = state;
            big.Pos = Nowhere + new Vector2(BigJumpPx, 0f);
            long popsBefore = NetSession.Metrics.PuppetPops;
            peer.SendStream(SnapshotFor(IdBigMarked, ufoIdx, NetProtocol.NetSnapshotFlags.Teleported, big));
            wire.Pump();
            NetSession.Update();
            Check("a MARKED over-threshold reposition snaps",
                Near(NetPuppets.FindPuppet(IdBigMarked).Position, big.Pos));
            Check("...and is NOT counted as a puppet pop (+"
                + (NetSession.Metrics.PuppetPops - popsBefore) + ")",
                NetSession.Metrics.PuppetPops == popsBefore);

            // 2d. The control that makes 2c's zero mean something: the identical jump unmarked
            // still snaps AND still pops, so the counter is demonstrably live in this rig.
            long popsBefore2 = NetSession.Metrics.PuppetPops;
            peer.SendStream(SnapshotFor(IdBigUnmarked, ufoIdx, NetProtocol.NetSnapshotFlags.None, big));
            wire.Pump();
            NetSession.Update();
            Check("CONTROL the same jump UNMARKED still snaps",
                Near(NetPuppets.FindPuppet(IdBigUnmarked).Position, big.Pos));
            Check("CONTROL ...and DOES count a pop (+"
                + (NetSession.Metrics.PuppetPops - popsBefore2) + ")",
                NetSession.Metrics.PuppetPops == popsBefore2 + 1);

            // 2e. An unknown flag BIT must not stop the known one being read. NetSnapshotFlags is
            // a bitmask, so a future build appending a flag must not make this build ignore the
            // teleport it is also carrying.
            NetBaseState big2 = state;
            big2.Pos = Nowhere + new Vector2(BigJumpPx, 40f);
            peer.SendStream(SnapshotFor(IdBigMarked, ufoIdx,
                (byte)(NetProtocol.NetSnapshotFlags.Teleported | 0x80), big2));
            wire.Pump();
            NetSession.Update();
            Check("an entry carrying an UNKNOWN flag bit still honours the teleport bit",
                Near(NetPuppets.FindPuppet(IdBigMarked).Position, big2.Pos));
        }

        // ---- helpers ---------------------------------------------------------------------------

        // Advance past the snapshot cadence and let one packet out. The clock is the pinned one,
        // so this is exact rather than a sleep.
        private static void Snapshot(PinnedNetHost clock, NetWire wire, List<byte[]> snaps)
        {
            snaps.Clear();
            clock.Advance(NetSession.SnapshotIntervalMs + 1);
            NetSession.Update();
            wire.Pump();
        }

        // The entry for `netId` in the most recent snapshot packet, decoded with the real reader.
        private static bool LastEntry(List<byte[]> snaps, ushort netId, out byte flags, out Vector2 vel)
        {
            flags = 0;
            vel = Vector2.Zero;
            if (snaps.Count == 0)
            {
                return false;
            }
            byte[] packet = snaps[snaps.Count - 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            for (int i = 0; i < packet[1]; i++)
            {
                if (!NetProtocol.TryReadSnapshotEntry(packet, ref off, out ushort id, out _,
                    out byte f, out NetBaseState st, out _, out _))
                {
                    return false;
                }
                if (id == netId)
                {
                    flags = f;
                    vel = st.Vel;
                    return true;
                }
            }
            return false;
        }

        // One world-snapshot packet carrying a single entry, built with the real WriteSnapshotEntry
        // so an entry-layout change moves this with it rather than silently passing.
        private static byte[] SnapshotFor(ushort id, byte typeIdx, byte flags, in NetBaseState state)
        {
            byte[] scratch = new byte[NetProtocol.SnapshotHeaderBytes
                + NetProtocol.SnapshotEntryBaseBytes + 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            NetProtocol.WriteSnapshotEntry(scratch, ref off, id, typeIdx, flags, state, null, 0);
            scratch[0] = NetProtocol.MsgWorldSnapshot;
            scratch[1] = 1;
            byte[] packet = new byte[off];
            Array.Copy(scratch, packet, off);
            return packet;
        }

        private static void TrackPuppets(Game game, List<GameComponent> planted)
        {
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is UFO && !planted.Contains(item))
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
            sb.Append(" 3. teardown\n");
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

        private static bool Near(Vector2 a, Vector2 b)
        {
            return Math.Abs(a.X - b.X) < 0.01f && Math.Abs(a.Y - b.Y) < 0.01f;
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
            return string.Format(CultureInfo.InvariantCulture,
                "[nettp] {0} passed, {1} failed\n", pass, fail);
        }

        // The minimum INetScene: something non-null, so the rx paths' "is a scene up" gate opens.
        // Nothing here is about what a scene DOES -- the whole subject is one entity's base state
        // -- so a stand-in is honest (the NetScenarioTest scenario-5 argument).
        private sealed class TeleportScene : INetScene
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
