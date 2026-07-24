// ---------------------------------------------------------------------------
// HeadlessTitleContainer — points TitleContainer at web/EvilAliensWeb/wwwroot.
//
// WebContentManager opens every asset through TitleContainer.OpenStream with a
// wwwroot-relative path ("Content/gfx/base/756.dds"). KNI's desktop strategy roots
// that at AppDomain.CurrentDomain.BaseDirectory — i.e. tools/headless/bin/<cfg>/net8.0 —
// where no content exists.
//
// The obvious fixes are both bad: copying wwwroot/Content into the output is 282 MB per
// configuration and goes stale the moment an asset pipeline reruns, and a directory
// junction is a Windows-only side effect that litters the tree. KNI instead lets a host
// register its own strategy BEFORE the engine reflects one in (that is what its
// "Initialize title with 'TitleContainerFactory.RegisterTitleContainerFactory(...)'"
// console notice is asking for), so the root is simply redirected in-process.
//
// MUST be registered before anything touches TitleContainer — the factory is
// register-once and throws "TitleContainerFactory allready registered" on a second call,
// including the reflective self-registration the engine does on first use.
//
// Content therefore comes LIVE from the working tree: regenerate a .dds or an .mgfxo with
// a tools/ pipeline and the next headless run picks it up with no copy step. That also
// means the headless host reads exactly the bytes the dev server would serve, so an asset
// bug reproduces here rather than being masked by a stale copy.
// ---------------------------------------------------------------------------
using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Platform;

namespace EvilAliensWeb.Headless
{
    internal sealed class HeadlessTitleContainer : TitleContainerStrategy
    {
        private readonly string _location;

        internal HeadlessTitleContainer(string location) { _location = location; }

        public override string Location => _location;

        // TitlePlatform.Windows. Named values live in an enum the game never references;
        // WebContentManager doesn't branch on it, and nor does anything else in this port.
        public override TitlePlatform Platform => (TitlePlatform)20;

        public override Stream PlatformOpenStream(string name)
            => File.OpenRead(Path.Combine(_location, name));
    }

    internal sealed class HeadlessTitleContainerFactory : TitleContainerFactory
    {
        private readonly string _location;

        private HeadlessTitleContainerFactory(string location) { _location = location; }

        public override TitleContainerStrategy CreateTitleContainerStrategy()
            => new HeadlessTitleContainer(_location);

        internal static void Register(string wwwroot)
            => RegisterTitleContainerFactory(new HeadlessTitleContainerFactory(wwwroot));
    }

    internal static class RepoPaths
    {
        // Walk up from the build output (tools/headless/bin/<cfg>/net8.0) looking for the
        // wwwroot that holds Content. Anchoring on Content/ rather than on a repo marker
        // means a moved/renamed checkout still resolves, and a WRONG hit is impossible —
        // the directory being probed for is the one actually needed.
        internal static string FindWwwroot(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            {
                string full = Path.GetFullPath(explicitPath);
                if (!Directory.Exists(Path.Combine(full, "Content")))
                    throw new DirectoryNotFoundException(
                        "--content '" + full + "' has no Content/ subdirectory. Pass the wwwroot "
                        + "directory itself (web/EvilAliensWeb/wwwroot), not wwwroot/Content.");
                return full;
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "web", "EvilAliensWeb", "wwwroot");
                if (Directory.Exists(Path.Combine(candidate, "Content")))
                    return Path.GetFullPath(candidate);
            }
            throw new DirectoryNotFoundException(
                "Could not locate web/EvilAliensWeb/wwwroot above " + AppContext.BaseDirectory
                + ". Pass --content <path-to-wwwroot> explicitly.");
        }
    }
}
