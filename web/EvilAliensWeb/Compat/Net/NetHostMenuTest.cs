using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the host pause menu's entry decision (card 0d6ffe70; per-peer rows
    // and the mid-session room toggle are card 0257f8ba). Invoke with eaHostMenu.test() from the
    // browser console (bare eaHostMenu() is the LIVE dump, and eaHostMenu.live() the
    // real-session suite), `eval HostMenuTest` under eahl, or the ProbeHostMenu case set in
    // tools/sim/logic_probe.
    //
    // WHY A DECISION SUITE AND NOT A SCREENSHOT. What these cards add is almost entirely a
    // predicate: WHICH rows exist, given whether a session is up, whether we are its host, which
    // peers are actually in it, and whether the running game is one toggle away from being
    // listed. Every one of those is expensive to reach in a live game (real peers, a real
    // signaling listing, a level, a pause) and trivial to state as data -- so the decision is
    // lifted into NetHostMenu.Entries(State), a pure function, and swept EXHAUSTIVELY here over
    // all 32 boolean combinations x the peer-seat masks. A screenshot would show one of them.
    //
    // THE PROPERTIES THAT ARE NOT RESTATEMENTS OF THE `if`s, each with a cost behind it:
    //   * entry 0 is never destructive. MenuSub1.Reset() forces selectedEntry = 0 and this menu
    //     opens over a frozen world, so whatever lands at index 0 is what a reflexive Enter
    //     hits. If a kick were ever first, a mis-keyed pause would end a stranger's run. This is
    //     the same reasoning NetKickMenu's Initialize documents one level up.
    //   * the kick rows COVER the mask, exactly -- one Kick + one KickAndBlock per up peer, in
    //     seat order, and none for a seat no peer holds. A row for a phantom seat is a no-op
    //     sold as a control; a missing row is an unkickable griefer.
    //   * the room toggle rides along MID-SESSION (card 0257f8ba: a host session with a free
    //     seat is listable, so NetListing's old session/room exclusivity is GONE) -- but never
    //     at index 0 while kick rows exist, and never for a client.
    //   * Available() agrees with Entries() everywhere. GameScene gates the pause row on the
    //     former and builds from the latter; if they ever disagree the player gets a row that
    //     opens an empty menu (or, worse, no row over a game with a griefer in it).
    //
    // THE NEGATIVE CONTROL is the pre-card behaviour -- no such menu, i.e. an empty entry set
    // for every state. The non-degeneracy section asserts each live shape is reachable, so a
    // predicate stuck at "empty" (or at "everything") cannot pass by agreeing with a table that
    // is itself wrong.
    //
    // DELIBERATELY GAME-FREE: it touches Entries/Available/Label only, never CurrentState() or
    // Caption() (which read Settings, NetSession and NetListing statics and, in Caption's case,
    // a live listing). That is what lets logic_probe run it on the desktop CLR.
    internal static class NetHostMenuTest
    {
        private const string RoomOn = "Allow Online Joins: Enabled";
        private const string RoomOff = "Allow Online Joins: Disabled";

        // The seat masks the sweep multiplies the booleans by: none settled, one peer (the
        // classic slot 1), two, and the full three-guest room. Slot 0 can never appear in a
        // real mask (it is the host's own seat) and is covered by the labels section instead.
        private static readonly byte[] SweepMasks = { 0, 0b0010, 0b0110, 0b1110 };

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

            sb.Append("[hostmenu] the pause menu's Online Play decision (cards 0d6ffe70 / 0257f8ba)\n");

            sb.Append(" 1. the exhaustive state sweep\n");
            SectionSweep(Check);

            sb.Append(" 2. entry 0 is never destructive\n");
            SectionSafeDefault(Check);

            sb.Append(" 3. Available agrees with Entries\n");
            SectionAvailable(Check);

            sb.Append(" 4. the kick rows cover the mask exactly\n");
            SectionMaskCoverage(Check);

            sb.Append(" 5. non-degeneracy + the pre-card control\n");
            SectionNonDegenerate(Check);

            sb.Append(" 6. labels\n");
            SectionLabels(Check);

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "[hostmenu] {0} passed, {1} failed\n", pass, fail));
            return sb.ToString();
        }

        private static void ForEachState(Action<NetHostMenu.State> visit)
        {
            for (int mask = 0; mask < 32; mask++)
            {
                bool session = (mask & 1) != 0;
                foreach (byte slots in SweepMasks)
                {
                    visit(new NetHostMenu.State(session, (mask & 2) != 0, (mask & 4) != 0,
                        (mask & 8) != 0, (mask & 16) != 0, session ? slots : (byte)0));
                    if (!session)
                    {
                        break; // no session, no seats -- the masks would be 4 copies of one state
                    }
                }
            }
        }

        // Every boolean combination (x the seat masks while a session is up), each with the row
        // list it must produce. Written as a table rather than as a second copy of the branch,
        // so a change to the branch has to be argued for HERE, one row at a time.
        private static void SectionSweep(Action<string, bool> check)
        {
            ForEachState(s =>
            {
                List<NetHostMenu.Row> want = new List<NetHostMenu.Row>();
                if (s.SessionActive)
                {
                    if (s.IsHost)
                    {
                        // Host + peers actually in the game: the kick rows, per settled seat --
                        // or the slotless fallback pair while no seat has settled. A client
                        // gets nothing (leaving IS its "kick the host", and the pause menu
                        // already offers it); a host with no peer yet gets no kick rows either
                        // -- a kick would no-op.
                        if (s.PeerUp)
                        {
                            want.Add(new NetHostMenu.Row(NetHostMenu.Entry.Back));
                            if (s.PeerSlotsMask == 0)
                            {
                                want.Add(new NetHostMenu.Row(NetHostMenu.Entry.Kick));
                                want.Add(new NetHostMenu.Row(NetHostMenu.Entry.KickAndBlock));
                            }
                            else
                            {
                                for (byte slot = 0; slot < 4; slot++)
                                {
                                    if ((s.PeerSlotsMask & (1 << slot)) != 0)
                                    {
                                        want.Add(new NetHostMenu.Row(NetHostMenu.Entry.Kick, slot));
                                        want.Add(new NetHostMenu.Row(NetHostMenu.Entry.KickAndBlock, slot));
                                    }
                                }
                            }
                        }
                        // Card 0257f8ba: the room toggle rides along whenever the running game
                        // is one toggle from joinable -- which since that card includes a host
                        // session with a free seat. After Back when kick rows exist, leading
                        // otherwise. Note it does NOT depend on `allow` -- offering the toggle
                        // only while the room is OPEN would make closing it a one-way door.
                        if (s.CouldList)
                        {
                            if (want.Count == 0)
                            {
                                want.Add(new NetHostMenu.Row(NetHostMenu.Entry.RoomToggle));
                                want.Add(new NetHostMenu.Row(NetHostMenu.Entry.Back));
                            }
                            else
                            {
                                want.Insert(1, new NetHostMenu.Row(NetHostMenu.Entry.RoomToggle));
                            }
                        }
                    }
                }
                else if (s.CouldList)
                {
                    want.Add(new NetHostMenu.Row(NetHostMenu.Entry.RoomToggle));
                    want.Add(new NetHostMenu.Row(NetHostMenu.Entry.Back));
                }

                List<NetHostMenu.Row> got = NetHostMenu.Entries(s);
                bool ok = Same(want, got);
                // One assertion per state (so the tally IS the sweep), with the ACTUAL list
                // appended only on a miss -- a failure that reports what was wanted and not what
                // happened costs a debugging round trip.
                check("sweep " + Describe(s) + " -> " + Render(want)
                    + (ok ? "" : "   [GOT " + Render(got) + "]"), ok);
            });
        }

        private static void SectionSafeDefault(Action<string, bool> check)
        {
            int nonEmpty = 0;
            bool allSafe = true;
            ForEachState(s =>
            {
                List<NetHostMenu.Row> got = NetHostMenu.Entries(s);
                if (got.Count == 0)
                {
                    return;
                }
                nonEmpty++;
                if (got[0].Kind == NetHostMenu.Entry.Kick || got[0].Kind == NetHostMenu.Entry.KickAndBlock)
                {
                    allSafe = false;
                    check("   ... " + Describe(s) + " leads with a KICK row", false);
                }
            });
            check("no state puts a kick row at index 0 (a reflexive Enter must be harmless)", allSafe);
            // Without this the leg passes vacuously on a build where Entries() returns nothing.
            check("... over a non-empty population (" + nonEmpty + " states offer rows)", nonEmpty > 0);
        }

        private static void SectionAvailable(Action<string, bool> check)
        {
            bool agree = true;
            ForEachState(s =>
            {
                if (NetHostMenu.Available(s) != (NetHostMenu.Entries(s).Count > 0))
                {
                    agree = false;
                    check("   ... " + Describe(s) + " disagrees", false);
                }
            });
            check("Available(s) == (Entries(s).Count > 0) for every swept state", agree);
        }

        // Card 0257f8ba's own property: one Kick + one KickAndBlock per masked seat, in seat
        // order, and NONE for an unmasked one. Checked structurally rather than re-listed, so
        // it holds for every mask the sweep visits.
        private static void SectionMaskCoverage(Action<string, bool> check)
        {
            bool cover = true;
            ForEachState(s =>
            {
                List<NetHostMenu.Row> got = NetHostMenu.Entries(s);
                for (byte slot = 0; slot < 4; slot++)
                {
                    bool wantPair = s.SessionActive && s.IsHost && s.PeerUp
                        && (s.PeerSlotsMask & (1 << slot)) != 0;
                    int kicks = 0;
                    int blocks = 0;
                    foreach (NetHostMenu.Row r in got)
                    {
                        if (r.Slot != slot)
                        {
                            continue;
                        }
                        if (r.Kind == NetHostMenu.Entry.Kick) { kicks++; }
                        if (r.Kind == NetHostMenu.Entry.KickAndBlock) { blocks++; }
                    }
                    if (kicks != (wantPair ? 1 : 0) || blocks != (wantPair ? 1 : 0))
                    {
                        cover = false;
                        check("   ... " + Describe(s) + " slot " + slot + " has " + kicks
                            + " kick / " + blocks + " block rows", false);
                    }
                }
            });
            check("every masked seat gets exactly one Kick and one KickAndBlock row; no other seat gets any", cover);
        }

        private static void SectionNonDegenerate(Action<string, bool> check)
        {
            // Room shape reachable: a plain listable single-player game.
            List<NetHostMenu.Row> room = NetHostMenu.Entries(
                new NetHostMenu.State(false, false, false, true, true));
            check("a listable single-player game DOES offer the room toggle",
                Has(room, NetHostMenu.Entry.RoomToggle));

            // Kick shape reachable: host with two settled peers.
            List<NetHostMenu.Row> kick = NetHostMenu.Entries(
                new NetHostMenu.State(true, true, true, false, true, 0b0110));
            check("a host with two peers DOES offer both kick rows for each",
                kick.Count == 5 && Has(kick, NetHostMenu.Entry.Kick) && Has(kick, NetHostMenu.Entry.KickAndBlock));

            // Card 0257f8ba: the composed shape -- a listable HOST SESSION gets the kick rows
            // AND the room toggle, with the toggle at index 1 (after Back, before any kick).
            List<NetHostMenu.Row> both = NetHostMenu.Entries(
                new NetHostMenu.State(true, true, true, true, true, 0b0010));
            check("a listable host session offers the kick rows AND the room toggle",
                Has(both, NetHostMenu.Entry.Kick) && Has(both, NetHostMenu.Entry.RoomToggle));
            check("... with the toggle at index 1 (Back leads; kicks follow)",
                both.Count == 4 && both[0].Kind == NetHostMenu.Entry.Back
                && both[1].Kind == NetHostMenu.Entry.RoomToggle
                && both[2].Kind == NetHostMenu.Entry.Kick);
            // ...and a session between levels (peerless-but-listable is unreachable today, a
            // lobby has no scene -- but the menu must not DEPEND on that): the room shape alone.
            List<NetHostMenu.Row> sessionRoom = NetHostMenu.Entries(
                new NetHostMenu.State(true, true, false, true, true));
            check("a listable host session with NO peer up degrades to the room shape",
                sessionRoom.Count == 2 && sessionRoom[0].Kind == NetHostMenu.Entry.RoomToggle);

            // The pre-card behaviour, as the control: there was no Online Play row at all, so a
            // predicate that quietly reverted to it would satisfy every "must NOT offer" leg in
            // the sweep and fail only these.
            check("... i.e. the pre-card behaviour (never any row) is genuinely excluded",
                room.Count > 0 && kick.Count > 0 && both.Count > 0);

            // And the other degenerate shape: a predicate stuck at "always".
            check("a CLIENT in a session is offered nothing",
                NetHostMenu.Entries(new NetHostMenu.State(true, false, true, false, true, 0b0010)).Count == 0);
            check("a host whose peer has not arrived (and with nothing to list) is offered nothing",
                NetHostMenu.Entries(new NetHostMenu.State(true, true, false, false, true)).Count == 0);
            check("an unlistable single-player game is offered nothing",
                NetHostMenu.Entries(new NetHostMenu.State(false, false, false, false, true)).Count == 0);
        }

        private static void SectionLabels(Action<string, bool> check)
        {
            NetHostMenu.State open = new NetHostMenu.State(false, false, false, true, true);
            NetHostMenu.State closed = new NetHostMenu.State(false, false, false, true, false);
            // The room row is a STATE READOUT, not a verb -- it must read differently in the two
            // states or the player cannot tell an open room from a closed one. (It deliberately
            // matches the Options entry word for word: it is the same switch.)
            check("the room row reads " + RoomOn + " while joins are allowed",
                NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.RoomToggle), open) == RoomOn);
            check("the room row reads " + RoomOff + " while they are not",
                NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.RoomToggle), closed) == RoomOff);
            // Card 0257f8ba: a seat-named kick row names the PLAYER NUMBER the score panels use
            // (slot + 1), so "Kick Player 2" is the seat the second panel calls Player 2.
            check("a seat-named kick row reads the score panel's player number",
                NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.Kick, 1), open) == "Kick Player 2"
                && NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.KickAndBlock, 3), open) == "Kick and Block Player 4");
            // The slotless fallback keeps the pre-card singular wording -- there is exactly one
            // peer it can mean (KickPeer's own resolution), and naming a number for a seat that
            // has not settled would be a guess.
            check("the slotless fallback keeps the singular wording",
                NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.Kick), open) == "Kick Other Player"
                && NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.KickAndBlock), open) == "Kick and Block Player");
            check("Back reads Back", NetHostMenu.Label(new NetHostMenu.Row(NetHostMenu.Entry.Back), open) == "Back");
        }

        private static bool Has(List<NetHostMenu.Row> rows, NetHostMenu.Entry kind)
        {
            foreach (NetHostMenu.Row r in rows)
            {
                if (r.Kind == kind)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Same(List<NetHostMenu.Row> a, List<NetHostMenu.Row> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Kind != b[i].Kind || a[i].Slot != b[i].Slot)
                {
                    return false;
                }
            }
            return true;
        }

        private static string Render(List<NetHostMenu.Row> e)
        {
            if (e.Count == 0)
            {
                return "(no menu)";
            }
            return string.Join(",", e);
        }

        private static string Describe(NetHostMenu.State s)
        {
            return (s.SessionActive ? "session" : "nosession")
                + "/" + (s.IsHost ? "host" : "client")
                + "/" + (s.PeerUp ? "peer" : "nopeer")
                + "/slots" + s.PeerSlotsMask
                + "/" + (s.CouldList ? "couldlist" : "nolist")
                + "/" + (s.AllowJoins ? "allow" : "closed");
        }
    }
}
