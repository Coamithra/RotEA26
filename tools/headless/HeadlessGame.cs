// ---------------------------------------------------------------------------
// HeadlessGame — Game1 with the loop driven from outside instead of by a clock.
//
// The browser build's loop is JS-driven (index.html initRenderJS/tickJS -> TickDotNet ->
// Game.Tick), paced by requestAnimationFrame. Game.Tick() paces itself off the wall clock,
// which is exactly wrong for a test rig: the same command sequence would produce different
// frames depending on how loaded the machine was, and a slow content decode would land as
// one enormous dt.
//
// So this subclass exposes Update and Draw separately and the host calls them with a
// SYNTHESISED fixed dt (60 Hz by default). Runs are then reproducible and can go as fast as
// the CPU allows -- there is no vsync, no rAF and no sleep anywhere in the loop.
//
// It subclasses rather than reimplements on purpose: Update/Draw stay the REAL Game1 ones,
// including the FrameProfiler brackets, the turbo/slow-mo/hit-stop rescale in UpdateScaled
// and the whole present blit. The only thing this class adds is a capture hook between
// Draw() and EndDraw() -- the one moment the back buffer holds the finished frame and has
// not yet been swapped away.
// ---------------------------------------------------------------------------
using System;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Headless
{
    internal sealed class HeadlessGame : Game1
    {
        // Set for the frame that should be captured; filled with the back buffer and
        // cleared by DrawFrame. Frame-scoped so a capture costs nothing on the frames
        // that don't want one (glReadPixels is a full pipeline stall).
        private bool _captureRequested;
        private Color[] _captured;
        private int _capturedWidth;
        private int _capturedHeight;

        internal long FrameNumber { get; private set; }

        // Mirrors GameStrategy.CallUpdate: the framework dispatcher pump is what services
        // SoundEffectInstance / dynamic audio, and skipping it leaks voices over a long run.
        internal void UpdateFrame(GameTime gameTime)
        {
            FrameworkDispatcher.Update();
            Update(gameTime);
            FrameNumber++;
        }

        // Present (EndDraw / SDL_GL_SwapWindow) on the hidden window. Off by default: see
        // HeadlessHost for why swapping a window nobody can see costs ~40ms a frame.
        internal bool Present;

        // Mirrors GameStrategy.CallDraw, with the capture spliced in before EndDraw.
        // Returns false when the platform refused the frame (device lost / not ready) --
        // then nothing was drawn and any pending capture stays pending.
        internal bool DrawFrame(GameTime gameTime)
        {
            var gdm = (IGraphicsDeviceManager)Services.GetService(typeof(IGraphicsDeviceManager));
            if (gdm != null && !gdm.BeginDraw())
                return false;
            if (!BeginDraw())
                return false;

            Draw(gameTime);

            if (_captureRequested)
            {
                PresentationParameters pp = GraphicsDevice.PresentationParameters;
                _capturedWidth = pp.BackBufferWidth;
                _capturedHeight = pp.BackBufferHeight;
                _captured = new Color[_capturedWidth * _capturedHeight];
                // Game1.Draw ends having blitted sceneTarget to the (null) back buffer
                // through the gamma shader, so this is the finished, letterboxed,
                // gamma-corrected frame -- byte-for-byte what the browser would show.
                GraphicsDevice.GetBackBufferData(_captured);
                _captureRequested = false;
            }

            if (Present)
                EndDraw();
            return true;
        }

        internal void RequestCapture() { _captureRequested = true; }

        // Hands over the last captured frame (and drops the reference -- these are
        // multi-megabyte arrays and a long run would otherwise pin one forever).
        internal bool TakeCapture(out Color[] pixels, out int width, out int height)
        {
            pixels = _captured;
            width = _capturedWidth;
            height = _capturedHeight;
            _captured = null;
            return pixels != null;
        }
    }
}
