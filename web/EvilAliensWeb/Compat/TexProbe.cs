// ---------------------------------------------------------------------------
// TexProbe — read the REAL texture load path for one asset, as data.
//
// Built for card 35834236. That card reported a tick-guard-swallowed throw whose
// only evidence was the string "Content/gfx/base/756.png", and spent a whole
// investigation on it — because KNI's TitleContainer.OpenStream ends in
//     catch (Exception inner) { throw new FileNotFoundException(name, inner); }
// so its Message is the bare PATH whatever actually failed, and index.html's tick
// guard prints exactly that. The message reads like a 404 for a decode error, a
// GPU error, an HTTP status, or a genuinely missing file alike. Nothing in the
// port could tell those apart from the console.
//
// This does. It drives WebContentManager for an arbitrary asset name and reports
// which sibling the offline build shipped, whether that sibling actually loaded,
// the resulting dimensions (actual AND logical — the mult-of-4 DXT pad makes those
// differ), and how many mip levels the texture carries. On failure it prints the
// whole exception chain rather than the outermost message.
//
// It answers the question that card asked in one console call:
//     eaTexProbe('GFX/Base/756')      -> .dds, 612x612 actual / 512x512 logical, 1 level
//     eaTexProbe('GFX/Base/756-v1')   -> .dds, 1348x1348 / 1248x1248, 11 levels (mipped)
// Those two lines are the distinction the card conflated when it blamed the mip
// work for a failure on the unmipped sibling.
//
// Negative control (no need to break an asset to test the tool):
//     eaTexProbe('GFX/Base/nope')
// drives the failure path end to end and prints the flattened chain, ending in
// the real cause — "IOException: HTTP request failed. Status:404".
//
// TWO CAVEATS, both from it using the SHARED manager (ServiceHelper's
// IContentManagerService is always Game1.content):
//   - A probe of a cold asset DECODES it. Not free, and it warms the shared cache.
//   - An asset owned by a scene-local WebContentManager (Bloom, Credits, Splash) is
//     not reachable here: probing one decodes a SECOND copy into
//     the shared manager, reports on that copy rather than the one the game is
//     drawing, and leaks it until game teardown. Each manager decodes its own
//     instances — see WebContentManager.Unload's note.
// ---------------------------------------------------------------------------
using System;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    internal static class TexProbe
    {
        public static string Run(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                return "[texprobe] usage: eaTexProbe('GFX/Base/756') — pass the name the game asks for, no extension";

            ContentManager cm;
            try
            {
                cm = EvilAliens.ServiceHelper.Get<EvilAliens.IContentManagerService>().ContentManager;
            }
            catch (Exception ex)
            {
                return $"[texprobe] no content manager yet — {WebContentManager.DescribeChain(ex)}";
            }

            if (!(cm is WebContentManager wcm))
                return $"[texprobe] content manager is {cm?.GetType().Name ?? "null"}, not WebContentManager — nothing to probe";

            string sib = wcm.DescribeSibling(assetName, out string key);
            string head = $"[texprobe] {assetName} -> {key} · sibling={sib ?? "none (PNG-only)"}";

            Texture2D tex;
            try
            {
                tex = wcm.Load<Texture2D>(assetName);
            }
            catch (Exception ex)
            {
                // The whole point: the cause, not just the outermost message. A
                // FlattenedContentLoadException already carries the chain in its Message (it has
                // to -- the tick guard in index.html prints nothing but e.message), so re-walking
                // it would print every frame of the chain twice.
                string detail = ex is WebContentManager.FlattenedContentLoadException
                    ? ex.Message
                    : WebContentManager.DescribeChain(ex);
                return $"{head}\n[texprobe] FAILED — {detail}";
            }

            if (tex == null)
                return $"{head}\n[texprobe] FAILED — Load<Texture2D> returned null";

            // LevelCount > 1 is exactly what KNI reads to pick LINEAR_MIPMAP_LINEAR, so it is
            // the honest answer to "is this asset trilinear?" — not what the .dds header claims
            // and not whether ?nomips was passed.
            int levels = tex.LevelCount;
            string mips = levels > 1 ? $"{levels} levels (mipped -> trilinear)" : "1 level (unmipped -> bilinear)";
            string pad = tex.IsPadded()
                ? $"{tex.Width}x{tex.Height} actual / {tex.LogicalWidth()}x{tex.LogicalHeight()} logical (DXT pad)"
                : $"{tex.Width}x{tex.Height} (unpadded)";

            // A successful Load<Texture2D> on this manager always ran LoadTexture, which always
            // records the source — so there is no "unknown" case to report here.
            string src = wcm.TextureSource(key);
            string via = sib != null && src != sib
                ? $"FELL BACK to {src} — the shipped {sib} did not load; the [dds]/[rtex] line saying why was logged when this asset FIRST loaded, which may be long since scrolled away — re-boot and probe again to see it"
                : $"loaded from {src}";

            return $"{head}\n[texprobe] OK — {pad} · {mips} · format {tex.Format} · {via}";
        }
    }
}
