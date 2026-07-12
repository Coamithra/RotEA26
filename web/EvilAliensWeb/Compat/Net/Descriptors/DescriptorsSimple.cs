using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Batch A: simple / ballistic common types. STUBS pending the per-type farm-out --
    // CreatePuppet returning null means "skip replicating this instance" (the entity stays
    // host-only until its real descriptor lands). Contract: NetTypeRegistry.cs; worked
    // example: UfoDescriptor.cs.

    internal sealed class EvilBulletDescriptor : NetTypeDescriptor<EvilBullet>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out A)
        }
    }

    internal sealed class AsteroidDescriptor : NetTypeDescriptor<Asteroid>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out A)
        }
    }

    internal sealed class SweepUfoDescriptor : NetTypeDescriptor<SweepUFO>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out A)
        }
    }

    internal sealed class StarMineDescriptor : NetTypeDescriptor<StarMine>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out A)
        }
    }

    internal sealed class PowerupDescriptor : NetTypeDescriptor<Powerup>
    {
        public override AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len)
        {
            return null; // TODO(farm-out A)
        }
    }
}
