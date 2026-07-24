// ---------------------------------------------------------------------------
// HeadlessJsRuntime — a fake browser for the Compat/*Interop.cs layer.
//
// WHY THIS AND NOT 13 STUB CLASSES:
// Every browser-facing shim (MusicInterop, SaveInterop, WebcamInterop, DebugInput,
// LoadProfiler, NetInterop, WebRtcInterop, ...) is *mostly pure C#* wrapped around a
// thin `_js.Invoke("eaSomething", ...)` seam. Replacing those classes with desktop
// stubs would fork exactly the logic worth testing — WebcamInterop.HitCircle /
// AvoidanceVector, LoadProfiler's preload manifests, and above all DebugInput, which
// IS the agent-facing command surface (eaPress / eaAiBench / eaTexProbe / eaTeamSeat).
// A fork would rot the moment either side changed.
//
// So instead the seam is shimmed one level DOWN, at IJSRuntime itself. Microsoft.JSInterop
// is a plain netstandard package with nothing WASM-specific in it, so every Compat file
// compiles here UNCHANGED and this class answers the ~37 `ea*` calls a browser would.
// Net effect: zero edits to shipped code, and the headless host exercises the real shims.
//
// The contract mirrors wwwroot/index.html's JS facade. Keep the two in sync: a new
// `ea*` function there wants a case here, otherwise it lands in the unknown-call log
// (visible with --verbose) and returns default(T) — which is the *safe* failure mode,
// since every caller already handles "JS unavailable" (that is the `_js == null` path
// they all guard). Nothing here should ever throw into game code.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Headless
{
    internal sealed class HeadlessJsRuntime : IJSInProcessRuntime
    {
        private readonly HeadlessSaveStore _saves;
        private readonly string _debugQuery;
        private readonly bool _verbose;

        // Distinct unrecognised identifiers, so an unmapped `ea*` is reported ONCE
        // rather than every frame (eaCursor.set alone would flood a run).
        private readonly HashSet<string> _unknown = new HashSet<string>(StringComparer.Ordinal);

        // Every call, counted. Dumped by --jscalls; this is how you confirm the game
        // actually reached a browser seam (e.g. that a level really did request music).
        private readonly Dictionary<string, int> _calls = new Dictionary<string, int>(StringComparer.Ordinal);

        internal HeadlessJsRuntime(HeadlessSaveStore saves, string debugQuery, bool verbose)
        {
            _saves = saves;
            _debugQuery = debugQuery ?? "";
            _verbose = verbose;
        }

        internal IReadOnlyDictionary<string, int> Calls => _calls;

        // ---- IJSInProcessRuntime -------------------------------------------------------

        public TResult Invoke<TResult>(string identifier, params object[] args)
        {
            object result = Dispatch(identifier, args ?? Array.Empty<object>());
            if (result is TResult typed)
                return typed;
            // InvokeVoid() lands here as Invoke<IJSVoidResult>; so does any handler that
            // has nothing to return. default(T) is exactly what the callers treat as
            // "the browser didn't answer", which is the truth in a headless run.
            return default;
        }

        // ---- IJSRuntime ----------------------------------------------------------------
        // The game is synchronous everywhere (it must be — the WASM build runs on the
        // browser main thread), so these just complete inline off the same dispatcher.

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
            => new ValueTask<TValue>(Invoke<TValue>(identifier, args));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args)
            => new ValueTask<TValue>(Invoke<TValue>(identifier, args));

        // ---- the fake browser ----------------------------------------------------------

        private object Dispatch(string identifier, object[] args)
        {
            _calls.TryGetValue(identifier, out int n);
            _calls[identifier] = n + 1;

            switch (identifier)
            {
                // --- boot ---------------------------------------------------------------
                // The URL query the browser would hand DebugFlags.Parse. Here it comes
                // from --flags, so `--flags "?level=Level3&brainboss&invuln"` is literally
                // the same string a dev would put in the address bar.
                case "getDebugQuery":
                    return _debugQuery;

                // --- saves (index.html eaSave: localStorage + IndexedDB) ------------------
                case "eaSave.load":
                    return _saves.LoadAllAsJson();
                case "eaSave.set":
                    return _saves.Set(Str(args, 0), Str(args, 1));
                case "eaSave.remove":
                    _saves.Remove(Str(args, 0));
                    return null;

                // --- online co-op ---------------------------------------------------------
                // No signaling server and no WebRTC headlessly. 'dev' matches what a local
                // (non-CI) browser build reports, so the peers-run-identical-binary check
                // behaves the same; the peer id is stable per run so kick/block logic that
                // reads it sees a consistent identity.
                case "eaRtc.buildHash":
                    return "dev";
                case "eaRtc.peerId":
                    return _peerId;

                // --- webcam ---------------------------------------------------------------
                // "" makes GetOverlayPixels return false, i.e. the documented
                // no-overlay fallback to the plain game frame.
                case "eaWebcam.overlayPixels":
                    return "";

                // --- load profiler --------------------------------------------------------
                case "eaLoadProfile.load":
                    return _loadProfile ?? "";
                case "eaLoadProfile.save":
                    _loadProfile = Str(args, 0);
                    return null;
                case "eaLoadProfile.download":
                    // The browser triggers a file download; write it next to the outputs
                    // instead so a headless run can still collect the artefact.
                    WriteDownload(Str(args, 0), Str(args, 1));
                    return null;
            }

            // Everything else is a browser side effect with no headless meaning: audio
            // (eaMusic.*), the DOM cursor (eaCursor.*), fullscreen, the trailer iframe,
            // the texture-viewer overlay, the webcam session, and the whole WebRTC/
            // signaling surface. Silently accepted — see the class comment.
            if (IsKnownVoid(identifier))
                return null;

            if (_unknown.Add(identifier) && _verbose)
                Console.WriteLine("[js] unhandled '" + identifier + "' -> default (add a case if it needs a value)");
            return null;
        }

        // Identifiers deliberately treated as no-ops. Listed explicitly rather than
        // catch-all'd so a genuinely NEW `ea*` function still shows up in --verbose
        // instead of being silently swallowed.
        private static bool IsKnownVoid(string id)
        {
            switch (id)
            {
                case "eaMusic.play":
                case "eaMusic.stop":
                case "eaMusic.setMuffle":
                case "eaMusic.setRate":
                case "eaCursor.set":
                case "eaFullscreen.set":
                case "eaQuit":
                case "eaTrailer":
                case "eaTexViewer.show":
                case "eaTexViewer.hide":
                case "eaWcTune.show":
                case "eaWcTune.hide":
                case "eaWebcam.begin":
                case "eaWebcam.stop":
                case "eaNet.open":
                case "eaNet.send":
                case "eaNet.close":
                case "eaRtc.host":
                case "eaRtc.join":
                case "eaRtc.send":
                case "eaRtc.close":
                case "eaRtc.list":
                case "eaRtc.relist":
                case "eaRtc.unlist":
                case "eaRtc.endListing":
                case "eaRtc.browse":
                case "eaRtc.endBrowse":
                case "eaRtc.promptCode":
                case "eaRtc.closePrompt":
                    return true;
                default:
                    return false;
            }
        }

        private static string Str(object[] args, int i)
            => (i < args.Length && args[i] != null) ? args[i].ToString() : null;

        private string _loadProfile;

        // Stable for the process; matches the browser's random-per-profile localStorage token.
        private readonly string _peerId = Guid.NewGuid().ToString("N").Substring(0, 16);

        internal string DownloadDir;

        private void WriteDownload(string name, string text)
        {
            if (string.IsNullOrEmpty(DownloadDir) || string.IsNullOrEmpty(name))
                return;
            try
            {
                Directory.CreateDirectory(DownloadDir);
                File.WriteAllText(Path.Combine(DownloadDir, Path.GetFileName(name)), text ?? "", Encoding.UTF8);
                Console.WriteLine("[js] download -> " + Path.Combine(DownloadDir, Path.GetFileName(name)));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[js] download '" + name + "' failed: " + ex.Message);
            }
        }
    }

    // -----------------------------------------------------------------------------------
    // HeadlessSaveStore — stands in for index.html's eaSave facade (localStorage for the
    // small XML saves, IndexedDB for the `.dat` screenshot blobs).
    //
    // Backed by a directory of base64 files. The DEFAULT is a throwaway temp dir wiped at
    // startup, because a test rig must boot from a known state: a leftover save would
    // silently change unlock state, difficulty and the attract flow between runs. Pass
    // --saves <dir> to keep a persistent profile when that is the point of the test.
    // -----------------------------------------------------------------------------------
    internal sealed class HeadlessSaveStore
    {
        private readonly Dictionary<string, string> _entries = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string _dir;

        internal HeadlessSaveStore(string dir, bool wipe)
        {
            _dir = dir;
            try
            {
                if (wipe && Directory.Exists(_dir))
                    Directory.Delete(_dir, true);
                Directory.CreateDirectory(_dir);
                foreach (string f in Directory.GetFiles(_dir, "*.b64"))
                {
                    // The name is URL-escaped on the way out so nested asset-ish names
                    // ("screenshots/level1.dat") survive a flat directory.
                    _entries[Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(f))] = File.ReadAllText(f);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[save] store init failed (" + ex.Message + ") — running in memory only");
            }
        }

        internal string LoadAllAsJson()
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in _entries)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(JsonSerializer.Serialize(kv.Key)).Append(':').Append(JsonSerializer.Serialize(kv.Value));
            }
            return sb.Append('}').ToString();
        }

        internal bool Set(string name, string b64)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            _entries[name] = b64 ?? "";
            try
            {
                File.WriteAllText(PathFor(name), _entries[name]);
                return true;
            }
            catch (Exception)
            {
                // Mirrors the browser's quota-exceeded case: the caller retries next sync.
                return false;
            }
        }

        internal void Remove(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            _entries.Remove(name);
            try { File.Delete(PathFor(name)); } catch (Exception) { }
        }

        private string PathFor(string name) => Path.Combine(_dir, Uri.EscapeDataString(name) + ".b64");
    }
}
