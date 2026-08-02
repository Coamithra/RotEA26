using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionLevelMap : ICollisionType
{
	private bool[,] map;

	private Vector2 offset;

	// Design-space size of one tile, SUPPLIED BY THE OWNER rather than re-derived here.
	//
	// It used to be the literal 800/width, which equals Wall's own drawn block size
	// (texture.LogicalWidth() * scale) ONLY because Wall.Setup computes scale as
	// 800/(LogicalWidth*width) -- an agreement by coincidence of two formulas in different files,
	// with nothing tying them together. Online co-op broke exactly that coincidence: a puppet's
	// `scale` came off the wire as a truncated u16 at 1/256 (up to 4.9% out for a Level-3 wall),
	// so the joiner DREW 63.4px rows while this grid still COLLIDED on 66.7px ones -- the
	// collision rows reaching progressively further down than the towers, which is why the joiner
	// hit walls before touching them and its bullets vanished short of them (cards 4392bd30 /
	// 80749dc4). The scale bug itself is fixed at the source (Wall.NetScaleLocal); taking the size
	// from the owner is what makes the two able to disagree again a compile error rather than a
	// silent divergence.
	private float tileSize;

	public int Width => map.GetLength(1);

	// Design-space width of one tile. GetMapCoords divides by this, so anything reasoning about
	// the grid in world units (the AI's wall navigation) needs the same number rather than a
	// second copy of the owner's formula.
	public float TileSize => tileSize;

	// World X of column `x`'s centre -- the inverse of GetMapCoords' X mapping. The grid's
	// `offset` scrolls and is private, so a caller that wants to FLY to a column (rather than
	// ask which column it is in) cannot derive this itself.
	public float ColumnCentreX(int x)
	{
		return offset.X + ((float)x + 0.5f) * TileSize;
	}

	// World Y of row `y`'s BOTTOM edge -- the face that arrives first as the grid scrolls down.
	// GetMapCoords uses the same tile size on both axes, so this is the Y counterpart of
	// ColumnCentreX. The AI needs the distance in PIXELS to the row that will block it; a row
	// COUNT cannot say whether that row is 60px away or 1000px away, and treating those the same
	// makes an avoidance push either permanent or far too late.
	public float RowBottomY(int y)
	{
		return offset.Y + ((float)y + 1f) * TileSize;
	}

	public bool TileIsOccupied(int x, int y)
	{
		if (y < 0 || y >= map.GetLength(0))
		{
			return false;
		}
		if (x >= 0 && x < map.GetLength(1))
		{
			return map[y, x];
		}
		return true;
	}

	public void GetMapCoords(ref int x, ref int y, Vector2 position)
	{
		position -= offset;
		float num = tileSize;
		x = (int)Math.Floor(position.X / num);
		y = (int)Math.Floor(position.Y / num);
	}

	public CollisionLevelMap(Vector2 offset, bool[,] map, float tileSize)
	{
		this.map = map;
		this.offset = offset;
		this.tileSize = tileSize;
	}

	public void SetOffset(Vector2 offset)
	{
		this.offset = offset;
	}

	public void SetTileSize(float tileSize)
	{
		this.tileSize = tileSize;
	}

	public void SetMap(bool[,] map)
	{
		this.map = map;
	}

	public bool TestCollision(ICollisionType other)
	{
		if (other is CollisionBox)
		{
			return TestCollisionBox((CollisionBox)other);
		}
		if (other is CollisionLine)
		{
			return TestCollisionLine((CollisionLine)other);
		}
		if (other is CollisionSimpleCircle)
		{
			return TestCollisionSimpleCircle((CollisionSimpleCircle)other);
		}
		if (other is CollisionMultibox)
		{
			return TestCollisionMultibox((CollisionMultibox)other);
		}
		return false;
	}

	private bool TestCollisionMultibox(CollisionMultibox collisionMultibox)
	{
		return collisionMultibox.TestCollision(this);
	}

	private bool TestCollisionSimpleCircle(CollisionSimpleCircle collisionSimpleCircle)
	{
		Vector2 val = new Vector2(collisionSimpleCircle.Radius * 0.8f, collisionSimpleCircle.Radius * 0.8f);
		CollisionBox collisionBox = new CollisionBox(collisionSimpleCircle.Position - val, collisionSimpleCircle.Position + val);
		return TestCollisionBox(collisionBox);
	}

	private bool TestCollisionLine(CollisionLine collisionLine)
	{
		int x = 0;
		int y = 0;
		GetMapCoords(ref x, ref y, collisionLine.Start);
		int x2 = 0;
		int y2 = 0;
		GetMapCoords(ref x2, ref y2, collisionLine.End);
		bool flag = false;
		for (int i = Math.Min(x, x2); i <= Math.Max(x, x2); i++)
		{
			for (int j = Math.Min(y, y2); j <= Math.Max(y, y2); j++)
			{
				if (i < map.GetLength(1) && i >= 0 && j < map.GetLength(0) && j >= 0)
				{
					flag |= map[j, i];
				}
			}
		}
		return flag;
	}

	private bool TestCollisionBox(CollisionBox collisionBox)
	{
		int x = 0;
		int y = 0;
		GetMapCoords(ref x, ref y, collisionBox.TopLeft);
		int x2 = 0;
		int y2 = 0;
		GetMapCoords(ref x2, ref y2, collisionBox.BottomRight);
		bool flag = false;
		for (int i = x; i <= x2; i++)
		{
			for (int j = y; j <= y2; j++)
			{
				if (i < map.GetLength(1) && i >= 0 && j < map.GetLength(0) && j >= 0)
				{
					flag |= map[j, i];
				}
			}
		}
		return flag;
	}
}
