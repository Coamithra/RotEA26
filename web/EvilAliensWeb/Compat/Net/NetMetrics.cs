using System;
using System.Globalization;

namespace EvilAliensWeb.Compat.Net
{
    // Cumulative sync-health counters, logged as one parseable "[net] ..." console line
    // every ~5s by NetSession. The two-tab verification gate asserts on THESE (correction
    // pops, buffer health, event ordering), not on screenshots -- the repo's isolated-sim
    // testing rule applied to networking.
    public sealed class NetMetrics
    {
        // stream lane
        public long StreamTx;
        public long StreamRx;
        // Per-slot HUD state (card 1a3ad45a). BOTH count ENTRIES (slot-updates), not packets, so
        // the two peers' figures are directly comparable even when one has a couch partner and
        // puts several slots in one packet. A stalled HudRx against a climbing peer HudTx is what
        // "the other player's combo/powerup readout is frozen" looks like.
        public long HudTx;
        public long HudRx;
        public long StreamDropped;      // out-of-order / duplicate samples the buffer refused
        public long StreamSeqGaps;      // stream sequence didn't advance by exactly 1 (loss/reorder)

        // interpolation health
        public long InterpSamples;      // puppet frames rendered from a bracketing pair
        public long Extrapolations;     // puppet frames past the newest sample (underruns)
        public long CorrectionPops;     // rendered position jumped > pop threshold vs prediction
        public float MaxPopPx;
        public float BufferDepthMs;     // newest sample - render clock, latest value

        // Times NetSession.ExplodePuppet played the PRIMARY remote ship's death LOOK (card
        // b4d0ba1d). The FX itself leaves no other trace a headless scenario can read -- two
        // Explosions and a cue into a live world -- so this is the observable that separates
        // "one death, one explosion" from the reset artifact that fired a second one. Counts
        // that method only: ExplodeFriend is a different lifecycle (stream timeout, not an
        // alive flag) and is deliberately not folded in. Not on the [net] line -- it is a
        // per-death event, not a health rate.
        public long RemoteShipExplosions;

        // reliable event lane
        // EventsTx counts SENDS, per recipient, since card 87242257 (a reliable "broadcast" is
        // one addressed send per up peer, each with its own channel seq) -- so at N peers one
        // logical event moves it by N, and comparing it against a single peer's EventsRx only
        // adds up at N=1... which is every 2-peer log, where the two read exactly as before.
        public long EventsTx;
        public long EventsRx;
        public long DupSpawns;          // EvSpawn that produced no puppet == the 3 below
        public long OrderViolations;    // death for an id never spawned
        public long SeqGaps;            // event sequence didn't advance by exactly 1

        // The reasons an EvSpawn produces no puppet (card 4c9448c8). Same split, and the same
        // reasoning, as snapNew/snapDead/snapBad one bullet down: they shared one `dup` counter
        // that mixed ordinary traffic with a registry/protocol mismatch, so the number the
        // co-op gate asserts on could not be judged -- a joiner arriving during a host reset
        // read dup=15 while a joiner arriving in steady state read 0, and neither told you
        // whether a type had gone missing off the wire. NetPuppets classifies them as
        // SpawnRejectKind; `dup` stays the SUM so the [net] line and every probe that greps it
        // keep working.
        public long DupLive;            // the id was already ours -- the snapshot self-heal beat
                                        // the ordered EvSpawn to it, or a checkpoint revert
                                        // re-spawned across a purge we were still settling.
                                        // BENIGN, and bursty at a join or a reset by nature.
        public long DupDeclined;        // the descriptor refused to construct. BENIGN by
                                        // construction; the id is marked removed and retried.
        public long DupBad;             // THE ONE THAT MEANS TROUBLE: no descriptor for the
                                        // typeIdx (a registry/protocol mismatch -- the peer is
                                        // sending a type this build does not have), plus the two
                                        // shapes that are unreachable today and would be news if
                                        // they fired (the bin swallowing the add, the puppet
                                        // layer not running). THIS is the one to assert at 0.

        // world snapshot lane (card 11.2)
        public long SnapTx;             // snapshot packets sent (host)
        public long SnapRx;             // snapshot packets received (client)
        public long SnapEntriesRx;      // per-entity entries decoded
        public long SnapUnknownIds;     // entries for ids not (yet / anymore) puppeted == the 3 below
        public long PuppetPops;         // snapshot error > snap threshold: hard corrected

