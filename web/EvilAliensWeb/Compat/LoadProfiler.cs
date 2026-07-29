// ---------------------------------------------------------------------------
// LoadProfiler — debug-only texture-load profiler + self-improving preload list.
//
// WHY: textures are decoded from PNG by StbImageSharp (managed, on the WASM main
// thread) inside Texture2D.FromStream — see WebContentManager.LoadTexture. The big
// HD sheets are multi-megapixel, so a *cold* decode that lands during gameplay
// (rather than on the level's loading screen) is a visible frame hitch. Each
// GameScene preloads a hardcoded list (PreloadGraphicalContent); anything an enemy
// uses that ISN'T in that list pays the decode on first spawn = stutter.
//
// WHAT: with the ?loadlog flag set, this records every texture decode — which
// level was active, how long FromStream took, the pixel size, and crucially
// whether it loaded during the preload phase (fine) or during live gameplay (a
// "cold" miss, the thing we hunt). It accumulates the result in browser
// localStorage so it survives reloads, and the level preloader feeds that set
// back in (ApplyManifest) so a level you've already visited stops stuttering on
// the next visit — it improves itself as you test.
//
// To make a discovery permanent for ALL players, call eaPreloadExport() in the
// browser console (or `eval PreloadExport` under tools/headless): it produces a
// plain-text manifest whose per-level sections you MERGE into
// wwwroot/Content/preload/manifest.txt and commit. Merge, never overwrite — the
// export contains only what THIS session recorded, so it omits every level that
// was not played and (the content manager being shared) every asset an earlier
// level in the same session already cached. That shipped file is read by
// EVERY build at preload time (release included) — but release NEVER records or
// writes (RecordTexture / Persist are no-ops unless ?loadlog), so a shipped build
// cannot append to the list. Mirrors the SaveInterop / eaSave localStorage pattern.
//
// Manifest line format (lines starting with '#' are comments, blanks ignored):
//   <Level>|<asset-path>[|COLD|<ms>|<WxH>]
// The shipped reader only needs <Level>|<asset-path>; the trailing fields are
// captured so the export / localStorage round-trip preserves ms + the cold flag.
// <asset-path> is the wwwroot/Content-relative, lowercased path with no extension
// (e.g. "gfx/sprites/spider_sheet2"), which the ContentManager re-resolves 1:1.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EvilAliens;                       // ServiceHelper, IContentManagerService
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;          // TitleContainer
using Microsoft.Xna.Framework.Content;  // ContentManager
using Microsoft.Xna.Framework.Graphics; // Texture2D

namespace EvilAliensWeb.Compat
{
    public static class LoadProfiler
    {
        private sealed class Stat
        {
            public int MaxMs;       // worst decode time seen for this asset
            public int W, H;        // pixel dimensions (cost driver)
            public bool Cold;       // decoded outside the preload phase at least once
        }

        private const string SentinelBoot = "(boot)";   // before any level is entered
        private const string ManifestPath = "Content/preload/manifest.txt";

        private static IJSInProcessRuntime _js;

        // level -> (assetId -> stat).
        private static readonly Dictionary<string, Dictionary<string, Stat>> _byLevel =
            new Dictionary<string, Dictionary<string, Stat>>();

        // Shipped, read-only manifest (Content/preload/manifest.txt), parsed once.
        private static Dictionary<string, List<string>> _shipped;

        private static string _currentLevel = SentinelBoot;
        private static bool _preloadActive;

