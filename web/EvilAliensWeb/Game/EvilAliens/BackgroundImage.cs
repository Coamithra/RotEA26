using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class BackgroundImage
{
	public Color color;

	public string[,] texturenames;

	public Texture2D[,] textures;

	public Vector2 position;

	public float scrollspeedmodifier;

	public float size;

	public Vector2 realsize;

	public bool mirrorX;

	public bool mirrorY;

	public SpriteBlendMode blendMode = (SpriteBlendMode)1;

	public string[,] new_texturenames;

	public Texture2D[,] new_textures;

	public Timer switchTimer = new Timer(5000f, repeating: false);

	public BackgroundImage()
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		color = Color.White;
		switchTimer.Stop();
		switchTimer.Reset();
	}

	private int UpperDiv(float a, float b)
	{
		double value = Math.Round(a / b + 0.5f);
		return Convert.ToInt16(value);
	}

	public void StartSwitch()
	{
		switchTimer.Reset();
		switchTimer.Start();
	}

	public void Move(Vector2 positionChange)
	{
		position.X = MyMath.Mod(position.X + positionChange.X * scrollspeedmodifier, realsize.X);
		position.Y = MyMath.Mod(position.Y + positionChange.Y * scrollspeedmodifier, realsize.Y);
	}

	public void LoadGraphics(ContentManager content)
	{
		for (int i = 0; i < texturenames.GetLength(0); i++)
		{
			for (int j = 0; j < texturenames.GetLength(1); j++)
			{
				textures[i, j] = content.Load<Texture2D>(texturenames[i, j]);
			}
		}
		if (!switchTimer.Active)
		{
			return;
		}
		for (int k = 0; k < new_texturenames.GetLength(0); k++)
		{
			for (int l = 0; l < new_texturenames.GetLength(1); l++)
			{
				new_textures[k, l] = content.Load<Texture2D>(new_texturenames[k, l]);
			}
		}
	}

	public void Draw(SpriteBatchWrapper spriteBatch, GameTime gameTime)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = position;
		val -= realsize;
		Vector2 val2 = val;
		spriteBatch.BlendMode = blendMode;
		float fade = 1f;
		if (switchTimer.Active)
		{
			fade = switchTimer.Normalized;
		}
		for (int i = 0; i < UpperDiv(800f, realsize.X) + 1; i++)
		{
			for (int j = 0; j < UpperDiv(600f, realsize.Y) + 1; j++)
			{
				DrawBackground(val2, spriteBatch, alternate: false, fade);
				val2.Y += realsize.Y;
			}
			val2.Y = val.Y;
			val2.X += realsize.X;
		}
		if (switchTimer.Active)
		{
			val = position - realsize;
			val2 = val;
			spriteBatch.BlendMode = (SpriteBlendMode)2;
			for (int k = 0; k < UpperDiv(800f, realsize.X) + 1; k++)
			{
				for (int l = 0; l < UpperDiv(600f, realsize.Y) + 1; l++)
				{
					DrawBackground(val2, spriteBatch, alternate: true, 1f - switchTimer.Normalized);
					val2.Y += realsize.Y;
				}
				val2.Y = val.Y;
				val2.X += realsize.X;
			}
		}
		switchTimer.Update(gameTime);
		if (switchTimer.Finished)
		{
			switchTimer.Reset();
			switchTimer.Stop();
			textures = new_textures;
			texturenames = new_texturenames;
		}
	}

	private void DrawBackground(Vector2 position, SpriteBatchWrapper spriteBatch, bool alternate, float fade)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Unknown result type (might be due to invalid IL or missing references)
		//IL_0354: Unknown result type (might be due to invalid IL or missing references)
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_0356: Unknown result type (might be due to invalid IL or missing references)
		//IL_0367: Unknown result type (might be due to invalid IL or missing references)
		//IL_0452: Unknown result type (might be due to invalid IL or missing references)
		//IL_0453: Unknown result type (might be due to invalid IL or missing references)
		//IL_0454: Unknown result type (might be due to invalid IL or missing references)
		//IL_0465: Unknown result type (might be due to invalid IL or missing references)
		Texture2D[,] array = textures;
		if (alternate)
		{
			array = new_textures;
		}
		Color val = default(Color);
		if (color == Color.White)
		{
			(val) = new Color(new Vector4(fade, fade, fade, 1f));
		}
		else
		{
			val = color;
		}
		Vector2 val2 = default(Vector2);
		(val2) = new Vector2(0f, 0f);
		for (int i = 0; i < array.GetLength(0); i++)
		{
			val2.Y = 0f;
			for (int j = 0; j < array.GetLength(1); j++)
			{
				if ((position.X + val2.X + (float)array[i, j].Width * size >= 0f) & (position.X + val2.X < 800f) & (position.Y + val2.Y + (float)array[i, j].Width * size >= 0f) & (position.Y + val2.Y < 600f))
				{
					spriteBatch.Draw(array[i, j], position + val2, 0f, size, center: false, val);
				}
				val2.Y += size * (float)array[i, j].Height;
			}
			if (mirrorY)
			{
				for (int num = array.GetLength(1) - 1; num >= 0; num--)
				{
					if ((position.X + val2.X + (float)array[i, num].Width * size >= 0f) & (position.X + val2.X < 800f) & (position.Y + val2.Y + (float)array[i, num].Width * size >= 0f) & (position.Y + val2.Y < 600f))
					{
						spriteBatch.Draw(array[i, num], position + val2, 0f, size, center: false, val, (SpriteEffects)256);
					}
					val2.Y += size * (float)array[i, num].Height;
				}
			}
			val2.X += size * (float)array[i, 0].Width;
		}
		if (!mirrorX)
		{
			return;
		}
		for (int num2 = array.GetLength(0) - 1; num2 >= 0; num2--)
		{
			val2.Y = 0f;
			for (int k = 0; k < array.GetLength(1); k++)
			{
				if ((position.X + val2.X + (float)array[num2, k].Width >= 0f) & (position.X + val2.X < 800f) & (position.Y + val2.Y + (float)array[num2, k].Width >= 0f) & (position.Y + val2.Y < 600f))
				{
					spriteBatch.Draw(array[num2, k], position + val2, 0f, size, center: false, val, (SpriteEffects)1);
				}
				val2.Y += size * (float)array[num2, k].Height;
			}
			if (mirrorY)
			{
				for (int num3 = array.GetLength(1) - 1; num3 >= 0; num3--)
				{
					if ((position.X + val2.X + (float)array[num2, num3].Width >= 0f) & (position.X + val2.X < 800f) & (position.Y + val2.Y + (float)array[num2, num3].Width >= 0f) & (position.Y + val2.Y < 600f))
					{
						spriteBatch.Draw(array[num2, num3], position + val2, 0f, size, center: false, val, (SpriteEffects)257);
					}
					val2.Y += size * (float)array[num2, num3].Height;
				}
			}
			val2.X += size * (float)array[num2, 0].Width;
		}
	}
}
