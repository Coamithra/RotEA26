// ---------------------------------------------------------------------------
// SoftwareGl — optional CPU rasterization via Mesa's llvmpipe (--software).
//
// WHEN YOU NEED THIS: you don't, on a normal dev box. The default path already needs no
// browser, no dev server and no visible window; it just happens to run its GL on whatever
// driver is installed, which is both correct and fast. --software exists for the case
// where there is no usable GPU AT ALL and the default would fail to create a context:
// a CI container, an SSH session with no display, a VM with no graphics driver.
//
// HOW: SDL2 picks its GL implementation with SDL_GL_LoadLibrary, which honours the
// SDL_VIDEO_GL_DRIVER environment variable. Pointing that at Mesa's opengl32.dll routes
// every GL call through llvmpipe on the CPU. That is preferred over the usual trick of
// dropping opengl32.dll next to the exe: nothing is copied, the choice is explicit and
// per-run, and the same build can do both. The env vars must be set BEFORE the graphics
// device is created (SDL loads GL lazily on first use), which is why Program.Main calls
// Apply() before constructing the host.
//
// Mesa is NOT vendored here -- it's a ~30 MB third-party binary that has no business in a
// game repo, and it is unnecessary on any machine with a GPU. Fetch a Windows llvmpipe
// build (e.g. the mesa-dist-win releases) and point --mesa / EAHL_MESA at its opengl32.dll,
// or drop it in tools/headless/mesa/.
//
// Expect roughly an order of magnitude slower rendering than a GPU. For behaviour and
// timing work that is irrelevant -- use `step --nodraw`, which skips rendering entirely.
// ---------------------------------------------------------------------------
using System;
using System.IO;

namespace EvilAliensWeb.Headless
{
    internal static class SoftwareGl
    {
        internal static bool Active { get; private set; }

        // Returns null on success, or a human-readable reason it could not be enabled.
        internal static string Apply(string explicitPath)
        {
            string dll = Resolve(explicitPath);
            if (dll == null)
            {
                return "no Mesa opengl32.dll found. Looked at: --mesa, $EAHL_MESA, "
                     + Path.Combine(AppContext.BaseDirectory, "mesa") + ", "
                     + "and tools/headless/mesa/. Download a Windows llvmpipe build of Mesa "
                     + "and point --mesa at its opengl32.dll.";
            }

            Environment.SetEnvironmentVariable("SDL_VIDEO_GL_DRIVER", dll);
            // Belt and braces: if the resolved library is a full Mesa with hardware Gallium
            // drivers compiled in, these force the CPU rasterizer rather than letting it
            // find a GPU.
            Environment.SetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE", "1");
            Environment.SetEnvironmentVariable("GALLIUM_DRIVER", "llvmpipe");
            Active = true;
            Console.WriteLine("[eahl] software GL via " + dll);
            return null;
        }

        private static string Resolve(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
                return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

            string env = Environment.GetEnvironmentVariable("EAHL_MESA");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return Path.GetFullPath(env);

            // Next to the exe, then the source tree (so it survives a rebuild without
            // being copied into every configuration's output).
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "mesa", "opengl32.dll"),
                Path.Combine(AppContext.BaseDirectory, "opengl32.dll"),
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
            {
                string c = Path.Combine(dir.FullName, "tools", "headless", "mesa", "opengl32.dll");
                if (File.Exists(c)) return c;
            }
            return null;
        }
    }
}
