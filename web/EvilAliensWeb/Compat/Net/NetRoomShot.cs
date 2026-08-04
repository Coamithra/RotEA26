using System;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat.Net
{
    // Room thumbnails, host half (card e7404647). The matchmaking server PULLS a picture of
    // this game roughly every 15 s while it is publicly listed; this is what produces one.
    //
    // WHAT IS CAPTURED: the resolved scene render target -- the 4:3 playing field, exactly as
    // ScreenshotSaver's level-select thumbnail takes it, and nothing else. Never the canvas,
    // never the page, never a camera. (WebcamAliens cannot be listed at all --
    // NetListing.IsNetEligibleLevel refuses it -- so no camera frame can reach a room
    // thumbnail even in principle.)
    //
    // WHY NOT canvas.toDataURL, which would need no C# at all: the WebGL context has no
    // preserveDrawingBuffer, so a canvas read outside the frame's own task is blank, and the
    // canvas also carries the letterbox bars rather than the field. Resolving the scene target
    // has neither problem, gives a fixed 4:3 source at any window size, and -- the reason that
    // matters for this repo -- runs under eahl, so the capture is verifiable headlessly.
    // JS is left with the one job C# cannot do: JPEG-encode the pixels (eaRtc.sendShot).
    internal static class NetRoomShot
    {
        // 200x150 is the card's cap: 4:3 like the 800x600 design field, so SubMenuOnlineGames'
        // existing 800/w * 600/h scaling draws it with no geometry change at all.
        internal const int Width = 200;
        internal const int Height = 150;

        // Reused across pulls -- one capture every ~15 s, but a fresh RT + ResolveTexture2D per
        // pull would churn two GPU surfaces for nothing.
        private static ResolveTexture2D scratch;
        private static RenderTarget2D thumbTarget;
        private static uint[] pixels;

        private static bool subscribed;
        private static bool armed;                 // a pull is waiting on the next post-draw
        private static Game1.PostDrawEvent hook;

        // Last capture's report, for eaRoomShot()/eval RoomShot. Kept as data rather than only
        // logged: whether the picture is BLANK is the one thing a screenshot of the game cannot
        // tell you, since a broken capture and a dark starfield look identical in a log line.
        internal static int LastWidth { get; private set; }
        internal static int LastHeight { get; private set; }
        internal static uint LastAlphaMin { get; private set; }
        internal static int LastDistinctColors { get; private set; }
        internal static int Captures { get; private set; }

        internal static void Tick()
        {
            if (!subscribed)
            {
                subscribed = true;
                WebRtcInterop.OnShotRequest += Arm;
            }
        }

        // A pull landed. Nothing is captured here: the scene target only holds a finished frame
        // during Draw, so all this does is book the next post-draw.
        //
        // A pull arriving while one is already booked is DROPPED, and in a tab that is not
        // drawing (occluded or backgrounded -- rAF is paused outright) that is every pull until
        // the tab draws again, at which point the booked capture fires and answers. Observed in
        // the browser pass and left alone: a room whose host is not rendering has nothing new to
        // photograph anyway, the server's own staleness bound covers it, and the alternative --
        // re-booking -- would combine the hook twice and capture twice on the frame it recovers.
        private static void Arm()
        {
            if (armed || !NetListing.Listed || GameScene.NetActiveScene == null)
            {
                return;
            }
            armed = true;
            if (hook == null)
            {
                hook = OnPostDraw;
            }
            Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Combine(Game1.onPostDraw, hook);
        }

        private static void OnPostDraw()
        {
            Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Remove(Game1.onPostDraw, hook);
            armed = false;
            byte[] rgba = Capture();
            if (rgba != null)
            {
                WebRtcInterop.SendShot(rgba, Width, Height);
            }
        }

        // Resolve the live scene into a Width x Height RGBA buffer. Public to the net layer (and
        // to eaRoomShot) rather than private to OnPostDraw, so the capture can be exercised with
        // no server, no socket and no pull -- which is the only way it is reachable headlessly.
        //
        // MUST run inside a Draw (the Game1.onPostDraw window). Outside one the scene target is
        // not the frame you think it is.
        internal static byte[] Capture()
        {
            IGraphicsDeviceService gds = ServiceHelper.Get<IGraphicsDeviceService>();
            ISpriteBatchWrapperService sbs = ServiceHelper.Get<ISpriteBatchWrapperService>();
            IContentManagerService cms = ServiceHelper.Get<IContentManagerService>();
            if (gds == null || sbs == null || cms == null)
            {
                return null;
            }
            GraphicsDevice device = gds.GraphicsDevice;
            SpriteBatchWrapper batch = sbs.SpriteBatchWrapper;
            try
            {
                int srcW = RenderScale.Width;
                int srcH = RenderScale.Height;
                if (scratch == null || ((Texture2D)scratch).Width != srcW || ((Texture2D)scratch).Height != srcH)
                {
                    if (scratch != null)
                    {
                        ((GraphicsResource)scratch).Dispose();
                    }
                    scratch = new ResolveTexture2D(device, srcW, srcH, 1,
                        device.PresentationParameters.BackBufferFormat);
                }
                device.ResolveBackBuffer(scratch);
                if (thumbTarget == null)
                {
                    thumbTarget = new RenderTarget2D(device, Width, Height, false,
                        device.PresentationParameters.BackBufferFormat, DepthFormat.None);
                }
                batch.Flush();
                device.SetRenderTarget(0, thumbTarget);
                device.Clear(Color.Black);
                // DrawPresent (identity transform), NOT Draw: the plain path bakes in
                // RenderScale.Matrix and would land only the top-left corner of the field in a
                // target this small -- the "screenshot is cropped" bug ScreenshotSaver documents.
                batch.BlendMode = (SpriteBlendMode)0;
                batch.DrawPresent((Texture2D)(object)scratch, new Rectangle(0, 0, Width, Height), Color.White);
                // Force alpha opaque before the read-back, the same seal ScreenshotSaver needs
                // (card d67755d2): every NonPremultiplied layer erodes alpha, so a busy frame
                // resolves well under 1. It matters MORE here than there -- the JS encoder paints
                // these pixels into a canvas, and toDataURL('image/jpeg') composites a
                // translucent canvas over black, so an unsealed frame reaches the server visibly
                // darkened in the shape of whatever background layers happened to be drawing.
                batch.SealAlpha(cms.ContentManager.Load<Texture2D>("GFX/Game/blank"), Width, Height,
                    "[roomshot] seal");
                batch.BlendMode = (SpriteBlendMode)1;
                device.SetRenderTarget(0, (RenderTarget2D)null);

                Texture2D texture = thumbTarget.GetTexture();
                if (pixels == null || pixels.Length != texture.Width * texture.Height)
                {
                    pixels = new uint[texture.Width * texture.Height];
                }
                texture.GetData<uint>(pixels);
                return Describe(pixels, texture.Width, texture.Height);
            }
            catch (Exception e)
            {
                Console.WriteLine("[roomshot] capture failed: " + e.Message);
                return null;
            }
        }

        // uint[] (the GetData layout) -> the byte[] JS wants, recording the diagnostics on the
        // way through so nothing has to walk the buffer twice.
        private static byte[] Describe(uint[] src, int w, int h)
        {
            byte[] rgba = new byte[src.Length * 4];
            uint alphaMin = 255u;
            // A cheap stand-in for "is there a picture here": how many DISTINCT 5-bit-per-channel
            // colours the frame contains. A failed capture is one flat colour (1), a real frame
            // is hundreds. Bucketed rather than exact so a 32-bit histogram stays a small array.
            bool[] seen = new bool[32 * 32 * 32];
            int distinct = 0;
            for (int i = 0; i < src.Length; i++)
            {
                uint c = src[i];
                // GetData on this surface returns Color (RGBA) packed little-endian: R is the
                // low byte, A the high one -- already the order ImageData wants.
                byte r = (byte)(c & 0xFFu);
                byte g = (byte)((c >> 8) & 0xFFu);
                byte b = (byte)((c >> 16) & 0xFFu);
                byte a = (byte)((c >> 24) & 0xFFu);
                int o = i * 4;
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = a;
                if (a < alphaMin)
                {
                    alphaMin = a;
                }
                int bucket = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                if (!seen[bucket])
                {
                    seen[bucket] = true;
                    distinct++;
                }
            }
            LastWidth = w;
            LastHeight = h;
            LastAlphaMin = alphaMin;
            LastDistinctColors = distinct;
            Captures++;
            if (DebugFlags.NetLog)
            {
                Console.WriteLine("[roomshot] captured " + w + "x" + h + " bytes=" + rgba.Length
                    + " alphaMin=" + alphaMin + " colors=" + distinct);
            }
            return rgba;
        }

        // Console/eval seam: capture the CURRENT frame through the real path and report it as
        // data (eaRoomShot() / `eval RoomShot`). Must be driven from inside a Draw, so it books
        // the same post-draw hook a pull would and prints when the frame lands -- a capture taken
        // straight from a console call would read whatever target happened to be bound.
        // `injectCode` non-empty additionally installs the captured frame as that room code's
        // thumbnail (eaRoomShot.inject), which is how a REAL captured game frame reaches the
        // carousel with no server in the loop -- the last link the ?gamebrowser=thumbs rig,
        // running at the menu with no level to capture, cannot supply itself.
        internal static void ProbeCapture(string injectCode)
        {
            if (probing)
            {
                return;
            }
            probing = true;
            probeInject = injectCode ?? "";
            Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Combine(Game1.onPostDraw, probeHook);
        }

        private static bool probing;
        private static string probeInject = "";
        private static readonly Game1.PostDrawEvent probeHook = ProbeOnPostDraw;

        private static void ProbeOnPostDraw()
        {
            Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Remove(Game1.onPostDraw, probeHook);
            probing = false;
            byte[] rgba = Capture();
            string inject = probeInject;
            probeInject = "";
            if (rgba != null && inject.Length > 0)
            {
                NetGameBrowser.SetThumbnail(inject, 1, rgba, LastWidth, LastHeight);
            }
            Console.WriteLine("[roomshot] probe " + (rgba == null
                ? "FAILED"
                : LastWidth + "x" + LastHeight + " bytes=" + rgba.Length
                  + " alphaMin=" + LastAlphaMin + " colors=" + LastDistinctColors
                  + (inject.Length > 0 ? " injected=" + inject : "")));
        }
    }
}
