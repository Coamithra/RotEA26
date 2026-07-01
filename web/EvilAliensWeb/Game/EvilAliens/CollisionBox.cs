using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionBox : ICollisionType
{
	private Vector2 _topleft;

	private Vector2 _bottomright;

	public float Width
	{
		get
		{
			return _bottomright.X - _topleft.X;
		}
		set
		{
			_bottomright.X = _topleft.X + value;
		}
	}

	public float Height
	{
		get
		{
			return _bottomright.Y - _topleft.Y;
		}
		set
		{
			_bottomright.Y = _topleft.Y + value;
		}
	}

	public float Left
	{
		get
		{
			return _topleft.X;
		}
		set
		{
			_topleft.X = value;
		}
	}

	public float Top
	{
		get
		{
			return _topleft.Y;
		}
		set
		{
			_topleft.Y = value;
		}
	}

	public float Right
	{
		get
		{
			return _bottomright.X;
		}
		set
		{
			_bottomright.X = value;
		}
	}

	public float Bottom
	{
		get
		{
			return _bottomright.Y;
		}
		set
		{
			_bottomright.Y = value;
		}
	}

	public Vector2 TopLeft
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _topleft;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			_topleft = value;
		}
	}

	public Vector2 BottomRight
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _bottomright;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			_bottomright = value;
		}
	}

	public Vector2 TopRight
	{
		get
		{
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			return new Vector2(_bottomright.X, _topleft.Y);
		}
		set
		{
			_bottomright.X = value.X;
			_topleft.Y = value.Y;
		}
	}

	public Vector2 BottomLeft
	{
		get
		{
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			return new Vector2(_topleft.X, _bottomright.Y);
		}
		set
		{
			_topleft.X = value.X;
			_bottomright.Y = value.Y;
		}
	}

	public CollisionBox()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		_topleft = Vector2.Zero;
		_bottomright = Vector2.One;
	}

	public CollisionBox(Vector2 topLeft, Vector2 bottomRight)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		_topleft = topLeft;
		_bottomright = bottomRight;
	}

	public CollisionBox(float x, float y, float width, float height, bool center)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		if (center)
		{
			_topleft = new Vector2(x - width / 2f, y - height / 2f);
			_bottomright = new Vector2(x + width / 2f, y + height / 2f);
		}
		else
		{
			_topleft = new Vector2(x, y);
			// Corrected from the 2008 original's `y + width` — the non-centred ctor ignored
			// the height param entirely (copy-paste typo). No live callers today, so this is
			// a latent-defect fix, not a behaviour change; kept so any future caller of this
			// overload gets a correctly-sized box.
			_bottomright = new Vector2(x + width, y + height);
		}
	}

	public void CenterAround(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = _bottomright - _topleft;
		_topleft = position - val / 2f;
		_bottomright = position + val / 2f;
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

	private bool TestCollisionSimpleCircle(CollisionSimpleCircle collisionSimpleCircle)
	{
		return collisionSimpleCircle.TestCollision(this);
	}

	private bool TestCollisionLine(CollisionLine collisionLine)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		BoundingBox val = default(BoundingBox);
		(val) = new BoundingBox(new Vector3(TopLeft, 0f), new Vector3(BottomRight, 0f));
		Ray val2 = default(Ray);
		(val2) = new Ray(new Vector3(collisionLine.Origin, 0f), new Vector3(collisionLine.DirectionalVector, 0f));
		if ((val).Intersects(val2).HasValue && (val).Intersects(val2) < collisionLine.Length)
		{
			return true;
		}
		return false;
	}

	private bool TestCollisionBox(CollisionBox other)
	{
		if (Bottom < other.Top)
		{
			return false;
		}
		if (Top > other.Bottom)
		{
			return false;
		}
		if (Right < other.Left)
		{
			return false;
		}
		if (Left > other.Right)
		{
			return false;
		}
		return true;
	}
}
