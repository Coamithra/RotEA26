using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class PlayerShipSummon : AlienDrawableGameComponent
{
	private int player;

	private int countdown;

	private Timer countdowntimer = new Timer(1000f, repeating: true);

	private SpriteFont font;

	private Vibrator vibrator;

	private float spawndirection;

	private LazerGenerator effect;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType => b;

	protected override void LoadContent()
	{
		base.LoadContent();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public PlayerShipSummon(Game game)
		: base(game)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		LoadAnimation(new AnimationData("GFX/Game/blank"));
		((DrawableGameComponent)this).DrawOrder = 20;
		timers.Add(countdowntimer);
		((DrawableGameComponent)this).DrawOrder = 11;
		vibrator = ServiceHelper.Get<IVibratorService>().Vibrator;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this && effect != null)
		{
			effect.Free();
			effect = null;
		}
	}

	public static PlayerShipSummon NewPlayerShipSummon(ComponentBin collection, Game game)
	{
		PlayerShipSummon playerShipSummon = collection.Recycle<PlayerShipSummon>();
		if (playerShipSummon == null)
		{
			playerShipSummon = new PlayerShipSummon(game);
		}
		return playerShipSummon;
	}

	public void Setup(int player, float spawndirection, Vector2 position, int respawntimebonus)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		this.spawndirection = spawndirection;
		this.player = player;
		base.Position = position;
		effect = LazerGenerator.NewLazerGenerator(collection, ((GameComponent)this).Game);
		effect.Setup(base.Position, 3f, 2f, 0f, 0f);
		collection.Add((GameComponent)(object)effect);
		countdown = (int)Math.Round((float)(15 - respawntimebonus) * Settings.GetInstance().CurrentDifficulty switch
		{
			Settings.DifficultyLevel.Easy => 0.66f, 
			Settings.DifficultyLevel.Medium => 0.66f, 
			Settings.DifficultyLevel.Hard => 0.8f, 
			Settings.DifficultyLevel.Very_Hard => 0.8f, 
			Settings.DifficultyLevel.Inzane => 0.9f, 
			_ => 0.66f, 
		});
	}

	public override void Initialize()
	{
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)effect).Draw(gameTime);
		Vector2 origin = font.MeasureString(countdown.ToString()) * 1.2f / 2f;
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		spriteBatch.DrawString(font, countdown.ToString(), base.Position, Color.DarkGoldenrod, 0f, origin, 1.2f, (SpriteEffects)0, 1f);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		if (countdowntimer.Finished)
		{
			bool flag = true;
			PlayerIndex playerIndex;
			switch (oracle.Controller(player))
			{
			case ControlDevice.PadOne:
				playerIndex = (PlayerIndex)0;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			case ControlDevice.PadTwo:
				playerIndex = (PlayerIndex)1;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			case ControlDevice.PadThree:
				playerIndex = (PlayerIndex)2;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			case ControlDevice.PadFour:
				playerIndex = (PlayerIndex)3;
				if (Settings.GetInstance().GetPlayerSettings(oracle.Controller(player)).DisableRumble)
				{
					flag = false;
				}
				break;
			default:
				playerIndex = (PlayerIndex)0;
				flag = false;
				break;
			}
			countdown--;
			if (countdown <= 3 && countdown != 0 && flag)
			{
				float num = MathHelper.Lerp(0.35f, 0.35f, (float)countdown / 3f);
				vibrator.addVibration(new Vector2(0f, num), 500f, playerIndex);
			}
			if (countdown <= 0)
			{
				PlayerShip playerShip = collection.Recycle<PlayerShip>();
				if (playerShip == null)
				{
					playerShip = new PlayerShip(((GameComponent)this).Game);
				}
				playerShip.Setup(player, base.Position, startup: false, invulnerable: true, spawndirection);
				collection.Add((GameComponent)(object)playerShip);
				Die();
				effect.Free();
				effect = null;
				if (flag)
				{
					vibrator.addVibration(new Vector2(0.35f, 0.5f), 1500f, playerIndex);
				}
			}
		}
		base.Update(gameTime);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}
}
