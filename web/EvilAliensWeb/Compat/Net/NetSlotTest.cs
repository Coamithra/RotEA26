using System.Collections.Generic;
using System.Text;
using EvilAliens;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the primary-slot negotiation (card c0229c57), in the eaKickTest /
    // eaNetScore.test / eaBinTest idiom: run `eaSlotTest()` and read PASS/FAIL.
    //
    // WHY a data test. The bug this covers was two consoles disagreeing about ONE BYTE -- the
    // host printing pri=0/1 while the joiner printed pri=0/0 -- after which the joiner never
    // built a remote puppet and the two players simply never saw each other. Nothing was drawn
    // wrong, nothing threw, nothing was logged above a debug line, and it could not self-heal.
    // No screenshot can show that. Reaching it live needs two visible OS windows, a signaling
    // rig, and an attract demo to roll the right branch (see web CLAUDE.md, "JIP pass trap 3").
    //
    // It drives the REAL decision points -- NetSession.DecideSlotAdopt (what the client does
    // with a grant) and NetSession.FirstMutuallyFreeSlot (what the host grants) -- plus the real
    // Oracle and the real v8 handshake codec. What it does NOT cover is the messaging around
    // them (the hello actually egressing, RejectFull reaching the peer, the host re-listing
    // afterwards): those need a live transport and are the two-window run's job. Do not read a
    // PASS as covering them.
    //
    // Leave-no-trace at any point in play: the roster legs run against a SCRATCH Oracle built
    // with the live one's Game and never added to it, so the real player table is untouched.
    internal static class NetSlotTest
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

            const byte None = NetProtocol.SlotNone;

            // ---- 1. DecideSlotAdopt: the client's side of the negotiation --------------------
            //
            // Argument order: (localSlot, granted, peerSlot, sceneUp, localSeated, grantedSeated).

            // Idempotent repeats: hellos arrive at 1 Hz until the pairing settles, so the same
            // grant is handled many times and must not re-do the move.
            Check(NetSession.DecideSlotAdopt(1, 1, 0, true, true, true) == NetSession.SlotAdopt.Settled,
                "a grant we already took is Settled");
            // ...but not before we have settled it. peerSlot == SlotNone is the "not settled yet"
            // marker, and reading it as settled is what silenced the retry.
            Check(NetSession.DecideSlotAdopt(1, 1, None, true, true, false) == NetSession.SlotAdopt.TakeSlot,
                "granted our current slot but not yet settled -> TakeSlot, not Settled");

            // THE CARD'S CASE. At the menu the roster is leftover bookkeeping (GameScene.Terminate
            // never clears it; ~60% of attract demos leave slot 1 seated) that the launch path's
            // ResetPlayers() wipes before seating us -- so a busy destination means nothing there.
            Check(NetSession.DecideSlotAdopt(0, 1, None, false, true, true) == NetSession.SlotAdopt.TakeSlot,
                "menu + granted seat busy -> TakeSlot (the reachable-by-a-real-player case)");
            Check(NetSession.DecideSlotAdopt(0, 1, None, false, true, false) == NetSession.SlotAdopt.TakeSlot,
                "menu + granted seat free -> TakeSlot");
            Check(NetSession.DecideSlotAdopt(0, 1, None, false, false, false) == NetSession.SlotAdopt.TakeSlot,
                "menu + nothing seated -> TakeSlot");

            // Mid-level (the dev ?net=join flow boots into a level before pairing): the seat is
            // load-bearing, so it and the live ship move across.
            Check(NetSession.DecideSlotAdopt(0, 1, None, true, true, false) == NetSession.SlotAdopt.MoveSeat,
                "scene up + seated + granted seat free -> MoveSeat");
            // Not seated yet mid-level (the grant landed before SpawnAllPlayers): nothing to move.
            Check(NetSession.DecideSlotAdopt(0, 1, None, true, false, false) == NetSession.SlotAdopt.TakeSlot,
                "scene up + not seated -> TakeSlot");
            // The only remaining case: a real race, our roster changed under the grant.
            Check(NetSession.DecideSlotAdopt(0, 1, None, true, true, true) == NetSession.SlotAdopt.Renegotiate,
                "scene up + seated + granted seat busy -> Renegotiate");

            // Renegotiate must never be reachable from the menu, at ANY slot pairing -- that is
            // the whole point of gating the move on a live scene. Sweep it rather than trusting
            // the three spot checks above.
            bool menuEverRenegotiates = false;
            for (int local = 0; local < Oracle.MaxPlayers; local++)
            {
                for (int granted = 0; granted < Oracle.MaxPlayers; granted++)
                {
                    foreach (bool seated in new[] { false, true })
                    {
                        foreach (bool destSeated in new[] { false, true })
                        {
                            menuEverRenegotiates |= NetSession.DecideSlotAdopt((byte)local, (byte)granted, None,
                                false, seated, destSeated) == NetSession.SlotAdopt.Renegotiate;
                        }
                    }
                }
            }
            Check(!menuEverRenegotiates, "no menu-side roster shape can reach Renegotiate");

            // ---- 2. FirstMutuallyFreeSlot: the host's side of the negotiation ----------------

            byte Mask(params int[] slots)
            {
                byte m = 0;
                foreach (int s in slots)
                {
                    m |= NetProtocol.SlotBit(s);
                }
                return m;
            }

            // The ordinary case: host alone, joiner unconstrained -> slot 1.
            Check(NetSession.FirstMutuallyFreeSlot(Mask(0), 0) == 1, "an empty game grants slot 1");
            // Slot 0 is the host's chair and is never granted, even when the roster is empty --
            // in the menu-lobby flow it is still unseated at pairing time.
            Check(NetSession.FirstMutuallyFreeSlot(0, 0) == 1, "slot 0 is never granted");
            // THE FIX: slot 1 free here but taken on the joiner -> grant slot 2 instead. Before
            // v8 the host could not see this and granted 1 anyway.
            Check(NetSession.FirstMutuallyFreeSlot(Mask(0), Mask(0, 1)) == 2,
                "a seat taken on the JOINER is skipped (the attract-demo roster shape)");
            // Both constraints at once: host has a couch player in 1, joiner blocks 2.
            Check(NetSession.FirstMutuallyFreeSlot(Mask(0, 1), Mask(2)) == 3,
                "host-side and joiner-side constraints are both honoured");
            // Exhausted -> -1, which is the RejectFull ("Game full") path, not a silent hang.
            Check(NetSession.FirstMutuallyFreeSlot(Mask(0, 1), Mask(2, 3)) == -1,
                "no seat free on both sides reports -1 (-> RejectFull)");
            Check(NetSession.FirstMutuallyFreeSlot(Mask(0, 1, 2, 3), 0) == -1, "a full host roster reports -1");
            Check(NetSession.FirstMutuallyFreeSlot(0, Mask(1, 2, 3)) == -1, "a full JOINER roster reports -1");

            // Convergence. The client re-hellos with a fresh mask when a grant does not work, so
            // the loop must terminate -- and must never re-offer a slot the joiner just refused.
            // Worst case: the joiner refuses every seat in turn.
            {
                byte hostOccupied = Mask(0);
                byte joinerBlocked = 0;
                int rounds = 0;
                int granted;
                var offered = new List<int>();
                while ((granted = NetSession.FirstMutuallyFreeSlot(hostOccupied, joinerBlocked)) >= 0 && rounds < 16)
                {
                    rounds++;
                    Check(!offered.Contains(granted), "round " + rounds + " does not re-offer slot " + granted);
                    offered.Add(granted);
                    joinerBlocked |= NetProtocol.SlotBit(granted); // the joiner refuses it
                }
                Check(granted < 0, "the negotiation terminates instead of looping forever");
                Check(rounds <= Oracle.MaxPlayers - 1, "it terminates within one round per grantable seat (was "
                    + rounds + ")");
            }

            // ---- 3. The premises, against the REAL Oracle -------------------------------------
            //
            // The two functions above are only correct if the oracle really behaves the way their
            // inputs assume. Ground that rather than leaving the suite a self-consistent fiction.
            // A scratch Oracle: its constructor seats a full 4-slot table and it is never added to
            // the game, so the live roster is untouched.
            Oracle live = ServiceHelper.Get<IOracleService>()?.Oracle;
            bool oracleLegRan = live != null && live.Game != null;
            if (oracleLegRan)
            {
                Oracle scratch = new Oracle(live.Game);
                Check(!scratch.IsSeated(0) && !scratch.IsSeated(1), "a fresh roster seats nobody");
                scratch.AddPlayerAt(0, ControlDevice.Keyboard);
                scratch.AddPlayerAt(1, ControlDevice.AI); // the shape an attract demo leaves behind
                Check(scratch.IsSeated(0) && scratch.IsSeated(1), "the attract-demo roster shape seats 0 and 1");

                // THE PREMISE OF THE WHOLE CARD: MovePlayerSlot refuses on the DESTINATION being
                // occupied, not the source. A joiner in slot 0 with slot 1 free moves fine; it is
                // the granted seat being taken that bites.
                Check(!scratch.MovePlayerSlot(0, 1), "MovePlayerSlot refuses when the DESTINATION is seated");
                Check(scratch.IsSeated(0), "a refused move leaves the source seat alone");
                Check(scratch.MovePlayerSlot(0, 2), "MovePlayerSlot allows a move to a FREE seat");
                Check(!scratch.IsSeated(0) && scratch.IsSeated(2), "an allowed move really moves the seat");

                // ...and that a full roster is what FirstMutuallyFreeSlot's -1 leg models.
                scratch.AddPlayerAt(0, ControlDevice.Keyboard);
                scratch.AddPlayerAt(3, ControlDevice.AI);
                Check(scratch.FirstFreeSlot(1) < 0, "a full roster really has no free slot above 0");
            }

            // ---- 4. Legacy control ------------------------------------------------------------
            //
            // A green tick above proves nothing unless the same input is shown to BREAK the old
            // code (the eaNetScore.test precedent). The old policy, verbatim in shape: settle
            // peerPrimarySlot FIRST, then bail out of a busy move "staying put".
            (byte local, byte peer, bool settled) LegacyAdopt(byte localSlot, byte granted, bool localSeated, bool grantedSeated)
            {
                byte peer = NetSession.HostPrimarySlot; // assigned BEFORE the early return
                if (localSlot != granted && localSeated && grantedSeated)
                {
                    return (localSlot, peer, true); // "staying put" -- and settled anyway
                }
                return (granted, peer, true);
            }

            // The menu case: the old code left the peers disagreeing (joiner 0, host granted 1)...
            var legacyMenu = LegacyAdopt(0, 1, localSeated: true, grantedSeated: true);
            Check(legacyMenu.local != 1, "LEGACY CONTROL: the old policy failed to take the granted slot");
            // ...and still marked the exchange settled, which is what stopped the 1 Hz hello on
            // BOTH peers and made it unrecoverable. The new code returns Renegotiate here and
            // leaves peerPrimarySlot at SlotNone, so the retry survives.
            Check(legacyMenu.settled, "LEGACY CONTROL: the old policy settled anyway, killing the retry");
            Check(NetSession.DecideSlotAdopt(0, 1, None, false, true, true) != NetSession.SlotAdopt.Renegotiate
                && NetSession.DecideSlotAdopt(0, 1, None, true, true, true) == NetSession.SlotAdopt.Renegotiate,
                "the new policy takes the slot at the menu and renegotiates mid-level");

            // The host's side of the control: the pre-v8 allocator could not see the joiner's
            // roster at all, so it granted slot 1 into the exact collision above.
            Check(NetSession.FirstMutuallyFreeSlot(Mask(0), 0) == 1
                && NetSession.FirstMutuallyFreeSlot(Mask(0), Mask(0, 1)) == 2,
                "LEGACY CONTROL: the same host roster grants 1 blind but 2 once the joiner speaks up");

            // ---- 5. The v8 handshake codec ----------------------------------------------------
            //
            // blockedSlots is a new byte at the END of the layout, which is exactly where a
            // silent read-past-the-end or an off-by-one lands. A slip here would block the wrong
            // seats (or none) with nothing visible on screen.
            foreach (bool asHost in new[] { true, false })
            {
                byte blocked = Mask(1, 3);
                byte[] hello = NetProtocol.EncodeHello(NetSession.ProtocolVersion, asHost, 7UL, 0,
                    primarySlot: 2, peerId: 9UL, blockedSlots: blocked);
                // Literal 22, not HelloBytes: EncodeHandshake allocates new byte[HelloBytes], so
                // comparing against it can never fail. The point is to catch the constant moving.
                Check(hello.Length == 22, "a v8 hello is 22 bytes (host=" + asHost + ")");
                Check(NetProtocol.TryDecodeHandshake(hello, out _, out _, out _, out _, out byte slot, out _, out byte gotBlocked),
                    "a v8 hello decodes (host=" + asHost + ")");
                Check(gotBlocked == blocked, "blockedSlots round-trips (host=" + asHost + ")");
                Check(slot == 2, "primarySlot still round-trips beside it (host=" + asHost + ")");
            }
            byte[] welcome = NetProtocol.EncodeWelcome(NetSession.ProtocolVersion, true, 1UL, 0, 1, 2UL, Mask(2));
            NetProtocol.TryDecodeHandshake(welcome, out _, out _, out _, out _, out _, out _, out byte welcomeBlocked);
            Check(welcomeBlocked == Mask(2), "blockedSlots round-trips through welcome");
            // A v7 (21-byte) handshake must be REFUSED, not read short -- otherwise its missing
            // last byte decodes as whatever follows in memory, i.e. a random blocked-slot mask.
            Check(!NetProtocol.TryDecodeHandshake(new byte[21], out _, out _, out _, out _, out _, out _, out _),
                "a pre-v8 (21-byte) handshake is refused, not misread");

            // The mask helpers themselves, over the full slot range plus the out-of-range guard.
            for (int i = 0; i < Oracle.MaxPlayers; i++)
            {
                Check(NetProtocol.SlotIsBlocked(NetProtocol.SlotBit(i), i), "slot " + i + " reads back as blocked");
                Check(!NetProtocol.SlotIsBlocked(0, i), "slot " + i + " is not blocked in an empty mask");
            }
            Check(!NetProtocol.SlotIsBlocked(0xFF, 4) && !NetProtocol.SlotIsBlocked(0xFF, -1),
                "an out-of-range slot is never blocked");

            StringBuilder sb = new StringBuilder();
            sb.Append("[slottest] ").Append(fails.Count == 0 ? "PASS" : "FAIL")
              .Append(" (").Append(checks - fails.Count).Append('/').Append(checks).Append(" checks)");
            foreach (string f in fails)
            {
                sb.Append("\n  FAILED: ").Append(f);
            }
            sb.Append("\n  covers: DecideSlotAdopt, FirstMutuallyFreeSlot + its convergence, the v8 codec,");
            sb.Append("\n          and a legacy control showing the old policy breaking on the same input.");
            if (!oracleLegRan)
            {
                // A skipped leg must never read as a passed one.
                sb.Append("\n  SKIPPED (no oracle service): the real-Oracle premise leg. Re-run in-game to cover it.");
            }
            sb.Append("\n  NOT covered (two-window run): hello delivery, RejectFull reaching the peer, the re-list.");
            return sb.ToString();
        }
    }
}
