using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class UFO : KillableAlien
{
	private enum UFOState
	{
		normal,
		lazor,
		bullet,
		classic
	}

	private Timer invincibilityTimer = new Timer(500f, repeating: false);

	private bool directionIsPreset;

	private float presetdirection;

	public bool IsBig;

	// True while a big UFO is winding up its laser but has not fired yet (UFOState.lazor, the
	// 2500ms charge). The AI needs this for the spider-boss fight: the beam is aimed at the
	// PLAYER and its direction is locked at the moment it fires, so standing on the far side of
	// the boss during the windup walks the beam straight through the boss -- which is the only
	// thing besides the helper mothership that can hurt it. See PlayerShip.DoAIMove.
	internal bool AiChargingLazer => state == UFOState.lazor && lazer == null;

	// T4 bait (owner spec, lap 12): is this UFO's beam being aimed at `ship`? The windup aims
	// at `target` and locks at fire time, so during the charge this is who should interpose.
	internal bool AiLazerAimedAt(PlayerShip ship) => ReferenceEquals(target, ship);

	public static int Nr;

	private int thisNr;

	private bool hasbonus;

	private Powerup bonus;

	private EnemyBehaviour behaviour;

	private Texture2D stationarySprite;

	private string stationarySpriteName;

	private PlayerShip target;

	private float lazertime;

	private UFOState state;

	private float accelDir = (float)Math.PI / 2f;

	private Lazer lazer;

	private LazerGenerator lazerGenerator;

	private Timer starttime;

	private Timer shoottimer;

	private Timer flyintimer;

	private Timer flyawaytimer;

	private Timer liftofftimer;

	private Timer bonusrandomizer;

	private bool stationary;

	private float stationaryLiftOffX;

	// Per-sprite landed-placement tuning (Content/data/landed_offsets.json, authored with
	// wwwroot/landed-editor.html). Identity until SetStationary loads the parked still's entry.
	private EvilAliensWeb.Compat.LandedOffsets.Entry landedTuning = EvilAliensWeb.Compat.LandedOffsets.Entry.Identity;

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public UFO(Game game)
		: base(game)
	{
		flyintimer = new Timer(0f, repeating: false);
		starttime = new Timer(1500f, repeating: false);
		shoottimer = new Timer(1500f, repeating: false);
		flyawaytimer = new Timer(1f, repeating: false);
		liftofftimer = new Timer(1000f, repeating: false);
		bonusrandomizer = new Timer(5000f, repeating: true);
		AddTimer(starttime);
		AddTimer(shoottimer);
		AddTimer(flyintimer);
		AddTimer(flyawaytimer);
		AddTimer(liftofftimer);
		shoottimer.Stop();
		base.Colorize = false;
		thisNr = Nr;
		Nr++;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			if (lazer != null)
			{
				lazer.Free();
				lazer = null;
			}
			if (lazerGenerator != null)
			{
				lazerGenerator.Free();
				lazerGenerator = null;
			}
			if (bonus != null)
			{
				collection.Remove((GameComponent)(object)bonus);
				bonus = null;
			}
		}
		if (e.GameComponent == target)
		{
			target = null;
		}
	}

	public override void Initialize()
	{
		base.Initialize();
		scale = 1f;
		stationarySprite = content.Load<Texture2D>(stationarySpriteName);
		target = null;
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
			if (base.Position.X > 800f)
			{
				accelDir = (float)Math.PI;
			}
			if (base.Position.X < 0f)
			{
				accelDir = 0f;
			}
			if (base.Position.Y < 0f)
			{
				accelDir = (float)Math.PI / 2f;
			}
			if (base.Position.Y > 600f)
			{
				accelDir = 4.712389f;
			}
			base.Direction = accelDir;
			state = UFOState.normal;
			base.Acceleration = 6.0000002E-05f;
			base.Deceleration = 1.8E-05f;
			flyawaytimer.Reset();
			flyawaytimer.Start();
			if (directionIsPreset)
			{
				base.Direction = presetdirection;
			}
			break;
		case EnemyBehaviour.classic:
			base.MaxSpeed = 0.21599999f;
			base.Speed = RandomHelper.RandomNextFloat(0.072000004f, 0.21599999f);
			base.Direction = RandomHelper.RandomNextAngle();
			state = UFOState.classic;
			if (((base.Position.X < 0f) | (base.Position.X > 800f)) && (double)Math.Abs(base.DirectionalVector.X) < 0.5)
			{
				base.DirectionalVector = new Vector2(0.5f * (float)(-Math.Sign(base.Position.X)), base.DirectionalVector.Y);
			}
			if (((base.Position.Y < 0f) | (base.Position.Y > 600f)) && (double)Math.Abs(base.DirectionalVector.Y) < 0.5)
			{
				base.DirectionalVector = new Vector2(base.DirectionalVector.X, 0.5f * (float)(-Math.Sign(base.Position.Y)));
			}
			break;
		}
		if (lazer != null)
		{
			lazer = null;
		}
		lazerGenerator = null;
		starttime.Start();
		shoottimer.Stop();
		liftofftimer.Stop();
	}

	public static UFO NewUFO(ComponentBin collection, Game game)
	{
		UFO uFO = collection.Recycle<UFO>();
		if (uFO == null)
		{
			uFO = new UFO(game);
		}
		return uFO;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
	}

	public void Setup(Vector2 position, bool isBig, EnemyBehaviour behaviour)
	{
		directionIsPreset = false;
		flyawaytimer.Duration = 7000f;
		invincibilityTimer.Reset();
		invincibilityTimer.Stop();
		base.Position = position;
		if (isBig)
		{
			MakeBig();
		}
		else
		{
			MakeSmall();
		}
		this.behaviour = behaviour;
		if (behaviour == EnemyBehaviour.normal)
		{
			base.Speed = 0f;
			base.MaxSpeed = 0.18f;
		}
		hasbonus = false;
		stationary = false;
		// Recycled instance: clear any landed tuning from a previous parked life so a ship
		// spawned flying uses the untouched generic shadow (SetStationary re-applies it).
		landedTuning = EvilAliensWeb.Compat.LandedOffsets.Entry.Identity;
		base.ShadowOffset = Microsoft.Xna.Framework.Vector2.Zero;
		base.ShadowSize = 1f;
		flyintimer.Stop();
	}

	public void FlyInTime(float time)
	{
		flyintimer.Duration = time;
		flyintimer.Reset();
		flyintimer.Start();
	}

	public void SetAsBonus(Powerup.PowerupType powerupType)
	{
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, base.Game);
		bonus.Setup(Vector2.Zero);
		bonus.MakeType(powerupType);
	}

	public void SetAsBonus()
	{
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, base.Game);
		bonus.Setup(Vector2.Zero);
	}

	public override void Draw(GameTime gameTime)
	{
		if (!stationary)
		{
			if (hasbonus)
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, Powerup.PowerUpHue(bonus.type));
				if (bonus.type == Powerup.PowerupType.OneUp)
				{
					// WorldTime, not gameTime: a Draw-time hue cycle on the raw clock kept the OneUp
					// rainbow rolling while the world sat frozen in a pause (card d79a2f48).
					spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, 250f * WorldTime.Seconds % 360f);
				}
				spriteBatch.colorizeEffect.Enable();
			}
			if (invincibilityTimer.Active && MyMath.Mod(invincibilityTimer.TimeElapsed, 100f) <= 50f)
			{
				spriteBatch.lightenEffect.Enable();
			}
			base.Draw(gameTime);
			spriteBatch.colorizeEffect.Disable();
			if (invincibilityTimer.Active)
			{
				spriteBatch.lightenEffect.Disable();
			}
		}
		else
		{
			spriteBatch.BlendMode = (SpriteBlendMode)1;
			// landed stills are drawn directly (not via DrawScale), so undo any supersample
			// factor here; 1 for not-yet-upscaled stills (Smallship_landed / Mediumship_landed)
			float landedScale = scale / SuperSampleFactor(stationarySpriteName, stationarySprite.LogicalWidth());
			// landedTuning.Landed plants the still (its "feet") relative to the flying anchor.
			spriteBatch.Draw(stationarySprite, base.Position + landedTuning.Landed, 0f, landedScale, center: true);
		}
		if (lazerGenerator != null)
		{
			((DrawableGameComponent)lazerGenerator).Draw(gameTime);
		}
	}

	public bool OffScreen()
	{
		return (base.Position.X < 0f) | (base.Position.X > 800f) | (base.Position.Y < 0f) | (base.Position.Y > 600f);
	}

	// A landed UFO's stationary branch breaks out of Update WITHOUT calling base.Update, so its
	// ObservedVelocity is frozen at the per-life reset of zero even while the scroll-carry line
	// slides it along the terrain. Announce the carry instead (the SpiderBoss idiom: the swept
	// path is announced, not observed) so landed UFOs project drift cones like everything else
	// that moves. StationaryBoss needs no override -- it calls base.Update before its carry.
	internal override bool TryGetAiSweptPath(out Vector2 anchor, out Vector2 velocity, out float halfWidth)
	{
		if (stationary)
		{
			anchor = base.Position;
			velocity = oracle.BackgroundSpeed;
			halfWidth = AiHalfExtent();
			return velocity.LengthSquared() > 0.000001f;
		}
		return base.TryGetAiSweptPath(out anchor, out velocity, out halfWidth);
	}

	public override void Update(GameTime gameTime)
	{
		invincibilityTimer.Update(gameTime);
		// NOTE: inert by design, preserved faithful to the 2008 Xbox build. bonusrandomizer
		// is never AddTimer'd (see the ctor), so it is never Update'd and .Finished never
		// becomes true — bonus.Randomize() therefore never runs and the carried bonus keeps
		// its initial type. Wiring the timer up would ADD bonus-type cycling the shipped game
		// never had, so this stays as-is intentionally rather than being "fixed" blind.
		if (hasbonus && bonusrandomizer.Finished)
		{
			bonus.Randomize();
		}
		if (target == null)
		{
			target = oracle.GetRandomPlayerShip();
		}
		switch (state)
		{
		case UFOState.classic:
		{
			base.Update(gameTime);
			Vector2 directionalVector = base.DirectionalVector;
			if (base.Position.X > 800f && directionalVector.X > 0f)
			{
				directionalVector.X *= -1f;
			}
			if (base.Position.X < 0f && directionalVector.X < 0f)
			{
				directionalVector.X *= -1f;
			}
			if (base.Position.Y > 600f && directionalVector.Y > 0f)
			{
				directionalVector.Y *= -1f;
			}
			if (base.Position.Y < 0f && directionalVector.Y < 0f)
			{
				directionalVector.Y *= -1f;
			}
			base.DirectionalVector = directionalVector;
			if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.00015 * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier)
			{
				FireBullet();
			}
			break;
		}
		case UFOState.normal:
		{
			if (stationary)
			{
				base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
				if (base.Position.X < stationaryLiftOffX)
				{
					accelDir = RandomHelper.RandomNextFloat(0.55f, 0.7f) * ((float)Math.PI * 2f);
					base.Direction = accelDir;
					base.Speed = base.MaxSpeed;
					stationary = false;
					// Feet compensation: shift to the flying frame's centre so the sprite doesn't
					// jump the instant it swaps the parked still for the flying animation. Also
					// drop the parked shadow tuning back to the generic in-flight shadow.
					base.Position += landedTuning.Takeoff;
					base.ShadowOffset = Microsoft.Xna.Framework.Vector2.Zero;
					base.ShadowSize = 1f;
					liftofftimer.Reset();
					liftofftimer.Start();
					flyawaytimer.Duration = 16000f;
					flyawaytimer.Reset();
				}
				break;
			}
			if (liftofftimer.Active)
			{
				base.Position -= new Vector2(0.6f, 0f) * liftofftimer.Normalized * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			}
			Move((float?)accelDir, gameTime);
			base.Update(gameTime);
			if (!flyawaytimer.Active)
			{
				if ((base.Position.X > 900f) | (base.Position.X < -100f) | (base.Position.Y > 700f) | (base.Position.Y < -100f))
				{
					Die();
				}
				break;
			}
			Vector2 v2 = MyMath.AngleToVector(accelDir);
			int num = 100;
			if (flyintimer.Active && (double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.00035 * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier)
			{
				FireBullet();
			}
			if (!flyintimer.Active)
			{
				if (base.Position.X > (float)(800 - num) && v2.X > 0f)
				{
					v2.X *= -1f;
				}
				if (base.Position.X < (float)num && v2.X < 0f)
				{
					v2.X *= -1f;
				}
				if (base.Position.Y > (float)(600 - num) && v2.Y > 0f)
				{
					v2.Y *= -1f;
				}
				if (base.Position.Y < (float)num && v2.Y < 0f)
				{
					v2.Y *= -1f;
				}
				accelDir = MyMath.VectorToAngle(v2);
				if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0005 * gameTime.ElapsedGameTime.TotalMilliseconds)
				{
					accelDir = RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f);
				}
			}
			if (starttime.Finished & IsBig & ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0009 * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier) & !OffScreen())
			{
				state = UFOState.lazor;
				lazerGenerator = LazerGenerator.NewLazerGenerator(collection, base.Game);
				lazerGenerator.Setup(base.Position, 1f, 1f, 0f, 0f);
				lazerGenerator.SetWindup(2.5f, loop: false); // this state charges for 2500ms (see UFOState.lazor) before firing
				collection.Add((GameComponent)(object)lazerGenerator);
				base.Deceleration = 0.0001f;
				lazertime = 0f;
			}
			if (starttime.Finished & !IsBig & ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.00015 * gameTime.ElapsedGameTime.TotalMilliseconds * (double)Settings.GetInstance().DifficultyModifier) & !OffScreen())
			{
				state = UFOState.bullet;
				shoottimer.Reset();
				shoottimer.Start();
				base.Deceleration = 0.0001f;
				lazertime = 0f;
			}
			break;
		}
		case UFOState.bullet:
			Move(gameTime);
			base.Update(gameTime);
			lazertime += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (shoottimer.Finished)
			{
				shoottimer.Stop();
				shoottimer.Reset();
				FireBullet();
			}
			if (lazertime > 2000f)
			{
				state = UFOState.normal;
				base.Deceleration = 0f;
			}
			break;
		case UFOState.lazor:
			Move(gameTime);
			base.Update(gameTime);
			lazertime += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (lazertime <= 2500f)
			{
				Vector2 val = ((target == null) ? (new Vector2(400f, 300f) - base.Position) : (target.GetPosition() - base.Position));
				(val).Normalize();
				lazerGenerator.SetPosition(base.Position + val * 75f);
				if (lazer != null)
				{
					throw new Exception("dus");
				}
			}
			if (lazertime > 2500f && lazer == null)
			{
				collection.Remove((GameComponent)(object)lazerGenerator);
				lazerGenerator = null;
				lazer = Lazer.NewLazer(collection, base.Game);
				Vector2 v = ((target == null) ? (new Vector2(400f, 300f) - base.Position) : (target.GetPosition() - base.Position));
				lazer.Setup(base.Position, MyMath.VectorToAngle(v), this, 75f);
				collection.Add((GameComponent)(object)lazer);
			}
			if ((lazertime > 3250f) & (lazertime > 5000f * Settings.GetInstance().DifficultyModifier))
			{
				lazer.Free();
				lazer = null;
				state = UFOState.normal;
				base.Deceleration = 0f;
			}
			break;
		}
	}

	private void FireBullet()
	{
		float num = 200f / Settings.GetInstance().DifficultyFactorized(0.4f);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			Vector2 val = ship.Position - base.Position;
			if ((val).Length() <= num)
			{
				return;
			}
		}
		EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
		float direction = MyMath.SnapAngle(oracle.GetRandomPlayerPosition() - base.Position, 32);
		evilBullet.Setup(base.Position, direction);
		collection.Add((GameComponent)(object)evilBullet);
	}

	public override void CollidesWith(ICollidable other)
	{
		if ((other is Asteroid || other is Ball) | (other is Lazer && ((Lazer)other).owner != this))
		{
			HitBy(other, isComboGenerator: false);
		}
		if (other is Floor)
		{
			Vector2 v = MyMath.AngleToVector(accelDir);
			if (v.Y > 0f)
			{
				v.Y *= -1f;
			}
			accelDir = MyMath.VectorToAngle(v);
		}
		if (other is Floorbottom && (!stationary & (base.DirectionalVector.Y > 0f)))
		{
			KilledBy(other, isComboGenerator: false);
		}
		if ((other is Spider || other is FlyingSpider) & !IsBig)
		{
			KilledBy(other, isComboGenerator: false);
		}
		if (other is SpiderBoss)
		{
			KilledBy(other, isComboGenerator: false);
		}
		if (!invincibilityTimer.Active)
		{
			base.CollidesWith(other);
		}
	}

	private void MakeBig()
	{
		LoadAnimation(new AnimationData("GFX/Sprites/mediumship", 8, 4, 1, 25f));
		stationarySpriteName = "GFX/Sprites/Mediumship_landed";
		IsBig = true;
		base.DrawOrder = 18;
		PointValue = 500f;
		SetHitPoints(11, scaleWithDifficulty: false);
	}

	public void SetStationary()
	{
		stationary = true;
		stationaryLiftOffX = RandomHelper.RandomNextFloat(400f, 550f);
		// Pull this parked still's authored placement and apply its shadow tuning; the flying
		// ship's generic shadow is restored at lift-off (Update) / on the next Setup.
		landedTuning = EvilAliensWeb.Compat.LandedOffsets.Get(stationarySpriteName);
		base.ShadowOffset = landedTuning.Shadow;
		base.ShadowSize = landedTuning.ShadowSize;
	}

	private void MakeSmall()
	{
		IsBig = false;
		if (RandomHelper.Random.Next(2) == 1)
		{
			LoadAnimation(new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f));
			stationarySpriteName = (stationarySpriteName = "GFX/Sprites/ufometpootjes");
			base.DrawOrder = 19;
		}
		else
		{
			LoadAnimation(new AnimationData("GFX/Sprites/smallship", 8, 4, 1, 25f));
			stationarySpriteName = (stationarySpriteName = "GFX/Sprites/Smallship_landed");
			base.DrawOrder = 17;
		}
		PointValue = 10f;
		scale = 1f;
		SetHitPoints(1, scaleWithDifficulty: false);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		if (!(other is Lazer) && !(other is Floorbottom) && !(other is Asteroid) && !(other is Spider) && !(other is FlyingSpider))
		{
			AwardScore(isComboGenerator, other);
		}
		Die();
		if (IsBig)
		{
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 3.5f, 2.5f, base.Speed * 0.3f, base.Direction);
			collection.Add((GameComponent)(object)explosion);
			explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.95f, base.Direction);
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl2");
		}
		else
		{
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			if (other is Asteroid)
			{
				Vector2 speed = ((Asteroid)other).GetSpeed();
				explosion.Setup(base.Position, 1f, 1f, (speed).Length(), MyMath.VectorToAngle(speed));
			}
			else
			{
				explosion.Setup(base.Position, 1f, 1f, base.Speed * 0.3f, base.Direction);
			}
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl1");
		}
		if (hasbonus)
		{
			bonus.Position = base.Position;
			collection.Add((GameComponent)(object)bonus);
			if (IsBig)
			{
				Powerup powerup = Powerup.NewPowerup(collection, base.Game);
				powerup.Setup(bonus.Position + new Vector2(20f, 0f));
				powerup.MakeType(bonus.type);
				collection.Add((GameComponent)(object)powerup);
				powerup = Powerup.NewPowerup(collection, base.Game);
				powerup.Setup(bonus.Position + new Vector2(10f, 20f));
				powerup.MakeType(bonus.type);
				collection.Add((GameComponent)(object)powerup);
			}
			bonus = null;
			hasbonus = false;
		}
	}

	internal void SpeedUp()
	{
		base.Speed = base.MaxSpeed;
	}

	internal void SetDirection(float a)
	{
		directionIsPreset = true;
		presetdirection = a;
	}

	internal void SetInvincible()
	{
		invincibilityTimer.Reset();
		invincibilityTimer.Start();
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/UfoDescriptor) ----------

	internal EnemyBehaviour NetBehaviour => behaviour;

	internal bool NetHasBonus => hasbonus;

	internal byte NetBonusType => (byte)(hasbonus ? bonus.type : Powerup.PowerupType.Blast);

	internal bool NetStationary => stationary;

	// MakeSmall picks one of two small sheets at RANDOM -- the client puppet must be forced
	// onto the host's pick or the two screens show different saucers.
	internal bool NetSmallUfoSheet => texturename == "GFX/Sprites/ufosheet";

	internal void NetForceSmallSheet(bool ufoSheet)
	{
		if (IsBig || NetSmallUfoSheet == ufoSheet)
		{
			return;
		}
		if (ufoSheet)
		{
			LoadAnimation(new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f));
			stationarySpriteName = "GFX/Sprites/ufometpootjes";
			base.DrawOrder = 19;
		}
		else
		{
			LoadAnimation(new AnimationData("GFX/Sprites/smallship", 8, 4, 1, 25f));
			stationarySpriteName = "GFX/Sprites/Smallship_landed";
			base.DrawOrder = 17;
		}
	}

	// Puppet-side lift-off: the flying look without the gameplay impulses (the real
	// trajectory arrives via snapshots).
	internal void NetLiftOff()
	{
		if (stationary)
		{
			stationary = false;
			base.ShadowOffset = Microsoft.Xna.Framework.Vector2.Zero;
			base.ShadowSize = 1f;
		}
	}

	// The laser-charge glow (card 57ea30cd). A BIG ufo winds up a child LazerGenerator for 2500ms
	// before firing (UFOState.lazor) and draws it BY HAND in Draw -- so on a join peer, where this
	// saucer is a frozen puppet whose Update never runs, the beam simply appeared with no windup at
	// all. Same shape as SweepUFO / MarsBoss / SpiderHelperMothership: the descriptor streams a
	// tiny charge state and NetDriveExtras rebuilds a local copy into this very field, so Draw and
	// the OnComponentRemoved Free() cover it with no edits. See Compat/Net/NetChargeGlow.
	private bool netCharging;

	private Microsoft.Xna.Framework.Vector2 netChargeOffset;

	private float netChargeWindup = 2.5f;

	private float netChargeSize = 1f;

	// This emitter instance's own eased copy of the replicated aim (card eb057163). The wire value
	// only changes on this entity's snapshot turn, so the glow SWEEPS toward it instead of stepping;
	// it lives here rather than in NetChargeGlow because the child is pooled and the emitter is
	// what persists across a charge. Host-side it is never read (Drive is client-only).
	private EvilAliensWeb.Compat.Net.NetChargeGlow.AimEase netChargeAim;

	// Host encode: read live off the real generator (non-null only during the lazor windup -- the
	// state clears it the moment the beam is fired).
	internal bool NetCharging => lazerGenerator != null;

	internal Microsoft.Xna.Framework.Vector2 NetChargeOffset => lazerGenerator != null ? lazerGenerator.Position - base.Position : Microsoft.Xna.Framework.Vector2.Zero;

	internal float NetChargeWindup => lazerGenerator != null ? lazerGenerator.NetWindupSeconds : 2.5f;

	internal float NetChargeSize => lazerGenerator != null ? lazerGenerator.NetSize : 1f;

	// Client apply: record only. The child is spawned in NetDriveExtras, never here -- the
	// descriptor contract forbids spawning from ApplyStateExtra.
	internal void NetApplyCharge(bool charging, Microsoft.Xna.Framework.Vector2 offset, float windup, float size)
	{
		netCharging = charging;
		netChargeOffset = offset;
		netChargeWindup = windup;
		netChargeSize = size;
	}

	internal override void NetDriveExtras(Microsoft.Xna.Framework.GameTime gameTime)
	{
		EvilAliensWeb.Compat.Net.NetChargeGlow.Drive(ref lazerGenerator, ref netChargeAim, netCharging,
			netChargeOffset, netChargeWindup, netChargeSize, 1f, collection, base.Game,
			base.Position, (float)gameTime.ElapsedGameTime.TotalMilliseconds);
	}

	internal void NetClearBonus()
	{
		hasbonus = false;
		bonus = null;
	}
}
