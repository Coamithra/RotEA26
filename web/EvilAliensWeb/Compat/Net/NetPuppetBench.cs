using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // PINNED MANY-PUPPET BENCH for the client-side puppet drive (card 25ad0659 step 2c-ii).
    // Invoke with eaNetPuppetBench(n, iters) / `eval NetPuppetBench <n> <iters>` -- best from
    // the main menu, and best HEADLESSLY (eahl), where no rAF paces the loop.
    //
    // WHY IT EXISTS. Step 2c-ii puts an interface in front of the replicable entity, and the
    // plan says to MEASURE before choosing the representation. Nothing in the repo could take
    // that measurement: there was no way to put a chosen number of puppets in the world and
    // read what driving them costs.
    //
    // AND THE PLAN NAMES THE WRONG INSTRUMENT, which is why this is a bench rather than a
    // FrameProfiler reading. plans/net-headless-sim.md says to read `FrameSection.UpdNet`,
    // but that bracket (Game1.UpdateInner) covers NetSession.Update + NetListing.Tick only.
    // NetPuppets.Drive is called from NetPuppetDriver.Update, i.e. from inside
    // base.Update(gameTime) -- so it lands in `UpdComponents`, buried under every other
    // component in the world, where a delta of a few percent is unreadable. UpdNet meanwhile
    // sees the HOST's <=16-entry snapshot encode at ~16 Hz, a tiny phase: judging this change
    // as a percentage of it is precisely the "10% of 0.3 ms is nothing" trap the plan warns
    // about two sentences later.
    //
    // So this times the real NetPuppets.Drive in a plain loop and reports ABSOLUTE
    // microseconds -- per Drive call and per puppet -- plus what that is as a share of the
    // 16.7 ms frame budget at 60 Hz. That number is comparable across builds and does not
    // depend on what else is on screen.
    //
    // Leave-no-trace, like the sibling suites: every puppet it builds is taken back out of
    // the world and Disable() clears the id maps, so back-to-back runs read identically.
    internal static class NetPuppetBench
    {
        // Far above any id a live session realistically reaches (AllocId counts from 1), so a
        // run can never collide with a real entry. 512 ids of headroom above it.
        private const ushort IdBase = 61000;
        private const int MaxPuppets = 512;

        // The 60 Hz frame budget, for the only comparison the plan says is meaningful.
        private const float FrameBudgetMs = 16.7f;

        public static string Run(int n, int iters)
        {
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            StringBuilder sb = new StringBuilder();
            sb.Append("[netpupbench] pinned puppet drive bench\n");

            // Same gate as NetSnapshotTest, for the same reasons: Enable/Disable here would
            // tear down a real session's puppet layer mid-flight, and building puppets into a
            // live world would leave stray enemies in it. Report the skip -- an unrun bench
            // must not read as a measurement (the eaBinTest rule).
            if (NetSession.Active || NetPuppets.LiveCount > 0 || NetScene.Current != null)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                return sb.ToString();
            }
            if (n < 1 || n > MaxPuppets)
            {
                sb.Append("  SKIP (n must be 1.." + MaxPuppets + ", was " + n + ")\n");
                return sb.ToString();
            }
            if (iters < 1)
            {
                sb.Append("  SKIP (iters must be >= 1, was " + iters + ")\n");
                return sb.ToString();
            }

            var built = new List<GameComponent>();
            try
            {
                NetPuppets.Enable(game);
                HashSet<GameComponent> before = CollectBullets(game);

                // Build n puppets through the REAL self-heal path (an unknown id in a snapshot
                // entry -> OnSpawn via the descriptor), so they are ordinary puppets built the
                // ordinary way rather than something this file assembled by hand.
                //
                // typeIdx 0 = EvilBulletDescriptor, the simplest replicable: a base-only
                // descriptor, so ApplyStateExtra is a no-op and what is being timed is the
                // BASE per-puppet work every type pays, with no per-type extra folded in.
                byte[] noExtras = new byte[1];
                for (int i = 0; i < n; i++)
                {
                    NetBaseState state = SpawnState(i);
                    NetPuppets.OnSnapshotEntry((ushort)(IdBase + i), 0, NetProtocol.NetSnapshotFlags.None, state, noExtras, 0, 0,
                        out _, out SnapUnknownKind kind);
                    // Collect AS WE GO, not after the loop: an ABORT below returns from inside
                    // the try, and the finally can only take back what `built` already holds.
                    // Collecting afterwards left every puppet built so far orphaned in
                    // Game.Components with nothing tracking it -- Disable() clears the id maps
                    // but does not remove components -- which is exactly what this file's
                    // header promises never happens.
                    foreach (GameComponent item in CollectBullets(game))
                    {
                        if (!before.Contains(item) && !built.Contains(item))
                        {
                            built.Add(item);
                        }
                    }
                    if (kind != SnapUnknownKind.Rebuilt)
                    {
                        sb.Append("  ABORT puppet ").Append(i).Append(" was not built (kind=")
                            .Append(kind).Append(")\n");
                        return sb.ToString();
                    }
                }

                // Give every puppet a SECOND snapshot, which is what puts it into steady state:
                // HasSnapshot goes true, so Drive's scale-lerp branch runs and the dead-reckon
                // is the live-play one rather than the spawn-tick special case.
                //
                // WHAT THIS BENCH DOES NOT MEASURE, stated rather than implied: the offset below
                // is under SnapThresholdPx, so it arms the correction blend -- but
                // CorrectionWindowMs is 150 ms and the warm-up alone advances ~1066 ms of virtual
                // time, so the blend has fully drained before the stopwatch starts and
                // `CorrectionMsLeft > 0f` is a dead branch for every TIMED call. So this is the
                // correction-FREE steady state. Re-arming inside the timed loop would fold
                // OnSnapshotEntry's own cost into the figure, which is a different measurement;
                // the branch is a couple of float ops either way, and the seam A/B this exists
                // for does not touch it.
                for (int i = 0; i < n; i++)
                {
                    NetBaseState state = SpawnState(i);
                    state.Pos += new Vector2(3f, 2f);
                    NetPuppets.OnSnapshotEntry((ushort)(IdBase + i), 0, NetProtocol.NetSnapshotFlags.None, state, noExtras, 0, 0,
                        out _, out _);
                }

                // PRECONDITION, asserted rather than assumed: a bench that silently drove
                // fewer puppets than it says would report a fast number and look like a win.
                if (NetPuppets.LiveCount != n || built.Count != n)
                {
                    sb.Append("  ABORT population is ").Append(NetPuppets.LiveCount)
                        .Append(" live / ").Append(built.Count).Append(" in world, wanted ")
                        .Append(n).Append('\n');
                    return sb.ToString();
                }

                // 16.667 ms: one 60 Hz frame of real time, which is what NetPuppetDriver hands
                // Drive in ordinary play.
                const float DtMs = 16.667f;

                // Warm up: first-call JIT of Drive and the descriptor path would otherwise land
                // entirely in iteration 0 and dominate a short run.
                //
                // IT DOES NOT REMOVE THE FIRST-CALL-PER-PROCESS BIAS, and that is measured, not
                // assumed: the FIRST NetPuppetBench invocation in a process reads ~24% high and
                // only later ones settle (desktop, n=128: 8.87 us then 7.06 / 7.13 / 7.15,
                // reproduced across two processes). So COMPARE LIKE WITH LIKE -- either discard
                // each process's first run, or make both sides of an A/B the same ordinal.
                // Back-to-back runs after that settle to well under 1%; a cold first run does not.
                for (int i = 0; i < 64; i++)
                {
                    NetPuppets.Drive(DtMs);
                }

                // POSITIVE CONTROL: the TIMED loop must actually have moved the puppets --
                // probeStart is sampled after the warm-up, so it is the timed calls this
                // brackets. Without it a Drive that early-returned (a disabled layer, an empty
                // live list) would still be timed, at a beautiful 0 us, and read as a
                // spectacular result.
                Vector2 probeStart = ((AlienDrawableGameComponent)built[0]).Position;

                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < iters; i++)
                {
                    NetPuppets.Drive(DtMs);
                }
                sw.Stop();

                Vector2 probeEnd = ((AlienDrawableGameComponent)built[0]).Position;
                bool moved = (probeEnd - probeStart).LengthSquared() > 0f;

                double totalUs = sw.Elapsed.TotalMilliseconds * 1000.0;
                double usPerCall = totalUs / iters;
                double nsPerPuppet = usPerCall * 1000.0 / n;
                double msPerFrame = usPerCall / 1000.0;

                sb.Append("  n=").Append(n).Append(" iters=").Append(iters)
                    .Append(" dt=").Append(DtMs.ToString("0.###")).Append("ms\n");
                sb.Append("  drive     ").Append(usPerCall.ToString("0.000")).Append(" us/call")
                    .Append("   (").Append(nsPerPuppet.ToString("0.0")).Append(" ns/puppet)\n");
                sb.Append("  at 60Hz   ").Append(msPerFrame.ToString("0.0000")).Append(" ms/frame")
                    .Append("   = ").Append((msPerFrame / FrameBudgetMs * 100.0).ToString("0.000"))
                    .Append("% of the ").Append(FrameBudgetMs.ToString("0.0")).Append("ms budget\n");
                sb.Append("  total     ").Append(sw.Elapsed.TotalMilliseconds.ToString("0.0")).Append(" ms\n");
                sb.Append(moved
                    ? "  control   PASS the puppets really moved under Drive\n"
                    : "  control   FAIL the puppets did NOT move -- the number above is meaningless\n");
                sb.Append("[netpupbench] ").Append(moved ? "OK" : "VACUOUS")
                    .Append(" n=").Append(n)
                    .Append(" us=").Append(usPerCall.ToString("0.000"))
                    .Append(" nsper=").Append(nsPerPuppet.ToString("0.0")).Append('\n');
            }
            finally
            {
                // Take every puppet back out of the world, then drop the layer. Remove is
                // queued, so the bin has to be pumped before Disable clears the id maps --
                // otherwise the removal seam fires against an already-cleared registry and the
                // components are left in Game.Components as orphans.
                foreach (GameComponent item in built)
                {
                    bin.Remove(item);
                }
                bin.Update();
                NetPuppets.Disable();
            }
            return sb.ToString();
        }

        // Deterministic, off-screen (never drawn, never collided) and spread out so no two
        // puppets share a position -- a heap of coincident entities is not a shape any real
        // world takes and could flatter a spatial code path.
        private static NetBaseState SpawnState(int i)
        {
            NetBaseState state = default(NetBaseState);
            state.Pos = new Vector2(-4000f - i * 7f, -4000f - (i % 37) * 11f);
            state.Vel = new Vector2(0.01f + i % 5 * 0.003f, -0.008f);
            state.Scale = 1f;
            state.Rotation = i % 16 * 0.1f;
            return state;
        }

        private static HashSet<GameComponent> CollectBullets(Game game)
        {
            HashSet<GameComponent> set = new HashSet<GameComponent>();
            foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
            {
                if (item is EvilBullet)
                {
                    set.Add(item);
                }
            }
            return set;
        }
    }
}
