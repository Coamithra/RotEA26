using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class ParatrooperBrain : KillableAlien
{
	private enum State
	{
		just_dropped,
		chuting,
		falling,
		landed,
		merging,
		merging2,
		fire
	}

	private Parachute chute;

	private Timer stateTimer = new Timer(0f, repeating: false);

	private State state;

	private bool big;

	private Vector2 mergepos;

	private bool dieaftermerge;

	private Vector2 startpos;

	private Timer mergetimer = new Timer(600f, repeating: false);

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

	public ParatrooperBrain(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/brainlargetransglow"));
		scale = 0.1f;
		SetHitPoints(1, scaleWithDifficulty: false);
		PointValue = 10f;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == chute)
		{
			chute = null;
			if (state == State.chuting)
			{
				state = State.falling;
			}
		}
	}

	public static ParatrooperBrain NewAlien(ComponentBin collection, Game game)
	{
		ParatrooperBrain paratrooperBrain = collection.Recycle<ParatrooperBrain>();
		if (paratrooperBrain == null)
		{
			paratrooperBrain = new ParatrooperBrain(game);
		}
		return paratrooperBrain;
	}

	public void Setup(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
	}

	public override void Initialize()
	{
		base.Initialize();
		((DrawableGameComponent)this).DrawOrder = 21;
		big = false;
		scale = 0.1f;
		state = State.just_dropped;
		stateTimer.Duration = RandomHelper.RandomNextFloat(200f, 1000f);
		stateTimer.Start();
		stateTimer.Reset();
		base.Direction = (float)Math.PI / 2f;
		rotation = 0f;
		base.Collides = true;
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0210: Unknown result type (might be due to invalid IL or missing references)
		//IL_021f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_0254: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Unknown result type (might be due to invalid IL or missing references)
		//IL_025f: Unknown result type (might be due to invalid IL or missing references)
		float num = 4.5f;
		float num2 = 2.8f * Settings.GetInstance().DifficultyModifier;
		float num3 = 4.5f;
		base.Update(gameTime);
		stateTimer.Update(gameTime);
		mergetimer.Update(gameTime);
		switch (state)
		{
		case State.just_dropped:
			base.Speed = num / 16.666666f;
			if (stateTimer.Finished)
			{
				state = State.chuting;
				chute = Parachute.NewAlien(collection, ((GameComponent)this).Game);
				chute.Setup(this);
				chute.Position = base.Position - new Vector2(0f, 40f);
				collection.Add((GameComponent)(object)chute);
			}
			break;
		case State.chuting:
			chute.Position = base.Position - new Vector2(0f, 40f);
			base.Speed = num2 / 16.666666f;
			break;
		case State.falling:
			base.Speed = num3 / 16.666666f;
			rotation += 0.002f * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			break;
		case State.merging:
			base.Position = Vector2.Lerp(mergepos, startpos, mergetimer.Normalized);
			if (mergetimer.Finished)
			{
				if (dieaftermerge)
				{
					Die();
					break;
				}
				scale = 0.2f;
				base.Position -= new Vector2(0f, 10f);
				big = true;
				((DrawableGameComponent)this).DrawOrder = 20;
			}
			break;
		case State.merging2:
			base.Position = Vector2.Lerp(mergepos, startpos, mergetimer.Normalized);
			if (mergetimer.Finished)
			{
				if (dieaftermerge)
				{
					Die();
					break;
				}
				scale = 0.33f;
				base.Position -= new Vector2(0f, 20f);
				state = State.fire;
				PlasmaBall plasmaBall = PlasmaBall.NewAlien(collection, ((GameComponent)this).Game);
				plasmaBall.Setup(base.Position, MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position));
				collection.Add((GameComponent)(object)plasmaBall);
			}
			break;
		case State.landed:
			break;
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		if (other is Floorbottom)
		{
			if (state == State.falling)
			{
				KilledBy(other, isComboGenerator: false);
			}
			else if (state == State.chuting)
			{
				base.Position = new Vector2(base.Position.X, ((Floorbottom)other).Bottom - ((CollisionBox)GetCollisionType()).Height / 2f);
				base.Speed = 0f;
				state = State.landed;
				if (chute != null)
				{
					chute.Remove();
					chute = null;
				}
			}
		}
		if (other is ParatrooperBrain && (state == State.landed || state == State.merging || state == State.merging2))
		{
			ParatrooperBrain paratrooperBrain = (ParatrooperBrain)other;
			if (paratrooperBrain.state == State.falling)
			{
				KilledBy(other, isComboGenerator: false);
				paratrooperBrain.Collides = false;
				paratrooperBrain.Die();
			}
		}
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		if (other is Bullet)
		{
			AwardScore(combo: false, other);
		}
		switch (state)
		{
		case State.just_dropped:
			Asplode(base.Direction);
			break;
		case State.chuting:
			Asplode(base.Direction);
			break;
		case State.falling:
			Asplode(-(float)Math.PI / 4f);
			break;
		case State.landed:
			Asplode(-(float)Math.PI / 4f);
			break;
		}
		Die();
	}

	private void Asplode(float direction)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, ((GameComponent)this).Game);
		bloodExplosion.Setup(base.Position, 2f, 0.8f, base.Speed * 0.5f, direction);
		collection.Add((GameComponent)(object)bloodExplosion);
		sound.PlayCue("small head asplode");
	}

	internal void MergeWith(ParatrooperBrain paratrooperBrain)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		state = State.merging;
		mergepos = (base.Position + paratrooperBrain.Position) / 2f;
		startpos = base.Position;
		mergetimer.Reset();
		mergetimer.Start();
		dieaftermerge = false;
		paratrooperBrain.state = State.merging;
		paratrooperBrain.mergepos = (paratrooperBrain.Position + base.Position) / 2f;
		paratrooperBrain.startpos = paratrooperBrain.Position;
		paratrooperBrain.mergetimer.Reset();
		paratrooperBrain.mergetimer.Start();
		paratrooperBrain.dieaftermerge = true;
	}

	internal void MergeWith2(ParatrooperBrain paratrooperBrain)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		state = State.merging2;
		mergepos = (base.Position + paratrooperBrain.Position) / 2f;
		startpos = base.Position;
		mergetimer.Reset();
		mergetimer.Start();
		dieaftermerge = false;
		paratrooperBrain.state = State.merging2;
		paratrooperBrain.mergepos = (paratrooperBrain.Position + base.Position) / 2f;
		paratrooperBrain.startpos = paratrooperBrain.Position;
		paratrooperBrain.mergetimer.Reset();
		paratrooperBrain.mergetimer.Start();
		paratrooperBrain.dieaftermerge = true;
	}

	internal bool ReadyToConnect()
	{
		return state == State.landed;
	}

	internal bool ReadyToConnect2()
	{
		if (big)
		{
			return state == State.merging;
		}
		return false;
	}
}
