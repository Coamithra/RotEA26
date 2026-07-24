using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliens;

public class StarMine : KillableAlien
{
	private enum MineState
	{
		free,
		attracted_to_player,
		attracted_to_boss
	}

	private JunkBoss boss;

	private Vector2 prevposition;

	private float r;

	private int hitpointsattached;

	private MineState state;

	private PlayerShip target;

	private float backgroundfactor;

	private bool connectedwithbg;

	private Timer timer = new Timer(1800f, repeating: false);

	private Timer soundtimer = new Timer(300f, repeating: false);

	private SoundEffectInstance sfx;

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public override ICollisionType CollisionType
	{
		get
		{
			c.Position = base.Position;
			c.Radius = r;
			return c;
		}
	}

	public StarMine(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/deathstarsheet2", 4, 8, 1, 25f));
		r = 24f;
		base.DrawOrder = 20;
		base.MaxSpeed = 0.18f;
		base.Deceleration = 6.0000002E-05f;
		SetHitPoints(10, scaleWithDifficulty: true);
		timers.Add(timer);
		timers.Add(soundtimer);
		PointValue = 20f;
	}

	private void connectToBackground()
	{
		connectedwithbg = true;
	}

	private void disconnectFromBackground()
	{
		connectedwithbg = false;
	}

