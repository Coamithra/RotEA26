using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class ShipConnector : AlienDrawableGameComponent
{
	public PlayerShip A;

	public PlayerShip B;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0034: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft = collisionBox.TopLeft * 0.8f + base.Position;
			collisionBox.BottomRight = collisionBox.BottomRight * 0.8f + base.Position;
			return collisionBox;
		}
	}

	public ShipConnector(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/connector"));
		base.DrawOrder = 11;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == A)
		{
			A = null;
		}
		if (e.GameComponent == B)
		{
			B = null;
		}
	}

	public static ShipConnector NewAlien(ComponentBin collection, Game game)
	{
		ShipConnector shipConnector = collection.Recycle<ShipConnector>();
		if (shipConnector == null)
		{
			shipConnector = new ShipConnector(game);
		}
		return shipConnector;
	}

	public void Setup(PlayerShip A, PlayerShip B)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		this.A = A;
		this.B = B;
		Vector2 position = A.Position;
		Vector2 position2 = B.Position;
		float num = MyMath.VectorToAngle(position2 - position);
		rotation = num;
		base.Position = position + (position2 - position) * 0.5f;
	}

	public override void Initialize()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		color = new Color(new Vector4(1f, 1f, 1f, 0.65f));
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		if (A == null || B == null)
		{
			Die();
			if (A != null)
			{
				A.TemporaryInvulnerability();
			}
			if (B != null)
			{
				B.TemporaryInvulnerability();
			}
			A = null;
			B = null;
		}
		else
		{
			Vector2 position = A.Position;
			Vector2 position2 = B.Position;
			float angle = (rotation = MyMath.VectorToAngle(position2 - position));
			base.Position = position + (position2 - position) * 0.5f;
			A.SetPosition(base.Position - MyMath.AngleToVector(angle) * 39f);
			B.SetPosition(base.Position + MyMath.AngleToVector(angle) * 39f);
			base.Update(gameTime);
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	public void TakeHit()
	{
		Die();
		A.TemporaryInvulnerability();
		B.TemporaryInvulnerability();
		A = null;
		B = null;
	}
}
