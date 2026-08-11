using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// A "helper" mothership the SpiderBoss fight summons every N completed jump->fly->land CYCLES (N is
// locked at the fight's start from the difficulty, so it holds for the whole fight -- see
// SpiderBoss.HelperCyclePeriod). The SpiderBoss can ONLY be
// hurt by a Lazer, so this ship makes the fight legible: it EASES in from the left showing just its
// underside at the top, halts at ~62% across, WINDS UP (a converging spark swarm, exactly like a
// medium UFO charging its laser), then fires a Lazer. Where that Lazer AIMS depends on the difficulty
// TIER (not the live ramped modifier, so a long fight can't drift it):
//   Easy/Medium -> at the SPIDER when it's a standing target (else straight down + a fly-by crosses it)
//   Hard        -> straight down (rely on a boss fly-by)
//   Very_Hard/Inzane -> AT THE PLAYER -- the "helper" turns into a hazard on the top tiers.
// When it's done it EASES out east and exits right.
//
// Movement + speed mirror the twin "2 motherships" (MarsBoss): MyMath.PowerCurve eases at a
// DIFFICULTY-SCALED fraction of their traverse speed. The laser's own descent speed is
// difficulty-scaled inside Lazer.Update (growthspeed * modifier).
//
// It is KILLABLE (finite HP): the player can wreck it. When it runs out of HP it does NOT die
// immediately -- it keeps its state machine running to finish its "sacred mission" (charge + fire the
// laser), erupting in tiny upward-thrown explosions the whole time, and then instead of flying off it
// CRASHES down-and-right off the bottom-right corner, ending in a big floor-level explosion drawn at
// the right edge (mirrors how the mid-Level2 MarsBoss dies). Feel knobs are tunable from the URL via
// DebugFlags (?spiderhelper*). Sprite + A/B-sheet animation mirror Boss/MarsBoss.
internal class SpiderHelperMothership : KillableAlien
{
	private enum HelperState
	{
		enter,
		charge,
		fire,
		leave,
		crash
	}

	// Finite hitpoint pool so the player can actually destroy it (?spiderhelperhp overrides). 50 base --
	// squishy enough to be fun to take down -- difficulty-scaled by DifficultyFactorized(0.7) (steeper
	// than the KillableAlien default 0.5, so the top tiers still put up a fight). It never dies
	// MID-mission though: KilledBy just flips `dying`, and Die() only happens at CrashImpact.
	private const int DefaultHelperHitPoints = 50;

	// The DifficultyFactorized factor for the HP scaling (see Initialize).
	private const float HitPointDifficultyFactor = 0.7f;

	// Movement geometry (design space). The ship enters off the LEFT edge, rests at RestX (~62% across,
	// not dead centre) while it winds up + fires, then exits off the RIGHT edge (flies all the way over).
	private const float EnterStartX = -260f;

	private const float RestX = 496f;

	private const float ExitX = 1100f;

	// Ease shapes, both via MyMath.PowerCurve (= Lerp(a,b,t^p)). Enter is a quad ease-OUT TO REST:
	// Lerp(from,to,1-(1-t)^p) == PowerCurve(to,from,p,1-t) -- endpoints swapped, t mirrored -- so the
	// ship flies in already moving and DECELERATES to a true stop (zero arrival velocity) at RestX.
	// EnterPower must be >=1: default 2 = a gentle glide-to-rest; higher = punchier start, still stops
	// smoothly (?spiderhelperenterpower overrides live -- null => the baked const ships unchanged).
	// Leave is a plain quad ease-IN: PowerCurve(rest,exit,LeavePower,t) starts at rest and accelerates
	// away east.
	private const float DefaultEnterPower = 2f;

	private static float EnterPower => EvilAliensWeb.Compat.DebugFlags.SpiderHelperEnterPower ?? DefaultEnterPower;

	private const float LeavePower = 2f;

	// Crash-land (used only when it's dying): Y accelerates DOWN to floor level over CrashDurationMs
	// (ease-IN, power CrashPower -- gravity), capped AT the floor so it doesn't sink through; X carries
	// the retained kill-time momentum (crashVelX). Then a big explosion AT the crashed position.
	private const float CrashDurationMs = 1100f;

	private const float CrashPower = 2f;

	// Floor level the crash drops to (lifted from MarsBoss's asplode, which lands at y=470).
	private const float CrashFloorY = 470f;

