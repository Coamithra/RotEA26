using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class SpiderBoss : AlienDrawableGameComponent
{
	private enum SpiderBossState
	{
		flyleft,
		flyright,
		flyup,
		land,
		standing,
		jump,
		dead
	}

	private const float edge = 345f;

	private const float xposstatic = 600f;

	private const float yposstatic = 400f;

	private AnimatedSprite spiderStand;

	private AnimatedSprite spiderJump;

	private AnimatedSprite spiderLand;

	private AnimatedSprite spiderFly;

	private AnimatedSprite currentAnimation;

	private float animationProgress;

	private Vector2 spriteOffset = new Vector2(430f, 310f);

	private Timer dunceTimer = new Timer(180000f, repeating: false);

	private Vector2 impulse;

	private int hp;

	private bool sfxplayed;

	private Timer hittimer = new Timer(800f, repeating: false);

	private Timer waittimer = new Timer(1000f, repeating: false);

	private List<Lazer> alreadyHitBy = new List<Lazer>();

	private List<Vector2> debrisposition = new List<Vector2>();

	private List<Vector2> debrisspeed = new List<Vector2>();

	private List<float> debrisrotation = new List<float>();

	private List<float> debrisrotationspeed = new List<float>();

	private Texture2D debris1;

	private Texture2D debris2;

	private Texture2D debris3;

	private Texture2D blank;

	private Timer stateTimer = new Timer(1f, repeating: false);

	private SpiderBossState state;

	private bool isPreload;

	public DeathEvent OnAlmostKilled;

	private CollisionMultibox boxes;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_02e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_02ff: Unknown result type (might be due to invalid IL or missing references)
			//IL_0304: Unknown result type (might be due to invalid IL or missing references)
			//IL_035f: Unknown result type (might be due to invalid IL or missing references)
			//IL_03c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_03e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_03e6: Unknown result type (might be due to invalid IL or missing references)
			//IL_0200: Unknown result type (might be due to invalid IL or missing references)
			//IL_021d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0222: Unknown result type (might be due to invalid IL or missing references)
			//IL_027d: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Unknown result type (might be due to invalid IL or missing references)
			//IL_0563: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
			//IL_0135: Unknown result type (might be due to invalid IL or missing references)
			//IL_0140: Unknown result type (might be due to invalid IL or missing references)
			//IL_019b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0483: Unknown result type (might be due to invalid IL or missing references)
			//IL_048e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0493: Unknown result type (might be due to invalid IL or missing references)
			//IL_04dd: Unknown result type (might be due to invalid IL or missing references)
			//IL_04fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ff: Unknown result type (might be due to invalid IL or missing references)
			//IL_0504: Unknown result type (might be due to invalid IL or missing references)
			//IL_0506: Unknown result type (might be due to invalid IL or missing references)
			if (boxes == null)
			{
				boxes = new CollisionMultibox();
				boxes.Items.Add(new CollisionBox());
				boxes.Items.Add(new CollisionBox());
			}
			switch (state)
			{
			case SpiderBossState.flyleft:
			case SpiderBossState.flyright:
			{
				boxes.Items[0].Width = 240f * scale;
				boxes.Items[0].Height = 186.66667f;
				float height = boxes.Items[0].Height;
				float num4 = 0f;
				if (base.Position.Y <= height)
				{
					num4 = height * 0.5f;
				}
				if (height <= base.Position.Y && base.Position.Y <= 1.5f * height)
				{
					num4 = height * 1.5f;
				}
				if (1.5f * height <= base.Position.Y)
				{
					num4 = height * 2.5f;
				}
				boxes.Items[0].CenterAround(new Vector2(base.Position.X, num4));
				boxes.Items[1].Height = 1f;
				boxes.Items[1].Width = 1f;
				boxes.Items[1].CenterAround(new Vector2(1000f, 1000f));
				break;
			}
			case SpiderBossState.jump:
				boxes.Items[0].Width = 240f * scale;
				boxes.Items[0].Height = 150f * scale;
				boxes.Items[0].CenterAround(base.Position + new Vector2(20f * scale, 40f * scale));
				boxes.Items[1].Height = 1f;
				boxes.Items[1].Width = 1f;
				boxes.Items[1].CenterAround(new Vector2(1000f, 1000f));
				break;
			case SpiderBossState.flyup:
			case SpiderBossState.land:
				boxes.Items[0].Width = 240f * scale;
				boxes.Items[0].Height = 150f * scale;
				boxes.Items[0].CenterAround(base.Position + new Vector2(20f * scale, -60f * scale));
				boxes.Items[1].Height = 1f;
				boxes.Items[1].Width = 1f;
				boxes.Items[1].CenterAround(new Vector2(1000f, 1000f));
				break;
			case SpiderBossState.standing:
				boxes.Items[0].Width = 240f * scale;
				boxes.Items[0].Height = 150f * scale;
				boxes.Items[0].CenterAround(base.Position + new Vector2(20f * scale, 40f * scale));
				boxes.Items[0].Bottom += 100f * scale;
				if (12f < animationProgress && animationProgress < 18f && currentAnimation == spiderStand)
				{
					float num = (animationProgress - 12f) / 6f;
					float num2 = MathHelper.Lerp(20f, 105f, num);
					float num3 = MathHelper.Lerp(0f, 30f, num);
					Vector2 val = new Vector2(num2, num3) * scale;
					boxes.Items[1].Height = 120f;
					boxes.Items[1].Width = 300f;
					boxes.Items[1].CenterAround(base.Position + new Vector2(20f * scale, 40f * scale) - val);
				}
				else
				{
					boxes.Items[1].Height = 1f;
					boxes.Items[1].Width = 1f;
					boxes.Items[1].CenterAround(new Vector2(1000f, 1000f));
				}
				break;
			}
			return boxes;
		}
	}

	public SpiderBoss(Game game)
		: base(game)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		base.DrawOrder = 20;
		interpolationOptions = InterpolationOptions.never;
		scale = 1f;
		timers.Add(stateTimer);
		timers.Add(hittimer);
		timers.Add(waittimer);
		PointValue = 2000f;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent is Lazer)
		{
			alreadyHitBy.Remove((Lazer)(object)e.GameComponent);
		}
		if (e.GameComponent == this)
		{
			OnAlmostKilled = null;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blank = content.Load<Texture2D>("GFX/Game/blank");
		spiderFly = new AnimatedSprite("GFX/Spider/spiderfly");
		spiderJump = new AnimatedSprite("GFX/Spider/spiderjump");
		spiderLand = new AnimatedSprite("GFX/Spider/spiderland");
		spiderStand = new AnimatedSprite("GFX/Spider/spiderstand");
		debris1 = content.Load<Texture2D>("GFX/Sprites/spiderdebris1");
		debris2 = content.Load<Texture2D>("GFX/Sprites/spiderdebris2");
		debris3 = content.Load<Texture2D>("GFX/Sprites/spiderdebris3");
	}

	public static SpiderBoss NewSpiderBoss(ComponentBin collection, Game game)
	{
		SpiderBoss spiderBoss = collection.Recycle<SpiderBoss>();
		if (spiderBoss == null)
		{
			spiderBoss = new SpiderBoss(game);
		}
		return spiderBoss;
	}

	public void Setup(bool intro)
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		if (intro)
		{
			state = SpiderBossState.flyleft;
			base.Position = new Vector2(1145f, 235f);
			ResetTimer(4f);
		}
		else
		{
			state = SpiderBossState.land;
			base.Position = new Vector2(600f, -345f);
		}
		isPreload = false;
	}

	private float randomYPosition()
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		int num = RandomHelper.Random.Next(3);
		if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.5f * Settings.GetInstance().DifficultyModifier)
		{
			float y = oracle.GetRandomPlayerPosition().Y;
			num = (int)(y / 183.33333f);
		}
		return num switch
		{
			0 => 70f, 
			1 => 235f, 
			2 => 380f, 
			_ => 0f, 
		};
	}

	public override void Initialize()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				SignedInGamer current = enumerator.Current;
				current.Presence.PresenceMode = (GamerPresenceMode)34;
			}
		}
		finally
		{
			((IDisposable)enumerator).Dispose();
		}
		dunceTimer.Reset();
		dunceTimer.Start();
		hittimer.Stop();
		hp = (int)(5f * Settings.GetInstance().DifficultyFactorized(0.75f));
		base.Initialize();
		debrisposition.Clear();
		debrisspeed.Clear();
		debrisrotation.Clear();
		debrisrotationspeed.Clear();
		sfxplayed = false;
		base.Collides = true;
		waittimer.Stop();
		currentAnimation = spiderFly;
		animationProgress = 0f;
	}

	private void ResetTimer(float seconds)
	{
		stateTimer.Duration = 1000f * seconds / Settings.GetInstance().DifficultyFactorized(0.5f);
		stateTimer.Reset();
		stateTimer.Start();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		if (hittimer.Active)
		{
			spriteBatch.lightenEffect.Enable();
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		SpiderBossState spiderBossState = state;
		if (spiderBossState == SpiderBossState.dead)
		{
			spriteEffects = (SpriteEffects)0;
			Color val = default(Color);
			(val) = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0f, 1f, stateTimer.TimeLeft * 3f / stateTimer.Duration)));
			for (int i = 0; i < debrisposition.Count; i++)
			{
				Texture2D val2 = (Texture2D)(i switch
				{
					0 => debris1, 
					1 => debris3, 
					_ => debris2, 
				});
				spriteBatch.Draw(val2, debrisposition[i], debrisrotation[i], scale, center: true, val);
			}
		}
		else
		{
			SpriteEffects e = (SpriteEffects)0;
			Vector2 val3 = spriteOffset;
			if (state == SpiderBossState.flyright)
			{
				e = (SpriteEffects)1;
				val3.X -= 260f;
			}
			if (state == SpiderBossState.flyleft || state == SpiderBossState.flyright)
			{
				val3.Y -= 130f;
			}
			currentAnimation.Draw((int)animationProgress, base.Position - val3, Color.White, scale, center: false, e);
		}
		if (hittimer.Active)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_023d: Unknown result type (might be due to invalid IL or missing references)
		//IL_025b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0265: Unknown result type (might be due to invalid IL or missing references)
		//IL_0270: Unknown result type (might be due to invalid IL or missing references)
		//IL_0329: Unknown result type (might be due to invalid IL or missing references)
		//IL_0334: Unknown result type (might be due to invalid IL or missing references)
		//IL_0351: Unknown result type (might be due to invalid IL or missing references)
		//IL_035c: Unknown result type (might be due to invalid IL or missing references)
		//IL_043f: Unknown result type (might be due to invalid IL or missing references)
		//IL_044a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0467: Unknown result type (might be due to invalid IL or missing references)
		//IL_0472: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0500: Unknown result type (might be due to invalid IL or missing references)
		//IL_050a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0574: Unknown result type (might be due to invalid IL or missing references)
		//IL_0584: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04af: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_05cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_039a: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0640: Unknown result type (might be due to invalid IL or missing references)
		//IL_064b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0658: Unknown result type (might be due to invalid IL or missing references)
		//IL_065d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0672: Unknown result type (might be due to invalid IL or missing references)
		//IL_0677: Unknown result type (might be due to invalid IL or missing references)
		//IL_0693: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0705: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_031c: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_071e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0744: Unknown result type (might be due to invalid IL or missing references)
		//IL_075c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0767: Unknown result type (might be due to invalid IL or missing references)
		if (isPreload)
		{
			return;
		}
		dunceTimer.Update(gameTime);
		if (dunceTimer.Finished)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Dunce);
		}
		float num = 30f * Settings.GetInstance().DifficultyFactorized(0.5f);
		if (currentAnimation == spiderStand)
		{
			num *= 0.7f;
		}
		float num2 = animationProgress;
		bool flag = false;
		animationProgress = MyMath.Mod(animationProgress + (float)gameTime.ElapsedGameTime.TotalSeconds * num, currentAnimation.Frames);
		if (animationProgress < num2)
		{
			flag = true;
		}
		base.Update(gameTime);
		if (waittimer.Active)
		{
			return;
		}
		float num3 = 0.78f * Settings.GetInstance().DifficultyModifier;
		switch (state)
		{
		case SpiderBossState.flyleft:
			base.Position = new Vector2(base.Position.X - num3 * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (base.Position.X < 800f && !sfxplayed)
			{
				sound.PlayCue("wasp");
				sfxplayed = true;
			}
			if (base.Position.X < -345f && stateTimer.Finished)
			{
				state = SpiderBossState.flyright;
				base.Position = new Vector2(-345f, randomYPosition());
				ResetTimer(4f);
				sfxplayed = false;
				AnimatedMessage animatedMessage3 = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
				animatedMessage3.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				float warningDirection3 = (float)Math.PI;
				if (base.Position.Y < 150f)
				{
					warningDirection3 = 3.6913714f;
				}
				if (base.Position.Y > 250f)
				{
					warningDirection3 = (float)Math.PI * 7f / 8f;
				}
				animatedMessage3.SetWarningDirection(warningDirection3);
				animatedMessage3.MakeShort();
				collection.Add((GameComponent)(object)animatedMessage3);
				waittimer.Reset();
				waittimer.Start();
			}
			break;
		case SpiderBossState.flyright:
			base.Position = new Vector2(base.Position.X + num3 * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (base.Position.X > 0f && !sfxplayed)
			{
				sound.PlayCue("wasp");
				sfxplayed = true;
			}
			if (base.Position.X > 1145f && stateTimer.Finished)
			{
				AnimatedMessage animatedMessage2 = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
				animatedMessage2.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				float warningDirection2 = -0.9424779f;
				animatedMessage2.SetWarningDirection(warningDirection2);
				animatedMessage2.MakeShort();
				collection.Add((GameComponent)(object)animatedMessage2);
				state = SpiderBossState.land;
				base.Position = new Vector2(600f, -345f);
			}
			break;
		case SpiderBossState.flyup:
			base.Position = new Vector2(base.Position.X, base.Position.Y - num3 * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			if (base.Position.Y < -345f && stateTimer.Finished)
			{
				state = SpiderBossState.flyleft;
				sfxplayed = false;
				base.Position = new Vector2(1145f, randomYPosition());
				ResetTimer(4f);
				AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
				animatedMessage.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				float warningDirection = 0f;
				if (base.Position.Y < 150f)
				{
					warningDirection = -0.5497787f;
				}
				if (base.Position.Y > 250f)
				{
					warningDirection = (float)Math.PI / 8f;
				}
				animatedMessage.SetWarningDirection(warningDirection);
				animatedMessage.MakeShort();
				collection.Add((GameComponent)(object)animatedMessage);
				waittimer.Reset();
				waittimer.Start();
			}
			break;
		case SpiderBossState.land:
			base.Position = new Vector2(base.Position.X, base.Position.Y + num3 * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			if (base.Position.Y > 400f)
			{
				state = SpiderBossState.standing;
				animationProgress = 0f;
				currentAnimation = spiderLand;
				base.Position = new Vector2(600f, 400f);
				ResetTimer(7f);
				rumble(base.Position);
			}
			break;
		case SpiderBossState.standing:
			base.Position = new Vector2(base.Position.X + oracle.BackgroundSpeed.X * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (stateTimer.Finished && flag)
			{
				state = SpiderBossState.jump;
				animationProgress = 0f;
				currentAnimation = spiderJump;
			}
			else if (flag && currentAnimation == spiderLand)
			{
				currentAnimation = spiderStand;
				animationProgress = 0f;
			}
			break;
		case SpiderBossState.jump:
			base.Position = new Vector2(base.Position.X + oracle.BackgroundSpeed.X * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (animationProgress > 30f)
			{
				base.Position = new Vector2(base.Position.X, base.Position.Y - num3 * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			}
			if (flag)
			{
				state = SpiderBossState.flyup;
				ResetTimer(3f);
				animationProgress = 0f;
				currentAnimation = spiderFly;
			}
			break;
		case SpiderBossState.dead:
		{
			for (int i = 0; i < debrisposition.Count; i++)
			{
				List<Vector2> list;
				int index;
				(list = debrisposition)[index = i] = list[index] + (oracle.BackgroundSpeed + debrisspeed[i]) * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
				List<Vector2> list2;
				int index2;
				(list2 = debrisspeed)[index2 = i] = list2[index2] + new Vector2(0f, 0.001f * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
				debrisrotation[i] += debrisrotationspeed[i] * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
				if (debrisposition[i].Y > 550f && debrisspeed[i].Y > 0f)
				{
					debrisspeed[i] = new Vector2(0.5f * debrisspeed[i].X, -0.5f * debrisspeed[i].Y);
					debrisrotationspeed[i] *= 0.5f;
				}
			}
			if (stateTimer.Finished)
			{
				Die();
			}
			break;
		}
		}
	}

	private void rumble(Vector2 Position)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = default(Vector2);
		Vector2 val2 = default(Vector2);
		for (int i = 0; i < oracle.Players; i++)
		{
			Vibrator vibrator = ServiceHelper.Get<IVibratorService>().Vibrator;
			(val) = new Vector2(0.35f, 0.35f);
			(val2) = new Vector2(0.15f, 0.15f);
			Vector2 val3 = Position - oracle.GetPlayerPosition(i);
			float num = (val3).Length();
			Vector2 power = Vector2.Lerp(val, val2, MathHelper.Clamp(num / 450f, 0f, 1f));
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
				vibrator.addVibration(power, 1000f, playerIndex);
			}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		base.CollidesWith(other);
		if (!(other is Lazer) || alreadyHitBy.Contains((Lazer)other))
		{
			return;
		}
		sound.PlayCue("bugdies");
		sound.PlayCue("bugdies");
		hp--;
		if (hp <= 0 && !base.IsDead)
		{
			switch (state)
			{
			case SpiderBossState.flyleft:
				impulse = new Vector2(-0.84f, 0f);
				break;
			case SpiderBossState.flyright:
				impulse = new Vector2(0.84f, 0f);
				break;
			case SpiderBossState.flyup:
				impulse = new Vector2(0f, -0.84f);
				break;
			case SpiderBossState.land:
				impulse = new Vector2(0f, 0.84f);
				break;
			case SpiderBossState.standing:
				impulse = Vector2.Zero;
				break;
			}
			state = SpiderBossState.dead;
			if (OnAlmostKilled != null)
			{
				OnAlmostKilled(this);
			}
			AwardScoreToAll(combo: false);
			sound.PlayCue("spiderbossdeath");
			sound.PlayCue("head asplode");
			for (int i = 0; i < 6; i++)
			{
				debrisposition.Add(base.Position);
				debrisspeed.Add(new Vector2(RandomHelper.RandomNextFloat(-0.3f, 0.3f), -0.3f + 0.5f * RandomHelper.RandomNextFloat(-0.3f, 0.3f)));
				debrisrotation.Add(RandomHelper.RandomNextAngle());
				debrisrotationspeed.Add(RandomHelper.RandomNextFloat(-0.03f, 0.03f));
			}
			base.Collides = false;
			ResetTimer(5f);
			for (int j = 0; j < 8; j++)
			{
				Bleed(2.5f);
			}
			for (int k = 0; k < 8; k++)
			{
				Bleed(3f);
			}
			for (int l = 0; l < 8; l++)
			{
				Bleed(5f);
			}
			for (int m = 0; m < 8; m++)
			{
				Bleed(6f);
			}
		}
		else
		{
			alreadyHitBy.Add((Lazer)other);
			hittimer.Start();
			hittimer.Reset();
			for (int n = 0; n < 5; n++)
			{
				Bleed(2.5f);
			}
		}
	}

	private static void FindSpawnSpot(out float angle, out float range)
	{
		angle = RandomHelper.RandomNextAngle();
		range = MyMath.PowerCurve(100f, 0f, 2f, RandomHelper.RandomNextFloat(0f, 1f));
	}

	private void Bleed(float size)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		FindSpawnSpot(out var angle, out var range);
		Vector2 position = MyMath.AngleToVector(angle) * range + base.Position;
		bloodExplosion.Setup(position, size, size * 0.7f, 0.12f, angle);
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
	}

	internal void SetupPreload()
	{
		isPreload = true;
	}
}
