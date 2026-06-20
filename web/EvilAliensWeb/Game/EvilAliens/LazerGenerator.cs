using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class LazerGenerator : AlienDrawableGameComponent
{
	private bool silent;

	private bool freed;

	private Vector2 impulse = Vector2.Zero;

	private LazerGeneratorData[] particles;

	private SoundEffectInstance sfx;

	private float size = 1f;

	private float lifetime = 1f;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType => b;

	public LazerGenerator(Game game)
		: base(game)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		base.Collides = false;
		LoadAnimation(new AnimationData("GFX/Menu/star"));
		base.DrawOrder = 40;
		particles = new LazerGeneratorData[10];
		for (int i = 0; i < particles.Length; i++)
		{
			particles[i] = new LazerGeneratorData();
		}
		base.Visible = false;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			sound.Stop(sfx);
		}
	}

	public static LazerGenerator NewLazerGenerator(ComponentBin collection, Game game)
	{
		LazerGenerator lazerGenerator = collection.Recycle<LazerGenerator>();
		if (lazerGenerator == null)
		{
			lazerGenerator = new LazerGenerator(game);
		}
		return lazerGenerator;
	}

	public void Setup(Vector2 position, float size, float lifetime, float impulse, float direction)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		silent = false;
		base.Position = position;
		this.size = size;
		this.lifetime = lifetime;
		base.Direction = direction;
		this.impulse = MyMath.AngleToVector(direction) * impulse;
	}

	public override void Initialize()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		LazerGeneratorData[] array = particles;
		foreach (LazerGeneratorData lazerGeneratorData in array)
		{
			lazerGeneratorData.Initialize(size, lifetime, impulse);
		}
		if (!silent)
		{
			sfx = sound.Play("lazercharge");
		}
		freed = false;
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		LazerGeneratorData[] array = particles;
		foreach (LazerGeneratorData lazerGeneratorData in array)
		{
			if (!(lazerGeneratorData.lifetime <= 0f))
			{
				float num = 4f * lazerGeneratorData.normalizedLifetime * (1f - lazerGeneratorData.normalizedLifetime);
				Color val = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(texture, base.Position + lazerGeneratorData.position, 0f, lazerGeneratorData.scale, center: true, val);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		if (freed)
		{
			collection.Remove((GameComponent)(object)this);
		}
		bool flag = false;
		LazerGeneratorData[] array = particles;
		foreach (LazerGeneratorData lazerGeneratorData in array)
		{
			lazerGeneratorData.Update(gameTime);
			if (lazerGeneratorData.lifetime > 0f)
			{
				flag = true;
			}
			if (lazerGeneratorData.lifetime <= 0f)
			{
				lazerGeneratorData.Initialize(size, lifetime, impulse);
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

	public void SetPosition(Vector2 vector2)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = vector2;
	}

	internal void Free()
	{
		freed = true;
	}

	internal void SetupSilent()
	{
		silent = true;
	}
}
