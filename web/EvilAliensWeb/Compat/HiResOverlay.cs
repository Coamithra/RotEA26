// ---------------------------------------------------------------------------
// HiResOverlay — the native-resolution overlay present pass.
//
// The game composites at a fixed 800x600 design resolution into Game1.sceneTarget,
// which is then scaled+letterboxed to the window. Art authored at far higher
// resolution (the menu title logo, the channel-flip splash reveal) is destroyed by
// that path: it gets downsampled to 600px tall, then upsampled back to the window
// — a double (for the title, triple) resample that throws away the resolution.
//
// Instead, a producer enqueues such art here in the SAME 800x600 design-space
// coordinates everything else uses. Game1.Draw drains the queue at PRESENT time
// (after the sceneTarget blit) and draws each request at native window resolution,
// using the exact scale+offset the presenter uses — so a design rect lines up
// pixel-perfect with the 800x600 layer, but is sampled ONCE, crisply, from the
// full-res source.
//
// The queue is per-frame: producers enqueue during their Draw (inside DrawInner);
// Game1 drains and clears it every frame in PresentHiResOverlay. An empty queue
// costs nothing. Requests draw in enqueue order (painter's order).
//
// A request may carry an optional pixel Effect + a Configure callback — that is how
// the channel-flip transition rides this same native-res path (s0 = old splash,
// the new splash bound as an effect texture parameter; see channelflip.fx).
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    // How a texture is fitted into its design-space slot.
    //   Stretch   : fill the slot exactly (use when slot aspect == texture aspect).
    //   AspectFit : scale to fit inside, preserving aspect (letter/pillarbox).
    //   Cover     : scale to cover the slot, preserving aspect (crops overflow).
    public enum OverlayFit { Stretch, AspectFit, Cover }

    public static class HiResOverlay
    {
        public struct Request
        {
            public Texture2D Texture;                    // primary texture (SpriteBatch s0)
            public Rectangle DesignRect;                 // slot in 800x600 design space
            public OverlayFit Fit;
            public Color Tint;
            public float Rotation;                       // radians, about the slot centre
            public float Scale;                          // extra uniform scale about centre (pulse)
            public Effect Effect;                        // optional custom pixel effect; null = default sprite
            public Action<Effect, Rectangle> Configure;  // set effect params; arg = the window dest rect
            public bool Glow;                            // bloom/glow this draw (when Bloom is enabled)
        }

        private static readonly List<Request> _queue = new List<Request>();

        public static IReadOnlyList<Request> Queue => _queue;

        public static void Clear() => _queue.Clear();

        // Enqueue a design-space draw. designRect is the slot in 800x600 coords;
        // rotation/scale animate about the slot centre (pulse/wobble). Pass an
        // effect + configure to run a custom pixel shader (e.g. the channel flip).
        // Premultiplied-alpha pipeline (see fix/premult-alpha-fades): tint must be
        // premultiplied — opaque Color.White is fine, for a fade use Color.White * fade
        // or Pm(); the source texture must be premultiplied too (Premultiply() converts
        // hand-made straight-alpha art like the chroma-keyed title).
        public static void Draw(Texture2D texture, Rectangle designRect, Color tint,
            OverlayFit fit = OverlayFit.Stretch, float rotation = 0f, float scale = 1f,
            Effect effect = null, Action<Effect, Rectangle> configure = null, bool glow = false)
        {
            if (texture == null)
                return;
            _queue.Add(new Request
            {
                Texture = texture,
                DesignRect = designRect,
                Tint = tint,
                Fit = fit,
                Rotation = rotation,
                Scale = (scale <= 0f) ? 1f : scale,
                Effect = effect,
                Configure = configure,
                Glow = glow,
            });
        }

        // Premultiply a straight-alpha colour (rgb *= a) for the premultiplied pipeline.
        // Use for any NON-opaque tint passed to Draw; opaque Color.White is a no-op, and
        // Color.White * fade is already premultiplied.
        public static Color Pm(Color c) =>
            new Color((byte)(c.R * c.A / 255), (byte)(c.G * c.A / 255), (byte)(c.B * c.A / 255), c.A);

        private static readonly HashSet<Texture2D> _premultiplied = new HashSet<Texture2D>();

        // Premultiply a straight-alpha texture in place (rgb *= a), once per instance.
        // The unpacked game assets are already premultiplied, but hand-made overlay art
        // (the chroma-keyed title) is straight, so it must be converted to live in the
        // same premultiplied pipeline (otherwise its flooded transparent pixels ghost
        // under premultiplied AlphaBlend). Opaque textures are unaffected.
        public static void Premultiply(Texture2D tex)
        {
            if (tex == null || !_premultiplied.Add(tex))
                return;
            Color[] data = new Color[tex.Width * tex.Height];
            tex.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                Color c = data[i];
                data[i] = new Color((byte)(c.R * c.A / 255), (byte)(c.G * c.A / 255), (byte)(c.B * c.A / 255), c.A);
            }
            tex.SetData(data);
        }
    }
}
