using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Pages
{
    public partial class Index
    {
        Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            if (firstRender)
            {
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

                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
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

            // run gameloop
            _game.Tick();
        }
    }
}
