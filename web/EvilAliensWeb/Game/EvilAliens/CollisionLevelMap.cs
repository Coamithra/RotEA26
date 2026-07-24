using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionLevelMap : ICollisionType
{
	private bool[,] map;

	private Vector2 offset;

	public int Width => map.GetLength(1);

	// Design-space width of one tile. GetMapCoords divides by this, so anything reasoning about
	// the grid in world units (the AI's wall navigation) needs the same number rather than a
	// second copy of the 800/width formula.
	public float TileSize => 800f / (float)map.GetLength(1);

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
		float num = 800f / (float)map.GetLength(1);
		x = (int)Math.Floor(position.X / num);
		y = (int)Math.Floor(position.Y / num);
	}

	public CollisionLevelMap(Vector2 offset, bool[,] map)
	{
		this.map = map;
		this.offset = offset;
	}

	public void SetOffset(Vector2 offset)
	{
		this.offset = offset;
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
		Vector2 val = default(Vector2);
		(val) = new Vector2(collisionSimpleCircle.Radius * 0.8f, collisionSimpleCircle.Radius * 0.8f);
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
