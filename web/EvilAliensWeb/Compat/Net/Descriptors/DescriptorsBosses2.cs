using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch D: bosses + gnarly world entities (Mars/base side). Contract: NetTypeRegistry.cs;
    // worked example: UfoDescriptor.cs. Internal Net* seams live at the bottom of each type's
    // own file (StationaryBoss has none -- base-only; MarsBoss.cs / Lazer.cs / Wall.cs).

    // StationaryBoss (landed Mothership_landed still, AlienDrawableGameComponent) state surface:
    //   Draw reads base.Position plus the STATIC LandedOffsets.Landed nudge (loaded
    //   deterministically from Content/data/landed_offsets.json in Initialize) and the base
    //   scale. The sprite is a single frame (LoadAnimation with no grid -> 1x1, no animation)
    //   and the mothership never lifts off, so there is no sheet / stance / landed-vs-flying
    //   state beyond the base fields. The only Draw state NOT carried is fakehittimer's white
    //   hit-flash -- a purely cosmetic bullet-hit blink each side drives from its OWN local
    //   bullet collisions (correctly off the wire), so it may differ frame-to-frame between
    //   peers; an accepted divergence.
    // Wire format: BASE-ONLY (no spawn or state extras). CollisionType is a box around
    //   base.Position, so client bullet hit-testing is faithful from the base fields alone.
    internal sealed class StationaryBossDescriptor : NetTypeDescriptor<StationaryBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            StationaryBoss b = StationaryBoss.NewAlien(bin, game);
            b.Setup();
            return b;
        }
    }

    // MarsBoss (KillableAlien, Mars boss mothershipB) state surface:
    //   Setup(BossPosition) only steers the entry flight path (Update-only) -- inert on a frozen
    //   puppet, but replicated for a faithful reconstruction. Draw reads the animated 4x4 sheet
    //   (curframe, a base field) which ALTERNATES between the mothershipA / mothershipB halves
    //   each 16-frame wrap in Update; that A/B swap is NOT a base field, so it is the one piece
    //   of state carried. HP-driven colorize redden tracks via the base Hp block (NetApplyHp).
    //   The charge-up LazerGenerator glow is host-only (never created on a frozen puppet) so it
    //   is not shown on the client; the fired Lazer itself replicates as its own puppet.
    // Spawn extras: [bossPosition:1]   (0 = left, 1 = right)
    // State extras: [flags:1]          (bit0 = showing the mothershipB / second sheet half)
    internal sealed class MarsBossDescriptor : NetTypeDescriptor<MarsBoss>
    {
        private const byte FlagSecondSheet = 1;

        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            buf[off++] = C(c).NetBossPosition;
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            byte pos = len >= 1 ? buf[off] : (byte)0;
            MarsBoss mb = MarsBoss.NewMarsBoss(bin, game);
            mb.Setup((MarsBoss.BossPosition)pos);
            return mb;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            byte flags = 0;
            if (C(c).NetSecondHalf)
            {
                flags |= FlagSecondSheet;
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
            C(c).NetSetSpritesheetHalf((buf[off] & FlagSecondSheet) != 0);
        }
    }

    // Lazer (enemy beam, AlienDrawableGameComponent driving a Quad) state surface:
    //   A growing line collidable that HURTS the local player and is Quad-Draw-heavy -- so it
    //   matters for fairness (the local player dodges it). The AIM (Direction) plus the growing
    //   LENGTH and LEAD are ALL streamed as state extras: they drive both the collision line
    //   (CollisionType: origin = Position + lead*dir, length = len - lead) and the drawn Quad.
    //   The aim is carried in STATE (not spawn) because the puppet driver rewrites base.Direction
    //   from the observed velocity every snapshot (NetSpeedVector setter) -- so the true aim has
    //   to be re-applied last, in ApplyStateExtra. The muzzle Position rides the base fields.
    //   Perfectly-VERTICAL beams are replicated exactly (PiOver2) -- NO tilt is added (the old
    //   DDA hang was fixed with a step cap; a tilt would only desync from the host). The puppet
    //   is built sound-free (SetupSingleShot playSound:false) and ApplyStateExtra plays nothing /
    //   spawns nothing.
    // Spawn extras: none (constructed at aim 0 / len ~0; the first snapshot supplies the beam).
    // State extras: [angle:2][len:2][lead:2]  (angle u16 normalised [0,2pi); len/lead u16 px)
    internal sealed class LazerDescriptor : NetTypeDescriptor<Lazer>
    {
        private const float TwoPi = 6.2831855f;

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            Lazer z = Lazer.NewLazer(bin, game);
            // playSound:false -- a puppet must never fire the beam SFX (it is not the local
            // shooter). Aim 0 / lead 0 for a frame until the first snapshot streams the beam.
            z.SetupSingleShot(state.Pos, 0f, 0f, playSound: false);
            return z;
        }

        public override int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            Lazer z = C(c);
            WriteAngle(buf, ref off, z.NetAngle);
            WriteU16Px(buf, ref off, z.NetLen);
            WriteU16Px(buf, ref off, z.NetLead);
            return off;
        }

        public override void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
            if (len < 6)
            {
                return;
            }
            float angle = NetProtocol.ReadU16(buf, off) / 65535f * TwoPi;
            float length = NetProtocol.ReadU16(buf, off + 2);
            float leadValue = NetProtocol.ReadU16(buf, off + 4);
            C(c).NetApplyBeam(angle, length, leadValue);
        }

        private static void WriteAngle(byte[] buf, ref int off, float angle)
        {
            float a = angle % TwoPi;
            if (a < 0f)
            {
                a += TwoPi;
            }
            ushort v = (ushort)(a / TwoPi * 65535f);
            buf[off++] = (byte)v;
            buf[off++] = (byte)(v >> 8);
        }

        private static void WriteU16Px(byte[] buf, ref int off, float px)
        {
            ushort v = (ushort)MathHelper.Clamp(px, 0f, 65535f);
            buf[off++] = (byte)v;
            buf[off++] = (byte)(v >> 8);
        }
    }

    // Wall (Level-3 scrolling tower grid, AlienDrawableGameComponent) state surface:
    //   Setup(variation) picks one of five block grids (0-4). Everything the frozen Draw and
    //   the CollisionLevelMap need then follows from base.Position (the vertical scroll offset,
    //   dead-reckoned by the driver from the base Vel) plus that fixed grid: scale is computed
    //   deterministically from the variation inside Setup (and also arrives in the base fields),
    //   and the wall is a single texture with no animation. So the variation is the only
    //   caller-chosen input that must be pinned.
    //   BEST-EFFORT: Setup halves the grid HEIGHT on Easy/Medium (difficulty-dependent). The
    //   client rebuilds from ITS Settings difficulty -- exact when the co-op session shares a
    //   difficulty (it does: TeamChallenge locks the menu-chosen difficulty), divergent
    //   otherwise. Grid files (variation 2) load via TitleContainer -- fine on the client.
    // Spawn extras: [variation:1]
    // State extras: none (base Position drives the scroll; the grid is fixed at spawn).
    internal sealed class WallDescriptor : NetTypeDescriptor<Wall>
    {
        public override int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            int v = C(c).NetVariation;
            buf[off++] = (byte)(v < 0 ? 0 : v);
            return off;
        }

        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            int variation = len >= 1 ? buf[off] : 0;
            Wall w = Wall.NewWall(bin, game);
            w.Setup(variation);
            return w;
        }
    }
}