	private void moveWithBackground(GameTime gameTime)
	{
		if (connectedwithbg)
		{
			backgroundfactor = MathHelper.Clamp(backgroundfactor + (float)gameTime.ElapsedGameTime.TotalMilliseconds / 1000f, 0f, 1f);
		}
		else
		{
			backgroundfactor = MathHelper.Clamp(backgroundfactor - (float)gameTime.ElapsedGameTime.TotalMilliseconds / 1000f, 0f, 1f);
		}
		base.Position += MyMath.PowerCurve(0f, 1f, 2f, backgroundfactor) * oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == target)
		{
			target = null;
		}
		if (e.GameComponent == boss)
		{
			boss = null;
			state = MineState.free;
			connectToBackground();
		}
		if (e.GameComponent == this && boss != null)
		{
			boss.RemoveChild();
			boss = null;
		}
	}

	public static StarMine NewStarMine(ComponentBin collection, Game game)
	{
		StarMine starMine = collection.Recycle<StarMine>();
		if (starMine == null)
		{
			starMine = new StarMine(game);
		}
		return starMine;
	}

	public void Setup()
	{
		base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -24f);
		base.Speed = 0f;
		backgroundfactor = 1f;
	}

	internal void SetupLaunch(Vector2 spawnposition, float a)
	{
		base.Position = spawnposition;
		base.Direction = a;
		base.Speed = base.MaxSpeed;
		backgroundfactor = 0f;
	}

	public override void Initialize()
	{
		base.Initialize();
		sfx = null;
		timer.Stop();
		soundtimer.Stop();
		connectedwithbg = true;
		state = MineState.free;
		hitpointsattached = 3;
		prevposition = base.Position;
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		float num = 250f * Settings.GetInstance().DifficultyFactorized(0.5f);
		prevposition = base.Position + oracle.BackgroundSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		switch (state)
		{
		case MineState.free:
		{
			bool flag = false;
			foreach (PlayerShip ship in oracle.GetShips())
			{
				Vector2 val2 = ship.Position - base.Position;
				if ((val2).LengthSquared() <= num * num)
				{
					target = ship;
					flag = true;
				}
			}
			if (flag)
			{
				if (!soundtimer.Active)
				{
					sound.Stop(sfx);
					sfx = sound.Play("targetacquired");
					soundtimer.Start();
				}
				state = MineState.attracted_to_player;
				disconnectFromBackground();
				timer.Duration = 1800f;
				timer.Reset();
				timer.Start();
			}
			break;
		}
		case MineState.attracted_to_player:
		{
			if (target == null)
			{
				state = MineState.free;
				connectToBackground();
				break;
			}
			float num2 = num + num * 0.08f;
			Vector2 val3 = target.Position - base.Position;
			if ((val3).LengthSquared() > 0.25f)
			{
				(val3).Normalize();
			}
			val3 *= 0.00029999999f;
			base.SpeedVector += val3 * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (timer.Finished)
			{
				Asplode();
			}
			Vector2 val4 = target.Position - base.Position;
			if ((val4).LengthSquared() >= num2 * num2)
			{
				state = MineState.free;
				connectToBackground();
			}
			break;
		}
		case MineState.attracted_to_boss:
		{
			if (boss == null)
			{
				state = MineState.free;
				connectToBackground();
				break;
			}
			Vector2 val = boss.Position - base.Position;
			if ((val).LengthSquared() > 0.25f)
			{
				(val).Normalize();
			}
			val *= 0.00029999999f;
			base.SpeedVector += val * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			break;
		}
		}
		Move(gameTime);
		moveWithBackground(gameTime);
		base.Update(gameTime);
		if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= 0.5 * gameTime.ElapsedGameTime.TotalSeconds * (double)Settings.GetInstance().DifficultyModifier)
		{
			Fire();
		}
		if (OffScreen(100f))
		{
			Die();
		}
	}

	private void Asplode()
	{
		if (!base.IsDead)
		{
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 3.5f, 2.5f, 0.03f, base.Direction);
			explosion.MakeBlue();
			collection.Add((GameComponent)(object)explosion);
			explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 2f, 1.3f, 0.06f, base.Direction);
			explosion.MakeBlue();
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl2");
			Die();
		}
	}

	private void Fire()
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
		evilBullet.Setup(base.Position, RandomHelper.RandomNextAngle());
		collection.Add((GameComponent)(object)evilBullet);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Wall)
		{
			base.Position = prevposition;
			if (DetectCollision(other))
			{
				Die();
			}
		}
		if (other is Bullet)
		{
			if (state == MineState.attracted_to_boss)
			{
				hitpointsattached--;
				if (hitpointsattached == 0)
				{
					float angle = MyMath.VectorToAngle(base.Position - boss.GetPosition) + (float)Math.PI / 4f * RandomHelper.RandomNextFloat(-1f, 1f);
					base.SpeedVector = MyMath.AngleToVector(angle) * 10f;
					state = MineState.free;
					boss.RemoveChild();
					boss = null;
					connectToBackground();
					Explosion explosion = Explosion.NewExplosion(collection, base.Game);
					explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
					collection.Add((GameComponent)(object)explosion);
					sound.PlayCue("expl1");
				}
			}
			else
			{
				Vector2 val = base.Position - ((Bullet)other).Position;
				(val).Normalize();
				val *= 0.036000002f;
				base.SpeedVector += val;
			}
		}
		if (state != MineState.attracted_to_boss && other is Explosion)
		{
			Asplode();
		}
		if (state != MineState.attracted_to_boss || (!(other is StarMine) && !(other is JunkBoss)))
		{
			return;
		}
		if (other is StarMine && ((StarMine)other).state == MineState.attracted_to_boss)
		{
			StarMine starMine = (StarMine)other;
			Vector2 val2 = starMine.Position - base.Position;
			float num = (val2).Length();
			if (num < r + starMine.r)
			{
				float num2 = r + starMine.r - num;
				Vector2 val3 = val2;
				(val3).Normalize();
				float num3 = scale / (starMine.scale + scale);
				base.Position -= val3 * num2 * (1f - num3);
				starMine.Position += val3 * num2 * num3;
			}
		}
		if (other is JunkBoss)
		{
			JunkBoss junkBoss = (JunkBoss)other;
			Vector2 val4 = junkBoss.GetPosition - base.Position;
			float num4 = (val4).Length();
			if (num4 < r + junkBoss.r)
			{
				_ = junkBoss.r;
				Vector2 val5 = val4;
				(val5).Normalize();
				// Fidelity (review M4): the spatial grid fires each circle pair once per direction
				// per frame; the shipped 2008 build's all-pairs scan fired this ungated 1px push-out
				// twice per frame — the x2 preserves the original net separation rate so
				// attached StarMines don't sink deeper into the JunkBoss.
				base.Position -= val5 * 2f;
			}
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		AwardScore(isComboGenerator, other);
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
		Die();
	}

	internal void AttractByBoss(JunkBoss junkBoss)
	{
		if (hitpointsattached > 0)
		{
			if (boss == null)
			{
				junkBoss.AddChild();
			}
			state = MineState.attracted_to_boss;
			boss = junkBoss;
			disconnectFromBackground();
		}
	}
}
