using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class MarsBoss : KillableAlien
{
	private enum BossState
	{
		entry,
		hover,
		charge,
		asplode
	}

	public enum BossPosition
	{
		left,
		right
	}

	private const float hoverHeight = 70f;

	private const int initialhitpoints = 150;

	private const float startFPS = 16f;

	private const float endFPS = 32f;

	private BossState state;

	private BossPosition bossPosition;

	private LazerGenerator lazerGenerator;

	private Lazer lazer;

	private Timer entryTimer = new Timer(1200f, repeating: false);

	private Timer stateTimer = new Timer(5000f, repeating: false);

	private PlayerShip target;

	private bool issurvivor;

	public DeathEvent OnAlmostKilled;

	private Texture2D firstHalfOfSpritesheet;

	private Texture2D secondHalfOfSpritesheet;

	private Texture2D blank;

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.Width *= 0.90999997f;
			collisionBox.Height *= 0.48999998f;
			collisionBox.CenterAround(base.Position - new Vector2(10f * scale, 0f));
			return collisionBox;
		}
	}

	public MarsBoss(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/mothershipB", 4, 4, 1, 16f));
		scale = 1f;
		AddTimer(entryTimer);
		AddTimer(stateTimer);
		SetHitPoints(150, scaleWithDifficulty: false);
		PointValue = 2000f;
		base.Colorize = true;
		base.IsBoss = true;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == target)
		{
			target = null;
		}
		if (e.GameComponent == this)
		{
			if (lazer != null)
			{
				lazer.Free();
				lazer = null;
				OnAlmostKilled = null;
			}
			if (lazerGenerator != null)
			{
				lazerGenerator.Free();
				lazerGenerator = null;
			}
		}
		if (e.GameComponent is MarsBoss)
		{
			issurvivor = true;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blank = content.Load<Texture2D>("GFX/Game/blank");
		firstHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipA");
		secondHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipB");
	}

	public static MarsBoss NewMarsBoss(ComponentBin collection, Game game)
	{
		MarsBoss marsBoss = collection.Recycle<MarsBoss>();
		if (marsBoss == null)
		{
			marsBoss = new MarsBoss(game);
		}
		return marsBoss;
	}

	public void Setup(BossPosition position)
	{
		bossPosition = position;
	}

	public override void Initialize()
	{
		base.Initialize();
		fps = 16f;
		interpolationOptions = InterpolationOptions.never;
		base.DrawOrder = 50;
		lazer = null;
		lazerGenerator = null;
		state = BossState.entry;
		base.Position = new Vector2(-500f, 70f);
		entryTimer.Start();
		issurvivor = false;
		base.Collides = true;
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		if (lazerGenerator != null)
		{
			((DrawableGameComponent)lazerGenerator).Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		fps = MathHelper.Lerp(32f, 16f, base.HitPointsNormalized);
		float num = curframe;
		base.Update(gameTime);
		if (curframe < num)
		{
			if (texture == firstHalfOfSpritesheet)
			{
				texture = secondHalfOfSpritesheet;
			}
			else
			{
				texture = firstHalfOfSpritesheet;
			}
		}
		switch (state)
		{
		case BossState.entry:
			if (entryTimer.Active)
			{
				float value = 0f;
				if (bossPosition == BossPosition.left)
				{
					value = 200f;
				}
				if (bossPosition == BossPosition.right)
				{
					value = 600f;
				}
				float num3 = MyMath.PowerCurve(-500f, value, 0.5f, 1f - entryTimer.Normalized);
				base.Position = new Vector2(num3, 70f);
			}
			else
			{
				state = BossState.hover;
				stateTimer.Duration = MathHelper.Lerp(1000f, 1500f, (float)base.HitPoints / 150f);
				stateTimer.Reset();
				stateTimer.Start();
			}
			break;
		case BossState.hover:
			if (stateTimer.Finished)
			{
				CreateGenerator();
				AimGenerator(100f);
				state = BossState.charge;
				if (lazer != null)
				{
					lazer.Free();
					lazer = null;
				}
				stateTimer.Duration = 2500f;
				stateTimer.Start();
				stateTimer.Reset();
			}
			break;
		case BossState.charge:
		{
			Vector2 val = ((target == null) ? (new Vector2(400f, 300f) - base.Position) : (target.GetPosition() - base.Position));
			(val).Normalize();
			lazerGenerator.SetPosition(base.Position + val * 100f);
			if (stateTimer.Finished)
			{
				state = BossState.hover;
				stateTimer.Duration = MathHelper.Lerp(1000f, 6000f, (float)base.HitPoints / 150f);
				stateTimer.Reset();
				stateTimer.Start();
				if (lazer != null)
				{
					lazer.Free();
					lazer = null;
				}
				if (lazerGenerator != null)
				{
					collection.Remove((GameComponent)(object)lazerGenerator);
					lazerGenerator = null;
				}
				lazer = Lazer.NewLazer(collection, base.Game);
				lazer.Setup(base.Position + val * 100f, MyMath.VectorToAngle(val), this, 0f);
				collection.Add((GameComponent)(object)lazer);
			}
			break;
		}
		case BossState.asplode:
		{
			if (RandomHelper.RandomFromAverage(5f, gameTime))
			{
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				Vector2 v = oracle.BackgroundSpeed + new Vector2(0f, -0.48f);
				explosion.Setup(base.Position + new Vector2(RandomHelper.RandomNextFloat(-200f, 200f), RandomHelper.RandomNextFloat(0f, 150f)), 1f, 1f, (v).Length(), MyMath.VectorToAngle(v));
				sound.PlayCue("expl1");
				collection.Add((GameComponent)(object)explosion);
			}
			if (RandomHelper.RandomFromAverage(0.3f, gameTime))
			{
				MiniExplosion();
			}
			float num2 = MyMath.PowerCurve(0f, 1f, 4f, 1f - stateTimer.Normalized);
			base.Position = new Vector2(base.Position.X - 0.1f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f, MathHelper.Lerp(70f, 470f, num2));
			if (stateTimer.Finished)
			{
				Explode();
			}
			break;
		}
		}
	}

	private Vector2 AimGenerator(float lazeroffset)
	{
		target = oracle.GetRandomPlayerShip();
		Vector2 val = ((target == null) ? (new Vector2(400f, 300f) - base.Position) : (target.GetPosition() - base.Position));
		(val).Normalize();
		lazerGenerator.SetPosition(base.Position + val * lazeroffset);
		return val;
	}

	private void CreateGenerator()
	{
		stateTimer.Duration = MathHelper.Lerp(1000f, 6000f, (float)base.HitPoints / 150f);
		stateTimer.Reset();
		stateTimer.Start();
		lazerGenerator = LazerGenerator.NewLazerGenerator(collection, base.Game);
		lazerGenerator.Setup(base.Position, 2f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)lazerGenerator);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		if (OnAlmostKilled != null)
		{
			OnAlmostKilled(this);
		}
		if (!issurvivor)
		{
			Explode();
			return;
		}
		base.Collides = false;
		state = BossState.asplode;
		stateTimer.Duration = 5000f;
		stateTimer.Reset();
		stateTimer.Start();
		MiniExplosion();
		base.DrawOrder = 20;
		if (lazerGenerator != null)
		{
			collection.Remove((GameComponent)(object)lazerGenerator);
			lazerGenerator = null;
		}
		if (lazer != null)
		{
			lazer.Free();
			lazer = null;
		}
	}

	private void MiniExplosion()
	{
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.9f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 4f, 2.3f, base.Speed * 0.5f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	private void Explode()
	{
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.9f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 4f, 2.3f, base.Speed * 0.5f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 6f, 5.3f, base.Speed * 0.1f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		AwardScoreToAll(combo: false);
	}

	// ---- Online co-op replication seams (Compat/Net, card 11.2) --------------------------
	// The entry BossPosition only steers the Update-only entry path (inert on a frozen puppet)
	// but is replicated for a faithful reconstruction. The 4x4 sheet ALTERNATES between the
	// mothershipA/mothershipB halves each animation wrap in Update; that A/B choice is the one
	// bit of Draw state the base fields (curframe/Hp) don't carry, so it is streamed.

	// The 4x4 loop IS free-running, but its RATE is not: Update re-derives
	// `fps = Lerp(32, 16, HitPointsNormalized)` every tick, so a wounded boss flaps up to twice as
	// fast. A puppet's Update never runs, so it would free-run forever at Initialize's 16 and drift
	// away from the host -- and drag the wrap-driven A/B sheet half out of step with the replicated
	// one. Keep taking the replicated frame. (SpiderHelperMothership is the near-identical type
	// that DOES qualify: its fps is a constant 16 set once in Initialize.)
	internal override bool NetFrameLocal => false;

	internal byte NetBossPosition => (byte)bossPosition;

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

	// The charge-up `lazerGenerator` energy well is a child the host draws by hand (see Draw). On a
	// JOIN peer this puppet is frozen, so the descriptor replicates the charge state and
	// NetDriveExtras rebuilds a local silent copy into the same `lazerGenerator` field (Draw + the
	// OnComponentRemoved Free() then cover it unchanged). See Compat/Net/NetChargeGlow.
	private bool netCharging;

	private Vector2 netChargeOffset;

	private float netChargeWindup = 2.5f;

	private float netChargeSize = 2f;

	internal bool NetCharging => lazerGenerator != null;

	internal Vector2 NetChargeOffset => lazerGenerator != null ? lazerGenerator.Position - base.Position : Vector2.Zero;

	internal float NetChargeWindup => lazerGenerator != null ? lazerGenerator.NetWindupSeconds : 2.5f;

	internal float NetChargeSize => lazerGenerator != null ? lazerGenerator.NetSize : 2f;

	internal void NetApplyCharge(bool charging, Vector2 offset, float windup, float size)
	{
		netCharging = charging;
		netChargeOffset = offset;
		netChargeWindup = windup;
		netChargeSize = size;
	}

	internal override void NetDriveExtras(GameTime gameTime)
	{
		EvilAliensWeb.Compat.Net.NetChargeGlow.Drive(ref lazerGenerator, netCharging, netChargeOffset, netChargeWindup, netChargeSize, collection, base.Game, base.Position);
	}
}
