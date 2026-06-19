using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionSimpleCircle : ICollisionType
{
	private Vector2 _position;

	private float _radius;

	private CollisionBox cachedCollisionBox = new CollisionBox();

	public Vector2 Position
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _position;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			_position = value;
		}
	}

	public float Radius
	{
		get
		{
			return _radius;
		}
		set
		{
			_radius = value;
		}
	}

	public CollisionSimpleCircle(Vector2 position, float radius)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		_position = position;
		_radius = radius;
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
		if (other is CollisionLevelMap)
		{
			return TestCollisionLevelMap((CollisionLevelMap)other);
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

	private bool TestCollisionLevelMap(CollisionLevelMap collisionLevelMap)
	{
		return collisionLevelMap.TestCollision(this);
	}

	private bool TestCollisionBox(CollisionBox collisionBox)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		cachedCollisionBox.TopLeft = _position + new Vector2((0f - _radius) / 1.4f, (0f - _radius) / 1.4f);
		cachedCollisionBox.BottomRight = _position + new Vector2(_radius / 1.4f, _radius / 1.4f);
		Vector2 val = collisionBox.TopLeft - _position;
		if (!(((Vector2)(ref val)).LengthSquared() <= _radius * _radius))
		{
			Vector2 val2 = collisionBox.TopRight - _position;
			if (!(((Vector2)(ref val2)).LengthSquared() <= _radius * _radius))
			{
				Vector2 val3 = collisionBox.BottomLeft - _position;
				if (!(((Vector2)(ref val3)).LengthSquared() <= _radius * _radius))
				{
					Vector2 val4 = collisionBox.BottomRight - _position;
					if (!(((Vector2)(ref val4)).LengthSquared() <= _radius * _radius))
					{
						return cachedCollisionBox.TestCollision(collisionBox);
					}
				}
			}
		}
		return true;
	}

	private bool TestCollisionLine(CollisionLine collisionLine)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = collisionLine.Start - _position;
		bool num = ((Vector2)(ref val)).LengthSquared() <= _radius * _radius;
		Vector2 val2 = collisionLine.End - _position;
		return num | (((Vector2)(ref val2)).LengthSquared() <= _radius * _radius);
	}

	private bool TestCollisionSimpleCircle(CollisionSimpleCircle collisionSimpleCircle)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = collisionSimpleCircle.Position - _position;
		return ((Vector2)(ref val)).LengthSquared() <= (_radius + collisionSimpleCircle.Radius) * (_radius + collisionSimpleCircle.Radius);
	}
}
