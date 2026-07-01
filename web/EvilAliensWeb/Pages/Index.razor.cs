using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Pages
{
    public partial class Index
    {
        Game _game;

        // Reused per tick by the frame-hitch watchdog (don't allocate a Stopwatch each
        // frame — that would feed the very GC pauses NoteFrame is meant to surface).
        readonly System.Diagnostics.Stopwatch _tickSw = new System.Diagnostics.Stopwatch();

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            if (firstRender)
            {
                EvilAliensWeb.Compat.MusicInterop.Init(JsRuntime);
                EvilAliensWeb.Compat.SaveInterop.Init(JsRuntime);
                EvilAliensWeb.Compat.FullscreenInterop.Init(JsRuntime);
                EvilAliensWeb.Compat.ExitInterop.Init(JsRuntime);
                EvilAliensWeb.Compat.TrailerInterop.Init(JsRuntime);

                // Parse the URL query (?menu / ?noattract / ?level=...) into DebugFlags
                // BEFORE the render loop starts, so Game1 (created on the first tick) sees
                // them. Synchronous in-process interop guarantees it lands before initRenderJS.
                if (JsRuntime is IJSInProcessRuntime jsSync)
                {
                    try
                    {
                        EvilAliensWeb.Compat.DebugFlags.Parse(jsSync.Invoke<string>("getDebugQuery"));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[debug] flag read failed: " + ex.Message);
                    }
                }

                // After DebugFlags.Parse (so LoadProfiler sees ?loadlog) but before the
                // render loop starts, so _js is ready before the first texture decode.
                EvilAliensWeb.Compat.LoadProfiler.Init(JsRuntime);

                // Fire-and-forget, but OBSERVE the fault: if initRenderJS throws the render
                // loop never starts, and an unobserved InvokeAsync would swallow the reason.
                _ = ObserveInitRender(JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this)));
            }
        }

        static async System.Threading.Tasks.Task ObserveInitRender(System.Threading.Tasks.ValueTask<object> init)
        {
            try
            {
                await init;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[loop] initRenderJS failed (render loop did not start): " + ex.Message);
            }
        }

        [JSInvokable]
        public void TickDotNet()
        {
            // init game on first tick
            if (_game == null)
            {
                _game = new EvilAliens.Game1();
                _game.Run();
            }

            // run gameloop, timing it so the frame-hitch watchdog can flag a long tick
            // (a cold texture decode, GC pause, etc.) to the console — see LoadProfiler.NoteFrame.
            // try/finally so a throw out of Tick() still stops the stopwatch + records the frame,
            // and the exception propagates to tickJS (which counts it and keeps the loop alive).
            _tickSw.Restart();
            try
            {
                _game.Tick();
            }
            finally
            {
                _tickSw.Stop();
                EvilAliensWeb.Compat.LoadProfiler.NoteFrame(_tickSw.Elapsed.TotalMilliseconds);
            }
        }
    }
}
