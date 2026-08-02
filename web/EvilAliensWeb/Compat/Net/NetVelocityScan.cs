using System;
using System.Collections.Generic;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Measures the FASTEST GENUINE MOVER on the wire, per replicable type, so the unmarked-teleport
    // DIAGNOSTIC threshold in NetSession can be set from data instead of from a hunch.
    //
    // WHY IT EXISTS, and note that its subject changed under it (card 8dabe812 -> e79bb994). It was
    // written for a plausibility CAP: an observed velocity above the threshold had its value
    // REFUSED, so a threshold set too low silently clipped a legitimately fast enemy and recreated,
    // one type at a time and invisibly, exactly the stutter that card removed. The cap is gone --
    // the host now MARKS its repositions (AlienDrawableGameComponent.NetNoteTeleport) and the
    // threshold only decides whether to print "you missed a reposition site". So the stake is
    // lower, but the measurement is the same one and is still what keeps the diagnostic from
    // crying wolf: the negative leg of the verification remains "no real type ever reaches it".
    //
    // WHAT IT MEASURES, and why it is the right quantity. NOT `Speed`/`SpeedVector`: half the
    // replicable set writes `Position` directly (boss entry curves, the SpiderBoss fly-by, every
    // eased arrival), and for those the declared speed reads zero. It samples the SAME finite
    // difference the host puts on the wire -- `(pos - lastPos) / dtMs` at the snapshot cadence --
    // over the real world, so a number out of this scan is directly comparable to the guard's
    // threshold. It deliberately does NOT need a net session: the quantity is a property of the
    // GAME's motion, and requiring a paired peer would make the measurement unrunnable headlessly.
    //
    // It samples at SnapshotIntervalMs rather than per frame because that is the interval the host
    // differentiates over, and a shorter one reports a different (larger, noisier) number for the
    // same motion.
    //
    // Console: eaNetVelScan(true) to arm, eaNetVelScan() to read, under eahl `eval NetVelScan true`
    // / `eval NetVelScan`. Arming clears. Runs as its own GameComponent so it needs no edit to any
    // shared per-frame file -- the NetPuppetDriver pattern.
    internal static class NetVelocityScan
    {
        private sealed class Row
        {
            public Vector2 LastPos;
            public bool Has;
            // A three-sample window, because classifying a sample needs BOTH its neighbours.
            public float Prev2Speed;
            public float PrevSpeed;
            public bool HasPrev2;
            public bool HasPrev;
            public int LastSeenTick;
        }

        // Keyed on the COMPONENT for the position history (each instance has its own), reduced to
        // the TYPE for reporting -- a per-type maximum is what a single global cap has to clear.
        private static readonly Dictionary<GameComponent, Row> perComp = new Dictionary<GameComponent, Row>();
        // Peak of ANY single sample -- includes repositions, pool recycles and screen wraps.
        private static readonly Dictionary<string, float> maxByType = new Dictionary<string, float>();
        // Peak of a sample that is part of a PLATEAU rather than a spike: at least one NEIGHBOUR
        // (before or after) is at least half as fast. This is the number the cap has to clear --
        // a reposition is a one-interval spike by definition, since the entity is elsewhere for
        // exactly one sample, while real flight holds its speed on at least one side.
        //
        // SYMMETRIC on purpose. A predecessor-only test under-reports every ACCELERATING entity's
        // true peak: MarsBoss's entry PowerCurve measured sustained 1.486 against a peak of 2.404,
        // and that 2.404 is genuine motion at the top of the ramp, not a teleport. Siting a cap
        // above the former and below the latter would clip the boss's own arrival.
        private static readonly Dictionary<string, float> sustainedByType = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> samplesByType = new Dictionary<string, int>();

        // Repositions this type ANNOUNCED (card e79bb994): the count of samples where the entity's
        // teleport latch was set. It is the scan's audit half and the reason the whole thing is
        // worth more than a speed table now -- see the marker-audit block above `Sample`.
        private static readonly Dictionary<string, int> markedByType = new Dictionary<string, int>();

        // Samples over the threshold with NO marker: a reposition site that has not been taught
        // to call NetNoteTeleport. See the marker-audit block in `Sample` for why the count lives
        // here rather than in NetMetrics.
        private static readonly Dictionary<string, int> unmarkedByType = new Dictionary<string, int>();

        private static Ticker ticker;
        private static float sampleAccMs;
        private static int tickIndex;

        internal static bool Enabled { get; private set; }

        // REFUSES TO ARM INSIDE A LIVE SESSION (card e79bb994). `Sample` read-and-CLEARS each
        // entity's teleport latch to audit the marking, so a scan running alongside a real host
        // would consume markers before NetSession.CaptureBaseState could put them on the wire --
        // i.e. the diagnostic would reintroduce the exact bug it exists to catch, and only while
        // someone was looking. It never had a reason to run in a session (it measures the GAME's
        // motion, not the link), so this costs nothing.
        internal static string Arm(Game game)
        {
            if (NetSession.Active)
            {
                return "[velscan] REFUSED -- a co-op session is up, and arming would consume the"
                    + " teleport markers before the host could send them. Run it offline.";
            }
            SetEnabled(true, game);
            return "[velscan] armed";
        }

        internal static void Disarm()
        {
            SetEnabled(false, null);
        }

        private static void SetEnabled(bool on, Game game)
        {
            Enabled = on;
            perComp.Clear();
            maxByType.Clear();
            sustainedByType.Clear();
            samplesByType.Clear();
            markedByType.Clear();
            unmarkedByType.Clear();
            NetSession.ClearUnmarkedTeleportReports();
            sampleAccMs = 0f;
            tickIndex = 0;
            if (on && ticker == null && game != null)
            {
                ticker = new Ticker(game);
                // Drop an entity's history the moment it LEAVES the world, which is precisely what
                // NetIdRegistry does (its Entry dies with the component and the replacement gets a
                // fresh netId with HasLastPos false). Every replicable type here is POOLED, so the
                // same object reference comes back out of the recycle pool somewhere else entirely
                // -- and a tick-gap heuristic cannot catch a recycle that completes between two
                // 60ms samples. Measured with only the gap heuristic: an EvilBullet whose declared
                // Speed is 0.24 px/ms reported a SUSTAINED 14.9, and a Braineroid 12.5. Matching
                // the production seam is the only way this number means anything.
                game.Components.ComponentRemoved += OnComponentRemoved;
                game.Components.Add(ticker);
            }
            else if (!on && ticker != null)
            {
                ticker.Game.Components.ComponentRemoved -= OnComponentRemoved;
                ticker.Game.Components.Remove(ticker);
                ticker = null;
            }
        }

        private static void OnComponentRemoved(object src, GameComponentCollectionEventArgs args)
        {
            if (args.GameComponent is GameComponent gc)
            {
                perComp.Remove(gc);
            }
        }

        // dtMs is the GAME's elapsed time, deliberately not NetHost.NowMs. The production capture
        // reads a wall clock, but a headless run is ~17x real time under --nodraw, so a wall-clock
        // cadence samples the same motion over a 17x shorter game interval and reports speeds that
        // are neither the host's nor the game's. Measured before this was fixed: it took ~10
        // samples out of 5000 frames and reported a UFO at 17 px/ms.
        private static void Sample(float dtMs)
        {
            sampleAccMs += dtMs;
            if (sampleAccMs < NetSession.SnapshotIntervalMs)
            {
                return;
            }
            float interval = sampleAccMs;
            sampleAccMs = 0f;
            tickIndex++;
            if (ticker == null)
            {
                return;
            }
            // A SESSION STARTING WHILE ARMED DISARMS THE SCAN, and this is the half that actually
            // holds the invariant `Arm` only states. Arm-time refusal covers "host, then arm";
            // the ordinary debug flow is the other order -- arm at the menu, then host a game --
            // and `Sample` read-and-CLEARS the teleport latch, so an armed scan would eat every
            // marker before CaptureBaseState saw it and silently reintroduce the exact bug this
            // audits, while someone was watching for it. Disarming loses a half-finished audit,
            // which is the cheap side of that trade; it is LOUD so the reading is not mistaken
            // for a complete one.
            if (NetSession.Active)
            {
                Console.WriteLine("[velscan] DISARMED -- a co-op session started while the scan was"
                    + " armed, and it consumes the teleport markers the host needs to send."
                    + " Re-run it offline; this run's tallies are gone.");
                Disarm();
                return;
            }
            // Walks Game.Components rather than Oracle.GetBaddies, and the difference is
            // load-bearing: GetBaddies is a hard-coded `is` chain that omits BOTH `Powerup` and
            // `SpiderHelperMothership`, so scanning it would silently exclude two of the replicable
            // types -- including the mothership, one of the types this card is about. The set that
            // matters is the one NetIdRegistry replicates, so ask its own predicate.
            // IsReplicableInstance also drops the cosmetic-only instances, which take no NetId and
            // whose motion therefore never reaches CaptureBaseState at all.
            foreach (IGameComponent item in ticker.Game.Components)
            {
                if (!(item is AlienDrawableGameComponent c))
                {
                    continue;
                }
                var gc = (GameComponent)(object)c;
                if (!NetTypeRegistry.IsReplicableInstance(gc))
                {
                    continue;
                }
                if (!perComp.TryGetValue(gc, out Row row))
                {
                    row = new Row();
                    perComp[gc] = row;
                }
                else if (row.LastSeenTick != tickIndex - 1)
                {
                    // The component left the world and came back -- these types are POOLED
                    // (New*/Recycle), so the same instance reappears somewhere else entirely and
                    // differencing across the gap invents an enormous speed. The production
                    // capture cannot hit this: NetIdRegistry.Entry is per NETID and a recycled
                    // component is issued a fresh one with HasLastPos false. Drop the history so
                    // the scan matches. (Measured: without this an ordinary Level-2 UFO reported
                    // 17.5 px/ms against a declared MaxSpeed of 0.216.)
                    row.Has = false;
                    row.HasPrev = false;
                    row.HasPrev2 = false;
                }
                // THE MARKER AUDIT (card e79bb994), and it is the reason this scan is now a
                // COVERAGE check rather than only a speed table.
                //
                // The teleport marker is only as good as its call sites: a reposition site that
                // never learned to call NetNoteTeleport fails exactly the way the original bug did
                // -- silently, on the other player's screen, one type at a time. Nothing else can
                // see that. NetSession.CaptureBaseState carries the same diagnostic, but it only
                // runs inside a LIVE HOST SESSION, so a headless soak of a level never reaches it
                // and a probe asserting on it would be vacuous (measured: a 30000-frame Level-2
                // run produced not one line, on a build with the markers intact OR removed).
                //
                // Consuming the latch here is why `Arm` refuses to run inside a session: this is a
                // READ-AND-CLEAR, so a scan armed alongside a real host would eat the markers
                // before CaptureBaseState saw them and un-fix the very bug it audits.
                //
                // Consumed UNCONDITIONALLY, before the `row.Has` gate: with no session nothing
                // else clears it, so a latch skipped on an entity's first sample would still be
                // standing on its second and read as a reposition that never happened.
                bool marked = ((INetEntity)c).NetTakeTeleport();
                if (row.Has && interval > 0f)
                {
                    float speed = ((c.Position - row.LastPos) / interval).Length();
                    string name = c.GetType().Name;
                    if (marked)
                    {
                        markedByType.TryGetValue(name, out int m);
                        markedByType[name] = m + 1;
                    }
                    else if (speed > NetSession.MaxObservedSpeedPxPerMs)
                    {
                        // Its OWN tally, not metrics.UnmarkedTeleports -- NetMetrics has no reset,
                        // so an offline audit's figure would sit in the first [net] line of an
                        // unrelated session later in the process. NetSession owns that counter and
                        // the wording; this owns the count it reports in its own table.
                        unmarkedByType.TryGetValue(name, out int u);
                        unmarkedByType[name] = u + 1;
                        NetSession.ReportUnmarkedTeleport(name, speed);
                    }
                    maxByType.TryGetValue(name, out float prevMax);
                    if (speed > prevMax)
                    {
                        maxByType[name] = speed;
                    }
                    // Classify the PREVIOUS sample, now that both its neighbours are known.
                    if (row.HasPrev)
                    {
                        bool plateau = speed >= row.PrevSpeed * 0.5f
                            || (row.HasPrev2 && row.Prev2Speed >= row.PrevSpeed * 0.5f);
                        if (plateau)
                        {
                            sustainedByType.TryGetValue(name, out float prevSus);
                            if (row.PrevSpeed > prevSus)
                            {
                                sustainedByType[name] = row.PrevSpeed;
                            }
                        }
                    }
                    samplesByType.TryGetValue(name, out int n);
                    samplesByType[name] = n + 1;
                    row.Prev2Speed = row.PrevSpeed;
                    row.HasPrev2 = row.HasPrev;
                    row.PrevSpeed = speed;
                    row.HasPrev = true;
                }
                row.LastPos = c.Position;
                row.LastSeenTick = tickIndex;
                row.Has = true;
            }
            // No trimming here on purpose: OnComponentRemoved drops each entity's row as it leaves
            // the world, so the map tracks the LIVE population rather than growing with the scan.
            // A size-triggered Clear() would also wipe every live entity's history at once and
            // silently cost a sample from each, which is the opposite of what a bound should do.
        }

        internal static string Report()
        {
            if (!Enabled)
            {
                return "[velscan] off -- arm it with eaNetVelScan(true) first";
            }
            var sb = new StringBuilder();
            var names = new List<string>(maxByType.Keys);
            names.Sort((a, b) => Sustained(b).CompareTo(Sustained(a)));
            sb.Append("[velscan] observed |vel| per replicable type, px/ms at the ")
              .Append(NetSession.SnapshotIntervalMs).Append("ms snapshot cadence")
              .Append(" (unmarked-teleport diagnostic threshold ")
              .Append(NetSession.MaxObservedSpeedPxPerMs.ToString("0.00"))
              .Append("; sustained = a plateau, i.e. a neighbouring sample on EITHER side is at"
                    + " least half as fast. peak = any single sample)\n");
            if (names.Count == 0)
            {
                sb.Append("[velscan] VACUOUS -- no replicable entity was sampled; a scan with no live"
                    + " world measures nothing and must not be read as a low ceiling");
                return sb.ToString();
            }
            float worst = 0f;
            string worstName = "";
            for (int i = 0; i < names.Count; i++)
            {
                float sus = Sustained(names[i]);
                bool repositions = RepositioningTypes.Contains(names[i]);
                sb.Append("  ").Append(names[i].PadRight(26))
                  .Append("sustained ").Append(sus.ToString("0.000"))
                  .Append("  peak ").Append(maxByType[names[i]].ToString("0.000"))
                  .Append("  (n=").Append(samplesByType[names[i]])
                  // The audit column, and the POSITIVE control for the coverage check: a build
                  // whose markers had all been deleted would print no UNMARKED lines here either
                  // -- it would simply stop repositioning as far as this scan can tell -- so the
                  // probe requires a known repositioning type to show a nonzero count.
                  .Append(", marked=").Append(Marked(names[i]))
                  .Append(Unmarked(names[i]) > 0
                      ? ", UNMARKED=" + Unmarked(names[i]).ToString() : "").Append(')')
                  .Append(repositions ? "  [repositions -- excluded]" : "").Append('\n');
                if (!repositions && sus > worst)
                {
                    worst = sus;
                    worstName = names[i];
                }
            }
            if (worstName.Length == 0)
            {
                sb.Append("[velscan] VACUOUS -- every type sampled repositions, so this run says"
                    + " nothing about the cap; play a level with an ordinary flier in it");
                return sb.ToString();
            }
            bool clear = worst < NetSession.MaxObservedSpeedPxPerMs;
            sb.Append("[velscan] fastest non-repositioning=").Append(worstName).Append(' ').Append(worst.ToString("0.000"))
              .Append(" px/ms, threshold=").Append(NetSession.MaxObservedSpeedPxPerMs.ToString("0.00"))
              .Append(clear ? " -- PASS (headroom x" : " -- FAIL (a REAL mover is at or over the threshold, x")
              .Append((NetSession.MaxObservedSpeedPxPerMs / Math.Max(worst, 1e-6f)).ToString("0.0")).Append(')');
            return sb.ToString();
        }

        private static float Sustained(string name)
        {
            return sustainedByType.TryGetValue(name, out float v) ? v : 0f;
        }

        private static int Marked(string name)
        {
            return markedByType.TryGetValue(name, out int v) ? v : 0;
        }

        private static int Unmarked(string name)
        {
            return unmarkedByType.TryGetValue(name, out int v) ? v : 0;
        }

        // Types that REPOSITION as part of ordinary play, so a big reading for them is the
        // mechanism working rather than the threshold being wrong. Each is a code fact, not an
        // opinion, and since card e79bb994 each also CALLS NetNoteTeleport at the line named:
        //   Braineroid  -- Update wraps it across the screen when `wrapping` is set
        //                  (`Position = new Vector2(0 - num, y)` and its three siblings).
        //   EvilSkull   -- respawns at `new Vector2(Random(0,800), Random(0,600))`, i.e. the
        //                  grinning face reappears somewhere else outright.
        //   SpiderBoss  -- parked at the far screen edge to start each fly-by. THE CARD.
        //   Ball        -- BallState.startup wraps it on all three edges
        //                  (`Position = new Vector2(800 + radius, y)` and its two siblings).
        //                  Found by card e79bb994's audit of every direct Position write on a
        //                  replicable type; it had been missing from this list, and the old cap
        //                  covered it only by luck (its wrap happens to imply ~13 px/ms).
        //
        // THIS LIST IS THE SCAN'S OWN BOOKKEEPING, NOT THE FEATURE. It exists because the scan
        // cannot tell a jump from motion -- the game itself now can, and says so. Adding a type
        // here without adding its NetNoteTeleport call fixes the probe's verdict and leaves the
        // actual bug in place; the `expect-not UNMARKED` leg of net_velguard.txt is what catches
        // that, so never "fix" a failure by editing only this list.
        //
        // DELIBERATELY ONLY THESE THREE. Every replicable type is pooled, so most of them show a
        // one-sample PEAK from an entry path placing them off-screen -- but the plateau test
        // already discounts those, which is its whole job, and listing them here as well would
        // remove them from the verdict permanently. That matters because this scan is the SOLE
        // negative test the cap rests on: if a pooled type ever did move fast enough to be clipped,
        // an over-broad exclusion list is exactly what would keep this green while the guard
        // stuttered it. Earn a place here by REPOSITIONING, not by being pooled.
        //
        // The verdict is computed over everything NOT in this list. Keeping it explicit is the
        // point: a new type showing a big SUSTAINED reading must be judged by a human -- either it
        // is genuinely that fast, and the cap goes up, or it repositions, and it is named here
        // WITH the line of code that proves it.
        private static readonly HashSet<string> RepositioningTypes = new HashSet<string>
        {
            "Braineroid", "EvilSkull", "SpiderBoss", "Ball",
        };

        // Its own component so arming this needs no edit to Game1 or GameScene -- the same reason
        // NetPuppetDriver is one.
        private sealed class Ticker : GameComponent
        {
            public Ticker(Game game)
                : base(game)
            {
                UpdateOrder = -900;
            }

            public override void Update(GameTime gameTime)
            {
                if (Enabled)
                {
                    Sample((float)gameTime.ElapsedGameTime.TotalMilliseconds);
                }
                base.Update(gameTime);
            }
        }
    }
}
