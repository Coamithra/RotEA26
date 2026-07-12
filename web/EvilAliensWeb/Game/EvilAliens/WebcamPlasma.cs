using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

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

	// ~78 design px across (~1.3x a saucer's 60 px frame). Dialed by eye from the
	// original 0.34 (~237 px, "way too large"): first halved, then trimmed to 66%.
	private const float FullScale = 0.112f;

	private const float FlySpeed = 0.17f;    // design px/ms — brisk, still dodgeable by leaning

	// Per-difficulty cruise-speed scale (WebcamLevel picks it from its tuning
	// table). Resolved speed = FlySpeed * speedMul; 1 = the baseline.
	private float flySpeed = FlySpeed;

	// Drawn additively at two independent spinning rotations (PlasmaBall's
	// flickering-lightning trick).
	private float[] rotations = new float[2];

	private PlasmaState state;

	// Steady-contact accumulator for the bad-collision leeway (WebcamLevel manages it): ms the
	// player mask has been continuously overlapping this orb; reset the instant contact breaks.
	public float ContactMs;

	// entry-grow duration: how fast the orb swells to full size at the muzzle
	// (was 650ms; halved so the shot reads as fired, not inflated)
	private Timer stateTimer = new Timer(325f, repeating: false);

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
	public float HitRadius => (float)texture.LogicalWidth() * 0.32f * DrawScale;

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

	public void Setup(Vector2 position, Vector2 target, float speedMultiplier = 1f)
	{
		base.Position = position;
		base.Direction = MyMath.VectorToAngle(target - position);
		flySpeed = FlySpeed * speedMultiplier;
		base.Speed = 0f;
		base.MaxSpeed = flySpeed;
		ContactMs = 0f;
	}

	// Live-retune hook (the ?wctune stepper panel): retarget the cruise speed of an
	// orb already in flight. Speed > 0 means the entry-grow phase already handed the
	// orb its cruise speed, so snap it to the new value; during entry Speed is 0 and
	// the fly transition below picks up the new flySpeed by itself.
	public void SetSpeedMultiplier(float multiplier)
	{
		if (multiplier <= 0f)
		{
			return;
		}
		flySpeed = FlySpeed * multiplier;
		base.MaxSpeed = flySpeed;
		if (base.Speed > 0f)
		{
			base.Speed = flySpeed;
		}
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
				base.Speed = flySpeed;
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

	// It reached the player (or the level is clearing shots): pop into an electric ZAP burst and
	// vanish. Electricity doesn't explode — instead of a fireball it discharges into a bloom +
	// radiating lightning streaks (WebcamZap). The life (if any) is docked by WebcamLevel.PlayerHit.
	public void Detonate(bool withZap)
	{
		if (withZap)
		{
			WebcamZap zap = WebcamZap.NewWebcamZap(collection, base.Game);
			zap.Setup(base.Position, MathHelper.Clamp(HitRadius / 40f, 0.8f, 2f));
			collection.Add((GameComponent)(object)zap);
		}
		Die();
	}
}
