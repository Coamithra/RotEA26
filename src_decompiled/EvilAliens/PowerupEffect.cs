using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class PowerupEffect : AlienDrawableGameComponent
{
	private Vector2 impulse = Vector2.Zero;

	private PowerupEffectData[] particles;

	private float size = 1f;

	private float lifetime = 1f;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType => b;

	public PowerupEffect(Game game)
		: base(game)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		LoadAnimation(new AnimationData("GFX/Sprites/playersheet", 4, 8, 1, 6f));
		((DrawableGameComponent)this).DrawOrder = 40;
		particles = new PowerupEffectData[5];
		for (int i = 0; i < particles.Length; i++)
		{
			particles[i] = new PowerupEffectData();
		}
		base.Collides = false;
	}

	public static PowerupEffect NewPowerupEffect(ComponentBin collection, Game game)
	{
		PowerupEffect powerupEffect = collection.Recycle<PowerupEffect>();
		if (powerupEffect == null)
		{
			powerupEffect = new PowerupEffect(game);
		}
		return powerupEffect;
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
	}

	public override void Initialize()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		PowerupEffectData[] array = particles;
		foreach (PowerupEffectData powerupEffectData in array)
		{
			powerupEffectData.Initialize(size, lifetime, impulse);
		}
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		PowerupEffectData[] array = particles;
		foreach (PowerupEffectData powerupEffectData in array)
		{
			if (!(powerupEffectData.lifetime <= 0f))
			{
				float num = 4f * powerupEffectData.normalizedLifetime * (1f - powerupEffectData.normalizedLifetime);
				Color val = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(texture, new Rectangle(0, 0, 48, 48), base.Position + powerupEffectData.position, powerupEffectData.rotation, powerupEffectData.scale, center: true, val);
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		bool flag = false;
		PowerupEffectData[] array = particles;
		foreach (PowerupEffectData powerupEffectData in array)
		{
			powerupEffectData.Update(gameTime);
			if (powerupEffectData.lifetime > 0f)
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

	public void SetPosition(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
	}
}
