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
    // enemy types minus Explosion, plus Powerup -- cosmetics never cross the wire), minus the
    // per-INSTANCE opt-outs (card 9a3175d0: a decorative background swarm replicates as one
    // spawner beat, so its entities never take an id or a snapshot turn here).
    internal static class NetIdRegistry
    {
        internal sealed class Entry
        {
            // Through INetEntity since step 2c-ii. `entries` below still keys on the
            // GameComponent, because the seam this registry hangs off IS the collection's own
            // add/remove events -- identity is deliberately not on INetEntity (see its header).
            public INetEntity Comp;
            public ushort Id;
            public byte TypeIdx;
            public INetTypeDescriptor Descriptor;
            // Observed-velocity tracking for the snapshot's base state: many enemies move
            // Position directly (arcs, easing) rather than via Speed/Direction, so the
            // encoder differentiates real positions between the entity's snapshot turns.
            public Vector2 LastPos;
            public long LastPosMs;
            public bool HasLastPos;
            // THE hp THIS ENTITY WAS LAST BROADCAST WITH, or -1 if it has not been yet (card
            // d108c459). Diagnostic only -- nothing in the session or the encoder reads it, and
            // it is written at the one point the value leaves the host (the snapshot entry, and
            // the catch-up spawn's base state).
            //
            // It is the HOST half of the client's PuppetInfo.LastAppliedHp, and the pair is what
            // makes hp comparable across two peers at all: a live `hp` on each end is two
            // different quantities (the client subtracts damage it has dealt locally; the host
            // has moved on since this entity's snapshot turn), while these two are both "what
            // crossed the wire" and must agree. Measured before the pair existed: a Boss read
            // 211 against 179, and no tolerance narrow enough to be worth having covered it.
            //
            // WELL-DEFINED PER PEER because a snapshot is ONE packet broadcast to everyone
            // (SendSnapshot writes a single buffer and hands it to transport.SendStream), so
            // there is no per-peer subset for this to be ambiguous about.
            public int LastSentHp = -1;
            // What the per-type death path credited, per slot (card b0ab09ec). Lazily allocated
            // -- most entities never award (they despawn) and this is per LIVE entity, so it
            // must not cost an array each. Filled by NetSession.NoteAward during KilledBy, read
            // one tick later by OnHostDeath at the removal seam.
            public float[] Awards;
            // The claim ledger for the window BEFORE this entity has a death record (card
            // 1bfcd705). NetSession.recentDeaths is only written at the removal seam, one
            // ComponentBin flush after a claim settles the entity -- so in between there is
            // nothing to pay a second claimant from and nothing to mask the first. These two
            // fields ARE the ledger for that window: ClaimSettled says the live settle branch
            // has already run (the ONLY signal for a Powerup or a plain non-killable, neither of
            // which flips IsDead), ClaimPaidMask is the paid-once bitmask per killer slot.
            // OnHostDeath folds the mask into the record it writes, so "paid at most once per
            // (entity, slot)" holds across the flush. Same lifetime as Awards -- per LIVE entity,
            // gone when the entity leaves the world, which is what stops a wrapped netId ever
            // inheriting a stale mask.
            public byte ClaimPaidMask;
            public bool ClaimSettled;
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
                // ...and if it is ALREADY DYING, say so straight after its spawn (card
                // f62116b5). A deferred death runs for 2.5-5 s, so a peer joining in progress
                // routinely arrives mid-animation -- and the EvDying beat for it fired before it
                // was here. Without this the joiner holds a frozen, intact copy until the
                // hp==0 snapshot fallback gets round to it, which is the one case that fallback
                // exists for and the slowest thing it does.
                //
                // The same discriminant as the live emitter, and it is a SEAM rather than a
                // type test (card ad9c8f8b): the base derives "a killable at zero hit points
                // still in the world", and a type whose death runs outside KillableAlien
                // answers for itself. (An ordinary kill is never seen here -- the entity leaves
                // liveList at the removal seam a flush later.)
                if (e.Comp.NetIsDying)
                {
                    NetSession.OnHostDeathBegan(e.Id);
                }
            }
        }

        private static void Components_ComponentAdded(object src, GameComponentCollectionEventArgs args)
        {
            if (args.GameComponent is GameComponent gc && !entries.ContainsKey(gc)
                && gc is INetEntity comp && !comp.NetCosmeticOnly
                && NetTypeRegistry.TryGet(gc, out byte typeIdx, out INetTypeDescriptor desc))
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
