using Microsoft.Xna.Framework;

namespace EvilAliens;

public class Floorbottom : DrawableGameComponent, ICollidable
{
	private float bottom;

	private CollisionBox b = new CollisionBox();

	public float Bottom => bottom;

	public Floorbottom(Game game, float bottom)
		: base(game)
	{
		this.bottom = bottom;
	}

	public bool DetectCollision(ICollidable other)
	{
		if (!(other is AlienDrawableGameComponent) || ((AlienDrawableGameComponent)other).Collides)
		{
			return GetCollisionType().TestCollision(other.GetCollisionType());
		}
		return false;
	}

	public ICollisionType GetCollisionType()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		b.TopLeft = new Vector2(-500f, bottom);
		b.BottomRight = new Vector2(1300f, 1100f);
		return b;
	}

	public void CollidesWith(ICollidable other)
	{
	}
}
