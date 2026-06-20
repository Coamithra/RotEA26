// ---------------------------------------------------------------------------
// WebContentManager — the web port's content loader.
//
// The shipped assets are Xbox 360 / XNA 3.1 .xnb (LZX-compressed, Xbox surface
// formats). KNI follows XNA 4.0 and cannot read them, so tools/xnb/unpack.py
// converts them to web-friendly files under wwwroot/Content:
//
//   Texture2D  -> <name>.png        (RGBA; loaded via Texture2D.FromStream)
//   SpriteFont -> <name>.fnt.png    (glyph atlas)
//                 <name>.fnt        (binary metrics; see tools/xnb/unpack.py)
//   Curve      -> <name>.curve      (binary)
//
// All output paths are lowercased so the case-sensitive GitHub Pages host
// serves them regardless of the (inconsistent) casing the game asks for; this
// manager lowercases every request to match.
//
// Effects (Stage 5), audio (Stage 6) and video (Stage 6) are NOT handled yet:
// those fall through to the base ContentManager (and will fail until ported).
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    public class WebContentManager : ContentManager
    {
        private readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
        private GraphicsDevice _graphicsDevice;

        public WebContentManager(IServiceProvider services, string rootDirectory)
            : base(services, rootDirectory)
        {
        }

        private GraphicsDevice GraphicsDevice
        {
            get
            {
                if (_graphicsDevice == null)
                {
                    var gds = (IGraphicsDeviceService)ServiceProvider.GetService(typeof(IGraphicsDeviceService));
                    _graphicsDevice = gds.GraphicsDevice;
                }
                return _graphicsDevice;
            }
        }

        // wwwroot-relative, lowercased asset path (no extension). The physical
        // root is wwwroot/Content. The game asks for assets in two inconsistent
        // ways — via a manager rooted at "Content" with names like "GFX/x", and
        // via one rooted at "" with names like "Content/GFX/x" — and with mixed
        // casing. Normalise both to exactly one "content/" root: strip every
        // leading "content/" segment, then prepend a single one.
        private string ResolvePath(string assetName)
        {
            string combined = string.IsNullOrEmpty(RootDirectory)
                ? assetName
                : RootDirectory + "/" + assetName;
            combined = combined.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
            while (combined.StartsWith("content/"))
                combined = combined.Substring("content/".Length);
            return "content/" + combined;
        }

        public override T Load<T>(string assetName)
        {
            string key = ResolvePath(assetName);
            if (_cache.TryGetValue(key, out var cached))
                return (T)cached;

            object asset;
            if (typeof(T) == typeof(Texture2D))
                asset = LoadTexture(key);
            else if (typeof(T) == typeof(SpriteFont))
                asset = LoadFont(key);
            else if (typeof(T) == typeof(Curve))
                asset = LoadCurve(key);
            else
                return base.Load<T>(assetName); // Effect / SoundEffect / Song / Video: later stages

            _cache[key] = asset;
            return (T)asset;
        }

        private Texture2D LoadTexture(string key)
        {
            using Stream s = TitleContainer.OpenStream(key + ".png");
            Texture2D tex = Texture2D.FromStream(GraphicsDevice, s);
            tex.Name = key;
            return tex;
        }

        private SpriteFont LoadFont(string key)
        {
            Texture2D texture;
            using (Stream s = TitleContainer.OpenStream(key + ".fnt.png"))
                texture = Texture2D.FromStream(GraphicsDevice, s);

            using Stream meta = TitleContainer.OpenStream(key + ".fnt");
            using var br = new BinaryReader(meta);
            int lineSpacing = br.ReadInt32();
            float spacing = br.ReadSingle();
            bool hasDefault = br.ReadInt32() != 0;
            int defaultCp = br.ReadInt32();
            int n = br.ReadInt32();

            var chars = new List<char>(n);
            for (int i = 0; i < n; i++)
                chars.Add((char)br.ReadInt32());
            var glyphs = new List<Rectangle>(n);
            for (int i = 0; i < n; i++)
                glyphs.Add(new Rectangle(br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32()));
            var cropping = new List<Rectangle>(n);
            for (int i = 0; i < n; i++)
                cropping.Add(new Rectangle(br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32()));
            var kerning = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
                kerning.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));

            char? defaultChar = hasDefault ? (char)defaultCp : (char?)null;
            return new SpriteFont(texture, glyphs, cropping, chars, lineSpacing, spacing, kerning, defaultChar);
        }

        private Curve LoadCurve(string key)
        {
            using Stream s = TitleContainer.OpenStream(key + ".curve");
            using var br = new BinaryReader(s);
            var curve = new Curve
            {
                PreLoop = (CurveLoopType)br.ReadInt32(),
                PostLoop = (CurveLoopType)br.ReadInt32(),
            };
            int count = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                float pos = br.ReadSingle();
                float val = br.ReadSingle();
                float tangentIn = br.ReadSingle();
                float tangentOut = br.ReadSingle();
                int continuity = br.ReadInt32();
                curve.Keys.Add(new CurveKey(pos, val, tangentIn, tangentOut, (CurveContinuity)continuity));
            }
            return curve;
        }
    }
}
