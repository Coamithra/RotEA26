// ---------------------------------------------------------------------------
// Stub of Microsoft.Xna.Framework.Storage (XNA 3.x synchronous storage API).
//
// The game uses the 3.x pattern: device.OpenContainer("name") -> StorageContainer,
// then File IO against container.Path. KNI follows the 4.0 async storage API, so
// we replace it entirely (the KNI Storage package is removed from the .csproj).
//
// Backing store: the WASM in-memory filesystem (MEMFS). System.IO works there in
// a sandbox, so settings / high-scores / unlockables persist for the SESSION.
// Cross-session persistence (localStorage/IndexedDB) is wired up in a later stage.
// ---------------------------------------------------------------------------
using System;
using System.IO;

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
        public void Dispose() { IsDisposed = true; }
    }

    public sealed class StorageDevice
    {
        public static readonly StorageDevice Default = new StorageDevice();

        private const string Root = "/eaweb_save/";

        public bool IsConnected => true;
        public long FreeSpace => long.MaxValue;
        public long TotalSpace => long.MaxValue;

        public StorageContainer OpenContainer(string titleName)
            => new StorageContainer(Root + titleName);
    }
}
