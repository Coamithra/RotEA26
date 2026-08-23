using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Dev-only artificial network impairment, decorating any INetTransport (card 40334a8f).
    //
    // WHY: BroadcastChannelTransport delivers both lanes reliably and in order (its own header
    // says so), so every drop-tolerance path cards 11.1-11.3 built -- the ShipStateBuffer
    // interpolation window, extrapolation on underrun, the snapshot "unknown id" puppet
    // self-heal, the generous claim ledgers, the peer timeout -- has never actually executed.
    // This makes packets go missing on demand, and does it BEHIND the interface so it impairs
    // the WebRTC transport (card 11.4) identically with no changes.
    //
    // Impairment is RX-ONLY: delaying/dropping our own inbound is equivalent to the peer's
    // outbound being bad, needs no protocol change, and makes an asymmetric link just two tabs
    // with different settings. Tx forwards verbatim.
    //
    // Per-lane policy -- the STREAM lane takes delay + loss (+ optional jitter), the RELIABLE
    // lane takes delay ONLY. Dropping or reordering the reliable lane would violate the
    // INetTransport contract that everything above it is entitled to assume, so it would only
    // ever manufacture fake bugs.
    public sealed class NetImpairment : INetTransport
    {
        public const float MaxLagMs = 500f;
        public const float MaxLossPct = 100f;
        public const float MaxJitterMs = 200f;

        public event Action<byte[], bool, string> OnData;
        public event Action<string> OnPeerBye;

        private struct Held
        {
            public byte[] Payload;
            public string From;
            public long ReleaseAt;
            public long Arrival;
        }

        // Arrival breaks ReleaseAt ties. List.Sort is an UNSTABLE introsort, and same-millisecond
        // stream arrivals are the norm, not an edge case: NetSession sends MsgShipState and
        // MsgWorldSnapshot on the stream lane in the same Update tick. Without the tiebreaker
        // those get delivered in arbitrary (in practice reversed) order, fabricating seqGaps that
        // look exactly like the network loss this tool exists to measure.
        private static readonly Comparison<Held> ByRelease = (a, b) =>
        {
            int c = a.ReleaseAt.CompareTo(b.ReleaseAt);
            return c != 0 ? c : a.Arrival.CompareTo(b.Arrival);
        };

        private readonly INetTransport inner;

        // Private generator, never the shared game RNG -- the house rule for anything
        // stochastic (see Quad / ShipConnector's private FX RNGs): a co-op session must not be
        // able to desync because one peer turned a dev knob on.
        private readonly Random rng = new Random();

        // Jitter can make a later arrival come due BEFORE an earlier one, so the stream lane
        // cannot be a head-first FIFO -- that would stall the early-due packet behind the head
        // and silently turn jitter back into pure delay. Scanned for all due entries instead.
        private readonly List<Held> streamHeld = new List<Held>();
        private readonly List<Held> due = new List<Held>();

        // Reliable release times are clamped monotone on the way in, so a plain FIFO is
        // provably order-preserving here.
        private readonly Queue<Held> reliableHeld = new Queue<Held>();
        private long lastReliableRelease;
        private long arrivalCounter;

        // Explicit settings for the self-test; null => read the live host values (which in
        // production ARE the DebugFlags ones -- see ServiceHelperNetHost).
        private readonly float? lagOverride;
        private readonly float? lossOverride;
        private readonly float? jitterOverride;

        public long Dropped { get; private set; }

        public int HeldCount => streamHeld.Count + reliableHeld.Count;

        // ---- bandwidth accounting (card 6fb406bc, Stage 11.11) --------------------------------
        //
        // PAYLOAD bytes per lane, counted here because this decorator is the one choke point
        // every session send and receive already passes through (NetSession.StartWith always
        // wraps its transport in one). SCTP/DTLS/UDP/IP framing is invisible from C# and stays
        // the documented ~2-3x multiplier at these packet sizes; what these counters answer is
        // the design doc's "measure the N=4 host uplink" -- payload, self-reported, on any
        // transport.
        //
        // An UNADDRESSED send is a fan-out: the JS/socket layer really writes it once per
        // connected peer (webrtc.js `send` loops the peer map; LocalSocketNet writes every
        // client socket), so it counts payload x BroadcastFanout -- which NetSession refreshes
        // to its up-peer count each send cadence. Addressed sends count once. RX counts at
        // ARRIVAL, before this wrapper's own loss roll -- the bytes crossed the wire whether or
        // not the impairment then eats the packet (Dropped reports that separately).
        //
        // CAVEAT: TX counts what the session OFFERED, one layer above webrtc.js's
        // bufferedAmount back-pressure gate -- on a genuinely stalled WebRTC link the stream
        // frames chanSend skips are still counted here, so txB over-reports by exactly
        // eaRtc.netStats().streamDropped frames' worth. On a healthy link (and on every eahl
        // transport, which has no such gate) the two agree; read the JS drop counter beside
        // txBps before quoting an uplink figure from a degraded run.
        public long TxStreamBytes { get; private set; }
        public long TxReliableBytes { get; private set; }
        public long RxStreamBytes { get; private set; }
        public long RxReliableBytes { get; private set; }
        public int BroadcastFanout = 1;

        // Public because the "[net]" line reports these: logging the flags directly would be
        // only accidentally correct, since the wrapper re-clamps and can be constructed with
        // explicit overrides. A self-describing log has to quote what is actually in force.
        public float LagMs => MathHelper.Clamp(lagOverride ?? NetHost.Current.NetLagMs, 0f, MaxLagMs);

        public float LossPct => MathHelper.Clamp(lossOverride ?? NetHost.Current.NetLossPct, 0f, MaxLossPct);

        public float JitterMs => MathHelper.Clamp(jitterOverride ?? NetHost.Current.NetJitterMs, 0f, MaxJitterMs);

        public NetImpairment(INetTransport inner)
            : this(inner, null, null, null)
        {
        }

        internal NetImpairment(INetTransport inner, float? lag, float? loss, float? jitter)
        {
            this.inner = inner;
            lagOverride = lag;
            lossOverride = loss;
            jitterOverride = jitter;
            if (inner != null)
            {
                inner.OnData += OnInnerData;
                // Lifecycle, not traffic: a pagehide 'bye' is forwarded immediately. Parking it
                // behind a 500ms queue would only muddy the disconnect path card 11.5 owns.
                inner.OnPeerBye += from => OnPeerBye?.Invoke(from);
            }
        }

        public void Open(string room)
        {
            inner.Open(room);
        }

        public void SendStream(byte[] payload)
        {
            TxStreamBytes += (long)payload.Length * Math.Max(1, BroadcastFanout);
            inner.SendStream(payload);
        }

        public void SendReliable(byte[] payload)
        {
            TxReliableBytes += (long)payload.Length * Math.Max(1, BroadcastFanout);
            inner.SendReliable(payload);
        }

        // Addressed sends forward verbatim like the unaddressed pair -- impairment is RX-ONLY
        // (see the header), so beyond the byte accounting the decorator has nothing to do on
        // any TX path.
        public void SendStreamTo(string peerId, byte[] payload)
        {
            TxStreamBytes += payload.Length;
            inner.SendStreamTo(peerId, payload);
        }

        public void SendReliableTo(string peerId, byte[] payload)
        {
            TxReliableBytes += payload.Length;
            inner.SendReliableTo(peerId, payload);
        }

        // Full reset, not just a queue drain: card 11.5 adds a real disconnect/reconnect path,
        // and a stale lastReliableRelease would pin every post-reconnect reliable packet to the
        // old session's clock (releasing them all at once, or never).
        public void Close()
        {
            streamHeld.Clear();
            reliableHeld.Clear();
            due.Clear();
            lastReliableRelease = 0;
            arrivalCounter = 0;
            Dropped = 0;
            TxStreamBytes = 0;
            TxReliableBytes = 0;
            RxStreamBytes = 0;
            RxReliableBytes = 0;
            BroadcastFanout = 1;
            inner.Close();
        }

        private void OnInnerData(byte[] payload, bool reliable, string from)
        {
            Receive(payload, reliable, from, NetHost.Current.NowMs);
        }

        // Split out from the event handler so the self-test can drive a VIRTUAL clock -- a test
        // that had to spend 500ms of real time per sample could never run enough of them.
        internal void Receive(byte[] payload, bool reliable, string from, long now)
        {
            if (reliable)
            {
                RxReliableBytes += payload.Length;
            }
            else
            {
                RxStreamBytes += payload.Length;
            }
            float lag = LagMs;
            float loss = LossPct;
            float jitter = JitterMs;

            // Unimpaired fast path: forward inline, no queue, no allocation. Requires the
            // queues to be EMPTY too -- turning the knobs down to 0 mid-session must still
            // drain whatever is already parked rather than stranding it forever.
            if (lag <= 0f && loss <= 0f && jitter <= 0f && streamHeld.Count == 0 && reliableHeld.Count == 0)
            {
                OnData?.Invoke(payload, reliable, from);
                return;
            }

            if (!reliable && loss > 0f && rng.NextDouble() * 100.0 < loss)
            {
                Dropped++;
                return;
            }

            // Loss-only impairment must not smuggle in latency: with no lag and no jitter the
            // packet isn't being held for anything, so release it inline rather than parking it
            // for the next Pump (which would silently add one tick, ~16ms, to every survivor and
            // make "loss with no lag" impossible to isolate).
            if (lag <= 0f && jitter <= 0f && streamHeld.Count == 0 && reliableHeld.Count == 0)
            {
                OnData?.Invoke(payload, reliable, from);
                return;
            }

            long release = now + (long)lag;
            if (!reliable && jitter > 0f)
            {
                release += (long)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                if (release < now)
                {
                    release = now;
                }
            }

            Held held = new Held { Payload = payload, From = from, ReleaseAt = release, Arrival = arrivalCounter++ };
            if (reliable)
            {
                if (held.ReleaseAt < lastReliableRelease)
                {
                    held.ReleaseAt = lastReliableRelease;
                }
                lastReliableRelease = held.ReleaseAt;
                reliableHeld.Enqueue(held);
            }
            else
            {
                streamHeld.Add(held);
            }
        }

        // Called from the top of NetSession.Update, BEFORE DrainRx, on the same host real-time
        // clock as the rest of the session cadence (so turbo / slow-mo / hit-stop never skew
        // impairment; and a scenario driving a virtual clock moves both together, which is why
        // the arrival stamp in OnInnerData comes from the host too). Granularity is one
        // game tick, ~16ms -- a lag setting below that is indistinguishable from 0.
        public void Pump(long now)
        {
            while (reliableHeld.Count > 0 && reliableHeld.Peek().ReleaseAt <= now)
            {
                Held h = reliableHeld.Dequeue();
                OnData?.Invoke(h.Payload, true, h.From);
            }

            // Forward scan, compacting survivors in place: `due` collects in ARRIVAL order (so
            // the sort's tiebreaker has something meaningful to preserve) and the packets left
            // behind keep their relative order too.
            due.Clear();
            if (streamHeld.Count == 0)
            {
                return;
            }
            int keep = 0;
            for (int i = 0; i < streamHeld.Count; i++)
            {
                if (streamHeld[i].ReleaseAt <= now)
                {
                    due.Add(streamHeld[i]);
                }
                else
                {
                    streamHeld[keep++] = streamHeld[i];
                }
            }
            if (due.Count == 0)
            {
                return;
            }
            streamHeld.RemoveRange(keep, streamHeld.Count - keep);
            due.Sort(ByRelease);
            for (int i = 0; i < due.Count; i++)
            {
                OnData?.Invoke(due[i].Payload, false, due[i].From);
            }
        }

        // ---- self-test -------------------------------------------------------------------

        // Drives N synthetic packets through a REAL NetImpairment on a virtual clock and reports
        // measured delay / drop rate / ordering per lane. This is the card's primary
        // verification: impairment is behaviour over time, so the repo rule says read the DATA,
        // not a frame -- and testing the shipped C# in the real runtime beats a tools/sim python
        // mirror that would drift out of sync with it.
        internal static string SelfTest(float lag, float loss, float jitter, int packets)
        {
            if (packets < 1)
            {
                packets = 200;
            }
            // The seq rides in 2 bytes and 3 packets are injected per iteration, so past this
            // the decode aliases an earlier slot and every delay/reorder number silently turns
            // to garbage. Clamp rather than trust a console-supplied count.
            if (packets > 20000)
            {
                packets = 20000;
            }
            NetImpairment imp = new NetImpairment(null, lag, loss, jitter);

            List<int> streamOrder = new List<int>();
            List<int> reliableOrder = new List<int>();
            List<long> streamDelay = new List<long>();
            List<long> reliableDelay = new List<long>();
            long virtualNow = 0;
            long[] sentAt = new long[packets * 3];

            imp.OnData += (payload, reliable, from) =>
            {
                int seq = payload[0] | (payload[1] << 8);
                (reliable ? reliableOrder : streamOrder).Add(seq);
                (reliable ? reliableDelay : streamDelay).Add(virtualNow - sentAt[seq]);
            };

            // Per iteration: TWO stream packets in the SAME virtual millisecond plus one
            // reliable, then pumped on the real 16ms tick. The same-ms stream pair is
            // deliberate -- it reproduces NetSession sending MsgShipState and MsgWorldSnapshot
            // in one Update tick, which is the case that a ReleaseAt-only sort silently
            // reversed. With jitter 0 the stream reorder count must be exactly 0.
            int seqNext = 0;
            for (int i = 0; i < packets; i++)
            {
                for (int lane = 0; lane < 3; lane++)
                {
                    byte[] p = new byte[] { (byte)(seqNext & 0xFF), (byte)(seqNext >> 8) };
                    sentAt[seqNext] = virtualNow;
                    imp.Receive(p, lane == 2, "test", virtualNow);
                    seqNext++;
                }
                for (int step = 0; step < 2; step++)
                {
                    virtualNow += 16;
                    imp.Pump(virtualNow);
                }
                virtualNow += 1;
            }
            // Drain the tail in real 16ms ticks, NOT one big jump: a single leap to
            // now + maxLag would stamp every still-held packet with the whole leap as its
            // measured delay, reporting a max of ~900ms for a 150ms setting. That is a
            // measurement artifact and it hid the real numbers the first time this ran.
            for (int guard = 0; imp.HeldCount > 0 && guard < 200; guard++)
            {
                virtualNow += 16;
                imp.Pump(virtualNow);
            }

            int streamSent = packets * 2;   // two stream packets per iteration, same millisecond
            int reliableSent = packets;
            return string.Format(CultureInfo.InvariantCulture,
                "[netsim] test lag={0:0}ms loss={1:0}% jitter={2:0}ms n={3}x2 stream / {3} reliable"
                + " | stream: got={4} drop={5:0.0}% delay avg={6:0}ms min={7}ms max={8}ms reorder={9}"
                + " | reliable: got={10} drop={11} delay avg={12:0}ms min={13}ms max={14}ms reorder={15}",
                lag, loss, jitter, packets,
                streamOrder.Count, 100.0 * (streamSent - streamOrder.Count) / (double)streamSent,
                Mean(streamDelay), Min(streamDelay), Max(streamDelay), Inversions(streamOrder),
                reliableOrder.Count, reliableSent - reliableOrder.Count,
                Mean(reliableDelay), Min(reliableDelay), Max(reliableDelay), Inversions(reliableOrder));
        }

        private static double Mean(List<long> v)
        {
            if (v.Count == 0)
            {
                return 0.0;
            }
            double sum = 0.0;
            for (int i = 0; i < v.Count; i++)
            {
                sum += v[i];
            }
            return sum / v.Count;
        }

        private static long Min(List<long> v)
        {
            if (v.Count == 0)
            {
                return 0;
            }
            long m = long.MaxValue;
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i] < m)
                {
                    m = v[i];
                }
            }
            return m;
        }

        private static long Max(List<long> v)
        {
            long m = 0;
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i] > m)
                {
                    m = v[i];
                }
            }
            return m;
        }

        // Adjacent out-of-order pairs in the delivered sequence. 0 on the reliable lane is the
        // contract; nonzero on the stream lane under jitter is the whole point of jitter.
        private static int Inversions(List<int> seq)
        {
            int n = 0;
            for (int i = 1; i < seq.Count; i++)
            {
                if (seq[i] < seq[i - 1])
                {
                    n++;
                }
            }
            return n;
        }
    }
}
