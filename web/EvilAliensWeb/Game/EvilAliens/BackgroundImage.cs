using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class BackgroundImage
{
	// One tile visit recorded by the cull trace: where it landed, its TRUE on-screen extent
	// (always LogicalWidth/Height * size, whatever the predicate under test measured), and
	// whether the cull kept it. Compat/BgCullTest reads these.
	internal struct TracedTile
	{
		public float X;

		public float Y;

		public float W;

		public float H;

		public bool Drawn;
	}

	// Cull instrumentation (card 5216412d), armed only by eaBgCull(). Unlike the FrameProfiler
	// these seams are NOT compiled out when idle: every build pays one static read per tile
	// (the null test here, plus the CullTraceDryRun test at each of the four cull sites and
	// twice in Draw). That is a handful of predictable branches on a path whose worst case is
	// the holodeck grid's ~150 tiles/frame, against a draw call each -- the ?binlog /
	// ?walltrace / LoadProfiler idiom. Nothing allocates while the log is null.
	internal static List<TracedTile> CullTraceLog;

	// While set, Draw walks and records every tile but touches no graphics state at all, so
	// a scenario layer can be exercised through the REAL draw path with a null SpriteBatch.
	internal static bool CullTraceDryRun;

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

	// A tile at (x,y) covers [x, x + tileW*scale) x [y, y + tileH*scale) in 800x600 design
	// space, so it only needs drawing when that rect overlaps the screen. ONE predicate on
	// purpose: this replaced four hand-maintained copies of the condition, and they had
	// drifted apart -- two measured the tile's WIDTH along Y, and the two mirrorX ones had
	// lost the * size factor entirely, so at any size != 1 they culled against unscaled
	// pixel extents (card 5216412d).
	//
	// Both slips cull tiles that are VISIBLE (a missing strip at the screen edge), not merely
	// draw spare ones: measuring width along Y under-tests a TALL tile, and dropping * size
	// under-tests any layer drawn bigger than its art. Neither could show on a shipped
	// background -- nothing sets mirrorX/mirrorY and every live tile is square or wider than
	// tall -- which is exactly why eaBgCull() reads the decisions as data.
	//
	// The right/bottom test is STRICT (card ef55b76e). The interval is half-open, so a tile whose
	// right or bottom edge lands exactly on 0 covers zero on-screen area and painting it is pure
	// cost: the quad spans [-w, 0], no pixel centre falls inside it, and nothing reaches the
	// framebuffer. This is not a rare tie. Draw starts its grid at position - realsize, so for any
	// layer whose realsize matches its tile the first column sits exactly on the boundary at scroll
	// phase 0 -- and since only X ever scrolls, position.Y stays 0 forever, which put the whole top
	// ROW of every [1,1] layer in this case on every single frame of play (half of every Mars
	// parallax layer's draws). eaBgCull()'s differential section is what proves the tightening can
	// change no pixel.
	internal static bool TileOnScreen(float x, float y, int tileW, int tileH, float scale)
	{
		return x + (float)tileW * scale > 0f && x < 800f && y + (float)tileH * scale > 0f && y < 600f;
	}

	private void NoteTile(bool drawn, float x, float y, Texture2D tile)
	{
		if (CullTraceLog != null)
		{
			CullTraceLog.Add(new TracedTile
			{
				X = x,
				Y = y,
				W = (float)tile.LogicalWidth() * size,
				H = (float)tile.LogicalHeight() * size,
				Drawn = drawn
			});
		}
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
		if (!CullTraceDryRun)
		{
			spriteBatch.BlendMode = blendMode;
		}
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
			if (!CullTraceDryRun)
			{
				spriteBatch.BlendMode = (SpriteBlendMode)2;
			}
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
		// A dry run must observe the layer, never advance it.
		if (CullTraceDryRun)
		{
			return;
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
		Vector2 offset = new Vector2(0f, 0f);
		for (int i = 0; i < tiles.GetLength(0); i++)
		{
			offset.Y = 0f;
			for (int j = 0; j < tiles.GetLength(1); j++)
			{
				bool onScreen = TileOnScreen(position.X + offset.X, position.Y + offset.Y, tiles[i, j].LogicalWidth(), tiles[i, j].LogicalHeight(), size);
				NoteTile(onScreen, position.X + offset.X, position.Y + offset.Y, tiles[i, j]);
				if (onScreen && !CullTraceDryRun)
				{
					spriteBatch.Draw(tiles[i, j], position + offset, 0f, size, center: false, tint);
				}
				offset.Y += size * (float)tiles[i, j].LogicalHeight();
			}
			if (mirrorY)
			{
				for (int mirrorRow = tiles.GetLength(1) - 1; mirrorRow >= 0; mirrorRow--)
				{
					bool mirrorRowOnScreen = TileOnScreen(position.X + offset.X, position.Y + offset.Y, tiles[i, mirrorRow].LogicalWidth(), tiles[i, mirrorRow].LogicalHeight(), size);
					NoteTile(mirrorRowOnScreen, position.X + offset.X, position.Y + offset.Y, tiles[i, mirrorRow]);
					if (mirrorRowOnScreen && !CullTraceDryRun)
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
				bool mirrorColOnScreen = TileOnScreen(position.X + offset.X, position.Y + offset.Y, tiles[mirrorCol, k].LogicalWidth(), tiles[mirrorCol, k].LogicalHeight(), size);
				NoteTile(mirrorColOnScreen, position.X + offset.X, position.Y + offset.Y, tiles[mirrorCol, k]);
				if (mirrorColOnScreen && !CullTraceDryRun)
				{
					spriteBatch.Draw(tiles[mirrorCol, k], position + offset, 0f, size, center: false, tint, (SpriteEffects)1);
				}
				offset.Y += size * (float)tiles[mirrorCol, k].LogicalHeight();
			}
			if (mirrorY)
			{
				for (int mirrorRow = tiles.GetLength(1) - 1; mirrorRow >= 0; mirrorRow--)
				{
					bool mirrorBothOnScreen = TileOnScreen(position.X + offset.X, position.Y + offset.Y, tiles[mirrorCol, mirrorRow].LogicalWidth(), tiles[mirrorCol, mirrorRow].LogicalHeight(), size);
					NoteTile(mirrorBothOnScreen, position.X + offset.X, position.Y + offset.Y, tiles[mirrorCol, mirrorRow]);
					if (mirrorBothOnScreen && !CullTraceDryRun)
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
