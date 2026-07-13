using System;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net.Descriptors
{
    // Typed convenience base for replication descriptors (contract: INetTypeDescriptor in
    // NetTypeRegistry.cs). Defaults are BASE-ONLY (no extras); override what the type needs.
    internal abstract class NetTypeDescriptor<T> : INetTypeDescriptor where T : AlienDrawableGameComponent
    {
        public Type ComponentType => typeof(T);

        public virtual int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            return off;
        }

        public abstract AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len);

        public virtual int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off)
        {
            return off;
        }

        public virtual void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len)
        {
        }

        protected static T C(AlienDrawableGameComponent c)
        {
            return (T)c;
        }
    }
}
