using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class EvilSkull : KillableAlien
{
	private EnemyBehaviour behaviour;

	private bool hasbonus;

	private Powerup bonus;

	private bool isInMaze;

	private bool justspawned;

	private bool hitwall;

	private float accelDir;

	private bool launched;

	private Timer launchedTimer = new Timer(1300f, repeating: false);

	private Timer fadeintimer;

	private Timer fadeouttimer;

	private Timer lifetime;

	private Timer shoottimer1;

	private Timer shoottimer2;

	private int bulletsfired;

	public bool Fading => fadeintimer.Active | fadeouttimer.Active | fadeouttimer.Finished;

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

	public EvilSkull(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/faceofdeathspritesheet", 4, 8, 1, 12f));
		base.DrawOrder = 20;
		PointValue = 25f;
		shoottimer1 = new Timer(5500f, repeating: true);
		shoottimer2 = new Timer(133f, repeating: true);
		fadeintimer = new Timer(1f, repeating: false);
		fadeouttimer = new Timer(800f, repeating: false);
		lifetime = new Timer(6000f, repeating: false);
		timers.Add(shoottimer1);
		timers.Add(shoottimer2);
		timers.Add(launchedTimer);
		timers.Add(fadeintimer);
		timers.Add(fadeouttimer);
		timers.Add(lifetime);
		SetHitPoints(1, scaleWithDifficulty: false);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this && bonus != null)
		{
			collection.Remove((GameComponent)(object)bonus);
			bonus = null;
		}
	}

	public static EvilSkull NewEvilSkull(ComponentBin collection, Game game)
	{
		EvilSkull evilSkull = collection.Recycle<EvilSkull>();
		if (evilSkull == null)
		{
			evilSkull = new EvilSkull(game);
		}
		return evilSkull;
	}

	public void Setup(Vector2 startposition, EnemyBehaviour behaviour)
	{
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		this.behaviour = behaviour;
		launched = false;
		hasbonus = false;
		isInMaze = false;
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
			justspawned = true;
			hitwall = false;
			base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f));
			fadeintimer.Duration = 1000f;
			fadeintimer.Start();
			lifetime.Start();
			fadeouttimer.Stop();
			break;
		case EnemyBehaviour.classic:
			base.Position = startposition;
			fadeouttimer.Stop();
			fadeintimer.Stop();
			lifetime.Stop();
			break;
		}
	}

	public void SetMaze()
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		isInMaze = true;
		base.Position = new Vector2(base.Position.X, -45f);
		fadeintimer.Duration = 10f;
		fadeintimer.Reset();
	}

	internal void SetMaze(bool p)
	{
		if (p)
		{
			SetMaze();
		}
		else
		{
			isInMaze = p;
		}
	}

	public void SetupLaunch(Vector2 startposition, float direction)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		behaviour = EnemyBehaviour.normal;
		base.Position = startposition;
		base.Direction = direction;
		launched = true;
		launchedTimer.Start();
		isInMaze = true;
		hasbonus = false;
		fadeintimer.Stop();
		lifetime.Start();
		fadeouttimer.Stop();
	}

	public void MakeBonus()
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		hasbonus = true;
		bonus = Powerup.NewPowerup(collection, base.Game);
		bonus.Setup(Vector2.Zero);
	}

	public override void Initialize()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		color = Color.White;
		base.Acceleration = 6.0000002E-05f;
		base.Deceleration = 1.8E-05f;
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
			if (launched)
			{
				base.MaxSpeed = 0.15f;
				base.Speed = base.MaxSpeed;
				accelDir = base.Direction;
				shoottimer1.Duration = 8000f;
			}
			else
			{
				base.MaxSpeed = 0.03f;
				base.Speed = 0f;
				accelDir = RandomHelper.RandomNextAngle();
				base.Direction = accelDir;
				shoottimer1.Duration = 5500f;
			}
			shoottimer1.Start();
			shoottimer1.Randomize();
			shoottimer2.Stop();
			break;
		case EnemyBehaviour.classic:
			base.MaxSpeed = 0.107999995f;
			base.Direction = RandomHelper.RandomNextAngle();
			base.Speed = RandomHelper.RandomNextFloat(0.036000002f, 0.107999995f);
			shoottimer1.Duration = 4500f;
			shoottimer1.Start();
			shoottimer1.Randomize();
			shoottimer2.Stop();
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
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		if (fadeintimer.Active)
		{
			float num = 1f - fadeintimer.Normalized;
			color = new Color(new Vector4(1f, 1f, 1f, num));
		}
		else if (fadeouttimer.Active | fadeouttimer.Finished)
		{
			float normalized = fadeouttimer.Normalized;
			color = new Color(new Vector4(1f, 1f, 1f, normalized));
		}
		else
		{
			color = Color.White;
		}
		if (hasbonus)
		{
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(72f, 160f, Powerup.PowerUpHue(bonus.type));
			if (bonus.type == Powerup.PowerupType.OneUp)
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(72f, 160f, 250f * (float)gameTime.TotalGameTime.TotalSeconds % 360f);
			}
			spriteBatch.colorizeEffect.Enable();
		}
		base.Draw(gameTime);
		spriteBatch.colorizeEffect.Disable();
	}

	public override void Update(GameTime gameTime)
	{
		//IL_04b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0520: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0554: Unknown result type (might be due to invalid IL or missing references)
		//IL_022c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0237: Unknown result type (might be due to invalid IL or missing references)
		//IL_024c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0262: Unknown result type (might be due to invalid IL or missing references)
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Unknown result type (might be due to invalid IL or missing references)
		//IL_0219: Unknown result type (might be due to invalid IL or missing references)
		//IL_021e: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0588: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_05cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0325: Unknown result type (might be due to invalid IL or missing references)
		//IL_0337: Unknown result type (might be due to invalid IL or missing references)
		//IL_034a: Unknown result type (might be due to invalid IL or missing references)
		//IL_035d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_040f: Unknown result type (might be due to invalid IL or missing references)
		//IL_041a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0420: Unknown result type (might be due to invalid IL or missing references)
		//IL_0425: Unknown result type (might be due to invalid IL or missing references)
		if (!hitwall)
		{
			justspawned = false;
		}
		else
		{
			hitwall = false;
		}
		if (justspawned)
		{
			return;
		}
		if (lifetime.Finished)
		{
			fadeouttimer.Start();
		}
		if (fadeouttimer.Finished)
		{
			collection.Remove((GameComponent)(object)this);
		}
		switch (behaviour)
		{
		case EnemyBehaviour.normal:
		{
			if (launchedTimer.Active & launched)
			{
				base.MaxSpeed = MathHelper.Lerp(0.03f, 2.5f, launchedTimer.Normalized / 16.666666f);
				base.Speed = base.MaxSpeed;
			}
			if (launchedTimer.Finished & launched)
			{
				base.MaxSpeed = 0.03f;
			}
			Move((float?)accelDir, gameTime);
			base.Update(gameTime);
			Vector2 v = MyMath.AngleToVector(accelDir);
			if (!isInMaze)
			{
				int num = 10;
				if (base.Position.X > (float)(800 - num) && v.X > 0f)
				{
					v.X *= -1f;
				}
				if (base.Position.X < (float)num && v.X < 0f)
				{
					v.X *= -1f;
				}
				if (base.Position.Y > (float)(600 - num) && v.Y > 0f)
				{
					v.Y *= -1f;
				}
				if (base.Position.Y < (float)num && v.Y < 0f)
				{
					v.Y *= -1f;
				}
			}
			else if (!launched | !launchedTimer.Active)
			{
				base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			}
			else
			{
				base.Position += oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * (1f - launchedTimer.Normalized);
			}
			accelDir = MyMath.VectorToAngle(v);
			if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.0002 * gameTime.ElapsedGameTime.TotalMilliseconds)
			{
				accelDir = RandomHelper.RandomNextFloat(0f, (float)Math.PI * 2f);
			}
			float num2 = 75f;
			if ((base.Position.X < 0f - num2) | (base.Position.X > 800f + num2) | (base.Position.Y < 0f - num2) | (base.Position.Y > 600f + num2))
			{
				Die();
			}
			if (shoottimer2.Finished)
			{
				if ((base.Position.X > 0f) & (base.Position.Y > 0f) & (base.Position.X < 800f) & (base.Position.Y < 600f))
				{
					bool flag = false;
					float num3 = 200f / Settings.GetInstance().DifficultyFactorized(0.4f);
					foreach (PlayerShip ship in oracle.GetShips())
					{
						bool num4 = flag;
						Vector2 val = ship.Position - base.Position;
						flag = num4 | ((val).Length() <= num3);
					}
					if (!(flag | fadeintimer.Active))
					{
						EvilBullet evilBullet2 = EvilBullet.NewEvilBullet(collection, base.Game);
						evilBullet2.Setup(base.Position, MyMath.SnapAngle(MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position), 32));
						collection.Add((GameComponent)(object)evilBullet2);
					}
				}
				bulletsfired++;
				if (bulletsfired >= (int)(4f * Settings.GetInstance().DifficultyModifier))
				{
					bulletsfired = 0;
					shoottimer2.Stop();
				}
			}
			if (shoottimer1.Finished)
			{
				shoottimer2.Reset();
				shoottimer2.Start();
			}
			break;
		}
		case EnemyBehaviour.classic:
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
			if (shoottimer2.Finished)
			{
				EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
				evilBullet.Setup(base.Position, MyMath.SnapAngle(MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position), 32));
				collection.Add((GameComponent)(object)evilBullet);
				bulletsfired++;
				if (bulletsfired >= (int)(4f * Settings.GetInstance().DifficultyModifier))
				{
					bulletsfired = 0;
					shoottimer2.Stop();
				}
			}
			if (shoottimer1.Finished)
			{
				shoottimer2.Reset();
				shoottimer2.Start();
			}
			break;
		}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		base.CollidesWith(other);
		if (other is Wall || other is PlayerShip)
		{
			if (justspawned)
			{
				base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f));
				if (isInMaze)
				{
					base.Position = new Vector2(base.Position.X, -45f);
				}
				fadeintimer.Reset();
				justspawned = true;
				hitwall = true;
			}
			else if (other is Wall)
			{
				KilledBy(other, isComboGenerator: false);
			}
		}
		if (other is Explosion)
		{
			KilledBy(other, isComboGenerator: false);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		Die();
		AwardScore(isComboGenerator, other);
		if (hasbonus)
		{
			if (isInMaze)
			{
				bonus.DoNotLimitSpeed();
			}
			else
			{
				bonus.SetSpeedLimit(0.051000003f);
			}
			collection.Add((GameComponent)(object)bonus);
			bonus.Position = base.Position;
			bonus = null;
			hasbonus = false;
		}
		Vector2 v = Vector2.Zero;
		if (other is Wall)
		{
			v = oracle.BackgroundSpeed;
		}
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, (v).Length(), MyMath.VectorToAngle(v));
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}
}
