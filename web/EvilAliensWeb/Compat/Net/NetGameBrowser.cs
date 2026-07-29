using System;
using System.Collections.Generic;
using System.Text.Json;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // Joiner-side game browser (card 2001fbd8). Opens the browse socket (a third role that
    // belongs to no room), receives the listed build-compatible games, and collects a real
    // per-host PING (browser -> server -> host -> back) as each pong lands. The list + pings
    // arrive from JS callbacks; they are queued and drained on the game tick (the NetLobby
    // pattern), so the menu only ever reads game state that mutated inside the update.
    //
    // Selecting an entry hands its room code to the normal 11.4 join flow
    // (NetLobby.JoinWithCode); the host is mid-level, so the join becomes join-in-progress.
    public static class NetGameBrowser
    {
        public sealed class GameEntry
        {
            public string Code = "";
            public int Level;
            public int Difficulty;
            public int Players;
            public int AgeSec;
            public int PingMs = -1; // -1 = not measured yet -> the carousel shows "--"
        }

        public static bool Active { get; private set; }
        public static string FailText { get; private set; } = "";

        // Bumped whenever the SET of listed games changes (a browse refresh), so the menu
        // knows to rebuild its entries. Ping-only updates do NOT bump it (the carousel reads
        // PingMs live each frame).
        public static int Version { get; private set; }

        public static IReadOnlyList<GameEntry> Games => games;

        private static readonly List<GameEntry> games = new List<GameEntry>();
        private static readonly Dictionary<string, int> pingByCode = new Dictionary<string, int>();

        private static bool subscribed;
        private static readonly Queue<string> roomsQueue = new Queue<string>();
        private static readonly Queue<(string code, int ping)> pingQueue = new Queue<(string, int)>();
        private static readonly Queue<string> failQueue = new Queue<string>();

        public static void Start()
        {
            if (!subscribed)
            {
                subscribed = true;
                WebRtcInterop.OnRooms += json => roomsQueue.Enqueue(json);
                WebRtcInterop.OnPing += (code, rtt) => pingQueue.Enqueue((code, rtt));
                WebRtcInterop.OnBrowseFail += reason => failQueue.Enqueue(reason);
            }
            games.Clear();
            pingByCode.Clear();
            roomsQueue.Clear();
            pingQueue.Clear();
            failQueue.Clear();
            FailText = "";
            Version++;
            Active = true;
            WebRtcInterop.Browse(DebugFlags.NetSignal, NetSession.ProtocolVersion);
        }

        public static void Stop()
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            WebRtcInterop.EndBrowse();
            games.Clear();
            pingByCode.Clear();
            roomsQueue.Clear();
            pingQueue.Clear();
            failQueue.Clear();
        }

        // ?gamebrowser: inject a fixed set of fake games so the carousel can be screenshotted
        // with no server and no WebRTC. Active is left FALSE (no socket), so Tick is inert.
        //
        // withUnmappedArt (?gamebrowser=fallback) appends the two entries no appearance shot
        // wants -- see below. Two rigs, one flag, because they share this whole boot path and
        // differ only in these two rows.
        public static void InjectFakeGames(bool withUnmappedArt)
        {
            games.Clear();
            pingByCode.Clear();
            // Players deliberately SPAN 1..MaxPlayers-1: a listed game is any game with a free
            // seat (card 4d904410), so a couch host advertises 2 or 3 taken, and this flag is
            // the only rig that ever screenshots that column. All-1 entries hid the hard-coded
            // "/2" denominator this card fixed (48ab9b2f) -- keep them varied.
            AddFake("QX7KP", Levels.Level1, 1, 1, 34, 41);
            AddFake("B29MT", Levels.Level2, 2, 2, 120, 88);
            AddFake("Z4HRW", Levels.Level3, 3, Oracle.MaxPlayers - 1, 7, 152);
            AddFake("KP8FN", Levels.ClassicAliens, 0, 1, 260, -1);
            // Card 0d166364: two UNMAPPED entries -- the rig for SubMenuOnlineGames'
            // no-bundled-art fallback, which nothing else can reach. A listed game's Level is an
            // int off the wire from a stranger's build, so it can be a level we know but have no
            // carousel art for (Tutorial), or one our Levels enum does not contain at all (a
            // NEWER peer's build -- the 9999). Both must draw the default shot rather than throw
            // or blank. Do not "tidy" these into real levels;
            // tools/headless/probes/gamebrowser_fallback.txt asserts on them by name.
            //
            // Behind ?gamebrowser=fallback rather than always on, because they are actively
            // WRONG for the flag's original job: both draw Mission 1's art under the generic
            // "Mission" title, so an appearance screenshot of the carousel would have two of six
            // rows the reader has to know to discount.
            if (withUnmappedArt)
            {
                AddFake("TU7OR", Levels.Tutorial, 1, 1, 12, 22);
                AddFake("FU7UR", (Levels)9999, 2, 2, 55, 190);
            }
            Version++;
        }

        private static void AddFake(string code, Levels level, int difficulty, int players, int ageSec, int ping)
        {
            games.Add(new GameEntry
            {
                Code = code,
                Level = (int)level,
                Difficulty = difficulty,
                Players = players,
                AgeSec = ageSec,
                PingMs = ping,
            });
        }

        public static void Tick()
        {
            if (!Active)
            {
                return;
            }
            bool listChanged = false;
            while (roomsQueue.Count > 0)
            {
                if (ParseRooms(roomsQueue.Dequeue()))
                {
                    listChanged = true;
                }
            }
            if (listChanged)
            {
                Version++;
            }
            while (pingQueue.Count > 0)
            {
                (string code, int ping) = pingQueue.Dequeue();
                pingByCode[code] = ping;
                foreach (GameEntry g in games)
                {
                    if (g.Code == code)
                    {
                        g.PingMs = ping;
                    }
                }
            }
            while (failQueue.Count > 0)
            {
                string reason = failQueue.Dequeue();
                FailText = reason switch
                {
                    "signal" => "Could not reach the server",
                    "busy" => "The server is busy\nTry again in a minute",
                    _ => "Browse failed (" + reason + ")",
                };
            }
        }

        // Rebuild `games` from a rooms JSON array, carrying forward any ping already measured
        // for a code. Returns true if the set of codes changed (so the menu rebuilds).
        private static bool ParseRooms(string json)
        {
            var fresh = new List<GameEntry>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement el in doc.RootElement.EnumerateArray())
                    {
                        string code = GetString(el, "code");
                        if (string.IsNullOrEmpty(code))
                        {
                            continue;
                        }
                        fresh.Add(new GameEntry
                        {
                            Code = code,
                            Level = GetInt(el, "level"),
                            Difficulty = GetInt(el, "difficulty"),
                            Players = GetInt(el, "players"),
                            AgeSec = GetInt(el, "ageSec"),
                            PingMs = pingByCode.TryGetValue(code, out int p) ? p : -1,
                        });
                    }
                }
            }
            catch (Exception)
            {
                return false; // malformed frame -- keep the last good list
            }
            bool changed = fresh.Count != games.Count;
            if (!changed)
            {
                for (int i = 0; i < fresh.Count; i++)
                {
                    if (fresh[i].Code != games[i].Code)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            games.Clear();
            games.AddRange(fresh);
            // Forget pings for codes that dropped off, so the dict can't grow unbounded.
            if (pingByCode.Count > 0)
            {
                var live = new HashSet<string>();
                foreach (GameEntry g in games)
                {
                    live.Add(g.Code);
                }
                var stale = new List<string>();
                foreach (string k in pingByCode.Keys)
                {
                    if (!live.Contains(k))
                    {
                        stale.Add(k);
                    }
                }
                foreach (string k in stale)
                {
                    pingByCode.Remove(k);
                }
            }
            return changed;
        }

        private static string GetString(JsonElement el, string name)
        {
            return el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : "";
        }

        private static int GetInt(JsonElement el, string name)
        {
            return el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)
                ? n
                : 0;
        }
    }
}
