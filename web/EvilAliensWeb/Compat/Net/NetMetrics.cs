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
        public long SnapUnknownIds;     // entries for ids not (yet / anymore) puppeted
        public long PuppetPops;         // snapshot error > snap threshold: hard corrected

        // claims (generous at-least-once)
        public long ClaimsTx;           // client: local deaths claimed
        public long ClaimsRx;           // host: claims received
        public long ClaimsHonored;      // host: claim settled a live entity (real kill path)
        public long ClaimsPaidDead;     // host: already dead, claimant paid from the record

        public string Report(bool isHost, bool peerUp, int liveIds)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[net] role={0} peer={1} txStream={2} rxStream={3} drop={4} sgap={5} buf={6:0}ms interp={7} extrap={8} pops={9} maxPop={10:0.0}px evTx={11} evRx={12} dup={13} ordViol={14} seqGap={15} liveIds={16} snapTx={17} snapRx={18} snapEnt={19} snapUnk={20} pupPops={21} clTx={22} clRx={23} clKill={24} clPaid={25}",
                isHost ? "host" : "join", peerUp ? "up" : "down",
                StreamTx, StreamRx, StreamDropped, StreamSeqGaps,
                BufferDepthMs, InterpSamples, Extrapolations, CorrectionPops, MaxPopPx,
                EventsTx, EventsRx, DupSpawns, OrderViolations, SeqGaps, liveIds,
                SnapTx, SnapRx, SnapEntriesRx, SnapUnknownIds, PuppetPops,
                ClaimsTx, ClaimsRx, ClaimsHonored, ClaimsPaidDead);
        }
    }
}
