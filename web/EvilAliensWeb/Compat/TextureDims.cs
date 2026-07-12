using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    // Logical (pre-pad) size registry for precompiled DXT textures.
    //
    // BC3 blocks are 4x4 and Chrome/ANGLE->D3D11 rejects a block texture whose W or H isn't a
    // multiple of 4 (renders black). So every .dds sibling is PADDED up to a mult-of-4 by
    // tools/textures/build_textures.py (transparent, bottom/right only, so the original content
    // keeps its exact top-left pixel coords). The original ("logical") size is stamped in the .dds
    // header and registered here by WebContentManager.TryLoadDds at load time.
    //
    // EVERY consumer of a texture's dimensions must read LogicalWidth()/LogicalHeight() instead of
    // .Width/.Height, and whole-texture draws must clamp their source rect to LogicalBounds(), so
    // the padded strip is never sampled and no frame rect / origin / scale / tile shifts. Textures
    // that were never padded (png, .rtex, render targets) are simply not registered, so the
    // accessors fall through to the real Width/Height — a safe no-op everywhere else.
    internal static class TextureDims
    {
        // Weak keys: an entry is collected with its Texture2D (no leak across level unloads).
        private static readonly ConditionalWeakTable<Texture2D, int[]> logical =
            new ConditionalWeakTable<Texture2D, int[]>();

        public static void Register(Texture2D tex, int w, int h)
        {
            if (tex != null) logical.AddOrUpdate(tex, new[] { w, h });
        }

        public static int LogicalWidth(this Texture2D tex)
            => tex != null && logical.TryGetValue(tex, out int[] wh) ? wh[0] : tex?.Width ?? 0;

        public static int LogicalHeight(this Texture2D tex)
            => tex != null && logical.TryGetValue(tex, out int[] wh) ? wh[1] : tex?.Height ?? 0;

        // Source rect covering only the logical (non-padded) region — for whole-texture draws.
        public static Rectangle LogicalBounds(this Texture2D tex)
            => new Rectangle(0, 0, tex.LogicalWidth(), tex.LogicalHeight());

        // True if this texture carries a registered logical size (i.e. it was padded).
        public static bool IsPadded(this Texture2D tex)
            => tex != null && logical.TryGetValue(tex, out _);
    }
}