        // Host-side: entities that told us they had been REPOSITIONED (card e79bb994) -- the
        // snapshot turns whose entry went out marked NetSnapshotFlags.Teleported. On a level with
        // no repositioning entity it stays at 0 and that is the correct reading; it is not a
        // health metric. What makes it worth printing is that it is the ONLY externally visible
        // sign the marker path ran at all: a marked sample looks, on the wire and on the client,
        // exactly like an entity that was standing still.
        public long Teleports;

        // Host-side: samples whose observed speed exceeded NetSession.MaxObservedSpeedPxPerMs
        // with NO marker -- i.e. a reposition site that has not been taught to call
        // NetNoteTeleport. **THIS ONE IS A 0 BAR**, and the opposite reading to `teleports`
        // beside it: every nonzero here is a type whose puppets will dead-reckon at teleport
        // speed on the other player's screen. The console names each type once; the count is what
        // says how often. See NetSession.NoteIfUnmarkedTeleport.
        public long UnmarkedTeleports;

        // The three reasons an entry can be "unknown" (card 48ab9b2f). They used to share one
        // counter, which made the total unreadable: two of them are ordinary traffic and one is
        // a fault, and a JIP pass that logged a big snapUnk could not tell which it had.
        // Named for their log tokens; NetPuppets classifies them as SnapUnknownKind
        // Rebuilt/LeftDead/Refused respectively, where the descriptive name is what reads well
        // at the branch.
        public long SnapNew;            // never-seen id: the self-heal BUILT it (stream outran the
                                        // reliable spawn) -- benign, tracks the world's spawn rate
        public long SnapDead;           // removed HERE recently enough that a death is still
                                        // settling -- benign, tracks the world's TOTAL removal rate
                                        // (host EvDeaths included, NOT just our own clTx claims).
                                        // RecentRemovalWindowMs for an ordinary removal; a puppet
                                        // RELEASED to finish a deferred death holds until its own
                                        // EvDeath, so a boss death reads here for as long as the
                                        // host streams it (cards 444eb614 / 5f506d11)
        public long SnapBad;            // the rebuild FAILED -- the one shape here that means
                                        // something is actually wrong. An unknown typeIdx (a
                                        // protocol/registry mismatch) re-counts on EVERY turn the
                                        // host streams that id; the bin swallowing the add marks
                                        // the id removed first, so it ticks roughly once per
                                        // RecentRemovalWindowMs with snapDead in between. Any
                                        // sustained nonzero reading deserves a look either way.
                                        // A DESCRIPTOR decline is NOT here any more -- card
                                        // 430494a7 made it routine traffic (snapDecl below).
        public long SnapDecl;           // the descriptor DECLINED the zero-extras rebuild on
                                        // purpose (WallDescriptor, card 430494a7: a defaulted
                                        // wall is the wrong section's whole grid). Benign --
                                        // tracks how often a wall's snapshot outruns its
                                        // reliable EvSpawn, so it reads 0 in steady state and
                                        // ticks around joins and section seams.

        // Snapshot entries REFUSED as stale by the per-netId seq guard (card f5cf7a5c) -- an
        // entry that decoded fine and named a puppet we hold, but was older than the sample
        // already applied to it. NOT counted in snapUnk: nothing about the id was unknown.
        //
        // It is not a fault counter and not a 0 bar. It tracks the LINK's reorder rate, so an
        // unimpaired BroadcastChannel or in-process run reads 0 while a real lossy WebRTC
        // pairing reads whatever that connection is doing -- which is the point, since before
        // the guard every one of those entries silently dragged a puppet backwards instead.
        public long SnapStale;

        // claims (generous at-least-once)
        public long ClaimsTx;           // client: local deaths claimed
        public long ClaimsRx;           // host: claims received
        public long ClaimsHonored;      // host: claim settled a live entity (real kill path)
        public long ClaimsPaidDead;     // host: already dead, claimant paid from the record
        // host: a claim we cannot credit to any slot, for an entity still LIVE -- the joiner
        // mis-simulated a death the host owns (card 9ccfe295). The entity is kept and
        // re-announced. Not a 0 bar and not on the `[net]` line: a peer running an older build
        // still sends these, and one per genuine puppet-vs-puppet mishap is expected. A
        // SUSTAINED rate means a client is killing puppets the host never kills -- read it
        // beside `clKill`, which is the same claims that could be credited.
        public long ClaimsUnattributed;

        // script beats + shared state machine (card 11.3)
        public long BeatsTx;            // host: script-beat events sent (message/unlock/bg/music/checkpoint)
        public long BeatsRx;            // client: script-beat events applied
        public long Resets;             // EvReset sent (host) / applied (client)
        public long Victories;          // EvVictory sent (host) / applied (client)
        public long Pauses;             // EvPause on-edges sent + received
        public long TetherBreaks;       // EvTetherBreak sent + received

