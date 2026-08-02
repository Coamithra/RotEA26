using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Why a snapshot entry had no puppet to apply itself to (card 48ab9b2f). Reported per
    // entry by NetPuppets.OnSnapshotEntry so NetMetrics can count the three separately -- the
    // single snapUnk total they used to share is unreadable, since two of them are ordinary
    // traffic and only the third is a fault.
    public enum SnapUnknownKind
    {
        None = 0,   // the id WAS puppeted; the entry applied normally
        Rebuilt,    // never-seen id, self-heal built it from the snapshot (stream outran the
                    // reliable EvSpawn, or a local purge dropped a world the host's still has)
        LeftDead,   // removed here < RecentRemovalWindowMs ago: a death still settling
        Refused,    // the rebuild was declined. Three causes, which tick at very different
                    // rates: no descriptor for the typeIdx (a registry/protocol mismatch)
                    // re-counts on EVERY turn, while a descriptor declining -- no live
                    // CreatePuppet does today -- or the bin swallowing the add both mark the id
                    // removed first, so they tick about once per RecentRemovalWindowMs.
    }

    // Why an EvSpawn did not produce a live puppet (card 4c9448c8). Reported by
    // NetPuppets.OnSpawn so NetMetrics can count the causes separately -- the single dup
    // total they used to share is unreadable for exactly the reason snapUnk was: most of it
    // is ordinary traffic and only one shape is a fault. Same split, one layer down.
    public enum SpawnRejectKind
    {
        None = 0,     // the puppet was BUILT and landed. Not a rejection.
        AlreadyLive,  // we already hold this id and are KEEPING what we hold. Card de4d5d65
                      // added a second way to reach it: a SELF-HEALED puppet is normally
                      // rebuilt from this spawn's extras (reporting None), but a rebuild that
                      // cannot be constructed reports AlreadyLive rather than destroying the
                      // working puppet. BENIGN and expected in bursts: the snapshot
                      // self-heal rebuilds ids off the unreliable stream lane, so the ordered
                      // EvSpawn for one of those arrives second, and a checkpoint revert
                      // re-spawns ids across a purge the client is still settling. Measured on
                      // a real WebRTC pairing: 15 in one burst for a joiner that arrived DURING
                      // a host reset, flat at 15 thereafter; 0 for the whole run of a joiner
                      // that arrived in steady state.
        Declined,     // the descriptor refused to construct (e.g. a Ball with no JunkBoss).
                      // BENIGN by construction, and the id is marked removed so the snapshot
                      // self-heal retries it after RecentRemovalWindowMs rather than every turn.
        Unknown,      // NO DESCRIPTOR for the typeIdx. THIS IS THE FAULT SHAPE -- it means the
                      // peer put a type on the wire this build's registry does not have, i.e. a
                      // registry/protocol mismatch, and the world silently disagrees from here
                      // on. Same meaning as snapBad's first cause.
        Swallowed,    // ComponentBin.TryAdd refused. Unreachable today (Constructing exempts the
                      // puppet layer from the standing purge filter) and logged unconditionally
                      // where it happens -- if it ever fires that is news, not traffic.
        Disabled,     // the puppet layer is not running. Unreachable from the EvSpawn path,
                      // which is already gated on a client session with a scene up; counted
                      // rather than silently dropped so "we never even looked" can never be
                      // mistaken for "the id was already live".
    }

    // Client-side world puppets (card 11.2, design: plans/stage11-online-coop.md).
    //
    // Every replicated enemy on a JOIN peer is a real game object built by its own
    // New*+Setup factory (the sprite-harness-proven construction path) and then FROZEN:
    // Enabled=false so its gameplay Update/AI never runs, while Draw still renders and a
    // CollisionHandler seam keeps it hit-testable by the client's own bullets. One driver
    // component dead-reckons all puppets between world snapshots (pos += vel*dt, curframe
    // advances at the type's own fps), blends in snapshot corrections over a short window
    // (a big error snaps and counts as a pop -- pops self-heal), and ticks each puppet's
    // timers so hit-blink decay etc. still animate.
    //
    // Death/claim flow (generous at-least-once, no arbitration):
    //   * LOCAL kill (client bullets/blasts, or the re-fired remote ship's bullets): the
    //     real per-type death path already ran (FX + score + combo paid locally). The
    //     removal watcher turns it into an EvClaim(netId, killerSlot) to the host.
    //   * REMOTE death (host EvDeath): live puppet -> NetKill through the real per-type
    //     death path (FX + credit to the killer slot); no killer -> silent despawn;
    //     already dead locally -> pay the killer slot once from the event's points/pos.
    //   * A per-(netId, slot) paid ledger makes every credit at-most-once per side while
    //     every distinct claimant still gets paid.
    public static class NetPuppets
    {
        // FLOOR of the blend window, not the window itself -- see CorrectionWindowFor.
        private const float CorrectionWindowMs = 150f;
        private const float SnapThresholdPx = 100f;    // bigger error: snap + count a pop
        private const int LedgerCap = 512;

        private sealed class PuppetInfo
        {
            // Through INetEntity since step 2c-ii: everything the driver and the snapshot
            // apply do to a puppet is on that seam. The bin/collection sites below cast back
            // on purpose -- see INetEntity's header for why identity is NOT on it.
            public INetEntity Comp;
            public byte TypeIdx;
            public Vector2 Vel;          // design px/ms from the last snapshot
            public Vector2 Correction;   // remaining position error being blended away
            public float CorrectionMsLeft;
            // The window THIS correction was opened with. Held per puppet rather than read live in
            // Drive because CorrectionWindowFor moves with the live count: a correction that opened
            // over 480 ms must finish over 480 ms, or a spawn burst mid-blend rescales the fraction
            // already applied and the puppet jumps.
            public float CorrectionMs;
            public float TargetScale;
            public bool HasSnapshot;
            // ---- anchored motion (card c1a38ef9) ----------------------------------------
            // Cached once at spawn: NetPathAnchored is a per-TYPE constant, and this is read
            // per puppet per tick.
            public bool PathAnchored;
            // Cached the same way and for the same reason: NetScaleLocal is a per-TYPE constant.
            // True means the puppet DERIVES its scale (Wall, from the replicated grid variation),
            // so TargetScale is never fed from the wire -- the base state's u16-at-1/256 Scale is
            // up to 1/256 out in absolute terms, which is ~5% of a Level-3 wall's 0.053 (cards
            // 4392bd30 / 80749dc4). See AlienDrawableGameComponent.NetScaleLocal.
            public bool ScaleLocal;
            // The velocity the host last reported, which for an anchored puppet is a TARGET
            // rather than an assignment -- Vel eases toward it over VelEaseMs. That is what
            // turns a shot-induced heading change into a nudge instead of a step; see
            // AlienDrawableGameComponent.NetPathAnchored.
            public Vector2 VelTarget;
            public float VelEaseMsLeft;
            public float VelEaseMs;
            // The type's own periodic offset as of the previous tick. The driver moves the
            // puppet by the DELTA, so only the offset's change matters and a puppet adopted
            // mid-cycle does not jump by the offset's absolute value.
            public Vector2 PathOffset;
            public bool HasPathOffset;
            // Built by the snapshot self-heal, i.e. with NO spawn extras -- so its variant
            // cosmetics are the descriptor's defaults, not the host's. The reliable EvSpawn
            // that follows rebuilds it properly (card de4d5d65) -- the rebuild is a whole new
            // PuppetInfo, so this is never cleared in place, it is replaced by a false one.
            public bool SelfHealed;
            // A snapshot entry for this puppet has already reported hp==0 once. The fallback
            // deferred-death trigger needs TWO consecutive such turns -- see
            // ApplyHostKilledFromSnapshot for why one is not enough now that EvDying exists.
            public bool SawZeroHp;
        }

        private static readonly Dictionary<ushort, PuppetInfo> byId = new Dictionary<ushort, PuppetInfo>();
        private static readonly Dictionary<GameComponent, ushort> idByComp = new Dictionary<GameComponent, ushort>();
        private static readonly List<PuppetInfo> live = new List<PuppetInfo>();

        // (netId -> slots already credited locally), bounded FIFO trim.
        private static readonly Dictionary<ushort, byte> paidLedger = new Dictionary<ushort, byte>();
        private static readonly Queue<ushort> paidOrder = new Queue<ushort>();

        // Provisional local credits awaiting the host's authoritative figure (card b0ab09ec).
        // The policy itself lives in NetScoreLedger, which is driveable on a virtual clock by
        // eaNetScore.test(); this file only feeds it from the real death paths.
        private static readonly NetScoreLedger scoreLedger = new NetScoreLedger();

        // Recently locally-removed ids: a snapshot entry for one of these is a death whose
        // claim/death event is still in flight, NOT a missed spawn -- don't resurrect it.
        private const float RecentRemovalWindowMs = 3000f;
        private static readonly Dictionary<ushort, long> recentlyRemoved = new Dictionary<ushort, long>();
        private static readonly Queue<ushort> recentlyRemovedOrder = new Queue<ushort>();

        private static Game game;
        private static ComponentBin bin;
        private static ScoreVisualiser score;
        private static NetPuppetDriver driver;
        private static Bullet scratchKiller; // cast-safe IAlienKiller agent for NetKill
        private static bool enabled;
        private static bool constructing;    // lets puppet adds through the client add-gate

        // Puppets whose death WE applied from a host EvDeath. Component removal is deferred
        // a tick (ComponentBin deathList), so a bool flag can't bridge to the removal seam --
        // membership here is what stops a host-initiated death echoing back as a claim.
        private static readonly HashSet<GameComponent> remoteDeaths = new HashSet<GameComponent>();

        public static int LiveCount => byId.Count;

        // True while the puppet layer itself is constructing/adding a puppet -- the ONLY
        // path allowed to add replicable types to a client world (see ComponentBin.Add).
        public static bool Constructing => constructing;

        public static void Enable(Game g)
        {
            if (enabled)
            {
                return;
            }
            enabled = true;
            game = g;
            // Through the host since step 2b (see INetHost). StartWith resolves its own copies a
            // few lines earlier in the same call -- two reads of the same seam, not one shared
            // one, because the puppet layer is also enabled by NetSnapshotTest with no session.
            bin = NetHost.Current.ComponentBin;
            score = NetHost.Current.Score;
            driver = new NetPuppetDriver(g);
            g.Components.Add(driver);
            g.Components.ComponentRemoved += Components_ComponentRemoved;
        }

        // Session teardown (card 11.4: menu sessions end when the match does). Live puppet
        // components are left to the scene's own Terminate purge (they're ordinary world
        // components); this only drops the driver + the id maps so nothing dangles into a
        // later session.
        public static void Disable()
        {
            if (!enabled)
            {
                return;
            }
            enabled = false;
            game.Components.ComponentRemoved -= Components_ComponentRemoved;
            game.Components.Remove(driver);
            // ComponentBin's ComponentRemoved handler pools EVERY departing component, so the
            // dead driver would sit in the recycle pool (and the watcher multiset) for the rest
            // of the process -- one per session, and one per eaBinTest() run. Nothing else can
            // reach it once `driver` is nulled, so drop it here.
            bin.PruneIdle(driver);
            driver = null;
            byId.Clear();
            idByComp.Clear();
            live.Clear();
            paidLedger.Clear();
            paidOrder.Clear();
            scoreLedger.Reset();
            recentlyRemoved.Clear();
            recentlyRemovedOrder.Clear();
            remoteDeaths.Clear();
        }

        // CollisionHandler seam: a frozen puppet is still hit-testable -- but only while the
        // driver is enabled, so a paused stack (ComponentBin.Push disables the driver too)
        // keeps collisions frozen exactly like single-player.
        public static bool CollidableOverride(GameComponent g)
        {
            return enabled && driver.Enabled && idByComp.ContainsKey(g);
        }

        public static bool IsPuppet(GameComponent g)
        {
            return enabled && idByComp.ContainsKey(g);
        }

        // The puppet a wire beat is addressed to, or null. Used by NetSession's EvFx apply --
        // a one-shot cosmetic beat names an entity by netId and has to reach the local copy.
        // Null for an id we never built, one already torn down, or a beat that arrived before
        // its EvSpawn: all three mean "nothing to light up", and an FX beat is never retried.
        internal static INetEntity FindPuppet(ushort netId)
        {
            return enabled && byId.TryGetValue(netId, out PuppetInfo info) ? info.Comp : null;
        }

        // ---- wire -> puppets ----------------------------------------------------------------

        // `selfHealed` is true ONLY for the snapshot self-heal below, which constructs with no
        // spawn extras at all (card de4d5d65). It is not the same question as `len == 0`: several
        // types legitimately have no spawn extras, and their puppets are complete.
        public static SpawnRejectKind OnSpawn(ushort netId, byte typeIdx, in NetBaseState state, byte[] buf, int off, int len, bool selfHealed = false)
        {
            if (!enabled)
            {
                return SpawnRejectKind.Disabled;
            }
            // The self-healed puppet this spawn is about to REPLACE, if any. It stays live and
            // registered until the replacement has been built AND landed -- see the three
            // "keep the stale" returns below.
            PuppetInfo stale = null;
            if (byId.TryGetValue(netId, out PuppetInfo livePuppet))
            {
                // A self-healed puppet was built from a SNAPSHOT, i.e. with no spawn extras --
                // so it is wearing whatever its descriptor's default construction produced: an
                // untinted bonus UFO (no SetAsBonus), a Powerup carrying Randomize()'s local
                // random type instead of the host's, a small saucer where the host has a big
                // one. The extras that would fix it are on the reliable EvSpawn that is arriving
                // RIGHT NOW, and discarding it as a duplicate is what made those defects
                // permanent. Rebuild the puppet from the real extras instead. Anything NOT
                // self-healed is an ordinary duplicate and still rejects (card de4d5d65).
                if (!livePuppet.SelfHealed || selfHealed)
                {
                    return SpawnRejectKind.AlreadyLive;
                }
                stale = livePuppet;
            }
            INetTypeDescriptor desc = NetTypeRegistry.Get(typeIdx);
            if (desc == null)
            {
                // A generically-dressed puppet beats no puppet: tearing the live one down for a
                // spawn we cannot build would leave the id `MarkRemoved` and every snapshot for
                // the next RecentRemovalWindowMs reading LeftDead. The typeIdx still came off
                // the wire from a stranger via the public game browser, so this is reachable.
                return stale != null ? SpawnRejectKind.AlreadyLive : SpawnRejectKind.Unknown;
            }
            AlienDrawableGameComponent comp;
            bool landed = false;
            constructing = true;
            try
            {
                comp = desc.CreatePuppet(bin, game, state, buf, off, len);
                if (comp != null)
                {
                    landed = bin.TryAdd((GameComponent)(object)comp);
                }
            }
            finally
            {
                constructing = false;
            }
            if (comp == null)
            {
                // A descriptor may legitimately decline (e.g. a Ball with no JunkBoss, or a
                // Powerup whose type byte is not a real PowerupType). Mark the id removed so the
                // snapshot self-heal doesn't re-attempt construction every 60ms turn -- it
                // retries after the suppression window. Keeping a stale puppet instead is the
                // same trade as the no-descriptor branch above.
                if (stale != null)
                {
                    return SpawnRejectKind.AlreadyLive;
                }
                MarkRemoved(netId);
                return SpawnRejectKind.Declined;
            }
            if (!landed)
            {
                // The bin swallowed it. `Constructing` exempts us from the standing purge
                // filter, so this should be unreachable -- but registering the id anyway is
                // what turns a swallowed add into a permanent GHOST: never drawn, never
                // collidable, and invisible to the self-heal below, which only rebuilds ids
                // that are NOT in byId. Take the same path as a declining descriptor instead,
                // so the id stays unknown and a later snapshot turn retries it once the
                // RecentRemovalWindowMs suppression expires (card 74403f83). Logged
                // unconditionally: it is defence in depth with no reachable trigger today, so
                // if it ever does fire that is news, and ?binlog cannot report it (the bin's
                // own divert log sits inside the branch the exemption skips).
                Console.WriteLine("[net] puppet add was diverted by the bin, id=" + netId
                    + " type=" + typeIdx + " -- retrying after the removal window");
                if (stale != null)
                {
                    return SpawnRejectKind.AlreadyLive;
                }
                MarkRemoved(netId);
                return SpawnRejectKind.Swallowed;
            }
            if (stale != null)
            {
                // The replacement has landed, so the stale one goes now. Detach BEFORE asking the
                // bin to remove: bin.Remove is DEFERRED to the next flush, by which point the
                // replacement is already registered under this same netId.
                // Components_ComponentRemoved early-returns on an unmapped component, so dropping
                // the maps here makes that late event a complete no-op -- leave them in place and
                // it evicts the REPLACEMENT from byId and MarkRemoveds the id, after which every
                // snapshot entry reads LeftDead and the puppet is never corrected again.
                var staleComp = (GameComponent)(object)stale.Comp;
                idByComp.Remove(staleComp);
                byId.Remove(netId);
                live.Remove(stale);
                bin.Remove(staleComp);
            }
            comp.Enabled = false; // frozen from the first tick (bin.Add force-enables)
            INetEntity entity = comp;
            PuppetInfo info = new PuppetInfo
            {
                Comp = entity,
                TypeIdx = typeIdx,
                Vel = state.Vel,
                VelTarget = state.Vel,
                // A scale-local type keeps whatever its own CreatePuppet/Setup derived -- the
                // wire's copy is a lossy fixed-point encoding of that same number.
                TargetScale = (!entity.NetScaleLocal && state.Scale > 0f) ? state.Scale : entity.NetScale,
                SelfHealed = selfHealed,
                // Cached rather than asked per tick: NetPathAnchored is a per-type constant.
                PathAnchored = entity.NetPathAnchored,
                ScaleLocal = entity.NetScaleLocal,
            };
            ApplySnapshotState(info, state, null, null, 0, 0, isSpawn: true);
            if (stale != null)
            {
                // `state` here is the SPAWN-time base state, and the snapshot that self-healed
                // this id is by definition newer -- that lane skew is the whole reason the puppet
                // existed to be rebuilt. ApplySnapshotState has just hard-written the entity back
                // to where it entered the world, so carry the corrected pose across: without it
                // the enemy teleports backwards and dead-reckons from there, collidable, until its
                // next round-robin turn (up to snapTurn, ~1.2s in a big world).
                entity.Position = stale.Comp.Position;
                info.Vel = stale.Vel;
                info.Correction = stale.Correction;
                info.CorrectionMs = stale.CorrectionMs;
                info.CorrectionMsLeft = stale.CorrectionMsLeft;
                // NOT the scale, for a scale-local type: the stale puppet was built from DEFAULT
                // spawn extras (that is what a self-heal is), so a Wall rebuilt on the host's real
                // grid variation would inherit the wrong grid's derived scale.
                if (!info.ScaleLocal)
                {
                    info.TargetScale = stale.TargetScale;
                }
                info.HasSnapshot = stale.HasSnapshot;
                // The in-flight velocity ease travels with the pose, for the same reason: the
                // replacement is a NEW entity object, so its offset baseline is re-seeded from
                // scratch (HasPathOffset stays false) and only the CHANGE is used from then on.
                info.VelTarget = stale.VelTarget;
                info.VelEaseMs = stale.VelEaseMs;
                info.VelEaseMsLeft = stale.VelEaseMsLeft;
            }
            byId[netId] = info;
            idByComp[(GameComponent)(object)comp] = netId;
            live.Add(info);
            return SpawnRejectKind.None;
        }

        public static bool OnSnapshotEntry(ushort netId, byte typeIdx, byte entryFlags, in NetBaseState state, byte[] buf, int extraOff, int extraLen, out bool popped, out SnapUnknownKind kind)
        {
            popped = false;
            kind = SnapUnknownKind.None;
            if (!enabled)
            {
                return false;
            }
            if (!byId.TryGetValue(netId, out PuppetInfo info))
            {
                // Self-heal: an id we never built (spawn raced the stream / a local purge
                // dropped the world while the host's lives on) is reconstructed from the
                // snapshot itself -- with NO spawn extras, so a variant looks generic (an
                // untinted bonus UFO, a Powerup carrying a locally-random type). The puppet is
                // flagged SelfHealed and the reliable EvSpawn, when it arrives, REBUILDS it from
                // the real extras rather than being dropped as a duplicate (card de4d5d65); when
                // it never arrives -- the purge case -- the generic look is all there is. An id
                // removed HERE moments ago is a death still settling (our claim, or the host's
                // EvDeath): leave it dead.
                //
                // WHICH of those three it was is reported to the caller (card 48ab9b2f). They
                // all return false and used to share one snapUnk counter, but they mean
                // completely different things: Rebuilt and LeftDead are ordinary traffic whose
                // rates track the world's spawn/removal rates, while Refused is a fault (see
                // SnapUnknownKind for how fast each of its causes re-counts).
                if (IsRecentlyRemoved(netId))
                {
                    kind = SnapUnknownKind.LeftDead;
                }
                else
                {
                    // The rebuild's own reject reason is DISCARDED here on purpose: the
                    // snapshot lane's split is Rebuilt/LeftDead/Refused, and every non-None
                    // kind means the same thing to it -- the entry did not apply. The finer
                    // causes are the EvSpawn lane's business (SpawnRejectKind), and folding
                    // them in would make snapBad and dupBad count the same event twice.
                    kind = OnSpawn(netId, typeIdx, state, buf, extraOff, 0, selfHealed: true) == SpawnRejectKind.None
                        ? SnapUnknownKind.Rebuilt
                        : SnapUnknownKind.Refused;
                }
                return false;
            }
            INetTypeDescriptor desc = NetTypeRegistry.Get(info.TypeIdx);
            // Unknown bits are ignored, not refused -- see NetProtocol.NetSnapshotFlags for why a
            // bitmask degrades by masking where a wire ENUM would have to reject.
            bool teleported = (entryFlags & NetProtocol.NetSnapshotFlags.Teleported) != 0;
            popped = ApplySnapshotState(info, state, desc, buf, extraOff, extraLen, isSpawn: false, teleported: teleported);
            ApplyHostKilledFromSnapshot(netId, info, state);
            return true;
        }

        // THE FALLBACK deferred-death trigger, since card f62116b5: the host's copy of this
        // killable is at ZERO HIT POINTS and still in its world, so its death has begun and is
        // taking a while -- BattleSkull's 2.5s dying state, MarsBoss's 5s crash and the other
        // types KillableAlien.NoteDeathBegan censuses (cards 13aa596c / 303bfb5b). The EvDeath for it does not arrive until that animation ENDS, so waiting for
        // it means the peer sees an intact enemy and then, seconds later, one frame of removal.
        //
        // The FAST path is now the host's explicit EvDying beat (NetSession.OnDeathBegan ->
        // OnDeathBegan below), emitted the moment KilledBy defers. This reads the hp that is
        // already in every snapshot entry's base block and so still covers what the beat cannot.
        // NOT packet loss -- EvDying rides the RELIABLE lane, so a lost one is not a case that
        // exists. What is left is a deferred-death path that reaches its dying state WITHOUT
        // going through KillableAlien, i.e. nothing today and a cheap safety net tomorrow.
        // (A peer JOINING IN PROGRESS mid-animation used to be the other one, and it was the
        // case that made the two-turn rule below expensive: NetIdRegistry.ReplayLive now sends
        // the beat with the catch-up spawn instead, so that peer is on the fast path too.)
        //
        // Zero is unambiguous here because the entry belongs to a puppet we know is killable --
        // NetBaseState.Hp is also 0 for a non-killable, which is why the NetKillable
        // discriminant, not the value, is what makes this readable. On a live KillableAlien hit
        // points can never be 0: Initialize floors them at 1, NetApplyHp floors at 1, and HitBy
        // reaches 0 only on the killing blow.
        //
        // IT NEEDS TWO CONSECUTIVE hp==0 TURNS, and that is what removes the pre-card
        // one-tick-early residual. The host's ComponentBin defers removal, so an entity killed in
        // the collision phase is still in the NetIdRegistry when that same tick's snapshot is
        // encoded -- if its round-robin turn landed in that ONE tick, a single-turn trigger ran
        // the death here a tick before the attributed EvDeath arrived, with the KillerNone
        // scratch agent instead of the real killer. That was accepted before because narrowing it
        // cost the deferred case a whole snapTurn; it does not any more, because EvDying owns the
        // live case (including the join-in-progress catch-up) and the extra turn is only ever
        // paid on a path nothing reaches today. An entity really dying stays at hp==0 for every
        // remaining turn, so the second one always comes.
        private static void ApplyHostKilledFromSnapshot(ushort netId, PuppetInfo info, in NetBaseState state)
        {
            if (state.Hp > 0)
            {
                info.SawZeroHp = false;
                return;
            }
            if (!(info.Comp.NetKillable is INetKillable killable))
            {
                return; // hp is 0 for every non-killable -- the discriminant, not the value
            }
            if (!info.SawZeroHp)
            {
                info.SawZeroHp = true;
                return;
            }
            BeginDeferredDeath(netId, info, killable);
        }

        // The host has told us -- by an explicit EvDying beat or by two hp==0 snapshot turns --
        // that this puppet's death has begun. Run the type's real death path for the FX and then
        // let the puppet GO, so its own Update finishes dying locally.
        //
        // Award-free (NetSuppressAward first): this is the FX only. Who gets paid is settled by
        // the host's EvDeath when it eventually lands, exactly as before.
        private static void BeginDeferredDeath(ushort netId, PuppetInfo info, INetKillable killable)
        {
            if (info.Comp.IsDead)
            {
                return; // already gone; the removal seam owns it from here
            }
            if (killable.NetHitPoints > 0)
            {
                info.Comp.NetSuppressAward();
                // Never echo this back as a claim -- the host is the one that told us. Same guard
                // OnRemoteDeath sets, and it is consumed by the same removal seam;
                // ReleaseDyingPuppet clears it by hand for the branch where that seam
                // early-returns.
                remoteDeaths.Add((GameComponent)info.Comp);
                killable.NetKill(KillerAgent(NetProtocol.KillerNone, info.Comp.Position), isComboGenerator: false);
                if (info.Comp.IsDead)
                {
                    // KilledBy ended in Die(), which has already queued the removal -- the
                    // ordinary instant-death types. Nothing to release; the removal seam tidies
                    // the maps. (Only reachable from the fallback: the host does not send EvDying
                    // for a death that removed its own component.)
                    return;
                }
            }
            // ...else a death path has ALREADY run on this puppet -- WE killed it locally with
            // our own bullet -- and it is still in the world, so its KilledBy deferred too and
            // the puppet has been standing frozen mid-animation ever since. Same answer, and
            // running NetKill again would be a no-op anyway (KillableAlien guards on `dead`).
            ReleaseDyingPuppet(netId, info);
        }

        // The host's explicit "this death has begun" beat (card f62116b5), which replaces
        // inferring it from a snapshot's hp and so lands on the tick the host's KilledBy
        // deferred, not up to a round-robin turn later. Unknown id = the puppet is already
        // released or was never built; nothing to do.
        public static void OnDeathBegan(ushort netId)
        {
            if (!enabled || !byId.TryGetValue(netId, out PuppetInfo info))
            {
                return;
            }
            if (info.Comp.NetKillable is INetKillable killable)
            {
                BeginDeferredDeath(netId, info, killable);
            }
        }

        private static bool IsRecentlyRemoved(ushort netId)
        {
            return recentlyRemoved.TryGetValue(netId, out long at)
                && NetHost.Current.NowMs - at < RecentRemovalWindowMs;
        }

        private static void MarkRemoved(ushort netId)
        {
            if (!recentlyRemoved.ContainsKey(netId))
            {
                recentlyRemovedOrder.Enqueue(netId);
                while (recentlyRemovedOrder.Count > LedgerCap)
                {
                    recentlyRemoved.Remove(recentlyRemovedOrder.Dequeue());
                }
            }
            recentlyRemoved[netId] = NetHost.Current.NowMs;
        }

        // How long to spread one snapshot's position error over. The 150 ms constant this replaced
        // was FIXED while the thing it has to absorb is not: the round-robin cursor gives an entity
        // a correction only every SnapshotTurnMs, which grows with the world (60 ms at 16 live
        // entities, 480 ms at 128), and each correction is a fresh velocity offset of err/window
        // that lasts until the next one lands. Drain it faster than the arrival rate and the puppet
        // spends most of its life on a stale dead-reckon and then lurches; drain it over ~2 turns
        // and successive corrections overlap into something continuous.
        //
        // Measured in tools/sim/net_puppet_drive_sim.py --smoothness (FlyingSpider-shaped motion,
        // jerk = stddev of successive per-tick step deltas; the host truth reads 0.0008):
        //     N          16      32      64     128
        //     fixed 150  0.089   0.114   0.180   0.327     maxstep 3.04 -> 8.52 px
        //     2x turn    0.096   0.092   0.091   0.090     maxstep 3.27 -> 3.03 px
        //
        // THOSE ABSOLUTE FIGURES ARE INFLATED BY A RIG ARTIFACT -- read the RATIOS, not the
        // ~100x-host gap (card c1a38ef9). The rig gave a new puppet vel = (0,0) for its first
        // turn, so it stood still and then ate one large correction, and that single transient
        // dominated every number in the table. A real EvSpawn carries CaptureBaseState's
        // velocity, which on a first observation is the DECLARED NetSpeedVector, so a puppet is
        // born moving. With the spawn modelled properly the same rows read 0.013 / 0.013 / 0.014
        // / 0.019, and the steady-state penalty is ~4x to ~21x the host, not ~100x. The COMPARISON
        // this constant rests on is unaffected -- both columns carried the same transient -- and
        // 2x-turn still wins at every N. See the ANCHORED MOTION section in Compat/Net/CLAUDE.md.
        // i.e. flat in the world size instead of degrading 3.7x, at the cost of a hair at N=16 --
        // which the 150 ms FLOOR keeps, since below 75 ms of turn the fixed window is the better of
        // the two. A longer window is NOT free in general (it holds a bigger error for longer), so
        // this is a floor-and-multiple rather than "make it big": past ~2 turns the same sweep shows
        // the curve flattening out.
        //
        // An EXPONENTIAL / critically-damped drain was the obvious alternative and was measured and
        // REJECTED -- it is worse at every N (0.132 / 0.187 / 0.317 / 0.654), because its tail keeps
        // a velocity offset alive to be re-hit by the next correction. Don't re-try it without
        // re-running the sweep.
        private static float CorrectionWindowFor(int liveCount)
        {
            return MathHelper.Max(CorrectionWindowMs, 2f * NetSession.SnapshotTurnMs(liveCount));
        }

        private static bool ApplySnapshotState(PuppetInfo info, in NetBaseState state, INetTypeDescriptor desc, byte[] buf, int extraOff, int extraLen, bool isSpawn, bool teleported = false)
        {
            bool popped = false;
            INetEntity comp = info.Comp;
            // A TELEPORT SNAPS, WHATEVER THE ERROR (card e79bb994). The host marked this sample as
            // a discontinuity, so blending it would slide the entity across the gap -- which is
            // what a jump SHORTER than SnapThresholdPx used to do (EvilSkull respawns at a random
            // point, so plenty of its jumps are under 100 px). Snapping an explained jump is not a
            // pop, either: `pupPops` means "an error the layer could not account for", and every
            // SpiderBoss fly-by used to inflate it.
            if (isSpawn || !info.HasSnapshot || teleported)
            {
                comp.Position = state.Pos;
                info.Correction = Vector2.Zero;
                info.CorrectionMsLeft = 0f;
            }
            else
            {
                Vector2 err = state.Pos - comp.Position;
                if (err.Length() > SnapThresholdPx)
                {
                    comp.Position = state.Pos;
                    info.Correction = Vector2.Zero;
                    info.CorrectionMsLeft = 0f;
                    popped = true;
                }
                else
                {
                    info.Correction = err;
                    info.CorrectionMs = CorrectionWindowFor(LiveCount);
                    info.CorrectionMsLeft = info.CorrectionMs;
                }
            }
            // An ANCHORED puppet EASES toward the reported velocity instead of adopting it (card
            // c1a38ef9). The host sends such a type's declared linear velocity, which is a step
            // function -- constant for an asteroid's whole life until a bullet tweaks its heading,
            // and then constant again. Assigning it puts that whole step into one tick, which is
            // the kink the card is about; spreading it over the SAME window the position error
            // already drains over makes it a nudge and needs no second constant.
            //
            // The very first snapshot (and any snap) assigns: there is nothing to ease FROM.
            if (info.PathAnchored && info.HasSnapshot && !popped)
            {
                info.VelTarget = state.Vel;
                info.VelEaseMs = CorrectionWindowFor(LiveCount);
                info.VelEaseMsLeft = info.VelEaseMs;
            }
            else
            {
                info.Vel = state.Vel;
                info.VelTarget = state.Vel;
                info.VelEaseMsLeft = 0f;
            }
            // A SCALE-LOCAL type never takes the wire's copy (cards 4392bd30 / 80749dc4). The base
            // state carries Scale as a u16 at 1/256 and the cast truncates, so the absolute error
            // is up to 1/256 whatever the value -- ~5% of a Level-3 Wall's 0.053, which sizes every
            // block it draws while its CollisionLevelMap keeps the exact tile size. The puppet's
            // own Setup derived the number the host derived, from the replicated grid variation.
            if (!info.ScaleLocal)
            {
                info.TargetScale = state.Scale;
            }
            info.HasSnapshot = true;
            if (comp.NetSpinPerMs == 0f)
            {
                comp.NetRotation = state.Rotation; // free-spinners rotate locally -- see NetSpinPerMs
            }
            if (isSpawn || !comp.NetFrameLocal)
            {
                // A free-running loop is PINNED once, at spawn, and then owned by the driver's
                // NetAdvanceFrame -- see NetFrameLocal for why re-snapping it every turn can only
                // ever hurt (the stream lane is unordered and unsequenced, so a late entry kicks
                // the animation backward). Types whose frame is host-gated still take it.
                comp.NetSetFrame(state.CurFrame);
            }
            comp.NetSpeedVector = state.Vel; // per-type Draw reading Direction stays truthful
            if (state.Hp > 0 && comp.NetKillable is INetKillable killable)
            {
                killable.NetApplyHp(state.Hp);
            }
            // ORDER MATTERS: state extras run LAST. The base writes above have per-type side
            // effects (NetSpeedVector's setter rewrites Direction, which zeroes Lazer's beam
            // angle) that an extra must be able to re-assert -- see Lazer.NetApplyBeam.
            //
            // The cast back is the descriptor surface, which step 2c-ii deliberately left on
            // the concrete type (INetEntity's header says why): moving it would mean editing a
            // parameter type in 41 overrides for no behaviour change. 2c-iii measured that and
            // DECLINED it, so this cast is permanent -- and safe by construction (INetEntity).
            desc?.ApplyStateExtra((AlienDrawableGameComponent)comp, buf, extraOff, extraLen);
            return popped;
        }

        // A DEFERRED death: the type's death path ran, but it did not remove the component --
        // it entered a multi-second dying STATE that its own Update drives (BattleSkull's 2.5s
        // shrink-and-flicker, MarsBoss's 5s crash to the ground). A puppet is frozen for life,
        // so as a puppet it would stand there intact and then blink out; that is cards 303bfb5b
        // and 13aa596c ("explosions and death animation don't properly play on P2's view").
        //
        // So we let it GO: drop it from the puppet registry and un-freeze it, and its own Update
        // finishes dying locally and its own Die() removes it. The card's own note said the
        // animation "doesn't need to be synced and can be done locally" -- this is that.
        //
        // Nothing about it is replicated after this point, deliberately. It is already dead on
        // the host, so there is nothing left to correct it toward, and the host's EvDeath (which
        // for these types arrives seconds later, at the END of ITS animation) then finds no
        // puppet and settles as an ordinary award-only reconciliation.
        private static void ReleaseDyingPuppet(ushort netId, PuppetInfo info)
        {
            INetEntity comp = info.Comp;
            GameComponent gc = (GameComponent)comp; // identity is off the seam -- INetEntity's header
            byId.Remove(netId);
            live.Remove(info);
            // Dropping idByComp is what makes the eventual local removal a no-op in
            // Components_ComponentRemoved -- which is wanted twice over: no EvClaim is sent (the
            // host already knows; it is the one that told us) and no second MarkRemoved.
            idByComp.Remove(gc);
            // The same early return means the seam will never consume an echo guard either, so
            // drop it here rather than leaving an entry in remoteDeaths for the session.
            remoteDeaths.Remove(gc);
            // ...which is exactly why MarkRemoved has to run HERE instead. Without it the next
            // snapshot entry for this id is an unknown id, and the self-heal REBUILDS the enemy
            // we just released -- a fresh, intact, collidable puppet standing where one is
            // visibly dying. The host stops streaming the id within a turn or two, so the window
            // is short and would have made this a rare, unreproducible ghost.
            MarkRemoved(netId);
            // It is dying, so it must not still be able to kill the local player -- both shipped
            // types clear this in their own KilledBy, but a released puppet is live code now and
            // may not rely on that. The cast back is the documented one (INetEntity's header):
            // every entry in the type registry is an AlienDrawableGameComponent.
            var adc = (AlienDrawableGameComponent)comp;
            adc.Collides = false;
            // The freeze is the thing that was stopping the death animation. KNOWN, ACCEPTED
            // DIVERGENCE: a release that lands while a ComponentBin.Push pause is up enables the
            // entity OUTSIDE any pause layer (nothing can retro-register an existing component
            // into one), so its dying animation runs on through the freeze. It is cosmetic, it is
            // an enemy that is already dead, and it removes itself when it finishes.
            comp.Enabled = true;
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] released dying puppet id=" + netId + " type=" + comp.GetType().Name);
            }
        }

        // Host said this entity is gone. Live puppet + killer -> the real per-type death
        // (FX + credit); live + no killer -> silent despawn; already gone -> generous pay.
        //
        // `awards` is what the HOST credited, slot by slot. It is the authority on the numbers
        // in every branch (card b0ab09ec): the real death path still runs for the FX, but its
        // AwardScore is suppressed first, because it would re-derive the amount from THIS
        // peer's combo counter -- a local simulation that has no reason to match the host's.
        public static void OnRemoteDeath(ushort netId, byte killerSlot, Vector2 pos, float[] awards)
        {
            if (!enabled)
            {
                return;
            }
            if (byId.TryGetValue(netId, out PuppetInfo info))
            {
                INetEntity comp = info.Comp;
                GameComponent gc = (GameComponent)comp; // identity is off the seam -- INetEntity's header
                remoteDeaths.Add(gc); // never echo this back as a claim
                if (killerSlot != NetProtocol.KillerNone && comp.NetKillable is INetKillable killable)
                {
                    comp.NetSuppressAward();
                    if (killerSlot == NetProtocol.KillerSelf)
                    {
                        // Nobody landed it -- the host's copy blew itself up or crashed. Same
                        // real death path, the type's own self-destruct look, and the award
                        // array below is all-zero because the host credited nobody either.
                        killable.NetReplayUnattributedDeath(KillerAgent(killerSlot, comp.Position));
                    }
                    else
                    {
                        killable.NetKill(KillerAgent(killerSlot, comp.Position), isComboGenerator: true);
                    }
                    if (!comp.IsDead)
                    {
                        // A death path has run and the entity is STILL IN THE WORLD, which for a
                        // killable means exactly one thing: its KilledBy deferred its own removal
                        // into a dying animation (BattleSkull's 2.5 s, the surviving MarsBoss's
                        // 5 s crash). The pre-card `bin.Remove` here is what deleted those
                        // mid-animation. Note this also covers the NetKill that no-opped because
                        // WE had already killed the puppet locally -- same state, and its dying
                        // animation had not played either, so it wants the same answer.
                        ReleaseDyingPuppet(netId, info);
                    }
                    ApplyAwards(netId, comp.Position, awards);
                }
                else if (killerSlot != NetProtocol.KillerNone && comp.NetPickup is INetPickup pu)
                {
                    // A powerup is a PICKUP, not a kill -- it must not take the generic-burst
                    // branch below (an explosion where the other player collected). Drive the
                    // collector's HUD slot instead; see NetSession.ApplyRemotePowerup.
                    MarkPaid(netId, killerSlot);
                    pu.NetMarkTaken();
                    NetSession.ApplyRemotePowerup(pu, killerSlot);
                    bin.Remove(gc);
                }
                else if (killerSlot != NetProtocol.KillerNone)
                {
                    // Non-killable replicable (Asteroid/EvilBullet/...): approximate the
                    // death look with a generic burst + credit the killer.
                    ApplyAwards(netId, comp.Position, awards);
                    Explosion explosion = Explosion.NewExplosion(bin, game);
                    explosion.Setup(comp.Position, 1.2f, 1f, 0f, 0f);
                    bin.Add((GameComponent)(object)explosion);
                    bin.Remove(gc);
                }
                else
                {
                    bin.Remove(gc); // plain despawn / fly-off
                }
                return;
            }
            // Already dead locally (we killed it and claimed). Reconcile whatever we credited
            // provisionally against the host's figures, and still pay a DIFFERENT killer once --
            // both peers focus-firing one target must both end up credited.
            ApplyAwards(netId, pos, awards);
        }

        // Settle one death's payout against the host's per-slot figures. A slot we already
        // credited locally is CORRECTED by the difference (silently -- no second floating text
        // for a kill the player already saw); a slot we never credited is paid in full.
        private static void ApplyAwards(ushort netId, Vector2 pos, float[] awards)
        {
            for (byte slot = 0; slot < NetProtocol.MaxSlots; slot++)
            {
                float hostAward = (awards != null && slot < awards.Length) ? awards[slot] : 0f;
                // ZERO IS "no figure", NOT "you earned nothing" -- NetSession.NoteAward filters
                // amount <= 0, so the host never books a real zero. The case that matters is both
                // peers killing one entity: the host's EvDeath carries only ITS killer's award and
                // pays our slot separately when our claim lands. Settling our provisional against
                // that zero would debit the whole local credit for a beat; leaving it on the books
                // would instead double-count it against a host total that already contains the
                // payout. Retiring it silently does neither -- the display holds our estimate
                // until the next sync lands on the host's exact number.
                float delta = scoreLedger.Settle(netId, slot, hostAward, out bool wasProvisional);
                if (wasProvisional)
                {
                    MarkPaid(netId, slot); // a re-delivered EvDeath must not pay this slot again
                    // Correct silently -- the player already saw the floating text for this kill.
                    if (hostAward > 0f && delta != 0f)
                    {
                        score.AddScore(delta, false, slot);
                    }
                    continue;
                }
                if (hostAward > 0f && !IsPaid(netId, slot))
                {
                    MarkPaid(netId, slot);
                    score.AddScore(hostAward, false, pos, slot);
                }
            }
        }

        // ---- local deaths -> claims -----------------------------------------------------------

        private static void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs args)
        {
            if (!(args.GameComponent is GameComponent gc) || !idByComp.TryGetValue(gc, out ushort netId))
            {
                return;
            }
            idByComp.Remove(gc);
            if (byId.TryGetValue(netId, out PuppetInfo info))
            {
                byId.Remove(netId);
                live.Remove(info);
            }
            MarkRemoved(netId);
            var comp = (INetEntity)gc;
            // Claim only GAMEPLAY deaths (Die() ran => IsDead), never scene teardown purges,
            // and never deaths we ourselves applied from a host EvDeath (echo guard).
            if (remoteDeaths.Remove(gc) || !comp.IsDead)
            {
                // Consume any kill note on the way out even though no claim is sent. A client
                // CAN write one -- a mine puppet's own Asplode() calls NetSession.NoteSelfDestruct
                // (cards 4e406eba) -- and killNotes is keyed on the ENTITY, which ComponentBin
                // recycles: an unconsumed note would sit in the bounded FIFO until eviction and,
                // if that instance came back out of the recycle pool first, be taken as the
                // attribution for a different death.
                NetSession.TakeKillNote(comp);
                return;
            }
            byte killerSlot = NetSession.TakeKillNote(comp);
            // AN UNATTRIBUTED CLAIM IS STILL SENT, AND THAT IS THE REPAIR PATH (card 9ccfe295).
            // A `KillerNone` note means our own copy died a gameplay death no player landed --
            // this peer MIS-SIMULATING state the host owns -- so the claim reads as "I have lost
            // this id", not "I killed it". The host refuses to settle it and RE-ANNOUNCES the
            // entity (`HandleClaim`), which is what rebuilds our puppet with the host's real
            // spawn extras. Suppressing the send here was tried and is WRONG: we have already
            // `MarkRemoved` the id, so with the host never told, the enemy is missing for
            // `RecentRemovalWindowMs` and then self-heals into a generically-dressed provisional
            // puppet that no later `EvSpawn` ever corrects.
            if (killerSlot != NetProtocol.KillerNone)
            {
                MarkPaid(netId, killerSlot); // the local death path already paid this slot
            }
            NetSession.SendClaim(netId, killerSlot);
        }

        // ---- driver ---------------------------------------------------------------------------

        // Ticked by NetPuppetDriver on REAL elapsed time (dtMs), NOT the turbo/slow-mo/hit-stop-
        // scaled game clock Game1.Update folds into the gameTime it hands components. The host
        // mirrors its enemies at its own real pace and stamps every snapshot's observed velocity
        // on real time (NetSession.CaptureBaseState uses TickCount64), so a client-side time-scale
        // window -- the wipe's 180ms player-death hit-stop, a 1-up slow-motion overlapping it --
        // must not stall the dead-reckoning OR the correction blend (which only advances here).
        // If it does, the puppets fall behind the host's real-time snapshots and the growing error
        // snaps past SnapThresholdPx again and again: that was the one-time pupPops burst on the
        // first wipe (deferred from card 11.3; characterised in tools/sim/net_puppet_drive_sim.py).
        // NOTE card 68f62e92 removed the death hit-stop FROM SESSIONS ENTIRELY (Juice.AddHitStop
        // refuses while NetSession.Active), for the RECIPROCAL fault -- the dying peer's world
        // halting while its snapshots kept flowing rewound the other peer's enemies. That does
        // NOT make this rule redundant: the 1-up slow motion is still local and unreplicated, and
        // a scaled driver would diverge under it exactly as before. Keep it on real time.
        // This is the same real-time rule the remote-SHIP puppet already follows
        // (NetSession.DriveRemoteShip advances on realDtMs "never the turbo/slowmo/hit-stop-scaled
        // game time") and that NetAdvanceFrame's own contract already assumes ("on real dt").
        internal static void Drive(float dtMs)
        {
            float dtSeconds = dtMs / 1000f;
            // Cosmetic timers (KillableAlien hit-blink decay, pulse timers) tick on the SAME real
            // clock -- a frozen puppet keeps animating through a local hit-stop, by design.
            GameTime realTime = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(dtMs));
            for (int i = 0; i < live.Count; i++)
            {
                PuppetInfo info = live[i];
                INetEntity comp = info.Comp;
                comp.Enabled = false; // re-assert the freeze (pause Pop / stray enables)
                // Hold position while the peer is stalled: the last-known velocity is stale by
                // up to the whole grace window, and dead-reckoning on it for seconds would fling
                // the enemy world hundreds of px off -- which the LOCAL player can be killed by,
                // since each peer owns its own hits -- then snap back hard when the host returns.
                // ShipStateBuffer caps its own extrapolation at 250ms for exactly this reason.
                // An in-flight correction still drains; it is finishing a snapshot we DID get.
                // Anchored motion (card c1a38ef9): ease the baseline toward the last reported
                // velocity, then add the type's own periodic component locally. Both are skipped
                // for a stalled peer for the same reason the dead-reckon is -- we are no longer
                // being told anything, so inventing motion moves a COLLIDABLE hitbox.
                if (info.PathAnchored && !NetSession.PeerStalled && info.VelEaseMsLeft > 0f
                    && info.VelEaseMs > 0f)
                {
                    float easeTake = MathHelper.Min(dtMs, info.VelEaseMsLeft);
                    // Fraction of what REMAINS, so the ease lands exactly on the target on the
                    // last tick whatever the dt pattern was -- a fraction of the whole window
                    // would leave a residue that the next snapshot then has to correct.
                    info.Vel += (info.VelTarget - info.Vel) * (easeTake / info.VelEaseMsLeft);
                    info.VelEaseMsLeft -= easeTake;
                }
                Vector2 step = NetSession.PeerStalled ? Vector2.Zero : info.Vel * dtMs;
                if (info.PathAnchored)
                {
                    // THE BASELINE IS REFRESHED EVEN WHILE STALLED, and only the STEP is
                    // withheld. NetTickTimers below keeps advancing the type's own timers
                    // throughout a stall (a puppet goes on animating by design), so a baseline
                    // left frozen would accumulate the whole stall's worth of sine and pay it
                    // out in ONE frame the tick the peer came back -- up to 2 x amplitude on a
                    // collidable hitbox, which is precisely what the hold above exists to
                    // prevent.
                    Vector2 offset = comp.NetPathOffset;
                    if (info.HasPathOffset && !NetSession.PeerStalled)
                    {
                        step += offset - info.PathOffset;
                    }
                    info.PathOffset = offset;
                    info.HasPathOffset = true;
                }
                if (info.CorrectionMsLeft > 0f && info.CorrectionMs > 0f)
                {
                    float take = MathHelper.Min(dtMs, info.CorrectionMsLeft);
                    step += info.Correction * (take / info.CorrectionMs);
                    info.CorrectionMsLeft -= take;
                }
                comp.Position += step;
                comp.NetRotation += comp.NetSpinPerMs * dtMs; // no-op unless the type opted out of replicated rotation
                comp.NetAdvanceFrame(dtSeconds);
                if (info.HasSnapshot && info.TargetScale > 0f)
                {
                    comp.NetScale = MathHelper.Lerp(comp.NetScale, info.TargetScale, MathHelper.Clamp(dtMs / 100f, 0f, 1f));
                }
                comp.NetTickTimers(realTime);
                // Per-type child-component upkeep (e.g. an enemy's laser-charge glow) against the
                // now-updated Position -- default no-op; overridden by the charging boss/UFO puppets.
                comp.NetDriveExtras(realTime);
            }
        }

        // ---- ledger + kill agent ---------------------------------------------------------------

        private static bool IsPaid(ushort netId, byte slot)
        {
            return slot < NetProtocol.PayableSlots && paidLedger.TryGetValue(netId, out byte mask) && (mask & (1 << slot)) != 0;
        }

        // ---- wire + apply round trip (card b0ab09ec) -------------------------------------------

        // Drives a synthetic death through the REAL EncodeDeathEvent -> ReadDeathAwards ->
        // ApplyAwards chain against the LIVE ScoreVisualiser, and puts the scores back.
        //
        // Two windows cannot be the gate for this: a backgrounded tab throttles to ~1 tick/sec
        // (measured -- txStream advanced 43 in 40s where 30Hz would be ~1200), so the peer never
        // plays long enough for a tally to diverge, and the two peers cannot be foregrounded at
        // once. Same reason eaNetBgTest replaced a two-window check for the JIP catch-up. What
        // this covers that the ledger self-test cannot: wire field offsets/width, the
        // fresh-pay vs settle branch, and the at-most-once guard.
        internal static string WireRoundTripTest()
        {
            ScoreVisualiser sv = NetHost.Current.Score;
            ScoreVisualiser prev = score;
            score = sv;
            var before = new float[NetProtocol.MaxSlots];
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                before[i] = sv.PointScore(i);
            }
            var sb = new System.Text.StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(bool ok, string what)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }
            // Far above any id a session is realistically at (AllocId counts from 1 and only
            // wraps at 65535), so the scenarios cannot collide with live entries.
            const ushort idA = 60001;
            const ushort idB = 60002;
            const ushort idC = 60003;
            try
            {
                // 1. Wire round trip: the award array must survive encode/decode intact.
                var awards = new float[NetProtocol.MaxSlots];
                awards[0] = 1234.5f;
                awards[1] = 77.25f;
                byte[] frame = NetProtocol.EncodeDeathEvent(1, idA, 0, new Vector2(100f, 200f), awards);
                var back = new float[NetProtocol.MaxSlots];
                NetProtocol.ReadDeathAwards(frame, back);
                Check(frame.Length == NetProtocol.DeathEventBytes,
                    "EvDeath frame is DeathEventBytes (" + frame.Length + " vs " + NetProtocol.DeathEventBytes + ")");
                Check(back[0] == 1234.5f && back[1] == 77.25f && back[2] == 0f && back[3] == 0f,
                    "award array round-trips [" + string.Join(",", back) + "]");

                // 2. Fresh pay (host killed it, we never credited): full award, both slots.
                float s0 = sv.PointScore(0);
                float s1 = sv.PointScore(1);
                ApplyAwards(idA, new Vector2(100f, 200f), back);
                Check(Near(sv.PointScore(0) - s0, 1234.5f) && Near(sv.PointScore(1) - s1, 77.25f),
                    "unclaimed death pays the host figure verbatim to every awarded slot");

                // 3. At-most-once: replaying the same death must not pay twice.
                s0 = sv.PointScore(0);
                ApplyAwards(idA, new Vector2(100f, 200f), back);
                Check(Near(sv.PointScore(0) - s0, 0f), "a repeated EvDeath for the same id pays nothing extra");

                // 4. Provisional OVER-estimate (our combo ran hotter than the host's): the
                //    correction must land us on the host's figure exactly, not above it.
                s0 = sv.PointScore(0);
                scoreLedger.NoteLocal(idB, 0, 2000f, NetHost.Current.NowMs);
                sv.AddScore(2000f, false, 0); // what the local kill credited
                ApplyAwards(idB, new Vector2(0f, 0f), back);
                Check(Near(sv.PointScore(0) - s0, 1234.5f),
                    "an over-credited local kill settles DOWN to the host figure (net +1234.5, not +2000)");
                Check(Near(UnsettledFor(0), 0f), "settled entry leaves the unsettled books (=" + UnsettledFor(0) + ")");

                // 5. A settled entry must not pay again if the EvDeath is re-delivered -- the
                //    provisional branch has to mark the slot paid, not just consume the entry.
                s0 = sv.PointScore(0);
                ApplyAwards(idB, new Vector2(0f, 0f), back);
                Check(Near(sv.PointScore(0) - s0, 0f), "a re-delivered EvDeath for a SETTLED id pays nothing extra");

                // 6. Expiry: a local credit the host never echoes back (its copy was already
                //    dead, so it paid our claim without re-broadcasting) must leave the books on
                //    age alone, WITHOUT touching the score -- the next sync then lands on the
                //    host's exact number. This is the one branch that drops a credit with no
                //    compensating score change, so it is worth an explicit case.
                s0 = sv.PointScore(0);
                scoreLedger.NoteLocal(idC, 0, 500f,
                    NetHost.Current.NowMs - (long)NetScoreLedger.AwardSettleWindowMs - 1);
                Check(Near(UnsettledFor(0), 0f), "an aged-out provisional is swept off the books");
                Check(Near(sv.PointScore(0) - s0, 0f), "sweeping an aged-out provisional does not move the score");
            }
            finally
            {
                paidLedger.Remove(idA);
                paidLedger.Remove(idB);
                // Settle the synthetic ids individually -- a blanket Reset() would wipe a live
                // session's real in-flight credits, and CLAUDE.md points people at this test
                // mid-session to compare two peers.
                scoreLedger.Settle(idA, 0, 0f, out _);
                scoreLedger.Settle(idB, 0, 0f, out _);
                scoreLedger.Settle(idC, 0, 0f, out _);
                for (int i = 0; i < NetProtocol.MaxSlots; i++)
                {
                    sv.NetSetScore(i, before[i], 0f);
                }
                score = prev;
            }
            sb.Insert(0, "[netscore] wire+apply round trip (real EncodeDeathEvent -> ApplyAwards -> live ScoreVisualiser)\n");
            sb.Append(fail == 0 ? "[netscore] wire PASS (" + pass + "/" + (pass + fail) + ")"
                                : "[netscore] wire FAIL (" + fail + " of " + (pass + fail) + ")");
            return sb.ToString();
        }

        // Relative, not absolute: PointScore is a float, so at a six-figure score the ULP
        // alone exceeds a fixed 0.05 and a late-run check would report a spurious FAIL.
        private static bool Near(float a, float b)
        {
            return Math.Abs(a - b) <= Math.Max(0.05f, Math.Abs(b) * 1e-4f);
        }

        // ---- provisional local credits (card b0ab09ec) -----------------------------------------

        // A local kill just credited `amount` to `slot` using OUR combo multiplier. Book it as
        // provisional so EvScoreSync can carry it on top of the host's score until the host's
        // own figure for this entity arrives. Entities with no netId (nothing replicated, so
        // nothing to reconcile against) are ignored.
        internal static void NoteLocalAward(INetEntity comp, byte slot, float amount)
        {
            if (enabled && idByComp.TryGetValue((GameComponent)comp, out ushort netId))
            {
                scoreLedger.NoteLocal(netId, slot, amount, NetHost.Current.NowMs);
            }
        }

        // Drop every provisional credit. Called on a replicated reset, where the score reverts
        // to a checkpoint baseline that pre-revert credits must not be added on top of.
        internal static void ResetScoreLedger()
        {
            scoreLedger.Reset();
        }

        // The provisional total still riding on top of the host's authoritative score for a slot.
        // No `enabled` gate: Disable() empties the ledger, so a dead session already reads 0 --
        // and gating here would make WireRoundTripTest's check of it vacuously true.
        internal static float UnsettledFor(int slot)
        {
            return scoreLedger.Unsettled(slot, NetHost.Current.NowMs);
        }

        private static void MarkPaid(ushort netId, byte slot)
        {
            if (slot >= NetProtocol.PayableSlots)
            {
                return;
            }
            if (paidLedger.TryGetValue(netId, out byte mask))
            {
                paidLedger[netId] = (byte)(mask | (1 << slot));
                return;
            }
            paidLedger[netId] = (byte)(1 << slot);
            paidOrder.Enqueue(netId);
            while (paidOrder.Count > LedgerCap)
            {
                paidLedger.Remove(paidOrder.Dequeue());
            }
        }

        // A recycled scratch Bullet as the forced-kill agent: per-type KilledBy overrides
        // freely cast `other` to Bullet (speed for corpse impulses, etc.), so a real Bullet
        // with the claimant's slot is the only cast-safe IAlienKiller. Never added to the
        // world.
        internal static Bullet KillerAgent(byte slot, Vector2 at)
        {
            if (scratchKiller == null)
            {
                scratchKiller = new Bullet(game ?? NetSession.SessionGame);
            }
            scratchKiller.Setup(at, 0f, 1f, slot);
            return scratchKiller;
        }

        internal static void EnsureGame(Game g)
        {
            if (game == null)
            {
                game = g;
            }
        }
    }

    // The one component that animates every frozen puppet. Deliberately updates far before
    // the world (UpdateOrder -1000) so a post-pause tick re-freezes puppets before any of
    // them could run gameplay. Pause's ComponentBin.Push disables it like everything else,
    // which also turns puppet collisions off (see CollidableOverride).
    public sealed class NetPuppetDriver : GameComponent
    {
        // The puppet clock runs on REAL time (the host's NowMs delta), never the
        // turbo/slow-mo/hit-stop-scaled game time Game1.Update folds into the gameTime it
        // hands components -- see NetPuppets.Drive for why (the pupPops burst). Clamped like
        // NetSession's own realDtMs so a long stall (a pause Pop re-enabling us, a tab
        // refocus) advances the dead-reckoning by at most one over-long frame, never a fling.
        // Real time is the HOST's clock since card 25ad0659 step 2a, so a scenario driving a
        // virtual clock advances the puppets with it rather than racing the wall clock.
        private long lastRealMs;

        public NetPuppetDriver(Game game)
            : base(game)
        {
            UpdateOrder = -1000;
        }

        public override void Update(GameTime gameTime)
        {
            long now = NetHost.Current.NowMs;
            float dtMs = lastRealMs == 0L
                ? (float)gameTime.ElapsedGameTime.TotalMilliseconds
                : MathHelper.Clamp(now - lastRealMs, 0f, 200f);
            lastRealMs = now;
            NetPuppets.Drive(dtMs);
            base.Update(gameTime);
        }
    }
}
