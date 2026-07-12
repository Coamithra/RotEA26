using System;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;

namespace EvilAliens;

// The webcam challenge's DeathStar-mine hazard (F2). A cousin of WebcamUfo: it flies in
// from an edge and wanders the starfield exactly like a saucer (same containment +
// player-avoidance steering, so it flows AROUND a still player instead of homing in like
// the Level-3 DeathStar does), but it is NOT something you kill — TOUCHING it costs the
// player a life and bursts the familiar blue explosion (Explosion.MakeBlue), so the player
// must DODGE it. Unlike the persistent saucers, a mine only sticks around for a lifetime
// (MineLifeMs) then flies off the nearest edge and despawns.
//
// Like WebcamUfo it deliberately does NOT go through the CollisionHandler (Collides=false):
// the "player" is the segmented webcam mask, so WebcamLevel hit-tests Position/HitRadius
// against the mask grid (WebcamInterop.HitCircle) and detonates on a touch.
internal class WebcamMine : AlienDrawableGameComponent
{
	private enum MineState
	{
		flyin,   // entering from an edge
		wander,  // drifting around; the lifetime clock runs here
		leave    // lifetime up — head off the nearest edge and despawn
	}

	// Visible size of the mine (deathstarsheet2 is DesignFrameWidth 48, so ~62 px on screen
	// at 1.3 — a touch bigger than a saucer's 60, easy to read as "avoid me").
	private const float MineScale = 1.3f;

	// Fly-around AI (mirrors WebcamUfo): steer away from + orbit the player's mask so a
	// still player isn't drifted into, but the mine never HOMES. ?wcavoid= tunes it.
	private const float DefaultAvoidStrength = 1.2f;

	private const float AvoidRadius = 150f;

	private const float OrbitStrength = 0.75f;

	private MineState state;

	private float wanderDir;

	// Steady-contact accumulator for the bad-collision leeway (WebcamLevel manages it).
	public float ContactMs;

	private int orbitSign = 1;

	// milliseconds of wandering before the mine leaves (set by Setup from the tuning table).
	private Timer lifeTimer = new Timer(6000f, repeating: false);

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

	// Design-space radius used for the mask hit test (0 until Initialize/LoadAnimation runs —
	// ComponentBin.Add defers Initialize to end-of-tick, so the level may hit-test a mine one
	// tick before it has a texture; 0 radius simply never hits).
	public float HitRadius => (texture == null || columns == 0) ? 0f : (float)texture.LogicalWidth() / (float)columns * DrawScale * 0.42f;

	public WebcamMine(Game game)
		: base(game)
	{
		AddTimer(lifeTimer);
		base.Collides = false;
		base.DrawOrder = 20;
		PointValue = 0f;
	}

	public static WebcamMine NewWebcamMine(ComponentBin collection, Game game)
	{
		WebcamMine mine = collection.Recycle<WebcamMine>();
		if (mine == null)
		{
			mine = new WebcamMine(game);
		}
		return mine;
	}

	// position: just off one screen edge. lifeMs: wander time before it leaves.
	public void Setup(Vector2 position, float lifeMs)
	{
		base.Position = position;
		lifeTimer.Duration = lifeMs;
		ContactMs = 0f;
		// point roughly at the field centre so the fly-in always enters the screen
		wanderDir = MyMath.VectorToAngle(new Vector2(RandomHelper.RandomNextFloat(250f, 550f), RandomHelper.RandomNextFloat(200f, 400f)) - position);
	}

	public override void Initialize()
	{
		base.Initialize();
		LoadAnimation(new AnimationData("GFX/Sprites/deathstarsheet2", 4, 8, 1, 25f));
		curframe = RandomHelper.RandomNextFloat(0f, 31f);
		scale = MineScale;
		state = MineState.flyin;
		lifeTimer.Stop();
		base.Direction = wanderDir;
		base.Speed = RandomHelper.RandomNextFloat(0.1f, 0.15f);
		base.MaxSpeed = 0.18f;
		base.Acceleration = 6E-05f;
		orbitSign = (RandomHelper.Random.Next(2) == 0) ? -1 : 1;
	}

