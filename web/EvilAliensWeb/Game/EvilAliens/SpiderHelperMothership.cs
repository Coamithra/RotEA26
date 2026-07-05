using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// A "helper" mothership the SpiderBoss fight summons when the boss has gone un-damaged for a
// while (see SpiderBoss's idle timer). The SpiderBoss can ONLY be hurt by a Lazer, and in normal
// play the only lazers around come from the big UFOs aiming at the player -- a very obscure way to
// realise you have to lure a lazer across the boss. This ship makes that legible: it EASES in from
// the left showing just its underside at the top, halts dead-centre, WINDS UP (a converging spark
// swarm, exactly like a medium UFO charging its laser), then fires a Lazer down long enough that
// one of the boss's left/right fly-bys crosses it (that Lazer hits the boss through the normal
// Lazer->SpiderBoss damage path). Then it EASES out east (accelerating from rest) and exits right.
//
// Movement + speed mirror the twin "2 motherships" (MarsBoss): a MyMath.PowerCurve ease, at a
// DIFFICULTY-SCALED fraction of their traverse speed (Easy ~1/5, Medium ~1/3, Very_Hard ~2/3,
// Inzane ~4/5). On Easy/Medium, if the boss is STANDING (a stationary target) the beam is aimed
// AT it; while it's flying around the beam just goes straight down (and a fly-by crosses it).
// The laser's own descent speed is difficulty-scaled inside Lazer.Update (growthspeed * modifier).
//
// It is deliberately "fake killable": it flashes and reddens like it is taking damage (so the
// player feels they are hurting it) but its hitpoint pool is astronomically large, so it can never
// actually die before finishing its job -- it just flies off. The feel knobs (idle threshold,
// hover height, speed override, windup, fire duration) are tunable from the URL via DebugFlags; see
// the "?spiderhelper*" flags. Sprite + A/B-sheet animation mirror Boss/MarsBoss (the other motherships).
internal class SpiderHelperMothership : KillableAlien
{
	private enum HelperState
	{
		enter,
		charge,
		fire,
		leave
	}

	// Fake-damage feedback: real hitpoints are astronomical (never dies), so drive the red tint off
	// a separate hit counter that ramps to fully-red over this many landed shots.
	private const int FakeHitsToFullRed = 40;

	// Movement geometry (design space). The ship enters off the LEFT edge, rests dead centre while it
	// winds up + fires, then exits off the RIGHT edge (flies all the way across).
	private const float EnterStartX = -260f;

	private const float CenterX = 400f;

	private const float ExitX = 1100f;

	// Ease shapes, both via MyMath.PowerCurve (= Lerp(a,b,t^p)). Enter is a quad ease-OUT TO REST:
	// Lerp(from,to,1-(1-t)^p) == PowerCurve(to,from,p,1-t) -- endpoints swapped, t mirrored -- so the
	// ship flies in already moving and DECELERATES to a true stop (zero arrival velocity) at centre.
	// (MarsBoss's raw power-0.5 entry arrives still moving at ~half average speed and hard-stops --
	// too abrupt for a park-at-centre.) enterPower must be >=1: default 2 = a gentle glide-to-rest;
	// higher = punchier start, still stops smoothly (?spiderhelperenterpower). Leave is a plain quad
	// ease-IN: PowerCurve(centre,exit,LeavePower,t) starts at rest and accelerates away east.
	private const float LeavePower = 2f;

	// Reference: the twin MarsBoss ("2 motherships") traverse -500 -> ~400 (mid of its left/right
	// targets) over its 1200ms entry timer, i.e. ~0.75 design-px/ms average. The helper moves at a
	// difficulty-scaled FRACTION of this.
	private const float TwoUfoRefSpeed = 0.75f;

	// Fraction of the twin-MarsBoss traverse speed per unit Settings.DifficultyModifier (0.35 Easy ..
	// 1.2 Inzane): Easy ~0.23, Medium ~0.40, Hard ~0.53, Very_Hard 0.66, Inzane ~0.79.
	private const float SpeedFracPerModifier = 0.66f;

	private HelperState state;

	private float hoverY;

	// Raw average-speed override (design-px/ms) from ?spiderhelperspeed; null = difficulty-scaled.
	private float? speedOverride;

	private float fireLead;

	private float windupMs;

	// Current move (enter/leave) as normalized 0..1 progress over a distance-derived duration.
	private float moveProgress;

	private float moveDurationMs;

	// Enter ease-out exponent (the ?spiderhelperenterpower value), captured in Setup.
	private float enterPower;

	private Timer fireTimer = new Timer(4500f, repeating: false);

	private Timer chargeTimer = new Timer(2500f, repeating: false);

	private Texture2D firstHalfOfSpritesheet;

	private Texture2D secondHalfOfSpritesheet;

	private Lazer lazer;

	private LazerGenerator windup;

	private SpiderBoss boss;

	private int fakeHits;

