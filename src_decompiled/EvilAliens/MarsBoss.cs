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
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		fps = 16f;
		interpolationOptions = InterpolationOptions.never;
		((DrawableGameComponent)this).DrawOrder = 50;
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
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_030d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		//IL_0317: Unknown result type (might be due to invalid IL or missing references)
		//IL_031c: Unknown result type (might be due to invalid IL or missing references)
		//IL_033f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0344: Unknown result type (might be due to invalid IL or missing references)
		//IL_035a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_029d: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b2: Unknown result type (might be due to invalid IL or missing references)
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
			((Vector2)(ref val)).Normalize();
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
				lazer = Lazer.NewLazer(collection, ((GameComponent)this).Game);
				lazer.Setup(base.Position + val * 100f, MyMath.VectorToAngle(val), this, 0f);
				collection.Add((GameComponent)(object)lazer);
			}
			break;
		}
		case BossState.asplode:
		{
			if (RandomHelper.RandomFromAverage(5f, gameTime))
			{
				Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				Vector2 v = oracle.BackgroundSpeed + new Vector2(0f, -0.48f);
				explosion.Setup(base.Position + new Vector2(RandomHelper.RandomNextFloat(-200f, 200f), RandomHelper.RandomNextFloat(0f, 150f)), 1f, 1f, ((Vector2)(ref v)).Length(), MyMath.VectorToAngle(v));
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
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		target = oracle.GetRandomPlayerShip();
		Vector2 val = ((target == null) ? (new Vector2(400f, 300f) - base.Position) : (target.GetPosition() - base.Position));
		((Vector2)(ref val)).Normalize();
		lazerGenerator.SetPosition(base.Position + val * lazeroffset);
		return val;
	}

	private void CreateGenerator()
	{
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		stateTimer.Duration = MathHelper.Lerp(1000f, 6000f, (float)base.HitPoints / 150f);
		stateTimer.Reset();
		stateTimer.Start();
		lazerGenerator = LazerGenerator.NewLazerGenerator(collection, ((GameComponent)this).Game);
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
		((DrawableGameComponent)this).DrawOrder = 20;
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
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.9f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 4f, 2.3f, base.Speed * 0.5f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	private void Explode()
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.9f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 4f, 2.3f, base.Speed * 0.5f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 6f, 5.3f, base.Speed * 0.1f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		AwardScoreToAll(combo: false);
	}
}
