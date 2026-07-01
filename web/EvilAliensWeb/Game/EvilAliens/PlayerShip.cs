using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class PlayerShip : AlienDrawableGameComponent
{
	public delegate void CollectPowerupEvent(Powerup.PowerupType powerup);

	private const int shotspersecdefault = 8;

	private const int shotspersecmax = 18;

	private const float bulletlifetimedefault = 450f;

	private const float bulletlifetimemax = 1500f;

	private const float bulletlifetimeperpowerup = 70f;

	private bool asplodeOnNextFrame;

	private ICollidable asplosionCauser;

	private Timer pacifistTimer = new Timer(90000f, repeating: false);

	private bool isTutorial;

	private int player;

	private float hue;

	private int shotspersec;

	private float startdir;

	private Texture2D gloweffect;

	private float bulletlifetime;

	private int respawntimebonus;

	private float asplodingbulletspercentage;

	private float asplodingbulletssize;

	private float bouncebulletspercentage;

	private int bounceamount;

	private int bulletsSplit;

	private Powerup.PowerupType currentPower;

	private bool haspower;

	private PowerupEffect powerupEffect;

	private Blast blast;

	private bool readyToConnect;

	private List<ShipConnector> connectors = new List<ShipConnector>();

	private bool hasWon;

	private List<Option>[] options;

	private Timer invulnerabilityTimer = new Timer(2500f, repeating: false);

	public Vector2 TopLeft;

	public Vector2 BottomRight;

	private CollisionBox boundBox;

	private Timer shoottimer;

	private Timer starttimer;

	private ControlDevice controller;

	private DeathEvent deathEvent;

	private int optionLevel;

	public int Owner => player;

	public ControlDevice Controller => controller;

	public int OptionLevel => optionLevel;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002e: Unknown result type (might be due to invalid IL or missing references)
			boundBox.TopLeft = base.Position + TopLeft;
			boundBox.BottomRight = base.Position + BottomRight;
			return boundBox;
		}
	}

	public event CollectPowerupEvent OnCollectPowerup;

	public PlayerShip(Game game)
		: base(game)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		LoadAnimation(new AnimationData("GFX/Sprites/playersheet", 4, 8, 1, 6f));
		interpolationOptions = InterpolationOptions.always;
		base.DrawOrder = 20;
		boundBox = new CollisionBox(Vector2.Zero, Vector2.Zero);
		starttimer = new Timer(520f, repeating: false);
		shoottimer = new Timer(125f, repeating: true);
		shoottimer.Stop();
		AddTimer(shoottimer);
		AddTimer(starttimer);
		AddTimer(invulnerabilityTimer);
		options = new List<Option>[2];
		options[0] = new List<Option>();
		options[1] = new List<Option>();
		deathEvent = PlayerShip_OnDeath;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent is Option)
		{
			List<Option>[] array = options;
			foreach (List<Option> list in array)
			{
				if (list.Contains((Option)(object)e.GameComponent))
				{
					list.Remove((Option)(object)e.GameComponent);
					RedressOptions();
				}
			}
		}
		if (e.GameComponent == powerupEffect)
		{
			powerupEffect = null;
		}
		if (e.GameComponent == blast)
		{
			blast = null;
		}
		if (e.GameComponent is ShipConnector && connectors.Contains((ShipConnector)(object)e.GameComponent))
		{
			connectors.Remove((ShipConnector)(object)e.GameComponent);
			if (connectors.Count == 0)
			{
				readyToConnect = false;
			}
		}
		if (e.GameComponent == this)
		{
			this.OnCollectPowerup = null;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		gloweffect = content.Load<Texture2D>("GFX/Sprites/singleconnectorglow");
	}

	private void RedressOptions()
	{
		List<Option>[] array = options;
		foreach (List<Option> list in array)
		{
			for (int j = 0; j < list.Count; j++)
			{
				float angle = (float)j * ((float)Math.PI * 2f) / (float)list.Count;
				list[j].SetAngle(angle);
			}
		}
	}

	private void PlayerShip_OnDeath(object sender)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		PlayerShipSummon playerShipSummon = PlayerShipSummon.NewPlayerShipSummon(collection, base.Game);
		playerShipSummon.Setup(player, startdir, base.Position, respawntimebonus);
		collection.Add((GameComponent)(object)playerShipSummon);
	}

	public Vector2 GetPosition()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return base.Position;
	}

	public void SetPosition(Vector2 newposition)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		base.Position = newposition;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		if (hue != -1f)
		{
			spriteBatch.colorizeEffect.Enable();
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(180f, 250f, hue);
		}
		if (oracle.Players == 1 && haspower)
		{
			spriteBatch.colorizeEffect.Enable();
			if (currentPower == Powerup.PowerupType.OneUp)
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(100f, 280f, 250f * (float)gameTime.TotalGameTime.TotalSeconds % 360f);
			}
			else
			{
				spriteBatch.colorizeEffect.RangeTarget = new Vector3(10f, 360f, Powerup.PowerUpHue(currentPower));
			}
		}
		if (invulnerabilityTimer.Active & (MyMath.Mod(invulnerabilityTimer.TimeElapsed, 100f) <= 50f))
		{
			spriteBatch.lightenEffect.Enable();
		}
		if (readyToConnect)
		{
			spriteBatch.BlendMode = (SpriteBlendMode)2;
			spriteBatch.Draw(gloweffect, base.Position, 0f, 1f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/singleconnectorglow", gloweffect.Width), center: true, Color.White);
			spriteBatch.BlendMode = (SpriteBlendMode)1;
		}
		base.Draw(gameTime);
		spriteBatch.lightenEffect.Disable();
		spriteBatch.colorizeEffect.Disable();
	}

	public void Setup(int player, Vector2 position, bool startup, bool invulnerable, float startdirection)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		pacifistTimer.Reset();
		pacifistTimer.Start();
		startdir = startdirection;
		base.Position = position;
		if (startup)
		{
			starttimer.Start();
		}
		else
		{
			starttimer.Stop();
		}
		this.player = player;
		controller = oracle.Controller(player);
		hue = oracle.Hue(player);
		if (invulnerable)
		{
			TemporaryInvulnerability();
		}
		else
		{
			invulnerabilityTimer.Stop();
		}
		bounceamount = 1;
		bulletsSplit = 0;
		bouncebulletspercentage = 0f;
		asplodingbulletspercentage = 0f;
		shotspersec = 8;
		bulletlifetime = 450f;
		List<Option>[] array = options;
		foreach (List<Option> list in array)
		{
			list.Clear();
		}
	}

	public void SetTutorial()
	{
		isTutorial = true;
	}

	public override void Initialize()
	{
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		optionLevel = 0;
		asplodeOnNextFrame = false;
		isTutorial = false;
		respawntimebonus = 0;
		readyToConnect = false;
		haspower = false;
		Score.ResetPowerup(player);
		invulnerabilityTimer.Reset();
		shoottimer.Duration = 1000f / (float)shotspersec;
		base.MaxSpeed = 0.33f;
		base.Deceleration = 0.0047999998f;
		base.Acceleration = 0.003f;
		CollisionBox collisionBox = retrieveBoundsFromTexture();
		TopLeft = collisionBox.TopLeft;
		BottomRight = collisionBox.BottomRight;
		starttimer.Reset();
		shoottimer.Reset();
		shoottimer.Stop();
		base.Initialize();
		hasWon = false;
		base.OnDeath += deathEvent;
		color = Color.White;
		if (Settings.GetInstance().PowerUp)
		{
			PowerUp();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_0400: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0301: Unknown result type (might be due to invalid IL or missing references)
		//IL_0303: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0339: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_0365: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_039b: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0288: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0293: Unknown result type (might be due to invalid IL or missing references)
		if (asplodeOnNextFrame)
		{
			if (asplosionCauser != null)
			{
				Asplode();
				return;
			}
			asplodeOnNextFrame = false;
		}
		if (!isTutorial && controller != ControlDevice.AI && Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard)
		{
			pacifistTimer.Update(gameTime);
		}
		if (pacifistTimer.Finished)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Pacifist);
			pacifistTimer.Reset();
		}
		if (powerupEffect != null)
		{
			powerupEffect.SetPosition(base.Position);
		}
		if (blast != null)
		{
			blast.SetPosition(base.Position);
		}
		if (!hasWon)
		{
			if (starttimer.Active)
			{
				Move((float?)startdir, gameTime);
			}
			else
			{
				Vector2 direction = Vector2.Zero;
				switch (controller)
				{
				case ControlDevice.PadOne:
				case ControlDevice.PadTwo:
				case ControlDevice.PadThree:
				case ControlDevice.PadFour:
				{
					int i = controller switch
					{
						ControlDevice.PadOne => 0, 
						ControlDevice.PadTwo => 1, 
						ControlDevice.PadThree => 2, 
						ControlDevice.PadFour => 3, 
						_ => throw new Exception(), 
					};
					Vector2 val = input.LeftStick(i);
					if ((val).LengthSquared() > 0.09f)
					{
						direction = input.LeftStick(i);
					}
					Vector2 val2 = input.RightStick(i);
					if ((val2).LengthSquared() > 0.0025000002f)
					{
						FireAt(MyMath.VectorToAngle(input.RightStick(i)));
					}
					else if (shoottimer.Finished)
					{
						shoottimer.Stop();
						shoottimer.Reset();
					}
					if (input.PadPressed(PadKeys.LTRT, i))
					{
						doBlast();
					}
					break;
				}
				case ControlDevice.Keyboard:
					if (input.Down(MyKeys.Down))
					{
						direction.Y += 1f;
					}
					if (input.Down(MyKeys.Up))
					{
						direction.Y -= 1f;
					}
					if (input.Down(MyKeys.Right))
					{
						direction.X += 1f;
					}
					if (input.Down(MyKeys.Left))
					{
						direction.X -= 1f;
					}
					if (input.Pressed(MyKeys.Mouse2))
					{
						doBlast();
					}
					if (input.Down(MyKeys.Mouse1))
					{
						float direction2 = MyMath.VectorToAngle(input.MousePosition - base.Position);
						FireAt(direction2);
					}
					else if (shoottimer.Finished)
					{
						shoottimer.Stop();
						shoottimer.Reset();
					}
					break;
				case ControlDevice.AI:
				{
					// Perf batch 2: GetBaddies() rebuilds its list by scanning every game
					// component; it was called three times per AI ship per frame (DoAIMove,
					// DoAIFire, doAIBomb). Build it once and thread it through — the component
					// set can't change mid-frame (adds/removes are deferred to ComponentBin.Update).
					List<AlienDrawableGameComponent> baddies = oracle.GetBaddies();
					DoAIMove(ref direction, gameTime, baddies);
					DoAIFire(gameTime, baddies);
					break;
				}
				}
				Move(direction, gameTime);
			}
			base.Update(gameTime);
			if (!starttimer.Active)
			{
				Vector2 position = base.Position;
				if (base.Position.X > 800f - BottomRight.X)
				{
					position.X = 800f - BottomRight.X;
				}
				if (base.Position.X < 0f - TopLeft.X)
				{
					position.X = 0f - TopLeft.X;
				}
				if (base.Position.Y > 600f - BottomRight.Y)
				{
					position.Y = 600f - BottomRight.Y;
				}
				if (base.Position.Y < 0f - TopLeft.Y)
				{
					position.Y = 0f - TopLeft.Y;
				}
				base.Position = position;
			}
		}
		else
		{
			base.MaxSpeed = 0.33f;
			Move((float?)startdir, gameTime);
			base.Update(gameTime);
		}
		oracle.SetPlayerPosition(player, base.Position);
	}

	private void doBlast()
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		if (Score.NrBombs(player) > 0)
		{
			Score.RemoveBomb(player);
			blast = Blast.NewBlast(collection, base.Game);
			blast.Setup(base.Position, Score.GetPowerupLevel(Powerup.PowerupType.Blast, player), player);
			collection.Add((GameComponent)(object)blast);
			sound.PlayCue("blast");
		}
	}

	private void DoAIFire(GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01af: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		float num = (float)Math.PI / 12f;
		float num2 = float.MaxValue;
		AlienDrawableGameComponent alienDrawableGameComponent = null;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (baddy is UFO || baddy is Braineroid || (baddy is Ball && ((Ball)baddy).IsConnected()) || baddy is JunkBoss || baddy is Boss || baddy is Spider || baddy is MarsBoss || baddy is DeathStar || baddy is ClassicBoss || baddy is BattleSkull || (baddy is FlyingSpider && baddy.Collides) || baddy is StarMine || (baddy is EvilSkull && !((EvilSkull)baddy).Fading) || baddy is SweepUFO)
			{
				if (isBlastable(baddy) && blast != null && blast.Collides)
				{
					break;
				}
				Vector2 val = baddy.Position - base.Position;
				if ((val).LengthSquared() < num2 && baddy.Position.X > 0f && baddy.Position.X < 800f && baddy.Position.Y > 0f && baddy.Position.Y < 600f)
				{
					Vector2 val2 = baddy.Position - base.Position;
					num2 = (val2).LengthSquared();
					alienDrawableGameComponent = baddy;
				}
			}
		}
		num2 = (float)Math.Sqrt(num2);
		if (num2 <= bulletlifetime * 0.78f)
		{
			if (alienDrawableGameComponent is JunkBoss)
			{
				FireAt(MyMath.VectorToAngle(alienDrawableGameComponent.Position - base.Position));
			}
			else
			{
				FireAt(MyMath.VectorToAngle(alienDrawableGameComponent.Position - base.Position) + RandomHelper.RandomNextFloat(0f - num, num));
			}
		}
		doAIBomb(baddies);
	}

	private void doAIBomb(List<AlienDrawableGameComponent> baddies)
	{
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		if (blast != null)
		{
			return;
		}
		int num;
		switch (Score.NrBombs(player))
		{
		case 0:
			return;
		case 1:
			num = 10;
			break;
		case 2:
			num = 7;
			break;
		case 3:
			num = 4;
			break;
		default:
			num = 4;
			break;
		}
		int num2 = 0;
		float num3 = 200 * (1 + Score.GetPowerupLevel(Powerup.PowerupType.Blast, player));
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (isBlastable(baddy))
			{
				Vector2 val = baddy.Position - base.Position;
				if ((val).LengthSquared() <= num3 * num3)
				{
					num2++;
				}
			}
		}
		if (num2 >= num)
		{
			doBlast();
		}
	}

	private bool isBlastable(AlienDrawableGameComponent alien)
	{
		if (!(alien is EvilBullet) && (!(alien is UFO) || ((UFO)alien).IsBig) && (!(alien is Braineroid) || !(alien.scale < 0.1f)))
		{
			return alien is EvilSkull;
		}
		return true;
	}

	private void DoAIMove(ref Vector2 direction, GameTime gameTime, List<AlienDrawableGameComponent> baddies)
	{
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_05fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_042d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0434: Unknown result type (might be due to invalid IL or missing references)
		//IL_0439: Unknown result type (might be due to invalid IL or missing references)
		//IL_043e: Unknown result type (might be due to invalid IL or missing references)
		//IL_03dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0403: Unknown result type (might be due to invalid IL or missing references)
		//IL_0408: Unknown result type (might be due to invalid IL or missing references)
		//IL_0610: Unknown result type (might be due to invalid IL or missing references)
		//IL_0485: Unknown result type (might be due to invalid IL or missing references)
		//IL_048c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0491: Unknown result type (might be due to invalid IL or missing references)
		//IL_0496: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0626: Unknown result type (might be due to invalid IL or missing references)
		//IL_0529: Unknown result type (might be due to invalid IL or missing references)
		//IL_0530: Unknown result type (might be due to invalid IL or missing references)
		//IL_0535: Unknown result type (might be due to invalid IL or missing references)
		//IL_053a: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0504: Unknown result type (might be due to invalid IL or missing references)
		//IL_0509: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_063c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0572: Unknown result type (might be due to invalid IL or missing references)
		//IL_057a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0581: Unknown result type (might be due to invalid IL or missing references)
		//IL_0586: Unknown result type (might be due to invalid IL or missing references)
		//IL_0593: Unknown result type (might be due to invalid IL or missing references)
		//IL_0598: Unknown result type (might be due to invalid IL or missing references)
		//IL_059d: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_028c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0296: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0216: Unknown result type (might be due to invalid IL or missing references)
		//IL_0317: Unknown result type (might be due to invalid IL or missing references)
		//IL_0321: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0252: Unknown result type (might be due to invalid IL or missing references)
		//IL_025f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_034d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0352: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0701: Unknown result type (might be due to invalid IL or missing references)
		//IL_070a: Unknown result type (might be due to invalid IL or missing references)
		//IL_070d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0712: Unknown result type (might be due to invalid IL or missing references)
		//IL_0717: Unknown result type (might be due to invalid IL or missing references)
		//IL_036a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0376: Unknown result type (might be due to invalid IL or missing references)
		//IL_037b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0380: Unknown result type (might be due to invalid IL or missing references)
		//IL_072d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0733: Unknown result type (might be due to invalid IL or missing references)
		//IL_0738: Unknown result type (might be due to invalid IL or missing references)
		//IL_073d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0724: Unknown result type (might be due to invalid IL or missing references)
		//IL_0729: Unknown result type (might be due to invalid IL or missing references)
		//IL_0833: Unknown result type (might be due to invalid IL or missing references)
		//IL_0838: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_09be: Unknown result type (might be due to invalid IL or missing references)
		//IL_0757: Unknown result type (might be due to invalid IL or missing references)
		//IL_075d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0762: Unknown result type (might be due to invalid IL or missing references)
		//IL_0767: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_09de: Unknown result type (might be due to invalid IL or missing references)
		//IL_09e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_09e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_09f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_09f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_09fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_09ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_07af: Unknown result type (might be due to invalid IL or missing references)
		//IL_07b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0782: Unknown result type (might be due to invalid IL or missing references)
		//IL_0788: Unknown result type (might be due to invalid IL or missing references)
		//IL_078d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0792: Unknown result type (might be due to invalid IL or missing references)
		//IL_068b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0692: Unknown result type (might be due to invalid IL or missing references)
		//IL_0697: Unknown result type (might be due to invalid IL or missing references)
		//IL_069c: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a29: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a99: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a41: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e60: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e65: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c59: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c65: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c6a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b27: Unknown result type (might be due to invalid IL or missing references)
		//IL_0abc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a73: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a84: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a89: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a8e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a93: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a5d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dbb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dcd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dd2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d15: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d22: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d27: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ca8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cb4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cb9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b97: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b3f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b01: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b12: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b17: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b1c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b21: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ae4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e0a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e1c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e21: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d65: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d72: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d77: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bb4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b71: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b82: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b87: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b8c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b91: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b5b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bf6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c07: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c0c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c11: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c16: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bd9: Unknown result type (might be due to invalid IL or missing references)
		CollisionLevelMap collisionLevelMap = null;
		bool flag = false;
		bool flag2 = false;
		float num = 150f;
		float num2 = 0f;
		float num3 = 4f;
		Vector2 position = default(Vector2);
		(position) = new Vector2(float.MaxValue, float.MaxValue);
		float num4 = 0f;
		if (player == 0)
		{
			num4 = (float)Math.PI / 16f;
		}
		if (player == 1)
		{
			num4 = -(float)Math.PI / 16f;
		}
		if (player == 2)
		{
			num4 = (float)Math.PI / 6f;
		}
		if (player == 3)
		{
			num4 = -(float)Math.PI / 6f;
		}
		Vector2 val;
		foreach (AlienDrawableGameComponent baddy in baddies)
		{
			if (isBlastable(baddy) && blast != null && blast.Collides)
			{
				val = baddy.Position - base.Position;
				float num5 = (val).LengthSquared();
				Vector2 val2 = position - base.Position;
				if (num5 < (val2).LengthSquared())
				{
					position = baddy.Position;
				}
				continue;
			}
			if (baddy is JunkBoss)
			{
				position = baddy.Position;
			}
			if (baddy is Wall)
			{
				flag = true;
				float num6 = 1.2f * (float)gameTime.ElapsedGameTime.TotalMilliseconds * base.MaxSpeed;
				float num7 = 0f;
				if (player == 0)
				{
					num7 = 8f;
				}
				if (player == 1)
				{
					num7 = 4f;
				}
				if (player == 2)
				{
					num7 = 6f;
				}
				if (player == 3)
				{
					num7 = 10f;
				}
				collisionLevelMap = (CollisionLevelMap)((Wall)baddy).GetCollisionType();
				CollisionBox collisionBox = (CollisionBox)GetCollisionType();
				int x = 0;
				int y = 0;
				collisionLevelMap.GetMapCoords(ref x, ref y, base.Position);
				int target_x = 0;
				int target_y = 0;
				findNextTileOnMap(x, y, ref target_x, ref target_y, collisionLevelMap);
				if (target_y < y)
				{
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Left - num6, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(num7, 0f);
					}
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Right + num6, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(0f - num7, 0f);
					}
				}
				else if (target_x > x)
				{
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Left - num6, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(num7, 0f);
					}
					if (collisionLevelMap.TileIsOccupied(target_x, y - 1))
					{
						direction += new Vector2(0f, num7);
					}
				}
				else if (target_x < x)
				{
					collisionLevelMap.GetMapCoords(ref x, ref y, new Vector2(collisionBox.Right + num6, base.Position.Y));
					if (collisionLevelMap.TileIsOccupied(x, y - 1))
					{
						direction += new Vector2(0f - num7, 0f);
					}
					if (collisionLevelMap.TileIsOccupied(target_x, y - 1))
					{
						direction += new Vector2(0f, num7);
					}
				}
				else if (target_x != x)
				{
				}
			}
			else if (baddy is Lazer)
			{
				getDistanceToLine(baddy, out var d, out var shortestpoint);
				if (d <= num)
				{
					float num8 = MyMath.PowerCurve(num3, num2, 2f, d / num);
					if (flag2)
					{
						num8 = MathHelper.Lerp(num3, num2, d / num);
					}
					direction += num8 * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - shortestpoint) + num4);
				}
			}
			else
			{
				if (!baddy.Collides)
				{
					continue;
				}
				float num9;
				if (baddy.GetCollisionType() is CollisionBox)
				{
					Vector2 val3 = base.Position - baddy.Position;
					num9 = (val3).Length() - ((CollisionBox)baddy.GetCollisionType()).Width / 2f * (float)Math.Sqrt(2.0);
				}
				else if (baddy.GetCollisionType() is CollisionMultibox)
				{
					Vector2 val4 = base.Position - baddy.Position;
					num9 = (val4).Length() - ((CollisionMultibox)baddy.GetCollisionType()).Items[0].Width / 2f * (float)Math.Sqrt(2.0);
				}
				else if (baddy.GetCollisionType() is CollisionSimpleCircle)
				{
					float radius = ((CollisionSimpleCircle)baddy.GetCollisionType()).Radius;
					Vector2 val5 = base.Position - baddy.Position;
					num9 = MathHelper.Clamp((val5).Length() - radius, 0f, 1000f);
				}
				else
				{
					Vector2 val6 = base.Position - baddy.Position;
					num9 = (val6).Length();
				}
				if (num9 <= num)
				{
					float num10 = MyMath.PowerCurve(num3, num2, 2f, num9 / num);
					if (flag2)
					{
						num10 = MathHelper.Lerp(num3, num2, num9 / num);
					}
					direction += num10 * MyMath.AngleToVector(MyMath.VectorToAngle(base.Position - baddy.Position) + num4);
				}
			}
		}
		foreach (Powerup powerup in oracle.GetPowerups())
		{
			if ((powerup.type == Powerup.PowerupType.Linker && readyToConnect) || !(powerup.Position.X > 0f) || !(powerup.Position.X < 800f) || !(powerup.Position.Y > 0f) || !(powerup.Position.Y < 600f))
			{
				continue;
			}
			bool flag3 = wantsToTakePowerup(powerup);
			if (flag3)
			{
				foreach (PlayerShip ship in oracle.GetShips())
				{
					if (ship.wantsToTakePowerup(powerup))
					{
						Vector2 val7 = ship.Position - powerup.Position;
						float num11 = (val7).LengthSquared();
						Vector2 val8 = base.Position - powerup.Position;
						if (num11 < (val8).LengthSquared() && !isConnectedWith(ship))
						{
							flag3 = false;
						}
					}
				}
			}
			if (!flag3)
			{
				continue;
			}
			Vector2 val9 = powerup.Position - base.Position;
			float num12 = (val9).Length();
			Vector2 val10 = position - base.Position;
			if (num12 < (val10).Length())
			{
				position = powerup.Position;
			}
			Vector2 val11 = powerup.Position - base.Position;
			if ((val11).Length() <= num)
			{
				Vector2 val12 = powerup.Position - base.Position;
				float num13 = MyMath.PowerCurve(num3, num2, 2f, (val12).Length() / num);
				if (flag2)
				{
					Vector2 val13 = powerup.Position - base.Position;
					num13 = MathHelper.Lerp(num3, num2, (val13).Length() / num);
				}
				direction += num13 * MyMath.AngleToVector(MyMath.VectorToAngle(powerup.Position - base.Position));
			}
		}
		foreach (PlayerShip ship2 in oracle.GetShips())
		{
			if (ship2.readyToConnect && ship2 != this && readyToConnect && !isConnectedWith(ship2))
			{
				position = ship2.Position;
			}
		}
		if (position.X > 2000f && !collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			if (oracle.LiveShips == 1)
			{
				if (collection.ContainsType<Wall>())
				{
					(position) = new Vector2(400f, 300f);
				}
				else
				{
					(position) = new Vector2(400f, 400f);
				}
			}
			else if (collection.ContainsType<Wall>())
			{
				float num14 = 800 / (oracle.Players + 1);
				(position) = new Vector2((float)(player + 1) * num14, 300f);
			}
			else
			{
				float num15 = 800 / (oracle.Players + 1);
				(position) = new Vector2((float)(player + 1) * num15, 400f);
			}
		}
		if (position.X > 2000f && collection.ContainsType<Floor>() && connectors.Count == 0)
		{
			if (oracle.LiveShips == 1)
			{
				(position) = new Vector2(266f, 300f);
			}
			else
			{
				(position) = new Vector2(266f, 600f / (float)(oracle.Players + 1) * (float)(player + 1));
			}
		}
		if (position.X < 2000f)
		{
			val = base.Position - position;
			float num16 = (val).Length();
			if (num16 > 10f)
			{
				direction += 0.8f * MyMath.AngleToVector(MyMath.VectorToAngle(position - base.Position));
			}
		}
		float num17 = num;
		float num18 = 600f;
		if (collection.ContainsType<Floor>())
		{
			num18 = 560f;
		}
		if (!flag2)
		{
			if (base.Position.X < num17)
			{
				float num19 = MyMath.PowerCurve(num3, num2, 2f, base.Position.X / num17);
				if (flag2)
				{
					num19 = MathHelper.Lerp(num3, num2, base.Position.X / num17);
				}
				direction += num19 * new Vector2(1f, 0f);
			}
			if (base.Position.X > 800f - num17)
			{
				float num20 = MyMath.PowerCurve(num3, num2, 2f, Math.Abs((800f - base.Position.X) / num17));
				if (flag2)
				{
					num20 = MathHelper.Lerp(num3, num2, Math.Abs((800f - base.Position.X) / num17));
				}
				direction += num20 * new Vector2(-1f, 0f);
			}
			if (base.Position.Y < num17)
			{
				float num21 = MyMath.PowerCurve(num3, num2, 2f, base.Position.Y / num17);
				if (flag2)
				{
					num21 = MathHelper.Lerp(num3, num2, base.Position.Y / num17);
				}
				direction += num21 * new Vector2(0f, 1f);
			}
			if (base.Position.Y > num18 - num17)
			{
				float num22 = MyMath.PowerCurve(num3, num2, 2f, Math.Abs((num18 - base.Position.Y) / num17));
				if (flag2)
				{
					num22 = MathHelper.Lerp(num3, num2, Math.Abs((num18 - base.Position.Y) / num17));
				}
				direction += num22 * new Vector2(0f, -1f);
			}
		}
		if (flag)
		{
			CollisionBox collisionBox2 = (CollisionBox)GetCollisionType();
			float num23 = 41.666668f * base.MaxSpeed;
			if (direction.X > 0f)
			{
				int x2 = 0;
				int y2 = 0;
				collisionLevelMap.GetMapCoords(ref x2, ref y2, collisionBox2.BottomRight + new Vector2(num23, 0f));
				if (collisionLevelMap.TileIsOccupied(x2, y2))
				{
					direction.X = 0f - MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
				collisionLevelMap.GetMapCoords(ref x2, ref y2, collisionBox2.TopRight + new Vector2(num23, 0f));
				if (collisionLevelMap.TileIsOccupied(x2, y2))
				{
					direction.X = 0f - MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
			}
			else if (direction.X < 0f)
			{
				int x3 = 0;
				int y3 = 0;
				collisionLevelMap.GetMapCoords(ref x3, ref y3, collisionBox2.BottomLeft + new Vector2(0f - num23, 0f));
				if (collisionLevelMap.TileIsOccupied(x3, y3))
				{
					direction.X = 0f + MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
				collisionLevelMap.GetMapCoords(ref x3, ref y3, collisionBox2.TopLeft + new Vector2(0f - num23, 0f));
				if (collisionLevelMap.TileIsOccupied(x3, y3))
				{
					direction.X = 0f + MathHelper.Max(Math.Abs(direction.Y), 1f);
				}
			}
			int x4 = 0;
			int y4 = 0;
			collisionLevelMap.GetMapCoords(ref x4, ref y4, collisionBox2.TopLeft + new Vector2(0f, -3f * num23));
			if (collisionLevelMap.TileIsOccupied(x4, y4))
			{
				direction.Y = MathHelper.Max(Math.Abs(direction.X), 1f);
			}
			collisionLevelMap.GetMapCoords(ref x4, ref y4, collisionBox2.TopRight + new Vector2(0f, -3f * num23));
			if (collisionLevelMap.TileIsOccupied(x4, y4))
			{
				direction.Y = MathHelper.Max(Math.Abs(direction.X), 1f);
			}
		}
		if ((direction).Length() <= 0.2f)
		{
			direction = Vector2.Zero;
		}
	}

	private void findNextTileOnMap(int x, int y, ref int target_x, ref int target_y, CollisionLevelMap map)
	{
		if (!map.TileIsOccupied(x, y - 1))
		{
			target_x = x;
			target_y = y - 1;
			return;
		}
		int num = x - 1;
		int num2 = 0;
		while (map.TileIsOccupied(num, y) || map.TileIsOccupied(num, y - 1))
		{
			num2++;
			num--;
			if (num < 0)
			{
				num2 = 1000;
				break;
			}
		}
		num = x + 1;
		int num3 = 0;
		while (map.TileIsOccupied(num, y) || map.TileIsOccupied(num, y - 1))
		{
			num3++;
			num++;
			if (num >= map.Width)
			{
				num3 = 1000;
				break;
			}
		}
		if (num2 < num3)
		{
			target_x = x - 1;
			target_y = y;
			return;
		}
		if (num2 > num3)
		{
			target_x = x + 1;
			target_y = y;
			return;
		}
		if (player == 0)
		{
			target_x = x - 1;
		}
		if (player == 1)
		{
			target_x = x + 1;
		}
		if (player == 2)
		{
			target_x = x - 1;
		}
		if (player == 3)
		{
			target_x = x + 1;
		}
		target_y = y;
	}

	private void getDistanceToLine(AlienDrawableGameComponent alien, out float d, out Vector2 shortestpoint)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		Vector2 start = ((CollisionLine)((Lazer)alien).GetCollisionType()).Start;
		Vector2 end = ((CollisionLine)((Lazer)alien).GetCollisionType()).End;
		Vector2 position = base.Position;
		if (start == end)
		{
			shortestpoint = start;
			Vector2 val = position - start;
			d = (val).Length();
			return;
		}
		float num = (position.X - start.X) * (end.X - start.X) + (position.Y - start.Y) * (end.Y - start.Y);
		float num2 = num;
		Vector2 val2 = end - start;
		num = num2 / (val2).LengthSquared();
		if (num < 0f)
		{
			shortestpoint = start;
		}
		else if (num > 1f)
		{
			shortestpoint = end;
		}
		else
		{
			shortestpoint = start + num * (end - start);
		}
		Vector2 val3 = position - shortestpoint;
		d = (val3).Length();
	}

	private void FireAt(float direction)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		pacifistTimer.Reset();
		pacifistTimer.Start();
		if (shoottimer.Finished | !shoottimer.Active)
		{
			shoottimer.Start();
			Bullet bullet = Bullet.NewBullet(collection, base.Game);
			bullet.Setup(base.Position, direction, bulletlifetime, player);
			if ((float)RandomHelper.Random.Next(100) < bouncebulletspercentage)
			{
				bullet.SetBouncing(bounceamount);
				bullet.SetSplit(bulletsSplit);
			}
			if ((float)RandomHelper.Random.Next(100) < asplodingbulletspercentage)
			{
				bullet.SetAsploding(asplodingbulletssize);
			}
			collection.Add((GameComponent)(object)bullet);
			sound.PlayCue("fire");
		}
	}

	private void DoSpecial(bool pickup)
	{
		if (!pickup)
		{
			return;
		}
		switch (currentPower)
		{
		case Powerup.PowerupType.Linker:
			readyToConnect = true;
			break;
		case Powerup.PowerupType.Blast:
			Score.AddBomb(player);
			break;
		case Powerup.PowerupType.Option:
		{
			int num = 1;
			int num2 = 1;
			if (optionLevel == 3)
			{
				num = 2;
			}
			if (optionLevel == 4)
			{
				num2 = 2;
			}
			for (int i = 0; i < num2; i++)
			{
				for (int j = 0; j < num; j++)
				{
					Option option = Option.NewOption(collection, base.Game);
					option.Setup(this, 0f, i + 1, player);
					collection.Add((GameComponent)(object)option);
					options[i].Add(option);
				}
			}
			RedressOptions();
			break;
		}
		case Powerup.PowerupType.FirePower:
			shotspersec++;
			shotspersec = Math.Min(shotspersec, 18);
			shoottimer.Duration = 1000f / (float)shotspersec;
			break;
		case Powerup.PowerupType.Range:
			bulletlifetime = MathHelper.Min(70f + bulletlifetime, 1500f);
			break;
		case Powerup.PowerupType.OneUp:
			Score.AddLife();
			break;
		}
	}

	private void doPowerupEffect()
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		powerupEffect = PowerupEffect.NewPowerupEffect(collection, base.Game);
		powerupEffect.Setup(base.Position, 1f, 0.6f, 0f, base.Direction);
		collection.Add((GameComponent)(object)powerupEffect);
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_029b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c7: Unknown result type (might be due to invalid IL or missing references)
		if (other is PlayerShip && (readyToConnect & ((PlayerShip)other).readyToConnect) && !isConnectedWith(other))
		{
			ShipConnector shipConnector = ShipConnector.NewAlien(collection, base.Game);
			shipConnector.Setup(this, (PlayerShip)other);
			((PlayerShip)other).connectors.Add(shipConnector);
			connectors.Add(shipConnector);
			collection.Add((GameComponent)(object)shipConnector);
			bool flag = false;
			foreach (PlayerShip ship in oracle.GetShips())
			{
				if (ship.controller != ControlDevice.AI)
				{
					flag = true;
				}
			}
			if (oracle.NrOfShipConnectors() == 3 && flag)
			{
				ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Coop);
			}
		}
		if ((other is UFO || other is Lazer || other is Boss || other is Braineroid || other is EvilBullet || other is Asteroid || other is Ball || other is JunkBoss || other is DeathStar || other is ClassicBoss || other is StationaryBoss || other is Spider || other is MarsBoss || other is BattleSkull || other is Wall || other is FlyingSpider || other is Explosion || other is StarMine || other is PlasmaBall || other is BrainBoss || other is FakeBoss || other is SweepUFO || other is SpiderBoss || other is PunchingBag || (other is EvilSkull && !((EvilSkull)other).Fading)) && (!invulnerabilityTimer.Active & !hasWon))
		{
			if (connectors.Count > 0)
			{
				foreach (ShipConnector connector in connectors)
				{
					connector.TakeHit();
				}
			}
			else if (!Settings.GetInstance().Invulnerability)
			{
				if (other is Wall)
				{
					AsplodeWall();
				}
				else if (other is AlienDrawableGameComponent)
				{
					if (!((AlienDrawableGameComponent)other).IsDead)
					{
						queueAsplosion(other);
						((AlienDrawableGameComponent)other).OnDeath += Killer_OnDeath;
					}
				}
				else
				{
					Asplode();
				}
			}
		}
		if (other is Floorbottom)
		{
			base.Position = new Vector2(base.Position.X, ((Floorbottom)other).Bottom - ((CollisionBox)GetCollisionType()).Height / 2f);
		}
		if (other is Powerup && !((Powerup)other).taken)
		{
			currentPower = ((Powerup)other).type;
			Score.SetPowerup(currentPower, player);
			haspower = true;
			DoSpecial(pickup: true);
			sound.PlayCue("powerup");
			((Powerup)other).taken = true;
			if (this.OnCollectPowerup != null)
			{
				this.OnCollectPowerup(currentPower);
			}
		}
		base.CollidesWith(other);
	}

	private void Killer_OnDeath(object sender)
	{
		asplosionCauser = null;
	}

	private void queueAsplosion(ICollidable other)
	{
		asplodeOnNextFrame = true;
		asplosionCauser = other;
	}

	private bool isConnectedWith(ICollidable other)
	{
		bool flag = false;
		foreach (ShipConnector connector in connectors)
		{
			flag |= connector.A == other;
			flag |= connector.B == other;
		}
		return flag;
	}

	public void Win()
	{
		hasWon = true;
	}

	public void TemporaryInvulnerability()
	{
		invulnerabilityTimer.Duration = 2500f;
		invulnerabilityTimer.Reset();
		invulnerabilityTimer.Start();
	}

	public void TemporaryInvulnerability(int seconds)
	{
		invulnerabilityTimer.Duration = seconds * 1000;
		invulnerabilityTimer.Reset();
		invulnerabilityTimer.Start();
	}

	private void AsplodeWall()
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		Die();
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		float impulse = (backgroundSpeed).Length();
		float direction = MyMath.VectorToAngle(oracle.BackgroundSpeed);
		explosion.Setup(base.Position, 2f, 2f, impulse, direction);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 3.5f, 3.5f, impulse, direction);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	public void Asplode()
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		if (!base.IsDead)
		{
			Die();
			Explosion explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 2f, 2f, 0f, 0f);
			collection.Add((GameComponent)(object)explosion);
			explosion = Explosion.NewExplosion(collection, base.Game);
			explosion.Setup(base.Position, 3.5f, 3.5f, 0f, 0f);
			collection.Add((GameComponent)(object)explosion);
			sound.PlayCue("expl2");
		}
	}

	internal void PowerUp()
	{
		shotspersec = 18;
		bulletlifetime = 1500f;
		shoottimer.Duration = 1000f / (float)shotspersec;
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < 6; j++)
			{
				Option option = Option.NewOption(collection, base.Game);
				option.Setup(this, 0f, i + 1, player);
				collection.Add((GameComponent)(object)option);
				options[i].Add(option);
			}
		}
		RedressOptions();
		Score.MaxExp(Owner);
		PowerUp(Powerup.PowerupType.Blast, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.FirePower, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.Linker, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.Range, 4, doEffect: false);
		PowerUp(Powerup.PowerupType.Option, 4, doEffect: false);
	}

	internal void AddRangePowerups(int p)
	{
		bulletlifetime = MathHelper.Min(70f * (float)p + bulletlifetime, 1500f);
	}

	internal void RemovePowerup()
	{
		haspower = false;
		Score.ResetPowerup(player);
	}

	internal void PowerUp(Powerup.PowerupType type, int newLevel, bool doEffect)
	{
		if (doEffect)
		{
			doPowerupEffect();
		}
		switch (type)
		{
		case Powerup.PowerupType.Option:
		{
			optionLevel = newLevel;
			Option option = Option.NewOption(collection, base.Game);
			option.Setup(this, 0f, 1, player);
			collection.Add((GameComponent)(object)option);
			options[0].Add(option);
			RedressOptions();
			break;
		}
		case Powerup.PowerupType.FirePower:
			switch (newLevel)
			{
			case 1:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 15f);
				asplodingbulletssize = 400f;
				break;
			case 2:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 30f);
				asplodingbulletssize = 400f;
				break;
			case 3:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 60f);
				asplodingbulletssize = 400f;
				break;
			case 4:
				asplodingbulletspercentage = MathHelper.Max(asplodingbulletspercentage, 75f);
				asplodingbulletssize = 1400f;
				break;
			default:
				throw new Exception("invalid powerup level" + newLevel);
			}
			break;
		case Powerup.PowerupType.Range:
			switch (newLevel)
			{
			case 1:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 50f);
				break;
			case 2:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 100f);
				bounceamount = Math.Max(bounceamount, 2);
				break;
			case 3:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 100f);
				bounceamount = Math.Max(bounceamount, 2);
				bulletsSplit = Math.Max(bulletsSplit, 1);
				break;
			case 4:
				bouncebulletspercentage = MathHelper.Max(bouncebulletspercentage, 100f);
				bounceamount = Math.Max(bounceamount, 5);
				bulletsSplit = Math.Max(bulletsSplit, 2);
				break;
			default:
				throw new Exception("invalid powerup level" + newLevel);
			}
			break;
		case Powerup.PowerupType.Linker:
			switch (newLevel)
			{
			case 1:
				respawntimebonus = Math.Max(2, respawntimebonus);
				break;
			case 2:
				respawntimebonus = Math.Max(4, respawntimebonus);
				break;
			case 3:
				respawntimebonus = Math.Max(7, respawntimebonus);
				break;
			case 4:
				respawntimebonus = Math.Max(14, respawntimebonus);
				break;
			default:
				throw new Exception("invalid powerup level" + newLevel);
			}
			break;
		case Powerup.PowerupType.OneUp:
			ServiceHelper.Get<IOracleService>().Oracle.SetSlowmotion(12f);
			Score.RemovePowerup(player);
			break;
		case Powerup.PowerupType.Blast:
			break;
		}
	}

	private bool wantsToTakePowerup(Powerup p)
	{
		if (Score.GetPowerupProgress(player) > 0.6f && p.type != currentPower)
		{
			return false;
		}
		if (readyToConnect && p.type == Powerup.PowerupType.Linker)
		{
			return false;
		}
		if (Score.NrBombs(player) == 3 && p.type == Powerup.PowerupType.Blast)
		{
			return false;
		}
		if (shotspersec == 18 && p.type == Powerup.PowerupType.FirePower)
		{
			return false;
		}
		if (bulletlifetime == 1500f && p.type == Powerup.PowerupType.Range)
		{
			return false;
		}
		return true;
	}
}
