// ---------------------------------------------------------------------------
// Stub of Microsoft.Xna.Framework.Storage (XNA 3.x synchronous storage API).
//
// The game uses the 3.x pattern: device.OpenContainer("name") -> StorageContainer,
// then File IO against container.Path. KNI follows the 4.0 async storage API, so
// we replace it entirely (the KNI Storage package is removed from the .csproj).
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

        internal const string Root = "/eaweb_save/";

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

    // Mirrors the save tree (/eaweb_save/**) to browser localStorage so settings,
    // unlockables, awardments and level screenshots survive a page reload. Entries
    // are keyed by path relative to Root (e.g. "EvilAliens/Settings.xml"). Sync only
    // writes files whose bytes changed since the last persist and prunes ones the
    // game deleted (e.g. cleared screenshots).
    internal static class PersistentSave
    {
        private static bool _hydrated;

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
                // (.dat screenshots) last — so if a screenshot blows the ~5MB
                // localStorage quota, the critical data is already persisted.
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
                if (_mirror.Count != present.Count)
                {
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
