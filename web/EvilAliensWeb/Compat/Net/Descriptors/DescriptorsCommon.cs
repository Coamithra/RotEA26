using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch B: animated / stateful medium types. STUBS pending the per-type farm-out.
    // Contract: NetTypeRegistry.cs; worked example: UfoDescriptor.cs.

    internal sealed class BraineroidDescriptor : NetTypeDescriptor<Braineroid>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out B)
        }
    }

    internal sealed class EvilSkullDescriptor : NetTypeDescriptor<EvilSkull>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out B)
        }
    }

    internal sealed class SpiderDescriptor : NetTypeDescriptor<Spider>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out B)
        }
    }

    internal sealed class FlyingSpiderDescriptor : NetTypeDescriptor<FlyingSpider>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out B)
        }
    }

    internal sealed class PunchingBagDescriptor : NetTypeDescriptor<PunchingBag>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out B)
        }
    }

    internal sealed class DeathStarDescriptor : NetTypeDescriptor<DeathStar>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out B)
        }
    }
}
