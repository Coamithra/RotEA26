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
        public static void InjectFakeGames()
        {
            games.Clear();
            pingByCode.Clear();
            AddFake("QX7KP", Levels.Level1, 1, 1, 34, 41);
            AddFake("B29MT", Levels.Level2, 2, 1, 120, 88);
            AddFake("Z4HRW", Levels.Level3, 3, 1, 7, 152);
            AddFake("KP8FN", Levels.ClassicAliens, 0, 1, 260, -1);
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
