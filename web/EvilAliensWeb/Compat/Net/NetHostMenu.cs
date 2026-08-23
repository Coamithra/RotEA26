using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat.Net
{
    // The pause menu's "Online Play" submenu (card 0d6ffe70) -- the host's agency over their own
    // online game, reachable whenever they want it rather than only when a griefer hands it to
    // them.
    //
    // It is a door onto machinery that exists elsewhere, not new machinery:
    //   - kick / kick+block is NetSession.KickPeer / KickPeerAt (cards 0b8a300b / 0257f8ba).
    //     Since card 0257f8ba a session holds up to three remote machines, so the kick rows are
    //     PER PEER, named by seat ("Kick Player 2") -- "kick the other player" stopped being a
    //     well-formed request. A peer whose seat has not settled yet keeps the old slotless
    //     pair, which falls back to KickPeer's only-up-peer resolution.
    //   - open / close the room is Settings.AllowOnlineJoins, which NetListing already watches:
    //     off -> Unlist() keeping the room code, on -> Relist() with the SAME code. So closing
    //     and re-opening mid-run is free and does not renumber anyone's room. Since card
    //     0257f8ba a HOST session with a free seat is itself listable, so this row now appears
    //     MID-SESSION too -- it is how a host stops a 3rd/4th stranger from joining a running
    //     match without kicking anyone.
    // Consequently this file adds NO protocol, NO wire bytes and NO server call of its own.
    //
    // The submenu is HOST-ONLY. A client has nothing to offer here: it cannot kick (kicking the
    // host is just leaving, which the pause menu already does) and it owns no listing.
    internal sealed class NetHostMenu : MenuSub1
    {
        // What a row DOES. The menu is rebuilt on every Show (the state it reflects changes
        // between pauses), so the rows are chosen from this set rather than being fixed at
        // construction the way pausedScene's used to be.
        internal enum Entry
        {
            Back,
            RoomToggle,
            Kick,
            KickAndBlock,
        }

        // A row: what it does and, for the kick rows, WHOSE seat it is about (card 0257f8ba).
        // Slot is NetProtocol.SlotNone for everything except a seat-named kick row -- including
        // the fallback kick pair for a peer whose slot exchange has not settled, whose SlotNone
        // routes through KickPeer's own target resolution.
        internal readonly struct Row
        {
            internal readonly Entry Kind;
            internal readonly byte Slot;

            internal Row(Entry kind, byte slot = NetProtocol.SlotNone)
            {
                Kind = kind;
                Slot = slot;
            }

            // The rows= rendering in Dump() -- an interface (net_host_menu.txt greps it), so a
            // slotless row prints exactly the pre-card name and only seat-named kicks differ.
            public override string ToString()
            {
                return Slot == NetProtocol.SlotNone ? Kind.ToString() : Kind + "@" + Slot;
            }
        }

        // Everything Entries() is allowed to look at, so the decision is a pure function of
        // named values and can be swept as DATA (NetHostMenuTest / ProbeHostMenu) instead of
        // needing a paused level and a live peer per case.
        internal readonly struct State
        {
            internal readonly bool SessionActive;
            internal readonly bool IsHost;
            internal readonly bool PeerUp;
            internal readonly bool CouldList;
            internal readonly bool AllowJoins;
            // Bit i = an UP peer's granted primary seat is oracle slot i (card 0257f8ba). 0
            // with PeerUp means "a peer whose seat has not settled" -- the slotless fallback.
            internal readonly byte PeerSlotsMask;

            internal State(bool sessionActive, bool isHost, bool peerUp, bool couldList, bool allowJoins,
                           byte peerSlotsMask = 0)
            {
                SessionActive = sessionActive;
                IsHost = isHost;
                PeerUp = peerUp;
                CouldList = couldList;
                AllowJoins = allowJoins;
                PeerSlotsMask = peerSlotsMask;
            }
        }

        // The live state, read once when the pause menu is built and again when this menu opens.
        internal static State CurrentState()
        {
            return new State(
                NetSession.Active,
                NetSession.IsHost,
                NetSession.PeerUp,
                NetListing.CouldList,
                Settings.GetInstance().AllowOnlineJoins,
                NetSession.Active ? NetSession.UpPeerPrimarySlotsMask() : (byte)0);
        }

        // THE decision. Order matters: entry 0 is what a reflexive Enter hits, so it is never
        // destructive -- every shape leads with Back or the room toggle (harmless and instantly
        // reversible), never a kick. Same reasoning as NetKickMenu preselecting "Keep Waiting".
        //
        // Since card 0257f8ba the session shape and the room shape COMPOSE: a host session with
        // a free seat is listable (NetListing's !Active term is gone), so a paused host can
        // close its room against further strangers AND kick a peer from the same menu.
        internal static List<Row> Entries(State s)
        {
            List<Row> rows = new List<Row>();
            if (s.SessionActive)
            {
                if (!s.IsHost)
                {
                    // A client gets nothing: leaving IS its "kick the host", and the pause menu
                    // already offers that.
                    return rows;
                }
                // Kicking is the host's call about the peers in their game. PeerUp gates it
                // rather than SessionActive alone: between StartWith and the handshake
                // completing (and during the post-kick RejectGraceMs teardown) a session is
                // Active with nobody in it, and a kick would be a no-op offered as live.
                if (s.PeerUp)
                {
                    rows.Add(new Row(Entry.Back));
                    if (s.PeerSlotsMask == 0)
                    {
                        // Seat not settled yet: the pre-0257f8ba slotless pair (KickPeer
                        // resolves "the only up peer" itself).
                        rows.Add(new Row(Entry.Kick));
                        rows.Add(new Row(Entry.KickAndBlock));
                    }
                    else
                    {
                        for (byte slot = 0; slot < Oracle.MaxPlayers; slot++)
                        {
                            if (NetProtocol.SlotInMask(s.PeerSlotsMask, slot))
                            {
                                rows.Add(new Row(Entry.Kick, slot));
                                rows.Add(new Row(Entry.KickAndBlock, slot));
                            }
                        }
                    }
                }
                if (s.CouldList)
                {
                    // The room toggle joins the session shape (card 0257f8ba). After Back when
                    // kick rows exist, leading otherwise -- entry 0 stays non-destructive either
                    // way.
                    if (rows.Count == 0)
                    {
                        rows.Add(new Row(Entry.RoomToggle));
                        rows.Add(new Row(Entry.Back));
                    }
                    else
                    {
                        rows.Insert(1, new Row(Entry.RoomToggle));
                    }
                }
                return rows;
            }
            if (s.CouldList)
            {
                rows.Add(new Row(Entry.RoomToggle));
                rows.Add(new Row(Entry.Back));
            }
            return rows;
        }

        // Whether the pause menu should offer "Online Play" at all. An empty submenu must never
        // be reachable -- a row that opens a menu with only "Back" in it is worse than no row.
        internal static bool Available(State s)
        {
            return Entries(s).Count > 0;
        }

        internal static string Label(Row r, State s)
        {
            switch (r.Kind)
            {
            case Entry.RoomToggle:
                // Deliberately the SAME wording as the Options entry it toggles, because it is
                // the same switch: this submenu is a shortcut to it, not a per-run override, so
                // closing the room here keeps future games unlisted too.
                return "Allow Online Joins: " + MenuScene.boolToGameString(s.AllowJoins);
            case Entry.Kick:
                return r.Slot == NetProtocol.SlotNone
                    ? "Kick Other Player"
                    : "Kick Player " + (r.Slot + 1);
            case Entry.KickAndBlock:
                return r.Slot == NetProtocol.SlotNone
                    ? "Kick and Block Player"
                    : "Kick and Block Player " + (r.Slot + 1);
            default:
                return "Back";
            }
        }

        // The caption under the rows: what the player needs to know to choose. Null = none.
        internal static string Caption(State s)
        {
            if (s.SessionActive)
            {
                int n = CountBits(s.PeerSlotsMask);
                // The kick rows name seats, so the caption counts MACHINES: any couch players a
                // peer brought are seated through it and leave with it -- there is still no
                // per-seat kick, and this wording must not imply one.
                string who = n <= 1
                    ? "Another player has joined your game"
                    : n + " other players have joined your game";
                if (!s.CouldList)
                {
                    return who;
                }
                // The room shape rides along mid-session (card 0257f8ba): say whether MORE
                // strangers can arrive, since that is what the toggle governs here.
                return who + "\n" + (s.AllowJoins
                    ? (NetListing.Listed && NetListing.RoomCode != ""
                        ? "Listed online  -  room " + NetListing.RoomCode
                        : "Your game is open to more players")
                    : "Your game is closed to new players");
            }
            if (!s.AllowJoins)
            {
                return "Your game is closed to online players";
            }
            return NetListing.Listed && NetListing.RoomCode != ""
                ? "Listed online  -  room " + NetListing.RoomCode
                : "Your game is open to online players";
        }

        private static int CountBits(byte mask)
        {
            int n = 0;
            for (int i = 0; i < 8; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    n++;
                }
            }
            return n;
        }

        // The LIVE decision, as one line (eaHostMenu() / `eval HostMenu`). NetHostMenuTest drives
        // the pure Entries() over synthetic states; this reports what the state actually IS right
        // now, which is the only way to tell "the row is missing because the predicate is wrong"
        // from "the row is missing because this game is not listable / has no peer" -- and the
        // menu itself cannot be screenshot into an answer, since a missing row looks like a menu
        // that was simply never given one.
        internal static string Dump()
        {
            State s = CurrentState();
            List<Row> entries = Entries(s);
            return "[hostmenu] session=" + s.SessionActive
                + " host=" + s.IsHost
                + " peer=" + s.PeerUp
                + " peerSlots=" + s.PeerSlotsMask
                + " couldList=" + s.CouldList
                + " allowJoins=" + s.AllowJoins
                + " listed=" + (NetListing.Listed ? (NetListing.RoomCode == "" ? "yes" : NetListing.RoomCode) : "no")
                + " available=" + Available(s)
                + " rows=" + (entries.Count == 0 ? "(none)" : string.Join(",", entries));
        }

        private readonly List<Row> live = new List<Row>();

        internal NetHostMenu(Game game)
            : base(game)
        {
            // The pausedScene value: this draws over a frozen world with the pause darkener
            // already up, so it must sort above the level but must NOT darken again itself
            // (which is why it is a plain MenuSub1 and not a ConfirmationMenu -- see
            // NetShowKickMenu's note about double-darkening).
            base.DrawOrder = 2000;
        }

        // Rebuild the rows from the live state. Returns false when there is nothing to show,
        // which the caller must treat as "do not open" -- the pause entry is gated on the same
        // predicate, so a false here means the state changed between the pause opening and the
        // row being chosen (a peer that dropped, a level that ended).
        // The kick callback carries the row's SEAT (SlotNone = the slotless fallback) and
        // whether it is the blocking variant.
        internal bool Rebuild(State s, ItemSelected onBack, ItemSelected onRoomToggle,
                              System.Action<byte, bool> onKickSlot)
        {
            live.Clear();
            live.AddRange(Entries(s));
            if (live.Count == 0)
            {
                return false;
            }
            RemoveAllEntries();
            foreach (Row r in live)
            {
                AddEntry(Label(r, s));
                switch (r.Kind)
                {
                case Entry.RoomToggle:
                    AddEntryEvent(onRoomToggle);
                    break;
                case Entry.Kick:
                {
                    byte slot = r.Slot;
                    AddEntryEvent(_ => onKickSlot(slot, false));
                    break;
                }
                case Entry.KickAndBlock:
                {
                    byte slot = r.Slot;
                    AddEntryEvent(_ => onKickSlot(slot, true));
                    break;
                }
                default:
                    AddEntryEvent(onBack);
                    break;
                }
            }
            return true;
        }

        // Re-label the room row in place after a toggle (the Options-menu SetEntry pattern) --
        // rebuilding the whole list here would reset the selection off the row the player is
        // standing on.
        internal void RefreshRoomToggleLabel(State s)
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].Kind == Entry.RoomToggle)
                {
                    SetEntry(i, Label(live[i], s));
                    return;
                }
            }
        }

        // The rows are chosen ONCE, when the submenu opens; the state behind them is not frozen
        // with them. A stranger completing a join-in-progress (or a peer dropping) while this is
        // on screen would otherwise leave the wrong shape up -- kick rows for a departed seat, a
        // missing pair for a fresh arrival, both looking perfectly live. Rebuilding in place
        // would move the selection under the player's fingers, so retract instead: doExit()
        // raises OnExit, which is the same path "Back" takes, so the caller returns to a pause
        // menu whose own rows are rebuilt there.
        //
        // The CAPTION is deliberately NOT frozen either way -- it reports the room code and the
        // open/closed state, which the toggle on this very menu changes and which must update.
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (live.Count > 0 && !SameRows(live, Entries(CurrentState())))
            {
                doExit();
            }
        }

        private static bool SameRows(List<Row> a, List<Row> b)
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

        public override void DrawMenu(GameTime gameTime, float yoffset)
        {
            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            base.DrawMenu(gameTime, yoffset + 75f);
            Vector2 titleOrigin = font.MeasureString("Online Play") / 2f + new Vector2(0f, 60f);
            base.SpriteBatch.DrawMetalString(font, "Online Play", new Vector2(400f, 300f), Color.AliceBlue, 0f, titleOrigin, 1f);
            string caption = Caption(CurrentState());
            if (!string.IsNullOrEmpty(caption))
            {
                Vector2 o = font.MeasureString(caption) / 2f;
                base.SpriteBatch.DrawString(font, caption, new Vector2(400f, GetBelowListY(75f)), Color.Gold, 0f, o, 0.6f, (SpriteEffects)0, 0f);
            }
        }
    }
}