	public override ICollisionType CollisionType
	{
		get
		{
			// Belly-only hitbox: the ship is drawn mostly off the top edge, so only the visible
			// underside can be shot. Mirror the other motherships' box (slightly slimmed).
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.Width *= 0.9f;
			collisionBox.Height *= 0.49f;
			collisionBox.CenterAround(base.Position);
			return collisionBox;
		}
	}

	public SpiderHelperMothership(Game game)
		: base(game)
	{
		scale = 1f;
		LoadAnimation(new AnimationData("GFX/Sprites/mothershipB", 4, 4, 1, 16f));
		base.DrawOrder = 19;
		AddTimer(fireTimer);
		AddTimer(chargeTimer);
		// Astronomically high so the player can never actually kill it inside the fly-by window;
		// the death path (KilledBy) is therefore never reached -- it leaves on its own.
		SetHitPoints(1000000, scaleWithDifficulty: false);
		// We manage the tint ourselves (fake-damage ramp), so leave the base colorize path off.
		base.Colorize = false;
		base.IsBoss = true;
		PointValue = 0f;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		firstHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipA");
		secondHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipB");
	}

	public static SpiderHelperMothership NewHelper(ComponentBin collection, Game game)
	{
		SpiderHelperMothership helper = collection.Recycle<SpiderHelperMothership>();
		if (helper == null)
		{
			helper = new SpiderHelperMothership(game);
		}
		return helper;
	}

	// hoverY: sprite-centre Y (negative pushes the ship up so only its underside shows).
	// speedOverride: raw avg design-px/ms; null = difficulty-scaled fraction of the twin-MarsBoss speed.
	// fireDurationMs: how long the downward Lazer holds if it hasn't caught the boss.
	// fireLead: gap from the sprite centre down to where the beam starts (its belly).
	// windupMs: charge-swarm duration before the beam (mirrors the medium UFO's ~2.5s laser windup).
	// enterPower: ease-out-to-rest exponent for the entrance (>=1; higher = punchier start, ~2 gentle).
	// boss: the summoning SpiderBoss (for Easy/Medium aim-at-a-standing-boss); may be null.
	public void Setup(float hoverY, float? speedOverride, float fireDurationMs, float fireLead, float windupMs, float enterPower, SpiderBoss boss)
	{
		this.hoverY = hoverY;
		this.speedOverride = speedOverride;
		this.fireLead = fireLead;
		this.windupMs = windupMs;
		this.enterPower = enterPower;
		this.boss = boss;
		fireTimer.Duration = fireDurationMs;
		state = HelperState.enter;
		base.Position = new Vector2(EnterStartX, hoverY);
		base.Collides = true;
		StartMove(CenterX - EnterStartX);
	}

