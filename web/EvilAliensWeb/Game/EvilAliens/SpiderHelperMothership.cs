using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// A "helper" mothership the SpiderBoss fight summons when the boss has gone un-damaged for a
// while (see SpiderBoss's idle timer). The SpiderBoss can ONLY be hurt by a Lazer, and in normal
// play the only lazers around come from the big UFOs aiming at the player -- a very obscure way to
// realise you have to lure a lazer across the boss. This ship makes that legible: it slides in
// showing just its underside at the top of the screen, halts dead-centre, and fires a Lazer
// straight DOWN long enough that one of the boss's left/right fly-bys crosses it (that Lazer hits
// the boss through the normal Lazer->SpiderBoss damage path). Then it continues east and leaves.
//
// It is deliberately "fake killable": it flashes and reddens like it is taking damage (so the
// player feels they are hurting it) but its hitpoint pool is astronomically large, so it can never
// actually die before finishing its job -- it just flies off. The feel knobs (idle threshold,
// hover height, fly speed, fire duration) are tunable from the URL via DebugFlags; see the
// "?spiderhelper*" flags. Sprite + A/B-sheet animation mirror Boss/MarsBoss (the other motherships).
internal class SpiderHelperMothership : KillableAlien
{
	private enum HelperState
	{
		enter,
		fire,
		leave
	}

	// Fake-damage feedback: real hitpoints are astronomical (never dies), so drive the red tint off
	// a separate hit counter that ramps to fully-red over this many landed shots.
	private const int FakeHitsToFullRed = 40;

	// The laser fires "straight down" but is nudged a hair off true vertical. A PERFECTLY vertical
	// Lazer is a degenerate input to CollisionHandler's line rasteriser: it walks the collision line
	// cell-by-cell and, for a near-vertical line, its loop exits on val.X while each step adds only
	// 80*cos(angle) to val.X. cos(PiOver2) is ~-4.4e-8, and at x~400 that per-step delta is below the
	// float ULP (~3e-5), so val.X never actually changes and the `while (val.X > End.X)` loop spins
	// forever (a hard game hang). ~1.1 degrees off vertical is visually indistinguishable but makes
	// the X step ~1.6 px/cell, far above the ULP, so the rasteriser terminates normally.
	private const float FireTilt = 0.02f;

	private HelperState state;

	private float hoverY;

	private float flySpeed;

	private float fireLead;

	private Timer fireTimer = new Timer(4500f, repeating: false);

	private Texture2D firstHalfOfSpritesheet;

	private Texture2D secondHalfOfSpritesheet;

	private Lazer lazer;

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
	// flySpeed: horizontal design-px per ms. fireDurationMs: how long the downward Lazer holds.
	// fireLead: gap from the sprite centre down to where the beam starts (its belly).
	public void Setup(float hoverY, float flySpeed, float fireDurationMs, float fireLead)
	{
		this.hoverY = hoverY;
		this.flySpeed = flySpeed;
		this.fireLead = fireLead;
		fireTimer.Duration = fireDurationMs;
		state = HelperState.enter;
		base.Position = new Vector2(-260f, hoverY);
		base.Collides = true;
	}

	public override void Initialize()
	{
		base.Initialize();
		interpolationOptions = InterpolationOptions.never;
		fps = 16f;
		lazer = null;
		fakeHits = 0;
		color = Color.White;
		fireTimer.Stop();
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
		}
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
			base.Position = new Vector2(base.Position.X + flySpeed * dt, hoverY);
			if (base.Position.X >= 400f)
			{
				base.Position = new Vector2(400f, hoverY);
				state = HelperState.fire;
				fireTimer.Reset();
				fireTimer.Start();
				lazer = Lazer.NewLazer(collection, base.Game);
				lazer.Setup(base.Position, MathHelper.PiOver2 + FireTilt, this, fireLead);
				collection.Add((GameComponent)(object)lazer);
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
			}
			break;
		}
		case HelperState.leave:
			base.Position = new Vector2(base.Position.X + flySpeed * dt, hoverY);
			if (base.Position.X > 1100f)
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
