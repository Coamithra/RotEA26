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
				Vector2 origin = font.MeasureString(text) / 2f;
				float num = MathHelper.SmoothStep(0f, 1f, lifetime / 923.07697f);
				float num2 = 2f + 1.2f * (1f - num);
				byte b = (byte)(225f * num);
				Color color = default(Color);
				(color) = new Color(byte.MaxValue, byte.MaxValue, (byte)128, b);
				Color color2 = default(Color);
				(color2) = new Color((byte)118, (byte)118, (byte)21, b);
				wrapper.DrawString(font, text, position - new Vector2(0f, 0f), color2, 0f, origin, num2 * scale, (SpriteEffects)0, 1f);
				wrapper.DrawString(font, text, position - new Vector2(3f, 3f), color, 0f, origin, num2 * scale, (SpriteEffects)0, 1f);
				break;
			}
			}
		}
	}
}
