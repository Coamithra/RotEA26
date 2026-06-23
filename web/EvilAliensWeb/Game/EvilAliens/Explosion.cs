using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Explosion : AlienDrawableGameComponent
{
	private bool blue;

	private Timer backgroundimpulsetimer = new Timer(500f, repeating: false);

	private Vector2 impulse = Vector2.Zero;

	private ExplosionData[] particles;

	private ExplosionData[] smokeparticles;

	private float size = 1f;

	private float lifetime = 1f;

	private Texture2D smoketexture;

	private Texture2D box;

	private Texture2D blueblast;

	private Texture2D redblast;

	private Timer collisiontimer = new Timer(700f, repeating: false);

	private CollisionSimpleCircle c = new CollisionSimpleCircle(Vector2.Zero, 1f);

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			c.Position = base.Position;
			c.Radius = 70f;
			return c;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		smoketexture = content.Load<Texture2D>("GFX/Sprites/smoke");
		box = content.Load<Texture2D>("GFX/Sprites/block");
		blueblast = content.Load<Texture2D>("GFX/Sprites/explosionpurple");
		redblast = content.Load<Texture2D>("GFX/Sprites/explosion");
	}

	public Explosion(Game game)
		: base(game)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		base.DrawOrder = 40;
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
		timers.Add(backgroundimpulsetimer);
		timers.Add(collisiontimer);
	}

	public static Explosion NewExplosion(ComponentBin collection, Game game)
	{
		Explosion explosion = collection.Recycle<Explosion>();
		if (explosion == null)
		{
			explosion = new Explosion(game);
		}
		return explosion;
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
		blue = false;
		collisiontimer.Stop();
		base.Collides = false;
		scale = size / 2f;
		fps = 30f / lifetime;
		if (fps < 25f)
		{
			interpolationOptions = InterpolationOptions.always;
		}
		else
		{
			interpolationOptions = InterpolationOptions.as_specified;
		}
	}

	public override void Initialize()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		ExplosionData[] array = particles;
		foreach (ExplosionData explosionData in array)
		{
			explosionData.Initialize(size, lifetime, impulse);
		}
		ExplosionData[] array2 = smokeparticles;
		foreach (ExplosionData explosionData2 in array2)
		{
			explosionData2.Initialize(size, lifetime * 1.35f, impulse * 0.85f);
		}
		base.Initialize();
		SmokeDrawer smokeDrawer = SmokeDrawer.NewSmokeDrawer(collection, base.Game);
		smokeDrawer.Setup(this);
		collection.Add((GameComponent)(object)smokeDrawer);
		backgroundimpulsetimer.Start();
		Vibrate();
		curframe = 0f;
		rotation = RandomHelper.RandomNextAngle();
	}

	private void Vibrate()
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = default(Vector2);
		for (int i = 0; i < oracle.Players; i++)
		{
			Vibrator vibrator = ServiceHelper.Get<IVibratorService>().Vibrator;
			if (size <= 1f)
			{
				(val) = new Vector2(0f, 0.5f);
			}
			else
			{
				(val) = new Vector2(0.5f, 0f);
			}
			Vector2 zero = Vector2.Zero;
			Vector2 val2 = base.Position - oracle.GetPlayerPosition(i);
			float num = (val2).Length();
			float num2 = (size - 1f) * 0.35f + 1f;
			Vector2 power = Vector2.Lerp(val, zero, MathHelper.Clamp(num / (200f * num2), 0f, 1f));
			PlayerIndex playerIndex;
			switch (oracle.Controller(i))
			{
			case ControlDevice.PadOne:
				playerIndex = (PlayerIndex)0;
				break;
			case ControlDevice.PadTwo:
				playerIndex = (PlayerIndex)1;
				break;
			case ControlDevice.PadThree:
				playerIndex = (PlayerIndex)2;
				break;
			case ControlDevice.PadFour:
				playerIndex = (PlayerIndex)3;
				break;
			default:
				continue;
			}
			if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(i)).DisableRumble)
			{
				break;
			}
			if (oracle.IsAlive(i))
			{
				vibrator.addVibration(power, lifetime * 600f, playerIndex);
			}
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		ExplosionData[] array = particles;
		foreach (ExplosionData explosionData in array)
		{
			if (!(explosionData.lifetime <= 0f))
			{
				float num = 4f * explosionData.normalizedLifetime * (1f - explosionData.normalizedLifetime);
				Color val2 = new Color(new Vector4(1f, 1f, 1f, num));
				Texture2D val3 = ((!blue) ? redblast : blueblast);
				spriteBatch.Draw(val3, base.Position + explosionData.position, explosionData.rotation, explosionData.scale, center: true, val2);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	public void DrawSmoke(GameTime gameTime)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		ExplosionData[] array = smokeparticles;
		foreach (ExplosionData explosionData in array)
		{
			if (!(explosionData.lifetime <= 0f))
			{
				float num = 4f * explosionData.normalizedLifetime * (1f - explosionData.normalizedLifetime);
				Color val = new Color(new Vector4(1f, 1f, 1f, num));
				spriteBatch.Draw(smoketexture, base.Position + explosionData.position, explosionData.rotation, explosionData.scale, center: true, val);
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = MathHelper.Lerp((backgroundSpeed).Length() * 0.45f, 0f, backgroundimpulsetimer.Normalized);
		base.Direction = MyMath.VectorToAngle(oracle.BackgroundSpeed);
		if (collisiontimer.Active && collisiontimer.TimeElapsed > 200f)
		{
			base.Collides = true;
		}
		else
		{
			base.Collides = false;
		}
		bool flag = false;
		ExplosionData[] array = particles;
		foreach (ExplosionData explosionData in array)
		{
			explosionData.Update(gameTime);
			if (explosionData.lifetime > 0f)
			{
				flag = true;
			}
		}
		ExplosionData[] array2 = smokeparticles;
		foreach (ExplosionData explosionData2 in array2)
		{
			explosionData2.Update(gameTime);
			if (explosionData2.lifetime > 0f)
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

	internal void MakeBlue()
	{
		blue = true;
		collisiontimer.Reset();
		collisiontimer.Start();
	}
}
