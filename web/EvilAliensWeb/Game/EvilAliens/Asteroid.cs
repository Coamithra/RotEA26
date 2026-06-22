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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return base.SpeedVector;
	}

	public void Setup(Vector2 position, float direction, float speed, bool reallyBig)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		Setup(position, direction, speed, reallyBig, randomSpeedOffset: true);
	}

	public void Setup(Vector2 position, float direction, float speed, bool reallyBig, bool randomSpeedOffset)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		base.Update(gameTime);
		if (base.Position.X > 1000f)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
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
}
