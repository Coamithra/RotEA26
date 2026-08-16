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
	// Online co-op, CLIENT side: this ball has already broken away as far as the FX are
	// concerned -- either we saw the hit ourselves or the host's beat told us. It is a latch of
	// its own rather than a `state == connected` test because a PUPPET's state never advances
	// past `startup` (Initialize sets it and Update is frozen for life), so a state test would
	// silently refuse every beat and the feature would do nothing at all.
	private bool netDetached;

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
		netDetached = false;
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
			// Online co-op (card e79bb994): these three are screen WRAPS -- a jump of the best
			// part of a screen, which the host's observed-velocity estimator would otherwise put
			// on the wire as real motion for the joiner's puppet to dead-reckon on. Same defect
			// class as the SpiderBoss fly-by park that card 8dabe812 was filed for; this type was
			// simply never noticed.
			if (base.Position.Y > 600f + radius + ybuffer / 3f)
			{
				base.Position = new Vector2(base.Position.X, -2f * ybuffer / 3f - radius);
				NetNoteTeleport();
			}
			if (base.Position.X < 0f - radius)
			{
				base.Position = new Vector2(800f + radius, base.Position.Y);
				NetNoteTeleport();
			}
			if (base.Position.X > 800f + radius)
			{
				base.Position = new Vector2(0f - radius, base.Position.Y);
				NetNoteTeleport();
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
			float moveDirection = MyMath.VectorToAngle(owner.GetPosition - base.Position) + (float)Math.PI / 8f;
			Move((float?)moveDirection, gameTime);
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
				// Online co-op (card c146422f, "the asteroids do not light up"): a Ball is not a
				// KillableAlien -- its hp and its 35ms blink are private to this method and ride
				// no wire field at all -- so a chip the HOST landed was completely invisible on
				// the join peer. Both beats are no-ops with no session or no peer.
				EvilAliensWeb.Compat.Net.NetSession.OnGameFx(
					EvilAliensWeb.Compat.Net.NetFxKind.EnemyHitFlash, this);
				if (hitpoints == 0)
				{
					base.Direction = MyMath.VectorToAngle(base.Position - owner.GetPosition) + (float)Math.PI / 4f * RandomHelper.RandomNextFloat(-1f, 1f);
					state = BallState.freed;
					owner.RemoveChild();
					base.MaxSpeed = 0.45f;
					base.Speed = base.MaxSpeed;
					base.MinSpeed = 0.18f * Settings.GetInstance().DifficultyModifier;
					netDetached = true;
					DetachEffect();
					// ...and the same beat for the DETACH, whose explosion + "expl1" are likewise
					// spawned here and nowhere the wire can see: the ball simply drifted away
					// silently on the other screen.
					EvilAliensWeb.Compat.Net.NetSession.OnGameFx(
						EvilAliensWeb.Compat.Net.NetFxKind.BallDetach, this);
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
					float ownScaleShare = scale / (ball.scale + scale);
					base.Position -= pushDir * overlap * (1f - ownScaleShare);
					ball.Position += pushDir * overlap * ownScaleShare;
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
	// Read by NetFxTest: the hit blink is a private timer with no other observable, and a beat
	// that quietly stopped starting it would change no counter anywhere.
	internal bool NetHitBlinking => hittimer.Active;

	internal bool NetDetachedFx => netDetached;

	// ---- local rotation (card 566474ae, "the asteroids rotate choppily for the joining player")
	//
	// THE BUG: `rotation` advances only in Update, which a frozen puppet never runs, so a client's
	// ball stepped to the replicated angle once per SnapshotTurnMs -- up to 13.7 degrees every
	// 240 ms. This is the same defect Asteroid.NetSpinPerMs already fixes for the rocks earlier in
	// the mission, and "the same system as the asteroids" is what the card asks for.
	//
	// THE JUSTIFICATION TRANSFERS, AND MORE STRONGLY THAN ASTEROID'S OWN. Asteroid argues "tumble
	// is decorative and no hitbox reads it"; here CollisionType is literally a
	// CollisionSimpleCircle, so rotation reaches nothing but Draw in ANY state. What the two peers
	// then disagree about is the tumble's phase and direction, which is the trade Asteroid already
	// ships with deliberately.
	//
	// THE PLAUSIBLE-BUT-WRONG READING, WRITTEN DOWN BECAUSE IT COST THIS CARD A WHOLE DESIGN.
	// `connected` looks like it settles: it picks the sign of its step to chase the bearing to the
	// owner the short way, which reads as a lock. It is not one. Both branches step by exactly
	// `rotationspeed * dt` -- only the SIGN is conditional -- so it is a bang-bang controller with
	// a fixed step, which can dither about its target or lag behind it but can never settle. A
	// CONNECTED BALL THEREFORE TURNS AT ITS FULL ROLLED SPEED, like every other state; measured off
	// the real Update at 1.00x for 16 of 16 balls, with 5-124 direction reversals per 10 s
	// (NetMotionTest section 6, which asserts it so this stops being a claim).
	//
	// That is why this override is UNCONDITIONAL and needs no wire byte, no protocol bump and no
	// "is it spinning" bit: a puppet free-spinning at its own roll has the right angular SPEED in
	// all four states, and only the phase and the occasional reversal differ. The rejected
	// alternative -- the host declaring per turn whether the ball is free, and a connected puppet
	// taking the replicated angle -- would have left exactly the balls that turn longest without
	// reversing still stepping 13.7 degrees a turn, i.e. it would have fixed the rain-in and left
	// the fight.
	internal override float NetSpinPerMs => rotationspeed;

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

	// The break-away burst, factored out of CollidesWith so the client can run the SAME code off
	// the wire beat rather than a copy that could drift from it. Pure FX -- no state, no score.
	// (Explosion is a cosmetic type and never replicates, so spawning one on a client is legal.)
	private void DetachEffect()
	{
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 1f, 1f, base.Speed * 0.05f, base.Direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
	}

	// Card c146422f: the client half of the two beats emitted in CollidesWith.
	//
	// Both guards are the puppet's own state, which is what makes them idempotent against the
	// client's own hit-testing: a chip it saw already started `hittimer`, and a ball it saw break
	// away is already out of `connected`, so the host's beat for the same event does nothing.
	// The detach beat deliberately does NOT change `state` or touch `owner` -- the freed ball's
	// motion arrives in the world snapshot like every other puppet's, and a puppet must never
	// run gameplay.
	internal override void NetPlayFx(EvilAliensWeb.Compat.Net.NetFxKind kind)
	{
		switch (kind)
		{
		case EvilAliensWeb.Compat.Net.NetFxKind.EnemyHitFlash:
			if (!netDetached && !hittimer.Active)
			{
				hittimer.Start();
				hittimer.Reset();
			}
			break;
		case EvilAliensWeb.Compat.Net.NetFxKind.BallDetach:
			if (!netDetached)
			{
				netDetached = true;
				DetachEffect();
			}
			break;
		}
	}
}
