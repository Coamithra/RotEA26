using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    // ?cones / eaCones() debug overlay (owner request, iterative rep 1 lap 5): draws every
    // mover's SWEPT REPULSION SHAPE over the composited frame -- the body circle plus the
    // triangle to `position + velocity * lead`, exactly as the AI's steering evaluates it this
    // tick. The geometry comes from PlayerShip.TryDescribeSweptShape, the same code the force
    // uses, so the picture cannot drift from the behaviour; a mover the steering skips
    // (stationary, refused teleport path, ?aicone=0) draws nothing, which is itself the
    // information.
    //
    // The HitboxOverlay pattern verbatim: drawn from Game1.DrawInner after game + bloom, in
    // 800x600 design space through the SpriteBatchWrapper; OUT of DebugFlags.Active, so a
    // shipped build is unaffected unless ?cones is on the URL or eaCones(true) is called.
    internal static class ConeOverlay
    {
        // Magenta-red, distinct from every HitboxOverlay tint (box cyan / circle green /
        // line orange) so both overlays can run at once and stay tellable-apart.
        private static readonly Color ShapeColor = new Color(1f, 0.3f, 0.55f);
        private const float Alpha = 0.85f;
        private const int Thickness = 2; // design-space px

        private static Texture2D pixel;
        private static Texture2D ring;

        public static void Draw(GraphicsDevice gd, SpriteBatchWrapper sb, IReadOnlyList<ICollidable> collidables)
        {
            if (collidables == null || collidables.Count == 0)
            {
                return;
            }
            EnsureTextures(gd);
            SpriteBlendMode prev = sb.BlendMode;
            sb.BlendMode = (SpriteBlendMode)1; // NonPremultiplied, like HitboxOverlay
            for (int i = 0; i < collidables.Count; i++)
            {
                if (!(collidables[i] is AlienDrawableGameComponent adc))
                {
                    continue;
                }
                if (!PlayerShip.TryDescribeSweptShape(adc, out Vector2 anchor, out float radius, out Vector2 apex))
                {
                    continue;
                }
                Color col = new Color(ShapeColor, Alpha);
                // The body circle (the ring texture's bright band sits on the true radius).
                sb.Draw(ring, anchor, 0f, radius / 64f, center: true, col, (SpriteEffects)0);
                // The triangle: perpendicular-diameter corners to the apex, base included for
                // legibility (the force skips the base edge on the circle-dominance proof, but
                // the eye wants the closed shape).
                Vector2 axis = apex - anchor;
                float len = axis.Length();
                if (len < 1f)
                {
                    continue;
                }
                axis /= len;
                Vector2 perp = new Vector2(0f - axis.Y, axis.X);
                Vector2 c1 = anchor + perp * radius;
                Vector2 c2 = anchor - perp * radius;
                DrawLine(sb, c1, apex, col);
                DrawLine(sb, c2, apex, col);
                DrawLine(sb, c1, c2, col);
            }
            sb.BlendMode = prev;
        }

        private static void DrawLine(SpriteBatchWrapper sb, Vector2 start, Vector2 end, Color col)
        {
            Vector2 d = end - start;
            float len = d.Length();
            if (len < 0.5f)
            {
                return;
            }
            float rot = (float)Math.Atan2(d.Y, d.X);
            Vector2 perp = new Vector2(-d.Y, d.X) / len;
            Vector2 pos = start - perp * (Thickness * 0.5f);
            sb.Draw(pixel, pos, rot, new Vector2(len, Thickness), center: false, col);
        }

        private static void EnsureTextures(GraphicsDevice gd)
        {
            if (pixel == null || ((GraphicsResource)pixel).IsDisposed)
            {
                pixel = new Texture2D(gd, 1, 1);
                pixel.SetData(new[] { Color.White });
            }
            if (ring == null || ((GraphicsResource)ring).IsDisposed)
            {
                ring = BuildRing(gd);
            }
        }

        // Same 128x128 annulus recipe as HitboxOverlay.BuildRing (band hugs the outer edge, so
        // scale radius/64 puts the bright ring on the true radius). Kept private per overlay --
        // two tiny textures beat a shared-internals coupling.
        private static Texture2D BuildRing(GraphicsDevice gd)
        {
            const int size = 128;
            const float half = size / 2f;
            const float inner = 0.9f;
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r >= inner && r <= 1f)
                    {
                        float t = (r - inner) / (1f - inner);
                        a = (float)Math.Sin(t * Math.PI);
                    }
                    data[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            var tex = new Texture2D(gd, size, size);
            tex.SetData(data);
            return tex;
        }
    }
}
