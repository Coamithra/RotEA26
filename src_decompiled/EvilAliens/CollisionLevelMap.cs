using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionLevelMap : ICollisionType
{
	private bool[,] map;

	private Vector2 offset;

	public int Width => map.GetLength(1);

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
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		position -= offset;
		float num = 800f / (float)map.GetLength(1);
		x = (int)Math.Floor(position.X / num);
		y = (int)Math.Floor(position.Y / num);
	}

	public CollisionLevelMap(Vector2 offset, bool[,] map)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		this.map = map;
		this.offset = offset;
	}

	public void SetOffset(Vector2 offset)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = default(Vector2);
		((Vector2)(ref val))._002Ector(collisionSimpleCircle.Radius * 0.8f, collisionSimpleCircle.Radius * 0.8f);
		CollisionBox collisionBox = new CollisionBox(collisionSimpleCircle.Position - val, collisionSimpleCircle.Position + val);
		return TestCollisionBox(collisionBox);
	}

	private bool TestCollisionLine(CollisionLine collisionLine)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
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