	// A little rightward "flee" boost (px/ms) when killed while PARKED (charging/firing): it lurches
	// off-screen right, crashes, and the blast scrolls back into view with the background -- like it did
	// before x-velocity retention. (A mid-exit kill uses its real retained momentum instead.)
	private const float CrashFleeBoostX = 0.3f;

	// While dying, tiny explosions erupt at ~this many per second, each thrown UPWARD (mirrors
	// MarsBoss's asplode debris).
	private const float DeathBoomRate = 6f;

	private const float DeathBoomRise = 0.45f;

	// Reference: the twin MarsBoss ("2 motherships") traverse -500 -> ~400 over its 1200ms entry timer,
	// i.e. ~0.75 design-px/ms average. The helper moves at a difficulty-scaled FRACTION of this.
	private const float TwoUfoRefSpeed = 0.75f;

	// Fraction of the twin-MarsBoss traverse speed per unit Settings.DifficultyModifier (0.35 Easy ..
	// 1.2 Inzane): Easy ~0.23, Medium ~0.40, Hard ~0.53, Very_Hard 0.66, Inzane ~0.79.
	private const float SpeedFracPerModifier = 0.66f;

	// Entrance + exit each move at a FRACTION of TraverseSpeed -- deliberately slow (enter 0.6, exit
	// even slower at 0.4) so the ship lingers on screen, giving the player time to shoot it down.
	private const float EnterSpeedFactor = 0.6f;

	private const float ExitSpeedFactor = 0.4f;

	private HelperState state;

	private float hoverY;

	// Raw average-speed override (design-px/ms) from ?spiderhelperspeed; null = difficulty-scaled.
	private float? speedOverride;

	private float fireLead;

	private float windupMs;

	// Current move (enter/leave) as normalized 0..1 progress over a distance-derived duration.
	private float moveProgress;

	private float moveDurationMs;

	// Crash arc (only while state == crash): 0..1 vertical-drop progress, the Y it dropped from, and the
	// retained horizontal velocity (px/ms) at the moment of the kill -- 0 if it was parked charging, its
	// exit speed if it was flying off -- so a mid-movement kill keeps its momentum through the fall.
	private float crashProgress;

	private float crashFromY;

	private float crashVelX;

	// Set the instant HP hits 0 (KilledBy). The mission keeps running; the ship erupts in explosions
	// and crash-lands instead of flying off. Die() is deferred to CrashImpact.
	private bool dying;

	private Timer fireTimer = new Timer(4500f, repeating: false);

	private Timer chargeTimer = new Timer(2500f, repeating: false);

	private Texture2D firstHalfOfSpritesheet;

	private Texture2D secondHalfOfSpritesheet;

	private Lazer lazer;

	private LazerGenerator windup;

	private SpiderBoss boss;

