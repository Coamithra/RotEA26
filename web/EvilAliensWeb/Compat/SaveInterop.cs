// ---------------------------------------------------------------------------
// SaveInterop — thin C# -> JS bridge to browser save storage (Stage 7).
//
// The game saves to the in-memory WASM filesystem (see StorageStub.cs): settings,
// unlockables, awardments (Achievements.xml) and the level-select screenshots all
// land under StorageDevice.Root + "EvilAliens/" -- in the browser /eaweb_save/, which
// is what this file is about (a desktop host repoints it; see StorageStub). That tree
// is lost on reload, so we mirror it
// to the browser: the StorageStub hydrates MEMFS from here before the first read
// and flushes changed files back here on every container Dispose.
//
// Mirrors the Stage-6 MusicInterop/eaMusic pattern. Keys are paths relative to the
// save root (e.g. "EvilAliens/Settings.xml"); values are base64 of the raw file
// bytes (the .dat screenshots are binary). The JS side (window.eaSave in index.html)
// owns the actual access and ROUTES by kind: the small XML uses localStorage, the
// large ".dat" screenshot blobs use IndexedDB (window.eaSaveBlob, a much bigger
// quota). This C# side is backend-agnostic — it just passes name+base64 through.
// The IndexedDB blobs are async, so eaSaveBlob.preload() (awaited by initRenderJS
// before the first game tick) pulls them into memory; Load() then reads them
// synchronously alongside the localStorage XML.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class SaveInterop
    {
        private static IJSInProcessRuntime _js;

        public static bool Available => _js != null;

        // Called once from Index.razor.cs after the JS runtime is available
        // (before the first game tick, so saves are persisted from the start).
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // Reads every persisted entry: relative name -> raw file bytes.
        public static IEnumerable<KeyValuePair<string, byte[]>> Load()
        {
            var result = new List<KeyValuePair<string, byte[]>>();
            if (_js == null)
                return result;
            string json = _js.Invoke<string>("eaSave.load");
            if (string.IsNullOrEmpty(json))
                return result;
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                string b64 = prop.Value.GetString();
                if (b64 == null)
                    continue;
                try
                {
                    result.Add(new KeyValuePair<string, byte[]>(prop.Name, Convert.FromBase64String(b64)));
                }
                catch (FormatException)
                {
                    // corrupt entry — skip it rather than abort the whole hydrate.
                }
            }
            return result;
        }

        // Persists one file. Returns false if it didn't stick (e.g. quota exceeded),
        // so the caller can keep trying on the next sync.
        public static bool Set(string name, byte[] bytes)
        {
            if (_js == null)
                return false;
            return _js.Invoke<bool>("eaSave.set", name, Convert.ToBase64String(bytes));
        }

        public static void Remove(string name)
        {
            _js?.InvokeVoid("eaSave.remove", name);
        }
    }
}
