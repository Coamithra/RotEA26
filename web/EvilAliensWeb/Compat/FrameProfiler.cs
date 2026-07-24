using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace EvilAliensWeb.Compat
{
    // The frame profiler behind the dev-build FPS HUD (Trello 22e655b5).
    //
    // WHY IT IS NOT JUST AN FPS COUNTER: the game loop is requestAnimationFrame-driven
    // (index.html tickJS -> Index.razor.cs TickDotNet -> Game.Tick), so the frame RATE is
    // vsync-gated. At 60Hz a 3ms frame and a 15ms frame both read "60 fps" -- right up until
    // the game falls off the cliff. A rate readout therefore cannot answer "which parts need
    // optimization"; frame COST can, so that is what this measures, split by phase.
    //
    // The two headline numbers, kept side by side and never conflated (the same distinction
    // WallProfiler draws for the tower pass):
    //   FPS      -- from the INTERVAL between frames. What the display actually shows;
    //                capped by vsync unless the HUD's uncapped pump is on.
    //   HEADROOM -- 1000 / mean tick ms. What the loop could sustain if nothing gated it.
    //                Rises when you optimize even while FPS sits flat at the refresh rate.
    // Headroom is CPU-side: WebGL commands are queued, so a GPU-bound frame's real ceiling is
    // lower than it claims. The HUD's "gpu sync" mode (gl.finish per tick) folds GPU execution
    // into the tick time, which is the honest reading when headroom looks too good to be true.
    //
    // Sections are an enum index into a flat array -- no dictionary, no string hashing, no
    // allocation per frame. TO ADD A SECTION: add an enum member (append; Count stays last),
    // add its label to Labels, and bracket the suspect block with
    //     long t = FrameProfiler.Begin(); ... FrameProfiler.End(FrameSection.X, t);
    // Begin() returns 0 and every End() early-outs while disabled, so an un-armed build pays
    // one static bool test per section.
    //
    // GOTCHA -- a BACKGROUNDED tab throttles rAF to ~1 frame/second, which makes every reading
    // garbage. "Time since the last frame" does NOT detect that (it slips under any short
    // threshold half the time); IsStale() checks the mean frame INTERVAL, and the HUD also
    // checks document.hidden/hasFocus, which is authoritative.
    internal enum FrameSection
    {
        Update,
        UpdComponents,
        UpdCollision,
        UpdNet,
        DrawScene,
        DrawPost,
        DrawPresent,
        Swap,
        Count
    }

    internal static class FrameProfiler
    {
        // ~2s at 60Hz: long enough to be stable, short enough to react to walking into a
        // heavy section. Matches WallProfiler's window so the two agree on the same fight.
        private const int Window = 120;

        private static readonly string[] Labels =
        {
            "update", "components", "collision", "net", "scene", "post", "present", "swap"
        };

        private static readonly double[] _tickMs = new double[Window];
        private static readonly double[] _intervalMs = new double[Window];
        private static readonly double[,] _sectionMs = new double[(int)FrameSection.Count, Window];
        private static readonly double[] _thisTick = new double[(int)FrameSection.Count];

        private static int _count;
        private static int _next;
        private static double _lastFrameAtMs = -1.0;
        private static readonly Stopwatch _wall = Stopwatch.StartNew();

        // Draw calls the GL context saw for the previous frame, pushed in from JS (index.html
        // patches drawElements/drawArrays). Counted there rather than in SpriteBatchWrapper
        // because the per-CALL cost is BlazorGL's dominant one and JS sees every source of
        // calls -- sprite batches, the bloom passes, the walls' 3D primitives -- at once.
        private static int _glCalls;

        // Scratch for Report(), reused so the 4Hz poll doesn't allocate a fresh builder.
        private static readonly StringBuilder _sb = new StringBuilder(512);

        public static bool Enabled { get; private set; }

        // Always clears the window: re-arming is how the HUD throws away the garbage samples
        // collected while the tab was backgrounded, the moment focus comes back.
        public static void SetEnabled(bool on)
        {
            Enabled = on;
            Reset();
        }

        public static void Reset()
        {
            _count = 0;
            _next = 0;
            _lastFrameAtMs = -1.0;
            _glCalls = 0;
            for (int s = 0; s < (int)FrameSection.Count; s++)
            {
                _thisTick[s] = 0.0;
            }
        }

        // Stopwatch.GetTimestamp() is the browser's high-res clock under WASM; the ~5us clamp
        // Chrome applies is orders below a section worth optimizing.
        public static long Begin()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void End(FrameSection section, long startTicks)
        {
            if (!Enabled || startTicks == 0L)
            {
                return;
            }
            // Accumulate rather than assign: a section may be entered more than once per tick
            // (DrawPost brackets two separate post-process passes).
            _thisTick[(int)section] +=
                (double)(Stopwatch.GetTimestamp() - startTicks) * 1000.0 / (double)Stopwatch.Frequency;
        }

        public static void NoteGlCalls(int calls)
        {
            _glCalls = calls;
        }

        // Called once per Game.Tick() from Index.razor.cs, beside LoadProfiler.NoteFrame and
        // WallProfiler.EndFrame -- one stopwatch, three consumers.
        public static void EndFrame(double tickMs)
        {
            if (!Enabled)
            {
                return;
            }
            double now = _wall.Elapsed.TotalMilliseconds;
            // The first frame after arming has no predecessor to measure an interval against,
            // so it seeds the clock and is skipped rather than recorded as a 0ms frame.
            if (_lastFrameAtMs >= 0.0)
            {
                _tickMs[_next] = tickMs;
                _intervalMs[_next] = now - _lastFrameAtMs;
                for (int s = 0; s < (int)FrameSection.Count; s++)
                {
                    _sectionMs[s, _next] = _thisTick[s];
                }
                _next = (_next + 1) % Window;
                if (_count < Window)
                {
                    _count++;
                }
            }
            for (int s = 0; s < (int)FrameSection.Count; s++)
            {
                _thisTick[s] = 0.0;
            }
            _lastFrameAtMs = now;
        }

        private static bool IsStale()
        {
            return _count < 5 || Mean(_intervalMs) > 100.0;
        }

        private static double Mean(double[] a)
        {
            if (_count == 0)
            {
                return 0.0;
            }
            double s = 0.0;
            for (int i = 0; i < _count; i++)
            {
                s += a[i];
            }
            return s / (double)_count;
        }

        private static double MeanSection(int section)
        {
            if (_count == 0)
            {
                return 0.0;
            }
            double s = 0.0;
            for (int i = 0; i < _count; i++)
            {
                s += _sectionMs[section, i];
            }
            return s / (double)_count;
        }

        private static double P95(double[] a)
        {
            if (_count == 0)
            {
                return 0.0;
            }
            double[] c = new double[_count];
            Array.Copy(a, c, _count);
            Array.Sort(c);
            int k = (int)Math.Round(0.95 * (double)(_count - 1));
            return c[k];
        }

        // The HUD's payload: JSON, polled ~4x/second (never per frame). Hand-built because the
        // shape is fixed and a serializer would drag reflection into a trimmed publish.
        // `stale` is a first-class field -- the HUD says "unfocused" instead of printing a
        // confident wrong number.
        public static string Report()
        {
            if (!Enabled)
            {
                return "{\"on\":false}";
            }
            _sb.Clear();
            _sb.Append("{\"on\":true,\"stale\":").Append(IsStale() ? "true" : "false");
            _sb.Append(",\"samples\":").Append(_count);
            double tick = Mean(_tickMs);
            double interval = Mean(_intervalMs);
            Num(",\"tickMs\":", tick);
            Num(",\"tickP95Ms\":", P95(_tickMs));
            Num(",\"intervalMs\":", interval);
            Num(",\"fps\":", interval > 0.0001 ? 1000.0 / interval : 0.0);
            // The point of the whole exercise: a work-derived rate that keeps moving after the
            // measured one has flattened against vsync.
            Num(",\"headroomFps\":", tick > 0.0001 ? 1000.0 / tick : 0.0);
            _sb.Append(",\"glCalls\":").Append(_glCalls.ToString(CultureInfo.InvariantCulture));
            _sb.Append(",\"sections\":{");
            double sectionSum = 0.0;
            for (int s = 0; s < (int)FrameSection.Count; s++)
            {
                if (s > 0)
                {
                    _sb.Append(',');
                }
                double v = MeanSection(s);
                // Update is the parent of the three update sub-sections, so only Update and
                // the draw sections count toward the "other" remainder below.
                if (s == (int)FrameSection.Update || s >= (int)FrameSection.DrawScene)
                {
                    sectionSum += v;
                }
                _sb.Append('"').Append(Labels[s]).Append("\":");
                AppendNum(v);
            }
            _sb.Append('}');
            // Whatever the tick spent outside every bracketed phase: KNI's Game.Tick overhead,
            // the interop hop, GC. A big `other` means the cost is NOT where the rows say.
            double other = tick - sectionSum;
            Num(",\"otherMs\":", other > 0.0 ? other : 0.0);
            // The raw window, oldest-first, for the HUD's sparkline. Means hide exactly what a
            // frame graph is for -- a 2ms mean with one 40ms spike per second is a stutter the
            // player feels and the average denies.
            _sb.Append(",\"frames\":[");
            for (int i = 0; i < _count; i++)
            {
                if (i > 0)
                {
                    _sb.Append(',');
                }
                AppendNum(_tickMs[Oldest(i)]);
            }
            _sb.Append(']');
            _sb.Append('}');
            return _sb.ToString();
        }

        // Ring-buffer index of the i'th oldest sample. Until the window fills, writes start at
        // 0 and _next IS the count, so the identity mapping is already chronological.
        private static int Oldest(int i)
        {
            return (_count < Window) ? i : (_next + i) % Window;
        }

        // One console line (eaFps.stats()), same information, human-shaped.
        public static string StatsLine()
        {
            if (!Enabled)
            {
                return "fps hud off";
            }
            if (IsStale())
            {
                return "focus the window - a backgrounded tab throttles rAF, readings are meaningless";
            }
            double tick = Mean(_tickMs);
            double interval = Mean(_intervalMs);
            double fps = interval > 0.0001 ? 1000.0 / interval : 0.0;
            double headroom = tick > 0.0001 ? 1000.0 / tick : 0.0;
            return string.Format(CultureInfo.InvariantCulture,
                "{0:0.0} fps measured | {1:0.00}ms/frame (p95 {2:0.00}) -> {3:0} fps headroom | "
                + "upd {4:0.00} (comp {5:0.00} coll {6:0.00} net {7:0.00}) | "
                + "scene {8:0.00} post {9:0.00} present {10:0.00} swap {11:0.00} | {12} gl calls",
                fps, tick, P95(_tickMs), headroom,
                MeanSection((int)FrameSection.Update),
                MeanSection((int)FrameSection.UpdComponents),
                MeanSection((int)FrameSection.UpdCollision),
                MeanSection((int)FrameSection.UpdNet),
                MeanSection((int)FrameSection.DrawScene),
                MeanSection((int)FrameSection.DrawPost),
                MeanSection((int)FrameSection.DrawPresent),
                MeanSection((int)FrameSection.Swap),
                _glCalls);
        }

        // Data self-test (eaFps.test()). Pushes a synthetic frame series through the REAL
        // accumulator on a virtual clock and reports what came back out, so the window maths --
        // and specifically the interval-vs-work distinction the whole card rests on -- is
        // proved as DATA rather than by squinting at a live readout. Written in place of a
        // tools/sim mirror on purpose (the eaNetSim.test precedent): a python copy of this
        // arithmetic would drift from the C# and prove nothing.
        //
        // The scenario is the vsync trap itself: `workMs` of work delivered every `intervalMs`.
        // Expected: fps == 1000/intervalMs, headroom == 1000/workMs. A profiler that reported
        // 1000/workMs as "fps" -- the bug this card exists to avoid -- fails here loudly.
        public static string SelfTest(double workMs, double intervalMs, int frames)
        {
            if (frames < 2) frames = 2;
            if (frames > Window * 4) frames = Window * 4;

            bool wasEnabled = Enabled;
            // Snapshot the live window so running the test mid-session doesn't wipe the HUD's
            // real samples (the test injects its own, then restores).
            double[] tickSave = (double[])_tickMs.Clone();
            double[] intervalSave = (double[])_intervalMs.Clone();
            double[,] sectionSave = (double[,])_sectionMs.Clone();
            int countSave = _count, nextSave = _next;
            double lastSave = _lastFrameAtMs;

            Enabled = true;
            Reset();
            // Virtual clock: the interval is fed in directly rather than slept. EndFrame would
            // read the real Stopwatch to derive it, which is the one thing a test can't wait
            // for -- so the samples land in the same ring buffer by the same rules.
            for (int i = 0; i < frames; i++)
            {
                _tickMs[_next] = workMs;
                _intervalMs[_next] = intervalMs;
                _sectionMs[(int)FrameSection.DrawScene, _next] = workMs * 0.5;
                _sectionMs[(int)FrameSection.Update, _next] = workMs * 0.3;
                _next = (_next + 1) % Window;
                if (_count < Window) _count++;
            }

            double tick = Mean(_tickMs);
            double interval = Mean(_intervalMs);
            double fps = interval > 0.0001 ? 1000.0 / interval : 0.0;
            double headroom = tick > 0.0001 ? 1000.0 / tick : 0.0;
            bool stale = IsStale();
            double expFps = intervalMs > 0.0001 ? 1000.0 / intervalMs : 0.0;
            double expHeadroom = workMs > 0.0001 ? 1000.0 / workMs : 0.0;
            bool pass = Math.Abs(fps - expFps) < 0.05
                && Math.Abs(headroom - expHeadroom) < 0.05
                && Math.Abs(Mean(_tickMs) - workMs) < 0.001
                && stale == (intervalMs > 100.0);

            string result = string.Format(CultureInfo.InvariantCulture,
                "[fps.test] {0}ms work every {1}ms x{2} frames -> "
                + "fps {3:0.00} (expect {4:0.00}) | headroom {5:0.00} (expect {6:0.00}) | "
                + "tick mean {7:0.000} p95 {8:0.000} | scene {9:0.000} update {10:0.000} | "
                + "stale {11} (expect {12}) | samples {13} | {14}",
                workMs, intervalMs, frames, fps, expFps, headroom, expHeadroom,
                tick, P95(_tickMs),
                MeanSection((int)FrameSection.DrawScene), MeanSection((int)FrameSection.Update),
                stale, intervalMs > 100.0, _count, pass ? "PASS" : "FAIL");

            Array.Copy(tickSave, _tickMs, Window);
            Array.Copy(intervalSave, _intervalMs, Window);
            Array.Copy(sectionSave, _sectionMs, sectionSave.Length);
            _count = countSave;
            _next = nextSave;
            _lastFrameAtMs = lastSave;
            Enabled = wasEnabled;
            return result;
        }

        private static void Num(string key, double v)
        {
            _sb.Append(key);
            AppendNum(v);
        }

        private static void AppendNum(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                v = 0.0;
            }
            _sb.Append(v.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }
}
