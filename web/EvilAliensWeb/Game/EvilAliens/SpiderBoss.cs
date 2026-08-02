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

	// Off-screen holds between fly segments, so the "Danger!" warning leads the boss's arrival
	// instead of coinciding with it. All three turns share the single `waittimer`; its Duration is
	// set explicitly at each site because Duration persists across Reset(), so the land value below
	// would otherwise leak into the next fly turn. The two mid-air turns (flyleft->flyright,
	// flyup->flyleft) already paused for the waittimer's old default 1000ms (flyPauseMs preserves
	// that exactly -- don't drop those two assignments or the land value leaks in). The
	// flyright->land turn (fly off the right edge, then drop from the top to land) had NO pause, so
	// the warning fired the instant the descent began; it now holds for landWarningLeadMs, kept EQUAL to
	// flyPauseMs so every spider warning leads by a consistent ~1s.
	private const float flyPauseMs = 1000f;

	private const float landWarningLeadMs = 1000f;

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

	// "Helper" mothership: a friendly mothership periodically flies over and lasers the boss (see
	// SpiderHelperMothership), keeping the Lazer-only-damageable fight legible. It arrives every N
	// completed jump->fly->land CYCLES, where N scales with difficulty (HelperCyclePeriod); the intro
	// landing doesn't count. Feel knobs + ?spiderhelpercycles live in DebugFlags (?spiderhelper*).
	private SpiderHelperMothership helper;

	// Set true the first time the boss lands (end of the intro fly-in); cycle counting starts after it.
	private bool hasLanded;

	// Completed jump->fly->land cycles since the last helper (or since the intro landing). When it
	// reaches helperCycleTarget the helper is summoned and this resets to 0.
	private int landingsSinceHelper;

	// The summon interval for THIS whole boss fight, sampled ONCE at the first landing (see the landing
	// logic) and held -- so the modifier's ramp can't drift it mid-fight (Very Hard stays 3 for all its
	// summons, not 3 then 4 then 5). It still scales by difficulty, just at fight granularity: the sample
	// is the fight-start modifier, so a higher tier -- or a long no-death run that's ramped in -- locks a
	// bigger interval.
	private int helperCycleTarget;

	// A warning arrow LEADS the helper's arrival by HelperWarningLeadMs (like the boss's own Danger
	// warns lead its fly-bys, flyPauseMs 1000): at the trigger landing we fire the arrow + arm this
	// timer, and only fly the mothership in when it finishes. helperPending gates the interval between.
	private const float HelperWarningLeadMs = 1000f;

	private Timer helpWarningTimer = new Timer(HelperWarningLeadMs, repeating: false);

	private bool helperPending;

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
				float laneCenterY = 0f;
				if (base.Position.Y <= height)
				{
					laneCenterY = height * 0.5f;
				}
				if (height <= base.Position.Y && base.Position.Y <= 1.5f * height)
				{
					laneCenterY = height * 1.5f;
				}
				if (1.5f * height <= base.Position.Y)
				{
					laneCenterY = height * 2.5f;
				}
				boxes.Items[0].CenterAround(new Vector2(base.Position.X, laneCenterY));
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
			{
				boxes.Items[0].Width = 240f * scale;
				boxes.Items[0].Height = 150f * scale;
				boxes.Items[0].CenterAround(base.Position + new Vector2(20f * scale, -60f * scale));
				// GAMEPLAY CHANGE (card f4d1721f), deliberate and it affects human players too:
				// the descent is hard-coded to X 600, which left a safe pocket between the boss and
				// the right screen edge. Standing in it trivialises the landing for anyone who
				// notices, and it is not intended behaviour for either side -- the AI found it
				// immediately and parked there, which is what surfaced it. The second box extends
				// the landing from the boss's right edge to the edge of the screen, so the only
				// answer to a landing is to get out from under it and to the LEFT.
				float bodyRight = boxes.Items[0].Right;
				float bodyMidY = (boxes.Items[0].Top + boxes.Items[0].Bottom) * 0.5f;
				float sweepWidth = MathHelper.Max(800f - bodyRight, 1f);
				boxes.Items[1].Width = sweepWidth;
				boxes.Items[1].Height = boxes.Items[0].Height;
				boxes.Items[1].CenterAround(new Vector2(bodyRight + sweepWidth * 0.5f, bodyMidY));
				break;
			}
			case SpiderBossState.standing:
				boxes.Items[0].Width = 240f * scale;
				boxes.Items[0].Height = 150f * scale;
				boxes.Items[0].CenterAround(base.Position + new Vector2(20f * scale, 40f * scale));
				boxes.Items[0].Bottom += 100f * scale;
				if (12f < animationProgress && animationProgress < 18f && currentAnimation == spiderStand)
				{
					float swipeT = (animationProgress - 12f) / 6f;
					float swipeX = MathHelper.Lerp(20f, 105f, swipeT);
					float swipeY = MathHelper.Lerp(0f, 30f, swipeT);
					Vector2 swipeOffset = new Vector2(swipeX, swipeY) * scale;
					boxes.Items[1].Height = 120f;
					boxes.Items[1].Width = 300f;
					boxes.Items[1].CenterAround(base.Position + new Vector2(20f * scale, 40f * scale) - swipeOffset);
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
		base.DrawOrder = 20;
		interpolationOptions = InterpolationOptions.never;
		scale = 1f;
		timers.Add(stateTimer);
		timers.Add(hittimer);
		timers.Add(waittimer);
		timers.Add(helpWarningTimer);
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
		int lane = RandomHelper.Random.Next(3);
		if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.5f * Settings.GetInstance().DifficultyModifier)
		{
			float y = oracle.GetRandomPlayerPosition().Y;
			lane = (int)(y / 183.33333f);
		}
		return lane switch
		{
			0 => 70f, 
			1 => 235f, 
			2 => 380f, 
			_ => 0f, 
		};
	}

	public override void Initialize()
	{
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
		// ?spiderbosshp=<n> overrides the pool so the boss survives many helper cycles while tuning.
		hp = EvilAliensWeb.Compat.DebugFlags.SpiderBossHp ?? (int)(5f * Settings.GetInstance().DifficultyFactorized(0.75f));
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
		helper = null;
		// Cycle counting starts at the boss's FIRST landing (so the intro fly-bys don't count).
		hasLanded = false;
		landingsSinceHelper = 0;
		helperCycleTarget = 0;
		helperPending = false;
		helpWarningTimer.Stop();
	}

	private void ResetTimer(float seconds)
	{
		stateTimer.Duration = 1000f * seconds / Settings.GetInstance().DifficultyFactorized(0.5f);
		stateTimer.Reset();
		stateTimer.Start();
	}

	public override void Draw(GameTime gameTime)
	{
		if (hittimer.Active)
		{
			spriteBatch.lightenEffect.Enable();
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		SpiderBossState spiderBossState = state;
		if (spiderBossState == SpiderBossState.dead)
		{
			spriteEffects = (SpriteEffects)0;
			Color tint = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0f, 1f, stateTimer.TimeLeft * 3f / stateTimer.Duration)));
			for (int i = 0; i < debrisposition.Count; i++)
			{
				Texture2D debrisTexture = (Texture2D)(i switch
				{
					0 => debris1, 
					1 => debris3, 
					_ => debris2, 
				});
				spriteBatch.Draw(debrisTexture, debrisposition[i], debrisrotation[i], scale, center: true, tint);
			}
		}
		else
		{
			SpriteEffects e = (SpriteEffects)0;
			Vector2 drawOffset = spriteOffset;
			if (state == SpiderBossState.flyright)
			{
				e = (SpriteEffects)1;
				drawOffset.X -= 260f;
			}
			if (state == SpiderBossState.flyleft || state == SpiderBossState.flyright)
			{
				drawOffset.Y -= 130f;
			}
			currentAnimation.Draw((int)animationProgress, base.Position - drawOffset, Color.White, scale, center: false, e);
		}
		if (hittimer.Active)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (isPreload)
		{
			return;
		}
		dunceTimer.Update(gameTime);
		if (dunceTimer.Finished)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Dunce);
		}
		float animFps = 30f * Settings.GetInstance().DifficultyFactorized(0.5f);
		if (currentAnimation == spiderStand)
		{
			animFps *= 0.7f;
		}
		float prevProgress = animationProgress;
		bool looped = false;
		animationProgress = MyMath.Mod(animationProgress + (float)gameTime.ElapsedGameTime.TotalSeconds * animFps, currentAnimation.Frames);
		if (animationProgress < prevProgress)
		{
			looped = true;
		}
		base.Update(gameTime);
		// The warning arrow led by HelperWarningLeadMs; now fly the mothership in. Checked even while
		// the boss is paused between fly turns, so the lead time is honoured regardless of boss state.
		if (helperPending && helpWarningTimer.Finished && helper == null && !base.IsDead)
		{
			SpawnHelper();
			helperPending = false;
		}
		if (waittimer.Active)
		{
			return;
		}
		float moveSpeed = 0.78f * Settings.GetInstance().DifficultyModifier;
		switch (state)
		{
		case SpiderBossState.flyleft:
			base.Position = new Vector2(base.Position.X - moveSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (base.Position.X < 800f && !sfxplayed)
			{
				sound.PlayCue("wasp");
				sfxplayed = true;
			}
			if (base.Position.X < -345f && stateTimer.Finished)
			{
				state = SpiderBossState.flyright;
				// THE CARD (8dabe812 -> e79bb994). The boss is PARKED to start each fly-by --
				// here the lane Y jumps outright. Unmarked, the host differentiated the jump and
				// stamped 42-57 px/ms onto the wire; the joiner's puppet then crossed the screen
				// at teleport speed, collidably, and killed the local player.
				base.Position = new Vector2(-345f, randomYPosition());
				NetNoteTeleport();
				ResetTimer(4f);
				sfxplayed = false;
				AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
				animatedMessage.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				float warningDirection = (float)Math.PI;
				if (base.Position.Y < 150f)
				{
					warningDirection = 3.6913714f;
				}
				if (base.Position.Y > 250f)
				{
					warningDirection = (float)Math.PI * 7f / 8f;
				}
				animatedMessage.SetWarningDirection(warningDirection);
				animatedMessage.MakeShort();
				collection.Add((GameComponent)(object)animatedMessage);
				// Online co-op (card ee939dd1): this arrow is the ONLY warning the player gets before
				// a screen-wide sweep, and it is spawned from the boss's own Update -- host-only, and
				// unreachable on a frozen puppet, so the join peer was swept with no warning at all.
				// MessageEvent's script banners already ride EvMessage; a boss-spawned one takes the
				// same lane (its compact MakeShort form rides that event's optional trailing byte).
				EvilAliensWeb.Compat.Net.NetSession.OnGameMessage(
					"Danger!", (int)SoundManager.Texts.Danger,
					(int)AnimatedMessage.MessageType.redwarning, warningDirection, isShort: true);
				waittimer.Duration = flyPauseMs;
				waittimer.Reset();
				waittimer.Start();
			}
			break;
		case SpiderBossState.flyright:
			base.Position = new Vector2(base.Position.X + moveSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (base.Position.X > 0f && !sfxplayed)
			{
				sound.PlayCue("wasp");
				sfxplayed = true;
			}
			if (base.Position.X > 1145f && stateTimer.Finished)
			{
				AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
				animatedMessage.Setup("Danger!", SoundManager.Texts.Danger, AnimatedMessage.MessageType.redwarning);
				float warningDirection = -0.9424779f;
				animatedMessage.SetWarningDirection(warningDirection);
				animatedMessage.MakeShort();
				collection.Add((GameComponent)(object)animatedMessage);
				// Online co-op (card ee939dd1): this arrow is the ONLY warning the player gets before
				// a screen-wide sweep, and it is spawned from the boss's own Update -- host-only, and
				// unreachable on a frozen puppet, so the join peer was swept with no warning at all.
				// MessageEvent's script banners already ride EvMessage; a boss-spawned one takes the
				// same lane (its compact MakeShort form rides that event's optional trailing byte).
				EvilAliensWeb.Compat.Net.NetSession.OnGameMessage(
					"Danger!", (int)SoundManager.Texts.Danger,
					(int)AnimatedMessage.MessageType.redwarning, warningDirection, isShort: true);
				state = SpiderBossState.land;
				// Parked for the landing: an ~800px jump back across the screen (card e79bb994).
				base.Position = new Vector2(600f, -345f);
				NetNoteTeleport();
				waittimer.Duration = landWarningLeadMs;
				waittimer.Reset();
				waittimer.Start();
			}
			break;
		case SpiderBossState.flyup:
			base.Position = new Vector2(base.Position.X, base.Position.Y - moveSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			if (base.Position.Y < -345f && stateTimer.Finished)
			{
				state = SpiderBossState.flyleft;
				sfxplayed = false;
				// Parked at the right edge for the next sweep -- both axes jump (card e79bb994).
				base.Position = new Vector2(1145f, randomYPosition());
				NetNoteTeleport();
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
				// Online co-op (card ee939dd1): this arrow is the ONLY warning the player gets before
				// a screen-wide sweep, and it is spawned from the boss's own Update -- host-only, and
				// unreachable on a frozen puppet, so the join peer was swept with no warning at all.
				// MessageEvent's script banners already ride EvMessage; a boss-spawned one takes the
				// same lane (its compact MakeShort form rides that event's optional trailing byte).
				EvilAliensWeb.Compat.Net.NetSession.OnGameMessage(
					"Danger!", (int)SoundManager.Texts.Danger,
					(int)AnimatedMessage.MessageType.redwarning, warningDirection, isShort: true);
				waittimer.Duration = flyPauseMs;
				waittimer.Reset();
				waittimer.Start();
			}
			break;
		case SpiderBossState.land:
			base.Position = new Vector2(base.Position.X, base.Position.Y + moveSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			if (base.Position.Y > 400f)
			{
				state = SpiderBossState.standing;
				animationProgress = 0f;
				currentAnimation = spiderLand;
				base.Position = new Vector2(600f, 400f);
				ResetTimer(7f);
				rumble(base.Position);
				// The intro fly-in ends at the FIRST landing (which does NOT count as a cycle). After
				// that, every completed jump->fly->land cycle ticks the counter, and every
				// HelperCyclePeriod() cycles the helper mothership flies in right as the boss hits the
				// ground. One helper at a time (helper == null).
				if (!hasLanded)
				{
					// Intro fly-in done -- LOCK the summon interval for the whole fight from the difficulty
					// NOW, sampled ONCE (not per-interval), so the modifier's ~+0.066/cycle ramp can't
					// drift it mid-fight. A fresh / post-death Very Hard fight is at ~baseline here, so
					// this is 3, held for all ~9 cycles; a ramped-in or higher-tier fight locks a bigger
					// value. (Re-sampling each interval instead would climb 3 -> 4 -> 5 over a VH fight.)
					hasLanded = true;
					helperCycleTarget = HelperCyclePeriod();
				}
				else if (helper == null && !helperPending && !base.IsDead && ++landingsSinceHelper >= helperCycleTarget)
				{
					// Fire the warning arrow NOW and fly the mothership in HelperWarningLeadMs later, so
					// the player gets a ~1s heads-up (the boss is landing anyway -- we know it's coming).
					WarnHelperIncoming();
					helpWarningTimer.Reset();
					helpWarningTimer.Start();
					helperPending = true;
					landingsSinceHelper = 0;
				}
			}
			break;
		case SpiderBossState.standing:
			base.Position = new Vector2(base.Position.X + oracle.BackgroundSpeed.X * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (stateTimer.Finished && looped)
			{
				state = SpiderBossState.jump;
				animationProgress = 0f;
				currentAnimation = spiderJump;
			}
			else if (looped && currentAnimation == spiderLand)
			{
				currentAnimation = spiderStand;
				animationProgress = 0f;
			}
			break;
		case SpiderBossState.jump:
			base.Position = new Vector2(base.Position.X + oracle.BackgroundSpeed.X * (float)gameTime.ElapsedGameTime.TotalMilliseconds, base.Position.Y);
			if (animationProgress > 30f)
			{
				base.Position = new Vector2(base.Position.X, base.Position.Y - moveSpeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			}
			if (looped)
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
				List<Vector2> posList;
				int posIndex;
				(posList = debrisposition)[posIndex = i] = posList[posIndex] + (oracle.BackgroundSpeed + debrisspeed[i]) * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
				List<Vector2> speedList;
				int speedIndex;
				(speedList = debrisspeed)[speedIndex = i] = speedList[speedIndex] + new Vector2(0f, 0.001f * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
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
		Vector2 nearPower = default(Vector2);
		Vector2 farPower = default(Vector2);
		// Per SEATED slot, not 0..Players-1: online co-op's roster is host-allocated and sparse
		// (card 4d904410), and Oracle.GetPlayerPosition/Controller THROW on an unseated slot.
		for (int i = 0; i < Oracle.MaxPlayers; i++)
		{
			if (!oracle.IsSeated(i))
			{
				continue;
			}
			Vibrator vibrator = ServiceHelper.Get<IVibratorService>().Vibrator;
			(nearPower) = new Vector2(0.35f, 0.35f);
			(farPower) = new Vector2(0.15f, 0.15f);
			Vector2 toPlayer = Position - oracle.GetPlayerPosition(i);
			float distance = (toPlayer).Length();
			Vector2 power = Vector2.Lerp(nearPower, farPower, MathHelper.Clamp(distance / 450f, 0f, 1f));
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
			// Online co-op (card 43e85936): the boss taking a hit read as "messy, missing sfx and
			// animation sometimes" on the join peer, and the "sometimes" is the whole tell -- the
			// client hit-tests puppets locally, so a hit IT saw ran this method and a hit only the
			// host saw produced nothing at all. Both the boss AND the beam that hurts it are
			// frozen puppets there, interpolated independently, so whether the two overlap on that
			// screen is a coin toss. The host owns "the boss was hit"; the peer plays the cue and
			// the light-up off this beat.
			EvilAliensWeb.Compat.Net.NetSession.OnGameFx(
				EvilAliensWeb.Compat.Net.NetFxKind.EnemyHitFlash, this);
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

	// How many completed jump->fly->land cycles between helper visits, from the difficulty modifier via
	// DifficultyFactorized. Sampled ONCE per fight (see helperCycleTarget) so within a fight it's fixed;
	// the scaling is ACROSS fights -- a higher tier, or a run that's ramped in without dying, gets a
	// bigger interval. 3 * DifficultyFactorized(5/3) is anchored so the tier baselines hit the spec:
	// Very_Hard(mod 1.0)->3 and Medium(0.6)->1, passing through Hard(0.8)->2 and Inzane(1.2)->4, with
	// Easy(0.35) clamped up to 1. ?spiderhelpercycles overrides with a fixed raw count.
	private static int HelperCyclePeriod()
	{
		int? overrideCycles = EvilAliensWeb.Compat.DebugFlags.SpiderHelperCycles;
		if (overrideCycles.HasValue)
		{
			return Math.Max(1, overrideCycles.Value);
		}
		return Math.Max(1, (int)Math.Round(3f * Settings.GetInstance().DifficultyFactorized(5f / 3f)));
	}

	private void SpawnHelper()
	{
		helper = SpiderHelperMothership.NewHelper(collection, base.Game);
		helper.Setup(
			EvilAliensWeb.Compat.DebugFlags.SpiderHelperHoverY,
			EvilAliensWeb.Compat.DebugFlags.SpiderHelperSpeed,
			EvilAliensWeb.Compat.DebugFlags.SpiderHelperFireSeconds * 1000f,
			EvilAliensWeb.Compat.DebugFlags.SpiderHelperFireLead,
			EvilAliensWeb.Compat.DebugFlags.SpiderHelperWindupSeconds * 1000f,
			this);
		helper.OnDeath += helper_OnDeath;
		collection.Add((GameComponent)(object)helper);
	}

	// A little warning arrow, top-left, announcing the incoming mothership -- now with the same
	// "ttf_warning" voice line every other boss/level warning banner plays (card 7deda68d: this used
	// to pass Nothing, so the arrow showed but never spoke). Reuses the boss's own redwarning arrow;
	// points up-left toward where the helper eases in from. Fired HelperWarningLeadMs BEFORE
	// SpawnHelper so it leads the arrival.
	private void WarnHelperIncoming()
	{
		AnimatedMessage warning = AnimatedMessage.NewAnimatedMessage(collection, base.Game);
		warning.Setup("Warning!", SoundManager.Texts.Warning, AnimatedMessage.MessageType.redwarning);
		warning.SetWarningDirection((float)Math.PI * 5f / 4f);
		warning.MakeShort();
		collection.Add((GameComponent)(object)warning);
		// Same lane as the sweep arrows above (card ee939dd1) -- the helper mothership is what
		// eventually kills this boss, so its arrival matters to both players.
		EvilAliensWeb.Compat.Net.NetSession.OnGameMessage(
			"Warning!", (int)SoundManager.Texts.Warning,
			(int)AnimatedMessage.MessageType.redwarning, (float)Math.PI * 5f / 4f, isShort: true);
	}

	// The centre of the boss's standing hitbox -- where the helper aims its beam on Easy/Medium when
	// the boss is a stationary (standing) target. Matches the SpiderBossState.standing collision box.
	// The "Danger!" arrow window: the boss is lined up off-screen in its lane and held by
	// waittimer for flyPauseMs before it sweeps. That pause exists to warn the player, so the AI
	// should use it the way a player does -- leave the lane BEFORE the boss crosses it, rather
	// than trying to out-accelerate a screen-wide sweep once it is already moving.
	// True for the WHOLE horizontal sweep -- the "Danger!" hold off-screen AND the crossing
	// itself. The lane is lethal for the entire time, not just while the arrow is up, so the AI
	// treats it as off limits throughout rather than trying to leave once the boss is already
	// on top of it.
	internal bool AiSweepIncoming => state == SpiderBossState.flyleft || state == SpiderBossState.flyright;

	// Centre of the horizontal band the sweep will actually occupy. The collision box snaps to
	// one of three lanes rather than tracking Position.Y exactly (see the flyleft/flyright case
	// in Update), so avoidance has to aim at the same snapped band or it dodges the wrong place.
	// The VERTICAL half of the cycle. Two strips, both always in the same place:
	//   land   -- the descent after a fly-by is hard-coded to X 600, falling from y -345 to 400.
	//   jump   -- the climb that starts the next cycle, straight up from wherever it is standing
	//             (which only drifts with the background scroll).
	// Like the horizontal sweep, the boss is either off-screen or barely moving when these start,
	// so nothing else in the AI sees them coming -- and the landing strip in particular is a
	// column the ship can simply be standing in.
	internal bool AiVerticalLaneActive => state == SpiderBossState.land
		|| state == SpiderBossState.jump
		|| state == SpiderBossState.flyup;

	internal float AiVerticalLaneX => base.Position.X;

	// True only for the DESCENT, which sweeps to the right screen edge -- so the escape is left,
	// not merely "away". The climb has no sweep and either side works.
	internal bool AiLandingSweep => state == SpiderBossState.land;

	internal float AiSweepLaneCentreY
	{
		get
		{
			float height = 186.66667f;
			if (base.Position.Y <= height)
			{
				return height * 0.5f;
			}
			if (base.Position.Y <= 1.5f * height)
			{
				return height * 1.5f;
			}
			return height * 2.5f;
		}
	}

	public Vector2 GetAimPoint()
	{
		return base.Position + new Vector2(20f * scale, 40f * scale);
	}

	// True while the boss is moving across the screen (fly/jump/land/drop) -- an unreliable aim
	// target, so the helper shoots straight down then. False only while standing on the ground.
	public bool IsFlyingAround()
	{
		return state != SpiderBossState.standing;
	}

	private void helper_OnDeath(object sender)
	{
		// Invariant: if the boss dies with a helper still airborne, GameScene.Purge removes the helper
		// (severing this handler) before either object is recycled, so this never nulls a recycled
		// boss's fresh helper ref.
		helper = null;
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsCoverage) --------
	// SpiderBoss draws `currentAnimation` (one of four AnimatedSprites) at `animationProgress` (its
	// own clock), with a horizontal flip + draw offset that depend on `state` -- all reached only by
	// the frozen Update. So a puppet needs three things beyond the base fields: the state (for the
	// Draw flip/offset AND the state-keyed collision box), which of the four sprites is current, and
	// the animation frame. It draws Color.White (no HP redden), so the base Hp is unused here. The
	// `dead` debris burst never crosses the wire -- an attributed remote death removes the puppet.
	internal byte NetState
	{
		get
		{
			return (byte)state;
		}
		set
		{
			// Never adopt `dead` from the wire (a dead boss is removed, never snapshotted); clamp
			// any stray value to a live state so the state-keyed CollisionType/Draw stay in range.
			SpiderBossState s = (SpiderBossState)value;
			if (s == SpiderBossState.dead)
			{
				s = SpiderBossState.standing;
			}
			state = s;
		}
	}

	// 0 = fly, 1 = stand, 2 = jump, 3 = land. currentAnimation is not 1:1 with state (the sprite
	// swaps lag the state transitions), so it is streamed independently. A null target sprite (before
	// LoadContent) leaves the current pick untouched.
	internal byte NetAnimIndex
	{
		get
		{
			if (currentAnimation == spiderStand)
			{
				return 1;
			}
			if (currentAnimation == spiderJump)
			{
				return 2;
			}
			if (currentAnimation == spiderLand)
			{
				return 3;
			}
			return 0;
		}
		set
		{
			AnimatedSprite target = value switch
			{
				1 => spiderStand,
				2 => spiderJump,
				3 => spiderLand,
				_ => spiderFly,
			};
			if (target != null)
			{
				currentAnimation = target;
			}
		}
	}

	// (int)animationProgress; always < currentAnimation.Frames on the host (Update mods it), so the
	// streamed byte is a valid index for whichever sprite NetAnimIndex just selected.
	internal byte NetAnimFrame
	{
		get
		{
			return (byte)(int)animationProgress;
		}
		set
		{
			animationProgress = value;
		}
	}

	// Card 43e85936: the client half of the hit beat emitted in CollidesWith. Reproduces exactly
	// what the host's own hit branch does MINUS the hp spend and the kill check -- the hit itself
	// is the host's to count, and the death arrives as an ordinary EvDeath.
	//
	// Idempotent on `hittimer.Active`: a Lazer the client saw connect already ran the real branch,
	// so the host's beat for that same hit lands inside the 800ms blink and does nothing.
	// (BloodExplosion is not a replicable type, so the bleed spray is a legal local spawn on a
	// client -- the same one its own hits produce.)
	//
	// NOTE the gate is NOT the same one CollidesWith opens with, unlike KillableAlien's. This boss
	// dedupes PER LAZER (`alreadyHitBy`), not on the blink, so the host can take two hits from two
	// beams inside one 800ms blink and the client will show one. Accepted: the alternative is
	// mirroring a beam-identity set the wire does not carry, for a second flash inside a blink
	// that is already lit. The hp and the death are host-authoritative either way.
	internal override void NetPlayFx(EvilAliensWeb.Compat.Net.NetFxKind kind)
	{
		if (kind != EvilAliensWeb.Compat.Net.NetFxKind.EnemyHitFlash || base.IsDead || hittimer.Active)
		{
			return;
		}
		sound.PlayCue("bugdies");
		sound.PlayCue("bugdies");
		hittimer.Start();
		hittimer.Reset();
		for (int i = 0; i < 5; i++)
		{
			Bleed(2.5f);
		}
	}
}
