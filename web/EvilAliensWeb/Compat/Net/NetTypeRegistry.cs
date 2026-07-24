using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Per-replicable-type replication descriptor (card 11.2 "world authority").
    //
    // The generic replicator already carries every AlienDrawableGameComponent base field
    // (NetBaseState: pos / observed vel / rotation / curframe / scale / hp). A descriptor
    // owns the two things the base contract cannot know:
    //
    //   * CONSTRUCTION -- how to build a client puppet through the type's real New*+Setup
    //     factory (the sprite-harness-proven path: every enemy draws correctly with gameplay
    //     Update never running). Spawn extras carry whatever Setup args pick the LOOK
    //     (sheet variant, size class, bonus tint...), not behaviour -- puppets never think.
    //   * STATE EXTRAS -- the per-type fields a frozen Draw reads that the base fields
    //     don't carry (animation-sheet swaps, phase/stance flags, landed stills, beam
    //     endpoints...). Encoded into the snapshot entry after the base block; applied on
    //     the client on every snapshot that includes the entity.
    //
    // Contract rules for descriptor authors (the per-type farm-out):
    //   * Extras are LITTLE-ENDIAN bytes written into scratch buffers; keep them tiny
    //     (0-8 bytes typical) -- the whole snapshot packet budget is ~500 B.
    //   * SpawnExtra/StateExtra lengths may vary per instance but must be self-consistent:
    //     whatever EncodeX writes, CreatePuppet/ApplyStateExtra must fully consume.
    //   * CreatePuppet must call the type's real New*(bin, game) + Setup(...) and then
    //     return WITHOUT adding to the bin -- the puppet layer owns add/freeze/registration.
    //   * ApplyStateExtra runs on the game tick with gameplay Update frozen; it may only
    //     touch draw-relevant state (fields the type's Draw reads). Never spawn components,
    //     never play sounds from it (state repeats every snapshot).
    //   * Private fields are exposed via small `internal` Net* accessors added to the game
    //     type itself (see UFO.cs) -- keep the per-type knowledge in the type.
    //   * A type whose Draw needs nothing beyond the base fields is a BASE-ONLY descriptor:
    //     return 0 from both extras and say why in a comment.
    internal interface INetTypeDescriptor
    {
        // Exact component type this descriptor replicates (wire typeIdx = registry order).
        Type ComponentType { get; }

        // Host: append the spawn-time construction extras for `c` to buf at off; return the
        // new offset. Called once per spawn event (and per late-join replay).
        int EncodeSpawnExtra(AlienDrawableGameComponent c, byte[] buf, int off);

        // Client: construct the puppet via the type's real New*+Setup using the spawn extras
        // (buf[off..off+len)). Return null to skip replicating this instance (a puppet that
        // cannot exist without a live owner, e.g. a Ball with no JunkBoss).
        AlienDrawableGameComponent CreatePuppet(ComponentBin bin, Game game, in NetBaseState state, byte[] buf, int off, int len);

        // Host: append the per-type continuous state extras; return the new offset.
        int EncodeStateExtra(AlienDrawableGameComponent c, byte[] buf, int off);

        // Client: apply the state extras (buf[off..off+len)) to the frozen puppet.
        void ApplyStateExtra(AlienDrawableGameComponent c, byte[] buf, int off, int len);
    }

    // The ordered replicable-type table. ORDER IS THE WIRE FORMAT (typeIdx byte in spawn
    // events + snapshot entries): append-only, never reorder. The set mirrors 11.1's
    // NetIdRegistry replicables = Oracle.GetBaddies' enemy types minus Explosion (cosmetics
    // never cross the wire) plus Powerup.
    internal static class NetTypeRegistry
    {
        private static readonly INetTypeDescriptor[] descriptors = BuildTable();
        private static readonly Dictionary<Type, byte> indexByType = BuildIndex();

        private static INetTypeDescriptor[] BuildTable()
        {
            return new INetTypeDescriptor[]
            {
                new Descriptors.EvilBulletDescriptor(),     // 0
                new Descriptors.UfoDescriptor(),            // 1
                new Descriptors.AsteroidDescriptor(),       // 2
                new Descriptors.BraineroidDescriptor(),     // 3
                new Descriptors.JunkBossDescriptor(),       // 4
                new Descriptors.BallDescriptor(),           // 5
                new Descriptors.BossDescriptor(),           // 6
                new Descriptors.SpiderDescriptor(),         // 7
                new Descriptors.StationaryBossDescriptor(), // 8
                new Descriptors.MarsBossDescriptor(),       // 9
                new Descriptors.EvilSkullDescriptor(),      // 10
                new Descriptors.LazerDescriptor(),          // 11
                new Descriptors.ClassicBossDescriptor(),    // 12
                new Descriptors.DeathStarDescriptor(),      // 13
                new Descriptors.WallDescriptor(),           // 14
                new Descriptors.BattleSkullDescriptor(),    // 15
                new Descriptors.FlyingSpiderDescriptor(),   // 16
                new Descriptors.StarMineDescriptor(),       // 17
                new Descriptors.SweepUfoDescriptor(),       // 18
                new Descriptors.PunchingBagDescriptor(),    // 19
                new Descriptors.PowerupDescriptor(),        // 20
                // --- card "world-authority coverage gaps": types 11.2 left host-only ---
                new Descriptors.PlasmaBallDescriptor(),             // 21
                new Descriptors.ParatrooperAlienDescriptor(),       // 22
                new Descriptors.ParatrooperBrainDescriptor(),       // 23
                new Descriptors.ParachuteDescriptor(),              // 24
                new Descriptors.FakeBossDescriptor(),               // 25
                new Descriptors.SpiderHelperMothershipDescriptor(), // 26
                new Descriptors.SpiderBossDescriptor(),             // 27
                new Descriptors.BrainBossDescriptor(),              // 28
            };
        }

        private static Dictionary<Type, byte> BuildIndex()
        {
            var map = new Dictionary<Type, byte>(descriptors.Length);
            for (int i = 0; i < descriptors.Length; i++)
            {
                map.Add(descriptors[i].ComponentType, (byte)i);
            }
            return map;
        }

        public static int Count => descriptors.Length;

        // Exact-type lookup: no replicable type subclasses another (verified against the
        // class hierarchy; WebcamUfo etc. live outside the co-op-eligible levels).
        public static bool TryGet(GameComponent c, out byte typeIdx, out INetTypeDescriptor descriptor)
        {
            if (c != null && indexByType.TryGetValue(c.GetType(), out typeIdx))
            {
                descriptor = descriptors[typeIdx];
                return true;
            }
            typeIdx = 0;
            descriptor = null;
            return false;
        }

        public static INetTypeDescriptor Get(byte typeIdx)
        {
            return typeIdx < descriptors.Length ? descriptors[typeIdx] : null;
        }

        public static bool IsReplicable(GameComponent c)
        {
            return c != null && indexByType.ContainsKey(c.GetType());
        }

        // Type-level replicable AND not an instance that has opted out as pure scenery (card
        // 9a3175d0). This is the predicate the LIVE world asks; IsReplicable is the type table.
        //
        // Every site that decides whether a component participates in replication must use this
        // one, not IsReplicable -- most obviously NetSession.SuppressWorldSpawn, where getting it
        // wrong is silent and total: the client's own cosmetic spawns would be diverted into the
        // recycle pool by the bin and the joiner would see no scenery at all, with no counter
        // moving anywhere.
        public static bool IsReplicableInstance(GameComponent c)
        {
            return IsReplicable(c) && !(c is AlienDrawableGameComponent a && a.NetCosmeticOnly);
        }
    }
}
