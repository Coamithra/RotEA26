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
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _origin;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
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
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return MyMath.AngleToVector(_direction);
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			_direction = MyMath.VectorToAngle(value);
		}
	}

	public Vector2 Start
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _origin;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0021: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0036: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
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
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			return _origin + _length * MyMath.AngleToVector(_direction);
		}
		set
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0004: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			_origin = value;
			Vector2 val = value - _origin;
			_length = (val).Length();
			_direction = MyMath.VectorToAngle(value);
		}
	}

	public CollisionLine(Vector2 origin, float length, float direction)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		_origin = origin;
		_length = length;
		_direction = direction;
	}

	public CollisionLine(Vector2 start, Vector2 end)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		_origin = start;
		Vector2 val = end - start;
		_length = (val).Length();
		_direction = MyMath.VectorToAngle(end - start);
	}

	public void Set(Vector2 origin, float length, float direction)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		_origin = origin;
		_length = length;
		_direction = direction;
	}

	public void Set(Vector2 start, Vector2 end)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
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
