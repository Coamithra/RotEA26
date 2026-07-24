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
        public long StreamDropped;      // out-of-order / duplicate samples the buffer refused
        public long StreamSeqGaps;      // stream sequence didn't advance by exactly 1 (loss/reorder)

        // interpolation health
        public long InterpSamples;      // puppet frames rendered from a bracketing pair
        public long Extrapolations;     // puppet frames past the newest sample (underruns)
        public long CorrectionPops;     // rendered position jumped > pop threshold vs prediction
        public float MaxPopPx;
        public float BufferDepthMs;     // newest sample - render clock, latest value

        // reliable event lane
        public long EventsTx;
        public long EventsRx;
        public long DupSpawns;          // spawn for an id already live (or unbuildable)
        public long OrderViolations;    // death for an id never spawned
        public long SeqGaps;            // event sequence didn't advance by exactly 1

        // world snapshot lane (card 11.2)
        public long SnapTx;             // snapshot packets sent (host)
        public long SnapRx;             // snapshot packets received (client)
        public long SnapEntriesRx;      // per-entity entries decoded
        public long SnapUnknownIds;     // entries for ids not (yet / anymore) puppeted == the 3 below
        public long PuppetPops;         // snapshot error > snap threshold: hard corrected

        // The three reasons an entry can be "unknown" (card 48ab9b2f). They used to share one
        // counter, which made the total unreadable: two of them are ordinary traffic and one is
        // a fault, and a JIP pass that logged a big snapUnk could not tell which it had.
        public long SnapRebuilt;        // never-seen id: the self-heal BUILT it (stream outran the
                                        // reliable spawn) -- benign, tracks the world's spawn rate
        public long SnapLeftDead;       // removed HERE < RecentRemovalWindowMs ago: a death still
                                        // settling -- benign, tracks the world's TOTAL removal rate
                                        // (host EvDeaths included, NOT just our own clTx claims)
        public long SnapRefused;        // the rebuild was declined (no descriptor / descriptor
                                        // returned null / the bin swallowed the add). Counts EVERY
                                        // turn for as long as the host keeps streaming that id --
                                        // the one shape here that means something is actually wrong

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
                "[net] role={0} peer={1} localShip={2} remoteShip={3} roster={38} txStream={4} rxStream={5} drop={6} sgap={7} buf={8:0}ms interp={9} extrap={10} pops={11} maxPop={12:0.0}px evTx={13} evRx={14} dup={15} ordViol={16} seqGap={17} liveIds={18} snapTurn={19}ms snapTx={20} snapRx={21} snapEnt={22} snapUnk={23} snapNew={24} snapDead={25} snapBad={26} pupPops={27} clTx={28} clRx={29} clKill={30} clPaid={31} beatTx={32} beatRx={33} resets={34} wins={35} pauses={36} tetherBrk={37}",
                isHost ? "host" : "join", peerUp ? "up" : "down",
                localShip ? 1 : 0, remoteShip ? 1 : 0,
                StreamTx, StreamRx, StreamDropped, StreamSeqGaps,
                BufferDepthMs, InterpSamples, Extrapolations, CorrectionPops, MaxPopPx,
                EventsTx, EventsRx, DupSpawns, OrderViolations, SeqGaps, liveIds, snapTurnMs,
                SnapTx, SnapRx, SnapEntriesRx, SnapUnknownIds, SnapRebuilt, SnapLeftDead, SnapRefused, PuppetPops,
                ClaimsTx, ClaimsRx, ClaimsHonored, ClaimsPaidDead,
                BeatsTx, BeatsRx, Resets, Victories, Pauses, TetherBreaks, roster) + sc + imp;
        }
    }
}
