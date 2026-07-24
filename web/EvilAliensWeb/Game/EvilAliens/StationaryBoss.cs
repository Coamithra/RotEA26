using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class StationaryBoss : AlienDrawableGameComponent
{
	private Timer fakehittimer = new Timer(35f, repeating: false);

	private Texture2D blank;

	// Authored placement for the Mothership_landed still (Content/data/landed_offsets.json).
	// The mothership never lifts off, so only Landed (draw nudge) + shadow tuning apply.
	private EvilAliensWeb.Compat.LandedOffsets.Entry landedTuning = EvilAliensWeb.Compat.LandedOffsets.Entry.Identity;

	public override ICollisionType CollisionType
	{
		get
		{
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
		base.Position = new Vector2(1100f, 440f);
		base.Initialize();
		fakehittimer.Stop();
		landedTuning = EvilAliensWeb.Compat.LandedOffsets.Get("GFX/Sprites/Mothership_landed");
		base.ShadowOffset = landedTuning.Shadow;
		base.ShadowSize = landedTuning.ShadowSize;
	}

	public override void Draw(GameTime gameTime)
	{
		if (fakehittimer.Active)
		{
			spriteBatch.lightenEffect.Enable();
		}
		// Nudge only the draw by the authored feet offset; Position (collisions + shadow source)
		// stays put, matching how the landed UFOs offset their still.
		Vector2 drawPos = base.Position;
		base.Position = drawPos + landedTuning.Landed;
		base.Draw(gameTime);
		base.Position = drawPos;
		if (fakehittimer.Active)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (base.Position.X < -500f)
		{
			Die();
		}
		base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
	}

	public override void CollidesWith(ICollidable other)
	{
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
