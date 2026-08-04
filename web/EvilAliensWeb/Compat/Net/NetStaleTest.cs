using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for card f5cf7a5c -- the world snapshot's STALENESS GUARD and the
    // NetBaseState.Scale precision raise that shipped with it. Invoke with eaNetStale() /
    // `eval NetStale`; MENU-runnable and leave-no-trace, the eaNetSnap / NetWallTest shape.
    //
    // WHY IT EXISTS. MsgWorldSnapshot rides the STREAM lane, which is unordered with
    // maxRetransmits:0, and carried no sequence and no timestamp -- so a reordered or late entry
    // handed NetPuppets.Drive an OLDER position than the one already on screen and the puppet
    // sagged BACKWARDS, then blended back over the correction window. It is the same defect
    // NetFrameLocal fixed for animation FRAMES; positions had no equivalent guard at all. And
    // nothing counted it: `pupPops` only moves on a correction past SnapThresholdPx, and a
    // reorder's error is far below that, so the metric read a contented 0 throughout.
    //
    // THE RIG IS NetWallTest'S, GENERALISED ONE LEVEL UP. That suite drives real
    // WriteBaseState/ReadBaseState frames into real puppets, which is exactly what section 1
    // needs -- but the seq lives in the packet HEADER, not in an entry, so section 2 has to run a
    // REAL CLIENT NetSession over a NetWire with a scripted host writing real MsgWorldSnapshot
    // packets (the NetScenarioTest scenario-5 shape). Sections 3-5 are about the guard's POLICY
    // and drive NetPuppets.OnSnapshotEntry directly with explicit seqs, which needs no session
    // and keeps them deterministic.
    internal static class NetStaleTest
    {
        private const string Room = "stale";

        // Far above any id a live session reaches (AllocId counts from 1), and disjoint from
        // NetWallTest's 603xx block so the two suites can run back to back.
        private const ushort IdSession = 60401;
        private const ushort IdOrder = 60402;
        private const ushort IdSagGuarded = 60403;
        private const ushort IdSagUnguarded = 60404;
        private const ushort IdSagControl = 60405;
        private const ushort IdWrap = 60406;
        private const ushort IdHeal = 60407;

        // EvilBullet: the simplest replicable -- no spawn extras, no state extras -- so nothing
        // in these sections is about a descriptor. Asserted against the live table rather than
        // trusted, since the wire typeIdx IS the registry order.
        private const byte TypeEvilBullet = 0;

        private const ulong PeerToken = 0xF5CFA5C0UL;

        private static readonly Vector2 Nowhere = new Vector2(-600f, -600f);

        // The Level-3 wall scroll at Very_Hard, in design px per ms -- the speed the card's ~12px
        // measurement was taken against, so the sag section reproduces its conditions rather than
        // inventing a shape.
        private const float ScrollPxPerMs = 0.31f;

        // The ceiling NetProtocol's u16-at-1/4096 scale can carry. Restated here rather than
        // exposed from NetProtocol: section 1's job is to CHECK the encoder, and reading the
        // bound out of the thing under test would make the check agree with whatever it became.
        private const float ScaleCeiling = 65535f / 4096f;

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

            sb.Append("[netstale] snapshot staleness guard + scale precision (card f5cf7a5c)\n");

            // The eaBinTest / eaNetSnap gate: this suite starts a REAL session and builds real
            // puppets into the live bin, so a live session, level or attract demo is a reason to
            // report a SKIP rather than let an unrun suite read as a pass.
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
                Check("registry index " + TypeEvilBullet + " is the EvilBullet descriptor",
                    NetTypeRegistry.Get(TypeEvilBullet) is Descriptors.EvilBulletDescriptor);

                sb.Append(" 1. the wire's SCALE precision, swept over the whole replicable set\n");
                SectionScaleSweep(bin, game, planted, sb, Check);

                sb.Append(" 2. the packet seq, end to end through a real client session\n");
                SectionSession(bin, game, planted, sb, Check);

                NetPuppets.Enable(game);

                sb.Append(" 3. reorder and late delivery -- the backward drag, measured\n");
                SectionOrdering(sb, Check);

                sb.Append(" 4. the u16 seq WRAPS rather than cliff-edging\n");
                SectionWrap(sb, Check);

                sb.Append(" 5. an id we do not hold is not judged stale\n");
                SectionSelfHeal(sb, Check);
            }
            finally
            {
                NetHost.Current = hostBefore;
                NetSession.Stop("netstale done");
                NetScene.Current = null;
                // THE PUPPETS HAVE TO GO BY HAND -- NetPuppets.Disable() clears the id maps but
                // does NOT remove the components the layer built, so without this every run
                // leaves its puppets in Game.Components, drawn and in the Oracle scans. Collected
                // BEFORE Disable, since FindPuppet reads the maps it clears. NetWallTest's shape.
                foreach (ushort id in new ushort[]
                {
                    IdSession, IdOrder, IdSagGuarded, IdSagUnguarded, IdSagControl, IdWrap, IdHeal,
                })
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

        // ---- 1. the scale sweep --------------------------------------------------------------
        //
        // THE CARD'S OTHER HALF, AND ITS EVIDENCE. It asked to WIDEN NetBaseState.Scale and to
        // identify who was left to benefit now that Wall.NetScaleLocal has opted the one known
        // victim out. This is that census, measured rather than argued: every replicable type is
        // CONSTRUCTED through its own descriptor's real CreatePuppet, its real `scale` is read
        // off the live object, and both the shipped quantum and the pre-card one are printed.
        //
        // The answer the sweep gives: nobody left DERIVES geometry from the replicated scale the
        // way a Wall does (CollisionLevelMap is the only scale-derived grid in the set, and Wall
        // owns it), and every other type's error is sub-pixel on a single sprite. So the card's
        // "widen" was superseded by a precision RAISE inside the existing u16 -- see
        // NetProtocol.ScaleScale for the trade.
        //
        // It also guards the ceiling. WriteBaseState CLAMPS, and a clamp is silent, so a future
        // type whose scale exceeds 15.999 would replicate at the wrong size with nothing said.
        //
        // KNOWN LIMIT, AND IT IS WHY THE VERDICT ABOVE RESTS ON MORE THAN THIS TABLE: what the
        // sweep reads is the scale a puppet is CONSTRUCTED at, off zero spawn extras. Types whose
        // scale is picked by Setup args or ANIMATED in Update therefore read their default here
        // and not their live extreme -- Braineroid's small size is 0.35 not the 1.0 printed,
        // Ball's is 0.45 x rand(0.42..0.85), PlasmaBall's entry telegraph shrinks to 0.025 and a
        // Parachute fades from 0.25 to 0. Those four were read out of the source when the card
        // was designed; at the pre-card quantum they were 0.67% / up to 2.1% / 6.25% / unbounded
        // out, and at the shipped one they are all under 0.05%. So the table is the SET and the
        // floor, not the worst case. A future type whose ANIMATED scale is what matters needs
        // reading here, not assuming.
        private static void SectionScaleSweep(ComponentBin bin, Game game,
            List<GameComponent> planted, StringBuilder sb, Action<string, bool> check)
        {
            NetBaseState blank = default(NetBaseState);
            blank.Pos = Nowhere;
            byte[] noExtras = new byte[1];

            int built = 0;
            float worstPct = 0f;
            float worstPrePct = 0f;
            float maxScale = 0f;
            string worstType = "none";
            bool ceilingHolds = true;

            for (byte i = 0; i < NetTypeRegistry.Count; i++)
            {
                INetTypeDescriptor desc = NetTypeRegistry.Get(i);
                AlienDrawableGameComponent c = null;
                try
                {
                    // Zero spawn extras is a LEGAL call -- it is exactly what the snapshot
                    // self-heal makes -- so a descriptor that needs them constructs on its own
                    // defaults rather than throwing. One (Ball, with no JunkBoss) returns null
                    // by design; that is a skip, not a failure.
                    c = desc.CreatePuppet(bin, game, blank, noExtras, 0, 0);
                }
                catch (Exception ex)
                {
                    sb.Append("       [" + i + "] " + desc.ComponentType.Name
                        + ": could not construct -- " + ex.GetType().Name + "\n");
                }
                if (c == null)
                {
                    continue;
                }
                planted.Add(c);
                built++;

                float exact = ((INetEntity)c).NetScale;
                float wire = ThroughWire(exact);
                float pre = PreCardWire(exact);
                float pct = exact > 0f ? Math.Abs(wire - exact) / exact * 100f : 0f;
                float prePct = exact > 0f ? Math.Abs(pre - exact) / exact * 100f : 0f;
                if (pct > worstPct)
                {
                    worstPct = pct;
                    worstType = desc.ComponentType.Name;
                }
                if (prePct > worstPrePct) { worstPrePct = prePct; }
                if (exact > maxScale) { maxScale = exact; }
                if (exact > ScaleCeiling) { ceilingHolds = false; }

                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "       [{0,2}] {1,-26} scale {2:F6} -> wire {3:F6} ({4:F3}%)"
                    + "  pre-card {5:F6} ({6:F2}%)\n",
                    i, desc.ComponentType.Name, exact, wire, pct, pre, prePct));
            }

            // A sweep that constructed nothing would pass every bound below, so its own
            // population is asserted first -- the eaNetPuppetBench rule.
            check("the sweep CONSTRUCTED most of the replicable set (" + built + " of "
                + NetTypeRegistry.Count + ")", built >= NetTypeRegistry.Count - 2);
            check("no replicable type's scale exceeds the u16 ceiling of "
                + ScaleCeiling.ToString("F3", CultureInfo.InvariantCulture)
                + " (max seen " + maxScale.ToString("F3", CultureInfo.InvariantCulture) + ")",
                ceilingHolds);
            // 0.2%, not a round 0.5%: TRUNCATING at the new quantum leaves the worst shipped
            // type (Wall, 0.0534) 0.37% out, so a looser bound would pass a build that had kept
            // the raise and dropped the rounding -- measured, by mutating exactly that.
            check("every type's scale survives the wire to within 0.2% (worst "
                + worstPct.ToString("F3", CultureInfo.InvariantCulture) + "% on " + worstType + ")",
                worstPct < 0.2f);

            // THE TWO PROPERTIES, ASSERTED DIRECTLY, because the aggregate above can only ever
            // say "small enough" and both halves of this change are specific claims.
            //
            // ROUNDING: a value six tenths of a quantum above a representable one must come back
            // as the NEXT quantum, not that one. A truncating cast fails this and nothing else
            // here; a bias that always shrinks is what accumulated 402px down a Level-3 grid.
            const float Quantum = 1f / 4096f;
            float justOver = 100f * Quantum + 0.6f * Quantum;
            check("the cast ROUNDS to the nearest quantum rather than truncating",
                Near(ThroughWire(justOver), 101f * Quantum, Quantum * 0.01f));
            // THE QUANTUM ITSELF: a value on the 1/4096 lattice but NOT on the 1/256 one must
            // round-trip exactly, which pins the raise independently of any error bound.
            check("the quantum really is 1/4096 (a value off the old 1/256 lattice is exact)",
                ThroughWire(101f * Quantum) == 101f * Quantum
                && PreCardWire(101f * Quantum) != 101f * Quantum);
            // The NEGATIVE CONTROL, and without it the line above means nothing: an encoder that
            // had not changed at all would still be under 0.5% for every type whose scale is
            // exactly 1.0, which is most of this table.
            check("...where the PRE-CARD u16-at-1/256 was materially worse on at least one type"
                + " (worst " + worstPrePct.ToString("F2", CultureInfo.InvariantCulture) + "%)",
                worstPrePct > worstPct * 4f && worstPrePct > 0.5f);
        }

        // ---- 2. the packet seq, end to end ---------------------------------------------------
        //
        // The guard's INPUT is the MsgWorldSnapshot header, so this is the only section that can
        // fail on the wiring rather than the policy: a real CLIENT NetSession, a scripted host on
        // the other endpoint of a NetWire, and real packets built by the production encoder. A
        // seq read from the wrong offset, or a header the sender forgot to stamp, shows up here
        // and nowhere else.
        private static void SectionSession(ComponentBin bin, Game game,
            List<GameComponent> planted, StringBuilder sb, Action<string, bool> check)
        {
            NetWire wire = new NetWire(2);
            InMemoryTransport ours = wire[0];
            InMemoryTransport peer = wire[1];
            ushort eventSeq = 1;

            // The client rx paths gate on "is a scene up", which the seam answers -- so this
            // needs the SEAM, not a GameScene. Nothing here is about what a scene DOES.
            NetScene.Current = new StaleScene();
            NetHost.Current = new PinnedNetHost();

            NetSession.StartForTest(game, host: false, ours, Room);
            peer.Open(Room);
            peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, true,
                NetSession.LocalBuildHash, 0, 1, PeerToken, 0));
            wire.Pump();
            NetSession.Update();
            check("session started as a CLIENT and paired", NetSession.IsClient && NetSession.PeerUp);

            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            byte[] noExtras = new byte[1];
            peer.SendReliable(NetProtocol.EncodeSpawnEvent(eventSeq++, IdSession, TypeEvilBullet,
                state, noExtras, 0));
            wire.Pump();
            NetSession.Update();
            INetEntity puppet = NetPuppets.FindPuppet(IdSession);
            check("the scripted host's EvSpawn built a puppet", puppet != null);
            if (puppet == null)
            {
                return;
            }
            planted.Add((GameComponent)(object)puppet);

            NetMetrics m = NetSession.Metrics;
            long staleBefore = m.SnapStale;
            long entriesBefore = m.SnapEntriesRx;
            long unknownBefore = m.SnapUnknownIds;

            // IN ORDER first -- the positive control. Without it a guard that refused everything
            // would satisfy the reorder leg below and look like a fix.
            Vector2 aheadOne = Nowhere + new Vector2(0f, 30f);
            Vector2 aheadTwo = Nowhere + new Vector2(0f, 60f);
            state.Pos = aheadOne;
            peer.SendStream(SnapshotPacket(500, IdSession, state));
            state.Pos = aheadTwo;
            peer.SendStream(SnapshotPacket(501, IdSession, state));
            wire.Pump();
            NetSession.Update();
            Settle();
            check("two IN-ORDER packets both apply (snapStale unmoved at " + m.SnapStale + ")",
                m.SnapStale == staleBefore);
            check("...and the puppet is on the NEWER of the two",
                Near(puppet.Position.Y, aheadTwo.Y, 1f));
            check("...with both entries really delivered (snapEnt +"
                + (m.SnapEntriesRx - entriesBefore) + ") -- the rig's own positive control",
                m.SnapEntriesRx - entriesBefore == 2);

            // REORDERED: the newer packet is sent first, so the older one arrives second. That is
            // what the stream lane does; the entry decodes perfectly and names a puppet we hold.
            Vector2 newest = Nowhere + new Vector2(0f, 120f);
            Vector2 older = Nowhere + new Vector2(0f, 90f);
            state.Pos = newest;
            peer.SendStream(SnapshotPacket(511, IdSession, state));
            state.Pos = older;
            peer.SendStream(SnapshotPacket(510, IdSession, state));
            wire.Pump();
            NetSession.Update();
            Settle();
            check("a REORDERED packet is refused (snapStale +" + (m.SnapStale - staleBefore) + ")",
                m.SnapStale == staleBefore + 1);
            check("...and the puppet stayed on the NEWER position rather than sagging back",
                Near(puppet.Position.Y, newest.Y, 1f));
            // The stale entry must not be laundered through the unknown-id counters either -- it
            // is not an unknown id, and folding it in would make snapUnk unreadable again (the
            // card 48ab9b2f lesson).
            check("...and it did NOT count as an unknown id (snapUnk " + m.SnapUnknownIds + ")",
                m.SnapUnknownIds == unknownBefore);
            check("...and the packet header really carried a seq the sender stamped",
                NetProtocol.TryReadSnapshotHeader(SnapshotPacket(1234, IdSession, state),
                    out _, out ushort roundSeq) && roundSeq == 1234);

            NetSession.Stop("netstale section 2 done");
            NetScene.Current = null;
        }

        // ---- 3. reorder, late delivery and the SAG -------------------------------------------
        //
        // The card's backward-drag defect, reproduced as a measurement. **The MAGNITUDE here is
        // not the card's ~12px** and is not meant to be: that figure was a Level-3 wall at the
        // user's own ?netlag=120 / jitter-40 rig, where the reorder window decides how far back
        // the superseded sample points. Here the displacement is chosen (40px) and what the
        // section asserts is the SHAPE -- that a superseded entry moves the puppet at all, and
        // that the guard removes exactly that motion. Three runs of the IDENTICAL frame sequence
        // on three fresh puppets:
        //
        //   control  -- the late entry is never delivered at all
        //   guarded  -- it is delivered and the guard refuses it
        //   unguarded-- it is delivered with ?netstaleguard=0's behaviour, i.e. pre-card
        //
        // The guarded run must land exactly where the control did; the unguarded one must land
        // BEHIND both, and by how much is the drag. Two assertions rather than one, because
        // "the guarded run did not sag" passes on a build where the late entry never arrived.
        private static void SectionOrdering(StringBuilder sb, Action<string, bool> check)
        {
            float control = DriveLateEntry(IdSagControl, deliverLate: false, guard: true);
            float guarded = DriveLateEntry(IdSagGuarded, deliverLate: true, guard: true);
            float unguarded = DriveLateEntry(IdSagUnguarded, deliverLate: true, guard: false);

            float sag = control - unguarded;
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "       final Y: control {0:F2}, guarded {1:F2}, unguarded {2:F2}"
                + " -> {3:F2}px of backward drag with the guard off\n",
                control, guarded, unguarded, sag));

            check("the guarded run lands EXACTLY where the un-delivered control did",
                Near(guarded, control, 0.01f));
            check("...while ?netstaleguard=0 drags the puppet backwards ("
                + sag.ToString("F2", CultureInfo.InvariantCulture) + "px)", sag > 1f);
        }

        // One run of the late-delivery scenario. A puppet scrolling at the Level-3 wall speed is
        // corrected at seq 5 and seq 19, and a seq-12 entry -- perfectly valid, simply superseded
        // -- arrives afterwards. `guard` is the ?netstaleguard=0 A/B, driven through the injected
        // host rather than a reboot.
        private static float DriveLateEntry(ushort id, bool deliverLate, bool guard)
        {
            INetHost before = NetHost.Current;
            PinnedNetHost host = new PinnedNetHost { StaleGuard = guard };
            NetHost.Current = host;
            try
            {
                NetBaseState state = default(NetBaseState);
                state.Pos = Nowhere;
                state.Vel = new Vector2(0f, ScrollPxPerMs);
                state.Scale = 1f;
                byte[] noExtras = new byte[1];
                if (NetPuppets.OnSpawn(id, TypeEvilBullet, state, noExtras, 0, 0)
                    != SpawnRejectKind.None)
                {
                    return float.NaN;
                }

                // An entity's snapshot TURNS, not consecutive packets: the round robin gives it
                // one every `live/16` packets, which is why a late entry can be many seqs behind
                // the newest without being ancient.
                state.Pos = Nowhere + new Vector2(0f, 40f);
                Apply(id, 5, state);
                Settle();
                state.Pos = Nowhere + new Vector2(0f, 120f);
                Apply(id, 19, state);
                if (deliverLate)
                {
                    // The superseded sample. Its position is where the entity WAS a couple of
                    // turns ago -- which is the whole defect: applied, it walks the puppet back
                    // there and the correction window then blends it forward again.
                    state.Pos = Nowhere + new Vector2(0f, 80f);
                    Apply(id, 12, state);
                }
                Settle();
                INetEntity puppet = NetPuppets.FindPuppet(id);
                return puppet == null ? float.NaN : puppet.Position.Y;
            }
            finally
            {
                NetHost.Current = before;
            }
        }

        // ---- 4. the wrap ---------------------------------------------------------------------
        //
        // The counter is a u16 and a busy host wraps it about every 65 minutes at 16.7 Hz, so the
        // comparison is on the SIGNED difference. A naive `seq > last` would refuse every entry
        // for a whole session from the moment it rolled over -- silently, since the puppets would
        // simply stop being corrected and dead-reckon on forever.
        private static void SectionWrap(StringBuilder sb, Action<string, bool> check)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            byte[] noExtras = new byte[1];
            if (NetPuppets.OnSpawn(IdWrap, TypeEvilBullet, state, noExtras, 0, 0)
                != SpawnRejectKind.None)
            {
                check("a puppet was built for the wrap legs", false);
                return;
            }
            INetEntity puppet = NetPuppets.FindPuppet(IdWrap);
            if (puppet == null)
            {
                check("a puppet was built for the wrap legs", false);
                return;
            }

            state.Pos = Nowhere + new Vector2(0f, 30f);
            Apply(IdWrap, 65534, state);
            Settle();

            Vector2 across = Nowhere + new Vector2(0f, 70f);
            state.Pos = across;
            bool applied = Apply(IdWrap, 1, state);
            Settle();
            check("a seq that WRAPPED past 65535 is newer, not older", applied
                && Near(puppet.Position.Y, across.Y, 1f));

            state.Pos = Nowhere + new Vector2(0f, 20f);
            bool refused = !Apply(IdWrap, 65534, state);
            Settle();
            check("...and the pre-wrap seq is still refused afterwards", refused
                && Near(puppet.Position.Y, across.Y, 1f));
        }

        // ---- 5. an id we do not hold ---------------------------------------------------------
        //
        // The guard is keyed per netId and can only speak for a puppet it already holds. An entry
        // for an UNKNOWN id must still reach the self-heal whatever its seq -- there is no
        // last-applied to compare against, and refusing it would strand a puppet the host has and
        // we do not, permanently (the id would keep arriving and keep being refused).
        //
        // The second half is the rebuild: a fresh PuppetInfo starts with no seq at all, so its
        // first entry is always accepted. A rebuilt puppet inheriting the old one's high-water
        // mark would refuse every correction the host sent it.
        private static void SectionSelfHeal(StringBuilder sb, Action<string, bool> check)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = Nowhere;
            state.Scale = 1f;
            byte[] noExtras = new byte[1];

            // A LOW seq on an id nothing has ever seen. If the guard were global rather than per
            // netId, the traffic in sections 3-4 would already have moved a shared high-water
            // mark well past this and the self-heal would never fire.
            bool applied = NetPuppets.OnSnapshotEntry(IdHeal, TypeEvilBullet,
                NetProtocol.NetSnapshotFlags.None, state, noExtras, 0, 0, 3,
                out _, out SnapUnknownKind kind, out bool stale);
            check("an unknown id is REBUILT rather than judged stale (kind=" + kind + ")",
                !applied && !stale && kind == SnapUnknownKind.Rebuilt);
            INetEntity puppet = NetPuppets.FindPuppet(IdHeal);
            check("...and the self-heal really built it", puppet != null);
            if (puppet == null)
            {
                return;
            }

            // Its FIRST correction is accepted whatever the seq -- here a seq LOWER than the one
            // that rebuilt it, which is the shape a reordered pair would produce.
            Vector2 target = Nowhere + new Vector2(0f, 45f);
            state.Pos = target;
            bool first = Apply(IdHeal, 2, state);
            Settle();
            check("a freshly rebuilt puppet accepts its first entry whatever the seq",
                first && Near(puppet.Position.Y, target.Y, 1f));
            // ...and the guard arms from there, so the same seq a second time is refused.
            state.Pos = Nowhere;
            bool second = Apply(IdHeal, 2, state);
            Settle();
            check("...and the guard arms from that entry (a repeat of the same seq is refused)",
                !second && Near(puppet.Position.Y, target.Y, 1f));
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static bool Apply(ushort id, ushort seq, in NetBaseState state)
        {
            return NetPuppets.OnSnapshotEntry(id, TypeEvilBullet,
                NetProtocol.NetSnapshotFlags.None, state, new byte[1], 0, 0, seq,
                out _, out _, out _);
        }

        // Drive well past the correction window's time constant. The layer BLENDS an ordinary
        // error rather than assigning it, so a short drive would leave every position assertion
        // reading a partly-applied correction and passing or failing on the drive length.
        private static void Settle()
        {
            for (int i = 0; i < 60; i++)
            {
                NetPuppets.Drive(16.7f);
            }
        }

        private static byte[] SnapshotPacket(ushort seq, ushort id, in NetBaseState state)
        {
            byte[] scratch = new byte[NetProtocol.SnapshotHeaderBytes
                + NetProtocol.SnapshotEntryBaseBytes + 1];
            int off = NetProtocol.SnapshotHeaderBytes;
            NetProtocol.WriteSnapshotEntry(scratch, ref off, id, TypeEvilBullet,
                NetProtocol.NetSnapshotFlags.None, state, new byte[1], 0);
            NetProtocol.WriteSnapshotHeader(scratch, 1, seq);
            byte[] packet = new byte[off];
            Array.Copy(scratch, packet, off);
            return packet;
        }

        private static float ThroughWire(float scale)
        {
            NetBaseState s = default(NetBaseState);
            s.Scale = scale;
            byte[] buf = new byte[NetProtocol.BaseStateBytes];
            int off = 0;
            NetProtocol.WriteBaseState(buf, ref off, s);
            NetBaseState back = default(NetBaseState);
            off = 0;
            NetProtocol.ReadBaseState(buf, ref off, ref back);
            return back.Scale;
        }

        // The PRE-f5cf7a5c scale codec, transcribed verbatim: a u16 at 1/256 with a TRUNCATING
        // cast. A reference implementation, the eaNetScore.test / eaNetFire idiom -- it is what
        // makes section 1's improvement claim mean something, and it cannot be measured off the
        // live encoder any more. NetWallTest holds its own copy for the same reason.
        private static float PreCardWire(float scale)
        {
            return (ushort)Math.Clamp(scale * 256f, 0f, 65535f) / 256f;
        }

        private static bool Near(float a, float b, float tol)
        {
            return Math.Abs(a - b) < tol;
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[netstale] {0} passed, {1} failed\n", pass, fail);
        }

        // Section 2 needs "a scene is up" and nothing else -- the client rx paths gate on it.
        // A recording stand-in would be dishonest in NetSceneOrderTest and is honest here for
        // the opposite reason: nothing in this suite is about what a scene DOES.
        private sealed class StaleScene : INetScene
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