        // True while a decode is happening ON PURPOSE. BeginWarm/EndWarm bracket Game1.Warm<T>,
        // which every queued lambda funnels through -- QueueMenuWarm's,
        // QueueIdleWarm's AND the pre-launch levelWarmQueue's. The level warm is additionally
        // inside a BeginPreload/EndPreload bracket, and _preloadActive is tested FIRST below, so
        // its decodes keep counting as preloads and still reach the per-level summary.
        // Without it every one of those ~34 boot decodes reported itself as a COLD gap -- the
        // menu warm doing exactly its job, logged as the thing it exists to prevent -- which
        // made the (boot) block unreadable and cost card 4d47c5ba a wrong asset list.
        //
        // Deliberately a BRACKET, not a blanket "_currentLevel == SentinelBoot" mute like the
        // one NoteFrame uses: the boot decodes that are NOT warm-queue driven (the splash art,
        // cursor2, awardmentblade -- all loaded from component LoadContent BEFORE Game1's own
        // LoadContent builds the queues, so no warm entry can ever reach them) are the real
        // signal in that block, and muting them would delete the evidence this card was filed
        // on.
        //
        // ScreenshotSaver.Init holds the SECOND bracket (card 2367b39c), labelled, across a
        // twelve-Load loop -- so "one asset per bracket" is no longer the non-reentrancy
        // argument. The real one: the two call sites cannot reach each other. Warm<T> runs from
        // Game1's queue pump/drain and Init from StartScreen.Update, both on the one game thread,
        // and neither a content Load nor anything it calls re-enters either. See BeginWarm.
        private static bool _warmActive;

        // Set by the LABELLED BeginWarm overload only (ScreenshotSaver.Init's stock-shot loop).
        // Non-null => EndWarm prints a one-line summary naming this bracket, instead of the
        // decodes vanishing without trace. Warm<T> passes no label, so the three warm queues
        // are byte-for-byte unchanged: one asset per bracket has nothing worth summarising.
        private static string _warmLabel;

        // Per-labelled-warm running totals, for that summary. Same shape as the preload set below.
        private static int _warmCount, _warmMs, _warmMaxMs;
        private static string _warmSlowest;

        // Per-preload running totals, for the one-line summary on EndPreload.
        private static int _preloadCount, _preloadMs, _preloadMaxMs;
        private static string _preloadSlowest;

        // Frame-hitch watchdog (NoteFrame). A tick this long is a visible freeze — a cold
        // texture decode, a GC pause, a shader compile, whatever. ALWAYS-ON (independent of
        // ?loadlog) so a "the game froze here" report has a number + the active level even in
        // a shipped build; the cost is one comparison per frame.
        private const double HitchMs = 120;     // ~7 dropped frames at 60fps
        private static bool _wasHitch;           // edge-detect: log only a hitch spike's first frame
        private static bool _sawPreloadThisTick; // a level's whole LoadContent preload runs inside ONE
                                                 // tick (the loading screen) — don't flag that tick

        // Recording / persisting is debug-only; reading the shipped manifest is not.
        private static bool Recording => DebugFlags.LoadLog;

        // Called once from Index.razor.cs after the JS runtime is available.
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
            if (!Recording)
                return;
            Hydrate();
            Console.WriteLine("[loadprofile] ON — recording texture decodes. Play through levels, "
                + "then run eaPreloadExport() in the console to download Content/preload/manifest.txt. "
                + "Cold (non-preloaded) decodes are logged below as they happen.");
        }

