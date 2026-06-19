using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class EvilBullet : AlienDrawableGameComponent, IComponentWatcher
{
	private PlayerShip hasHit;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public EvilBullet(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/bulletevil"));
		((DrawableGameComponent)this).DrawOrder = 800;
	}

	public static EvilBullet NewEvilBullet(ComponentBin collection, Game game)
	{
		EvilBullet evilBullet = collection.Recycle<EvilBullet>();
		if (evilBullet == null)
		{
			evilBullet = new EvilBullet(game);
		}
		return evilBullet;
	}

	public void Setup(Vector2 position, float direction)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
		base.Direction = direction;
	}

	public override void Initialize()
	{
		hasHit = null;
		base.Speed = 0.24f;
		base.MaxSpeed = 0.24f;
		base.Initialize();
	}

	public bool OffScreen()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		return (base.Position.X < -50f) | (base.Position.X > 850f) | (base.Position.Y < -50f) | (base.Position.Y > 650f);
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		if (OffScreen())
		{
			Die();
		}
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		if (other is Option || (other is Blast && !((Blast)other).IsMini) || other is Ball)
		{
			Die();
		}
		if (other is PlayerShip)
		{
			hasHit = (PlayerShip)other;
		}
		base.CollidesWith(other);
		if (other is Floor && base.Position.Y > ((Floor)other).Bottom)
		{
			Die();
		}
		if (other is Wall)
		{
			Die();
		}
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == hasHit)
		{
			Die();
		}
	}
}
