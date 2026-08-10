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
        // The WINNING element (owner request, lap 11): the candidate whose force is actually
        // pushing the ship this tick draws bright yellow, so circle-vs-triangle competition is
        // visible instead of inferred. Relative to the first live ship in the pass.
        private static readonly Color WinColor = new Color(1f, 0.9f, 0.15f);
        // The T4 do-not-shoot marker (owner request, lap 12): a big X over every UFO the fire
        // rules currently protect -- a spared slot or a mid-charge platform during the fight.
        private static readonly Color SpareColor = new Color(1f, 0.15f, 0.15f);
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
            PlayerShip ship = null;
            for (int i = 0; i < collidables.Count; i++)
            {
                if (collidables[i] is PlayerShip ps && !ps.IsDead)
                {
                    ship = ps;
                    break;
                }
            }
            bool spiderBossAlive = false;
            for (int i = 0; i < collidables.Count; i++)
            {
                if (collidables[i] is SpiderBoss boss && !boss.IsDead)
                {
                    spiderBossAlive = true;
                    break;
                }
            }
            SpriteBlendMode prev = sb.BlendMode;
            sb.BlendMode = (SpriteBlendMode)1; // NonPremultiplied, like HitboxOverlay
            for (int i = 0; i < collidables.Count; i++)
            {
                if (!(collidables[i] is AlienDrawableGameComponent adc))
                {
                    continue;
                }
                // The do-not-shoot X, drawn whether or not the mover describes a shape -- the
                // protection is a FIRE rule and holds for a parked platform too.
                if (ship != null && ship.IsSpareProtected(adc, spiderBossAlive, out float xRadius))
                {
                    Color xCol = new Color(SpareColor, Alpha);
                    Vector2 c = adc.Position;
                    Vector2 arm = new Vector2(xRadius * 0.7071f, xRadius * 0.7071f);
                    DrawLine(sb, c - arm, c + arm, xCol);
                    DrawLine(sb, new Vector2(c.X - arm.X, c.Y + arm.Y), new Vector2(c.X + arm.X, c.Y - arm.Y), xCol);
                }
                if (!PlayerShip.TryDescribeSweptShape(adc, out Vector2 anchor, out float radius, out Vector2 apex))
                {
                    continue;
                }
                // 0 = nothing pushing from this object right now, 1 = circle/body wins,
                // 2 = triangle wins -- the winning element draws in WinColor.
                int winner = PlayerShip.SweptShapeWinnerAt(ship, adc);
                Color col = new Color(ShapeColor, Alpha);
                Color circleCol = (winner == 1) ? new Color(WinColor, Alpha) : col;
                Color triCol = (winner == 2) ? new Color(WinColor, Alpha) : col;
                Vector2 axis = apex - anchor;
                float len = axis.Length();
                // A STATIONARY box-threat draws the BOX the field now measures from (owner catch,
                // lap 8 -- the circle approximation lied about the StationaryBoss's offset, skirt
                // and flatness, which is what caused the accidents). Movers keep the circle: the
                // capsule the steering evaluates really is circle-based.
                if (len < 1f && adc.GetCollisionType() is CollisionBox stillBox)
                {
                    DrawBoxOutline(sb, stillBox, circleCol);
                    continue;
                }
                if (len < 1f && adc.GetCollisionType() is CollisionMultibox stillMulti)
                {
                    foreach (CollisionBox item in stillMulti.Items)
                    {
                        DrawBoxOutline(sb, item, circleCol);
                    }
                    continue;
                }
                // The body circle (the ring texture's bright band sits on the true radius).
                sb.Draw(ring, anchor, 0f, radius / 64f, center: true, circleCol, (SpriteEffects)0);
                // The triangle: perpendicular-diameter corners to the apex, base included for
                // legibility (the force skips the base edge on the circle-dominance proof, but
                // the eye wants the closed shape).
                if (len < 1f)
                {
                    continue;
                }
                axis /= len;
                Vector2 perp = new Vector2(0f - axis.Y, axis.X);
                Vector2 c1 = anchor + perp * radius;
                Vector2 c2 = anchor - perp * radius;
                DrawLine(sb, c1, apex, triCol);
                DrawLine(sb, c2, apex, triCol);
                DrawLine(sb, c1, c2, triCol);
            }
            sb.BlendMode = prev;
        }

        private static void DrawBoxOutline(SpriteBatchWrapper sb, CollisionBox box, Color col)
        {
            Vector2 tl = new Vector2(box.Left, box.Top);
            Vector2 tr = new Vector2(box.Right, box.Top);
            Vector2 br = new Vector2(box.Right, box.Bottom);
            Vector2 bl = new Vector2(box.Left, box.Bottom);
            DrawLine(sb, tl, tr, col);
            DrawLine(sb, tr, br, col);
            DrawLine(sb, br, bl, col);
            DrawLine(sb, bl, tl, col);
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
