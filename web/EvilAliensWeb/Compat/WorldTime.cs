// ---------------------------------------------------------------------------
// WorldTime — the WORLD's own clock, for Draw-time cosmetics (card d79a2f48).
//
// THE PROBLEM IT SOLVES. A pause is ComponentBin.Push(): every world component
// gets Enabled = false, so its Update stops — and with it everything the shared
// animation machinery drives (AlienDrawableGameComponent.curframe, every Timer).
// Draw keeps running, because the frozen world still has to be drawn behind the
// pause menu. So any animation that advances inside Draw carries on regardless,
// and Draw is handed the RAW GameTime — Game1.UpdateScaled folds turbo, the 1-up
// slow-mo and Juice's hit-stop into the Update path only. Reading
// gameTime.TotalGameTime from a Draw therefore ignores all four freezes at once.
//
// Measured before the fix: BrainBoss paused, two frames 45 steps apart, 22482
// pixels differing outside the pause menu (the aura, the glows and the overlay
// patches all still cycling).
//
// THE RULE. A Draw in a WORLD component reads WorldTime.Seconds, never
// gameTime.TotalGameTime. It advances with the SCALED delta and only while the
// world is unfrozen, so pause, the net layer's remote pause, the Guide freeze,
// hit-stop and slow-mo are all honoured by construction rather than by each
// call site remembering to check — which is the whole point, since the bug was
// eleven call sites each independently forgetting.
//
// WHAT DELIBERATELY DOES NOT USE IT. The menus (MenuSub1, Option, StartScreen,
// SplashScene, CastDisplayer, SpriteBatchWrapper.MetalTime, …) keep real time:
// they draw the pause menu ITSELF, or only exist outside a level. Freezing them
// would stop the pause menu's own glint and selection pulse. Same for
// WebcamLevel's "Step into view!" HUD prompt — a text prompt, not a world FX.
//
// Prefer the shared animation classes where a component owns real per-instance
// state: LoadAnimation + curframe for a sprite sheet, Timer for a countdown.
// This clock is for the stateless ambient case (a shimmer, a hue cycle, a spin
// phase) where a per-object accumulator would be pure overhead.
// ---------------------------------------------------------------------------

namespace EvilAliensWeb.Compat
{
    public static class WorldTime
    {
        // Seconds of unfrozen, time-scaled world time since boot. Monotonic and
        // non-decreasing: it stalls under a freeze, it never runs backwards, so a
        // `% period` phase stays continuous across a pause instead of jumping.
        public static float Seconds { get; private set; }

        // Called once per tick from Game1.UpdateScaled, AFTER the turbo/slow-mo/
        // hit-stop scale and only when ComponentBin.FreezeDepth == 0. Guarded the
        // same way Juice.Update is: a stall (tab refocus, GC hitch) must not jump
        // the phase of every shimmer in the world.
        public static void Advance(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt))
            {
                return;
            }
            if (dt > 0.1f)
            {
                dt = 0.1f;
            }
            Seconds += dt;
        }

        // Test/probe rezero -- DebugInput.WorldClockReset is the only caller, and it exists so
        // a probe can assert an exact reading rather than a boot-tick count. Nothing calls it at
        // boot (the field starts at 0) and nothing calls it on level entry: these are ambient
        // phases, so a level starting mid-shimmer is invisible, while resetting would pop every
        // effect that survived the transition.
        internal static void Reset()
        {
            Seconds = 0f;
        }
    }
}
