// ---------------------------------------------------------------------------
// WorldCensus -- "how much work is on screen right now", as DATA.
//
// The FPS HUD answers WHERE the time goes (per-phase ms) but not WHY: a frame that
// costs 8ms because 400 sprites are alive and a frame that costs 8ms because 40
// sprites each open their own GL batch look identical in the phase rows. This
// counts the two things that actually drive the cost in this port:
//
//   BATCHES -- SpriteBatch.Begin/End pairs opened during one frame. BlazorGL's cost
//              is per-CALL, so a batch that flushes per sprite is the single most
//              expensive shape a draw path can take here. Counted at the one place
//              the wrapper opens a batch.
//   POPULATION -- live components by type, plus the collidable count the collision
//              grid runs over.
//
// Read it with eaWorldCensus() / `eval WorldCensus` under eahl.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    internal static class WorldCensus
    {
        // Batches opened since the last frame boundary, and the rolling mean over a
        // short window so a one-frame blip does not read as a trend.
        private const int Window = 60;
        private static readonly int[] _batchRing = new int[Window];
        private static int _count;
        private static int _next;
        private static int _thisFrame;

        internal static bool Enabled;

        internal static void NoteBatch()
        {
            if (Enabled)
            {
                _thisFrame++;
            }
        }

        // Called once per tick from Game1.DrawInner's tail.
        internal static void EndFrame()
        {
            if (!Enabled)
            {
                return;
            }
            _batchRing[_next] = _thisFrame;
            _next = (_next + 1) % Window;
            if (_count < Window)
            {
                _count++;
            }
            _thisFrame = 0;
        }

        internal static void SetEnabled(bool on)
        {
            Enabled = on;
            _count = 0;
            _next = 0;
            _thisFrame = 0;
        }

        internal static double MeanBatches()
        {
            if (_count == 0)
            {
                return 0.0;
            }
            double s = 0.0;
            for (int i = 0; i < _count; i++)
            {
                s += _batchRing[i];
            }
            return s / _count;
        }

        internal static string Report(Game game)
        {
            var sb = new StringBuilder();
            sb.Append("[census] batches/frame ")
              .Append(MeanBatches().ToString("0.0"))
              .Append(" (window ").Append(_count).Append(")");
            if (game == null)
            {
                return sb.ToString();
            }
            var counts = new Dictionary<string, int>();
            int total = 0;
            foreach (IGameComponent gc in game.Components)
            {
                string n = gc.GetType().Name;
                counts.TryGetValue(n, out int c);
                counts[n] = c + 1;
                total++;
            }
            sb.Append(" | components ").Append(total).Append(" |");
            var names = new List<string>(counts.Keys);
            names.Sort((a, b) => counts[b].CompareTo(counts[a]));
            for (int i = 0; i < names.Count && i < 14; i++)
            {
                sb.Append(' ').Append(names[i]).Append('=').Append(counts[names[i]]);
            }
            return sb.ToString();
        }
    }
}
