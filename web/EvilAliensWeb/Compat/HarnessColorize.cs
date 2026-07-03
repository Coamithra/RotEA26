// ---------------------------------------------------------------------------
// HarnessColorize — colorize (hue-remap) override for the sprite harness.
//
// Built for the card "the level-3 alien bosses (the little lightbulbs) don't
// colorize well — a tool to see them live with tweakable hue params". The
// alienboss "lightbulb" boss (BattleSkull) recolours a band of the sprite's
// hues [Minimum,Maximum] toward a Target hue (see sprite.fx COLORIZE + the
// ColorizeEffect / EffectHandler path). In-game that band is (-10,10) and the
// Target sweeps with HP. This lets the harness override those three numbers
// live from the URL (?huestart / ?hueend / ?huetarget / ?hue / ?huecycle) so
// the band + target can be tuned by eye, exactly like the ?blast* knobs tune
// the bomb lifetime.
//
// Apply() is a no-op unless the harness is up (DebugFlags.Harness != null) AND
// at least one hue flag is present — so normal gameplay is byte-identical and a
// shipped build is unaffected. The BattleSkull calls it around its own
// colorizeEffect.RangeTarget set; HarnessScene reads Describe()/IsActive() for
// the on-screen readout, and everything shares this one cycle clock.
// ---------------------------------------------------------------------------
using System.Globalization;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    internal static class HarnessColorize
    {
        // True when the harness is up and the user actually asked for a colorize override.
        // Off => Apply() returns its input unchanged, so gameplay/other bosses are untouched.
        public static bool IsActive =>
            DebugFlags.Harness != null
            && (DebugFlags.HueStart.HasValue || DebugFlags.HueEnd.HasValue
                || DebugFlags.HueTarget.HasValue || DebugFlags.HueCycle);

        // The last resolved (Minimum, Maximum, Target) in DEGREES, for the readout.
        public static Vector3 LastRange { get; private set; }

        // Override a coded ColorizeEffect.RangeTarget (degrees: X=min, Y=max, Z=target hue)
        // with whatever hue flags are present. Absent components keep the coded value, so
        // e.g. ?huetarget=200 alone re-aims the target while keeping the coded (-10,10) band.
        public static Vector3 Apply(Vector3 codedRange, GameTime gameTime)
        {
            if (!IsActive)
            {
                LastRange = codedRange;
                return codedRange;
            }

            float min = DebugFlags.HueStart ?? codedRange.X;
            float max = DebugFlags.HueEnd ?? codedRange.Y;
            float target;
            if (DebugFlags.HueCycle)
            {
                // Sweep the target 0..360 so a screenshot at any moment shows a different
                // point of the recolour range. Wall-clock, independent of the frozen object.
                float loop = (DebugFlags.HueLoopSeconds > 0f) ? DebugFlags.HueLoopSeconds : 6f;
                float t = (float)(gameTime.TotalGameTime.TotalSeconds % loop) / loop;
                target = t * 360f;
                // ?huetarget with ?huecycle biases the sweep so it orbits the pinned hue
                // (target +/- 180) rather than a raw 0..360, handy to inspect near one hue.
                if (DebugFlags.HueTarget.HasValue)
                {
                    target = DebugFlags.HueTarget.Value + (t - 0.5f) * 360f;
                }
            }
            else
            {
                target = DebugFlags.HueTarget ?? codedRange.Z;
            }

            var range = new Vector3(min, max, target);
            LastRange = range;
            return range;
        }

        // One-line readout for the harness caption (empty when not overriding).
        public static string Describe()
        {
            if (!IsActive)
            {
                return "";
            }
            Vector3 r = LastRange;
            string t = DebugFlags.HueCycle
                ? "target " + r.Z.ToString("0", CultureInfo.InvariantCulture) + " (cycling)"
                : "target " + r.Z.ToString("0", CultureInfo.InvariantCulture);
            return "colorize band [" + r.X.ToString("0", CultureInfo.InvariantCulture)
                + ".." + r.Y.ToString("0", CultureInfo.InvariantCulture) + "]   " + t;
        }
    }
}
