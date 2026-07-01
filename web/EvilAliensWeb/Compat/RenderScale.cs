// ---------------------------------------------------------------------------
// RenderScale — the single source of truth for the unified design->render scale
// (Stage 10).
//
// The game is authored in a fixed 800x600 design space. Through Stage 9 the whole
// frame was rendered into an 800x600 scene target and any genuinely high-res art
// (the menu title, the channel-flip splash reveal) was bolted on in a SEPARATE
// native-resolution overlay pass at present time. Stage 10 removes that split: the
// ENTIRE frame is now rendered into ONE scene target sized to the window's 4:3
// letterbox, so the legacy art is upscaled and the hi-res art is drawn at native
// density into the same target — and they then share one bloom, one gamma, and one
// present blit.
//
// How it's used:
//   * Game1.Draw calls Update() once per frame from the window back-buffer size.
//   * Every game-content SpriteBatch.Begin (via SpriteBatchWrapper) multiplies by
//     Matrix, so a draw at design coord (x,y) lands at (x,y) in render space and
//     fills the larger target.
//   * Size-dependent render targets (sceneTarget, the bloom targets, the menu and
//     background offscreen targets, the level-select screenshot) are sized to
//     Width x Height and recreated when it changes.
//   * The present blit copies the scene target into the window letterbox (1:1 when
//     uncapped; a bilinear upscale when the cap kicks in on a very large window).
// ---------------------------------------------------------------------------
using System;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    public static class RenderScale
    {
        public const int DesignWidth = 800;
        public const int DesignHeight = 600;

        // Cap the render height so a 4K-fullscreen window doesn't render an enormous
        // scene + bloom every frame. The legacy art is authored at 600px tall and the
        // hi-res art (title/splash) is well under this once fitted, so beyond the cap
        // the present blit's bilinear upscale is imperceptible while the fill-rate /
        // VRAM cost (a 4x target is 16x the pixels) is bounded. Tunable.
        public const int MaxHeight = 1440;

        // Current render-target resolution (the 4:3 letterbox size, capped).
        public static int Width { get; private set; } = DesignWidth;
        public static int Height { get; private set; } = DesignHeight;

        // Raw window back-buffer size — what Game1.Draw's present blit letterboxes the
        // scene INTO. Distinct from Width/Height (the capped internal render-target size):
        // the on-screen letterbox geometry uses the *uncapped* window size, so the
        // window->design mouse mapping below must too. See WindowToDesign.
        public static int WindowWidth { get; private set; } = DesignWidth;
        public static int WindowHeight { get; private set; } = DesignHeight;

        // Nominal design->render scalar (Width/800 ~= Height/600). Kept for any code
        // that needs a single factor; the Matrix below maps each axis exactly.
        public static float Scale { get; private set; } = 1f;

        // design coords -> render coords. Per-axis so the design corners (800,600) map
        // exactly onto (Width,Height) with no sub-pixel seam at the screen edge.
        public static Matrix Matrix { get; private set; } = Matrix.Identity;

        // Recompute from the current window back-buffer size. Cheap; safe to call every
        // frame. Each size-dependent target re-checks its own size against Width/Height,
        // so this just refreshes the shared state.
        public static void Update(int windowWidth, int windowHeight)
        {
            // Track the raw window size every call (even when the rounded render size is
            // unchanged) so WindowToDesign always inverts the present blit exactly.
            WindowWidth = windowWidth > 0 ? windowWidth : DesignWidth;
            WindowHeight = windowHeight > 0 ? windowHeight : DesignHeight;
            float s = Math.Min((float)windowWidth / DesignWidth, (float)windowHeight / DesignHeight);
            if (s <= 0f || float.IsNaN(s) || float.IsInfinity(s))
            {
                s = 1f;
            }
            int h = (int)Math.Round((double)(DesignHeight * s));
            if (h > MaxHeight)
            {
                h = MaxHeight;
                s = (float)h / DesignHeight;
            }
            int w = (int)Math.Round((double)(DesignWidth * s));
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            if (w == Width && h == Height)
            {
                return;
            }
            Width = w;
            Height = h;
            Scale = s;
            Matrix = Matrix.CreateScale((float)w / DesignWidth, (float)h / DesignHeight, 1f);
        }

        // Uncapped design->window fit scalar (min axis, so the 4:3 design fits inside the
        // window with letterbox bars). Shared by WindowDestRect and WindowToDesign so the
        // present blit and the inverse mouse mapping round identically. Distinct from Scale
        // above (the CAPPED internal render-target scalar); the on-screen letterbox is not
        // capped, it always fills the window's short axis.
        private static float FitScale(int windowWidth, int windowHeight)
        {
            float s = Math.Min((float)windowWidth / DesignWidth, (float)windowHeight / DesignHeight);
            if (s <= 0f || float.IsNaN(s) || float.IsInfinity(s))
            {
                s = 1f;
            }
            return s;
        }

        // The on-screen letterbox rectangle: where sceneTarget is blitted inside a window of
        // the given back-buffer size (centered, uncapped 4:3 fit, rounded). The SINGLE source
        // of truth for the present blit in Game1.Draw AND the inverse mouse mapping below, so
        // the two can't drift (they used to hand-keep the same math with different rounding —
        // Game1 truncated, this rounds).
        public static Rectangle WindowDestRect(int windowWidth, int windowHeight)
        {
            float s = FitScale(windowWidth, windowHeight);
            int destW = (int)Math.Round((double)(DesignWidth * s));
            int destH = (int)Math.Round((double)(DesignHeight * s));
            if (destW < 1) destW = 1;
            if (destH < 1) destH = 1;
            return new Rectangle((windowWidth - destW) / 2, (windowHeight - destH) / 2, destW, destH);
        }

        // Map a window/back-buffer pixel (where the browser reports the mouse, full
        // viewport — see wwwroot/index.html: clientX/clientY into a 100vw/100vh canvas)
        // back into 800x600 design space. This is the EXACT inverse of the letterbox
        // present blit in Game1.Draw — it inverts the very same WindowDestRect — so a
        // click on the ship's target lands on the matching design coord. Without this the
        // mouse arrives in window pixels (Stage 10's presenter makes the back buffer the
        // browser-window size, not 800x600), so PlayerShip's mouse-fire aims at the wrong
        // point and the software cursor sits in the wrong place.
        public static Vector2 WindowToDesign(Vector2 windowPos)
        {
            Rectangle dest = WindowDestRect(WindowWidth, WindowHeight);
            float sx = (float)dest.Width / DesignWidth;
            float sy = (float)dest.Height / DesignHeight;
            if (sx <= 0f) sx = 1f;
            if (sy <= 0f) sy = 1f;
            return new Vector2((windowPos.X - dest.X) / sx, (windowPos.Y - dest.Y) / sy);
        }
    }
}
