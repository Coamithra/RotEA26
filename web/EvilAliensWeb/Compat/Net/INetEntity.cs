using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The replicable ENTITY seam (card 25ad0659 step 2c-ii; plan: plans/net-headless-sim.md).
    //
    // The third of the de-static refactor's seams, after INetHost (the clock, the dev flags and
    // the four ServiceHelper services) and INetScene (the live GameScene). This one is the
    // union of everything NetPuppets / NetIdRegistry / NetSession read or write ON a replicated
    // component, which is what lets those three cores stop naming AlienDrawableGameComponent.
    //
    // SEVENTEEN members, and the arithmetic against the card's census is worth stating because
    // the two figures do NOT match: that census measured 16 distinct members over 42 call sites.
    // One of its 16 is `GetType()` (the descriptor lookup), which comes free from object and is
    // not declared here; the other 15 are. The two discriminants below are the difference --
    // they are not "members the cores call" at all, they are what REPLACES two type tests.
    //
    // IMPLEMENTED DIRECTLY ON AlienDrawableGameComponent, never via an adapter object: the
    // Net* accessors at the bottom of that class already ARE the implementation, and an adapter
    // would allocate per entity on a path that runs per puppet per tick.
    //
    // ---- three things this seam deliberately does NOT carry -----------------------------
    //
    // These were filed as the next slice's (2c-iii, entity creation). 2c-iii then MEASURED that
    // surface and DECLINED it, so all three are PERMANENT properties of this seam rather than
    // deferred work -- do not "finish the job" without a new reason, because the one the plan
    // gave ("the host owns entity lifecycle so the sim never constructs a Game") is dead: the
    // harness runs under eahl, which HAS a Game. Full census in plans/net-headless-sim.md.
    //
    // 1. COLLECTION IDENTITY. The cores also hand entities to ComponentBin.Add/Remove/TryAdd
    //    and key two maps (NetPuppets.idByComp, NetIdRegistry.entries) on GameComponent. Those
    //    sites cast back, visibly, rather than this interface exposing a `GameComponent` --
    //    which would defeat its whole purpose in one member. That coupling is real, and it is
    //    about the shared Game.Components collection rather than the entity type: ComponentBin's
    //    only ctor binds to game.Components, and Oracle and CollisionHandler subscribe to it too,
    //    which is why two peers with independent worlds in one process is unreachable and why
    //    the sim drives ONE real context and scripts its peers onto the wire.
    // 2. DESCRIPTOR EXTRAS. INetTypeDescriptor's EncodeSpawnExtra / EncodeStateExtra /
    //    ApplyStateExtra still take the concrete type, so the THREE call sites that reach a
    //    descriptor cast (NetPuppets.ApplySnapshotState, NetSession.OnHostSpawn and
    //    NetSession.SendWorldSnapshot; CreatePuppet needs none -- it RETURNS the concrete
    //    type). Moving those signatures means editing the parameter type in 41 extras overrides
    //    across six descriptor files -- 70 overrides once CreatePuppet's return type is counted
    //    too, and ~80 edits in all with the four interface declarations and the six sites in
    //    NetTypeDescriptor<T>, i.e. eight files. For no behaviour change and no capability:
    //    the sim builds REAL puppets through the production table.
    //    THOSE THREE CASTS ARE SAFE BY CONSTRUCTION, and that is the invariant to preserve:
    //    NetTypeRegistry.TryGet matches the EXACT runtime type against a table whose every
    //    entry is an AlienDrawableGameComponent subclass, and CreatePuppet returns that type.
    //    An INetEntity implementer that is NOT one could only reach them by joining that table.
    // 3. THE INBOUND HOOKS. NetSession.NoteKill / NotePowerupTaken keep their concrete
    //    parameter types: they are the GAME calling the net layer, not the net layer reading
    //    an entity, and a concrete argument converts to this interface for free. So no game
    //    call site outside Compat/Net changed.
    //
    // ---- why the members are shaped the way they are ------------------------------------
    //
    // `scale`, `rotation` and `curframe` are public FIELDS on AlienDrawableGameComponent (2008
    // code), and an interface cannot expose a field. They appear here as NetScale / NetRotation
    // / NetCurFrame; the field stays exactly where it is and the class forwards.
    //
    // The interface is INTERNAL and AlienDrawableGameComponent is PUBLIC, so the Net* members
    // are implemented EXPLICITLY rather than widened to public -- the opposite of the choice
    // 2c-i made for INetScene, and for the opposite reason: GameScene is itself internal, so
    // widening its 15 members widened nothing, whereas widening here would grow a public game
    // type's API by a dozen names purely to satisfy a private seam. Position / Enabled /
    // IsDead are already public and satisfy their members implicitly, for free.
    internal interface INetEntity
    {
        // Base state: what the snapshot carries and what the driver dead-reckons.
        Vector2 Position { get; set; }
        float NetRotation { get; set; }
        float NetScale { get; set; }
        float NetCurFrame { get; }
        Vector2 NetSpeedVector { get; set; }

        // Lifecycle / replication policy.
        bool Enabled { get; set; }
        bool IsDead { get; }
        float NetPointValue { get; }
        float NetSpinPerMs { get; }
        bool NetFrameLocal { get; }
        bool NetCosmeticOnly { get; }

        // Frozen-puppet upkeep, all driven by NetPuppets.Drive on REAL dt.
        void NetSetFrame(float frame);
        void NetAdvanceFrame(float dtSeconds);
        void NetTickTimers(GameTime gameTime);
        void NetDriveExtras(GameTime gameTime);

        // Claim the award slot before a client-side death path runs (card b0ab09ec).
        void NetSuppressAward();

        // Read-and-CLEAR "this entity was repositioned since you last asked" (card e79bb994).
        //
        // The host sets the latch at the reposition itself (NetNoteTeleport, called from the
        // ~dozen sites that write Position as a JUMP rather than as motion: the SpiderBoss's
        // fly-by park, a wrapping Braineroid, EvilSkull's respawn, a wrapping Ball).
        // NetSession.CaptureBaseState consumes it, which is the only reader -- so a peer-less
        // game just sets a bool nothing looks at.
        //
        // READ-AND-CLEAR rather than a plain property because the latch has to survive from
        // whenever the reposition happened until that entity's next snapshot TURN (up to
        // ~1.2 s in a big world), and must then be spent exactly once: a latch left set would
        // refuse the following turn's velocity too, freezing the puppet's dead reckoning.
        bool NetTakeTeleport();

        // Play a one-shot cosmetic beat the host observed (EvFx / NetFxKind). DRAW AND AUDIO
        // ONLY: an implementation must not damage, kill, award, spawn a replicable entity or
        // touch gameplay state, and must be IDEMPOTENT against the client's own simulation --
        // a client hit-tests puppets with its own bullets, so the effect may already be running,
        // and the beat is then a no-op. The base answers the kinds that are generic
        // (AlienDrawableGameComponent); a type with its own hit timer or death chunk overrides.
        void NetPlayFx(NetFxKind kind);

        // ---- the two discriminants an interface cannot carry as a type test --------------
        //
        // The layer does `is KillableAlien` and `is Powerup` in four and three places. A type
        // test cannot ride an interface, so each subtype answers for itself: non-null means
        // "yes, and here is the surface". Both return `this` on the type that has them and
        // null everywhere else, so the call sites read the same way the `is` patterns did.

        // Non-null iff this entity can be damaged and killed (KillableAlien).
        INetKillable NetKillable { get; }

        // Non-null iff this entity is COLLECTED rather than killed (Powerup). A pickup must
        // never take the generic death-burst branch -- an explosion where the other player
        // picked something up.
        INetPickup NetPickup { get; }
    }

    // The KillableAlien half of the discriminant above.
    internal interface INetKillable
    {
        int NetHitPoints { get; }
        void NetApplyHp(int hp);
        void NetKill(ICollidable killer, bool isComboGenerator);
        // The death nobody landed -- a self-destruct, a scripted crash. Separate from NetKill
        // because the FX can differ from being shot; see KillableAlien.NetReplayUnattributedDeath.
        void NetReplayUnattributedDeath(ICollidable agent);
    }

    // The Powerup half. `NetMarkTaken` fronts the public `taken` field for the same reason
    // NetScale fronts `scale`: an interface cannot expose a field.
    internal interface INetPickup
    {
        Powerup.PowerupType NetPickupType { get; }
        void NetMarkTaken();
    }
}
