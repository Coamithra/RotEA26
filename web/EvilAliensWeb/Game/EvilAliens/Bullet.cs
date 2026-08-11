using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Bullet : AlienDrawableGameComponent, IAlienKiller
{
	private int player;

	private int bouncing;

	private int split;

	private bool firsthit;

	private bool asploding;

	private float asplodingsize;

	private Vector2 TopLeft;

	private Vector2 BottomRight;

	private Vector2 prevpos;

	private Timer bouncedTimer = new Timer(40f, repeating: false);

	private DeathEvent deathEvent;

	private float lifetime;

	private bool bounceTimerInitiallyEnabled;

	private bool isCloned;

	private CollisionLine collisionLine = new CollisionLine(Vector2.Zero, Vector2.One);

	private CollisionLine tmp = new CollisionLine(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType
	{
		get
		{
			collisionLine.Set(prevpos, base.Position + base.DirectionalVector * 20f);
			return collisionLine;
		}
	}

	public Bullet(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/bulletgood"));
		base.DrawOrder = 800;
		timers.Add(bouncedTimer);
		deathEvent = Bullet_OnDeath;
	}

	public static Bullet NewBullet(ComponentBin collection, Game game)
	{
		Bullet bullet = collection.Recycle<Bullet>();
		if (bullet == null)
		{
			bullet = new Bullet(game);
		}
		return bullet;
	}

	public void Setup(Vector2 position, float direction, float lifetime, int player)
	{
		this.player = player;
		asploding = false;
		bouncing = 0;
		split = 0;
		this.lifetime = lifetime;
		prevpos = position;
		base.Position = position;
		base.Direction = direction;
		bounceTimerInitiallyEnabled = false;
		isCloned = false;
	}

	public override void Initialize()
	{
		color = Color.White;
		base.Speed = 0.78f;
		base.MaxSpeed = 0.78f;
		base.Initialize();
		if (bounceTimerInitiallyEnabled)
		{
			bouncedTimer.Start();
			bouncedTimer.Reset();
			base.Collides = false;
		}
		else
		{
			bouncedTimer.Stop();
			base.Collides = true;
		}
		CollisionBox collisionBox = retrieveBoundsFromTexture();
		TopLeft = collisionBox.TopLeft;
		BottomRight = collisionBox.BottomRight;
		base.OnDeath += deathEvent;
		firsthit = !isCloned;
	}

	private void Bullet_OnDeath(object sender)
	{
		if (asploding)
		{
			Blast blast = Blast.NewBlast(collection, base.Game);
			blast.SetupAsMini(base.Position, asplodingsize, player);
			collection.Add((GameComponent)(object)blast);
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public bool OffScreen()
	{
		return (base.Position.X < -200f) | (base.Position.X > 1000f) | (base.Position.Y < -200f) | (base.Position.Y > 800f);
	}

	public override void Update(GameTime gameTime)
	{
		lifetime -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (lifetime < 100f)
		{
			color = new Color(new Vector4(1f, 1f, 1f, lifetime / 100f));
		}
		if (lifetime < 0f)
		{
			color = new Color(new Vector4(1f, 1f, 1f, 0f));
		}
		if (OffScreen() | (lifetime <= 0f))
		{
			collection.Remove((GameComponent)(object)this);
		}
		if (bouncedTimer.Finished)
		{
			base.Collides = true;
		}
		prevpos = base.Position;
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		bool flag = true;
		// Asteroids + loose (unconnected) Balls don't sustain a combo. (The helper mothership used to be
		// excluded here too, as an immortal fake-damage target -- it's now killable, so it keeps the combo
		// alive like any other alien.)
		if (other is Asteroid || (other is Ball && !((Ball)other).IsConnected()))
		{
			flag = false;
		}
		if ((other is UFO || other is Boss || other is Braineroid || other is Asteroid || other is Ball || other is JunkBoss || other is EvilSkull || other is DeathStar || other is ClassicBoss || other is BattleSkull || other is Spider || other is StationaryBoss || other is MarsBoss || other is StarMine || other is BrainBoss || other is FlyingSpider || other is FakeBoss || other is SweepUFO || other is ParatrooperAlien || other is Parachute || other is ParatrooperBrain || other is PunchingBag || other is SpiderHelperMothership) && !bouncedTimer.Active)
		{
			if (firsthit && flag)
			{
				Score.SustainCombo(player, base.Position);
				firsthit = false;
			}
			if (other is Braineroid || other is BrainBoss)
			{
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
				bloodExplosion.Setup(base.Position, 1.5f, 0.3f, base.Speed * 0.2f, base.Direction + (float)Math.PI);
				collection.Add((GameComponent)(object)bloodExplosion);
			}
			if (bouncing < 1)
			{
				Die();
			}
			else
			{
				if (asploding)
				{
					Blast blast = Blast.NewBlast(collection, base.Game);
					blast.SetupAsMini(base.Position + base.DirectionalVector * 20f, asplodingsize, player);
					collection.Add((GameComponent)(object)blast);
					asploding = false;
				}
				base.Direction += RandomHelper.RandomNextAngle();
				bouncing--;
				bouncedTimer.Start();
				bouncedTimer.Reset();
				base.Collides = false;
				createClone(resetBounceTimer: true);
			}
		}
		if ((other is Floorbottom) & (base.DirectionalVector.Y > 0f))
		{
			if (bouncing < 1)
			{
				Die();
			}
			else
			{
				bouncing--;
				base.DirectionalVector = new Vector2(base.DirectionalVector.X, 0f - base.DirectionalVector.Y);
				createClone(resetBounceTimer: false);
			}
		}
		if ((other is Wall) & !bouncedTimer.Active)
		{
			if (bouncing < 1)
			{
				Die();
			}
			else
			{
				base.DirectionalVector = new Vector2(0f - base.DirectionalVector.X, base.DirectionalVector.Y);
				tmp.Set(base.Position, base.Position + base.DirectionalVector * 20f);
				if (((Wall)other).GetCollisionType().TestCollision(tmp))
				{
					base.DirectionalVector = new Vector2(0f - base.DirectionalVector.X, 0f - base.DirectionalVector.Y);
					if (((Wall)other).GetCollisionType().TestCollision(tmp))
					{
						base.DirectionalVector = new Vector2(0f - base.DirectionalVector.X, base.DirectionalVector.Y);
						if (((Wall)other).GetCollisionType().TestCollision(tmp))
						{
							Die();
						}
					}
				}
				bouncedTimer.Start();
				bouncedTimer.Reset();
				bouncing--;
				createClone(resetBounceTimer: true);
			}
		}
		if (other is SpiderBoss)
		{
			base.Direction = MyMath.VectorToAngle(base.Position - ((SpiderBoss)other).Position);
		}
		base.CollidesWith(other);
	}

	private void createClone(bool resetBounceTimer)
	{
		if (split > 0)
		{
			split--;
			float num = RandomHelper.RandomNextFloat((float)Math.PI / 32f, (float)Math.PI / 4f);
			base.Direction += num;
			Bullet bullet = NewBullet(collection, base.Game);
			bullet.Setup(base.Position, base.Direction - num * 2f, lifetime, player);
			bullet.SetSplit(split);
			bullet.SetBouncing(bouncing);
			bullet.isCloned = true;
			if (resetBounceTimer)
			{
				bullet.SetBounceTimerInitiallyEnabled();
			}
			collection.Add((GameComponent)(object)bullet);
		}
	}

	private void SetBounceTimerInitiallyEnabled()
	{
		bounceTimerInitiallyEnabled = true;
	}

	public Vector2 GetSpeed()
	{
		return base.SpeedVector;
	}

	public void SetBouncing(int times)
	{
		bouncing = times;
	}

	public void SetAsploding(float size)
	{
		asploding = true;
		asplodingsize = size;
	}

	// Read seams for eaNetFire()'s roll legs (card 950bb70a): the LIVE flags at this instant,
	// not a record of the roll -- `asploding` is spent at the first bounce-blast and `bouncing`
	// counts DOWN, so both only equal the rolled outcome when read straight after spawn, which
	// is how the suite reads them. Neither is visible in any frame until the bullet dies, which
	// is exactly why the sync defect read as "seems random".
	internal bool NetAsploding => asploding;

	internal bool NetBouncing => bouncing > 0;

	public bool CausesCombo()
	{
		return true;
	}

	public bool CanHitBosses()
	{
		return true;
	}

	public int Player()
	{
		return player;
	}

	internal void ClampAngle(float p, float p_2)
	{
		base.Direction = MyMath.Mod(base.Direction, (float)Math.PI * 2f);
		float num = MyMath.Mod(p, (float)Math.PI * 2f);
		float num2 = MyMath.Mod(p_2, (float)Math.PI * 2f);
		if (base.Direction < num || base.Direction > num2)
		{
			if (Math.Abs(MyMath.DifferenceMod(base.Direction, num, (float)Math.PI * 2f)) < Math.Abs(MyMath.DifferenceMod(base.Direction, num2, (float)Math.PI * 2f)))
			{
				base.Direction = num;
			}
			else
			{
				base.Direction = num2;
			}
		}
	}

	internal void SetSplit(int bulletsSplit)
	{
		split = bulletsSplit;
	}
}
