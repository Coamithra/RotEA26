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

        // script beats + shared state machine (card 11.3)
        public long BeatsTx;            // host: script-beat events sent (message/unlock/bg/music/checkpoint)
        public long BeatsRx;            // client: script-beat events applied
        public long Resets;             // EvReset sent (host) / applied (client)
        public long Victories;          // EvVictory sent (host) / applied (client)
        public long Pauses;             // EvPause on-edges sent + received
        public long TetherBreaks;       // EvTetherBreak sent + received

        public string Report(bool isHost, bool peerUp, int liveIds, bool localShip, bool remoteShip)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[net] role={0} peer={1} localShip={2} remoteShip={3} txStream={4} rxStream={5} drop={6} sgap={7} buf={8:0}ms interp={9} extrap={10} pops={11} maxPop={12:0.0}px evTx={13} evRx={14} dup={15} ordViol={16} seqGap={17} liveIds={18} snapTx={19} snapRx={20} snapEnt={21} snapUnk={22} pupPops={23} clTx={24} clRx={25} clKill={26} clPaid={27} beatTx={28} beatRx={29} resets={30} wins={31} pauses={32} tetherBrk={33}",
                isHost ? "host" : "join", peerUp ? "up" : "down",
                localShip ? 1 : 0, remoteShip ? 1 : 0,
                StreamTx, StreamRx, StreamDropped, StreamSeqGaps,
                BufferDepthMs, InterpSamples, Extrapolations, CorrectionPops, MaxPopPx,
                EventsTx, EventsRx, DupSpawns, OrderViolations, SeqGaps, liveIds,
                SnapTx, SnapRx, SnapEntriesRx, SnapUnknownIds, PuppetPops,
                ClaimsTx, ClaimsRx, ClaimsHonored, ClaimsPaidDead,
                BeatsTx, BeatsRx, Resets, Victories, Pauses, TetherBreaks);
        }
    }
}
