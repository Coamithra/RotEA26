using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class StationaryBoss : AlienDrawableGameComponent
{
	private Timer fakehittimer = new Timer(35f, repeating: false);

	private Texture2D blank;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.Width *= 0.90999997f;
			collisionBox.Height *= 0.48999998f;
			collisionBox.CenterAround(base.Position - new Vector2(10f * scale, 0f));
			collisionBox.Bottom += 100f;
			return collisionBox;
		}
	}

	public StationaryBoss(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/Mothership_landed"));
		base.DrawOrder = 20;
		AddTimer(fakehittimer);
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blank = content.Load<Texture2D>("GFX/Game/blank");
	}

	public static StationaryBoss NewAlien(ComponentBin collection, Game game)
	{
		StationaryBoss stationaryBoss = collection.Recycle<StationaryBoss>();
		if (stationaryBoss == null)
		{
			stationaryBoss = new StationaryBoss(game);
		}
		return stationaryBoss;
	}

	public void Setup()
	{
	}

	public override void Initialize()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		base.Position = new Vector2(1100f, 440f);
		base.Initialize();
		fakehittimer.Stop();
	}

	public override void Draw(GameTime gameTime)
	{
		if (fakehittimer.Active)
		{
			spriteBatch.lightenEffect.Enable();
		}
		base.Draw(gameTime);
		if (fakehittimer.Active)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		if (base.Position.X < -500f)
		{
			Die();
		}
		base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		base.CollidesWith(other);
		if ((other is Bullet) & !fakehittimer.Active)
		{
			fakehittimer.Reset();
			fakehittimer.Start();
			if (RandomHelper.RandomNextFloat(0f, 100f) <= 30f)
			{
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				Vector2 v = oracle.BackgroundSpeed + new Vector2(0f, -0.48f);
				explosion.Setup(base.Position + new Vector2(RandomHelper.RandomNextFloat(-200f, 200f), RandomHelper.RandomNextFloat(0f, 150f)), 1f, 1f, (v).Length(), MyMath.VectorToAngle(v));
				sound.PlayCue("expl1");
				collection.Add((GameComponent)(object)explosion);
			}
		}
	}
}
