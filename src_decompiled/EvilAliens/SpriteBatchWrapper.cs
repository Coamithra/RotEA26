using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

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

	private void _beginDrawing()
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		if (effectHandler.HasChanged())
		{
			Flush();
		}
		if (!enabled)
		{
			spriteBatch.Begin(blendmode, (SpriteSortMode)0, (SaveStateMode)0);
			effectHandler.LoadEffects();
			enabled = true;
		}
	}

	public void Flush()
	{
		if (enabled)
		{
			effectHandler.UnloadEffects();
			spriteBatch.End();
			enabled = false;
		}
	}

	public void DrawString(SpriteFont spritefont, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects spriteeffect, float layerdepth)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.DrawString(spritefont, text, position, color, rotation, origin, scale, spriteeffect, layerdepth);
	}

	public void DrawString(string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects spriteeffect, float layerdepth)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		_beginDrawing();
		spriteBatch.DrawString(font, text, position, color, rotation, origin, scale, spriteeffect, layerdepth);
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
		spriteBatch.DrawString(font, text, position, color, rotation, val, scale, spriteeffect, layerdepth);
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
		spriteBatch.DrawString(font, text, position, color, rotation, val, scale, spriteeffect, layerdepth);
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
			((Vector2)(ref zero))._002Ector((float)(texture.Width / 2), (float)(texture.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(texture.Width / 2), (float)(texture.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(texture.Width / 2), (float)(texture.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(texture.Width / 2), (float)(texture.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(texture.Width / 2), (float)(texture.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(source.Width / 2), (float)(source.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(source.Width / 2), (float)(source.Height / 2));
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
			((Vector2)(ref zero))._002Ector((float)(source.Width / 2), (float)(source.Height / 2));
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
		((DrawableGameComponent)this).LoadContent();
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
		((DrawableGameComponent)this).UnloadContent();
	}
}
