using System;
using EvilAliens.Constants;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;

namespace EvilAliens;

// The webcam challenge's saucer (Levels.WebcamAliens — the remake of the 2004
// webcam game the "I made this!" splash is from). Flies in from a screen edge,
// wanders the starfield, and if the player doesn't swat it in time it starts
// BLINKING at an ever-faster rate — the classic "I'm about to do something
// evil" telegraph — then fires one big slow plasma shot at the player's body
// (the webcam mask centroid) and flees off-screen.
//
// It deliberately does NOT go through the CollisionHandler (Collides = false):
// the "player" here is the segmented webcam mask, not an ICollidable, so
// WebcamLevel hit-tests Position/HitRadius against the mask grid
// (WebcamInterop.HitCircle) and calls Asplode() on a touch.
internal class WebcamUfo : AlienDrawableGameComponent
{
	private enum UfoState
	{
		flyin,     // entering from an edge, can't be armed yet
		wander,    // drifting around the field
		arming,    // blinking faster and faster
		flee       // shot fired — head for the nearest edge and despawn
	}

	// How hard a body-touch hit is scored + the visible size of the saucer.
	private const float UfoScale = 1.25f;

	private UfoState state;

	private float wanderDir;

	// Per-difficulty saucer-speed multiplier (WebcamLevel picks it from its tuning
	// table). Applied to the base/max drift speed in Initialize; 1 = the baseline.
	private float speedMul = 1f;

	// milliseconds of wandering before the blink phase starts (set by Setup).
	private Timer armTimer = new Timer(8000f, repeating: false);

	// the blink phase: blinkClock runs it, blinkPhase accumulates the variable-
	// frequency square wave (period ramps 400ms -> 70ms across the phase).
	private Timer blinkClock = new Timer(3000f, repeating: false);

	private float blinkPhase;

	private CollisionSimpleCircle circle = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public delegate void FiredHandler(WebcamUfo sender, Vector2 target);

	// Raised at the instant the blink phase completes; the level spawns the
	// plasma shot (it owns projectile bookkeeping).
	public event FiredHandler OnFired;

	public override ICollisionType CollisionType
	{
		get
		{
			circle.Position = base.Position;
			circle.Radius = HitRadius;
			return circle;
		}
	}

	// Design-space radius used for the mask hit test (and the unused ICollisionType).
	// 0 until Initialize has run: ComponentBin.Add defers Initialize (and thus
	// LoadAnimation) to the end of the tick, so the level's hit test can see this
	// saucer one tick before it has a texture — 0 radius simply never hits.
	public float HitRadius => (texture == null || columns == 0) ? 0f : (float)texture.Width / (float)columns * DrawScale * 0.42f;

	public bool IsFleeing => state == UfoState.flee;

	public WebcamUfo(Game game)
		: base(game)
	{
		AddTimer(armTimer);
		AddTimer(blinkClock);
		base.Collides = false;
		base.DrawOrder = 19;
		PointValue = PointValues.WebcamUfo;
	}

	public static WebcamUfo NewWebcamUfo(ComponentBin collection, Game game)
	{
		WebcamUfo webcamUfo = collection.Recycle<WebcamUfo>();
		if (webcamUfo == null)
		{
			webcamUfo = new WebcamUfo(game);
		}
		return webcamUfo;
	}

	// position: just off one screen edge. armDelayMs: wander time before the
	// blink phase; blinkMs: length of the blink phase (both scale difficulty).
	// speedMultiplier: per-difficulty drift-speed scale (applied in Initialize).
	public void Setup(Vector2 position, float armDelayMs, float blinkMs, float speedMultiplier = 1f)
	{
		base.Position = position;
		speedMul = speedMultiplier;
		armTimer.Duration = armDelayMs;
		blinkClock.Duration = blinkMs;
		// point roughly at the field centre so the fly-in always enters the screen
		wanderDir = MyMath.VectorToAngle(new Vector2(RandomHelper.RandomNextFloat(250f, 550f), RandomHelper.RandomNextFloat(200f, 400f)) - position);
	}

	// Live-retune hook (the ?wctune stepper panel): rescale this saucer's current +
	// max drift speed in place so a mid-run speed change is felt immediately, not
	// only by the next spawn. Ratio-based so the random per-instance base survives.
	public void SetSpeedMultiplier(float multiplier)
	{
		if (multiplier <= 0f || speedMul <= 0f || multiplier == speedMul)
		{
			return;
		}
		float ratio = multiplier / speedMul;
		speedMul = multiplier;
		base.Speed *= ratio;
		base.MaxSpeed *= ratio;
	}

