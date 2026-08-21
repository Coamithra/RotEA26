using System;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the in-process wire and everything that rides it as pure bytes
    // (card 25ad0659, step 1). Invoke with eaNetWire.test() from the browser console,
    // `eval NetWireTest` under eahl, or the ProbeNetWire case set in tools/sim/logic_probe.
    //
    // WHY THIS EXISTS. Two things had no end-to-end coverage at all before it:
    //   * the TRANSPORT CONTRACT. Two impls shipped (BroadcastChannel, WebRTC) and both are
    //     unreachable outside a browser, so "the lane bit survives", "the senderId is the
    //     sender", "a bye reaches the peers" were only ever observed incidentally in a
    //     two-window run whose rates are meaningless (see this directory's CLAUDE.md).
    //   * the CODEC round trip THROUGH a transport. NetProtocol's encoders and decoders are
    //     each exercised (logic_probe's ProbeWireEnums drives the validating decoders;
    //     NetPuppets.WireRoundTripTest drives EvDeath into the real ScoreVisualiser), but
    //     nothing had ever put a frame on a wire and decoded what came off the far end. A
    //     layout slip that writes and reads the same wrong offset passes an encode/decode
    //     pair and fails in play.
    //
    // DELIBERATELY GAME-FREE -- no ServiceHelper, no Game, no GraphicsDevice, no content. That
    // is what lets tools/sim/logic_probe load the built assembly on the desktop CLR and run
    // this for real with no browser and no GL (tools/CLAUDE.md, "Headless logic oracle").
    // Anything needing a live session or a real ComponentBin belongs in a different suite.
    //
    // NO REAL CLOCK is read anywhere in it, deliberately: every assertion below is either
    // clock-independent or driven by an explicit `now` we choose, so it cannot flake. That is
    // also why section 2 asserts ORDER and COUNT rather than measured delay -- delay
    // measurement is eaNetSim.test()'s job and it needs its own virtual clock.
    internal static class NetWireTest
    {
        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int pass = 0;
            int fail = 0;
            void Check(string what, bool ok)
            {
                sb.Append(ok ? "  PASS " : "  FAIL ").Append(what).Append('\n');
                if (ok) { pass++; } else { fail++; }
            }

            sb.Append("[netwire] in-process wire + wire-level codec round trips\n");

            sb.Append(" 1. transport contract\n");
            SectionContract(Check);

            sb.Append(" 1b. addressed sends (card 583a3ef8)\n");
            SectionAddressed(Check);

            sb.Append(" 2. NetImpairment composed over an endpoint\n");
            SectionImpairment(Check);

            sb.Append(" 3. codec round trips through the wire\n");
            SectionCodecs(Check);

            sb.Append(" 4. stream-lane reorder + dedup (ShipStateBuffer)\n");
            SectionStreamOrder(Check);

            sb.Append(" 5. scaled-i16 motion rates (card c1a38ef9)\n");
            SectionMotionRates(Check);

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "[netwire] {0} passed, {1} failed\n", pass, fail));
            return sb.ToString();
        }

        // ---- 1. the transport contract ----------------------------------------------------

        private static void SectionContract(Action<string, bool> check)
        {
            // Endpoint-count guard. A wire is sized by a caller's parameter, so the two ways to
            // get it wrong must throw rather than allocate or silently make a useless wire.
            check("NetWire(0) throws", Throws(() => new NetWire(0)));
            check("NetWire(MaxEndpoints + 1) throws", Throws(() => new NetWire(NetWire.MaxEndpoints + 1)));
            check("NetWire(MaxEndpoints) is allowed", !Throws(() => new NetWire(NetWire.MaxEndpoints)));

            NetWire wire = new NetWire(2);
            Recorder a = new Recorder(wire[0]);
            Recorder b = new Recorder(wire[1]);
            wire[0].Open("room");
            wire[1].Open("room");

            // Delivery is on the RECEIVER'S Pump, never inline on the send. Every ordering
            // scenario this rig exists for depends on that, and an inline-delivering transport
            // would make them all pass vacuously -- so it is asserted before anything else.
            wire[0].SendReliable(new byte[] { 1 });
            check("a send does not deliver inline", b.Count == 0 && wire[1].Pending == 1);
            wire.Pump();
            check("the receiver's Pump delivers it", b.Count == 1);

            // Lane bit and sender identity.
            b.Clear();
            wire[0].SendStream(new byte[] { 2 });
            wire[0].SendReliable(new byte[] { 3 });
            wire.Pump();
            check("both lanes arrive", b.Count == 2);
            check("the stream lane arrives as stream", b.Count == 2 && !b.Reliable[0]);
            check("the reliable lane arrives as reliable", b.Count == 2 && b.Reliable[1]);
            check("senderId is the sending endpoint's id",
                b.Count == 2 && b.From[0] == wire[0].Id && b.From[0] != wire[1].Id);

            // Order within a lane. Both lanes are ordered here (BroadcastChannelTransport is
            // too, for the same reason) -- reorder comes from NetImpairment, section 2.
            b.Clear();
            for (int i = 0; i < 32; i++)
            {
                wire[0].SendStream(new byte[] { (byte)i });
            }
            wire.Pump();
            bool ordered = b.Count == 32;
            for (int i = 0; ordered && i < 32; i++)
            {
                ordered = b.Payloads[i][0] == (byte)i;
            }
            check("32 stream sends arrive in order, none dropped", ordered);
            // No loopback at N=2 either: `a` has been subscribed to the SENDER throughout every
            // leg above, so nothing it sent may have come back to it. (The rooms wire asserts the
            // same thing at N=3; without this leg the two-endpoint case was uncovered.)
            check("nothing a sender sends comes back to it", a.Count == 0);

            // No aliasing. The caller may reuse its buffer the moment Send returns -- NetSession
            // does exactly that on the snapshot lane (a static scratch array). A queue that
            // handed the same array on would deliver whatever the sender happened to leave in
            // it, and both sides would agree, so nothing would look wrong.
            b.Clear();
            byte[] scratch = new byte[] { 0xAA, 0xBB };
            wire[0].SendStream(scratch);
            scratch[0] = 0x00;
            scratch[1] = 0x00;
            wire.Pump();
            check("the delivered payload is a copy, not the caller's buffer",
                b.Count == 1 && b.Payloads[0][0] == 0xAA && b.Payloads[0][1] == 0xBB);

            // A reply sent from inside a handler must not be delivered in the same Pump -- a
            // same-tick round trip no real transport can do, and one that would silently satisfy
            // an ordering assertion.
            //
            // BOTH DIRECTIONS, and the lower -> higher one is the leg that matters: NetWire.Pump
            // drains endpoints in index order, so a reply to a HIGHER index is the case a budget
            // taken inside each endpoint's own Pump would NOT have caught (measured -- that is
            // why the budget is captured for every endpoint up front). A test that only replied
            // downward passed either way.
            for (int dir = 0; dir < 2; dir++)
            {
                int sender = dir == 0 ? 0 : 1;      // dir 0: reply travels 1 -> 0 (downward)
                int replier = dir == 0 ? 1 : 0;     // dir 1: reply travels 0 -> 1 (upward)
                Recorder replyRx = new Recorder(wire[sender]);
                Action<byte[], bool, string> replyOnce = null;
                replyOnce = (payload, reliable, from) =>
                {
                    wire[replier].OnData -= replyOnce;
                    wire[replier].SendReliable(new byte[] { 0x5A });
                };
                wire[replier].OnData += replyOnce;
                wire[sender].SendReliable(new byte[] { 0x01 });
                wire.Pump();
                check("a reply sent while draining waits for the next Pump (p" + replier
                    + " -> p" + sender + ")", replyRx.Count == 0);
                wire.Pump();
                check("the reply arrives on the following Pump (p" + replier + " -> p" + sender + ")",
                    replyRx.Count == 1 && replyRx.Payloads[0][0] == 0x5A);
            }
            a.Clear();
            b.Clear();

            // Rooms isolate. This is what `?room=` promises on the BroadcastChannel rig; here it
            // additionally lets one wire host two independent pairings.
            NetWire rooms = new NetWire(3);
            Recorder r0 = new Recorder(rooms[0]);
            Recorder r1 = new Recorder(rooms[1]);
            Recorder r2 = new Recorder(rooms[2]);
            rooms[0].Open("alpha");
            rooms[1].Open("alpha");
            rooms[2].Open("beta");
            rooms[0].SendReliable(new byte[] { 7 });
            rooms.Pump();
            check("a send reaches the sender's room-mate", r1.Count == 1);
            check("a send does NOT reach another room", r2.Count == 0);
            check("a send never loops back to the sender", r0.Count == 0);

            // Fan-out is per endpoint, which is the property that makes the peer count a real
            // parameter rather than a number in a comment. TxSent counts calls, TxDelivered
            // counts endpoint-deliveries: with three room-mates one send must read 1 and 3.
            NetWire four = new NetWire(4);
            Recorder[] rec = new Recorder[4];
            for (int i = 0; i < 4; i++)
            {
                rec[i] = new Recorder(four[i]);
                four[i].Open("mesh");
            }
            four[0].SendStream(new byte[] { 9 });
            four.Pump();
            check("N=4: one send reaches all three peers",
                rec[1].Count == 1 && rec[2].Count == 1 && rec[3].Count == 1);
            check("N=4: the sender is not one of them", rec[0].Count == 0);
            check("N=4: TxSent counts calls, TxFanout counts the enqueues they produced",
                four[0].TxSent == 1 && four[0].TxFanout == 3);

            // A bye fans out to room-mates, and only to them. Best-effort by contract (the JS
            // pagehide frame), so the assertion is that it arrives, not when.
            NetWire byeWire = new NetWire(3);
            int byes1 = 0;
            int byes2 = 0;
            string byeFrom = null;
            byeWire[1].OnPeerBye += from => { byes1++; byeFrom = from; };
            byeWire[2].OnPeerBye += from => byes2++;
            byeWire[0].Open("alpha");
            byeWire[1].Open("alpha");
            byeWire[2].Open("beta");
            byeWire[0].Close();
            check("Close() byes the room-mate once", byes1 == 1);
            check("the bye names the departing endpoint", byeFrom == byeWire[0].Id);
            check("Close() does not bye another room", byes2 == 0);

            // A closed endpoint is inert both ways -- NetSession.Stop() closes the transport and
            // a late tick must behave like a closed DataChannel, not throw and not deliver.
            Recorder afterClose = new Recorder(byeWire[1]);
            byeWire[0].SendReliable(new byte[] { 1 });
            byeWire.Pump();
            check("a closed endpoint cannot send", afterClose.Count == 0);
            // The receive half needs a NON-ZERO baseline or it is near-vacuous: p0's RxDelivered
            // was already 0 before it closed, so "still 0" would hold even with both IsOpen guards
            // gone. Take the count from a second wire where the endpoint really did receive first.
            NetWire closeRx = new NetWire(2);
            closeRx[0].Open("alpha");
            closeRx[1].Open("alpha");
            closeRx[1].SendReliable(new byte[] { 1 });
            closeRx.Pump();
            long beforeClose = closeRx[0].RxDelivered;
            check("precondition: the endpoint had received something", beforeClose == 1);
            closeRx[0].Close();
            closeRx[1].SendReliable(new byte[] { 2 });
            closeRx.Pump();
            check("a closed endpoint receives nothing more", closeRx[0].RxDelivered == beforeClose);
        }

        // ---- 1b. addressed sends (card 583a3ef8) ------------------------------------------

        // The N-peer stages' unicast: SendStreamTo/SendReliableTo must reach EXACTLY the named
        // endpoint, and every way to address nothing (unknown id, self, closed target, right id
        // in the wrong room) must be a SILENT drop with TxSent moving and TxFanout not -- the
        // closed-DataChannel semantic INetTransport pins. A fan-out that quietly delivered a
        // "unicast" to everyone would satisfy every broadcast-era assertion, which is why the
        // negative "the others received NOTHING" legs carry this section.
        private static void SectionAddressed(Action<string, bool> check)
        {
            NetWire wire = new NetWire(4);
            Recorder[] rec = new Recorder[4];
            for (int i = 0; i < 4; i++)
            {
                rec[i] = new Recorder(wire[i]);
                wire[i].Open("mesh");
            }

            wire[0].SendStreamTo(wire[2].Id, new byte[] { 0x11 });
            wire.Pump();
            check("an addressed stream send reaches exactly its target",
                rec[2].Count == 1 && rec[2].Payloads[0][0] == 0x11);
            check("the other two endpoints received nothing",
                rec[1].Count == 0 && rec[3].Count == 0);
            check("it arrives on the stream lane", rec[2].Count == 1 && !rec[2].Reliable[0]);
            check("its senderId is still the sender", rec[2].Count == 1 && rec[2].From[0] == wire[0].Id);

            wire[0].SendReliableTo(wire[3].Id, new byte[] { 0x22 });
            wire.Pump();
            check("an addressed reliable send reaches exactly its target, on the reliable lane",
                rec[3].Count == 1 && rec[3].Reliable[0] && rec[1].Count == 0 && rec[2].Count == 1);

            // Counter arithmetic: +1 fanout per addressed enqueue, +N-1 per broadcast -- which is
            // what makes the silent drops below assertable at all.
            long sent0 = wire[0].TxSent;
            long fan0 = wire[0].TxFanout;
            wire[0].SendStreamTo(wire[1].Id, new byte[] { 1 });
            check("an addressed send moves TxSent by 1 and TxFanout by 1",
                wire[0].TxSent == sent0 + 1 && wire[0].TxFanout == fan0 + 1);
            wire[0].SendStream(new byte[] { 2 });
            check("a broadcast beside it still moves TxFanout by N-1",
                wire[0].TxSent == sent0 + 2 && wire[0].TxFanout == fan0 + 4);

            long sent1 = wire[0].TxSent;
            long fan1 = wire[0].TxFanout;
            wire[0].SendStreamTo("nope", new byte[] { 3 });
            check("an unknown peer id is a silent drop (TxSent moved, TxFanout did not)",
                wire[0].TxSent == sent1 + 1 && wire[0].TxFanout == fan1);
            wire[0].SendStreamTo(wire[0].Id, new byte[] { 4 });
            check("addressing yourself is a silent drop", wire[0].TxFanout == fan1);
            wire[3].Close();
            wire[0].SendReliableTo(wire[3].Id, new byte[] { 5 });
            check("a closed target is a silent drop", wire[0].TxFanout == fan1);
            wire.Pump();
            // rec[1]: the addressed {1} + the broadcast {2}. rec[2]: 0x11 + the broadcast.
            // rec[3]: closed before the pump, so still only 0x22. The three dropped sends
            // delivered nowhere.
            check("none of the dropped sends delivered anywhere",
                rec[1].Count == 2 && rec[2].Count == 2 && rec[3].Count == 1);

            // Room isolation holds for addressed sends: the right id in the WRONG room is the
            // same silent drop, with the same-room target as the positive control.
            NetWire rooms = new NetWire(3);
            Recorder q1 = new Recorder(rooms[1]);
            Recorder q2 = new Recorder(rooms[2]);
            rooms[0].Open("alpha");
            rooms[1].Open("alpha");
            rooms[2].Open("beta");
            long fanR = rooms[0].TxFanout;
            rooms[0].SendReliableTo(rooms[2].Id, new byte[] { 6 });
            rooms.Pump();
            check("an addressed send to a right id in the WRONG room is a silent drop",
                q2.Count == 0 && rooms[0].TxFanout == fanR);
            rooms[0].SendReliableTo(rooms[1].Id, new byte[] { 7 });
            rooms.Pump();
            check("the same-room target still receives (positive control)",
                q1.Count == 1 && q1.Payloads[0][0] == 7);

            // The NetImpairment decorator passes addressed sends through verbatim on both lanes
            // (TX is untouched by design -- impairment is RX-only).
            NetWire impWire = new NetWire(2);
            NetImpairment imp = new NetImpairment(impWire[0], 0f, 0f, 0f);
            Recorder impRx = new Recorder(impWire[1]);
            impWire[0].Open("room");
            impWire[1].Open("room");
            imp.SendStreamTo(impWire[1].Id, new byte[] { 8 });
            imp.SendReliableTo(impWire[1].Id, new byte[] { 9 });
            impWire.Pump();
            check("addressed sends pass through the NetImpairment decorator on both lanes",
                impRx.Count == 2 && !impRx.Reliable[0] && impRx.Reliable[1]);
        }

        // ---- 2. NetImpairment over a real endpoint ----------------------------------------

        private static void SectionImpairment(Action<string, bool> check)
        {
            // Production ALWAYS wraps the transport in NetImpairment (NetSession.StartWith), at
            // 0/0/0 for almost every session. That composition had never been executed outside a
            // browser; here it is, over a transport whose delivery we control.
            NetWire wire = new NetWire(2);
            NetImpairment clean = new NetImpairment(wire[1], 0f, 0f, 0f);
            Recorder rx = new Recorder(clean);
            wire[0].Open("room");
            wire[1].Open("room");
            for (int i = 0; i < 8; i++)
            {
                wire[0].SendStream(new byte[] { (byte)i });
                wire[0].SendReliable(new byte[] { (byte)(0x80 | i) });
            }
            wire.Pump();
            check("at 0/0/0 every packet is forwarded", rx.Count == 16);
            check("at 0/0/0 nothing is held or dropped", clean.HeldCount == 0 && clean.Dropped == 0);
            bool cleanOrder = rx.Count == 16;
            for (int i = 0; cleanOrder && i < 16; i++)
            {
                // Interleaved as sent: stream i, reliable 0x80|i, ... Both lanes forward inline
                // at 0/0/0, so the sequence must come out exactly as it went in.
                byte want = (i % 2 == 0) ? (byte)(i / 2) : (byte)(0x80 | (i / 2));
                cleanOrder = rx.Payloads[i][0] == want;
            }
            check("at 0/0/0 the order is unchanged", cleanOrder);

            // loss=100 with no lag: the headline invariant of the impairment layer, now proven
            // through a transport instead of a synthetic push. The reliable lane must be
            // untouched in EVERY configuration -- dropping it would break the contract everything
            // above INetTransport is entitled to assume.
            NetWire lossy = new NetWire(2);
            NetImpairment allLoss = new NetImpairment(lossy[1], 0f, 100f, 0f);
            Recorder lossRx = new Recorder(allLoss);
            lossy[0].Open("room");
            lossy[1].Open("room");
            for (int i = 0; i < 24; i++)
            {
                lossy[0].SendStream(new byte[] { (byte)i });
                lossy[0].SendReliable(new byte[] { (byte)i });
            }
            lossy.Pump();
            int reliableSeen = 0;
            int streamSeen = 0;
            bool reliableOrdered = true;
            int prev = -1;
            for (int i = 0; i < lossRx.Count; i++)
            {
                if (lossRx.Reliable[i])
                {
                    reliableSeen++;
                    reliableOrdered &= lossRx.Payloads[i][0] > prev;
                    prev = lossRx.Payloads[i][0];
                }
                else
                {
                    streamSeen++;
                }
            }
            check("loss=100 drops the whole stream lane", streamSeen == 0);
            check("loss=100 leaves the reliable lane complete", reliableSeen == 24);
            check("loss=100 leaves the reliable lane in order", reliableOrdered);
            // The control: "nothing arrived on the stream lane" must mean "it was dropped", not
            // "nothing was ever sent". Without this the leg above passes on a broken wire.
            check("loss=100 actually dropped 24 stream packets", allLoss.Dropped == 24);

            // lag>0: nothing is released until Pump is called with a `now` past the release
            // stamp, and then everything comes out in order on both lanes. `now` is chosen
            // explicitly here (the internal Receive/Pump seam) so no real clock is involved --
            // an Environment.TickCount64 read would make this leg time-dependent, and the
            // production OnData path still reads that clock until the seam lands.
            NetImpairment laggy = new NetImpairment(null, 200f, 0f, 0f);
            Recorder lagRx = new Recorder(laggy);
            for (int i = 0; i < 10; i++)
            {
                laggy.Receive(new byte[] { (byte)i }, i % 2 == 1, "p0", 1000L);
            }
            laggy.Pump(1100L);
            check("lag=200 holds everything at now+100", lagRx.Count == 0 && laggy.HeldCount == 10);
            laggy.Pump(1200L);
            check("lag=200 releases everything at now+200", lagRx.Count == 10);
            bool lagOrder = lagRx.Count == 10;
            int lastStream = -1;
            int lastReliable = -1;
            for (int i = 0; lagOrder && i < lagRx.Count; i++)
            {
                int v = lagRx.Payloads[i][0];
                if (lagRx.Reliable[i])
                {
                    lagOrder = v > lastReliable;
                    lastReliable = v;
                }
                else
                {
                    lagOrder = v > lastStream;
                    lastStream = v;
                }
            }
            check("lag=200 preserves per-lane order", lagOrder);
        }

        // ---- 3. codec round trips THROUGH the wire ----------------------------------------

        private static void SectionCodecs(Action<string, bool> check)
        {
            NetWire wire = new NetWire(2);
            Recorder rx = new Recorder(wire[1]);
            wire[0].Open("room");
            wire[1].Open("room");

            // Every frame below goes out on the lane production uses for it and is decoded from
            // what the far endpoint received -- never from the local encoder's return value,
            // which is what makes this a wire test rather than another encode/decode pair.
            byte[] Round(byte[] frame, bool reliable)
            {
                rx.Clear();
                if (reliable) { wire[0].SendReliable(frame); } else { wire[0].SendStream(frame); }
                wire.Pump();
                return rx.Count == 1 ? rx.Payloads[0] : null;
            }

            // MsgShipState (stream lane, ~30 Hz). senderMs is the sample time the jitter buffer
            // sorts on, so it is part of the layout that matters most.
            // The v21 roll rings ride as two ASYMMETRIC bytes, so a swap between them (or either
            // landing on the neighbouring field) cannot pass. Since v23 every frame leads with
            // its slot and the flags byte carries PRIMARY -- pinned SET here with a non-zero
            // slot, and CLEAR below on the extra-ship leg.
            byte[] ship = Round(NetProtocol.EncodeShipState(
                1, primary: true, 4242, 1234567u, new Vector2(123.5f, -45.25f), new Vector2(0.125f, -0.5f),
                1.75f, alive: true, shotCount: 200, shotsPerSec: 17, bulletLife: 640f,
                scriptGate: false, asplodeBits: 0xA5, bounceBits: 0x3C), reliable: false);
            bool shipOk = false;
            if (ship != null && NetProtocol.TryDecodeShipState(ship, out byte shipSlot, out bool shipPrimary,
                out ushort shipSeq, out ShipSample s, out int sps, out float blife))
            {
                shipOk = shipSlot == 1 && shipPrimary && shipSeq == 4242 && s.T == 1234567.0
                    && Near(s.Pos.X, 123.5f) && Near(s.Pos.Y, -45.25f)
                    && Near(s.Vel.X, 0.125f) && Near(s.Vel.Y, -0.5f)
                    && Near(s.Aim, 1.75f) && s.Alive && s.ShotCount == 200 && !s.ScriptGate
                    && sps == 17 && Near(blife, 640f)
                    && s.AsplodeBits == 0xA5 && s.BounceBits == 0x3C;
            }
            check("MsgShipState round-trips every field (slot + primary flag included)", shipOk);

            // Card 8a7772d6's script-gate bit. It shares the flags byte with `alive` (and, since
            // v23, `primary`), so the leg above pins it CLEAR while alive is SET, and this one
            // pins it SET while alive is CLEAR -- either half alone passes a bit wired to the
            // wrong mask.
            byte[] gated = Round(NetProtocol.EncodeShipState(
                0, primary: true, 1, 5u, Vector2.Zero, Vector2.Zero, 0f, alive: false, shotCount: 0,
                shotsPerSec: 8, bulletLife: 450f, scriptGate: true), reliable: false);
            bool gateOk = false;
            if (gated != null && NetProtocol.TryDecodeShipState(gated, out _, out _, out _,
                out ShipSample gs, out _, out _))
            {
                gateOk = gs.ScriptGate && !gs.Alive;
            }
            check("MsgShipState carries the script-gate flag independently of alive", gateOk);

            // EvIntroVolley (card 8a7772d6): a bare seed, and every 32-bit value is legal --
            // so the risk is the SIGN, which a positive-only seed would never show.
            byte[] volley = Round(NetProtocol.EncodeIntroVolleyEvent(9, int.MinValue), reliable: true);
            check("EvIntroVolley round-trips a negative seed",
                volley != null && NetProtocol.TryDecodeIntroVolleyEvent(volley, out int vseed)
                    && vseed == int.MinValue);
            check("EvIntroVolley refuses a truncated frame",
                !NetProtocol.TryDecodeIntroVolleyEvent(new byte[7], out _));

            // EvSlowmo (card a66e190a): a bare duration. The risk is the WIDTH -- the game sends
            // 12000 ms, which a byte would have silently truncated to 224 (a fifth of a second
            // of slow motion on the peer, which reads as "it nearly works").
            byte[] slowmo = Round(NetProtocol.EncodeSlowmoEvent(11, 12000), reliable: true);
            check("EvSlowmo round-trips a 12000ms duration",
                slowmo != null && NetProtocol.TryDecodeSlowmoEvent(slowmo, out ushort smMs)
                    && smMs == 12000);
            check("EvSlowmo refuses a truncated frame",
                !NetProtocol.TryDecodeSlowmoEvent(new byte[5], out _));
            // CLAMPED rather than refused: the field is a time scale that ends by itself, so
            // degrading a silly value beats dropping the message -- but a bare u16 is a 65.5 s
            // hold off a stranger's wire, against the 12 s the game can produce.
            byte[] tooLong = Round(NetProtocol.EncodeSlowmoEvent(12, ushort.MaxValue), reliable: true);
            check("EvSlowmo clamps an over-long duration to what the game can produce",
                tooLong != null && NetProtocol.TryDecodeSlowmoEvent(tooLong, out ushort clampedMs)
                    && clampedMs == NetProtocol.MaxSlowmoMs);

            // The extra-ship frame is the SAME message with the primary flag clear since v23
            // (card b2828be8) -- one layout, so the leg pins a different slot, the flag CLEAR,
            // and alive SET (an extra is only ever streamed while alive; its death is the
            // receiver's timeout).
            byte[] friend = Round(NetProtocol.EncodeShipState(
                3, primary: false, 77, 999u, new Vector2(-8f, 16f), new Vector2(-0.25f, 0.75f),
                -2.5f, alive: true, shotCount: 137, shotsPerSec: 5, bulletLife: 300f,
                scriptGate: false, asplodeBits: 0x81, bounceBits: 0x42), reliable: false);
            bool friendOk = false;
            if (friend != null && NetProtocol.TryDecodeShipState(friend, out byte fslot, out bool fprimary,
                out ushort fseq, out ShipSample fs, out int fsps, out float fblife))
            {
                friendOk = fslot == 3 && !fprimary && fseq == 77 && fs.T == 999.0
                    && Near(fs.Pos.X, -8f) && Near(fs.Pos.Y, 16f)
                    && Near(fs.Vel.X, -0.25f) && Near(fs.Vel.Y, 0.75f)
                    && Near(fs.Aim, -2.5f) && fs.Alive && fs.ShotCount == 137
                    && fsps == 5 && Near(fblife, 300f)
                    && fs.AsplodeBits == 0x81 && fs.BounceBits == 0x42;
            }
            check("an extra-ship frame round-trips every field (primary flag clear)", friendOk);

            // MsgHudState: variable-length, two slots, and a combo past 255 -- the value whose
            // width is load-bearing because the host SPENDS it (AwardScoreToAll -> comboModify).
            byte[] slots = new byte[] { 1, 2 };
            int[] combos = new int[] { 400, 3 };
            byte[] types = new byte[] { 2, NetProtocol.HudPowerupNone };
            float[] progress = new float[] { 0f, 1f };
            int[][] levels = new int[][]
            {
                new int[] { 1, 2, 3, 4, 0 },
                new int[] { 0, 0, 0, 0, 0 },
            };
            // Per-layer Option counts (v16, card c5228350). Asymmetric per entry AND per layer, so
            // a swap between the two layers or between the two entries cannot pass.
            int[][] optionCounts = new int[][]
            {
                new int[] { 4, 2 },
                new int[] { 0, 1 },
            };
            // v20: the owner-declared totals, distinct per entry so a cross-entry swap cannot pass.
            float[] scoreTotals = new float[] { 9001.5f, 17f };
            // v23: the combo timer's remaining fraction, distinct per entry AND distinct from
            // `progress` on entry 0 (0.75 against 0), so a swap with the bar cannot pass. The
            // byte quantisation makes the round trip exact at n/255 values only.
            float[] comboLefts = new float[] { 0.6f, 1f };
            byte[] hud = Round(NetProtocol.EncodeHudState(slots, combos, comboLefts, types, progress, levels, optionCounts, scoreTotals, 2), reliable: false);
            // One scratch array PER ENTRY. TryDecodeHudState writes the levels of whichever entry
            // it was asked for, so a shared buffer makes every later assertion depend on decode
            // ORDER -- which is the exact latent defect this commit fixes in NetComboTest. Cheap
            // here, so it is simply avoided rather than commented around.
            int[] outLevels = new int[NetProtocol.HudLevelCount];
            int[] outLevels1 = new int[NetProtocol.HudLevelCount];
            int[] outOptions = new int[NetProtocol.HudOptionLayers];
            int[] outOptions1 = new int[NetProtocol.HudOptionLayers];
            bool hudOk = hud != null
                && NetProtocol.TryDecodeHudCount(hud, out int hudCount) && hudCount == 2
                && NetProtocol.TryDecodeHudState(hud, 0, outLevels, outOptions, out byte hslot, out int hcombo,
                    out float hleft, out EvilAliens.Powerup.PowerupType? hactive, out float hprog, out float hscore)
                && hslot == 1 && hcombo == 400 && hscore == 9001.5f && hactive.HasValue && (byte)hactive.Value == 2
                && Near(hprog, 0f) && Math.Abs(hleft - 0.6f) < 1f / 255f
                && outLevels[0] == 1 && outLevels[1] == 2 && outLevels[2] == 3 && outLevels[3] == 4
                && outLevels[4] == 0
                && outOptions[0] == 4 && outOptions[1] == 2;
            check("MsgHudState round-trips entry 0 (combo > 255, comboLeft and the per-layer option counts survive)", hudOk);
            bool hud1Ok = hud != null
                && NetProtocol.TryDecodeHudState(hud, 1, outLevels1, outOptions1, out byte h1slot, out int h1combo,
                    out float h1left, out EvilAliens.Powerup.PowerupType? h1active, out float h1prog, out float h1score)
                && h1slot == 2 && h1combo == 3 && h1score == 17f && !h1active.HasValue && Near(h1prog, 1f)
                && Near(h1left, 1f)
                && outOptions1[0] == 0 && outOptions1[1] == 1;
            check("MsgHudState entry 1 decodes, and HudPowerupNone reads as no powerup", hud1Ok);

            // The handshake, both spellings. isHost is what tells the two roles apart and the
            // only difference between the two frames besides the type byte, so both are sent.
            byte[] hello = Round(NetProtocol.EncodeHello(
                NetSession.ProtocolVersion, isHost: false, buildHash: 0x0123456789ABCDEFul,
                flags: NetProtocol.HelloFlagDebugActive, primarySlot: NetProtocol.SlotNone,
                peerId: 0xFEDCBA9876543210ul, blockedSlots: 0x05), reliable: true);
            bool helloOk = hello != null && hello.Length == NetProtocol.HelloBytes
                && hello[0] == NetProtocol.MsgHello
                && NetProtocol.TryDecodeHandshake(hello, out byte hv, out bool hIsHost, out ulong hHash,
                    out byte hFlags, out byte hSlot, out ulong hPeer, out byte hBlocked)
                && hv == NetSession.ProtocolVersion && !hIsHost
                && hHash == 0x0123456789ABCDEFul && hFlags == NetProtocol.HelloFlagDebugActive
                && hSlot == NetProtocol.SlotNone && hPeer == 0xFEDCBA9876543210ul && hBlocked == 0x05;
            check("MsgHello round-trips v8 (hash, flags, slot, peerId, blockedSlots)", helloOk);
            byte[] welcome = Round(NetProtocol.EncodeWelcome(
                NetSession.ProtocolVersion, isHost: true, buildHash: 1ul, flags: 0,
                primarySlot: 1, peerId: 2ul, blockedSlots: 0), reliable: true);
            bool welcomeOk = welcome != null && welcome[0] == NetProtocol.MsgWelcome
                && NetProtocol.TryDecodeHandshake(welcome, out _, out bool wIsHost, out _, out _,
                    out byte wSlot, out _, out _)
                && wIsHost && wSlot == 1;
            check("MsgWelcome round-trips, and isHost distinguishes the roles", welcomeOk);

            // EvSpawn carries the whole base block plus per-type spawn extras, and the extras are
            // the part a length slip corrupts silently (the puppet builds with the wrong look).
            NetBaseState baseState = new NetBaseState
            {
                Pos = new Vector2(400f, 300f),
                Vel = new Vector2(-0.0625f, 0.03125f),
                Rotation = 0.5f,
                CurFrame = 7.5f,
                Scale = 1.25f,
                Hp = 40,
            };
            byte[] extras = new byte[] { 0xDE, 0xAD, 0xBE };
            byte[] spawn = Round(NetProtocol.EncodeSpawnEvent(11, 4096, 12, baseState, extras, extras.Length), reliable: true);
            bool spawnOk = spawn != null
                && spawn[0] == NetProtocol.MsgEvent && spawn[1] == NetProtocol.EvSpawn
                && NetProtocol.TryDecodeSpawnEvent(spawn, out ushort spNetId, out byte spType,
                    out NetBaseState spState, out int spExtraOff, out int spExtraLen)
                && spNetId == 4096 && spType == 12
                && Near(spState.Pos.X, 400f) && Near(spState.Pos.Y, 300f)
                && Near(spState.Vel.X, -0.0625f) && Near(spState.Vel.Y, 0.03125f)
                && Near(spState.Rotation, 0.5f) && Near(spState.CurFrame, 7.5f)
                && Near(spState.Scale, 1.25f) && spState.Hp == 40
                && spExtraLen == 3
                && spawn[spExtraOff] == 0xDE && spawn[spExtraOff + 1] == 0xAD
                && spawn[spExtraOff + 2] == 0xBE;
            check("EvSpawn round-trips the base block and its spawn extras", spawnOk);

            // A world snapshot is several length-prefixed entries in one packet, walked by a
            // ref offset -- the one message whose decode is stateful across entries, so two
            // entries with DIFFERENT extra lengths is the case that finds a stride bug.
            byte[] snap = new byte[512];
            int wOff = NetProtocol.SnapshotHeaderBytes;
            //
            // The two entries also carry DIFFERENT per-sample flags (card e79bb994), which is
            // what makes the flags leg below non-vacuous: a codec that dropped the byte, or read
            // it from a fixed offset, would report the same value for both.
            NetProtocol.WriteSnapshotEntry(snap, ref wOff, 101, 1,
                NetProtocol.NetSnapshotFlags.None, baseState, extras, 3);
            NetBaseState second = baseState;
            second.Pos = new Vector2(1f, 2f);
            second.Hp = 9;
            NetProtocol.WriteSnapshotEntry(snap, ref wOff, 202, 2,
                NetProtocol.NetSnapshotFlags.Teleported, second, null, 0);
            // The header is stamped LAST, as the sender does it -- the entry loop is what knows
            // how many entries fit. The seq (card f5cf7a5c) is picked ABOVE the u8 count's range
            // and with distinct high and low bytes, so a decoder reading it from the wrong offset
            // or as one byte cannot pass.
            const ushort SnapSeq = 0x2A07;
            NetProtocol.WriteSnapshotHeader(snap, 2, SnapSeq);
            byte[] packed = new byte[wOff];
            Array.Copy(snap, packed, wOff);
            byte[] gotSnap = Round(packed, reliable: false);
            int rOff = NetProtocol.SnapshotHeaderBytes;
            byte f1 = 0xFF;
            byte f2 = 0xFF;
            check("MsgWorldSnapshot's header round-trips its count AND its packet seq",
                gotSnap != null
                && NetProtocol.TryReadSnapshotHeader(gotSnap, out byte hdrCount, out ushort hdrSeq)
                && hdrCount == 2 && hdrSeq == SnapSeq);
            // The negatives beside it: the header decoder is what every entry walk is gated on,
            // so a truncated or mistyped packet must be refused rather than read as count 0.
            check("a snapshot packet shorter than its header is refused",
                !NetProtocol.TryReadSnapshotHeader(new byte[] { NetProtocol.MsgWorldSnapshot, 1, 0 },
                    out _, out _));
            check("a packet whose type is not MsgWorldSnapshot is refused",
                !NetProtocol.TryReadSnapshotHeader(new byte[] { NetProtocol.MsgShipState, 1, 0, 0 },
                    out _, out _));
            bool snapOk = gotSnap != null && gotSnap[1] == 2
                && NetProtocol.TryReadSnapshotEntry(gotSnap, ref rOff, out ushort id1, out byte t1,
                    out f1, out NetBaseState st1, out int e1Off, out int e1Len)
                && id1 == 101 && t1 == 1 && e1Len == 3 && gotSnap[e1Off] == 0xDE && st1.Hp == 40
                && NetProtocol.TryReadSnapshotEntry(gotSnap, ref rOff, out ushort id2, out byte t2,
                    out f2, out NetBaseState st2, out _, out int e2Len)
                && id2 == 202 && t2 == 2 && e2Len == 0 && st2.Hp == 9
                && Near(st2.Pos.X, 1f) && Near(st2.Pos.Y, 2f)
                && rOff == gotSnap.Length;
            check("MsgWorldSnapshot walks two entries of different extra length", snapOk);
            check("snapshot entry flags survive the wire per ENTRY (none vs teleported)",
                f1 == NetProtocol.NetSnapshotFlags.None
                && f2 == NetProtocol.NetSnapshotFlags.Teleported);

            // An unrecognised BIT must survive decode rather than being refused: NetSnapshotFlags
            // is a bitmask, not a wire enum, so a peer masks the bits it knows and ignores the
            // rest. A decoder that validated the byte as a whole would drop the entry -- i.e.
            // stop correcting that entity -- the moment a later build appended a flag.
            byte[] fut = new byte[NetProtocol.SnapshotHeaderBytes + NetProtocol.SnapshotEntryBaseBytes];
            NetProtocol.WriteSnapshotHeader(fut, 1, 0);
            int futOff = NetProtocol.SnapshotHeaderBytes;
            const byte FutureBits = 0x81; // Teleported + an undefined high bit
            NetProtocol.WriteSnapshotEntry(fut, ref futOff, 303, 3, FutureBits, second, null, 0);
            byte[] gotFut = Round(fut, reliable: false);
            int futROff = NetProtocol.SnapshotHeaderBytes;
            check("an unknown snapshot flag BIT decodes rather than refusing the entry",
                gotFut != null
                && NetProtocol.TryReadSnapshotEntry(gotFut, ref futROff, out ushort idF, out _,
                    out byte flagsF, out _, out _, out _)
                && idF == 303 && flagsF == FutureBits
                && (flagsF & NetProtocol.NetSnapshotFlags.Teleported) != 0);

            // A snapshot entry one byte short of the base block must be REFUSED, not read past
            // its end -- the flags byte grew that block, so this is the boundary that moved.
            byte[] runt = new byte[NetProtocol.SnapshotHeaderBytes + NetProtocol.SnapshotEntryBaseBytes];
            NetProtocol.WriteSnapshotHeader(runt, 1, 0);
            runt[NetProtocol.SnapshotHeaderBytes] = (byte)(NetProtocol.SnapshotEntryBaseBytes - 1);
            byte[] gotRunt = Round(runt, reliable: false);
            int runtOff = NetProtocol.SnapshotHeaderBytes;
            check("a snapshot entry shorter than the base block is refused",
                gotRunt != null
                && !NetProtocol.TryReadSnapshotEntry(gotRunt, ref runtOff, out _, out _, out _,
                    out _, out _, out _));

            byte[] bg = Round(NetProtocol.EncodeBackgroundEvent(
                12, (byte)NetBackgroundOp.SetSpeed, new Vector2(0.75f, -0.25f)), reliable: true);
            bool bgOk = bg != null
                && NetProtocol.TryDecodeBackgroundEvent(bg, out NetBackgroundOp op, out Vector2 bv)
                && op == NetBackgroundOp.SetSpeed && Near(bv.X, 0.75f) && Near(bv.Y, -0.25f);
            check("EvBackground round-trips the opcode and its vector", bgOk);

            byte[] swarm = Round(NetProtocol.EncodeCosmeticSwarmEvent(
                13, (byte)NetCosmeticKind.FlyingSpiderBackground, on: true, rate: 5.5f), reliable: true);
            bool swarmOk = swarm != null
                && NetProtocol.TryDecodeCosmeticSwarmEvent(swarm, out NetCosmeticKind kind,
                    out bool on, out float rate)
                && kind == NetCosmeticKind.FlyingSpiderBackground && on && Near(rate, 5.5f);
            check("EvCosmeticSwarm round-trips kind/on/rate", swarmOk);

            // The string-carrying events. Length-prefixed text is the other stride-sensitive
            // shape, and it is the one an over-long banner would overflow.
            byte[] msg = Round(NetProtocol.EncodeMessageEvent(
                14, 0, 0, 1.25f, "wire test"), reliable: true);
            bool msgOk = msg != null
                && NetProtocol.TryDecodeMessageEvent(msg, out EvilAliens.AnimatedMessage.MessageType mt,
                    out EvilAliens.SoundManager.Texts speech, out float angle, out string text, out _)
                && (byte)mt == 0 && (byte)speech == 0 && Near(angle, 1.25f) && text == "wire test";
            check("EvMessage round-trips its text and angle", msgOk);

            // The `short` flag (the compact boss warning arrow) is APPENDED PAST the variable-length
            // text, which is what makes it optional in both directions and is why this change needed
            // no protocol bump. Three legs, and the third is the one that matters: a frame WITHOUT
            // the byte -- i.e. one an older peer encoded -- must still decode, as not-short.
            byte[] msgShort = Round(NetProtocol.EncodeMessageEvent(
                14, 1, 0, 2.5f, "danger", isShort: true), reliable: true);
            check("EvMessage carries the short flag past its text", msgShort != null
                && NetProtocol.TryDecodeMessageEvent(msgShort, out _, out _, out float shortAngle,
                    out string shortText, out bool wasShort)
                && wasShort && shortText == "danger" && Near(shortAngle, 2.5f));
            check("...and a banner encoded without it decodes as not-short", msg != null
                && NetProtocol.TryDecodeMessageEvent(msg, out _, out _, out _, out _, out bool notShort)
                && !notShort);
            byte[] msgLegacy = msgShort != null ? Truncate(msgShort) : null;
            check("a frame with the flag byte MISSING (an older peer's) still decodes, as not-short",
                msgLegacy != null
                && NetProtocol.TryDecodeMessageEvent(msgLegacy, out _, out _, out _,
                    out string legacyText, out bool legacyShort)
                && !legacyShort && legacyText == "danger");

            // EvFx: the transient-feedback beat. A netId flanked by two single bytes, which is
            // exactly the shape a one-byte offset slip reads back as a plausible WRONG entity --
            // and a beat applied to the wrong puppet is invisible in every log.
            byte[] fx = Round(NetProtocol.EncodeFxEvent(
                21, (byte)NetFxKind.BallDetach, 4242, 7), reliable: true);
            check("EvFx round-trips kind/netId/param", fx != null
                && NetProtocol.TryDecodeFxEvent(fx, out NetFxKind fxKind, out ushort fxId,
                    out byte fxParam)
                && fxKind == NetFxKind.BallDetach && fxId == 4242 && fxParam == 7);
            // netId 0 is the reserved "positional, no entity" form (EnemyLazerFire), so it has to
            // survive the round trip rather than reading as a decode failure.
            byte[] fxPositional = Round(NetProtocol.EncodeFxEvent(
                22, (byte)NetFxKind.EnemyLazerFire, 0, 0), reliable: true);
            check("EvFx carries the entity-free form (netId 0)", fxPositional != null
                && NetProtocol.TryDecodeFxEvent(fxPositional, out NetFxKind lazerKind, out ushort zeroId,
                    out _)
                && lazerKind == NetFxKind.EnemyLazerFire && zeroId == 0);

            // The inline-decoded families (EvDeath / EvClaim / EvScoreSync / EvBlast and the
            // bare byte/empty events) are read straight out of the buffer in
            // NetSession.HandleEvent rather than through a Try* decoder, so what is pinned here
            // is the ENVELOPE -- type byte, seq and total length -- which is what a layout slip
            // in one of them moves. Since v20 EvDeath carries no award payload (card af96bcc2);
            // the score's own wire leg lives in NetScoreTest (eaNetScore.test), not here.
            byte[] death = Round(NetProtocol.EncodeDeathEvent(15, 700, 1, new Vector2(5f, 6f)), reliable: true);
            check("EvDeath envelope: type, seq and DeathEventBytes",
                death != null && death.Length == NetProtocol.DeathEventBytes
                && death[0] == NetProtocol.MsgEvent && death[1] == NetProtocol.EvDeath
                && death[2] == 15 && death[3] == 0);
            byte[] claim = Round(NetProtocol.EncodeClaimEvent(16, 700, 2), reliable: true);
            check("EvClaim envelope", claim != null
                && claim[0] == NetProtocol.MsgEvent && claim[1] == NetProtocol.EvClaim && claim[2] == 16);
            byte[] sync = Round(NetProtocol.EncodeScoreSync(17, 3), reliable: true);
            check("EvScoreSync envelope", sync != null
                && sync[0] == NetProtocol.MsgEvent && sync[1] == NetProtocol.EvScoreSync && sync[2] == 17);
            byte[] blast = Round(NetProtocol.EncodeBlastEvent(18, 1, new Vector2(9f, 9f), 2), reliable: true);
            check("EvBlast envelope", blast != null
                && blast[0] == NetProtocol.MsgEvent && blast[1] == NetProtocol.EvBlast && blast[2] == 18);
            // EvRespawn (card 37f3a663) DOES have a Try* decoder, so it is round-tripped by
            // VALUE rather than by envelope. Slot, position and duration are driven to three
            // distinct values so a pair of swapped offsets cannot pass.
            byte[] respawn = Round(NetProtocol.EncodeRespawnEvent(21, 3, new Vector2(123f, 456f), 9500), reliable: true);
            check("EvRespawn round-trips slot/pos/duration", respawn != null
                && respawn[0] == NetProtocol.MsgEvent && respawn[1] == NetProtocol.EvRespawn
                && NetProtocol.TryDecodeRespawnEvent(respawn, out byte rsSlot, out Vector2 rsPos,
                    out int rsMs)
                && rsSlot == 3 && rsPos.X == 123f && rsPos.Y == 456f && rsMs == 9500);
            byte[] pause = Round(NetProtocol.EncodeByteEvent(19, NetProtocol.EvPause, 1), reliable: true);
            check("a byte event envelope carries its value", pause != null
                && pause.Length == 5 && pause[1] == NetProtocol.EvPause && pause[4] == 1);
            byte[] ready = Round(NetProtocol.EncodeEmptyEvent(20, NetProtocol.EvReady), reliable: true);
            check("an empty event is envelope-only", ready != null
                && ready.Length == 4 && ready[1] == NetProtocol.EvReady && ready[2] == 20);

            // NEGATIVE half: a frame one byte short must be REFUSED, not decoded from whatever
            // follows. Every positive leg above would pass on a decoder with no length check, so
            // without these the section proves only that encode and decode agree.
            check("a truncated MsgShipState is refused",
                !NetProtocol.TryDecodeShipState(Truncate(ship), out _, out _, out _, out _, out _, out _));
            check("a truncated extra-ship frame is refused",
                !NetProtocol.TryDecodeShipState(Truncate(friend), out _, out _, out _, out _, out _, out _));
            check("a truncated MsgHudState is refused (count vs bytes)",
                !NetProtocol.TryDecodeHudCount(Truncate(hud), out _));
            check("a truncated handshake is refused",
                !NetProtocol.TryDecodeHandshake(Truncate(hello), out _, out _, out _, out _, out _, out _, out _));
            check("a truncated EvSpawn is refused",
                !NetProtocol.TryDecodeSpawnEvent(Truncate(spawn), out _, out _, out _, out _, out _));
            check("a truncated EvBackground is refused",
                !NetProtocol.TryDecodeBackgroundEvent(Truncate(bg), out _, out _));
            check("a truncated EvCosmeticSwarm is refused",
                !NetProtocol.TryDecodeCosmeticSwarmEvent(Truncate(swarm), out _, out _, out _));
            check("a truncated EvRespawn is refused",
                !NetProtocol.TryDecodeRespawnEvent(Truncate(respawn), out _, out _, out _));
            // TWICE, and that is not a typo: since the short flag was appended, dropping ONE byte
            // off a banner produces a legal older-peer frame (asserted three lines up), so a
            // single Truncate here would assert the OPPOSITE of the compatibility leg and fail.
            // Two bytes cuts into the text itself, which is the bound this leg is actually about.
            check("an EvMessage truncated INTO its text is refused",
                !NetProtocol.TryDecodeMessageEvent(Truncate(Truncate(msg)), out _, out _, out _, out _, out _));
            check("a truncated EvFx is refused",
                !NetProtocol.TryDecodeFxEvent(Truncate(fx), out _, out _, out _));
            // The kind is REJECT-policy, so an out-of-enum byte must drop the whole frame rather
            // than decode to something plausible -- a beat is EXECUTED on arrival.
            byte[] fxBadKind = fx != null ? (byte[])fx.Clone() : new byte[8];
            fxBadKind[4] = 200;
            check("an EvFx naming an unknown kind is refused",
                !NetProtocol.TryDecodeFxEvent(fxBadKind, out _, out _, out _));
            // ...with the positive control beside it, or a decoder that refused EVERYTHING would
            // pass the line above.
            byte[] fxGoodKind = fx != null ? (byte[])fx.Clone() : new byte[8];
            fxGoodKind[4] = (byte)NetFxKind.EnemyHitFlash;
            check("...and the same frame with a known kind is accepted (positive control)",
                NetProtocol.TryDecodeFxEvent(fxGoodKind, out NetFxKind okKind, out _, out _)
                && okKind == NetFxKind.EnemyHitFlash);
            // A frame of the right length whose TYPE byte is wrong must also be refused -- the
            // lanes are shared, so every decoder is handed frames of other types routinely.
            // MsgFriendState is the RETIRED pre-v23 id, which makes it the perfect wrong type:
            // an old build's extra-ship frame must decode as nothing at all.
            byte[] mistyped = ship != null ? (byte[])ship.Clone() : new byte[NetProtocol.ShipStateBytes];
            mistyped[0] = NetProtocol.MsgFriendState;
            check("a correctly-sized frame of the wrong (retired) type is refused",
                !NetProtocol.TryDecodeShipState(mistyped, out _, out _, out _, out _, out _, out _));
        }

        // ---- 4. stream-lane reorder + dedup ----------------------------------------------

        private static void SectionStreamOrder(Action<string, bool> check)
        {
            // The stream lane may drop and REORDER by contract, and ShipStateBuffer is what has
            // to tolerate it: a sample no newer than the newest is refused, so `Add` returning
            // false is exactly the StreamDropped counter's source. Driven here off real
            // MsgShipState frames pulled off the wire rather than hand-built samples, so a
            // senderMs layout slip shows up as a buffer that stops rejecting stale samples.
            //
            // The reorder is applied at the SEND site: the wire itself is ordered (like
            // BroadcastChannel), and sending a scrambled sequence models the lane having
            // reordered them. NetImpairment's jitter is the other way in, but it needs a clock,
            // and this assertion should not.
            NetWire wire = new NetWire(2);
            ShipStateBuffer buffer = new ShipStateBuffer();
            int accepted = 0;
            int refused = 0;
            wire[1].OnData += (payload, reliable, from) =>
            {
                if (NetProtocol.TryDecodeShipState(payload, out _, out _, out _, out ShipSample s, out _, out _))
                {
                    if (buffer.Add(s)) { accepted++; } else { refused++; }
                }
            };
            wire[0].Open("room");
            wire[1].Open("room");

            // Sender times 0,33,66,...  delivered as 0, 66, 33, 99, 132, 132(dup), 165.
            // 33 arrives after 66 (stale) and the repeat of 132 is a duplicate: both refused.
            uint[] order = new uint[] { 0u, 66u, 33u, 99u, 132u, 132u, 165u };
            for (int i = 0; i < order.Length; i++)
            {
                wire[0].SendStream(NetProtocol.EncodeShipState(
                    0, primary: true, (ushort)i, order[i], new Vector2(order[i], 0f), Vector2.Zero, 0f,
                    alive: true, shotCount: 0, shotsPerSec: 8, bulletLife: 450f));
            }
            wire.Pump();
            check("every stream frame was delivered and decoded", accepted + refused == order.Length);
            check("a stale (reordered) sample is refused", refused == 2);
            check("the fresh samples are accepted", accepted == 5);
            check("the buffer's newest is the newest sender time", buffer.NewestMs == 165.0);

            // Positive control for the refusals: the SAME times in order must all be accepted,
            // so "refused == 2" is the reorder being caught and not the buffer refusing
            // everything (a buffer whose Add always returned false would pass the leg above).
            ShipStateBuffer monotone = new ShipStateBuffer();
            int monoAccepted = 0;
            uint[] sorted = new uint[] { 0u, 33u, 66u, 99u, 132u, 165u };
            for (int i = 0; i < sorted.Length; i++)
            {
                byte[] frame = NetProtocol.EncodeShipState(0, primary: true, (ushort)i, sorted[i], Vector2.Zero,
                    Vector2.Zero, 0f, alive: true, shotCount: 0, shotsPerSec: 8, bulletLife: 450f);
                if (NetProtocol.TryDecodeShipState(frame, out _, out _, out _, out ShipSample s, out _, out _)
                    && monotone.Add(s))
                {
                    monoAccepted++;
                }
            }
            check("control: an in-order stream is accepted in full", monoAccepted == sorted.Length);
        }

        // ---- 5. scaled-i16 motion rates ---------------------------------------------------
        //
        // The primitive under LazerDescriptor's three sent rates and FlyingSpiderDescriptor's
        // amplitude/phase (card c1a38ef9). The DESCRIPTORS themselves cannot be tested here --
        // building an entity needs a Game, and this suite is deliberately Game-free so it also
        // runs under tools/sim/logic_probe -- so what is pinned is the two-byte field they are
        // built on, over the real wire: sign, saturation, and that the two SCALES are not
        // interchangeable.
        private static void SectionMotionRates(Action<string, bool> check)
        {
            NetWire wire = new NetWire(2);
            Recorder rx = new Recorder(wire[1]);
            wire[0].Open("room");
            wire[1].Open("room");

            // Six rate fields in one frame, mirroring LazerDescriptor's block shape and sent on
            // the STREAM lane, which is where snapshot state extras really ride.
            float[] values = { 0.4f, 0f, -0.0007f, 0.0007f, 12.5f, -12.5f };
            float[] scales =
            {
                NetProtocol.RatePxPerMsScale, NetProtocol.RatePxPerMsScale,
                NetProtocol.RateRadPerMsScale, NetProtocol.RateRadPerMsScale,
                NetProtocol.RatePxPerMsScale, NetProtocol.RatePxPerMsScale,
            };
            byte[] frame = new byte[values.Length * 2];
            int w = 0;
            for (int i = 0; i < values.Length; i++)
            {
                NetProtocol.WriteScaledI16(frame, ref w, values[i], scales[i]);
            }
            check("the block is exactly two bytes per rate", w == frame.Length);

            rx.Clear();
            wire[0].SendStream(frame);
            wire.Pump();
            byte[] got = rx.Count == 1 ? rx.Payloads[0] : null;
            bool allBack = got != null && got.Length == frame.Length;
            if (allBack)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    // 1/scale is the quantisation step, so half of it is the tightest honest bound.
                    if (Math.Abs(NetProtocol.ReadScaledI16(got, i * 2, scales[i]) - values[i])
                        > 0.5f / scales[i])
                    {
                        allBack = false;
                    }
                }
            }
            check("every rate round-trips within its quantisation step", allBack);

            // THE DISCRIMINATING LEG. A wrapping (short) cast of an out-of-range value flips the
            // SIGN, which on the angle field turns the miniboss' sweep into a counter-sweep -- so
            // saturation is asserted in BOTH directions and by sign, not just by magnitude.
            byte[] sat = new byte[4];
            int sw = 0;
            NetProtocol.WriteScaledI16(sat, ref sw, 1000f, NetProtocol.RatePxPerMsScale);
            NetProtocol.WriteScaledI16(sat, ref sw, -1000f, NetProtocol.RatePxPerMsScale);
            float satHi = NetProtocol.ReadScaledI16(sat, 0, NetProtocol.RatePxPerMsScale);
            float satLo = NetProtocol.ReadScaledI16(sat, 2, NetProtocol.RatePxPerMsScale);
            // The range is asymmetric because two's complement is (-32768..32767), so the two
            // ends are NOT mirror images -- asserting one figure for both would be asserting a
            // wrong number that a wrapping cast could still satisfy.
            check("an over-range rate SATURATES rather than wrapping (sign kept)",
                satHi > 0f && satLo < 0f && Near(satHi, 32.767f) && Near(satLo, -32.768f));

            // THE TWO SCALES ARE NOT INTERCHANGEABLE, which is the whole reason there are two.
            // The miniboss sweep is -0.0007 rad/ms: at the rad scale that is 7 wire units and
            // survives exactly, while at the px scale it is 0.7 of a unit and lands on 1 -- a 43%
            // error in the beam's angular rate, integrated over the beam's whole life. Sharing
            // one scale would leave every swept beam turning at the wrong speed with no frame and
            // no counter to say so.
            byte[] fine = new byte[4];
            int fw = 0;
            NetProtocol.WriteScaledI16(fine, ref fw, -0.0007f, NetProtocol.RateRadPerMsScale);
            NetProtocol.WriteScaledI16(fine, ref fw, -0.0007f, NetProtocol.RatePxPerMsScale);
            check("the sweep rate survives exactly at the rad scale",
                Math.Abs(NetProtocol.ReadScaledI16(fine, 0, NetProtocol.RateRadPerMsScale)
                    - -0.0007f) < 0.00001f);
            check("...and is badly quantised at the px scale (why the scales differ)",
                Math.Abs(NetProtocol.ReadScaledI16(fine, 2, NetProtocol.RatePxPerMsScale)
                    - -0.0007f) > 0.0002f);
        }

        // ---- helpers ---------------------------------------------------------------------

        // Collects everything an endpoint delivers, so a leg can assert on order and lane
        // without each one re-writing the subscription.
        private sealed class Recorder
        {
            public readonly System.Collections.Generic.List<byte[]> Payloads
                = new System.Collections.Generic.List<byte[]>();
            public readonly System.Collections.Generic.List<bool> Reliable
                = new System.Collections.Generic.List<bool>();
            public readonly System.Collections.Generic.List<string> From
                = new System.Collections.Generic.List<string>();

            public Recorder(INetTransport t)
            {
                t.OnData += (payload, reliable, from) =>
                {
                    Payloads.Add(payload);
                    Reliable.Add(reliable);
                    From.Add(from);
                };
            }

            public int Count => Payloads.Count;

            public void Clear()
            {
                Payloads.Clear();
                Reliable.Clear();
                From.Clear();
            }
        }

        private static bool Throws(Action a)
        {
            try
            {
                a();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // One byte short of the real frame. Null-safe so a leg whose frame never arrived reports
        // the missing frame rather than throwing here and losing the rest of the section.
        private static byte[] Truncate(byte[] frame)
        {
            if (frame == null || frame.Length < 2)
            {
                return new byte[1];
            }
            byte[] shorter = new byte[frame.Length - 1];
            Array.Copy(frame, shorter, shorter.Length);
            return shorter;
        }

        // Wire floats are written verbatim (WriteF32), so every value chosen above is exactly
        // representable and this could be equality. It is a tolerance anyway so that a future
        // quantised field (the existing curframe x64 / scale x256 pattern) does not need the
        // comparison rewritten.
        private static bool Near(float a, float b)
        {
            return Math.Abs(a - b) < 0.001f;
        }
    }
}
