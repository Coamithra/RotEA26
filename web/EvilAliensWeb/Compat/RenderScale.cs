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
    }
}
