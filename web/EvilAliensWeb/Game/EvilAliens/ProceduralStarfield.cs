using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

// ProceduralStarfield — the new space background (replaces SetSpace's three
// hand-placed BackgroundImage layers).
//
// A deterministic, infinite, scrolling GRID of overlapping nebula/star tiles. Each
// grid cell deterministically picks one of N tile textures and a mirror variant
// (none / H / V / HV — NO rotation: the tiles are 4:3, so rotation would complicate
// both the geometry and the crossfade; mirroring keeps everything axis-aligned).
// The pick is a pure function of (cellX, cellY, seed), so the field is identical
// every run AND infinite as it scrolls — no stored grid, no wrap seam. A cell also
// avoids matching its already-decided left / up / up-left neighbours, which is the
// "no direct repeats" rule expressed positionally so it survives infinite scroll.
//
// Tiles overlap by `overlap` and are drawn ADDITIVELY into the black-cleared scene
// through starwindow.fx, which multiplies each tile by a separable, mirror-symmetric
// window that sums to 1 across every overlap. Additive accumulation of weights that
// sum to 1 is a convex blend (a true crossfade) -> uniform brightness, seamless, no
// double-exposure where tiles meet. See tools/shaders/src/starwindow.fx.
//
// Everything is authored in 800x600 DESIGN units (so the look is identical at any
// window size) but DRAWN in render-space pixels at native scale (identity transform,
// not the design->render upscale the rest of the background uses) so the high-res
// tiles stay crisp instead of being upscaled through the 800x600 grid.
internal sealed class ProceduralStarfield : IDisposable
{
    private const int TileCount = 12;

    // --- the two aesthetic knobs (design units) ---------------------------------
    // tileDesignH = on-screen height of ONE tile in 800x600 design space. 600 = a
    // tile fills the screen height (few, large tiles); smaller = a denser grid of
    // smaller tiles / more variety. Width is derived from the texture's 4:3 aspect.
    // feather = crossfade-band width as a FRACTION of each tile (uniform on both axes),
    // so the blend look is resolution-independent. ~0.16 is roughly the 200px-on-a-1448px
    // demo blend. Constraint: feather < 0.5 (so a flat full-strength centre survives).
    private readonly float tileDesignH;
    private readonly float tileDesignW;
    private readonly float feather;
    private readonly uint seed;
    private readonly Color tint;

    private Texture2D[] tiles;
    private Effect window;
    private SpriteBatch batch;
    private GraphicsDevice gd;

    // Scroll position in DESIGN units; advanced by Advance(), wrapped to the grid
    // pitch so it never loses float precision over a long session (the pattern is
    // keyed by integer cell coords, so wrapping by a whole pitch keeps it aligned).
    private Vector2 scrollDesign;

    // Live brightness multiplier (the ?fx tuning panel drives this via Background). Applied
    // to the draw alpha so it scales each tile's additive contribution linearly (1 = full).
    public float Brightness = 1f;