	public override void Update(GameTime gameTime)
	{
		switch (state)
		{
		case MineState.flyin:
			base.Update(gameTime);
			if (!OffScreen(-60f))
			{
				// fully on screen: start wandering + the lifetime countdown
				state = MineState.wander;
				lifeTimer.Reset();
				lifeTimer.Start();
			}
			break;
		case MineState.wander:
			UpdateWander(gameTime);
			if (lifeTimer.Finished)
			{
				// lifetime up: head for the nearest edge and leave (accelerate away)
				state = MineState.leave;
				wanderDir = MyMath.VectorToAngle(NearestEdgeDirection());
				base.Direction = wanderDir;
				base.MaxSpeed = 0.28f;
				base.Acceleration = 3E-04f;
			}
			break;
		case MineState.leave:
			Move((float?)wanderDir, gameTime);
			base.Update(gameTime);
			if (OffScreen(60f))
			{
				Die();
			}
			break;
		}
	}

	// Vector from the mine toward whichever screen edge it is closest to (so it exits the
	// short way, off-screen, when its lifetime is up).
	private Vector2 NearestEdgeDirection()
	{
		float toLeft = base.Position.X;
		float toRight = 800f - base.Position.X;
		float toTop = base.Position.Y;
		float toBottom = 600f - base.Position.Y;
		float min = MathHelper.Min(MathHelper.Min(toLeft, toRight), MathHelper.Min(toTop, toBottom));
		if (min == toLeft)
		{
			return new Vector2(-1f, 0f);
		}
		if (min == toRight)
		{
			return new Vector2(1f, 0f);
		}
		if (min == toTop)
		{
			return new Vector2(0f, -1f);
		}
		return new Vector2(0f, 1f);
	}

	// Bouncing drift with authoritative edge containment + player-avoidance, identical in
	// spirit to WebcamUfo.UpdateWander (avoidance applied FIRST, edge reflection LAST, plus an
	// off-screen watchdog) so a mine flows around the player but can never stall off-screen.
	private void UpdateWander(GameTime gameTime)
	{
		Vector2 v = MyMath.AngleToVector(wanderDir);
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0005 * gameTime.ElapsedGameTime.TotalMilliseconds)
		{
			v = MyMath.AngleToVector(RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f));
		}
		float avoidStrength = DebugFlags.WebcamAvoid ?? DefaultAvoidStrength;
		if (avoidStrength > 0f)
		{
			Vector2 away = WebcamInterop.AvoidanceVector(base.Position, AvoidRadius);
			if (away.LengthSquared() > 1E-04f)
			{
				away.Normalize();
				Vector2 tangent = new Vector2(0f - away.Y, away.X) * orbitSign;
				v += away * avoidStrength + tangent * (OrbitStrength * avoidStrength);
			}
		}
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
		if (OffScreen(0f))
		{
			v = new Vector2(400f, 300f) - base.Position;
		}
		if (v.LengthSquared() > 1E-04f)
		{
			wanderDir = MyMath.VectorToAngle(v);
		}
		Move((float?)wanderDir, gameTime);
		base.Update(gameTime);
	}

	// The player touched it: the beefy blue DeathStar burst (mirrors StarMine.Asplode — two
	// stacked blue explosions, a big 3.5 + a medium 2, both with a little debris impulse), then
	// gone. WebcamLevel also docks a life (PlayerHit) on the same touch. The "tweety" cue
	// (`targetacquired`, the DeathStar's hone-in-on-target sound in the normal game) sounds on
	// the pop as a cheeky callback.
	public void Detonate()
	{
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 3.5f, 2.5f, 0.03f, base.Direction);
		explosion.MakeBlue();
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, 0.06f, base.Direction);
		explosion.MakeBlue();
		collection.Add((GameComponent)(object)explosion);
		// The "tweety" hone-in beep as a cheeky callback, PLUS a big explosion boom (expl2 —
		// the same large-burst cue StarMine.Asplode uses) so the player-damaging blue pop
		// actually sounds as beefy as it looks.
		sound.PlayCue("targetacquired");
		sound.PlayCue("expl2");
		Die();
	}

	// The mothership's bisecting laser swept over it: a plain (non-blue) explosion and gone. No
	// life is docked — the laser clearing mines is a MERCY for the player, not a hit.
	public void DestroyByLaser()
	{
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1.4f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
		Die();
	}
}
