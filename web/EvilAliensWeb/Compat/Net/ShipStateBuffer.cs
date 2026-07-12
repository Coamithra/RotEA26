using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Jitter buffer for the remote ship stream. Samples are kept sorted by sender time;
    // the puppet renders ~NetSession.InterpDelayMs behind the newest sample, interpolating
    // between the bracketing pair. Past the newest sample (underrun) it extrapolates along
    // the last velocity, capped so a stalled peer freezes instead of flying off.
    public sealed class ShipStateBuffer
    {
        private const double ExtrapolateCapMs = 250.0;
        private const double TrimBehindMs = 1000.0;

        private readonly List<ShipSample> samples = new List<ShipSample>(64);

        public bool HasSamples => samples.Count > 0;

        public double NewestMs => samples.Count > 0 ? samples[samples.Count - 1].T : 0.0;

        public ShipSample Newest => samples[samples.Count - 1];

        // Out-of-order / duplicate stream packets are dropped (returns false so the caller
        // can count them) -- with interpolation there is no value in resurrecting them.
        public bool Add(in ShipSample s)
        {
            if (samples.Count > 0 && s.T <= samples[samples.Count - 1].T)
            {
                return false;
            }
            samples.Add(s);
            double cutoff = s.T - TrimBehindMs;
            int k = 0;
            while (k < samples.Count - 2 && samples[k + 1].T < cutoff)
            {
                k++;
            }
            if (k > 0)
            {
                samples.RemoveRange(0, k);
            }
            return true;
        }

        // Position at render time t. extrapolated == true when t is past the newest sample
        // (buffer underrun -- a health metric, not an error).
        public Vector2 Sample(double t, out bool extrapolated)
        {
            extrapolated = false;
            ShipSample last = samples[samples.Count - 1];
            if (t >= last.T)
            {
                extrapolated = t > last.T;
                float ahead = (float)Math.Min(t - last.T, ExtrapolateCapMs);
                return last.Pos + last.Vel * ahead;
            }
            if (t <= samples[0].T)
            {
                return samples[0].Pos;
            }
            for (int i = samples.Count - 2; i >= 0; i--)
            {
                if (samples[i].T <= t)
                {
                    ShipSample a = samples[i];
                    ShipSample b = samples[i + 1];
                    float f = (float)((t - a.T) / (b.T - a.T));
                    return Vector2.Lerp(a.Pos, b.Pos, f);
                }
            }
            return samples[0].Pos;
        }

        public void Clear()
        {
            samples.Clear();
        }
    }
}
