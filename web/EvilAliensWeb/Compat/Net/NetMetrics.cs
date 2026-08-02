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

        // The three reasons an entry can be "unknown" (card 48ab9b2f). They used to share one
        // counter, which made the total unreadable: two of them are ordinary traffic and one is
        // a fault, and a JIP pass that logged a big snapUnk could not tell which it had.
        // Named for their log tokens; NetPuppets classifies them as SnapUnknownKind
        // Rebuilt/LeftDead/Refused respectively, where the descriptive name is what reads well
        // at the branch.
        public long SnapNew;            // never-seen id: the self-heal BUILT it (stream outran the
                                        // reliable spawn) -- benign, tracks the world's spawn rate
        public long SnapDead;           // removed HERE < RecentRemovalWindowMs ago: a death still
                                        // settling -- benign, tracks the world's TOTAL removal rate
                                        // (host EvDeaths included, NOT just our own clTx claims)
        public long SnapBad;            // the rebuild was declined -- the one shape here that means
                                        // something is actually wrong. An unknown typeIdx (a
                                        // protocol/registry mismatch) re-counts on EVERY turn the
                                        // host streams that id; the other two causes -- a descriptor
                                        // declining, or the bin swallowing the add -- mark the id
                                        // removed first, so they tick roughly once per
                                        // RecentRemovalWindowMs with snapDead in between. Any
                                        // sustained nonzero reading deserves a look either way.

        // claims (generous at-least-once)
        public long ClaimsTx;           // client: local deaths claimed
        public long ClaimsRx;           // host: claims received
        public long ClaimsHonored;      // host: claim settled a live entity (real kill path)
        public long ClaimsPaidDead;     // host: already dead, claimant paid from the record

        // Score reconciliation (card b0ab09ec). Client-side only: how far the displayed score
        // had drifted from (host authoritative + our un-settled local credits) at each sync.
        // Under the old max() adoption this grew all level; it is now the direct read on
        // whether the award replication is holding, and should sit at ~0.
        public float ScoreSkewLast;
        public float ScoreSkewMax;

        public void NoteScoreSkew(float delta)
        {
            ScoreSkewLast = delta;
            float mag = Math.Abs(delta);
            if (mag > Math.Abs(ScoreSkewMax))
            {
                ScoreSkewMax = delta;
            }
        }

        // script beats + shared state machine (card 11.3)
        public long BeatsTx;            // host: script-beat events sent (message/unlock/bg/music/checkpoint)
        public long BeatsRx;            // client: script-beat events applied
        public long Resets;             // EvReset sent (host) / applied (client)
        public long Victories;          // EvVictory sent (host) / applied (client)
        public long Pauses;             // EvPause on-edges sent + received
        public long TetherBreaks;       // EvTetherBreak sent + received

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
            // Score skew is a CLIENT reading (the host is the authority and never adopts), so
            // it would be a constant pair of zeroes on the host line.
            string sc = isHost ? "" : string.Format(CultureInfo.InvariantCulture,
                " scSkew={0:0.0} scSkewMax={1:0.0}", ScoreSkewLast, ScoreSkewMax);
            // roster= is the multi-local verification (card 4d904410): both peers must print the
            // SAME slot->owner map, since the host allocates every slot and the wire slot IS the
            // oracle slot. A disagreement here is the bug that used to cross-credit kills.
            // snapTurn is DERIVED, not counted: the snapshot cursor round-robins a fixed number of
            // entries per packet, so it is how long a puppet dead-reckons blind between
            // corrections. Printed because pupPops cannot be judged without it -- a big world
            // stretches the turn and pops follow, on a perfectly healthy link (card 48ab9b2f).
            return string.Format(CultureInfo.InvariantCulture,
                "[net] role={0} peer={1} localShip={2} remoteShip={3} roster={38} txStream={4} rxStream={5} drop={6} sgap={7} buf={8:0}ms interp={9} extrap={10} pops={11} maxPop={12:0.0}px evTx={13} evRx={14} dup={15} dupLive={41} dupDecl={42} dupBad={43} ordViol={16} seqGap={17} liveIds={18} snapTurn={19}ms snapTx={20} snapRx={21} snapEnt={22} snapUnk={23} snapNew={24} snapDead={25} snapBad={26} pupPops={27} clTx={28} clRx={29} clKill={30} clPaid={31} beatTx={32} beatRx={33} resets={34} wins={35} pauses={36} tetherBrk={37} hudTx={39} hudRx={40}",
                isHost ? "host" : "join", peerUp ? "up" : "down",
                localShip ? 1 : 0, remoteShip ? 1 : 0,
                StreamTx, StreamRx, StreamDropped, StreamSeqGaps,
                BufferDepthMs, InterpSamples, Extrapolations, CorrectionPops, MaxPopPx,
                EventsTx, EventsRx, DupSpawns, OrderViolations, SeqGaps, liveIds, snapTurnMs,
                SnapTx, SnapRx, SnapEntriesRx, SnapUnknownIds, SnapNew, SnapDead, SnapBad, PuppetPops,
                ClaimsTx, ClaimsRx, ClaimsHonored, ClaimsPaidDead,
                BeatsTx, BeatsRx, Resets, Victories, Pauses, TetherBreaks, roster,
                HudTx, HudRx, DupLive, DupDeclined, DupBad) + sc + imp;
        }
    }
}
