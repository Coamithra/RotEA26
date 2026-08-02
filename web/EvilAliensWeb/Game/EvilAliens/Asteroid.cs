using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Asteroid : AlienDrawableGameComponent
{
	private float rotationspeed;

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

	public Asteroid(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/large_asteroid"));
	}

	public static Asteroid NewAsteroid(ComponentBin collection, Game game)
	{
		Asteroid asteroid = collection.Recycle<Asteroid>();
		if (asteroid == null)
		{
			asteroid = new Asteroid(game);
		}
		return asteroid;
	}

	public Vector2 GetSpeed()
	{
		return base.SpeedVector;
	}

	public void Setup(Vector2 position, float direction, float speed, bool reallyBig)
	{
		Setup(position, direction, speed, reallyBig, randomSpeedOffset: true);
	}

	public void Setup(Vector2 position, float direction, float speed, bool reallyBig, bool randomSpeedOffset)
	{
		// The big level-opening asteroid uses the hi-res large_asteroid (7x texture, drawn at
		// scale 3). Normal asteroids draw at scale 0.45, where that big sheet would be massively
		// oversampled, so they use the lower-res AsteroidSmall variants (footprint-matched to the
		// same design, picked at random each spawn -- recycled, so per-spawn here rather than the ctor).
		if (reallyBig)
		{
			LoadAnimation(new AnimationData("GFX/Sprites/large_asteroid"));
		}
		else
		{
			LoadAnimation(new AnimationData("GFX/Sprites/AsteroidSmall" + RandomHelper.Random.Next(1, 5)));
		}
		base.Position = position;
		if (randomSpeedOffset)
		{
			base.Direction = direction + RandomHelper.RandomNextFloat(-(float)Math.PI / 20f, (float)Math.PI / 20f);
			base.Speed = speed + RandomHelper.RandomNextFloat((0f - speed) * 0.1f, speed * 0.1f);
		}
		else
		{
			base.Direction = direction;
			base.Speed = speed;
		}
		base.Speed *= 1f + (Settings.GetInstance().DifficultyModifier - 1f) / 2f;
		base.Collides = true;
		color = Color.White;
		base.DrawOrder = 20;
		if (reallyBig)
		{
			scale = 3f;
		}
		else
		{
			scale = 0.45f;
		}
	}

	public override void Initialize()
	{
		base.Initialize();
		float num = 0.0014702653f * Settings.GetInstance().DifficultyModifier;
		rotationspeed = RandomHelper.RandomNextFloat(0f - num, num);
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	// Tumble is decorative and the hitbox is a circle, so a client puppet spins on its own
	// locally-rolled rotationspeed rather than stepping to the replicated angle once per
	// round-robin turn (which read as a stutter).
	internal override float NetSpinPerMs => rotationspeed;

	// Anchored motion (card c1a38ef9). An asteroid's whole translation is `Position +=
	// SpeedVector * dt` at a Speed/Direction pair Setup fixes -- a constant-velocity line with no
	// periodic component, so NetPathOffset stays the base's zero and this flag buys exactly one
	// thing: the CHANGE of heading when a bullet nudges it (CollidesWith below) arrives on the
	// client as an eased velocity rather than a step at the snapshot turn that reports it.
	//
	// It carries NO spawn anchor and no new wire bytes, which is a deliberate narrowing of the
	// card's wording. The steady linear path is ALREADY dead-reckoned exactly -- a finite
	// difference of a straight line is that line -- and it measures so:
	// tools/sim/net_puppet_drive_sim.py --smoothness reads a steady-state jerk of 0.000 (N=16) to
	// 0.001 (N=128) for a linear mover against the host's own 0.0008. There is nothing there for
	// an anchor to improve; the kink at a heading change is the whole defect, and the easing is
	// what removes it.
	internal override bool NetPathAnchored => true;

	public override void Update(GameTime gameTime)
	{
		rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		base.Update(gameTime);
		if (base.Position.X > 1000f)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		if (other is Bullet)
		{
			Vector2 speed = ((Bullet)other).GetSpeed();
			(speed).Normalize();
			Vector2 v = base.SpeedVector + speed * 0.001f;
			base.Direction = MyMath.VectorToAngle(v);
			base.Speed = (v).Length();
		}
		base.CollidesWith(other);
	}

	internal void SetBackground()
	{
		base.Collides = false;
		color = new Color(new Vector3(0.3f, 0.3f, 0.3f));
		scale *= 0.55f;
		base.Speed *= 0.4f;
		base.DrawOrder = 1;
	}

	internal bool IsBig()
	{
		return scale > 1f;
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/AsteroidDescriptor) ------
	// The LOOK is fixed at Setup: reallyBig picks the hi-res large_asteroid (scale 3), else one
	// of four AsteroidSmall{1..4} sheets at RANDOM (scale 0.45); SetBackground then greys + sinks
	// the belt-decoration copies. Position/rotation/scale ride the base state; only the SHEET
	// pick and the grey/DrawOrder background flag can't be reconstructed from it. Asteroid has no
	// HP and never splits, so there is no damage/state visual to replicate.

	internal bool NetReallyBig => texturename == "GFX/Sprites/large_asteroid";

	// 0..3 for AsteroidSmall1..4 (the trailing digit); 0 when big / unrecognised.
	internal int NetSmallSheetIndex
	{
		get
		{
			if (texturename != null && texturename.StartsWith("GFX/Sprites/AsteroidSmall"))
			{
				int n = texturename[texturename.Length - 1] - '1';
				if (n >= 0 && n <= 3)
				{
					return n;
				}
			}
			return 0;
		}
	}

	// SetBackground is the only site that drops DrawOrder to 1 -- a reliable belt-decoration marker.
	internal bool NetIsBackground => base.DrawOrder == 1;

	// Card 9a3175d0: the belt-decoration copies are pure scenery -- SetBackground clears Collides
	// and nothing ever sets it again on that instance (only Setup does, and it runs BEFORE
	// SetBackground at the one spawner that makes them). So they are not replicated per entity;
	// AsteroidSpawner announces NetCosmeticKind.BackgroundAsteroids instead and the joiner runs
	// its own background-only copy of the spawner. The real asteroids in the same DoEvent are
	// unaffected.
	internal override bool NetCosmeticOnly => NetIsBackground;

	// Client puppet: force the host's exact sheet pick (Setup re-randomises the small variant).
	internal void NetForceSheet(bool reallyBig, int smallIndex)
	{
		if (reallyBig)
		{
			LoadAnimation(new AnimationData("GFX/Sprites/large_asteroid"));
		}
		else
		{
			int n = (smallIndex >= 0 && smallIndex <= 3) ? smallIndex : 0;
			LoadAnimation(new AnimationData("GFX/Sprites/AsteroidSmall" + (n + 1)));
		}
	}
}
