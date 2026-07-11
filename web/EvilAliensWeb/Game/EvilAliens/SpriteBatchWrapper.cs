using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class SpriteBatchWrapper : DrawableGameComponent, ISpriteBatchWrapperService
{
	private SpriteFont font;

	private SpriteBatch spriteBatch;

	private bool enabled;

	private SpriteBlendMode blendmode;

	private EffectHandler effectHandler;

	// Stage 13: shared text render target for DrawMetalString (the chrome-sheen font
	// path). GROW-ONLY — it expands to the largest string seen and is then reused for
	// every metal string in a frame (the menu draws several; recreating per call would
	// thrash). Each string renders into the top-left corner; the composite passes its
	// used sub-rect to the shader as UvExtent so the local UV stays 0..1.
	private RenderTarget2D metalRT;

	// Group-flatten (card "flying spiders drawn on a rendertarget as a group, given one opacity"):
	// shared grow-only RT + a design->RT capture matrix. Between BeginGroupFlatten/EndGroupFlatten
	// every wrapper draw is redirected here (OPAQUE), then the union is composited ONCE at a group
	// alpha — so N overlapping translucent sprites don't double-brighten where they cover each other.
	private RenderTarget2D groupRT;
	private bool capturing;
	private Matrix captureMatrix;
	private RenderTargetBinding[] captureRestore;
	private int groupUsedW;
	private int groupUsedH;
	private Rectangle groupDesignRect;

	// The chrome-sheen effect (metal.fx), owned here so call sites don't each load/pass
	// it. Loaded in LoadContent; null => DrawMetalString degrades to a plain DrawString.
	private Effect metalEffect;

	// The one 3D effect (see DrawGeometry3D — the Level-3 tower shafts). Owned here for the same
	// reason metalEffect is: this class is the single choke point that owns the sprite batch and
	// every shared Effect, so a call site never constructs (and re-links) its own. That matters
	// here: constructing a BasicEffect re-reads and re-links its precompiled shader, and Level 3
	// spawns a fresh Wall per section, so a per-caller effect would pay that on every section.
	// Created once in LoadContent, disposed in UnloadContent; null => DrawGeometry3D no-ops, the
	// same graceful degrade metalEffect gets on a partial deploy.
	//
	// BasicEffect is real on BlazorGL — KNI embeds Resources.BasicEffect.fxo in the platform
	// assembly — and TextureEnabled + VertexColorEnabled IS a textured + vertex-colour shader, so
	// no bespoke .fx (and no hand-written vertex shader, which this project has never needed) has
	// to be compiled for it.
	private BasicEffect basicEffect;

	// One-time GL program warm-up for the 3D path (WarmGeometry3D). BlazorGL/ANGLE defers the
	// driver-side compile+link of a shader program to its FIRST draw, and Chrome then caches the
	// binary — so the very first DrawGeometry3D of a session (the first Level-3 wall) paid a
	// ~120ms one-off stall mid-gameplay (Trello 3e81fdcd), which no texture preload could cover
	// because it is the BasicEffect program, not an asset. Forced once from the tower scenes'
	// PreloadGraphicalContent (Level3/Demo3/OwnLevel), the same loading-screen phase (watchdog-suppressed)
	// as the throwaway enemy spawns that prewarm the JIT.
	private bool geom3dWarmed;

	// Throwaway 1x1 texture + render target for the warm draw. The target is bound around the draw so
	// the program compiles even when the caller runs OUTSIDE a Draw (the preload is Update-phase, so no
	// scene target is bound and a bare draw could hit an incomplete framebuffer and be dropped).
	private Texture2D warmPixel;

	private RenderTarget2D warmTarget;

	// Cached metal.fx EffectParameter handles for the params that VARY per call (Time / the two
	// glyph-band insets / the used-subrect UV). The invariant params (GradTop/Mid/Bot, Glint*,
	// Sweep*) are identical for every call and are set ONCE in LoadContent, so SetMetalParams
	// avoids ~11 string-keyed Parameters[name] dictionary lookups per call. Populated when
	// metalEffect loads; null when the effect is missing (partial deploy) — SetMetalParams no-ops.
	private EffectParameter mpTime;
	private EffectParameter mpPadTop;
	private EffectParameter mpPadBot;
	private EffectParameter mpUvExtent;

	// Per-key cached rasterised shadow-string element for the in-game score HUD (DrawShadowStringCached).
	// The score re-drew every HUD string through the full RT pipeline every frame (2 RT switches, a
	// metalRT clear, 3 Begin/End flushes, an allocating GetRenderTargets(), per-string) for text that
	// changes at most a few times a second. Each entry owns a persistent RT holding its rasterised
	// shadow+text; Pass 1 (the RT ping-pong) re-runs only when the text/scale/colours/render-scale
	// change, while Pass 2 (the composite) still runs every frame because alpha + glintTime vary and
	// are composite-time inputs.
	private sealed class CachedTextSprite
	{
		public RenderTarget2D Rt;
		public string Text;
		public float Scale;
		public Color ShadowColor;
		public Color TextColor;
		public Vector2 ShadowOffset;
		public float BuiltRs;    // RenderScale.Scale the RT was rasterised at (rebuild on a res change)
		public int UsedW;        // render-px the element fills within Rt (Pass-2 sub-rect + UvExtent)
		public int UsedH;
		public float BoxH;       // padded design-space box height (metal padFrac math)
	}

	private readonly System.Collections.Generic.Dictionary<int, CachedTextSprite> textSpriteCache = new System.Collections.Generic.Dictionary<int, CachedTextSprite>();

	// Content-addressed cache for DrawMetalStringCached (the static menu chrome rows). Keyed on
	// (text, packed tint) because menu labels never change, so each distinct label+colour is
	// rasterised into its own persistent RT exactly ONCE and reused every frame — unlike the score's
	// int-keyed textSpriteCache (whose text changes, so it keeps a fixed per-slot grow-only RT). The
	// (string, uint) tuple key value-compares the label, so equal labels across frames/menus share one
	// raster with no per-frame string allocation and no cache-key threading through menu renderers. Grows
	// for the session (disposed only in UnloadContent) — bounded ONLY because menu labels + tints are a
	// finite constant set; a caller passing a time-varying tint would spawn a fresh RT every frame, so keep
	// this path to static-text/static-tint call sites (dynamic text belongs on DrawMetalString / the score's
	// int-keyed DrawShadowStringCached).
	private readonly System.Collections.Generic.Dictionary<(string, uint), CachedTextSprite> metalSpriteCache = new System.Collections.Generic.Dictionary<(string, uint), CachedTextSprite>();

	// Transparent border (design px) baked around the text in the metal RT so the glint
	// sweep and bloom have overshoot room and don't clip at the glyph edges.
	private const int MetalPad = 6;

	// Straight-alpha source -> PREMULTIPLIED destination OVER, for flattening LAYERED straight-alpha
	// draws into an RT (RasteriseShadowText's shadow-then-text). Neither stock state can stack
	// straight layers correctly: NonPremultiplied squares the alpha onto a transparent target, and
	// AlphaBlend (One/InvSrcAlpha) only copies the FIRST layer verbatim — a second straight-alpha
	// layer's anti-aliased edge texels then land at FULL brightness over the layer below
	// (out.rgb = src.rgb + dst.rgb*(1-a), the premultiplied equation fed straight colour), which
	// destroyed the text's AA everywhere it overlapped its own drop shadow — the "combo counter /
	// POWER UP pop looks jaggy, like it renders no transparency" bug (card 37c4ccca). This state
	// premultiplies each incoming straight layer (color SrcAlpha/InvSrcAlpha) while accumulating
	// correct coverage (alpha One/InvSrcAlpha), so the RT ends up a correct PREMULTIPLIED flatten;
	// the composite then draws it with One/InvSrcAlpha (see CompositeShadowText).
	private static readonly BlendState PremultiplyOver = new BlendState
	{
		ColorSourceBlend = Blend.SourceAlpha,
		ColorDestinationBlend = Blend.InverseSourceAlpha,
		AlphaSourceBlend = Blend.One,
		AlphaDestinationBlend = Blend.InverseSourceAlpha,
	};

	// Writes ONLY the alpha channel (RGB masked out), overwriting it with the source —
	// used by SealAlpha to force a render target opaque without touching its colour.
	private static readonly BlendState WriteAlphaOne = new BlendState
	{
		ColorWriteChannels = ColorWriteChannels.Alpha,
		ColorSourceBlend = Blend.One,
		ColorDestinationBlend = Blend.Zero,
		AlphaSourceBlend = Blend.One,
		AlphaDestinationBlend = Blend.Zero,
	};

	// Glint-sweep timing fed to metal.fx (Time mod SweepPeriod in [0, Period*Active] = one
	// crossing). Public so an event-driven caller (the score, which sweeps on a digit
	// rollover rather than the continuous menu marquee clock) can compute the matching
	// one-shot window and a parked "glint off" value without duplicating the magic numbers.
	public const float MetalSweepPeriod = 9f;   // seconds per glint cycle (crossing + rest gap)
	public const float MetalSweepActive = 0.12f; // fraction of the period the glint spends crossing
	public static float MetalSweepDuration => MetalSweepPeriod * MetalSweepActive; // ~1.08s crossing

	// Per-frame glint clock for the no-time DrawMetalString overloads, set once by
	// Game1.DrawInner so any call site works without threading GameTime through every
	// menu/draw helper (many bespoke menu renderers don't have it in scope).
	public float MetalTime;

	public StaticAlphaEffect staticAlphaEffect => effectHandler.StaticAlphaEffect;

	public InterpolateEffect interpolateEffect => effectHandler.InterpolateEffect;

	public LightenEffect lightenEffect => effectHandler.LightenEffect;

	public ColorizeEffect colorizeEffect => effectHandler.ColorizeEffect;

	public OutlineEffect outlineEffect => effectHandler.OutlineEffect;

	public FadeEffect fadeEffect => effectHandler.FadeEffect;

	public SpriteBlendMode BlendMode
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return blendmode;
		}
		set
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			if (value != blendmode)
			{
				Flush();
				blendmode = value;
			}
		}
	}

	SpriteBatchWrapper ISpriteBatchWrapperService.SpriteBatchWrapper => this;

	public SpriteBatchWrapper(Game game)
		: base(game)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		blendmode = (SpriteBlendMode)1;
		effectHandler = new EffectHandler();
	}

	// XNA 3.x mapped its SpriteBlendMode to fixed-function blend state; 4.0 uses
	// BlendState objects. Content is STRAIGHT (non-premultiplied) alpha, exactly as the
	// original Xbox 3.1 build shipped it (the source .xnb store transparent pixels with
	// real RGB; the explosion code explicitly swaps to Additive — both impossible under
	// premultiply). So AlphaBlend maps to BlendState.NonPremultiplied (SrcAlpha/InvSrcAlpha),
	// the exact equation 3.x's SpriteBlendMode.AlphaBlend used. NOTE: KNI's BlendState.AlphaBlend
	// is the *premultiplied* variant (One/InvSrcAlpha) — a same-name, different-equation trap;
	// pairing it with straight content is what made fades go additive-bright instead of dissolving.
	// Additive (SrcAlpha/One) and Opaque are the straight variants too, matching the original.
	private static BlendState ToBlendState(SpriteBlendMode mode)
	{
		switch (mode)
		{
		case SpriteBlendMode.Additive:
			return BlendState.Additive;
		case SpriteBlendMode.None:
			return BlendState.Opaque;
		default:
			return BlendState.NonPremultiplied;
		}
	}

	private void _beginDrawing()
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		if (effectHandler.HasChanged())
		{
			Flush();
		}
		if (!enabled)
		{
			// 4.0: select the effect first, then begin the batch WITH it — the pass
			// is applied during End()/DrawBatch. A null effect = default sprite shader.
			// Stage 10: every game-content draw is authored in 800x600 design space;
			// RenderScale.Matrix scales it up to fill the window-sized scene target so
			// the legacy art shares the unified high-res pipeline. The custom sprite
			// effects are pixel-only (the internal sprite VS stays bound), so the
			// transform flows through them unchanged.
			effectHandler.LoadEffects();
			// While flattening a group into groupRT, force PremultiplyOver so the LAYERED
			// straight-alpha sprites stack correctly into a PREMULTIPLIED flatten (same fix as
			// RasteriseShadowText: One/InvSrcAlpha only copies the FIRST layer verbatim — a wing's
			// AA edge texels would land at full brightness over the already-drawn body, the
			// destroyed-AA fringe of card 37c4ccca; NonPremultiplied would square the edge alpha),
			// and use the design->RT capture matrix instead of the design->render one. The group
			// alpha is applied by EndGroupFlatten's single premult composite, so the callers still
			// draw at full opacity here. Ignores per-caller BlendMode changes (base.Draw resets it),
			// which is intended: every sprite in the group must land opaque for the union to have no
			// double-up.
			BlendState bs = capturing ? PremultiplyOver : ToBlendState(blendmode);
			Matrix mtx = capturing ? captureMatrix : RenderScale.Matrix;
			spriteBatch.Begin(SpriteSortMode.Deferred, bs, null, null, null, effectHandler.CurrentEffect, mtx);
			enabled = true;
		}
	}

	public void Flush()
	{
		if (enabled)
		{
			spriteBatch.End();
			effectHandler.UnloadEffects();
			enabled = false;
		}
	}

	// Stage 10: composite a full-scene-sized offscreen target (a menu / background
	// cross-fade render target, now sized to the render resolution) into the scene at
	// 1:1, bypassing the design->render scale that content draws use — the texture is
	// already at render resolution. `position`/`origin`/`scale` are in RENDER space
	// (e.g. centre = (RenderScale.Width/2, RenderScale.Height/2)); `scale` carries any
	// entry/exit animation. Honours the current BlendMode.
	public void DrawPresent(Texture2D texture, Vector2 position, Vector2 origin, float scale, Color color)
	{
		Flush();
		spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, null, Matrix.Identity);
		spriteBatch.Draw(texture, position, (Rectangle?)null, color, 0f, origin, scale, (SpriteEffects)0, 0f);
		spriteBatch.End();
	}

	// Force the whole current render target's ALPHA channel to 1, leaving RGB untouched, by
	// drawing `whitePixel` over a `width`x`height` region (RENDER space, identity transform).
	// The death cross-fade snapshots the background into an RGBA8 target and later composites
	// it over the scene with straight alpha as the dissolve overlay; the partial-alpha veil /
	// cloud draws inside that snapshot leave its alpha < 1, which would make the overlay
	// under-cover (a residual ghost of the faded objects). Sealing alpha to 1 makes the target
	// behave like the alpha-less Bgr565 original — the overlay covers by its tint alpha alone.
	public void SealAlpha(Texture2D whitePixel, int width, int height)
	{
		Flush();
		spriteBatch.Begin(SpriteSortMode.Deferred, WriteAlphaOne, null, null, null, null, Matrix.Identity);
		spriteBatch.Draw(whitePixel, new Rectangle(0, 0, width, height), Color.White);
		spriteBatch.End();
	}

	// Draw `texture` filling `dest` with an IDENTITY transform, bypassing the design->render
	// RenderScale.Matrix that every content draw bakes in. Use when the destination is a
	// fixed-size target that is NOT the scaled scene (the level-select thumbnail RT: SIZE is a
	// literal 300x225, not an 800x600 design coord). Going through the plain Draw()/_beginDrawing
	// path would multiply `dest` by RenderScale.Scale and overflow the small target, so only its
	// top-left corner would receive the image (the "screenshot is cropped" bug). Honours the
	// current BlendMode; default linear sampling gives a clean downscale.
	public void DrawPresent(Texture2D texture, Rectangle dest, Color color)
	{
		Flush();
		spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, null, Matrix.Identity);
		spriteBatch.Draw(texture, dest, color);
		spriteBatch.End();
	}

	// Draw indexed 3D geometry through the shared BasicEffect, in ONE buffered call, inside the
	// scene the sprites are drawing into (the Level-3 tower shafts — Wall.DrawTowerShafts3D).
	//
	// This is NOT the Quad.cs mistake. That class's comment describes the ORIGINAL beam pushing
	// three textured quads per laser via DrawUserIndexedPrimitives, each forcing a leading
	// SpriteBatch flush — a batching pathology, not a verdict on 3D throughput. BlazorGL creates
	// and destroys a transient vertex + index buffer per CALL, so the overhead is per-call, not
	// per-vertex: one call for a whole wall is exactly the shape that path wants.
	//
	// `verts`/`indices` may be grow-only scratch arrays larger than the used range; only
	// `vertexCount` vertices and `primitiveCount` triangles are drawn. `view`/`projection` are the
	// caller's camera (World stays identity). Honours the current BlendMode. Depth testing is OFF
	// (sceneTarget has no depth attachment) and culling is OFF, so the caller must submit only the
	// faces it wants, back-to-front — see the painter's-order argument in Wall.DrawTowerShafts3D.
	//
	// Optional fixed-function DISTANCE FOG (fogStart/fogEnd are eye distances). It LERPS rgb toward
	// fogColor and leaves alpha untouched, so a caller can fog a surface's COLOUR while still fading
	// its COVERAGE with vertex alpha. Only real geometry can have this: a sprite Color tint
	// multiplies, so it can never paint a sprite UP to a haze colour, only scale it down.
	//
	// Nothing is restored by hand afterwards: the wrapper's next _beginDrawing() calls
	// SpriteBatch.Begin, which re-applies blend / depth / rasterizer / sampler state itself.
	public void DrawGeometry3D(Texture2D texture, VertexPositionColorTexture[] verts, int vertexCount,
		int[] indices, int primitiveCount, Matrix view, Matrix projection,
		bool fogEnabled = false, Vector3 fogColor = default(Vector3), float fogStart = 0f, float fogEnd = 1f)
	{
		if (basicEffect == null || vertexCount < 3 || primitiveCount < 1)
		{
			return;
		}
		Flush();
		GraphicsDevice gd = base.GraphicsDevice;
		basicEffect.View = view;
		basicEffect.Projection = projection;
		basicEffect.Texture = texture;
		basicEffect.FogEnabled = fogEnabled;
		if (fogEnabled)
		{
			basicEffect.FogColor = fogColor;
			basicEffect.FogStart = fogStart;
			basicEffect.FogEnd = fogEnd;
		}
		gd.BlendState = ToBlendState(blendmode);
		gd.DepthStencilState = DepthStencilState.None;
		gd.RasterizerState = RasterizerState.CullNone;
		gd.SamplerStates[0] = SamplerState.LinearClamp;
		foreach (EffectPass pass in basicEffect.CurrentTechnique.Passes)
		{
			pass.Apply();
			gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, verts, 0, vertexCount, indices, 0, primitiveCount);
		}
	}

	// Force the BasicEffect GL programs the towers use to compile+link NOW, by running a throwaway
	// DrawGeometry3D through the exact same path. ANGLE defers a program's driver compile to its first
	// draw and Chrome caches the result, so without this the first Level-3 wall of a fresh (cold-cache)
	// session stalled ~120ms mid-play (Trello 3e81fdcd) — the one first-use cost no asset preload could
	// warm, since it is the program, not a texture.
	//
	// BasicEffect selects a DIFFERENT vertex+pixel program per feature permutation (fog participates in
	// its shader index), linked lazily on that permutation's first draw. So both fog states are warmed:
	// the shipping towers draw fog-ON (Wall's baked ?wallfog 0.55) and the ?wallfog=0 debug path draws
	// fog-OFF — warming only one would leave the other to pay the compile on its first real wall. Every
	// other permutation flag (TextureEnabled/VertexColorEnabled) is fixed at the effect's construction,
	// so fog is the only axis that varies between the warm and the real tower draws.
	//
	// Called once per session from the tower scenes' PreloadGraphicalContent (Level3/Demo3/OwnLevel),
	// alongside the off-screen throwaway enemy spawns that prewarm the JIT — same loading-screen phase,
	// where the hitch watchdog is suppressed. That phase is Update-time, so no scene target is bound; the
	// warm binds its own 1x1 target so the draws hit a COMPLETE framebuffer and the compile actually
	// happens (a draw against an incomplete FBO is silently dropped), then restores the previous binding.
	// Idempotent, and a no-op until the effect exists (the same degrade-to-null contract DrawGeometry3D
	// has), so a partial deploy stays safe.
	public void WarmGeometry3D()
	{
		if (geom3dWarmed || basicEffect == null)
		{
			return;
		}
		if (warmPixel == null)
		{
			warmPixel = new Texture2D(base.GraphicsDevice, 1, 1);
			warmPixel.SetData(new Color[1] { Color.White });
		}
		if (warmTarget == null)
		{
			warmTarget = new RenderTarget2D(base.GraphicsDevice, 1, 1);
		}
		// A fully-transparent triangle that COVERS the throwaway 1x1 target (identity view+projection,
		// so world coords ARE clip space): it rasterises a fragment — driving the pixel program too —
		// rather than relying on a zero-area draw slipping through ANGLE's compile-at-draw-setup, and the
		// zero alpha + disposable target mean nothing visible is written.
		Color clear = new Color(0, 0, 0, 0);
		VertexPositionColorTexture[] verts =
		{
			new VertexPositionColorTexture(new Vector3(-1f, -1f, 0f), clear, Vector2.Zero),
			new VertexPositionColorTexture(new Vector3(3f, -1f, 0f), clear, Vector2.Zero),
			new VertexPositionColorTexture(new Vector3(-1f, 3f, 0f), clear, Vector2.Zero)
		};
		int[] indices = { 0, 1, 2 };
		RenderTargetBinding[] prevTargets = base.GraphicsDevice.GetRenderTargets();
		base.GraphicsDevice.SetRenderTarget(0, warmTarget);
		DrawGeometry3D(warmPixel, verts, 3, indices, 1, Matrix.Identity, Matrix.Identity, fogEnabled: false);
		DrawGeometry3D(warmPixel, verts, 3, indices, 1, Matrix.Identity, Matrix.Identity, fogEnabled: true,
			Vector3.Zero, 0f, 1f);
		// Restore whatever was bound on entry (usually nothing during preload; Game1.Draw rebinds the
		// scene target next frame regardless, but restore so a Draw-phase caller stays correct too).
		if (prevTargets != null && prevTargets.Length > 0)
		{
			base.GraphicsDevice.SetRenderTargets(prevTargets);
		}
		else
		{
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		}
		// Latch only after the draws actually ran, so a throw in resource creation retries next scene.
		geom3dWarmed = true;
	}

	// Begin flattening a group of overlapping sprites into the shared offscreen RT. `designRect` is
	// the group's bounding box in 800x600 design space; between here and EndGroupFlatten every
	// wrapper draw is redirected into groupRT (at render resolution, OPAQUE — see _beginDrawing) with
	// a matrix that maps the box onto the RT's top-left corner, so callers keep drawing at their
	// normal design coords. Grow-only shared RT, recreated on a render-scale (window) change; ONE RT
	// is reused across every group in a frame (each brackets its own draws + composite). Not
	// re-entrant. The classic use is a translucent multi-part sprite (flying-spider body + two
	// wings): flatten the union opaque, then fade it as one silhouette so the overlaps don't
	// double-brighten. See FlyingSpider.Draw.
	public void BeginGroupFlatten(Rectangle designRect)
	{
		Flush();
		float rs = RenderScale.Scale;
		if (rs <= 0f) { rs = 1f; }
		int usedW = Math.Max(1, (int)Math.Ceiling(designRect.Width * rs));
		int usedH = Math.Max(1, (int)Math.Ceiling(designRect.Height * rs));
		EnsureGroupRT(usedW, usedH);
		groupUsedW = usedW;
		groupUsedH = usedH;
		groupDesignRect = designRect;
		// Capture whatever is bound (the scene target, or a menu RT) so EndGroupFlatten restores IT,
		// not a hardcoded null — same mid-draw ping-pong the metal-text RT does.
		captureRestore = base.GraphicsDevice.GetRenderTargets();
		base.GraphicsDevice.SetRenderTarget(0, groupRT);
		base.GraphicsDevice.Clear(Color.Transparent);
		// design coord -> RT texel: (coord - box.TopLeft) * rs, landing the box at the RT origin.
		captureMatrix = Matrix.CreateTranslation(0f - designRect.X, 0f - designRect.Y, 0f) * Matrix.CreateScale(rs);
		capturing = true;
	}

	// Finish the group: composite the flattened union ONCE, back at the design bbox, tinted by
	// `groupColor` (a STRAIGHT tint whose alpha is the group opacity — callers keep the normal
	// convention). The RT holds a PREMULTIPLIED flatten (see _beginDrawing), so the tint is
	// premultiplied here (rgb*a, a) and the draw uses One/InvSrcAlpha (BlendState.AlphaBlend —
	// correct BECAUSE the source is premultiplied; the same premult-intermediate exception as
	// CompositeShadowText): rgb and coverage scale together, so the whole silhouette fades as one
	// sprite with correctly blended internal AA edges. Render-res texels, so drawScale = 1/rs
	// under RenderScale.Matrix maps them 1:1 into the scene.
	public void EndGroupFlatten(Color groupColor)
	{
		Flush();
		capturing = false;
		if (captureRestore != null && captureRestore.Length > 0)
		{
			base.GraphicsDevice.SetRenderTargets(captureRestore);
		}
		else
		{
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		}
		captureRestore = null;
		float rs = RenderScale.Scale;
		if (rs <= 0f) { rs = 1f; }
		Rectangle used = new Rectangle(0, 0, groupUsedW, groupUsedH);
		Vector2 pos = new Vector2((float)groupDesignRect.X, (float)groupDesignRect.Y);
		Vector4 t = groupColor.ToVector4();
		Color premultTint = new Color(t.X * t.W, t.Y * t.W, t.Z * t.W, t.W);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, RenderScale.Matrix);
		spriteBatch.Draw(groupRT, pos, (Rectangle?)used, premultTint, 0f, Vector2.Zero, 1f / rs, (SpriteEffects)0, 0f);
		spriteBatch.End();
	}

	private void EnsureGroupRT(int w, int h)
	{
		int haveW = (groupRT != null && !((GraphicsResource)groupRT).IsDisposed) ? ((Texture2D)groupRT).Width : 0;
		int haveH = (groupRT != null && !((GraphicsResource)groupRT).IsDisposed) ? ((Texture2D)groupRT).Height : 0;
		if (haveW < w || haveH < h)
		{
			if (groupRT != null && !((GraphicsResource)groupRT).IsDisposed)
			{
				((GraphicsResource)groupRT).Dispose();
			}
			groupRT = new RenderTarget2D(base.GraphicsDevice, Math.Max(haveW, w), Math.Max(haveH, h), false, SurfaceFormat.Color, DepthFormat.None);
		}
	}

	// Stage 10: draw `texture` over `designRect` (800x600 space) through a custom
	// full-frame pixel effect (the splash channel-flip), at render resolution. Runs a
	// one-off batch with the effect + the design->render matrix so it lands in the
	// unified scene target like everything else. `configure` sets the effect params
	// (arg = the render-space dest rect). Honours the current BlendMode.
	public void DrawEffect(Texture2D texture, Rectangle designRect, Effect effect, Action<Effect, Rectangle> configure)
	{
		Flush();
		Vector2 tl = Vector2.Transform(new Vector2((float)designRect.Left, (float)designRect.Top), RenderScale.Matrix);
		Vector2 br = Vector2.Transform(new Vector2((float)designRect.Right, (float)designRect.Bottom), RenderScale.Matrix);
		Rectangle renderDest = new Rectangle((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
		configure?.Invoke(effect, renderDest);
		spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, effect, RenderScale.Matrix);
		spriteBatch.Draw(texture, designRect, Color.White);
		spriteBatch.End();
	}

	// Stage 13: draw `text` centered at design-space `center` with a metallic chrome
	// sheen (metal.fx). The string is first rasterised into a text-only render target at
	// render resolution (reusing the supersampled DrawStringScaled glyph walk, so it
	// stays crisp), then composited as ONE quad through the metal effect. Because the
	// composite is a single full-texture quad, the shader's texCoord is 0..1 LOCAL to the
	// text element — the sheen is relative to the letters, not the screen, so stacked
	// strings at different heights all get the identical look (a screen-space VPOS
	// gradient would slice them differently). The text is drawn in its real `tint`, which
	// the shader modulates (white -> chrome-white, red -> chrome-red). `scale` is an extra
	// (e.g. pulsate) factor applied to the COMPOSITE only; `time` (seconds) animates the
	// glint. A null `metal` (missing on a partial deploy) degrades to a plain DrawString.
	public void DrawMetalString(string text, Vector2 position, Color tint, float rotation, Vector2 origin, float scale, float time)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		if (string.IsNullOrEmpty(text) || font == null)
		{
			return;
		}
		if (scale <= 0f)
		{
			// Nothing visible (e.g. the redwarning flicker drops scale to 0) — skip the RT work.
			return;
		}
		if (metalEffect == null)
		{
			// Effect missing (partial deploy): plain string at the same transform.
			DrawString(font, text, position, tint, rotation, origin, scale, (SpriteEffects)0, 0f);
			return;
		}

		float rs = RenderScale.Scale;
		if (rs <= 0f) { rs = 1f; }
		Vector2 textSz = font.MeasureString(text);                 // unscaled design size
		float boxW = textSz.X + 2 * MetalPad;
		float boxH = textSz.Y + 2 * MetalPad;
		int usedW = Math.Max(1, (int)Math.Ceiling(boxW * rs));     // render-px the text fills
		int usedH = Math.Max(1, (int)Math.Ceiling(boxH * rs));

		// Grow-only shared RT: expand to fit the biggest string ever seen, then reuse it
		// for every metal string this frame (each renders into the top-left corner). The
		// composite passes its used sub-rect as UvExtent so the shader's local UV is 0..1.
		EnsureTextRT(usedW, usedH);
		RasteriseMetalText(metalRT, text, tint, rs);               // Pass 1 (time-independent)
		CompositeMetalText(metalRT, usedW, usedH, boxH, position, rotation, origin, scale, time, rs); // Pass 2
	}

	// Cached variant of DrawMetalString for the STATIC menu chrome rows (MenuSub1/MenuSubWithSkull draw
	// them every frame on an idle screen). Menu labels never change, so the plain-text raster (Pass 1) is
	// content-addressed on (text, tint) and built into its own persistent RT exactly ONCE, then reused
	// every frame; only the metal.fx composite (Pass 2) runs per frame. That's what keeps the moving glint
	// alive while skipping the per-row RT ping-pong (target capture/restore + Clear + a Begin/End rasterise
	// batch): the sheen — including the sweep clock `time` — is a Pass-2 input, NEVER baked into the raster,
	// so reusing the raster across frames can't freeze the sheen (the concern the card raised). Same idea as
	// DrawShadowStringCached, but keyed by content rather than an int slot (the score's text changes, so it
	// keeps a fixed per-slot grow-only RT; menu text is fixed, so content addressing avoids RT churn AND
	// threading a cache key through every bespoke menu renderer). Output is pixel-identical to DrawMetalString.
	public void DrawMetalStringCached(string text, Vector2 position, Color tint, float rotation, Vector2 origin, float scale, float time)
	{
		if (string.IsNullOrEmpty(text) || font == null)
		{
			return;
		}
		if (scale <= 0f)
		{
			return;
		}
		if (metalEffect == null)
		{
			DrawString(font, text, position, tint, rotation, origin, scale, (SpriteEffects)0, 0f);
			return;
		}

		float rs = RenderScale.Scale;
		if (rs <= 0f) { rs = 1f; }
		(string, uint) key = (text, tint.PackedValue);
		if (!metalSpriteCache.TryGetValue(key, out CachedTextSprite sprite))
		{
			sprite = new CachedTextSprite();
			metalSpriteCache[key] = sprite;
		}

		// text + tint are in the key; only a render-scale change (window resize) invalidates the raster.
		bool dirty = sprite.Rt == null || ((GraphicsResource)sprite.Rt).IsDisposed || sprite.BuiltRs != rs;
		if (dirty)
		{
			Vector2 textSz = font.MeasureString(text);
			float boxW = textSz.X + 2 * MetalPad;
			float boxH = textSz.Y + 2 * MetalPad;
			int usedW = Math.Max(1, (int)Math.Ceiling(boxW * rs));
			int usedH = Math.Max(1, (int)Math.Ceiling(boxH * rs));
			// Per-entry grow-only RT, independent of the shared metalRT so a cached raster survives other
			// text draws between frames (same reason DrawShadowStringCached keeps its own per-slot RTs).
			int haveW = (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed) ? ((Texture2D)sprite.Rt).Width : 0;
			int haveH = (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed) ? ((Texture2D)sprite.Rt).Height : 0;
			if (haveW < usedW || haveH < usedH)
			{
				if (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed)
				{
					((GraphicsResource)sprite.Rt).Dispose();
				}
				sprite.Rt = new RenderTarget2D(base.GraphicsDevice, Math.Max(haveW, usedW), Math.Max(haveH, usedH), false, SurfaceFormat.Color, DepthFormat.None);
			}
			RasteriseMetalText(sprite.Rt, text, tint, rs);
			sprite.Text = text;
			sprite.TextColor = tint;
			sprite.BuiltRs = rs;
			sprite.UsedW = usedW;
			sprite.UsedH = usedH;
			sprite.BoxH = boxH;
		}
		CompositeMetalText(sprite.Rt, sprite.UsedW, sprite.UsedH, sprite.BoxH, position, rotation, origin, scale, time, rs);
	}

	// Pass 1 of the metal path: rasterise just the TINTED text (no shadow) into `rt`'s top-left corner at
	// render res. Time-INDEPENDENT — the sheen (incl. the moving glint) is applied in the Pass-2 composite,
	// never here, which is exactly why DrawMetalStringCached can reuse this raster across frames while the
	// glint keeps sweeping. BlendState.AlphaBlend (One/InvSrcAlpha) onto a TRANSPARENT target copies the
	// straight-alpha glyphs verbatim (dst is 0, so the InvSrcAlpha*dst term vanishes). NonPremultiplied here
	// would instead square the alpha (srcA*srcA) and premultiply the colour, thinning the edges — invisible
	// over black but haloed over the menu. Capture whatever target is currently bound so we can restore IT
	// after the RT ping-pong: DrawMetalString runs mid-draw, and from inside a menu (MenuSub1.Draw) the bound
	// target is the menu's OWN render target (later composited with the zoom-transition scale via
	// DrawPresent). Hardcoding SetRenderTarget(0, null) here resolves (via the compat shim) to the scene
	// target, so after the first metal string the menu RT would be abandoned: its composite + every later
	// entry would leak straight to the scene unzoomed, breaking the transition and the selection highlight.
	private void RasteriseMetalText(RenderTarget2D rt, string text, Color tint, float rs)
	{
		RenderTargetBinding[] prevTargets = base.GraphicsDevice.GetRenderTargets();
		Flush();                                                   // end any active scene batch
		base.GraphicsDevice.SetRenderTarget(0, rt);
		base.GraphicsDevice.Clear(Color.Transparent);
		// design -> RT: translate the padded box to the RT origin, then scale to render res.
		Matrix m = Matrix.CreateTranslation(MetalPad, MetalPad, 0f) * Matrix.CreateScale(rs);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, m);
		DrawStringScaled(font, text, Vector2.Zero, tint, 0f, Vector2.Zero, new Vector2(1f, 1f), (SpriteEffects)0, 0f);
		spriteBatch.End();
		// Restore the target that was bound on entry (menu RT or scene target), NOT a hardcoded null, so
		// Pass 2's composite + any following draws land where the caller expects. In practice prevTargets is
		// always length 1 here: every metal string runs inside Game1.DrawInner, which keeps a target bound
		// for the whole frame, so the empty-array case (real back buffer bound) is unreachable in that flow.
		// The fallback routes through the compat null (-> BaseRenderTarget) purely defensively.
		if (prevTargets != null && prevTargets.Length > 0)
		{
			base.GraphicsDevice.SetRenderTargets(prevTargets);
		}
		else
		{
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		}
	}

	// Pass 2 of the metal path: composite `rt`'s used sub-rect through metal.fx at position/origin/scale,
	// with `time` driving the glint sweep. FLOAT-precise (sub-pixel) draw mirroring DrawString's transform
	// EXACTLY: the RT holds the text offset by MetalPad at render scale, so origin (in RT texels) =
	// (origin + pad) * rs and drawScale = scale / rs reproduce DrawString's placement for any design
	// `origin` (incl. centred) while applying the sheen. Integer dest rects are avoided on purpose —
	// rounding a pulsating rect each frame wobbles. Symmetric box (no drop shadow): equal top/bottom
	// glyph-band inset. Flush first because the cached fast path skips Pass 1, so an earlier _beginDrawing
	// batch may still be open (spriteBatch.Begin would otherwise throw); idempotent right after a rasterise.
	private void CompositeMetalText(RenderTarget2D rt, int usedW, int usedH, float boxH, Vector2 position, float rotation, Vector2 origin, float scale, float time, float rs)
	{
		Flush();
		Rectangle used = new Rectangle(0, 0, usedW, usedH);
		int texW = ((Texture2D)rt).Width;
		int texH = ((Texture2D)rt).Height;
		float padFracY = (float)MetalPad / boxH;
		Vector2 uvExtent = new Vector2((float)usedW / texW, (float)usedH / texH);
		SetMetalParams(time, padFracY, padFracY, uvExtent);
		float drawScale = scale / rs;
		Vector2 rtOrigin = (origin + new Vector2(MetalPad, MetalPad)) * rs;
		spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, metalEffect, RenderScale.Matrix);
		spriteBatch.Draw(rt, position, (Rectangle?)used, Color.White, rotation, rtOrigin, drawScale, (SpriteEffects)0, 0f);
		spriteBatch.End();
	}

	// Convenience overloads mirroring the DrawString signatures so a menu call site is a
	// literal DrawString -> DrawMetalString rename (time comes from MetalTime). The
	// SpriteFont arg is ignored — every text call site uses the one supersampled menufont.
	public void DrawMetalString(string text, Vector2 position, Color tint, float rotation, Vector2 origin, float scale)
	{
		DrawMetalString(text, position, tint, rotation, origin, scale, MetalTime);
	}

	public void DrawMetalString(SpriteFont spritefont, string text, Vector2 position, Color tint, float rotation, Vector2 origin, float scale)
	{
		DrawMetalString(text, position, tint, rotation, origin, scale, MetalTime);
	}

	public void DrawMetalString(string text, Vector2 position, Color tint, float rotation, bool centered, float scale)
	{
		Vector2 origin = (centered && font != null) ? font.MeasureString(text) / 2f : Vector2.Zero;
		DrawMetalString(text, position, tint, rotation, origin, scale, MetalTime);
	}

	private static void SetParam(Effect e, string name, float value)
	{
		EffectParameter p = e.Parameters[name];
		if (p != null)
		{
			p.SetValue(value);
		}
	}

	private static void SetParam(Effect e, string name, Vector2 value)
	{
		EffectParameter p = e.Parameters[name];
		if (p != null)
		{
			p.SetValue(value);
		}
	}

	// The metal.fx per-call parameter block, in ONE place so the menu marquee (DrawMetalString) and
	// the score chrome (DrawShadowString) can't silently diverge. Only the glint clock `time` and
	// the top/bottom glyph-band insets + used-subrect UV differ per call site; the chrome gradient +
	// glint sweep tuning is invariant and set ONCE in LoadContent (SweepPeriod/Active make the glint
	// cross ~every 9s then rest ~1.1s). Uses the cached handles so a per-call draw does no
	// string-keyed Parameters[name] lookups.
	private void SetMetalParams(float time, float padFracTop, float padFracBot, Vector2 uvExtent)
	{
		mpTime?.SetValue(time);
		mpPadTop?.SetValue(padFracTop);
		mpPadBot?.SetValue(padFracBot);
		mpUvExtent?.SetValue(uvExtent);
	}

	// Grow the shared text RT (metalRT) to at least w x h render-px, reusing it otherwise.
	// Shared by DrawMetalString and DrawShadowString. CONTRACT: a caller must rasterise into
	// the RT AND composite its result before the next text-composite string runs — each string
	// renders into the RT's top-left corner, so a deferred composite would be clobbered by the
	// next rasterise. Both callers honour this (rasterise + composite back-to-back per call),
	// which lets one RT serve every text-composite string in a frame. Grow-only — it expands to
	// the largest string seen.
	private void EnsureTextRT(int w, int h)
	{
		int haveW = (metalRT != null && !((GraphicsResource)metalRT).IsDisposed) ? ((Texture2D)metalRT).Width : 0;
		int haveH = (metalRT != null && !((GraphicsResource)metalRT).IsDisposed) ? ((Texture2D)metalRT).Height : 0;
		if (haveW < w || haveH < h)
		{
			if (metalRT != null && !((GraphicsResource)metalRT).IsDisposed)
			{
				((GraphicsResource)metalRT).Dispose();
			}
			metalRT = new RenderTarget2D(base.GraphicsDevice, Math.Max(haveW, w), Math.Max(haveH, h), false, SurfaceFormat.Color, DepthFormat.None);
		}
	}

	// Card "Score text minor visual tweak": draw `text` with a drop shadow flattened into ONE
	// semi-transparent sprite. The old score drew shadow and text each at the SAME partial
	// alpha, so the translucent shadow showed THROUGH the translucent text where they overlap
	// (shadow offset is only 2px, so they overlap almost entirely). Fix: rasterise shadow then
	// text at FULL opacity into the shared text RT (text on top fully hides the shadow it
	// covers), then composite the whole element ONCE at `alpha` — so shadow+text fade together
	// as a single sprite and no shadow bleeds through. Reuses DrawMetalString's RT plumbing
	// (grow-only RT + mid-draw target capture/restore). `metal=true` runs the composite through
	// the chrome-sheen effect (the card's "try the chrome shader on the score" experiment).
	//
	// position/origin are 800x600 design space (origin 0,0 = top-left, as the score uses);
	// shadowOffset is a FIXED design-px drop (NOT multiplied by scale, matching the original
	// 2px offset). shadowColor/textColor supply the RGB for each layer (their alpha is ignored
	// — the layers are opaque in the RT; `alpha` is the only transparency). The font is the
	// shared menufont (DrawStringScaled keeps it crisp at render resolution).
	//
	// `glintTime` drives the metal.fx sweep clock (only when metal=true). The default overload
	// passes the shared MetalTime (the continuous menu-marquee clock); callers that want an
	// event-driven one-shot sweep (the score, on a leading-digit rollover) pass their own clock
	// — see ScoreVisualiser. The static chrome gradient is time-independent and always shows;
	// only the moving glint streak depends on this clock.
	public void DrawShadowString(string text, Vector2 position, float scale, Color shadowColor, Color textColor, Vector2 shadowOffset, float alpha, bool metal)
	{
		DrawShadowString(text, position, scale, shadowColor, textColor, shadowOffset, alpha, metal, MetalTime);
	}

	public void DrawShadowString(string text, Vector2 position, float scale, Color shadowColor, Color textColor, Vector2 shadowOffset, float alpha, bool metal, float glintTime)
	{
		if (string.IsNullOrEmpty(text) || font == null)
		{
			return;
		}
		if (scale <= 0f || alpha <= 0f)
		{
			return;
		}

		float rs = RenderScale.Scale;
		if (rs <= 0f) { rs = 1f; }
		Vector2 textSz = font.MeasureString(text) * scale;          // scaled glyph extent (design px)
		float boxW = textSz.X + Math.Abs(shadowOffset.X) + 2 * MetalPad;
		float boxH = textSz.Y + Math.Abs(shadowOffset.Y) + 2 * MetalPad;
		int usedW = Math.Max(1, (int)Math.Ceiling(boxW * rs));      // render-px the element fills
		int usedH = Math.Max(1, (int)Math.Ceiling(boxH * rs));
		EnsureTextRT(usedW, usedH);
		RasteriseShadowText(metalRT, text, scale, shadowColor, textColor, shadowOffset, rs);
		CompositeShadowText(metalRT, usedW, usedH, boxH, shadowOffset, position, alpha, metal, glintTime, rs);
	}

	// Cached variant of DrawShadowString for the in-game score HUD (called every frame per player
	// slot). `cacheKey` identifies a persistent per-slot element: Pass 1 (the RT ping-pong
	// rasterise) re-runs only when the text / scale / colours / render-scale change since the last
	// call for that key; Pass 2 (the composite) runs every frame because `alpha` + `glintTime`
	// vary and are composite-time inputs. Output is pixel-identical to DrawShadowString for the
	// same inputs — it just skips re-rasterising unchanged text. See ScoreVisualiser.DrawStr.
	public void DrawShadowStringCached(int cacheKey, string text, Vector2 position, float scale, Color shadowColor, Color textColor, Vector2 shadowOffset, float alpha, bool metal, float glintTime)
	{
		if (string.IsNullOrEmpty(text) || font == null)
		{
			return;
		}
		if (scale <= 0f || alpha <= 0f)
		{
			return;
		}

		float rs = RenderScale.Scale;
		if (rs <= 0f) { rs = 1f; }
		if (!textSpriteCache.TryGetValue(cacheKey, out CachedTextSprite sprite))
		{
			sprite = new CachedTextSprite();
			textSpriteCache[cacheKey] = sprite;
		}

		bool dirty = sprite.Rt == null || ((GraphicsResource)sprite.Rt).IsDisposed
			|| sprite.BuiltRs != rs || sprite.Scale != scale
			|| sprite.ShadowColor != shadowColor || sprite.TextColor != textColor
			|| sprite.ShadowOffset != shadowOffset || sprite.Text != text;

		if (dirty)
		{
			Vector2 textSz = font.MeasureString(text) * scale;
			float boxW = textSz.X + Math.Abs(shadowOffset.X) + 2 * MetalPad;
			float boxH = textSz.Y + Math.Abs(shadowOffset.Y) + 2 * MetalPad;
			int usedW = Math.Max(1, (int)Math.Ceiling(boxW * rs));
			int usedH = Math.Max(1, (int)Math.Ceiling(boxH * rs));
			// Per-slot grow-only RT, independent of the shared metalRT so a cached sprite survives
			// other text draws (menus/pops/other slots) between frames.
			int haveW = (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed) ? ((Texture2D)sprite.Rt).Width : 0;
			int haveH = (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed) ? ((Texture2D)sprite.Rt).Height : 0;
			if (haveW < usedW || haveH < usedH)
			{
				if (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed)
				{
					((GraphicsResource)sprite.Rt).Dispose();
				}
				sprite.Rt = new RenderTarget2D(base.GraphicsDevice, Math.Max(haveW, usedW), Math.Max(haveH, usedH), false, SurfaceFormat.Color, DepthFormat.None);
			}
			RasteriseShadowText(sprite.Rt, text, scale, shadowColor, textColor, shadowOffset, rs);
			sprite.Text = text;
			sprite.Scale = scale;
			sprite.ShadowColor = shadowColor;
			sprite.TextColor = textColor;
			sprite.ShadowOffset = shadowOffset;
			sprite.BuiltRs = rs;
			sprite.UsedW = usedW;
			sprite.UsedH = usedH;
			sprite.BoxH = boxH;
		}

		CompositeShadowText(sprite.Rt, sprite.UsedW, sprite.UsedH, sprite.BoxH, sprite.ShadowOffset, position, alpha, metal, glintTime, rs);
	}

	// Pass 1: rasterise shadow-then-text OPAQUE into `rt`'s top-left corner at render res, as a
	// PREMULTIPLIED flatten (PremultiplyOver — see its comment; the old BlendState.AlphaBlend here
	// only copied the FIRST layer verbatim and hard-edged the text's AA wherever it overlapped the
	// shadow, the card-37c4ccca jaggies). The text top-left sits at the MetalPad inset; the shadow is
	// offset from there by shadowOffset, so the whole drop fits inside the padded box. Where the
	// opaque text covers the opaque shadow, the text wins — the bleed-through fix — and the AA edge
	// texels now blend correctly over the shadow instead of landing at full brightness. Does the
	// mid-draw target ping-pong and restores whatever was bound on entry (scene target, or a menu's
	// own RT), NOT a hardcoded null — see the long note in DrawMetalString.
	private void RasteriseShadowText(RenderTarget2D rt, string text, float scale, Color shadowColor, Color textColor, Vector2 shadowOffset, float rs)
	{
		RenderTargetBinding[] prevTargets = base.GraphicsDevice.GetRenderTargets();
		Flush();
		base.GraphicsDevice.SetRenderTarget(0, rt);
		base.GraphicsDevice.Clear(Color.Transparent);
		Matrix m = Matrix.CreateTranslation(MetalPad, MetalPad, 0f) * Matrix.CreateScale(rs);
		Color shadowOpaque = new Color(shadowColor.R, shadowColor.G, shadowColor.B, byte.MaxValue);
		Color textOpaque = new Color(textColor.R, textColor.G, textColor.B, byte.MaxValue);
		spriteBatch.Begin(SpriteSortMode.Deferred, PremultiplyOver, null, null, null, null, m);
		DrawStringScaled(font, text, shadowOffset, shadowOpaque, 0f, Vector2.Zero, new Vector2(scale, scale), (SpriteEffects)0, 0f);
		DrawStringScaled(font, text, Vector2.Zero, textOpaque, 0f, Vector2.Zero, new Vector2(scale, scale), (SpriteEffects)0, 0f);
		spriteBatch.End();
		if (prevTargets != null && prevTargets.Length > 0)
		{
			base.GraphicsDevice.SetRenderTargets(prevTargets);
		}
		else
		{
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		}
	}

	// Pass 2: composite `rt`'s used sub-rect (usedW x usedH) ONCE at `position`/`alpha`, optionally
	// through metal.fx. The RT holds a PREMULTIPLIED flatten (see RasteriseShadowText), so the
	// composite uses One/InvSrcAlpha (BlendState.AlphaBlend — correct here BECAUSE the source is
	// premultiplied; this is the deliberate premult-intermediate exception, NOT the straight-content
	// trap CLAUDE.md warns about) with an (a,a,a,a) tint: rgb and coverage scale together, so the
	// element fades as one sprite. metal.fx returns float4(rgb, mask) * color, so the same tint
	// carries through the chrome path too (its gradient is a linear multiply — premult-safe; the
	// additive glint is multiplied by the glyph mask in metal.fx, so under this premultiplied One
	// blend it stays confined to the letters and can't paint the transparent padding — see the
	// `* mask` in metal.fx's glint line). boxH + shadowOffset.Y feed the
	// asymmetric glyph-band insets (the drop shadow extends the bottom) so the chrome gradient lands
	// on the letters, not on the shadow overshoot.
	private void CompositeShadowText(RenderTarget2D rt, int usedW, int usedH, float boxH, Vector2 shadowOffset, Vector2 position, float alpha, bool metal, float glintTime, float rs)
	{
		// End any active wrapper batch before opening our own. RasteriseShadowText already flushes,
		// but on the cached fast path (clean sprite) Pass 1 is skipped, so an earlier _beginDrawing
		// batch may still be open here — flush it or spriteBatch.Begin throws "Begin cannot be called
		// again until End". Idempotent when a rasterise just ran (enabled is already false).
		Flush();
		Rectangle used = new Rectangle(0, 0, usedW, usedH);
		float fade = MathHelper.Clamp(alpha, 0f, 1f);
		Color composite = new Color(fade, fade, fade, fade);   // premultiplied fade: rgb + coverage together
		Effect fx = (metal && metalEffect != null) ? metalEffect : null;
		if (fx != null)
		{
			int texW = ((Texture2D)rt).Width;
			int texH = ((Texture2D)rt).Height;
			// Asymmetric box: the drop shadow extends the bottom, so the glyph band sits MetalPad
			// from the top but MetalPad + |shadowOffset.Y| from the bottom.
			float padFracTop = (float)MetalPad / boxH;
			float padFracBot = (float)(MetalPad + Math.Abs(shadowOffset.Y)) / boxH;
			Vector2 uvExtent = new Vector2((float)usedW / texW, (float)usedH / texH);
			SetMetalParams(glintTime, padFracTop, padFracBot, uvExtent);
		}
		float drawScale = 1f / rs;
		Vector2 rtOrigin = new Vector2(MetalPad, MetalPad) * rs;    // text top-left in RT texels
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, fx, RenderScale.Matrix);
		spriteBatch.Draw(rt, position, (Rectangle?)used, composite, 0f, rtOrigin, drawScale, (SpriteEffects)0, 0f);
		spriteBatch.End();
	}

	public void DrawString(SpriteFont spritefont, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects spriteeffect, float layerdepth)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		DrawStringScaled(spritefont, text, position, color, rotation, origin, new Vector2(scale, scale), spriteeffect, layerdepth);
	}

	public void DrawString(string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects spriteeffect, float layerdepth)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		DrawStringScaled(font, text, position, color, rotation, origin, new Vector2(scale, scale), spriteeffect, layerdepth);
	}

	public void DrawString(string text, Vector2 position, Color color, float rotation, bool centered, float scale, SpriteEffects spriteeffect, float layerdepth)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = ((!centered) ? Vector2.Zero : (font.MeasureString(text) / 2f));
		_beginDrawing();
		DrawStringScaled(font, text, position, color, rotation, val, new Vector2(scale, scale), spriteeffect, layerdepth);
	}

	public void DrawString(string text, Vector2 position, Color color, float rotation, bool centered, Vector2 scale, SpriteEffects spriteeffect, float layerdepth)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = ((!centered) ? Vector2.Zero : (font.MeasureString(text) / 2f));
		_beginDrawing();
		DrawStringScaled(font, text, position, color, rotation, val, scale, spriteeffect, layerdepth);
	}

	// Stage 12: hi-res font draw. The atlas is supersampled (each glyph's
	// BoundsInTexture is N x its design size), but every SpriteFont metric
	// (Cropping / kerning / LineSpacing / Spacing) stays in DESIGN units so
	// MeasureString -- called directly across the game for layout -- is unchanged.
	// Stock SpriteBatch.DrawString sizes each glyph quad from BoundsInTexture*scale,
	// which would draw N x too big; this re-walks KNI's exact DrawString layout but
	// sizes each quad from its DESIGN Cropping size instead. Per-glyph quad scale =
	// Cropping.Size / BoundsInTexture.Size (= 1/N for the redrawn glyphs, = 1 for the
	// un-supersampled merged originals), so design-space layout is byte-identical to
	// before while the texels come from the dense atlas -> crisp after RenderScale.
	private void DrawStringScaled(SpriteFont sf, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerdepth)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		if (sf == null || string.IsNullOrEmpty(text))
			return;
		Texture2D tex = sf.Texture;
		float cos = 1f, sin = 0f;
		if (rotation != 0f) { cos = (float)Math.Cos(rotation); sin = (float)Math.Sin(rotation); }
		// transformation matrix (KNI's no-flip path: scale, rotate, origin, position)
		float m11 = scale.X * cos, m12 = scale.X * sin;
		float m21 = scale.Y * (0f - sin), m22 = scale.Y * cos;
		float m41 = (0f - origin.X) * m11 + (0f - origin.Y) * m21 + position.X;
		float m42 = (0f - origin.X) * m12 + (0f - origin.Y) * m22 + position.Y;
		float offX = 0f, offY = 0f;
		bool first = true;
		foreach (char ch in text)
		{
			if (ch == '\r')
				continue;
			if (ch == '\n')
			{
				offX = 0f; offY += sf.LineSpacing; first = true;
				continue;
			}
			char c = ch;
			if (!sf.Glyphs.ContainsKey(c))
			{
				if (sf.DefaultCharacter.HasValue) c = sf.DefaultCharacter.Value;
				else continue;
			}
			SpriteFont.Glyph g = sf.Glyphs[c];
			if (first) { offX = Math.Max(g.LeftSideBearing, 0f); first = false; }
			else offX += sf.Spacing + g.LeftSideBearing;
			float vx = offX + g.Cropping.X;
			float vy = offY + g.Cropping.Y;
			float wx = vx * m11 + vy * m21 + m41;
			float wy = vx * m12 + vy * m22 + m42;
			Rectangle b = g.BoundsInTexture;
			float gsx = (b.Width > 0 ? (float)g.Cropping.Width / b.Width : 0f) * scale.X;
			float gsy = (b.Height > 0 ? (float)g.Cropping.Height / b.Height : 0f) * scale.Y;
			spriteBatch.Draw(tex, new Vector2(wx, wy), b, color, rotation, Vector2.Zero, new Vector2(gsx, gsy), effects, layerdepth);
			offX += g.Width + g.RightSideBearing;
		}
	}

	public void Draw(Texture2D texture, Vector2 position)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.Draw(texture, position, Color.White);
	}

	public void Draw(Texture2D texture, Vector2 position, Color color)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.Draw(texture, position, color);
	}

	public void Draw(Texture2D texture, Vector2 position, Vector2 scale, bool center)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(texture.Width / 2), (float)(texture.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, Color.White, 0f, zero, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Vector2 position, float rotation, float scale, bool center, Color color, SpriteEffects spriteEffects)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(texture.Width / 2), (float)(texture.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, color, rotation, zero, scale, spriteEffects, 0f);
	}

	public void Draw(Texture2D texture, Vector2 position, float rotation, float scale, Vector2 offset, Color color, SpriteEffects spriteEffects)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, color, rotation, offset, scale, spriteEffects, 0f);
	}

	public void Draw(Texture2D texture, Vector2 position, float rotation, float scale, bool center)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(texture.Width / 2), (float)(texture.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, Color.White, rotation, zero, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Vector2 position, float rotation, float scale, Vector2 offset)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, Color.White, rotation, offset, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Vector2 position, float rotation, float scale, bool center, Color color)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(texture.Width / 2), (float)(texture.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, color, rotation, zero, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Vector2 position, float rotation, Vector2 scale, bool center, Color color)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(texture.Width / 2), (float)(texture.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)null, color, rotation, zero, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Rectangle source, Vector2 position, float rotation, float scale, bool center, Color color)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(source.Width / 2), (float)(source.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)source, color, rotation, zero, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Rectangle source, Rectangle dest, Color color)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.Draw(texture, dest, (Rectangle?)source, color);
	}

	// Full-control overload: source rect + rotation + non-uniform scale + explicit origin
	// (origin in source-rect pixels). Needed by Quad's capsule beam-glow pieces, whose
	// dome ends pivot on the middle of one source edge, not the sprite centre.
	public void Draw(Texture2D texture, Rectangle source, Vector2 position, float rotation, Vector2 scale, Vector2 origin, Color color)
	{
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)source, color, rotation, origin, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Rectangle source, Vector2 position, float rotation, float scale, bool center, Color color, SpriteEffects spriteEffects)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(source.Width / 2), (float)(source.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)source, color, rotation, zero, scale, spriteEffects, 0f);
	}

	public void Draw(Texture2D texture, Rectangle source, Vector2 position, float rotation, float scale, bool center)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		Vector2 zero = default(Vector2);
		if (center)
		{
			(zero) = new Vector2((float)(source.Width / 2), (float)(source.Height / 2));
		}
		else
		{
			zero = Vector2.Zero;
		}
		_beginDrawing();
		spriteBatch.Draw(texture, position, (Rectangle?)source, Color.White, rotation, zero, scale, (SpriteEffects)0, 0f);
	}

	public void Draw(Texture2D texture, Rectangle dest, Color color)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.Draw(texture, dest, color);
	}

	protected override void LoadContent()
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected O, but got Unknown
		base.LoadContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		spriteBatch = new SpriteBatch(ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice);
		font = contentManager.Load<SpriteFont>("GFX/menu/menufont");
		// Chrome-sheen effect (Stage 13). Owned here so every DrawMetalString call site
		// stays a one-liner; degrade gracefully if it's missing on a partial deploy.
		try
		{
			metalEffect = contentManager.Load<Effect>("GFX/Effects/metal");
		}
		catch (System.Exception ex)
		{
			metalEffect = null;
			System.Console.WriteLine("[metal] effect load failed: " + ex);
		}
		if (metalEffect != null)
		{
			// Invariant metal.fx params are identical for every SetMetalParams call, so set them
			// ONCE here rather than re-looking-up + re-setting all 11 per call. Safe because
			// metalEffect is created once and never recreated: BlazorGL/WASM has no device-lost/reset
			// cycle, so neither the set-once values nor the cached param handles below can go stale.
			// (On a backend that reloaded effects on a graphics reset, both would need re-applying.)
			SetParam(metalEffect, "GradTop", 1.18f);
			SetParam(metalEffect, "GradMid", 0.50f);
			SetParam(metalEffect, "GradBot", 0.95f);
			SetParam(metalEffect, "GlintStrength", 0.9f);
			SetParam(metalEffect, "GlintWidth", 0.06f);
			SetParam(metalEffect, "SweepPeriod", MetalSweepPeriod);
			SetParam(metalEffect, "SweepActive", MetalSweepActive);
			// Cache the handles for the params SetMetalParams sets every call.
			mpTime = metalEffect.Parameters["Time"];
			mpPadTop = metalEffect.Parameters["PadFracTop"];
			mpPadBot = metalEffect.Parameters["PadFracBot"];
			mpUvExtent = metalEffect.Parameters["UvExtent"];
		}
		// The shared 3D effect (DrawGeometry3D). Same create-once / degrade-to-null contract as
		// metalEffect above; World never changes, and View/Projection are set per call.
		try
		{
			basicEffect = new BasicEffect(ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice)
			{
				TextureEnabled = true,
				VertexColorEnabled = true,
				LightingEnabled = false,
				World = Matrix.Identity,
			};
		}
		catch (System.Exception ex)
		{
			basicEffect = null;
			System.Console.WriteLine("[basic3d] effect create failed: " + ex);
		}
		effectHandler.LoadGraphicsContent(loadAllContent: true);
	}

	protected override void UnloadContent()
	{
		Flush();
		effectHandler.UnloadGraphicsContent(unloadAllContent: true);
		foreach (CachedTextSprite sprite in textSpriteCache.Values)
		{
			if (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed)
			{
				((GraphicsResource)sprite.Rt).Dispose();
			}
		}
		textSpriteCache.Clear();
		foreach (CachedTextSprite sprite in metalSpriteCache.Values)
		{
			if (sprite.Rt != null && !((GraphicsResource)sprite.Rt).IsDisposed)
			{
				((GraphicsResource)sprite.Rt).Dispose();
			}
		}
		metalSpriteCache.Clear();
		if (groupRT != null && !((GraphicsResource)groupRT).IsDisposed)
		{
			((GraphicsResource)groupRT).Dispose();
		}
		if (basicEffect != null && !((GraphicsResource)basicEffect).IsDisposed)
		{
			((GraphicsResource)basicEffect).Dispose();
			basicEffect = null;
		}
		if (warmPixel != null && !((GraphicsResource)warmPixel).IsDisposed)
		{
			((GraphicsResource)warmPixel).Dispose();
			warmPixel = null;
		}
		if (warmTarget != null && !((GraphicsResource)warmTarget).IsDisposed)
		{
			((GraphicsResource)warmTarget).Dispose();
			warmTarget = null;
		}
		// Re-arm the warm: LoadContent recreates basicEffect, whose GL programs must be re-compiled on a
		// context-loss/reload, so the next tower scene must warm again rather than early-return.
		geom3dWarmed = false;
		spriteBatch.Dispose();
		base.UnloadContent();
	}
}
