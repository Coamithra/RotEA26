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
		effectHandler.LoadGraphicsContent(loadAllContent: true);
	}

	protected override void UnloadContent()
	{
		Flush();
		effectHandler.UnloadGraphicsContent(unloadAllContent: true);
		spriteBatch.Dispose();
		base.UnloadContent();
	}
}
