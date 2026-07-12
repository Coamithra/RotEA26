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

        // interpolation health
        public long InterpSamples;      // puppet frames rendered from a bracketing pair
        public long Extrapolations;     // puppet frames past the newest sample (underruns)
        public long CorrectionPops;     // rendered position jumped > pop threshold vs prediction
        public float MaxPopPx;
        public float BufferDepthMs;     // newest sample - render clock, latest value

        // reliable event lane
        public long EventsTx;
        public long EventsRx;
        public long DupSpawns;          // spawn for an id already live
        public long OrderViolations;    // death for an id never spawned
        public long SeqGaps;            // event sequence didn't advance by exactly 1

        public string Report(bool isHost, bool peerUp, int liveIds)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[net] role={0} peer={1} txStream={2} rxStream={3} drop={4} buf={5:0}ms interp={6} extrap={7} pops={8} maxPop={9:0.0}px evTx={10} evRx={11} dup={12} ordViol={13} seqGap={14} liveIds={15}",
                isHost ? "host" : "join", peerUp ? "up" : "down",
                StreamTx, StreamRx, StreamDropped,
                BufferDepthMs, InterpSamples, Extrapolations, CorrectionPops, MaxPopPx,
                EventsTx, EventsRx, DupSpawns, OrderViolations, SeqGaps, liveIds);
        }
    }
}
