using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class LazerGeneratorData
{
	public float lifetime;

	public float lifetimeinitial;

	public float scale;

	public float scalespeed;

	public Vector2 startposition;

	public Vector2 position;

	public Vector2 endposition;

	public Vector2 impulse;

	public float normalizedLifetime => lifetime / lifetimeinitial;

	public void Initialize(float size, float lifetime, Vector2 impulse)
	{
		this.impulse = impulse;
		float num = size * RandomHelper.RandomNextFloat(15f, 65f);
		float angle = RandomHelper.RandomNextAngle();
		this.lifetime = lifetime * RandomHelper.RandomNextFloat(350f, 800f);
		lifetimeinitial = this.lifetime;
		// Base per-particle scale only; the live chargeup ramp (1 -> peak over the windup) is applied
		// at DRAW time in LazerGenerator.Draw so the whole swarm ramps crisply rather than lagging by
		// a particle lifetime.
		scale = size * (1f + RandomHelper.RandomNextFloat(-0.2f, 0.2f)) * 0.015f;
		scalespeed = size * 0.00025f;
		startposition = MyMath.AngleToVector(angle) * num;
		position = startposition;
		endposition = Vector2.Zero;
	}

	public void Update(GameTime gameTime)
	{
		if (lifetime > 0f)
		{
			float num = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			position = endposition + (startposition - endposition) * normalizedLifetime;
			scale += scalespeed;
			lifetime -= num;
		}
	}
}
