using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class BloodExplosion : AlienDrawableGameComponent
{
	private bool green;

	private Vector2 impulse = Vector2.Zero;

	private BloodExplosionData[] particles;

	private BloodExplosionData[] gooparticles;

	private Texture2D goo;

	private Texture2D greenblood;

	private float size = 1f;

	private float lifetime = 1f;

	private CollisionBox boundBox = new CollisionBox();

	public override ICollisionType CollisionType
	{
		get
		{
			boundBox.TopLeft = base.Position + new Vector2(-10f, -10f);
			boundBox.BottomRight = base.Position + new Vector2(10f, 10f);
			return boundBox;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		goo = content.Load<Texture2D>("GFX/Sprites/braingoo");
		greenblood = content.Load<Texture2D>("GFX/Sprites/blooddrop_green");
	}

	public BloodExplosion(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/blooddrop"));
		base.DrawOrder = 40;
		particles = new BloodExplosionData[30];
		gooparticles = new BloodExplosionData[4];
		for (int i = 0; i < particles.Length; i++)
		{
			particles[i] = new BloodExplosionData();
		}
		for (int j = 0; j < gooparticles.Length; j++)
		{
			gooparticles[j] = new BloodExplosionData();
		}
		base.Collides = false;
	}

	public static BloodExplosion NewExplosion(ComponentBin collection, Game game)
	{
		BloodExplosion bloodExplosion = collection.Recycle<BloodExplosion>();
		if (bloodExplosion == null)
		{
			bloodExplosion = new BloodExplosion(game);
		}
		return bloodExplosion;
	}

	public void MakeGreen()
	{
		green = true;
	}

	public void Setup(Vector2 position, float size, float lifetime, float impulse, float direction)
	{
		base.Position = position;
		this.size = size;
		this.lifetime = lifetime;
		base.Direction = direction;
		this.impulse = MyMath.AngleToVector(direction) * impulse;
		green = false;
	}

	public override void Initialize()
	{
		BloodExplosionData[] drops = particles;
		foreach (BloodExplosionData particle in drops)
		{
			particle.Initialize(size, lifetime, impulse);
		}
		BloodExplosionData[] blobs = gooparticles;
		foreach (BloodExplosionData particle in blobs)
		{
			particle.Initialize(size, lifetime, impulse * 1.2f);
		}
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		BloodExplosionData[] drops = particles;
		foreach (BloodExplosionData particle in drops)
		{
			if (!(particle.lifetime <= 0f))
			{
				float alpha = 4f * particle.normalizedLifetime * (1f - particle.normalizedLifetime);
				Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
				if (green)
				{
					spriteBatch.Draw(greenblood, base.Position + particle.position, particle.rotation, particle.scale / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/blooddrop_green", greenblood.LogicalWidth()), center: true, tint);
				}
				else
				{
					spriteBatch.Draw(texture, base.Position + particle.position, particle.rotation, particle.scale / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/blooddrop", texture.LogicalWidth()), center: true, tint);
				}
			}
		}
		BloodExplosionData[] blobs = gooparticles;
		foreach (BloodExplosionData particle in blobs)
		{
			if (!(particle.lifetime <= 0f))
			{
				float alpha = 4f * particle.normalizedLifetime * (1f - particle.normalizedLifetime);
				Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
				spriteBatch.Draw(goo, base.Position + particle.position, particle.rotation, particle.scale * 0.2f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/braingoo", goo.LogicalWidth()), center: true, tint);
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		bool anyAlive = false;
		BloodExplosionData[] drops = particles;
		foreach (BloodExplosionData particle in drops)
		{
			particle.Update(gameTime);
			if (particle.lifetime > 0f)
			{
				anyAlive = true;
			}
		}
		BloodExplosionData[] blobs = gooparticles;
		foreach (BloodExplosionData particle in blobs)
		{
			particle.Update(gameTime);
			if (particle.lifetime > 0f)
			{
				anyAlive = true;
			}
		}
		base.Update(gameTime);
		if (!anyAlive)
		{
			collection.Remove((GameComponent)(object)this);
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}
}
