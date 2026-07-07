// ---------------------------------------------------------------------------
// WebcamInterop — C# half of the "I Made This!" webcam challenge level
// (Levels.WebcamAliens). The JS half is wwwroot/webcam.js (window.eaWebcam).
//
// Split of responsibilities:
//   * JS owns everything camera: the setup dialog (device picker + preview),
//     getUserMedia, MediaPipe background removal, and DRAWING the player — the
//     segmented, mirrored person is a DOM canvas overlaid on the game's 4:3
//     letterbox (a real-time video texture can't cross the JS->WASM boundary
//     at 60fps, and the repo's overlay pattern — touch UI, trailers — already
//     lives outside the GL canvas).
//   * C# owns everything gameplay. For collision JS pushes, per processed
//     camera frame (~30Hz), a 40x30 occupancy grid of the person mask in
//     800x600 design space (mirrored, i.e. exactly what's on screen) as ~200
//     bytes of base64. WebcamLevel/WebcamUfo hit-test aliens and plasma shots
//     against that grid here (HitCircle), and aim at its centroid.
//
// The JS pushes arrive via the same [JSInvokable]-static pattern as
// DebugInput.eaPress. State is polled by the level each Update; masks go stale
// (MaskFresh false) if frames stop (tab hidden, camera unplugged) so the level
// can pause fairly instead of letting an invisible player die.
// ---------------------------------------------------------------------------
using System;
using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
    public static class WebcamInterop
    {
        public enum SessionState
        {
            Idle,        // no webcam session
            Setup,       // JS setup dialog is up
            Playing,     // player joined; masks streaming
            Cancelled    // player backed out of the dialog (level should exit)
        }

        // MUST match webcam.js (GRID_W/GRID_H over the 800x600 design space).
        public const int GridW = 40;
        public const int GridH = 30;
        private const float CellW = 800f / GridW;   // 20 design px
        private const float CellH = 600f / GridH;   // 20 design px
        // Half-diagonal of a cell: a circle "touches" a cell if it reaches the
        // cell's centre within this slack, which keeps body-touch hits generous
        // (right for gameplay driven by a fuzzy person mask).
        private static readonly float CellReach = (float)Math.Sqrt(CellW * CellW + CellH * CellH) / 2f;

        // A mask older than this is stale (camera stopped / tab hidden).
        private const long FreshMs = 1500;

        // Fewer occupied cells than this = nobody meaningfully in frame.
        private const int MinPresenceCells = 8;

        private static IJSInProcessRuntime _js;

        private static readonly bool[] grid = new bool[GridW * GridH];
        private static int occupiedCells;
        private static Vector2 centroid = new Vector2(400f, 300f);
        private static long lastMaskAt = long.MinValue;

        public static SessionState State { get; private set; } = SessionState.Idle;

        // "segmented" (background removal live) or "simple" (oval fallback) — for HUD hints.
        public static string Mode { get; private set; } = "";

        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // Show the JS camera-setup dialog. The level keeps running underneath and
        // polls State until the player joins or cancels.
        public static void BeginSetup()
        {
            ClearMask();
            State = SessionState.Setup;
            if (_js == null)
            {
                // No JS runtime (should never happen in the browser) — treat as a
                // cancel so the level exits gracefully instead of waiting forever.
                State = SessionState.Cancelled;
                return;
            }
            _js.InvokeVoid("eaWebcam.begin");
        }

        // Tear the whole JS session down (camera tracks, overlay, dialog). Safe to
        // call in any state, from any exit path.
        public static void Stop()
        {
            State = SessionState.Idle;
            ClearMask();
            _js?.InvokeVoid("eaWebcam.stop");
        }

        // Show/seed the live tuning stepper panel (eaWcTune in index.html; ?wctune only —
        // WebcamLevel gates the call on DebugFlags.WebcamTune, so a normal boot never
        // reaches JS). Also called after every applied change so the panel re-renders the
        // level's actual resolved values (e.g. after its "Reset to tier" button).
        public static void TuneShow(string tier, int hearts, int kills, int saucers, float saucerSpeed, float plasmaSpeed, float spawnInterval, float armDelay, float chargeTime, int mineMax, float mineSpawn, float mineLife, float mothership)
        {
            _js?.InvokeVoid("eaWcTune.show", tier, hearts, kills, saucers, saucerSpeed, plasmaSpeed, spawnInterval, armDelay, chargeTime, mineMax, mineSpawn, mineLife, mothership);
        }

        public static void TuneHide()
        {
            _js?.InvokeVoid("eaWcTune.hide");
        }

        // Grab the in-game person overlay's pixels for the level-select thumbnail (the
        // opt-in Settings.WebcamScreenshot capture). JS renders the overlay canvas into a
        // reqW x reqH offscreen and returns {w,h,px} where px is straight-alpha RGBA,
        // base64. Only valid while Playing; returns false (and leaves outputs empty) on
        // any failure so the caller falls back to the plain game frame.
        public static bool GetOverlayPixels(int reqW, int reqH, out byte[] rgba, out int w, out int h)
        {
            rgba = null;
            w = 0;
            h = 0;
            if (_js == null || State != SessionState.Playing)
            {
                return false;
            }
            string json;
            try
            {
                json = _js.Invoke<string>("eaWebcam.overlayPixels", reqW, reqH);
            }
            catch
            {
                return false;
            }
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                w = root.GetProperty("w").GetInt32();
                h = root.GetProperty("h").GetInt32();
                string b64 = root.GetProperty("px").GetString();
                if (string.IsNullOrEmpty(b64) || w <= 0 || h <= 0)
                {
                    return false;
                }
                rgba = Convert.FromBase64String(b64);
                return rgba.Length >= w * h * 4;
            }
            catch
            {
                w = 0;
                h = 0;
                rgba = null;
                return false;
            }
        }

        private static void ClearMask()
        {
            Array.Clear(grid, 0, grid.Length);
            occupiedCells = 0;
            centroid = new Vector2(400f, 300f);
            lastMaskAt = long.MinValue;
            Mode = "";
        }

        [JSInvokable("webcamJoined")]
        public static void Joined(string mode)
        {
            if (State == SessionState.Setup)
            {
                State = SessionState.Playing;
                Mode = mode ?? "";
            }
        }

        [JSInvokable("webcamCancelled")]
        public static void Cancelled()
        {
            if (State == SessionState.Setup)
            {
                State = SessionState.Cancelled;
            }
        }

        // Per processed camera frame (~30Hz): the person-mask occupancy grid,
        // packed LSB-first (bit index = gy*GridW+gx), base64-encoded.
        [JSInvokable("webcamMask")]
        public static void Mask(string b64, double coverage)
        {
            if (State != SessionState.Playing || string.IsNullOrEmpty(b64))
            {
                return;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                return;
            }
            if (bytes.Length * 8 < grid.Length)
            {
                return;
            }
            int count = 0;
            float sumX = 0f, sumY = 0f;
            for (int i = 0; i < grid.Length; i++)
            {
                bool on = (bytes[i >> 3] & (1 << (i & 7))) != 0;
                grid[i] = on;
                if (on)
                {
                    count++;
                    sumX += (i % GridW) + 0.5f;
                    sumY += (i / GridW) + 0.5f;
                }
            }
            occupiedCells = count;
            if (count > 0)
            {
                centroid = new Vector2(sumX / count * CellW, sumY / count * CellH);
            }
            lastMaskAt = Environment.TickCount64;
        }

        public static bool MaskFresh => Environment.TickCount64 - lastMaskAt < FreshMs;

        // Player is meaningfully in frame with a live mask.
        public static bool PlayerVisible => State == SessionState.Playing && MaskFresh && occupiedCells >= MinPresenceCells;

        // Design-space centre of the person mask (where aliens aim). Falls back to
        // screen centre before the first mask.
        public static Vector2 Centroid => centroid;

        // Fraction of the play field the player covers (0..1).
        public static float Coverage => occupiedCells / (float)(GridW * GridH);

        // Does a design-space circle overlap the player's mask right now?
        public static bool HitCircle(Vector2 pos, float radius)
        {
            if (!PlayerVisible || radius <= 0f)
            {
                return false;
            }
            int gx0 = (int)((pos.X - radius) / CellW);
            int gx1 = (int)((pos.X + radius) / CellW);
            int gy0 = (int)((pos.Y - radius) / CellH);
            int gy1 = (int)((pos.Y + radius) / CellH);
            if (gx0 < 0) gx0 = 0;
            if (gy0 < 0) gy0 = 0;
            if (gx1 >= GridW) gx1 = GridW - 1;
            if (gy1 >= GridH) gy1 = GridH - 1;
            float reach = radius + CellReach;
            float reachSq = reach * reach;
            for (int gy = gy0; gy <= gy1; gy++)
            {
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    if (!grid[gy * GridW + gx])
                    {
                        continue;
                    }
                    float dx = (gx + 0.5f) * CellW - pos.X;
                    float dy = (gy + 0.5f) * CellH - pos.Y;
                    if (dx * dx + dy * dy <= reachSq)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Body-shaped avoidance: a vector pointing AWAY from the player's mask, summed
        // over every occupied cell within `radius` of `pos` (each contributes a unit
        // push away, linearly stronger the closer it is). Zero when the player isn't
        // meaningfully in frame or nothing is within range. WebcamUfo steers its wander
        // by this so the saucers flow around the player's silhouette instead of drifting
        // into a still body — driven by the actual camera image, not just the centroid.
        public static Vector2 AvoidanceVector(Vector2 pos, float radius)
        {
            if (!PlayerVisible || radius <= 0f)
            {
                return Vector2.Zero;
            }
            int gx0 = (int)((pos.X - radius) / CellW);
            int gx1 = (int)((pos.X + radius) / CellW);
            int gy0 = (int)((pos.Y - radius) / CellH);
            int gy1 = (int)((pos.Y + radius) / CellH);
            if (gx0 < 0) gx0 = 0;
            if (gy0 < 0) gy0 = 0;
            if (gx1 >= GridW) gx1 = GridW - 1;
            if (gy1 >= GridH) gy1 = GridH - 1;
            Vector2 push = Vector2.Zero;
            for (int gy = gy0; gy <= gy1; gy++)
            {
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    if (!grid[gy * GridW + gx])
                    {
                        continue;
                    }
                    float dx = pos.X - (gx + 0.5f) * CellW;
                    float dy = pos.Y - (gy + 0.5f) * CellH;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= radius * radius || d2 < 1f)
                    {
                        continue;
                    }
                    float d = (float)Math.Sqrt(d2);
                    float w = 1f - d / radius;
                    push.X += dx / d * w;
                    push.Y += dy / d * w;
                }
            }
            return push;
        }

        // Does the player's mask overlap a thick beam SEGMENT right now? The segment runs
        // from `origin` for `length` design px along `direction` (radians, (cos,sin) — same
        // convention as MyMath.AngleToVector); a mask cell counts as hit if its centre is
        // within halfWidth (+ a cell's slack, like HitCircle) of the segment. Used by the F1
        // mothership's screen-bisecting laser (WebcamMothership) — the beam isn't an
        // ICollidable, so WebcamLevel tests it against the mask here.
        public static bool HitBeam(Vector2 origin, float direction, float length, float halfWidth)
        {
            if (!PlayerVisible || length <= 0f || halfWidth <= 0f)
            {
                return false;
            }
            float dirX = (float)Math.Cos(direction);
            float dirY = (float)Math.Sin(direction);
            Vector2 end = new Vector2(origin.X + dirX * length, origin.Y + dirY * length);
            float reach = halfWidth + CellReach;
            int gx0 = (int)((Math.Min(origin.X, end.X) - reach) / CellW);
            int gx1 = (int)((Math.Max(origin.X, end.X) + reach) / CellW);
            int gy0 = (int)((Math.Min(origin.Y, end.Y) - reach) / CellH);
            int gy1 = (int)((Math.Max(origin.Y, end.Y) + reach) / CellH);
            if (gx0 < 0) gx0 = 0;
            if (gy0 < 0) gy0 = 0;
            if (gx1 >= GridW) gx1 = GridW - 1;
            if (gy1 >= GridH) gy1 = GridH - 1;
            float reachSq = reach * reach;
            for (int gy = gy0; gy <= gy1; gy++)
            {
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    if (!grid[gy * GridW + gx])
                    {
                        continue;
                    }
                    float px = (gx + 0.5f) * CellW;
                    float py = (gy + 0.5f) * CellH;
                    // closest point on the segment to this cell centre (projection, clamped)
                    float t = (px - origin.X) * dirX + (py - origin.Y) * dirY;
                    if (t < 0f) t = 0f; else if (t > length) t = length;
                    float dx = px - (origin.X + dirX * t);
                    float dy = py - (origin.Y + dirY * t);
                    if (dx * dx + dy * dy <= reachSq)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