        // Called by WebContentManager.LoadTexture for every texture decode. `key` is
        // the resolved "Content/<lower-path>" used to open the .png; msD is the
        // Texture2D.FromStream wall time.
        public static void RecordTexture(string key, double msD, int w, int h)
        {
            if (!Recording)
                return;
            // A boot warm that LEAKS INTO A LEVEL is recorded against nothing. The idle queue
            // drains one-per-tick and a ?level= boot into a non-space level never calls
            // SetSpace, so its leftovers trickle-decode during early gameplay -- and recording
            // them there is how eaPreloadExport bakes the space tile set into a non-space
            // level's manifest section (a hazard Game1.QueueIdleWarm has warned about in prose
            // since card 97727578; the COLD lines that used to make it visible are, correctly,
            // no longer printed). Warms under the (boot) sentinel record as normal.
            //
            // `!_preloadActive` is what keeps this narrow, and it is NOT optional: the
            // pre-launch levelWarmQueue also runs through Game1.Warm<T>, so without it this
            // would drop every entry of the level warm -- i.e. exactly the per-level manifest
            // set eaPreloadExport exists to capture. Measured: a ?level=Level2&loadlog capture
            // came back with ZERO Level2 entries.
            if (_warmActive && !_preloadActive && _currentLevel != SentinelBoot)
                return;
            string assetId = key.StartsWith("Content/", StringComparison.Ordinal)
                ? key.Substring("Content/".Length)
                : key;
            int ms = (int)Math.Round(msD);

            if (!_byLevel.TryGetValue(_currentLevel, out var lvl))
                _byLevel[_currentLevel] = lvl = new Dictionary<string, Stat>();
            if (!lvl.TryGetValue(assetId, out var st))
                lvl[assetId] = st = new Stat();
            if (ms > st.MaxMs) st.MaxMs = ms;
            st.W = w; st.H = h;

            if (_preloadActive)
            {
                _preloadCount++;
                _preloadMs += ms;
                if (ms > _preloadMaxMs) { _preloadMaxMs = ms; _preloadSlowest = assetId; }
            }
            else if (_warmActive)
            {
                // A deliberate boot warm: not a gap, because nothing is on screen waiting for
                // it. Recorded above under the (boot) sentinel so the export keeps the asset and
                // its size, but never flagged Cold and never logged. Not folded into the preload
                // counters either -- those summarise one level's bracket, and a warm decode
                // belongs to no level.
                //
                // A LABELLED bracket gets its own totals so EndWarm can print one summary line.
                // Unlabelled (Warm<T>) brackets accumulate too -- harmless, three int stores and
                // at most one string reference -- but never print, so the queues' output is
                // unchanged. Note ms is rounded per asset, so a fast run can total 0ms honestly.
                _warmCount++;
                _warmMs += ms;
                if (ms > _warmMaxMs) { _warmMaxMs = ms; _warmSlowest = assetId; }
            }
            else
            {
                // Cold decode during live gameplay/menu — the stutter we hunt for.
                if (!st.Cold)
                {
                    st.Cold = true;
                    Persist();   // a freshly-found gap — save now so a crash keeps it
                }
                Console.WriteLine($"[loadprofile] COLD decode in {_currentLevel}: {assetId} {ms}ms "
                    + $"{w}x{h} — not preloaded (add to PreloadGraphicalContent, or run eaPreloadExport)");
            }
        }

        // Frame-hitch watchdog. Called once per tick from Index.razor.cs TickDotNet with the
        // wall time the whole Game.Tick() (Update+Draw) took. Logs the LEADING frame of each
        // hitch spike (edge-detected — a single long decode = one line, and a persistently slow
        // scene doesn't spam). Skips the preload phase (the loading screen is meant to be slow)
        // and the boot/menu warm-up (Game1.QueueMenuWarm deliberately decodes there). Always-on,
        // so it complements ?loadlog: ?loadlog attributes the texture; this catches ANY long
        // tick (incl. non-texture hangs ?loadlog can't see). Note: _currentLevel persists past a
        // level (see BeginPreload), so a hitch on the menu AFTER playing carries the last level's
        // name — fine for a "froze here" report, just don't read the level name as authoritative.
        public static void NoteFrame(double ms)
        {
            // The level-load tick (BeginPreload..EndPreload all run within it) is the loading
            // screen, not a gameplay freeze — _preloadActive is already back to false by the time
            // this runs at tick-end, so a one-shot flag set in EndPreload excuses exactly that tick.
            if (_sawPreloadThisTick)
            {
                _sawPreloadThisTick = false;
                _wasHitch = false;
                return;
            }
            if (_preloadActive || _currentLevel == SentinelBoot)
                return;
            bool hitch = ms >= HitchMs;
            if (hitch && !_wasHitch)
                Console.WriteLine($"[hitch] {(int)Math.Round(ms)}ms frame in {_currentLevel}"
                    + (Recording
                        ? " — see the [loadprofile] line above for the asset, if a decode caused it"
                        : " — long tick (texture decode / GC / shader compile?); add ?loadlog to attribute texture decodes"));
            _wasHitch = hitch;
        }

