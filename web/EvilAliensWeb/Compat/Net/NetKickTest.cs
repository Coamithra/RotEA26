using System.Collections.Generic;
using System.Text;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the kick/block rules (card 0b8a300b), in the eaNetSim.test /
    // eaBinTest / eaNetBgTest idiom: run `eaKickTest()` and read PASS/FAIL.
    //
    // WHY a data test and not a screenshot or a two-window run. The block is a pure predicate
    // over a set -- "would this peer be refused at the hello" -- and its two most dangerous
    // failure modes are both INVISIBLE in play: a block that silently fails to persist across
    // the Stop() a kick triggers (the host re-lists seconds later, and the griefer walks back
    // in), and a wire-layout slip in the v6 handshake that decodes someone else's bytes as a
    // peer id (which would block the wrong person, or nobody). Neither shows on screen, and
    // reaching them live needs two windows, a cooperating griefer and a 4s pause.
    //
    // It drives the REAL decision points -- NetSession.ApplyKickBlock / IsPeerBlocked, the two
    // methods KickPeer and HandleHello themselves call, and the real NetProtocol codec. What it
    // does NOT cover, by construction, is the messaging and teardown around them (EvKick
    // actually reaching the peer, the freeze lifting, the seats freeing, the re-list): those
    // need a live transport and are the two-window run's job. Do not read a PASS as covering
    // them.
    //
    // It writes to the live block set but snapshots and restores it exactly, so a mid-level run
    // is safe. The one leg it will NOT run over a live session is the survives-Stop() check --
    // that would end a real match to prove a point -- and it says so in the report rather than
    // counting a skipped leg as a pass. Run it from the menu to get full coverage.
    internal static class NetKickTest
    {
        public static string Run()
        {
            List<string> fails = new List<string>();
            int checks = 0;

            void Check(bool ok, string what)
            {
                checks++;
                if (!ok)
                {
                    fails.Add(what);
                }
            }

            // Snapshot the WHOLE live set so a mid-level run puts every real block back.
            ulong[] saved = NetSession.SnapshotBlockedPeers();
            const ulong griefer = 0x1122334455667788UL;
            const ulong bystander = 0x99AABBCCDDEEFF00UL;
            NetSession.ClearBlockedPeers();

            // 1. Nobody is blocked by default -- an ordinary joiner must get in.
            Check(!NetSession.IsPeerBlocked(griefer), "clean set blocks nobody");

            // 2. A kick WITHOUT block does not make them unwelcome. This is the distinction the
            //    whole two-entry menu rests on: "Kick Player" must be re-joinable.
            NetSession.ApplyKickBlock(block: false, peerId: griefer);
            Check(!NetSession.IsPeerBlocked(griefer), "kick without block leaves them joinable");
            Check(NetSession.BlockedPeerCount == 0, "kick without block records nothing");

            // 3. Kick AND block refuses that peer...
            NetSession.ApplyKickBlock(block: true, peerId: griefer);
            Check(NetSession.IsPeerBlocked(griefer), "kick+block refuses that peer");

            // 4. ...and only that peer. A block is not a "close the game to everyone" switch;
            //    the host stays listed and open to the rest of the world.
            Check(!NetSession.IsPeerBlocked(bystander), "kick+block refuses ONLY that peer");

            // 5. Survives the session teardown a kick triggers. This is the one that matters:
            //    KickPeer Stop()s the session, NetListing re-lists within a tick or two, and the
            //    block has to still be standing when the griefer's rejoin hello lands.
            //    Drives ResetPerSessionState (the whole body of Stop) rather than Stop itself:
            //    Stop early-returns when nothing is Active, so calling it here would execute no
            //    reset at all and the leg would pass no matter what -- including against the very
            //    regression it exists to catch (a blockedPeers.Clear() added to the teardown).
            //    Still skipped over a LIVE session: the reset would wipe real per-session state.
            bool stopLegRan = !NetSession.Active;
            if (stopLegRan)
            {
                NetSession.ResetPerSessionState();
                Check(NetSession.IsPeerBlocked(griefer), "block survives the session teardown reset");
            }

            // 6. Id 0 (the peer could not produce a token) is never recorded and never matched,
            //    so one broken localStorage cannot get every such peer refused at once.
            NetSession.ApplyKickBlock(block: true, peerId: 0UL);
            Check(!NetSession.IsPeerBlocked(0UL), "peerId 0 is never blocked");
            Check(NetSession.BlockedPeerCount == 1, "peerId 0 is never recorded");

            // 7. Blocks are scoped to the level run -- GameScene.Terminate calls this.
            NetSession.ClearBlockedPeers();
            Check(!NetSession.IsPeerBlocked(griefer), "level exit clears the block list");

            // 8. The v6 handshake carries the peer id intact. A silent layout slip here would
            //    block the wrong person (or nobody) with nothing visible on screen -- so assert
            //    the round trip through the real codec, at both handshake types and both roles.
            foreach (bool asHost in new[] { true, false })
            {
                byte[] hello = NetProtocol.EncodeHello(NetSession.ProtocolVersion, asHost, 0xDEADBEEFCAFEF00DUL,
                    NetProtocol.HelloFlagDebugActive, primarySlot: 2, peerId: griefer, blockedSlots: 0);
                // Literal 22, not HelloBytes: EncodeHandshake allocates new byte[HelloBytes], so
                // comparing against it can never fail. The point is to catch the constant moving.
                Check(hello.Length == 22, "hello is 22 bytes (host=" + asHost + ")");
                bool ok = NetProtocol.TryDecodeHandshake(hello, out byte ver, out bool isHost, out ulong hash,
                    out byte flags, out byte slot, out ulong id, out _);
                Check(ok, "hello decodes (host=" + asHost + ")");
                Check(ver == NetSession.ProtocolVersion, "version round-trips (host=" + asHost + ")");
                Check(isHost == asHost, "role round-trips (host=" + asHost + ")");
                Check(hash == 0xDEADBEEFCAFEF00DUL, "build hash round-trips (host=" + asHost + ")");
                Check(flags == NetProtocol.HelloFlagDebugActive, "flags round-trip (host=" + asHost + ")");
                Check(slot == 2, "primary slot round-trips (host=" + asHost + ")");
                Check(id == griefer, "peer id round-trips (host=" + asHost + ")");
            }
            byte[] welcome = NetProtocol.EncodeWelcome(NetSession.ProtocolVersion, true, 1UL, 0, 1, bystander, 0);
            NetProtocol.TryDecodeHandshake(welcome, out _, out _, out _, out _, out _, out ulong wid, out _);
            Check(wid == bystander, "peer id round-trips through welcome");
            Check(welcome[0] == NetProtocol.MsgWelcome, "welcome keeps its message type");

            // 9. A v5 hello (the pre-peerId layout) must be REFUSED, not read short. Its 13
            //    bytes would otherwise decode with whatever followed as a peer id.
            byte[] old = new byte[13];
            Check(!NetProtocol.TryDecodeHandshake(old, out _, out _, out _, out _, out _, out _, out _),
                "a pre-v6 (13-byte) hello is refused, not misread");

            // 10. The kick payload the client reads its notice from.
            byte[] kick = NetProtocol.EncodeByteEvent(7, NetProtocol.EvKick, 1);
            Check(kick[0] == NetProtocol.MsgEvent && kick[1] == NetProtocol.EvKick && kick[4] == 1,
                "EvKick encodes [MsgEvent][EvKick][seq:2][blocked]");

            // Restore whatever was live before.
            NetSession.ClearBlockedPeers();
            foreach (ulong id in saved)
            {
                NetSession.ApplyKickBlock(block: true, peerId: id);
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("[kicktest] ").Append(fails.Count == 0 ? "PASS" : "FAIL")
              .Append(" (").Append(checks - fails.Count).Append('/').Append(checks).Append(" checks)");
            foreach (string f in fails)
            {
                sb.Append("\n  FAILED: ").Append(f);
            }
            sb.Append("\n  covers: the block predicate + the v6 handshake codec.");
            if (!stopLegRan)
            {
                // A skipped leg must never read as a passed one.
                sb.Append("\n  SKIPPED (session live): survives-Stop(). Re-run from the main menu to cover it.");
            }
            sb.Append("\n  NOT covered (two-window run): EvKick delivery, the unfreeze, seat release, re-listing.");
            return sb.ToString();
        }
    }
}
