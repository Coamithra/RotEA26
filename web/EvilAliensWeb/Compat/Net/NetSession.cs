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
    // locally from its replicated cumulative shot count.
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
        // v11 (card f62116b5): EvDying -- the host announces that a deferred death has BEGUN, at
        // the moment KilledBy returns without removing the component, instead of the joiner
        // inferring it from hp==0 on that entity's next snapshot turn. A v10 peer would ignore
        // the event and fall back to the hp==0 trigger, i.e. the pre-card latency rather than a
        // desync -- so this bump is the cheap-protocol ruling's "put it on the wire and move the
        // number" rather than a forced incompatibility.
        // v12 (card a45b78f6): MsgShipState / MsgFriendState carry a cumulative u8 shotCount in
        // place of the `firing` LEVEL flag. MsgFriendState MISPARSES on a v11 peer -- its b[2] was
        // the flags byte and is now a raw count, so bit 1 of the count reads as "firing" and every
        // couch/AI-friend puppet fires at random. MsgShipState degrades more quietly (the count
        // took the byte after `aim`, which v11 never read, so it would simply see firing=false
        // forever and the remote ship would never shoot). Either way a mixed pairing is wrong
        // rather than merely older, which is the bump test.
        // v13 (card e79bb994): a per-SAMPLE flags byte on every world-snapshot ENTRY
        // (NetProtocol.NetSnapshotFlags), carrying the host's teleport marker. Like v12 and unlike
        // the appended event types before it, this MOVED AN EXISTING LAYOUT -- a v12 peer would
        // mis-parse every snapshot entry it received, so the bump is not a courtesy here, it is
        // the only thing standing between a stale peer and a garbage world.
        // v14 (card c1a38ef9): motion parameters on the wire -- LazerDescriptor's state extras
        // grow three sent RATES (6 -> 12 bytes) and FlyingSpiderDescriptor gains a path anchor in
        // its spawn extras (1 -> 5) plus state extras where it had none (0 -> 4).
        // Both blocks are length-guarded, so an older peer degrades to exactly the pre-card
        // behaviour rather than desyncing -- the ca4fd94f bump test, which this passes. The bump
        // is the batch convention rather than a strict requirement; see NetProtocol's header.
        // v15 (card a66e190a): EvSlowmo (event 25) -- either peer announces its 1up slow motion
        // so both worlds scale together instead of one crawling while the other runs. A v14 peer
        // ignores the unknown event and falls back to the pre-card unilateral slowdown, so like
        // v14 this is the batch convention rather than a forced incompatibility.
        // v16 (card c5228350): MsgHudState's per-slot entry carries the owner's OPTION SHIP
        // POPULATION, per orbit layer (HudSlotBytes 10 -> 12). The entries are FIXED WIDTH, so
        // this MOVED AN EXISTING LAYOUT like v13 -- a v15 peer would mis-parse every entry after
        // the first, which is a garbage HUD rather than a missing field. Not a courtesy bump.
        // v17 (card 37f3a663): EvRespawn (event 26) -- either peer announces that one of its ships
        // has started its respawn clock, so the OTHER peer draws the indicator too and knows its
        // buddy is coming back and where. A v16 peer ignores the unknown event and simply does not
        // draw it, i.e. the pre-card behaviour, so like v14 and v15 this bump is the parallel
        // batch's convention rather than a forced incompatibility.
        // v18 (card 9ccfe295): LazerDescriptor gains a `[ownerNetId:2]` SPAWN extra where it
        // had none -- the beam's EMITTER, so a client's replicated beam stops killing the ship
        // that fired it. Like v14 the block is APPEND-ONLY and LENGTH-GUARDED, so an older peer
        // degrades to exactly the pre-card behaviour (an ownerless beam) rather than
        // mis-parsing: the bump is the parallel batch's convention, not a forced
        // incompatibility.
        // v21 (card 950bb70a): MsgShipState / MsgFriendState grow two trailing roll-ring bytes
        // (31 -> 33) -- bit i = the owner's asplode / bounce roll for the shot whose cumulative
        // count is ShotCount-i, so the puppet applies the owner's per-bullet outcome instead of
        // re-rolling and the mini-blasts land on the SAME bullets on both screens. Like v12 this
        // is a fixed-width per-tick layout, and the decoders' length gates moved 31 -> 33 with
        // it -- so a v20 peer's ship frames would be REFUSED wholesale (a frozen puppet, not a
        // graceful re-roll). A real bump, not a courtesy one; only the other direction (a v20
        // peer receiving our 33-byte frames) tolerates the extra bytes.
        // v22 (card 1210e14e): BallDescriptor gains its first STATE EXTRAS, [flags:1], bit0 = the
        // ball is CONNECTED to the junkboss -- the one bit a joiner needs to hit-test the rocks at
        // the same radius the host does (it was reading them 20% small). The block is APPEND-ONLY
        // and length-guarded (snapshot entries are length-prefixed and ApplyStateExtra gates on
        // `len`), which is what keeps the DECODER robust and the bump mechanical.
        // v23 (card b2828be8, Stage 11.8): EVERY ship on the wire carries its slot. MsgShipState
        // becomes the slot-keyed form (34 bytes: a leading slot byte, the flags byte gaining
        // ShipFlagPrimary to mark the sender's heartbeat frame) and MsgFriendState is RETIRED --
        // one message, one receive path, one drive path for every remote ship, because "the
        // sender's primary" stops being a meaningful identity once there can be more than one
        // sender. MsgHudState's fixed-width entry also grows a combo remaining-time byte
        // (HudSlotBytes 16 -> 17, folding card a5b1e941): the observer parks its combotimer at
        // the owner's actual remaining time instead of refreshing it to full, so the two
        // screens' combo readouts fade in phase. A FORCED bump three ways over -- a v22 peer
        // would mis-parse every ship frame in both directions and every HUD entry after the
        // first. Behaviour-neutral at 2 peers by design; the session layer's N-peer semantics
        // are card 87242257's.
        // v24 (card 87242257, Stage 11.9): EvPeerLeft (event 27) -- the host tells the remaining
        // clients that a departed peer's roster seats are free, which the new match-end policy
        // (a client leaving no longer ends the match) makes a fact they cannot infer from the
        // relay going quiet. New event type only, so an older peer would merely leak the seats
        // -- but the handshake refuses a mismatched pairing anyway (the NOTE below). The same
        // card makes the host->client EvPause carry a per-recipient AGGREGATE ("someone besides
        // you is paused") rather than the host's own pause alone; payload unchanged.
        // v25 (card 0257f8ba, Stage 11.10): EvLobbyRoster (event 28) -- the host tells its lobby
        // clients which roster seats are taken, so the waiting panel can show who is in. New
        // event type only (an older peer would merely show no roster line), so like v14/v15/v17
        // the bump is the convention rather than a forced incompatibility. The same card raises
        // the REAL rooms to 4 machines (menu lobby + listed/JIP -- webrtc.js/server side, no wire
        // layout involved), which is what makes the 11.9 N-peer session reachable by a player.
        // NOTE, because the v18/v21 notes above can read otherwise: no peer ever sees a version it
        // does not itself speak. OnHandshake refuses `ver != ProtocolVersion` outright with
        // RejectVersion before a single snapshot is exchanged, and the build-hash equality check
        // right behind it would refuse anyway. So a bump here is BOOKKEEPING -- it names the wire
        // layout for the next reader; it is not a compatibility measure, and there is no
        // graceful-degradation path to reason about in either direction.
        // v26 (card ed32efe1): EvRespawn grows a [rewardLevel:1] byte. The respawn pop's reward
        // Blast is not itself replicated, so the two peers' copies used to match by construction
        // (both a constant); now that the level is the owner's "2" powerup level, an observer
        // re-deriving it from its own ~10 Hz MsgHudState view could latch a stale one -- and the
        // blast kills, so on a host observer that is a gameplay difference, not a cosmetic one.
        // A WIDENED EXISTING EVENT, so unlike v14/v15/v17/v25 an unbumped peer really would
        // misread it (the decoder's length check would refuse the short frame and drop the whole
        // announcement); the note below still applies -- no such peer can exist.
        public const byte ProtocolVersion = 26;
        public const float InterpDelayMs = 100f;
        // Card 6fb406bc (Stage 11.11): the cushion for a channel whose frames take the star's
        // SECOND hop (client -> host -> client, ShipFlagRelayed). The relay adds
        // ~half(RTT_A+RTT_B) plus up to one 33 ms re-send beat of arrival jitter on top of the
        // one-hop path the 100 ms above was tuned for, so a relayed channel rendered at 100 ms
        // lives on the extrapolation cap instead of the buffer. 150 ms is the 4p design doc's
        // own budget ("~150ms, or derived from observed arrival jitter"); fixed rather than
        // jitter-derived so the already-tuned 2-peer feel cannot drift.
        public const float RelayedInterpDelayMs = 150f;

        // ~30 Hz ship stream. INTERNAL because NetFireTest scripts its packet cadence against it.
        internal const long StreamIntervalMs = 33;
        // Per-slot HUD state changes far slower than a ship pose (a combo tick, a bar creeping up),
        // and it is a readout rather than something the sim reads back, so a third of the ship
        // rate is plenty and keeps the added stream traffic under ~400 B/s.
        private const long HudIntervalMs = 100;      // ~10 Hz per-slot HUD state
        internal const long SnapshotIntervalMs = 60;  // ~16.7 Hz world snapshot (host)

        // Ceiling on a believable observed velocity, design px/ms.
        //
        // IT IS A DIAGNOSTIC THRESHOLD, NOT A GUARD (card e79bb994). Card 8dabe812 used this as a
        // plausibility CAP -- a sample above it had its velocity refused -- which was an estimator
        // with a threshold, kept honest only by the measured gap below. It is now purely the
        // trip-wire for a reposition site that forgot to call NetNoteTeleport
        // (NetSession.NoteIfUnmarkedTeleport), and nothing it does can change what goes on the
        // wire. That inverts its risk: as a cap, a value set too LOW silently clipped a genuinely
        // fast enemy and recreated the very stutter it existed to remove; as a diagnostic, the
        // worst it can do is print a spurious line. The measured separation is still what makes
        // it useful, so the reasoning is kept verbatim:
        //
        // DERIVED FROM A MEASURED GAP, via `eaNetVelScan` (Compat/Net/NetVelocityScan) over
        // Level1/2/3 at Medium and Inzane, ~8 sim-minutes each. That tool reports each replicable
        // type's SUSTAINED speed -- one whose neighbouring sample is at least half as fast, i.e. a
        // plateau rather than a one-interval spike -- which is what separates flight from a
        // reposition. Highest sustained readings for types that do NOT reposition, px/ms:
        //     MarsBoss 2.404 (its entry PowerCurve) · Spider 1.237 · SweepUFO 0.385 ·
        //     EvilBullet 0.240 · PlasmaBall 0.600 · BrainBoss 0.101 · JunkBoss 0.075
        // and the fastest DECLARED speed anywhere in the set is EvilSkull's launched MaxSpeed 2.5.
        // So genuine motion tops out at ~2.5 px/ms.
        //
        // The repositions sit an order of magnitude up, and each is a code fact rather than a
        // reading to explain away: SpiderBoss's fly-by park measures 42-57 (THE CARD), EvilSkull
        // respawns at a random point on screen and measures 11.6, and a `wrapping` Braineroid
        // teleports across the screen and measures 13.5.
        //
        // 5.0 is the log midpoint of 2.5..11.6: 2.0x above the fastest real mover and 2.3x below
        // the slowest reposition. Well separated in both directions and tuned to neither.
        // (Those three repositions are all MARKED now, so in a healthy build the diagnostic never
        // sees them at all -- the right-hand side of the gap is what an UNMARKED one would land
        // in, and is why the threshold is sited where it is rather than just above 2.5.)
        //
        // THE SEPARATION IS THE POINT, NOT THE PRECISION -- gameplay RNG is unseeded, so a soak
        // samples the MarsBoss entry curve wherever it happens to land and three runs of the same
        // rig read 1.777 / 2.013 / 2.404. Any cap chosen inside that band would be a coin flip
        // (measured: a trial cap of 2.0 passed the probe on one run and failed it on the next),
        // which is exactly why this sits a factor of two clear of the whole band rather than just
        // above the largest reading. Do not "tighten" it toward the measurements.
        //
        // A new fast type RAISES this; it never gets clipped. `eaNetVelScan` re-measures the
        // left-hand side and reports PASS/FAIL against this constant, and
        // tools/headless/probes/net_velguard.txt runs it -- that probe IS the negative test the
        // cap rests on. Two things it caught, both of which would have shipped a wrong number:
        // a first cut at 3.0 clipped MarsBoss's own arrival, and the scan's own sampler was
        // differencing across POOL RECYCLES, which reported an EvilBullet whose declared speed is
        // 0.24 px/ms at a sustained 14.9.
        //
        // BLAST RADIUS BEYOND THE NET LAYER (card c1d783ad): the AI's swept-path seam
        // (AlienDrawableGameComponent.AiSweptMaxSpeedPxPerMs) consumes this same constant, and
        // there it IS a guard -- a value set too low deletes a genuinely fast mover's directional
        // repellent instead of merely printing a line. `logic_probe`'s ProbeAiSweptPathGuard
        // asserts the measured SEPARATION above (>= 2x the fastest real mover, <= half the
        // slowest reposition), so a retune that leaves that band fails there rather than quietly
        // changing how the bot flies.
        internal const float MaxObservedSpeedPxPerMs = 5.0f;
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
        private const float RenderClockSnapMs = 250f;
        // Pop detection: a rendered step larger than any plausible ship motion over the same
        // real time (PlayerShip.MaxSpeed is 0.33 px/ms; x2 margin + slack for frame jitter).
        private const float ShipMaxSpeedPxPerMs = 0.33f;
        private const float PopSlackPx = 3f;

        private const int SnapshotMaxEntries = 16;   // <= ~500B/packet within extras budget
        private const int SnapshotScratchBytes = NetProtocol.SnapshotHeaderBytes + SnapshotMaxEntries * 64;
        private const int ExtraScratchBytes = 64;
        private const int DeathRecordCap = 512;

        public static bool Active { get; private set; }

        // FACADE over the peer channels (card b2828be8; a real SET since card 87242257): the
        // static API is unchanged for its ~60 external call sites while the state behind it
        // lives per-peer. The boolean facades are AGGREGATES now -- "a peer is up", "someone
        // remote holds a pause" -- which is exactly what every external caller was asking.
        public static bool PeerUp => AnyPeerUp();

        public static bool IsHost => Active && isHost;
        public static bool IsClient => Active && !isHost;

        // Client sim-split: the join peer never runs the level script / spawners (GameScene
        // checks this before eventList.Update) and never lets game code add replicable
        // types to the world (ComponentBin.Add checks SuppressWorldSpawn).
        public static bool SuppressLevelScript => IsClient;

        // Card 8a7772d6: the HOST's level script is holding its player spawn, so we hold ours
        // too and both ships fly in together at the end of the cinematic.
        //
        // FAIL-OPEN BY CONSTRUCTION, and every clause here is part of that. It is false with no
        // session, on the host, and while the peer is not up -- so a dropped peer, a torn-down
        // session or a build that never sends the bit all leave the joiner spawning normally.
        // The only thing that can hold a ship back is a live host actively saying so, which is
        // what keeps the worst case "P2 flies around during the intro" (the pre-card bug) rather
        // than "P2 never gets a ship". (A client's one channel IS its host, so the any-peer scan
        // reads exactly the host's bit -- HandleShipFrame only ever latches it client-side.)
        public static bool PeerHoldsShipSpawn
        {
            get
            {
                if (!IsClient)
                {
                    return false;
                }
                foreach (PeerChannel p in peers.Values)
                {
                    if (p.Up && p.ScriptGate)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

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
        // (NetPuppets.Drive); never freezes the world. An AGGREGATE since card 87242257: the
        // banner (and the parked dead reckoning behind it) is about "is some participant's
        // stream unwell", and the per-peer flags live on the channels.
        internal static bool PeerStalled
        {
            get
            {
                foreach (PeerChannel p in peers.Values)
                {
                    if (p.Stalled)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static bool isHost;
        private static Game game;
        private static Oracle oracle;
        private static ComponentBin bin;
        private static SoundManager sound;
        private static ScoreVisualiser score;
        private static INetTransport transport;
        private static NetImpairment impairment;

        private static readonly Queue<(byte[] data, bool reliable, string from)> rxQueue = new Queue<(byte[], bool, string)>();

        // ---- the peer channels (card b2828be8; a SET since card 87242257) -------------------
        //
        // Keyed by the transport senderId (the 11.7 address). A HOST hub holds up to
        // MaxRemotePeers channels on the star topology plans/4p-online-coop.md fixed; a CLIENT
        // holds exactly one -- its host -- and only ever CREATES it from a handshake frame whose
        // role byte says host, because on a bus medium (BroadcastChannel) a client sees its
        // fellow clients' frames directly and must not bind to one.
        private static readonly Dictionary<string, PeerChannel> peers = new Dictionary<string, PeerChannel>();
        // (Snapshots for set-mutating walks are FRESH lists -- see PeersSnapshot.)
        // Oracle.MaxPlayers minus the host's own seat: more peers than this can never all be
        // seated, so the surplus is refused at the door with an addressed RejectFull.
        private const int MaxRemotePeers = 3;
        // SenderIds refused at the door (over capacity), so each is rejected + named once.
        private static readonly HashSet<string> extraSenderReported = new HashSet<string>();
        // How long a Refused channel keeps soaking up its sender's frames before it is swept;
        // its bye sweeps it earlier. Generous -- the cost of holding one is a dictionary probe.
        private const long RefusedChannelSweepMs = 30000;
        // Byes queued from the transport callback, drained on the game tick (world mutation
        // belongs there). The id is the departing peer's, except WebRtcTransport's terminal
        // whole-link failure, which keeps its legacy "phase:reason" string -- an unrecognized
        // id therefore means "every peer" (INetTransport's documented contract).
        private static readonly Queue<string> byeQueue = new Queue<string>();

        private static bool AnyPeerUp()
        {
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Up)
                {
                    return true;
                }
            }
            return false;
        }

        private static int UpPeerCount()
        {
            int n = 0;
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Up)
                {
                    n++;
                }
            }
            return n;
        }

        // A CLIENT's one channel -- its host -- up or not; null before first contact. Host-side
        // there is no such thing as "the" peer any more; do not call it there.
        private static PeerChannel ClientHostChannel()
        {
            foreach (PeerChannel p in peers.Values)
            {
                return p;
            }
            return null;
        }

        // A FRESH list per call, deliberately: PeerLost / a kick removes channels mid-walk, and
        // a walk can NEST another (Update's liveness loop -> PeerLost -> ReleaseDepartedPeer ->
        // RevertToSinglePlayer snapshots again), so one shared scratch list would be cleared
        // under an enumerator that only escapes by the !Active guards -- a coincidence, not a
        // contract. The cost is a handful of tiny allocations per tick at N <= 4, on a layer
        // that allocates a packet every 33 ms anyway.
        private static List<PeerChannel> PeersSnapshot()
        {
            return new List<PeerChannel>(peers.Values);
        }

        // Resolve a senderId to its channel, creating one where the role's rules allow. A DOWN,
        // unrefused channel re-keys to a fresh identity (a reloaded tab mints a new
        // BroadcastChannel id), so the granted seat and seq bookkeeping survive a reconnect --
        // the pre-11.9 behaviour, kept per channel. (With several down channels the first found
        // is taken: which seat a reconnecting stranger owned is not derivable from a senderId,
        // and the dev ?net= flow this serves runs one joiner per tab.) A Refused channel soaks
        // its sender's frames up silently until swept.
        private static PeerChannel GetOrCreatePeer(string from, byte[] data)
        {
            from = from ?? "";
            if (peers.TryGetValue(from, out PeerChannel existing))
            {
                return existing.Refused ? null : existing;
            }
            if (!isHost)
            {
                // The client side of the star: exactly one channel, created only by a
                // host-role Hello/Welcome (data[2] is the role byte in both layouts). Fellow
                // clients' hellos and streams on a bus medium are routine, not faults, so the
                // drop is silent. The cost of the filter is that a mid-session reload waits for
                // the hello exchange instead of the host's next stream frame -- one second.
                if (peers.Count > 0)
                {
                    return null;
                }
                bool hostHandshake = (data[0] == NetProtocol.MsgHello || data[0] == NetProtocol.MsgWelcome)
                    && data.Length >= 3 && data[2] != 0;
                if (!hostHandshake)
                {
                    return null;
                }
                PeerChannel host = new PeerChannel(from);
                peers[host.Id] = host;
                return host;
            }
            foreach (PeerChannel candidate in peers.Values)
            {
                if (!candidate.Up && !candidate.Refused)
                {
                    peers.Remove(candidate.Id);
                    candidate.Id = from;
                    peers[candidate.Id] = candidate;
                    return candidate;
                }
            }
            if (peers.Count < MaxRemotePeers)
            {
                PeerChannel p = new PeerChannel(from);
                peers[p.Id] = p;
                return p;
            }
            // Over capacity: refuse at the door, ADDRESSED so the newcomer hears "Game full"
            // instead of hanging on a silent drop. The reject goes out on EVERY knock (its
            // hello retries at 1 Hz until it gets one); only the console line is capped, so a
            // reconnect-looping tab cannot spam the log or grow the set forever.
            transport.SendReliableTo(from, NetProtocol.EncodeReject(NetProtocol.RejectFull));
            if (extraSenderReported.Count < 16 && extraSenderReported.Add(from))
            {
                Console.WriteLine("[net] refusing peer id '" + from + "' -- session already holds "
                    + peers.Count + " peers (cap " + MaxRemotePeers + ")");
            }
            return null;
        }

        // handshake / heartbeat
        private static long sessionStartAt;
        // The BROADCAST hello's clock -- only used while no channel exists at all (it is how a
        // pairing is initiated); once a channel is known its own LastHelloTx paces the
        // addressed retries.
        private static long lastHelloTx;

        // tx
        private static ushort txSeq;
        // (the reliable EVENT seq lives per-recipient on PeerChannel.TxEventSeq since card
        // 87242257 -- see SendEventToPeer; the relayed ship stream keeps one shared seq below,
        // since the extras receive path reads none)
        private static ushort relayShipSeq;
        // The world snapshot's own packet counter (card f5cf7a5c). Monotone for the whole SESSION,
        // not per match: ResetPerMatchState deliberately leaves it alone, exactly as it leaves the
        // tx/rx event sequences alone -- the puppet layer's id maps are cycled there, so no
        // receiver carries a last-applied seq across a level anyway, and a counter that restarts
        // is a counter something can mis-order.
        private static ushort txSnapshotSeq;
        private static long lastStreamTx;
        private static long lastSnapshotTx;
        private static long lastScoreSyncTx;
        // Bandwidth rate baseline (card 6fb406bc): the impairment wrapper's cumulative byte
        // counters as of the previous [net] report, so the line can print a per-interval Bps
        // beside the totals. -1 = no report yet this session (the first line prints rate 0
        // rather than averaging over the whole boot-to-first-report stretch); each rate gates
        // on ITS OWN sentinel so neither can quietly ride the other's lifecycle.
        private static long lastReportTxBytes = -1;
        private static long lastReportRxBytes = -1;
        private static Vector2 lastTxPos = new Vector2(400f, 300f);
        private static float lastTxAim = 4.712389f;
        // THE COUNT ON THE WIRE BELONGS TO THE SLOT, NOT TO THE SHIP, and that distinction is the
        // whole reason these are two fields (card a45b78f6). `PlayerShip.NetShotCount` restarts at
        // 0 with every ship -- it is pooled, so it has to -- while the receiver holds ONE baseline
        // for as long as it holds a puppet. A ship that died at 252 and respawned at 0 is a
        // wrapped delta of 4: inside the catch-up bound, so the peer would spawn four bullets
        // nobody fired. Advancing our own counter by the SHIP's delta, and taking no delta at all
        // across a ship swap, makes what we send monotone per slot however often the ship behind
        // it is replaced -- and leaves NetMaxCatchUpShots to mean only what it says, packet loss.
        // Held across a shipless heartbeat exactly as lastTxPos/lastTxAim are.
        private static byte lastTxShotCount;
        private static PlayerShip lastTxShip;
        private static byte lastTxShipShots;
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
        private static readonly int[][] hudTxLevels = CreateHudScratch(NetProtocol.HudLevelCount);
        private static readonly int[][] hudTxOptions = CreateHudScratch(NetProtocol.HudOptionLayers);
        private static readonly float[] hudTxScores = new float[NetProtocol.MaxSlots];
        private static readonly float[] hudTxComboLeft = new float[NetProtocol.MaxSlots];
        private static readonly int[] hudRxLevels = new int[NetProtocol.HudLevelCount];
        private static readonly int[] hudRxOptions = new int[NetProtocol.HudOptionLayers];

        private static int[][] CreateHudScratch(int width)
        {
            int[][] rows = new int[NetProtocol.MaxSlots][];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new int[width];
            }
            return rows;
        }

        // rx / remote-ship puppet state lives on the PeerChannel's ShipChannels since card
        // b2828be8 -- see PeerChannel.cs. Only the tick clock stays here.
        private static long lastUpdateAt;
        private static float realDtMs;

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

        // (The PEER's primary slot lives on its PeerChannel since card b2828be8.)

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
            // Pickup TYPE, null for anything that is not a powerup (06ac5df2 follow-up; was a
            // bare OneUp bool). Carrying the type is what lets a claim landing AFTER the removal
            // flush still run the full remote-pickup apply -- AddLife for a OneUp, and
            // ApplyRemotePowerup (HUD icon, ship mirror, cue) for every type -- instead of the
            // flushed side of the window silently dropping everything but the extra life.
            public Powerup.PowerupType? Pickup;
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
        // Card 0b8a300b. Our own identity token (hashed), as exchanged in the v6 hello.
        // SELF-REPORTED -- see NetProtocol.HelloBytes; good enough to stop a kicked griefer
        // walking straight back in, and nothing more. (The peer's is on its PeerChannel.)
        private static ulong localPeerId;
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
        // (The remote-pause kick-offer clock, card 0b8a300b, lives on the PeerChannel.)
        // The room string the session opened on -- for the menu lobby that is the 5-char room
        // code, which NetListing needs when it ADOPTS the session's already-open signaling room
        // as a public listing (card 0257f8ba). Cleared with the session.
        private static string sessionRoom = "";
        // Whether the session's transport is the real WebRTC one. A stranger from the public
        // browser can only ever arrive THROUGH eaRtc, so a session on any other transport must
        // never let NetListing advertise while it is up -- the stranger's frames would land on a
        // transport the session is not reading (card 0257f8ba).
        private static bool sessionRtc;
        // Card 0257f8ba, the lobby roster beat (EvLobbyRoster). Client: the last mask received
        // (-1 = none yet). Host: the last mask broadcast, so the beat is edge-triggered.
        private static int lobbyRosterMaskRx = -1;
        private static int lobbyRosterMaskTx = -1;
        private static bool sceneWasUp;       // GameScene edge detection (EvReady / match end)
        private static bool pendingLaunchHas;
        // Validated at the decode boundary, so these hold real enum members and never a raw
        // wire byte -- see the wire-enum contract in NetProtocol.
        private static Levels pendingLaunchLevel;
        private static Settings.DifficultyLevel pendingLaunchDifficulty;

        // A short user-facing notice for the menus ("PLAYER LEFT -- MATCH ENDED", "UPDATE
        // REQUIRED..."). Set on session-ending events, consumed by MenuScene.
        public static string MenuNotice { get; private set; }

        // Every cadence in this file reads through here, so injecting the host's clock (card
        // 25ad0659 step 2a) makes the whole session drivable on a virtual clock in one line.
        private static long NowMs => NetHost.Current.NowMs;

        // ---- per-recipient reliable sends (card 87242257) -----------------------------------
        //
        // Every reliable EVENT is stamped with the RECIPIENT channel's own TxEventSeq: addressed
        // sends (a welcome's grant, a replay catch-up, a relayed event) under one global counter
        // would open a false seqGap at every peer that was not the target, and seqGap=0 is a
        // health bar the verification gate asserts. A broadcast is therefore N addressed sends,
        // each contiguous on its own channel -- byte-identical to the old broadcast at one peer.

        private delegate byte[] EventEncoder(ushort seq);

        // The ADDRESSED CATCH-UP latch: while set, "session-wide" event sends route to this one
        // peer instead. The EvReady handler wraps NetIdRegistry.ReplayLive() +
        // NetReplayCatchUp() with it, so a late joiner's burst reaches only the peer that asked
        // -- re-blasting every already-caught-up peer was the 4p plan's named hazard with the
        // unaddressed replay.
        private static PeerChannel replayTarget;

        private static void SendEventToPeer(PeerChannel p, EventEncoder encode)
        {
            transport.SendReliableTo(p.Id, encode(p.TxEventSeq++));
            metrics.EventsTx++;
        }

        // "Broadcast" an event: one addressed send per UP peer, minus an optional exclusion (the
        // relay's source must not hear its own event back). Honours the replayTarget latch.
        private static void SendEventToSessionPeers(EventEncoder encode, PeerChannel except = null)
        {
            if (replayTarget != null)
            {
                if (replayTarget.Up && replayTarget != except)
                {
                    SendEventToPeer(replayTarget, encode);
                }
                return;
            }
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Up && p != except)
                {
                    SendEventToPeer(p, encode);
                }
            }
        }

        // Host hub: re-emit a client-sent symmetric event to every OTHER client, under each
        // recipient channel's own event seq (card 87242257). No-op for a client, or with nobody
        // else up. Addressed by construction -- the source must never hear its own event back
        // (an echoed pause would freeze the pauser's world under a "remote" pause).
        private static void RelayFromClient(PeerChannel from, EventEncoder encode)
        {
            if (isHost)
            {
                SendEventToSessionPeers(encode, except: from);
            }
        }

        // The stream lane's session send. The host genuinely broadcasts (everyone wants its
        // frames); a client addresses its host, so a bus medium carries no client-to-client
        // stream noise for the other tabs to filter.
        private static void SendStreamToSession(byte[] payload)
        {
            if (!isHost)
            {
                PeerChannel host = ClientHostChannel();
                if (host != null)
                {
                    transport.SendStreamTo(host.Id, payload);
                    return;
                }
            }
            transport.SendStream(payload);
        }

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
            INetTransport t = NewDevTransport();
            switch (DebugFlags.NetRole)
            {
            case NetRole.JipJoin:
                // The joiner half of the two-process JIP rig (card 054947f3): a REAL menu
                // session, exactly as NetLobby builds one -- so it sits at the menu with no
                // world, mirrors the host's EvLaunch through MenuScene.NetLaunchMirror, warms
                // and Initializes the level itself, and its own scene-up edge sends EvReady.
                // That whole first half of an attach is unreachable from ?net=join, which boots
                // straight into a level and so already holds a scene when it pairs.
                StartMenuSession(g, host: false, t, DebugFlags.NetRoom);
                return;
            case NetRole.JipHost:
                // The host half. It must NOT start a session here: a listed game is plain
                // single-player until a stranger actually arrives, and starting early would
                // make every NetSession.Active branch (the hit-stop refusal, the turbo lock,
                // the client sim-split guards) fire for the whole run before the join. So the
                // transport is opened and HELD, and the first inbound frame arms the real
                // StartListedSession -- see TickPendingListedAttach.
                pendingListedTransport = t;
                pendingListedGame = g;
                pendingListedRoom = DebugFlags.NetRoom;
                t.OnData += OnPreSessionData;
                t.Open(DebugFlags.NetRoom);
                Console.WriteLine("[net] listed (join-in-progress) host armed on room "
                    + DebugFlags.NetRoom + " -- single-player until a peer arrives");
                return;
            }
            StartWith(g, DebugFlags.NetRole == NetRole.Host, t, DebugFlags.NetRoom, asMenuSession: false, asListedSession: false);
        }

        // The transport a `?net=` boot builds. ONE definition, because the JipHost role builds one
        // twice -- at the initial arm and at every re-arm -- and a second copy of the expression
        // would silently drop `?rtc` on every match after the first.
        private static INetTransport NewDevTransport()
        {
            return DebugFlags.NetRtc
                ? (INetTransport)new WebRtcTransport(attachOnly: false)
                : new BroadcastChannelTransport();
        }

        // ---- ?net=jiphost: attach a real listed session when a peer shows up -----------------

        private static INetTransport pendingListedTransport;
        private static Game pendingListedGame;
        private static string pendingListedRoom;
        private static bool pendingListedArmed;

        // Queued, never inline: this fires from the transport's own delivery callback, and
        // starting a session enables NetIdRegistry over the live world. World mutation belongs
        // on the game tick, the same rule OnPeerBye follows.
        private static void OnPreSessionData(byte[] data, bool reliable, string from)
        {
            pendingListedArmed = true;
        }

        // Polled from Update() while nothing is Active. THE FRAME THAT ARMED US IS DROPPED, and
        // that is fine rather than sloppy: the only thing a peer sends before pairing is its
        // hello, which repeats at 1 Hz until the pairing settles (see HandleHello's callers), so
        // the next one lands in a session that exists. Buffering it instead would mean
        // re-injecting into a private rx queue from outside StartWith.
        private static void TickPendingListedAttach()
        {
            // RE-ARM after a match ends. A listed host's session ending is not the end of the
            // GAME -- it drops back to single-player and NetListing re-lists it under the same
            // room code, which is exactly how a real host takes a second stranger. Stop() closed
            // the transport, so a fresh one is opened; without this the whole soak gets one join.
            if (pendingListedTransport == null && DebugFlags.NetRole == NetRole.JipHost
                && pendingListedGame != null)
            {
                INetTransport re = NewDevTransport();
                pendingListedTransport = re;
                pendingListedArmed = false;
                re.OnData += OnPreSessionData;
                re.Open(pendingListedRoom);
                Console.WriteLine("[net] listed host re-armed on room " + pendingListedRoom
                    + " -- back to single-player, waiting for the next peer");
            }
            if (!pendingListedArmed || pendingListedTransport == null)
            {
                return;
            }
            pendingListedArmed = false;
            INetTransport t = pendingListedTransport;
            Game g = pendingListedGame;
            string room = pendingListedRoom;
            // The transport reference is cleared (the session owns it now) but the GAME and the
            // ROOM are kept: they are what the re-arm above needs when this match ends.
            pendingListedTransport = null;
            t.OnData -= OnPreSessionData;
            // The REAL entry point NetListing uses, so the attach under test is production's:
            // PeerConnected sends EvLaunch into our running level and EvReady triggers the
            // ReplayLive + NetReplayCatchUp burst. StartWith re-Opens the transport, which is a
            // no-op on one already open.
            StartListedSession(g, t, room);
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
        //
        // `asMenuSession` (card 3b6c12e7) opts back IN, for the one scenario whose SUBJECT is
        // menu-session match-end semantics. It does not dodge the refusal above -- it inherits
        // it -- so such a scenario must boot with `?netallowdebug` beside its `?level=`, which
        // is the production flag for exactly this (a debug-flagged peer in a menu lobby), and
        // must assert its own pairing so a refusal reads as a FAIL rather than a vacuous pass.
        internal static void StartForTest(Game g, bool host, INetTransport t, string room,
            bool asMenuSession = false)
        {
            if (Active)
            {
                return;
            }
            StartWith(g, host, t, room, asMenuSession, asListedSession: false);
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
        internal static bool HasRemotePuppet => AnyPrimaryPuppet();

        // Channel-census read seams for NetNPeerTest -- the peer SET is otherwise observable
        // only through log lines.
        internal static int PeerChannelCount => peers.Count;
        internal static int UpPeerCountNow => UpPeerCount();

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
            sessionRoom = room ?? "";
            sessionRtc = t is WebRtcTransport;
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
            // The senderId is KEPT now (card b2828be8) -- it is the peer-channel key. 11.7 made
            // it real on every transport; this is the card that stops discarding it.
            transport.OnData += (data, reliable, from) => rxQueue.Enqueue((data, reliable, from));
            // Queued, not applied inline: the bye fires from a JS callback, and the menu-
            // session PeerLost now tears down the whole match (world mutation belongs on
            // the game tick).
            transport.OnPeerBye += from => byeQueue.Enqueue(from ?? "");
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
            foreach (PeerChannel p in peers.Values)
            {
                p.Up = false;
                p.RemotePaused = false;
                p.Stalled = false;
            }
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
            // Never leave the world frozen, or the banner up, over a session that no longer
            // exists -- the aggregates just went false with the flags above.
            SyncRemotePauseToScene();
            SyncStallBannerToScene();
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
            // The channels die with the session: a fresh pairing negotiates fresh peers, and
            // the puppet COMPONENTS are torn down by the scene's own purge as always.
            foreach (PeerChannel p in peers.Values)
            {
                p.Primary.Puppet = null;
            }
            ResetFriends();
            peers.Clear();
            remotePauseApplied = false;
            stallBannerApplied = false;
            kickOfferPeer = null;
            replayTarget = null;
            byeQueue.Clear();
            extraSenderReported.Clear();
            localPrimarySlot = HostPrimarySlot;
            joinRequestPending = false;
            dropGrantUsed = false;
            grantsAwaitingStream.Clear();
            localJoinSimDone = 0;
            localJoinSimAt = 0;
            unmarkedTeleportReported.Clear();
            txSeq = 0;
            relayShipSeq = 0;
            txSnapshotSeq = 0;
            lastTxShotCount = 0;
            lastTxShip = null;   // also drops a stale ship reference from the previous session
            lastTxShipShots = 0;
            lastStreamTx = 0;
            lastSnapshotTx = 0;
            lastScoreSyncTx = 0;
            lastReportTxBytes = -1;
            lastReportRxBytes = -1;
            lastHudTx = 0;
            lastHelloTx = 0;
            lastUpdateAt = 0;
            killNotes.Clear();
            killNoteOrder.Clear();
            recentDeaths.Clear();
            recentDeathOrder.Clear();
            pendingLaunchHas = false;
            listedSession = false;
            sessionRoom = "";
            sessionRtc = false;
            lobbyRosterMaskRx = -1;
            lobbyRosterMaskTx = -1;
            // Card 3b6c12e7. Both are per-MATCH latches; a session that ends outright must not
            // leave the menus about to enter a lobby for a pairing that no longer exists.
            levelFinishedCleanly = false;
            pendingLobbyReturn = false;
            pendingStopAt = 0;
            pendingStopNotice = null;
            pendingStopReason = "pairing rejected";
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

        // Test/diagnostic seam (card 72143c11): park a session-ending notice at the menus with
        // no peer, no session and no transport. Every production writer of MenuNotice is inside
        // Stop(), which needs a live session -- so the ONLY offline way to reach the menus'
        // notice path (and the two-live-menus bug it used to leave behind) is to set it here.
        // Reached through DebugInput.NetNotice. It starts and stops nothing -- but it does
        // CLOBBER any notice already pending, so do not fire it during a real session teardown
        // and expect the real reason to survive.
        internal static void SetMenuNoticeForTest(string notice)
        {
            MenuNotice = notice;
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeLaunchEvent(seq, (byte)level, (byte)difficulty));
            Console.WriteLine("[net] tx launch level=" + level + " difficulty=" + difficulty);
        }

        // ---- lobby roster + capacity seams (card 0257f8ba) ------------------------------

        // The room string this session opened on. For a menu-lobby session it is the 5-char
        // room code; NetListing reads it when it adopts the session's signaling room as a
        // public listing (there is no 'code' phase to learn it from on that path).
        internal static string SessionRoomCode => Active ? sessionRoom : "";

        // Can a stranger from the public game browser still be taken INTO this session?
        // The 11.10 relaxation of NetListing's old `!NetSession.Active` term: a HOST session on
        // the real WebRTC transport (menu lobby or listed/JIP -- never a `?net=` dev shape,
        // whose eaRtc room does not feed its transport) accepts late arrivals through the same
        // room; the free-seat half of the question stays NetListing's own Players() term.
        public static bool HostOpenToJoinInProgress => Active && isHost && sessionRtc
            && (menuSession || listedSession);

        // Which roster seats are taken, for the lobby panels (bit i = oracle slot i). The host
        // derives it live -- its own seat always counts, plus everything seated in the oracle
        // (granted Remote primaries at the menu; every seat once a level is up). A client reads
        // the host's EvLobbyRoster beat; -1 = no beat yet (the panel then keeps the pre-card
        // wording rather than inventing a roster).
        public static int LobbyRosterMask => !Active ? -1
            : isHost ? ComputeLobbyRosterMask() : lobbyRosterMaskRx;

        private static int ComputeLobbyRosterMask()
        {
            // Channels AND oracle, unioned: the oracle covers everything seated in a live level
            // (couch players included) but GameScene.Terminate wipes it (card ee96ea61), so at
            // the post-level lobby the up channels' granted seats are the only record of who is
            // still connected -- oracle alone would show a full room as empty there.
            return NetProtocol.SlotBit(HostPrimarySlot)
                | UpPeerPrimarySlotsMask()
                | OccupiedMask(oracle, exclude: -1);
        }

        // Edge-triggered broadcast of the mask above (host, menu-lobby sessions only -- a JIP
        // joiner lands mid-level, where the panels this feeds are never on screen; couch seats
        // taken mid-level still move the mask, which is what keeps the post-level lobby's
        // roster honest). Newcomers additionally get an ADDRESSED copy at PeerConnected, so a
        // reconnect that changes nothing still learns the current roster.
        private static void TickLobbyRoster()
        {
            if (!isHost || !menuSession)
            {
                return;
            }
            int mask = ComputeLobbyRosterMask();
            if (mask == lobbyRosterMaskTx)
            {
                return;
            }
            lobbyRosterMaskTx = mask;
            SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvLobbyRoster, (byte)mask));
        }

        // Ticked once per game tick from Game1.UpdateInner. Cadence runs on REAL time
        // (TickCount64), so turbo/slowmo/hit-stop never starve the stream or heartbeats.
        public static void Update()
        {
            if (!Active)
            {
                // ?net=jiphost holds an open transport with no session; this is the tick that
                // turns a peer's arrival into the real StartListedSession.
                TickPendingListedAttach();
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
            DrainPeerByes();
            if (!Active)
            {
                return; // a bye ended the session (the host went away, or the last peer did)
            }
            UpdateSceneEdges();
            if (!Active)
            {
                return; // the local match ended -- Stop() ran
            }
            SweepRefusedChannels(now);
            // Initiate: while NO channel exists the hello is a broadcast -- it is how a pairing
            // starts (a client knocking on its host, a dev host knocking first). Once a channel
            // is known, its handshake retries ADDRESSED below, each on its own clock, until THAT
            // peer's slot exchange settles -- whoever hears the other's hello first goes up
            // immediately, and a client that fell silent there would never be answered with its
            // slot grant. An unset PrimarySlot means "not settled" for both roles (the host sets
            // it when it reserves the joiner's seat, the client when it adopts its own).
            if (peers.Count == 0 && now - lastHelloTx >= HelloIntervalMs)
            {
                lastHelloTx = now;
                transport.SendReliable(NetProtocol.EncodeHello(ProtocolVersion, isHost, localBuildHash, LocalHelloFlags(),
                    NetProtocol.SlotNone, localPeerId, LocalBlockedSlots()));
            }
            // The paused widening covers ANY held pause, not just this channel's: a world frozen
            // by anyone's pause has every participant's tab liable to background-throttle, so
            // every link takes the wide backstop while the freeze lasts (card 87242257).
            bool anyPaused = localPaused || AnyPeerPaused();
            foreach (PeerChannel p in PeersSnapshot())
            {
                if (p.Refused)
                {
                    continue;
                }
                AdvanceShipClock(p.Primary);
                if ((!p.Up || p.PrimarySlot == NetProtocol.SlotNone) && now - p.LastHelloTx >= HelloIntervalMs)
                {
                    p.LastHelloTx = now;
                    transport.SendReliableTo(p.Id, NetProtocol.EncodeHello(ProtocolVersion, isHost, localBuildHash, LocalHelloFlags(),
                        isHost ? p.PrimarySlot : NetProtocol.SlotNone, localPeerId, LocalBlockedSlots()));
                }
                if (!p.Up)
                {
                    continue;
                }
                long quiet = now - p.LastRxStreamAt;
                if (quiet > (anyPaused ? PausedPeerTimeoutMs : PeerTimeoutMs + PeerGraceMs))
                {
                    // PER-PEER verdict (card 87242257): for a host this is one client departing
                    // -- seats free, play continues; only a client losing its HOST (or the last
                    // client leaving a lobby with no level up) ends anything.
                    PeerLost(p, "timeout");
                }
                else
                {
                    // Grace window (card 11.5): past PeerStallMs the link is visibly unwell,
                    // but the verdict is deferred by PeerGraceMs and we keep streaming
                    // throughout, so a wifi hiccup or a backgrounded tab's burst-send recovers
                    // instead of ending the run. A PAUSED world is an explicit "here but
                    // frozen" state whose own overlay already says so -- no stall banner on
                    // top of it, and the much wider backstop still applies.
                    SetPeerStalled(p, !anyPaused && quiet > PeerStallMs, recovered: quiet <= PeerStallMs);
                }
                if (!Active)
                {
                    return; // the loss above ended the session
                }
            }
            // Bandwidth accounting (card 6fb406bc): an unaddressed send really goes out once
            // per connected peer at the JS/socket layer, so the byte counters multiply
            // broadcasts by the live up-peer count. Refreshed every tick, UNCONDITIONALLY --
            // inside the AnyPeerUp() block it went stale the moment the last peer left, and the
            // initiate hello (a broadcast sent precisely while no peer is up) was then counted
            // at the departed roster's fanout forever (review finding).
            impairment.BroadcastFanout = UpPeerCount();
            if (AnyPeerUp())
            {
                if (now - lastStreamTx >= StreamIntervalMs)
                {
                    SendShipState(now);
                    // Couch players + host AI friends ride the same cadence, both directions --
                    // and the host's hub duty: every client's ships re-streamed to the others.
                    SendFriendStates(now);
                    if (isHost)
                    {
                        RelayPeerShips(now);
                    }
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
                TickLocalJoinSim(now);
                TickLobbyRoster();
                if (isHost)
                {
                    ExpireUnclaimedGrants(now);
                    foreach (PeerChannel p in PeersSnapshot())
                    {
                        if (p.Up)
                        {
                            TickKickOffer(p, now);
                        }
                    }
                }
            }
            // ONE evaluation for the whole peer sweep (card c1cdd3e5). It is peer-invariant --
            // it asks about OUR world, not about any channel -- and nothing this loop does can
            // change it: `FindLocalShip` matches a LOCALLY-OWNED ship in `localPrimarySlot`,
            // while SpawnPuppet/SpawnFriend only ever seat Remote/RemoteFriend devices in a
            // PEER's slot, and neither raises a summon. Hoisted because it walks
            // `game.Components` in the miss case and both ticks below need the same answer.
            bool worldTakesPuppets = WorldTakesPuppets();
            foreach (PeerChannel p in PeersSnapshot())
            {
                if (p.Refused)
                {
                    continue;
                }
                ManagePuppet(p, worldTakesPuppets);
                // spawn/interpolate/expire the peer's couch + AI-friend puppets
                TickFriends(p, worldTakesPuppets);
            }
            if (now - lastMetricsAt >= MetricsIntervalMs)
            {
                long sinceMs = now - lastMetricsAt;
                lastMetricsAt = now;
                // Bandwidth (card 6fb406bc): totals off the impairment wrapper (the one choke
                // point every session byte passes), rate over the interval since the LAST report
                // -- the first report of a session prints 0 Bps rather than a boot-stretch mean.
                metrics.TxBytes = impairment.TxStreamBytes + impairment.TxReliableBytes;
                metrics.RxBytes = impairment.RxStreamBytes + impairment.RxReliableBytes;
                metrics.TxBps = lastReportTxBytes >= 0 && sinceMs > 0
                    ? (metrics.TxBytes - lastReportTxBytes) * 1000f / sinceMs : 0f;
                metrics.RxBps = lastReportRxBytes >= 0 && sinceMs > 0
                    ? (metrics.RxBytes - lastReportRxBytes) * 1000f / sinceMs : 0f;
                lastReportTxBytes = metrics.TxBytes;
                lastReportRxBytes = metrics.RxBytes;
                metrics.ImpDropped = impairment.Dropped;
                metrics.ImpHeld = impairment.HeldCount;
                metrics.ImpLagMs = impairment.LagMs;
                metrics.ImpLossPct = impairment.LossPct;
                metrics.ImpJitterMs = impairment.JitterMs;
                int liveCount = isHost ? NetIdRegistry.LiveCount : NetPuppets.LiveCount;
                Console.WriteLine(metrics.Report(isHost, PeerUp, liveCount, SnapshotTurnMs(liveCount),
                    FindLocalShip() != null, AnyPrimaryPuppet(), RosterReport()));
                // The per-peer half (card 87242257), only once the session actually holds more
                // than one channel -- so every existing 2-peer probe's log is byte-identical.
                if (peers.Count > 1)
                {
                    Console.WriteLine(PeersReport(now));
                }
            }
        }

        private static bool AnyPrimaryPuppet()
        {
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Primary.Puppet != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AnyPeerPaused()
        {
            foreach (PeerChannel p in peers.Values)
            {
                if (p.RemotePaused)
                {
                    return true;
                }
            }
            return false;
        }

        // The per-peer [net] metrics line (card 87242257): one entry per channel -- identity,
        // ladder state, granted seat, stream quiet, primary buffer depth, extras population and
        // the per-recipient event seqs. Parseable like the rest of the [net] family; ';' between
        // peers, ',' inside one.
        private static string PeersReport(long now)
        {
            string s = "";
            foreach (PeerChannel p in peers.Values)
            {
                string state = p.Refused ? "refused" : !p.Up ? "down" : p.Stalled ? "stalled" : p.RemotePaused ? "paused" : "up";
                s += (s.Length > 0 ? "; " : "")
                    + p.Id + "=" + state
                    + ",pri=" + (p.PrimarySlot == NetProtocol.SlotNone ? "-" : p.PrimarySlot.ToString())
                    + ",quiet=" + (p.LastRxStreamAt == 0 ? -1 : now - p.LastRxStreamAt) + "ms"
                    + ",buf=" + (p.Primary.Buffer.HasSamples && !double.IsNaN(p.Primary.RenderMs)
                        ? ((int)(p.Primary.Buffer.NewestMs - p.Primary.RenderMs)).ToString() : "-") + "ms"
                    + ",extras=" + p.Extras.Count
                    + ",evTx=" + p.TxEventSeq + ",evRx=" + (p.LastRxEventSeq < 0 ? "-" : p.LastRxEventSeq.ToString());
            }
            return "[netpeers] n=" + peers.Count + " " + (s.Length > 0 ? s : "-");
        }

        private static void SweepRefusedChannels(long now)
        {
            foreach (PeerChannel p in PeersSnapshot())
            {
                if (p.Refused && now >= p.RemoveAtMs)
                {
                    peers.Remove(p.Id);
                }
            }
        }

        // Byes are queued from the transport callback and spent here, on the game tick. The id
        // names the departing peer; an UNRECOGNIZED id is WebRtcTransport's terminal whole-link
        // "phase:reason" string, which means EVERY peer at once (INetTransport's contract) --
        // so that reading walks the whole set.
        private static void DrainPeerByes()
        {
            while (byeQueue.Count > 0)
            {
                string from = byeQueue.Dequeue();
                if (peers.TryGetValue(from, out PeerChannel byId))
                {
                    if (byId.Refused)
                    {
                        peers.Remove(byId.Id); // a refused/kicked sender finally went away
                    }
                    else
                    {
                        PeerLost(byId, "bye");
                    }
                }
                else
                {
                    foreach (PeerChannel p in PeersSnapshot())
                    {
                        if (!p.Refused)
                        {
                            PeerLost(p, "bye");
                        }
                        if (!Active)
                        {
                            break;
                        }
                    }
                }
                if (!Active)
                {
                    byeQueue.Clear();
                    return;
                }
            }
        }

        private static byte LocalHelloFlags()
        {
            // ?netjip is a deliberate two-window JIP test bypass: a host booted with ?level=
            // has DebugFlags.Active, which a clean menu-session joiner would reject -- so a
            // ?netjip boot presents as clean. Every real listed host has no debug flags anyway
            // (NetListing's eligibility refuses them unless ?netjip is set).
            // ?netallowdebug is the same bypass for the MENU-LOBBY route -- the rig for driving
            // the join paths by hand with `?aiplayer` flying. It does NOT touch NetListing, so
            // neither flag can advertise a flagged game the other would have hidden.
            bool presentClean = NetHost.Current.NetJip || NetHost.Current.NetAllowDebug;
            return (NetHost.Current.DebugActive && !presentClean) ? NetProtocol.HelloFlagDebugActive : (byte)0;
        }

        // FINISHING A LEVEL RETURNS A MENU-LOBBY PAIRING TO ITS LOBBY WITH THE SESSION ALIVE
        // (card 3b6c12e7). Latched by GameScene.Terminate off the terminate MODE, immediately
        // before it nulls NetActiveScene -- the scene-down edge below is the only teardown
        // trigger for a normal level end and by the time it fires the scene is already gone, so
        // it cannot ask NetEndingNormally (and _state alone would also accept a quit taken
        // during the victory choreography). Spent by that edge, so it can never survive into a
        // later level.
        private static bool levelFinishedCleanly;

        internal static void OnLevelFinished()
        {
            if (Active)
            {
                levelFinishedCleanly = true;
            }
        }

        // Set when a finished level left the session standing: the menus poll it once and enter
        // the lobby instead of the main menu. Take-once, so a second MenuScene.Initialize (the
        // credits -> menu hop, a later return) cannot re-enter the lobby off a stale flag.
        public static bool TakeLobbyReturn()
        {
            bool r = pendingLobbyReturn;
            pendingLobbyReturn = false;
            return r;
        }

        internal static bool PendingLobbyReturn => pendingLobbyReturn;

        private static bool pendingLobbyReturn;

        // GameScene lifecycle edges (card 11.4): the client announces its scene coming up
        // (EvReady -> the host replays the live world into it, covering a client that
        // finished its level warm after the host started spawning); a scene going DOWN in
        // a menu session means the local match ended (quit, game over, drop) -- so tell the
        // peer and wind the session down. Since card 3b6c12e7 a level FINISHED is the
        // exception: the pairing survives and both peers walk back to the lobby.
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
                    SendEventToSessionPeers(seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvReady));
                }
                return;
            }
            bool finished = levelFinishedCleanly;
            levelFinishedCleanly = false;
            if (finished && menuSession)
            {
                // Card 3b6c12e7: the host picks the next level and the pair keeps playing. No
                // EvLeave and no Stop -- each peer independently reaches this off the EvVictory
                // both already ran, so nothing new crosses the wire. listedSession is excluded
                // deliberately: a join-in-progress host has no lobby to return to, so its level
                // ending is still a match end (and its joiner sees the EvLeave as before).
                Console.WriteLine("[net] level finished -- session kept alive, returning to the lobby");
                ResetPerMatchState();
                pendingLobbyReturn = true;
                return;
            }
            if (menuSession || listedSession)
            {
                // Our own level ended / we quit: tell the peers and leave. Under the 11.9
                // match-end policy the two roles diverge at the RECEIVER, not here: a HOST's
                // EvLeave ends the match for every client (host leaves -> match over, no
                // migration), while a CLIENT's EvLeave only frees its seats on the host and
                // play continues for everyone else.
                if (PeerUp)
                {
                    SendEventToSessionPeers(seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvLeave));
                }
                Stop("match ended");
            }
        }

        // The WORLD-scoped half of ResetPerSessionState, for a session that outlives its level
        // (card 3b6c12e7). Everything here describes the level that just ended and would be
        // stale -- or actively wrong -- in the next one: the interpolation buffer would place
        // the next level's puppet at the last level's final position, and the id maps would
        // collide with the next level's ids.
        //
        // DELIBERATELY KEPT, because they describe the PAIRING rather than the match: the
        // transport, PeerUp/menuSession, the peer identity and block list, the roster grants
        // (the same two peers keep their seats), the monotone tx/rx event sequences, and
        // pendingLaunch* (the host may already have picked the next level).
        //
        // A URL `?net=` SESSION IS THE ONE OTHER SHAPE THAT OUTLIVES A LEVEL, and it deliberately
        // does NOT come through here -- the scene-down branch below only reaches menu/listed
        // sessions, so a dev rig keeps its buffers and id maps across a level exactly as it always
        // has. That is pre-existing and rig-only, and changing it would alter what those two-tab
        // recipes measure; it is called out so the omission reads as a decision rather than a
        // missed call site.
        private static void ResetPerMatchState()
        {
            localPaused = false;
            rxQueue.Clear();
            // Each channel's world-scoped half (buffers, puppets, alive latch, script gate, the
            // extras). Up / PrimarySlot / PeerId / the event seqs survive -- they describe the
            // PAIRING, exactly the split the comment above spells out.
            foreach (PeerChannel p in peers.Values)
            {
                p.Stalled = false;
                p.ResetMatchState();
            }
            SyncStallBannerToScene();
            SyncRemotePauseToScene();
            // The TX half of the extra-ship stream is session-level scratch; reset it with the
            // channels so the next level's counters start clean, as ResetFriends always did.
            ResetFriendTx();
            unmarkedTeleportReported.Clear();
            lastTxShotCount = 0;
            lastTxShip = null;
            lastTxShipShots = 0;
            killNotes.Clear();
            killNoteOrder.Clear();
            recentDeaths.Clear();
            recentDeathOrder.Clear();
            // Drop the dead level's id maps exactly as a Stop()/Start() pair would, then
            // re-arm for the next one. NetIdRegistry keeps its `next` counter across the
            // cycle by design, so the next level's ids cannot collide with this one's.
            if (isHost)
            {
                NetIdRegistry.Disable(game);
                NetIdRegistry.Enable(game);
            }
            else
            {
                NetPuppets.Disable();
                NetPuppets.Enable(game);
            }
        }

        // ---- local ship -> wire ---------------------------------------------------------

        // THE SHIP STREAM CARRIES A CUMULATIVE SHOT COUNT, NOT A FIRE INTENT (card a45b78f6).
        //
        // WHAT IT REPLACED, because the failure mode is the instructive part. `firing` used to be
        // a LEVEL on the wire, sampled at packet rate, and the peer re-fired from it through a
        // cadence gate it set from the SAME packet -- so one tap spawned `1 + floor(window /
        // period)` bullets over there and the sender had to hold the flag for a window narrower
        // than one cadence period to keep that at 1 (card a5c2a39b's `FiringHoldMsFor`, `P/2`).
        // That bound held, but two things could not be fixed inside it: at the top fire rates the
        // window is one packet wide, so a stream-lane DROP silently lost that bullet on the peer;
        // and the intent was stamped BEFORE the owner's own gate, so two taps inside one cadence
        // period were one bullet for the owner and two for the peer.
        //
        // A COUNT HAS NEITHER PROBLEM, and both for the same reason: it is cumulative, so the
        // wire says WHAT HAPPENED rather than what is happening now. A dropped, reordered or late
        // packet costs nothing (the next one carries the total); a delta is only ever produced by
        // a bullet the owner really spawned, because the increment sits beside that bullet inside
        // FireAt's gate. The receiver spends the delta through PlayerShip's own shot spawn with
        // NO second gate -- see NetApplyRemoteState -- so there is no rate for either side to
        // re-derive and nothing left to get wrong at 100 Hz, at 18 shots/sec, or under loss.
        //
        // SendShipState therefore streams `local.NetShotCount` verbatim and holds no window at
        // all. NetSession.Friends.cs does the same for couch players and AI friends.
        //
        // Legacy note for anyone reading a `firing` in an old branch: the flag bit is gone from
        // NetProtocol (ShipFlagFiring), ShipSample carries ShotCount instead, and there is no
        // sender-side timing left on this path -- which is also what let eaNetFire grow a real
        // SENDER leg, the old design's stamp being unreachable without a clock seam on FireAt.
        // Fold a ship's own shot count into the SLOT's wire counter. A change of ship (a respawn,
        // or a pooled instance re-seated) contributes nothing: its counter is a fresh sequence,
        // and any bullets it fired before we first saw it were fired into a life the peer's puppet
        // was never watching. See the lastTxShotCount comment for why per-slot is the requirement.
        private static byte AdvanceTxShots(PlayerShip ship, ref PlayerShip lastShip,
            ref byte lastShipShots, ref byte wireCount)
        {
            bool sameShip = ReferenceEquals(ship, lastShip);
            lastShip = ship;
            return AdvanceTxShotCount(sameShip, ship.NetShotCount, ref lastShipShots, ref wireCount);
        }

        // The arithmetic of the above, with the ship identity already resolved to a bool -- so
        // eaNetFire can drive a ship swap and a counter restart without needing two live ships.
        internal static byte AdvanceTxShotCount(bool sameShip, byte shipShots, ref byte lastShipShots,
            ref byte wireCount)
        {
            if (!sameShip)
            {
                lastShipShots = shipShots;
            }
            wireCount = (byte)(wireCount + (byte)(shipShots - lastShipShots));
            lastShipShots = shipShots;
            return wireCount;
        }

        private static void SendShipState(long now)
        {
            lastStreamTx = now;
            PlayerShip local = FindLocalShip();
            bool alive = local != null;
            Vector2 pos = lastTxPos;
            Vector2 vel = Vector2.Zero;
            float aim = lastTxAim;
            byte shotCount = lastTxShotCount;
            int shots = 8;
            float bulletLife = 450f;
            // The roll rings travel beside the count they describe (card 950bb70a). A dead
            // ship's packet carries zeros: its count has not moved, so the receiver owes no
            // shots and never reads them.
            byte asplodeBits = 0;
            byte bounceBits = 0;
            if (alive)
            {
                pos = local.GetPosition();
                vel = local.NetVelocity;
                // A ship that has never fired reports NetLastFireAim's own seed (facing up), so
                // there is no "has it fired yet" test left to get wrong.
                aim = local.NetLastFireAim;
                shotCount = AdvanceTxShots(local, ref lastTxShip, ref lastTxShipShots, ref lastTxShotCount);
                shots = local.NetShotsPerSec;
                bulletLife = local.NetBulletLife;
                asplodeBits = local.NetAsplodeBits;
                bounceBits = local.NetBounceBits;
                lastTxPos = pos;
                lastTxAim = aim;
                lastTxShotCount = shotCount;
            }
            // Card 8a7772d6: "my level script is holding the player spawn". Only meaningful
            // from the HOST -- a client's own script never runs, and NetScriptHoldsShipSpawn
            // reads through the client-side override, so what a CLIENT puts here is an echo of
            // the host's own bit rather than a report about itself. Harmless because
            // HandleShipFrame latches it only from a non-host peer; encoded unconditionally
            // rather than gated on the role so there is one expression, not two.
            bool scriptGate = NetScene.Current?.NetScriptHoldsShipSpawn ?? false;
            // Slot-keyed like every ship frame since v23; the PRIMARY flag is what marks this as
            // the heartbeat stream, so the receiver's routing never depends on the slot-settle
            // race (the slot here can change mid-handshake on a re-grant).
            SendStreamToSession(NetProtocol.EncodeShipState(localPrimarySlot, primary: true, txSeq++, (uint)(now - sessionStartAt), pos, vel, aim, alive, shotCount, shots, bulletLife, scriptGate, asplodeBits, bounceBits));
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
                score.NetReadHudState(slot, hudTxLevels[count], out int combo, out float comboLeft, out Powerup.PowerupType? activeType, out float progress);
                hudTxSlots[count] = (byte)slot;
                hudTxCombos[count] = combo;
                // The combo TIMER's remaining fraction (v23, folding card a5b1e941): the observer
                // parks its own timer here instead of refreshing it to full, so the two screens'
                // combo readouts fade in phase and lapse together.
                hudTxComboLeft[count] = comboLeft;
                // No active powerup goes on the wire as the HudPowerupNone sentinel; the
                // receiver folds it back into the same null (NetProtocol.TryDecodeHudState).
                hudTxTypes[count] = activeType.HasValue ? (byte)activeType.Value : NetProtocol.HudPowerupNone;
                hudTxProgress[count] = progress;
                // The Option population is SHIP state, unlike everything else in this entry, which
                // is roster state that outlives a death (card c5228350). With no ship we report
                // 0/0, which is indistinguishable from a live ship flying none -- and that is
                // correct either way, since an Option dies with its owner
                // (Option.OnComponentRemoved). The cost is that a dead owner's 0/0 can reach the
                // observer before the puppet's own death does, so the orbit blinks out up to one
                // interpolation delay early.
                PlayerShip owner = FindShipForSlot(slot);
                for (int layer = 0; layer < NetProtocol.HudOptionLayers; layer++)
                {
                    hudTxOptions[count][layer] = owner != null ? owner.NetOptionLayerCount(layer) : 0;
                }
                // The slot's TOTAL, declared by its one writer (v20, card af96bcc2). Read live
                // rather than flush-aligned: unlike the old EvScoreSync there is no EvDeath award
                // for the replica to double-count against -- it adopts this figure verbatim and
                // credits nothing for our kills itself.
                hudTxScores[count] = score.PointScore(slot);
                count++;
            }
            if (count == 0)
            {
                return;
            }
            SendStreamToSession(NetProtocol.EncodeHudState(hudTxSlots, hudTxCombos, hudTxComboLeft, hudTxTypes, hudTxProgress, hudTxLevels, hudTxOptions, hudTxScores, count));
            // Counted in ENTRIES, matching HudRx -- a peer with a couch partner puts two slots in
            // one packet, so counting packets here would make the two sides incomparable.
            metrics.HudTx += count;
        }

        private static void HandleHudState(PeerChannel from, byte[] data)
        {
            if (score == null || !NetProtocol.TryDecodeHudCount(data, out int count))
            {
                return;
            }
            // The hub relays a client's HUD packet to the other clients VERBATIM (card
            // 87242257): it has no seq and carries only the sender's owned slots, and every
            // receiver's own-slot guard below already protects its own panels. Addressed, so the
            // sender never hears its own state back.
            if (isHost)
            {
                foreach (PeerChannel q in peers.Values)
                {
                    if (q.Up && q != from)
                    {
                        transport.SendStreamTo(q.Id, data);
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (!NetProtocol.TryDecodeHudState(data, i, hudRxLevels, hudRxOptions, out byte slot, out int combo, out float comboLeft, out Powerup.PowerupType? activeType, out float progress, out float scoreTotal))
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
                score.NetSetHudState(slot, combo, comboLeft, activeType, progress, hudRxLevels);
                // AFTER the HUD state, never before: its level loop drives the real
                // PlayerShip.PowerUp one step at a time, which spawns the level-driven options
                // itself. Reconciling first would leave those extras standing until the next
                // packet. The owner's count is authoritative over the whole population, so this
                // both catches up a join-in-progress peer (which replays no EvClaim, so it never
                // saw the per-pickup options at all) and drops any this peer is over.
                FindShipForSlot(slot)?.NetSetOptionCounts(hudRxOptions);
                // The owner's declared TOTAL, adopted verbatim (v20, card af96bcc2). This is the
                // whole score reconciliation now: this peer never credits a slot it does not own
                // (AwardScore's OwnsSlot gate), so the replica cannot drift -- only be one packet
                // (~100 ms) stale. A JIP joiner adopts the running totals from the first packet,
                // with no award history to replay.
                score.NetSetScore(slot, scoreTotal);
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

        // The ship carried by the PRIMARY-flagged ship frame: the one in our granted primary
        // slot. Every OTHER locally-owned ship (couch players, AI friends) rides a slot-tagged
        // MsgShipState frame with the primary flag clear instead (SendFriendStates).
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
            byte slot = (byte)ship.Owner;
            SendEventToSessionPeers(seq => NetProtocol.EncodeBlastEvent(seq, slot, pos, level));
        }

        // Called from PlayerShip.PlayerShip_OnDeath, at the moment a respawn summon is spawned for
        // a ship we own (card 37f3a663). Same shape and same reasoning as OnLocalBlast above: a
        // respawn is discrete, so it rides the reliable lane, and it is slot-tagged so a couch
        // player's respawn indicator does not appear over the peer's primary.
        //
        // Only the ANNOUNCEMENT crosses -- the far peer's copy is cosmetic and its ship still
        // arrives through the ordinary remoteAlive edge. So a lost or ignored frame costs the
        // indicator, never the ship.
        //
        // `rewardLevel` (card ed32efe1, v26) is the summon's own latched value, taken from the
        // SUMMON rather than re-read here, so the announcement cannot describe a different bomb
        // from the one this peer will drop. See NetProtocol.EncodeRespawnEvent for why it rides
        // the wire instead of being re-derived on the far side.
        public static void OnLocalRespawnSummon(PlayerShip ship, Vector2 pos, int durationMs, int rewardLevel)
        {
            if (!Active || !PeerUp || ship == null || !IsLocallyOwned(ship))
            {
                return;
            }
            byte slot = (byte)ship.Owner;
            SendEventToSessionPeers(seq => NetProtocol.EncodeRespawnEvent(seq, slot, pos, durationMs, rewardLevel));
        }

        // ---- level-script beats + shared state machine (card 11.3) ---------------------------

        // True while the OTHER peer holds a pause. GameScene's resume paths consult this so
        // an overlapping local+remote pause only unfreezes when both are clear. A facade over
        // the peer channel since card b2828be8.
        public static bool RemotePaused => AnyPeerPaused();

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
            SendEventToSessionPeers(seq => NetProtocol.EncodeMessageEvent(seq, (byte)msgType, (byte)speech, angle, text));
            metrics.BeatsTx++;
        }

        // A banner spawned by BOSS code rather than by a MessageEvent -- SpiderBoss's three
        // "Danger!" sweep warnings and its helper-mothership "Warning!", JunkBoss's meteor
        // "Danger!". Same lane as OnScriptMessage and for the same reason (the host runs the
        // Update that spawns them and a frozen puppet never can), with the compact MakeShort
        // layout the bosses use carried in EvMessage's optional trailing byte.
        public static void OnGameMessage(string text, int speech, int msgType, float angle, bool isShort)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeMessageEvent(seq, (byte)msgType, (byte)speech, angle, text, isShort));
            metrics.BeatsTx++;
        }

        // A one-shot cosmetic beat the host observed (EvFx). `target` is null for an
        // entity-free kind (EnemyLazerFire); when it is given, the beat is addressed to that
        // entity's netId and is DROPPED if it has none -- an entity outside the replicable set
        // has no puppet on the other screen to light up, so there is nothing to say.
        //
        // TRAFFIC: reliable lane, 8 bytes, and the rate is bounded by the emitters rather than
        // here. Each entity's own re-hit gate (KillableAlien/Ball's 35ms hittimer, SpiderBoss's
        // per-Lazer set) means one beat per entity per hit, so a bomb clearing a wave costs the
        // same order as the EvDeaths that wave already sends. Worth re-reading if a kind is ever
        // added whose emitter has no such gate.
        public static void OnGameFx(NetFxKind kind, AlienDrawableGameComponent target, byte param = 0)
        {
            if (!IsHost || !PeerUp || NetScene.Current == null)
            {
                return;
            }
            ushort netId = 0;
            if (target != null)
            {
                if (!NetIdRegistry.TryGetByComp((GameComponent)(object)target, out NetIdRegistry.Entry entry))
                {
                    return;
                }
                netId = entry.Id;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeFxEvent(seq, (byte)kind, netId, param));
            metrics.BeatsTx++;
        }

        // The local 1up slow motion has started -- run it over there too (card a66e190a).
        // Called from Oracle.SetSlowmotion, the ONE place a local slow motion begins (the 1up
        // bar's PlayerShip.PowerUp case and the eaSlowmo() QA seam both go through it), so
        // there is no per-caller plumbing. The rx side calls Oracle.NetSetSlowmotion instead,
        // which does the identical work WITHOUT this send -- so the two peers cannot echo each
        // other into a permanent slowdown, by construction rather than by a latch.
        //
        // EITHER PEER, unlike OnGameFx: the bar belongs to whichever player filled it, and that
        // is as often the joiner as the host.
        //
        // WHY REPLICATING A TIME SCALE IS SAFE HERE, when Juice.AddHitStop is refused outright:
        // a hit-stop is scale ZERO on ONE peer, so that peer stops producing motion while the
        // wire keeps streaming and the other peer's puppets are corrected backward. This is 0.4
        // on BOTH, and every net clock (the cadence, NetPuppets.Drive, the observed velocities)
        // reads real time and is untouched by the game time scale -- so the wire carries the
        // slowed truth and nothing is corrected backward. What it REMOVES is a 12 s divergence:
        // pre-card one peer crawled while the other ran, which is exactly what the card reported.
        // The residual is the ~one-way-trip skew at each end of the window.
        public static void OnLocalSlowmotion(float seconds)
        {
            if (!Active || !PeerUp || NetScene.Current == null)
            {
                return;
            }
            float ms = MathHelper.Clamp(seconds * 1000f, 0f, 65535f);
            SendEventToSessionPeers(seq => NetProtocol.EncodeSlowmoEvent(seq, (ushort)ms));
            metrics.BeatsTx++;
        }

        // "This entity's death has BEGUN and is going to take a while" (card f62116b5). Called
        // from KillableAlien the moment a KilledBy returns WITHOUT having removed the component
        // -- the discriminant is `!IsDead`, which is exactly the test the client already makes
        // to spot the same thing, so the two ends agree by construction. An ordinary type ends
        // its KilledBy in Die(), so it is dead by then and no beat goes out at all.
        //
        // WHY IT IS NOT INFERRED FROM THE SNAPSHOT ANY MORE. hp==0 in a snapshot entry says the
        // same thing, and it is still the fallback (NetPuppets.ApplyHostKilledFromSnapshot) --
        // but it only says it on that entity's round-robin turn, which is 60 ms at best and
        // ~1.2 s in a big world, against a 2.5-5 s animation. This is immediate at any world
        // size, and it cannot fire a tick early on an ordinary kill the way a sampled hp can.
        //
        // An entity with no netId has no puppet on the other screen to release, so there is
        // nothing to say. Traffic is one 6-byte reliable frame per DEFERRED death, which is a
        // handful per level.
        //
        // NO `NetScene.Current` GATE, unlike OnGameFx and the script beats -- this is an ENTITY
        // LIFECYCLE event off the NetIdRegistry, like OnHostSpawn/OnHostDeath (further down this
        // file, in the registry-seam block), and the registry's own enablement is what decides
        // whether a world exists. Adding one would also make the host leg of eaNetDeathFx
        // unreachable, since that suite plants real entities from the MENU. The client's rx
        // handler is scene-gated, exactly as EvDeath's is.
        //
        // Named for the HOST side, like OnHostSpawn/OnHostDeath: NetPuppets.OnDeathBegan is the
        // rx half, and a bare OnDeathBegan in a stack trace would not say which end it is.
        public static void OnHostDeathBegan(AlienDrawableGameComponent comp)
        {
            if (!IsHost || !PeerUp || comp == null)
            {
                return;
            }
            if (!NetIdRegistry.TryGetByComp((GameComponent)(object)comp, out NetIdRegistry.Entry entry))
            {
                return;
            }
            OnHostDeathBegan(entry.Id);
        }

        // The by-id half, for NetIdRegistry.ReplayLive's catch-up: a peer joining mid-animation
        // needs the beat for a death that began before it arrived.
        internal static void OnHostDeathBegan(ushort netId)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeDyingEvent(seq, netId));
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx dying id=" + netId);
            }
        }

        public static void OnScriptUnlock(int item, int unlockType, int speech, string text)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeUnlockEvent(seq, (byte)item, (byte)unlockType, (byte)speech, text));
            metrics.BeatsTx++;
        }

        public static void OnBackgroundOp(NetBackgroundOp op, Vector2 v)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeBackgroundEvent(seq, (byte)op, v));
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeCosmeticSwarmEvent(seq, (byte)kind, on, rate));
            metrics.BeatsTx++;
        }

        // Card 8a7772d6: Level 1's intro bullet volley is starting on the host. Fire-and-forget
        // -- a joiner that is not there yet simply misses it, which is correct (there is no
        // catch-up leg, deliberately: replaying a 2.3s volley at a peer that arrived after it
        // would put a hail of bullets on their screen with the cinematic already over -- the
        // same reasoning that keeps EnemyLazerFire off the EvSpawn/ReplayLive path).
        public static void OnIntroVolley(int seed)
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeIntroVolleyEvent(seq, seed));
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvMusic, song < 0 ? NetProtocol.MusicStop : (byte)song));
            metrics.BeatsTx++;
        }

        public static void OnCheckpoint()
        {
            if (!IsHost || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvCheckpoint));
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvReset, mode));
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvVictory));
            metrics.Victories++;
        }

        // Either peer: local pause menu pushed / every resume path. The receiving side
        // freezes its world with a hint overlay (no interactive menu -- you can't navigate
        // the peer's menu for them).
        //
        // PAUSE IS A SET since card 87242257. A client still announces its own pause level to
        // its host; the HOST instead maintains, per client, the AGGREGATE "someone besides you
        // holds a pause" (its own pause OR any other client's) and sends EvPause edges of THAT
        // -- which is exactly the semantic every client's single RemotePaused bool already
        // implements, and what keeps B frozen when A pauses, B pauses, A unpauses. At two peers
        // the wire traffic is byte-identical to the old direct announce.
        public static void OnLocalPause(bool on)
        {
            localPaused = on && Active;
            if (!Active || !PeerUp)
            {
                return;
            }
            if (on)
            {
                metrics.Pauses++;
            }
            if (isHost)
            {
                SyncPauseAggregateToPeers();
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvPause, (byte)(on ? 1 : 0)));
        }

        // The world freezes while ANYONE ELSE holds a pause; the scene setter self-guards, so
        // this only has to hand it the aggregate's edges. `remotePauseApplied` mirrors what the
        // scene was last told, because a scene that Initializes mid-pause reads RemotePaused
        // itself (GameScene.Initialize) and the flags can churn while no scene is up.
        private static bool remotePauseApplied;

        private static void SyncRemotePauseToScene()
        {
            bool any = AnyPeerPaused();
            if (any == remotePauseApplied)
            {
                return;
            }
            remotePauseApplied = any;
            NetScene.Current?.NetSetRemotePaused(any);
        }

        // HOST hub: push each client the edge of ITS aggregate (local pause OR any OTHER
        // client's). Tracked per channel as PauseSentTo, so a no-change recompute sends nothing.
        private static void SyncPauseAggregateToPeers()
        {
            if (!isHost)
            {
                return;
            }
            foreach (PeerChannel q in peers.Values)
            {
                if (!q.Up)
                {
                    continue;
                }
                bool forQ = localPaused;
                if (!forQ)
                {
                    foreach (PeerChannel other in peers.Values)
                    {
                        if (other != q && other.RemotePaused)
                        {
                            forQ = true;
                            break;
                        }
                    }
                }
                if (forQ != q.PauseSentTo)
                {
                    q.PauseSentTo = forQ;
                    bool val = forQ;
                    SendEventToPeer(q, seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvPause, (byte)(val ? 1 : 0)));
                }
            }
        }

        // The stall banner is the same shape: per-peer flags, one aggregate to the scene.
        private static bool stallBannerApplied;

        private static void SyncStallBannerToScene()
        {
            bool any = PeerStalled;
            if (any == stallBannerApplied)
            {
                return;
            }
            stallBannerApplied = any;
            NetScene.Current?.NetSetPeerStalled(any);
        }

        // Either peer: the TeamChallenge tether broke on this screen (enemy hit / endpoint
        // died). Or-of-either-peer, idempotent -- the receiver breaks silently.
        public static void OnTetherBreak()
        {
            if (!Active || !PeerUp)
            {
                return;
            }
            SendEventToSessionPeers(seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvTetherBreak));
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
            NoteKillSlot(comp, slot >= 0 && slot < NetProtocol.PayableSlots ? (byte)slot : NetProtocol.KillerNone);
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

        // "I am about to die a real death that nobody landed" -- a self-detonating space mine, a
        // scripted crash (cards 4e406eba / 303bfb5b / 13aa596c). Called by the game immediately
        // before its own Die(), so the note is on the entity when the removal seam reads it and
        // OnHostDeath can put KillerSelf on the wire instead of KillerNone.
        //
        // IT IS OPT-IN AT THE CALL SITE, and that is the whole safety argument. The alternative
        // -- inferring it from `IsDead` at the removal seam -- cannot tell a self-destruct from
        // the dozens of FX-free Die() sites that mean "I have left the world" (every OffScreen
        // despawn, Parachute's fade-out, ParatrooperBrain's merge, Lazer being eaten by the
        // spider boss), so it would put a bang and a sound on the peer's screen where the host
        // showed nothing. A hook says exactly what the game meant.
        //
        // Costs one bool test offline. Runs on BOTH peers -- a client's own mine puppet is still
        // hit-testable, so its Asplode() reaches here too. The note is harmless there but NOT
        // ignorable: the client's removal seam takes the echo-guard early return, so it consumes
        // the note explicitly (killNotes is keyed on the entity, which ComponentBin recycles).
        // When a claim IS sent for one, KillerSelf is non-payable at HandleClaim exactly as
        // KillerNone was, so nothing is credited either way.
        public static void NoteSelfDestruct(AlienDrawableGameComponent comp)
        {
            if (Active && NetTypeRegistry.IsReplicableInstance((GameComponent)(object)comp))
            {
                NoteKillSlot(comp, NetProtocol.KillerSelf);
            }
        }

        // Powerup pickups are claims too: the collecting side records WHO took it before
        // Powerup.Die() cascades into removal.
        public static void NotePowerupTaken(Powerup powerup, int playerSlot)
        {
            if (Active && playerSlot >= 0 && playerSlot < NetProtocol.PayableSlots)
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
        // SetPowerup below a redundant-but-immediate head start on the next ~10 Hz packet.
        // Idempotent: the collector's own side already ran the local path and never reaches
        // a settle branch for its own pickup (its entity is gone before the echo arrives).
        //
        // Cards 83271f3d / 10f9dba4: the HUD icon used to be ALL of it, so a remote collector got
        // the readout and none of the ship-side effect -- see PlayerShip.NetApplyRemotePickup for
        // which types that costs and which are already covered elsewhere.
        //
        // Card 06ac5df2: it is AUDIBLE again -- the other player's pickup plays the "powerup" cue
        // on this screen too, reversing card d53431b4's mute (which was itself the user's ruling;
        // so is this). Gated on !OwnsSlot below with the ship-side mirror, so the host settling a
        // claim for its OWN slot -- whose ship already played the cue in CollidesWith -- never
        // doubles it. Local pickups and local co-op are untouched either way; both go through
        // PlayerShip.CollidesWith.
        internal static void ApplyRemotePowerup(INetPickup powerup, byte slot)
        {
            ApplyRemotePowerup(powerup.NetPickupType, slot);
        }

        // The TYPE-keyed half (06ac5df2 follow-up): PayDeadClaim reaches here off the death
        // record, where the entity itself is already out of the world.
        internal static void ApplyRemotePowerup(Powerup.PowerupType type, byte slot)
        {
            // Bound against the SCORE PANELS (4), not the 8 of the claim ledgers' PaidMask --
            // slot is a raw wire byte, so a corrupt or mismatched peer must not index past
            // ScoreVisualiser's fixed 4-slot list.
            if (slot == NetProtocol.KillerNone || slot >= ScoreVisualiser.SlotCount)
            {
                return;
            }
            score.SetPowerup(type, slot);
            // OwnsSlot is the double-apply guard, not a formality: the host runs this for a
            // CLIENT's claim too, so a claim naming a slot we own would otherwise re-run the
            // pickup on a ship that already took it -- a second batch of Options every time.
            // (The HUD SetPowerup above is idempotent and stays ungated.)
            if (!OwnsSlot(slot))
            {
                FindShipForSlot(slot)?.NetApplyRemotePickup(type);
                // Outside the ship lookup on purpose: the cue reports the pickup, not the puppet,
                // and the collector's puppet can be dead-or-lagging between packets without that
                // making their pickup silent.
                sound.PlayCue("powerup");
            }
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] remote powerup " + type + " -> slot " + slot);
            }
        }

        // Client: apply a one-shot cosmetic beat (EvFx). Split out of HandleEvent so a scenario
        // can drive it directly, and kept DELIBERATELY THIN -- the per-type knowledge lives on
        // the entity (INetEntity.NetPlayFx), so the wire never has to name a cue or a sprite.
        //
        // Every branch is a no-op when the beat has nothing to act on: an unknown or late netId
        // simply has no puppet. An FX beat is never retried or queued -- it is stale by the time
        // a missing entity could arrive, and a flash for a hit two seconds ago is worse than none.
        internal static void ApplyFx(NetFxKind kind, ushort netId, byte param)
        {
            switch (kind)
            {
            case NetFxKind.EnemyHitFlash:
            case NetFxKind.BallDetach:
            {
                INetEntity target = NetPuppets.FindPuppet(netId);
                target?.NetPlayFx(kind);
                break;
            }
            case NetFxKind.EnemyLazerFire:
                // Entity-free: the beam itself already replicates as its own Lazer puppet, built
                // sound-free by LazerDescriptor; this is only its report. An ENEMY telegraph is a
                // world event both players are dodging, unlike a remote PLAYER's own summon glow,
                // which stays silent. (The remote pickup cue moved sides of that line twice:
                // muted by card d53431b4, audible again by card 06ac5df2 -- ApplyRemotePowerup.)
                sound.PlayCue("lazershotnoloop");
                break;
            }
        }


        // The collector's ship, or null. Null is a real case rather than a defensive one: the
        // claim scenarios drive this path from the MENU, where no ship (and no oracle binding)
        // exists -- the HUD half still lands there.
        private static PlayerShip FindShipForSlot(int slot)
        {
            if (oracle == null)
            {
                return null;
            }
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.Owner == slot)
                {
                    return s;
                }
            }
            return null;
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
            // The entry flags are DISCARDED here, and that is not an oversight: EvSpawn's payload
            // is the shared base-state block, which has no flags byte, because a spawn is the
            // entity's first observation and "this sample is a discontinuity" is meaningless of
            // it. Consuming the latch is still correct -- CaptureBaseState just advanced this
            // entity's velocity baseline, so an unspent latch would refuse the FOLLOWING snapshot
            // turn's velocity for a jump that happened before the entity existed on the wire.
            NetBaseState state = CaptureBaseState(e, NowMs, out _);
            // Cast back for the descriptor: its extras surface is deliberately still on the
            // concrete type: 2c-iii measured moving it and DECLINED, so this cast is permanent
            // and is safe by construction -- see INetEntity's header for the argument.
            int extraLen = e.Descriptor.EncodeSpawnExtra((AlienDrawableGameComponent)e.Comp, extraScratch, 0);
            SendEventToSessionPeers(seq => NetProtocol.EncodeSpawnEvent(seq, e.Id, e.TypeIdx, state, extraScratch, extraLen));
            // The spawn's base state carries hp too, and for a catch-up spawn it is the FIRST hp
            // the joiner ever applies -- so recording it here is what stops a freshly attached
            // peer reading as "never sent any hp" (card d108c459).
            e.LastSentHp = state.Hp;
            metrics.EventsTx++;
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx spawn id=" + e.Id + " type=" + e.Comp.GetType().Name);
            }
        }

        // How far outside the 800x600 design screen a self-destruct still counts as visible.
        // Matches the buffer AlienDrawableGameComponent.OffScreen is called with by the types
        // that despawn themselves (StarMine/ParatrooperAlien use 100), so "off screen enough to
        // despawn" and "off screen enough not to bother exploding" are the same edge.
        private const float DeathFxMarginPx = 100f;

        // The same test AlienDrawableGameComponent.OffScreen(100) makes, inverted -- restated
        // here rather than reached through the seam because INetEntity carries Position and not
        // OffScreen, and putting a screen-bounds test on the entity seam for one caller would be
        // a worse trade than four literals that already appear verbatim in the game code.
        private static bool OnScreenForDeathFx(Vector2 pos)
        {
            return pos.X >= 0f - DeathFxMarginPx && pos.X <= 800f + DeathFxMarginPx
                && pos.Y >= 0f - DeathFxMarginPx && pos.Y <= 600f + DeathFxMarginPx;
        }

        internal static void OnHostDeath(NetIdRegistry.Entry e)
        {
            if (!Active)
            {
                return;
            }
            byte killer = TakeKillNote(e.Comp);
            Vector2 pos = e.Comp.Position;
            if (killer == NetProtocol.KillerSelf && !OnScreenForDeathFx(pos))
            {
                // A self-destruct the host itself showed nothing of: play nothing at the peer
                // either. Downgrading to KillerNone means the ordinary silent despawn.
                killer = NetProtocol.KillerNone;
            }
            // recentDeaths keeps the BASE value: a later claim from the other peer is a fresh
            // generous payout the host still credits with its own live combo (card 11.2), not
            // a replay of the award below.
            ushort points = (ushort)MathHelper.Clamp(e.Comp.NetPointValue, 0f, 65535f);
            RecordDeath(e.Id, pos, points, killer,
                e.Comp.NetPickup is INetPickup pu ? pu.NetPickupType : (Powerup.PowerupType?)null,
                e.ClaimPaidMask);
            if (!PeerUp)
            {
                return;
            }
            // No award payload since v20 (card af96bcc2): each peer credits its own slots off
            // its own observation of the kill, so the event carries only the removal + FX facts.
            SendEventToSessionPeers(seq => NetProtocol.EncodeDeathEvent(seq, e.Id, killer, pos));
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx death id=" + e.Id + " killer=" + killer);
            }
        }

        // prepaidMask is the Entry's own ledger: the slots already paid for this death while the
        // removal was still queued (card 1bfcd705). Folded in HERE rather than merged into
        // whatever recentDeaths already holds for the id, so the write below stays a straight
        // assignment and a wrapped netId can never inherit a stale mask from a previous entity.
        private static void RecordDeath(ushort id, Vector2 pos, ushort points, byte killerSlot,
            Powerup.PowerupType? pickup, byte prepaidMask)
        {
            DeathRecord rec = new DeathRecord
            {
                Pos = pos,
                Points = points,
                PaidMask = (byte)(prepaidMask | (killerSlot < NetProtocol.PayableSlots ? 1 << killerSlot : 0)),
                Pickup = pickup,
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
                NetBaseState state = CaptureBaseState(e, now, out byte entryFlags);
                NetProtocol.WriteSnapshotEntry(snapshotScratch, ref off, e.Id, e.TypeIdx, entryFlags, state, extraScratch, extraLen);
                // Recorded where the value LEAVES the host, after the fit check that can skip an
                // entry -- so it always names hp a peer was really sent (card d108c459).
                e.LastSentHp = state.Hp;
                written++;
            }
            if (written == 0)
            {
                return;
            }
            NetProtocol.WriteSnapshotHeader(snapshotScratch, (byte)written, txSnapshotSeq++);
            byte[] packet = new byte[off];
            Array.Copy(snapshotScratch, packet, off);
            transport.SendStream(packet);
            metrics.SnapTx++;
        }

        private static NetBaseState CaptureBaseState(NetIdRegistry.Entry e, long now, out byte entryFlags)
        {
            INetEntity c = e.Comp;
            Vector2 pos = c.Position;
            // TELEPORT MARKER (card e79bb994, replacing card 8dabe812's plausibility cap).
            //
            // Read-and-CLEAR first, unconditionally: the latch must be spent on this turn whether
            // or not we go on to differentiate, or a teleport recorded before the entity's first
            // observation would sit set and refuse a LATER turn's perfectly good velocity.
            bool teleported = c.NetTakeTeleport();
            entryFlags = teleported ? NetProtocol.NetSnapshotFlags.Teleported : NetProtocol.NetSnapshotFlags.None;

            bool anchored = c.NetPathAnchored;
            bool scripted = c.TryGetNetScriptedVelocity(out Vector2 announced);
            Vector2 vel = ResolveBaseVelocity(c.NetSpeedVector, anchored, teleported, pos,
                e.HasLastPos, e.LastPos, e.LastPosMs, now, scripted, announced);
            if (teleported)
            {
                metrics.Teleports++;
            }
            else if (!anchored && e.HasLastPos && now > e.LastPosMs)
            {
                // ANCHORED TYPES ARE SKIPPED, and it costs nothing: `vel` is the DECLARED vector
                // for them, not an observed one, so the safety net below would be measuring the
                // wrong quantity. Neither anchored type repositions (a wasp and a rock fly a
                // straight line and die off-screen), so there is no reposition site to miss.
                //
                // SCRIPTED TYPES ARE **NOT** SKIPPED, and the asymmetry is deliberate (card
                // 76ec8bdb). Their wire velocity is announced rather than observed too, so it is
                // equally unfit to measure -- but the SpiderBoss is this detector's principal
                // subject, holding three of the game's four reposition sites, and it is the type
                // most likely to grow a fourth. So the raw difference is recomputed here purely
                // to be judged, and never reaches the wire. It is only reached on an UNMARKED
                // turn, where that difference describes genuine motion.
                NoteIfUnmarkedTeleport(c, scripted
                    ? (pos - e.LastPos) / (now - e.LastPosMs)
                    : vel);
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

        // The velocity a snapshot carries, as a PURE decision (the OwnsSlotCore precedent): the
        // whole of it is unobservable from any frame and all three of its branches are chosen by
        // something other than the numbers, so a test that could not table-drive it could not
        // cover it at all. Measured: a mutation dropping the ANCHORED branch passed the whole
        // probe suite and every other leg of eaNetMotion until this was split out.
        //
        // Observed velocity: differentiate real positions between this entity's snapshot turns --
        // robust for enemies that move Position directly (arcs, easing) where Speed/Direction
        // would lie. THREE things fall back to the declared vector instead:
        //
        //   * the FIRST observation, which has nothing to difference against;
        //   * a marked TELEPORT (card e79bb994) -- a finite difference cannot tell motion from a
        //     jump, and the SpiderBoss is parked at the far screen edge to start each fly-by;
        //     differentiating that ~800 px jump stamped 42-57 px/ms onto the wire and the client
        //     dead-reckoned on it, collidably, killing the local player;
        //   * an ANCHORED type (card c1a38ef9), whose real position is a linear baseline plus a
        //     periodic component the client integrates for itself (INetEntity.NetPathOffset). A
        //     difference taken over a whole snapshot turn describes a CHORD of that periodic
        //     part rather than the baseline, so the estimate is wrong by construction and worse
        //     the longer the turn. Here the declared vector is not a fallback at all -- it is the
        //     baseline, exactly.
        //
        // A FOURTH branch, SCRIPTED, now outranks the two fallbacks above (card 76ec8bdb): a type
        // that moves by writing `Position` can ANNOUNCE the velocity it is moving at, and that
        // answer is used on EVERY turn, not only the ones the fallbacks would have caught. It
        // beats the finite difference for the same reason the anchored branch does and one more:
        // a difference reported at turn T describes [T-1, T] while the client dead-reckons over
        // [T, T+1], so it is a whole turn stale at every phase boundary of a scripted set-piece
        // -- and a boss stepping from a standing 0 to a 0.78 px/ms sweep leaves a COLLIDABLE
        // puppet ~375 px behind at a 480 ms turn before popping. An announced velocity is
        // forward-looking, which is the direction dead reckoning needs.
        //
        // WITHOUT A SCRIPTED ANSWER THE DECLARED SPEED IS THE BEST THE HOST HAS, NOT A GOOD
        // ANSWER -- do not read that fallback as informative. Half the replicable set moves by
        // writing `Position` directly and never assigns `Speed`/`Direction`, so its
        // NetSpeedVector is ZERO: a marked park sends zero velocity and the puppet stands still
        // until its next turn, up to ~1.2 s in a big world. That was the correct trade against
        // flinging it across the screen collidably, and it is still what every type that has not
        // taken the scripted seam gets. `SpiderBoss` is the one that has.
        //
        // ANCHORED STILL OUTRANKS SCRIPTED, and no type may hold both: the two answer the same
        // question from opposite evidence (a declared vector that is honest, versus a script that
        // makes the declared vector irrelevant), so a type claiming both is a contradiction the
        // ordering resolves rather than a blend. eaNetScriptedMotion asserts none does.
        //
        // The CALLER still stamps LastPos/LastPosMs whatever this returns: they are the entity's
        // observation history and must stay live in case a type ever stops being anchored or
        // scripted mid-life -- and for the SCRIPTED case the caller reads them back, to keep the
        // unmarked-teleport safety net on the one type most likely to grow a reposition site.
        internal static Vector2 ResolveBaseVelocity(Vector2 declared, bool anchored, bool teleported,
            Vector2 pos, bool hasLastPos, Vector2 lastPos, long lastPosMs, long now,
            // NOT defaulted, though only one type answers true today. A default would let a new
            // caller -- or a suite written to the old signature -- opt out of the scripted branch
            // by saying nothing, which is exactly how card c1a38ef9's anchored mutation survived
            // an entire probe suite.
            bool scripted, Vector2 announced)
        {
            if (anchored)
            {
                return declared;
            }
            if (scripted)
            {
                return announced;
            }
            if (teleported || !hasLastPos || now <= lastPosMs)
            {
                return declared;
            }
            return (pos - lastPos) / (now - lastPosMs);
        }

        // Types already reported as suspected unmarked teleporters, so the console says each name
        // ONCE. A reposition site fires every fly-by, and this runs inside the snapshot encode.
        //
        // Cleared per SESSION (ResetPerSessionState) and per SCAN (NetVelocityScan.Arm), not per
        // process: a name burned once for the whole process would let a suite that reports a type
        // on purpose -- NetTeleportTest's unmarked-jump control does exactly that with a UFO --
        // silence a genuine one hit later in the same run, leaving only a counter to notice it.
        private static readonly HashSet<string> unmarkedTeleportReported = new HashSet<string>();

        internal static void ClearUnmarkedTeleportReports()
        {
            unmarkedTeleportReported.Clear();
        }

        // THE SAFETY NET FOR A MISSED REPOSITION SITE (card e79bb994).
        //
        // The marker is only as good as its call sites, and a missed one fails exactly the way the
        // pre-card bug did -- silently, on the OTHER player's screen, one type at a time. So the
        // 5.0 px/ms figure card 8dabe812 measured survives here, DEMOTED: nothing above reads it,
        // it cannot alter a single byte on the wire, and a wrong value can therefore no longer
        // clip a legitimately fast mover (which was that cap's own dangerous failure). All it can
        // do now is name a type.
        //
        // Read the line as "add NetNoteTeleport() at this type's reposition site", not as a fault
        // in the net layer. The ceiling under it is measured -- `eaNetVelScan` reports every
        // replicable type's SUSTAINED speed and tops out at MarsBoss's 2.404 px/ms entry curve --
        // and tools/headless/probes/net_velguard.txt asserts BOTH halves: that the threshold still
        // clears every genuine mover (so this cannot cry wolf), and that a Level-2 soak produces
        // no line at all (so every reposition site reachable there is marked).
        private static void NoteIfUnmarkedTeleport(INetEntity c, Vector2 observed)
        {
            if (observed.LengthSquared() <= MaxObservedSpeedPxPerMs * MaxObservedSpeedPxPerMs)
            {
                return;
            }
            metrics.UnmarkedTeleports++;
            ReportUnmarkedTeleport(c.GetType().Name, observed.Length());
        }

        // THE ONE PLACE THAT WORDS IT, because there are TWO detectors and they must not drift.
        // This one (a live host session) is what a real player would hit; NetVelocityScan carries
        // the other, which needs no session and is therefore the one a headless probe can assert
        // -- `tools/headless/probes/net_velguard.txt` greps this exact line, so its shape is an
        // interface. The once-per-type set is shared too: the message names a CODE fact, so
        // repeating it per fly-by would only bury it.
        //
        // **THE METRIC IS NOT BUMPED HERE, deliberately** -- `NetMetrics` has no reset (every
        // scenario asserts on DELTAS instead), so letting the offline scan bump `tpUnmarked` would
        // leave a figure from a menu-time audit sitting in the first `[net]` line of an unrelated
        // session. The caller above owns it, so that counter stays what its comment says: what a
        // LIVE HOST observed. The scan keeps its own tally and prints it in the velscan table.
        internal static void ReportUnmarkedTeleport(string typeName, float speedPxPerMs)
        {
            if (unmarkedTeleportReported.Add(typeName))
            {
                Console.WriteLine("[net] UNMARKED teleport suspected: " + typeName + " at "
                    + speedPxPerMs.ToString("0.0") + " px/ms (threshold "
                    + MaxObservedSpeedPxPerMs.ToString("0.0")
                    + ") -- add NetNoteTeleport() at its reposition site");
            }
        }

        // ---- host score sync ------------------------------------------------------------------

        // Host: lives as they stood at the LAST top-of-tick flush. The per-slot score array this
        // block used to snapshot is GONE (card af96bcc2, one writer per slot) -- each slot's
        // total is owner-declared on MsgHudState now, and the sync carries only what the host is
        // still the one writer of. The flush-aligned snapshot survives for lives because the
        // reasoning is unchanged: a life spent in this tick's collision phase should reach the
        // client after the EvDeath that spent it.
        private static int scoreSyncSnapshotLives;

        internal static void SnapshotScoresForSync()
        {
            if (!Active || !isHost || score == null)
            {
                return;
            }
            scoreSyncSnapshotLives = score.Lives;
        }

        private static void SendScoreSync(long now)
        {
            lastScoreSyncTx = now;
            SendEventToSessionPeers(seq => NetProtocol.EncodeScoreSync(seq, scoreSyncSnapshotLives));
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeClaimEvent(seq, netId, killerSlot));
            metrics.ClaimsTx++;
            if (NetHost.Current.NetLog)
            {
                Console.WriteLine("[net] tx claim id=" + netId + " killer=" + killerSlot);
            }
        }

        // (card 1878b321) A CLIENT's own killing blow on a type whose KilledBy DEFERS -- the
        // SpiderHelperMothership's crash-after-the-mission, BattleSkull's 2.5 s shrink, the
        // surviving MarsBoss's 5 s crash. The claim normally rides NetPuppets' removal seam,
        // but a frozen puppet's deferred death never runs its own Die(), so nothing was ever
        // sent and the kill was PHANTOM: the host's copy flew on untouched while the joiner
        // kept a red, unresponsive zombie. Send the claim at death-began instead, consuming
        // the kill note HitBy just wrote (killNotes is keyed on the pooled entity, so an
        // unconsumed one could attribute a later death). No double claim: the puppet is only
        // ever removed after ReleaseDyingPuppet has unmapped it or OnRemoteDeath has guarded
        // it, and both make the removal seam's own send a no-op. A no-op for ordinary types
        // (their KilledBy ended in Die(), so IsDead is already true here), on the host (its
        // kills are authoritative, announced by EvDying/EvDeath) and offline.
        public static void OnClientDeferredKill(KillableAlien comp)
        {
            if (!Active || IsHost || comp == null || comp.IsDead)
            {
                return;
            }
            if (!NetPuppets.TryGetId((GameComponent)(object)comp, out ushort netId))
            {
                return;
            }
            SendClaim(netId, TakeKillNote(comp));
        }

        // ---- wire -> state ----------------------------------------------------------------

        private static void DrainRx()
        {
            while (rxQueue.Count > 0)
            {
                (byte[] data, bool reliable, string from) = rxQueue.Dequeue();
                if (data.Length == 0)
                {
                    continue;
                }
                // A REJECT is handled BEFORE the channel resolve, because it must not need a
                // channel: the refused side often has none -- a client only creates its one
                // channel from a host-role handshake, and an over-capacity newcomer was turned
                // away at the door -- and dropping the reject there would replace "Game full" /
                // "Update required" with a silent hang (review finding on card 87242257).
                if (data[0] == NetProtocol.MsgReject)
                {
                    HandleReject(from, data);
                    continue;
                }
                // Every other message resolves its sender's channel first: host-side a hello
                // creates it, and a stream frame from an unpaired sender does too (the stream IS
                // the heartbeat -- see HandleShipFrame's reconnect note); client-side only a
                // host-role handshake frame can (the bus-medium rule -- see GetOrCreatePeer). A
                // refused sender (over capacity, rejected, kicked) is dropped wholesale.
                PeerChannel p = GetOrCreatePeer(from, data);
                if (p == null)
                {
                    continue;
                }
                switch (data[0])
                {
                case NetProtocol.MsgHello:
                    HandleHello(p, data, welcomeBack: true);
                    break;
                case NetProtocol.MsgWelcome:
                    HandleHello(p, data, welcomeBack: false);
                    break;
                case NetProtocol.MsgShipState:
                    HandleShipFrame(p, data);
                    break;
                case NetProtocol.MsgHudState:
                    HandleHudState(p, data);
                    break;
                case NetProtocol.MsgEvent:
                    HandleEvent(p, data);
                    break;
                case NetProtocol.MsgWorldSnapshot:
                    HandleWorldSnapshot(p, data);
                    break;
                }
            }
        }

        private static void HandleHello(PeerChannel p, byte[] data, bool welcomeBack)
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
                RefusePairing(p, NetProtocol.RejectVersion);
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
                RefusePairing(p, NetProtocol.RejectBuild);
                return;
            }
            // ?netallowdebug waives OUR OWN half of this only. The peer's bit still refuses --
            // whoever carries the flags is the one who opts in, so an unflagged player can
            // never be silently paired into a flagged run by the other side setting a flag.
            // (A flagged peer that has opted in presents clean anyway, so its bit is absent.)
            bool localDebugRefuses = NetHost.Current.DebugActive && !NetHost.Current.NetAllowDebug;
            if (menuSession && ((peerFlags & NetProtocol.HelloFlagDebugActive) != 0 || localDebugRefuses))
            {
                Console.WriteLine("[net] gameplay debug flags active in a menu session -- rejecting");
                RefusePairing(p, NetProtocol.RejectFlags);
                return;
            }
            p.PeerId = helloPeerId;
            // Card 0b8a300b: the block gate. Checked here because the hello is the ONE point
            // both rejoin routes converge on (the public browser and a typed room code), and
            // because it is BEFORE PeerConnected/slot reservation -- a blocked peer re-pairing
            // never reaches the world, so repeated attempts cost the host a re-pair and nothing
            // else. peerId 0 = the peer could not produce a token; never blockable, so a broken
            // localStorage can't get someone refused by accident.
            if (isHost && IsPeerBlocked(helloPeerId))
            {
                Console.WriteLine("[net] blocked peer tried to rejoin -- rejecting");
                RefusePairing(p, NetProtocol.RejectBanned);
                return;
            }
            // Slot allocation (card 4d904410). The host reserves the joiner's primary seat the
            // moment it knows a real peer is there -- BEFORE replying -- so its own couch joins
            // and AI friends can never be handed the same slot. The client adopts what it is
            // given. Both are idempotent: hellos repeat at 1 Hz until the pairing settles.
            if (isHost)
            {
                if (!ReserveRemotePrimarySlot(p, peerBlockedSlots))
                {
                    return; // refused (no seat free on both sides) -- RefusePairing owned it
                }
            }
            else if (grantedSlot != NetProtocol.SlotNone)
            {
                AdoptGrantedPrimarySlot(p, grantedSlot);
            }
            if (welcomeBack)
            {
                // ADDRESSED: the welcome carries THIS peer's granted seat, which is exactly the
                // field that made the old broadcast wrong the moment a second joiner existed.
                transport.SendReliableTo(p.Id, NetProtocol.EncodeWelcome(ProtocolVersion, isHost, localBuildHash, LocalHelloFlags(),
                    isHost ? p.PrimarySlot : NetProtocol.SlotNone, localPeerId, LocalBlockedSlots()));
            }
            if (!p.Up)
            {
                PeerConnected(p);
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
        private static bool ReserveRemotePrimarySlot(PeerChannel p, byte peerBlocked)
        {
            if (p.PrimarySlot != NetProtocol.SlotNone)
            {
                if (!NetProtocol.SlotInMask(peerBlocked, p.PrimarySlot))
                {
                    return true;
                }
                // The joiner has told us the seat we are holding does not work for it (its roster
                // changed between our grant and its hello). Release and re-pick against the new
                // mask rather than leaving it stranded -- it keeps helloing until its slot
                // settles, so this converges: we never re-offer a seat the mask still blocks.
                Console.WriteLine("[net] joiner cannot take granted slot=" + p.PrimarySlot + " -- re-allocating");
                if (p.Primary.Puppet != null)
                {
                    // The peer streams as soon as it is PeerUp, which can be BEFORE the slot
                    // exchange settles -- so a puppet may already be flying in the seat we are
                    // about to give up. ManagePuppet only re-adopts a ship the scene spawned; it
                    // never re-stamps a live one's Owner, so leaving it would strand the remote
                    // player in the old slot while the wire, EvScoreSync and EvBlast all moved to
                    // the new one. Drop it and let the next stream rebuild it in the right seat.
                    ExplodePuppet(p.Primary);
                }
                oracle.RemovePlayerAt(p.PrimarySlot, ControlDevice.Remote);
                p.PrimarySlot = NetProtocol.SlotNone;
            }
            int slot = FindLeftoverRemoteSeat(p);
            if (slot >= 0 && NetProtocol.SlotInMask(peerBlocked, slot))
            {
                // A leftover Remote registration the joiner cannot use (one that outlived a
                // restarted session, or was re-seated by SpawnAllPlayers). Free it before
                // re-picking, or we would hand out a second seat and leave this one squatting
                // the roster.
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
                    RefusePairing(p, NetProtocol.RejectFull);
                    return false;
                }
                if (!oracle.AddPlayerAt(slot, ControlDevice.Remote))
                {
                    return true; // lost a race for the seat -- retry on the next hello
                }
            }
            p.PrimarySlot = (byte)slot;
            Console.WriteLine("[net] granted joiner primary slot=" + slot);
            return true;
        }

        // A Remote registration no OTHER channel claims (one that outlived a restarted session,
        // or was re-seated by SpawnAllPlayers), reusable by `forPeer`. With several remote peers
        // a bare GetPlayerIndex(Remote) scan would happily return a seat a LIVE peer is flying
        // in -- the ambiguity every slot decision here now avoids by keying off the channels.
        private static int FindLeftoverRemoteSeat(PeerChannel forPeer)
        {
            for (int i = 0; i < Oracle.MaxPlayers; i++)
            {
                if (!oracle.IsSeated(i) || oracle.Controller(i) != ControlDevice.Remote)
                {
                    continue;
                }
                bool claimed = false;
                foreach (PeerChannel q in peers.Values)
                {
                    if (q != forPeer && !q.Refused && q.PrimarySlot == i)
                    {
                        claimed = true;
                        break;
                    }
                }
                if (!claimed)
                {
                    return i;
                }
            }
            return -1;
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
        private static void AdoptGrantedPrimarySlot(PeerChannel p, byte slot)
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
            SlotAdopt action = DecideSlotAdopt(localPrimarySlot, slot, p.PrimarySlot,
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
            p.PrimarySlot = HostPrimarySlot;
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
            SendEventToSessionPeers(seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvJoinRequest));
        }

        // The one seat allocator, used for our own couch joins and for answering the peers'. It
        // must never hand out a seat ANY peer's PRIMARY ship occupies: in the menu-lobby flow
        // that reservation is made before the level launches and Game1.MenuFinished's
        // ResetPlayers() wipes it, and SpawnPuppet only re-asserts it once the peer's first live
        // sample lands (seconds later). A couch join landing in that window would take the seat
        // and leave that remote player permanently unseatable.
        private static int AllocateSeat()
        {
            List<int> peerPrimaries = new List<int>(4);
            foreach (PeerChannel p in peers.Values)
            {
                if (!p.Refused && p.PrimarySlot != NetProtocol.SlotNone)
                {
                    peerPrimaries.Add(p.PrimarySlot);
                }
            }
            return AllocateSeatFrom(oracle, localPrimarySlot, peerPrimaries);
        }

        // The allocation itself, as a function of a roster and the primary seats to exclude, so
        // the reserve -> hold -> expire -> REALLOCATE cycle can be driven against a scratch
        // Oracle with no session (eaSlotTest). That cycle is the only proof that a seat the host
        // reclaims from an unclaimed grant is genuinely re-usable rather than merely released.
        // The single-peer overload is the suite's original surface; the list form is the ONE
        // walk AllocateSeat ships, so the suite exercises the live path rather than a copy that
        // could drift (review finding on card 87242257).
        internal static int AllocateSeatFrom(Oracle roster, int localPrimary, int peerPrimary)
        {
            return AllocateSeatFrom(roster, localPrimary, new[] { peerPrimary });
        }

        internal static int AllocateSeatFrom(Oracle roster, int localPrimary, IReadOnlyList<int> peerPrimaries)
        {
            for (int slot = roster.FirstFreeSlot(); slot >= 0; slot = roster.FirstFreeSlot(slot + 1))
            {
                if (slot == localPrimary)
                {
                    continue;
                }
                bool held = false;
                for (int i = 0; i < peerPrimaries.Count; i++)
                {
                    if (peerPrimaries[i] == slot)
                    {
                        held = true;
                        break;
                    }
                }
                if (!held)
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

        // HOST: a client couch player pressed Start. Allocate a seat and answer -- ADDRESSED to
        // the asking peer, so another client mid-join-request can never adopt a grant that was
        // not its (card 87242257); the seat is held as a RemoteFriend registration right away so
        // the next allocation can't reuse it while the grant is still in flight.
        private static void HandleJoinRequest(PeerChannel from)
        {
            int slot = AllocateSeat();
            if (slot >= 0 && !oracle.AddPlayerAt(slot, ControlDevice.RemoteFriend))
            {
                slot = -1;
            }
            byte granted = slot < 0 ? NetProtocol.SlotNone : (byte)slot;
            SendEventToPeer(from, seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvSlotGrant, granted));
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
            // One granted primary per channel; '+'-joined past the first, so a 2-peer log keeps
            // its exact pre-11.9 shape (pri=0/1) and a 3-peer one reads pri=0/1+2.
            string peerPris = "";
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Refused || p.PrimarySlot == NetProtocol.SlotNone)
                {
                    continue;
                }
                peerPris += (peerPris.Length > 0 ? "+" : "") + p.PrimarySlot;
            }
            return (s.Length > 0 ? s : "-")
                + " pri=" + localPrimarySlot + "/" + (peerPris.Length > 0 ? peerPris : "-")
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

        // Refuse ONE pairing (card 87242257). With another peer already up the refusal is
        // PER-PEER: a blocked griefer or stale-build straggler knocking on a live 3-player game
        // must not end it -- the addressed reject needs no egress grace, because the session
        // (and its transport) stays open. With nobody else up the pre-11.9 whole-session
        // wind-down stands, notice included: a client refusing its host, an empty lobby, a
        // listed game's first joiner -- there the session had nothing else to live for and the
        // player is owed the reason.
        private static void RefusePairing(PeerChannel p, byte reason)
        {
            if (!isHost || p == null || UpPeerCountExcept(p) == 0)
            {
                SendRejectOnce(reason);
                return;
            }
            transport.SendReliableTo(p.Id, NetProtocol.EncodeReject(reason));
            Console.WriteLine("[net] refused peer '" + p.Id + "' (reason=" + reason
                + ") -- session continues with the others");
            DetachRefusedPeer(p);
        }

        // Turn one channel into a refused husk: down, latched Refused (frames dropped until the
        // sweep or its bye), seats freed, aggregates re-synced. Shared by RefusePairing and the
        // INBOUND-reject mirror in HandleReject -- the refusal is symmetric by design, so both
        // directions must leave the same state behind.
        private static void DetachRefusedPeer(PeerChannel p)
        {
            bool wasUp = p.Up;
            p.Up = false;
            p.Refused = true;
            p.RemoveAtMs = NowMs + RefusedChannelSweepMs;
            if (wasUp)
            {
                // It had a world footprint (a stream-first reconnect that then failed its
                // handshake): free it and tell the others -- the departure path, minus the
                // channel removal, which stays as the refusal latch.
                byte mask = PeerSlotMask(p);
                if (p.RemotePaused)
                {
                    p.RemotePaused = false;
                    SyncRemotePauseToScene();
                }
                ClearPeerStalled(p);
                if (kickOfferPeer == p)
                {
                    kickOfferPeer = null;
                }
                ReleasePeerSeats(p);
                if (mask != 0)
                {
                    SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvPeerLeft, mask));
                }
                SyncPauseAggregateToPeers();
            }
            else if (p.PrimarySlot != NetProtocol.SlotNone)
            {
                // A reservation made before the refusal (defensive -- today every reject fires
                // before the seat is reserved).
                oracle.RemovePlayerAt(p.PrimarySlot, ControlDevice.Remote);
                p.PrimarySlot = NetProtocol.SlotNone;
            }
        }

        private static int UpPeerCountExcept(PeerChannel except)
        {
            int n = 0;
            foreach (PeerChannel q in peers.Values)
            {
                if (q.Up && q != except)
                {
                    n++;
                }
            }
            return n;
        }

        // Deliberately channel-OPTIONAL: the refused side often has no channel for the sender
        // (a client pre-handshake, an over-capacity newcomer), and a reject means the most
        // exactly then -- see DrainRx's early routing.
        private static void HandleReject(string from, byte[] data)
        {
            if (data.Length < 2)
            {
                return;
            }
            peers.TryGetValue(from ?? "", out PeerChannel p);
            if (p != null && p.Refused)
            {
                return; // we already turned this one away; its symmetric reject is old news
            }
            if (!isHost && peers.Count > 0 && p == null)
            {
                return; // bus hygiene: once paired, a reject from anyone but our host is noise
            }
            // A HOST with OTHER peers up: one straggler refusing ITS pairing (a stale build's
            // symmetric detection firing before our own refusal reached it, a mismatched dev
            // tab) must not end a live match -- the receive-side mirror of RefusePairing.
            if (isHost && UpPeerCountExcept(p) > 0)
            {
                Console.WriteLine("[net] peer " + (p != null ? "'" + p.Id + "' " : "")
                    + "rejected its pairing (reason=" + data[1] + ") -- session continues with the others");
                if (p != null)
                {
                    DetachRefusedPeer(p);
                }
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

        private static void PeerConnected(PeerChannel p)
        {
            p.Up = true;
            p.LastRxStreamAt = NowMs;
            p.PauseSentTo = false;
            Console.WriteLine("[net] peer connected (" + (isHost ? "join" : "host") + " side is up)"
                + (isHost && UpPeerCount() > 1 ? " -- " + UpPeerCount() + " peers" : ""));
            if (isHost)
            {
                if (listedSession || menuSession)
                {
                    // Join-in-progress: the joiner paired with our game while we are already
                    // mid-level -- a stranger off a LISTED room, or (card 0257f8ba, capacity-4
                    // rooms) a friend arriving by code / a 3rd-4th stranger after the session
                    // started. Launch it into our current level+difficulty (it is a menu-session
                    // client that mirrors EvLaunch); its EvReady then triggers the live-world
                    // replay below plus the deep scenery catch-up (background ops / music cue),
                    // and the 1 Hz EvScoreSync trues up score/lives. ADDRESSED: a second
                    // joiner's arrival must not re-launch the peers already playing. At the
                    // MENUS (scene null -- the lobby) nothing is sent: the launch comes from
                    // the host's own pick (SendLaunch), exactly as before.
                    INetScene scene = NetScene.Current;
                    if (scene != null)
                    {
                        SendEventToPeer(p, seq => NetProtocol.EncodeLaunchEvent(seq,
                            (byte)scene.Level, (byte)Settings.GetInstance().CurrentDifficulty));
                        Console.WriteLine("[net] jip launch level=" + scene.Level
                            + " difficulty=" + Settings.GetInstance().CurrentDifficulty);
                    }
                    else if (menuSession)
                    {
                        // Card 0257f8ba: the newcomer's waiting panel wants the roster NOW,
                        // and the edge-triggered broadcast only fires when the mask CHANGES --
                        // a re-keyed reconnect changes nothing and would never learn it.
                        SendEventToPeer(p, seq => NetProtocol.EncodeByteEvent(seq,
                            NetProtocol.EvLobbyRoster, (byte)ComputeLobbyRosterMask()));
                    }
                }
                // Late joiner: replay the live NetId set so it can construct the already-
                // alive world instead of starting from a death-before-spawn storm. ADDRESSED
                // to the newcomer -- the peers already caught up must not be re-blasted with
                // the whole live set (card 87242257; the EvReady replay routes the same way).
                replayTarget = p;
                try
                {
                    NetIdRegistry.ReplayLive();
                }
                finally
                {
                    replayTarget = null;
                }
                // A held pause (ours or another client's) reaches the newcomer through the
                // per-recipient aggregate.
                SyncPauseAggregateToPeers();
                return;
            }
            if (localPaused)
            {
                // Client: re-announce a held pause across a reconnect so the host re-freezes.
                SendEventToPeer(p, seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvPause, 1));
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

        // The other match-end shape: every peer goes but WE stay in our level, playing solo.
        //
        // Freeing the Remote seats matters as much as exploding the puppets: leave one
        // registered and oracle.Players stays high, so NetListing never re-lists and a phantom
        // score panel lingers. Couch players a peer brought (card 4d904410) go the same way --
        // our level keeps running, so nothing else would ever purge their puppets or free their
        // seats.
        private static void RevertToSinglePlayer(string reason)
        {
            foreach (PeerChannel p in PeersSnapshot())
            {
                ReleasePeerSeats(p);
            }
            Stop(reason);
        }

        // THE MATCH-END POLICY'S CLIENT-DEPARTURE HALF (card 87242257, the design decision the
        // card asked for): a CLIENT leaving -- clean EvLeave, drop verdict, closed tab -- frees
        // its seats and play continues for everyone else; only the HOST leaving ends the match.
        // The listedSession peer-loss semantic, generalised to every host-side session kind.
        //
        // The departed peer's seats are freed here AND on every remaining client (EvPeerLeft --
        // they cannot infer "gone for good" from the relay going quiet, which is also what a
        // hiccup looks like). When the LAST client goes: mid-level the host reverts to plain
        // single-player (a listed game re-lists once Active falls -- its old room died with the
        // transport); at the MENUS a lobby host keeps its room and waits for new players
        // (card 0257f8ba -- before 11.10 a peerless lobby was a dead end and Stopped).
        private static void ReleaseDepartedPeer(PeerChannel p, string reason)
        {
            byte mask = PeerSlotMask(p);
            if (p.RemotePaused)
            {
                p.RemotePaused = false;
                SyncRemotePauseToScene();
            }
            ClearPeerStalled(p);
            if (kickOfferPeer == p)
            {
                kickOfferPeer = null;
            }
            ReleasePeerSeats(p);
            peers.Remove(p.Id);
            if (mask != 0)
            {
                SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvPeerLeft, mask));
            }
            SyncPauseAggregateToPeers();
            if (AnyPeerUp())
            {
                Console.WriteLine("[net] " + reason + " -- " + UpPeerCount()
                    + " peer(s) remain, playing on (seats freed: mask " + mask + ")");
                return;
            }
            if (NetScene.Current != null)
            {
                RevertToSinglePlayer(reason);
                return;
            }
            if (menuSession && isHost)
            {
                // Card 0257f8ba: a menu-lobby HOST whose last guest left keeps its room. The
                // session idles peerless (the broadcast hello resumes, which is how the next
                // pairing initiates), the signaling room is still registered (a >2-capacity ws
                // stays open for the room's whole life -- webrtc.js), and the lobby panel just
                // reads "waiting for players" again. Pre-11.10 this was the documented dead
                // end: zero peers at the menus meant Stop + notice, because the room could not
                // take a replacement anyway.
                Console.WriteLine("[net] " + reason + " -- lobby is empty, room stays open for new players");
                return;
            }
            Stop(reason, "The other player left\nMatch ended");
        }

        // The departed peer's roster footprint -- its primary seat plus every couch/AI seat it
        // streamed -- as the EvPeerLeft slot mask.
        private static byte PeerSlotMask(PeerChannel p)
        {
            byte mask = 0;
            if (p.PrimarySlot != NetProtocol.SlotNone && p.PrimarySlot < Oracle.MaxPlayers)
            {
                mask |= NetProtocol.SlotBit(p.PrimarySlot);
            }
            foreach (byte slot in p.Extras.Keys)
            {
                if (slot < Oracle.MaxPlayers)
                {
                    mask |= NetProtocol.SlotBit(slot);
                }
            }
            return mask;
        }

        // The visible half of a peer's departure, without any teardown -- the kick path needs
        // these separable, because it must free the world NOW but keep the transport alive for
        // the egress grace (Stop() closes it). Seat release is SLOT-keyed: with several remote
        // peers a ControlDevice.Remote scan is ambiguous, and each channel knows its own seat.
        private static void ReleasePeerSeats(PeerChannel p)
        {
            if (p.Primary.Puppet != null)
            {
                ExplodePuppet(p.Primary);
            }
            if (p.PrimarySlot != NetProtocol.SlotNone)
            {
                oracle.RemovePlayerAt(p.PrimarySlot, ControlDevice.Remote);
            }
            ReleaseAllFriendPuppets(p);
        }

        // Host-only, card 0b8a300b: once the peer's pause has outlasted KickOfferDelayMs, swap
        // the passive "OTHER PLAYER PAUSED" curtain for the interactive kick menu.
        //
        // The timing lives HERE, not in GameScene or the overlay, for the reason the whole card
        // exists: Push() disables every collection component, GameScene included, so neither can
        // tick during the freeze. NetSession.Update is driven straight from Game1.UpdateInner,
        // which makes it the one clock still running -- and it is already real-time (NowMs), the
        // right basis for a frozen world where gameTime means nothing.
        private static void TickKickOffer(PeerChannel p, long now)
        {
            if (!p.RemotePaused)
            {
                p.KickOfferShown = false;
                p.RemotePauseAt = 0;
                return;
            }
            if (p.KickOfferShown || p.RemotePauseAt == 0 || now - p.RemotePauseAt < KickOfferDelayMs)
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
            // exactly the griefing hole this card closes. The kick TARGET latches with it: the
            // menu is one shared surface (11.10 owns a per-peer kick UI), so it acts on the peer
            // whose pause earned the offer.
            p.KickOfferShown = scene.NetShowKickMenu();
            if (p.KickOfferShown)
            {
                kickOfferPeer = p;
            }
        }

        // The peer the showing kick menu is about (card 87242257) -- the pause-holder whose
        // offer fired. Null whenever no offer is up.
        private static PeerChannel kickOfferPeer;

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
            KickPeerCore(block, explicitTarget: null);
        }

        // Card 0257f8ba: the host pause menu's per-peer kick rows name their target by SEAT --
        // with up to three remote machines "kick the peer" stopped being a well-formed request.
        // A slot that matches no up peer is a no-op (the peer left between the menu opening and
        // the row being chosen; NetHostMenu retracts itself on that edge anyway).
        public static void KickPeerAt(int slot, bool block)
        {
            if (!Active || !isHost)
            {
                return;
            }
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Up && p.PrimarySlot == slot)
                {
                    KickPeerCore(block, p);
                    return;
                }
            }
        }

        // The up peers' granted primary seats as a slot mask -- what the host pause menu builds
        // its per-peer kick rows from (card 0257f8ba). A peer whose slot exchange has not
        // settled contributes nothing; NetHostMenu falls back to the slotless kick pair then.
        internal static byte UpPeerPrimarySlotsMask()
        {
            byte mask = 0;
            foreach (PeerChannel p in peers.Values)
            {
                if (p.Up && p.PrimarySlot != NetProtocol.SlotNone && p.PrimarySlot < Oracle.MaxPlayers)
                {
                    mask |= NetProtocol.SlotBit(p.PrimarySlot);
                }
            }
            return mask;
        }

        private static void KickPeerCore(bool block, PeerChannel explicitTarget)
        {
            if (!Active || !isHost || pendingStopAt != 0)
            {
                return;
            }
            // The named target (a per-peer menu row), else the peer the offer was about, then
            // falling back through "a paused peer" to "the only up peer" so the
            // ?netkickshot/eaKickTest shapes (no offer latched) keep working.
            PeerChannel target = explicitTarget ?? kickOfferPeer;
            if (target == null || !target.Up)
            {
                target = null;
                foreach (PeerChannel p in peers.Values)
                {
                    if (p.Up && (target == null || p.RemotePaused))
                    {
                        target = p;
                        if (p.RemotePaused)
                        {
                            break;
                        }
                    }
                }
            }
            if (target == null)
            {
                return;
            }
            kickOfferPeer = null;
            ApplyKickBlock(block, target.PeerId);
            SendEventToPeer(target, seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvKick, (byte)(block ? 1 : 0)));
            Console.WriteLine("[net] kicked the peer"
                + (target.PrimarySlot != NetProtocol.SlotNone ? " (slot " + target.PrimarySlot + ")" : "")
                + (block ? " (blocked for this level)" : ""));
            // Release the freeze BEFORE the seat release: the world may still be pushed under
            // the kicked player's pause, and the aggregate sync is what pops it (another peer's
            // held pause keeps it frozen, correctly).
            if (target.RemotePaused)
            {
                target.RemotePaused = false;
                SyncRemotePauseToScene();
            }
            target.Up = false;
            target.KickOfferShown = false;
            byte mask = PeerSlotMask(target);
            ReleasePeerSeats(target);
            // The channel is kept, Refused, so the kicked player's still-flowing frames are
            // dropped cheaply while the addressed EvKick gets its egress time; its bye (or the
            // sweep) removes it. The rest of the session -- other peers included -- plays on.
            target.Refused = true;
            target.RemoveAtMs = NowMs + RefusedChannelSweepMs;
            if (mask != 0)
            {
                SendEventToSessionPeers(seq => NetProtocol.EncodeByteEvent(seq, NetProtocol.EvPeerLeft, mask));
            }
            SyncPauseAggregateToPeers();
            if (!AnyPeerUp())
            {
                // Nobody left. Deliberately NOT RevertToSinglePlayer() -- its Stop() would close
                // the transport and abort the EvKick we just queued. The deadline does the
                // Stop() instead; until then the session is Active with no peer, which Update
                // handles by taking the pendingStopAt branch and nothing else.
                pendingStopReason = "kick teardown";
                pendingStopNotice = null;
                pendingStopAt = NowMs + RejectGraceMs;
            }
        }

        // "Keep Waiting": the host declined this offer, so hide the menu and start the delay
        // again. Waiting once must not forfeit the option -- a griefer holding pause forever
        // would otherwise get exactly one refusal and then a permanently frozen host.
        public static void RearmKickOffer()
        {
            PeerChannel p = kickOfferPeer;
            kickOfferPeer = null;
            if (p == null)
            {
                return;
            }
            p.RemotePauseAt = NowMs;
            p.KickOfferShown = false;
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
        // peer did NOT recover and saying so would be a lie. Banner via the aggregate: another
        // peer still stalled keeps it up.
        private static void ClearPeerStalled(PeerChannel p)
        {
            if (!p.Stalled)
            {
                return;
            }
            p.Stalled = false;
            SyncStallBannerToScene();
        }

        // `recovered` distinguishes the two ways the banner drops: the stream actually came
        // back, versus the peer announcing a pause (which suppresses the banner but leaves
        // the stream just as quiet -- lastRxStreamAt is only refreshed by the STREAM lane:
        // ship state, friend state and snapshots, never by an event; that source list is also
        // what TickFriends' link-quiet arm means by "the link is alive", card 14c5943e).
        // Claiming a recovery in the second case would be a
        // lie, and a backgrounded tab bursting out a late EvPause hits it routinely.
        private static void SetPeerStalled(PeerChannel p, bool on, bool recovered)
        {
            if (on == p.Stalled)
            {
                return;
            }
            if (!on)
            {
                ClearPeerStalled(p);
                if (recovered)
                {
                    Console.WriteLine("[net] peer recovered");
                }
                return;
            }
            p.Stalled = true;
            Console.WriteLine("[net] peer stalled (stream quiet > " + PeerStallMs + "ms) -- grace running");
            SyncStallBannerToScene();
        }

        private static void PeerLost(PeerChannel p, string reason)
        {
            if (!p.Up)
            {
                return;
            }
            p.Up = false;
            Console.WriteLine("[net] peer lost (" + reason + ")");
            if (!isHost)
            {
                // The lost peer IS our host -- and the host leaving ends the match, the 11.9
                // policy's other half (no host migration).
                if (menuSession)
                {
                    EndMatchPeerGone("peer lost: " + reason, "The other player disconnected\nMatch ended");
                    return;
                }
                DevSessionPeerDown(p);
                return;
            }
            if (menuSession || listedSession)
            {
                // A CLIENT departed: free its seats and play on (the card's design decision) --
                // the session only ends when the last one goes, and even then only at the menus.
                ReleaseDepartedPeer(p, "peer lost: " + reason);
                return;
            }
            DevSessionPeerDown(p);
        }

        // The dev `?net=` shape: the session survives and the SAME pairing can resume, so the
        // channel is kept -- its granted seat included -- and only the world-facing state is
        // dropped, exactly as the singletons were.
        private static void DevSessionPeerDown(PeerChannel p)
        {
            ClearPeerStalled(p);
            p.Primary.Alive = false;
            if (p.Primary.Puppet != null)
            {
                // Remove the puppet NOW (with the death FX) -- ManagePuppet won't, it
                // early-returns while the peer is down.
                ExplodePuppet(p.Primary);
            }
            p.Primary.ClearSamples();
            p.Primary.HasLastPuppetPos = false;
            p.Primary.HaveRxSeq = false;
            p.LastRxEventSeq = -1;
            if (p.RemotePaused)
            {
                // Never leave the world frozen by a peer that's gone.
                p.RemotePaused = false;
                SyncRemotePauseToScene();
                SyncPauseAggregateToPeers();
            }
        }

        // ONE receive path for every ship on the wire (card b2828be8): the frame routes by its
        // PRIMARY flag -- the peer's primary keeps the heartbeat / alive-edge / script-gate
        // semantics, every other slot keeps the timeout-death extra-ship semantics. The
        // asymmetries are per-CHANNEL behaviour now, not per-message-type.
        private static void HandleShipFrame(PeerChannel p, byte[] data)
        {
            if (!NetProtocol.TryDecodeShipState(data, out byte slot, out bool primary, out ushort seq, out ShipSample sample, out int shots, out float bulletLife))
            {
                return;
            }
            p.LastRxStreamAt = NowMs;
            if (!p.Up)
            {
                // Stream before/without a finished handshake (e.g. we reloaded mid-session):
                // treat it as the peer being up -- the stream IS the heartbeat.
                PeerConnected(p);
            }
            if (!primary)
            {
                // The extra-ship half (couch players, AI friends) -- NetSession.Friends.cs.
                HandleExtraShipFrame(p, slot, sample, shots, bulletLife);
                return;
            }
            // (The slot byte on a primary frame is carried but not consumed at 2 peers -- the
            // receiver's own granted seat is authoritative. It is what card 87242257's host
            // relay re-addresses by.)
            ShipChannel ch = p.Primary;
            metrics.StreamRx++;
            if (ch.HaveRxSeq && (ushort)(seq - ch.LastRxSeq) != 1)
            {
                // Loopback delivers in order; count anything else so the WebRTC transport
                // (11.4) gets loss/reorder visibility for free. Distinct from StreamDropped
                // (the buffer's authoritative sample-refused count) so neither double-counts.
                metrics.StreamSeqGaps++;
            }
            ch.LastRxSeq = seq;
            ch.HaveRxSeq = true;
            // Card df72b051: a RESPAWN starts the puppet from its own samples. While the peer's
            // ship is dead the stream keeps flowing as the heartbeat with pos = lastTxPos -- the
            // position the ship DIED at, repeated for the whole death -- and every one of those
            // samples lands in this buffer. Without the clear, the render clock (~InterpDelayMs
            // behind the newest sample) reads those dead-period samples first on the respawn, so
            // the puppet materialised at the old death spot and visibly slid across the screen to
            // the real spawn point. Skipping the dead Adds instead is NOT enough: the buffer's
            // trim always keeps the last pre-death sample, so the bracketing pair straddling the
            // death gap survives and the bridge remains. Cleared on the rising edge, before the
            // first alive sample is added, so the interpolator can never bridge a death.
            //
            // BOTH the edge and the alive latch are gated on the sample being IN-ORDER (the same
            // T test buffer.Add applies): the stream lane is unordered, so a stale dead heartbeat
            // delivered after the respawn's first alive packets would otherwise flip the latch off
            // a sample the buffer refuses -- exploding the healthy puppet on the fake falling edge
            // and wiping the fresh buffer when the next alive packet re-arms the rising one.
            // The renderMs/hasLastPuppetPos resets are belt-and-braces for the ADOPT path (a
            // scene-spawned ship taken into the Remote seat while the peer was dead, where no
            // SpawnPuppet runs to reset them); on the ordinary respawn SpawnPuppet clears both.
            if (!ch.Buffer.HasSamples || sample.T > ch.Buffer.NewestMs)
            {
                if (sample.Alive && !ch.Alive)
                {
                    ch.ClearSamples();
                    ch.HasLastPuppetPos = false;
                }
                ch.Alive = sample.Alive;
            }
            // Card 8a7772d6. HOST ONLY: the world is host-authoritative, and a client's own
            // bit describes a script that never runs. Latched raw off the newest sample, with
            // no edge detection here on purpose -- the edge belongs to the SCENE, which is not
            // guaranteed to exist when the packet lands (a join-in-progress peer is still
            // warming its level while this stream is already flowing).
            if (!isHost)
            {
                p.ScriptGate = sample.ScriptGate;
            }
            ch.ShotsPerSec = shots;
            ch.BulletLife = bulletLife;
            if (!ch.Buffer.Add(sample))
            {
                metrics.StreamDropped++;
            }
        }

        private static void HandleWorldSnapshot(PeerChannel p, byte[] data)
        {
            if (isHost || !NetProtocol.TryReadSnapshotHeader(data, out byte count, out ushort packetSeq))
            {
                return;
            }
            p.LastRxStreamAt = NowMs;
            metrics.SnapRx++;
            if (NetScene.Current == null)
            {
                // Menu-lobby flow: the host may be in-level while we're still warming --
                // don't build puppets into a menu world; EvReady triggers a replay once
                // our scene is up. (Counts as heartbeat above either way.)
                return;
            }
            int off = NetProtocol.SnapshotHeaderBytes;
            for (int i = 0; i < count; i++)
            {
                if (!NetProtocol.TryReadSnapshotEntry(data, ref off, out ushort netId, out byte typeIdx, out byte entryFlags, out NetBaseState state, out int extraOff, out int extraLen))
                {
                    break;
                }
                metrics.SnapEntriesRx++;
                bool applied = NetPuppets.OnSnapshotEntry(netId, typeIdx, entryFlags, state, data,
                    extraOff, extraLen, packetSeq, out bool popped, out SnapUnknownKind kind,
                    out bool stale);
                if (stale)
                {
                    // NOT a fault and not counted as an unknown id: the entry decoded fine and
                    // named a puppet we hold, it was simply older than the sample already applied
                    // to it. On an unimpaired link this stays at 0; it tracks the link's reorder
                    // rate, so it is the observable for "how much backwards drag is this
                    // connection producing" (card f5cf7a5c).
                    //
                    // COUNTED BEFORE `applied` IS BRANCHED ON, and that is deliberate: under
                    // ?netstaleguard=0 the entry is stale AND applied, and the flag must change
                    // only the drag, never the measurement it exists to let you take. Counting
                    // this inside the not-applied arm made the negative control silently stop
                    // reporting -- found by mutation-testing NetStaleTest, not by review.
                    metrics.SnapStale++;
                }
                if (applied)
                {
                    if (popped)
                    {
                        metrics.PuppetPops++;
                    }
                }
                else if (!stale)
                {
                    // `!stale` because a refused STALE entry is not an unknown id -- the id was
                    // ours and the entry decoded perfectly. Folding it in here would put the
                    // link's reorder rate into snapUnk, which is exactly the conflation card
                    // 48ab9b2f split these counters to remove.
                    //
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

        private static void HandleEvent(PeerChannel p, byte[] data)
        {
            if (data.Length < 4)
            {
                return;
            }
            byte eventType = data[1];
            int seq = NetProtocol.ReadU16(data, 2);
            metrics.EventsRx++;
            if (p.LastRxEventSeq >= 0 && (ushort)(seq - p.LastRxEventSeq) != 1)
            {
                metrics.SeqGaps++;
            }
            p.LastRxEventSeq = seq;
            switch (eventType)
            {
            case NetProtocol.EvSpawn:
            {
                if (isHost || NetScene.Current == null
                    || !NetProtocol.TryDecodeSpawnEvent(data, out ushort id, out byte typeIdx, out NetBaseState state, out int extraOff, out int extraLen))
                {
                    return;
                }
                // Count WHICH way it failed, not just that it did (card 4c9448c8). dup stays the
                // sum; dupBad is the only member that means something is wrong, and it is the
                // one the co-op verification bar asserts at 0.
                SpawnRejectKind reject = NetPuppets.OnSpawn(id, typeIdx, state, data, extraOff, extraLen);
                if (reject != SpawnRejectKind.None)
                {
                    metrics.DupSpawns++;
                    switch (reject)
                    {
                    case SpawnRejectKind.AlreadyLive:
                        metrics.DupLive++;
                        break;
                    case SpawnRejectKind.Declined:
                        metrics.DupDeclined++;
                        break;
                    default:
                        metrics.DupBad++;
                        break;
                    }
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
                // Wire slot == oracle slot on both peers. Clamped at the decode boundary like
                // every other raw wire value -- see NetProtocol.ClampKillerSlot for why this
                // one degrades to KillerNone instead of dropping the message.
                byte killer = NetProtocol.ClampKillerSlot(data[6]);
                Vector2 pos = new Vector2(NetProtocol.ReadF32(data, 7), NetProtocol.ReadF32(data, 11));
                NetPuppets.OnRemoteDeath(id, killer, pos);
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] rx death id=" + id + " killer=" + killer);
                }
                break;
            }
            case NetProtocol.EvDying:
            {
                // A deferred death has begun on the host (card f62116b5). Scene-gated like every
                // world message; the settlement still arrives as the EvDeath that follows.
                if (isHost || NetScene.Current == null
                    || !NetProtocol.TryDecodeDyingEvent(data, out ushort dyingId))
                {
                    return;
                }
                NetPuppets.OnDeathBegan(dyingId);
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] rx dying id=" + dyingId);
                }
                break;
            }
            case NetProtocol.EvClaim:
            {
                if (!isHost || data.Length < 7)
                {
                    return;
                }
                // Clamped like EvDeath's copy of the same byte -- one decode-boundary reader for
                // both, per NetProtocol's validation contract. KillerSelf is a legal inbound
                // value here (a client's own mine puppet can self-destruct on its screen) and is
                // simply not payable, exactly as KillerNone is not.
                HandleClaim(NetProtocol.ReadU16(data, 4), NetProtocol.ClampKillerSlot(data[6]));
                break;
            }
            case NetProtocol.EvScoreSync:
            {
                // LIVES ONLY since v20 (card af96bcc2): the per-slot totals it used to carry
                // were the second writer, and they ride MsgHudState owner-sourced now.
                if (isHost || data.Length < 5 || NetScene.Current == null)
                {
                    return;
                }
                score.Lives = (sbyte)data[4];
                break;
            }
            case NetProtocol.EvBlast:
            {
                if (data.Length < 14)
                {
                    return;
                }
                byte blastSlot = data[4];
                int level = data[13];
                // Hub relay (card 87242257): a client's bomb must detonate on EVERY screen, so
                // the host re-emits it to the other clients under their own seqs -- before its
                // own apply gates, since a beat this world cannot use right now may still be
                // usable over there.
                Vector2 blastPos = new Vector2(NetProtocol.ReadF32(data, 5), NetProtocol.ReadF32(data, 9));
                RelayFromClient(p, seq => NetProtocol.EncodeBlastEvent(seq, blastSlot, blastPos, level));
                if (NetScene.Current == null)
                {
                    return;
                }
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
            case NetProtocol.EvRespawn:
            {
                // Card 37f3a663: the peer's ship died and its respawn clock is running -- draw the
                // same indicator here so this player can see their buddy coming back, and where.
                if (!NetProtocol.TryDecodeRespawnEvent(data, out byte respawnSlot, out Vector2 respawnPos, out int respawnMs, out int respawnReward))
                {
                    return;
                }
                // Hub relay (card 87242257): the buddy-is-coming-back ring belongs on every
                // screen, not just the host's.
                RelayFromClient(p, seq => NetProtocol.EncodeRespawnEvent(seq, respawnSlot, respawnPos, respawnMs, respawnReward));
                if (NetScene.Current == null)
                {
                    return;
                }
                // Never over one of OUR seats. A slot disagreement (a reconnect race, a refused
                // move) would otherwise park a phantom indicator on a player who is alive and
                // flying -- and, when it popped, drop a free bomb into our world. The EvBlast
                // case above refuses the same way and for the same reason.
                if (respawnSlot >= Oracle.MaxPlayers || OwnsSlot(respawnSlot))
                {
                    return;
                }
                // Re-point an indicator we are already showing for that slot rather than stacking
                // a second one. A duplicate is unlikely on the ordered reliable lane, but nothing
                // stops one, and the cost is not cosmetic: two rings pop into TWO reward blasts
                // in our world.
                PlayerShipSummon summon = FindCosmeticSummon(respawnSlot);
                if (summon != null)
                {
                    summon.SetupRemote(respawnSlot, respawnPos, respawnMs, respawnReward);
                    break;
                }
                summon = PlayerShipSummon.NewPlayerShipSummon(bin, game);
                summon.SetupRemote(respawnSlot, respawnPos, respawnMs, respawnReward);
                if (!bin.TryAdd((GameComponent)(object)summon))
                {
                    // A standing Purge<PlayerShipSummon> is live this tick (NetApplyReset purges
                    // from inside this very rx drain -- see SpawnPuppet's guard for the full
                    // reasoning). Dropping it is correct: a reset wipes the indicator anyway.
                    return;
                }
                if (NetHost.Current.NetLog)
                {
                    Console.WriteLine("[net] rx respawn slot=" + respawnSlot + " ms=" + respawnMs
                        + " reward=" + respawnReward
                        + " at=" + (int)respawnPos.X + "," + (int)respawnPos.Y);
                }
                break;
            }
            case NetProtocol.EvJoinRequest:
            {
                if (isHost)
                {
                    HandleJoinRequest(p);
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
                    || !NetProtocol.TryDecodeMessageEvent(data, out AnimatedMessage.MessageType msgType, out SoundManager.Texts speech, out float angle, out string text, out bool isShort))
                {
                    return;
                }
                AnimatedMessage msg = AnimatedMessage.NewAnimatedMessage(bin, game);
                msg.Setup(text, speech, msgType);
                if (msgType == AnimatedMessage.MessageType.redwarning)
                {
                    msg.SetWarningDirection(angle);
                }
                // MakeShort AFTER Setup -- Setup re-seeds the layout for the type, so the compact
                // form has to be applied on top of it (the boss call sites do the same).
                if (isShort)
                {
                    msg.MakeShort();
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
                // THE JOIN PEER IS A GUEST -- no grant, no save, no banner (card 125490d9).
                //
                // This used to be "generous: the join peer played the level too", and it granted
                // the item (plus the HarderDifficulties -> InsaneDifficulty, cheat -> Cheats and
                // challenge -> Challenges pair-ups) and then called SaveThreaded(). That framing
                // dated from card 11.3, when a session could only be two people who had
                // deliberately swapped a room code. The public game browser changed the
                // population: anyone can join a listed game and "Allow Online Joins" defaults ON,
                // so the same path let a STRANGER write HarderDifficulties / Level2 / Level3 /
                // the challenge levels / Cheats / Awardments into your Unlockables.xml. A joiner
                // is on their own machine with their own save -- it is not a couch player sharing
                // the host's.
                //
                // The USER'S RULING (2026-08-01), and it is a product decision, not a security
                // one: joining a game online makes you a guest for that game, and your personal
                // unlock state is wholly unaffected. The banner went with the grant -- announcing
                // an unlock the joiner did not receive is worse than saying nothing.
                //
                // THE DECODE STAYS, and that is deliberate. It is the only live caller of
                // TryDecodeUnlockEvent, so dropping it would leave the wire-enum validator (and
                // ProbeWireEnums' row for it) asserting about a function nothing calls. Keeping
                // it also means a malformed frame is still REFUSED rather than counted as a beat,
                // and the protection is already in place if the grant is ever restored.
                //
                // NOT REMOVED FROM THE WIRE: the host still emits EvUnlock and an older peer
                // still applies it, so there is no protocol change and no version bump.
                if (isHost || !NetProtocol.TryDecodeUnlockEvent(data, out _, out _, out _, out _))
                {
                    return;
                }
                // Counted: BeatsRx is "script beats received off the wire", not "effects
                // applied". Skipping it here would make a healthy session look like the host had
                // stopped sending beats.
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
            case NetProtocol.EvIntroVolley:
            {
                // Card 8a7772d6. Scene-gated like every world message: with no GameScene up
                // there is nothing to fire into, and the volley is a moment, not a state.
                if (isHost || NetScene.Current == null
                    || !NetProtocol.TryDecodeIntroVolleyEvent(data, out int volleySeed))
                {
                    return;
                }
                NetScene.Current.NetApplyIntroVolley(volleySeed);
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvFx:
            {
                // One-shot cosmetic feedback the host observed. Scene-gated like every world
                // message; DRAW/AUDIO ONLY on this side (see the NetFxKind contract).
                if (isHost || NetScene.Current == null
                    || !NetProtocol.TryDecodeFxEvent(data, out NetFxKind fxKind, out ushort fxId, out byte fxParam))
                {
                    return;
                }
                ApplyFx(fxKind, fxId, fxParam);
                metrics.BeatsRx++;
                break;
            }
            case NetProtocol.EvSlowmo:
            {
                // The peer's 1up filled (card a66e190a). NOT host-gated -- either peer's bar can
                // fill -- but scene-gated like every world message: Oracle.Update clears slow
                // motion whenever no player ship is alive, so applying one at the menus would be
                // a no-op with a tick of scaled time in it.
                //
                // NetSetSlowmotion, never SetSlowmotion: the latter would send this straight back.
                if (!NetProtocol.TryDecodeSlowmoEvent(data, out ushort slowmoMs))
                {
                    return;
                }
                // Hub relay (card 87242257): the 1up slow motion is the WORLD's, so every peer's
                // world scales together -- the very property that makes it safe at all.
                RelayFromClient(p, seq => NetProtocol.EncodeSlowmoEvent(seq, slowmoMs));
                if (NetScene.Current == null || oracle == null)
                {
                    return;
                }
                oracle.NetSetSlowmotion(slowmoMs / 1000f);
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
                p.RemotePaused = on;
                // Pause is a SET (card 87242257): the scene freezes on the aggregate's edges, so
                // one peer unpausing under another's held pause keeps the world frozen.
                SyncRemotePauseToScene();
                if (on)
                {
                    metrics.Pauses++;
                    // Arm the host's kick offer (card 0b8a300b). Note this is the EVENT edge,
                    // not the freeze edge: the freeze itself is skipped while our own pause menu
                    // is up, and the clock should still run then -- NetLocalPauseReleased
                    // re-freezes into a pause that may already have earned the offer.
                    p.RemotePauseAt = NowMs;
                    p.KickOfferShown = false;
                }
                else
                {
                    p.RemotePauseAt = 0;
                    p.KickOfferShown = false;
                    if (kickOfferPeer == p)
                    {
                        // The offer's subject unpaused; let a later offer re-target whoever
                        // still holds one.
                        kickOfferPeer = null;
                    }
                }
                // ...and the hub folds this client's pause into every OTHER client's
                // per-recipient aggregate.
                SyncPauseAggregateToPeers();
                break;
            }
            case NetProtocol.EvTetherBreak:
            {
                // Hub relay (card 87242257) -- moot while TeamChallenge stays 2-player, kept so
                // the or-of-either-peer contract survives any future N-seat tether.
                RelayFromClient(p, seq => NetProtocol.EncodeEmptyEvent(seq, NetProtocol.EvTetherBreak));
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
                // live world so it isn't waiting on snapshot self-heals for spawn extras --
                // ADDRESSED to the peer that asked (card 87242257), so the peers already
                // caught up are not re-blasted with the whole live set.
                replayTarget = p;
                try
                {
                    NetIdRegistry.ReplayLive();
                    // ...and the deep mid-level scenery our script already ran (card 45a4e48d).
                    // This is the seam, not PeerConnected: a join-in-progress peer has no
                    // GameScene at pairing time, and the Initialize that gives it one would
                    // clobber anything we sent earlier with the level's INITIAL background/music.
                    NetScene.Current?.NetReplayCatchUp();
                }
                finally
                {
                    replayTarget = null;
                }
                break;
            }
            case NetProtocol.EvLeave:
            {
                if (isHost && (menuSession || listedSession))
                {
                    // A CLIENT left cleanly: free its seats everywhere and play on -- the 11.9
                    // match-end policy. ReleaseDepartedPeer owns all three shapes (peers
                    // remain / last one mid-level / last one at the lobby).
                    p.Up = false;
                    ReleaseDepartedPeer(p, "peer left the match");
                    break;
                }
                // For a CLIENT this is the HOST leaving, which ends the match (no host
                // migration). A shared victory/game-over also lands here (whichever scene
                // terminates first sends the leave) -- EndMatchPeerGone treats that as a normal
                // end and shows no notice. (A null scene there = the lobby/warm phase, i.e. a
                // real walk-out, so the notice does show; our OWN finished level can't reach
                // this -- its scene-down edge already stopped the session.)
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
            case NetProtocol.EvPeerLeft:
            {
                // Card 87242257: another CLIENT left the match for good -- the host freed its
                // seats, so free ours too. Host-sent only; a client must not be able to vacate
                // anyone's seat on this screen.
                if (isHost || data.Length < 5)
                {
                    return;
                }
                ApplyPeerLeft(p, data[4]);
                break;
            }
            case NetProtocol.EvLobbyRoster:
            {
                // Card 0257f8ba: the host's lobby roster, for the waiting panel. Presentation
                // only -- stored and drawn (NetLobby.RosterLines), never fed to the oracle, so
                // nothing off this byte can seat or unseat anyone.
                if (isHost || data.Length < 5)
                {
                    return;
                }
                lobbyRosterMaskRx = data[4];
                break;
            }
            }
        }

        // Free a departed peer's roster footprint on a CLIENT: its ships arrive here as relayed
        // extras on the HOST channel, so each masked slot's channel goes (puppet exploded -- the
        // owner is gone for real, not hiccuping) and its RemoteFriend seat frees. Own slots are
        // refused like every slot-carrying rx path; the HOST's own seat can never be masked
        // (only clients depart this way).
        private static void ApplyPeerLeft(PeerChannel host, byte mask)
        {
            for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
            {
                if (!NetProtocol.SlotInMask(mask, slot) || OwnsSlot(slot))
                {
                    continue;
                }
                if (host.Extras.TryGetValue((byte)slot, out ShipChannel ch))
                {
                    if (ch.Puppet != null)
                    {
                        ExplodeFriend(host, ch, (byte)slot); // drops the channel too
                    }
                    else
                    {
                        host.Extras.Remove((byte)slot);
                    }
                }
                oracle.RemovePlayerAt(slot, ControlDevice.RemoteFriend);
                Console.WriteLine("[net] peer left -- freed slot " + slot);
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
                    // Settled, but the removal has not flushed, so there is no record yet.
                    // Settle from the Entry, off the very fields OnHostDeath will read a flush
                    // later -- the pickup TYPE included (06ac5df2 follow-up): the death record
                    // carries it now, so PayDeadClaim runs the same remote-pickup apply on both
                    // sides of a ComponentBin flush and the claimant cannot observe which side
                    // its claim landed on. (Score never moves on any claim branch since v20 --
                    // one writer per slot, card af96bcc2.)
                    if (payable)
                    {
                        e.ClaimPaidMask |= (byte)(1 << killerSlot);
                        PayDeadClaim(netId, killerSlot, e.Comp.Position,
                            (ushort)MathHelper.Clamp(e.Comp.NetPointValue, 0f, 65535f),
                            e.Comp.NetPickup is INetPickup dead ? dead.NetPickupType : (Powerup.PowerupType?)null);
                    }
                    return;
                }
                // AN UNATTRIBUTED CLAIM NEVER SETTLES A LIVE ENTITY -- it asks for it BACK
                // (card 9ccfe295). `payable` is false for KillerNone, KillerSelf and any
                // out-of-range slot, and the live branch below then fell through to the
                // non-killable arm's bare `bin.Remove`: the host's enemy vanished with NO
                // explosion, no cue, no award and a KillerNone EvDeath, whose client branch is
                // also a silent despawn. That is the reported "large laser-firing UFOs just
                // disappear, and P2's kill plays no explosion on P1's screen".
                //
                // A claim we cannot credit means the JOINER LOST THE ENTITY, not that it killed
                // it: its own copy died a gameplay death nobody landed -- a mis-simulated
                // puppet-vs-puppet hit, or a puppet dead-reckoned into the Floorbottom. The host
                // owns unattributed deaths, so the entity stays, and we re-announce it so the
                // joiner -- which has already dropped its puppet and MarkRemoved the id -- gets a
                // correctly-dressed rebuild instead of a RecentRemovalWindowMs blackout followed
                // by a permanently generic self-heal. `OnHostSpawn` is the SAME call
                // NetIdRegistry.ReplayLive makes for a join-in-progress catch-up, so this needs
                // no new protocol and the client path is already exercised.
                //
                // KILLERSELF IS DELIBERATELY NOT IN HERE, even though it is equally unpayable.
                // It is an OPT-IN report that a real death happened (`NoteSelfDestruct`, a
                // StarMine's own Asplode -- card 4e406eba), so it settles the entity on its
                // pre-card path and simply credits nobody. Routing it here instead would
                // re-announce a mine the claimant has just watched explode, and it would pop
                // back onto their screen.
                //
                // The DEAD branches are untouched: an unpayable claim for an entity already
                // settled or already out of the world pays nothing and always did.
                if (killerSlot != NetProtocol.KillerSelf && !payable)
                {
                    metrics.ClaimsUnattributed++;
                    OnHostSpawn(e);
                    if (NetHost.Current.NetLog)
                    {
                        Console.WriteLine("[net] unattributed claim id=" + netId
                            + " slot=" + killerSlot + " -- entity kept, re-announced");
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
                    // A killable still in the world after NetKill DEFERRED its death (card
                    // 1878b321): its own Update finishes the dying animation/mission and Die()s
                    // itself -- the SpiderHelperMothership completes its charge/fire before
                    // crashing, exactly as an offline kill would. NetKill's own NoteDeathBegan
                    // has already announced EvDying to the claimant. The `bin.Remove` that used
                    // to sit here force-deleted the host's copy mid-animation -- a claimed kill
                    // ended a helper's "sacred mission" on the spot where the host's own kill
                    // let it finish.
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
                        // NO score credit here since v20 (card af96bcc2): the claimant's slot has
                        // one writer -- its owner -- and the owner credited itself the moment it
                        // observed the kill on its own screen. The host only settles the entity
                        // and plays the world-side FX.
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
                // KillerSelf reaches here and is NOT payable -- an opted-in self-destruct is a
                // real death report, so it settles the entity while crediting nobody.
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
                PayDeadClaim(netId, killerSlot, rec.Pos, rec.Points, rec.Pickup);
            }
        }

        // A claim whose target was already settled -- from the Entry while its removal is still
        // queued, from the death record once that has flushed. ONE helper on purpose (card
        // 1bfcd705): the two ledgers cover consecutive halves of the same window, so a claim's
        // effect must not depend on which side of a ComponentBin flush it landed. Since v20
        // (card af96bcc2) no SCORE moves here -- the claimant's slot has one writer, its owner,
        // and the owner credited itself at its own kill -- so what is left is the shared lives
        // pool, the paid-once bookkeeping the caller keeps for it, and (06ac5df2 follow-up) the
        // remote-pickup apply, now that both ledgers carry the pickup type.
        private static void PayDeadClaim(ushort netId, byte killerSlot, Vector2 pos, ushort points,
            Powerup.PowerupType? pickup)
        {
            if (pickup == Powerup.PowerupType.OneUp)
            {
                score.AddLife(); // overlapping collectors inside the RTT window each add one
            }
            if (pickup != null)
            {
                // The full remote-pickup apply -- HUD icon, ship-side mirror, cue -- exactly what
                // the live branch runs (06ac5df2 follow-up). Both callers guarantee `payable`, so
                // the slot is already inside the overload's own bound.
                ApplyRemotePowerup(pickup.Value, killerSlot);
            }
            metrics.ClaimsPaidDead++;
            if (NetHost.Current.NetLog)
            {
                // "settled", not "already dead": the pre-flush half of this reaches a Powerup
                // whose settle path sets `taken` and never flips IsDead.
                Console.WriteLine("[net] claim honored (already settled) id=" + netId + " slot=" + killerSlot);
            }
        }

        // The cosmetic respawn indicator we are already drawing for that slot, if any (card
        // 37f3a663). COSMETIC only: a summon of our own in that slot would be a real countdown
        // with a ship at the end of it, and re-pointing one off the wire would move a respawn we
        // own -- the rx path refuses those slots anyway, so this is belt and braces.
        private static PlayerShipSummon FindCosmeticSummon(int slot)
        {
            foreach (IGameComponent item
                in (System.Collections.ObjectModel.Collection<IGameComponent>)(object)game.Components)
            {
                if (item is PlayerShipSummon summon && summon.IsCosmetic && summon.Owner == slot)
                {
                    return summon;
                }
            }
            return null;
        }

        // ---- remote-ship puppet lifecycle ---------------------------------------------------

        private static void ManagePuppet(PeerChannel p, bool worldTakesPuppets)
        {
            ShipChannel ch = p.Primary;
            // Adopt / release: the GameScene can spawn (SpawnAllPlayers after a reset) or
            // purge (LoseLife/Terminate) the remote-slot ship without asking us.
            if (ch.Puppet != null && !oracle.GetShips().Contains(ch.Puppet))
            {
                ch.Puppet = null;
                ch.SeenAlive = false;
                ch.HasLastPuppetPos = false;
            }
            if (ch.Puppet == null && p.PrimarySlot != NetProtocol.SlotNone)
            {
                foreach (PlayerShip s in oracle.GetShips())
                {
                    // BY SEAT, not by device alone: with several remote peers a bare Remote scan
                    // would adopt whichever remote ship the list yields first -- someone else's.
                    if (s.Controller == ControlDevice.Remote && s.Owner == p.PrimarySlot)
                    {
                        ch.Puppet = s;
                        // ADOPTED, not spawned by us, so we have NOT seen the peer alive on it.
                        ch.SeenAlive = false;
                        ch.HasLastPuppetPos = false;
                        break;
                    }
                }
            }
            if (!p.Up)
            {
                return;
            }
            ch.SeenAlive |= ch.Alive && ch.Puppet != null;
            if (ch.Alive && ch.Puppet == null && ch.Buffer.HasSamples && worldTakesPuppets)
            {
                SpawnPuppet(p);
            }
            else if (!ch.Alive && ch.Puppet != null)
            {
                // THE FALLING EDGE, not the level (card b4d0ba1d). A death LOOK belongs to a
                // peer we have actually seen alive on this puppet; firing on the level meant
                // any ship that arrived in the Remote seat while the peer was still dead --
                // the reset respawn, before that card stopped SpawnAllPlayers producing one --
                // got the full explosion + cue for a death that never happened. A puppet we
                // adopted without ever seeing the peer alive is released QUIETLY instead: the
                // peer is dead, so its ship does not belong in our world either way.
                if (ch.SeenAlive)
                {
                    ExplodePuppet(ch);
                }
                else
                {
                    ReleasePuppetQuietly(ch);
                }
            }
        }

        // Is the world in a state where a peer's ship belongs in it? (Card c1cdd3e5.)
        //
        // The gate for BOTH puppet spawners -- `ManagePuppet`'s primary remote ship and
        // `TickFriends`'s couch/AI-friend ships. It used to be a bare `FindLocalShip() != null`
        // at each, and the reported bug is what that costs: *"on a joining client, while I was
        // dead and respawning, the other players' ships (who respawned before me) did not appear
        // on the playing field until mine did."* Our own ship being absent says nothing about
        // whether THEIRS should be drawn -- in co-op a death is not a world wipe, the level keeps
        // running, and the peer really is flying around out there. So the second arm: our own
        // RESPAWN SUMMON is up.
        //
        // WHY THAT IS THE RIGHT SIGNAL, and not merely a convenient one. The gate exists to keep a
        // puppet out of a world that is being WIPED -- and the load-bearing fact is NOT "every
        // wipe purges the summon" (see the client caveat below), it is that **every wipe arms a
        // standing `Purge<T>` filter, and the filter and that wipe's queued removals expire
        // together in the SAME `TopOfTickFlush`**. So for as long as a summon of ours is still in
        // `Game.Components` after a wipe, the filter that ate it is still armed -- and
        // `bin.TryAdd` in SpawnPuppet/SpawnFriend honours it, so the add is refused anyway. That
        // pairing is exact, and it is what makes relaxing this safe at all. (`Purge` matches with
        // `Type.IsInstanceOfType`, so the base-typed purges -- `Terminate`, `UpdateWin`,
        // `UpdateResetting`, all `Purge<AlienDrawableGameComponent>` -- cover `PlayerShip` too.
        // `UpdateStartup`'s `standing: false` purge is host-only AND is flushed before the rx
        // drain, so it is not exposed.)
        //
        // THE ONE WIPE THAT ARMS NOTHING is a CLIENT's own `GameScene.LoseLife`, which early-returns
        // on `NetSession.IsClient` before both its purges -- a joining client's wipe only ever
        // arrives as the host's `EvReset`. So between "our world went shipless" and that EvReset
        // landing there is genuinely no filter. It is still safe, but for a different reason: a
        // wipe means every peer reported dead, so `ch.Alive` is false on every channel and neither
        // spawner is reached. Do not restate the purge argument for that window; it does not hold
        // there.
        //
        // `PlayerShipSummon.ShouldSummon` seals the other end: a summon is only ever raised while
        // ANOTHER ship is still alive, so in single player -- where a death IS the wipe -- there is
        // never one and this arm can never open. And the summon must be OURS -- a summon for
        // another seat says nothing about whether this world is still running for us.
        //
        // The `!IsCosmetic` term is NOT redundant, though the seat test covers today's shapes.
        // A cosmetic summon is the PEER's respawn, and `HandleRespawnEvent` refuses to raise one
        // over a slot we own -- but `OwnsSlot` is DEVICE-based, so an UNSEATED slot answers "not
        // ours", and `SlotAdopt.TakeSlot` assigns `localPrimarySlot` without seating it. That
        // window is exactly the "slot disagreement (a reconnect race, a refused move)" the
        // respawn handler names, so the two terms are separable state and the suite pins the
        // cosmetic case on its own leg.
        //
        // KNOWN NEW BEHAVIOUR: `UpdateWin` does not purge until t+4 s, so if we die and a partner
        // wins, their ship can now pop into the world during the victory choreography and be
        // purged four seconds later. That is arguably right -- their ship really is flying the
        // victory thrust -- but it is new, it is visible, and nothing pins it.
        private static bool WorldTakesPuppets()
        {
            if (FindLocalShip() != null)
            {
                return true;
            }
            foreach (IGameComponent item
                in (System.Collections.ObjectModel.Collection<IGameComponent>)(object)game.Components)
            {
                if (item is PlayerShipSummon summon && !summon.IsCosmetic
                    && summon.Owner == localPrimarySlot)
                {
                    return true;
                }
            }
            return false;
        }

        // Take a ship out of the Remote seat with no death FX and no cue -- see ManagePuppet.
        // Same teardown as ExplodePuppet minus the explosions, the sound and the log's meaning.
        private static void ReleasePuppetQuietly(ShipChannel ch)
        {
            PlayerShip released = ch.Puppet;
            ch.Puppet = null;
            ch.SeenAlive = false;
            ch.HasLastPuppetPos = false;
            bin.Remove((GameComponent)(object)released);
            if (NetHost.Current.NetLog)
            {
                // "on this puppet", not "yet": a peer that was alive minutes ago on a PREVIOUS
                // puppet, died, and had a fresh ship adopted into its seat lands here too.
                Console.WriteLine("[net] remote ship released (never seen alive on this puppet, no death FX)");
            }
        }

        // Card b4a9fe60. Both puppet spawns used to hard-code South (4.712389f = up the screen),
        // which is only right for the levels that happen to use it: `startdir` is also the
        // direction PlayerShip.Update's hasWon arm thrusts at forever, so at victory the remote
        // ship flew UP off Level 2 while every local ship flew RIGHT. The scene owns the angle;
        // the fallback only covers a spawn with no scene up. That used to follow from the
        // callers' gate being "we have a live local ship"; since card c1cdd3e5 it follows from
        // `NetScene.Current` instead -- `Terminate` nulls it ABOVE its own purges, so a world
        // being torn down has no scene AND no summon, and `NetApplyReset` runs inside the rx
        // drain with a live scene. A spawn reaching here at all would be news.
        private const float FallbackSpawnDirection = 4.712389f;

        internal static float PuppetSpawnDirection()
        {
            INetScene scene = NetScene.Current;
            return scene != null ? scene.PlayerSpawnDirection : FallbackSpawnDirection;
        }

        private static void SpawnPuppet(PeerChannel p)
        {
            ShipChannel ch = p.Primary;
            // The peer's primary seat was allocated at handshake time (host: it granted it;
            // client: slot 0), so this only has to fill it -- never pick one.
            if (p.PrimarySlot == NetProtocol.SlotNone)
            {
                return;
            }
            // The seat is THIS peer's granted slot, never "wherever a Remote device sits" -- a
            // GetPlayerIndex(Remote) scan is ambiguous with several remote peers. Already seated
            // as Remote (the handshake reservation, or a previous life) is the expected case.
            bool seated = oracle.IsSeated(p.PrimarySlot) && oracle.Controller(p.PrimarySlot) == ControlDevice.Remote;
            if (!seated && !oracle.AddPlayerAt(p.PrimarySlot, ControlDevice.Remote))
            {
                return;
            }
            int slot = p.PrimarySlot;
            PlayerShip ship = bin.Recycle<PlayerShip>();
            if (ship == null)
            {
                ship = new PlayerShip(game);
            }
            ship.Setup(slot, ch.Buffer.Newest.Pos, startup: false, invulnerable: false, PuppetSpawnDirection());
            if (!bin.TryAdd((GameComponent)(object)ship))
            {
                // A standing Purge<PlayerShip> is live this tick. The one that can actually
                // reach us is NetApplyReset's, because it purges from inside this very rx
                // drain; the LoseLife / UpdateWin / UpdateResetting purges run back in
                // base.Update and their deaths are flushed by collectionHelper.Update() before
                // the drain, so the summon those wipes purged is gone with them and
                // WorldTakesPuppets answers false -- the caller's gate is shut. The
                // ship being purged is CORRECT either way (a reset wipes all ships and
                // SpawnAllPlayers respawns every seated slot), but adopting one that never
                // entered the world points `puppet` at a ship the world does not have, and the
                // retry gate above is `puppet == null`. Leave it clear and retry next tick, once
                // TopOfTickFlush has expired the filter; the seat we just took is reused via
                // the `seated` check above (card 74403f83).
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
            ch.Puppet = ship;
            // We only get here with the alive latch true, so this puppet HAS been seen alive --
            // set it here rather than waiting for ManagePuppet's next pass, or a peer that
            // died in the very next tick would be released quietly instead of exploding.
            ch.SeenAlive = true;
            ch.HasLastPuppetPos = false;
            ch.RenderMs = double.NaN;
            Console.WriteLine("[net] remote ship joined slot=" + slot);
        }

        // The remote peer said its ship died: mirror the death LOOK locally (explosions +
        // cue), but never through Die() -- that would fire PlayerShip_OnDeath and spawn a
        // local respawn summon for a ship we don't own. Its oracle slot stays reserved so
        // the peer's respawn reuses it.
        private static void ExplodePuppet(ShipChannel ch)
        {
            PlayerShip exploded = ch.Puppet;
            ch.Puppet = null;
            ch.SeenAlive = false;
            ch.HasLastPuppetPos = false;
            metrics.RemoteShipExplosions++;
            Vector2 at = exploded.GetPosition();
            Explosion explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 2f, 2f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            explosion = Explosion.NewExplosion(bin, game);
            explosion.Setup(at, 3.5f, 3.5f, 0f, 0f);
            bin.Add((GameComponent)(object)explosion);
            sound.PlayCue("expl2");
            bin.Remove((GameComponent)(object)exploded);
            Console.WriteLine("[net] remote ship died");
        }

        // A ship channel's render clock runs on REAL time (never the turbo/slowmo/hit-stop-scaled
        // game time): the remote peer plays at its own real pace, so a local hit-stop or
        // slowmo must not drag the interpolation point away from the stream. Advances with
        // the real frame delta, softly servoing toward the ideal offset behind the newest
        // sample; a big error (tab was hidden, peer hiccuped) snaps instead of gliding.
        // ONE clock rule for every channel since card b2828be8 -- the primary is advanced from
        // Update, the extras from TickFriends, both through here.
        private static void AdvanceShipClock(ShipChannel ch)
        {
            if (!ch.Buffer.HasSamples)
            {
                ch.RenderMs = double.NaN;
                return;
            }
            double target = ch.Buffer.NewestMs - InterpDelayFor(ch);
            if (double.IsNaN(ch.RenderMs))
            {
                ch.RenderMs = target;
                return;
            }
            ch.RenderMs += realDtMs;
            double err = target - ch.RenderMs;
            if (Math.Abs(err) > RenderClockSnapMs)
            {
                ch.RenderMs = target;
            }
            else
            {
                ch.RenderMs += err * 0.1;
            }
        }

        // Which cushion does this channel render behind? Internal so NetNPeerTest can assert the
        // relayed budget without restating the constant beside the code it pins.
        internal static float InterpDelayFor(ShipChannel ch)
        {
            return ch.Relayed ? RelayedInterpDelayMs : InterpDelayMs;
        }

        // Called from PlayerShip.Update for ControlDevice.Remote ships: position from the
        // interpolation buffer (~InterpDelayMs behind the newest sample), shots respawned locally
        // from the newest sample's cumulative shot COUNT (card a45b78f6). The count comes off
        // `Buffer.Newest` rather than the interpolated pose deliberately -- it is a tally, not a
        // quantity to lerp, and a stale sample would re-owe shots already fired.
        public static void DriveRemoteShip(PlayerShip ship, GameTime gameTime)
        {
            if (!Active)
            {
                return;
            }
            // Whose primary is this? By adopted puppet first, else by the seat -- the scene can
            // respawn a Remote-seated ship behind our back (SpawnAllPlayers is card b4d0ba1d's
            // subject) and the granted slot is the identity that survives that.
            ShipChannel ch = null;
            foreach (PeerChannel q in peers.Values)
            {
                if (ReferenceEquals(q.Primary.Puppet, ship))
                {
                    ch = q.Primary;
                    break;
                }
            }
            if (ch == null)
            {
                foreach (PeerChannel q in peers.Values)
                {
                    if (!q.Refused && q.PrimarySlot == ship.Owner)
                    {
                        ch = q.Primary;
                        ch.Puppet = ship;
                        ch.HasLastPuppetPos = false;
                        break;
                    }
                }
            }
            if (ch == null)
            {
                return;
            }
            DriveShip(ch, ship);
        }

        // THE one drive path for every remote ship (card b2828be8) -- DriveRemoteShip and
        // DriveFriendShip are the channel-resolving entry points, this is the work. The pop /
        // extrapolation / buffer-depth metrics are primary-gated so the [net] line keeps
        // meaning exactly what it always did.
        private static void DriveShip(ShipChannel ch, PlayerShip ship)
        {
            if (!ch.Buffer.HasSamples || double.IsNaN(ch.RenderMs))
            {
                return; // hold the spawn pose until the first sample lands
            }
            Vector2 pos = ch.Buffer.Sample(ch.RenderMs, out bool extrapolated);
            if (ch.IsPrimary)
            {
                if (extrapolated)
                {
                    metrics.Extrapolations++;
                }
                else
                {
                    metrics.InterpSamples++;
                }
                if (ch.HasLastPuppetPos)
                {
                    // A step no real ship could make over the same real time = a correction pop.
                    float step = Vector2.Distance(ch.LastPuppetPos, pos);
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
                ch.LastPuppetPos = pos;
                ch.HasLastPuppetPos = true;
                metrics.BufferDepthMs = (float)(ch.Buffer.NewestMs - ch.RenderMs);
            }
            ShipSample newest = ch.Buffer.Newest;
            ship.NetApplyRemoteState(pos, newest.Aim, newest.ShotCount, ch.ShotsPerSec, ch.BulletLife, newest.AsplodeBits, newest.BounceBits);
        }
    }
}