	public override void Initialize()
	{
		base.Initialize();
		interpolationOptions = InterpolationOptions.never;
		fps = 16f;
		lazer = null;
		windup = null;
		fakeHits = 0;
		color = Color.White;
		fireTimer.Stop();
		chargeTimer.Stop();
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			// Only retract a Lazer that is still ours + alive; if it already Died on a boss fly-by it
			// may have been recycled into another emitter, so don't Free it out from under them.
			if (lazer != null && !lazer.IsDead && lazer.owner == this)
			{
				lazer.Free();
			}
			lazer = null;
			if (windup != null)
			{
				windup.Free();
				windup = null;
			}
			boss = null;
		}
	}

	// Average traverse speed (design-px/ms): the raw override if given, else a difficulty-scaled
	// fraction of the twin-MarsBoss traverse speed.
	private float TraverseSpeed()
	{
		if (speedOverride.HasValue)
		{
			return speedOverride.Value;
		}
		float modifier = Settings.GetInstance().DifficultyModifier;
		float frac = MathHelper.Clamp(SpeedFracPerModifier * modifier, 0.1f, 1.3f);
		return frac * TwoUfoRefSpeed;
	}

	private void StartMove(float distance)
	{
		moveDurationMs = distance / Math.Max(0.001f, TraverseSpeed());
		moveProgress = 0f;
	}

	private Vector2 BellyPoint()
	{
		return base.Position + new Vector2(0f, fireLead);
	}

	private void BeginCharge()
	{
		chargeTimer.Duration = windupMs;
		chargeTimer.Reset();
		chargeTimer.Start();
		// The converging spark swarm, same effect + params a medium UFO uses to wind up its laser
		// (LazerGenerator.Setup(pos, size, lifetime, impulse, direction)).
		windup = LazerGenerator.NewLazerGenerator(collection, base.Game);
		windup.Setup(BellyPoint(), 2f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)windup);
	}

	// Easy/Medium only, and only while the boss is a STANDING (stationary) target -- while it flies
	// around, aiming is unreliable, so the beam just goes straight down and a fly-by crosses it.
	private bool ShouldAimAtBoss()
	{
		if (boss == null || boss.IsDead)
		{
			return false;
		}
		Settings.DifficultyLevel difficulty = Settings.GetInstance().CurrentDifficulty;
		if (difficulty != Settings.DifficultyLevel.Easy && difficulty != Settings.DifficultyLevel.Medium)
		{
			return false;
		}
		return !boss.IsFlyingAround();
	}

	private void FireBeam()
	{
		// Straight down by default -- exactly PiOver2 is safe: the CollisionHandler line rasteriser
		// is hardened against degenerate near-axis-aligned lines (card 7a3e70ad), so the old
		// ~1.1deg FireTilt workaround (and the aimed-beam vertical snap) are gone.
		float direction = MathHelper.PiOver2;
		if (ShouldAimAtBoss())
		{
			Vector2 aim = boss.GetAimPoint() - BellyPoint();
			if (aim.LengthSquared() > 1f)
			{
				direction = MyMath.VectorToAngle(aim);
			}
		}
		lazer = Lazer.NewLazer(collection, base.Game);
		lazer.Setup(base.Position, direction, this, fireLead);
		collection.Add((GameComponent)(object)lazer);
	}

	protected override void HitBy(ICollidable other, bool isComboGenerator)
	{
		// Register the hit for the blink flash + (harmless) hitpoint decrement, then drive our own
		// reddening tint. WasHit is only set when the hit actually landed (not swallowed by the
		// short blink cooldown), so the ramp advances at a natural rate.
		base.HitBy(other, isComboGenerator);
		if (WasHit)
		{
			fakeHits++;
			float t = MathHelper.Clamp((float)fakeHits / (float)FakeHitsToFullRed, 0f, 1f);
			color = new Color(new Vector3(1f, 1f - t, 1f - t));
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		// Unreachable in practice (hitpoints never hit 0), but KillableAlien requires it and it is
		// the honest "if we want it to xplode" payoff should the pool ever be tuned down.
		Explode();
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		// The charge swarm is a LazerGenerator, which sets Visible=false in its ctor, so the component
		// collection never calls its Draw -- its owner must draw it BY HAND (exactly as MarsBoss.Draw
		// and UFO.Draw do for their generators). Without this the windup animation is invisible.
		if (windup != null)
		{
			((DrawableGameComponent)windup).Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		float prevFrame = curframe;
		base.Update(gameTime);
		if (curframe < prevFrame)
		{
			texture = (texture == firstHalfOfSpritesheet) ? secondHalfOfSpritesheet : firstHalfOfSpritesheet;
		}
		float dt = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		switch (state)
		{
		case HelperState.enter:
			moveProgress = MathHelper.Clamp(moveProgress + dt / moveDurationMs, 0f, 1f);
			// Quad ease-out to rest: PowerCurve with the endpoints swapped and t mirrored (see the
			// ease-shapes comment up top).
			base.Position = new Vector2(MyMath.PowerCurve(CenterX, EnterStartX, enterPower, 1f - moveProgress), hoverY);
			if (moveProgress >= 1f)
			{
				base.Position = new Vector2(CenterX, hoverY);
				state = HelperState.charge;
				BeginCharge();
			}
			break;
		case HelperState.charge:
			// Keep the swarm parked at the belly while it converges; then swap to the real beam.
			if (windup != null)
			{
				windup.SetPosition(BellyPoint());
			}
			if (chargeTimer.Finished)
			{
				if (windup != null)
				{
					collection.Remove((GameComponent)(object)windup);
					windup = null;
				}
				FireBeam();
				state = HelperState.fire;
				fireTimer.Reset();
				fireTimer.Start();
			}
			break;
		case HelperState.fire:
		{
			// The beam Dies (via Lazer.CollidesWith -> Die) the instant it catches the boss on a
			// fly-by, which ALSO drops that Lazer into the recycle pool -- a UFO in this same fight
			// can then grab and re-Setup it while we still hold the ref. So treat "no longer our live
			// Lazer" as job-done and leave, and only Free() a Lazer that is STILL ours + alive (the
			// fire window timed out without a hit). Never Free a recycled-away one.
			bool lazerLost = lazer == null || lazer.IsDead || lazer.owner != this;
			if (lazerLost || fireTimer.Finished)
			{
				if (!lazerLost)
				{
					lazer.Free();
				}
				lazer = null;
				state = HelperState.leave;
				StartMove(ExitX - CenterX);
			}
			break;
		}
		case HelperState.leave:
			moveProgress = MathHelper.Clamp(moveProgress + dt / moveDurationMs, 0f, 1f);
			// Quad ease-in: at rest at centre, accelerating away east.
			base.Position = new Vector2(MyMath.PowerCurve(CenterX, ExitX, LeavePower, moveProgress), hoverY);
			if (moveProgress >= 1f)
			{
				Die();
			}
			break;
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		// Ignore our own downward Lazer; otherwise take fake damage from player weapons like any alien.
		if (other is Lazer && ((Lazer)other).owner == this)
		{
			return;
		}
		base.CollidesWith(other);
	}

	private void Explode()
	{
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 4f, 2.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 6f, 5.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}
}
