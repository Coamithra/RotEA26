// ---------------------------------------------------------------------------
// TextureUtil — small texture helpers used by the renderer (Stage 10).
//
// Premultiply() converts hand-made straight-alpha art (the chroma-keyed menu title)
// into the premultiplied-alpha pipeline the rest of the game uses (unpacked assets
// are already premultiplied — see Stage 3). Without this, a straight-alpha texture's
// flooded transparent pixels ghost under the premultiplied AlphaBlend.
//
// (This used to live on the now-removed HiResOverlay; the overlay pass is gone in
// Stage 10 — everything renders through the one unified scene target — but the
// premultiply helper is still needed for the title.)
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    public static class TextureUtil
    {
        private static readonly HashSet<Texture2D> _premultiplied = new HashSet<Texture2D>();

        // Premultiply a straight-alpha texture in place (rgb *= a), once per instance.
        // Opaque textures are unaffected. Idempotent per Texture2D.
        public static void Premultiply(Texture2D tex)
        {
            if (tex == null || !_premultiplied.Add(tex))
            {
                return;
            }
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
