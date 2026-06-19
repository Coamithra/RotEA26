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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		this.impulse = impulse;
		float num = size * RandomHelper.RandomNextFloat(15f, 65f);
		float angle = RandomHelper.RandomNextAngle();
		this.lifetime = lifetime * RandomHelper.RandomNextFloat(350f, 800f);
		lifetimeinitial = this.lifetime;
		scale = size * (1f + RandomHelper.RandomNextFloat(-0.2f, 0.2f)) * 0.015f;
		scalespeed = size * 0.00025f;
		startposition = MyMath.AngleToVector(angle) * num;
		position = startposition;
		endposition = Vector2.Zero;
	}

	public void Update(GameTime gameTime)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (lifetime > 0f)
		{
			float num = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			position = endposition + (startposition - endposition) * normalizedLifetime;
			scale += scalespeed;
			lifetime -= num;
		}
	}
}