        // Bandwidth (card 6fb406bc, Stage 11.11): cumulative PAYLOAD bytes both ways (stream +
        // reliable lanes summed; broadcasts counted once per connected peer -- see
        // NetImpairment's accounting header) and the rate over the last report interval. This
        // is what turned the design doc's ~33 KB/s N=4 host-uplink ESTIMATE into a measurement;
        // real wire cost adds SCTP/DTLS/UDP/IP framing, ~2-3x at these packet sizes. Neither is
        // a health bar -- they describe the level and the peer count, not the link.
        public long TxBytes;
        public long RxBytes;
        public float TxBps;
        public float RxBps;

        // Artificial impairment (card 40334a8f). Reported so a captured "[net]" line from an
        // impaired run is self-describing -- without the settings inline, a deliberately
        // degraded log is indistinguishable from a genuinely broken one months later.
        public long ImpDropped;         // stream packets the impairment wrapper dropped
        public int ImpHeld;             // packets currently parked in its delay queues
        public float ImpLagMs;          // settings in force at report time
        public float ImpLossPct;
        public float ImpJitterMs;

        public string Report(bool isHost, bool peerUp, int liveIds, int snapTurnMs, bool localShip, bool remoteShip, string roster)
        {
            // Impairment is off in the overwhelmingly common case; keep the line unchanged
            // there rather than padding every log with five zeroes.
            string imp = ImpLagMs > 0f || ImpLossPct > 0f || ImpJitterMs > 0f || ImpDropped > 0
                ? string.Format(CultureInfo.InvariantCulture,
                    " impLag={0:0}ms impLoss={1:0}% impJit={2:0}ms impDrop={3} impHeld={4}",
                    ImpLagMs, ImpLossPct, ImpJitterMs, ImpDropped, ImpHeld)
                : "";
            // roster= is the multi-local verification (card 4d904410): both peers must print the
            // SAME slot->owner map, since the host allocates every slot and the wire slot IS the
            // oracle slot. A disagreement here is the bug that used to cross-credit kills.
            // snapTurn is DERIVED, not counted: the snapshot cursor round-robins a fixed number of
            // entries per packet, so it is how long a puppet dead-reckons blind between
            // corrections. Printed because pupPops cannot be judged without it -- a big world
            // stretches the turn and pops follow, on a perfectly healthy link (card 48ab9b2f).
            return string.Format(CultureInfo.InvariantCulture,
                "[net] role={0} peer={1} localShip={2} remoteShip={3} roster={38} txStream={4} rxStream={5} drop={6} sgap={7} buf={8:0}ms interp={9} extrap={10} pops={11} maxPop={12:0.0}px evTx={13} evRx={14} dup={15} dupLive={41} dupDecl={42} dupBad={43} ordViol={16} seqGap={17} liveIds={18} snapTurn={19}ms snapTx={20} snapRx={21} snapEnt={22} snapUnk={23} snapNew={24} snapDead={25} snapBad={26} snapStale={46} pupPops={27} clTx={28} clRx={29} clKill={30} clPaid={31} beatTx={32} beatRx={33} resets={34} wins={35} pauses={36} tetherBrk={37} hudTx={39} hudRx={40} teleports={44} tpUnmarked={45} txB={47} rxB={48} txBps={49:0} rxBps={50:0} snapDecl={51}",
                isHost ? "host" : "join", peerUp ? "up" : "down",
                localShip ? 1 : 0, remoteShip ? 1 : 0,
                StreamTx, StreamRx, StreamDropped, StreamSeqGaps,
                BufferDepthMs, InterpSamples, Extrapolations, CorrectionPops, MaxPopPx,
                EventsTx, EventsRx, DupSpawns, OrderViolations, SeqGaps, liveIds, snapTurnMs,
                SnapTx, SnapRx, SnapEntriesRx, SnapUnknownIds, SnapNew, SnapDead, SnapBad, PuppetPops,
                ClaimsTx, ClaimsRx, ClaimsHonored, ClaimsPaidDead,
                BeatsTx, BeatsRx, Resets, Victories, Pauses, TetherBreaks, roster,
                HudTx, HudRx, DupLive, DupDeclined, DupBad, Teleports, UnmarkedTeleports,
                SnapStale, TxBytes, RxBytes, TxBps, RxBps, SnapDecl) + imp;
        }
    }
}
