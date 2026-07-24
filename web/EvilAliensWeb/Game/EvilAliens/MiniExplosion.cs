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
		Active = true;
		ExplosionData[] fire = particles;
		foreach (ExplosionData explosionData in fire)
		{
			explosionData.Initialize(size, lifetime, Vector2.Zero);
		}
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData explosionData2 in smoke)
		{
			explosionData2.Initialize(size, lifetime * 1.35f, Vector2.Zero);
		}
		this.position = position;
	}

	public void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData explosionData in smoke)
		{
			if (!(explosionData.lifetime <= 0f))
			{
				float alpha = 4f * explosionData.normalizedLifetime * (1f - explosionData.normalizedLifetime);
				Color color = new Color(new Vector4(1f, 1f, 1f, alpha));
				spriteBatch.Draw(smoketexture, position + explosionData.position, explosionData.rotation, explosionData.scale, center: true, color);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		ExplosionData[] fire = particles;
		foreach (ExplosionData explosionData2 in fire)
		{
			if (!(explosionData2.lifetime <= 0f))
			{
				float alpha = 4f * explosionData2.normalizedLifetime * (1f - explosionData2.normalizedLifetime);
				Color color = new Color(new Vector4(1f, 1f, 1f, alpha));
				spriteBatch.Draw(particletexture, position + explosionData2.position, explosionData2.rotation, explosionData2.scale, center: true, color);
			}
		}
	}

	public void Update(GameTime gameTime)
	{
		ExplosionData[] fire = particles;
		foreach (ExplosionData explosionData in fire)
		{
			explosionData.Update(gameTime);
			if (explosionData.lifetime > 0f)
			{
				Active = true;
			}
		}
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData explosionData2 in smoke)
		{
			explosionData2.Update(gameTime);
			if (explosionData2.lifetime > 0f)
			{
				Active = true;
			}
		}
	}
}
