using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class PowerupEffectData
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		this.impulse = impulse;
		float angle = RandomHelper.RandomNextAngle();
		float num = size * RandomHelper.RandomNextFloat(0f, 5f);
		this.lifetime = lifetime * RandomHelper.RandomNextFloat(500f, 650f);
		lifetimeinitial = this.lifetime;
		rotationspeed = RandomHelper.RandomNextFloat(-0.001f, 0.001f);
		scale = size * (1f + RandomHelper.RandomNextFloat(-0.05f, 0.05f));
		scalespeed = size * 0.00017f;
		position = MyMath.AngleToVector(angle) * num;
		speed = Vector2.Zero;
		speed = size * MyMath.AngleToVector(RandomHelper.RandomNextAngle()) * RandomHelper.RandomNextFloat(0.08f, 0.08f);
		ServiceHelper.Get<ISoundManagerService>().SoundManager.PlayCue("usepowerup");
	}

	public void Update(GameTime gameTime)
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		if (lifetime > 0f)
		{
			float num = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			rotation += num * rotationspeed;
			position += num * speed + num * impulse;
			scale += scalespeed;
			lifetime -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		}
	}
}
