using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

// DriftingStars — the near (foreground) star layer: a handful of INDIVIDUAL stars drawn
// additively over the nebula, each with its OWN scroll speed, scale and twinkle. Replaces
// the old uniform near tile sheet, where every star moved at one speed and read as a flat
// moving wall. The dense background stars live in the far nebula tiles (ProceduralStarfield);
// these are the few prominent foreground stars whose varied speeds sell the parallax depth.
//
// The star sprites are cut from the space_near tiles by tools/textures/extract_stars.py
// (glow on black, soft-vignetted) so additive blending has no hard edge. Stars are placed in
// 800x600 DESIGN space (grown by a margin so they wrap without popping at the border) and
// drawn in render-space pixels at native scale (like ProceduralStarfield) so they stay crisp.
internal sealed class DriftingStars : IDisposable
{
    private struct Star
    {
        public int tex;          // sprite index
        public Vector2 pos;      // position in the wrapped (margin-grown) design field
        public float parallax;   // scroll-speed multiplier — per-star, so speeds differ
        public float scale;      // design px per texture px
        public float baseBright; // additive brightness baseline
        public float phase;      // twinkle phase (rad)
        public float rate;       // twinkle rate (rad/ms)
        public float depth;      // twinkle amplitude (0..1)
    }

    // The field is the 800x600 design rect grown by Margin on every side, so a star wraps
    // off one edge and re-enters the opposite one entirely outside the visible area.
    private const float Margin = 120f;
    private const float FieldW = 800f + 2f * Margin;
    private const float FieldH = 600f + 2f * Margin;

    private Texture2D[] sprites;
    private SpriteBatch batch;
    private GraphicsDevice gd;
    private Star[] stars;
    private float clockMs;

    // Master brightness (the ?fx tuning panel drives this via Background); multiplies every
    // star's additive contribution. The per-star twinkle rides on top of it.
    public float Brightness = 1f;

    private readonly int count;
    private readonly int spriteCount;
    private readonly uint seed;

    public DriftingStars(int count = 28, int spriteCount = 8, uint seed = 20260625u)
    {
        this.count = count;
        this.spriteCount = spriteCount;
        this.seed = seed;
    }

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        gd = graphicsDevice;
        batch = new SpriteBatch(gd);
        sprites = new Texture2D[spriteCount];
        for (int i = 0; i < spriteCount; i++)
            sprites[i] = content.Load<Texture2D>($"GFX/Game/space/star{i:00}");

        // Local RNG (constant seed): a stable, reproducible layout that does NOT perturb the
        // global RandomHelper sequence gameplay draws from.
        Random rng = new Random((int)seed);
        stars = new Star[count];
        for (int i = 0; i < count; i++)
        {
            // Speed is STRATIFIED across the whole range (one star per slot, + jitter inside
            // the slot), so every star gets a distinct parallax and the full spread is always
            // covered — uniform-random would clump and leave gaps. Crucially speed is DECOUPLED
            // from size/brightness: correlating them (nearer=faster=bigger=brighter) made the
            // slow stars small+dim+invisible, so every star you could actually see was a fast
            // one and they all looked the same speed. Now prominent stars occur at every speed.
            float speedT = (i + (float)rng.NextDouble()) / count;          // 0..1, one per slot
            stars[i] = new Star
            {
                tex = rng.Next(spriteCount),
                pos = new Vector2((float)rng.NextDouble() * FieldW, (float)rng.NextDouble() * FieldH),
                parallax = MathHelper.Lerp(1.3f, 3.8f, speedT),            // wide spread, ~3x fastest/slowest
                scale = MathHelper.Lerp(0.12f, 0.26f, (float)rng.NextDouble()),       // independent
                baseBright = MathHelper.Lerp(0.7f, 1.0f, (float)rng.NextDouble()),    // independent, all visible
                phase = (float)(rng.NextDouble() * Math.PI * 2.0),
                rate = (float)(Math.PI * 2.0 / MathHelper.Lerp(1600f, 4200f, (float)rng.NextDouble())), // 1.6-4.2s
                depth = MathHelper.Lerp(0.25f, 0.6f, (float)rng.NextDouble()),
            };
        }
    }

    // baseDelta = the scene scroll delta (design units) this frame (same value the far field
    // gets); elapsedMs = real elapsed time, driving each star's twinkle clock.
    public void Advance(Vector2 baseDelta, float elapsedMs)
    {
        if (stars == null) return;
        clockMs += elapsedMs;
        for (int i = 0; i < count; i++)
        {
            stars[i].pos += baseDelta * stars[i].parallax;
            stars[i].pos.X = MyMath.Mod(stars[i].pos.X, FieldW);
            stars[i].pos.Y = MyMath.Mod(stars[i].pos.Y, FieldH);
        }
    }

    public void Draw()
    {
        if (sprites == null) return;
        float s = RenderScale.Scale;
        batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        for (int i = 0; i < count; i++)
        {
            ref Star st = ref stars[i];
            // un-margin to design coords, then design -> render px (identity-matrix batch).
            Vector2 rpos = new Vector2(st.pos.X - Margin, st.pos.Y - Margin) * s;
            float tw = (1f - st.depth) + st.depth * (0.5f + 0.5f * (float)Math.Sin(st.phase + st.rate * clockMs));
            float a = MathHelper.Clamp(Brightness * st.baseBright * tw, 0f, 1f);
            Texture2D tex = sprites[st.tex];
            Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);
            batch.Draw(tex, rpos, null, new Color(new Vector4(1f, 1f, 1f, a)), 0f, origin,
                st.scale * s, SpriteEffects.None, 0f);
        }
        batch.End();
    }

    public void Dispose()
    {
        // sprite textures are ContentManager-cached/shared — only the SpriteBatch is ours.
        batch?.Dispose();
        batch = null;
        sprites = null;
        stars = null;
    }
}
