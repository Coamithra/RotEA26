using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Powerup : AlienDrawableGameComponent
{
	public enum PowerupType
	{
		Blast,
		Option,
		FirePower,
		Range,
		Linker,
		OneUp
	}

	public bool taken;

	public PowerupType type;

	private Vector2 impulse;

	private Timer impulsetimer = new Timer(666f, repeating: false);

	private bool limitspeed;

	private float speedLimitedAt;

	private SpriteFont font;

	private Timer animtimer = new Timer(700f, repeating: true);

	private string p;

	private static List<PowerupType> powerupTypeValues = Game1.GetEnumValues<PowerupType>();

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.Height *= 1.6f;
			collisionBox.Width *= 1.6f;
			collisionBox.CenterAround(base.Position);
			return collisionBox;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public Powerup(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/powerupbw"));
		base.DrawOrder = 20;
		base.DrawOrder = 12;
		timers.Add(animtimer);
		timers.Add(impulsetimer);
	}

	public static Powerup NewPowerup(ComponentBin collection, Game game)
	{
		Powerup powerup = collection.Recycle<Powerup>();
		if (powerup == null)
		{
			powerup = new Powerup(game);
		}
		return powerup;
	}

	public void Setup(Vector2 position)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = position;
		Randomize();
		limitspeed = true;
		speedLimitedAt = 0.12f;
		impulsetimer.Stop();
	}

	public void DoNotLimitSpeed()
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		limitspeed = false;
		impulse = new Vector2(0f, -0.42f);
		impulsetimer.Start();
		impulsetimer.Reset();
	}

	public void SetSpeedLimit(float value)
	{
		speedLimitedAt = value;
	}

	public void Randomize()
	{
		int num = 0;
		bool flag = false;
		while (!flag)
		{
			num = RandomHelper.Random.Next(powerupTypeValues.Count);
			flag = true;
			if ((num == 4) & (oracle.Players <= 1))
			{
				flag = false;
			}
			if (num == 5 && (Score.Lives < 0 || RandomHelper.RandomNextFloat(0f, 100f) > 6f))
			{
				flag = false;
			}
		}
		MakeType((PowerupType)num);
	}

	public static Color PowerUpColor(PowerupType type)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		return (Color)(type switch
		{
			PowerupType.Blast => Color.Red, 
			PowerupType.Option => Color.Teal, 
			PowerupType.FirePower => Color.Gold, 
			PowerupType.Range => Color.Purple, 
			PowerupType.Linker => Color.LimeGreen, 
			PowerupType.OneUp => Color.Yellow, 
			_ => Color.CornflowerBlue, 
		});
	}

	public static float PowerUpHue(PowerupType type)
	{
		return type switch
		{
			PowerupType.Blast => 0f, 
			PowerupType.Option => 235f, 
			PowerupType.FirePower => 39f, 
			PowerupType.Range => 283f, 
			PowerupType.Linker => 120f, 
			PowerupType.OneUp => 62f, 
			_ => 218f, 
		};
	}

	public static string PowerUpString(PowerupType type)
	{
		return type switch
		{
			PowerupType.Blast => "B", 
			PowerupType.Option => "O", 
			PowerupType.FirePower => "F", 
			PowerupType.Range => "R", 
			PowerupType.Linker => "2", 
			PowerupType.OneUp => "1up", 
			_ => "ERRORZ!", 
		};
	}

	public void MakeType(PowerupType type)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		this.type = type;
		p = PowerUpString(type);
		color = PowerUpColor(type);
	}

	public override void Initialize()
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		color = new Color((color).R, (color).G, (color).B, (byte)204);
		base.Collides = true;
		taken = false;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		Vector2 origin = font.MeasureString(p) / 2f;
		origin.Y = origin.Y * 3f / 4f;
		spriteBatch.DrawString(font, p, base.Position, color, 0f, origin, scale, (SpriteEffects)0, 1f);
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		spriteBatch.DrawString(font, p, base.Position, Color.Gray, 0f, origin, scale, (SpriteEffects)0, 1f);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		if (limitspeed)
		{
			Vector2 backgroundSpeed = oracle.BackgroundSpeed;
			base.Speed = MathHelper.Min((backgroundSpeed).Length() + 0.018000001f, speedLimitedAt);
		}
		else
		{
			Vector2 backgroundSpeed2 = oracle.BackgroundSpeed;
			base.Speed = (backgroundSpeed2).Length();
		}
		base.Direction = MyMath.VectorToAngle(oracle.BackgroundSpeed);
		if (impulsetimer.Active)
		{
			base.SpeedVector += impulse * impulsetimer.Normalized;
		}
		scale = MathHelper.SmoothStep(1f, 1.2f, 1f - animtimer.Normalized);
		base.Update(gameTime);
		if ((base.Position.X > 1000f) | (base.Position.X < -200f) | (base.Position.Y > 800f) | (base.Position.Y < -200f))
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Wall)
		{
			impulsetimer.Stop();
		}
		if (other is PlayerShip)
		{
			Die();
		}
	}
}
