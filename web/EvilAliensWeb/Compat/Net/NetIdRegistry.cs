using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Host-side NetId registry on the ComponentBin seam: every replicable component gets a
    // ushort id when it actually enters Game.Components (adds are deferred through the bin's
    // birthList, so hooking the collection's own events -- the same seam Oracle uses -- sees
    // exactly the live world) and frees it when it leaves. Spawn/death are forwarded to
    // NetSession as reliable events.
    //
    // Card 11.1 scope: the ids only exercise the event lane + client-side ordering metrics.
    // Card 11.3 keys the world snapshot + client puppet construction off these same ids.
    public static class NetIdRegistry
    {
        private static readonly Dictionary<GameComponent, ushort> ids = new Dictionary<GameComponent, ushort>();
        private static readonly HashSet<ushort> inUse = new HashSet<ushort>();
        private static ushort next = 1;
        private static bool enabled;

        public static int LiveCount => ids.Count;

        public static void Enable(Game game)
        {
            if (enabled)
            {
                return;
            }
            enabled = true;
            game.Components.ComponentAdded += Components_ComponentAdded;
            game.Components.ComponentRemoved += Components_ComponentRemoved;
        }

        // Replay the full live set (used when a peer connects mid-world so its ordering
        // bookkeeping starts from the truth, not from a death-before-spawn storm).
        internal static void ReplayLive()
        {
            foreach (KeyValuePair<GameComponent, ushort> kv in ids)
            {
                NetSession.OnHostSpawn(kv.Value, kv.Key.GetType().Name);
            }
        }

        private static void Components_ComponentAdded(object src, GameComponentCollectionEventArgs args)
        {
            if (args.GameComponent is GameComponent gc && IsReplicable(gc) && !ids.ContainsKey(gc))
            {
                ushort id = AllocId();
                ids[gc] = id;
                NetSession.OnHostSpawn(id, gc.GetType().Name);
            }
        }

        private static void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs args)
        {
            if (args.GameComponent is GameComponent gc && ids.TryGetValue(gc, out ushort id))
            {
                ids.Remove(gc);
                inUse.Remove(id);
                NetSession.OnHostDeath(id);
            }
        }

        private static ushort AllocId()
        {
            // Wrapping counter, skipping ids still live (65k concurrent is unreachable here).
            // `next` starts at 1 and re-wraps to 1, so an allocated id is never 0.
            ushort id;
            do
            {
                id = next++;
                if (next == 0)
                {
                    next = 1;
                }
            }
            while (!inUse.Add(id));
            return id;
        }

        // The replication set: Oracle.GetBaddies' enemy types (minus Explosion -- cosmetics
        // never cross the wire, they spawn locally from events) plus Powerups.
        private static bool IsReplicable(GameComponent val)
        {
            return val is EvilBullet || val is UFO || val is Asteroid || val is Braineroid
                || val is JunkBoss || val is Ball || val is Boss || val is Spider
                || val is StationaryBoss || val is MarsBoss || val is EvilSkull || val is Lazer
                || val is ClassicBoss || val is DeathStar || val is Wall || val is BattleSkull
                || val is FlyingSpider || val is StarMine || val is SweepUFO || val is PunchingBag
                || val is Powerup;
        }
    }
}
