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
