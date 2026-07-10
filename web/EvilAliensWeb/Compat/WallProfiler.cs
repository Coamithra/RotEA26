using System;
using System.Diagnostics;

namespace EvilAliensWeb.Compat
{
    // Live cost meter for the Level-3 wall towers (Trello 9dcb4695). The tower extrusion is the most
    // expensive thing on the walls sections -- ~600-1500 batched sprite draws per frame -- and the
    // only honest way to weigh it against the flat path (or against the real-3D spike, card a66fc73e)
    // is to time it in situ rather than reason about draw counts.
    //
    // Three numbers, because they answer different questions:
    //   TOWER ms  -- just the slice + wisp passes inside Wall.Draw. What ?walltowers=0 actually saves,
    //                and the number to compare against the 3D spike.
    //   TICK ms   -- the whole Game.Tick() (Update + Draw + present), from the same stopwatch the hitch
    //                watchdog uses. This is the WORK done in a frame.
    //   FPS       -- derived from the INTERVAL BETWEEN frames, never from tick ms. Those are different
    //                things: at 60Hz vsync a 5ms tick sits inside a 16.7ms frame, so 1000/tick would
    //                claim 200fps. Conversely, once vsync-bound, fps CANNOT see a 4ms regression that
    //                tick ms shows plainly -- which is exactly why both are printed.
    //
    // GOTCHA — a BACKGROUNDED tab throttles requestAnimationFrame, so every reading is garbage unless
    // the Chrome window is focused and in front. It still delivers ~1 frame/second though, so a
    // "time since the last frame" test does NOT detect it; IsStale() checks the mean frame INTERVAL,
    // and the panel checks document.hidden/hasFocus, which is the authoritative signal. Either way it
    // says so rather than showing a confident wrong number.
    //
    // Off by default; DebugInput.WallPerf (eaWallPerf) turns it on, which the eaWalls panel does when it
    // builds. When disabled the accumulators are never touched, so a shipped build pays nothing.
    internal static class WallProfiler
    {
        // Rolling window. ~2s at 60fps: long enough to be stable, short enough to react to a slider drag.
        private const int Window = 120;

        private static readonly double[] _frameMs = new double[Window];
        private static readonly double[] _intervalMs = new double[Window];
        private static readonly double[] _towerMs = new double[Window];
        private static readonly int[] _sliceDraws = new int[Window];
        private static int _count;
        private static int _next;

        // Accumulated across every Wall drawn this tick (a section can have more than one live).
        private static double _tickTowerMs;
        private static int _tickSliceDraws;
        private static int _tickBlocks;
        private static int _tickSlices;

        private static double _lastFrameAtMs;
        private static readonly Stopwatch _wall = Stopwatch.StartNew();

        public static bool Enabled { get; private set; }

        // Always clears the window. Re-arming is how the panel discards the garbage samples collected
        // while the tab was backgrounded, the moment focus comes back.
        public static void SetEnabled(bool on)
        {
            Enabled = on;
            _count = 0;
            _next = 0;
            _lastFrameAtMs = -1.0;
            _tickTowerMs = 0.0;
            _tickSliceDraws = 0;
        }

        // Timestamp helpers. Stopwatch.GetTimestamp() is the browser's high-res clock under WASM; the
        // ~5us clamp Chrome applies is three orders below what we're measuring.
        public static long Begin()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void EndTowers(long startTicks)
        {
            if (!Enabled || startTicks == 0L)
            {
                return;
            }
            _tickTowerMs += (double)(Stopwatch.GetTimestamp() - startTicks) * 1000.0 / (double)Stopwatch.Frequency;
        }

        // Draw count is derived, not counted per-sprite: the slice loop draws exactly `slices` sprites
        // for every visible block, so multiplying costs nothing and can't drift from the loop.
        public static void NoteSlices(int slices, int visibleBlocks)
        {
            if (!Enabled)
            {
                return;
            }
            _tickSlices = slices;
            _tickBlocks += visibleBlocks;
            _tickSliceDraws += slices * visibleBlocks;
        }

        // Called once per Game.Tick() from Index.razor.cs, beside LoadProfiler.NoteFrame.
        public static void EndFrame(double frameMs)
        {
            if (!Enabled)
            {
                return;
            }
            // FPS must come from the INTERVAL between frames, never from frameMs (the tick's work).
            // At 60Hz vsync a 5ms tick sits in a 16.7ms frame; dividing 1000 by the work would claim
            // 200fps. The first frame after arming has no predecessor, so it seeds and is skipped.
            double now = _wall.Elapsed.TotalMilliseconds;
            if (_lastFrameAtMs >= 0.0)
            {
                _frameMs[_next] = frameMs;
                _intervalMs[_next] = now - _lastFrameAtMs;
                _towerMs[_next] = _tickTowerMs;
                _sliceDraws[_next] = _tickSliceDraws;
                _next = (_next + 1) % Window;
                if (_count < Window)
                {
                    _count++;
                }
            }
            _tickTowerMs = 0.0;
            _tickSliceDraws = 0;
            _tickBlocks = 0;
            _lastFrameAtMs = now;
        }

        // A backgrounded tab still delivers ~1 frame/second, so "time since the last frame" does NOT
        // detect throttling -- it slips under any short threshold half the time. The mean INTERVAL does:
        // nothing this project runs at under 10fps when focused (WASM Debug sits around 24ms/40fps).
        // The panel additionally checks document.hidden/hasFocus, which is the authoritative signal.
        private static bool IsStale()
        {
            return _count < 5 || Mean(_intervalMs) > 100.0;
        }

        private static double Mean(double[] a)
        {
            double s = 0.0;
            for (int i = 0; i < _count; i++)
            {
                s += a[i];
            }
            return s / (double)_count;
        }

        private static double P95(double[] a)
        {
            double[] c = new double[_count];
            Array.Copy(a, c, _count);
            Array.Sort(c);
            int k = (int)Math.Round(0.95 * (double)(_count - 1));
            return c[k];
        }

        // One line for the panel. Compact rather than pretty: it re-renders 4x/second.
        public static string Report()
        {
            if (!Enabled)
            {
                return "perf off";
            }
            if (IsStale())
            {
                return "focus the window — a backgrounded tab throttles rAF, readings are meaningless";
            }
            double f = Mean(_frameMs);
            double iv = Mean(_intervalMs);
            double t = Mean(_towerMs);
            int draws = 0;
            for (int i = 0; i < _count; i++)
            {
                draws += _sliceDraws[i];
            }
            draws /= _count;
            double fps = (iv > 0.0001) ? (1000.0 / iv) : 0.0;
            string load = (draws == 0)
                ? "no wall on screen"
                : $"{draws} slice draws ({_tickSlices}/blk)";
            return $"{fps:0.0} fps · tick {f:0.0}ms (p95 {P95(_frameMs):0.0}) · towers {t:0.00}ms · {load}";
        }
    }
}
