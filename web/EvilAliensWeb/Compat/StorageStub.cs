// ---------------------------------------------------------------------------
// Stub of Microsoft.Xna.Framework.Storage (XNA 3.x synchronous storage API).
//
// The game uses the 3.x pattern: device.OpenContainer("name") -> StorageContainer,
// then File IO against container.Path. KNI follows the 4.0 async storage API, so
// we replace it entirely. The KNI Storage package is still REFERENCED in the
// .csproj, with ExcludeAssets="compile" -- it ships at runtime (the platform dll
// is linked against it) but the compiler never sees it, so the types below are the
// only StorageDevice/StorageContainer the game compiles against. Do not read that
// reference as this stub being superseded.
//
// Backing store: the WASM in-memory filesystem (MEMFS). System.IO works there in
// a sandbox. Stage 7 makes it PERSISTENT by mirroring the save tree to browser
// localStorage (see PersistentSave + Compat/SaveInterop.cs): hydrate localStorage
// -> MEMFS once before the first read, flush changed files MEMFS -> localStorage on
// every container Dispose. The game's Savable subclasses are untouched.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using EvilAliensWeb.Compat;

namespace Microsoft.Xna.Framework.Storage
{
    public sealed class StorageContainer : IDisposable
    {
        // Trailing separator: the game concatenates "c.Path + \"Settings.xml\"".
        public string Path { get; }

        internal StorageContainer(string path)
        {
            Path = path.EndsWith("/") ? path : path + "/";
            try { Directory.CreateDirectory(Path); } catch { /* MEMFS best effort */ }
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            // Saves are write-container-then-Dispose, so Dispose is the flush point.
            // (Sync skips files whose bytes are unchanged, so read-only opens — e.g.
            // loading a screenshot — cost nothing.)
            PersistentSave.Sync();
        }
    }

    public sealed class StorageDevice
    {
        public static readonly StorageDevice Default = new StorageDevice();

        // The save tree's root. In the BROWSER this is a MEMFS path: per-page, in-memory, and
        // only persistent because PersistentSave mirrors it to localStorage/IndexedDB. On a
        // DESKTOP host (tools/headless) the very same string resolves to a REAL directory
        // (C:\eaweb_save\ on Windows) that outlives the process -- which silently made every
        // eahl run inherit the previous one's saves, `--saves` notwithstanding, since that flag
        // only ever owned the b64 mirror. A ?unlockall probe run therefore left every LATER run
        // with all ten awardments unlocked, and AwardAchievement dropping every award (cards
        // 57555583 / d2f746d5 lost an investigation to exactly that).
        //
        // So it is settable, and the headless host points it inside its own --saves dir: one
        // store, one owner, and the "runs start clean" promise true by construction rather than
        // by hope. Never call SetRoot from game code -- the browser default is correct there.
        internal static string Root { get; private set; } = "/eaweb_save/";

        // Repoint the save tree. MUST be called before the first OpenContainer (the hydrate +
        // every Load runs off it); throws rather than silently splitting the tree in two if a
        // container has already been opened.
        internal static void SetRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("save root must not be empty", nameof(path));
            if (PersistentSave.Hydrated)
                throw new InvalidOperationException(
                    "StorageDevice.SetRoot must be called before the first OpenContainer");
            Root = path.EndsWith("/") || path.EndsWith("\\") ? path : path + "/";
        }

        public bool IsConnected => true;
        public long FreeSpace => long.MaxValue;
        public long TotalSpace => long.MaxValue;

        public StorageContainer OpenContainer(string titleName)
        {
            // Pull persisted saves into MEMFS before the first read (the savables'
            // Load() runs right after the first OpenContainer in StartScreen).
            PersistentSave.EnsureHydrated();
            return new StorageContainer(Root + titleName);
        }
    }

    // Mirrors the save tree (/eaweb_save/**) to the browser so settings, unlockables,
    // awardments and level screenshots survive a page reload. Entries are keyed by path
    // relative to Root (e.g. "EvilAliens/Settings.xml"). Sync only writes files whose
    // bytes changed since the last persist and prunes ones the game deleted (e.g. cleared
    // screenshots). BACKEND SPLIT (routed inside eaSave, index.html): the small XML lives
    // in localStorage; the large ".dat" screenshot blobs live in IndexedDB (big quota) —
    // this C# side is backend-agnostic and drives both through SaveInterop.
    internal static class PersistentSave
    {
        private static bool _hydrated;

        // Read by StorageDevice.SetRoot: repointing the tree after the first hydrate would
        // leave half the saves behind the old root.
        internal static bool Hydrated => _hydrated;

        // Last bytes we persisted, per relative name. Lets Sync skip unchanged files
        // (Dispose fires on read-only opens too) and detect deletions.
        private static readonly Dictionary<string, byte[]> _mirror = new Dictionary<string, byte[]>();

        public static void EnsureHydrated()
        {
            if (_hydrated)
                return;
            _hydrated = true;
            try
            {
                foreach (KeyValuePair<string, byte[]> entry in SaveInterop.Load())
                {
                    string full = StorageDevice.Root + entry.Key;
                    string dir = System.IO.Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(full, entry.Value);
                    _mirror[entry.Key] = entry.Value;
                }
            }
            catch
            {
                // First run / no data / interop unavailable — nothing to hydrate.
            }
        }

        public static void Sync()
        {
            if (!SaveInterop.Available)
                return;
            try
            {
                string root = StorageDevice.Root;
                if (!Directory.Exists(root))
                    return;

                string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                // Small files (settings/unlockables/awardments XML) first, large ones
                // (.dat screenshots) last. Screenshots now route to IndexedDB (separate,
                // large quota) via eaSave's backend split, so they no longer compete with
                // the XML for the ~5MB localStorage cap; the ordering is kept as belt-and-
                // suspenders for the IndexedDB-unavailable fallback (where .dat -> localStorage).
                Array.Sort(files, (a, b) =>
                {
                    long la = new FileInfo(a).Length;
                    long lb = new FileInfo(b).Length;
                    return la.CompareTo(lb);
                });

                var present = new HashSet<string>();
                foreach (string file in files)
                {
                    string name = file.Substring(root.Length).Replace('\\', '/');
                    present.Add(name);
                    byte[] bytes = File.ReadAllBytes(file);
                    if (_mirror.TryGetValue(name, out byte[] old) && BytesEqual(old, bytes))
                        continue;                         // unchanged — no write needed
                    if (SaveInterop.Set(name, bytes))
                        _mirror[name] = bytes;            // leave dirty if it didn't stick
                }

                // Prune entries the game removed from MEMFS since the last sync.
                // Run unconditionally (O(n) over a handful of names): a count-equality
                // shortcut is unsound — a quota-failed Set (which leaves _mirror short one
                // ADD) coinciding with a same-Sync deletion can keep _mirror.Count ==
                // present.Count while a deleted name is still mirrored, so the shortcut
                // would skip the prune and the deleted save resurrects on next hydrate.
                var gone = new List<string>();
                foreach (string name in _mirror.Keys)
                    if (!present.Contains(name))
                        gone.Add(name);
                foreach (string name in gone)
                {
                    SaveInterop.Remove(name);
                    _mirror.Remove(name);
                }
            }
            catch
            {
                // Best effort — a persistence hiccup must never break the game loop.
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
