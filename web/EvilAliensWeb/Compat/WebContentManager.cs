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
//   Effect     -> <name>.mgfxo      (MGFX v10 GLSL blob; see tools/shaders/)
//
// Audio (Stage 6) and video (Stage 6) are NOT handled yet: those fall through to
// the base ContentManager (and will fail until ported).
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
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
        // casing. Normalise both to exactly one "Content/" root: lowercase the
        // whole thing, strip every leading "content/" segment, then prepend a
        // single "Content/".
        //
        // The root segment MUST be capital "Content" to match the physical
        // wwwroot/Content directory: GitHub Pages serves from a case-sensitive
        // Linux filesystem, so a lowercase "content/..." request 404s there even
        // though it resolves fine on a case-insensitive Windows dev box. Files
        // *under* the root are all lowercase on disk, so lowercasing everything
        // after the root is correct. (The JS music layer + music.json already use
        // capital "Content/" — keep all consumers aligned.)
        private string ResolvePath(string assetName)
        {
            string combined = string.IsNullOrEmpty(RootDirectory)
                ? assetName
                : RootDirectory + "/" + assetName;
            combined = combined.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
            while (combined.StartsWith("content/"))
                combined = combined.Substring("content/".Length);
            return "Content/" + combined;
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
            else if (typeof(T) == typeof(Effect))
                asset = LoadEffect(key);
            else if (typeof(T) == typeof(SoundEffect))
                asset = LoadSoundEffect(key);
            else
                return base.Load<T>(assetName); // Song / Video: later stages

            _cache[key] = asset;
            return (T)asset;
        }

        private Texture2D LoadTexture(string key)
        {
            // Time the load. A PNG goes through Texture2D.FromStream -> StbImageSharp
            // (managed, on the WASM main thread), so a cold multi-megapixel PNG is a real
            // frame hitch — that's what the profiler (?loadlog) flags. Precompiled
            // variants skip the managed decode entirely (build via tools/textures):
            //   .dds  — BC3/DXT5 blocks uploaded as-is (lossy, small). Preferred.
            //   .rtex — uncompressed straight-alpha RGBA8 (lossless, large). Use where
            //           DXT artifacts are unacceptable; still beats a PNG decode.
            // Precedence dds -> rtex -> png; per asset, the build step ships exactly one
            // precompiled form (or none). Stopwatch is sub-microsecond; harmless in release.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Texture2D tex = TryLoadDds(key) ?? TryLoadRaw(key);
            if (tex == null)
            {
                using Stream s = TitleContainer.OpenStream(key + ".png");
                tex = Texture2D.FromStream(GraphicsDevice, s);
            }
            sw.Stop();
            tex.Name = key;
            LoadProfiler.RecordTexture(key, sw.Elapsed.TotalMilliseconds, tex.Width, tex.Height);
            return tex;
        }

        // Load a precompiled DXT/BCn texture from <key>.dds if one was shipped, else
        // return null so the caller falls back to the .png. Built offline by
        // tools/textures/build_dxt.py (texconv, BC3_UNORM, no mips, straight alpha).
        // Parses only the legacy FourCC DDS header (DXT1/3/5 -> a Dxt SurfaceFormat) and
        // uploads the block bytes straight to the GPU via the compressed path. Any
        // problem (missing file, odd header, unsupported format) yields null + the PNG.
        private Texture2D TryLoadDds(string key)
        {
            byte[] data;
            try
            {
                using Stream s = TitleContainer.OpenStream(key + ".dds");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            catch
            {
                return null; // no .dds for this asset — normal; use the PNG
            }

            try
            {
                if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
                    throw new InvalidDataException("bad DDS magic");
                int height = BitConverter.ToInt32(data, 12);
                int width = BitConverter.ToInt32(data, 16);
                uint fourcc = BitConverter.ToUInt32(data, 84);
                SurfaceFormat fmt = fourcc switch
                {
                    0x31545844u => SurfaceFormat.Dxt1, // 'DXT1'
                    0x33545844u => SurfaceFormat.Dxt3, // 'DXT3'
                    0x35545844u => SurfaceFormat.Dxt5, // 'DXT5'
                    _ => throw new NotSupportedException($"DDS FourCC 0x{fourcc:X8} (need DXT1/3/5, no DX10 header)")
                };
                const int headerLen = 128; // legacy DDS_HEADER; we never emit the DX10 extension
                var tex = new Texture2D(GraphicsDevice, width, height, false, fmt);
                tex.SetData(0, null, data, headerLen, data.Length - headerLen);
                return tex;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[dds] {key}: {ex.Message} — falling back to PNG");
                return null;
            }
        }

        // Load a precompiled uncompressed RGBA texture from <key>.rtex if shipped, else
        // null (caller tries the PNG). Built offline by tools/textures/build_textures.py.
        // Trivial format — 16-byte header then width*height*4 straight-alpha RGBA8 bytes,
        // matching SurfaceFormat.Color's layout, so it uploads with zero decode. Lossless
        // and unconstrained by dimension (only block formats need multiples of 4), at the
        // cost of a large file. Any problem yields null + the PNG fallback.
        //   bytes 0..3  'R','T','E','X'   4..4 version(1)   5..5 format(0=RGBA8 straight)
        //   6..7 reserved   8..11 width (uint32 LE)   12..15 height (uint32 LE)
        private Texture2D TryLoadRaw(string key)
        {
            byte[] data;
            try
            {
                using Stream s = TitleContainer.OpenStream(key + ".rtex");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            catch
            {
                return null; // no .rtex for this asset — normal; use the PNG
            }

            try
            {
                if (data.Length < 16 || data[0] != 'R' || data[1] != 'T' || data[2] != 'E' || data[3] != 'X')
                    throw new InvalidDataException("bad RTEX magic");
                int width = BitConverter.ToInt32(data, 8);
                int height = BitConverter.ToInt32(data, 12);
                const int headerLen = 16;
                long need = (long)width * height * 4;
                if (width <= 0 || height <= 0 || data.Length - headerLen < need)
                    throw new InvalidDataException($"RTEX size mismatch ({data.Length - headerLen} < {need} for {width}x{height})");
                // Copy the payload to its own array and use the plain SetData(T[]) overload:
                // the (level,rect,data,startIndex,count) overload rejects a non-zero
                // startIndex for uncompressed SurfaceFormat.Color in KNI's BlazorGL backend
                // (the compressed .dds path tolerates it, hence it's only needed here).
                var pixels = new byte[need];
                Array.Copy(data, headerLen, pixels, 0, (int)need);
                var tex = new Texture2D(GraphicsDevice, width, height, false, SurfaceFormat.Color);
                tex.SetData(pixels);
                return tex;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[rtex] {key}: {ex.Message} — falling back to PNG");
                return null;
            }
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

        // Effects (Stage 5): the lost XNA 3.x .fx were rewritten in HLSL under
        // tools/shaders/src and compiled offline by tools/shaders/build_shaders.py
        // (KNI's MGCB, BlazorGL target) to a raw MGFX v10 GLSL blob, shipped as
        // <name>.mgfxo. new Effect(gd, bytes) is exactly the ctor the stock
        // EffectReader feeds, so we read the blob and hand it over directly.
        private Effect LoadEffect(string key)
        {
            byte[] code;
            using (Stream s = TitleContainer.OpenStream(key + ".mgfxo"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                code = ms.ToArray();
            }
            return new Effect(GraphicsDevice, code) { Name = key };
        }

        // Audio (Stage 6): the XACT banks were cracked offline to PCM WAV under
        // Content/sfx (tools/audio/build_audio.py). KNI decodes WAV via
        // SoundEffect.FromStream and plays it through its WebAudio backend.
        // (Music does NOT come through here — it needs seamless loop points and
        // is handled by the JS eaMusic layer; see MusicInterop.)
        private SoundEffect LoadSoundEffect(string key)
        {
            using Stream s = TitleContainer.OpenStream(key + ".wav");
            SoundEffect fx = SoundEffect.FromStream(s);
            fx.Name = key;
            return fx;
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
