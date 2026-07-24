using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionLine : ICollisionType
{
	private Vector2 _origin;

	private float _length;

	private float _direction;

	public Vector2 Origin
	{
		get
		{
			return _origin;
		}
		set
		{
			_origin = value;
		}
	}

	public float Length
	{
		get
		{
			return _length;
		}
		set
		{
			_length = value;
		}
	}

	public float Direction
	{
		get
		{
			return _direction;
		}
		set
		{
			_direction = value;
		}
	}

	public Vector2 DirectionalVector
	{
		get
		{
			return MyMath.AngleToVector(_direction);
		}
		set
		{
			_direction = MyMath.VectorToAngle(value);
		}
	}

	public Vector2 Start
	{
		get
		{
			return _origin;
		}
		set
		{
			Vector2 val = _origin + _length * MyMath.AngleToVector(_direction);
			_origin = value;
			Vector2 val2 = val - _origin;
			_length = (val2).Length();
			_direction = MyMath.VectorToAngle(val);
		}
	}

	public Vector2 End
	{
		get
		{
			return _origin + _length * MyMath.AngleToVector(_direction);
		}
		set
		{
			_origin = value;
			Vector2 val = value - _origin;
			_length = (val).Length();
			_direction = MyMath.VectorToAngle(value);
		}
	}

	public CollisionLine(Vector2 origin, float length, float direction)
	{
		_origin = origin;
		_length = length;
		_direction = direction;
	}

	public CollisionLine(Vector2 start, Vector2 end)
	{
		_origin = start;
		Vector2 val = end - start;
		_length = (val).Length();
		_direction = MyMath.VectorToAngle(end - start);
	}

	public void Set(Vector2 origin, float length, float direction)
	{
		_origin = origin;
		_length = length;
		_direction = direction;
	}

	public void Set(Vector2 start, Vector2 end)
	{
		_origin = start;
		Vector2 val = end - start;
		_length = (val).Length();
		_direction = MyMath.VectorToAngle(end - start);
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
		return collisionSimpleCircle.TestCollision(this);
	}

	private bool TestCollisionLine(CollisionLine collisionLine)
	{
		return false;
	}

	private bool TestCollisionBox(CollisionBox collisionBox)
	{
		return collisionBox.TestCollision(this);
	}
}
