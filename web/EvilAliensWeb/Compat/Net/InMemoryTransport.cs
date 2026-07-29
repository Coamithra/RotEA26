using System;
using System.Collections.Generic;

namespace EvilAliensWeb.Compat.Net
{
    // In-process INetTransport implementation #3, for headless scenario work: N endpoints in one
    // process, no browser, no JS, no BroadcastChannel, no WebRTC (design:
    // plans/net-headless-sim.md; card 25ad0659).
    //
    // WHY: every net verification before this either needed two Chrome windows (which cannot both
    // tick at a useful rate -- see the two-window GOTCHA in this directory's CLAUDE.md) or drove
    // one internal policy through a synthetic input (NetImpairment.SelfTest, NetScoreLedger.
    // SelfTest). Neither can reach a real NetSession's own decisions off a real wire. A paired
    // in-memory transport can: NetSession.StartWith already takes an arbitrary INetTransport, so
    // a scenario opens one endpoint into the live session and drives the other by hand.
    //
    // THE PEER COUNT IS A PARAMETER, deliberately (card 25ad0659's absorbed 11.6 half). A
    // two-endpoint transport is one field -- `peer` -- and every scenario written against it bakes
    // 2 in. The N-peer stages (plans/4p-online-coop.md, 11.7-11.11) then rebuild the rig instead
    // of adding contexts to it. So this is a SWITCH with per-(src -> dst) queues from the start,
    // even though today's protocol is 2-peer and only 2-endpoint scenarios can pass.
    //
    // FIDELITY, and its limits. It models what the interface promises and nothing more:
    //   * both lanes deliver in order and never drop -- exactly like BroadcastChannelTransport,
    //     and for the same reason (see INetTransport's header: nothing above the interface may
    //     assume that). Loss / reorder / jitter come from wrapping an endpoint in NetImpairment,
    //     which is what a real session does anyway.
    //   * ROOMS are honoured: a send only reaches endpoints opened on the same room string, so a
    //     scenario can run two independent pairings in one wire (and `?room=`'s real isolation
    //     property is testable rather than assumed).
    //   * delivery is on the RECEIVING endpoint's Pump, never inline on the send. That is the
    //     whole point -- a scenario decides when each peer drains, which is what makes event
    //     ORDERING assertions possible at all.
    public sealed class NetWire
    {
        // Bounded so a typo (`new NetWire(1000000)`) fails loudly instead of allocating. Well
        // above the 4 the player dimension tops out at and the 2 the protocol supports.
        public const int MaxEndpoints = 8;

        private readonly InMemoryTransport[] endpoints;

        public NetWire(int peers)
        {
            if (peers < 1 || peers > MaxEndpoints)
            {
                throw new ArgumentOutOfRangeException(nameof(peers),
                    "NetWire endpoint count must be 1.." + MaxEndpoints + " (got " + peers + ")");
            }
            endpoints = new InMemoryTransport[peers];
            for (int i = 0; i < peers; i++)
            {
                endpoints[i] = new InMemoryTransport(this, i);
            }
        }

        public int PeerCount => endpoints.Length;

        // Typed as the concrete class rather than INetTransport: a scenario needs Pump() and the
        // counters, and the production consumers only ever see it through the interface anyway
        // (NetSession stores INetTransport).
        public InMemoryTransport this[int index] => endpoints[index];

        // Deliver everything queued as of ENTRY, on every endpoint, in index order.
        //
        // Every endpoint's budget is captured BEFORE any of them drains, and that ordering is the
        // whole point: a handler that sends while draining must be delivered on the NEXT Pump, and
        // capturing per endpoint as each one's turn came round would still let a send to a
        // HIGHER-indexed endpoint arrive inside this same Pump. That is a same-tick round trip no
        // real transport can do, and it would silently satisfy an ordering assertion -- exactly the
        // class of false pass this rig exists to avoid. (Measured: with the budget taken inside
        // each endpoint's own Pump, NetWireTest's "a reply waits for the next Pump" leg passed
        // whether the budget was there or not.)
        public void Pump()
        {
            int[] budget = new int[endpoints.Length];
            for (int i = 0; i < endpoints.Length; i++)
            {
                budget[i] = endpoints[i].Pending;
            }
            for (int i = 0; i < endpoints.Length; i++)
            {
                endpoints[i].Pump(budget[i]);
            }
        }

