using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The webcam challenge's danger shot: the big, slow plasma orb a WebcamUfo
// fires at the player's body after its blink telegraph. A close cousin of
// PlasmaBall (same plasmaball2 art, same additive two-layer lightning-flicker
// draw) but tuned for a game where the "ship" is a human on a webcam: much
// larger, much slower (a body can lean out of its way), a constant heading
// captured at fire time, and NO CollisionHandler participation — WebcamLevel
// tests Position/HitRadius against the person mask and calls Detonate().
internal class WebcamPlasma : AlienDrawableGameComponent
{
	private enum PlasmaState
	{
		entry,   // growing at the firing saucer's position
		fly
	}

	private const float FullScale = 0.34f;   // ~80 design px radius — LARGE

	private const float FlySpeed = 0.085f;   // design px/ms — slow enough to dodge

	// Drawn additively at two independent spinning rotations (PlasmaBall's
	// flickering-lightning trick).
	private float[] rotations = new float[2];

	private PlasmaState state;

	private Timer stateTimer = new Timer(650f, repeating: false);

	private CollisionSimpleCircle circle = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public override ICollisionType CollisionType
	{
		get
		{
			circle.Position = base.Position;
			circle.Radius = HitRadius;
			return circle;
		}
	}

	// Tracks the visible disc (same 0.32-of-width factor as PlasmaBall).
	public float HitRadius => (float)texture.Width * 0.32f * DrawScale;

	public WebcamPlasma(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/plasmaball2"));
		base.DrawOrder = 800;
		color = Color.LightGreen;
		blendMode = (SpriteBlendMode)2;
		base.Collides = false;
		timers.Add(stateTimer);
	}

	public static WebcamPlasma NewWebcamPlasma(ComponentBin collection, Game game)
	{
		WebcamPlasma webcamPlasma = collection.Recycle<WebcamPlasma>();
		if (webcamPlasma == null)
		{
			webcamPlasma = new WebcamPlasma(game);
		}
		return webcamPlasma;
	}

	public void Setup(Vector2 position, Vector2 target)
	{
		base.Position = position;
		base.Direction = MyMath.VectorToAngle(target - position);
		base.Speed = 0f;
		base.MaxSpeed = FlySpeed;
	}

	public override void Initialize()
	{
		base.Initialize();
		state = PlasmaState.entry;
		stateTimer.Start();
		scale = 0.02f;
		for (int i = 0; i < rotations.Length; i++)
		{
			rotations[i] = RandomHelper.RandomNextAngle();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		for (int i = 0; i < rotations.Length; i++)
		{
			rotation = rotations[i];
			base.Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (RandomHelper.RandomFromAverage(10f, gameTime))
		{
			int num = RandomHelper.Random.Next(rotations.Length);
			rotations[num] = RandomHelper.RandomNextAngle();
		}
		for (int i = 0; i < rotations.Length; i++)
		{
			float sign = (i % 2 == 0) ? -1f : 1f;
			rotations[i] += (float)Math.PI / 2f * sign * (float)gameTime.ElapsedGameTime.TotalSeconds;
		}
		switch (state)
		{
		case PlasmaState.entry:
			scale = MathHelper.SmoothStep(FullScale, 0.02f, stateTimer.Normalized);
			if (stateTimer.Finished)
			{
				state = PlasmaState.fly;
				scale = FullScale;
				base.Speed = FlySpeed;
			}
			break;
		case PlasmaState.fly:
			break;
		}
		base.Update(gameTime);
		if (OffScreen(160f))
		{
			Die();
		}
	}

	// It reached the player (or the level is clearing shots): burst and vanish.
	public void Detonate(bool withExplosion)
	{
		if (withExplosion)
		{
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 2.2f, 1.4f, 0f, 0f);
			collection.Add((GameComponent)(object)explosion);
		}
		Die();
	}
}
