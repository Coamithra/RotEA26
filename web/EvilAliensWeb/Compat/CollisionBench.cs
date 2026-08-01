// ---------------------------------------------------------------------------
// CollisionBench — the pinned instrument behind card 391e11d2's collision work.
//
// Invoke with eaCollisionBench(n, iters) / `eval CollisionBench <n> <iters>` — MENU-only, and
// best HEADLESSLY (eahl), where no rAF paces the loop. Args default to 160 / 400.
//
// TWO HALVES, and the CORRECTNESS one is the point.
//
//   1. CORRECTNESS. The broad-phase changes (skip non-colliding entities; dedupe candidates with
//      an O(1) stamp instead of List.Contains; snapshot GetCollisionType once per pass) are
//      claimed to be BEHAVIOUR-NEUTRAL. That claim is worth nothing unless something checks it,
//      and no screenshot or frame time can. So this builds a scripted population of probe
//      collidables that RECORD every CollidesWith they receive, runs the real
//      CollisionHandler.DetectCollisions over it, and diffs the resulting pair set against
//      ReferencePass below — a verbatim transcription of the PRE-CARD algorithm (references in
//      the cells, Contains dedupe, GetCollisionType re-read per access, nothing skipped). The
//      reference is the negative control, in the eaNetScore.test / eaTeamSeat idiom: a
//      re-implementation that agrees with the shipped code proves nothing unless it is the OLD
//      code, and this one is.
//
//   2. TIMING. n probes on a deterministic grid, the real DetectCollisions timed in a plain loop,
//      reported in ABSOLUTE microseconds per pass and per collidable. The FPS HUD cannot answer
//      this at the resolution needed: `UpdCollision` is a per-frame mean over whatever happened
//      to be on screen, so an A/B across builds is confounded by population. This is not.
//      Like eaNetPuppetBench, the FIRST run in a process reads high (JIT); compare at MATCHED
//      RUN ORDINALS, or discard each process's first.
//
// Leave-no-trace: every probe is taken back out of the world before returning, on every exit path.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    internal static class CollisionBench
    {
        private const int MaxProbes = 512;
        private const float FrameBudgetMs = 16.7f;

        // A collidable that exists only to be collided with. Deliberately an
        // AlienDrawableGameComponent so it goes through the REAL DetectCollision / Collides gates
        // the change reasons about — a bespoke ICollidable would test a different code path from
        // the one every game entity uses. It loads no content (texturename stays null) and never
        // draws.
        private sealed class Probe : AlienDrawableGameComponent
        {
            private readonly CollisionBox box = new CollisionBox();
            private readonly CollisionSimpleCircle circle = new CollisionSimpleCircle(Vector2.Zero, 12f);
            private readonly CollisionLine line = new CollisionLine(Vector2.Zero, Vector2.One);

            // 0 = box, 1 = circle, 2 = line. All three gridded shapes are represented so the
            // type-dispatch hoist (one GetCollisionType per pass instead of up to four) is
            // exercised on every branch, not just the box one.
            internal int Shape;
            internal float HalfW = 12f;
            internal float HalfH = 12f;

            // Every CollidesWith this probe received during the pass under test.
            internal readonly List<Probe> Hits = new List<Probe>();

            // How many times CollisionType was evaluated — the direct measure of the snapshot
            // hoist, and the reason a "no behaviour change" claim can also show its own win.
            internal int ShapeReads;

            public Probe(Game game)
                : base(game)
            {
                Visible = false;
            }

            public override ICollisionType CollisionType
            {
                get
                {
                    ShapeReads++;
                    switch (Shape)
                    {
                        case 1:
                            circle.Position = Position;
                            circle.Radius = HalfW;
                            return circle;
                        case 2:
                            line.Origin = Position;
                            line.End = Position + new Vector2(HalfW * 2f, HalfH * 2f);
                            return line;
                        default:
                            box.TopLeft = Position + new Vector2(0f - HalfW, 0f - HalfH);
                            box.BottomRight = Position + new Vector2(HalfW, HalfH);
                            return box;
                    }
                }
            }

            public override void CollidesWith(ICollidable other)
            {
                // Deliberately NOT calling base.CollidesWith: the base drives real gameplay
                // reactions (Floor/Wall handling, the IAlienKiller hit path). Recording is the
                // whole job here.
                if (other is Probe p)
                {
                    Hits.Add(p);
                }
            }
        }

        public static string Run(int n, int iters)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[collbench] broad-phase correctness + cost\n");

            ComponentBin bin = ServiceHelper.Get<IComponentBinService>()?.ComponentBin;
            Game1 game = bin?.Game as Game1;
            if (bin == null || game == null)
            {
                sb.Append("  SKIP (no component bin / Game1 yet)\n");
                return sb.ToString();
            }
            // Same gate as eaBinTest / eaNetPuppetBench: adding hundreds of probe collidables to a
            // LIVE world would have them collide with real entities and drive real reactions, and
            // a scene's own population would make the timing figure meaningless. Report the skip
            // — an unrun bench must never read as a measurement.
            if (GameScene.NetActiveScene != null || EvilAliensWeb.Compat.Net.NetSession.Active)
            {
                sb.Append("  SKIP (run from the main menu, with no level, attract demo or session up)\n");
                return sb.ToString();
            }
            if (n < 4 || n > MaxProbes)
            {
                sb.Append("  SKIP (n must be 4..").Append(MaxProbes).Append(", was ").Append(n).Append(")\n");
                return sb.ToString();
            }
            if (iters < 1)
            {
                sb.Append("  SKIP (iters must be >= 1, was ").Append(iters).Append(")\n");
                return sb.ToString();
            }

            CollisionHandler handler = game.CollisionHandler;
            List<Probe> probes = new List<Probe>();
            try
            {
                // A deterministic grid tight enough that neighbours genuinely overlap — a bench
                // whose probes never touch would time the fill phase only and report the dedupe
                // and narrow-phase work as free.
                // Pitch is DELIBERATELY smaller than a probe's extent (24 px across a +-12 box)
                // so neighbours genuinely overlap and land in shared cells -- the vacuous-run
                // guard below fails the whole bench if that ever stops being true.
                int cols = 24;
                for (int i = 0; i < n; i++)
                {
                    Probe p = new Probe(game);
                    // Every 4th probe is non-colliding: that is the population the skip is FOR
                    // (BloodExplosion and friends), so it has to be in both halves of the bench.
                    p.Collides = (i % 4) != 0;
                    p.Shape = i % 3;
                    p.Position = new Vector2(40f + (i % cols) * 18f, 40f + (i / cols) * 16f);
                    probes.Add(p);
                    bin.Add(p);
                }

                // PRECONDITION, asserted rather than assumed: if the bin diverted the adds (a
                // standing purge filter) the pass below would run over an empty world and every
                // number, including the correctness verdict, would be vacuously good.
                int registered = 0;
                foreach (ICollidable c in handler.Collidables)
                {
                    if (c is Probe)
                    {
                        registered++;
                    }
                }
                if (registered != n)
                {
                    sb.Append("  ABORT only ").Append(registered).Append(" of ").Append(n)
                      .Append(" probes reached the collision handler\n");
                    return sb.ToString();
                }

                // ---- 1. correctness -------------------------------------------------------
                // WARM-UP PASSES FIRST: some of the state the change introduced (the dedupe
                // stamp array) survives BETWEEN passes, so anything that only misbehaves on a
                // second pass over the same population is invisible to a cold single-pass
                // comparison.
                //
                // THIS IS NOT A COMPLETE GUARD FOR THAT CLASS, and the gap is worth knowing.
                // A first cut stamped with the resolution index, which reads its own value back
                // from the previous pass and silently drops a candidate. eaBinTest scenario 8
                // caught it; this bench, even warmed, did NOT -- because the grid below is dense,
                // so some earlier entity in the next pass almost always overwrites the stale
                // stamp before the entity that would have tripped on it gets there. A sparse
                // world is what exposes it, which is exactly what eaBinTest's two- and
                // three-collidable scenarios are. So the cross-pass guard is
                // `tools/headless/probes/net_selftests.txt` (which runs eaBinTest); the warm-up
                // here narrows the window rather than closing it. Do not read a PASS here as
                // covering per-pass state.
                for (int i = 0; i < 4; i++)
                {
                    handler.DetectCollisions();
                }
                ResetProbes(probes);
                handler.DetectCollisions();
                List<string> live = SnapshotPairs(probes);
                int liveShapeReads = TotalShapeReads(probes);

                ResetProbes(probes);
                ReferencePass(handler.Collidables);
                List<string> reference = SnapshotPairs(probes);
                int refShapeReads = TotalShapeReads(probes);

                bool same = SameSet(live, reference, out string firstDiff);
                // POSITIVE CONTROL: an empty pair set matches an empty pair set, so without this
                // a broad phase that found nothing at all would pass the diff with full marks.
                if (reference.Count == 0)
                {
                    sb.Append("  FAIL vacuous — the reference pass found NO collisions at all;")
                      .Append(" the probe grid is not overlapping\n");
                }
                else if (same)
                {
                    sb.Append("  PASS behaviour-neutral: ").Append(live.Count)
                      .Append(" collision callbacks, identical to the pre-card algorithm\n");
                }
                else
                {
                    sb.Append("  FAIL live and pre-card algorithms disagree: ").Append(firstDiff)
                      .Append("  (live ").Append(live.Count).Append(", reference ")
                      .Append(reference.Count).Append(")\n");
                }
                sb.Append("  CollisionType evaluations per pass: live ").Append(liveShapeReads)
                  .Append(", pre-card ").Append(refShapeReads).Append('\n');

                // ---- 2. cost --------------------------------------------------------------
                for (int i = 0; i < 32; i++)
                {
                    handler.DetectCollisions();
                }
                ResetProbes(probes);
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < iters; i++)
                {
                    handler.DetectCollisions();
                }
                sw.Stop();
                // POSITIVE CONTROL for the timing half, same reasoning as eaNetPuppetBench's: a
                // pass that early-returned would time beautifully and mean nothing.
                if (TotalHits(probes) == 0)
                {
                    sb.Append("  FAIL the TIMED passes produced no collisions — the figure below is not a measurement\n");
                }
                double usPerPass = sw.Elapsed.TotalMilliseconds * 1000.0 / iters;
                sb.Append("  ").Append(n).Append(" collidables (")
                  .Append(n - (n + 3) / 4).Append(" colliding), ").Append(iters).Append(" passes: ")
                  .Append(usPerPass.ToString("0.00")).Append(" us/pass, ")
                  .Append((usPerPass / n).ToString("0.000")).Append(" us/collidable, ")
                  .Append((usPerPass / 1000.0 / FrameBudgetMs * 100.0).ToString("0.00"))
                  .Append("% of a 60Hz frame\n");
                return sb.ToString();
            }
            finally
            {
                for (int i = 0; i < probes.Count; i++)
                {
                    bin.Remove(probes[i]);
                }
                // Removals are QUEUED (ComponentBin.Remove -> deathList) and normally flush at a
                // tick boundary -- but a bench runs entirely inside one eval, with no tick in
                // between, so without this the probes are still in the world when the NEXT run
                // starts and its population precondition aborts. Update() is the mid-tick flush
                // point; it is a pure deathList drain (births are instant), so calling it here
                // does not advance anything.
                bin.Update();
            }
        }

        // The PRE-CARD broad phase, transcribed. The negative control for the correctness half:
        // references in the cells, List.Contains dedupe, GetCollisionType re-read per access, and
        // no CanCollide skip anywhere. Deliberately a private copy rather than a switch inside
        // CollisionHandler — a flag there would have to be maintained forever and could drift into
        // agreeing with the new code by construction.
        //
        // Kept faithful down to the details that could matter: the frozen count, the both-ways
        // callback on the non-gridded branch, and the one-way callback in the resolution loop.
        private static void ReferencePass(IReadOnlyList<ICollidable> collidables)
        {
            int count = collidables.Count;
            var cells = new Dictionary<int, List<ICollidable>>();
            var boxes = new List<List<int>>();
            for (int i = 0; i < count; i++)
            {
                boxes.Add(new List<int>());
            }
            for (int l = 0; l < count; l++)
            {
                ICollidable collidable = collidables[l];
                if (collidable.GetCollisionType() is CollisionBox b)
                {
                    RefFillBox(cells, boxes, l, collidable, b.Left, b.Top, b.Right, b.Bottom);
                    continue;
                }
                if (collidable.GetCollisionType() is CollisionLine ln)
                {
                    // The DDA is not what this control exists to re-verify; covering the line's
                    // endpoints and its bounding cells is a SUPERSET of the cells the real
                    // rasteriser marks, so a disagreement caused by the change would still show.
                    RefFillBox(cells, boxes, l, collidable,
                        Math.Min(ln.Origin.X, ln.End.X), Math.Min(ln.Origin.Y, ln.End.Y),
                        Math.Max(ln.Origin.X, ln.End.X), Math.Max(ln.Origin.Y, ln.End.Y));
                    continue;
                }
                if (collidable.GetCollisionType() is CollisionSimpleCircle c)
                {
                    RefFillBox(cells, boxes, l, collidable,
                        c.Position.X - c.Radius, c.Position.Y - c.Radius,
                        c.Position.X + c.Radius, c.Position.Y + c.Radius);
                    continue;
                }
                for (int m = 0; m < count; m++)
                {
                    ICollidable other = collidables[m];
                    if (RefActive(other) && RefActive(collidable) && other != collidable
                        && collidable.DetectCollision(other))
                    {
                        other.CollidesWith(collidable);
                        collidable.CollidesWith(other);
                    }
                }
            }
            var colliders = new List<ICollidable>();
            for (int m = 0; m < count; m++)
            {
                colliders.Clear();
                foreach (int cell in boxes[m])
                {
                    foreach (ICollidable occupant in cells[cell])
                    {
                        if (!colliders.Contains(occupant) && occupant != collidables[m])
                        {
                            colliders.Add(occupant);
                        }
                    }
                }
                foreach (ICollidable collider in colliders)
                {
                    if (RefActive(collidables[m]) && RefActive(collider)
                        && collidables[m].DetectCollision(collider))
                    {
                        collidables[m].CollidesWith(collider);
                    }
                }
            }
        }

        private static void RefFillBox(Dictionary<int, List<ICollidable>> cells,
            List<List<int>> boxes, int i, ICollidable collidable,
            float left, float top, float right, float bottom)
        {
            int l0 = Math.Max(0, (int)(left / 80f));
            int t0 = Math.Max(0, (int)(top / 80f));
            int r0 = Math.Min(9, (int)(right / 80f));
            int b0 = Math.Min(7, (int)(bottom / 80f));
            for (int x = l0; x <= r0; x++)
            {
                for (int y = t0; y <= b0; y++)
                {
                    int key = x * 8 + y;
                    if (!cells.TryGetValue(key, out List<ICollidable> list))
                    {
                        list = new List<ICollidable>();
                        cells[key] = list;
                    }
                    if (!list.Contains(collidable))
                    {
                        list.Add(collidable);
                        boxes[i].Add(key);
                    }
                }
            }
        }

        private static bool RefActive(ICollidable collidable)
        {
            GameComponent gc = (GameComponent)collidable;
            return gc.Enabled || EvilAliensWeb.Compat.Net.NetPuppets.CollidableOverride(gc);
        }

        private static void ResetProbes(List<Probe> probes)
        {
            for (int i = 0; i < probes.Count; i++)
            {
                probes[i].Hits.Clear();
                probes[i].ShapeReads = 0;
            }
        }

        private static int TotalShapeReads(List<Probe> probes)
        {
            int total = 0;
            for (int i = 0; i < probes.Count; i++)
            {
                total += probes[i].ShapeReads;
            }
            return total;
        }

        private static int TotalHits(List<Probe> probes)
        {
            int total = 0;
            for (int i = 0; i < probes.Count; i++)
            {
                total += probes[i].Hits.Count;
            }
            return total;
        }

        // "<receiver index>-><other index>" per callback, so a diff names the exact pair. Counted
        // per occurrence, not deduped: a pair reported twice by one algorithm and once by the
        // other is a real difference (the double-nudge bug perf batch 2 fixed was exactly that).
        private static List<string> SnapshotPairs(List<Probe> probes)
        {
            var pairs = new List<string>();
            for (int i = 0; i < probes.Count; i++)
            {
                for (int j = 0; j < probes[i].Hits.Count; j++)
                {
                    pairs.Add(i + "->" + probes.IndexOf(probes[i].Hits[j]));
                }
            }
            pairs.Sort(StringComparer.Ordinal);
            return pairs;
        }

        private static bool SameSet(List<string> a, List<string> b, out string firstDiff)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    firstDiff = "live has " + a[i] + " where pre-card has " + b[i];
                    return false;
                }
            }
            if (a.Count != b.Count)
            {
                firstDiff = a.Count > b.Count
                    ? "live has extra callback " + a[n]
                    : "live is missing callback " + b[n];
                return false;
            }
            firstDiff = null;
            return true;
        }
    }
}
