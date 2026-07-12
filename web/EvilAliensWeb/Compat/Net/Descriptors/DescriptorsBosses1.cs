using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch C: bosses (Level 1 / Classic side). Contract: NetTypeRegistry.cs; worked example:
    // UfoDescriptor.cs. Every puppet here is built by the type's real New*+Setup factory and then
    // FROZEN (Enabled=false forever); its gameplay Update never runs, only Draw. So for each type:
    // any RANDOM/caller-chosen LOOK is pinned via spawn extras, and the Draw-visible state that the
    // frozen Update would otherwise reach (sheet swaps, own-clock animation frames) is host-encoded
    // per snapshot and re-applied on the client. Best-effort is accepted for deep boss internals --
    // the divergences are documented per descriptor. ApplyStateExtra never spawns/plays sounds.

    // JunkBoss (Level 1 eye boss). State surface (JunkBoss.cs):
    //   * Draw draws base.Draw (the eye sheet at `color`, animated by curframe) + a `suckeffect`
    //     particle swarm when it exists.
    //   * The eye has TWO sheets: idle on/off loop (eye_idle 4x2) vs the spin+lightning ATTRACT
    //     sheet (eye_attract 9x8), swapped in UpdateEyeAnim off state==attracting. This swap is the
    //     one Draw-visible state a frozen puppet can't reach -> replicated in a STATE EXTRA; the base
    //     curframe (driver-advanced at the sheet's 12fps) animates within whichever sheet is loaded.
    //   * `color` reddens with HP (Colorize=true; initial HP 150, NOT difficulty-scaled) -> carried
    //     by the base Hp field + KillableAlien.NetApplyHp, which recomputes the redden identically.
    // Spawn extras: none. (Setup(isbase) only gates Update-side ball summoning / danger message /
    //   Presence -- pure behaviour, invisible on a frozen puppet; the puppet is always the non-base
    //   variant.) State extras: [flags:1]  (bit0 = eye showing the attract sheet).
    // Best-effort divergences: the `suckeffect` suck-in swarm and the ~3px sine `ydrawingoffset` bob
    //   are Update-spawned/Update-computed and absent on the puppet (the eye still shows the attract
    //   spin, the main tell). Its multi-explosion `asplode` death sequence does not play -- an
    //   attributed remote death removes the puppet immediately (NetPuppets.OnRemoteDeath).
    internal sealed class JunkBossDescriptor : NetTypeDescriptor<JunkBoss>
    {
        private const byte FlagAttract = 1;

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            JunkBoss j = JunkBoss.NewJunkBoss(bin, game);
            j.Setup(false);
            return j;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            byte flags = 0;
            if (C(c).NetEyeAttracting)
            {
                flags |= FlagAttract;
            }
            buf[off++] = flags;
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetSetEyeAttract((buf[off] & FlagAttract) != 0);
        }
    }

    // Ball (JunkBoss orbit ball). Investigation: the harness omits Ball because in the harness the
    // object's Update RUNS and dereferences a null owner. A PUPPET never runs Update, so a standalone
    // Ball is drawable: it is a small asteroid sprite whose pos/vel/rotation/scale all arrive in the
    // base state. Its CollidesWith is also null-owner-safe (CheckOwner flips a null-owner ball to the
    // `freed` state, which skips every owner deref), so client-side bullet hit-testing is safe too.
    // Therefore NOT a null return -- Ball replicates as a base-state puppet.
    //   * Draw: base.Draw (asteroid sheet) + a hit-blink lightenEffect while hittimer is Active
    //     (ticked by the driver; started locally on the client's own hits). No boss-internal state.
    // Spawn extras: [variant:1]  -- the ctor picks one of AsteroidSmall1..4 at RANDOM; without pinning
    //   the same netId is a different rock on each screen (the UFO small-sheet precedent). State
    //   extras: none (size arrives as base Scale; spin as base Rotation).
    internal sealed class BallDescriptor : NetTypeDescriptor<Ball>
    {
        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)C(c).NetAsteroidVariant;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Ball b = Ball.NewBall(bin, game);
            b.Setup(null); // no live JunkBoss owner -- puppet never Updates, so it is never dereferenced
            b.NetForceAsteroidVariant(len >= 1 ? buf[off] : 1);
            return b;
        }
    }

    // Boss (mothership). Construction: `new Boss(game)` (no recycle factory -- matches BossSpawner) +
    // Setup(pos). State surface (Boss.cs):
    //   * Draw is base.Draw (mothershipB sheet, 4x4, interpolation NEVER), curframe-animated at 16fps
    //     -> replicated by the base CurFrame + driver.
    //   * Update alternates `texture` between the two 16-frame halves (mothershipA/mothershipB) each
    //     time curframe wraps -- a 32-frame loop. That half-select is Draw-visible state the frozen
    //     puppet can't reach -> STATE EXTRA bit; the base curframe drives the 16 frames within it.
    //   * `color` reddens with HP (Colorize=true) via the base Hp + NetApplyHp.
    // Spawn extras: none. State extras: [flags:1]  (bit0 = second half / mothershipB showing).
    // Best-effort: the redden ramp uses the LOCAL initial HP, which IS difficulty-scaled
    //   (SetHitPoints scaleWithDifficulty:true) -- exact only when both peers share difficulty (co-op
    //   does). The fired Lazers are separate replicated entities (LazerDescriptor), not this puppet.
    internal sealed class BossDescriptor : NetTypeDescriptor<Boss>
    {
        private const byte FlagSecondHalf = 1;

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Boss b = new Boss(game);
            b.Setup(state.Pos);
            return b;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            byte flags = 0;
            if (C(c).NetSecondHalf)
            {
                flags |= FlagSecondHalf;
            }
            buf[off++] = flags;
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetSetSpritesheetHalf((buf[off] & FlagSecondHalf) != 0);
        }
    }

    // ClassicBoss (classic-mode boss). Construction: NewClassicBoss + Setup() (empty). State surface
    // (ClassicBoss.cs):
    //   * Draw draws an AnimatedSprite (GFX/alienboss/alienboss) at `animationProgress` -- its OWN
    //     20fps clock, NOT the component curframe -- tinted by `color`, sized by `scale`, blink via
    //     hittimer. `scale` and `color` are HP-driven but arrive in the base state (Scale, and Hp ->
    //     NetApplyHp redden; initial HP 350, NOT difficulty-scaled so the redden matches exactly).
    //   * `animationProgress` is advanced only in the frozen Update, so it would freeze the body on
    //     one frame -> replicated in a STATE EXTRA so the sprite keeps animating.
    // Spawn extras: none. State extras: [animFrame:1]  ((int)animationProgress; always < sprite.Frames
    //   on the host, and any byte is a valid index for the shared sprite, so the apply is bounds-safe).
    // Best-effort: at snapshot cadence (~30Hz) the body animation steps rather than free-runs.
    internal sealed class ClassicBossDescriptor : NetTypeDescriptor<ClassicBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            ClassicBoss cb = ClassicBoss.NewClassicBoss(bin, game);
            cb.Setup();
            return cb;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)C(c).NetAnimFrame;
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetAnimFrame = buf[off];
        }
    }

    // BattleSkull (L3 mini-boss). Construction: NewBattleSkull + Setup(pos). State surface
    // (BattleSkull.cs):
    //   * Draw hue-remaps the alienboss sprite: colorizeEffect.RangeTarget = (-10, 10,
    //     HitPointsNormalized*100), i.e. the target hue sweeps green(full)->red(dead) with HP.
    //   * HP CHECK (per brief): SetHitPoints(25, scaleWithDifficulty:FALSE), so the initial HP is a
    //     fixed 25 on BOTH peers regardless of difficulty. HitPointsNormalized = hp/25 is therefore
    //     reproduced EXACTLY from the replicated absolute Hp (base state) + NetApplyHp -- no need to
    //     pin initial HP in spawn extras, and no seam for it. (Were it difficulty-scaled we would.)
    //   * `animationProgress` (own 20fps clock, Update-advanced) -> STATE EXTRA, same as ClassicBoss.
    // Spawn extras: none. State extras: [animFrame:1].
    // Best-effort: the ~2.5s `dying` shrink/flicker sequence does not play on the puppet -- an
    //   attributed remote death removes it right after the opening pop (NetPuppets.OnRemoteDeath).
    internal sealed class BattleSkullDescriptor : NetTypeDescriptor<BattleSkull>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            BattleSkull bs = BattleSkull.NewBattleSkull(bin, game);
            bs.Setup(state.Pos);
            return bs;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)C(c).NetAnimFrame;
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetAnimFrame = buf[off];
        }
    }
}
