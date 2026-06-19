using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class DeathStar : KillableAlien
{
	private EnemyBehaviour behaviour;

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
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		Die();
		AwardScore(isComboGenerator, other);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}
}
