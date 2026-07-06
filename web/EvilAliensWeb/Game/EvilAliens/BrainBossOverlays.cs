// ---------------------------------------------------------------------------
// BrainBossOverlays — live animated patches drawn on top of the static Brain
// final-boss sprite (brainbosshd).
//
// The full 1448x1086 boss is too big to animate as one sheet, so instead a few
// SELECTED on-screen regions (mechanical pods/lenses flickering, fleshy folds
// pulsating, ...) are animated offline with a local i2v model
// (tools/brainanim/*) and packed into small feathered sprite sheets. This class
// reads the manifest (Content/data/brainoverlays.json), lazily loads each sheet,
// and — every frame, from BrainBoss.Draw, AFTER the base sprite — composites each
// patch at its texture-space anchor so it tracks the boss's Position and pulsation
// scale and blends seamlessly (feathered straight-alpha) into the static art.
//
// The manifest anchors (texCenter/texW/texH) are in brainbosshd TEXTURE pixels
// (reference 1448x1086); the patch's on-screen footprint is pinned to that crop,
// so a patch always sits exactly over the region it was cut from and pulses with
// the boss. Playback ping-pongs (seamless loop) and rides the frame-interpolation
// shader (same path as the animated Braineroid), so a low frame count still plays
// smooth. Advancing on Draw time (not Update) keeps it cosmetic — unaffected by
// hit-stop, like the metal sheen.
//
// A missing/broken manifest, or a sheet that won't load, just draws nothing — the
// boss falls back to its static self. Built by tools/brainanim/build_brain_overlays.py.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal sealed class BrainBossOverlays
{
    // Reference dims of brainbosshd.png the manifest coordinates are authored in.
    private const float RefW = 1448f;
    private const float RefH = 1086f;
    private const string ManifestPath = "Content/data/brainoverlays.json";

    private sealed class Overlay
    {
        public Texture2D Tex;
        public int Cols, Rows, Frames, Sep;
        public float Fps;
        public float TexCenterX, TexCenterY, TexW, TexH;
        public int CellW, CellH;
        public bool PingPong;
        public SpriteBlendMode Blend;
        public float Clock;   // seconds of real (draw) time
    }

    private readonly List<Overlay> _overlays = new List<Overlay>();
    private bool _loaded;

    // Zero every patch's playback clock. Called from BrainBoss.Initialize so a RECYCLED
    // boss (re-fight) restarts its overlays at phase 0 instead of mid-loop.
    public void Reset()
    {
        foreach (Overlay ov in _overlays)
            ov.Clock = 0f;
    }

    // Sheets are loaded through the shared ContentManager (which caches by asset name and
    // owns their lifetime), so re-spawns hit the cache and nothing is disposed here — do
    // NOT add a Dispose, it would corrupt the shared cache.
    public void Load(ContentManager content)
    {
        if (_loaded)
            return;
        _loaded = true;
        string json;
        try
        {
            using Stream s = TitleContainer.OpenStream(ManifestPath);
            using var r = new StreamReader(s);
            json = r.ReadToEnd();
        }
        catch
        {
            return;   // no manifest -> no overlays (static boss). Fine.
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("overlays", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
                return;
            foreach (JsonElement e in list.EnumerateArray())
                TryAdd(content, e);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[brainoverlays] parse failed, drawing static boss: " + ex.Message);
        }
    }

    private void TryAdd(ContentManager content, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("sheet", out JsonElement sheetEl)
            || sheetEl.ValueKind != JsonValueKind.String)
            return;
        Texture2D tex;
        try
        {
            tex = content.Load<Texture2D>(sheetEl.GetString());
        }
        catch (Exception ex)
        {
            Console.WriteLine("[brainoverlays] sheet load failed (" + sheetEl.GetString() + "): " + ex.Message);
            return;
        }
        int cols = GetInt(e, "cols", 1);
        int rows = GetInt(e, "rows", 1);
        var ov = new Overlay
        {
            Tex = tex,
            Cols = Math.Max(1, cols),
            Rows = Math.Max(1, rows),
            Frames = Math.Max(1, GetInt(e, "frames", cols * rows)),
            Sep = GetInt(e, "sep", 1),
            Fps = GetFloat(e, "fps", 10f),
            TexCenterX = GetFloat(e, "texCenterX", RefW / 2f),
            TexCenterY = GetFloat(e, "texCenterY", RefH / 2f),
            TexW = GetFloat(e, "texW", 1f),
            TexH = GetFloat(e, "texH", 1f),
            CellW = Math.Max(1, GetInt(e, "cellW", 1)),
            CellH = Math.Max(1, GetInt(e, "cellH", 1)),
            PingPong = !e.TryGetProperty("pingpong", out JsonElement pp) || pp.ValueKind != JsonValueKind.False,
            Blend = ParseBlend(e),
        };
        _overlays.Add(ov);
    }

    /// <summary>
    /// Draw every overlay glued to the boss. position/drawScale/bossTexW/H come from
    /// BrainBoss (drawScale = its DrawScale, already scale/textureScale); tint is the
    /// boss's live `color` so the patches redden in lockstep with the base sprite.
    /// </summary>
    public void Draw(SpriteBatchWrapper sb, Vector2 position, float drawScale,
                     int bossTexW, int bossTexH, Color tint, GameTime gameTime)
    {
        if (_overlays.Count == 0)
            return;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        // Map manifest (1448x1086 reference) coords onto the actual boss texture, so a
        // future higher-res brainbosshd (with a matching DesignFrameWidth) still lines up —
        // provided it keeps the 1448:1086 aspect (else sx != sy and a patch's crop aspect
        // no longer matches its cell, distorting it).
        float sx = bossTexW / RefW;
        float sy = bossTexH / RefH;
        SpriteBlendMode savedBlend = sb.BlendMode;
        foreach (Overlay ov in _overlays)
        {
            ov.Clock += dt;
            FramePair(ov, out int f0, out int f1, out float frac);
            Rectangle r0 = CellRect(ov, f0);
            Rectangle r1 = CellRect(ov, f1);

            float centerX = position.X + (ov.TexCenterX * sx - bossTexW / 2f) * drawScale;
            float centerY = position.Y + (ov.TexCenterY * sy - bossTexH / 2f) * drawScale;
            // On-screen footprint = the crop's brain-texel size; cell is a resized copy.
            float patchScale = (ov.TexW * sx / ov.CellW) * drawScale;
            var patchPos = new Vector2(centerX, centerY);

            sb.BlendMode = ov.Blend;
            if (frac > 0.0001f && f1 != f0)
            {
                sb.interpolateEffect.Enable();
                sb.interpolateEffect.Offset = new Vector2(r1.Left - r0.Left, r1.Top - r0.Top)
                    / new Vector2(ov.Tex.Width, ov.Tex.Height);
                sb.interpolateEffect.Delta = frac;
                sb.fadeEffect.Enable();
                sb.fadeEffect.Value = tint.ToVector4();
                sb.Draw(ov.Tex, r0, patchPos, 0f, patchScale, center: true, tint, SpriteEffects.None);
                sb.interpolateEffect.Disable();
                sb.fadeEffect.Disable();
            }
            else
            {
                sb.Draw(ov.Tex, r0, patchPos, 0f, patchScale, center: true, tint, SpriteEffects.None);
            }
        }
        sb.BlendMode = savedBlend;
    }

    // Ping-pong (0..N-1..0) triangle so the loop is seamless (no hard cut back to frame 0).
    private static void FramePair(Overlay ov, out int f0, out int f1, out float frac)
    {
        if (ov.Frames <= 1 || ov.Fps <= 0f)
        {
            f0 = 0; f1 = 0; frac = 0f;
            return;
        }
        int last = ov.Frames - 1;
        float pos;
        if (ov.PingPong)
        {
            float span = 2f * last;
            float t = (ov.Clock * ov.Fps) % span;
            pos = t > last ? span - t : t;      // triangle wave 0..last..0
        }
        else
        {
            pos = (ov.Clock * ov.Fps) % ov.Frames;
        }
        f0 = (int)pos;
        if (f0 > last) f0 = last;
        frac = pos - f0;
        f1 = f0 + 1;
        if (f1 > last) { f1 = last; frac = 0f; }
    }

    private static Rectangle CellRect(Overlay ov, int frame)
    {
        int c = frame % ov.Cols;
        int r = frame / ov.Cols;
        return new Rectangle(c * (ov.CellW + ov.Sep), r * (ov.CellH + ov.Sep), ov.CellW, ov.CellH);
    }

    private static SpriteBlendMode ParseBlend(JsonElement e)
    {
        if (e.TryGetProperty("blend", out JsonElement b) && b.ValueKind == JsonValueKind.String
            && string.Equals(b.GetString(), "additive", StringComparison.OrdinalIgnoreCase))
            return (SpriteBlendMode)2;   // Additive
        return (SpriteBlendMode)1;       // AlphaBlend -> NonPremultiplied (straight)
    }

    private static int GetInt(JsonElement e, string name, int fallback)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : fallback;

    private static float GetFloat(JsonElement e, string name, float fallback)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? (float)v.GetDouble() : fallback;
}
