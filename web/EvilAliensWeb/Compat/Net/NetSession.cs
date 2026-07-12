using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Stage 11.1 co-op session orchestrator (design: plans/stage11-online-coop.md).
    // Distributed authority: each peer owns its OWN ship completely -- local input is read
    // untouched with zero added latency; the wire carries ship STATE (never inputs); the
    // other peer's ship is an interpolated puppet ~InterpDelayMs behind, whose shots spawn
    // locally from its replicated firing state.
    //
    // Lifecycle: Game1.Initialize calls Start() iff ?net=host/join was parsed (a plain boot
    // never constructs anything here -- Active stays false and Update() is a single branch);
    // Game1.UpdateInner ticks Update() once per game tick. All received messages are queued
    // by the transport's JS-driven callbacks and drained ON the game tick, so game state is
    // only ever mutated inside the normal update.
    //
    // 11.1 limits (by design, see the card): both peers run independent worlds (host world
    // authority = 11.3), score/lives/pause/checkpoint-reset are not synced (11.3/11.4), and
    // the roster is exactly two peers.
    public static class NetSession
    {
        public const byte ProtocolVersion = 1;
        public const float InterpDelayMs = 100f;

        private const long StreamIntervalMs = 33;    // ~30 Hz
        private const long HelloIntervalMs = 1000;
        private const long PeerTimeoutMs = 3000;
        private const long MetricsIntervalMs = 5000;
        private const float FiringHoldMs = 150f;     // "still firing" window after the last FireAt intent
        private const float RenderClockSnapMs = 250f;
        // Pop detection: a rendered step larger than any plausible ship motion over the same
        // real time (PlayerShip.MaxSpeed is 0.33 px/ms; x2 margin + slack for frame jitter).
        private const float ShipMaxSpeedPxPerMs = 0.33f;
        private const float PopSlackPx = 3f;

        public static bool Active { get; private set; }
        public static bool PeerUp { get; private set; }

        private static bool isHost;
        private static Game game;
        private static Oracle oracle;
        private static ComponentBin bin;
        private static SoundManager sound;
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
        private static Vector2 lastTxPos = new Vector2(400f, 300f);
        private static float lastTxAim = 4.712389f;

        // rx / puppet
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

        // reliable-event bookkeeping (client-side ordering assertions)
        private static readonly HashSet<ushort> rxLiveIds = new HashSet<ushort>();
        private static int lastRxEventSeq = -1;

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
            isHost = DebugFlags.NetRole == NetRole.Host;
            transport = new BroadcastChannelTransport();
            transport.OnData += (data, reliable, from) => rxQueue.Enqueue((data, reliable));
            transport.OnPeerBye += from => PeerLost("bye");
            transport.Open(DebugFlags.NetRoom);
            if (isHost)
            {
                NetIdRegistry.Enable(g);
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
                else if (now - lastStreamTx >= StreamIntervalMs)
                {
                    SendShipState(now);
                }
            }
            ManagePuppet();
            if (now - lastMetricsAt >= MetricsIntervalMs)
            {
                lastMetricsAt = now;
                Console.WriteLine(metrics.Report(isHost, PeerUp, isHost ? NetIdRegistry.LiveCount : rxLiveIds.Count));
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

        // ---- host NetIdRegistry -> wire ---------------------------------------------------

        internal static void OnHostSpawn(ushort netId, string typeName)
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeSpawnEvent(txEventSeq++, netId, NetProtocol.TypeHash(typeName)));
            metrics.EventsTx++;
            if (DebugFlags.NetLog)
            {
                Console.WriteLine("[net] tx spawn id=" + netId + " type=" + typeName);
            }
        }

        internal static void OnHostDeath(ushort netId)
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeDeathEvent(txEventSeq++, netId));
            metrics.EventsTx++;
            if (DebugFlags.NetLog)
            {
                Console.WriteLine("[net] tx death id=" + netId);
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
                    // Reserved -- card 11.3 (host world authority).
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
                // Late joiner: replay the live NetId set so its ordering bookkeeping starts
                // from the truth instead of a death-before-spawn storm.
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
            rxLiveIds.Clear();
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
                if (data.Length < 10)
                {
                    return;
                }
                ushort id = NetProtocol.ReadU16(data, 4);
                if (!rxLiveIds.Add(id))
                {
                    metrics.DupSpawns++;
                }
                if (DebugFlags.NetLog)
                {
                    Console.WriteLine("[net] rx spawn id=" + id + " typeHash=" + NetProtocol.ReadU32(data, 6).ToString("x8"));
                }
                break;
            }
            case NetProtocol.EvDeath:
            {
                if (data.Length < 6)
                {
                    return;
                }
                ushort id = NetProtocol.ReadU16(data, 4);
                if (!rxLiveIds.Remove(id))
                {
                    metrics.OrderViolations++;
                }
                if (DebugFlags.NetLog)
                {
                    Console.WriteLine("[net] rx death id=" + id);
                }
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

        // ---- remote puppet lifecycle -------------------------------------------------------

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
