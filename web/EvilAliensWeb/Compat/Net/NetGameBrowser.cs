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
        // A listing is DISPLAY-ONLY, so its enums take the SENTINEL policy rather than being
        // rejected (wire-enum contract: NetProtocol). A game whose level or difficulty this
        // build does not know is a NORMAL production case -- a stranger on a newer build --
        // and hiding the row would hide a joinable game. The raw ints stay for the log line;
        // the checked nullables beside them are what the carousel reads, so no consumer has
        // to cast, and null means "not a value this build knows".
        public sealed class GameEntry
        {
            public string Code = "";
            public int Level;
            public int Difficulty;
            public int Players;
            public int AgeSec;
            public int PingMs = -1; // -1 = not measured yet -> the carousel shows "--"

            // Room thumbnail (card e7404647). ShotSeq is the server's sequence number for the
            // stored picture, 0 = it has none; ShotAgeSec is how old that picture is. The BYTES
            // are not here -- they are fetched separately and live in the thumbnail store below,
            // because this object is rebuilt from scratch on every browse refresh and a
            // thumbnail must survive that.
            public int ShotSeq;
            public int ShotAgeSec;

            public Levels? KnownLevel =>
                NetProtocol.TryLevel(Level, out Levels l) ? l : (Levels?)null;

            public Settings.DifficultyLevel? KnownDifficulty =>
                NetProtocol.TryDifficulty(Difficulty, out Settings.DifficultyLevel d) ? d : (Settings.DifficultyLevel?)null;
        }

        public static bool Active { get; private set; }
        public static string FailText { get; private set; } = "";

        // Bumped whenever the SET of listed games changes (a browse refresh), so the menu
        // knows to rebuild its entries. Ping-only updates do NOT bump it (the carousel reads
        // PingMs live each frame).
        public static int Version { get; private set; }

        public static IReadOnlyList<GameEntry> Games => games;

        // Room thumbnails (card e7404647): the decoded RGBA the carousel draws instead of stock
        // level art, keyed by room code. Held HERE rather than on GameEntry because the entries
        // are thrown away and rebuilt every browse refresh (~4 s) while a thumbnail is refreshed
        // every ~15 s -- tying the two together would blank every picture four times a minute.
        //
        // A thumbnail this old is dropped in favour of the stock art. Deliberately generous
        // against the ~15 s pull cadence: the server's schedule is a fixed global budget, so a
        // busy server stretches the per-room interval, and the intended degradation is a stale
        // picture, never no picture. Mirrors the server's own SHOT_MAX_AGE_SECONDS.
        internal const int StaleAfterSec = 180;

        internal sealed class Thumbnail
        {
            public int Seq;
            public byte[] Rgba;
            public int Width;
            public int Height;
            // Bumped whenever the PIXELS change, so a consumer holding a GPU texture knows to
            // re-upload without comparing buffers. Distinct from Seq, which is the server's
            // number and restarts at 1 whenever a room re-lists.
            public int Revision;
        }

        public static IReadOnlyList<GameEntry> GamesForTest => games;

        private static readonly List<GameEntry> games = new List<GameEntry>();
        private static readonly Dictionary<string, int> pingByCode = new Dictionary<string, int>();
        private static readonly Dictionary<string, Thumbnail> thumbs = new Dictionary<string, Thumbnail>();
        // Codes whose fetch is out on the wire. Cleared by the answer (including the empty
        // "nothing stored" answer), so a room with no picture is asked once per seq change
        // rather than once per frame.
        private static readonly HashSet<string> shotInFlight = new HashSet<string>();
        private static int thumbRevision;

        private static bool subscribed;
        private static readonly Queue<string> roomsQueue = new Queue<string>();
        private static readonly Queue<(string code, int ping)> pingQueue = new Queue<(string, int)>();
        private static readonly Queue<string> failQueue = new Queue<string>();
        private static readonly Queue<(string code, int seq, byte[] rgba, int w, int h)> shotQueue
            = new Queue<(string, int, byte[], int, int)>();

        public static void Start()
        {
            if (!subscribed)
            {
                subscribed = true;
                WebRtcInterop.OnRooms += json => roomsQueue.Enqueue(json);
                WebRtcInterop.OnPing += (code, rtt) => pingQueue.Enqueue((code, rtt));
                WebRtcInterop.OnBrowseFail += reason => failQueue.Enqueue(reason);
                WebRtcInterop.OnShot += (code, seq, rgba, w, h) => shotQueue.Enqueue((code, seq, rgba, w, h));
            }
            games.Clear();
            pingByCode.Clear();
            ClearThumbnails();
            roomsQueue.Clear();
            pingQueue.Clear();
            failQueue.Clear();
            shotQueue.Clear();
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
            ClearThumbnails();
            roomsQueue.Clear();
            pingQueue.Clear();
            failQueue.Clear();
            shotQueue.Clear();
        }

        // Room thumbnails are a live picture of somebody else's session, so they are dropped
        // whenever the browser is not up rather than kept warm for a later visit: a minutes-old
        // frame from a room that may not exist any more is worse than the stock art.
        private static void ClearThumbnails()
        {
            thumbs.Clear();
            shotInFlight.Clear();
            thumbRevision++;
        }

        internal static bool TryGetThumbnail(string code, out Thumbnail thumb)
        {
            return thumbs.TryGetValue(code, out thumb);
        }

        // Install a thumbnail directly, bypassing the wire. The entry point for BOTH the JS
        // answer path and the offline rigs (?gamebrowser=thumbs, eaRoomShot.inject), so the two
        // reach the carousel through identical code -- a rig that installed pictures its own way
        // would prove nothing about the real one. rgba == null forgets the code's picture.
        internal static void SetThumbnail(string code, int seq, byte[] rgba, int w, int h)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }
            if (rgba == null || w <= 0 || h <= 0 || rgba.Length != w * h * 4)
            {
                thumbs.Remove(code);
                return;
            }
            thumbRevision++;
            thumbs[code] = new Thumbnail
            {
                Seq = seq,
                Rgba = rgba,
                Width = w,
                Height = h,
                Revision = thumbRevision,
            };
        }

        // ?gamebrowser: inject a fixed set of fake games so the carousel can be screenshotted
        // with no server and no WebRTC. Active is left FALSE (no socket), so Tick is inert.
        //
        // withUnmappedArt (?gamebrowser=fallback) appends the two entries no appearance shot
        // wants -- see below. Two rigs, one flag, because they share this whole boot path and
        // differ only in these two rows.
        public static void InjectFakeGames(bool withUnmappedArt, bool withThumbnails)
        {
            games.Clear();
            pingByCode.Clear();
            ClearThumbnails();
            // Players deliberately SPAN 1..MaxPlayers-1: a listed game is any game with a free
            // seat (card 4d904410), so a couch host advertises 2 or 3 taken, and this flag is
            // the only rig that ever screenshots that column. All-1 entries hid the hard-coded
            // "/2" denominator this card fixed (48ab9b2f) -- keep them varied.
            AddFake("QX7KP", (int)Levels.Level1, 1, 1, 34, 41);
            AddFake("B29MT", (int)Levels.Level2, 2, 2, 120, 88);
            AddFake("Z4HRW", (int)Levels.Level3, 3, Oracle.MaxPlayers - 1, 7, 152);
            AddFake("KP8FN", (int)Levels.ClassicAliens, 0, 1, 260, -1);
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
                AddFake("TU7OR", (int)Levels.Tutorial, 1, 1, 12, 22);
                // Card 88f87ba2: this row's DIFFICULTY is out of range too (7, against an enum
                // that stops at Inzane=4). It is the only offline rig for the difficulty
                // sentinel -- LevelArt.DifficultyName's "?" branch -- which every other fake
                // and every real listing leaves unreached. Kept on the row that is already
                // deliberately unrecognisable so the two unknowns travel together.
                AddFake("FU7UR", 9999, 7, 2, 55, 190);
            }
            // ?gamebrowser=thumbs (card e7404647): give SOME entries a live thumbnail and leave
            // the rest on stock art, so one screenshot shows both halves of the rule the
            // carousel implements -- prefer the picture, fall back to the art. Two of four, and
            // deliberately not the first, so a fallback that silently drew a thumbnail (or the
            // reverse) is visible rather than plausible.
            if (withThumbnails)
            {
                InstallFakeThumbnail("B29MT");
                InstallFakeThumbnail("KP8FN");
            }
            Version++;
        }

        // A synthetic 200x150 thumbnail, installed through the REAL SetThumbnail path so the
        // offline rig exercises the same store, staleness and draw code the wire does. The
        // pattern is derived from the code string, so each fake room is visibly distinct and any
        // two runs produce identical pixels (a screenshot rig has to be comparable frame to
        // frame). Not a captured game frame: this rig runs at the MENU, where there is no level
        // to capture -- eaRoomShot.inject() is the seam that installs a real one.
        internal static void InstallFakeThumbnail(string code)
        {
            int w = NetRoomShot.Width;
            int h = NetRoomShot.Height;
            int hash = 0;
            foreach (char c in code)
            {
                hash = hash * 31 + c;
            }
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    rgba[o] = (byte)((x * 255 / w) ^ (hash & 0x3F));
                    rgba[o + 1] = (byte)(y * 255 / h);
                    rgba[o + 2] = (byte)((hash >> 3) & 0xFF);
                    // A bright diagonal, so a thumbnail drawn at the wrong scale, rotated or
                    // flipped is obvious in a screenshot rather than merely "a coloured square".
                    if (Math.Abs(x * h - y * w) < w * 2)
                    {
                        rgba[o] = 255;
                        rgba[o + 1] = 255;
                        rgba[o + 2] = 255;
                    }
                    rgba[o + 3] = 255;
                }
            }
            SetThumbnail(code, 1, rgba, w, h);
        }

        private static void AddFake(string code, int level, int difficulty, int players, int ageSec, int ping)
        {
            games.Add(new GameEntry
            {
                Code = code,
                Level = level,
                Difficulty = difficulty,
                Players = players,
                AgeSec = ageSec,
                PingMs = ping,
            });
        }

        // What the carousel would actually draw, as text (eaGameBrowserShots / `eval
        // GameBrowserShots`). The ONLY observable for the thumbnail half that is not a
        // screenshot: a room drawing stock art because its thumbnail never installed looks
        // exactly like a room that legitimately has none, and no picture can tell them apart.
        // Per code so a thumbnail landing on the WRONG row is visible too.
        internal static string ThumbReport()
        {
            string codes = "";
            foreach (GameEntry g in games)
            {
                string state = thumbs.TryGetValue(g.Code, out Thumbnail t)
                    ? t.Width + "x" + t.Height + "@" + t.Seq
                    : "stock";
                codes = codes.Length == 0 ? g.Code + ":" + state : codes + "," + g.Code + ":" + state;
            }
            return "[gamebrowser] entries=" + games.Count + " thumbs=" + thumbs.Count
                + " inflight=" + shotInFlight.Count + " codes=" + codes;
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
            while (shotQueue.Count > 0)
            {
                (string code, int seq, byte[] rgba, int w, int h) = shotQueue.Dequeue();
                shotInFlight.Remove(code);
                if (seq > 0 && rgba != null)
                {
                    SetThumbnail(code, seq, rgba, w, h);
                }
                else
                {
                    // The server had nothing (or the picture failed to decode). Forget any older
                    // one rather than keeping it: the listing said seq>0, so a picture we cannot
                    // fetch is one the server has dropped.
                    thumbs.Remove(code);
                }
            }
            RequestMissingShots();
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

        // Fetch the thumbnail for every listed game whose stored picture we do not already hold
        // at the server's current sequence number (card e7404647).
        //
        // THE SEQ GATE IS ALSO THE COMPATIBILITY GATE. An older signaling server never emits
        // `shot` in a listing entry, so ShotSeq is 0 there and no shotget is ever sent -- which
        // matters, because that server would answer the unknown frame with an `error`, and the
        // browse socket treats any error as a failed browse. Never fetch on anything but a
        // server-advertised sequence.
        private static void RequestMissingShots()
        {
            foreach (GameEntry g in games)
            {
                if (g.ShotSeq <= 0 || g.ShotAgeSec > StaleAfterSec)
                {
                    continue;
                }
                if (thumbs.TryGetValue(g.Code, out Thumbnail have) && have.Seq == g.ShotSeq)
                {
                    continue;
                }
                if (shotInFlight.Add(g.Code))
                {
                    WebRtcInterop.ShotGet(g.Code);
                }
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
                            // FLOORED at 0, deliberately NOT capped at MaxPlayers. It is drawn
                            // as "N/<MaxPlayers>" and nothing branches on it, so the only
                            // question is what reads most honestly: capping 99 to 4 would make
                            // a nonsense row look like a genuinely FULL game and get skipped
                            // over, where "99/4" is visibly absurd. A negative has no such
                            // reading and is just wrong, so it goes.
                            Players = Math.Max(GetInt(el, "players"), 0),
                            AgeSec = GetInt(el, "ageSec"),
                            PingMs = pingByCode.TryGetValue(code, out int p) ? p : -1,
                            // Absent on an older server -> 0 -> no thumbnail is ever requested
                            // and the carousel draws stock art, exactly as it did before.
                            ShotSeq = GetInt(el, "shot"),
                            ShotAgeSec = GetInt(el, "shotAge"),
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
            ForgetDepartedThumbnails();
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

        // Drop thumbnails (and in-flight fetches) for rooms no longer in the listing -- same
        // unbounded-growth argument as the ping map below, but with ~120 KB per entry rather
        // than an int, so it is the one that actually matters.
        private static void ForgetDepartedThumbnails()
        {
            if (thumbs.Count == 0 && shotInFlight.Count == 0)
            {
                return;
            }
            var live = new HashSet<string>();
            foreach (GameEntry g in games)
            {
                live.Add(g.Code);
            }
            var stale = new List<string>();
            foreach (string k in thumbs.Keys)
            {
                if (!live.Contains(k))
                {
                    stale.Add(k);
                }
            }
            foreach (string k in stale)
            {
                thumbs.Remove(k);
            }
            stale.Clear();
            foreach (string k in shotInFlight)
            {
                if (!live.Contains(k))
                {
                    stale.Add(k);
                }
            }
            foreach (string k in stale)
            {
                shotInFlight.Remove(k);
            }
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
