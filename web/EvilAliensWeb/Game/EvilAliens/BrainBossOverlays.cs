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
// smooth. It advances in Draw rather than Update, but on Compat/WorldTime's delta
// (card d79a2f48): it is cosmetic, yet it decorates a boss whose Update a pause
// freezes, so on raw Draw time the patches kept cycling over a motionless boss.
//
// A patch with `triggerAvgSeconds` set does NOT loop: it rests on frame 0 (the
// untouched crop, so it reads as the static art) and plays ONE ping-pong cycle each
// time a chance-per-tick roll fires — on average once per that many seconds. That's
// the eye: a lidded eye that opens, looks around and closes now and then is a
// punctuation mark; the same motion on repeat is wallpaper.
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
using EvilAliensWeb.Compat;

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
        public string Name;
        public int Cols, Rows, Frames, Sep;
        public float Fps;
        public float TexCenterX, TexCenterY, TexW, TexH;
        public int CellW, CellH;
        public bool PingPong;
        // false => draw the current (floor) frame, no interpolation shader. The eye's discrete
        // open/look/close reads better STEPPED than cross-faded (the tween morphs the
        // eyeball). Mechanical flicker (pods) keeps interpolation for smooth light changes.
        public bool Interpolate;
        public SpriteBlendMode Blend;
        public float Clock;   // seconds of WORLD time (frozen by a pause, scaled by slow-mo)
        // > 0 => triggered: rest on frame 0, play one cycle every ~this many seconds.
        public float TriggerAvgSeconds;
        // true (gate:"spawn") => rest on frame 0 unless the boss is actively spawning enemies;
        // while spawning it loops, and when spawning stops it finishes the current cycle then
        // rests. The "exhaust" pods only fire when the boss is venting a wave.
        public bool SpawnGated;
        public float CycleSeconds;   // length of one full playthrough at Fps
        public bool Playing;         // triggered / spawn-gated patches only
    }

    private readonly List<Overlay> _overlays = new List<Overlay>();
    private bool _loaded;
    // WorldTime reading at the last Draw, so the patches advance on the WORLD's clock rather
    // than on raw Draw time (card d79a2f48). Negative until the first Draw seeds it.
    private float _lastWorldSeconds = -1f;

    // Zero every patch's playback clock. Called from BrainBoss.Initialize so a RECYCLED
    // boss (re-fight) restarts its overlays at phase 0 instead of mid-loop.
    public void Reset()
    {
        foreach (Overlay ov in _overlays)
        {
            ov.Clock = 0f;
            ov.Playing = false;
        }
        _lastWorldSeconds = -1f;
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
        catch (Exception ex)
        {
            // The manifest not loading (e.g. a stale-cache 404 on the freshly-added file) is a REAL
            // bug in dev, not a benign "static boss" fallback — surface it loudly so it can't hide.
            FailLoud("manifest load failed (" + ManifestPath + ")", ex);
            return;   // Release only: degrade to the static boss rather than crash the boss fight.
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
            FailLoud("manifest parse failed", ex);
        }
    }

    // Fail fast in Debug (the locally-played build) so a missing/broken asset is impossible to
    // miss; log + degrade to the static boss only in Release, so a stray 404 can never hard-crash
    // a player's final-boss fight on Pages.
    private static void FailLoud(string what, Exception ex)
    {
        Console.WriteLine("[brainoverlays] " + what + ": " + ex.Message);
#if DEBUG
        throw new InvalidOperationException("[brainoverlays] " + what, ex);
#endif
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
            FailLoud("sheet load failed (" + sheetEl.GetString() + ")", ex);
            return;
        }
        int cols = GetInt(e, "cols", 1);
        int rows = GetInt(e, "rows", 1);
        var ov = new Overlay
        {
            Tex = tex,
            Name = e.TryGetProperty("name", out JsonElement nm) && nm.ValueKind == JsonValueKind.String
                ? nm.GetString() : null,
            SpawnGated = e.TryGetProperty("gate", out JsonElement g) && g.ValueKind == JsonValueKind.String
                && string.Equals(g.GetString(), "spawn", StringComparison.OrdinalIgnoreCase),
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
            Interpolate = !e.TryGetProperty("interpolate", out JsonElement it) || it.ValueKind != JsonValueKind.False,
            Blend = ParseBlend(e),
            TriggerAvgSeconds = Math.Max(0f, GetFloat(e, "triggerAvgSeconds", 0f)),
        };
        ov.CycleSeconds = CycleLength(ov);
        // A patch that can't animate (one frame / no fps) has nothing to trigger or gate; leaving
        // it "triggered"/"gated" would just roll/hold on frame 0 either way.
        if (ov.CycleSeconds <= 0f)
        {
            ov.TriggerAvgSeconds = 0f;
            ov.SpawnGated = false;
        }
        _overlays.Add(ov);
    }

    private static float CycleLength(Overlay ov)
    {
        if (ov.Frames <= 1 || ov.Fps <= 0f)
            return 0f;
        return (ov.PingPong ? 2f * (ov.Frames - 1) : ov.Frames) / ov.Fps;
    }

    /// <summary>
    /// Draw every overlay glued to the boss. position/drawScale/bossTexW/H come from
    /// BrainBoss (drawScale = its DrawScale, already scale/textureScale); tint is the
    /// boss's live `color` so the patches redden in lockstep with the base sprite.
    /// </summary>
    public void Draw(SpriteBatchWrapper sb, Vector2 position, float drawScale,
                     int bossTexW, int bossTexH, Color tint, bool spawnActive)
    {
        if (_overlays.Count == 0)
            return;
        // The one bespoke animation clock in the game, and it used to tick on the frame's RAW
        // elapsed time inside Draw -- so the eye and the pods kept cycling while the boss sat
        // frozen in a pause (measured: 22482 px between two paused frames 45 steps apart).
        // It now advances by however far the WORLD's clock moved since the last Draw, which is
        // zero under a pause, a hit-stop or the Guide, and scaled by the 1-up slow-mo.
        // Two other things fall out of that and are worth keeping:
        //   * a `shot` with no `step` between it and the previous one is now IDENTICAL (dt is
        //     zero), so a BrainBoss screenshot is repeatable without ?brainoverlayphase=;
        //   * the sprite harness still PLAYS the overlays -- it freezes the boss with
        //     Enabled=false rather than a pause layer, so WorldTime keeps running there.
        // Clamped both ways, the same rule WorldTime.Advance and ShipConnector.Draw use: a
        // stretch with no Draw at all (Visible off, a scene skipped during a level warm) leaves
        // the last reading stale, and spending the whole accumulated gap in one step would jump
        // the eye or the pods mid-cycle.
        float dt = (_lastWorldSeconds < 0f) ? 0f : WorldTime.Seconds - _lastWorldSeconds;
        _lastWorldSeconds = WorldTime.Seconds;
        if (dt < 0f)
        {
            dt = 0f;
        }
        else if (dt > 0.1f)
        {
            dt = 0.1f;
        }
        // Map manifest (1448x1086 reference) coords onto the actual boss texture, so a
        // future higher-res brainbosshd (with a matching DesignFrameWidth) still lines up —
        // provided it keeps the 1448:1086 aspect (else sx != sy and a patch's crop aspect
        // no longer matches its cell, distorting it).
        float sx = bossTexW / RefW;
        float sy = bossTexH / RefH;
        SpriteBlendMode savedBlend = sb.BlendMode;
        // ?brainoverlayphase=<0..1>: pin every patch at a chosen point in its cycle instead of
        // advancing it (card 9f90978c). The eye rests CLOSED on frame 0 and opens only on a ~15 s
        // random roll, so it is otherwise unreachable for a screenshot; the pods only run while
        // the boss vents. Draw-side only -- nothing about the boss's state changes.
        float? parkPhase = DebugFlags.BrainOverlayPhase;
        foreach (Overlay ov in _overlays)
        {
            if (parkPhase.HasValue)
            {
                ov.Playing = true;
                ov.Clock = parkPhase.Value * ov.CycleSeconds;
            }
            else if (dt > 0f)
            {
                // dt == 0 skips the whole advance, not just the accumulate: the triggered
                // patch's per-frame roll would otherwise still fire under a pause and start
                // an animation that then cannot run.
                AdvanceClock(ov, dt, spawnActive);
            }
            FramePair(ov, out int f0, out int f1, out float frac);
            Rectangle r0 = CellRect(ov, f0);
            Rectangle r1 = CellRect(ov, f1);

            float centerX = position.X + (ov.TexCenterX * sx - bossTexW / 2f) * drawScale;
            float centerY = position.Y + (ov.TexCenterY * sy - bossTexH / 2f) * drawScale;
            // On-screen footprint = the crop's brain-texel size; cell is a resized copy.
            float patchScale = (ov.TexW * sx / ov.CellW) * drawScale;
            var patchPos = new Vector2(centerX, centerY);

            sb.BlendMode = ov.Blend;
            if (ov.Interpolate && frac > 0.0001f && f1 != f0)
            {
                sb.interpolateEffect.Enable();
                // UV-space offset -> normalise by the ACTUAL (padded) texture size (interpolate.fx
                // adds it to SpriteBatch texcoords, which are pixel/paddedSize); the rects are logical.
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

    // Continuous patches just accumulate draw time. A TRIGGERED patch sits at clock 0
    // (frame 0 = the untouched crop) and rolls RandomHelper's chance-per-tick each frame;
    // when it fires, one full cycle plays out and the clock snaps back to rest. The roll
    // is skipped mid-cycle, so the average gap between animations is TriggerAvgSeconds.
    private static void AdvanceClock(Overlay ov, float dt, bool spawnActive)
    {
        // Spawn-gated (the exhaust pods): loop while the boss is venting a wave; when it stops,
        // finish the current cycle then rest at frame 0 (a clean power-down, no mid-flicker cut).
        if (ov.SpawnGated)
        {
            if (!ov.Playing)
            {
                if (!spawnActive)
                    return;             // idle: hold at frame 0
                ov.Playing = true;
            }
            ov.Clock += dt;
            if (ov.Clock >= ov.CycleSeconds)
            {
                if (spawnActive)
                    ov.Clock -= ov.CycleSeconds;   // keep looping
                else { ov.Clock = 0f; ov.Playing = false; }   // powered down
            }
            return;
        }
        if (ov.TriggerAvgSeconds <= 0f)
        {
            ov.Clock += dt;
            return;
        }
        if (!ov.Playing)
        {
            // The roll takes the WORLD dt, not the frame's: on the raw one the eye would go on
            // triggering at its full real-time rate while the cycle it starts played back slowed
            // by the 1-up slow-mo, and under ?aiff=<n> (n world ticks per Draw) it would trigger
            // n times too rarely per world-second.
            if (RandomHelper.RandomFromAverage(1f / ov.TriggerAvgSeconds, dt))
                ov.Playing = true;
            return;
        }
        ov.Clock += dt;
        if (ov.Clock >= ov.CycleSeconds)
        {
            ov.Clock = 0f;
            ov.Playing = false;
        }
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
