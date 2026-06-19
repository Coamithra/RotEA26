using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class JunkBoss : KillableAlien
{
	private enum JunkBossState
	{
		enter,
		summonmeteors,
		attracting,
		normal,
		naked,
		asplode
	}

	private const float explosionduration = 125f;

	private const int thresholdinitially = 5;

	private const int hitpointsinitially = 150;

	private const float lazertimeduration = 2500f;

	private const float shoottime = 1100f;

	private bool isbase;

	private JunkBossState state;

	public float r;

	private float targetdir;

	private Timer lazertimer;

	private Timer shoottimer;

	private Timer generictimer;

	private Timer sucktimer;

	private int explosions;

	private int threshold;

	private bool retaliate;

	private bool dangermessage;

	private float ydrawingoffset;

	private int children;

	private LazerGenerator suckeffect;

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public Vector2 GetPosition => base.Position;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			c.Position = base.Position;
			c.Radius = r;
			return c;
		}
	}

	public JunkBoss(Game game)
		: base(game)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		LoadAnimation(new AnimationData("GFX/Sprites/eye"));
		((DrawableGameComponent)this).DrawOrder = 20;
		lazertimer = new Timer(2500f, repeating: true);
		shoottimer = new Timer(1100f, repeating: false);
		generictimer = new Timer(0f, repeating: false);
		sucktimer = new Timer(5000f, repeating: false);
		timers.Add(lazertimer);
		timers.Add(shoottimer);
		timers.Add(generictimer);
		timers.Add(sucktimer);
		base.IsBoss = true;
		base.Colorize = true;
		PointValue = 2000f;
		SetHitPoints(150, scaleWithDifficulty: false);
	}

	private void Components_ComponentRemoved(object sender, GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this && suckeffect != null)
		{
			suckeffect = null;
		}
	}

	public static JunkBoss NewJunkBoss(ComponentBin collection, Game game)
	{
		JunkBoss junkBoss = collection.Recycle<JunkBoss>();
		if (junkBoss == null)
		{
			junkBoss = new JunkBoss(game);
		}
		return junkBoss;
	}

	public void AddChild()
	{
		children++;
	}

	public void RemoveChild()
	{
		children--;
	}

	public void Setup(bool isbase)
	{
		this.isbase = isbase;
	}

	public override void Initialize()
	{
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		if (!isbase)
		{
			GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					SignedInGamer current = enumerator.Current;
					current.Presence.PresenceMode = (GamerPresenceMode)34;
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
		}
		scale = 0.13f;
		r = 1f * scale * (float)(texture.Width / 2);
		base.Initialize();
		children = 0;
		lazertimer.Start();
		shoottimer.Start();
		state = JunkBossState.enter;
		generictimer.Duration = 7000f / Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.Players);
		generictimer.Reset();
		generictimer.Start();
		sucktimer.Stop();
		color = Color.White;
		base.Position = new Vector2(400f, (0f - r) * 2f);
		base.Direction = 0f;
		targetdir = base.Direction;
		base.MaxSpeed = 0.042f;
		base.Acceleration = 3.0000001E-05f;
		base.Deceleration = 1.19999995E-05f;
		base.Speed = 0f;
		threshold = 5;
		retaliate = false;
		suckeffect = null;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		base.Position += new Vector2(0f, ydrawingoffset);
		base.Draw(gameTime);
		base.Position -= new Vector2(0f, ydrawingoffset);
		if (suckeffect != null)
		{
			((DrawableGameComponent)suckeffect).Draw(gameTime);
		}
	}

	private void SwapDir()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		float num = 150f;
		if (base.Position.X > 800f - num)
		{
			targetdir = (float)Math.PI;
		}
		if (base.Position.X < num)
		{
			targetdir = 0f;
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0238: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0256: Unknown result type (might be due to invalid IL or missing references)
		//IL_027d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0406: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_0479: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		ydrawingoffset = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6.0) * 3f;
		if (!shoottimer.Active & retaliate)
		{
			int num = (int)(10f * Settings.GetInstance().DifficultyModifier);
			float num2 = RandomHelper.RandomNextFloat(0f, 1f);
			for (int i = 0; i < num; i++)
			{
				float direction = (float)i * ((float)Math.PI * 2f) / (float)num + num2;
				EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, ((GameComponent)this).Game);
				evilBullet.Setup(base.Position, direction);
				collection.Add((GameComponent)(object)evilBullet);
			}
			retaliate = false;
			shoottimer.Duration = 1100f / Settings.GetInstance().DifficultyModifier;
			shoottimer.Reset();
			shoottimer.Start();
		}
		switch (state)
		{
		case JunkBossState.asplode:
			if (generictimer.Finished)
			{
				generictimer.Duration = 125f * RandomHelper.RandomNextFloat(0.8f, 1.2f);
				generictimer.Reset();
				generictimer.Start();
				Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion.Setup(base.Position, 1.5f * RandomHelper.RandomNextFloat(0.8f, 1.2f), 2f, 0.13f, RandomHelper.RandomNextAngle());
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl1");
				explosions++;
				if (explosions == 25)
				{
					explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
					explosion.Setup(base.Position, 6f, 3.3f, 0f, 0f);
					collection.Add((GameComponent)(object)explosion);
					sound.PlayCue("expl2");
					AwardScoreToAll(combo: true);
					Die();
				}
			}
			break;
		case JunkBossState.enter:
			if (base.Position.Y < 100f)
			{
				base.Position = new Vector2(base.Position.X, base.Position.Y + (float)gameTime.ElapsedGameTime.TotalMilliseconds * 1.25f / 16.666666f);
			}
			else
			{
				base.Position = new Vector2(base.Position.X, 100f);
			}
			if (generictimer.Finished)
			{
				dangermessage = false;
				state = JunkBossState.summonmeteors;
				generictimer.Duration = 2000f;
				generictimer.Reset();
				generictimer.Start();
			}
			break;
		case JunkBossState.summonmeteors:
		{
			Move(gameTime);
			if (!dangermessage && !isbase)
			{
				AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collection, ((GameComponent)this).Game);
				animatedMessage.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				animatedMessage.SetWarningDirection(4.712389f);
				collection.Add((GameComponent)(object)animatedMessage);
				dangermessage = true;
			}
			if (!generictimer.Finished)
			{
				break;
			}
			for (int j = 0; j < (int)(30f * Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.Players)); j++)
			{
				if (!isbase)
				{
					Ball ball = Ball.NewBall(collection, ((GameComponent)this).Game);
					ball.Setup(this);
					collection.Add((GameComponent)(object)ball);
				}
			}
			generictimer.Duration = 13000f;
			sucktimer.Reset();
			sucktimer.Start();
			generictimer.Reset();
			generictimer.Start();
			state = JunkBossState.attracting;
			break;
		}
		case JunkBossState.attracting:
			Move(gameTime);
			if (suckeffect != null)
			{
				suckeffect.SetPosition(base.Position);
				foreach (StarMine starMine in oracle.GetStarMines())
				{
					starMine.AttractByBoss(this);
				}
			}
			if (sucktimer.Finished)
			{
				suckeffect = LazerGenerator.NewLazerGenerator(collection, ((GameComponent)this).Game);
				suckeffect.Setup(base.Position, 4f, 0.5f, 0f, 0f);
				collection.Add((GameComponent)(object)suckeffect);
				sucktimer.Reset();
			}
			SwapDir();
			if (generictimer.Finished)
			{
				state = JunkBossState.normal;
				collection.Remove((GameComponent)(object)suckeffect);
				suckeffect = null;
				threshold = 5;
			}
			break;
		case JunkBossState.naked:
			FireLazer();
			Move((float?)targetdir, gameTime);
			SwapDir();
			if (generictimer.Finished)
			{
				dangermessage = false;
				state = JunkBossState.summonmeteors;
				generictimer.Duration = 2000f;
				generictimer.Reset();
				generictimer.Start();
			}
			break;
		case JunkBossState.normal:
			if ((children == 0) | ((children <= 5) & (threshold <= 0)))
			{
				state = JunkBossState.naked;
				generictimer.Duration = 10000f / Settings.GetInstance().MultiPlayerDifficultyModifier(oracle.Players);
				generictimer.Reset();
				generictimer.Start();
			}
			FireLazer();
			Move((float?)targetdir, gameTime);
			SwapDir();
			break;
		}
	}

	private void FireLazer()
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		if (lazertimer.Finished)
		{
			Lazer lazer = Lazer.NewLazer(collection, ((GameComponent)this).Game);
			lazer.SetupSingleShot(base.Position, MyMath.SnapAngle(MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position), 16), 0f);
			collection.Add((GameComponent)(object)lazer);
			lazertimer.Duration = 2500f / Settings.GetInstance().DifficultyModifier;
			lazertimer.Reset();
		}
	}

	protected override void HitBy(ICollidable other, bool isComboGenerator)
	{
		if (state != JunkBossState.asplode)
		{
			base.HitBy(other, isComboGenerator);
			threshold--;
			if (!shoottimer.Active)
			{
				retaliate = true;
			}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		state = JunkBossState.asplode;
		base.Speed = 0f;
		explosions = 0;
		generictimer.Duration = 125f;
		generictimer.Start();
		generictimer.Reset();
	}
}