        // Bracket a level's preload phase: loads in [BeginPreload, EndPreload) are
        // "preload" (intended), loads after it for the same level are "cold" misses.
        // _currentLevel persists past EndPreload so in-game spawns still attribute to
        // the level until the next level enters. (Loads on the menu after returning
        // from a level attribute to that level — minor, acceptable noise.)
        public static void BeginPreload(string level)
        {
            _currentLevel = string.IsNullOrEmpty(level) ? SentinelBoot : level;
            _preloadActive = true;
            _wasHitch = false;   // the long preload ticks aren't hitches; start gameplay clean
            _preloadCount = 0; _preloadMs = 0; _preloadMaxMs = 0; _preloadSlowest = null;
        }

        // Bracket deliberate decodes. See _warmActive: inside the bracket a decode is recorded
        // but never flagged Cold and never logged, because there is no frame waiting on it.
        // Cheap enough to leave unguarded by Recording -- a bool, a string reference and four
        // scalar resets, none of which allocate.
        //
        // TWO callers, and exactly two (card 2367b39c):
        //   * Game1.Warm<T>, unlabelled, around ONE queued asset -- all three warm queues funnel
        //     through it. Nothing to summarise per asset, so it prints nothing.
        //   * ScreenshotSaver.Init, labelled "stockshots", around its whole twelve-asset loop.
        //     That loop warms the SAME list Game1.QueueMenuWarm queues, so on a full-splash boot
        //     the pump got there first and these are cache hits -- zero decodes, no summary. On a
        //     splash-SKIPPING boot (?menu / ?skipsplash / ?autostart, or a player double-tapping
        //     past the splash inside ~24 ticks) the pump has not REACHED them yet -- the menu's
        //     own twelve are queued ahead of them -- so they really do decode here. They
        //     used to report as twelve COLD gaps sitting at the top of every ?loadlog capture,
        //     which is what this label replaces with one line.
        //
        // INTERNAL on purpose, unlike the rest of this class's surface. A caller that opens the
        // bracket and never closes it (a missing finally, an early return between the two) mutes
        // every COLD line for the rest of the session -- the diagnostic this class exists to
        // provide, failing closed and silently. Keeping the pair unreachable from outside the
        // assembly keeps that two-caller claim checkable by grep, and BOTH call sites close in a
        // `finally`. A depth counter would not help: an unbalanced Begin leaves it stuck either
        // way. Non-reentrant by construction -- neither call site can be reached from inside the
        // other.
        internal static void BeginWarm(string label = null)
        {
            _warmActive = true;
            _warmLabel = label;
            _warmCount = 0; _warmMs = 0; _warmMaxMs = 0; _warmSlowest = null;
        }

        internal static void EndWarm()
        {
            _warmActive = false;
            string label = _warmLabel;
            _warmLabel = null;
            // Only a LABELLED bracket that actually decoded something reports. Count 0 is the
            // normal full-splash case (the pump already warmed them) and must stay silent, or
            // every boot would gain a noise line saying nothing happened.
            if (!Recording || label == null || _warmCount == 0)
                return;
            // The tail says "not a COLD gap", NOT "free". These decodes are deliberate, so they
            // are not the preload miss a COLD line hunts -- but reaching this line at all means
            // something decoded synchronously here, and for the stockshots bracket that is a real
            // stall on the Press-Start -> menu handoff (Game1.QueueMenuWarm exists to prevent it).
            // So the ms is the number to judge; do not let the prose talk a reader out of it.
            Console.WriteLine($"[loadprofile] {label} warm: {_warmCount} textures, "
                + $"{_warmMs}ms decode total"
                + (_warmSlowest != null ? $" (slowest {_warmSlowest} {_warmMaxMs}ms)" : "")
                + " -- deliberate, not a COLD gap; the ms is still time spent here");
        }

        public static void EndPreload()
        {
            _preloadActive = false;
            _sawPreloadThisTick = true;   // excuse this (load-screen) tick in NoteFrame
            if (!Recording)
                return;
            Persist();
            Console.WriteLine($"[loadprofile] {_currentLevel} preload: {_preloadCount} textures, "
                + $"{_preloadMs}ms decode total"
                + (_preloadSlowest != null ? $" (slowest {_preloadSlowest} {_preloadMaxMs}ms)" : ""));
        }

