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
//     stutter; boss kills get a longer, cooldown-exempt stop. **Both are OFF by
//     default** — a micro-stutter on every kill read as the game hitching, not
//     juice (Trello bd5efd9d) — gated on DebugFlags.Hitstop (default false;
//     ?hitstop=1 re-enables for A/B). The kill's screen-shake trauma is
//     untouched either way. Player-death hit-stop (PlayerShip.Asplode/
//     AsplodeWall) calls AddHitStop directly and is NOT gated by that flag — a
//     beat when the PLAYER is destroyed reads as intentional, not stuttery.
//     The eaHitstop() console/JS hook also calls AddHitStop directly, so it
//     always fires on demand regardless of the flag.
//   * ONLINE CO-OP REFUSES EVERY HIT-STOP, whatever the caller (card 68f62e92).
//     A freeze halts this peer's whole world while the wire keeps streaming on
//     the real clock, and the peer's puppets then get corrected BACKWARD — the
//     "when P1 dies the whole game rewinds" report. See AddHitStop for the full
//     mechanism; `?nethitstop=1` restores the pre-card behaviour. Shake is
//     untouched (present-blit only, no gameplay time).
//
// Tuning/QA:
//   * URL: ?shake=0 (off) / ?shake=1.5 (amplify, 0..3) · ?hitstop=1 (re-enable
//     the per-kill/boss-kill micro-stop, off by default — see above).
//     Those two are pure feel/render toggles, deliberately OUT of
//     DebugFlags.Active. ?nethitstop=1 is the third and is the opposite case:
//     it lets a hit-stop freeze game time inside a co-op session again, i.e. it
//     reintroduces the desync above, so it is IN DebugFlags.Active.
//   * Console: eaShake() / eaShake(0.8) fires a shake burst on demand;
//     eaShake.state() reads the PEAK offset/roll/zoom since the last call;
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
        // Peak shake at full trauma: max per-axis offset in 800x600 design px, max roll
        // in degrees, and the blit's edge-covering zoom coefficient. Offset and roll are
        // re-rolled from a uniform random every tick; the zoom is not randomised. All
        // three scale with `strength`.
        //
        // HALVED TWICE. 14/2 -> 7/1 (Trello 8e439865) because full trauma was strong
        // enough to impact gameplay -- readability of bullets and aim -- rather than
        // just being juice; then 7/1/0.06 -> 3.5/0.5/0.03 (card 085ebddc), the owner
        // asking for "a global reduction by 50% across the board".
        //
        // THE ZOOM IS PART OF "THE MAGNITUDE" AND MUST MOVE WITH THEM -- and the reason
        // is arithmetic, not taste. It exists to keep the letterbox from showing at the
        // frame edges: containment of the axis-aligned destination rect inside the
        // rotated, offset, scaled quad needs
        //
        //     Z >= A/300 + (4/3) * radians(R)
        //
        // (A = MaxOffsetDesignPx, R = MaxRollDegrees; the vertical axis is the tighter
        // one on a 4:3 design frame). At the shipped values that is
        // 0.01167 + 0.01164 = 0.02330, so Z = 0.03 is a **1.28x margin** -- and the
        // pre-card 7/1/0.06 triple had 0.04686 against 0.06, i.e. **1.281x**. The
        // halving preserves the shipped safety factor exactly, which is what makes it
        // safe; it is not "lots of spare cover".
        //
        // SO DO NOT READ SPARE ROOM INTO THIS. **The roll is half the budget** (0.01164
        // of 0.02330): dropping the zoom on its own -- say to 0.02, which looks generous
        // beside a 3.5px offset -- exposes black at the edge on every strong shake.
        // Verified by brute force over all four sign choices, sixteen window shapes
        // (4:3, 16:9, 21:9, portrait, 4K, sizes with integer WindowDestRect rounding)
        // and strength swept 0.05..3.0: worst-case Zmin = 0.023496. `?shake=` is safe at
        // its 3x ceiling because offset, roll and zoom all scale by the same `strength`,
        // so the condition is scale-invariant.
        public const float MaxOffsetDesignPx = 3.5f;
        public const float MaxRollDegrees = 0.5f;
        public const float MaxBlitZoom = 0.03f;

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

        // PEAK sampled shake since the last read, read-and-cleared (card 085ebddc). The offset and
        // roll are re-rolled from a uniform random EVERY tick, so no single frame is the maximum
        // and a one-shot reading of ShakeOffset says almost nothing -- a build with the peak
        // halved and one with it intact both produce small values most ticks. A running peak over
        // a burst is the observable that separates them, and it is the only one: shake is applied
        // at the present blit, so it moves no gameplay state and a screenshot of it is a frame of
        // a moving thing. Spent on read so two consecutive reads describe two different windows.
        //
        // PRIVATE, with TakePeaks the sole accessor, deliberately: a non-destructive property read
        // cannot coexist with a destructive take -- a second reader would silently eat the first
        // one's window.
        private static float peakOffsetPx;

        private static float peakRollDegrees;

        private static float peakZoom;

        // The live trauma, for the same readback -- it is what says whether a burst is still
        // running, i.e. whether a zero peak means "damped" or "never fired".
        public static float TraumaNow => trauma;

        // Put the trauma back where a suite found it. Menu-runnable suites that drive real death
        // paths add REAL trauma (Explosion.Initialize does), and a suite that leaves the menu
        // rattling is not leave-no-trace -- measured at 0.93 after one eaSfxBurst() run before
        // this existed. Write-only test seam, in the TakePeaks/SetSfxCoalesceForTest spirit;
        // nothing in production calls it.
        internal static void SetTraumaForTest(float value)
        {
            trauma = MathHelper.Clamp(value, 0f, 1f);
        }

        // The zoom the PRESENT BLIT actually drew with, reported by Game1 rather than recomputed
        // here (card 085ebddc). Recomputing `MaxBlitZoom * strength` in Update would be a second
        // copy of the blit's own expression, so the readback would restate the constant instead of
        // observing the draw -- and a Game1 that dropped the zoom entirely (the very shipping bug
        // the zoom exists to prevent) would still measure perfectly. Mutation-proven: with the peak
        // taken here, `float zoom = 1f;` at the blit left the probe GREEN.
        //
        // Takes the FULL factor and stores the coefficient, so it reads on the same scale as the
        // constant. Only called while ShakeActive, i.e. only while there is a shake to measure.
        public static void NoteBlitZoom(float zoomFactor)
        {
            peakZoom = Math.Max(peakZoom, zoomFactor - 1f);
        }

        public static void TakePeaks(out float offsetPx, out float rollDegrees, out float zoom)
        {
            offsetPx = peakOffsetPx;
            rollDegrees = peakRollDegrees;
            zoom = peakZoom;
            peakOffsetPx = 0f;
            peakRollDegrees = 0f;
            peakZoom = 0f;
        }

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

        // Would a hit-stop request be refused right now? True only inside an online
        // co-op session (card 68f62e92) — see AddHitStop for why. `?nethitstop=1`
        // restores the pre-card behaviour and is the deliberate bug reproduction.
        public static bool HitStopSuppressed =>
            Net.NetSession.Active && !DebugFlags.NetHitstop;

        // Freeze game time for `seconds` of REAL time. Overlapping requests take the
        // max (never accumulate), so stacked events can't freeze the game solid.
        // NOT gated by DebugFlags.Hitstop here — that flag only governs whether
        // KillPunch's automatic per-kill/boss-kill stop fires (see below); a direct
        // caller (player death, the eaHitstop() console/JS hook) always gets its freeze.
        //
        // ONLINE CO-OP REFUSES EVERY HIT-STOP (card 68f62e92), and the reason is not
        // "feel" — a freeze here desyncs the two worlds. Game1.UpdateScaled folds
        // TimeScale into the gameTime it hands UpdateInner, so a freeze halts this
        // peer's WHOLE world (every host-authoritative enemy included) while
        // NetSession.Update keeps streaming on the real clock: the peer receives ~180ms
        // of snapshots carrying UNCHANGED positions while its own NetPuppets.Drive keeps
        // dead-reckoning forward on real time (deliberately — see Drive's header). The
        // corrections that follow then glide every replicated enemy BACKWARD at once,
        // which is what "when P1 dies the whole game rewinds a bit" was. Symmetric, so
        // it is gated for both roles: a client freeze stalls its own ship stream and the
        // host's ShipStateBuffer pays the same price.
        // Shake is NOT gated with it — that is applied at the present blit and touches
        // no gameplay time, so the death still reads as an impact.
        public static void AddHitStop(float seconds)
        {
            if (seconds <= 0f || HitStopSuppressed)
            {
                return;
            }
            if (seconds > hitStopLeft)
            {
                hitStopLeft = seconds;
            }
        }

        // The per-kill impact: a tap of shake, always; a micro freeze-frame ONLY if
        // DebugFlags.Hitstop is on (default false — Trello bd5efd9d: the freeze read as
        // a stutter, not juice). Called from the central kill branch
        // (KillableAlien.HitBy); the cooldown makes kill CHAINS read as one punch (and
        // gates the freeze the same way whether or not it's actually enabled). Boss
        // kills bypass the cooldown and hit harder.
        public static void KillPunch(bool boss)
        {
            if (boss)
            {
                if (DebugFlags.Hitstop)
                {
                    AddHitStop(BossStopSeconds);
                }
                AddTrauma(BossTrauma);
                return;
            }
            if (killStopCooldown > 0f)
            {
                return;
            }
            killStopCooldown = KillStopCooldownSeconds;
            if (DebugFlags.Hitstop)
            {
                AddHitStop(KillStopSeconds);
            }
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
            // Off what was just SAMPLED, not off the constants -- a peak recomputed from
            // MaxOffsetDesignPx would restate the number under test instead of measuring it.
            // (The zoom's peak is NOT taken here for exactly that reason; Game1 reports the
            // factor it drew with, through NoteBlitZoom.)
            peakOffsetPx = Math.Max(peakOffsetPx,
                Math.Max(Math.Abs(ShakeOffset.X), Math.Abs(ShakeOffset.Y)));
            peakRollDegrees = Math.Max(peakRollDegrees, Math.Abs(MathHelper.ToDegrees(ShakeRoll)));
        }
    }
}
