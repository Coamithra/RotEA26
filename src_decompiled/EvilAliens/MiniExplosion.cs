using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class MiniExplosion
{
	private Vector2 position;

	private ExplosionData[] particles;

	private ExplosionData[] smokeparticles;

	private float size = 0.6f;

	private float lifetime = 0.8f;

	private Texture2D smoketexture;

	private Texture2D particletexture;

	private SpriteBatchWrapper spriteBatch;

	public bool Active;

	public void LoadGraphics()
	{
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		smoketexture = contentManager.Load<Texture2D>("GFX/Sprites/smoke");
		particletexture = contentManager.Load<Texture2D>("GFX/Sprites/explosion");
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
	}

	public MiniExplosion(Game game)
	{
		particles = new ExplosionData[5];
		smokeparticles = new ExplosionData[2];
		for (int i = 0; i < particles.Length; i++)
		{
			particles[i] = new ExplosionData();
		}
		for (int j = 0; j < smokeparticles.Length; j++)
		{
			smokeparticles[j] = new ExplosionData();
		}
	}

	public void Reset()
	{
		Active = false;
	}

	public void Show(Vector2 position)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		Active = true;
		ExplosionData[] array = particles;
		foreach (ExplosionData explosionData in array)
		{
			explosionData.Initialize(size, lifetime, Vector2.Zero);
		}
		ExplosionData[] array2 = smokeparticles;
		foreach (ExplosionData explosionData2 in array2)
		{
			explosionData2.Initialize(size, lifetime * 1.35f, Vector2.Zero);
		}
		this.position = position;
	}

	public void Draw(GameTime gameTime)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		ExplosionData[] array = smokeparticles;
		foreach (ExplosionData explosionData in array)
		{
			if (!(explosionData.lifetime <= 0f))
			{
				float num = 4f * explosionData.normalizedLifetime * (1f - explosionData.normalizedLifetime);
				Color color = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(smoketexture, position + explosionData.position, explosionData.rotation, explosionData.scale, center: true, color);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		ExplosionData[] array2 = particles;
		foreach (ExplosionData explosionData2 in array2)
		{
			if (!(explosionData2.lifetime <= 0f))
			{
				float num = 4f * explosionData2.normalizedLifetime * (1f - explosionData2.normalizedLifetime);
				Color color = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(particletexture, position + explosionData2.position, explosionData2.rotation, explosionData2.scale, center: true, color);
			}
		}
	}

	public void Update(GameTime gameTime)
	{
		ExplosionData[] array = particles;
		foreach (ExplosionData explosionData in array)
		{
			explosionData.Update(gameTime);
			if (explosionData.lifetime > 0f)
			{
				Active = true;
			}
		}
		ExplosionData[] array2 = smokeparticles;
		foreach (ExplosionData explosionData2 in array2)
		{
			explosionData2.Update(gameTime);
			if (explosionData2.lifetime > 0f)
			{
				Active = true;
			}
		}
	}
}
