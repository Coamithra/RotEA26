// ---------------------------------------------------------------------------
// XNA 3.x -> XNA 4.0 compatibility shims.
//
// The game was written against XNA Game Studio 3.x. KNI follows the XNA 4.0
// API, which made several breaking changes. These shims re-introduce the few
// 3.x constructs the game uses so the original code compiles unchanged:
//
//   * SpriteBlendMode / SaveStateMode enums + SpriteBatch.Begin overloads
//     (4.0 replaced these with BlendState objects).
//   * ResolveTexture2D + GraphicsDevice.ResolveBackBuffer
//     (4.0 replaced these with RenderTarget2D).
// ---------------------------------------------------------------------------
using Microsoft.Xna.Framework;

// Declared in the XNA Graphics namespace so the original game files (which already
// `using Microsoft.Xna.Framework.Graphics;`) pick these up with no edits.
namespace Microsoft.Xna.Framework.Graphics
{
    // XNA 3.x sprite blend modes (4.0 replaced these with BlendState).
    public enum SpriteBlendMode
    {
        None = 0,
        AlphaBlend = 1,
        Additive = 2,
    }

    // XNA 3.x device-state save mode (4.0 removed it; Begin manages state itself).
    public enum SaveStateMode
    {
        None = 0,
        SaveState = 1,
    }

    public static class Xna3SpriteBatchCompat
    {
        // TODO(visual): confirm the SpriteBlendMode int->BlendState mapping against
        // the original once content renders. AlphaBlend is the safe default for 2D.
        private static BlendState ToBlendState(SpriteBlendMode mode)
        {
            switch (mode)
            {
                case SpriteBlendMode.Additive: return BlendState.Additive;
                case SpriteBlendMode.None:     return BlendState.Opaque;
                case SpriteBlendMode.AlphaBlend:
                default:                       return BlendState.AlphaBlend;
            }
        }

        public static void Begin(this SpriteBatch sb, SpriteBlendMode blendMode)
            => sb.Begin(SpriteSortMode.Deferred, ToBlendState(blendMode));

        public static void Begin(this SpriteBatch sb, SpriteBlendMode blendMode,
            SpriteSortMode sortMode, SaveStateMode stateMode)
            => sb.Begin(sortMode, ToBlendState(blendMode));

        public static void Begin(this SpriteBatch sb, SpriteBlendMode blendMode,
            SpriteSortMode sortMode, SaveStateMode stateMode, Matrix transformMatrix)
            => sb.Begin(sortMode, ToBlendState(blendMode), null, null, null, null, transformMatrix);
    }

    // XNA 3.x back-buffer resolve target (4.0 replaced with RenderTarget2D).
    // It must BE a RenderTarget2D (not just a Texture2D) so ResolveBackBuffer can
    // render the current scene into it (Stage 5: bloom needs the scene as a texture).
    public class ResolveTexture2D : RenderTarget2D
    {
        public ResolveTexture2D(GraphicsDevice graphicsDevice, int width, int height,
            int numberLevels, SurfaceFormat format)
            : base(graphicsDevice, width, height, numberLevels > 1, format, DepthFormat.None)
        {
        }
    }

    public static class Xna3GraphicsDeviceCompat
    {
        // Stage-4 presenter target (see Game1.Draw). The game is authored at a fixed
        // 800x600, but KNI's BlazorGL backend forces the back buffer to the browser
        // window size and rewrites PreferredBackBuffer on every resize (GameWindow.
        // OnResize -> UpdateBackBufferSize). So Game1 renders the whole frame into an
        // 800x600 RenderTarget and blits it scaled+letterboxed to the window. The game
        // returns to "the back buffer" via SetRenderTarget(0, null) in many places
        // (Background, MenuScene, MenuSub1, ...); when a presenter target is active we
        // redirect those nulls to it, so the entire frame composites at 800x600.
        // Game1 sets this around its frame; null = the real back buffer (default).
        public static RenderTarget2D BaseRenderTarget;

        // Stage 5: snapshot the current scene (the active presenter target,
        // BaseRenderTarget) into 'target' so post-process effects (bloom) have the
        // scene as a sampleable texture. XNA 3.x's ResolveBackBuffer copied the back
        // buffer; here we blit the presenter target with a private SpriteBatch, then
        // restore it as the render target (it preserves contents).
        private static SpriteBatch _resolveBatch;

        public static void ResolveBackBuffer(this GraphicsDevice device, ResolveTexture2D target)
        {
            RenderTarget2D src = BaseRenderTarget;
            if (target == null || src == null)
                return;
            if (_resolveBatch == null)
                _resolveBatch = new SpriteBatch(device);
            device.SetRenderTarget(target);
            _resolveBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, null, null);
            _resolveBatch.Draw(src, new Rectangle(0, 0, target.Width, target.Height), Color.White);
            _resolveBatch.End();
            device.SetRenderTarget(src);
        }

        // XNA 3.x had multiple render targets addressed by index; 4.0 dropped the index.
        // A null target means "the back buffer" — redirected to the presenter target when
        // one is active (see BaseRenderTarget above).
        public static void SetRenderTarget(this GraphicsDevice device, int renderTargetIndex, RenderTarget2D renderTarget)
            => device.SetRenderTarget(renderTarget ?? BaseRenderTarget);
    }

    public static class Xna3RenderTargetCompat
    {
        // XNA 3.x: RenderTarget2D.GetTexture(). 4.0: RenderTarget2D IS a Texture2D.
        public static Texture2D GetTexture(this RenderTarget2D renderTarget) => renderTarget;
    }

    // XNA 3.x effects used Begin()/End() around passes; 4.0 uses EffectPass.Apply().
    // No-ops keep the original draw code compiling/running; the real shader work
    // (calling pass.Apply()) is handled when shaders are ported in a later stage.
    public static class Xna3EffectCompat
    {
        public static void Begin(this Effect effect) { }
        public static void End(this Effect effect) { }
        public static void Begin(this EffectPass pass) { }
        public static void End(this EffectPass pass) { }
    }

    // XNA 3.x graphics types removed in 4.0; declared so references compile.
    public enum TextureUsage { None = 0, AutoGenerateMipMap = 1, Linear = 2, Tiled = 4 }
    public enum ShaderProfile { PS_1_1, PS_1_2, PS_1_3, PS_1_4, PS_2_0, PS_2_A, PS_2_B, PS_3_0, XPS_3_0, Unknown }
    public class EffectPool { }
}
