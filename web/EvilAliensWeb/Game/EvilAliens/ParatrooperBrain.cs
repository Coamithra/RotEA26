using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

	// Additive blue glow behind the brain (BrainGlow), replacing the halo the old
	// brainlargetransglow sprite had baked in. See the constructor.
	private Texture2D glowTexture;

	private float glowPhase;

	// Card c25883a2: the pre-card 0.1 / 0.2 / 0.33 carried through the SAME x5 the Braineroid
	// migration used (0.4/0.2/0.07 -> 2/1/0.35), so the two brain families stay in proportion.
	// Why x5 and not something derived: on-screen frame width is designWidth * scale, and the
	// swap goes from brainlargetransglow -- unregistered in
	// AlienDrawableGameComponent.DesignFrameWidth, so its designWidth is its own 519px logical
	// texture -- to brainanimated, registered at 100. Preserving the FRAME width would be x5.19;
	// preserving the VISIBLE BRAIN would be x5.52, because the old sprite filled 0.900 of its
	// texture (alpha > 128) and a sheet cell fills 0.846. x5 is neither, and is deliberately the
	// Braineroid number: it lands the visible brain at 0.906 of its old size, exactly the ratio
	// that migration already shipped (huge went 186.8 -> 169.2 design px on the same measure).
	private const float ScaleDropping = 0.5f;   // 0.1 * 5

	private const float ScaleMerged = 1f;       // 0.2 * 5

	private const float ScaleMerged2 = 1.65f;   // 0.33 * 5

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

	public ParatrooperBrain(Game game)
		: base(game)
	{
		// Card c25883a2: the animated cyborg brain, same art the in-world Braineroid uses. This
		// was the LAST consumer of the old static brainlargetransglow -- the Braineroids moved to
		// the sheet with it, the cast screen followed in card 208da2fe, and the paratroopers were
		// simply missed, so the challenge level shipped with the previous generation's sprite.
		// Sheet + interpolation args mirror Braineroid exactly (5 cols x 4 rows, 20 frames at
		// 0.4fps; interpolationOptions = always so interpolate.fx cross-fades N->N+1 regardless
		// of the global Interpolate setting, which is what makes that low frame rate read smooth).
		LoadAnimation(new AnimationData("GFX/Sprites/brainanimated", 4, 5, 0, 0.4f, 0, 20));
		interpolationOptions = InterpolationOptions.always;
		glowTexture = content.Load<Texture2D>("GFX/Sprites/brainanimatedglow");
		scale = ScaleDropping;
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
		base.Position = position;
	}

	public override void Initialize()
	{
		base.Initialize();
		base.DrawOrder = 21;
		big = false;
		scale = ScaleDropping;
		state = State.just_dropped;
		stateTimer.Duration = RandomHelper.RandomNextFloat(200f, 1000f);
		stateTimer.Start();
		stateTimer.Reset();
		base.Direction = (float)Math.PI / 2f;
		rotation = 0f;
		base.Collides = true;
		glowPhase = BrainGlow.RandomPhase();
		// Desync the animation too: a drop wave is a dozen brains at once, and on one shared
		// 50s loop they would breathe in perfect lock-step. After base.Initialize, which resets
		// curframe to FirstFrame (and which the sprite harness overrides again for a frozen frame).
		curframe = RandomHelper.RandomNextFloat(0f, Math.Max(1, rows * columns));
	}

	public override void Draw(GameTime gameTime)
	{
		BrainGlow.Draw(spriteBatch, glowTexture, Position, rotation, DrawScale, glowPhase, blendMode);
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
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
				chute = Parachute.NewAlien(collection, base.Game);
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
				scale = ScaleMerged;
				base.Position -= new Vector2(0f, 10f);
				big = true;
				base.DrawOrder = 20;
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
				scale = ScaleMerged2;
				base.Position -= new Vector2(0f, 20f);
				state = State.fire;
				PlasmaBall plasmaBall = PlasmaBall.NewAlien(collection, base.Game);
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
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(base.Position, 2f, 0.8f, base.Speed * 0.5f, direction);
		collection.Add((GameComponent)(object)bloodExplosion);
		sound.PlayCue("small head asplode");
	}

	internal void MergeWith(ParatrooperBrain paratrooperBrain)
	{
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
