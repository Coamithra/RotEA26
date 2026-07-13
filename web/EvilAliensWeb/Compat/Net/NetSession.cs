using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Stage 11 co-op session orchestrator (design: plans/stage11-online-coop.md).
    // Distributed authority: each peer owns its OWN ship completely -- local input is read
    // untouched with zero added latency; the wire carries ship STATE (never inputs); the
    // other peer's ship is an interpolated puppet ~InterpDelayMs behind, whose shots spawn
    // locally from its replicated firing state.
    //
    // Card 11.2 adds WORLD authority: the host runs the real sim (spawners, level script,
    // enemy AI, score/lives); a join peer suppresses those (GameScene skips its event list,
    // ComponentBin swallows stray replicable spawns) and mirrors the world as frozen
    // NetPuppets driven by ~16.7Hz round-robin world snapshots. Kills and powerups are
    // GENEROUS at-least-once claims: every claimant is credited (once per (entity, slot)
    // per side), the first claim settles the entity, nothing is ever rejected. Score/lives
    // true up from the host's 1Hz EvScoreSync.
    //
    // Lifecycle: Game1.Initialize calls Start() iff ?net=host/join was parsed (a plain boot
    // never constructs anything here -- Active stays false and Update() is a single branch);
    // Game1.UpdateInner ticks Update() once per game tick. All received messages are queued
    // by the transport's JS-driven callbacks and drained ON the game tick, so game state is
    // only ever mutated inside the normal update.
    //
    // Remaining gaps (by design, later cards): level-script beats (messages/music/boss
    // phases), pause + checkpoint-reset/shared-fate death, tether; WebRTC transport; the
    // roster is exactly two peers.
    public static class NetSession
    {
        public const byte ProtocolVersion = 2;
        public const float InterpDelayMs = 100f;

        private const long StreamIntervalMs = 33;    // ~30 Hz ship stream
        private const long SnapshotIntervalMs = 60;  // ~16.7 Hz world snapshot (host)
        private const long ScoreSyncIntervalMs = 1000;
        private const long HelloIntervalMs = 1000;
        private const long PeerTimeoutMs = 3000;
        private const long MetricsIntervalMs = 5000;
        private const float FiringHoldMs = 150f;     // "still firing" window after the last FireAt intent
        private const float RenderClockSnapMs = 250f;
        // Pop detection: a rendered step larger than any plausible ship motion over the same
        // real time (PlayerShip.MaxSpeed is 0.33 px/ms; x2 margin + slack for frame jitter).
        private const float ShipMaxSpeedPxPerMs = 0.33f;
        private const float PopSlackPx = 3f;

        private const int SnapshotMaxEntries = 16;   // <= ~500B/packet within extras budget
        private const int SnapshotScratchBytes = 2 + SnapshotMaxEntries * 64;
        private const int ExtraScratchBytes = 64;
        private const int DeathRecordCap = 512;

        public static bool Active { get; private set; }
        public static bool PeerUp { get; private set; }

        public static bool IsHost => Active && isHost;
        public static bool IsClient => Active && !isHost;

        // Client sim-split: the join peer never runs the level script / spawners (GameScene
        // checks this before eventList.Update) and never lets game code add replicable
        // types to the world (ComponentBin.Add checks SuppressWorldSpawn).
        public static bool SuppressLevelScript => IsClient;

        public static bool SuppressWorldSpawn(GameComponent component)
        {
            return IsClient && !NetPuppets.Constructing && NetTypeRegistry.IsReplicable(component);
        }

        // ComponentBin.Pop must not thaw a frozen puppet back into a live AI.
        public static bool IsFrozenPuppet(GameComponent component)
        {
            return IsClient && NetPuppets.IsPuppet(component);
        }

        internal static Game SessionGame => game;

        private static bool isHost;
        private static Game game;
        private static Oracle oracle;
        private static ComponentBin bin;
        private static SoundManager sound;
        private static ScoreVisualiser score;
        private static INetTransport transport;

        private static readonly Queue<(byte[] data, bool reliable)> rxQueue = new Queue<(byte[], bool)>();

        // handshake / heartbeat
        private static long sessionStartAt;
        private static long lastHelloTx;
        private static long lastRxStreamAt;

        // tx
        private static ushort txSeq;
        private static ushort txEventSeq;
        private static long lastStreamTx;
        private static long lastSnapshotTx;
        private static long lastScoreSyncTx;
        private static Vector2 lastTxPos = new Vector2(400f, 300f);
        private static float lastTxAim = 4.712389f;
        private static int snapshotCursor;
        private static readonly byte[] snapshotScratch = new byte[SnapshotScratchBytes];
        private static readonly byte[] extraScratch = new byte[ExtraScratchBytes];

        // rx / remote-ship puppet
        private static readonly ShipStateBuffer buffer = new ShipStateBuffer();
        private static ushort lastRxSeq;
        private static bool haveRxSeq;
        private static bool remoteAlive;
        private static int remoteShotsPerSec = 8;
        private static float remoteBulletLife = 450f;
        private static PlayerShip puppet;
        private static double renderMs = double.NaN;
        private static long lastUpdateAt;
        private static float realDtMs;
        private static Vector2 lastPuppetPos;
        private static bool hasLastPuppetPos;

        // reliable-event bookkeeping
        private static int lastRxEventSeq = -1;

        // Kill attribution: who landed the killing blow, recorded just before the per-type
        // death cascades into removal (KillableAlien.HitBy / claim handling), consumed at
        // the removal seam on either side.
        private static readonly Dictionary<AlienDrawableGameComponent, byte> killNotes = new Dictionary<AlienDrawableGameComponent, byte>();
        private static readonly Queue<AlienDrawableGameComponent> killNoteOrder = new Queue<AlienDrawableGameComponent>();

        // Host: recently-dead replicables so late/overlapping claims still pay generously,
        // exactly once per (entity, slot). Bounded FIFO.
        private struct DeathRecord
        {
            public Vector2 Pos;
            public ushort Points;
            public byte PaidMask;
            public bool OneUp; // extra-life powerup: a late claim must still AddLife (lives are host-authoritative)
        }

        private static readonly Dictionary<ushort, DeathRecord> recentDeaths = new Dictionary<ushort, DeathRecord>();
        private static readonly Queue<ushort> recentDeathOrder = new Queue<ushort>();

        private static readonly NetMetrics metrics = new NetMetrics();
        private static long lastMetricsAt;

        private static long NowMs => Environment.TickCount64;

        public static void Start(Game g)
        {
            if (Active || DebugFlags.NetRole == NetRole.None)
            {
                return;
            }
            game = g;
            oracle = ServiceHelper.Get<IOracleService>().Oracle;
            bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
            score = ServiceHelper.Get<IScoreService>().Score;
            isHost = DebugFlags.NetRole == NetRole.Host;
            transport = new BroadcastChannelTransport();
            transport.OnData += (data, reliable, from) => rxQueue.Enqueue((data, reliable));
            transport.OnPeerBye += from => PeerLost("bye");
            transport.Open(DebugFlags.NetRoom);
            if (isHost)
            {
                NetIdRegistry.Enable(g);
            }
            else
            {
                NetPuppets.Enable(g);
            }
            Active = true;
            sessionStartAt = NowMs;
            lastMetricsAt = sessionStartAt;
            Console.WriteLine("[net] session start role=" + (isHost ? "host" : "join")
                + " room=" + DebugFlags.NetRoom + " protocol=v" + ProtocolVersion
                + " transport=BroadcastChannel");
        }

        // Ticked once per game tick from Game1.UpdateInner. Cadence runs on REAL time
        // (TickCount64), so turbo/slowmo/hit-stop never starve the stream or heartbeats.
        public static void Update()
        {
            if (!Active)
            {
                return;
            }
            long now = NowMs;
            realDtMs = lastUpdateAt == 0 ? 16f : MathHelper.Clamp(now - lastUpdateAt, 0f, 200f);
            lastUpdateAt = now;
            DrainRx();
            AdvanceRenderClock();
            if (!PeerUp)
            {
                if (now - lastHelloTx >= HelloIntervalMs)
                {
                    lastHelloTx = now;
                    transport.SendReliable(NetProtocol.EncodeHello(ProtocolVersion, isHost));
                }
            }
            else
            {
                if (now - lastRxStreamAt > PeerTimeoutMs)
                {
                    PeerLost("timeout");
                }
                else
                {
                    if (now - lastStreamTx >= StreamIntervalMs)
                    {
                        SendShipState(now);
                    }
                    if (isHost && now - lastSnapshotTx >= SnapshotIntervalMs)
                    {
                        SendWorldSnapshot(now);
                    }
                    if (isHost && now - lastScoreSyncTx >= ScoreSyncIntervalMs)
                    {
                        SendScoreSync(now);
                    }
                }
            }
            ManagePuppet();
            if (now - lastMetricsAt >= MetricsIntervalMs)
            {
                lastMetricsAt = now;
                Console.WriteLine(metrics.Report(isHost, PeerUp, isHost ? NetIdRegistry.LiveCount : NetPuppets.LiveCount));
            }
        }

        // ---- local ship -> wire ---------------------------------------------------------

        private static void SendShipState(long now)
        {
            lastStreamTx = now;
            PlayerShip local = FindLocalShip();
            bool alive = local != null;
            Vector2 pos = lastTxPos;
            Vector2 vel = Vector2.Zero;
            float aim = lastTxAim;
            bool firing = false;
            int shots = 8;
            float bulletLife = 450f;
            if (alive)
            {
                pos = local.GetPosition();
                vel = local.NetVelocity;
                firing = now - local.NetLastFireMs < FiringHoldMs;
                if (firing || local.NetLastFireMs > 0)
                {
                    aim = local.NetLastFireAim;
                }
                shots = local.NetShotsPerSec;
                bulletLife = local.NetBulletLife;
                lastTxPos = pos;
                lastTxAim = aim;
            }
            transport.SendStream(NetProtocol.EncodeShipState(txSeq++, (uint)(now - sessionStartAt), pos, vel, aim, alive, firing, shots, bulletLife));
            metrics.StreamTx++;
        }

        // The streamed ship: the primary local player (never the remote puppet, never the
        // in-game AI "friend" ships -- with ?aiplayer the controller stays Keyboard/pad and
        // only the Update branch is forced to AI, so this stays correct).
        private static PlayerShip FindLocalShip()
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.Controller != ControlDevice.Remote && s.Controller != ControlDevice.AI)
                {
                    return s;
                }
            }
            return null;
        }

        // Called from PlayerShip.doBlast (bombs are discrete -> the reliable event lane,
        // not the stream). Only the streamed local ship replicates its blasts.
        public static void OnLocalBlast(PlayerShip ship, Vector2 pos, int level)
        {
            if (!Active || !PeerUp || ship == null || ship.Controller == ControlDevice.Remote || ship.Controller == ControlDevice.AI)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeBlastEvent(txEventSeq++, pos, level));
            metrics.EventsTx++;
        }

        // ---- kill attribution notes ---------------------------------------------------------

        // Recorded by KillableAlien.HitBy at the killing blow (both sides run it: the host
        // through its real sim, the client on its frozen puppets via local hit-testing).
        public static void NoteKill(AlienDrawableGameComponent comp, ICollidable killer)
        {
            if (!Active || !NetTypeRegistry.IsReplicable((GameComponent)(object)comp))
            {
                return;
            }
            int slot = killer is IAlienKiller k ? k.Player() : -1;
            NoteKillSlot(comp, slot >= 0 && slot < 8 ? (byte)slot : NetProtocol.KillerNone);
        }

        internal static void NoteKillSlot(AlienDrawableGameComponent comp, byte slot)
        {
            if (!Active)
            {
                return;
            }
            // Leak guard: bounded FIFO (notes are normally consumed at removal within a
            // tick). Evict oldest instead of clearing -- a wholesale clear would drop
            // legitimate pending attributions in a dense wave.
            while (killNoteOrder.Count > 0 && !killNotes.ContainsKey(killNoteOrder.Peek()))
            {
                killNoteOrder.Dequeue(); // drain keys already consumed by TakeKillNote
            }
            if (!killNotes.ContainsKey(comp))
            {
                killNoteOrder.Enqueue(comp);
            }
            killNotes[comp] = slot;
            while (killNotes.Count > 64 && killNoteOrder.Count > 0)
            {
                killNotes.Remove(killNoteOrder.Dequeue());
            }
        }

        // Powerup pickups are claims too: the collecting side records WHO took it before
        // Powerup.Die() cascades into removal.
        public static void NotePowerupTaken(Powerup powerup, int playerSlot)
        {
            if (Active && playerSlot >= 0 && playerSlot < 8)
            {
                NoteKillSlot(powerup, (byte)playerSlot);
            }
        }

        internal static byte TakeKillNote(AlienDrawableGameComponent comp)
        {
            if (killNotes.TryGetValue(comp, out byte slot))
            {
                killNotes.Remove(comp);
                return slot;
            }
            return NetProtocol.KillerNone;
        }

        // Wire player slots are HOST-relative (0 = host ship, 1 = join ship), but each side
        // numbers its LOCAL ship slot 0 (the join peer's oracle seats its own player first and
        // the remote puppet second) -- so the JOIN side swaps 0<->1 at the wire boundary, in
        // both directions. Host-side and KillerNone/AI slots pass through untouched.
        private static byte TranslateSlot(byte slot)
        {
            if (isHost || slot > 1)
            {
                return slot;
            }
            return (byte)(1 - slot);
        }

        // ---- host NetIdRegistry -> wire ---------------------------------------------------

        internal static void OnHostSpawn(NetIdRegistry.Entry e)
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            NetBaseState state = CaptureBaseState(e, NowMs);
            int extraLen = e.Descriptor.EncodeSpawnExtra(e.Comp, extraScratch, 0);
            transport.SendReliable(NetProtocol.EncodeSpawnEvent(txEventSeq++, e.Id, e.TypeIdx, state, extraScratch, extraLen));
            metrics.EventsTx++;
            if (DebugFlags.NetLog)
            {
                Console.WriteLine("[net] tx spawn id=" + e.Id + " type=" + e.Comp.GetType().Name);
            }
        }

        internal static void OnHostDeath(NetIdRegistry.Entry e)
        {
            if (!Active)
            {
                return;
            }
            byte killer = TakeKillNote(e.Comp);
            Vector2 pos = e.Comp.Position;
            ushort points = (ushort)MathHelper.Clamp(e.Comp.NetPointValue, 0f, 65535f);
            RecordDeath(e.Id, pos, points, killer, e.Comp is Powerup pu && pu.type == Powerup.PowerupType.OneUp);
            if (!PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeDeathEvent(txEventSeq++, e.Id, killer, pos, points));
            metrics.EventsTx++;
            if (DebugFlags.NetLog)
            {
                Console.WriteLine("[net] tx death id=" + e.Id + " killer=" + killer);
            }
        }

        private static void RecordDeath(ushort id, Vector2 pos, ushort points, byte killerSlot, bool oneUp)
        {
            DeathRecord rec = new DeathRecord
            {
                Pos = pos,
                Points = points,
                PaidMask = (byte)(killerSlot < 8 ? 1 << killerSlot : 0),
                OneUp = oneUp,
            };
            if (!recentDeaths.ContainsKey(id))
            {
                recentDeathOrder.Enqueue(id);
                while (recentDeathOrder.Count > DeathRecordCap)
                {
                    recentDeaths.Remove(recentDeathOrder.Dequeue());
                }
            }
            recentDeaths[id] = rec;
        }

        // ---- host world snapshot ------------------------------------------------------------

        private static void SendWorldSnapshot(long now)
        {
            lastSnapshotTx = now;
            IReadOnlyList<NetIdRegistry.Entry> live = NetIdRegistry.Live;
            if (live.Count == 0)
            {
                return;
            }
            int count = Math.Min(SnapshotMaxEntries, live.Count);
            int off = NetProtocol.SnapshotHeaderBytes;
            int written = 0;
            if (snapshotCursor >= live.Count)
            {
                snapshotCursor = 0;
            }
            for (int i = 0; i < count; i++)
            {
                NetIdRegistry.Entry e = live[snapshotCursor % live.Count];
                // Fit check BEFORE consuming the cursor slot or capturing state --
                // CaptureBaseState advances the entity's observed-velocity baseline, so a
                // skipped entry must not touch it (it leads the next packet instead).
                int extraLen = e.Descriptor.EncodeStateExtra(e.Comp, extraScratch, 0);
                if (off + NetProtocol.SnapshotEntryBaseBytes + extraLen > snapshotScratch.Length)
                {
                    break;
                }
                snapshotCursor = (snapshotCursor + 1) % live.Count;
                NetBaseState state = CaptureBaseState(e, now);
                NetProtocol.WriteSnapshotEntry(snapshotScratch, ref off, e.Id, e.TypeIdx, state, extraScratch, extraLen);
                written++;
            }
            if (written == 0)
            {
                return;
            }
            snapshotScratch[0] = NetProtocol.MsgWorldSnapshot;
            snapshotScratch[1] = (byte)written;
            byte[] packet = new byte[off];
            Array.Copy(snapshotScratch, packet, off);
            transport.SendStream(packet);
            metrics.SnapTx++;
        }

        private static NetBaseState CaptureBaseState(NetIdRegistry.Entry e, long now)
        {
            AlienDrawableGameComponent c = e.Comp;
            Vector2 pos = c.Position;
            // Observed velocity: differentiate real positions between this entity's snapshot
            // turns -- robust for enemies that move Position directly (arcs, easing) where
            // Speed/Direction would lie. First observation falls back to SpeedVector.
            Vector2 vel = c.NetSpeedVector;
            if (e.HasLastPos && now > e.LastPosMs)
            {
                vel = (pos - e.LastPos) / (now - e.LastPosMs);
            }
            e.LastPos = pos;
            e.LastPosMs = now;
            e.HasLastPos = true;
            return new NetBaseState
            {
                Pos = pos,
                Vel = vel,
                Rotation = c.rotation,
                CurFrame = c.curframe,
                Scale = c.scale,
                Hp = c is KillableAlien k ? k.NetHitPoints : 0,
            };
        }

        // ---- host score sync ------------------------------------------------------------------

        private static void SendScoreSync(long now)
        {
            lastScoreSyncTx = now;
            transport.SendReliable(NetProtocol.EncodeScoreSync(txEventSeq++, score.Lives, score.PointScore(0), score.PointScore(1)));
            metrics.EventsTx++;
        }

        // ---- client claims ----------------------------------------------------------------------

        // Fired by NetPuppets at the removal seam for every gameplay death it observed
        // locally (its own bullets, the re-fired remote bullets, blasts, pickups).
        internal static void SendClaim(ushort netId, byte killerSlot)
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            killerSlot = TranslateSlot(killerSlot); // wire slots are host-relative
            transport.SendReliable(NetProtocol.EncodeClaimEvent(txEventSeq++, netId, killerSlot));
            metrics.EventsTx++;
            metrics.ClaimsTx++;
            if (DebugFlags.NetLog)
            {
                Console.WriteLine("[net] tx claim id=" + netId + " killer=" + killerSlot);
            }
        }

        // ---- wire -> state ----------------------------------------------------------------

        private static void DrainRx()
        {
            while (rxQueue.Count > 0)
            {
                (byte[] data, bool reliable) = rxQueue.Dequeue();
                if (data.Length == 0)
                {
                    continue;
                }
                switch (data[0])
                {
                case NetProtocol.MsgHello:
                    HandleHello(data, welcomeBack: true);
                    break;
                case NetProtocol.MsgWelcome:
                    HandleHello(data, welcomeBack: false);
                    break;
                case NetProtocol.MsgShipState:
                    HandleShipState(data);
                    break;
                case NetProtocol.MsgEvent:
                    HandleEvent(data);
                    break;
                case NetProtocol.MsgWorldSnapshot:
                    HandleWorldSnapshot(data);
                    break;
                }
            }
        }

        private static void HandleHello(byte[] data, bool welcomeBack)
        {
            if (data.Length < 3)
            {
                return;
            }
            byte ver = data[1];
            bool peerIsHost = data[2] != 0;
            if (ver != ProtocolVersion)
            {
                Console.WriteLine("[net] peer protocol v" + ver + " != v" + ProtocolVersion + " -- ignoring");
                return;
            }
            if (peerIsHost == isHost)
            {
                Console.WriteLine("[net] WARNING: peer has the SAME role (" + (isHost ? "host" : "join")
                    + ") in room '" + DebugFlags.NetRoom + "' -- one tab should use ?net="
                    + (isHost ? "join" : "host"));
                return;
            }
            if (welcomeBack)
            {
                transport.SendReliable(NetProtocol.EncodeWelcome(ProtocolVersion, isHost));
            }
            if (!PeerUp)
            {
                PeerConnected();
            }
        }

        private static void PeerConnected()
        {
            PeerUp = true;
            lastRxStreamAt = NowMs;
            Console.WriteLine("[net] peer connected (" + (isHost ? "join" : "host") + " side is up)");
            if (isHost)
            {
                // Late joiner: replay the live NetId set so it can construct the already-
                // alive world instead of starting from a death-before-spawn storm.
                NetIdRegistry.ReplayLive();
            }
            else
            {
                ApplyJoinHues();
            }
        }

        // Consistent ship colours across both screens: the host's ship is player-slot-0
        // white everywhere, the joiner's is player-slot-1 purple everywhere. On the join
        // side the LOCAL ship sits in slot 0, so swap that slot's hue (and any already-live
        // ship that read it at Setup).
        private static void ApplyJoinHues()
        {
            oracle.SetHue(300f, 0);
            oracle.SetHue(-1f, 1);
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.Owner < 2)
                {
                    s.NetApplyHue(oracle.Hue(s.Owner));
                }
            }
        }

        private static void PeerLost(string reason)
        {
            if (!PeerUp)
            {
                return;
            }
            PeerUp = false;
            Console.WriteLine("[net] peer lost (" + reason + ")");
            remoteAlive = false;
            if (puppet != null)
            {
                // Remove the puppet NOW (with the death FX) -- ManagePuppet won't, it
                // early-returns while the peer is down. 11.4 owns the real leave flow
                // ("any player leaves -> the match ends").
                ExplodePuppet();
            }
            buffer.Clear();
            renderMs = double.NaN;
            hasLastPuppetPos = false;
            haveRxSeq = false;
            lastRxEventSeq = -1;
        }

        private static void HandleShipState(byte[] data)
        {
            if (!NetProtocol.TryDecodeShipState(data, out ushort seq, out ShipSample sample, out int shots, out float bulletLife))
            {
                return;
            }
            lastRxStreamAt = NowMs;
            if (!PeerUp)
            {
                // Stream before/without a finished handshake (e.g. we reloaded mid-session):
                // treat it as the peer being up -- the stream IS the heartbeat.
                PeerConnected();
            }
            metrics.StreamRx++;
            if (haveRxSeq && (ushort)(seq - lastRxSeq) != 1)
            {
                // Loopback delivers in order; count anything else so the WebRTC transport
                // (11.4) gets loss/reorder visibility for free. Distinct from StreamDropped
                // (the buffer's authoritative sample-refused count) so neither double-counts.
                metrics.StreamSeqGaps++;
            }
            lastRxSeq = seq;
            haveRxSeq = true;
            remoteAlive = sample.Alive;
            remoteShotsPerSec = shots;
            remoteBulletLife = bulletLife;
            if (!buffer.Add(sample))
            {
                metrics.StreamDropped++;
            }
        }

        private static void HandleWorldSnapshot(byte[] data)
        {
            if (isHost || data.Length < NetProtocol.SnapshotHeaderBytes)
            {
                return;
            }
            lastRxStreamAt = NowMs;
            metrics.SnapRx++;
            int count = data[1];
            int off = NetProtocol.SnapshotHeaderBytes;
            for (int i = 0; i < count; i++)
            {
                if (!NetProtocol.TryReadSnapshotEntry(data, ref off, out ushort netId, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen))
                {
                    break;
                }
                metrics.SnapEntriesRx++;
                if (NetPuppets.OnSnapshotEntry(netId, typeIdx, state, data, extraOff, extraLen, out bool popped))
                {
                    if (popped)
                    {
                        metrics.PuppetPops++;
                    }
                }
                else
                {
                    // Not spawned yet (stream outran the reliable lane) or died locally with
                    // the claim still in flight -- both self-heal; just count it.
                    metrics.SnapUnknownIds++;
                }
            }
        }

        private static void HandleEvent(byte[] data)
        {
            if (data.Length < 4)
            {
                return;
            }
            byte eventType = data[1];
            int seq = NetProtocol.ReadU16(data, 2);
            metrics.EventsRx++;
            if (lastRxEventSeq >= 0 && (ushort)(seq - lastRxEventSeq) != 1)
            {
                metrics.SeqGaps++;
            }
            lastRxEventSeq = seq;
            switch (eventType)
            {
            case NetProtocol.EvSpawn:
            {
                if (isHost || !NetProtocol.TryDecodeSpawnEvent(data, out ushort id, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen))
                {
                    return;
                }
                if (!NetPuppets.OnSpawn(id, typeIdx, state, data, extraOff, extraLen))
                {
                    metrics.DupSpawns++;
                }
                if (DebugFlags.NetLog)
                {
                    Console.WriteLine("[net] rx spawn id=" + id + " typeIdx=" + typeIdx);
                }
                break;
            }
            case NetProtocol.EvDeath:
            {
                if (isHost || data.Length < 17)
                {
                    return;
                }
                ushort id = NetProtocol.ReadU16(data, 4);
                byte killer = TranslateSlot(data[6]); // wire slots are host-relative
                Vector2 pos = new Vector2(NetProtocol.ReadF32(data, 7), NetProtocol.ReadF32(data, 11));
                ushort points = NetProtocol.ReadU16(data, 15);
                NetPuppets.OnRemoteDeath(id, killer, pos, points);
                if (DebugFlags.NetLog)
                {
                    Console.WriteLine("[net] rx death id=" + id + " killer=" + killer);
                }
                break;
            }
            case NetProtocol.EvClaim:
            {
                if (!isHost || data.Length < 7)
                {
                    return;
                }
                HandleClaim(NetProtocol.ReadU16(data, 4), data[6]);
                break;
            }
            case NetProtocol.EvScoreSync:
            {
                if (isHost || data.Length < 13)
                {
                    return;
                }
                score.Lives = (sbyte)data[4];
                // Wire order is host-relative: score0 = the host's ship = OUR slot 1, etc.
                score.NetAdoptScore(TranslateSlot(0), NetProtocol.ReadF32(data, 5));
                score.NetAdoptScore(TranslateSlot(1), NetProtocol.ReadF32(data, 9));
                break;
            }
            case NetProtocol.EvBlast:
            {
                if (data.Length < 13 || puppet == null)
                {
                    return;
                }
                int level = data[12];
                puppet.NetDoBlast(level);
                if (DebugFlags.NetLog)
                {
                    Console.WriteLine("[net] rx blast level=" + level);
                }
                break;
            }
            }
        }

        // GENEROUS at-least-once claim honoring (host). Alive -> the real per-type death
        // path credited to the claimant (authoritative children spawn here and replicate).
        // Already dead -> pay the claimant once from the death record. Never rejected.
        private static void HandleClaim(ushort netId, byte killerSlot)
        {
            metrics.ClaimsRx++;
            if (NetIdRegistry.TryGetById(netId, out NetIdRegistry.Entry e) && !e.Comp.IsDead)
            {
                if (killerSlot != NetProtocol.KillerNone)
                {
                    NoteKillSlot(e.Comp, killerSlot); // attribution for the death broadcast
                }
                if (e.Comp is KillableAlien killable && killerSlot != NetProtocol.KillerNone)
                {
                    killable.NetKill(NetPuppets.KillerAgent(killerSlot, e.Comp.Position), isComboGenerator: true);
                    if (!e.Comp.IsDead)
                    {
                        bin.Remove((GameComponent)(object)e.Comp);
                    }
                }
                else
                {
                    // Non-killable replicable (EvilBullet swept by a blast, Powerup pickup,
                    // Asteroid...): settle it directly, pay any claimant its points.
                    if (e.Comp is Powerup p)
                    {
                        p.taken = true;
                        // Lives are host-authoritative (EvScoreSync sends them verbatim), so a
                        // client-collected extra life must be applied HERE or the next sync
                        // silently reverts it. Other powerup effects are per-ship on the collector.
                        if (p.type == Powerup.PowerupType.OneUp && killerSlot != NetProtocol.KillerNone)
                        {
                            score.AddLife();
                        }
                    }
                    else if (killerSlot != NetProtocol.KillerNone)
                    {
                        if (e.Comp.NetPointValue > 0f)
                        {
                            score.AddScore(e.Comp.NetPointValue, true, e.Comp.Position, killerSlot);
                        }
                        Explosion explosion = Explosion.NewExplosion(bin, game);
                        explosion.Setup(e.Comp.Position, 1.2f, 1f, 0f, 0f);
                        bin.Add((GameComponent)(object)explosion);
                    }
                    bin.Remove((GameComponent)(object)e.Comp);
                }
                metrics.ClaimsHonored++;
                if (DebugFlags.NetLog)
                {
                    Console.WriteLine("[net] claim honored (live kill) id=" + netId + " slot=" + killerSlot);
                }
                return;
            }
            // Already dead here: still pay the claimant, once.
            if (killerSlot != NetProtocol.KillerNone && recentDeaths.TryGetValue(netId, out DeathRecord rec))
            {
                if (killerSlot < 8 && (rec.PaidMask & (1 << killerSlot)) == 0)
                {
                    rec.PaidMask |= (byte)(1 << killerSlot);
                    recentDeaths[netId] = rec;
                    if (rec.Points > 0)
                    {
                        score.AddScore(rec.Points, true, rec.Pos, killerSlot);
                    }
                    if (rec.OneUp)
                    {
                        score.AddLife(); // overlapping collectors inside the RTT window each add one
                    }
                    metrics.ClaimsPaidDead++;
                    if (DebugFlags.NetLog)
                    {
                        Console.WriteLine("[net] claim honored (already dead, paid) id=" + netId + " slot=" + killerSlot);
                    }
                }
            }
        }

        // ---- remote-ship puppet lifecycle ---------------------------------------------------

        private static void ManagePuppet()
        {
            // Adopt / release: the GameScene can spawn (SpawnAllPlayers after a reset) or
            // purge (LoseLife/Terminate) the remote-slot ship without asking us.
            if (puppet != null && !oracle.GetShips().Contains(puppet))
            {
                puppet = null;
                hasLastPuppetPos = false;
            }
            if (puppet == null)
            {
                foreach (PlayerShip s in oracle.GetShips())
                {
                    if (s.Controller == ControlDevice.Remote)
                    {
                        puppet = s;
                        hasLastPuppetPos = false;
                        break;
                    }
                }
            }
            if (!PeerUp)
            {
                return;
            }
            if (remoteAlive && puppet == null && buffer.HasSamples && FindLocalShip() != null)
            {
                SpawnPuppet();
            }
            else if (!remoteAlive && puppet != null)
            {
                ExplodePuppet();
            }
        }

        private static void SpawnPuppet()
        {
            if (!oracle.DeviceIsPlaying(ControlDevice.Remote))
            {
                if (oracle.Players >= Oracle.MaxPlayers)
                {
                    return;
                }
                oracle.AddPlayer(ControlDevice.Remote);
            }
            int slot = oracle.GetPlayerIndex(ControlDevice.Remote);
            PlayerShip ship = bin.Recycle<PlayerShip>();
            if (ship == null)
            {
                ship = new PlayerShip(game);
            }
            ship.Setup(slot, buffer.Newest.Pos, startup: false, invulnerable: false, 4.712389f);
            bin.Add((GameComponent)(object)ship);
            puppet = ship;
            hasLastPuppetPos = false;
            renderMs = double.NaN;
            Console.WriteLine("[net] remote ship joined slot=" + slot);
        }

        // The remote peer said its ship died: mirror the death LOOK locally (explosions +
        // cue), but never through Die() -- that would fire PlayerShip_OnDeath and spawn a
        // local respawn summon for a ship we don't own. Its oracle slot stays reserved so
        // the peer's respawn reuses it.
        private static void ExplodePuppet()
        {
            PlayerShip p = puppet;
            puppet = null;
            hasLastPuppetPos = false;
            Vector2 at = p.GetPosition();
            Explosion explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 2f, 2f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 3.5f, 3.5f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            sound.PlayCue("expl2");
            bin.Remove((GameComponent)(object)p);
            Console.WriteLine("[net] remote ship died");
        }

        // The puppet's render clock runs on REAL time (never the turbo/slowmo/hit-stop-scaled
        // game time): the remote peer plays at its own real pace, so a local hit-stop or
        // slowmo must not drag the interpolation point away from the stream. Advances with
        // the real frame delta, softly servoing toward the ideal offset behind the newest
        // sample; a big error (tab was hidden, peer hiccuped) snaps instead of gliding.
        private static void AdvanceRenderClock()
        {
            if (!buffer.HasSamples)
            {
                renderMs = double.NaN;
                return;
            }
            double target = buffer.NewestMs - InterpDelayMs;
            if (double.IsNaN(renderMs))
            {
                renderMs = target;
                return;
            }
            renderMs += realDtMs;
            double err = target - renderMs;
            if (Math.Abs(err) > RenderClockSnapMs)
            {
                renderMs = target;
            }
            else
            {
                renderMs += err * 0.1;
            }
        }

        // Called from PlayerShip.Update for ControlDevice.Remote ships: position from the
        // interpolation buffer (~InterpDelayMs behind the newest sample), shots re-fired
        // locally from the replicated firing state through the real FireAt path.
        public static void DriveRemoteShip(PlayerShip ship, GameTime gameTime)
        {
            if (!Active)
            {
                return;
            }
            if (!ReferenceEquals(puppet, ship))
            {
                puppet = ship;
                hasLastPuppetPos = false;
            }
            if (!buffer.HasSamples || double.IsNaN(renderMs))
            {
                return; // hold the spawn pose until the first sample lands
            }
            Vector2 pos = buffer.Sample(renderMs, out bool extrapolated);
            if (extrapolated)
            {
                metrics.Extrapolations++;
            }
            else
            {
                metrics.InterpSamples++;
            }
            if (hasLastPuppetPos)
            {
                // A step no real ship could make over the same real time = a correction pop.
                float step = Vector2.Distance(lastPuppetPos, pos);
                float maxStep = ShipMaxSpeedPxPerMs * realDtMs * 2f + PopSlackPx;
                if (step > maxStep)
                {
                    metrics.CorrectionPops++;
                    if (step > metrics.MaxPopPx)
                    {
                        metrics.MaxPopPx = step;
                    }
                }
            }
            lastPuppetPos = pos;
            hasLastPuppetPos = true;
            metrics.BufferDepthMs = (float)(buffer.NewestMs - renderMs);
            ShipSample newest = buffer.Newest;
            ship.NetApplyRemoteState(pos, newest.Aim, newest.Firing, remoteShotsPerSec, remoteBulletLife);
        }
    }
}
