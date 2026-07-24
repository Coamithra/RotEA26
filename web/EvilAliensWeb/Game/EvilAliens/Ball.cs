using System;
using Microsoft.Xna.Framework;
using EvilAliensWeb.Compat;

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
			float radiusFactor = state switch
			{
				BallState.startup => 0.8f, 
				BallState.connected => 1f, 
				BallState.attracted => 0.8f, 
				BallState.freed => 0.8f, 
				_ => 1f, 
			};
			collisionSimpleCircle.Position = base.Position;
			collisionSimpleCircle.Radius = radiusFactor * r;
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
		base.Initialize();
		rotationspeed = RandomHelper.RandomNextFloat(-0.001f, 0.001f);
		scale = 0.45f * RandomHelper.RandomNextFloat(0.42f, 0.85f);
		// physics/collision radius must match the on-screen size, so use DrawScale (= scale /
		// textureScale) against the texel width -- like the small asteroids' retrieveBoundsFromTexture.
		// (Raw `scale * texture.Width` would scale the hitbox by the supersample factor.)
		r = DrawScale * (float)(texture.LogicalWidth() / 2);
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
			float radius = r;
			if (base.Position.Y > 600f + radius + ybuffer / 3f)
			{
				base.Position = new Vector2(base.Position.X, -2f * ybuffer / 3f - radius);
			}
			if (base.Position.X < 0f - radius)
			{
				base.Position = new Vector2(800f + radius, base.Position.Y);
			}
			if (base.Position.X > 800f + radius)
			{
				base.Position = new Vector2(0f - radius, base.Position.Y);
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
			float angleToOwner = MyMath.VectorToAngle(owner.GetPosition - base.Position);
			float angleDelta = MyMath.Mod(angleToOwner - rotation, (float)Math.PI * 2f);
			if (angleDelta < (float)Math.PI)
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
			float despawnMargin = 400f;
			if ((base.Position.X > 800f + despawnMargin) | (base.Position.X < 0f - despawnMargin) | (base.Position.Y < 0f - despawnMargin) | (base.Position.Y > 600f + despawnMargin))
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
				Vector2 toBall = ball.Position - base.Position;
				float distance = (toBall).Length();
				if (distance < r + ball.r)
				{
					float overlap = r + ball.r - distance;
					Vector2 pushDir = toBall;
					(pushDir).Normalize();
					float massShare = scale / (ball.scale + scale);
					base.Position -= pushDir * overlap * (1f - massShare);
					ball.Position += pushDir * overlap * massShare;
				}
			}
			if (other is JunkBoss)
			{
				JunkBoss junkBoss = (JunkBoss)other;
				Vector2 toBoss = junkBoss.GetPosition - base.Position;
				float distance = (toBoss).Length();
				if (distance < r + junkBoss.r)
				{
					_ = junkBoss.r;
					Vector2 pushDir = toBoss;
					(pushDir).Normalize();
					// Fidelity (review M4): the spatial grid fires each circle pair once per direction
					// per frame; the shipped 2008 build's all-pairs scan fired this ungated 1px push-out
					// twice per frame — the x2 preserves the original net separation rate so
					// connected Balls don't sink deeper into the JunkBoss.
					base.Position -= pushDir * 2f;
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

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsBosses1) --------
	// The ctor picks one of AsteroidSmall1..4 at RANDOM; the client puppet must be forced onto the
	// host's pick or the same netId ball is a different rock on each screen.
	// 1..4 = the asset's trailing digit (1-BASED -- unlike Asteroid.NetSmallSheetIndex's 0..3).
	internal int NetAsteroidVariant
	{
		get
		{
			if (texturename != null && texturename.Length > 0)
			{
				char last = texturename[texturename.Length - 1];
				if (last >= '1' && last <= '4')
				{
					return last - '0';
				}
			}
			return 1;
		}
	}

	internal void NetForceAsteroidVariant(int variant)
	{
		if (variant < 1 || variant > 4 || variant == NetAsteroidVariant)
		{
			return;
		}
		LoadAnimation(new AnimationData("GFX/Sprites/AsteroidSmall" + variant));
	}
}
