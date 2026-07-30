using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the INetHost seam (card 25ad0659, step 2a). Invoke with eaNetHost()
    // from the browser console, `eval NetHostTest` under eahl, or the ProbeNetHost case set in
    // tools/sim/logic_probe.
    //
    // WHAT IT IS FOR. Step 2a moves a clock and a dozen flag reads behind an interface and
    // claims the production path is unchanged. That claim has two halves and they fail in
    // completely different ways, so they are asserted separately:
    //   * "the production host still reads what the call site read" -- a mis-wired member
    //     (NetJip returning DebugFlags.NetLog) changes behaviour silently and no counter moves.
    //     Section 2 compares every member against its own source, and drives the impairment
    //     triple to three DISTINCT values so a swap among them cannot pass.
    //   * "the seam is actually load-bearing" -- if a consumer kept its own
    //     Environment.TickCount64 the refactor bought nothing, and every downstream scenario
    //     would go on being a race. Section 3 proves the injected clock reaches the real
    //     NetImpairment queue over a real NetWire endpoint.
    //
    // SECTION 3 IS THE ONE THAT DISCRIMINATES, and it is built so the POSITIVE assertion is the
    // discriminator (this repo's probe rule: an absence assertion passes on a run that never
    // got there). The virtual clock starts at 0, so a wall-clock read stamps arrival at the
    // machine's uptime -- far in the future of anything we then Pump to -- and the packet is
    // never delivered at all. Verified by reverting NetImpairment.OnInnerData: the
    // "released on the host's clock" assertion fails, the "not yet due" one still passes.
    //
    // DELIBERATELY GAME-FREE -- no ServiceHelper, no Game, no GraphicsDevice, no content, and
    // no real clock outside the one bounded sanity check in section 2. That is what lets
    // tools/sim/logic_probe run it on the desktop CLR, and what keeps it non-flaky.
    //
    // WHAT IT DOES NOT COVER, so nobody reads a green tick as more than it is: NetPuppets' six
    // clock reads and NetSession's own NowMs have no Game-free consumer to point at (they sit
    // behind private statics and a live session). NetSession's is covered on the live path by
    // NetResetSpawnTest, which runs on a PinnedNetHost since this card; NetPuppets' are covered
    // by the decompiled-diff review and by net_selftests.txt staying green, not by an assertion
    // here.
    internal static class NetHostTest
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

            sb.Append("[nethost] INetHost seam: production mapping + the injected clock\n");

            sb.Append(" 1. NetHost.Current contract\n");
            SectionCurrent(Check);

            sb.Append(" 2. ServiceHelperNetHost maps 1:1 onto what the call sites read\n");
            SectionProductionMapping(Check, sb);

            sb.Append(" 3. the injected clock reaches NetImpairment over a real endpoint\n");
            SectionInjectedClock(Check);

            sb.Append(" 4. impairment knobs come from the host, and explicit overrides still win\n");
            SectionImpairmentKnobs(Check);

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "[nethost] {0} passed, {1} failed\n", pass, fail));
            return sb.ToString();
        }

        // ---- 1. the holder -----------------------------------------------------------------

        private static void SectionCurrent(Action<string, bool> check)
        {
            INetHost entry = NetHost.Current;
            check("the suite starts on the production host (nothing leaked an override)",
                ReferenceEquals(entry, NetHost.Production));

            PinnedNetHost pinned = new PinnedNetHost();
            try
            {
                NetHost.Current = pinned;
                check("an installed host is what Current returns", ReferenceEquals(NetHost.Current, pinned));
                check("and the layer reads it", NetHost.Current.NowMs == 0L);

                // The null case is the one that matters at teardown: a scenario's finally must be
                // able to hand the seam back without knowing what was there before, and a Current
                // that could go null would NRE the whole net layer on the next tick.
                NetHost.Current = null;
                check("assigning null restores the production host",
                    ReferenceEquals(NetHost.Current, NetHost.Production));
                check("the production host is a real clock again", NetHost.Current.NowMs > 0L);
            }
            finally
            {
                NetHost.Current = entry;
            }
            check("the seam is restored on the way out", ReferenceEquals(NetHost.Current, entry));
        }

        // ---- 2. the production mapping -----------------------------------------------------

        private static void SectionProductionMapping(Action<string, bool> check, StringBuilder sb)
        {
            INetHost host = NetHost.Production;

            // The clock. Bounded rather than equal: two reads of TickCount64 straddling an
            // interface call are not obliged to agree, and asserting equality would be a
            // once-in-a-while FAIL that says nothing.
            long before = Environment.TickCount64;
            long viaHost = host.NowMs;
            long after = Environment.TickCount64;
            check("NowMs is Environment.TickCount64 (" + viaHost + " within [" + before + "," + after + "])",
                viaHost >= before && viaHost <= after);

            // The two fingerprints. Compared against the expression they replaced, INCLUDING its
            // ?netfake* branch, so a host that dropped the override would fail here rather than
            // in a two-window run where "the peers refuse each other" has a dozen causes.
            string expectHash = string.IsNullOrEmpty(DebugFlags.NetFakeBuildHash)
                ? WebRtcInterop.BuildHash()
                : DebugFlags.NetFakeBuildHash;
            string expectToken = string.IsNullOrEmpty(DebugFlags.NetFakePeerId)
                ? WebRtcInterop.PeerId()
                : DebugFlags.NetFakePeerId;
            check("BuildHash resolves ?netfakehash exactly as StartWith used to ('" + host.BuildHash + "')",
                host.BuildHash == expectHash);
            check("PeerToken resolves ?netfakepeer exactly as StartWith used to",
                host.PeerToken == expectToken);

            // The boolean/int flags. These compare EQUAL-TO-SOURCE, which on a boot where they
            // are all false cannot distinguish two members wired to each other -- so the values
            // in force are reported rather than merely asserted, and a run under ?netlog /
            // ?netjip / ?netlocal= is the one that makes this leg bite. The impairment triple
            // below is the leg that is non-vacuous unconditionally.
            check("DebugActive == DebugFlags.Active", host.DebugActive == DebugFlags.Active);
            check("NetJip == DebugFlags.NetJip", host.NetJip == DebugFlags.NetJip);
            check("NetLog == DebugFlags.NetLog", host.NetLog == DebugFlags.NetLog);
            check("NetDropGrant == DebugFlags.NetDropGrant", host.NetDropGrant == DebugFlags.NetDropGrant);
            check("NetLocal == DebugFlags.NetLocal", host.NetLocal == DebugFlags.NetLocal);
            sb.Append("  flags in force: active=").Append(host.DebugActive)
                .Append(" jip=").Append(host.NetJip)
                .Append(" log=").Append(host.NetLog)
                .Append(" dropgrant=").Append(host.NetDropGrant)
                .Append(" local=").Append(host.NetLocal).Append('\n');

            // The impairment triple, driven to three DISTINCT values through the panel's own
            // runtime setter. Any swap among the three fails; so does a member left pointing at
            // a constant. Restored to whatever was in force -- eaNetSim's panel may be live.
            float lag0 = DebugFlags.NetLagMs;
            float loss0 = DebugFlags.NetLossPct;
            float jitter0 = DebugFlags.NetJitterMs;
            try
            {
                DebugFlags.SetNetSimOverride(11f, 22f, 33f);
                check("NetLagMs follows ?netlag / the eaNetSim panel", host.NetLagMs == 11f);
                check("NetLossPct follows ?netloss (not lag)", host.NetLossPct == 22f);
                check("NetJitterMs follows the jitter knob (not lag or loss)", host.NetJitterMs == 33f);
            }
            finally
            {
                DebugFlags.SetNetSimOverride(lag0, loss0, jitter0);
            }
            check("the impairment knobs are restored",
                DebugFlags.NetLagMs == lag0 && DebugFlags.NetLossPct == loss0 && DebugFlags.NetJitterMs == jitter0);
        }

        // ---- 3. the injected clock, end to end ---------------------------------------------

        private static void SectionInjectedClock(Action<string, bool> check)
        {
            const long Lag = 200;
            INetHost entry = NetHost.Current;
            PinnedNetHost pinned = new PinnedNetHost { LagMs = Lag, LossPct = 0f, JitterMs = 0f };
            try
            {
                NetHost.Current = pinned;

                // The PRODUCTION composition: NetImpairment with no explicit overrides, wrapping a
                // real transport endpoint. Its arrival stamp comes from OnInnerData, which is the
                // read step 2a moved -- so this is the seam under test, not a mock of it.
                NetWire wire = new NetWire(2);
                NetImpairment imp = new NetImpairment(wire[1]);
                List<byte[]> got = new List<byte[]>();
                imp.OnData += (payload, reliable, from) => got.Add(payload);
                wire[0].Open("clock");
                wire[1].Open("clock");

                wire[0].SendStream(new byte[] { 42 });
                wire.Pump();   // the endpoint delivers -> OnInnerData stamps arrival from the host
                check("the packet is held, not forwarded inline", got.Count == 0 && imp.HeldCount == 1);

                imp.Pump(pinned.Now + Lag - 1);
                check("still held one ms before its release deadline", got.Count == 0 && imp.HeldCount == 1);

                // THE DISCRIMINATOR. Arrival was stamped at the virtual 0, so the packet is due at
                // exactly 200 on OUR clock. Read from Environment.TickCount64 instead and it would
                // be due at uptime+200 -- unreachable from here -- so this assertion is what fails
                // if the injected clock is not actually the one in use.
                imp.Pump(pinned.Now + Lag);
                check("released on the HOST's clock at exactly now+lag", got.Count == 1 && imp.HeldCount == 0);
                check("and it is the payload we sent", got.Count == 1 && got[0].Length == 1 && got[0][0] == 42);

                // Advancing the virtual clock is the only thing that moves time here: a second
                // packet parked at the new Now must NOT come due at the old deadline.
                got.Clear();
                pinned.Advance(10000);
                wire[0].SendStream(new byte[] { 43 });
                wire.Pump();
                imp.Pump(10000 + Lag - 1);
                check("a packet arriving after Advance() takes its deadline from the NEW now",
                    got.Count == 0 && imp.HeldCount == 1);
                imp.Pump(10000 + Lag);
                check("and comes due on that deadline", got.Count == 1 && got[0][0] == 43);
            }
            finally
            {
                NetHost.Current = entry;
            }
        }

        // ---- 4. knob precedence ------------------------------------------------------------

        private static void SectionImpairmentKnobs(Action<string, bool> check)
        {
            INetHost entry = NetHost.Current;
            PinnedNetHost pinned = new PinnedNetHost { LagMs = 111f, LossPct = 22f, JitterMs = 33f };
            try
            {
                NetHost.Current = pinned;

                NetImpairment live = new NetImpairment(null);
                check("an un-overridden NetImpairment reads lag from the host", live.LagMs == 111f);
                check("... and loss", live.LossPct == 22f);
                check("... and jitter", live.JitterMs == 33f);

                // Precedence is unchanged by step 2a and has to stay that way: SelfTest and
                // NetWireTest both construct explicitly-configured impairments and would silently
                // start measuring the live knobs instead if the host won.
                NetImpairment forced = new NetImpairment(null, 7f, 0f, 0f);
                check("an explicit override still beats the host", forced.LagMs == 7f);
                check("... on every knob it sets", forced.LossPct == 0f && forced.JitterMs == 0f);

                // A partial construction is the mixed case the nullable overrides exist for.
                NetImpairment mixed = new NetImpairment(null, null, 5f, null);
                check("an unset knob still falls through to the host",
                    mixed.LagMs == 111f && mixed.JitterMs == 33f);
                check("while the set one wins", mixed.LossPct == 5f);

                // The clamp is the wrapper's, not the flag's: a host reporting past the ceiling
                // must still be clamped, or a scenario could ask for a lag NetImpairment's own
                // release maths was never written for.
                pinned.LagMs = NetImpairment.MaxLagMs + 500f;
                check("a host value past MaxLagMs is still clamped by the wrapper",
                    new NetImpairment(null).LagMs == NetImpairment.MaxLagMs);
            }
            finally
            {
                NetHost.Current = entry;
            }
        }
    }
}
