// ---------------------------------------------------------------------------
// Juice — shared game-feel state: trauma-based SCREEN SHAKE + HIT-STOP (freeze
// frames). The two classic "game juice" techniques this port was missing (per
// Vlambeer's "The Art of Screenshake" and Jonasson/Purho's "Juice it or lose
// it"); the rest of the canon — hit flash, rumble, particles, slow-motion,
// ghost trails, floating score text — already existed in the game.
//
// SCREEN SHAKE (the "trauma" model, from Squirrel Eiserloh's GDC camera talk):
//   * Events add TRAUMA (0..1, additive, clamped). Shake strength is trauma
//     SQUARED, so one small hit barely nudges the screen while a bomb-cleared
//     wave (many kills stacking trauma) rattles it hard — the nonlinearity is
//     what makes stacked events read as bigger than any single one.
//   * Trauma decays linearly (~0.7s from full), and each tick samples a fresh
//     random offset (design-space px) + a small random roll angle. Both scale
//     with strength, so the shake eases out instead of stopping dead.
//   * Applied at the PRESENT BLIT in Game1.Draw (offset + roll + a slight zoom
//     so the shaken frame keeps covering the letterbox) — the whole composited
//     scene shakes as one, including bloom, and no gameplay coordinate ever
//     changes (collision/aim are untouched; it's purely a camera effect).
//
// HIT-STOP (freeze frames — "impact pause"):
//   * AddHitStop(seconds) freezes GAME time (Game1.Update folds TimeScale into
//     the same turbo*slowmotion scale it already applies) while REAL time keeps
//     ticking this class, the shake, and input polling. Overlapping requests
//     take the MAX, never the sum, so a multi-kill can't freeze the game solid.
//   * KillPunch() is the per-kill micro-stop (~1.5 frames) with a real-time
//     cooldown so a bomb clearing 20 enemies reads as one meaty impact, not a
//     stutter; boss kills get a longer, cooldown-exempt stop.
//
// Tuning/QA:
//   * URL: ?shake=0 (off) / ?shake=1.5 (amplify, 0..3) · ?hitstop=0 (off).
//     Both are pure feel/render toggles, deliberately OUT of DebugFlags.Active.
//   * Console: eaShake() / eaShake(0.8) fires a shake burst on demand;
//     eaHitstop() / eaHitstop(250) a freeze — see DebugInput + index.html.
// Update cadence: Game1.Update calls Update(realDt) ONCE per tick with the
// UNSCALED frame delta (before turbo/slowmo/hit-stop scaling), so shake keeps
// moving and the freeze can end while game time is stopped.
// ---------------------------------------------------------------------------
using System;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    public static class Juice
    {
        // Peak shake at full trauma: max per-axis offset in 800x600 design px, and
        // max roll in degrees. Both sampled fresh every tick, scaled by strength.
        public const float MaxOffsetDesignPx = 14f;
        public const float MaxRollDegrees = 2f;

        // Trauma lost per real second — a full bar shakes for ~0.7s (strength, being
        // trauma^2, falls below "visible" well before trauma itself reaches 0).
        private const float TraumaDecayPerSecond = 1.4f;

        // Per-kill micro freeze: ~1.5 frames at 60Hz, gated by a real-time cooldown
        // so rapid kill chains (a bomb wave) land ONE punch instead of a stutter.
        private const float KillStopSeconds = 0.025f;
        private const float KillStopCooldownSeconds = 0.25f;
        private const float KillTrauma = 0.05f;

        // Boss kills: a longer, cooldown-exempt stop + a real shake — the marquee
        // moment of a level should be the biggest impact the player feels.
        private const float BossStopSeconds = 0.09f;
        private const float BossTrauma = 0.3f;

        private static readonly Random rng = new Random();

        private static float trauma;
        private static float hitStopLeft;
        private static float killStopCooldown;

        // Current sampled shake, consumed by Game1.Draw's present blit. Offset is in
        // design-space px (the blit converts to window px), roll in radians.
        public static Vector2 ShakeOffset { get; private set; }
        public static float ShakeRoll { get; private set; }

        // Current shake strength (trauma^2 x the ?shake= multiplier), 0..~3. Drives
        // the blit's edge-covering zoom as well, so tuning ?shake= keeps them in step.
        public static float ShakeMagnitude { get; private set; }

        public static bool ShakeActive => ShakeMagnitude > 0f;

        // 0 while a hit-stop is freezing game time, else 1. Folded into Game1.Update's
        // existing turbo*slowmotion time scale.
        public static float TimeScale => hitStopLeft > 0f ? 0f : 1f;

        // Add shake energy (0..1 per event; total clamped to 1). Safe from any thread
        // of gameplay code — it only bumps a float the next tick samples from.
        public static void AddTrauma(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            trauma = MathHelper.Clamp(trauma + amount, 0f, 1f);
        }

        // Freeze game time for `seconds` of REAL time. Overlapping requests take the
        // max (never accumulate), so stacked events can't freeze the game solid.
        public static void AddHitStop(float seconds)
        {
            if (!DebugFlags.Hitstop || seconds <= 0f)
            {
                return;
            }
            if (seconds > hitStopLeft)
            {
                hitStopLeft = seconds;
            }
        }

        // The per-kill impact: micro freeze + a tap of shake. Called from the central
        // kill branch (KillableAlien.HitBy); the cooldown makes kill CHAINS read as one
        // punch. Boss kills bypass the cooldown and hit harder.
        public static void KillPunch(bool boss)
        {
            if (boss)
            {
                AddHitStop(BossStopSeconds);
                AddTrauma(BossTrauma);
                return;
            }
            if (killStopCooldown > 0f)
            {
                return;
            }
            killStopCooldown = KillStopCooldownSeconds;
            AddHitStop(KillStopSeconds);
            AddTrauma(KillTrauma);
        }

        // Tick with the UNSCALED frame delta (real seconds). Decays trauma + the
        // cooldowns and samples this tick's shake offset/roll.
        public static void Update(float dt)
        {
            if (dt < 0f || float.IsNaN(dt))
            {
                dt = 0f;
            }
            else if (dt > 0.1f)
            {
                // A stall (tab refocus, GC hitch) shouldn't burn a whole shake/freeze.
                dt = 0.1f;
            }
            if (killStopCooldown > 0f)
            {
                killStopCooldown -= dt;
            }
            if (hitStopLeft > 0f)
            {
                hitStopLeft -= dt;
                if (hitStopLeft < 0f)
                {
                    hitStopLeft = 0f;
                }
            }
            if (trauma > 0f)
            {
                trauma -= TraumaDecayPerSecond * dt;
                if (trauma < 0f)
                {
                    trauma = 0f;
                }
            }
            float strength = trauma * trauma * DebugFlags.ShakeAmount;
            if (strength < 0.0005f)
            {
                ShakeMagnitude = 0f;
                ShakeOffset = Vector2.Zero;
                ShakeRoll = 0f;
                return;
            }
            ShakeMagnitude = strength;
            ShakeOffset = new Vector2(
                MaxOffsetDesignPx * strength * ((float)rng.NextDouble() * 2f - 1f),
                MaxOffsetDesignPx * strength * ((float)rng.NextDouble() * 2f - 1f));
            ShakeRoll = MathHelper.ToRadians(MaxRollDegrees) * strength * ((float)rng.NextDouble() * 2f - 1f);
        }
    }
}