	public override void Initialize()
	{
		base.Initialize();
		LoadAnimation(new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f));
		curframe = RandomHelper.RandomNextFloat(0f, 31f);
		scale = UfoScale;
		state = UfoState.flyin;
		blinkPhase = 0f;
		armTimer.Stop();
		blinkClock.Stop();
		base.Direction = wanderDir;
		base.Speed = RandomHelper.RandomNextFloat(0.1f, 0.15f) * speedMul;
		base.MaxSpeed = 0.18f * speedMul;
		base.Acceleration = 6E-05f;
		base.Deceleration = 1.8E-05f;
	}

	public override void Update(GameTime gameTime)
	{
		switch (state)
		{
		case UfoState.flyin:
			base.Update(gameTime);
			if (!OffScreen(-60f))
			{
				// fully on screen: start the wander + the countdown to arming
				state = UfoState.wander;
				armTimer.Reset();
				armTimer.Start();
			}
			break;
		case UfoState.wander:
			UpdateWander(gameTime);
			if (armTimer.Finished)
			{
				state = UfoState.arming;
				blinkPhase = 0f;
				blinkClock.Reset();
				blinkClock.Start();
			}
			break;
		case UfoState.arming:
		{
			// keep drifting (slower — it is taking aim) while the blink ramps up
			UpdateWander(gameTime, 0.55f);
			float t = blinkClock.Normalized;   // elapsed 0 -> 1 handled below
			// Timer.Normalized is time REMAINING fraction (starts at 1), invert:
			t = 1f - t;
			float period = MathHelper.Lerp(400f, 70f, t);
			blinkPhase += (float)gameTime.ElapsedGameTime.TotalMilliseconds / period;
			if (blinkClock.Finished)
			{
				Fire();
			}
			break;
		}
		case UfoState.flee:
			Move((float?)wanderDir, gameTime);
			base.Update(gameTime);
			if (OffScreen(80f))
			{
				Die();
			}
			break;
		}
	}

	// Standard bouncing drift, borrowed from UFO's normal state: keep off the
	// edges, occasionally pick a fresh heading.
	private void UpdateWander(GameTime gameTime, float speedFactor = 1f)
	{
		Vector2 v = MyMath.AngleToVector(wanderDir);
		int margin = 80;
		if (base.Position.X > (float)(800 - margin) && v.X > 0f)
		{
			v.X *= -1f;
		}
		if (base.Position.X < (float)margin && v.X < 0f)
		{
			v.X *= -1f;
		}
		if (base.Position.Y > (float)(600 - margin) && v.Y > 0f)
		{
			v.Y *= -1f;
		}
		if (base.Position.Y < (float)margin && v.Y < 0f)
		{
			v.Y *= -1f;
		}
		wanderDir = MyMath.VectorToAngle(v);
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0005 * gameTime.ElapsedGameTime.TotalMilliseconds)
		{
			wanderDir = RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f);
		}
		float keepMax = base.MaxSpeed;
		base.MaxSpeed = keepMax * speedFactor;
		Move((float?)wanderDir, gameTime);
		base.Update(gameTime);
		base.MaxSpeed = keepMax;
	}

	private void Fire()
	{
		state = UfoState.flee;
		// flee towards the nearest horizontal edge, slightly upward — reads as
		// "job done, bailing out"
		wanderDir = MyMath.VectorToAngle(new Vector2((base.Position.X < 400f) ? -1f : 1f, -0.35f));
		base.Speed = base.MaxSpeed;
		if (this.OnFired != null)
		{
			this.OnFired(this, WebcamInterop.Centroid);
		}
	}

	// The player touched it: score + explosion + gone. `swattedBy` is where the
	// touch registered (the explosion centre).
	public void Asplode()
	{
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1.4f, 1f, base.Speed * 0.3f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
		Die();
	}

	public override void Draw(GameTime gameTime)
	{
		bool blinkOn = state == UfoState.arming && blinkPhase % 1f < 0.5f;
		if (blinkOn)
		{
			spriteBatch.lightenEffect.Enable();
		}
		base.Draw(gameTime);
		if (blinkOn)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			this.OnFired = null;
		}
	}
}
