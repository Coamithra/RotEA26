using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class ExplosionData
{
	public float lifetime;

	public float lifetimeinitial;

	public float rotation;

	public float rotationspeed;

	public float scale;

	public float scalespeed;

	public Vector2 position;

	public Vector2 speed;

	public Vector2 impulse;

	public float normalizedLifetime => lifetime / lifetimeinitial;

	public void Initialize(float size, float lifetime, Vector2 impulse)
	{
		this.impulse = impulse;
		float angle = RandomHelper.RandomNextAngle();
		float num = size * RandomHelper.RandomNextFloat(0f, 5f);
		this.lifetime = lifetime * RandomHelper.RandomNextFloat(350f, 800f);
		lifetimeinitial = this.lifetime;
		rotation = RandomHelper.RandomNextAngle();
		rotationspeed = RandomHelper.RandomNextFloat(-0.001f, 0.001f);
		scale = size * (0.2f + RandomHelper.RandomNextFloat(-0.05f, 0.05f));
		scalespeed = size * 0.00017f;
		position = MyMath.AngleToVector(angle) * num;
		speed = Vector2.Zero;
		speed = size * MyMath.AngleToVector(RandomHelper.RandomNextAngle()) * RandomHelper.RandomNextFloat(0.001f, 0.015f);
	}

	public void Update(GameTime gameTime)
	{
		float num = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		rotation += num * rotationspeed;
		position += num * speed + num * impulse;
		scale += scalespeed;
		lifetime -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
	}
}
