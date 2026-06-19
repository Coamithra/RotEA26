using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class TestBlock : AlienDrawableGameComponent
{
	private Vector2 topleft;

	private Vector2 bottomright;

	private bool l;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			b.TopLeft = topleft;
			b.BottomRight = bottomright;
			return b;
		}
	}

	public TestBlock(Game game)
		: base(game)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		LoadAnimation(new AnimationData("GFX/Sprites/Block"));
		base.DrawOrder = 10;
	}

	public static TestBlock NewTestBlock(ComponentBin collection, Game game)
	{
		TestBlock testBlock = collection.Recycle<TestBlock>();
		if (testBlock == null)
		{
			testBlock = new TestBlock(game);
		}
		return testBlock;
	}

	public void Setup(Vector2 topleft, Vector2 bottomright)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		this.topleft = topleft;
		this.bottomright = bottomright;
	}

	public override void Initialize()
	{
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		if (l)
		{
			spriteBatch.lightenEffect.Enable();
		}
		spriteBatch.Draw(texture, new Rectangle((int)topleft.X, (int)topleft.Y, (int)bottomright.X - (int)topleft.X, (int)bottomright.Y - (int)topleft.Y), Color.White);
		if (l)
		{
			spriteBatch.lightenEffect.Disable();
			l = false;
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		if (other is Lazer || other is PlayerShip || other is EvilBullet || other is Bullet || other is Blast)
		{
			l = true;
		}
		base.CollidesWith(other);
	}
}
