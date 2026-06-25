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
// avoids matching the neighbours it overlaps, which is the "no direct repeats" rule
// expressed positionally so it survives infinite scroll.
//
// The rows are laid BRICK-style (running bond): every other row is shifted half a
// tile horizontally, like staggered masonry, so the seams don't line up into long
// straight grout lines. This stays perfectly seamless for free: starwindow.fx's
// window is SEPARABLE (w = wx*wy) and each row is its own independent 1D grid whose
// horizontal windows already sum to 1 at every x — so a per-row horizontal phase
// shift is invisible to the total brightness, while vertically the rows still stack
// and sum to 1 exactly as before. Net result: uniform brightness 1 everywhere, only
// the seams stagger. (Pass brick:false for the old aligned grid.)
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

    // Brick-laying (running-bond) layout: shift every other ROW horizontally by half a
    // tile pitch, like staggered masonry, instead of a plain aligned grid. Seamless —
    // see the class header for why the separable window makes the row phase irrelevant
    // to total brightness. false = the original aligned grid.
    private readonly bool brick;

    // The tile set this field draws from, and how many. Default (and only current caller)
    // = the 12 far nebula tiles; the parameter stays so a different tiled field is a one-
    // liner. (The near foreground stars are the separate discrete DriftingStars layer.)
    private readonly string[] tilePaths;
    private readonly int tileCount;

    // Scroll-speed multiplier applied to every Advance() delta. 1 = same rate as the
    // far field; >1 = a nearer layer that streaks past faster (parallax depth).
    private readonly float parallax;

    private Texture2D[] tiles;
    private Effect window;
    private SpriteBatch batch;
    private GraphicsDevice gd;

    // Scroll position in DESIGN units; advanced by Advance(). Accumulated UNBOUNDED as double
    // and never wrapped: the per-cell pattern (Pick/Hash) is not periodic, so wrapping the
    // scroll by any fixed span would teleport the visible field at the wrap boundary. (Contrast
    // DriftingStars, which CAN wrap its float field: its star set really is periodic.) double
    // plus the per-scene SetSpace() recreation keep Draw()'s float math sub-pixel for any
    // realistic scene runtime. See Advance for the full story.
    private double scrollDesignX;
    private double scrollDesignY;

    // Live brightness multiplier (the ?fx tuning panel drives this via Background). Applied
    // to the draw alpha so it scales each tile's additive contribution linearly (1 = full).
    public float Brightness = 1f;

    public ProceduralStarfield(uint seed = 1337u, float tileDesignH = 480f, float feather = 0.16f,
                               Color? tint = null, bool brick = true,
                               string[] tilePaths = null, float parallax = 1f)
    {
        this.seed = seed;
        this.tileDesignH = tileDesignH;
        this.tileDesignW = tileDesignH * (4f / 3f); // tiles are 4:3
        this.feather = Math.Clamp(feather, 0.02f, 0.49f);
        this.tint = tint ?? Color.White;
        this.brick = brick;
        this.parallax = parallax;
        // Default tile set = the 12 full-frame nebula tiles (the far/background field).
        if (tilePaths == null)
        {
            tilePaths = new string[12];
            for (int i = 0; i < 12; i++) tilePaths[i] = $"GFX/Game/space/space{i:00}";
        }
        this.tilePaths = tilePaths;
        this.tileCount = tilePaths.Length;
    }

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        gd = graphicsDevice;
        batch = new SpriteBatch(gd);
        window = content.Load<Effect>("GFX/Effects/starwindow");
        tiles = new Texture2D[tileCount];
        for (int i = 0; i < tileCount; i++)
            tiles[i] = content.Load<Texture2D>(tilePaths[i]);
    }

    // Advance the field by a per-frame scroll delta (design units), matching how
    // Background moves its layers (scrollspeed * elapsedMs * scrollspeedmodifier).
    public void Advance(Vector2 designDelta)
    {
        // Subtract: the draw maps cell -> screen as (cell*pitch - scroll), so a positive
        // scrollspeed must DECREASE the offset to move the field the same way the legacy
        // BackgroundImage.Move did (which added to a position drawn top-down). Adding here
        // scrolled the field the exact opposite direction.
        //
        // Do NOT wrap (mod) the accumulator. The old code wrapped to `pitch * tileCount * 4`,
        // assuming the field repeats over that span; but the pattern is keyed by a NON-periodic
        // per-cell Hash, so Hash(cx) != Hash(cx + tileCount*4): crossing a wrap boundary shifts
        // every cell index by a whole period and instantly swaps the entire visible nebula. The
        // worst case is the X axis, which stays 0 until a scene first adds a horizontal speed
        // (level 1's asteroid section): the first sub-zero step wrapped X from 0 to ~modulus
        // (MyMath.Mod returns [0, m)), so the field visibly jumped the instant it scrolled
        // sideways. (Y wrapped the same way on frame 1 of a level, but the hyperspace fade-in's
        // near-opaque white overlay covers it.) Accumulating as double, with the field recreated
        // each SetSpace(), keeps Draw()'s float math sub-pixel for any realistic scene; no wrap.
        scrollDesignX -= (double)designDelta.X * parallax;
        scrollDesignY -= (double)designDelta.Y * parallax;
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
        Vector2 scrollR = new Vector2((float)(scrollDesignX * scale), (float)(scrollDesignY * scale));

        // Cell index range whose tiles (centred at cx*pitchRX - scrollR) touch the
        // screen, plus a one-tile margin so every visible pixel has full sum-to-1
        // coverage (no darkened edge).
        int cx0 = (int)Math.Floor((scrollR.X - tileRW) / pitchRX);
        int cx1 = (int)Math.Ceiling((scrollR.X + rw + tileRW) / pitchRX);
        int cy0 = (int)Math.Floor((scrollR.Y - tileRH) / pitchRY);
        int cy1 = (int)Math.Ceiling((scrollR.Y + rh + tileRH) / pitchRY);
        // brick rows shift up to half a pitch sideways, so widen the column range by
        // one cell each side to guarantee full edge coverage for the shifted rows.
        if (brick) { cx0--; cx1++; }

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
                // running-bond: odd rows are nudged half a pitch to the right.
                float rowShift = (brick && (cy & 1) != 0) ? pitchRX * 0.5f : 0f;
                Vector2 center = new Vector2(cx * pitchRX + rowShift - scrollR.X, cy * pitchRY - scrollR.Y);
                Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);
                batch.Draw(tex, center, null, drawColor, 0f, origin, sTex, fx, 0f);
            }
        }
        batch.End();
    }

    // Deterministic per-cell choice of (texture index, mirror), avoiding a direct
    // repeat against the neighbours this cell actually overlaps (their base pick).
    // Pure function of (cx, cy, seed) -> identical every run, infinite scroll.
    private void Pick(int cx, int cy, out int idx, out SpriteEffects fx)
    {
        // The two cells in the row above that this cell straddles. In a plain grid the
        // overlap is the cell directly above (up) plus up-left; in brick layout the
        // half-pitch row stagger shifts which pair it is, by row parity.
        int upA, upB;
        if (brick && (cy & 1) == 0) { upA = cx - 1; upB = cx; }      // even row beneath right-shifted odd row
        else if (brick) { upA = cx; upB = cx + 1; }                  // odd row beneath even row
        else { upA = cx - 1; upB = cx; }                             // aligned grid: up-left + up

        idx = (int)(Hash(cx, cy, seed) % tileCount);
        int salt = 1;
        while (salt <= 8 && (idx == BaseIdx(cx - 1, cy) || idx == BaseIdx(upA, cy - 1) || idx == BaseIdx(upB, cy - 1)))
        {
            idx = (int)(Hash(cx, cy, seed ^ (uint)(salt * 0x9E3779B9)) % tileCount);
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

    private int BaseIdx(int cx, int cy) => (int)(Hash(cx, cy, seed) % tileCount);

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
