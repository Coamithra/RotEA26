using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch C: bosses (Level 1/Classic side). STUBS pending the per-type farm-out.
    // Contract: NetTypeRegistry.cs; worked example: UfoDescriptor.cs.

    internal sealed class JunkBossDescriptor : NetTypeDescriptor<JunkBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out C)
        }
    }

    internal sealed class BallDescriptor : NetTypeDescriptor<Ball>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out C)
        }
    }

    internal sealed class BossDescriptor : NetTypeDescriptor<Boss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out C)
        }
    }

    internal sealed class ClassicBossDescriptor : NetTypeDescriptor<ClassicBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out C)
        }
    }

    internal sealed class BattleSkullDescriptor : NetTypeDescriptor<BattleSkull>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out C)
        }
    }
}
