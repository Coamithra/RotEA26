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

	// Trello 8e439865: an explosion normally rattles the camera (see Initialize below); a
	// caller spawning a RAPID SERIES of them (e.g. BattleSkull's death flicker) can opt a given
	// instance OUT of that shake so only the series' actual finale contributes trauma. Defaults
	// false, so every existing call site (which doesn't pass the new Setup arg) is unaffected.
	private bool noShake;

	public override ICollisionType CollisionType
	{
		get
		{
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

	public void Setup(Vector2 position, float size, float lifetime, float impulse, float direction, bool noShake = false)
	{
		base.Position = position;
		this.size = size;
		this.lifetime = lifetime;
		base.Direction = direction;
		this.impulse = MyMath.AngleToVector(direction) * impulse;
		this.noShake = noShake;
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
		ExplosionData[] fire = particles;
		foreach (ExplosionData explosionData in fire)
		{
			explosionData.Initialize(size, lifetime, impulse);
		}
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData explosionData2 in smoke)
		{
			explosionData2.Initialize(size, lifetime * 1.35f, impulse * 0.85f);
		}
		base.Initialize();
		SmokeDrawer smokeDrawer = SmokeDrawer.NewSmokeDrawer(collection, base.Game);
		smokeDrawer.Setup(this);
		collection.Add((GameComponent)(object)smokeDrawer);
		backgroundimpulsetimer.Start();
		Vibrate();
		// Game juice: an explosion rattles the CAMERA as well as the pad — trauma scaled by
		// size, so a routine blast nudges (~0.1) while a player death / boss finale (several
		// size 2-3.5 explosions stacking) builds a real shake. The fixed camera shows the
		// whole arena, so no distance attenuation (unlike the per-player Vibrate above).
		// Skipped entirely when noShake (a rapid death-flicker series opting out — see the field).
		if (!noShake)
		{
			EvilAliensWeb.Compat.Juice.AddTrauma(0.05f + size * 0.06f);
		}
		curframe = 0f;
		rotation = RandomHelper.RandomNextAngle();
	}

	private void Vibrate()
	{
		Vector2 nearPower = default(Vector2);
		// Per SEATED slot, not 0..Players-1: online co-op's roster is host-allocated and sparse
		// (card 4d904410), and Oracle.GetPlayerPosition/Controller THROW on an unseated slot.
		for (int i = 0; i < Oracle.MaxPlayers; i++)
		{
			if (!oracle.IsSeated(i))
			{
				continue;
			}
			Vibrator vibrator = ServiceHelper.Get<IVibratorService>().Vibrator;
			if (size <= 1f)
			{
				(nearPower) = new Vector2(0f, 0.5f);
			}
			else
			{
				(nearPower) = new Vector2(0.5f, 0f);
			}
			Vector2 zero = Vector2.Zero;
			Vector2 toPlayer = base.Position - oracle.GetPlayerPosition(i);
			float distance = (toPlayer).Length();
			float rangeScale = (size - 1f) * 0.35f + 1f;
			Vector2 power = Vector2.Lerp(nearPower, zero, MathHelper.Clamp(distance / (200f * rangeScale), 0f, 1f));
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
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		ExplosionData[] fire = particles;
		foreach (ExplosionData explosionData in fire)
		{
			if (!(explosionData.lifetime <= 0f))
			{
				float alpha = 4f * explosionData.normalizedLifetime * (1f - explosionData.normalizedLifetime);
				Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
				Texture2D blastTexture = ((!blue) ? redblast : blueblast);
				spriteBatch.Draw(blastTexture, base.Position + explosionData.position, explosionData.rotation, explosionData.scale, center: true, tint);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	public void DrawSmoke(GameTime gameTime)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData explosionData in smoke)
		{
			if (!(explosionData.lifetime <= 0f))
			{
				float alpha = 4f * explosionData.normalizedLifetime * (1f - explosionData.normalizedLifetime);
				Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
				spriteBatch.Draw(smoketexture, base.Position + explosionData.position, explosionData.rotation, explosionData.scale, center: true, tint);
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
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
		bool anyAlive = false;
		ExplosionData[] fire = particles;
		foreach (ExplosionData explosionData in fire)
		{
			explosionData.Update(gameTime);
			if (explosionData.lifetime > 0f)
			{
				anyAlive = true;
			}
		}
		ExplosionData[] smoke = smokeparticles;
		foreach (ExplosionData explosionData2 in smoke)
		{
			explosionData2.Update(gameTime);
			if (explosionData2.lifetime > 0f)
			{
				anyAlive = true;
			}
		}
		base.Update(gameTime);
		if (!anyAlive)
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
