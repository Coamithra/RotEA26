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
        // Which file each cached texture actually came from (".dds"/".rtex"/".png"). A
        // fallback is otherwise invisible after the fact — the .rtex and .png paths both
        // yield SurfaceFormat.Color, so nothing about the finished Texture2D can tell them
        // apart — and "did this asset silently degrade to the PNG path?" is precisely the
        // question card 35834236 could not answer. One dictionary write per load, on a path
        // that already runs a Stopwatch and a LoadProfiler call. Read via TexProbe.
        private readonly Dictionary<string, string> _textureSources = new Dictionary<string, string>();
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

        // Free every asset this manager loaded. The base ContentManager only tracks
        // assets it loaded itself (loadedAssets/disposableAssets), but Load<T> above
        // routes textures/fonts/effects/sounds into our own _cache and never touches
        // that tracking — so base.Unload() alone frees NONE of them, and every
        // localContent.Unload() in the game was a silent no-op (a permanent GPU/texture
        // leak; meaningful on mobile). Dispose the cached GPU/audio resources ourselves,
        // then clear the cache. Each WebContentManager owns its own instances (no cache
        // is shared between managers — a miss always decodes fresh), so disposing one
        // manager's assets never affects another's; callers must still only Unload a
        // manager they own (audited: per-scene localContent, Bloom/Credits' own content,
        // Game1.content only at game teardown). base.Unload() still handles anything that
        // fell through to base.Load<T> (Song/Video, later stages).
        public override void Unload()
        {
            foreach (var asset in _cache.Values)
            {
                switch (asset)
                {
                    // SpriteFont isn't IDisposable, but its glyph atlas Texture2D is —
                    // free it explicitly or the font atlas leaks.
                    case SpriteFont font:
                        font.Texture?.Dispose();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            _cache.Clear();
            _textureSources.Clear();
            base.Unload();
        }

        // Open a content file, restating any failure with its actual cause.
        //
        // KNI's TitleContainer.OpenStream ends in
        //     catch (Exception inner) { throw new FileNotFoundException(name, inner); }
        // so the Message of anything it throws is the bare PATH and nothing else — the real
        // cause (an HTTP status, a decode error, an OOM) is only in InnerException. That
        // exception escapes into TickDotNet, where index.html's guard prints e.message: a lone
        // "Content/gfx/base/756.png", which reads like a 404 whatever actually went wrong.
        // Card 35834236 was filed and investigated on exactly that misreading.
        //
        // The chain has to be flattened INTO the message: e.message is all the JS guard can
        // see. EVERY Load* path goes through here — a bare OpenStream anywhere in this class
        // reintroduces the trap for that asset kind.
        private static Stream OpenOrThrow(string key, string extension, string siblingTried = null)
        {
            try
            {
                return TitleContainer.OpenStream(key + extension);
            }
            catch (Exception ex)
            {
                string sib = siblingTried == null ? "" : $" (sibling tried: {siblingTried})";
                throw new FlattenedContentLoadException(
                    $"{key}{extension} failed to load{sib} — {DescribeChain(ex)}", ex);
            }
        }

        // A ContentLoadException whose Message ALREADY carries the flattened inner chain, so a
        // reader can print it verbatim instead of walking the chain again and doubling every
        // frame. Its own type is the signal — testing for the ContentLoadException base would
        // also match one raised elsewhere, whose message is not flattened, and print only its
        // outermost line: the exact information loss this class exists to prevent.
        internal sealed class FlattenedContentLoadException : ContentLoadException
        {
            public FlattenedContentLoadException(string message, Exception inner)
                : base(message, inner) { }
        }

        // Flatten an exception chain into one console-friendly line.
        internal static string DescribeChain(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (sb.Length > 0)
                    sb.Append(" <- ");
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }
            return sb.ToString();
        }

        // Which precompiled sibling (if any) the offline build shipped for this asset name.
        // Takes the game's spelling ("GFX/Base/756") and resolves it the way Load<T> does.
        internal string DescribeSibling(string assetName, out string resolvedKey)
        {
            resolvedKey = ResolvePath(assetName);
            return PrecompiledTextures.Siblings.TryGetValue(resolvedKey, out string sib) ? sib : null;
        }

        // Which file the cached texture for this resolved key was actually built from, or null
        // if this manager has never loaded it.
        internal string TextureSource(string resolvedKey)
        {
            return _textureSources.TryGetValue(resolvedKey, out string src) ? src : null;
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
            // Per asset, the offline build ships exactly ONE precompiled form (or none), recorded
            // in the generated PrecompiledTextures.Siblings map. Probe that one sibling only —
            // most textures are PNG-only, and the old blind "dds ?? rtex" probe cost two
            // guaranteed-failing OpenStream calls + two thrown/caught exceptions per PNG-only
            // texture (dear on the interpreted WASM runtime; two blocking 404s on the live host).
            // An unlisted key (or a missing/stale sibling) falls through to the .png below.
            // Stopwatch is sub-microsecond; harmless in release.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Texture2D tex = null;
            PrecompiledTextures.Siblings.TryGetValue(key, out string sib);
            if (sib != null)
                tex = sib == ".dds" ? TryLoadDds(key) : TryLoadRaw(key);
            if (tex == null)
            {
                using Stream s = OpenOrThrow(key, ".png", sib ?? "none");
                tex = Texture2D.FromStream(GraphicsDevice, s);
                _textureSources[key] = ".png";
            }
            else
            {
                _textureSources[key] = sib;
            }
            sw.Stop();
            tex.Name = key;
            LoadProfiler.RecordTexture(key, sw.Elapsed.TotalMilliseconds, tex.Width, tex.Height);
            return tex;
        }

        // Load a precompiled DXT/BCn texture from <key>.dds if one was shipped, else
        // return null so the caller falls back to the .png. Built offline by
        // tools/textures/build_textures.py (texconv, BC3_UNORM, straight alpha; mips only for
        // assets whose config line carries the "mip" keyword — see the level loop below).
        // Parses only the legacy FourCC DDS header (DXT1/3/5 -> a Dxt SurfaceFormat) and
        // uploads the block bytes straight to the GPU via the compressed path. Any
        // problem (missing file, odd header, unsupported format) yields null + the PNG.
        private Texture2D TryLoadDds(string key)
        {
            // Only reached when PrecompiledTextures.Siblings says a .dds WAS shipped for this
            // key, so a read failure here is an anomaly, not the ordinary "PNG-only asset" case
            // — say so. Swallowing it silently (as this did) downgrades the asset to the PNG
            // path, i.e. unmipped and a StbImageSharp decode on the WASM main thread, leaving no
            // trace of why; and if the PNG then fails too, the only surviving evidence is the
            // path in KNI's message. That is the hole card 35834236 fell into.
            byte[] data;
            try
            {
                using Stream s = TitleContainer.OpenStream(key + ".dds");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[dds] {key}: registered .dds sibling could not be read — {DescribeChain(ex)} — falling back to PNG");
                return null;
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
                // Mip chain (build_textures.py's "mip" config keyword). dwMipMapCount is 0 or 1
                // on the unmipped siblings, which keeps the original single-level path exactly.
                // KNI derives the GL min filter from LevelCount > 1, so uploading the levels is
                // all trilinear needs — SamplerState.LinearClamp already maps Linear to
                // LINEAR_MIPMAP_LINEAR once the texture has them. NPOT + mips needs WebGL 2,
                // which BlazorGL uses.
                int levels = Math.Max(1, BitConverter.ToInt32(data, 28));
                if (DebugFlags.NoMips)
                    levels = 1;
                // A PARTIAL chain is worse than none: KNI allocates CalculateMipLevels(w,h) levels
                // whenever mipMap is true, and GL only samples a mipmap-COMPLETE texture, so
                // uploading fewer would render solid black rather than degrade. Demand the full
                // chain or fall back to the PNG.
                if (levels > 1)
                {
                    int full = 1;
                    for (int m = Math.Max(width, height); m > 1; m /= 2)
                        full++;
                    if (levels != full)
                        throw new InvalidDataException(
                            $"mip chain has {levels} levels, need the full {full} for {width}x{height}");
                }
                int blockBytes = fmt == SurfaceFormat.Dxt1 ? 8 : 16;
                var tex = new Texture2D(GraphicsDevice, width, height, levels > 1, fmt);
                try
                {
                    int offset = headerLen;
                    for (int level = 0; level < levels; level++)
                    {
                        // Level dims are floor-halved with a floor of 1 (TextureHelpers.GetSizeForLevel),
                        // and each level's payload is exactly ceil(w/4)*ceil(h/4) blocks — the DDS
                        // layout, which is also what Texture2D.SetData validates elementCount against.
                        int lw = Math.Max(width >> level, 1);
                        int lh = Math.Max(height >> level, 1);
                        int bytes = ((lw + 3) / 4) * ((lh + 3) / 4) * blockBytes;
                        if (offset + bytes > data.Length)
                            throw new InvalidDataException(
                                $"truncated mip chain: level {level} ({lw}x{lh}) needs {bytes} B at {offset}, file is {data.Length} B");
                        tex.SetData(level, null, data, offset, bytes);
                        offset += bytes;
                    }
                }
                catch
                {
                    tex.Dispose();   // else a half-uploaded GPU texture leaks on the PNG fallback
                    throw;
                }
                // build_textures.py pads dxt siblings up to a mult-of-4 and stamps the logical
                // (pre-pad) size into reserved1[0..2] (offsets 32/36 = w/h, 40 = "LOGD" marker).
                // Register it so every consumer uses the logical size, not the padded upload size.
                if (data.Length >= 44 && data[40] == (byte)'L' && data[41] == (byte)'O'
                    && data[42] == (byte)'G' && data[43] == (byte)'D')
                {
                    int lw = BitConverter.ToInt32(data, 32);
                    int lh = BitConverter.ToInt32(data, 36);
                    if (lw > 0 && lh > 0 && lw <= width && lh <= height)
                        TextureDims.Register(tex, lw, lh);
                }
                return tex;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[dds] {key}: {DescribeChain(ex)} — falling back to PNG");
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
            catch (Exception ex)
            {
                // Registered sibling; see the matching note in TryLoadDds.
                Console.WriteLine($"[rtex] {key}: registered .rtex sibling could not be read — {DescribeChain(ex)} — falling back to PNG");
                return null;
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
                Console.WriteLine($"[rtex] {key}: {DescribeChain(ex)} — falling back to PNG");
                return null;
            }
        }

        private SpriteFont LoadFont(string key)
        {
            Texture2D texture;
            using (Stream s = OpenOrThrow(key, ".fnt.png"))
                texture = Texture2D.FromStream(GraphicsDevice, s);

            using Stream meta = OpenOrThrow(key, ".fnt");
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
            using (Stream s = OpenOrThrow(key, ".mgfxo"))
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
            using Stream s = OpenOrThrow(key, ".wav");
            SoundEffect fx = SoundEffect.FromStream(s);
            fx.Name = key;
            return fx;
        }

        private Curve LoadCurve(string key)
        {
            using Stream s = OpenOrThrow(key, ".curve");
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
