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

	public SweepUFO(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/mediumship", 8, 4, 1, 25f));
		scale = 1f;
		PointValue = 500f;
		SetHitPoints(11, scaleWithDifficulty: false);
		((DrawableGameComponent)this).DrawOrder = 18;
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
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_021f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0229: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
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
				g = LazerGenerator.NewLazerGenerator(collection, ((GameComponent)this).Game);
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
				l = Lazer.NewLazer(collection, ((GameComponent)this).Game);
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		Vector2 targetPosition = GetTargetPosition();
		Vector2 val = targetPosition - base.Position;
		((Vector2)(ref val)).Normalize();
		return base.Position + val * 75f;
	}

	private Vector2 GetTargetPosition()
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		Vector2 position = default(Vector2);
		if (targetplayer)
		{
			if (target != null)
			{
				position = target.Position;
				return position;
			}
			((Vector2)(ref position))._002Ector(400f, 300f);
		}
		else
		{
			((Vector2)(ref position))._002Ector(base.Position.X + 100f, base.Position.Y);
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
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 3.5f, 2.5f, base.Speed * 0.3f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.95f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		if (!(other is FlyingSpider))
		{
			AwardScore(isComboGenerator, other);
		}
	}
}