        // Fan a send out to every OTHER open endpoint in the sender's room. Returns how many
        // endpoints it reached (0 is normal: nobody else has opened yet).
        internal int Dispatch(InMemoryTransport from, byte[] payload, bool reliable)
        {
            int reached = 0;
            for (int i = 0; i < endpoints.Length; i++)
            {
                InMemoryTransport to = endpoints[i];
                if (to != from && to.IsOpen && to.Room == from.Room)
                {
                    // Cloned per RECIPIENT, not once per send: two endpoints handing out the same
                    // array would let one peer's handler mutate the other's still-queued packet.
                    // (NetSession decodes rather than mutates today, but the aliasing would be a
                    // silent cross-peer channel and this is the layer that must not have one.)
                    to.Enqueue((byte[])payload.Clone(), reliable, from.Id);
                    reached++;
                }
            }
            return reached;
        }

        // A departing endpoint's "bye" reaches its room-mates only. Mirrors the JS pagehide frame
        // (webrtc.js) / NetInterop's bye, which is likewise best-effort and per-room.
        internal void DispatchBye(InMemoryTransport from)
        {
            for (int i = 0; i < endpoints.Length; i++)
            {
                InMemoryTransport to = endpoints[i];
                if (to != from && to.IsOpen && to.Room == from.Room)
                {
                    to.RaiseBye(from.Id);
                }
            }
        }
    }

    // One endpoint of a NetWire. Constructed only by NetWire, so a scenario cannot end up with an
    // unpaired endpoint that silently swallows everything.
    public sealed class InMemoryTransport : INetTransport
    {
        public event Action<byte[], bool, string> OnData;
        public event Action<string> OnPeerBye;

        private readonly NetWire wire;
        private readonly Queue<(byte[] Payload, bool Reliable, string From)> inbound
            = new Queue<(byte[], bool, string)>();

        internal InMemoryTransport(NetWire owner, int index)
        {
            wire = owner;
            Id = "p" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // The senderId every OnData on the far side carries. Stable and short so a scenario's
        // log lines stay readable.
        public string Id { get; }

        public string Room { get; private set; }

        public bool IsOpen { get; private set; }

        // Counters, so a scenario can assert about traffic without subscribing to OnData. TxSent
        // counts CALLS; TxDelivered counts endpoint-deliveries, so with one peer up they agree and
        // with three they do not -- which is the check that a fan-out really fanned out.
        public long TxSent { get; private set; }

        public long TxDelivered { get; private set; }

        public long RxDelivered { get; private set; }

        public int Pending => inbound.Count;

        public void Open(string room)
        {
            if (IsOpen)
            {
                return;
            }
            IsOpen = true;
            Room = room ?? string.Empty;
        }

        public void SendStream(byte[] payload)
        {
            Send(payload, reliable: false);
        }

        public void SendReliable(byte[] payload)
        {
            Send(payload, reliable: true);
        }

        private void Send(byte[] payload, bool reliable)
        {
            // A send on a closed endpoint is DROPPED, not thrown: NetSession.Stop() closes the
            // transport and a late tick must behave the way a closed DataChannel does, not take
            // the process down.
            if (!IsOpen || payload == null)
            {
                return;
            }
            TxSent++;
            TxDelivered += wire.Dispatch(this, payload, reliable);
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            // Announce BEFORE clearing our own queue: the bye goes to the peers, and anything
            // still inbound for us is now undeliverable (we will never Pump again while closed).
            wire.DispatchBye(this);
            inbound.Clear();
        }

        internal void Enqueue(byte[] payload, bool reliable, string from)
        {
            inbound.Enqueue((payload, reliable, from));
        }

        internal void RaiseBye(string from)
        {
            OnPeerBye?.Invoke(from);
        }

        // Drain up to `budget` packets. The budget comes from NetWire.Pump, which takes every
        // endpoint's count before draining any of them -- see the reasoning there. Public so a
        // scenario can drain ONE peer without the others (which is how a peer is made to lag a
        // tick behind); Pending is the whole-queue budget.
        public void Pump(int budget)
        {
            if (!IsOpen)
            {
                return;
            }
            for (int i = 0; i < budget && inbound.Count > 0; i++)
            {
                (byte[] Payload, bool Reliable, string From) item = inbound.Dequeue();
                RxDelivered++;
                OnData?.Invoke(item.Payload, item.Reliable, item.From);
            }
        }

        // Drain everything queued as of this call.
        public void Pump()
        {
            Pump(Pending);
        }
    }
}
