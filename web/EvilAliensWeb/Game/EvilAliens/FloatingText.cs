using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class FloatingText
{
	public enum ShowType
	{
		scrollup,
		pop
	}

	private const float lifetimeinitial = 1200f;

	private const float standardscale = 0.4f;

	private ShowType showType;

	private string text;

	private Vector2 position;

	private float lifetime;

	public bool done;

	private float scale;

	public FloatingText(Vector2 position, ShowType type, string suffix)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		Reset(position, type, suffix);
	}

	public FloatingText(int amount, Vector2 position, ShowType type, string suffix)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		Reset(amount, position, type, suffix);
	}

	public void Reset(Vector2 position, ShowType type, string suffix)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		Reset(100, position, type, suffix);
		text = suffix;
	}

	public void Reset(int amount, Vector2 position, ShowType type, string suffix)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		showType = type;
		text = amount + suffix;
		this.position = position;
		lifetime = 1200f;
		done = false;
		scale = 0.4f;
		if (amount >= 100)
		{
			scale = 0.6f;
		}
		if (amount >= 500)
		{
			scale = 0.8f;
		}
		if (amount >= 1000)
		{
			scale = 1.6f;
		}
		if (amount >= 5000)
		{
			scale = 2f;
		}
		if (amount >= 10000)
		{
			scale = 4f;
		}
	}

	public void Update(GameTime gameTime)
	{
		lifetime -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (lifetime <= 0f)
		{
			lifetime = 0f;
			done = true;
		}
	}

	public void Draw(SpriteFont font, SpriteBatchWrapper wrapper)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		if (!done)
		{
			switch (showType)
			{
			case ShowType.scrollup:
			{
				Vector2 origin2 = font.MeasureString(text) / 2f;
				float num3 = MathHelper.SmoothStep(0f, 1f, lifetime / 1200f);
				byte b2 = (byte)(225f * num3);
				float num4 = 25f * (1f - num3);
				Color color3 = default(Color);
				(color3) = new Color((byte)0, (byte)128, (byte)128, b2);
				wrapper.DrawString(font, text, position - new Vector2(0f, num4), color3, 0f, origin2, scale, (SpriteEffects)0, 1f);
				break;
			}
			case ShowType.pop:
			{
				// "Power Up!" / combo pops. The original drew the dark drop shadow and the bright
				// text as two SEPARATE translucent DrawStrings at the SAME alpha (text offset
				// up-left by 3px), so the translucent shadow showed THROUGH the translucent text
				// where they overlap — the same bleed-through the score had. Flatten them into ONE
				// sprite via DrawShadowString (shadow+text rasterised opaque, the text on top hides
				// the shadow it covers, then composited once at `alpha`), so they fade as a single
				// sprite with no bleed. Placement is preserved exactly: DrawShadowString lands the
				// text top-left at `position`, so we pass the original centred top-left
				// (centre position-(3,3), origin = measure/2) and the shadow sits +3,+3 from the
				// text as before. metal:false keeps the plain floating-text look (no chrome sheen).
				float num = MathHelper.SmoothStep(0f, 1f, lifetime / 923.07697f);
				float popscale = (2f + 1.2f * (1f - num)) * scale;
				float alpha = 225f / 255f * num;
				Color textColor = new Color(byte.MaxValue, byte.MaxValue, (byte)128, byte.MaxValue);
				Color shadowColor = new Color((byte)118, (byte)118, (byte)21, byte.MaxValue);
				Vector2 textTopLeft = position - new Vector2(3f, 3f) - font.MeasureString(text) / 2f * popscale;
				wrapper.DrawShadowString(text, textTopLeft, popscale, shadowColor, textColor, new Vector2(3f, 3f), alpha, metal: false);
				break;
			}
			}
		}
	}
}
