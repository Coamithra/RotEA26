using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
		this.size = size;
		this.lifetime = lifetime;
		base.Direction = direction;
		this.impulse = MyMath.AngleToVector(direction) * impulse;
		green = false;
	}

	public override void Initialize()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		BloodExplosionData[] array = particles;
		foreach (BloodExplosionData bloodExplosionData in array)
		{
			bloodExplosionData.Initialize(size, lifetime, impulse);
		}
		BloodExplosionData[] array2 = gooparticles;
		foreach (BloodExplosionData bloodExplosionData2 in array2)
		{
			bloodExplosionData2.Initialize(size, lifetime, impulse * 1.2f);
		}
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		BloodExplosionData[] array = particles;
		foreach (BloodExplosionData bloodExplosionData in array)
		{
			if (!(bloodExplosionData.lifetime <= 0f))
			{
				float num = 4f * bloodExplosionData.normalizedLifetime * (1f - bloodExplosionData.normalizedLifetime);
				Color val = new Color(new Vector4(1f, 1f, 1f, num));
				if (green)
				{
					spriteBatch.Draw(greenblood, base.Position + bloodExplosionData.position, bloodExplosionData.rotation, bloodExplosionData.scale / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/blooddrop_green", greenblood.Width), center: true, val);
				}
				else
				{
					spriteBatch.Draw(texture, base.Position + bloodExplosionData.position, bloodExplosionData.rotation, bloodExplosionData.scale / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/blooddrop", texture.Width), center: true, val);
				}
			}
		}
		BloodExplosionData[] array2 = gooparticles;
		foreach (BloodExplosionData bloodExplosionData2 in array2)
		{
			if (!(bloodExplosionData2.lifetime <= 0f))
			{
				float num = 4f * bloodExplosionData2.normalizedLifetime * (1f - bloodExplosionData2.normalizedLifetime);
				Color val = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(goo, base.Position + bloodExplosionData2.position, bloodExplosionData2.rotation, bloodExplosionData2.scale * 0.2f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/braingoo", goo.Width), center: true, val);
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		bool flag = false;
		BloodExplosionData[] array = particles;
		foreach (BloodExplosionData bloodExplosionData in array)
		{
			bloodExplosionData.Update(gameTime);
			if (bloodExplosionData.lifetime > 0f)
			{
				flag = true;
			}
		}
		BloodExplosionData[] array2 = gooparticles;
		foreach (BloodExplosionData bloodExplosionData2 in array2)
		{
			bloodExplosionData2.Update(gameTime);
			if (bloodExplosionData2.lifetime > 0f)
			{
				flag = true;
			}
		}
		base.Update(gameTime);
		if (!flag)
		{
			collection.Remove((GameComponent)(object)this);
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}
}
