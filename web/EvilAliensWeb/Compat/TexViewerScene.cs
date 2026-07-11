// ---------------------------------------------------------------------------
// TexViewerScene — the ?texviewer per-sprite texture-format viewer.
//
// The Trello card "Revisit per-sprite texture format (dxt vs raw vs png)" asks
// for a dedicated app that shows each sprite's COMPRESSED (DXT/BC3) and RAW
// (lossless) version, lets you FLIP between them to scrutinise the artifacts,
// pick a format, cycle through every image, and lock the decision into
// tools/textures/textures.config. See plans/texviewer.md.
//
// Both textures are drawn through the REAL game GPU pipeline: the DXT view is a
// .dds uploaded to a BC-compressed GPU texture (the same ANGLE->D3D11 block
// decode the shipped .dds hits in play), and the RAW view is the original .png
// decoded to RGBA8 (== what an .rtex ships, pixel-for-pixel). So what you flip
// between here is exactly what the game would draw.
//
// Preview .dds files + a manifest are built OFFLINE by
// tools/textures/build_texviewer.py into Content/texviewer/ (gitignored, dev
// only) — they are NOT the shipped siblings, so an undecided sprite is never
// auto-loaded by WebContentManager.
//
// Save is done JS-side: the eaTexViewer panel (index.html) POSTs to the dev-only
// /api/texdecide endpoint on web/DevServer, which upserts the textures.config
// line. Nothing here writes the config.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    internal class TexViewerScene : Scene
    {
        public delegate void ExitEvent();
        public ExitEvent OnExitToMenu;

        private sealed class Rec
        {
            public string Asset;
            public int W, H, Cols, Rows;
            public int PngKB, DdsKB, RawKB;
            public string Current;   // dxt|raw|png from textures.config at build time
            public int Pick;         // pending 0=dxt 1=raw 2=png (seeded from Current)
        }

        private readonly List<Rec> assets = new List<Rec>();
        private int index;

        private SpriteBatch raw;
        private Texture2D checker;
        private Texture2D pixel;
        private Texture2D pngTex;   // RAW reference (PNG-decoded RGBA8)
        private Texture2D ddsTex;   // DXT (BC3 GPU texture)
        private string loadNote = "";

        // View state.
        private bool showDxt;        // flip: false = RAW (png), true = DXT (dds)
        private int mode;            // 0 = flip, 1 = split (left RAW | right DXT)
        private float splitFrac = 0.5f;
        private float zoom = 1f;     // 1 = fit-to-viewport
        private Vector2 pan;         // render-px offset from centre

        private bool dragging;
        private Vector2 lastMouse;
        private bool panelDirty = true;
        private bool loadFailed;
        private string fatal;

        public TexViewerScene(Game game)
            : base(game)
        {
            base.DrawOrder = 2000;
        }

        public override void Initialize()
        {
            base.Initialize();
            raw = new SpriteBatch(base.GraphicsDevice);
            checker = BuildChecker();
            pixel = BuildPixel();

            try
            {
                LoadManifest();
            }
            catch (Exception ex)
            {
                fatal = "manifest load failed: " + ex.Message
                    + "  — run: python tools/textures/build_texviewer.py";
            }

            if (assets.Count > 0)
            {
                LoadCurrent();
            }
            else if (fatal == null)
            {
                fatal = "no assets in Content/texviewer/manifest.json"
                    + "  — run: python tools/textures/build_texviewer.py";
            }
        }

        private void LoadManifest()
        {
            using Stream s = Microsoft.Xna.Framework.TitleContainer.OpenStream("Content/texviewer/manifest.json");
            using var doc = JsonDocument.Parse(s);
            foreach (JsonElement e in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var r = new Rec
                {
                    Asset = e.GetProperty("asset").GetString(),
                    W = e.GetProperty("w").GetInt32(),
                    H = e.GetProperty("h").GetInt32(),
                    Cols = e.GetProperty("cols").GetInt32(),
                    Rows = e.GetProperty("rows").GetInt32(),
                    PngKB = e.GetProperty("pngBytes").GetInt32() / 1024,
                    DdsKB = e.GetProperty("ddsBytes").GetInt32() / 1024,
                    RawKB = e.GetProperty("rawBytes").GetInt32() / 1024,
                    Current = e.GetProperty("current").GetString(),
                };
                r.Pick = r.Current == "dxt" ? 0 : (r.Current == "raw" ? 1 : 2);
                assets.Add(r);
            }
        }

        private void LoadCurrent()
        {
            DisposeTextures();
            loadNote = "";
            loadFailed = false;
            zoom = 1f;
            pan = Vector2.Zero;
            Rec r = assets[index];

            try
            {
                using Stream s = Microsoft.Xna.Framework.TitleContainer.OpenStream("Content/" + r.Asset + ".png");
                pngTex = Texture2D.FromStream(base.GraphicsDevice, s);
            }
            catch (Exception ex)
            {
                loadFailed = true;
                loadNote = "PNG load failed: " + ex.Message;
            }

            ddsTex = TryLoadDds("Content/texviewer/" + r.Asset);
            if (ddsTex == null)
            {
                loadNote = (loadNote.Length > 0 ? loadNote + "   " : "")
                    + "no DXT preview (build_texviewer.py)";
            }
            panelDirty = true;
        }

        // Parse a legacy-FourCC BC1/3/5 .dds and upload the blocks straight to the GPU —
        // the same path WebContentManager.TryLoadDds uses in real play, so the artifacts
        // shown here are exactly what ships. Any problem yields null (RAW-only view).
        private Texture2D TryLoadDds(string key)
        {
            byte[] data;
            try
            {
                using Stream s = Microsoft.Xna.Framework.TitleContainer.OpenStream(key + ".dds");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            catch
            {
                return null;
            }
            try
            {
                if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
                    return null;
                int height = BitConverter.ToInt32(data, 12);
                int width = BitConverter.ToInt32(data, 16);
                uint fourcc = BitConverter.ToUInt32(data, 84);
                SurfaceFormat fmt = fourcc switch
                {
                    0x31545844u => SurfaceFormat.Dxt1,
                    0x33545844u => SurfaceFormat.Dxt3,
                    0x35545844u => SurfaceFormat.Dxt5,
                    _ => (SurfaceFormat)(-1)
                };
                if ((int)fmt < 0)
                    return null;
                const int headerLen = 128;
                var tex = new Texture2D(base.GraphicsDevice, width, height, false, fmt);
                tex.SetData(0, null, data, headerLen, data.Length - headerLen);
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private Texture2D BuildChecker()
        {
            const int n = 64, cell = 8;
            var d = new Color[n * n];
            var a = new Color(38, 38, 44);
            var b = new Color(58, 58, 66);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    d[y * n + x] = (((x / cell) + (y / cell)) & 1) == 0 ? a : b;
            var t = new Texture2D(base.GraphicsDevice, n, n);
            t.SetData(d);
            return t;
        }

        private Texture2D BuildPixel()
        {
            var t = new Texture2D(base.GraphicsDevice, 1, 1);
            t.SetData(new[] { Color.White });
            return t;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            while (TexViewerInterop.TryDequeue(out string cmd))
            {
                HandleCommand(cmd);
            }

            if (assets.Count > 0)
            {
                HandleKeys();
                HandlePan();
            }

            if (panelDirty)
            {
                PushPanel();
                panelDirty = false;
            }

            if (base.InputHandler.Pressed(MyKeys.Esc) && OnExitToMenu != null)
            {
                OnExitToMenu();
            }
        }

        private void HandleKeys()
        {
            if (base.InputHandler.Pressed(MyKeys.Right)) Nav(1);
            if (base.InputHandler.Pressed(MyKeys.Left)) Nav(-1);
            if (base.InputHandler.Pressed(MyKeys.Enter)) { showDxt = !showDxt; panelDirty = true; }
            if (base.InputHandler.Pressed(MyKeys.Up)) SetZoom(zoom * 1.25f);
            if (base.InputHandler.Pressed(MyKeys.Down)) SetZoom(zoom * 0.8f);
            if (base.InputHandler.Pressed(MyKeys.Mouse2)) { zoom = 1f; pan = Vector2.Zero; panelDirty = true; }
        }

        private void HandlePan()
        {
            Vector2 m = base.InputHandler.MousePosition;   // design space
            bool down = base.InputHandler.Down(MyKeys.Mouse1);
            if (down && !dragging)
            {
                dragging = true;
                lastMouse = m;
            }
            else if (down)
            {
                Vector2 dDesign = m - lastMouse;
                pan += dDesign * RenderScale.Scale;   // design delta -> render px
                lastMouse = m;
            }
            else
            {
                dragging = false;
            }
        }

        private void HandleCommand(string cmd)
        {
            int c = cmd.IndexOf(':');
            string head = c < 0 ? cmd : cmd.Substring(0, c);
            string arg = c < 0 ? null : cmd.Substring(c + 1);
            switch (head)
            {
                case "next": Nav(1); break;
                case "prev": Nav(-1); break;
                case "flip":
                    showDxt = arg != null ? arg == "1" : !showDxt;
                    panelDirty = true;
                    break;
                case "mode":
                    mode = arg == "1" ? 1 : 0;
                    panelDirty = true;
                    break;
                case "split":
                    if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float sf))
                        splitFrac = MathHelper.Clamp(sf, 0f, 1f);
                    break;
                case "pick":
                    if (int.TryParse(arg, out int p) && assets.Count > 0)
                    {
                        assets[index].Pick = Math.Max(0, Math.Min(2, p));
                        panelDirty = true;
                    }
                    break;
                case "zoom":
                    if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                        SetZoom(z);
                    break;
                case "fit": zoom = 1f; pan = Vector2.Zero; panelDirty = true; break;
                case "goto":
                    if (int.TryParse(arg, out int gi) && assets.Count > 0)
                    {
                        index = ((gi % assets.Count) + assets.Count) % assets.Count;
                        LoadCurrent();
                    }
                    break;
            }
        }

        private void Nav(int d)
        {
            if (assets.Count == 0) return;
            index = ((index + d) % assets.Count + assets.Count) % assets.Count;
            LoadCurrent();
        }

        private void SetZoom(float z)
        {
            zoom = MathHelper.Clamp(z, 0.05f, 40f);
            panelDirty = true;
        }

        // Push the current state to the eaTexViewer HTML panel so it re-renders its readout
        // + selects the right format radio. Manual JSON build (no serializer) keeps it trim-proof.
        private void PushPanel()
        {
            if (assets.Count == 0)
            {
                TexViewerInterop.Show("{\"count\":0}");
                return;
            }
            Rec r = assets[index];
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"index\":").Append(index).Append(',');
            sb.Append("\"count\":").Append(assets.Count).Append(',');
            sb.Append("\"asset\":\"").Append(r.Asset).Append("\",");
            sb.Append("\"w\":").Append(r.W).Append(',');
            sb.Append("\"h\":").Append(r.H).Append(',');
            sb.Append("\"cols\":").Append(r.Cols).Append(',');
            sb.Append("\"rows\":").Append(r.Rows).Append(',');
            sb.Append("\"pngKB\":").Append(r.PngKB).Append(',');
            sb.Append("\"ddsKB\":").Append(r.DdsKB).Append(',');
            sb.Append("\"rawKB\":").Append(r.RawKB).Append(',');
            sb.Append("\"hasDds\":").Append(ddsTex != null ? "true" : "false").Append(',');
            sb.Append("\"current\":\"").Append(r.Current).Append("\",");
            sb.Append("\"pick\":").Append(r.Pick).Append(',');
            sb.Append("\"view\":").Append(showDxt ? 1 : 0).Append(',');
            sb.Append("\"mode\":").Append(mode).Append(',');
            sb.Append("\"zoom\":").Append(zoom.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append('}');
            TexViewerInterop.Show(sb.ToString());
        }

        public void Teardown()
        {
            TexViewerInterop.Hide();
            DisposeTextures();
            checker?.Dispose(); checker = null;
            pixel?.Dispose(); pixel = null;
            raw?.Dispose(); raw = null;
        }

        private void DisposeTextures()
        {
            pngTex?.Dispose(); pngTex = null;
            ddsTex?.Dispose(); ddsTex = null;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            base.SpriteBatch.Flush();   // close the wrapper batch before the raw batches

            int vw = RenderScale.Width, vh = RenderScale.Height;

            // Checkerboard backdrop (tiled via a wrap sampler so alpha reads clearly).
            raw.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointWrap, null, null, null, Matrix.Identity);
            raw.Draw(checker, new Rectangle(0, 0, vw, vh), new Rectangle(0, 0, vw, vh), Color.White);
            raw.End();

            if (assets.Count > 0 && !loadFailed && pngTex != null)
            {
                DrawImages(vw, vh);
            }

            // HUD via the wrapper (design space); it re-begins on the first DrawString.
            DrawHud();
        }

        private void DrawImages(int vw, int vh)
        {
            Rec r = assets[index];
            float fit = Math.Min((float)vw / r.W, (float)vh / r.H) * 0.92f;
            float s = fit * zoom;
            int dw = Math.Max(1, (int)(r.W * s));
            int dh = Math.Max(1, (int)(r.H * s));
            int cx = vw / 2 + (int)pan.X;
            int cy = vh / 2 + (int)pan.Y;
            var dest = new Rectangle(cx - dw / 2, cy - dh / 2, dw, dh);

            raw.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, null, null, null, Matrix.Identity);
            if (mode == 1 && ddsTex != null)
            {
                // Split: left = RAW (png), right = DXT (dds), divider at texture column splitFrac.
                int splitTexX = (int)(r.W * splitFrac);
                int splitDestX = dest.X + (int)(dw * splitFrac);
                var lSrc = new Rectangle(0, 0, splitTexX, r.H);
                var lDst = new Rectangle(dest.X, dest.Y, splitDestX - dest.X, dh);
                var rSrc = new Rectangle(splitTexX, 0, r.W - splitTexX, r.H);
                var rDst = new Rectangle(splitDestX, dest.Y, dest.Right - splitDestX, dh);
                if (lSrc.Width > 0) raw.Draw(pngTex, lDst, lSrc, Color.White);
                if (rSrc.Width > 0) raw.Draw(ddsTex, rDst, rSrc, Color.White);
                raw.Draw(pixel, new Rectangle(splitDestX - 1, dest.Y, 2, dh), new Color(1f, 0.8f, 0.2f, 0.9f));
            }
            else
            {
                Texture2D t = (showDxt && ddsTex != null) ? ddsTex : pngTex;
                raw.Draw(t, dest, new Rectangle(0, 0, t.Width, t.Height), Color.White);
            }
            raw.End();
        }

        private void DrawHud()
        {
            var white = new Color(Color.White, 0.9f);
            if (fatal != null)
            {
                base.SpriteBatch.DrawString(fatal, new Vector2(24f, 60f), Color.OrangeRed, 0f, centered: false, 0.5f, (SpriteEffects)0, 0f);
                base.SpriteBatch.DrawString("Esc: menu", new Vector2(24f, 90f), white, 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
                return;
            }
            Rec r = assets[index];
            string viewLabel = mode == 1 ? "SPLIT  (RAW | DXT)" : (showDxt ? "DXT (compressed)" : "RAW (lossless)");
            string pickLabel = r.Pick == 0 ? "dxt" : (r.Pick == 1 ? "raw" : "png");
            string l1 = "texviewer  " + (index + 1) + "/" + assets.Count + "   " + r.Asset;
            string l2 = r.W + "x" + r.H + "   png " + r.PngKB + "KB   dds " + r.DdsKB + "KB   raw " + r.RawKB + "KB";
            string l3 = "view: " + viewLabel + "   zoom " + zoom.ToString("0.##", CultureInfo.InvariantCulture)
                + "x   config: " + r.Current + "   pick: " + pickLabel;
            base.SpriteBatch.DrawString(l1, new Vector2(16f, 12f), white, 0f, centered: false, 0.55f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l2, new Vector2(16f, 38f), white, 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l3, new Vector2(16f, 60f),
                showDxt ? new Color(1f, 0.85f, 0.5f, 0.95f) : new Color(0.6f, 1f, 0.7f, 0.95f),
                0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            if (loadNote.Length > 0)
                base.SpriteBatch.DrawString(loadNote, new Vector2(16f, 82f), new Color(1f, 0.6f, 0.5f, 0.9f), 0f, centered: false, 0.4f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("<-/->: prev/next   Enter: flip   Up/Down: zoom   drag: pan   RMB: fit   Esc: menu",
                new Vector2(16f, 576f), new Color(Color.White, 0.5f), 0f, centered: false, 0.4f, (SpriteEffects)0, 0f);
        }
    }
}