        // Warm every asset the manifest associates with `level`, so it's hot before
        // gameplay. ALWAYS applies the shipped manifest (Content/preload/manifest.txt
        // — how a release benefits); in debug it ALSO applies the localStorage set,
        // so a gap hit last run is preloaded this run. Each load is isolated: a stale
        // or bad entry logs and is skipped, never breaking the level. Call between
        // PreloadGraphicalContent() and EndPreload() so these count as preloads.
        public static void ApplyManifest(string level)
        {
            if (string.IsNullOrEmpty(level))
                return;
            ContentManager cm;
            try { cm = ServiceHelper.Get<IContentManagerService>().ContentManager; }
            catch { return; }

            // ManifestAssets returns a fresh list (a copy of the keys), so pruning _byLevel in the
            // catch below can't invalidate this loop.
            foreach (string id in ManifestAssets(level))
            {
                try { cm.Load<Texture2D>(id); }      // cache-deduped against the code list
                catch (Exception ex)
                {
                    Console.WriteLine($"[loadprofile] manifest entry '{id}' for {level} failed: {ex.Message}");
                    PruneLearnedEntry(level, id);
                }
            }
        }

        // Self-heal the localStorage-learned set: when an entry no longer loads (its asset was
        // deleted since it was recorded — e.g. 756-v1-side after the wall-tower rewrite), drop it
        // and re-persist, so it stops being retried (and log-spamming) every load. Debug-only, since
        // only ?loadlog records/applies the learned set. A SHIPPED (committed manifest) entry that
        // fails is a real gap to fix in the file, not to hide, so those are left to surface.
        private static void PruneLearnedEntry(string level, string id)
        {
            if (!Recording)
                return;
            if (Shipped().TryGetValue(level, out var shippedIds) && shippedIds.Contains(id))
                return;
            if (_byLevel.TryGetValue(level, out var lvl) && lvl.Remove(id))
            {
                Console.WriteLine($"[loadprofile] dropped stale learned entry '{id}' for {level} (asset no longer loads)");
                Persist();
            }
        }

