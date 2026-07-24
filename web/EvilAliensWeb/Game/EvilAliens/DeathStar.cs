using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class DeathStar : KillableAlien
{
	private EnemyBehaviour behaviour;

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public DeathStar(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/deathstarsheet2", 4, 8, 1, 25f));
		base.DrawOrder = 20;
		base.MaxSpeed = 0.25199997f;
		base.Acceleration = 0.000120000004f;
		PointValue = 10f;
		SetHitPoints(1, scaleWithDifficulty: false);
	}

	public static DeathStar NewDeathStar(ComponentBin collection, Game game)
	{
		DeathStar deathStar = collection.Recycle<DeathStar>();
		if (deathStar == null)
		{
			deathStar = new DeathStar(game);
		}
		return deathStar;
	}

	public void Setup(Vector2 position, EnemyBehaviour behaviour)
	{
		this.behaviour = behaviour;
		base.Position = position;
	}

	public override void Initialize()
	{
		base.Speed = 0f;
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		float value = MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position);
		Move((float?)value, gameTime);
		if (behaviour == EnemyBehaviour.classic)
		{
			Vector2 directionalVector = base.DirectionalVector;
			if (base.Position.X > 800f && directionalVector.X > 0f)
			{
				directionalVector.X *= -1f;
			}
			if (base.Position.X < 0f && directionalVector.X < 0f)
			{
				directionalVector.X *= -1f;
			}
			if (base.Position.Y > 600f && directionalVector.Y > 0f)
			{
				directionalVector.Y *= -1f;
			}
			if (base.Position.Y < 0f && directionalVector.Y < 0f)
			{
				directionalVector.Y *= -1f;
			}
			base.DirectionalVector = directionalVector;
		}
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		Die();
		AwardScore(isComboGenerator, other);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DeathStarDescriptor) -----
	// Draw is just base.Draw (no charge/attack visuals), so the frozen puppet needs nothing
	// beyond the base fields + curframe animation. behaviour only steers Update's wall-bounce
	// (which never runs on a puppet) -- pinned purely for construction fidelity.

	internal EnemyBehaviour NetBehaviour => behaviour;
}
