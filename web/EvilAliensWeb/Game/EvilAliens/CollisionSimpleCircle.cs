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
			return _position;
		}
		set
		{
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
		cachedCollisionBox.TopLeft = _position + new Vector2((0f - _radius) / 1.4f, (0f - _radius) / 1.4f);
		cachedCollisionBox.BottomRight = _position + new Vector2(_radius / 1.4f, _radius / 1.4f);
		Vector2 val = collisionBox.TopLeft - _position;
		if (!((val).LengthSquared() <= _radius * _radius))
		{
			Vector2 val2 = collisionBox.TopRight - _position;
			if (!((val2).LengthSquared() <= _radius * _radius))
			{
				Vector2 val3 = collisionBox.BottomLeft - _position;
				if (!((val3).LengthSquared() <= _radius * _radius))
				{
					Vector2 val4 = collisionBox.BottomRight - _position;
					if (!((val4).LengthSquared() <= _radius * _radius))
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
		Vector2 val = collisionLine.Start - _position;
		bool num = (val).LengthSquared() <= _radius * _radius;
		Vector2 val2 = collisionLine.End - _position;
		return num | ((val2).LengthSquared() <= _radius * _radius);
	}

	private bool TestCollisionSimpleCircle(CollisionSimpleCircle collisionSimpleCircle)
	{
		Vector2 val = collisionSimpleCircle.Position - _position;
		return (val).LengthSquared() <= (_radius + collisionSimpleCircle.Radius) * (_radius + collisionSimpleCircle.Radius);
	}
}