	// The player the beam aims at on Very_Hard/Inzane, cached at charge start + tracked live through the
	// fire (mirrors MarsBoss caching its lazer target). Nulled if that ship is removed.
	private PlayerShip aimTarget;

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
		// 50 (matching the MarsBoss twins) so the ship draws AFTER -- and thus ON TOP OF -- its fired
		// Lazer (DrawOrder 40): the beam renders BEHIND the ship. The charge swarm still sits on top,
		// drawn by hand in Draw(). (Was 19, which put the beam in front.)
		base.DrawOrder = 50;
		AddTimer(fireTimer);
		AddTimer(chargeTimer);
		// Base pool; the DifficultyFactorized(0.7) scaling is applied per fight in Initialize (the
		// KillableAlien scaling path only offers a fixed 0.5 factor, so we do our own).
		SetHitPoints(HelperHitPoints(), scaleWithDifficulty: false);
		// Let the base KillableAlien red-tint ramp handle the "taking damage" look.
		base.Colorize = true;
		base.IsBoss = true;
		PointValue = 0f;
	}

	private static int HelperHitPoints()
	{
		return Math.Max(1, EvilAliensWeb.Compat.DebugFlags.SpiderHelperHitPoints ?? DefaultHelperHitPoints);
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
	// fireLead: gap from the sprite centre to the beam's origin, along the aim direction (BeamOrigin).
	// windupMs: charge-swarm duration before the beam (mirrors the medium UFO's ~2.5s laser windup).
	// boss: the summoning SpiderBoss (for Easy/Medium aim-at-a-standing-boss); may be null.
	// (The entrance ease exponent is NOT a param: EnterPower reads ?spiderhelperenterpower in-class,
	// like LeavePower it's a curve shape, not a per-spawn value.)
	public void Setup(float hoverY, float? speedOverride, float fireDurationMs, float fireLead, float windupMs, SpiderBoss boss)
	{
		this.hoverY = hoverY;
		this.speedOverride = speedOverride;
		this.fireLead = fireLead;
		this.windupMs = windupMs;
		this.boss = boss;
		fireTimer.Duration = fireDurationMs;
		state = HelperState.enter;
		base.Position = new Vector2(EnterStartX, hoverY);
		base.Collides = true;
		StartMove(RestX - EnterStartX, EnterSpeedFactor);
	}

	public override void Initialize()
	{
		// Difficulty-scale the pool ourselves with DifficultyFactorized(0.7) (steeper than the
		// KillableAlien default 0.5) and seed initialhitpoints with the scaled value so the tint ramp +
		// HitPointsNormalized stay correct; base.Initialize (scaleWithDifficulty:false) then just copies it.
		SetHitPoints((int)MathHelper.Max(1f, HelperHitPoints() * Settings.GetInstance().DifficultyFactorized(HitPointDifficultyFactor)), scaleWithDifficulty: false);
		base.Initialize();
		interpolationOptions = InterpolationOptions.never;
		fps = 16f;
		lazer = null;
		windup = null;
		aimTarget = null;
		dying = false;
		netDying = false;
		crashProgress = 0f;
		crashVelX = 0f;
		color = Color.White;
		fireTimer.Stop();
		chargeTimer.Stop();
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == aimTarget)
		{
			aimTarget = null;
		}
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

	private void StartMove(float distance, float speedFactor)
	{
		moveDurationMs = distance / Math.Max(0.001f, TraverseSpeed() * speedFactor);
		moveProgress = 0f;
	}

	// The muzzle: fireLead px from the sprite centre ALONG the aim direction -- exactly MarsBoss's
	// scheme (base.Position + aimDir * 100, and fireLead defaults to that same 100; same sprite, so
	// the beam emerges from the same spot on the hull). The charge swarm parks here and the beam is
	// fired FROM here (lead 0), so the two always line up.
	private Vector2 BeamOrigin(float direction)
	{
		return base.Position + fireLead * MyMath.AngleToVector(direction);
	}

	// Aim direction, keyed on the difficulty TIER (not the ramped modifier): Easy/Medium at the standing
	// spider, Hard straight down, Very_Hard/Inzane at the player. Computed from the sprite centre; the
	// charge swarm + beam origin both offset from here by fireLead along this direction (BeamOrigin).
	private float CurrentAimDirection()
	{
		// Straight down by default (Hard). Exactly PiOver2 is safe: the CollisionHandler line rasteriser
		// is hardened against degenerate near-axis-aligned lines (card 7a3e70ad).
		float direction = MathHelper.PiOver2;
		Vector2 aim = Vector2.Zero;
		bool hasAim = false;
		switch (Settings.GetInstance().CurrentDifficulty)
		{
		case Settings.DifficultyLevel.Easy:
		case Settings.DifficultyLevel.Medium:
			if (BossIsStandingTarget())
			{
				aim = boss.GetAimPoint() - base.Position;
				hasAim = true;
			}
			break;
		case Settings.DifficultyLevel.Very_Hard:
		case Settings.DifficultyLevel.Inzane:
			// The "helper" turns hazard on the top tiers: it shoots at the cached player (tracked live,
			// like MarsBoss). If that player is gone, fall through to straight down.
			if (aimTarget != null)
			{
				aim = aimTarget.GetPosition() - base.Position;
				hasAim = true;
			}
			break;
		}
		if (hasAim && aim.LengthSquared() > 1f)
		{
			direction = MyMath.VectorToAngle(aim);
		}
		return direction;
	}

	private void BeginCharge()
	{
		chargeTimer.Duration = windupMs;
		chargeTimer.Reset();
		chargeTimer.Start();
		// Cache the aim target for the whole windup + fire (MarsBoss style); harmless on the tiers that
		// don't aim at the player.
		aimTarget = oracle.GetRandomPlayerShip();
		// The converging spark swarm, same effect + params a medium UFO uses to wind up its laser
		// (LazerGenerator.Setup(pos, size, lifetime, impulse, direction)), parked where the beam will emerge.
		windup = LazerGenerator.NewLazerGenerator(collection, base.Game);
		windup.Setup(BeamOrigin(CurrentAimDirection()), 2f, 1f, 0f, 0f);
		windup.SetWindup(windupMs / 1000f, loop: false); // ramp fills the (difficulty-scaled) charge exactly
		collection.Add((GameComponent)(object)windup);
	}

	// Only meaningful on Easy/Medium: the boss is a reliable aim target only while STANDING; mid-fly
	// aiming is unreliable, so the beam goes straight down (and a fly-by crosses it) instead.
	private bool BossIsStandingTarget()
	{
		return boss != null && !boss.IsDead && !boss.IsFlyingAround();
	}

	private void FireBeam()
	{
		// Fire from the aim-offset origin (like MarsBoss: base.Position + aimDir*offset, lead 0), so the
		// beam emerges where the charge swarm converged and shifts with the aim, not straight below.
		float direction = CurrentAimDirection();
		lazer = Lazer.NewLazer(collection, base.Game);
		lazer.Setup(BeamOrigin(direction), direction, this, 0f);
		collection.Add((GameComponent)(object)lazer);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		// Don't die yet -- the mothership is on a sacred mission. It keeps its state machine running
		// (finishing the charge + laser) while erupting in explosions, then crash-lands off the
		// bottom-right (mirrors MarsBoss's survivor asplode). Die() happens later, in CrashImpact.
		dying = true;
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
		// Once dying, erupt in tiny upward-thrown explosions the whole way down (boom boom boom...).
		if (dying)
		{
			SpawnDeathBooms(gameTime);
		}
		switch (state)
		{
		case HelperState.enter:
			moveProgress = MathHelper.Clamp(moveProgress + dt / moveDurationMs, 0f, 1f);
			// Quad ease-out to rest: PowerCurve with the endpoints swapped and t mirrored (see the
			// ease-shapes comment up top).
			base.Position = new Vector2(MyMath.PowerCurve(RestX, EnterStartX, EnterPower, 1f - moveProgress), hoverY);
			if (moveProgress >= 1f)
			{
				base.Position = new Vector2(RestX, hoverY);
				state = HelperState.charge;
				BeginCharge();
			}
			break;
		case HelperState.charge:
			// Track the aim while the swarm converges so it (and the beam origin) shift toward the target.
			if (windup != null)
			{
				windup.SetPosition(BeamOrigin(CurrentAimDirection()));
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
				// Mission done. If we were destroyed along the way, crash-land instead of flying off.
				if (dying)
				{
					// Killed during the mission -> parked, so give it a little rightward flee-boost: it
					// lurches off-screen right, crashes, and the blast scrolls back in with the background.
					BeginCrash(CrashFleeBoostX);
				}
				else
				{
					state = HelperState.leave;
					StartMove(ExitX - RestX, ExitSpeedFactor);
				}
			}
			break;
		}
		case HelperState.leave:
			// Killed while flying off? Abandon the graceful exit and crash -- carrying the exit momentum.
			if (dying)
			{
				BeginCrash(LeaveVelocityX());
				break;
			}
			moveProgress = MathHelper.Clamp(moveProgress + dt / moveDurationMs, 0f, 1f);
			// Quad ease-in: at rest at RestX, accelerating away east.
			base.Position = new Vector2(MyMath.PowerCurve(RestX, ExitX, LeavePower, moveProgress), hoverY);
			if (moveProgress >= 1f)
			{
				Die();
			}
			break;
		case HelperState.crash:
			crashProgress = MathHelper.Clamp(crashProgress + dt / CrashDurationMs, 0f, 1f);
			// Y: accelerating drop (ease-IN) to floor level, capped there. X: keep drifting at the
			// retained kill-time velocity so it arcs the way it was going (0 => straight down).
			base.Position = new Vector2(
				base.Position.X + crashVelX * dt,
				MyMath.PowerCurve(crashFromY, CrashFloorY, CrashPower, crashProgress));
			if (crashProgress >= 1f)
			{
				CrashImpact();
			}
			break;
		}
	}

	private void BeginCrash(float velX)
	{
		state = HelperState.crash;
		crashFromY = base.Position.Y;
		crashVelX = velX;
		crashProgress = 0f;
	}

	// Horizontal speed (px/ms) of the leave tween, by finite difference: sample the SAME ease the
	// movement uses at the current progress and ~one frame (16ms) ahead, and divide the gap by 16. So a
	// kill mid-exit carries its real momentum into the crash -- no hand-derivative, works for any curve.
	private float LeaveVelocityX()
	{
		if (moveDurationMs <= 0.001f)
		{
			return 0f;
		}
		const float stepMs = 16f;
		float x0 = MyMath.PowerCurve(RestX, ExitX, LeavePower, moveProgress);
		float x1 = MyMath.PowerCurve(RestX, ExitX, LeavePower, moveProgress + stepMs / moveDurationMs);
		return (x1 - x0) / stepMs;
	}

	// A tiny explosion somewhere across the visible belly, thrown UPWARD (the particle burst rises and
	// fades = the "upward velocity with ease-out"). Same recipe as MarsBoss's asplode debris.
	private void SpawnDeathBooms(GameTime gameTime)
	{
		if (!RandomHelper.RandomFromAverage(DeathBoomRate, gameTime))
		{
			return;
		}
		Vector2 pos = base.Position + new Vector2(RandomHelper.RandomNextFloat(-90f, 90f), RandomHelper.RandomNextFloat(-20f, 120f));
		SpawnDeathBoomAt(pos);
	}

	private void SpawnDeathBoomAt(Vector2 pos)
	{
		Vector2 v = oracle.BackgroundSpeed + new Vector2(0f, -DeathBoomRise);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(pos, 1f, 1f, (v).Length(), MyMath.VectorToAngle(v));
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}

	// End of the crash: the ship has hit the floor. Die() now (clears the boss's helper ref via OnDeath)
	// and blow it up AT the crashed position, with the three explosion sizes lifted from MarsBoss's finale.
	private void CrashImpact()
	{
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 4f, 2.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 6f, 5.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	public override void CollidesWith(ICollidable other)
	{
		// Ignore our own downward Lazer; otherwise take damage from player weapons like any alien.
		if (other is Lazer && ((Lazer)other).owner == this)
		{
			return;
		}
		base.CollidesWith(other);
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsCoverage) --------
	// Mirrors MarsBoss/Boss: the 4x4 mothershipB sheet ALTERNATES between the mothershipA/mothershipB
	// halves each animation wrap in Update; that A/B choice is a bit of Draw state the base
	// fields (curframe/Hp) don't carry, so it is streamed (alongside the dying bit -- see the
	// deferred-death region below). The HP-redden colorize rides the base Hp
	// (NetApplyHp); initialhitpoints is difficulty-scaled but the client shares the session
	// difficulty (TeamChallenge locks it), so the redden matches. The charge-swarm windup glow is a
	// child LazerGenerator that replicates separately (see LazerGeneratorDescriptor); the fired Lazer
	// is its own replicated entity.
	internal bool NetSecondHalf => texture == secondHalfOfSpritesheet;

	internal void NetSetSpritesheetHalf(bool second)
	{
		if (second)
		{
			if (secondHalfOfSpritesheet != null)
			{
				texture = secondHalfOfSpritesheet;
			}
		}
		else if (firstHalfOfSpritesheet != null)
		{
			texture = firstHalfOfSpritesheet;
		}
	}

	// The charge-swarm `windup` energy well is a child the host draws by hand (see Draw). On a JOIN
	// peer this puppet is frozen, so the descriptor replicates the charge state and NetDriveExtras
	// rebuilds a local silent copy into the same `windup` field (Draw + the OnComponentRemoved Free()
	// then cover it unchanged). See Compat/Net/NetChargeGlow.
	private bool netCharging;

	private Vector2 netChargeOffset;

	private float netChargeWindup = 2.5f;

	private float netChargeSize = 2f;

	// This emitter instance's own eased copy of the replicated aim (card eb057163). The wire value
	// only changes on this entity's snapshot turn, so the glow SWEEPS toward it instead of stepping;
	// it lives here rather than in NetChargeGlow because the child is pooled and the emitter is
	// what persists across a charge. Host-side it is never read (Drive is client-only).
	private EvilAliensWeb.Compat.Net.NetChargeGlow.AimEase netChargeAim;

	internal bool NetCharging => windup != null;

	internal Vector2 NetChargeOffset => windup != null ? windup.Position - base.Position : Vector2.Zero;

	internal float NetChargeWindup => windup != null ? windup.NetWindupSeconds : 2.5f;

	internal float NetChargeSize => windup != null ? windup.NetSize : 2f;

	internal void NetApplyCharge(bool charging, Vector2 offset, float windupSeconds, float size)
	{
		netCharging = charging;
		netChargeOffset = offset;
		netChargeWindup = windupSeconds;
		netChargeSize = size;
	}

	internal override void NetDriveExtras(GameTime gameTime)
	{
		EvilAliensWeb.Compat.Net.NetChargeGlow.Drive(ref windup, ref netChargeAim, netCharging,
			netChargeOffset, netChargeWindup, netChargeSize, 1f, collection, base.Game,
			base.Position, (float)gameTime.ElapsedGameTime.TotalMilliseconds);
		if (netDying)
		{
			NetSpawnDeathBooms((float)gameTime.ElapsedGameTime.TotalMilliseconds);
		}
	}

	// ---- Online co-op deferred death (card 1878b321) -----------------------------------------
	// The helper's KilledBy only FLAGS the death -- the ship keeps flying its charge/fire mission
	// and Die() waits for CrashImpact -- so on a join peer the generic deferred-death handling was
	// wrong twice over: releasing the puppet at the death-began beat restarted its (unreplicated)
	// HelperState at `enter`, teleporting it off-screen left to REPLAY the whole entrance/charge/
	// fire before crashing -- the card's "hangs around when dead". So instead:
	//   - the dying mission is TRACKED frozen (NetDyingStaysReplicated): the host keeps streaming
	//     the id for the whole remnant, and position / charge glow / hp-redden all already ride
	//     the wire. The `dying` flag itself replicates as a state-extra bit (the descriptor's
	//     flags byte) so the frozen puppet can erupt the same death booms the host shows;
	//   - the FINAL EvDeath (the host's CrashImpact) then ends it through NetBeginDeferredDeath:
	//     by that point the crash arc has already been mirrored by snapshots, so the local death
	//     IS the impact -- CrashImpact() Die()s, and NetPuppets skips the release.

	// The host's `dying` flag, for the descriptor's state extras.
	internal bool NetDying => dying;

	// The replicated copy on a frozen puppet. Kept apart from `dying` on purpose: `dying` drives
	// the frozen Update's boom spawner (never runs on a puppet) and the mission's crash branches,
	// while this one only feeds NetSpawnDeathBooms in NetDriveExtras.
	private bool netDying;

	internal void NetSetDying(bool value)
	{
		netDying = value;
	}

	// Client-side copy of SpawnDeathBooms for a frozen dying puppet. Private RNG, never
	// RandomHelper -- the Quad/ShipConnector rule: a per-puppet-per-tick cosmetic must not pull
	// the shared gameplay generator out from under this peer's other consumers.
	private static readonly System.Random netBoomRandom = new System.Random();

	private void NetSpawnDeathBooms(float dtMs)
	{
		// Same law as the host leg's RandomHelper.RandomFromAverage(DeathBoomRate, gameTime),
		// on the private RNG: hit with probability rate * dtSeconds per tick.
		if (netBoomRandom.NextDouble() > (double)(DeathBoomRate * dtMs / 1000f))
		{
			return;
		}
		Vector2 pos = base.Position + new Vector2(
			(float)netBoomRandom.NextDouble() * 180f - 90f,
			(float)netBoomRandom.NextDouble() * 140f - 20f);
		SpawnDeathBoomAt(pos);
	}

	private protected override bool NetDyingStaysReplicatedSelf => true;

	// Reached from the FINAL EvDeath (NetPuppets.OnRemoteDeath consults the seam before
	// releasing a deferred killable). The host has just run CrashImpact, and the crash arc was
	// already mirrored by snapshots -- so the local death is the impact itself, at the
	// replicated crash-end position. Die() inside CrashImpact queues the removal, so the caller
	// releases nothing. Idempotent: a second beat finds IsDead and does nothing. (`dying` is
	// deliberately not touched here -- nothing on a frozen puppet reads it, and the booms run
	// off `netDying`.)
	private protected override bool NetBeginDeferredDeathSelf()
	{
		if (!IsDead)
		{
			CrashImpact();
		}
		return true;
	}
}
