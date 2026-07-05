using System;
using System.Collections.Generic;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    // ?hitboxes / eaHitboxes() debug overlay: draws EVERY live collidable's collision shape
    // over the composited frame, colour-coded by kind —
    //   CollisionBox / CollisionMultibox  -> rectangle outline (cyan-blue)
    //   CollisionSimpleCircle              -> ring              (green)
    //   CollisionLine                      -> segment           (orange)
    // A lot of objects offset their DRAW from their Position/collision (the landed Mars UFOs
    // and the drifting StationaryBoss nudge only their Draw, not their hitbox), so sprite and
    // hitbox drift apart with no way to see it — this makes that visible at a glance in real
    // gameplay + levels (not just the sprite harness, which only rings parked circular hitboxes).
    //
    // Drawn from Game1.DrawInner AFTER the game components + bloom have painted sceneTarget, in
    // 800x600 DESIGN space through the SpriteBatchWrapper (RenderScale.Matrix scales it to fill
    // the window-sized scene target, exactly like the HideSafeArea letterbox draw right below the
    // hook). So it composites on top of everything and shares the unified present/gamma path.
    //
    // A pure debug overlay, deliberately kept OUT of DebugFlags.Active — a shipped build is
    // unaffected unless ?hitboxes is appended to the URL or eaHitboxes(true) is called from the
    // browser console (Compat/DebugInput.Hitboxes -> DebugFlags.SetShowHitboxes).
    internal static class HitboxOverlay
    {
        // Straight-alpha tints per shape kind; active hitboxes draw bright, inactive dim
        // (Collides == false), so "this collidable is currently live" is legible too.
        private static readonly Color BoxColor = new Color(0.35f, 0.7f, 1f);
        private static readonly Color CircleColor = new Color(0.4f, 1f, 0.5f);
        private static readonly Color LineColor = new Color(1f, 0.55f, 0.2f);
        private const float ActiveAlpha = 0.85f;
        private const float InactiveAlpha = 0.35f;
        private const int Thickness = 2; // design-space px line / edge width

        private static Texture2D pixel; // 1x1 white, tinted per draw (lines + box edges)
        private static Texture2D ring;  // 128x128 annulus whose bright band hugs the outer edge

        public static void Draw(GraphicsDevice gd, SpriteBatchWrapper sb, IReadOnlyList<ICollidable> collidables)
        {
            if (collidables == null || collidables.Count == 0)
            {
                return;
            }
            EnsureTextures(gd);
            SpriteBlendMode prev = sb.BlendMode;
            sb.BlendMode = (SpriteBlendMode)1; // NonPremultiplied — crisp straight-alpha outlines
            // Index (not foreach) so a component add/remove event mid-frame can't throw; the
            // snapshot may miss/duplicate one collidable on the frame it changes — harmless here.
            for (int i = 0; i < collidables.Count; i++)
            {
                ICollidable c = collidables[i];
                if (c == null)
                {
                    continue;
                }
                ICollisionType shape = c.GetCollisionType();
                if (shape == null)
                {
                    continue;
                }
                bool active = !(c is AlienDrawableGameComponent adc) || adc.Collides;
                DrawShape(sb, shape, active);
            }
            sb.BlendMode = prev;
        }

        private static void DrawShape(SpriteBatchWrapper sb, ICollisionType shape, bool active)
        {
            switch (shape)
            {
                case CollisionBox box:
                    DrawBox(sb, box, active);
                    break;
                case CollisionMultibox multi:
                    foreach (CollisionBox b in multi.Items)
                    {
                        DrawBox(sb, b, active);
                    }
                    break;
                case CollisionSimpleCircle circle:
                    DrawCircle(sb, circle, active);
                    break;
                case CollisionLine line:
                    DrawLine(sb, line.Start, line.End, LineColor, active);
                    break;
                // CollisionLevelMap (the Level-3 wall occupancy grid) is a static field of cells,
                // not one drawable shape — intentionally skipped.
            }
        }

        private static void DrawBox(SpriteBatchWrapper sb, CollisionBox box, bool active)
        {
            Color col = new Color(BoxColor, active ? ActiveAlpha : InactiveAlpha);
            int l = (int)box.Left;
            int t = (int)box.Top;
            int w = (int)box.Width;
            int h = (int)box.Height;
            if (w <= 0 || h <= 0)
            {
                return;
            }
            // CollisionBox is always axis-aligned, so four thin dest-rect edges (no rotation).
            sb.Draw(pixel, new Rectangle(l, t, w, Thickness), col);                    // top
            sb.Draw(pixel, new Rectangle(l, t + h - Thickness, w, Thickness), col);    // bottom
            sb.Draw(pixel, new Rectangle(l, t, Thickness, h), col);                    // left
            sb.Draw(pixel, new Rectangle(l + w - Thickness, t, Thickness, h), col);    // right
        }

        private static void DrawCircle(SpriteBatchWrapper sb, CollisionSimpleCircle circle, bool active)
        {
            float radius = circle.Radius;
            if (radius <= 0.5f)
            {
                return;
            }
            Color col = new Color(CircleColor, active ? ActiveAlpha : InactiveAlpha);
            // ring is 128x128; its bright band peaks at the outer edge (half = 64px), so the true
            // hit radius maps to scale = radius / 64.
            sb.Draw(ring, circle.Position, 0f, radius / 64f, center: true, col, (SpriteEffects)0);
        }

        private static void DrawLine(SpriteBatchWrapper sb, Vector2 start, Vector2 end, Color baseColor, bool active)
        {
            Vector2 d = end - start;
            float len = d.Length();
            if (len < 0.5f)
            {
                return;
            }
            Color col = new Color(baseColor, active ? ActiveAlpha : InactiveAlpha);
            float rot = (float)Math.Atan2(d.Y, d.X);
            // 1x1 pixel scaled to (length, thickness) and rotated to the segment; offset back by
            // half-thickness along the perpendicular so the strip runs down the line's centre.
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

        // 128x128 annulus with a smooth band hugging the outer edge (same approach as
        // HarnessScene.BuildRingTexture), so drawn at scale radius/64 the bright ring sits on
        // the true collision radius. White; tinted per draw.
        private static Texture2D BuildRing(GraphicsDevice gd)
        {
            const int size = 128;
            const float half = size / 2f;
            const float inner = 0.9f; // band spans normalised radius 0.9..1.0
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
                        float t = (r - inner) / (1f - inner);      // 0..1 across the band
                        a = (float)Math.Sin(t * Math.PI);          // smooth bump, peak ~0.95 radius
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
