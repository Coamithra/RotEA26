using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // The LIVE-SESSION half of card 0d6ffe70's verification (eaHostMenu.live() / `eval
    // HostMenuLive`). MENU-runnable and leave-no-trace -- the eaNetScenarios shape.
    //
    // WHAT IT COVERS THAT NetHostMenuTest CANNOT, which is the whole reason it exists.
    // That suite sweeps Entries() over 32 SYNTHETIC states and is deliberately Game-free, so it
    // can say nothing about `CurrentState()` -- five static reads that could be wired to the
    // wrong statics (or to a stale copy) and still leave every one of its 47 assertions green.
    // The consequence of that failing is the card's whole feature silently not appearing: a
    // host with a griefer in their game pauses and finds no Online Play row, which looks exactly
    // like a host who never had one. So this pairs a REAL host session with a scripted peer over
    // an in-process NetWire and reads the decision back through the live statics.
    //
    // It also drives the KICK, by making the exact call the menu handler makes
    // (NetSession.KickPeer(false)) and asserting the frame really reached the peer. The kick
    // RULES -- the block predicate, the v6 identity codec -- belong to card 0b8a300b and are
    // covered by eaKickTest; what is new here is that the pause menu can reach them at all, and
    // that the row RETRACTS afterwards rather than offering a kick with nobody left to kick.
    //
    // It runs on a PinnedNetHost for NetResetSpawnTest's reason: the kick's teardown is deferred
    // by RejectGraceMs, so the suite has to move the clock past it deliberately rather than
    // race a real one -- and a virtual clock also means no peer-stall or drop verdict can fire
    // mid-run and turn an assertion into a coin flip.
    internal static class NetHostMenuLiveTest
    {
        private const string Room = "hostmenu";

        // Distinct from NetScenarioTest's, so a token left behind by one suite could never make
        // the other's pairing look settled.
        private const ulong PeerToken = 0x0D6FFE70UL;

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

            sb.Append("[hostmenulive] the Online Play decision over a REAL session (card 0d6ffe70)\n");

            // The eaNetScenarios gate: this starts and stops a real session and releases seats,
            // so a live session, level or attract demo is a reason to report a SKIP rather than
            // let an unrun suite read as a pass.
            if (NetSession.Active || GameScene.NetActiveScene != null || NetPuppets.LiveCount > 0)
            {
                sb.Append("  SKIP (run from the main menu, with no session, level or attract demo up)\n");
                sb.Append(Tally(pass, fail));
                return sb.ToString();
            }

            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
            Game game = bin.Game;
            INetHost hostBefore = NetHost.Current;
            PinnedNetHost clock = new PinnedNetHost();
            NetHost.Current = clock;

            try
            {
                // ---- 1. the OFFLINE reading, as the control -------------------------------
                // Without it the session legs below could not distinguish "CurrentState reads
                // the live session" from "CurrentState is hard-wired to the kick shape".
                sb.Append(" 1. offline -- the control\n");
                NetHostMenu.State off = NetHostMenu.CurrentState();
                Check("no session, so the state reads session=False host=False peer=False"
                    + " (" + Describe(off) + ")",
                    !off.SessionActive && !off.IsHost && !off.PeerUp);
                Check("... and the menu offers nothing at the menu (rows=" + Render(NetHostMenu.Entries(off)) + ")",
                    NetHostMenu.Entries(off).Count == 0 && !NetHostMenu.Available(off));

                // ---- 2. rig: a real HOST session with a scripted client --------------------
                sb.Append(" 2. rig -- a real HOST session with a scripted client on the wire\n");
                NetWire wire = new NetWire(2);
                InMemoryTransport ours = wire[0];
                InMemoryTransport peer = wire[1];
                List<byte[]> peerReliable = new List<byte[]>();
                peer.OnData += (data, reliable, room) =>
                {
                    if (reliable)
                    {
                        peerReliable.Add(data);
                    }
                };

                NetSession.StartForTest(game, host: true, ours, Room);
                peer.Open(Room);
                Check("session started as the HOST", NetSession.IsHost);

                // LocalBuildHash is READ, never recomputed -- re-deriving it here would drift
                // from StartWith's own ?netfakehash-aware expression and the only symptom would
                // be a pairing this suite cannot explain.
                peer.SendReliable(NetProtocol.EncodeHello(NetSession.ProtocolVersion, false,
                    NetSession.LocalBuildHash, 0, NetProtocol.SlotNone, PeerToken, 0));
                wire.Pump();
                NetSession.Update();
                bool paired = NetSession.PeerUp;
                Check("the scripted client paired (peer=" + (paired ? "up" : "down") + ")", paired);
                // Nothing below can mean anything without a settled pairing, and a refused hello
                // prints its own [net] line above. Guarded rather than an early `return`: the
                // teardown section runs in a finally and appends to `sb` AFTER a return has
                // already rendered it, so an early exit would silently drop it.
                if (paired)
                {
                    // ---- 3. the live state drives the kick shape ------------------------------
                    sb.Append(" 3. the host with a peer is offered the kick rows\n");
                    NetHostMenu.State live = NetHostMenu.CurrentState();
                    Check("CurrentState reads the LIVE session (" + Describe(live) + ")",
                        live.SessionActive && live.IsHost && live.PeerUp);
                    List<NetHostMenu.Entry> rows = NetHostMenu.Entries(live);
                    Check("the pause menu would carry the Online Play row", NetHostMenu.Available(live));
                    Check("rows are Back,Kick,KickAndBlock (got " + Render(rows) + ")",
                        rows.Count == 3 && rows[0] == NetHostMenu.Entry.Back
                        && rows[1] == NetHostMenu.Entry.Kick && rows[2] == NetHostMenu.Entry.KickAndBlock);
                    // A listing cannot coexist with a session, so the room toggle must be absent --
                    // NetHostMenuTest asserts the same disjointness over synthetic states; this is
                    // the one place the two halves are read off the SAME live world.
                    Check("... and NOT the room toggle, because a session is never listed",
                        !rows.Contains(NetHostMenu.Entry.RoomToggle) && !NetListing.CouldList);
                    Check("the caption names the joined player, singular (2-peer protocol)",
                        NetHostMenu.Caption(live) == "Another player has joined your game");

                    // ---- 4. the kick the menu row makes ---------------------------------------
                    sb.Append(" 4. the kick row's action reaches the peer\n");
                    peerReliable.Clear();
                    // Verbatim the call GameScene.NetHostMenuKick makes. Going through KickPeer
                    // rather than a paraphrase is the point: a handler wired to the wrong overload
                    // (block instead of no-block) is a real mistake, and eaKickTest owns the rules
                    // on the other side of this call.
                    NetSession.KickPeer(block: false);
                    wire.Pump();
                    Check("the peer is gone (PeerUp=" + NetSession.PeerUp + ")", !NetSession.PeerUp);
                    Check("an EvKick frame really reached the peer (" + peerReliable.Count
                        + " reliable frame(s))", HasKickFrame(peerReliable));
                    Check("the Remote seat was released", !oracle.DeviceIsPlaying(ControlDevice.Remote));

                    // The row must RETRACT. Offering a kick with nobody to kick is the shape the
                    // PeerUp gate in Entries() exists to prevent, and it is invisible in a
                    // screenshot -- the menu looks identical either way until the row is chosen.
                    NetHostMenu.State after = NetHostMenu.CurrentState();
                    Check("the Online Play row retracts once the peer is gone (" + Describe(after)
                        + " rows=" + Render(NetHostMenu.Entries(after)) + ")",
                        !NetHostMenu.Available(after));

                    // ---- 5. the deferred teardown completes -----------------------------------
                    // KickPeer holds the session Active for RejectGraceMs so the queued EvKick can
                    // egress (Stop() -> pc.close() is abortive on WebRTC). On the virtual clock that
                    // window is stepped over deliberately instead of waited out.
                    sb.Append(" 5. the deferred teardown\n");
                    Check("PRECONDITION the session is still Active during the grace",
                        NetSession.Active);
                    clock.Advance(5000);
                    NetSession.Update();
                    Check("the session stopped once the grace elapsed", !NetSession.Active);
                }
            }
            catch (Exception ex)
            {
                Check("the suite ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
            }
            finally
            {
                sb.Append(" 6. teardown -- what this suite must hand back\n");
                if (NetSession.Active)
                {
                    // A leg threw before the grace elapsed. Advance and pump rather than leaving
                    // a session standing: the next suite in net_selftests.txt would SKIP itself
                    // and read as a pass.
                    clock.Advance(60000);
                    NetSession.Update();
                }
                Check("no session is left standing", !NetSession.Active);
                Check("no Remote seat is left standing",
                    !oracle.DeviceIsPlaying(ControlDevice.Remote));
                NetHost.Current = hostBefore;
                Check("the injected clock is handed back", ReferenceEquals(NetHost.Current, hostBefore));
            }

            sb.Append(Tally(pass, fail));
            return sb.ToString();
        }

        // The reliable lane carries the handshake too, so this looks for the EvKick envelope
        // rather than asserting a frame count -- a count would make the leg a hostage to any
        // future reliable chatter the session adds around a kick.
        private static bool HasKickFrame(List<byte[]> frames)
        {
            foreach (byte[] f in frames)
            {
                if (f != null && f.Length >= 4 && f[0] == NetProtocol.MsgEvent && f[1] == NetProtocol.EvKick)
                {
                    return true;
                }
            }
            return false;
        }

        private static string Render(List<NetHostMenu.Entry> e)
        {
            return e.Count == 0 ? "(none)" : string.Join(",", e);
        }

        private static string Describe(NetHostMenu.State s)
        {
            return "session=" + s.SessionActive + " host=" + s.IsHost + " peer=" + s.PeerUp
                + " couldList=" + s.CouldList;
        }

        private static string Tally(int pass, int fail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[hostmenulive] {0} passed, {1} failed\n", pass, fail);
        }
    }
}
