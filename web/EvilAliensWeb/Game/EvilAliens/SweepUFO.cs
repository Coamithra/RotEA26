using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class SweepUFO : KillableAlien
{
	private enum SweepState
	{
		entry,
		charge,
		fire,
		exit
	}

	private const float lazeroffset = 75f;

	private int spiderHP;

	private bool targetplayer;

	private Timer stateTimer = new Timer(1f, repeating: false);

	private LazerGenerator g;

	private Lazer l;

	private PlayerShip target;

	private SweepState state;

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

	public SweepUFO(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/mediumship", 8, 4, 1, 25f));
		scale = 1f;
		PointValue = 500f;
		SetHitPoints(11, scaleWithDifficulty: false);
		base.DrawOrder = 18;
		timers.Add(stateTimer);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			if (g != null)
			{
				g.Free();
				g = null;
			}
			if (l != null)
			{
				l.Free();
				l = null;
			}
		}
		if (e.GameComponent == target)
		{
			FindTarget();
		}
	}

	private void FindTarget()
	{
		target = oracle.GetRandomPlayerShip();
	}

	public static SweepUFO NewSweepUFO(ComponentBin collection, Game game)
	{
		SweepUFO sweepUFO = collection.Recycle<SweepUFO>();
		if (sweepUFO == null)
		{
			sweepUFO = new SweepUFO(game);
		}
		return sweepUFO;
	}

	public void Setup(bool targetplayer, int number, int total)
	{
		this.targetplayer = targetplayer;
		float num = 520f / (float)(total - 1);
		base.Position = new Vector2(-100f, (float)number * num);
	}

	public override void Initialize()
	{
		spiderHP = Math.Max((int)(3f * Settings.GetInstance().DifficultyModifier), 1);
		stateTimer.Duration = 700f;
		base.Initialize();
		stateTimer.Start();
		state = SweepState.entry;
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		if (g != null)
		{
			((DrawableGameComponent)g).Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		switch (state)
		{
		case SweepState.entry:
		{
			float num = MathHelper.SmoothStep(80f, -100f, stateTimer.Normalized);
			base.Position = new Vector2(num, base.Position.Y);
			if (stateTimer.Finished)
			{
				base.Position = new Vector2(80f, base.Position.Y);
				stateTimer.Duration = 2000f;
				stateTimer.Reset();
				stateTimer.Start();
				state = SweepState.charge;
				FindTarget();
				g = LazerGenerator.NewLazerGenerator(collection, base.Game);
				g.Setup(GetLazerSpawnSpot(), 1f, 1f, 0f, 0f);
				collection.Add((GameComponent)(object)g);
			}
			break;
		}
		case SweepState.charge:
			g.SetPosition(GetLazerSpawnSpot());
			if (stateTimer.Finished)
			{
				stateTimer.Duration = 2000f;
				stateTimer.Reset();
				stateTimer.Start();
				g.Free();
				g = null;
				state = SweepState.fire;
				l = Lazer.NewLazer(collection, base.Game);
				float direction = MyMath.VectorToAngle(GetTargetPosition() - GetLazerSpawnSpot());
				l.Setup(GetLazerSpawnSpot(), direction, this, 10f);
				collection.Add((GameComponent)(object)l);
			}
			break;
		case SweepState.fire:
			if (stateTimer.Finished)
			{
				l.Free();
				stateTimer.Duration = 700f;
				stateTimer.Reset();
				stateTimer.Start();
				state = SweepState.exit;
			}
			break;
		case SweepState.exit:
		{
			float num = MathHelper.SmoothStep(-100f, 80f, stateTimer.Normalized);
			base.Position = new Vector2(num, base.Position.Y);
			if (stateTimer.Finished)
			{
				Die();
			}
			break;
		}
		}
	}

	private Vector2 GetLazerSpawnSpot()
	{
		Vector2 targetPosition = GetTargetPosition();
		Vector2 val = targetPosition - base.Position;
		(val).Normalize();
		return base.Position + val * 75f;
	}

	private Vector2 GetTargetPosition()
	{
		Vector2 position = default(Vector2);
		if (targetplayer)
		{
			if (target != null)
			{
				position = target.Position;
				return position;
			}
			(position) = new Vector2(400f, 300f);
		}
		else
		{
			(position) = new Vector2(base.Position.X + 100f, base.Position.Y);
		}
		return position;
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (!(other is FlyingSpider))
		{
			return;
		}
		HitBy(other, isComboGenerator: false);
		if (state != 0)
		{
			spiderHP--;
			if (spiderHP <= 0)
			{
				KilledBy(other, isComboGenerator: false);
			}
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 3.5f, 2.5f, base.Speed * 0.3f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.95f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		if (!(other is FlyingSpider))
		{
			AwardScore(isComboGenerator, other);
		}
	}

	// ---- Online co-op replication seams (Compat/Net, coverage-gaps follow-up) ----------------
	// The charge swarm `g` is a child LazerGenerator the host draws by hand (see Draw). On a JOIN
	// peer this puppet is frozen, so it never spawns `g`; the descriptor replicates the charge state
	// and NetDriveExtras rebuilds a local silent copy into the same `g` field (so Draw + the
	// OnComponentRemoved Free() cover it unchanged). See Compat/Net/NetChargeGlow.
	private bool netCharging;

	private Vector2 netChargeOffset;

	private float netChargeWindup = 2.5f;

	private float netChargeSize = 1f;

	// This emitter instance's own eased copy of the replicated aim (card eb057163). The wire value
	// only changes on this entity's snapshot turn, so the glow SWEEPS toward it instead of stepping;
	// it lives here rather than in NetChargeGlow because the child is pooled and the emitter is
	// what persists across a charge. Host-side it is never read (Drive is client-only).
	private EvilAliensWeb.Compat.Net.NetChargeGlow.AimEase netChargeAim;

	// Host encode: read live off the real generator (non-null only during the charge state).
	internal bool NetCharging => g != null;

	internal Vector2 NetChargeOffset => g != null ? g.Position - base.Position : Vector2.Zero;

	internal float NetChargeWindup => g != null ? g.NetWindupSeconds : 2.5f;

	internal float NetChargeSize => g != null ? g.NetSize : 1f;

	// Client apply: record the replicated charge state (draw-relevant only; the child spawn happens
	// in NetDriveExtras, never here -- the descriptor contract forbids spawning from ApplyStateExtra).
	internal void NetApplyCharge(bool charging, Vector2 offset, float windup, float size)
	{
		netCharging = charging;
		netChargeOffset = offset;
		netChargeWindup = windup;
		netChargeSize = size;
	}

	internal override void NetDriveExtras(GameTime gameTime)
	{
		EvilAliensWeb.Compat.Net.NetChargeGlow.Drive(ref g, ref netChargeAim, netCharging,
			netChargeOffset, netChargeWindup, netChargeSize, 1f, collection, base.Game,
			base.Position, (float)gameTime.ElapsedGameTime.TotalMilliseconds);
	}
}
