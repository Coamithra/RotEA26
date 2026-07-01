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

	// The chrome-sheen effect (metal.fx), owned here so call sites don't each load/pass
	// it. Loaded in LoadContent; null => DrawMetalString degrades to a plain DrawString.
	private Effect metalEffect;

	// Cached metal.fx EffectParameter handles for the params that VARY per call (Time / the two
	// glyph-band insets / the used-subrect UV). The invariant params (GradTop/Mid/Bot, Glint*,
	// Sweep*) are identical for every call and are set ONCE in LoadContent, so the per-call path
	// avoids ~11 string-keyed Parameters[name] dictionary lookups. Populated when metalEffect loads.
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

	// Transparent border (design px) baked around the text in the metal RT so the glint
	// sweep and bloom have overshoot room and don't clip at the glyph edges.
	private const int MetalPad = 6;

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
			spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, effectHandler.CurrentEffect, RenderScale.Matrix);
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

		// --- Pass 1: rasterise the text into the RT's top-left corner at render res ---
		// BlendState.AlphaBlend (One/InvSrcAlpha) onto a TRANSPARENT target copies the
		// straight-alpha glyphs verbatim (dst is 0, so the InvSrcAlpha*dst term vanishes).
		// NonPremultiplied here would instead square the alpha (srcA*srcA) and premultiply
		// the colour, thinning the edges — invisible over black but haloed over the menu.
		// Capture whatever target is currently bound so we can restore IT after the RT
		// ping-pong. DrawMetalString runs mid-draw: from a plain scene draw the bound
		// target is the Stage-10 scene target, but from inside a menu (MenuSub1.Draw)
		// it's the menu's OWN render target, which the menu later composites with the
		// zoom-transition scale via DrawPresent. Hardcoding SetRenderTarget(0, null) here
		// resolves (via the compat shim) to the scene target, so after the first metal
		// string the menu RT is abandoned: its composite + every later entry leak
		// straight to the scene unzoomed, breaking the transition and the selection
		// highlight. Restore the captured binding instead.
		RenderTargetBinding[] prevTargets = base.GraphicsDevice.GetRenderTargets();
		Flush();                                                   // end any active scene batch
		base.GraphicsDevice.SetRenderTarget(0, metalRT);
		base.GraphicsDevice.Clear(Color.Transparent);
		// design -> RT: translate the padded box to the RT origin, then scale to render res.
		Matrix m = Matrix.CreateTranslation(MetalPad, MetalPad, 0f) * Matrix.CreateScale(rs);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, m);
		DrawStringScaled(font, text, Vector2.Zero, tint, 0f, Vector2.Zero, new Vector2(1f, 1f), (SpriteEffects)0, 0f);
		spriteBatch.End();
		// Restore the target that was bound on entry (menu RT or scene target), NOT a
		// hardcoded null, so Pass 2's composite + any following draws land where the
		// caller expects. In practice prevTargets is always length 1 here: every
		// DrawMetalString runs inside Game1.DrawInner, which keeps a target bound for the
		// whole frame, so the empty-array case (real back buffer bound) is unreachable in
		// that flow. The fallback routes through the compat null (-> BaseRenderTarget)
		// rather than handing an empty array to SetRenderTargets, purely defensively.
		if (prevTargets != null && prevTargets.Length > 0)
		{
			base.GraphicsDevice.SetRenderTargets(prevTargets);
		}
		else
		{
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		}

		// --- Pass 2: composite the RT's used sub-rect through metal.fx ---
		// FLOAT-precise (sub-pixel) draw mirroring DrawString's transform EXACTLY: the RT
		// holds the text offset by MetalPad at render scale, so origin (in RT texels) =
		// (origin + pad) * rs and drawScale = scale / rs reproduce DrawString's placement
		// for any design `origin` (incl. centred) while applying the sheen. Integer dest
		// rects are avoided on purpose — rounding a pulsating rect each frame wobbles.
		Rectangle used = new Rectangle(0, 0, usedW, usedH);
		int texW = ((Texture2D)metalRT).Width;
		int texH = ((Texture2D)metalRT).Height;
		// Symmetric box (no drop shadow): equal top/bottom glyph-band inset.
		float padFracY = (float)MetalPad / boxH;
		Vector2 uvExtent = new Vector2((float)usedW / texW, (float)usedH / texH);
		// Invariant params (Grad*/Glint*/Sweep*) are set once in LoadContent; only these vary per call.
		mpTime?.SetValue(time);
		mpPadTop?.SetValue(padFracY);
		mpPadBot?.SetValue(padFracY);
		mpUvExtent?.SetValue(uvExtent);
		float drawScale = scale / rs;
		Vector2 rtOrigin = (origin + new Vector2(MetalPad, MetalPad)) * rs;
		spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, metalEffect, RenderScale.Matrix);
		spriteBatch.Draw(metalRT, position, (Rectangle?)used, Color.White, rotation, rtOrigin, drawScale, (SpriteEffects)0, 0f);
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

	// Pass 1: rasterise shadow-then-text OPAQUE into `rt`'s top-left corner at render res.
	// BlendState.AlphaBlend (One/InvSrcAlpha) onto a TRANSPARENT target copies the straight-alpha
	// glyphs verbatim (same trick DrawMetalString documents). The text top-left sits at the
	// MetalPad inset; the shadow is offset from there by shadowOffset, so the whole drop fits inside
	// the padded box. Where the opaque text covers the opaque shadow, the text wins — the
	// bleed-through fix. Does the mid-draw target ping-pong and restores whatever was bound on entry
	// (scene target, or a menu's own RT), NOT a hardcoded null — see the long note in DrawMetalString.
	private void RasteriseShadowText(RenderTarget2D rt, string text, float scale, Color shadowColor, Color textColor, Vector2 shadowOffset, float rs)
	{
		RenderTargetBinding[] prevTargets = base.GraphicsDevice.GetRenderTargets();
		Flush();
		base.GraphicsDevice.SetRenderTarget(0, rt);
		base.GraphicsDevice.Clear(Color.Transparent);
		Matrix m = Matrix.CreateTranslation(MetalPad, MetalPad, 0f) * Matrix.CreateScale(rs);
		Color shadowOpaque = new Color(shadowColor.R, shadowColor.G, shadowColor.B, byte.MaxValue);
		Color textOpaque = new Color(textColor.R, textColor.G, textColor.B, byte.MaxValue);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, m);
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
	// through metal.fx. The RT holds a straight-alpha image; composite with the current BlendMode
	// (NonPremultiplied for the score) and a white tint carrying `alpha`, so the element fades as one
	// sprite. metal.fx returns float4(rgb, mask) * color, so the same tint alpha carries through the
	// chrome path too. boxH + shadowOffset.Y feed the asymmetric glyph-band insets (the drop shadow
	// extends the bottom) so the chrome gradient lands on the letters, not on the shadow overshoot.
	private void CompositeShadowText(RenderTarget2D rt, int usedW, int usedH, float boxH, Vector2 shadowOffset, Vector2 position, float alpha, bool metal, float glintTime, float rs)
	{
		// End any active wrapper batch before opening our own. RasteriseShadowText already flushes,
		// but on the cached fast path (clean sprite) Pass 1 is skipped, so an earlier _beginDrawing
		// batch may still be open here — flush it or spriteBatch.Begin throws "Begin cannot be called
		// again until End". Idempotent when a rasterise just ran (enabled is already false).
		Flush();
		Rectangle used = new Rectangle(0, 0, usedW, usedH);
		Color composite = new Color((byte)255, (byte)255, (byte)255, (byte)MathHelper.Clamp(alpha * 255f, 0f, 255f));
		Effect fx = (metal && metalEffect != null) ? metalEffect : null;
		if (fx != null)
		{
			int texW = ((Texture2D)rt).Width;
			int texH = ((Texture2D)rt).Height;
			// Invariant params (Grad*/Glint*/Sweep*) are set once in LoadContent; only these vary.
			float padFracTop = (float)MetalPad / boxH;
			float padFracBot = (float)(MetalPad + Math.Abs(shadowOffset.Y)) / boxH;
			mpTime?.SetValue(glintTime);
			mpPadTop?.SetValue(padFracTop);
			mpPadBot?.SetValue(padFracBot);
			mpUvExtent?.SetValue(new Vector2((float)usedW / texW, (float)usedH / texH));
		}
		float drawScale = 1f / rs;
		Vector2 rtOrigin = new Vector2(MetalPad, MetalPad) * rs;    // text top-left in RT texels
		spriteBatch.Begin(SpriteSortMode.Deferred, ToBlendState(blendmode), null, null, null, fx, RenderScale.Matrix);
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
			if (!sf.Glyphs.TryGetValue(ch, out SpriteFont.Glyph g))
			{
				if (!sf.DefaultCharacter.HasValue || !sf.Glyphs.TryGetValue(sf.DefaultCharacter.Value, out g))
					continue;
			}
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
			// Invariant metal.fx params are identical for every DrawMetalString / DrawShadowString
			// call, so set them ONCE here rather than re-looking-up + re-setting all 11 per call.
			// Safe because metalEffect is created once and never recreated: BlazorGL/WASM has no
			// device-lost/reset cycle, so neither the set-once values nor the cached param handles
			// below can go stale. (On a backend that reloaded effects on a graphics reset, both
			// would need to be re-applied/re-fetched after the reset.)
			SetParam(metalEffect, "GradTop", 1.18f);
			SetParam(metalEffect, "GradMid", 0.50f);
			SetParam(metalEffect, "GradBot", 0.95f);
			SetParam(metalEffect, "GlintStrength", 0.9f);
			SetParam(metalEffect, "GlintWidth", 0.06f);
			SetParam(metalEffect, "SweepPeriod", MetalSweepPeriod);
			SetParam(metalEffect, "SweepActive", MetalSweepActive);
			// Cache the handles for the params that vary per call (set every draw).
			mpTime = metalEffect.Parameters["Time"];
			mpPadTop = metalEffect.Parameters["PadFracTop"];
			mpPadBot = metalEffect.Parameters["PadFracBot"];
			mpUvExtent = metalEffect.Parameters["UvExtent"];
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
		spriteBatch.Dispose();
		base.UnloadContent();
	}
}