        // The asset ids the manifest associates with `level`: ALWAYS the shipped manifest,
        // plus (debug ?loadlog only) the localStorage-learned set, so a gap hit last run is
        // covered this run. Feeds ApplyManifest above AND Game1's pre-launch level warm
        // (card fe25712a), which decodes these one-per-tick BEFORE the level component is
        // added, so the level's own synchronous preload becomes cache hits and the browser
        // event loop keeps breathing (no Chrome "page unresponsive" popup).
        public static List<string> ManifestAssets(string level)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(level))
                return ids;
            if (Shipped().TryGetValue(level, out var shippedIds))
                ids.AddRange(shippedIds);
            if (Recording && _byLevel.TryGetValue(level, out var lvl))
                ids.AddRange(lvl.Keys);
            return ids;
        }

        // Build + download the accumulated manifest (and echo it to the console).
        // Exposed to the browser console as window.eaPreloadExport().
        [JSInvokable]
        public static string ExportPreloadManifest()
        {
            string text = Serialize();
            Console.WriteLine($"[loadprofile] manifest — {CountAssets()} assets across {_byLevel.Count} "
                + "level(s). MERGE the captured sections into wwwroot/Content/preload/manifest.txt "
                + "(never overwrite it — this is only what this session recorded):\n" + text);
            try { _js?.InvokeVoid("eaLoadProfile.download", "preload_manifest.txt", text); }
            catch (Exception ex) { Console.WriteLine("[loadprofile] download failed: " + ex.Message); }
            return text;
        }

        // ---- internals -----------------------------------------------------

        private static void Persist()
        {
            if (_js == null)
                return;
            try { _js.InvokeVoid("eaLoadProfile.save", Serialize()); }
            catch { /* quota / disabled storage — non-fatal, retried next time */ }
        }

        private static void Hydrate()
        {
            if (_js == null)
                return;
            string text;
            try { text = _js.Invoke<string>("eaLoadProfile.load"); }
            catch { return; }
            ParseInto(text, _byLevel);
        }

        private static Dictionary<string, List<string>> Shipped()
        {
            if (_shipped != null)
                return _shipped;
            _shipped = new Dictionary<string, List<string>>();
            string text;
            try
            {
                using Stream s = TitleContainer.OpenStream(ManifestPath);
                using var r = new StreamReader(s);
                text = r.ReadToEnd();
            }
            catch
            {
                // No committed manifest yet — fine; levels just rely on their code lists.
                return _shipped;
            }
            foreach (string line in text.Split('\n'))
            {
                if (!ParseLine(line, out string level, out string id, out _, out _, out _, out _))
                    continue;
                if (!_shipped.TryGetValue(level, out var list))
                    _shipped[level] = list = new List<string>();
                list.Add(id);
            }
            return _shipped;
        }

        private static void ParseInto(string text, Dictionary<string, Dictionary<string, Stat>> dest)
        {
            if (string.IsNullOrEmpty(text))
                return;
            foreach (string line in text.Split('\n'))
            {
                if (!ParseLine(line, out string level, out string id, out bool cold, out int ms, out int w, out int h))
                    continue;
                if (!dest.TryGetValue(level, out var lvl))
                    dest[level] = lvl = new Dictionary<string, Stat>();
                if (!lvl.TryGetValue(id, out var st))
                    lvl[id] = st = new Stat();
                if (ms > st.MaxMs) st.MaxMs = ms;
                if (w > 0) st.W = w;
                if (h > 0) st.H = h;
                st.Cold |= cold;
            }
        }

        private static bool ParseLine(string raw, out string level, out string id,
            out bool cold, out int ms, out int w, out int h)
        {
            level = id = null; cold = false; ms = 0; w = 0; h = 0;
            if (raw == null)
                return false;
            string line = raw.Trim();          // also strips a trailing '\r' from CRLF
            if (line.Length == 0 || line[0] == '#')
                return false;
            string[] p = line.Split('|');
            if (p.Length < 2)
                return false;
            level = p[0].Trim();
            id = p[1].Trim().ToLowerInvariant();
            if (level.Length == 0 || id.Length == 0)
                return false;
            if (p.Length >= 3)
                cold = p[2].Trim().Equals("COLD", StringComparison.OrdinalIgnoreCase);
            if (p.Length >= 4)
                int.TryParse(p[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            if (p.Length >= 5)
            {
                string[] d = p[4].Trim().ToLowerInvariant().Split('x');
                if (d.Length == 2)
                {
                    int.TryParse(d[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out w);
                    int.TryParse(d[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out h);
                }
            }
            return true;
        }

        private static string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Evil Aliens preload manifest — generated by LoadProfiler / eaPreloadExport().");
            sb.AppendLine("# MERGE the sections you captured into wwwroot/Content/preload/manifest.txt —");
            sb.AppendLine("# do NOT overwrite that file with this one. This is only what THIS session");
            sb.AppendLine("# recorded, so it omits every level you did not play, and (because the content");
            sb.AppendLine("# manager is shared) every asset an earlier level in this session already");
            sb.AppendLine("# cached. Every build preloads these per level on top of PreloadGraphicalContent().");
            sb.AppendLine("# Format: <Level>|<asset-path>[|COLD|<ms>|<WxH>]   (# = comment)");
            sb.AppendLine("# COLD = was decoded during live gameplay (a stutter) at least once.");

            var levels = new List<string>(_byLevel.Keys);
            levels.Sort(StringComparer.Ordinal);
            foreach (string level in levels)
            {
                var lvl = _byLevel[level];
                sb.AppendLine();
                sb.AppendLine("# " + level);
                var ids = new List<string>(lvl.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    Stat st = lvl[id];
                    sb.Append(level).Append('|').Append(id)
                      .Append('|').Append(st.Cold ? "COLD" : "WARM")
                      .Append('|').Append(st.MaxMs.ToString(CultureInfo.InvariantCulture))
                      .Append('|').Append(st.W.ToString(CultureInfo.InvariantCulture))
                      .Append('x').Append(st.H.ToString(CultureInfo.InvariantCulture))
                      .AppendLine();
                }
            }
            return sb.ToString();
        }

        private static int CountAssets()
        {
            int n = 0;
            foreach (var kv in _byLevel)
                n += kv.Value.Count;
            return n;
        }
    }
}