    public ProceduralStarfield(uint seed = 1337u, float tileDesignH = 480f, float feather = 0.16f, Color? tint = null)
    {
        this.seed = seed;
        this.tileDesignH = tileDesignH;
        this.tileDesignW = tileDesignH * (1448f / 1086f); // tiles are 4:3
        this.feather = Math.Clamp(feather, 0.02f, 0.49f);
        this.tint = tint ?? Color.White;
    }

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        gd = graphicsDevice;
        batch = new SpriteBatch(gd);
        window = content.Load<Effect>("GFX/Effects/starwindow");
        tiles = new Texture2D[TileCount];
        for (int i = 0; i < TileCount; i++)
            tiles[i] = content.Load<Texture2D>($"GFX/Game/space/space{i:00}");
    }

    // Advance the field by a per-frame scroll delta (design units), matching how
    // Background moves its layers (scrollspeed * elapsedMs * scrollspeedmodifier).
    public void Advance(Vector2 designDelta)
    {
        scrollDesign += designDelta;
        // keep within one pitch to preserve precision; pattern is per-cell so this
        // is invisible.
        float px = tileDesignW * (1f - feather), py = tileDesignH * (1f - feather);
        scrollDesign.X = MyMath.Mod(scrollDesign.X, px * TileCount * 4f);
        scrollDesign.Y = MyMath.Mod(scrollDesign.Y, py * TileCount * 4f);
    }

    public void Draw()
    {
        if (tiles == null) return;
        float scale = RenderScale.Scale;
        float rw = RenderScale.Width, rh = RenderScale.Height;
        float tileRW = tileDesignW * scale;
        float tileRH = tileDesignH * scale;
        float pitchRX = tileDesignW * (1f - feather) * scale;
        float pitchRY = tileDesignH * (1f - feather) * scale;
        Vector2 scrollR = scrollDesign * scale;

        // Cell index range whose tiles (centred at cx*pitchRX - scrollR) touch the
        // screen, plus a one-tile margin so every visible pixel has full sum-to-1
        // coverage (no darkened edge).
        int cx0 = (int)Math.Floor((scrollR.X - tileRW) / pitchRX);
        int cx1 = (int)Math.Ceiling((scrollR.X + rw + tileRW) / pitchRX);
        int cy0 = (int)Math.Floor((scrollR.Y - tileRH) / pitchRY);
        int cy1 = (int)Math.Ceiling((scrollR.Y + rh + tileRH) / pitchRY);

        window.Parameters["Feather"].SetValue(new Vector2(feather, feather));
        // Brightness rides in the draw alpha only (rgb stays 1): the shader outputs
        // alpha = window * color.a, and additive multiplies rgb by that, so the tile's
        // contribution scales linearly with Brightness (not squared).
        Vector4 tv = tint.ToVector4();
        Color drawColor = new Color(new Vector4(tv.X, tv.Y, tv.Z, tv.W * Brightness));
        batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, window, Matrix.Identity);
        for (int cy = cy0; cy <= cy1; cy++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                Pick(cx, cy, out int idx, out SpriteEffects fx);
                Texture2D tex = tiles[idx];
                float sTex = tileRW / tex.Width; // uniform: tile aspect == texture aspect
                Vector2 center = new Vector2(cx * pitchRX - scrollR.X, cy * pitchRY - scrollR.Y);
                Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);
                batch.Draw(tex, center, null, drawColor, 0f, origin, sTex, fx, 0f);
            }
        }
        batch.End();
    }

    // Deterministic per-cell choice of (texture index, mirror), avoiding a direct
    // repeat against the already-decided left / up / up-left neighbours (their base
    // pick). Pure function of (cx, cy, seed) -> identical every run, infinite scroll.
    private void Pick(int cx, int cy, out int idx, out SpriteEffects fx)
    {
        idx = (int)(Hash(cx, cy, seed) % TileCount);
        int salt = 1;
        while (salt <= 8 && (idx == BaseIdx(cx - 1, cy) || idx == BaseIdx(cx, cy - 1) || idx == BaseIdx(cx - 1, cy - 1)))
        {
            idx = (int)(Hash(cx, cy, seed ^ (uint)(salt * 0x9E3779B9)) % TileCount);
            salt++;
        }
        uint m = (Hash(cx, cy, seed ^ 0xA5A5A5A5u) >> 3) & 3u;
        fx = m switch
        {
            1u => SpriteEffects.FlipHorizontally,
            2u => SpriteEffects.FlipVertically,
            3u => SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically,
            _ => SpriteEffects.None,
        };
    }

    private int BaseIdx(int cx, int cy) => (int)(Hash(cx, cy, seed) % TileCount);

    private static uint Hash(int x, int y, uint seed)
    {
        uint h = seed + 0x9E3779B9u;
        h ^= (uint)x; h *= 0x85EBCA6Bu; h ^= h >> 13;
        h ^= (uint)y; h *= 0xC2B2AE35u; h ^= h >> 16;
        return h;
    }

    public void Dispose()
    {
        // textures + effect are ContentManager-cached/shared — do NOT dispose them;
        // only the SpriteBatch is ours.
        batch?.Dispose();
        batch = null;
        tiles = null;
    }
}
