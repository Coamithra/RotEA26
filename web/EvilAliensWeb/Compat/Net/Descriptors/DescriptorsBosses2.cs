using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch D: bosses + gnarly world entities (Mars/base side). STUBS pending the per-type
    // farm-out. Contract: NetTypeRegistry.cs; worked example: UfoDescriptor.cs.

    internal sealed class StationaryBossDescriptor : NetTypeDescriptor<StationaryBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out D)
        }
    }

    internal sealed class MarsBossDescriptor : NetTypeDescriptor<MarsBoss>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out D)
        }
    }

    internal sealed class LazerDescriptor : NetTypeDescriptor<Lazer>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out D)
        }
    }

    internal sealed class WallDescriptor : NetTypeDescriptor<Wall>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out D)
        }
    }
}
