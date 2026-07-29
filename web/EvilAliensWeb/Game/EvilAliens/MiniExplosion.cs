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
		foreach (ExplosionData particle in fire)
		{
			particle.Initialize(size, lifetime, Vector2.Zero);
		}
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData particle in smoke)
		{
			particle.Initialize(size, lifetime * 1.35f, Vector2.Zero);
		}
		this.position = position;
	}

	public void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData particle in smoke)
		{
			if (!(particle.lifetime <= 0f))
			{
				float alpha = 4f * particle.normalizedLifetime * (1f - particle.normalizedLifetime);
				Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
				spriteBatch.Draw(smoketexture, position + particle.position, particle.rotation, particle.scale, center: true, tint);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		ExplosionData[] fire = particles;
		foreach (ExplosionData particle in fire)
		{
			if (!(particle.lifetime <= 0f))
			{
				float alpha = 4f * particle.normalizedLifetime * (1f - particle.normalizedLifetime);
				Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
				spriteBatch.Draw(particletexture, position + particle.position, particle.rotation, particle.scale, center: true, tint);
			}
		}
	}

	public void Update(GameTime gameTime)
	{
		ExplosionData[] fire = particles;
		foreach (ExplosionData particle in fire)
		{
			particle.Update(gameTime);
			if (particle.lifetime > 0f)
			{
				Active = true;
			}
		}
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData particle in smoke)
		{
			particle.Update(gameTime);
			if (particle.lifetime > 0f)
			{
				Active = true;
			}
		}
	}
}
