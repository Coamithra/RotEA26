using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_0210: Unknown result type (might be due to invalid IL or missing references)
		//IL_0222: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, ((GameComponent)this).Game);
		bonus.Setup(Vector2.Zero);
		bonus.MakeType(powerupType);
	}

	public void SetAsBonus()
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, ((GameComponent)this).Game);
		bonus.Setup(Vector2.Zero);
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		if (!stationary)
		{
			if (hasbonus)
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, Powerup.PowerUpHue(bonus.type));
				if (bonus.type == Powerup.PowerupType.OneUp)
				{
					spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, 250f * (float)gameTime.TotalGameTime.TotalSeconds % 360f);
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
			spriteBatch.Draw(stationarySprite, base.Position, 0f, scale, center: true);
		}
		if (lazerGenerator != null)
		{
			((DrawableGameComponent)lazerGenerator).Draw(gameTime);
		}
	}

	public bool OffScreen()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		return (base.Position.X < 0f) | (base.Position.X > 800f) | (base.Position.Y < 0f) | (base.Position.Y > 600f);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Unknown result type (might be due to invalid IL or missing references)
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0274: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_06da: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_06bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_031b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0320: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_078c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0792: Unknown result type (might be due to invalid IL or missing references)
		//IL_0797: Unknown result type (might be due to invalid IL or missing references)
		//IL_079c: Unknown result type (might be due to invalid IL or missing references)
		//IL_076e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0774: Unknown result type (might be due to invalid IL or missing references)
		//IL_0779: Unknown result type (might be due to invalid IL or missing references)
		//IL_077e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_037f: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_07aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0518: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_041e: Unknown result type (might be due to invalid IL or missing references)
		//IL_044f: Unknown result type (might be due to invalid IL or missing references)
		invincibilityTimer.Update(gameTime);
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
				lazerGenerator = LazerGenerator.NewLazerGenerator(collection, ((GameComponent)this).Game);
				lazerGenerator.Setup(base.Position, 1f, 1f, 0f, 0f);
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
				((Vector2)(ref val)).Normalize();
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
				lazer = Lazer.NewLazer(collection, ((GameComponent)this).Game);
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
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		float num = 200f / Settings.GetInstance().DifficultyFactorized(0.4f);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			Vector2 val = ship.Position - base.Position;
			if (((Vector2)(ref val)).Length() <= num)
			{
				return;
			}
		}
		EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, ((GameComponent)this).Game);
		float direction = MyMath.SnapAngle(oracle.GetRandomPlayerPosition() - base.Position, 32);
		evilBullet.Setup(base.Position, direction);
		collection.Add((GameComponent)(object)evilBullet);
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
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
		((DrawableGameComponent)this).DrawOrder = 18;
		PointValue = 500f;
		SetHitPoints(11, scaleWithDifficulty: false);
	}

	public void SetStationary()
	{
		stationary = true;
		stationaryLiftOffX = RandomHelper.RandomNextFloat(400f, 550f);
	}

	private void MakeSmall()
	{
		IsBig = false;
		if (RandomHelper.Random.Next(2) == 1)
		{
			LoadAnimation(new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f));
			stationarySpriteName = (stationarySpriteName = "GFX/Sprites/ufometpootjes");
			((DrawableGameComponent)this).DrawOrder = 19;
		}
		else
		{
			LoadAnimation(new AnimationData("GFX/Sprites/smallship", 8, 4, 1, 25f));
			stationarySpriteName = (stationarySpriteName = "GFX/Sprites/Smallship_landed");
			((DrawableGameComponent)this).DrawOrder = 17;
		}
		PointValue = 10f;
		scale = 1f;
		SetHitPoints(1, scaleWithDifficulty: false);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0244: Unknown result type (might be due to invalid IL or missing references)
		if (!(other is Lazer) && !(other is Floorbottom) && !(other is Asteroid) && !(other is Spider) && !(other is FlyingSpider))
		{
			AwardScore(isComboGenerator, other);
		}
		Die();
		if (IsBig)
		{
			Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
			explosion.Setup(base.Position, 3.5f, 2.5f, base.Speed * 0.3f, base.Direction);
			collection.Add((GameComponent)(object)explosion);
			explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
			explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.95f, base.Direction);
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl2");
		}
		else
		{
			Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
			if (other is Asteroid)
			{
				Vector2 speed = ((Asteroid)other).GetSpeed();
				explosion.Setup(base.Position, 1f, 1f, ((Vector2)(ref speed)).Length(), MyMath.VectorToAngle(speed));
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
			collection.Add((GameComponent)(object)bonus);
			bonus.Position = base.Position;
			if (IsBig)
			{
				Powerup powerup = Powerup.NewPowerup(collection, ((GameComponent)this).Game);
				powerup.Setup(bonus.Position + new Vector2(20f, 0f));
				powerup.MakeType(bonus.type);
				collection.Add((GameComponent)(object)powerup);
				powerup = Powerup.NewPowerup(collection, ((GameComponent)this).Game);
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
}
