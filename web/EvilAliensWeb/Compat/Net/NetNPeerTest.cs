using System;
using System.Collections.Generic;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // THE N-PEER SESSION SUITE (card 87242257, Stage 11.9). Run `eaNetNPeer()` from the MAIN
    // MENU, or `eval NetNPeer` under eahl. Committed as a leg of
    // tools/headless/probes/net_selftests.txt.
    //
    // WHAT IT IS. One REAL session at a time on an in-process wire -- first a HOST hub with TWO
    // scripted joiners (plus a straggler for the per-peer reject legs), then a CLIENT with a
    // scripted host -- covering exactly what going past
    // two peers added: per-peer hello/welcome with grants serialized against races, per-peer
    // rejects that leave a live match standing, the host
    // relay of client ship/HUD state, symmetric events re-emitted under per-recipient seqs,
    // pause as a set, the per-peer liveness verdict, and the new match-end policy (a client
    // leaving frees its seats and play continues; only the last one leaving ends anything).
    // The eaNetScenarios shape: menu-runnable, leave-no-trace, every frame built with the REAL
    // codec, every assertion a DELTA or an exact frame read off a scripted endpoint.
    //
    // WHY THE FRAMES ARE READ AT THE ENDPOINTS. Most of what this card changes is WHO receives
    // WHAT: an addressed welcome, a relay that must reach the other client and never echo to
    // its source, per-recipient event seqs that must stay contiguous per channel. None of that
    // is visible in this process's world at all -- the collectors on the scripted endpoints are
    // the only observables there are.
    //
    // WHAT IT DELIBERATELY DOES NOT COVER: the mid-level shapes (revert-to-single-player when
    // the last client leaves a running level, puppet spawns for relayed ships -- both need a
    // scene and live ships) belong to tools/sim/net_npeer_smoke.py, the three-process eahl rig;
    // and nothing here touches WebRTC or the signaling server (rooms stay capacity 2 until card
    // 0257f8ba anyway).
    internal static class NetNPeerTest
    {
        private const string Room = "npeer";
        private const string Room2 = "npeer2";
        private const string Room3 = "npeer3";

        private const ulong TokenA = 0xA11CE0001UL;
        private const ulong TokenB = 0xB0B0B0002UL;

        private sealed class Collector
        {
            public readonly List<(byte[] Data, bool Reliable)> Frames = new List<(byte[], bool)>();

            public void Attach(InMemoryTransport t)
            {
                t.OnData += (data, reliable, from) => Frames.Add((data, reliable));
            }

            public int Count(Func<byte[], bool, bool> match)
            {
                int n = 0;
                foreach ((byte[] d, bool r) in Frames)
                {
                    if (match(d, r))
                    {
                        n++;
                    }
                }
                return n;
            }

            public List<byte[]> Events(byte evType)
            {
                List<byte[]> list = new List<byte[]>();
                foreach ((byte[] d, bool r) in Frames)
                {
                    if (r && d.Length >= 4 && d[0] == NetProtocol.MsgEvent && d[1] == evType)
                    {
                        list.Add(d);
                    }
                }
                return list;
            }
        }

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

            sb.Append("[netnpeer] N-peer session suite (card 87242257)\n");

            if (NetSession.Active || GameScene.NetActiveScene != null || NetPuppets.LiveCount > 0)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            int playersBefore = oracle.Players;

            PinnedNetHost clock = new PinnedNetHost();
            // The host legs run a MENU session (their subject is menu-session match-end
            // semantics), and a menu session refuses its own pairing while DebugFlags.Active is
            // set -- which it is under net_selftests' ?menu boot. The refusal is not this
            // suite's subject, so it is waived the way ?netallowdebug would.
            clock.AllowDebug = true;
            INetHost hostBefore = NetHost.Current;
            NetHost.Current = clock;
            try
            {
                RunHostLegs(sb, Check, game, oracle, clock);
                RunClientLegs(sb, Check, game, oracle, clock);
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 9. teardown -- what this suite must hand back\n");
                if (NetSession.Active)
                {
                    NetSession.Stop("netnpeer teardown");
                }
                NetSession.TakeMenuNotice(); // never leave a phantom notice for the menus
                NetHost.Current = hostBefore;
                Check("no session is left running", !NetSession.Active);
                // <= rather than ==, deliberately: an earlier suite in the same process can
                // leave a leftover Remote grant seated at the menu (NetTeleportTest's host
                // section does -- ReserveRemotePrimarySlot reserves and nothing at the menu
                // ever cleans a stopped session's roster; production tolerates this because
                // every launch path ResetPlayers() first, and the allocator deliberately
                // REUSES such seats). This suite then adopts that seat for joiner A and the
                // departure path frees it -- cleaning up the inheritance is fine; ADDING a
                // seat is the leak this check exists for.
                Check("no seat this suite created remains (players " + oracle.Players
                    + " <= " + playersBefore + " before)", oracle.Players <= playersBefore);
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // ---- the HOST hub with two scripted joiners -----------------------------------------
        private static void RunHostLegs(StringBuilder sb, Action<string, bool> Check,
            Game game, Oracle oracle, PinnedNetHost clock)
        {
            NetWire wire = new NetWire(4);
            InMemoryTransport ours = wire[0];
            InMemoryTransport joinerA = wire[1];
            InMemoryTransport joinerB = wire[2];
            InMemoryTransport straggler = wire[3];
            // The scripted joiners ADDRESS the host (SendStreamTo/SendReliableTo ours.Id),
            // exactly as production clients do since this card -- the in-process wire is a
            // bus, and broadcasting would land each joiner's raw frames in the other's
            // collector, polluting every who-received-what assertion below.
            Collector atA = new Collector();
            Collector atB = new Collector();
            atA.Attach(joinerA);
            atB.Attach(joinerB);
            ushort seqA = 0;
            ushort seqB = 0;
            uint msA = 1;

            sb.Append(" 1. per-peer hello/welcome -- two joiners in ONE drain, grants serialized\n");
            // A MENU session deliberately: the match-end legs below are about menu-session
            // semantics. The debug-flag refusal is waived via the injected host (Run() sets
            // AllowDebug); the scripted joiners' flag bytes are 0 (clean) by construction.
            NetSession.StartForTest(game, host: true, ours, Room, asMenuSession: true);
            joinerA.Open(Room);
            joinerB.Open(Room);
            Check("session started as the HOST", NetSession.IsHost);

            // Both hellos land in the SAME rx drain -- the serialization claim is that the
            // first reservation is already in the oracle when the second allocates.
            joinerA.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, TokenA, 0));
            joinerB.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, TokenB, 0));
            wire.Pump();
            NetSession.Update();
            wire.Pump(); // deliver our welcomes to the collectors

            Check("both joiners are up (upPeers=" + NetSession.UpPeerCountNow + ")",
                NetSession.UpPeerCountNow == 2);
            byte slotA = WelcomeSlot(atA);
            byte slotB = WelcomeSlot(atB);
            Check("joiner A's welcome grants a real seat (slot=" + slotA + ")",
                slotA != NetProtocol.SlotNone && slotA > 0 && slotA < Oracle.MaxPlayers);
            Check("joiner B's welcome grants a DIFFERENT real seat (slot=" + slotB + ")",
                slotB != NetProtocol.SlotNone && slotB > 0 && slotB < Oracle.MaxPlayers && slotB != slotA);
            Check("both seats are reserved as Remote on the host roster",
                oracle.IsSeated(slotA) && oracle.Controller(slotA) == ControlDevice.Remote
                && oracle.IsSeated(slotB) && oracle.Controller(slotB) == ControlDevice.Remote);
            // The welcome is ADDRESSED: each collector must hold exactly the welcomes meant for
            // it. A broadcast welcome would put A's granted seat in B's hands -- the exact
            // wrongness the addressed handshake removes.
            Check("joiner A saw only its own welcome (welcomes=" + Welcomes(atA) + ")", Welcomes(atA) == 1);
            Check("joiner B saw only its own welcome (welcomes=" + Welcomes(atB) + ")", Welcomes(atB) == 1);

            sb.Append(" 1a. the lobby roster beat (EvLobbyRoster, card 0257f8ba)\n");
            // Both waiting screens must know who is in. The final mask everyone holds is the
            // host's seat + both grants; A's addressed copy at ITS PeerConnected predates B's
            // seat, so it is the LAST mask that is asserted -- the edge-triggered broadcast is
            // what brings A up to date, and a beat that never fired leaves the list empty.
            byte fullMask = (byte)(NetProtocol.SlotBit(0) | NetProtocol.SlotBit(slotA) | NetProtocol.SlotBit(slotB));
            List<byte[]> rosterAtA = atA.Events(NetProtocol.EvLobbyRoster);
            List<byte[]> rosterAtB = atB.Events(NetProtocol.EvLobbyRoster);
            Check("joiner A was told the full lobby roster (beats=" + rosterAtA.Count + " last mask="
                + (rosterAtA.Count > 0 ? rosterAtA[rosterAtA.Count - 1][4].ToString() : "-") + ")",
                rosterAtA.Count > 0 && rosterAtA[rosterAtA.Count - 1][4] == fullMask);
            Check("joiner B was told it too (beats=" + rosterAtB.Count + ")",
                rosterAtB.Count > 0 && rosterAtB[rosterAtB.Count - 1][4] == fullMask);

            sb.Append(" 1b. per-peer rejects -- a straggler must not end the live match\n");
            // A stale-build machine knocking on a live 3-player game: refused PER-PEER on the
            // way in, told why (the addressed MsgReject), and its own SYMMETRIC reject -- which
            // pre-review tore the whole session down through Stop() -- is old news.
            Collector atS = new Collector();
            atS.Attach(straggler);
            straggler.Open(Room);
            straggler.SendReliable(NetProtocol.EncodeHello((byte)(NetSession.ProtocolVersion + 1), false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, 0xBAD, 0));
            wire.Pump();
            NetSession.Update();
            wire.Pump();
            Check("a bad-version straggler was refused with the match intact (upPeers="
                + NetSession.UpPeerCountNow + ")", NetSession.Active && NetSession.UpPeerCountNow == 2);
            Check("...and was TOLD why (addressed MsgReject reached it)",
                atS.Count((d, r) => r && d.Length >= 2 && d[0] == NetProtocol.MsgReject) >= 1);
            straggler.SendReliableTo(ours.Id, NetProtocol.EncodeReject(NetProtocol.RejectVersion));
            wire.Pump();
            NetSession.Update();
            Check("...and its own symmetric reject ends nothing either",
                NetSession.Active && NetSession.UpPeerCountNow == 2);

            sb.Append(" 2. host relay -- A's ship reaches B as a slot-keyed extra, never echoes to A\n");
            int shipsAtABefore = ShipFrames(atA, slotA);
            int shipsAtBBefore = ShipFrames(atB, slotA);
            // A streams its primary: three frames, then the relay cadence fires on our side.
            for (int i = 0; i < 3; i++)
            {
                joinerA.SendStreamTo(ours.Id, NetProtocol.EncodeShipState(slotA, primary: true, seqA++, msA += 33,
                    new Vector2(211f, 137f), Vector2.Zero, 4.7f, alive: true, shotCount: 5,
                    shotsPerSec: 8, bulletLife: 450f));
                wire.Pump();
                clock.Advance(NetSession.StreamIntervalMs + 1);
                NetSession.Update();
            }
            wire.Pump();
            int relayedToB = ShipFrames(atB, slotA) - shipsAtBBefore;
            Check("B received A's ship as slot-" + slotA + " frames (n=" + relayedToB + ")", relayedToB >= 1);
            Check("...NON-primary (an extras channel over there, v23's one ship path)",
                RelayedFramesAreNonPrimary(atB, slotA));
            Check("...every one carrying ShipFlagRelayed (card 6fb406bc -- the 150ms cushion's cue)",
                RelayedFramesCarryRelayedBit(atB, slotA));
            Check("...and A got NONE of its own state back (echo guard)",
                ShipFrames(atA, slotA) == shipsAtABefore);
            Check("...at A's streamed position (211,137)", RelayedPosMatches(atB, slotA, 211f, 137f));

            sb.Append(" 3. HUD relay -- A's packet reaches B verbatim, not A\n");
            int hudAtABefore = HudFrames(atA);
            int hudAtBBefore = HudFrames(atB);
            joinerA.SendStreamTo(ours.Id, EncodeHudFor(slotA));
            wire.Pump();
            NetSession.Update();
            wire.Pump();
            Check("B received the HUD packet (+" + (HudFrames(atB) - hudAtBBefore) + ")",
                HudFrames(atB) == hudAtBBefore + 1);
            Check("A did not hear its own HUD back", HudFrames(atA) == hudAtABefore);

            sb.Append(" 4. pause as a set -- per-recipient aggregates, A-on/B-on/A-off holds the freeze\n");
            joinerA.SendReliableTo(ours.Id, NetProtocol.EncodeByteEvent(NextSeq(ref seqA), NetProtocol.EvPause, 1));
            wire.Pump();
            NetSession.Update();
            wire.Pump();
            Check("A's pause is held (RemotePaused aggregate up)", NetSession.RemotePaused);
            Check("B was told someone paused (EvPause on)", LastPauseValue(atB) == 1);
            Check("A was NOT told about its own pause", atA.Events(NetProtocol.EvPause).Count == 0);
            joinerB.SendReliableTo(ours.Id, NetProtocol.EncodeByteEvent(NextSeq(ref seqB), NetProtocol.EvPause, 1));
            wire.Pump();
            NetSession.Update();
            wire.Pump();
            Check("now A hears that someone (B) paused", LastPauseValue(atA) == 1);
            int pausesAtBAfterBoth = atB.Events(NetProtocol.EvPause).Count;
            joinerA.SendReliableTo(ours.Id, NetProtocol.EncodeByteEvent(NextSeq(ref seqA), NetProtocol.EvPause, 0));
            wire.Pump();
            NetSession.Update();
            wire.Pump();
            Check("A unpausing under B's held pause keeps the aggregate up", NetSession.RemotePaused);
            Check("...and B is told the last other pause ended (EvPause off)",
                LastPauseValue(atB) == 0 && atB.Events(NetProtocol.EvPause).Count == pausesAtBAfterBoth + 1);
            Check("...while A hears nothing new (B still holds its own pause)", LastPauseValue(atA) == 1);
            joinerB.SendReliableTo(ours.Id, NetProtocol.EncodeByteEvent(NextSeq(ref seqB), NetProtocol.EvPause, 0));
            wire.Pump();
            NetSession.Update();
            wire.Pump();
            Check("B unpausing clears the set everywhere", !NetSession.RemotePaused && LastPauseValue(atA) == 0);

            sb.Append(" 5. per-peer liveness -- B goes silent, its seat frees, A plays on\n");
            long resetsBefore = NetSession.Metrics.Resets;
            for (int step = 0; step < 20 && NetSession.UpPeerCountNow == 2; step++)
            {
                clock.Advance(500);
                joinerA.SendStreamTo(ours.Id, NetProtocol.EncodeShipState(slotA, primary: true, seqA++, msA += 500,
                    new Vector2(211f, 137f), Vector2.Zero, 4.7f, alive: true, shotCount: 5,
                    shotsPerSec: 8, bulletLife: 450f));
                wire.Pump();
                NetSession.Update();
            }
            wire.Pump();
            Check("B timed out while A stayed up (upPeers=" + NetSession.UpPeerCountNow + ")",
                NetSession.UpPeerCountNow == 1 && NetSession.PeerUp);
            Check("the SESSION SURVIVED the departure (the match-end policy's whole point)",
                NetSession.Active);
            Check("B's seat was freed on the host", !oracle.IsSeated(slotB));
            Check("A's seat was NOT", oracle.IsSeated(slotA) && oracle.Controller(slotA) == ControlDevice.Remote);
            List<byte[]> peerLeftAtA = atA.Events(NetProtocol.EvPeerLeft);
            Check("A was told B's seats are gone (EvPeerLeft mask=" +
                (peerLeftAtA.Count > 0 ? peerLeftAtA[peerLeftAtA.Count - 1][4].ToString() : "-") + ")",
                peerLeftAtA.Count == 1 && peerLeftAtA[0][4] == NetProtocol.SlotBit(slotB));
            Check("B was told nothing (it is the one that left)", atB.Events(NetProtocol.EvPeerLeft).Count == 0);

            sb.Append(" 6. per-recipient event seqs -- contiguous at EVERY endpoint\n");
            // The strong claim behind SendEventToPeer: for all the addressed traffic above
            // (welcomes aside -- they carry no seq), each endpoint's event stream counts
            // 0,1,2,... with no gaps, which one global counter could not have produced.
            Check("A's event seqs are contiguous from 0 (" + EventCount(atA) + " events)", SeqsContiguous(atA));
            Check("B's event seqs are contiguous from 0 (" + EventCount(atB) + " events)", SeqsContiguous(atB));
            Check("...and both endpoints actually received events", EventCount(atA) >= 3 && EventCount(atB) >= 3);

            sb.Append(" 7. the last client leaves at the menus -- the LOBBY SURVIVES (card 0257f8ba)\n");
            joinerA.SendReliableTo(ours.Id, NetProtocol.EncodeEmptyEvent(NextSeq(ref seqA), NetProtocol.EvLeave));
            wire.Pump();
            NetSession.Update();
            // The pre-0257f8ba policy -- Stop + "match ended" notice -- was the dead end 11.10
            // removes: a menu-lobby HOST keeps its session (and, in production, its still-open
            // signaling room) and waits for new players. The Stop survives only for the
            // non-lobby shapes; this session is asMenuSession, so it must idle peerless.
            Check("the session SURVIVED with zero peers (the lobby waits for new players)",
                NetSession.Active && !NetSession.PeerUp);
            string notice = NetSession.TakeMenuNotice();
            Check("no leave notice reaches the menus (nothing ended)", string.IsNullOrEmpty(notice));
            Check("A's seat was freed with it", !oracle.IsSeated(slotA));
            Check("no reset leaked from the whole exchange", NetSession.Metrics.Resets == resetsBefore);
            // Wound down DELIBERATELY -- the lobby no longer ends itself, so the suite must, or
            // the client legs' StartForTest would early-return against this session.
            NetSession.Stop("netnpeer host legs done");
            Check("teardown: the host-legs session is stopped", !NetSession.Active);
        }

        // ---- the CLIENT side ----------------------------------------------------------------
        private static void RunClientLegs(StringBuilder sb, Action<string, bool> Check,
            Game game, Oracle oracle, PinnedNetHost clock)
        {
            NetWire wire = new NetWire(3);
            InMemoryTransport ours = wire[0];
            InMemoryTransport scriptedHost = wire[1];
            InMemoryTransport stranger = wire[2];
            ushort hostSeq = 0;
            uint hostMs = 1;
            const byte OurSlot = 1;
            const byte OtherClientSlot = 2;

            sb.Append(" 8. client legs -- bus hygiene, own-slot refusal, EvPeerLeft apply\n");
            NetSession.StartForTest(game, host: false, ours, Room2);
            scriptedHost.Open(Room2);
            stranger.Open(Room2);

            // A fellow CLIENT's hello on a bus medium must not create our host channel.
            stranger.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, 0xDEAD, 0));
            wire.Pump();
            NetSession.Update();
            Check("a join-role hello did not become our host (channels=" + NetSession.PeerChannelCount + ")",
                NetSession.PeerChannelCount == 0);

            // The real host welcomes us with our granted seat.
            scriptedHost.SendReliable(NetProtocol.EncodeWelcome(NetSession.ProtocolVersion, true,
                NetSession.LocalBuildHash, 0, OurSlot, 0xCAFE, 0));
            wire.Pump();
            NetSession.Update();
            Check("the host-role welcome paired us (peer=up, ourSlot=" + NetSession.LocalPrimarySlot + ")",
                NetSession.PeerUp && NetSession.LocalPrimarySlot == OurSlot);

            // A stranger's frames must stay dropped once paired: its EvPause must not freeze us.
            stranger.SendReliable(NetProtocol.EncodeByteEvent(0, NetProtocol.EvPause, 1));
            wire.Pump();
            NetSession.Update();
            Check("a non-host sender's pause is dropped, not applied", !NetSession.RemotePaused);

            // The lobby roster beat, receive side (card 0257f8ba): stored for the waiting panel,
            // with "no beat yet" reading -1 as the control -- a mask invented before the first
            // beat would put a fictitious roster on the join screen.
            Check("no roster beat yet reads -1", NetSession.LobbyRosterMask == -1);
            scriptedHost.SendReliable(NetProtocol.EncodeByteEvent(hostSeq++, NetProtocol.EvLobbyRoster, 0b0111));
            wire.Pump();
            NetSession.Update();
            Check("the host's EvLobbyRoster is stored for the panel (mask=" + NetSession.LobbyRosterMask + ")",
                NetSession.LobbyRosterMask == 0b0111);

            // A relayed extras frame for OUR OWN slot is refused; one for another client's slot
            // builds a channel (its puppet needs a level, which the smoke rig owns).
            scriptedHost.SendStream(NetProtocol.EncodeShipState(OurSlot, primary: false, hostSeq++, hostMs += 33,
                new Vector2(100f, 100f), Vector2.Zero, 4.7f, alive: true, shotCount: 0,
                shotsPerSec: 8, bulletLife: 450f));
            scriptedHost.SendStream(NetProtocol.EncodeShipState(OtherClientSlot, primary: false, hostSeq++, hostMs += 33,
                new Vector2(300f, 300f), Vector2.Zero, 4.7f, alive: true, shotCount: 0,
                shotsPerSec: 8, bulletLife: 450f));
            wire.Pump();
            NetSession.Update();
            Check("an extras frame naming OUR OWN seat built no channel",
                !NetSession.FriendChannelExists(OurSlot));
            Check("one naming another client's seat did (slot " + OtherClientSlot + ")",
                NetSession.FriendChannelExists(OtherClientSlot));

            // Card 6fb406bc: the interpolation cushion follows the frame's relayed bit. The
            // slot-2 channel above was built from UNFLAGGED frames (a host couch ship, one hop)
            // and is the negative control; a channel built from ShipFlagRelayed frames (another
            // client's ship via the hub) latches the wider budget. The latch changes no pixel
            // and no counter, so this readback is its only observable.
            Check("a DIRECT extras channel renders on InterpDelayMs (" + NetSession.InterpDelayMs + "ms)",
                NetSession.FriendInterpDelayMs(OtherClientSlot) == NetSession.InterpDelayMs);
            const byte RelayedSlot = 3;
            scriptedHost.SendStream(NetProtocol.EncodeShipState(RelayedSlot, primary: false, hostSeq++, hostMs += 33,
                new Vector2(500f, 200f), Vector2.Zero, 4.7f, alive: true, shotCount: 0,
                shotsPerSec: 8, bulletLife: 450f, scriptGate: false, asplodeBits: 0, bounceBits: 0,
                relayed: true));
            wire.Pump();
            NetSession.Update();
            Check("a RELAYED extras channel latches RelayedInterpDelayMs ("
                + NetSession.RelayedInterpDelayMs + "ms)",
                NetSession.FriendChannelExists(RelayedSlot)
                && NetSession.FriendInterpDelayMs(RelayedSlot) == NetSession.RelayedInterpDelayMs);

            // EvPeerLeft: that client left -- its channel and its (planted) seat must free.
            bool seated = oracle.AddPlayerAt(OtherClientSlot, ControlDevice.RemoteFriend);
            Check("PRECONDITION the departed client's seat is planted", seated);
            scriptedHost.SendReliable(NetProtocol.EncodeByteEvent(hostSeq++, NetProtocol.EvPeerLeft,
                NetProtocol.SlotBit(OtherClientSlot)));
            wire.Pump();
            NetSession.Update();
            Check("EvPeerLeft freed the seat", !oracle.IsSeated(OtherClientSlot));
            Check("...and dropped the extras channel", !NetSession.FriendChannelExists(OtherClientSlot));
            Check("...without touching our own primary seat bookkeeping",
                NetSession.LocalPrimarySlot == OurSlot && NetSession.PeerUp);

            NetSession.Stop("netnpeer client legs done");
            Check("client session stopped cleanly", !NetSession.Active);

            sb.Append(" 8b. a reject reaches a client with NO channel yet -- 'Game full', not a hang\n");
            // The over-capacity door turns a newcomer away with an addressed RejectFull before
            // any handshake, so the newcomer has no channel -- and a reject that needed one
            // would be silently dropped, leaving the player hanging on "Connecting" instead of
            // being told. DrainRx therefore routes MsgReject before the channel resolve.
            NetWire wire2 = new NetWire(2);
            NetSession.StartForTest(game, host: false, wire2[0], Room3);
            wire2[1].Open(Room3);
            wire2[1].SendReliableTo(wire2[0].Id, NetProtocol.EncodeReject(NetProtocol.RejectFull));
            wire2.Pump();
            NetSession.Update();
            Check("the pre-channel reject ended the pairing attempt (no silent hang)", !NetSession.Active);
            string fullNotice = NetSession.TakeMenuNotice();
            Check("...and the menus get the reason", fullNotice != null && fullNotice.Contains("full"));
        }

        // ---- frame-reading helpers ------------------------------------------------------------

        private static byte WelcomeSlot(Collector c)
        {
            byte slot = NetProtocol.SlotNone;
            foreach ((byte[] d, bool r) in c.Frames)
            {
                if (r && d.Length >= 3 && d[0] == NetProtocol.MsgWelcome
                    && NetProtocol.TryDecodeHandshake(d, out _, out _, out _, out _, out byte granted, out _, out _))
                {
                    slot = granted;
                }
            }
            return slot;
        }

        private static int Welcomes(Collector c)
        {
            return c.Count((d, r) => r && d.Length >= 1 && d[0] == NetProtocol.MsgWelcome);
        }

        private static int ShipFrames(Collector c, byte slot)
        {
            return c.Count((d, r) => !r && d.Length >= NetProtocol.ShipStateBytes
                && d[0] == NetProtocol.MsgShipState && d[1] == slot);
        }

        private static bool RelayedFramesAreNonPrimary(Collector c, byte slot)
        {
            bool any = false;
            foreach ((byte[] d, bool r) in c.Frames)
            {
                if (!r && d.Length >= NetProtocol.ShipStateBytes && d[0] == NetProtocol.MsgShipState && d[1] == slot)
                {
                    any = true;
                    if (!NetProtocol.TryDecodeShipState(d, out _, out bool primary, out _, out _, out _, out _) || primary)
                    {
                        return false;
                    }
                }
            }
            return any;
        }

        private static bool RelayedFramesCarryRelayedBit(Collector c, byte slot)
        {
            bool any = false;
            foreach ((byte[] d, bool r) in c.Frames)
            {
                if (!r && d.Length >= NetProtocol.ShipStateBytes && d[0] == NetProtocol.MsgShipState && d[1] == slot)
                {
                    any = true;
                    if (!NetProtocol.TryDecodeShipState(d, out _, out _, out _, out ShipSample s, out _, out _)
                        || !s.Relayed)
                    {
                        return false;
                    }
                }
            }
            return any;
        }

        private static bool RelayedPosMatches(Collector c, byte slot, float x, float y)
        {
            foreach ((byte[] d, bool r) in c.Frames)
            {
                if (!r && d.Length >= NetProtocol.ShipStateBytes && d[0] == NetProtocol.MsgShipState && d[1] == slot
                    && NetProtocol.TryDecodeShipState(d, out _, out _, out _, out ShipSample s, out _, out _)
                    && Math.Abs(s.Pos.X - x) < 0.01f && Math.Abs(s.Pos.Y - y) < 0.01f)
                {
                    return true;
                }
            }
            return false;
        }

        private static int HudFrames(Collector c)
        {
            return c.Count((d, r) => !r && d.Length >= 2 && d[0] == NetProtocol.MsgHudState);
        }

        private static int LastPauseValue(Collector c)
        {
            List<byte[]> frames = c.Events(NetProtocol.EvPause);
            return frames.Count == 0 ? -1 : frames[frames.Count - 1][4];
        }

        private static int EventCount(Collector c)
        {
            return c.Count((d, r) => r && d.Length >= 4 && d[0] == NetProtocol.MsgEvent);
        }

        private static bool SeqsContiguous(Collector c)
        {
            int expected = 0;
            foreach ((byte[] d, bool r) in c.Frames)
            {
                if (!r || d.Length < 4 || d[0] != NetProtocol.MsgEvent)
                {
                    continue;
                }
                if (NetProtocol.ReadU16(d, 2) != (ushort)expected)
                {
                    return false;
                }
                expected++;
            }
            return expected > 0;
        }

        private static ushort NextSeq(ref ushort seq)
        {
            return seq++;
        }

        // A minimal real MsgHudState for one owned slot -- through the production encoder, so
        // the relay leg carries the bytes a real client would send.
        private static byte[] EncodeHudFor(byte slot)
        {
            byte[] slots = new byte[NetProtocol.MaxSlots];
            int[] combos = new int[NetProtocol.MaxSlots];
            float[] comboLeft = new float[NetProtocol.MaxSlots];
            byte[] types = new byte[NetProtocol.MaxSlots];
            float[] progress = new float[NetProtocol.MaxSlots];
            int[][] levels = new int[NetProtocol.MaxSlots][];
            int[][] options = new int[NetProtocol.MaxSlots][];
            float[] scores = new float[NetProtocol.MaxSlots];
            for (int i = 0; i < NetProtocol.MaxSlots; i++)
            {
                levels[i] = new int[NetProtocol.HudLevelCount];
                options[i] = new int[NetProtocol.HudOptionLayers];
            }
            slots[0] = slot;
            combos[0] = 7;
            types[0] = NetProtocol.HudPowerupNone;
            scores[0] = 1200f;
            return NetProtocol.EncodeHudState(slots, combos, comboLeft, types, progress, levels, options, scores, 1);
        }

        private static string Tally(int pass, int fail)
        {
            return "[netnpeer] " + (fail == 0 ? "ALL PASS" : "FAILURES") + " -- "
                + pass + " passed, " + fail + " failed\n";
        }
    }
}
