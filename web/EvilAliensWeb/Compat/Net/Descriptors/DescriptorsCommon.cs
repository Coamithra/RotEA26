using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch B: animated / stateful medium types (contract: NetTypeRegistry.cs; worked example:
    // UfoDescriptor.cs). Every client puppet is built by the type's real New*+Setup, added, then
    // frozen (Enabled=false): its Draw runs every frame, its gameplay Update never does. So each
    // descriptor owns (a) SPAWN EXTRAS -- the Setup args / random look picks that select the LOOK,
    // and (b) STATE EXTRAS -- the fields a frozen Draw (or the CollisionType a client bullet hits)
    // reads that the base block (pos/vel/rotation/curframe/scale/hp) does not carry.

    // Braineroid: an animated 20-frame cyborg brain (KillableAlien, Colorize=false so no hp tint).
    // Draw reads: the size-derived scale/DrawOrder that Initialize picks (huge x2 & DrawOrder 20,
    // medium x1, small x0.35 & DrawOrder 800 -- a real layering difference), the pulsate breathe
    // (carried by the base-state scale + the driver's client-side scale lerp), curframe (base state
    // + NetAdvanceFrame), and -- only for a bonus-carrying brain -- a colorize hue keyed off
    // bonus.type. initialrotation/wrapping steer Update only (never runs) so they are not sent;
    // rotation rides the base state. size MUST be pinned (it drives scale + DrawOrder in Initialize).
    // Spawn extras: [flags:1][bonusType:1]  (flags: bits0-1 = BrainSize, bit2 = hasbonus)
    // State extras: [flags:1]               (bit0 = hasbonus; bonus can only ever turn OFF in play
    //   (dropped on death), so a late clear is cosmetic-safe -- mirrors UFO)
    internal sealed class BraineroidDescriptor : NetTypeDescriptor<Braineroid>
    {
        private const byte SizeMask = 3;
        private const byte FlagBonus = 4;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            Braineroid b = C(c);
            byte flags = (byte)((byte)b.NetSize & SizeMask);
            if (b.NetHasBonus)
            {
                flags |= FlagBonus;
            }
            buf[off++] = flags;
            buf[off++] = b.NetBonusType;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte flags = len >= 1 ? buf[off] : (byte)0;
            Braineroid b = Braineroid.NewBraineroid(bin, game);
            b.Setup(state.Pos, (BrainSize)(flags & SizeMask), 0f, wrapping: false);
            if ((flags & FlagBonus) != 0 && len >= 2)
            {
                b.NetMakeBonus((Powerup.PowerupType)buf[off + 1]);
            }
            return b;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)(C(c).NetHasBonus ? FlagBonus : 0);
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            Braineroid b = C(c);
            if ((buf[off] & FlagBonus) == 0 && b.NetHasBonus)
            {
                b.NetClearBonus();
            }
        }
    }

    // EvilSkull: a fading face-of-death (KillableAlien, Colorize=false). Setup(pos, behaviour)
    // configures the fade (normal: fade in + lifetime running; classic: no fade). Draw reads:
    // the fade alpha (from the fade timers) and -- for a bonus skull -- a colorize hue. CRUCIAL:
    // the public Fading state gates PlayerShip collision (a fading skull is intangible), and Draw's
    // alpha comes from the same timers -- but the puppet's Update never runs to START the fade-out
    // when its lifetime ends, so the phase is replicated and NetSetFadePhase drives the timers
    // (NetTickTimers then advances them for the alpha ramp). justspawned is cleared on build so a
    // stray contact can't teleport the puppet.
    // Spawn extras: [flags:1][bonusType:1]  (flags: bit0 = classic, bit1 = hasbonus)
    // State extras: [flags:1]               (bits0-1 = fade phase 0/1/2, bit2 = hasbonus)
    internal sealed class EvilSkullDescriptor : NetTypeDescriptor<EvilSkull>
    {
        private const byte FlagClassic = 1;
        private const byte FlagSpawnBonus = 2;
        private const byte PhaseMask = 3;
        private const byte FlagStateBonus = 4;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            EvilSkull s = C(c);
            byte flags = 0;
            if (s.NetBehaviour == EnemyBehaviour.classic)
            {
                flags |= FlagClassic;
            }
            if (s.NetHasBonus)
            {
                flags |= FlagSpawnBonus;
            }
            buf[off++] = flags;
            buf[off++] = s.NetBonusType;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte flags = len >= 1 ? buf[off] : (byte)0;
            EvilSkull s = EvilSkull.NewEvilSkull(bin, game);
            s.Setup(state.Pos, (flags & FlagClassic) != 0 ? EnemyBehaviour.classic : EnemyBehaviour.normal);
            s.NetSettle();
            if ((flags & FlagSpawnBonus) != 0 && len >= 2)
            {
                s.NetMakeBonus((Powerup.PowerupType)buf[off + 1]);
            }
            return s;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            EvilSkull s = C(c);
            byte flags = (byte)(s.NetFadePhase & PhaseMask);
            if (s.NetHasBonus)
            {
                flags |= FlagStateBonus;
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
            EvilSkull s = C(c);
            s.NetSetFadePhase((byte)(buf[off] & PhaseMask));
            if ((buf[off] & FlagStateBonus) == 0 && s.NetHasBonus)
            {
                s.NetClearBonus();
            }
        }
    }

    // Spider (Mars jumping spider): grounded rear-up sheet vs airborne spiderjump sheet. Spider.Draw
    // branches PURELY on hasJumped -- grounded => base.Draw (the rear-up sheet, curframe-animated);
    // airborne => the spiderjump sheet whose frame self-drives off the wall clock, tumbling by the
    // base-state rotation and arcing by the base-state position. So the only continuous state beyond
    // the base block is the single airborne (hasJumped) bit; the jump arc, the land-frame snap and
    // the shadow all fall out of base pos/rotation/curframe. Initialize randomises a grey tint
    // (DarkGray/White/DimGray) -- a spawn-time LOOK pick, forced onto the host's choice.
    // Spawn extras: [colorIdx:1]  (0 DarkGray, 1 White, 2 DimGray)
    // State extras: [flags:1]     (bit0 = airborne)
    internal sealed class SpiderDescriptor : NetTypeDescriptor<Spider>
    {
        private const byte FlagAirborne = 1;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = C(c).NetColorIndex;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Spider s = Spider.NewSpider(bin, game);
            s.Setup();
            s.NetForceColor(len >= 1 ? buf[off] : (byte)0);
            return s;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)(C(c).NetAirborne ? FlagAirborne : 0);
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 1)
            {
                return;
            }
            C(c).NetAirborne = (buf[off] & FlagAirborne) != 0;
        }
    }

    // FlyingSpider: background-fog spider vs foreground spider, chosen by Setup(bool isbackground).
    // That bool picks the ENTIRE look -- fog alpha 0.2 + smaller scale + DrawOrder 1 + Collides=false
    // + the group-flatten Draw path (background) vs opaque + full scale + DrawOrder 20 + Collides
    // (foreground) -- so it is pinned as the construction arg. The wing flap (flaptimer, ticked by
    // NetTickTimers), the vertical bob (carried by base pos), and curframe (base + NetAdvanceFrame)
    // all self-animate, so there is NO continuous state extra. Foreground spiders also take a random
    // grey tint (background ones are forced to the fog colour), forced onto the host's pick.
    // Spawn extras: [flags:1]  (bit0 = isbackground, bits1-2 = colorIdx for foreground)
    // State extras: none -- everything continuous rides base fields + self-driven timers.
    internal sealed class FlyingSpiderDescriptor : NetTypeDescriptor<FlyingSpider>
    {
        private const byte FlagBackground = 1;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            FlyingSpider f = C(c);
            byte flags = 0;
            if (f.NetIsBackground)
            {
                flags |= FlagBackground;
            }
            flags |= (byte)((f.NetColorIndex & 3) << 1);
            buf[off++] = flags;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte flags = len >= 1 ? buf[off] : (byte)0;
            FlyingSpider f = FlyingSpider.NewFlyingSpider(bin, game);
            f.Setup((flags & FlagBackground) != 0);
            f.NetForceColor((byte)((flags >> 1) & 3));
            return f;
        }
    }

    // PunchingBag: the tutorial target. BASE-ONLY. No Setup args (built straight from the ctor); no
    // random look pick, no sheet swap, no fade. Draw's only non-base ingredient is a vertical bob
    // (ydrawingoffset) computed from the wall clock in Draw itself, so it self-animates on a frozen
    // puppet; curframe rides base + NetAdvanceFrame; HP is pinned at 100 so the base hp-colorize is
    // constant. Nothing beyond the base block to send.
    internal sealed class PunchingBagDescriptor : NetTypeDescriptor<PunchingBag>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return PunchingBag.NewPunchingBag(bin, game);
        }
    }

    // DeathStar: a homing star mine. Draw is just base.Draw -- no charge/attack visuals -- so a
    // frozen puppet needs nothing beyond the base fields + curframe (25 fps sheet, NetAdvanceFrame).
    // Setup(pos, behaviour); behaviour only steers Update's classic wall-bounce (never runs on a
    // puppet, and the trajectory rides base pos/vel regardless), so it is pinned purely for
    // construction fidelity, not because it changes the frozen look.
    // Spawn extras: [behaviour:1]  (0 normal, 1 classic)
    // State extras: none.
    internal sealed class DeathStarDescriptor : NetTypeDescriptor<DeathStar>
    {
        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = (byte)(C(c).NetBehaviour == EnemyBehaviour.classic ? 1 : 0);
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte b = len >= 1 ? buf[off] : (byte)0;
            DeathStar d = DeathStar.NewDeathStar(bin, game);
            d.Setup(state.Pos, b != 0 ? EnemyBehaviour.classic : EnemyBehaviour.normal);
            return d;
        }
    }
}
