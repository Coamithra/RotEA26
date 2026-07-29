// ---------------------------------------------------------------------------
// HeadlessHost — boots the real game with no browser and no visible window, steps it
// at a fixed dt, and writes frames out as PNGs.
//
// THE NO-WINDOW TRICK (the thing that makes this "headless" rather than "a game with a
// small window"): KNI's SDL2 backend creates its window with SDL_WINDOW_HIDDEN and shows
// it in exactly ONE place -- ConcreteGame.RunGameLoop, the blocking loop behind Game.Run().
// This host never calls Run(). It calls RunOneFrame() to build the device and run
// Initialize/LoadContent, then drives Update/Draw itself forever after. The window is
// created (WGL needs one for a GL context) but never shown, never focused and never
// pumped, so nothing appears on screen and nothing steals focus -- a run is safe to leave
// going in the background while the user works.
//
// A real GL context on the installed driver is used by default. That is genuinely headless
// already, and it is fast. When there is no usable GPU at all (a CI container, an SSH
// session, a VM with no driver) pass --software, which routes the same GL through Mesa's
// llvmpipe rasterizer on the CPU -- see SoftwareGl.cs.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Headless
{
    internal sealed class HeadlessHost : IDisposable
    {
        private readonly Options _opt;
        private readonly HeadlessJsRuntime _js;
        private HeadlessGame _game;

        // Fixed simulated timestep. Everything time-based in the game reads
        // GameTime.ElapsedGameTime, so synthesising it is what makes a run reproducible.
        private readonly TimeSpan _step;
        private TimeSpan _total = TimeSpan.Zero;

        internal HeadlessHost(Options opt)
        {
            _opt = opt;
            _step = TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond / Math.Max(1.0, opt.Fps)));

            string wwwroot = RepoPaths.FindWwwroot(opt.ContentDir);
            Log("content  " + wwwroot);
            // Before ANY content touch -- the factory is register-once (see the file comment).
            HeadlessTitleContainerFactory.Register(wwwroot);

            var saves = new HeadlessSaveStore(opt.SaveDir, opt.WipeSaves);
            // Put the XML save tree INSIDE the --saves dir. On desktop the stub's browser
            // default ("/eaweb_save/") is a real directory at the drive root that nothing here
            // owns or wipes, so Achievements.xml survived every run and a ?unlockall probe
            // poisoned every later one (card 36db5d75). Ordered after the store on purpose:
            // its wipe deletes the whole SaveDir, so this subdirectory must be created after.
            Microsoft.Xna.Framework.Storage.StorageDevice.SetRoot(
                Path.Combine(Path.GetFullPath(opt.SaveDir), "fs"));
            _js = new HeadlessJsRuntime(saves, opt.Flags, opt.Verbose)
            {
                DownloadDir = Path.GetDirectoryName(Path.GetFullPath(opt.OutPath ?? "out.png"))
            };
        }

        internal HeadlessGame Game => _game;
        internal long Frame => _game?.FrameNumber ?? 0;
        internal HeadlessJsRuntime Js => _js;

        // ---- boot ----------------------------------------------------------------------

        internal void Boot()
        {
            var sw = Stopwatch.StartNew();

            // Same order as Pages/Index.razor.cs OnAfterRender, for the same reason: every
            // shim must have its runtime before the first tick, and DebugFlags must be
            // parsed before Game1 exists (it reads them during construction/first update).
            MusicInterop.Init(_js);
            SaveInterop.Init(_js);
            FullscreenInterop.Init(_js);
            CursorInterop.Init(_js);
            ExitInterop.Init(_js);
            TrailerInterop.Init(_js);
            WebcamInterop.Init(_js);
            TexViewerInterop.Init(_js);
            Compat.Net.NetInterop.Init(_js);
            Compat.Net.WebRtcInterop.Init(_js);
            DebugFlags.Parse(_opt.Flags);
            LoadProfiler.Init(_js);

            _game = new HeadlessGame();

            // Game1 deliberately does not pin a back-buffer size (KNI's BlazorGL rewrites it
            // on every browser resize). On desktop nothing rewrites it, so the size is ours
            // to choose -- and choosing a 4:3 one means RenderScale letterboxes to exactly
            // the full frame, i.e. no black bars in the screenshot.
            var gdm = (GraphicsDeviceManager)_game.Services.GetService(typeof(IGraphicsDeviceManager));
            gdm.PreferredBackBufferWidth = _opt.Width;
            gdm.PreferredBackBufferHeight = _opt.Height;
            // Nothing is being presented to a monitor, so waiting for vblank would just cap
            // the rig at the refresh rate.
            gdm.SynchronizeWithVerticalRetrace = false;
            _game.Present = _opt.Present;

            // Creates the GL device and runs Initialize/LoadContent. It also ticks ONCE off
            // the wall clock (RunOneFrame = CallInitialize + CallBeginRun + Tick + CallEndRun)
            // -- that one boot frame is the only non-synthesised dt in a run, and it is
            // clamped by Game.MaxElapsedTime. Frames counted/stepped below are all fixed-dt.
            _game.RunOneFrame();

            Log("device   " + GraphicsAdapter.DefaultAdapter.Description
                + "  profile=" + _game.GraphicsDevice.GraphicsProfile
                + (SoftwareGl.Active ? "  [llvmpipe software]" : ""));
            PresentationParameters pp = _game.GraphicsDevice.PresentationParameters;
            Log("frame    " + pp.BackBufferWidth + "x" + pp.BackBufferHeight
                + "  render=" + RenderScale.Width + "x" + RenderScale.Height
                + "  dt=" + _step.TotalMilliseconds.ToString("0.##") + "ms");
            if (pp.BackBufferWidth != _opt.Width || pp.BackBufferHeight != _opt.Height)
                Log("WARNING  back buffer is " + pp.BackBufferWidth + "x" + pp.BackBufferHeight
                    + ", not the requested " + _opt.Width + "x" + _opt.Height);
            Log("boot     " + sw.ElapsedMilliseconds + "ms");
        }

        // ---- stepping ------------------------------------------------------------------

        // Advance n frames. `draw` false runs update only -- much faster, and the right
        // choice for behaviour/timing soaks where no pixels are wanted (the same reason
        // AiBench.RunHeadless exists). Rendering is most of a frame's cost.
        internal void Step(int frames, bool draw)
        {
            for (int i = 0; i < frames; i++)
            {
                // KNI brings OpenAL up lazily on the first sound, so the mixer-level half of
                // the mute can only be applied once a context exists -- which may happen
                // anywhere inside a long `step`, not before it. One bool test per frame after
                // it lands. (Hoisting this out of the loop looks like an optimisation and is
                // a bug: a script that does the whole run in one `step 3600` would then check
                // exactly once, before any sound had ever played, and never apply it.)
                HeadlessAudio.Pump();
                _total += _step;
                var gt = new GameTime(_total, _step);
                _game.UpdateFrame(gt);
                if (draw)
                    _game.DrawFrame(gt);
            }
        }

        // Draw one frame and write it to disk. Split from Step so a screenshot never
        // advances the simulation: the captured frame is the CURRENT state, which is what
        // makes "step to the interesting moment, then look" work.
        internal string Shot(string path)
        {
            _game.RequestCapture();
            var gt = new GameTime(_total, _step);
            if (!_game.DrawFrame(gt))
                throw new InvalidOperationException("the platform refused the frame (device not ready)");
            if (!_game.TakeCapture(out Color[] pixels, out int w, out int h))
                throw new InvalidOperationException("no pixels were captured");

            WarnIfStillFadingIn(pixels);

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            using (var tex = new Texture2D(_game.GraphicsDevice, w, h))
            {
                tex.SetData(pixels);
                using (FileStream fs = File.Create(full))
                    tex.SaveAsPng(fs, w, h);
            }
            return full + " (" + w + "x" + h + ")";
        }

        // Every scene that calls Background.Reset() -- level entry AND the debug scenes
        // (?harness=, ?textshot, ...) -- starts in BackgroundState.LeavingHyperspace with
        // fadeFactor 0.998, a white flash that decays at 0.0005/ms. At 60 Hz that is ~120
        // frames, so ANY screenshot taken in the first two seconds is a white rectangle
        // with the sprite on it. That reads exactly like a broken renderer, and cost a
        // full investigation the first time; one line here saves the next person that.
        //
        // Deliberately narrow: it only fires while the fade could still be running, so a
        // legitimately white frame later on (a real flash, a white level) never trips it.
        private void WarnIfStillFadingIn(Color[] pixels)
        {
            const double FadeSeconds = 2.2;   // 0.998 / 0.0005 ms, plus a little slack
            if (_total.TotalSeconds > FadeSeconds)
                return;

            // Sparse sample -- this runs on a multi-megapixel array and the answer is the
            // same from a few thousand points.
            int step = Math.Max(1, pixels.Length / 4096), n = 0, white = 0;
            for (int i = 0; i < pixels.Length; i += step, n++)
                if (pixels[i].R > 235 && pixels[i].G > 235 && pixels[i].B > 235)
                    white++;
            if (n == 0 || white < n * 0.9)
                return;

            Log("NOTE     this frame is almost entirely white at " + _total.TotalSeconds.ToString("0.00")
                + "s of sim time -- that is very likely the background's LeavingHyperspace");
            Log("         flash, not a render fault. It decays over ~" + (int)(FadeSeconds * _opt.Fps)
                + " frames; step past it (e.g. --frames " + (int)(FadeSeconds * _opt.Fps + 20) + ") and shoot again.");
        }

        // ---- diagnostics ---------------------------------------------------------------

        internal string Info()
        {
            PresentationParameters pp = _game.GraphicsDevice.PresentationParameters;
            return "frame=" + _game.FrameNumber
                 + " simtime=" + _total.TotalSeconds.ToString("0.000") + "s"
                 + " backbuffer=" + pp.BackBufferWidth + "x" + pp.BackBufferHeight
                 + " render=" + RenderScale.Width + "x" + RenderScale.Height
                 + " scene=" + SceneName();
        }

        // Best-effort label for "what is on screen", so a driver can tell whether the boot
        // flags actually landed somewhere before spending frames.
        private string SceneName()
        {
            try
            {
                if (Compat.Net.NetSession.Active) return "net";
                if (DebugFlags.Level.HasValue) return DebugFlags.Level.Value.ToString();
                return DebugFlags.SkipSplash ? "menu" : "splash";
            }
            catch (Exception) { return "?"; }
        }

        internal void DumpJsCalls()
        {
            Log("js calls the game made (a browser would have serviced these):");
            var keys = new List<string>(_js.Calls.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys)
                Log("  " + k + "  x" + _js.Calls[k]);
        }

        private void Log(string s) => Console.WriteLine("[eahl] " + s);

        public void Dispose() => _game?.Dispose();
    }
}
