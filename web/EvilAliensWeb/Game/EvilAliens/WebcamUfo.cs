using System;
using EvilAliens.Constants;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;

namespace EvilAliens;

// The webcam challenge's saucer (Levels.WebcamAliens — the remake of the 2004
// webcam game the "I made this!" splash is from). Flies in from a screen edge,
// wanders the starfield, and if the player doesn't swat it in time it halts and starts
// BLINKING at an ever-faster rate — the classic "I'm about to do something
// evil" telegraph — then fires one big slow plasma shot at the player's body
// (the webcam mask centroid) and retreats. Unlike a normal UFO it does NOT despawn when
// it flies off-screen — it loops back into the field to keep harassing the player
// (ReturnToField); the ONLY thing that removes a webcam saucer is a player swat (Asplode).
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
		flee       // shot fired — retreat toward the nearest edge, then loop back (ReturnToField)
	}

	// How hard a body-touch hit is scored + the visible size of the saucer.
	private const float UfoScale = 1.25f;

	// Firing (item): when the blink-charge begins the saucer decelerates to a COMPLETE
	// stop over ~HaltMs so it fires from a clear, stationary, on-screen position (instead
	// of drifting while it blinks); after firing it ACCELERATES away from rest (FleeAccel
	// up to FleeMaxFactor x the wander max) rather than snapping to full speed.
	private const float HaltMs = 350f;

	private const float FleeAccel = 5E-04f;

	private const float FleeMaxFactor = 1.6f;

	// Persistence (item): a webcam saucer must STICK AROUND — the main game GCs a UFO that
	// flies too far off-screen, but here we want it back in the fight. After firing it
	// retreats only until it is this far past an edge (sprite fully hidden), then whips
	// around and flies back in. It only ever despawns when the player swats it.
	private const float RetreatMargin = 50f;

	// After a getaway the saucer holds off-screen for this long before looping back in,
	// so the retreat reads as a real exit (a beat of breathing room) rather than an
	// instant U-turn. Live-overridable via ?wcreturndelay= (ms); null => this baked value.
	private const float ReturnDelayMs = 900f;

	// Arming (blink-charge + fire) is only allowed to START when the saucer is at least
	// this far inside every edge, so a shot can NEVER originate off-screen or half-off it
	// (expressly-forbidden behavior). If the arm timer expires while the saucer is nearer
	// an edge than this, arming simply waits until wander containment has drawn it back in.
	private const float ArmInset = 60f;

	// Fly-around AI (item): while wandering, steer away from + orbit the player's mask
	// silhouette so a still player isn't drifted into. Strength is overridable live via
	// ?wcavoid= (null => DefaultAvoidStrength; 0 disables). AvoidRadius = how close (design
	// px) before a saucer reacts; OrbitStrength adds a tangential push so they circle the
	// player rather than only fleeing radially.
	private const float DefaultAvoidStrength = 1.2f;

	private const float AvoidRadius = 150f;

	private const float OrbitStrength = 0.75f;

	private UfoState state;

	private float wanderDir;

	// per-instance orbit handedness (+/-1) so a cluster doesn't circle in lock-step
	private int orbitSign = 1;

	// decel rate (px/ms per ms) captured when the halt begins, sized to reach 0 in ~HaltMs
	private float haltDecelPerMs;

	// Per-difficulty saucer-speed multiplier (WebcamLevel picks it from its tuning
	// table). Applied to the base/max drift speed in Initialize; 1 = the baseline.
	private float speedMul = 1f;

	// milliseconds of wandering before the blink phase starts (set by Setup).
	private Timer armTimer = new Timer(8000f, repeating: false);

	// the blink phase: blinkClock runs it, blinkPhase accumulates the variable-
	// frequency square wave (period ramps 400ms -> 70ms across the phase).
	private Timer blinkClock = new Timer(3000f, repeating: false);

	private float blinkPhase;

	// flee -> return dwell: once the saucer is fully off-screen after firing, this holds it
	// out for ReturnDelayMs before ReturnToField loops it back in. awaitingReturn latches the
	// dwell so it starts exactly once per getaway.
	private Timer returnTimer = new Timer(ReturnDelayMs, repeating: false);

	private bool awaitingReturn;

	private CollisionSimpleCircle circle = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public delegate void FiredHandler(WebcamUfo sender, Vector2 target);

	// Raised at the instant the blink phase completes; the level spawns the
	// plasma shot (it owns projectile bookkeeping).
	public event FiredHandler OnFired;

	// Soft "one ball at a time" cheat: when the blink-charge completes the saucer asks the
	// level whether the field is clear of plasma before actually firing. If a ball is already
	// out it HOLDS its charge (keeps blinking at max) and re-checks each tick, firing the
	// instant the ball clears. null => no gate (always fire). Two saucers finishing their
	// charge on the same tick can still both fire (they don't see each other's not-yet-spawned
	// ball) — that's the accepted edge case, not worth over-engineering out.
	public Func<bool> CanFire;

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
		AddTimer(returnTimer);
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
		returnTimer.Stop();
		awaitingReturn = false;
		base.Direction = wanderDir;
		base.Speed = RandomHelper.RandomNextFloat(0.1f, 0.15f) * speedMul;
		base.MaxSpeed = 0.18f * speedMul;
		base.Acceleration = 6E-05f;
		base.Deceleration = 1.8E-05f;
		orbitSign = (RandomHelper.Random.Next(2) == 0) ? -1 : 1;
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
			// Arm ONLY once the arm delay has elapsed AND the saucer is safely inside every
			// edge — so the halt/blink/fire can never happen off-screen. If the timer is up
			// but it's hugging an edge, keep wandering (containment reels it in) and arm the
			// moment it's inset. The timer stays Finished, so this defers, never skips.
			if (armTimer.Finished && OnScreenToArm())
			{
				state = UfoState.arming;
				blinkPhase = 0f;
				blinkClock.Reset();
				blinkClock.Start();
				// size the deceleration to bring this saucer to a full stop over ~HaltMs
				haltDecelPerMs = base.Speed / HaltMs;
			}
			break;
		case UfoState.arming:
		{
			// come to — and hold — a complete stop on-screen, then blink-charge in place,
			// so the shot clearly originates from a stationary, visible saucer.
			float dt = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			base.Speed = MathHelper.Max(0f, base.Speed - haltDecelPerMs * dt);
			base.Update(gameTime);   // ticks blinkClock + coasts the decaying drift + spins
			float t = 1f - blinkClock.Normalized;   // Normalized is time REMAINING; invert to 0->1
			float period = MathHelper.Lerp(400f, 70f, t);
			blinkPhase += dt / period;
			// Fire once fully charged — but only if the field is clear of plasma (the soft
			// one-ball cheat). If a ball is out, hold here (blinkClock stays Finished, so t=1
			// keeps it blinking at max) and re-check next tick until it clears.
			if (blinkClock.Finished && (CanFire == null || CanFire()))
			{
				Fire();
			}
			break;
		}
		case UfoState.flee:
			Move((float?)wanderDir, gameTime);
			base.Update(gameTime);
			if (!awaitingReturn)
			{
				if (OffScreen(RetreatMargin))
				{
					// fully off-screen after the getaway: start the dwell, then loop back in
					// (below) once it elapses — a beat of breathing room, not an instant U-turn.
					awaitingReturn = true;
					returnTimer.Duration = DebugFlags.WebcamReturnDelay ?? ReturnDelayMs;
					returnTimer.Reset();
					returnTimer.Start();
				}
			}
			else if (returnTimer.Finished)
			{
				ReturnToField();
			}
			break;
		}
	}

	// Standard bouncing drift, borrowed from UFO's normal state: keep off the edges,
	// occasionally pick a fresh heading, and steer around the player. ORDER MATTERS —
	// the player-avoidance steering is applied FIRST and the edge containment LAST, so
	// containment is authoritative: avoidance can never point the heading out through an
	// edge (the old order let it, which drifted saucers off-screen with no way back).
	private void UpdateWander(GameTime gameTime)
	{
		Vector2 v = MyMath.AngleToVector(wanderDir);
		// occasional fresh heading (before containment, so containment still has final say)
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0005 * gameTime.ElapsedGameTime.TotalMilliseconds)
		{
			v = MyMath.AngleToVector(RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f));
		}
		// Fly-around AI: bias the heading away from + tangentially around the player's mask
		// silhouette, so a saucer flows around a still player instead of drifting into them
		// (driven by the actual camera image, not just the centroid). ?wcavoid= tunes it.
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
		// Edge containment LAST: reflect any heading component that points out through a near
		// edge, so the saucer is kept in the field no matter what avoidance asked for.
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
		// Hard watchdog: if it has slipped past a screen edge anyway, forget everything and
		// head straight back to field centre — so a wandering saucer can never stall off-screen.
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

	// True only when the saucer is at least ArmInset inside every edge — the precondition
	// for entering the arming (blink-charge + fire) phase, so a shot never comes from off-screen.
	private bool OnScreenToArm()
	{
		return base.Position.X >= ArmInset && base.Position.X <= 800f - ArmInset && base.Position.Y >= ArmInset && base.Position.Y <= 600f - ArmInset;
	}

	private void Fire()
	{
		state = UfoState.flee;
		// flee towards the nearest horizontal edge, slightly upward — reads as "job done,
		// bailing out". Accelerate away FROM the dead stop (was: snap straight to full
		// speed) so the exit reads as a getaway, not a teleport.
		wanderDir = MyMath.VectorToAngle(new Vector2((base.Position.X < 400f) ? -1f : 1f, -0.35f));
		base.Direction = wanderDir;
		base.Acceleration = FleeAccel;
		base.MaxSpeed = 0.18f * speedMul * FleeMaxFactor;
		// fire at the player's CURRENT centre of mass, locked at the instant of release
		if (this.OnFired != null)
		{
			this.OnFired(this, WebcamInterop.Centroid);
		}
	}

	// Off-screen after the getaway: whip around (a snap turn is invisible out here — the
	// "cheat" it's fine to make) and fly back into the field via the fly-in path, so it
	// re-arms and fires again on the normal cadence. Webcam saucers are persistent — the
	// only thing that despawns one is the player swatting it (Asplode).
	private void ReturnToField()
	{
		state = UfoState.flyin;
		awaitingReturn = false;
		returnTimer.Stop();
		wanderDir = MyMath.VectorToAngle(new Vector2(RandomHelper.RandomNextFloat(250f, 550f), RandomHelper.RandomNextFloat(200f, 400f)) - base.Position);
		base.Direction = wanderDir;
		base.Speed = 0.15f * speedMul;
		base.MaxSpeed = 0.18f * speedMul;
		base.Acceleration = 6E-05f;
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
