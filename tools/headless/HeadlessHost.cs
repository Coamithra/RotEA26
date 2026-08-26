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

        // --nettime game only. The clock is integer ms and the step is 16.666..., so the
        // FRACTION is carried rather than truncated -- truncating loses 40ms per second, which
        // over a 600s soak is 24s of drift between the two peers' cadences.
        private Compat.Net.PinnedNetHost _netClock;
        private double _netClockCarry;

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
            // poisoned every later one (card 36db5d75). The constraint is only that SetRoot
            // precedes the first OpenContainer, which Boot() (later) guarantees -- SetRoot
            // itself creates nothing, the first StorageContainer creates `fs/` lazily.
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

            // The ?net= loopback's medium (card 054947f3). Which end dials is decided by the
            // boot role, not by who opened first, and it must be set before Game1.Initialize
            // reaches NetSession.Start -- which is the first eaNet.open.
            LocalSocketNet.SetPortOverride(_opt.NetPort);
            LocalSocketNet.SetMaxPeers(_opt.NetPeers);
            LocalSocketNet.ConfigureFromRole(DebugFlags.NetRole);
            if (_opt.NetTimeGame)
            {
                // A PinnedNetHost DECORATOR, so only the clock changes: the build hash, the peer
                // token and the debug flags all still come from production, which is what keeps
                // a "deterministic" rig from quietly being a different rig (see its header).
                _netClock = new Compat.Net.PinnedNetHost();
                Compat.Net.NetHost.Current = _netClock;
                Log("nettime  GAME time (" + _step.TotalMilliseconds.ToString("0.##")
                    + "ms/frame) -- the wire's cadences track world motion, not the wall clock");
            }

            // BEFORE the boot tick, which already polls input. There is no window to point at
            // here, but KNI's SDL2 backend does not know that: it answers Mouse.GetState() from
            // SDL_GetGlobalMouseState, so without this the run reads the developer's desktop
            // pointer and desktop button mask -- an uncontrolled external input, in the one host
            // whose entire value is repeatability. It is the same reason the boot dt is pinned
            // just below. Card 83054936; the mechanism and the two probes it flaked are at
            // DebugInput.SuppressPhysicalMouse.
            Compat.DebugInput.SuppressPhysicalMouse = !_opt.RealMouse;

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

            // Creates the GL device and runs Initialize/LoadContent. It also ticks ONCE
            // (RunOneFrame = CallInitialize + CallBeginRun + Tick + CallEndRun) -- that boot
            // frame is the one tick this host does not step itself. Frames counted/stepped
            // below are all fixed-dt.
            //
            // Its dt is pinned here as far as this host can (card d937c721). Game1 sets
            // IsFixedTimeStep = false, which handed that tick however many milliseconds the boot
            // happened to take -- and since RandomHelper.RandomFromAverage
            // is dt-PROPORTIONAL, a variable boot dt makes even a ?seed=<n> run draw a different
            // amount of the seeded stream. That is why seeding alone did NOT make a level A/B
            // repeat: measured on ?level=OwnLevel&noattract&seed=12345, two runs still differed
            // by mean |diff| 0.45 / MAX 203 -- the unseeded noise floor. With this pin the same
            // pair was byte-identical for 10 consecutive runs on a quiet box (though only 6 of
            // 10 while sibling builds loaded the CPU -- see below), while a different seed (1.48)
            // and an unseeded pair (1.08) still diverge, so it is the seed doing the work.
            // `IsFixedTimeStep` is put back straight after; `TargetElapsedTime` is deliberately
            // left at `_step`, because nothing reads it once Step() drives Update/Draw by hand
            // and `_step` is the value it would want anyway.
            //
            // NOT the whole story, and the remainder is a card of its own (see
            // tools/headless/README.md -> "Reproducibility"): a fixed-step Tick still runs
            // `accumulated / TargetElapsedTime` catch-up updates, and the boot's accumulated wall
            // time varies with machine load, so a run starts some whole number of steps in and
            // the same seed yields one of a handful of discrete worlds (measured: 4 states over
            // 10 runs under load). Two fixes were
            // tried and refuted here: MaxElapsedTime = _step throws (KNI enforces a 0.5 s floor),
            // and ResetElapsedTime() from a BeginRun override made every run diverge again.
            _game.IsFixedTimeStep = true;
            _game.TargetElapsedTime = _step;
            _game.RunOneFrame();
            _game.IsFixedTimeStep = false;

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
            // Announced rather than assumed: a run that IS reading the desktop mouse looks
            // exactly like one that is not until something flakes, which is the whole history
            // of card 83054936.
            Log("input    physical mouse " + (_opt.RealMouse ? "LIVE (--real-mouse)" : "suppressed"));
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
                StepOnce(_step, draw);
            }
        }

        // One frame at a CALLER-CHOSEN dt, in ms -- the browser-hitch rig (card 430494a7).
        // The browser runs IsFixedTimeStep=false, and after a main-thread stall KNI's
        // GameStrategy.Tick hands the game its whole real elapsed time as ONE dt, clamped
        // only by MaxElapsedTime (500 ms, and its setter refuses anything lower). The
        // fixed-step loop above can never produce such a tick, so this is the only headless
        // way to look at what one does to the world -- and the rig Game1's world-dt clamp
        // is demonstrated and probed with.
        internal void StepDt(double ms, bool draw)
        {
            StepOnce(TimeSpan.FromMilliseconds(ms), draw);
        }

        private void StepOnce(TimeSpan dt, bool draw)
        {
            // KNI brings OpenAL up lazily on the first sound, so the mixer-level half of
            // the mute can only be applied once a context exists -- which may happen
            // anywhere inside a long `step`, not before it. One bool test per frame after
            // it lands. (Hoisting this out to the callers looks like an optimisation and is
            // a bug: a script that does the whole run in one `step 3600` would then check
            // exactly once, before any sound had ever played, and never apply it.)
            HeadlessAudio.Pump();
            if (_netClock != null)
            {
                _netClockCarry += dt.TotalMilliseconds;
                long whole = (long)_netClockCarry;
                _netClockCarry -= whole;
                _netClock.Advance(whole);
            }
            _total += dt;
            var gt = new GameTime(_total, dt);
            // FrameProfiler's per-phase brackets live inside Game1 and so already run here,
            // but the sample only ENTERS the ring on EndFrame -- which in the browser is
            // called from Index.razor.cs, i.e. code this host does not run. Without this the
            // headless profiler reports a permanently stale window and `eval FpsStatsLine`
            // is useless. One stopwatch, same contract as the browser's tick timer.
            long tickStart = FrameProfiler.Enabled ? Stopwatch.GetTimestamp() : 0L;
            _game.UpdateFrame(gt);
            if (draw)
                _game.DrawFrame(gt);
            if (tickStart != 0L)
                FrameProfiler.EndFrame(
                    (Stopwatch.GetTimestamp() - tickStart) * 1000.0 / Stopwatch.Frequency);
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
