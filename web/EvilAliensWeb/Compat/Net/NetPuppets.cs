using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
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
        private const float CorrectionWindowMs = 150f; // blend a snapshot error over this
        private const float SnapThresholdPx = 100f;    // bigger error: snap + count a pop
        private const int LedgerCap = 512;

        private sealed class PuppetInfo
        {
            public AlienDrawableGameComponent Comp;
            public byte TypeIdx;
            public Vector2 Vel;          // design px/ms from the last snapshot
            public Vector2 Correction;   // remaining position error being blended away
            public float CorrectionMsLeft;
            public float TargetScale;
            public bool HasSnapshot;
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
            bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            score = ServiceHelper.Get<IScoreService>().Score;
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

        // ---- wire -> puppets ----------------------------------------------------------------

        public static bool OnSpawn(ushort netId, byte typeIdx, in NetBaseState state, byte[] buf, int off, int len)
        {
            if (!enabled || byId.ContainsKey(netId))
            {
                return false;
            }
            INetTypeDescriptor desc = NetTypeRegistry.Get(typeIdx);
            if (desc == null)
            {
                return false;
            }
            AlienDrawableGameComponent comp;
            constructing = true;
            try
            {
                comp = desc.CreatePuppet(bin, game, state, buf, off, len);
                if (comp == null)
                {
                    // A descriptor may legitimately decline (e.g. a Ball with no JunkBoss).
                    // Mark the id removed so the snapshot self-heal doesn't re-attempt
                    // construction every 60ms turn -- it retries after the suppression window.
                    MarkRemoved(netId);
                    return false;
                }
                bin.Add((GameComponent)(object)comp);
            }
            finally
            {
                constructing = false;
            }
            comp.Enabled = false; // frozen from the first tick (bin.Add force-enables)
            PuppetInfo info = new PuppetInfo
            {
                Comp = comp,
                TypeIdx = typeIdx,
                Vel = state.Vel,
                TargetScale = state.Scale > 0f ? state.Scale : comp.scale,
            };
            ApplySnapshotState(info, state, null, null, 0, 0, isSpawn: true);
            byId[netId] = info;
            idByComp[(GameComponent)(object)comp] = netId;
            live.Add(info);
            return true;
        }

        public static bool OnSnapshotEntry(ushort netId, byte typeIdx, in NetBaseState state, byte[] buf, int extraOff, int extraLen, out bool popped)
        {
            popped = false;
            if (!enabled)
            {
                return false;
            }
            if (!byId.TryGetValue(netId, out PuppetInfo info))
            {
                // Self-heal: an id we never built (spawn raced the stream / a local purge
                // dropped the world while the host's lives on) is reconstructed from the
                // snapshot itself -- default construction extras, so a variant may look
                // generic until nothing (spawn extras only pick cosmetics). An id that died
                // HERE moments ago is a claim still in flight: leave it dead.
                if (!IsRecentlyRemoved(netId))
                {
                    OnSpawn(netId, typeIdx, state, buf, extraOff, 0);
                }
                return false;
            }
            INetTypeDescriptor desc = NetTypeRegistry.Get(info.TypeIdx);
            popped = ApplySnapshotState(info, state, desc, buf, extraOff, extraLen, isSpawn: false);
            return true;
        }

        private static bool IsRecentlyRemoved(ushort netId)
        {
            return recentlyRemoved.TryGetValue(netId, out long at)
                && Environment.TickCount64 - at < RecentRemovalWindowMs;
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
            recentlyRemoved[netId] = Environment.TickCount64;
        }

        private static bool ApplySnapshotState(PuppetInfo info, in NetBaseState state, INetTypeDescriptor desc, byte[] buf, int extraOff, int extraLen, bool isSpawn)
        {
            bool popped = false;
            AlienDrawableGameComponent comp = info.Comp;
            if (isSpawn || !info.HasSnapshot)
            {
                comp.Position = state.Pos;
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
                    info.CorrectionMsLeft = CorrectionWindowMs;
                }
            }
            info.Vel = state.Vel;
            info.TargetScale = state.Scale;
            info.HasSnapshot = true;
            if (comp.NetSpinPerMs == 0f)
            {
                comp.rotation = state.Rotation; // free-spinners rotate locally -- see NetSpinPerMs
            }
            comp.NetSetFrame(state.CurFrame);
            comp.NetSpeedVector = state.Vel; // per-type Draw reading Direction stays truthful
            if (state.Hp > 0 && comp is KillableAlien killable)
            {
                killable.NetApplyHp(state.Hp);
            }
            // ORDER MATTERS: state extras run LAST. The base writes above have per-type side
            // effects (NetSpeedVector's setter rewrites Direction, which zeroes Lazer's beam
            // angle) that an extra must be able to re-assert -- see Lazer.NetApplyBeam.
            desc?.ApplyStateExtra(comp, buf, extraOff, extraLen);
            return popped;
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
                AlienDrawableGameComponent comp = info.Comp;
                remoteDeaths.Add((GameComponent)(object)comp); // never echo this back as a claim
                if (killerSlot != NetProtocol.KillerNone && comp is KillableAlien killable)
                {
                    comp.NetSuppressAward();
                    killable.NetKill(KillerAgent(killerSlot, comp.Position), isComboGenerator: true);
                    if (!comp.IsDead)
                    {
                        bin.Remove((GameComponent)(object)comp); // dead-guarded NetKill no-op
                    }
                    ApplyAwards(netId, comp.Position, awards);
                }
                else if (killerSlot != NetProtocol.KillerNone && comp is Powerup pu)
                {
                    // A powerup is a PICKUP, not a kill -- it must not take the generic-burst
                    // branch below (an explosion where the other player collected). Drive the
                    // collector's HUD slot instead; see NetSession.ApplyRemotePowerup.
                    MarkPaid(netId, killerSlot);
                    pu.taken = true;
                    NetSession.ApplyRemotePowerup(pu, killerSlot);
                    bin.Remove((GameComponent)(object)comp);
                }
                else if (killerSlot != NetProtocol.KillerNone)
                {
                    // Non-killable replicable (Asteroid/EvilBullet/...): approximate the
                    // death look with a generic burst + credit the killer.
                    ApplyAwards(netId, comp.Position, awards);
                    Explosion explosion = Explosion.NewExplosion(bin, game);
                    explosion.Setup(comp.Position, 1.2f, 1f, 0f, 0f);
                    bin.Add((GameComponent)(object)explosion);
                    bin.Remove((GameComponent)(object)comp);
                }
                else
                {
                    bin.Remove((GameComponent)(object)comp); // plain despawn / fly-off
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
                float delta = scoreLedger.Settle(netId, slot, hostAward, out bool wasProvisional);
                if (wasProvisional)
                {
                    // Correct silently -- the player already saw the floating text for this kill.
                    if (delta != 0f)
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
            var comp = (AlienDrawableGameComponent)(object)gc;
            // Claim only GAMEPLAY deaths (Die() ran => IsDead), never scene teardown purges,
            // and never deaths we ourselves applied from a host EvDeath (echo guard).
            if (remoteDeaths.Remove(gc) || !comp.IsDead)
            {
                return;
            }
            byte killerSlot = NetSession.TakeKillNote(comp);
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
                AlienDrawableGameComponent comp = info.Comp;
                comp.Enabled = false; // re-assert the freeze (pause Pop / stray enables)
                // Hold position while the peer is stalled: the last-known velocity is stale by
                // up to the whole grace window, and dead-reckoning on it for seconds would fling
                // the enemy world hundreds of px off -- which the LOCAL player can be killed by,
                // since each peer owns its own hits -- then snap back hard when the host returns.
                // ShipStateBuffer caps its own extrapolation at 250ms for exactly this reason.
                // An in-flight correction still drains; it is finishing a snapshot we DID get.
                Vector2 step = NetSession.PeerStalled ? Vector2.Zero : info.Vel * dtMs;
                if (info.CorrectionMsLeft > 0f)
                {
                    float take = MathHelper.Min(dtMs, info.CorrectionMsLeft);
                    step += info.Correction * (take / CorrectionWindowMs);
                    info.CorrectionMsLeft -= take;
                }
                comp.Position += step;
                comp.rotation += comp.NetSpinPerMs * dtMs; // no-op unless the type opted out of replicated rotation
                comp.NetAdvanceFrame(dtSeconds);
                if (info.HasSnapshot && info.TargetScale > 0f)
                {
                    comp.scale = MathHelper.Lerp(comp.scale, info.TargetScale, MathHelper.Clamp(dtMs / 100f, 0f, 1f));
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
            return slot < 8 && paidLedger.TryGetValue(netId, out byte mask) && (mask & (1 << slot)) != 0;
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
            ScoreVisualiser sv = ServiceHelper.Get<IScoreService>().Score;
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
            // Deliberately far above any id a live session allocates, so a stray real entry
            // cannot collide with the scenarios below.
            const ushort idA = 60001;
            const ushort idB = 60002;
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
                scoreLedger.NoteLocal(idB, 0, 2000f, Environment.TickCount64);
                sv.AddScore(2000f, false, 0); // what the local kill credited
                ApplyAwards(idB, new Vector2(0f, 0f), back);
                Check(Near(sv.PointScore(0) - s0, 1234.5f),
                    "an over-credited local kill settles DOWN to the host figure (net +1234.5, not +2000)");
                Check(Near(UnsettledFor(0), 0f), "settled entry leaves the unsettled books (=" + UnsettledFor(0) + ")");
            }
            finally
            {
                paidLedger.Remove(idA);
                paidLedger.Remove(idB);
                scoreLedger.Reset();
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

        private static bool Near(float a, float b)
        {
            return Math.Abs(a - b) < 0.05f;
        }

        // ---- provisional local credits (card b0ab09ec) -----------------------------------------

        // A local kill just credited `amount` to `slot` using OUR combo multiplier. Book it as
        // provisional so EvScoreSync can carry it on top of the host's score until the host's
        // own figure for this entity arrives. Entities with no netId (nothing replicated, so
        // nothing to reconcile against) are ignored.
        internal static void NoteLocalAward(AlienDrawableGameComponent comp, byte slot, float amount)
        {
            if (enabled && idByComp.TryGetValue((GameComponent)(object)comp, out ushort netId))
            {
                scoreLedger.NoteLocal(netId, slot, amount, Environment.TickCount64);
            }
        }

        // The provisional total still riding on top of the host's authoritative score for a slot.
        // No `enabled` gate: Disable() empties the ledger, so a dead session already reads 0 --
        // and gating here would make WireRoundTripTest's check of it vacuously true.
        internal static float UnsettledFor(int slot)
        {
            return scoreLedger.Unsettled(slot, Environment.TickCount64);
        }

        private static void MarkPaid(ushort netId, byte slot)
        {
            if (slot >= 8)
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
        // The puppet clock runs on REAL time (Environment.TickCount64 delta), never the
        // turbo/slow-mo/hit-stop-scaled game time Game1.Update folds into the gameTime it
        // hands components -- see NetPuppets.Drive for why (the pupPops burst). Clamped like
        // NetSession's own realDtMs so a long stall (a pause Pop re-enabling us, a tab
        // refocus) advances the dead-reckoning by at most one over-long frame, never a fling.
        private long lastRealMs;

        public NetPuppetDriver(Game game)
            : base(game)
        {
            UpdateOrder = -1000;
        }

        public override void Update(GameTime gameTime)
        {
            long now = Environment.TickCount64;
            float dtMs = lastRealMs == 0L
                ? (float)gameTime.ElapsedGameTime.TotalMilliseconds
                : MathHelper.Clamp(now - lastRealMs, 0f, 200f);
            lastRealMs = now;
            NetPuppets.Drive(dtMs);
            base.Update(gameTime);
        }
    }
}
