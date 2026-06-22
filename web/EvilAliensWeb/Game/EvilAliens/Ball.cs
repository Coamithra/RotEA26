using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class Ball : AlienDrawableGameComponent
{
	private enum BallState
	{
		startup,
		connected,
		attracted,
		freed
	}

	private const int initialhitpoints = 3;

	private const float maxspeedconnected = 0.18f;

	private const float maxspeedstartup = 0.24f;

	private const float maxspeedfreed = 0.45f;

	private const float minspeedfreed = 0.18f;

	private const float accelerationconnected = 0.0011999999f;

	private const float decelerationconnected = 0.00045f;

	private const float accelerationattracted = 0.000120000004f;

	private const float decelerationattracted = 7.2E-05f;

	private BallState state;

	private float ybuffer = 900f;

	private JunkBoss owner;

	private float r;

	private int hitpoints;

	private Timer hittimer;

	private Timer starttimer;

	private float rotationspeed;

	private CollisionSimpleCircle collisionSimpleCircle = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			float num = state switch
			{
				BallState.startup => 0.8f, 
				BallState.connected => 1f, 
				BallState.attracted => 0.8f, 
				BallState.freed => 0.8f, 
				_ => 1f, 
			};
			collisionSimpleCircle.Position = base.Position;
			collisionSimpleCircle.Radius = num * r;
			return collisionSimpleCircle;
		}
	}

	public bool IsConnected()
	{
		return state == BallState.connected;
	}

	public Ball(Game game)
		: base(game)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		// Balls are visually small asteroids -> use the same lower-res AsteroidSmall set the
		// normal small asteroids use (picked at random), NOT the hi-res big-asteroid texture.
		LoadAnimation(new AnimationData("GFX/Sprites/AsteroidSmall" + RandomHelper.Random.Next(1, 5)));
		base.DrawOrder = 22;
		hittimer = new Timer(35f, repeating: false);
		hittimer.Stop();
		starttimer = new Timer(5000f, repeating: false);
		starttimer.Stop();
		starttimer.Reset();
		PointValue = 20f;
		timers.Add(hittimer);
		timers.Add(starttimer);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == owner)
		{
			owner = null;
		}
	}

	public static Ball NewBall(ComponentBin collection, Game game)
	{
		Ball ball = collection.Recycle<Ball>();
		if (ball == null)
		{
			ball = new Ball(game);
		}
		return ball;
	}

	public void Setup(JunkBoss owner)
	{
		this.owner = owner;
	}

	public override void Initialize()
	{
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		rotationspeed = RandomHelper.RandomNextFloat(-0.001f, 0.001f);
		scale = 0.45f * RandomHelper.RandomNextFloat(0.42f, 0.85f);
		// physics/collision radius must match the on-screen size, so use DrawScale (= scale /
		// textureScale) against the texel width -- like the small asteroids' retrieveBoundsFromTexture.
		// (Raw `scale * texture.Width` would scale the hitbox by the supersample factor.)
		r = DrawScale * (float)(texture.Width / 2);
		state = BallState.startup;
		base.Position = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f - r, -600f - ybuffer));
		base.Direction = (float)Math.PI / 2f + RandomHelper.RandomNextFloat(-(float)Math.PI / 12f, (float)Math.PI / 12f);
		base.MaxSpeed = 0.24f * RandomHelper.RandomNextFloat(0.9f, 1.1f) * Settings.GetInstance().DifficultyFactorized(0.5f);
		base.MinSpeed = 0f;
		base.Speed = base.MaxSpeed;
		base.Acceleration = 0.000120000004f;
		base.Deceleration = 7.2E-05f;
		hittimer.Reset();
		hittimer.Stop();
		starttimer.Reset();
		starttimer.Start();
		hitpoints = 3;
		ybuffer = 900f / Settings.GetInstance().DifficultyFactorized(0.5f);
	}

	public override void Draw(GameTime gameTime)
	{
		if (hittimer.Active)
		{
			spriteBatch.lightenEffect.Enable();
		}
		base.Draw(gameTime);
		if (hittimer.Active)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public void CheckOwner()
	{
		if (owner == null)
		{
			state = BallState.freed;
			base.MaxSpeed = 0.45f;
			base.MinSpeed = 0.18f * Settings.GetInstance().DifficultyModifier;
			base.Speed = MathHelper.Max(base.MinSpeed, base.Speed);
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0287: Unknown result type (might be due to invalid IL or missing references)
		//IL_029c: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_02be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0220: Unknown result type (might be due to invalid IL or missing references)
		//IL_0225: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		CheckOwner();
		switch (state)
		{
		case BallState.attracted:
		{
			rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			float value = MyMath.VectorToAngle(owner.GetPosition - base.Position);
			Move((float?)value, gameTime);
			break;
		}
		case BallState.startup:
		{
			rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			float num4 = r;
			if (base.Position.Y > 600f + num4 + ybuffer / 3f)
			{
				base.Position = new Vector2(base.Position.X, -2f * ybuffer / 3f - num4);
			}
			if (base.Position.X < 0f - num4)
			{
				base.Position = new Vector2(800f + num4, base.Position.Y);
			}
			if (base.Position.X > 800f + num4)
			{
				base.Position = new Vector2(0f - num4, base.Position.Y);
			}
			if (!starttimer.Active)
			{
				Move(gameTime);
				if (base.Speed < 0.01f)
				{
					state = BallState.attracted;
					base.Acceleration = 0.000120000004f;
					base.Deceleration = 7.2E-05f;
				}
			}
			break;
		}
		case BallState.connected:
		{
			float num2 = MyMath.VectorToAngle(owner.GetPosition - base.Position);
			float num3 = MyMath.Mod(num2 - rotation, (float)Math.PI * 2f);
			if (num3 < (float)Math.PI)
			{
				rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			}
			else
			{
				rotation -= rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			}
			float value2 = MyMath.VectorToAngle(owner.GetPosition - base.Position) + (float)Math.PI / 8f;
			Move((float?)value2, gameTime);
			break;
		}
		case BallState.freed:
		{
			rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			Move((float?)null, gameTime);
			float num = 400f;
			if ((base.Position.X > 800f + num) | (base.Position.X < 0f - num) | (base.Position.Y < 0f - num) | (base.Position.Y > 600f + num))
			{
				Die();
			}
			break;
		}
		}
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Unknown result type (might be due to invalid IL or missing references)
		//IL_0276: Unknown result type (might be due to invalid IL or missing references)
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0210: Unknown result type (might be due to invalid IL or missing references)
		//IL_0215: Unknown result type (might be due to invalid IL or missing references)
		//IL_0219: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0237: Unknown result type (might be due to invalid IL or missing references)
		//IL_023c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0240: Unknown result type (might be due to invalid IL or missing references)
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		//IL_024c: Unknown result type (might be due to invalid IL or missing references)
		CheckOwner();
		switch (state)
		{
		case BallState.connected:
			if ((((other is Bullet) | (other is Blast && !((Blast)other).IsMini)) || other is Option) & (state == BallState.connected) & !hittimer.Active)
			{
				hitpoints--;
				hittimer.Start();
				hittimer.Reset();
				if (hitpoints == 0)
				{
					base.Direction = MyMath.VectorToAngle(base.Position - owner.GetPosition) + (float)Math.PI / 4f * RandomHelper.RandomNextFloat(-1f, 1f);
					state = BallState.freed;
					owner.RemoveChild();
					base.MaxSpeed = 0.45f;
					base.Speed = base.MaxSpeed;
					base.MinSpeed = 0.18f * Settings.GetInstance().DifficultyModifier;
					Explosion explosion = Explosion.NewExplosion(collection, base.Game);
					explosion.Setup(base.Position, 1f, 1f, base.Speed * 0.05f, base.Direction);
					collection.Add((GameComponent)(object)explosion);
					sound.PlayCue("expl1");
					if (other is Bullet)
					{
						AwardScore(combo: true, other);
					}
					if (other is Blast)
					{
						AwardScore(combo: false, other);
					}
				}
			}
			if (other is Ball && ((Ball)other).state == BallState.connected)
			{
				Ball ball = (Ball)other;
				Vector2 val = ball.Position - base.Position;
				float num = (val).Length();
				if (num < r + ball.r)
				{
					float num2 = r + ball.r - num;
					Vector2 val2 = val;
					(val2).Normalize();
					float num3 = scale / (ball.scale + scale);
					base.Position -= val2 * num2 * (1f - num3);
					ball.Position += val2 * num2 * num3;
				}
			}
			if (other is JunkBoss)
			{
				JunkBoss junkBoss = (JunkBoss)other;
				Vector2 val3 = junkBoss.GetPosition - base.Position;
				float num4 = (val3).Length();
				if (num4 < r + junkBoss.r)
				{
					_ = junkBoss.r;
					Vector2 val4 = val3;
					(val4).Normalize();
					base.Position -= val4;
				}
			}
			break;
		case BallState.attracted:
			if ((other is JunkBoss) | (other is Ball && ((Ball)other).state == BallState.connected))
			{
				state = BallState.connected;
				if (owner != null)
				{
					owner.AddChild();
				}
				base.MaxSpeed = 0.18f;
				base.Acceleration = 0.0011999999f;
				base.Deceleration = 0.00045f;
			}
			break;
		}
		base.CollidesWith(other);
	}
}
