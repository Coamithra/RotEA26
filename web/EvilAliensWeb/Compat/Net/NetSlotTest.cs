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
    // with the live one's Game and never added to it, so the real player table is untouched --
    // and detached again in a finally, because Oracle's constructor subscribes to
    // Game.Components and would otherwise leave this fixture mirroring the live world for good.
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

            // THE CARD'S CASE. At the menu a seated slot means nothing to the client: the launch
            // path's ResetPlayers() wipes the roster before seating us either way. It used to be
            // reachable in ordinary play too (~60% of attract demos left slot 1 seated, since
            // GameScene.Terminate did not clear the roster) -- card ee96ea61 closed that at the
            // source, but the client's indifference here is what makes it safe regardless, so the
            // case stays covered.
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
            // A scratch Oracle: its constructor ALLOCATES a 4-slot table (it seats nobody) and it
            // is never added to the game, so the live roster is untouched. Its constructor does
            // subscribe to Game.Components, hence the DetachFromComponents() in the finally --
            // without it every run would leak a handler pair and keep mirroring the live world.
            Oracle live = ServiceHelper.Get<IOracleService>()?.Oracle;
            bool oracleLegRan = live != null && live.Game != null;
            if (oracleLegRan)
            {
                Oracle scratch = new Oracle(live.Game);
                try
                {
                    Check(!scratch.IsSeated(0) && !scratch.IsSeated(1), "a fresh roster seats nobody");
                    scratch.AddPlayerAt(0, ControlDevice.Keyboard);
                    scratch.AddPlayerAt(1, ControlDevice.AI); // the shape an attract demo leaves behind
                    Check(scratch.IsSeated(0) && scratch.IsSeated(1), "the attract-demo roster shape seats 0 and 1");

                    // THE PREMISE OF THE WHOLE CARD: MovePlayerSlot refuses on the DESTINATION
                    // being occupied, not the source. A joiner in slot 0 with slot 1 free moves
                    // fine; it is the granted seat being taken that bites.
                    Check(!scratch.MovePlayerSlot(0, 1), "MovePlayerSlot refuses when the DESTINATION is seated");
                    Check(scratch.IsSeated(0), "a refused move leaves the source seat alone");

                    // The mask both sides of the negotiation run on, off that same roster. The
                    // `exclude` rule is the subtle one: the client must NOT report its own seat,
                    // or it blocks the very slot it is trying to move out of.
                    Check(NetSession.OccupiedMask(scratch, exclude: -1) == Mask(0, 1),
                        "OccupiedMask reports every seated slot");
                    Check(NetSession.OccupiedMask(scratch, exclude: 0) == Mask(1),
                        "OccupiedMask omits our own seat, so we never block our own move");
                    Check(NetSession.OccupiedMask(scratch, exclude: 2) == Mask(0, 1),
                        "excluding an EMPTY slot changes nothing");
                    // End to end: that mask, handed to the allocator, is what dodges the collision.
                    Check(NetSession.FirstMutuallyFreeSlot(Mask(0), NetSession.OccupiedMask(scratch, exclude: 0)) == 2,
                        "the real roster's mask makes the host grant slot 2, not the occupied 1");

                    Check(scratch.MovePlayerSlot(0, 2), "MovePlayerSlot allows a move to a FREE seat");
                    Check(!scratch.IsSeated(0) && scratch.IsSeated(2), "an allowed move really moves the seat");

                    // ...and that a full roster is what FirstMutuallyFreeSlot's -1 leg models.
                    scratch.AddPlayerAt(0, ControlDevice.Keyboard);
                    scratch.AddPlayerAt(3, ControlDevice.AI);
                    Check(scratch.FirstFreeSlot(1) < 0, "a full roster really has no free slot above 0");
                    Check(NetSession.FirstMutuallyFreeSlot(NetSession.OccupiedMask(scratch, exclude: -1), 0) == -1,
                        "a full real roster reaches the RejectFull leg");
                }
                finally
                {
                    scratch.DetachFromComponents();
                }
            }

            // ---- 4. Legacy control ------------------------------------------------------------
            //
            // A green tick above proves nothing unless the same input is shown to BREAK the old
            // code (the eaNetScore.test precedent). This is an inlined restatement of the old
            // branch -- it cannot share a data path with the new one, because the whole change is
            // that the decision moved out of the live method -- so it is only worth what the
            // CONTRAST below is worth: the same six inputs, the two policies, different answers.
            byte LegacyAdopt(byte localSlot, byte granted, bool localSeated, bool grantedSeated)
            {
                // The old code assigned peerPrimarySlot HERE, before the early return below --
                // which is what silenced the 1 Hz hello on both peers and made the failure
                // unrecoverable. It is unconditional, so there is nothing to assert about it; the
                // observable difference is the slot it ends up with, asserted below.
                if (localSlot != granted && localSeated && grantedSeated)
                {
                    return localSlot; // "staying put"
                }
                return granted;
            }

            // The menu case, on identical inputs. Old: stays at 0 while the host granted 1, i.e.
            // the permanent disagreement. New: takes the slot, because at the menu that seat is
            // bookkeeping about to be wiped.
            Check(LegacyAdopt(0, 1, localSeated: true, grantedSeated: true) == 0,
                "LEGACY CONTROL: the old policy stayed at slot 0 while the host granted 1");
            Check(NetSession.DecideSlotAdopt(0, 1, None, false, true, true) == NetSession.SlotAdopt.TakeSlot,
                "...where the new policy takes slot 1");
            // Mid-level the two also differ, but the other way: the old one silently carried on in
            // the wrong slot, the new one refuses to settle so the host can re-grant.
            Check(LegacyAdopt(0, 1, localSeated: true, grantedSeated: true) == 0
                && NetSession.DecideSlotAdopt(0, 1, None, true, true, true) == NetSession.SlotAdopt.Renegotiate,
                "LEGACY CONTROL: mid-level the old policy carried on, the new one renegotiates");

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
                Check(NetProtocol.SlotInMask(NetProtocol.SlotBit(i), i), "slot " + i + " reads back as blocked");
                Check(!NetProtocol.SlotInMask(0, i), "slot " + i + " is not blocked in an empty mask");
            }
            Check(!NetProtocol.SlotInMask(0xFF, 4) && !NetProtocol.SlotInMask(0xFF, -1),
                "an out-of-range slot is never blocked");

            // ---- 6. The MENU roster reaches the host allocator (card ee96ea61) ----------------
            //
            // Everything above tests the negotiation given a roster. This tests where the host's
            // roster COMES FROM at the moment it allocates -- the menu-lobby handshake runs at the
            // main menu, and until this card `GameScene.Terminate` left the last level's or attract
            // demo's seats standing there. Unlike the client's `LocalBlockedSlots`, which is
            // guarded on a live `GameScene`, `HostOccupiedSlots` reads the roster raw.
            //
            // So this is a NEGATIVE CONTROL in the eaNetScore.test sense: it shows the exact
            // roster an attract demo leaves behind driving the real allocator to RejectFull, and
            // the same roster after ResetPlayers granting a seat normally.
            if (oracleLegRan)
            {
                Oracle demo = new Oracle(live.Game);
                try
                {
                    // mainMenu_DemoSelected seats slot 0, then Demo1/2/3.Initialize adds 3 more on
                    // a 20% roll -- the shape that fills the table.
                    demo.AddPlayerAt(0, ControlDevice.AI);
                    demo.AddPlayerAt(1, ControlDevice.AI);
                    demo.AddPlayerAt(2, ControlDevice.AI);
                    demo.AddPlayerAt(3, ControlDevice.AI);
                    Check(NetSession.FirstMutuallyFreeSlot(NetSession.OccupiedMask(demo, exclude: -1), 0) == -1,
                        "STALE-ROSTER CONTROL: a full attract-demo roster makes the host reject a good joiner");

                    // The 40% roll is the quieter half: a seat is granted, just the WRONG one, so
                    // the joiner spends the session on another slot's HUD panel and colour.
                    demo.ResetPlayers();
                    demo.AddPlayerAt(0, ControlDevice.AI);
                    demo.AddPlayerAt(1, ControlDevice.AI);
                    Check(NetSession.FirstMutuallyFreeSlot(NetSession.OccupiedMask(demo, exclude: -1), 0) == 2,
                        "STALE-ROSTER CONTROL: a 2-seat leftover pushes the joiner to slot 2, not 1");

                    // THE FIX. Terminate now does this on the way out of every scene, so the
                    // handshake allocates from an empty table however the player got to the menu.
                    demo.ResetPlayers();
                    Check(demo.Players == 0, "ResetPlayers empties the roster");
                    for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
                    {
                        Check(!demo.IsSeated(slot), "slot " + slot + " is unseated after ResetPlayers");
                    }
                    Check(NetSession.OccupiedMask(demo, exclude: -1) == 0,
                        "a reset roster reports an empty occupied mask");
                    Check(NetSession.FirstMutuallyFreeSlot(NetSession.OccupiedMask(demo, exclude: -1), 0) == 1,
                        "a reset roster grants the joiner slot 1, the seat it should have had");
                }
                finally
                {
                    demo.DetachFromComponents();
                }
            }

            // ---- 7. ?netdropgrant is ONE-SHOT, and its latch clears (card ee96ea61) -----------
            //
            // The flag drops a granted couch seat so the host's ExpireUnclaimedGrants path -- which
            // nothing else can reach -- actually runs. Making it one-shot is what lets a single run
            // cover the drop AND the recovery, and the cost is a latch that MUST NOT outlive its
            // session. A missed reset there is silent: the second session in a page would quietly
            // take its first grant instead of dropping it, and the seam would look broken only to
            // whoever was relying on it.
            //
            // Save/restore, so running this over a live ?netdropgrant session cannot eat the drop
            // it is waiting for.
            bool dropLatchWas = NetSession.DropGrantUsed;
            try
            {
                // Flag off: never drops, and -- the subtle half -- never CONSUMES. If an off run
                // armed the latch, whether a session still had its drop would depend on how many
                // grants went past while the seam was disabled.
                NetSession.DropGrantUsed = false;
                Check(!NetSession.ShouldDropGrant(false), "flag off: the first grant is taken");
                Check(!NetSession.ShouldDropGrant(false), "flag off: so is the second");
                Check(!NetSession.DropGrantUsed, "flag off: the latch is not consumed");

                // Flag on: exactly one drop, then every later grant completes normally.
                Check(NetSession.ShouldDropGrant(true), "flag on: the FIRST grant is dropped");
                Check(NetSession.DropGrantUsed, "flag on: the drop is recorded");
                Check(!NetSession.ShouldDropGrant(true), "flag on: the SECOND grant is taken (one-shot)");
                Check(!NetSession.ShouldDropGrant(true), "flag on: and the third");

                // THE PART THAT MATTERS. Driving ResetPerSessionState rather than Stop() for the
                // NetKickTest reason: Stop() early-returns when nothing is Active, so calling it
                // here would execute no reset at all and this leg would pass against the very
                // regression it exists to catch (the latch left out of the reset). Skipped over a
                // LIVE session, which the reset would wipe.
                if (!NetSession.Active)
                {
                    NetSession.ResetPerSessionState();
                    Check(!NetSession.DropGrantUsed, "a session teardown clears the drop latch");
                    Check(NetSession.ShouldDropGrant(true),
                        "the NEXT session drops its first grant too, instead of silently taking it");
                }
            }
            finally
            {
                NetSession.DropGrantUsed = dropLatchWas;
            }

            // ---- 8. A reclaimed couch seat is genuinely RE-USABLE (card ee96ea61) -------------
            //
            // The half ?netdropgrant existed to reach but could never show while it dropped every
            // grant: the host releasing an unclaimed seat is only worth something if the seat then
            // comes BACK. Driven as the real cycle -- allocate, reserve as RemoteFriend the way
            // HandleJoinRequest does, watch the allocator route around it, expire the claim clock,
            // release it the way ExpireUnclaimedGrants does, allocate again.
            if (oracleLegRan)
            {
                Oracle seats = new Oracle(live.Game);
                try
                {
                    const int hostPrimary = 0;
                    const int peerPrimary = 1;
                    seats.AddPlayerAt(hostPrimary, ControlDevice.Keyboard);
                    seats.AddPlayerAt(peerPrimary, ControlDevice.Remote);

                    int granted = NetSession.AllocateSeatFrom(seats, hostPrimary, peerPrimary);
                    Check(granted == 2, "a couch join is allocated slot 2");

                    // HandleJoinRequest holds the seat the moment it grants, so a second join
                    // cannot be handed the same one while the first grant is still in flight.
                    Check(seats.AddPlayerAt(granted, ControlDevice.RemoteFriend),
                        "the grant reserves its seat as RemoteFriend");
                    Check(NetSession.AllocateSeatFrom(seats, hostPrimary, peerPrimary) == 3,
                        "a held grant is not re-allocated -- the next join gets slot 3");

                    // The claim clock. Strictly greater, so a peer whose first stream lands
                    // exactly on the deadline keeps the seat it was given.
                    long deadline = 10000L;
                    Check(!NetSession.GrantHasExpired(deadline, deadline - 1), "a grant is live before its deadline");
                    Check(!NetSession.GrantHasExpired(deadline, deadline), "a grant is still live ON its deadline");
                    Check(NetSession.GrantHasExpired(deadline, deadline + 1), "a grant expires past its deadline");

                    // What ExpireUnclaimedGrants does once the clock is up.
                    seats.RemovePlayerAt(granted, ControlDevice.RemoteFriend);
                    Check(!seats.IsSeated(granted), "the expired grant leaves the roster rather than leaking");

                    // THE CLAIM: the seat is re-allocatable, not merely released.
                    Check(NetSession.AllocateSeatFrom(seats, hostPrimary, peerPrimary) == granted,
                        "the reclaimed seat is handed to the NEXT couch join");
                    Check(seats.AddPlayerAt(granted, ControlDevice.RemoteFriend),
                        "and can actually be seated again");

                    // The leak this guards against, stated as its own assertion: had the seat NOT
                    // come back, a roster with both primaries and one leaked seat would have only
                    // slot 3 left, and the session would run one seat short for good.
                    Check(NetSession.AllocateSeatFrom(seats, hostPrimary, peerPrimary) == 3,
                        "with the seat retaken the allocator moves on to slot 3");
                }
                finally
                {
                    seats.DetachFromComponents();
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("[slottest] ").Append(fails.Count == 0 ? "PASS" : "FAIL")
              .Append(" (").Append(checks - fails.Count).Append('/').Append(checks).Append(" checks)");
            foreach (string f in fails)
            {
                sb.Append("\n  FAILED: ").Append(f);
            }
            sb.Append("\n  covers: DecideSlotAdopt, FirstMutuallyFreeSlot + its convergence, the v8 codec,");
            sb.Append("\n          a legacy control showing the old policy breaking on the same input,");
            sb.Append("\n          the stale menu roster reaching the host allocator (+ ResetPlayers as the fix),");
            sb.Append("\n          ?netdropgrant's one-shot latch and its clearing on a session teardown,");
            sb.Append("\n          and the reserve -> hold -> expire -> REALLOCATE cycle for a reclaimed couch seat.");
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
