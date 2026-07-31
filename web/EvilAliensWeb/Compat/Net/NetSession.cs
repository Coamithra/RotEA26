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
    // Card 11.3 adds the SHARED STATE MACHINE: level-script beats (messages, unlock
    // banners, background ops, music switches, checkpoints) replicate as reliable events
    // hooked at their side-effect primitives on the host; death/checkpoint reset, victory
    // and pause replicate as GameScene state transitions (EvReset mirrors the host's
    // LoseLife branch, EvPause freezes the peer's world without a menu); the TeamChallenge
    // tether becomes a local soft pull with an or-of-either break event.
    //
    // Card 11.5 adds the drop grace (a stall banner + a deferred verdict instead of an
    // instant match end) and the single EndMatchPeerGone path behind every way a peer can
    // go. Remaining gaps (by design): TURN relay for restrictive NATs, which is gated on a
    // real-world connect-failure rate; the roster is exactly two peers.
    // Card 11.4 adds the REAL TRANSPORT + lobby flow: WebRtcTransport behind the same
    // interface (menu lobby via NetLobby, or ?net=...&rtc for the URL dev rig), a v4
    // handshake carrying a build hash (peers must run the identical published binary --
    // stale-cached clients are REJECTED with an "update required" notice, not desynced)
    // and a DebugFlags.Active bit (menu sessions refuse gameplay-hijacking flags), the
    // menu-lobby launch flow (EvLaunch mirrors the host's level+difficulty pick, EvReady
    // triggers a live-world replay once the client's scene is up), and match-end
    // semantics for menu sessions: any player leaving (EvLeave / peer loss) ends the
    // match for both -- Stop() tears the session down and the menus surface a notice.
    public static partial class NetSession
    {
        // v6 (card 0b8a300b) appends the peer-identity token to the handshake -- a WIRE LAYOUT
        // change (HelloBytes 13 -> 21), so the version byte must move with it or two builds
        // would both claim v5 and mis-decode each other's hellos.
        // v7 (card b0ab09ec): EvDeath carries the host's per-slot AWARDED score instead of the
        // entity's base point value, so both peers tally the identical number. It landed
        // alongside v6 rather than after it -- two independent wire changes cannot share a
        // version number for exactly the reason above, so the merge takes the next one.
        // v8 (card c0229c57): the handshake gains a blockedSlots mask (HelloBytes 21 -> 22) so
        // the host grants the joiner a seat that is free on BOTH rosters. Another WIRE LAYOUT
        // change, so the version moves with it for the same reason v6 did.
        // v9 (card 1a3ad45a): MsgHudState -- each peer streams the combo counter, active powerup,
        // bar progress and per-type levels for the slots it OWNS, and stops simulating them for
        // the slots it does not (ScoreVisualiser.SustainCombo). Authored against v8 and merged
        // after c0229c57 had taken it; two independent wire changes cannot share a version, so
        // this took the next one -- the same resolution the v6/v7 note above records.
        // v10 (card 9a3175d0): EvCosmeticSwarm -- a purely decorative swarm replicates as one
        // "effect on/off" beat carrying the spawner's rate, and its entities stop being
        // replicated individually. A v9 peer would ignore the beat (unknown event type) AND
        // still expect the per-entity spawns, so it would see empty scenery: a real
        // incompatibility, hence the version move even though no existing layout changed.
        public const byte ProtocolVersion = 10;
        public const float InterpDelayMs = 100f;

        private const long StreamIntervalMs = 33;    // ~30 Hz ship stream
        // Per-slot HUD state changes far slower than a ship pose (a combo tick, a bar creeping up),
        // and it is a readout rather than something the sim reads back, so a third of the ship
        // rate is plenty and keeps the added stream traffic under ~400 B/s.
        private const long HudIntervalMs = 100;      // ~10 Hz per-slot HUD state
        private const long SnapshotIntervalMs = 60;  // ~16.7 Hz world snapshot (host)
        private const long ScoreSyncIntervalMs = 1000;
        private const long HelloIntervalMs = 1000;
        private const long PeerTimeoutMs = 3000;
        // Card 11.5 grace: stream silence past PeerStallMs raises the "waiting for other
        // player" banner, but the match is not called until PeerTimeoutMs + PeerGraceMs of
        // continuous silence. Ending a run on the first 3s hiccup was the complaint; the
        // stream is ~30 Hz, so PeerStallMs is already ~36 missed packets -- long enough that
        // ordinary jitter never flashes the banner, short enough that the player learns why
        // their co-op partner stopped moving before the match ends under them.
        private const long PeerStallMs = 1200;
        private const long PeerGraceMs = 5000;
        // Grace between refusing a pairing (SendRejectOnce) and tearing the transport down.
        // A reject sends a reliable MsgReject then Stop()s -- but Stop()->transport.Close()->
        // pc.close() is ABORTIVE on WebRTC and discards a still-buffered reliable frame, so
        // the peer would see only a channel close ("other player disconnected") instead of
        // the real reason ("update required"). Holding the session open this long keeps the
        // SCTP association alive so the reliable reject (and our own hello, which drives the
        // peer's symmetric detection) actually reach it. One hello interval + RTT headroom;
        // imperceptible in the reject UX (the peer is being told to reload either way).
        private const long RejectGraceMs = 1000;
        // Card 0b8a300b: how long the remote peer must hold its pause before the HOST is
        // offered the kick menu. A remote pause freezes our whole world with no way out (see
        // NetKickMenu), so the host needs an escape -- but most pauses are innocent and short,
        // and a menu inviting you to kick your co-op partner 200ms into their doorbell break
        // would be worse than the wait. Past this the pause stops looking accidental.
        // Declining ("Keep Waiting") re-arms it, so waiting once never forfeits the option.
        private const long KickOfferDelayMs = 4000;
        // While either side holds a PAUSE the stream-heartbeat is unreliable: the paused
        // tab is usually backgrounded AND the pause muffle ducks its audio, which revokes
        // Chrome's audio exemption from intensive timer throttling -- its ticks (and so its
        // stream) arrive in ~1/min bursts. A pause is an explicit "here but frozen" state,
        // so only a long backstop applies (recovers a peer that silently died mid-pause);
        // a closed tab still departs instantly via the pagehide 'bye'.
        private const long PausedPeerTimeoutMs = 120000;
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

        // Card 9a3175d0: IsReplicableInstance, not IsReplicable -- a decorative instance is the
        // client's OWN to spawn (its spawner is what got replicated), so the bin must let it
        // through or the joiner's scenery vanishes entirely.
        public static bool SuppressWorldSpawn(GameComponent component)
        {
            return IsClient && !NetPuppets.Constructing && NetTypeRegistry.IsReplicableInstance(component);
        }

        // ComponentBin.Pop must not thaw a frozen puppet back into a live AI.
        public static bool IsFrozenPuppet(GameComponent component)
        {
            return IsClient && NetPuppets.IsPuppet(component);
        }

        internal static Game SessionGame => game;

        // Peer stream quiet past PeerStallMs but not yet past the drop verdict -- the grace
        // window. Drives the "waiting for other player" banner and parks puppet dead-reckoning
        // (NetPuppets.Drive); never freezes the world.
        internal static bool PeerStalled => peerStalled;

        private static bool peerStalled;

        private static bool isHost;
        private static Game game;
        private static Oracle oracle;
        private static ComponentBin bin;
        private static SoundManager sound;
        private static ScoreVisualiser score;
        private static INetTransport transport;
        private static NetImpairment impairment;

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
        private static long lastHudTx;
        private static int snapshotCursor;
        private static readonly byte[] snapshotScratch = new byte[SnapshotScratchBytes];
        private static readonly byte[] extraScratch = new byte[ExtraScratchBytes];
        // Per-slot HUD state gather buffers (card 1a3ad45a) -- reused every send/receive, so only
        // the encoder's own packet allocates (as in every other encoder here).
        private static readonly byte[] hudTxSlots = new byte[NetProtocol.MaxSlots];
        private static readonly int[] hudTxCombos = new int[NetProtocol.MaxSlots];
        private static readonly byte[] hudTxTypes = new byte[NetProtocol.MaxSlots];
        private static readonly float[] hudTxProgress = new float[NetProtocol.MaxSlots];
        private static readonly int[][] hudTxLevels = CreateHudLevelScratch();
        private static readonly int[] hudRxLevels = new int[NetProtocol.HudLevelCount];

        private static int[][] CreateHudLevelScratch()
        {
            int[][] rows = new int[NetProtocol.MaxSlots][];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new int[NetProtocol.HudLevelCount];
            }
            return rows;
        }

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

        // ---- roster slots (card 4d904410) -------------------------------------------------
        //
        // The oracle slot IS the wire slot on both peers, and the HOST allocates every one of
        // them -- so there is no host-relative translation anywhere (the old TranslateSlot 0<->1
        // mirror and the ApplyJoinHues compensating swap are both gone; per-slot hues now agree
        // by construction). The host's own primary is always slot 0: it seats itself first in its
        // own game, and couch players only ever arrive later.
        internal const byte HostPrimarySlot = 0;

        // OUR primary ship's slot. Host: always 0. Client: granted by the host in MsgWelcome
        // (SlotNone until the handshake completes).
        private static byte localPrimarySlot = HostPrimarySlot;

        // The PEER's primary slot -- the seat its MsgShipState stream drives. Host: the slot it
        // granted the client. Client: the host's, i.e. 0.
        private static byte peerPrimarySlot = NetProtocol.SlotNone;

        // Client only: a couch join is waiting on the host's EvSlotGrant. Holds the device that
        // pressed Start and whether the scene wanted the ship spawned immediately, so the grant
        // can finish the join the local path would have done synchronously offline.
        private static bool joinRequestPending;
        private static ControlDevice pendingJoinDevice;
        private static bool pendingJoinSpawn;

        // Client only, ?netdropgrant: whether this session has already spent its one dropped
        // grant. Per-SESSION, not per-page -- ResetPerSessionState clears it, so the second
        // session in a page drops its first grant too instead of silently behaving differently
        // from the first. See ShouldDropGrant.
        private static bool dropGrantUsed;

        // Host only: slots granted to the peer that it has not streamed into yet -> deadline.
        // A grant the client silently fails to take would otherwise hold the seat forever.
        private const long GrantClaimTimeoutMs = 10000;
        private static readonly Dictionary<byte, long> grantsAwaitingStream = new Dictionary<byte, long>();
        private static readonly List<byte> grantScratchSlots = new List<byte>(4);

        // Kill attribution: who landed the killing blow, recorded just before the per-type
        // death cascades into removal (KillableAlien.HitBy / claim handling), consumed at
        // the removal seam on either side.
        private static readonly Dictionary<INetEntity, byte> killNotes = new Dictionary<INetEntity, byte>();
        private static readonly Queue<INetEntity> killNoteOrder = new Queue<INetEntity>();

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

        // Card 11.4 session-flow state.
        private static bool menuSession;      // started from the menu lobby (match-end semantics apply)
        // Card 2001fbd8: a HOST session that spun up mid-level when a stranger joined our
        // LISTED single-player game (join-in-progress). Like a menu session it is exactly two
        // peers with a clean end, but its peer-loss is different: the joiner leaving reverts
        // the host to plain single-player (NetListing re-lists) rather than force-exiting the
        // host's own level. Always host-side (the joiner is a normal menu-session client).
        private static bool listedSession;
        private static ulong localBuildHash;
        // Card 0b8a300b. Our own identity token (hashed) and the peer's, as exchanged in the
        // v6 hello. SELF-REPORTED -- see NetProtocol.HelloBytes; good enough to stop a kicked
        // griefer walking straight back in, and nothing more.
        private static ulong localPeerId;
        private static ulong peerPeerId;
        // Peers this HOST kicked with "and Block", for the rest of its current level.
        // Deliberately static-and-not-cleared-by-Stop(): a kick ENDS the session, the host
        // re-lists seconds later, and the whole point is that the block outlives that. Emptied
        // by GameScene.Terminate via ClearBlockedPeers() -- i.e. scoped to one level run.
        private static readonly HashSet<ulong> blockedPeers = new HashSet<ulong>();
        // Reject-in-progress (see RejectGraceMs): NowMs deadline at which we Stop() after
        // having queued a reliable MsgReject or EvKick, and the reason/notice to use then.
        // 0 = not winding down. Keeping the transport alive until the deadline lets the frame
        // actually egress before the abortive close discards it.
        private static long pendingStopAt;
        private static string pendingStopNotice;
        private static string pendingStopReason = "pairing rejected";
        // Remote-pause kick offer (card 0b8a300b): when the current remote pause started (or
        // was last re-armed by "Keep Waiting"), and whether the menu is currently offered.
        private static long remotePauseAt;
        private static bool kickOfferShown;
        private static bool sceneWasUp;       // GameScene edge detection (EvReady / match end)
        private static bool pendingLaunchHas;
        // Validated at the decode boundary, so these hold real enum members and never a raw
        // wire byte -- see the wire-enum contract in NetProtocol.
        private static Levels pendingLaunchLevel;
        private static Settings.DifficultyLevel pendingLaunchDifficulty;
        private static bool peerByeQueued;

        // A short user-facing notice for the menus ("PLAYER LEFT -- MATCH ENDED", "UPDATE
        // REQUIRED..."). Set on session-ending events, consumed by MenuScene.
        public static string MenuNotice { get; private set; }

        // Every cadence in this file reads through here, so injecting the host's clock (card
        // 25ad0659 step 2a) makes the whole session drivable on a virtual clock in one line.
        private static long NowMs => NetHost.Current.NowMs;

        // URL boot path (?net=host/join [&rtc]) -- called from Game1.Initialize; a plain
        // boot (NetRole.None) constructs nothing. Deliberately still reads DebugFlags direct:
        // this is the COMPOSITION ROOT, deciding whether a session exists at all and which
        // transport it gets, and no injected host can answer that -- the host is chosen after.
        public static void Start(Game g)
        {
            if (Active || DebugFlags.NetRole == NetRole.None)
            {
                return;
            }
            INetTransport t = DebugFlags.NetRtc
                ? (INetTransport)new WebRtcTransport(attachOnly: false)
                : new BroadcastChannelTransport();
            StartWith(g, DebugFlags.NetRole == NetRole.Host, t, DebugFlags.NetRoom, asMenuSession: false, asListedSession: false);
        }

        // Menu-lobby path (card 11.4) -- called by NetLobby once the DataChannels are up.
        public static void StartMenuSession(Game g, bool host, INetTransport t, string room)
        {
            if (Active)
            {
                return;
            }
            StartWith(g, host, t, room, asMenuSession: true, asListedSession: false);
        }

        // Join-in-progress path (card 2001fbd8) -- called by NetListing when a stranger pairs
        // with our LISTED single-player game. Always the HOST, mid-level: the running
        // GameScene is already up (NetActiveScene set), so PeerConnected sends the joiner an
        // EvLaunch into our level + replays the live world; the joiner is a normal menu-session
        // client that mirrors the launch.
        public static void StartListedSession(Game g, INetTransport t, string room)
        {
            if (Active)
            {
                return;
            }
            StartWith(g, host: true, t, room, asMenuSession: false, asListedSession: true);
        }

        // Scenario-harness path (card 25ad0659): start a REAL session over one endpoint of an
        // in-process NetWire so a scripted peer can drive the other end (NetResetSpawnTest).
        // Deliberately NEITHER a menu nor a listed session: `menuSession` makes HandleHello refuse
        // a peer while DebugFlags.Active is set, and a scenario that needs a live world boots with
        // ?level=, so a menu session would reject its own scripted pairing; `listedSession` would
        // make PeerConnected send an EvLaunch no scenario wants.
        internal static void StartForTest(Game g, bool host, INetTransport t, string room)
        {
            if (Active)
            {
                return;
            }
            StartWith(g, host, t, room, asMenuSession: false, asListedSession: false);
        }

        // The hash a scripted peer's hello must carry to be accepted. READ rather than recomputed:
        // restating HashBuildString(host.BuildHash) in a scenario would drift silently from
        // StartWith's own expression, and the only symptom would be a pairing the scenario
        // cannot explain. Still true with the host injected: a scenario that sets its own
        // BuildHash still reads the hash back from here rather than re-hashing it.
        // The live counters, for a scenario harness (card 25ad0659 step 4). The REFERENCE is
        // mutable -- NetMetrics is a bag of public fields -- so this is a read seam by convention,
        // not by construction: nothing outside this layer writes it, and a scenario must not
        // start. There is no reset either, which is the load-bearing part: a scenario asserts on
        // DELTAS across its own frames rather than zeroing a counter the [net] line is also
        // reporting. Same shape as step 1b's seams -- a scenario's whole production cost is a getter.
        internal static NetMetrics Metrics => metrics;

        internal static ulong LocalBuildHash => localBuildHash;

        // Is the remote peer's PRIMARY ship puppet currently adopted? The subject of
        // NetResetSpawnTest: SpawnPuppet must leave this false when its bin.TryAdd is refused,
        // because the retry gate IS `puppet == null` -- adopting a ship the bin diverted points it
        // at a ship the world does not have (card 74403f83; the window is one tick, see the note
        // in SpawnPuppet).
        internal static bool HasRemotePuppet => puppet != null;

        private static void StartWith(Game g, bool host, INetTransport t, string room, bool asMenuSession, bool asListedSession)
        {
            game = g;
            // The four services arrive through the host since step 2b -- ServiceHelperNetHost
            // holds these four lookups verbatim. Resolved ONCE here, exactly as before: the
            // fields below are what the 79 call sites read, so where they came from is the only
            // thing this card changes.
            oracle = NetHost.Current.Oracle;
            bin = NetHost.Current.ComponentBin;
            sound = NetHost.Current.SoundManager;
            score = NetHost.Current.Score;
            isHost = host;
            menuSession = asMenuSession;
            listedSession = asListedSession;
            // Both fingerprints arrive ALREADY resolved against ?netfakehash / ?netfakepeer --
            // the resolution expressions moved verbatim into ServiceHelperNetHost (step 2a), so
            // a scenario supplies two strings instead of reaching into JS interop.
            localBuildHash = NetProtocol.HashBuildString(NetHost.Current.BuildHash);
            string peerToken = NetHost.Current.PeerToken;
            // An EMPTY token must map to 0 ("no identity"), not to a hash: HashBuildString("")
            // returns the FNV-1a offset basis, which is a perfectly ordinary non-zero id -- so
            // every peer whose JS could not mint a token would share it, and blocking one would
            // block them all. 0 is the value ApplyKickBlock/IsPeerBlocked refuse to touch.
            localPeerId = string.IsNullOrEmpty(peerToken) ? 0UL : NetProtocol.HashBuildString(peerToken);
            // Impairment wraps whichever transport the caller picked -- BroadcastChannel dev
            // loopback or the real WebRTC one. It decorates INetTransport precisely so it does
            // not care which. Always in the chain inside a net session (a plain boot never gets
            // here, so the single-player invariant is untouched) because the knobs are live-
            // settable from eaNetSim; at 0/0 it forwards inline with no queue.
            impairment = new NetImpairment(t);
            transport = impairment;
            transport.OnData += (data, reliable, from) => rxQueue.Enqueue((data, reliable));
            // Queued, not applied inline: the bye fires from a JS callback, and the menu-
            // session PeerLost now tears down the whole match (world mutation belongs on
            // the game tick).
            transport.OnPeerBye += from => peerByeQueued = true;
            transport.Open(room);
            if (isHost)
            {
                NetIdRegistry.Enable(g);
            }
            else
            {
                NetPuppets.Enable(g);
            }
            Active = true;
            pendingStopAt = 0;
            pendingStopNotice = null;
            sceneWasUp = NetScene.Current != null;
            pendingLaunchHas = false;
            MenuNotice = null;
            sessionStartAt = NowMs;
            lastMetricsAt = sessionStartAt;
            Console.WriteLine("[net] session start role=" + (isHost ? "host" : "join")
                + " room=" + room + " protocol=v" + ProtocolVersion
                // Names all THREE impls: the two-way test read "WebRTC" for an InMemoryTransport,
                // i.e. the one line that says which wire a run is on lied about the headless one.
                + " transport=" + (t is BroadcastChannelTransport ? "BroadcastChannel"
                    : t is InMemoryTransport ? "InMemory" : "WebRTC")
                + (menuSession ? " (menu lobby)" : listedSession ? " (join-in-progress)" : ""));
        }

        // End the session entirely (card 11.4): match over, peer rejected, or lobby
        // cancel. Closes the transport and resets every piece of per-session state so a
        // fresh Start()/StartMenuSession() is clean. `notice` (optional) is surfaced to
        // the menus via MenuNotice.
        public static void Stop(string reason, string notice = null)
        {
            if (!Active)
            {
                return;
            }
            Console.WriteLine("[net] session stop (" + reason + ")");
            Active = false;
            PeerUp = false;
            transport.Close();
            transport = null;
            // Dropped alongside `transport` (it IS `transport`) so a restarted session can't
            // pump a closed wrapper -- Start() builds a fresh one around the new transport.
            impairment = null;
            if (isHost)
            {
                NetIdRegistry.Disable(game);
            }
            else
            {
                NetPuppets.Disable();
            }
            if (RemotePaused)
            {
                RemotePaused = false;
                NetScene.Current?.NetSetRemotePaused(false);
            }
            ClearPeerStalled(); // never leave the banner up over a session that no longer exists
            ResetPerSessionState();
            if (notice != null)
            {
                MenuNotice = notice;
            }
            if (menuSession)
            {
                menuSession = false;
                NetLobby.OnSessionEnded();
            }
        }

        // Every field a session owns, back to its pre-session value, so a fresh
        // Start()/StartMenuSession() is clean. Split out of Stop() so NetKickTest can execute
        // it directly: Stop() early-returns when no session is Active, which made the test's
        // "the block survives a teardown" leg vacuous -- it ran only when it could do nothing.
        internal static void ResetPerSessionState()
        {
            localPaused = false;
            rxQueue.Clear();
            buffer.Clear();
            renderMs = double.NaN;
            hasLastPuppetPos = false;
            haveRxSeq = false;
            lastRxEventSeq = -1;
            remoteAlive = false;
            puppet = null;
            ResetFriends();
            localPrimarySlot = HostPrimarySlot;
            peerPrimarySlot = NetProtocol.SlotNone;
            joinRequestPending = false;
            dropGrantUsed = false;
            grantsAwaitingStream.Clear();
            localJoinSimDone = 0;
            localJoinSimAt = 0;
            txSeq = 0;
            txEventSeq = 0;
            lastStreamTx = 0;
            lastSnapshotTx = 0;
            lastScoreSyncTx = 0;
            lastHudTx = 0;
            lastHelloTx = 0;
            lastUpdateAt = 0;
            killNotes.Clear();
            killNoteOrder.Clear();
            recentDeaths.Clear();
            recentDeathOrder.Clear();
            pendingLaunchHas = false;
            peerByeQueued = false;
            listedSession = false;
            pendingStopAt = 0;
            pendingStopNotice = null;
            pendingStopReason = "pairing rejected";
            peerPeerId = 0;
            remotePauseAt = 0;
            kickOfferShown = false;
            // NOT blockedPeers -- it must outlive the session it was populated in (that IS the
            // point: a kick stops the session, the host re-lists, the block still holds).
            // NetKickTest asserts exactly this; do not "tidy up" by clearing it here.
        }

        // ---- menu-flow accessors (card 11.4) --------------------------------------------

        public static string TakeMenuNotice()
        {
            string n = MenuNotice;
            MenuNotice = null;
            return n;
        }

        // Client side: the host picked a level in the lobby -- MenuScene polls this and
        // mirrors the launch.
        public static bool TakePendingLaunch(out Levels level, out Settings.DifficultyLevel difficulty)
        {
            level = pendingLaunchLevel;
            difficulty = pendingLaunchDifficulty;
            if (!pendingLaunchHas)
            {
                return false;
            }
            pendingLaunchHas = false;
            return true;
        }

        // Host side: called from the menu's difficulty pick just before the fade to game.
        public static void SendLaunch(Levels level, Settings.DifficultyLevel difficulty)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeLaunchEvent(txEventSeq++, (byte)level, (byte)difficulty));
            metrics.EventsTx++;
            Console.WriteLine("[net] tx launch level=" + level + " difficulty=" + difficulty);
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
            // Release anything the impairment wrapper is holding BEFORE draining, so a
            // delayed packet lands in rxQueue in time for this tick rather than the next.
            impairment.Pump(now);
            DrainRx();
            if (!Active)
            {
                return; // a drained event (EvLeave / reject) ended the session
            }
            if (pendingStopAt != 0)
            {
                // We refused the pairing, or kicked the peer (card 0b8a300b): keep pumping
                // (impairment.Pump + DrainRx above ran, and the transport is still open) so the
                // reliable MsgReject/EvKick actually reaches the peer, then wind our side down
                // once the grace elapses. If the peer's OWN reject drained above it already
                // Stop()ped us (Active would be false). A peer bye/close during the grace is
                // ignored on purpose -- our own notice still wins at the deadline.
                // NOTE for the kick path: the world was already unfrozen and reverted to
                // single-player synchronously in KickPeer, so this grace is a background
                // teardown, not a second of frozen screen.
                if (now >= pendingStopAt)
                {
                    Stop(pendingStopReason, pendingStopNotice);
                }
                return;
            }
            if (peerByeQueued)
            {
                peerByeQueued = false;
                PeerLost("bye");
                if (!Active)
                {
                    return;
                }
            }
            UpdateSceneEdges();
            if (!Active)
            {
                return; // the local match ended (menu session) -- Stop() ran
            }
            AdvanceRenderClock();
            // Keep saying hello until the SLOT exchange has settled too, not just until the peer
            // is up: whoever hears the other's hello first goes PeerUp immediately, and a client
            // that fell silent there would never be answered with its slot grant.
            // `peerPrimarySlot` unset means "not settled" for both roles (the host sets it when
            // it reserves the joiner's seat, the client when it adopts its own).
            if (!PeerUp || peerPrimarySlot == NetProtocol.SlotNone)
            {
                if (now - lastHelloTx >= HelloIntervalMs)
                {
                    lastHelloTx = now;
                    transport.SendReliable(NetProtocol.EncodeHello(ProtocolVersion, isHost, localBuildHash, LocalHelloFlags(),
                        isHost ? peerPrimarySlot : NetProtocol.SlotNone, localPeerId, LocalBlockedSlots()));
                }
            }
            if (PeerUp)
            {
                long quiet = now - lastRxStreamAt;
                bool paused = RemotePaused || localPaused;
                if (quiet > (paused ? PausedPeerTimeoutMs : PeerTimeoutMs + PeerGraceMs))
                {
                    PeerLost("timeout");
                }
                else
                {
                    // Grace window (card 11.5): past PeerStallMs the link is visibly unwell,
                    // but the verdict is deferred by PeerGraceMs and we keep streaming
                    // throughout, so a wifi hiccup or a backgrounded tab's burst-send recovers
                    // instead of ending the run. A PAUSED peer is an explicit "here but
                    // frozen" state whose own overlay already says so -- no stall banner on
                    // top of it, and its much wider backstop still applies.
                    SetPeerStalled(!paused && quiet > PeerStallMs, recovered: quiet <= PeerStallMs);
                    if (now - lastStreamTx >= StreamIntervalMs)
                    {
                        SendShipState(now);
                        // Couch players + host AI friends ride the same cadence, both directions.
                        SendFriendStates(now);
                    }
                    if (now - lastHudTx >= HudIntervalMs)
                    {
                        SendHudState(now);
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
            // PeerUp is re-tested deliberately: the block above can call PeerLost("timeout"), and
            // roster bookkeeping must not run against a session that just ended.
            if (PeerUp)
            {
                TickLocalJoinSim(now);
                if (isHost)
                {
                    ExpireUnclaimedGrants(now);
                    TickKickOffer(now);
                }
            }
            ManagePuppet();
            TickFriends(); // spawn/interpolate/expire the peer's couch + AI-friend puppets
            if (now - lastMetricsAt >= MetricsIntervalMs)
            {
                lastMetricsAt = now;
                metrics.ImpDropped = impairment.Dropped;
                metrics.ImpHeld = impairment.HeldCount;
                metrics.ImpLagMs = impairment.LagMs;
                metrics.ImpLossPct = impairment.LossPct;
                metrics.ImpJitterMs = impairment.JitterMs;
                int liveCount = isHost ? NetIdRegistry.LiveCount : NetPuppets.LiveCount;
                Console.WriteLine(metrics.Report(isHost, PeerUp, liveCount, SnapshotTurnMs(liveCount),
                    FindLocalShip() != null, puppet != null, RosterReport()));
            }
        }

        private static byte LocalHelloFlags()
        {
            // ?netjip is a deliberate two-window JIP test bypass: a host booted with ?level=
            // has DebugFlags.Active, which a clean menu-session joiner would reject -- so a
            // ?netjip boot presents as clean. Every real listed host has no debug flags anyway
            // (NetListing's eligibility refuses them unless ?netjip is set).
            return (NetHost.Current.DebugActive && !NetHost.Current.NetJip) ? NetProtocol.HelloFlagDebugActive : (byte)0;
        }

        // GameScene lifecycle edges (card 11.4): the client announces its scene coming up
        // (EvReady -> the host replays the live world into it, covering a client that
        // finished its level warm after the host started spawning); a scene going DOWN in
        // a menu session means the local match ended (quit, game over, victory credits) --
        // one match per lobby, so tell the peer and wind the session down.
        private static void UpdateSceneEdges()
        {
            bool sceneUp = NetScene.Current != null;
            if (sceneUp == sceneWasUp)
            {
                return;
            }
            sceneWasUp = sceneUp;
            if (sceneUp)
            {
                if (!isHost && PeerUp)
                {
                    transport.SendReliable(NetProtocol.EncodeEmptyEvent(txEventSeq++, NetProtocol.EvReady));
                    metrics.EventsTx++;
                }
            }
            else if (menuSession || listedSession)
            {
                // Our own level ended / we quit: tell the peer and end the match. For a JIP
                // host this fires when its level finishes; the joiner (menu session) then
                // exits to its menu with a notice.
                if (PeerUp)
                {
                    transport.SendReliable(NetProtocol.EncodeEmptyEvent(txEventSeq++, NetProtocol.EvLeave));
                }
                Stop("match ended");
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

        // Card 1a3ad45a. Stream the per-slot HUD state for every slot we OWN -- combo counter,
        // active powerup, bar progress, per-type levels. Keyed off the ROSTER, not off live ships:
        // a slot keeps its combo and its powerup levels across a death and respawn, and its panel
        // is drawn throughout, so going quiet while the ship is down would freeze the peer's
        // readout at whatever it held when the player died.
        private static void SendHudState(long now)
        {
            lastHudTx = now;
            if (score == null)
            {
                return;
            }
            int count = 0;
            for (int slot = 0; slot < NetProtocol.MaxSlots && slot < ScoreVisualiser.SlotCount; slot++)
            {
                if (!OwnsSlot(slot))
                {
                    continue;
                }
                score.NetReadHudState(slot, hudTxLevels[count], out int combo, out Powerup.PowerupType? activeType, out float progress);
                hudTxSlots[count] = (byte)slot;
                hudTxCombos[count] = combo;
                // No active powerup goes on the wire as the HudPowerupNone sentinel; the
                // receiver folds it back into the same null (NetProtocol.TryDecodeHudState).
                hudTxTypes[count] = activeType.HasValue ? (byte)activeType.Value : NetProtocol.HudPowerupNone;
                hudTxProgress[count] = progress;
                count++;
            }
            if (count == 0)
            {
                return;
            }
            transport.SendStream(NetProtocol.EncodeHudState(hudTxSlots, hudTxCombos, hudTxTypes, hudTxProgress, hudTxLevels, count));
            // Counted in ENTRIES, matching HudRx -- a peer with a couch partner puts two slots in
            // one packet, so counting packets here would make the two sides incomparable.
            metrics.HudTx += count;
        }

        private static void HandleHudState(byte[] data)
        {
            if (score == null || !NetProtocol.TryDecodeHudCount(data, out int count))
            {
                return;
            }
            for (int i = 0; i < count; i++)
            {
                if (!NetProtocol.TryDecodeHudState(data, i, hudRxLevels, out byte slot, out int combo, out Powerup.PowerupType? activeType, out float progress))
                {
                    continue;
                }
                // slot is a raw wire byte, so bound it against the SCORE PANELS (4) -- the same
                // rule and reasoning as ApplyRemotePowerup. A peer claiming a slot we own is
                // ignored rather than trusted: our own simulation is authoritative for it, and
                // adopting it would let a confused or hostile peer rewrite our HUD.
                if (slot >= ScoreVisualiser.SlotCount || OwnsSlot(slot))
                {
                    continue;
                }
                score.NetSetHudState(slot, combo, activeType, progress, hudRxLevels);
                metrics.HudRx++;
            }
        }

        // A ship THIS peer simulates: its owner reads real input (or runs the local AI) and
        // decides its own motion, hits and pickups. The inverse is a network-driven puppet.
        // With ?aiplayer the controller stays Keyboard/pad and only the Update branch is
        // forced to AI, so a forced-AI local ship is still correctly "ours".
        private static bool IsLocallyOwned(PlayerShip s)
        {
            return s.Controller != ControlDevice.Remote && s.Controller != ControlDevice.RemoteFriend;
        }

        // The same question asked of a ROSTER SLOT rather than a live ship (card 1a3ad45a):
        // does this peer simulate what happens in that seat? Asked of the seat, not the ship,
        // because a slot's combo and powerup levels outlive its ship -- they persist across a
        // death and respawn, and the gate must not flip while the player is waiting to come back.
        //
        // OFFLINE THIS IS TRUE FOR EVERY SLOT, which is what keeps single-player and local co-op
        // byte-identical: with no session there is nobody else to own anything.
        public static bool OwnsSlot(int slot)
        {
            bool seated = Active && oracle != null && oracle.IsSeated(slot);
            return OwnsSlotCore(Active, seated ? oracle.Controller(slot) : null);
        }

        // The decision itself, with the live roster lookup lifted out so eaNetCombo.test can
        // table-drive every case. Offline the predicate cannot discriminate at all (correctly --
        // there is nobody else to own anything), so a test that could only reach it through the
        // live Oracle would be structurally unable to cover Remote/RemoteFriend/unseated.
        // `seatedDevice` is null for an unseated slot.
        internal static bool OwnsSlotCore(bool sessionActive, ControlDevice? seatedDevice)
        {
            if (!sessionActive)
            {
                return true;
            }
            return seatedDevice.HasValue
                && seatedDevice.Value != ControlDevice.Remote
                && seatedDevice.Value != ControlDevice.RemoteFriend;
        }

        // The ship carried by the primary MsgShipState stream: the one in our granted primary
        // slot. Every OTHER locally-owned ship (couch players, AI friends) rides the slot-tagged
        // MsgFriendState stream instead.
        private static PlayerShip FindLocalShip()
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (IsLocallyOwned(s) && s.Owner == localPrimarySlot)
                {
                    return s;
                }
            }
            return null;
        }

        // Called from PlayerShip.doBlast (bombs are discrete -> the reliable event lane, not the
        // stream). Every locally-owned ship replicates its blasts, slot-tagged so the peer
        // detonates the right puppet -- a couch player's bomb used to land on our primary.
        public static void OnLocalBlast(PlayerShip ship, Vector2 pos, int level)
        {
            if (!Active || !PeerUp || ship == null || !IsLocallyOwned(ship))
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeBlastEvent(txEventSeq++, (byte)ship.Owner, pos, level));
            metrics.EventsTx++;
        }

        // ---- level-script beats + shared state machine (card 11.3) ---------------------------

        // True while the OTHER peer holds a pause. GameScene's resume paths consult this so
        // an overlapping local+remote pause only unfreezes when both are clear.
        public static bool RemotePaused { get; private set; }

        // True while OUR pause menu is up (set by OnLocalPause) -- widens the peer timeout
        // symmetrically: our own throttled ticking must not misread the peer as gone.
        private static bool localPaused;

        // Host-side script beats, called from the side-effect PRIMITIVES themselves
        // (MessageEvent / UnlockEvent / Background ops / SoundManager music / the checkpoint
        // callback) -- every level, and any future boss code using the same primitives,
        // replicates without per-level work. All no-ops unless an active host with a peer.

        public static void OnScriptMessage(string text, int speech, int msgType, float angle)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeMessageEvent(txEventSeq++, (byte)msgType, (byte)speech, angle, text));
            metrics.EventsTx++;
            metrics.BeatsTx++;
        }

        public static void OnScriptUnlock(int item, int unlockType, int speech, string text)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeUnlockEvent(txEventSeq++, (byte)item, (byte)unlockType, (byte)speech, text));
            metrics.EventsTx++;
            metrics.BeatsTx++;
        }

        public static void OnBackgroundOp(NetBackgroundOp op, Vector2 v)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeBackgroundEvent(txEventSeq++, (byte)op, v));
            metrics.EventsTx++;
            metrics.BeatsTx++;
        }

        // A decorative swarm turned on or off on the host (card 9a3175d0). Sent as an ordinary
        // reliable beat, exactly like the Background ops -- the joiner then runs its own spawner
        // for it and its own scenery, instead of taking one snapshot turn per fog spider.
        //
        // The LATCH that a join-in-progress peer catches up from is kept by GameScene, not here:
        // this early-returns while no peer is connected, which for a listed single-player game is
        // precisely the window whose beats have to be remembered (same reasoning as Background's
        // netLast* latches).
        public static void OnCosmeticSwarm(NetCosmeticKind kind, bool on, float rate)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeCosmeticSwarmEvent(txEventSeq++, (byte)kind, on, rate));
            metrics.EventsTx++;
            metrics.BeatsTx++;
        }

        // song = -1 replicates a StopMusic. Unlike the other beat hooks (whose primitives
        // only scripts call), PlayMusic is also the MENU's -- gate on a live GameScene so
        // a host navigating menus mid-session can't retune the client. Deliberately fired
        // ABOVE the host's local mute check: a muted host still replicates script beats.
        public static void OnMusic(int song)
        {
            if (!IsHost || !PeerUp || NetScene.Current == null)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeByteEvent(txEventSeq++, NetProtocol.EvMusic, song < 0 ? NetProtocol.MusicStop : (byte)song));
            metrics.EventsTx++;
            metrics.BeatsTx++;
        }

        public static void OnCheckpoint()
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeEmptyEvent(txEventSeq++, NetProtocol.EvCheckpoint));
            metrics.EventsTx++;
            metrics.BeatsTx++;
        }

        // The branch the host's LoseLife took, broadcast so the client mirrors the same
        // state transition instead of deciding from its own (suppressed) death logic.
        public const byte ResetModeRespawn = 0;  // DirectRespawn: in-place respawn
        public const byte ResetModeReset = 1;    // full checkpoint reset (purge + replay)
        public const byte ResetModeGameOver = 2; // lives exhausted

        public static void OnHostReset(byte mode)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeByteEvent(txEventSeq++, NetProtocol.EvReset, mode));
            metrics.EventsTx++;
            metrics.Resets++;
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx reset mode=" + mode);
            }
        }

        public static void OnHostVictory()
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeEmptyEvent(txEventSeq++, NetProtocol.EvVictory));
            metrics.EventsTx++;
            metrics.Victories++;
        }

        // Either peer: local pause menu pushed / every resume path. The receiving side
        // freezes its world with a hint overlay (no interactive menu -- you can't navigate
        // the peer's menu for them).
        public static void OnLocalPause(bool on)
        {
            localPaused = on && Active;
            if (!Active || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeByteEvent(txEventSeq++, NetProtocol.EvPause, (byte)(on ? 1 : 0)));
            metrics.EventsTx++;
            if (on)
            {
                metrics.Pauses++;
            }
        }

        // Either peer: the TeamChallenge tether broke on this screen (enemy hit / endpoint
        // died). Or-of-either-peer, idempotent -- the receiver breaks silently.
        public static void OnTetherBreak()
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeEmptyEvent(txEventSeq++, NetProtocol.EvTetherBreak));
            metrics.EventsTx++;
            metrics.TetherBreaks++;
        }

        // ---- kill attribution notes ---------------------------------------------------------

        // Recorded by KillableAlien.HitBy at the killing blow (both sides run it: the host
        // through its real sim, the client on its frozen puppets via local hit-testing).
        public static void NoteKill(AlienDrawableGameComponent comp, ICollidable killer)
        {
            if (!Active || !NetTypeRegistry.IsReplicableInstance((GameComponent)(object)comp))
            {
                return;
            }
            int slot = killer is IAlienKiller k ? k.Player() : -1;
            NoteKillSlot(comp, slot >= 0 && slot < 8 ? (byte)slot : NetProtocol.KillerNone);
        }

        internal static void NoteKillSlot(INetEntity comp, byte slot)
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

        // The other peer collected a powerup: drive THEIR HUD panel here. The local pickup
        // path (PlayerShip.CollidesWith) is the only SetPowerup caller and it is gated to the
        // local ship, so without this a remote pickup settles as a bare despawn and the
        // claimant's powerup icon never changes -- which reads as "the powerup always goes to
        // player 1".
        //
        // Card 1a3ad45a moved the LEVEL half of this elsewhere: that slot's progression is its
        // owner's alone now (ScoreVisualiser.SustainCombo is gated on OwnsSlot), and its real
        // level arrives over MsgHudState -- which also re-asserts the indicator, making the
        // SetPowerup below a redundant-but-immediate head start on the next ~10 Hz packet. The
        // sound cue is what only this path can do: it belongs to the pickup INSTANT.
        // Idempotent: the collector's own side already ran the local path and never reaches
        // a settle branch for its own pickup (its entity is gone before the echo arrives).
        internal static void ApplyRemotePowerup(INetPickup powerup, byte slot)
        {
            // Bound against the SCORE PANELS (4), not the 8 of the claim ledgers' PaidMask --
            // slot is a raw wire byte, so a corrupt or mismatched peer must not index past
            // ScoreVisualiser's fixed 4-slot list.
            if (slot == NetProtocol.KillerNone || slot >= ScoreVisualiser.SlotCount)
            {
                return;
            }
            score.SetPowerup(powerup.NetPickupType, slot);
            // Only the powerup INDICATOR is mirrored. The local path also does AddBomb for
            // PowerupType.Blast, deliberately not mirrored here: the spend side (NetDoBlast)
            // does not decrement bombs on the remote either, so replicating the increment
            // alone would make the other player's bomb icons pile up and never clear.
            sound.PlayCue("powerup"); // local co-op plays it for either collector too
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] remote powerup " + powerup.NetPickupType + " -> slot " + slot);
            }
        }

        internal static byte TakeKillNote(INetEntity comp)
        {
            if (killNotes.TryGetValue(comp, out byte slot))
            {
                killNotes.Remove(comp);
                return slot;
            }
            return NetProtocol.KillerNone;
        }

        // ---- host NetIdRegistry -> wire ---------------------------------------------------

        internal static void OnHostSpawn(NetIdRegistry.Entry e)
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            NetBaseState state = CaptureBaseState(e, NowMs);
            // Cast back for the descriptor: its extras surface is deliberately still on the
            // concrete type: 2c-iii measured moving it and DECLINED, so this cast is permanent
            // and is safe by construction -- see INetEntity's header for the argument.
            int extraLen = e.Descriptor.EncodeSpawnExtra((AlienDrawableGameComponent)e.Comp, extraScratch, 0);
            transport.SendReliable(NetProtocol.EncodeSpawnEvent(txEventSeq++, e.Id, e.TypeIdx, state, extraScratch, extraLen));
            metrics.EventsTx++;
            if (NetHost.Current.NetLog)
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
            // recentDeaths keeps the BASE value: a later claim from the other peer is a fresh
            // generous payout the host still credits with its own live combo (card 11.2), not
            // a replay of the award below.
            ushort points = (ushort)MathHelper.Clamp(e.Comp.NetPointValue, 0f, 65535f);
            RecordDeath(e.Id, pos, points, killer,
                e.Comp.NetPickup is INetPickup pu && pu.NetPickupType == Powerup.PowerupType.OneUp,
                e.ClaimPaidMask);
            if (!PeerUp)
            {
                return;
            }
            // e.Awards is what the per-type death path just credited, slot by slot (null when
            // nothing was awarded -- a despawn, a zero-point type, an already-awarded entity).
            transport.SendReliable(NetProtocol.EncodeDeathEvent(txEventSeq++, e.Id, killer, pos, e.Awards));
            metrics.EventsTx++;
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx death id=" + e.Id + " killer=" + killer
                    + " award=" + (e.Awards == null ? "-" : string.Join("/", e.Awards)));
            }
        }

        // ---- award attribution (card b0ab09ec) ------------------------------------------------
        //
        // Called by AlienDrawableGameComponent.AwardScore/AwardScoreToAll with the figure the
        // ScoreVisualiser ACTUALLY credited (already combo-modified). The host stashes it on the
        // NetId entry for the death broadcast a tick later (the ComponentBin defers removal, so a
        // single "last award" static could not bridge the gap); the client books it as a
        // provisional credit its own EvDeath will replace. Offline / no session: one bool test.
        internal static void NoteAward(INetEntity comp, int slot, float amount)
        {
            if (!Active || amount <= 0f || slot < 0 || slot >= NetProtocol.MaxSlots)
            {
                return;
            }
            if (isHost)
            {
                if (NetIdRegistry.TryGetByComp((GameComponent)comp, out NetIdRegistry.Entry e))
                {
                    (e.Awards ??= new float[NetProtocol.MaxSlots])[slot] += amount;
                }
                return;
            }
            NetPuppets.NoteLocalAward(comp, (byte)slot, amount);
        }

        // prepaidMask is the Entry's own ledger: the slots already paid for this death while the
        // removal was still queued (card 1bfcd705). Folded in HERE rather than merged into
        // whatever recentDeaths already holds for the id, so the write below stays a straight
        // assignment and a wrapped netId can never inherit a stale mask from a previous entity.
        private static void RecordDeath(ushort id, Vector2 pos, ushort points, byte killerSlot,
            bool oneUp, byte prepaidMask)
        {
            DeathRecord rec = new DeathRecord
            {
                Pos = pos,
                Points = points,
                PaidMask = (byte)(prepaidMask | (killerSlot < 8 ? 1 << killerSlot : 0)),
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

        // How long a single entity waits between world-snapshot corrections, given how many are
        // live: the cursor round-robins SnapshotMaxEntries per packet, so a big world stretches
        // every puppet's blind dead-reckoning window (card 48ab9b2f). This is the context
        // pupPops cannot be read without -- at 320 entities each puppet only hears from the host
        // every 1.2s, and anything not moving in a straight line then pops on a healthy link.
        //
        // It is the MEAN interval, deliberately, not the worst case. The cursor wraps
        // continuously rather than restarting each cycle, so an entity's gap alternates between
        // floor and ceil of liveCount/SnapshotMaxEntries packets and averages the ratio itself;
        // rounding UP to whole packets would report 120ms for a 17-entity world whose typical
        // blind window is 64ms -- nearly 2x, on exactly the small worlds this gets read for.
        // Floored at one packet interval, since a world that fits in a single packet is fully
        // refreshed every SnapshotIntervalMs and cannot do better than that.
        //
        // Reported as snapTurn= on both peers. NOTE the two peers derive it from different
        // counts -- the host from its authoritative NetIdRegistry, the join side from its own
        // puppet count, which lags during spawn bursts, id churn and JIP catch-up (i.e. exactly
        // when it is interesting). The host's line is the authoritative one.
        // Also the number the population sweep in tools/sim/net_puppet_drive_sim.py sweeps.
        internal static int SnapshotTurnMs(int liveCount)
        {
            if (liveCount <= 0)
            {
                return 0;
            }
            int mean = liveCount * (int)SnapshotIntervalMs / SnapshotMaxEntries;
            return Math.Max((int)SnapshotIntervalMs, mean);
        }

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
                int extraLen = e.Descriptor.EncodeStateExtra((AlienDrawableGameComponent)e.Comp, extraScratch, 0);
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
            INetEntity c = e.Comp;
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
                Rotation = c.NetRotation,
                CurFrame = c.NetCurFrame,
                Scale = c.NetScale,
                Hp = c.NetKillable is INetKillable k ? k.NetHitPoints : 0,
            };
        }

        // ---- host score sync ------------------------------------------------------------------

        private static readonly float[] scoreSyncScratch = new float[NetProtocol.MaxSlots];

        // Host: per-slot scores as they stood at the LAST top-of-tick flush (card b0ab09ec).
        //
        // Reading them live at send time is a one-tick lie. Game1.UpdateInner runs
        // TopOfTickFlush -> DetectCollisions -> NetSession.Update, so a kill in THIS tick's
        // collision phase has already credited the score, while the death it queued is not
        // flushed -- and its EvDeath therefore not sent -- until the NEXT tick's flush. A sync
        // sent in between would carry an award the client has not been told about, so the client
        // would count it once from the sync and again when the EvDeath finally arrived.
        // Snapshotting right after the flush closes that window: everything in the snapshot has
        // already had its EvDeath emitted (both ride the ordered reliable lane, flush first).
        private static readonly float[] scoreSyncSnapshot = new float[NetProtocol.MaxSlots];
        private static int scoreSyncSnapshotLives;

        internal static void SnapshotScoresForSync()
        {
            if (!Active || !isHost || score == null)
            {
                return;
            }
            for (int slot = 0; slot < NetProtocol.MaxSlots; slot++)
            {
                scoreSyncSnapshot[slot] = score.PointScore(slot);
            }
            scoreSyncSnapshotLives = score.Lives;
        }

        private static void SendScoreSync(long now)
        {
            lastScoreSyncTx = now;
            for (int slot = 0; slot < NetProtocol.MaxSlots; slot++)
            {
                scoreSyncScratch[slot] = scoreSyncSnapshot[slot];
            }
            transport.SendReliable(NetProtocol.EncodeScoreSync(txEventSeq++, scoreSyncSnapshotLives, scoreSyncScratch));
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
            // No translation: the host allocates every slot, so our oracle slot IS the wire slot.
            transport.SendReliable(NetProtocol.EncodeClaimEvent(txEventSeq++, netId, killerSlot));
            metrics.EventsTx++;
            metrics.ClaimsTx++;
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx claim id=" + netId + " killer=" + killerSlot);
            }
        }

        // ---- wire -> state ----------------------------------------------------------------

        // Client rx: decoded per-slot awards from the EvDeath currently being applied.
        private static readonly float[] deathAwardScratch = new float[NetProtocol.MaxSlots];

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
                case NetProtocol.MsgReject:
                    HandleReject(data);
                    break;
                case NetProtocol.MsgShipState:
                    HandleShipState(data);
                    break;
                case NetProtocol.MsgFriendState:
                    HandleFriendState(data);
                    break;
                case NetProtocol.MsgHudState:
                    HandleHudState(data);
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
            // A v3-or-older peer sends the short 3-byte hello -- caught by the length
            // check and rejected as a version mismatch below (data[1] still carries its
            // protocol version in every historical layout).
            if (data.Length < 3)
            {
                return;
            }
            byte ver = data[1];
            bool peerIsHost = data[2] != 0;
            if (ver != ProtocolVersion || !NetProtocol.TryDecodeHandshake(data, out _, out _, out ulong peerHash, out byte peerFlags, out byte grantedSlot, out ulong helloPeerId, out byte peerBlockedSlots))
            {
                Console.WriteLine("[net] peer protocol v" + ver + " != v" + ProtocolVersion);
                SendRejectOnce(NetProtocol.RejectVersion);
                return;
            }
            if (peerIsHost == isHost)
            {
                Console.WriteLine("[net] WARNING: peer has the SAME role (" + (isHost ? "host" : "join")
                    + ") -- one side should be " + (isHost ? "join" : "host"));
                return;
            }
            if (peerHash != localBuildHash)
            {
                // Different binaries would desync subtly (types, descriptors, sim code) --
                // refuse loudly instead. The usual cause is a stale-cached client.
                Console.WriteLine("[net] peer build hash mismatch -- rejecting (update required)");
                SendRejectOnce(NetProtocol.RejectBuild);
                return;
            }
            if (menuSession && ((peerFlags & NetProtocol.HelloFlagDebugActive) != 0 || NetHost.Current.DebugActive))
            {
                Console.WriteLine("[net] gameplay debug flags active in a menu session -- rejecting");
                SendRejectOnce(NetProtocol.RejectFlags);
                return;
            }
            peerPeerId = helloPeerId;
            // Card 0b8a300b: the block gate. Checked here because the hello is the ONE point
            // both rejoin routes converge on (the public browser and a typed room code), and
            // because it is BEFORE PeerConnected/slot reservation -- a blocked peer re-pairing
            // never reaches the world, so repeated attempts cost the host a re-pair and nothing
            // else. peerId 0 = the peer could not produce a token; never blockable, so a broken
            // localStorage can't get someone refused by accident.
            if (isHost && IsPeerBlocked(helloPeerId))
            {
                Console.WriteLine("[net] blocked peer tried to rejoin -- rejecting");
                SendRejectOnce(NetProtocol.RejectBanned);
                return;
            }
            // Slot allocation (card 4d904410). The host reserves the joiner's primary seat the
            // moment it knows a real peer is there -- BEFORE replying -- so its own couch joins
            // and AI friends can never be handed the same slot. The client adopts what it is
            // given. Both are idempotent: hellos repeat at 1 Hz until the pairing settles.
            if (isHost)
            {
                if (!ReserveRemotePrimarySlot(peerBlockedSlots))
                {
                    return; // refused (no seat free on both sides) -- SendRejectOnce owns the wind-down
                }
            }
            else if (grantedSlot != NetProtocol.SlotNone)
            {
                AdoptGrantedPrimarySlot(grantedSlot);
            }
            if (welcomeBack)
            {
                transport.SendReliable(NetProtocol.EncodeWelcome(ProtocolVersion, isHost, localBuildHash, LocalHelloFlags(),
                    isHost ? peerPrimarySlot : NetProtocol.SlotNone, localPeerId, LocalBlockedSlots()));
            }
            if (!PeerUp)
            {
                PeerConnected();
            }
        }

        // ---- roster slot allocation (card 4d904410) --------------------------------------

        // Which slots WE cannot seat our primary ship in, as a v8 handshake mask. Only the client
        // ever reports a constraint (the host allocates), and only while a scene is up: at the
        // menu -- where both the menu-lobby and the join-in-progress joiner hello from -- the
        // roster is leftover bookkeeping from the last level or attract demo that the launch
        // path's ResetPlayers() wipes before it seats us, so nothing there blocks anything.
        // Our OWN current seat is excluded: that is the seat we would move out of, not a blocker.
        private static byte LocalBlockedSlots()
        {
            if (isHost || NetScene.Current == null)
            {
                return 0;
            }
            return OccupiedMask(oracle, exclude: localPrimarySlot);
        }

        // A roster as a slot mask, optionally with one seat left out. `exclude` is how the client
        // omits its OWN seat: that is the seat it would move out of, not one that blocks a grant.
        // Pass -1 to mask every seated slot. Split out so eaSlotTest can drive it against a
        // scratch Oracle -- the "which seats are in the way" question is the input BOTH sides of
        // the negotiation run on, so a slip here is a silent bad grant.
        internal static byte OccupiedMask(Oracle roster, int exclude)
        {
            byte mask = 0;
            for (int i = 0; i < Oracle.MaxPlayers; i++)
            {
                if (i != exclude && roster.IsSeated(i))
                {
                    mask |= NetProtocol.SlotBit(i);
                }
            }
            return mask;
        }

        // HOST: pick the seat the joining peer's primary ship will occupy, and hold it
        // immediately as a Remote registration so nothing else can take it. Normally slot 1; a
        // listed game with a couch player already aboard hands out whatever is free instead --
        // which is exactly why wire slots can no longer be pinned to 0/1.
        //
        // `peerBlocked` (v8, card c0229c57) is the joiner's own occupied slots. Honouring it is
        // what makes the grant a NEGOTIATION rather than a guess: a seat free here but taken
        // there used to be granted anyway, and the joiner could not take it, could not say so,
        // and could not recover -- the two peers just disagreed about its slot forever.
        // Returns false when the pairing was REFUSED (no seat free on both sides) and the caller
        // must stop.
        private static bool ReserveRemotePrimarySlot(byte peerBlocked)
        {
            if (peerPrimarySlot != NetProtocol.SlotNone)
            {
                if (!NetProtocol.SlotInMask(peerBlocked, peerPrimarySlot))
                {
                    return true;
                }
                // The joiner has told us the seat we are holding does not work for it (its roster
                // changed between our grant and its hello). Release and re-pick against the new
                // mask rather than leaving it stranded -- it keeps helloing until its slot
                // settles, so this converges: we never re-offer a seat the mask still blocks.
                Console.WriteLine("[net] joiner cannot take granted slot=" + peerPrimarySlot + " -- re-allocating");
                if (puppet != null)
                {
                    // The peer streams as soon as it is PeerUp, which can be BEFORE the slot
                    // exchange settles -- so a puppet may already be flying in the seat we are
                    // about to give up. ManagePuppet only re-adopts a ship the scene spawned; it
                    // never re-stamps a live one's Owner, so leaving it would strand the remote
                    // player in the old slot while the wire, EvScoreSync and EvBlast all moved to
                    // the new one. Drop it and let the next stream rebuild it in the right seat.
                    ExplodePuppet();
                }
                oracle.RemovePlayerAt(peerPrimarySlot, ControlDevice.Remote);
                peerPrimarySlot = NetProtocol.SlotNone;
            }
            int slot = oracle.GetPlayerIndex(ControlDevice.Remote);
            if (slot >= 0 && NetProtocol.SlotInMask(peerBlocked, slot))
            {
                // A Remote registration the joiner cannot use (one that outlived a restarted
                // session, or was re-seated by SpawnAllPlayers). Free it before re-picking, or we
                // would hand out a second seat and leave this one squatting the roster.
                oracle.RemovePlayerAt(slot, ControlDevice.Remote);
                slot = -1;
            }
            if (slot < 0)
            {
                // Never slot 0: that is the host's own primary seat, which in the menu-lobby flow
                // is still EMPTY at pairing time (the level launches after the peers connect).
                slot = FirstMutuallyFreeSlot(HostOccupiedSlots(), peerBlocked);
                if (slot < 0)
                {
                    // No seat that works on BOTH sides. REFUSE -- do not just wait: the joiner
                    // would go PeerUp, never be granted a usable slot, keep slot 0 (our own
                    // player) and address every claim/blast at it. Our own game survives this:
                    // Stop() does not exit a level, so a listed host drops back to single-player
                    // and NetListing re-lists it.
                    Console.WriteLine("[net] no roster slot free for the joiner on both sides (peerBlocked="
                        + peerBlocked + ") -- rejecting");
                    SendRejectOnce(NetProtocol.RejectFull);
                    return false;
                }
                if (!oracle.AddPlayerAt(slot, ControlDevice.Remote))
                {
                    return true; // lost a race for the seat -- retry on the next hello
                }
            }
            peerPrimarySlot = (byte)slot;
            Console.WriteLine("[net] granted joiner primary slot=" + slot);
            return true;
        }

        // Our own roster as a slot mask, in the same shape as the peer's blockedSlots -- so the
        // allocation decision below is a pure function of two masks and can be tested (and its
        // convergence asserted) with no oracle, transport or session. eaSlotTest() drives it.
        private static byte HostOccupiedSlots()
        {
            return OccupiedMask(oracle, exclude: -1);
        }

        // The lowest seat free on OUR roster and not blocked on the peer's, or -1 when there is
        // none. Never slot 0 (HostPrimarySlot): that seat is the host's own primary, and in the
        // menu-lobby flow it is still empty at pairing time, so a plain "first free" would hand
        // the joiner the host's chair.
        internal static int FirstMutuallyFreeSlot(byte hostOccupied, byte peerBlocked)
        {
            for (int i = HostPrimarySlot + 1; i < Oracle.MaxPlayers; i++)
            {
                if (!NetProtocol.SlotInMask(hostOccupied, i) && !NetProtocol.SlotInMask(peerBlocked, i))
                {
                    return i;
                }
            }
            return -1;
        }

        // What taking the host's granted seat requires of us. Split out as a pure function
        // (the NetListing.ComputeEligible / PlayerShip.IsAiShootable house style) because the
        // live method needs an oracle, a transport, a paired session and a GameScene -- so the
        // branch that shipped this card's bug could not be tested at all. eaSlotTest() drives
        // this directly.
        internal enum SlotAdopt
        {
            Settled,     // idempotent repeat of a grant we already took
            TakeSlot,    // nothing seated that matters -- just adopt the number
            MoveSeat,    // a live scene seat (and its ship) must move across with us
            Renegotiate, // the seat cannot move; do NOT settle, hello again and let the host re-pick
        }

        internal static SlotAdopt DecideSlotAdopt(byte localSlot, byte granted, byte peerSlot,
            bool sceneUp, bool localSeated, bool grantedSeated)
        {
            if (localSlot == granted && peerSlot != NetProtocol.SlotNone)
            {
                return SlotAdopt.Settled;
            }
            // Only a seat inside a LIVE scene is load-bearing. At the menu -- where both the
            // menu-lobby and the join-in-progress joiner hello from -- the roster is whatever the
            // last level or attract demo left behind (GameScene.Terminate never clears it, and
            // ~60% of attract demos seat slot 1), and the launch path's ResetPlayers() wipes it
            // before seating us at the granted slot. So there is nothing to move and a busy
            // destination means nothing. Treating that stale roster as real is what made this
            // reachable from an ordinary "idle at the menu, then join a game".
            if (localSlot == granted || !sceneUp || !localSeated)
            {
                return SlotAdopt.TakeSlot;
            }
            // We are already seated mid-level (the dev ?net=join flow boots into a level before
            // pairing): the registration and the live ship both have to move. If they can't, our
            // slot must NOT advance -- claiming a slot our ship isn't in silently stops the
            // primary stream (FindLocalShip goes null -> alive=false forever) and re-streams the
            // real ship as a friend the host will refuse.
            return grantedSeated ? SlotAdopt.Renegotiate : SlotAdopt.MoveSeat;
        }

        // CLIENT: take the seat the host granted. In the menu-lobby and JIP flows our ship isn't
        // seated yet (EvLaunch -> Game1.MenuFinished reads LocalPrimarySlot), so this is just
        // bookkeeping. In the dev ?net=join flow we are already mid-level at slot 0, so the
        // registration AND any live ship move across.
        private static void AdoptGrantedPrimarySlot(byte slot)
        {
            if (slot >= NetProtocol.MaxSlots)
            {
                // Off-the-wire value, so bound it before anything acts on it. An out-of-range
                // grant is unreachable from our own host code, but taking it on trust would be
                // the one input the negotiation cannot converge on: LocalBlockedSlots can never
                // set a bit for a slot that does not exist, so the host would re-offer the same
                // impossible seat every second forever -- and at the menu we would silently
                // adopt a slot AddPlayerAt then refuses, leaving our ship in a seat the peer
                // never addresses.
                Console.WriteLine("[net] ignoring out-of-range granted slot=" + slot);
                return;
            }
            SlotAdopt action = DecideSlotAdopt(localPrimarySlot, slot, peerPrimarySlot,
                NetScene.Current != null, oracle.IsSeated(localPrimarySlot), oracle.IsSeated(slot));
            if (action == SlotAdopt.Settled)
            {
                return;
            }
            if (action == SlotAdopt.MoveSeat && !oracle.MovePlayerSlot(localPrimarySlot, slot))
            {
                action = SlotAdopt.Renegotiate; // lost a race for the seat since we decided
            }
            if (action == SlotAdopt.Renegotiate)
            {
                // Do NOT settle. Update's retry condition is `!PeerUp || peerPrimarySlot ==
                // SlotNone`, so leaving peerPrimarySlot unset keeps the 1 Hz hello going on both
                // peers -- and our next hello carries a fresh blockedSlots mask, off which the
                // host releases this seat and grants another (or refuses with RejectFull once
                // nothing works on both sides). Settling here instead is the whole bug: it
                // silenced the retry on BOTH peers, so a pairing that failed this way could
                // never recover and nothing was ever surfaced to the player.
                Console.WriteLine("[net] granted primary slot " + slot + " is occupied locally -- asking the host to re-grant");
                return;
            }
            if (action == SlotAdopt.MoveSeat)
            {
                foreach (PlayerShip s in oracle.GetShips())
                {
                    if (s.Owner == localPrimarySlot)
                    {
                        s.NetSetOwner(slot, oracle.Hue(slot));
                    }
                }
                Console.WriteLine("[net] moved local primary slot " + localPrimarySlot + " -> " + slot);
            }
            localPrimarySlot = slot;
            // Only ever assigned on a SETTLED adoption -- see the Renegotiate note above.
            peerPrimarySlot = HostPrimarySlot;
        }

        // Which seat our primary ship uses. Read by Game1.MenuFinished / LaunchLevelDirect /
        // TeamChallenge so a client seats its starter directly in the host-granted slot instead
        // of grabbing slot 0. Offline and host-side this is 0.
        public static int LocalPrimarySlot => Active ? localPrimarySlot : 0;

        // ---- couch (local) players joining an online session (card 4d904410) ---------------

        // GameScene.AddPlayer routes here while a session is up: the host allocates its own
        // couch seat locally, the client has to ask for one. Not seating locally on the client
        // is the whole point -- both peers used to grab "the next free slot" independently and
        // land two different players on the same slot number.
        internal static void TrySeatLocalJoin(ControlDevice device, bool spawnPlayer)
        {
            // AI friends are the one device that can seat more than once (several friends share
            // ControlDevice.AI) -- Oracle.AddPlayer exempts them from the same check.
            if (!Active || (device != ControlDevice.AI && oracle.DeviceIsPlaying(device)))
            {
                return;
            }
            if (isHost)
            {
                int slot = AllocateSeat();
                if (slot < 0 || !oracle.AddPlayerAt(slot, device))
                {
                    return; // roster full -- Start is a no-op
                }
                SeatJoinedShip(slot, device, spawnPlayer);
                return;
            }
            if (!PeerUp)
            {
                return; // no host to allocate from yet; press Start again once connected
            }
            if (joinRequestPending)
            {
                return; // one outstanding request at a time
            }
            joinRequestPending = true;
            pendingJoinDevice = device;
            pendingJoinSpawn = spawnPlayer;
            transport.SendReliable(NetProtocol.EncodeEmptyEvent(txEventSeq++, NetProtocol.EvJoinRequest));
            metrics.EventsTx++;
        }

        // The one seat allocator, used for our own couch joins and for answering the peer's. It
        // must never hand out the seat the joining peer's PRIMARY ship occupies: in the
        // menu-lobby flow that reservation is made before the level launches and
        // Game1.MenuFinished's ResetPlayers() wipes it, and SpawnPuppet only re-asserts it once
        // the peer's first live sample lands (seconds later). A couch join landing in that window
        // would take slot 1 and leave the remote player permanently unseatable.
        private static int AllocateSeat()
        {
            return AllocateSeatFrom(oracle, localPrimarySlot, peerPrimarySlot);
        }

        // The allocation itself, as a function of a roster and the two primary seats, so the
        // reserve -> hold -> expire -> REALLOCATE cycle can be driven against a scratch Oracle
        // with no session (eaSlotTest). That cycle is the only proof that a seat the host
        // reclaims from an unclaimed grant is genuinely re-usable rather than merely released.
        internal static int AllocateSeatFrom(Oracle roster, int localPrimary, int peerPrimary)
        {
            for (int slot = roster.FirstFreeSlot(); slot >= 0; slot = roster.FirstFreeSlot(slot + 1))
            {
                if (slot != peerPrimary && slot != localPrimary)
                {
                    return slot;
                }
            }
            return -1;
        }

        // The grant claim clock, split out for the same reason. STRICTLY greater: a grant is
        // still live on the tick its deadline lands, so a peer whose first stream arrives
        // exactly then keeps the seat it was given. Argument order mirrors the `now > deadline`
        // it replaced -- transposing the two would silently invert the predicate.
        internal static bool GrantHasExpired(long nowMs, long deadlineMs)
        {
            return nowMs > deadlineMs;
        }

        // HOST: a client couch player pressed Start. Allocate a seat and answer; the seat is held
        // as a RemoteFriend registration right away so the next allocation can't reuse it while
        // the grant is still in flight.
        private static void HandleJoinRequest()
        {
            int slot = AllocateSeat();
            if (slot >= 0 && !oracle.AddPlayerAt(slot, ControlDevice.RemoteFriend))
            {
                slot = -1;
            }
            transport.SendReliable(NetProtocol.EncodeByteEvent(txEventSeq++, NetProtocol.EvSlotGrant,
                slot < 0 ? NetProtocol.SlotNone : (byte)slot));
            metrics.EventsTx++;
            if (slot >= 0)
            {
                // Hold the reservation only until the peer's first stream for it arrives. The
                // client can silently fail to take a grant (its device got seated meanwhile, its
                // scene changed), and nothing else would ever release the seat -- one seat fewer
                // for the rest of the session, and the game stops being re-listable.
                grantsAwaitingStream[(byte)slot] = NowMs + GrantClaimTimeoutMs;
            }
            Console.WriteLine(slot < 0
                ? "[net] refused peer couch join -- roster full"
                : "[net] granted peer couch join slot=" + slot);
        }

        // Release a granted seat the peer never streamed into (see HandleJoinRequest).
        private static void ExpireUnclaimedGrants(long now)
        {
            if (grantsAwaitingStream.Count == 0)
            {
                return;
            }
            grantScratchSlots.Clear();
            foreach (KeyValuePair<byte, long> g in grantsAwaitingStream)
            {
                if (GrantHasExpired(now, g.Value))
                {
                    grantScratchSlots.Add(g.Key);
                }
            }
            foreach (byte slot in grantScratchSlots)
            {
                grantsAwaitingStream.Remove(slot);
                if (!FriendChannelExists(slot))
                {
                    oracle.RemovePlayerAt(slot, ControlDevice.RemoteFriend);
                    Console.WriteLine("[net] released unclaimed couch grant slot=" + slot);
                }
            }
        }

        // ?netdropgrant's decision, split out as a function of the flag so eaSlotTest() can drive
        // the latch with no session, no transport and no flag parsing (the flag is boot-only).
        //
        // WHY A LATCH AT ALL. Dropping EVERY grant means no couch join can ever complete for the
        // life of the page, so a run could only show the DROP half; one-shot lets the same run
        // show the host reclaiming the seat and someone then taking it. The cost is a flag that
        // outlives the thing that set it, which is the exact bug class this seam exists to hunt
        // -- so the clearing is the load-bearing half, and it lives in ResetPerSessionState
        // beside joinRequestPending rather than anywhere clever.
        //
        // Not consumed when the flag is off: an off run must leave the latch exactly as it found
        // it, so "has this session spent its drop" never depends on how many grants went past
        // while the seam was disabled.
        internal static bool ShouldDropGrant(bool flagOn)
        {
            if (!flagOn || dropGrantUsed)
            {
                return false;
            }
            dropGrantUsed = true;
            return true;
        }

        // Test access to the latch, so a suite can drive ShouldDropGrant without stranding a
        // consumed flag on a live ?netdropgrant session.
        internal static bool DropGrantUsed
        {
            get { return dropGrantUsed; }
            set { dropGrantUsed = value; }
        }

        // CLIENT: the host answered our couch-join request. On a grant, finish the join the
        // offline path would have done synchronously.
        private static void HandleSlotGrant(byte slot)
        {
            if (!joinRequestPending)
            {
                return;
            }
            joinRequestPending = false;
            if (slot == NetProtocol.SlotNone)
            {
                Console.WriteLine("[net] couch join refused by host (roster full)");
                return;
            }
            // ?netdropgrant: fail to take the grant on purpose, the way a real client can (its
            // device got seated meanwhile, its scene changed). ONE-SHOT per session, so a single
            // run covers the drop AND the recovery -- the seat the host reclaims is proved
            // re-takeable rather than merely released. Dropped AFTER clearing
            // joinRequestPending so this side is left exactly as a genuine failed take leaves it
            // -- no outstanding request, no seat -- and the host is the only one holding the
            // reservation. That is the state ExpireUnclaimedGrants exists to clean up.
            if (ShouldDropGrant(NetHost.Current.NetDropGrant))
            {
                Console.WriteLine("[net] ?netdropgrant: dropping granted couch slot=" + slot
                    + " -- host should release it in " + GrantClaimTimeoutMs + "ms");
                return;
            }
            // Same AI exemption as TrySeatLocalJoin: several friends legitimately share
            // ControlDevice.AI, so "already playing" must not block the second one.
            if ((pendingJoinDevice != ControlDevice.AI && oracle.DeviceIsPlaying(pendingJoinDevice))
                || !oracle.AddPlayerAt(slot, pendingJoinDevice))
            {
                Console.WriteLine("[net] could not take granted couch slot=" + slot + " device=" + pendingJoinDevice);
                return; // the host's grant expires on its side and the seat comes back
            }
            SeatJoinedShip(slot, pendingJoinDevice, pendingJoinSpawn);
        }

        private static void SeatJoinedShip(int slot, ControlDevice device, bool spawnPlayer)
        {
            Console.WriteLine("[net] couch player joined slot=" + slot + " device=" + device);
            if (spawnPlayer)
            {
                NetScene.Current?.SpawnPlayer(device, slot);
            }
        }

        // slot:device per seated slot, with a * on the ships WE simulate -- the two peers must
        // print identical maps (modulo which side owns what), which is the whole point of the
        // host allocating slots. Cheap enough for the 5s metrics cadence.
        private static string RosterReport()
        {
            string s = "";
            for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
            {
                if (!oracle.IsSeated(slot))
                {
                    continue;
                }
                ControlDevice device = oracle.Controller(slot);
                bool ours = device != ControlDevice.Remote && device != ControlDevice.RemoteFriend;
                s += (s.Length > 0 ? "," : "") + slot + ":" + device + (ours ? "*" : "");
            }
            // ...plus the ships actually alive. A seat with no ship, or a ship whose Owner
            // disagrees with its seat, is precisely the failure this card is about, and it is
            // invisible from the seat map alone.
            string ships = "";
            foreach (PlayerShip p in oracle.GetShips())
            {
                ships += (ships.Length > 0 ? "," : "") + p.Owner + ":" + p.Controller;
            }
            return (s.Length > 0 ? s : "-")
                + " pri=" + localPrimarySlot + "/" + (peerPrimarySlot == NetProtocol.SlotNone ? "-" : peerPrimarySlot.ToString())
                + " ships=" + (ships.Length > 0 ? ships : "-");
        }

        // On-demand roster dump for the console (eaNetRoster, card af0eb00a). The metrics line
        // already carries roster=, but it prints on a 5s cadence while the reset it has to be read
        // across (LoseLife -> respawn -> Normal) lasts ~2.7s -- a sampled before/after can straddle
        // the whole transition and show nothing. Called by hand on both peers either side of an
        // eaKillShips() reset, this makes the comparison exact instead. Same string the metrics
        // line builds, plus the reset counter the assertion is against.
        internal static string RosterDump()
        {
            if (!Active)
            {
                return "[net] no session (roster is single-player local state)";
            }
            // Positions are the half the metrics line's roster= cannot carry: a puppet that never
            // re-adopted after a reset still SHOWS as a ship in its seat, and only its pose
            // distinguishes "driven by the peer's stream" from "frozen where SpawnAllPlayers put
            // it". Sampled twice a second apart, a live slot moves and a frozen one does not.
            // ';' between entries, ',' only inside a coordinate -- the whole [net] family is meant
            // to be parseable, and a single separator for both would make the field ambiguous.
            string at = "";
            foreach (PlayerShip p in oracle.GetShips())
            {
                Vector2 pos = p.GetPosition();
                at += (at.Length > 0 ? ";" : "")
                    + p.Owner + ":" + p.Controller + "@" + (int)pos.X + "," + (int)pos.Y;
            }
            return "[net] roster=" + RosterReport()
                + " at=" + (at.Length > 0 ? at : "-")
                + " resets=" + metrics.Resets
                + " role=" + (isHost ? "host" : "join")
                + " peer=" + (PeerUp ? "up" : "down");
        }

        // Seat one couch player NOW, on the same path a gamepad Start press takes (card
        // af0eb00a). Distinct from TickLocalJoinSim below, which is deliberately PeerUp-gated:
        // this one is not, because the case worth reaching is the host filling its roster BEFORE
        // anyone pairs -- the only state in which a later joiner finds no seat and gets
        // RejectFull. Device choice mirrors the sim (Generic first, then AI for extras) so the
        // two seams seat the same kinds of player.
        internal static string DebugCouchJoin()
        {
            if (!Active)
            {
                return "no net session"; // oracle is only assigned once a session starts
            }
            ControlDevice device = oracle.DeviceIsPlaying(ControlDevice.Generic)
                ? ControlDevice.AI
                : ControlDevice.Generic;
            // Take the branch a pad press would take THIS tick: seating during the reset window
            // or a scripted no-ship phase must leave the spawn to SpawnAllPlayers, or we hand the
            // slot a ship the very next purge eats -- a seated slot with no ship, which is the
            // "missing owner" artifact this card's own gate reads as a failure.
            bool spawn = NetScene.Current?.JoinWouldSpawnNow ?? false;
            // TrySeatLocalJoin has five silent early returns (device already playing, roster full,
            // client not yet paired, a request already outstanding), so report what actually
            // happened rather than what was asked for: on the host the seat appears synchronously,
            // on the client the grant is a round trip and "requested" is the honest answer.
            int before = oracle.Players;
            TrySeatLocalJoin(device, spawn);
            string outcome = oracle.Players > before
                ? "seated device=" + device + " spawn=" + (spawn ? "now" : "deferred")
                : isHost ? "refused (roster full or device already playing)"
                : PeerUp ? "requested from host (grant is a round trip)"
                : "ignored (client, no host paired yet)";
            Console.WriteLine("[net] eaNetCouchJoin: " + outcome);
            return outcome;
        }

        // ---- ?netlocal=<n>: synthetic couch joins (card 4d904410 verification seam) ----------
        //
        // A couch join is a gamepad Start press, which the automated rig cannot produce: there are
        // no physical pads, and seating a Pad device with none connected trips GameScene's
        // disconnected-gamepad force-pause every tick. So the sim seats devices that behave like a
        // couch player WITHOUT needing hardware: `Generic` (the on-screen start device -- a real
        // human device with no connected-check) first, then `AI` for any extras. Both are locally
        // owned and therefore stream exactly like a human couch player; with ?aiplayer the Generic
        // ship flies itself too (it is not a puppet, so EffectiveController forces the AI branch).
        private const long LocalJoinSimDelayMs = 3000;

        private static int localJoinSimDone;
        private static long localJoinSimAt;

        private static void TickLocalJoinSim(long now)
        {
            if (NetHost.Current.NetLocal <= 0 || localJoinSimDone >= NetHost.Current.NetLocal)
            {
                return;
            }
            if (NetScene.Current == null || FindLocalShip() == null)
            {
                localJoinSimAt = 0; // wait for a settled level + our own ship before joining anyone
                return;
            }
            if (localJoinSimAt == 0)
            {
                localJoinSimAt = now + LocalJoinSimDelayMs;
                return;
            }
            if (now < localJoinSimAt || joinRequestPending)
            {
                return;
            }
            ControlDevice device = localJoinSimDone == 0 ? ControlDevice.Generic : ControlDevice.AI;
            localJoinSimDone++;
            localJoinSimAt = now + LocalJoinSimDelayMs; // stagger, so each join is legible in the log
            Console.WriteLine("[net] ?netlocal: simulating couch join " + localJoinSimDone + "/" + NetHost.Current.NetLocal
                + " device=" + device);
            TrySeatLocalJoin(device, spawnPlayer: true);
        }

        // Refuse the pairing: tell the peer why, then wind our side down after RejectGraceMs so
        // the reliable MsgReject actually egresses first (an immediate Stop()->pc.close() aborts
        // the still-buffered frame, and the peer would see only a channel close). Sent at most
        // once: repeat mismatched hellos during the grace are ignored -- our queued reject and
        // notice already stand, and the peer's hello retries at 1 Hz until it gets the reject.
        private static void SendRejectOnce(byte reason)
        {
            if (pendingStopAt != 0)
            {
                return;
            }
            transport.SendReliable(NetProtocol.EncodeReject(reason));
            pendingStopNotice = RejectNotice(reason, weSentIt: true);
            pendingStopReason = "pairing rejected";
            pendingStopAt = NowMs + RejectGraceMs;
        }

        private static void HandleReject(byte[] data)
        {
            if (data.Length < 2)
            {
                return;
            }
            Console.WriteLine("[net] peer rejected the pairing (reason=" + data[1] + ")");
            Stop("rejected by peer", RejectNotice(data[1], weSentIt: false));
        }

        private static string RejectNotice(byte reason, bool weSentIt)
        {
            switch (reason)
            {
            case NetProtocol.RejectVersion:
            case NetProtocol.RejectBuild:
                // Symmetric wording: ONE of the two builds is stale, and a reload fixes
                // the stale side whichever it is.
                return "Update required\nOne of you runs an outdated version\n(reload the page)";
            case NetProtocol.RejectFlags:
                return "Debug flags are active\nOnline co-op needs a clean boot (no ?flags)";
            case NetProtocol.RejectFull:
                // Asymmetric in cause but not in wording: the host ran out of seats (a couch
                // player took the last one), and there is nothing either side can do but retry.
                return weSentIt
                    ? "Game full\nNo free player slot for the other player"
                    : "Game full\nThat game has no free player slot";
            case NetProtocol.RejectBanned:
                // Asymmetric on purpose -- unlike the cases above, the two sides genuinely know
                // different things, and the refused player is owed a reason that isn't a lie.
                return weSentIt
                    ? "Blocked player\nThey tried to rejoin your game"
                    : "Removed from that game\nThe host blocked you from rejoining";
            default:
                return "Connection refused";
            }
        }

        private static void PeerConnected()
        {
            PeerUp = true;
            lastRxStreamAt = NowMs;
            Console.WriteLine("[net] peer connected (" + (isHost ? "join" : "host") + " side is up)");
            if (isHost)
            {
                if (listedSession)
                {
                    // Join-in-progress: the joiner paired with our LISTED game while we are
                    // already mid-level. Launch it into our current level+difficulty (it is a
                    // menu-session client that mirrors EvLaunch); its EvReady then triggers the
                    // live-world replay below plus the deep scenery catch-up (background ops /
                    // music cue), and the 1 Hz EvScoreSync trues up score/lives.
                    INetScene scene = NetScene.Current;
                    if (scene != null)
                    {
                        transport.SendReliable(NetProtocol.EncodeLaunchEvent(txEventSeq++,
                            (byte)scene.Level, (byte)Settings.GetInstance().CurrentDifficulty));
                        metrics.EventsTx++;
                        Console.WriteLine("[net] jip launch level=" + scene.Level
                            + " difficulty=" + Settings.GetInstance().CurrentDifficulty);
                    }
                }
                // Late joiner: replay the live NetId set so it can construct the already-
                // alive world instead of starting from a death-before-spawn storm.
                NetIdRegistry.ReplayLive();
            }
            if (localPaused)
            {
                // Re-announce a held pause across a reconnect so the peer re-freezes.
                transport.SendReliable(NetProtocol.EncodeByteEvent(txEventSeq++, NetProtocol.EvPause, 1));
                metrics.EventsTx++;
            }
        }

        // Card 11.5: THE one match-end path for a departed peer. A clean exit (EvLeave), a
        // drop verdict (stream timeout) and a closed tab (pagehide 'bye') are the same
        // outcome -- the match is over and both players go back to the menu -- so they share
        // this code instead of three near-copies that drift. Only the notice differs, because
        // "they left" and "the link died" are genuinely different information for the player;
        // a normal victory/game-over wind-down passes none (it is not a walk-out).
        private static void EndMatchPeerGone(string reason, string notice)
        {
            // (the banner is dropped by Stop() below, which every path here goes through)
            INetScene scene = NetScene.Current;
            bool normalEnd = scene != null && scene.NetEndingNormally;
            Stop(reason, normalEnd ? null : notice);
            scene?.NetApplyPeerLeft();
        }

        // The other match-end shape: the peer goes but WE stay in our level, playing solo. Used
        // whenever the host outlives the peer -- a JIP joiner dropping or leaving, and (card
        // 0b8a300b) a kick, which is the same outcome the host asked for deliberately.
        //
        // Freeing the Remote seat matters as much as exploding the puppet: leave it registered
        // and oracle.Players stays 2, so NetListing never re-lists and a phantom score panel
        // lingers. Couch players the peer brought (card 4d904410) go the same way -- our level
        // keeps running, so nothing else would ever purge their puppets or free their seats.
        private static void RevertToSinglePlayer(string reason)
        {
            ReleasePeerSeats();
            Stop(reason);
        }

        // The visible half of the revert, without the teardown -- the kick path needs these two
        // separable, because it must free the world NOW but keep the transport alive for the
        // grace (Stop() closes it).
        private static void ReleasePeerSeats()
        {
            if (puppet != null)
            {
                ExplodePuppet();
            }
            oracle.ReleasePlayer(ControlDevice.Remote);
            ReleaseAllFriendPuppets();
        }

        // Host-only, card 0b8a300b: once the peer's pause has outlasted KickOfferDelayMs, swap
        // the passive "OTHER PLAYER PAUSED" curtain for the interactive kick menu.
        //
        // The timing lives HERE, not in GameScene or the overlay, for the reason the whole card
        // exists: Push() disables every collection component, GameScene included, so neither can
        // tick during the freeze. NetSession.Update is driven straight from Game1.UpdateInner,
        // which makes it the one clock still running -- and it is already real-time (NowMs), the
        // right basis for a frozen world where gameTime means nothing.
        private static void TickKickOffer(long now)
        {
            if (!RemotePaused)
            {
                kickOfferShown = false;
                remotePauseAt = 0;
                return;
            }
            if (kickOfferShown || remotePauseAt == 0 || now - remotePauseAt < KickOfferDelayMs)
            {
                return;
            }
            INetScene scene = NetScene.Current;
            if (scene == null)
            {
                return;
            }
            // Latch ONLY on a menu that actually went up. NetShowKickMenu refuses when we hold
            // no freeze of our own -- which happens whenever our OWN pause menu was up when the
            // peer's EvPause landed (NetSetRemotePaused defers to the local pause). Latching
            // regardless would burn the single offer on a menu nobody saw, and once the host
            // resumed into the peer's still-held pause it would be frozen with no way out:
            // exactly the griefing hole this card closes.
            kickOfferShown = scene.NetShowKickMenu();
        }

        // Host action (card 0b8a300b): throw the peer out of the match and carry on playing.
        // `block` also refuses their rejoin for the rest of this level (see blockedPeers).
        //
        // The teardown is deliberately SPLIT: everything the player can see happens now (the
        // world unfreezes, the puppet goes, the seat frees), but transport.Close() waits out
        // RejectGraceMs -- Stop() -> pc.close() is abortive on WebRTC and would discard the
        // still-buffered EvKick, leaving the kicked player staring at a generic "disconnected"
        // instead of being told what happened. Same reason the reject path has that grace.
        public static void KickPeer(bool block)
        {
            if (!Active || !isHost || pendingStopAt != 0)
            {
                return;
            }
            ApplyKickBlock(block, peerPeerId);
            transport.SendReliable(NetProtocol.EncodeByteEvent(txEventSeq++, NetProtocol.EvKick, (byte)(block ? 1 : 0)));
            metrics.EventsTx++;
            Console.WriteLine("[net] kicked the peer" + (block ? " (blocked for this level)" : ""));
            // Release the freeze BEFORE the revert: the world is still pushed under the kicked
            // player's pause, and NetSetRemotePaused(false) is what pops it.
            if (RemotePaused)
            {
                RemotePaused = false;
                NetScene.Current?.NetSetRemotePaused(false);
            }
            PeerUp = false;
            ReleasePeerSeats();
            // Deliberately NOT RevertToSinglePlayer() -- its Stop() would close the transport
            // and abort the EvKick we just queued. The deadline below does the Stop() instead;
            // until then the session is Active with no peer, which Update handles by taking the
            // pendingStopAt branch and nothing else.
            kickOfferShown = false;
            pendingStopReason = "kick teardown";
            pendingStopNotice = null;
            pendingStopAt = NowMs + RejectGraceMs;
        }

        // "Keep Waiting": the host declined this offer, so hide the menu and start the delay
        // again. Waiting once must not forfeit the option -- a griefer holding pause forever
        // would otherwise get exactly one refusal and then a permanently frozen host.
        public static void RearmKickOffer()
        {
            remotePauseAt = NowMs;
            kickOfferShown = false;
        }

        // The two halves of the block rule, factored out of KickPeer and HandleHello so
        // NetKickTest can drive the REAL decisions rather than a paraphrase of them (the
        // messaging + teardown around them is what the two-window run covers).
        //
        // peerId 0 means the peer could not produce a token at all: never recorded, never
        // matched. Otherwise one broken localStorage would block every other such peer at once.
        internal static void ApplyKickBlock(bool block, ulong peerId)
        {
            if (block && peerId != 0)
            {
                blockedPeers.Add(peerId);
            }
        }

        internal static bool IsPeerBlocked(ulong peerId)
        {
            return peerId != 0 && blockedPeers.Contains(peerId);
        }

        // Scoped to one level run (GameScene.Terminate) -- the card's "for that session only".
        public static void ClearBlockedPeers()
        {
            blockedPeers.Clear();
        }

        internal static int BlockedPeerCount => blockedPeers.Count;

        // For NetKickTest's save/restore, so running the self-test mid-level cannot quietly
        // un-block someone the host had already thrown out.
        internal static ulong[] SnapshotBlockedPeers()
        {
            ulong[] ids = new ulong[blockedPeers.Count];
            blockedPeers.CopyTo(ids);
            return ids;
        }

        // Drop the banner with no verdict attached -- used by the teardown paths, where the
        // peer did NOT recover and saying so would be a lie.
        private static void ClearPeerStalled()
        {
            if (!peerStalled)
            {
                return;
            }
            peerStalled = false;
            NetScene.Current?.NetSetPeerStalled(false);
        }

        // `recovered` distinguishes the two ways the banner drops: the stream actually came
        // back, versus the peer announcing a pause (which suppresses the banner but leaves
        // the stream just as quiet -- lastRxStreamAt is only refreshed by ship state and
        // snapshots, never by an event). Claiming a recovery in the second case would be a
        // lie, and a backgrounded tab bursting out a late EvPause hits it routinely.
        private static void SetPeerStalled(bool on, bool recovered)
        {
            if (on == peerStalled)
            {
                return;
            }
            if (!on)
            {
                ClearPeerStalled();
                if (recovered)
                {
                    Console.WriteLine("[net] peer recovered");
                }
                return;
            }
            peerStalled = true;
            Console.WriteLine("[net] peer stalled (stream quiet > " + PeerStallMs + "ms) -- grace running");
            NetScene.Current?.NetSetPeerStalled(true);
        }

        private static void PeerLost(string reason)
        {
            if (!PeerUp)
            {
                return;
            }
            PeerUp = false;
            Console.WriteLine("[net] peer lost (" + reason + ")");
            if (menuSession)
            {
                // Card 11.4 match-end semantics: any player leaving ends the match --
                // menu-lobby sessions have no reconnect flow.
                EndMatchPeerGone("peer lost: " + reason, "The other player disconnected\nMatch ended");
                return;
            }
            ClearPeerStalled();
            if (listedSession)
            {
                RevertToSinglePlayer("jip peer lost: " + reason);
                return;
            }
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
            if (RemotePaused)
            {
                // Never leave the world frozen by a peer that's gone.
                RemotePaused = false;
                NetScene.Current?.NetSetRemotePaused(false);
            }
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
            if (NetScene.Current == null)
            {
                // Menu-lobby flow: the host may be in-level while we're still warming --
                // don't build puppets into a menu world; EvReady triggers a replay once
                // our scene is up. (Counts as heartbeat above either way.)
                return;
            }
            int count = data[1];
            int off = NetProtocol.SnapshotHeaderBytes;
            for (int i = 0; i < count; i++)
            {
                if (!NetProtocol.TryReadSnapshotEntry(data, ref off, out ushort netId, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen))
                {
                    break;
                }
                metrics.SnapEntriesRx++;
                if (NetPuppets.OnSnapshotEntry(netId, typeIdx, state, data, extraOff, extraLen, out bool popped, out SnapUnknownKind kind))
                {
                    if (popped)
                    {
                        metrics.PuppetPops++;
                    }
                }
                else
                {
                    // Keep the total AND why (card 48ab9b2f). Rebuilt/LeftDead are ordinary
                    // traffic -- their rates track the world's spawn and removal rates, so a
                    // busy level logs plenty of both on a perfectly healthy link. Refused is
                    // the fault shape (an unknown typeIdx re-counts every turn; the other two
                    // causes mark the id removed first and so tick more slowly -- NetMetrics).
                    metrics.SnapUnknownIds++;
                    switch (kind)
                    {
                    case SnapUnknownKind.Rebuilt:  metrics.SnapNew++; break;
                    case SnapUnknownKind.LeftDead: metrics.SnapDead++; break;
                    case SnapUnknownKind.Refused:  metrics.SnapBad++; break;
                    }
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
                if (isHost || NetScene.Current == null
                    || !NetProtocol.TryDecodeSpawnEvent(data, out ushort id, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen))
                {
                    return;
                }
                if (!NetPuppets.OnSpawn(id, typeIdx, state, data, extraOff, extraLen))
                {
                    metrics.DupSpawns++;
                }
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] rx spawn id=" + id + " typeIdx=" + typeIdx);
                }
                break;
            }
            case NetProtocol.EvDeath:
            {
                if (isHost || data.Length < NetProtocol.DeathEventBytes || NetScene.Current == null)
                {
                    return;
                }
                ushort id = NetProtocol.ReadU16(data, 4);
                byte killer = data[6]; // wire slot == oracle slot on both peers
                Vector2 pos = new Vector2(NetProtocol.ReadF32(data, 7), NetProtocol.ReadF32(data, 11));
                NetProtocol.ReadDeathAwards(data, deathAwardScratch);
                NetPuppets.OnRemoteDeath(id, killer, pos, deathAwardScratch);
                if (NetHost.Current.NetLog)
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
                if (isHost || data.Length < 5 + 4 * NetProtocol.MaxSlots || NetScene.Current == null)
                {
                    return;
                }
                score.Lives = (sbyte)data[4];
                // Worst skew ACROSS the slots, recorded once. Recording per slot would just
                // leave the last one standing -- slot 3, unseated in any 2-peer session, so a
                // hard-coded 0.0 that looks like proof of nothing going wrong.
                float worstSkew = 0f;
                for (int slot = 0; slot < NetProtocol.MaxSlots; slot++)
                {
                    float host = NetProtocol.ReadF32(data, 5 + 4 * slot);
                    // EvDeath and EvScoreSync both ride the ordered reliable lane, and the host
                    // snapshots these scores at its top-of-tick flush -- the point where that
                    // tick's EvDeath events go out. So an award inside `host` has already been
                    // announced (and is off the unsettled books) and one that has not been
                    // announced is not inside it: the sum is exact either way round.
                    float pending = NetPuppets.UnsettledFor(slot);
                    float skew = score.PointScore(slot) - (host + pending);
                    if (Math.Abs(skew) > Math.Abs(worstSkew))
                    {
                        worstSkew = skew;
                    }
                    score.NetSetScore(slot, host, pending);
                }
                metrics.NoteScoreSkew(worstSkew);
                break;
            }
            case NetProtocol.EvBlast:
            {
                if (data.Length < 14)
                {
                    return;
                }
                if (NetScene.Current == null)
                {
                    return;
                }
                byte blastSlot = data[4];
                int level = data[13];
                // Slot-tagged (v5): detonate the puppet that actually bombed -- the peer's
                // primary or any of its couch/AI ships. Never one of OURS: any slot disagreement
                // (a reconnect race, a refused move) would otherwise hand the peer a free bomb
                // on our own player.
                PlayerShip bomber = oracle.GetPlayerShip(blastSlot);
                if (bomber == null || IsLocallyOwned(bomber))
                {
                    return;
                }
                bomber.NetDoBlast(level);
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] rx blast slot=" + blastSlot + " level=" + level);
                }
                break;
            }
            case NetProtocol.EvJoinRequest:
            {
                if (isHost)
                {
                    HandleJoinRequest();
                }
                break;
            }
            case NetProtocol.EvSlotGrant:
            {
                if (!isHost && data.Length >= 5)
                {
                    HandleSlotGrant(data[4]);
                }
                break;
            }
            case NetProtocol.EvMessage:
            {
                if (isHost || NetScene.Current == null
                    || !NetProtocol.TryDecodeMessageEvent(data, out AnimatedMessage.MessageType msgType, out SoundManager.Texts speech, out float angle, out string text))
                {
                    return;
                }
                AnimatedMessage msg = AnimatedMessage.NewAnimatedMessage(bin, game);
                msg.Setup(text, speech, msgType);
                if (msgType == AnimatedMessage.MessageType.redwarning)
                {
                    msg.SetWarningDirection(angle);
                }
                // A standing Purge<AnimatedMessage> can eat this add (GameScene.UpdateWin /
                // UpdateResetting arm one, and the rx drain runs later in the same tick), and
                // that is CORRECT -- do not "fix" it by exempting the add (card 74403f83).
                // Terminate is already excluded by the NetActiveScene gate above, which it nulls
                // before its purges. In the two remaining windows, eating the banner is what
                // MATCHES the host: the level script is host-only and only runs in
                // GameState.Normal, so the host cannot emit a beat while it is itself in Win or
                // Resetting, and both peers enter those states from the host's own broadcast. A
                // banner the host is showing is a banner the host has not purged. Reaching this
                // would mean the two state machines had already diverged -- a different bug,
                // which letting the banner through would only mask. Nothing dangles either way:
                // the banner is one-shot and nothing holds a reference past the Add. ?binlog
                // logs the divert if it ever does fire.
                bin.Add((GameComponent)(object)msg);
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvUnlock:
            {
                if (isHost || !NetProtocol.TryDecodeUnlockEvent(data, out Unlockables.Items unlockItem, out AnimatedMessage.UnlockType ut, out SoundManager.Texts speech, out string text))
                {
                    return;
                }
                // Generous: the join peer played the level too -- grant the same unlocks the
                // host-side UnlockEvent granted (idempotent), then show the same banner.
                Unlockables.GetInstance().Unlock(unlockItem);
                if (unlockItem == Unlockables.Items.HarderDifficulties)
                {
                    Unlockables.GetInstance().Unlock(Unlockables.Items.InsaneDifficulty);
                }
                if (ut == AnimatedMessage.UnlockType.cheat)
                {
                    Unlockables.GetInstance().Unlock(Unlockables.Items.Cheats);
                }
                if (ut == AnimatedMessage.UnlockType.challenge)
                {
                    Unlockables.GetInstance().Unlock(Unlockables.Items.Challenges);
                }
                Unlockables.GetInstance().SaveThreaded();
                // The GRANT above always applies; the banner is world dressing -- skip it
                // if our scene isn't up (menu-lobby warm race).
                if (NetScene.Current != null)
                {
                    AnimatedMessage banner = AnimatedMessage.NewAnimatedMessage(bin, game);
                    banner.Setup(text, speech, AnimatedMessage.MessageType.unlocked);
                    banner.SetUnlockType(ut);
                    // Same standing-purge analysis as EvMessage above: eating this matches the
                    // host, and the GRANT (which is what actually matters) already happened
                    // unconditionally. Card 74403f83.
                    bin.Add((GameComponent)(object)banner);
                }
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvBackground:
            {
                if (isHost || !NetProtocol.TryDecodeBackgroundEvent(data, out NetBackgroundOp bgOp, out Vector2 v))
                {
                    return;
                }
                NetScene.Current?.NetApplyBackgroundOp(bgOp, v);
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvCosmeticSwarm:
            {
                if (isHost || !NetProtocol.TryDecodeCosmeticSwarmEvent(data, out NetCosmeticKind kind, out bool swarmOn, out float swarmRate))
                {
                    return;
                }
                NetScene.Current?.NetApplyCosmeticSwarm(kind, swarmOn, swarmRate);
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvMusic:
            {
                if (isHost || data.Length < 5 || NetScene.Current == null)
                {
                    return;
                }
                sound.NetApplyMusic(data[4] == NetProtocol.MusicStop ? -1 : data[4]);
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvCheckpoint:
            {
                if (isHost)
                {
                    return;
                }
                NetScene.Current?.NetApplyCheckpoint();
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvReset:
            {
                if (isHost || data.Length < 5)
                {
                    return;
                }
                NetScene.Current?.NetApplyReset(data[4]);
                metrics.Resets++;
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] rx reset mode=" + data[4]);
                }
                break;
            }
            case NetProtocol.EvVictory:
            {
                if (isHost)
                {
                    return;
                }
                NetScene.Current?.NetApplyVictory();
                metrics.Victories++;
                break;
            }
            case NetProtocol.EvPause:
            {
                if (data.Length < 5)
                {
                    return;
                }
                bool on = data[4] != 0;
                RemotePaused = on;
                NetScene.Current?.NetSetRemotePaused(on);
                if (on)
                {
                    metrics.Pauses++;
                    // Arm the host's kick offer (card 0b8a300b). Note this is the EVENT edge,
                    // not the freeze edge: the freeze itself is skipped while our own pause menu
                    // is up, and the clock should still run then -- NetLocalPauseReleased
                    // re-freezes into a pause that may already have earned the offer.
                    remotePauseAt = NowMs;
                    kickOfferShown = false;
                }
                else
                {
                    remotePauseAt = 0;
                    kickOfferShown = false;
                }
                break;
            }
            case NetProtocol.EvTetherBreak:
            {
                NetScene.Current?.NetApplyTetherBreak();
                metrics.TetherBreaks++;
                break;
            }
            case NetProtocol.EvLaunch:
            {
                if (isHost)
                {
                    return;
                }
                // A launch we cannot mirror ENDS the pairing rather than being ignored: we are
                // sitting in the lobby on "the host is choosing a mission", and a silently
                // dropped launch leaves that screen there forever -- the same never-advances
                // failure the validation exists to remove. Reuses the peer-gone path (no
                // GameScene here, so it is exactly Stop() + the notice).
                if (!NetProtocol.TryDecodeLaunchEvent(data, out Levels launchLevel, out Settings.DifficultyLevel launchDifficulty))
                {
                    Console.WriteLine("[net] refused launch: level/difficulty off the wire is not in this build"
                        + (data.Length >= 6 ? " (level=" + data[4] + " difficulty=" + data[5] + ")" : " (truncated)"));
                    // Wording covers BOTH refusals -- the decoder fails on an unknown level,
                    // an unknown difficulty or a short frame, and naming only the mission
                    // would be simply untrue for a joiner refused over the tier.
                    EndMatchPeerGone("launch not understood",
                        "Update required\nThe host picked a mission or difficulty\nthis version does not have\n(reload the page)");
                    return;
                }
                pendingLaunchLevel = launchLevel;
                pendingLaunchDifficulty = launchDifficulty;
                pendingLaunchHas = true;
                Console.WriteLine("[net] rx launch level=" + launchLevel + " difficulty=" + launchDifficulty);
                break;
            }
            case NetProtocol.EvReady:
            {
                if (!isHost)
                {
                    return;
                }
                // The client's scene just came up (it may have out-warmed us): replay the
                // live world so it isn't waiting on snapshot self-heals for spawn extras.
                NetIdRegistry.ReplayLive();
                // ...and the deep mid-level scenery our script already ran (card 45a4e48d).
                // This is the seam, not PeerConnected: a join-in-progress peer has no
                // GameScene at pairing time, and the Initialize that gives it one would
                // clobber anything we sent earlier with the level's INITIAL background/music.
                NetScene.Current?.NetReplayCatchUp();
                break;
            }
            case NetProtocol.EvLeave:
            {
                if (listedSession)
                {
                    RevertToSinglePlayer("jip peer left the match");
                    break;
                }
                // A shared victory/game-over also lands here (whichever scene terminates
                // first sends the leave) -- EndMatchPeerGone treats that as a normal end and
                // shows no notice. (A null scene there = the lobby/warm phase, i.e. a real
                // walk-out, so the notice does show; our OWN finished level can't reach this
                // -- its scene-down edge already stopped the session.)
                EndMatchPeerGone("peer left the match", "The other player left\nMatch ended");
                break;
            }
            case NetProtocol.EvKick:
            {
                // The host threw us out (card 0b8a300b). Only the host may kick, so ignore it
                // coming the other way rather than letting a client end the host's match.
                if (isHost || data.Length < 5)
                {
                    return;
                }
                bool blocked = data[4] != 0;
                Console.WriteLine("[net] kicked by the host" + (blocked ? " (blocked)" : ""));
                // Same shape as any other match end: Stop() first (which releases the freeze if
                // the host was holding one), then NetApplyPeerLeft(), which unwinds our own
                // pause-menu depth -- we are almost certainly sitting in it, since holding the
                // pause is what got us kicked -- and force-exits to the main menu.
                EndMatchPeerGone("kicked by the host", blocked
                    ? "Removed from the game\nThe host blocked you from rejoining"
                    : "Removed from the game\nThe host kicked you");
                break;
            }
            }
        }

        // GENEROUS at-least-once claim honoring (host). Alive -> the real per-type death
        // path credited to the claimant (authoritative children spawn here and replicate).
        // Already settled -> pay the claimant once. Never rejected.
        //
        // THE LEDGER OPENS AT THE CLAIM, NOT AT THE REMOVAL FLUSH (card 1bfcd705). A settled
        // entity does not get its recentDeaths record until OnHostDeath runs at the
        // ComponentRemoved seam, one ComponentBin flush later, so for the rest of the tick the
        // Entry carries the ledger instead (Entry.ClaimSettled / Entry.ClaimPaidMask) and
        // OnHostDeath folds it into the record. Without that window covered, a second claimant
        // landing in the same DrainRx was paid nothing at all, and a repeat from the slot just
        // paid was refused by nothing -- unbounded on a Powerup, which never flips IsDead and so
        // re-entered the live branch (and its AddLife) once per claim frame.
        private static void HandleClaim(ushort netId, byte killerSlot)
        {
            metrics.ClaimsRx++;
            // Is there a slot this claim can be credited to at all? Bound against the SCORE
            // PANELS (4), not the 8 of the ledgers' PaidMask, for ApplyRemotePowerup's reason --
            // killerSlot is a raw wire byte, and ScoreVisualiser.AddScore indexes a fixed 4-slot
            // list, so a corrupt or mismatched peer must not reach it. An unattributed
            // (KillerNone) or out-of-range claim is settle-only: nothing is paid, nothing masked.
            bool payable = killerSlot != NetProtocol.KillerNone
                && killerSlot < ScoreVisualiser.SlotCount;
            if (NetIdRegistry.TryGetById(netId, out NetIdRegistry.Entry e))
            {
                if (payable && (e.ClaimPaidMask & (1 << killerSlot)) != 0)
                {
                    return; // this slot already had its payout for this entity
                }
                if (e.Comp.IsDead || e.ClaimSettled)
                {
                    // Settled, but the removal has not flushed, so there is no record yet. Pay
                    // from the Entry, off the very fields OnHostDeath will read a flush later.
                    //
                    // It pays exactly what the RECORD branch below pays -- no NoteAward, and no
                    // ApplyRemotePowerup even though the pickup is right here on e.Comp. Both
                    // omissions are deliberate: the death record carries neither an award array
                    // nor a pickup type, so doing either here would make a claim worth more on
                    // one side of a ComponentBin flush than the other, for a difference the
                    // claimant cannot observe (a zero in e.Awards reads as "no figure" to
                    // NetPuppets.ApplyAwards, and EvScoreSync carries the host total that
                    // already contains this payout).
                    if (payable)
                    {
                        e.ClaimPaidMask |= (byte)(1 << killerSlot);
                        PayDeadClaim(netId, killerSlot, e.Comp.Position,
                            (ushort)MathHelper.Clamp(e.Comp.NetPointValue, 0f, 65535f),
                            e.Comp.NetPickup is INetPickup dead && dead.NetPickupType == Powerup.PowerupType.OneUp);
                    }
                    return;
                }
                if (payable)
                {
                    NoteKillSlot(e.Comp, killerSlot); // attribution for the death broadcast
                }
                if (e.Comp.NetKillable is INetKillable killable && payable)
                {
                    killable.NetKill(NetPuppets.KillerAgent(killerSlot, e.Comp.Position), isComboGenerator: true);
                    if (!e.Comp.IsDead)
                    {
                        bin.Remove((GameComponent)e.Comp);
                    }
                }
                else
                {
                    // Non-killable replicable (EvilBullet swept by a blast, Powerup pickup,
                    // Asteroid...): settle it directly, pay any claimant its points.
                    if (e.Comp.NetPickup is INetPickup p)
                    {
                        p.NetMarkTaken();
                        ApplyRemotePowerup(p, killerSlot);
                        // Lives are host-authoritative (EvScoreSync sends them verbatim), so a
                        // client-collected extra life must be applied HERE or the next sync
                        // silently reverts it. Other powerup effects are per-ship on the collector.
                        if (p.NetPickupType == Powerup.PowerupType.OneUp && payable)
                        {
                            score.AddLife();
                        }
                    }
                    else if (payable)
                    {
                        if (e.Comp.NetPointValue > 0f)
                        {
                            // Through NoteAward, not a bare AddScore: this branch settles a claim
                            // WITHOUT going via AwardScore, so without it e.Awards stays null and
                            // the EvDeath below would carry an all-zero award array (card b0ab09ec).
                            NoteAward(e.Comp, killerSlot,
                                score.AddScore(e.Comp.NetPointValue, true, e.Comp.Position, killerSlot));
                        }
                        Explosion explosion = Explosion.NewExplosion(bin, game);
                        explosion.Setup(e.Comp.Position, 1.2f, 1f, 0f, 0f);
                        bin.Add((GameComponent)(object)explosion);
                    }
                    bin.Remove((GameComponent)(object)e.Comp);
                }
                // The live branch settles the entity exactly ONCE, whoever else claims it after
                // this. Neither of the two branches above flips IsDead for a pickup or a plain
                // non-killable -- both just queue the removal -- so this flag is what tells a
                // later claim in the same tick that the settling has already happened.
                e.ClaimSettled = true;
                if (payable)
                {
                    e.ClaimPaidMask |= (byte)(1 << killerSlot);
                }
                metrics.ClaimsHonored++;
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] claim honored (live kill) id=" + netId + " slot=" + killerSlot);
                }
                return;
            }
            // Out of the world already: pay the claimant from the bounded death record, once.
            if (payable && recentDeaths.TryGetValue(netId, out DeathRecord rec)
                && (rec.PaidMask & (1 << killerSlot)) == 0)
            {
                rec.PaidMask |= (byte)(1 << killerSlot);
                recentDeaths[netId] = rec;
                PayDeadClaim(netId, killerSlot, rec.Pos, rec.Points, rec.OneUp);
            }
        }

        // The generous payout for a claim whose target was already settled -- from the Entry
        // while its removal is still queued, from the death record once that has flushed. ONE
        // helper on purpose (card 1bfcd705): the two ledgers cover consecutive halves of the same
        // window, so a claim's value must not depend on which side of a ComponentBin flush it
        // landed. The caller owns the paid-once bookkeeping; this only pays.
        private static void PayDeadClaim(ushort netId, byte killerSlot, Vector2 pos, ushort points, bool oneUp)
        {
            if (points > 0)
            {
                score.AddScore(points, true, pos, killerSlot);
            }
            if (oneUp)
            {
                score.AddLife(); // overlapping collectors inside the RTT window each add one
            }
            metrics.ClaimsPaidDead++;
            if (NetHost.Current.NetLog)
            {
                // "settled", not "already dead": the pre-flush half of this reaches a Powerup
                // whose settle path sets `taken` and never flips IsDead.
                Console.WriteLine("[net] claim honored (already settled, paid) id=" + netId + " slot=" + killerSlot);
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
            // The peer's primary seat was allocated at handshake time (host: it granted it;
            // client: slot 0), so this only has to fill it -- never pick one.
            if (peerPrimarySlot == NetProtocol.SlotNone)
            {
                return;
            }
            if (!oracle.DeviceIsPlaying(ControlDevice.Remote) && !oracle.AddPlayerAt(peerPrimarySlot, ControlDevice.Remote))
            {
                return;
            }
            int slot = oracle.GetPlayerIndex(ControlDevice.Remote);
            PlayerShip ship = bin.Recycle<PlayerShip>();
            if (ship == null)
            {
                ship = new PlayerShip(game);
            }
            ship.Setup(slot, buffer.Newest.Pos, startup: false, invulnerable: false, 4.712389f);
            if (!bin.TryAdd((GameComponent)(object)ship))
            {
                // A standing Purge<PlayerShip> is live this tick. The one that can actually
                // reach us is NetApplyReset's, because it purges from inside this very rx
                // drain; the LoseLife / UpdateWin / UpdateResetting purges run back in
                // base.Update and their deaths are flushed by collectionHelper.Update() before
                // the drain, which leaves FindLocalShip() null and the caller's gate shut. The
                // ship being purged is CORRECT either way (a reset wipes all ships and
                // SpawnAllPlayers respawns every seated slot), but adopting one that never
                // entered the world points `puppet` at a ship the world does not have, and the
                // retry gate above is `puppet == null`. Leave it clear and retry next tick, once
                // TopOfTickFlush has expired the filter; the seat we just took is reused via
                // DeviceIsPlaying above (card 74403f83).
                // MEASURED WINDOW: one tick, not the session -- an earlier revision of this
                // comment said "invisible for the rest of the session" and that is wrong.
                // ManagePuppet opens by RELEASING a puppet the oracle does not hold
                // (`!oracle.GetShips().Contains(puppet)`), a block that predates this fix
                // (Stage 11.1, 6f36aae), so the pre-card bug self-heals on the next tick. The
                // release is a safety net, not the intended path, so the guard stays -- but do
                // not re-inflate the severity. Pinned by NetResetSpawnTest, whose mutation
                // record has the numbers.
                return;
            }
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
