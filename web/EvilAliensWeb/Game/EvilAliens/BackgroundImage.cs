using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

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

	// Holodeck glitch hook (driven by Background while in the simulator): drawOffset shifts
	// the whole tiled layer by a few px without touching the wrapped scroll position. A no-op
	// at its default, so non-simulator backgrounds render exactly as before.
	public Vector2 drawOffset;

	public string[,] new_texturenames;

	public Texture2D[,] new_textures;

	public Timer switchTimer = new Timer(5000f, repeating: false);

	public BackgroundImage()
	{
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
		Vector2 origin = position;
		origin -= realsize;
		origin += drawOffset;
		Vector2 cursor = origin;
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
				DrawBackground(cursor, spriteBatch, alternate: false, fade);
				cursor.Y += realsize.Y;
			}
			cursor.Y = origin.Y;
			cursor.X += realsize.X;
		}
		if (switchTimer.Active)
		{
			origin = position - realsize + drawOffset;
			cursor = origin;
			spriteBatch.BlendMode = (SpriteBlendMode)2;
			for (int k = 0; k < UpperDiv(800f, realsize.X) + 1; k++)
			{
				for (int l = 0; l < UpperDiv(600f, realsize.Y) + 1; l++)
				{
					DrawBackground(cursor, spriteBatch, alternate: true, 1f - switchTimer.Normalized);
					cursor.Y += realsize.Y;
				}
				cursor.Y = origin.Y;
				cursor.X += realsize.X;
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
		Texture2D[,] tiles = textures;
		if (alternate)
		{
			tiles = new_textures;
		}
		Color tint = default(Color);
		if (color == Color.White)
		{
			(tint) = new Color(new Vector4(fade, fade, fade, 1f));
		}
		else
		{
			tint = color;
		}
		// Tile placement is PIXEL-space: use each texture's LOGICAL (pre-pad) size, not the padded
		// .Width/.Height, or a padded .dds advances/wraps ~pad px too far and leaves a transparent
		// gap between tiles (the DXT mult-of-4 + --padtest canary; unpadded png is a no-op here).
		Vector2 offset = default(Vector2);
		(offset) = new Vector2(0f, 0f);
		for (int i = 0; i < tiles.GetLength(0); i++)
		{
			offset.Y = 0f;
			for (int j = 0; j < tiles.GetLength(1); j++)
			{
				if ((position.X + offset.X + (float)tiles[i, j].LogicalWidth() * size >= 0f) & (position.X + offset.X < 800f) & (position.Y + offset.Y + (float)tiles[i, j].LogicalWidth() * size >= 0f) & (position.Y + offset.Y < 600f))
				{
					spriteBatch.Draw(tiles[i, j], position + offset, 0f, size, center: false, tint);
				}
				offset.Y += size * (float)tiles[i, j].LogicalHeight();
			}
			if (mirrorY)
			{
				for (int mirrorRow = tiles.GetLength(1) - 1; mirrorRow >= 0; mirrorRow--)
				{
					if ((position.X + offset.X + (float)tiles[i, mirrorRow].LogicalWidth() * size >= 0f) & (position.X + offset.X < 800f) & (position.Y + offset.Y + (float)tiles[i, mirrorRow].LogicalWidth() * size >= 0f) & (position.Y + offset.Y < 600f))
					{
						spriteBatch.Draw(tiles[i, mirrorRow], position + offset, 0f, size, center: false, tint, (SpriteEffects)256);
					}
					offset.Y += size * (float)tiles[i, mirrorRow].LogicalHeight();
				}
			}
			offset.X += size * (float)tiles[i, 0].LogicalWidth();
		}
		if (!mirrorX)
		{
			return;
		}
		for (int mirrorCol = tiles.GetLength(0) - 1; mirrorCol >= 0; mirrorCol--)
		{
			offset.Y = 0f;
			for (int k = 0; k < tiles.GetLength(1); k++)
			{
				if ((position.X + offset.X + (float)tiles[mirrorCol, k].LogicalWidth() >= 0f) & (position.X + offset.X < 800f) & (position.Y + offset.Y + (float)tiles[mirrorCol, k].LogicalWidth() >= 0f) & (position.Y + offset.Y < 600f))
				{
					spriteBatch.Draw(tiles[mirrorCol, k], position + offset, 0f, size, center: false, tint, (SpriteEffects)1);
				}
				offset.Y += size * (float)tiles[mirrorCol, k].LogicalHeight();
			}
			if (mirrorY)
			{
				for (int mirrorRow = tiles.GetLength(1) - 1; mirrorRow >= 0; mirrorRow--)
				{
					if ((position.X + offset.X + (float)tiles[mirrorCol, mirrorRow].LogicalWidth() >= 0f) & (position.X + offset.X < 800f) & (position.Y + offset.Y + (float)tiles[mirrorCol, mirrorRow].LogicalWidth() >= 0f) & (position.Y + offset.Y < 600f))
					{
						spriteBatch.Draw(tiles[mirrorCol, mirrorRow], position + offset, 0f, size, center: false, tint, (SpriteEffects)257);
					}
					offset.Y += size * (float)tiles[mirrorCol, mirrorRow].LogicalHeight();
				}
			}
			offset.X += size * (float)tiles[mirrorCol, 0].LogicalWidth();
		}
	}
}
