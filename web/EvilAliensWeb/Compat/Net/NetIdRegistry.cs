using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Host-side NetId registry on the ComponentBin seam: every replicable component gets a
    // ushort id when it actually enters Game.Components (bin adds are instant since card
    // 02d9ad67, so the collection's own events -- the same seam Oracle uses -- fire right in
    // Add and always see exactly the live world) and frees it when it leaves. Spawn/death are forwarded to
    // NetSession as reliable events; the live list is what the world-snapshot scheduler
    // round-robins over (card 11.2).
    //
    // The replication set is NetTypeRegistry's descriptor table (11.1's Oracle.GetBaddies
    // enemy types minus Explosion, plus Powerup -- cosmetics never cross the wire).
    internal static class NetIdRegistry
    {
        internal sealed class Entry
        {
            public AlienDrawableGameComponent Comp;
            public ushort Id;
            public byte TypeIdx;
            public INetTypeDescriptor Descriptor;
            // Observed-velocity tracking for the snapshot's base state: many enemies move
            // Position directly (arcs, easing) rather than via Speed/Direction, so the
            // encoder differentiates real positions between the entity's snapshot turns.
            public Vector2 LastPos;
            public long LastPosMs;
            public bool HasLastPos;
            // What the per-type death path credited, per slot (card b0ab09ec). Lazily allocated
            // -- most entities never award (they despawn) and this is per LIVE entity, so it
            // must not cost an array each. Filled by NetSession.NoteAward during KilledBy, read
            // one tick later by OnHostDeath at the removal seam.
            public float[] Awards;
        }

        private static readonly Dictionary<GameComponent, Entry> entries = new Dictionary<GameComponent, Entry>();
        private static readonly Dictionary<ushort, Entry> byId = new Dictionary<ushort, Entry>();
        private static readonly List<Entry> liveList = new List<Entry>(); // round-robin order
        private static ushort next = 1;
        private static bool enabled;

        public static int LiveCount => liveList.Count;

        public static IReadOnlyList<Entry> Live => liveList;

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

        // Session teardown (card 11.4: menu sessions end when the match does). A later
        // Enable starts fresh; `next` deliberately keeps counting so ids from a dead
        // session never collide with a new one inside a peer's recent-death windows.
        public static void Disable(Game game)
        {
            if (!enabled)
            {
                return;
            }
            enabled = false;
            game.Components.ComponentAdded -= Components_ComponentAdded;
            game.Components.ComponentRemoved -= Components_ComponentRemoved;
            entries.Clear();
            byId.Clear();
            liveList.Clear();
        }

        public static bool TryGetById(ushort id, out Entry entry)
        {
            return byId.TryGetValue(id, out entry);
        }

        public static bool TryGetByComp(GameComponent comp, out Entry entry)
        {
            return entries.TryGetValue(comp, out entry);
        }

        // Replay the full live set (used when a peer connects mid-world so it can build the
        // already-alive puppets instead of starting from a death-before-spawn storm).
        internal static void ReplayLive()
        {
            foreach (Entry e in liveList)
            {
                NetSession.OnHostSpawn(e);
            }
        }

        private static void Components_ComponentAdded(object src, GameComponentCollectionEventArgs args)
        {
            if (args.GameComponent is GameComponent gc && !entries.ContainsKey(gc)
                && gc is AlienDrawableGameComponent comp && NetTypeRegistry.TryGet(gc, out byte typeIdx, out INetTypeDescriptor desc))
            {
                Entry e = new Entry
                {
                    Comp = comp,
                    Id = AllocId(),
                    TypeIdx = typeIdx,
                    Descriptor = desc,
                };
                entries[gc] = e;
                byId[e.Id] = e;
                liveList.Add(e);
                NetSession.OnHostSpawn(e);
            }
        }

        private static void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs args)
        {
            if (args.GameComponent is GameComponent gc && entries.TryGetValue(gc, out Entry e))
            {
                entries.Remove(gc);
                byId.Remove(e.Id);
                liveList.Remove(e);
                NetSession.OnHostDeath(e);
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
            while (byId.ContainsKey(id));
            return id;
        }
    }
}
