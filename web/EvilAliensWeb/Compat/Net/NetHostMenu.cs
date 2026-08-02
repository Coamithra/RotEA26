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
    // It is the SECOND door onto machinery that already existed, not new machinery:
    //   - kick / kick+block is NetSession.KickPeer(bool) (card 0b8a300b), until now reachable
    //     only from NetKickMenu, i.e. only once the PEER had held a pause for 4s. A peer who
    //     never pauses (blocking shots, hogging powerups, idling in a corner) was unkickable --
    //     that gap is deferred card 98217618, and this menu closes it.
    //   - open / close the room is Settings.AllowOnlineJoins, which NetListing already watches:
    //     off -> Unlist() keeping the room code, on -> Relist() with the SAME code. So closing
    //     and re-opening mid-run is free and does not renumber anyone's room.
    // Consequently this file adds NO protocol, NO wire bytes and NO server call of its own.
    //
    // The submenu is HOST-ONLY. A client has nothing to offer here: it cannot kick (kicking the
    // host is just leaving, which "Exit to Main Menu" already does) and it owns no listing.
    //
    // The two shapes are MUTUALLY EXCLUSIVE by construction, which is why Entries() never has to
    // mix them: NetListing.ComputeEligibleIgnoringSetting refuses while NetSession.Active, so a
    // game with a peer in it is never listable and a listable game never has a peer.
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

        // Everything Entries() is allowed to look at, so the decision is a pure function of
        // named booleans and can be swept as DATA (NetHostMenuTest / ProbeHostMenu) instead of
        // needing a paused level and a live peer per case.
        internal readonly struct State
        {
            internal readonly bool SessionActive;
            internal readonly bool IsHost;
            internal readonly bool PeerUp;
            internal readonly bool CouldList;
            internal readonly bool AllowJoins;

            internal State(bool sessionActive, bool isHost, bool peerUp, bool couldList, bool allowJoins)
            {
                SessionActive = sessionActive;
                IsHost = isHost;
                PeerUp = peerUp;
                CouldList = couldList;
                AllowJoins = allowJoins;
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
                Settings.GetInstance().AllowOnlineJoins);
        }

        // THE decision. Order matters: entry 0 is what a reflexive Enter hits, so it is never
        // destructive -- the room toggle (harmless and instantly reversible) leads its shape, and
        // the kick shape leads with Back instead. Same reasoning as NetKickMenu preselecting
        // "Keep Waiting", one level up.
        internal static List<Entry> Entries(State s)
        {
            List<Entry> entries = new List<Entry>();
            if (s.SessionActive)
            {
                // Kicking is the host's call about the peer in their game. PeerUp gates it rather
                // than SessionActive alone: between StartWith and the handshake completing (and
                // during the post-kick RejectGraceMs teardown) a session is Active with nobody in
                // it, and KickPeer would be a no-op offered as a live option.
                if (s.IsHost && s.PeerUp)
                {
                    entries.Add(Entry.Back);
                    entries.Add(Entry.Kick);
                    entries.Add(Entry.KickAndBlock);
                }
            }
            else if (s.CouldList)
            {
                entries.Add(Entry.RoomToggle);
                entries.Add(Entry.Back);
            }
            return entries;
        }

        // Whether the pause menu should offer "Online Play" at all. An empty submenu must never
        // be reachable -- a row that opens a menu with only "Back" in it is worse than no row.
        internal static bool Available(State s)
        {
            return Entries(s).Count > 0;
        }

        internal static string Label(Entry e, State s)
        {
            switch (e)
            {
            case Entry.RoomToggle:
                // Deliberately the SAME wording as the Options entry it toggles, because it is
                // the same switch: this submenu is a shortcut to it, not a per-run override, so
                // closing the room here keeps future games unlisted too.
                return "Allow Online Joins: " + MenuScene.boolToGameString(s.AllowJoins);
            case Entry.Kick:
                return "Kick Other Player";
            case Entry.KickAndBlock:
                return "Kick and Block Player";
            default:
                return "Back";
            }
        }

        // The caption under the rows: what the player needs to know to choose. Null = none.
        internal static string Caption(State s)
        {
            if (s.SessionActive)
            {
                // Singular on purpose. The protocol is 2-peer, so there is exactly one other
                // MACHINE to kick; any couch players it brought (card 4d904410) are seated
                // through that peer and leave with it. There is no per-seat kick and this
                // wording must not imply one.
                return "Another player has joined your game";
            }
            if (!s.AllowJoins)
            {
                return "Your game is closed to online players";
            }
            return NetListing.Listed && NetListing.RoomCode != ""
                ? "Listed online  -  room " + NetListing.RoomCode
                : "Your game is open to online players";
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
            List<Entry> entries = Entries(s);
            return "[hostmenu] session=" + s.SessionActive
                + " host=" + s.IsHost
                + " peer=" + s.PeerUp
                + " couldList=" + s.CouldList
                + " allowJoins=" + s.AllowJoins
                + " listed=" + (NetListing.Listed ? (NetListing.RoomCode == "" ? "yes" : NetListing.RoomCode) : "no")
                + " available=" + Available(s)
                + " rows=" + (entries.Count == 0 ? "(none)" : string.Join(",", entries));
        }

        private readonly List<Entry> live = new List<Entry>();

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
        internal bool Rebuild(State s, ItemSelected onBack, ItemSelected onRoomToggle,
                              ItemSelected onKick, ItemSelected onKickAndBlock)
        {
            live.Clear();
            live.AddRange(Entries(s));
            if (live.Count == 0)
            {
                return false;
            }
            RemoveAllEntries();
            foreach (Entry e in live)
            {
                AddEntry(Label(e, s));
                switch (e)
                {
                case Entry.RoomToggle:
                    AddEntryEvent(onRoomToggle);
                    break;
                case Entry.Kick:
                    AddEntryEvent(onKick);
                    break;
                case Entry.KickAndBlock:
                    AddEntryEvent(onKickAndBlock);
                    break;
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
                if (live[i] == Entry.RoomToggle)
                {
                    SetEntry(i, Label(Entry.RoomToggle, s));
                    return;
                }
            }
        }

        // The rows currently built, for eaHostMenu()'s dump of the LIVE menu (as opposed to
        // NetHostMenuTest, which drives the pure Entries() over synthetic states).
        internal IReadOnlyList<Entry> LiveEntries => live;

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
