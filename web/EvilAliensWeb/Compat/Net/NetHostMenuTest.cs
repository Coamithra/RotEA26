using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EvilAliensWeb.Compat.Net
{
    // Console self-test for the host pause menu's entry decision (card 0d6ffe70). Invoke with
    // eaHostMenu() from the browser console, `eval HostMenuTest` under eahl, or the
    // ProbeHostMenu case set in tools/sim/logic_probe.
    //
    // WHY A DECISION SUITE AND NOT A SCREENSHOT. What this card adds is almost entirely a
    // predicate: WHICH rows exist, given whether a session is up, whether we are its host,
    // whether a peer is actually in it, and whether the running game is one toggle away from
    // being listed. Every one of those is expensive to reach in a live game (a real peer, a real
    // signaling listing, a level, a pause) and trivial to state as data -- so the decision is
    // lifted into NetHostMenu.Entries(State), a pure function of five booleans, and swept
    // EXHAUSTIVELY here over all 32 combinations. A screenshot would show one of them.
    //
    // THE THREE PROPERTIES THAT ARE NOT RESTATEMENTS OF THE `if`s, and each has a cost behind it:
    //   * entry 0 is never destructive. MenuSub1.Reset() forces selectedEntry = 0 and this menu
    //     opens over a frozen world, so whatever lands at index 0 is what a reflexive Enter
    //     hits. If Kick were ever first, a mis-keyed pause would end a stranger's run. This is
    //     the same reasoning NetKickMenu's Initialize documents one level up.
    //   * the room shape and the kick shape NEVER coexist. In production that holds because
    //     NetListing refuses to list while NetSession.Active -- a property of a DIFFERENT file.
    //     Section 4 asserts the menu does not RELY on it: fed the contradictory state anyway, it
    //     still yields one shape, so the day listing during a session becomes possible the
    //     failure is not a stray row over a live match.
    //   * Available() agrees with Entries() everywhere. GameScene gates the pause row on the
    //     former and builds from the latter; if they ever disagree the player gets a row that
    //     opens an empty menu (or, worse, no row over a game with a griefer in it).
    //
    // THE NEGATIVE CONTROL is the pre-card behaviour -- there was no such menu, i.e. the entry
    // set was empty for every state. Section 5 asserts a NON-degeneracy: at least one state
    // yields the room shape and at least one yields the kick shape, so a predicate stuck at
    // "empty" (or at "everything") cannot pass the sweep by agreeing with a table that is itself
    // wrong. Without it, deleting the whole feature would still fail only the states it changes.
    //
    // DELIBERATELY GAME-FREE: it touches Entries/Available/Label only, never CurrentState() or
    // Caption() (which read Settings, NetSession and NetListing statics and, in Caption's case,
    // a live listing). That is what lets logic_probe run it on the desktop CLR.
    internal static class NetHostMenuTest
    {
        private const string RoomOn = "Allow Online Joins: Enabled";
        private const string RoomOff = "Allow Online Joins: Disabled";

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

            sb.Append("[hostmenu] the pause menu's Online Play decision (card 0d6ffe70)\n");

            sb.Append(" 1. the exhaustive state sweep\n");
            SectionSweep(Check);

            sb.Append(" 2. entry 0 is never destructive\n");
            SectionSafeDefault(Check);

            sb.Append(" 3. Available agrees with Entries\n");
            SectionAvailable(Check);

            sb.Append(" 4. the two shapes never coexist\n");
            SectionShapesDisjoint(Check);

            sb.Append(" 5. non-degeneracy + the pre-card control\n");
            SectionNonDegenerate(Check);

            sb.Append(" 6. labels\n");
            SectionLabels(Check);

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "[hostmenu] {0} passed, {1} failed\n", pass, fail));
            return sb.ToString();
        }

        // Every combination of the five booleans, each with the entry list it must produce.
        // Written as a table rather than as a second copy of the branch, so a change to the
        // branch has to be argued for HERE, one row at a time.
        private static void SectionSweep(Action<string, bool> check)
        {
            for (int mask = 0; mask < 32; mask++)
            {
                bool session = (mask & 1) != 0;
                bool host = (mask & 2) != 0;
                bool peer = (mask & 4) != 0;
                bool could = (mask & 8) != 0;
                bool allow = (mask & 16) != 0;
                NetHostMenu.State s = new NetHostMenu.State(session, host, peer, could, allow);

                List<NetHostMenu.Entry> want = new List<NetHostMenu.Entry>();
                if (session)
                {
                    // Host + a peer actually in the game: the kick shape. A client gets nothing
                    // (leaving IS its "kick the host", and the pause menu already offers it), and
                    // a host with no peer yet gets nothing either -- KickPeer would no-op.
                    if (host && peer)
                    {
                        want.Add(NetHostMenu.Entry.Back);
                        want.Add(NetHostMenu.Entry.Kick);
                        want.Add(NetHostMenu.Entry.KickAndBlock);
                    }
                }
                else if (could)
                {
                    // No session, but this game is one toggle away from being joinable: the room
                    // shape. Note it does NOT depend on `allow` -- offering the toggle only while
                    // the room is OPEN would make closing it a one-way door for the rest of the
                    // run, which is the exact opposite of what the card asks for.
                    want.Add(NetHostMenu.Entry.RoomToggle);
                    want.Add(NetHostMenu.Entry.Back);
                }

                List<NetHostMenu.Entry> got = NetHostMenu.Entries(s);
                bool ok = Same(want, got);
                // One assertion per state (so the tally IS the sweep), with the ACTUAL list
                // appended only on a miss -- a failure that reports what was wanted and not what
                // happened costs a debugging round trip.
                check("sweep " + Describe(s) + " -> " + Render(want)
                    + (ok ? "" : "   [GOT " + Render(got) + "]"), ok);
            }
        }

        private static void SectionSafeDefault(Action<string, bool> check)
        {
            int nonEmpty = 0;
            bool allSafe = true;
            for (int mask = 0; mask < 32; mask++)
            {
                NetHostMenu.State s = new NetHostMenu.State(
                    (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0);
                List<NetHostMenu.Entry> got = NetHostMenu.Entries(s);
                if (got.Count == 0)
                {
                    continue;
                }
                nonEmpty++;
                if (got[0] == NetHostMenu.Entry.Kick || got[0] == NetHostMenu.Entry.KickAndBlock)
                {
                    allSafe = false;
                    check("   ... " + Describe(s) + " leads with a KICK row", false);
                }
            }
            check("no state puts a kick row at index 0 (a reflexive Enter must be harmless)", allSafe);
            // Without this the leg passes vacuously on a build where Entries() returns nothing.
            check("... over a non-empty population (" + nonEmpty + " states offer rows)", nonEmpty > 0);
        }

        private static void SectionAvailable(Action<string, bool> check)
        {
            bool agree = true;
            for (int mask = 0; mask < 32; mask++)
            {
                NetHostMenu.State s = new NetHostMenu.State(
                    (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0);
                if (NetHostMenu.Available(s) != (NetHostMenu.Entries(s).Count > 0))
                {
                    agree = false;
                    check("   ... " + Describe(s) + " disagrees", false);
                }
            }
            check("Available(s) == (Entries(s).Count > 0) for all 32 states", agree);
        }

        private static void SectionShapesDisjoint(Action<string, bool> check)
        {
            bool disjoint = true;
            for (int mask = 0; mask < 32; mask++)
            {
                NetHostMenu.State s = new NetHostMenu.State(
                    (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0);
                List<NetHostMenu.Entry> got = NetHostMenu.Entries(s);
                bool room = got.Contains(NetHostMenu.Entry.RoomToggle);
                bool kick = got.Contains(NetHostMenu.Entry.Kick) || got.Contains(NetHostMenu.Entry.KickAndBlock);
                if (room && kick)
                {
                    disjoint = false;
                    check("   ... " + Describe(s) + " offers both", false);
                }
            }
            check("no state offers the room toggle AND a kick row", disjoint);
            // Production reaches that by a property of a DIFFERENT file -- NetListing refuses to
            // list while NetSession.Active, so CouldList and SessionActive are never both true.
            // This leg deliberately feeds the contradictory state anyway: the menu must not
            // DEPEND on that guarantee, because the day listing during a session becomes
            // possible, the failure would be a silent extra row over a live match rather than a
            // compile error. (The NetListing half is Game-bound and cannot be asserted here; it
            // is covered by that file's own early return.)
            List<NetHostMenu.Entry> contradictory = NetHostMenu.Entries(
                new NetHostMenu.State(true, true, true, true, true));
            check("... even for the impossible session+couldList state, which yields the kick shape only",
                contradictory.Contains(NetHostMenu.Entry.Kick)
                && !contradictory.Contains(NetHostMenu.Entry.RoomToggle));
        }

        private static void SectionNonDegenerate(Action<string, bool> check)
        {
            // Room shape reachable: a plain listable single-player game.
            List<NetHostMenu.Entry> room = NetHostMenu.Entries(
                new NetHostMenu.State(false, false, false, true, true));
            check("a listable single-player game DOES offer the room toggle",
                room.Contains(NetHostMenu.Entry.RoomToggle));

            // Kick shape reachable: host with a peer.
            List<NetHostMenu.Entry> kick = NetHostMenu.Entries(
                new NetHostMenu.State(true, true, true, false, true));
            check("a host with a peer DOES offer both kick rows",
                kick.Contains(NetHostMenu.Entry.Kick) && kick.Contains(NetHostMenu.Entry.KickAndBlock));

            // The pre-card behaviour, as the control: there was no Online Play row at all, so a
            // predicate that quietly reverted to it would satisfy every "must NOT offer" leg in
            // the sweep and fail only these two.
            check("... i.e. the pre-card behaviour (never any row) is genuinely excluded",
                room.Count > 0 && kick.Count > 0);

            // And the other degenerate shape: a predicate stuck at "always".
            check("a CLIENT in a session is offered nothing",
                NetHostMenu.Entries(new NetHostMenu.State(true, false, true, false, true)).Count == 0);
            check("a host whose peer has not arrived is offered nothing",
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
                NetHostMenu.Label(NetHostMenu.Entry.RoomToggle, open) == RoomOn);
            check("the room row reads " + RoomOff + " while they are not",
                NetHostMenu.Label(NetHostMenu.Entry.RoomToggle, closed) == RoomOff);
            // Singular on purpose: the protocol is 2-peer, so there is exactly one other machine
            // to kick, and any couch players it brought leave with it. Wording that implied a
            // per-seat kick would promise something the wire cannot do.
            check("the kick rows name ONE other player (2-peer protocol)",
                NetHostMenu.Label(NetHostMenu.Entry.Kick, open) == "Kick Other Player"
                && NetHostMenu.Label(NetHostMenu.Entry.KickAndBlock, open) == "Kick and Block Player");
            check("Back reads Back", NetHostMenu.Label(NetHostMenu.Entry.Back, open) == "Back");
        }

        private static bool Same(List<NetHostMenu.Entry> a, List<NetHostMenu.Entry> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string Render(List<NetHostMenu.Entry> e)
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
                + "/" + (s.CouldList ? "couldlist" : "nolist")
                + "/" + (s.AllowJoins ? "allow" : "closed");
        }
    }
}
