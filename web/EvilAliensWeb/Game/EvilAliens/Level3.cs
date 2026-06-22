using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Level3 : GameScene
{
	private const int InitialLives = 7;

	private BattleSkull preloadBattleSkull;

	public Level3(Game game)
		: base(game, Levels.Level3)
	{
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)13);
		base.SoundManager.PlayMusic(Songs.Level3);
		Background.SetAlienBase();
		base.Initialize();
		if (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Easy)
		{
			Settings.GetInstance().DirectRespawn = true;
			Settings.GetInstance().AdaptiveDifficulty = true;
			Settings.GetInstance().DifficultyModifier = Settings.GetInstance().GetDifficultyValue(Settings.DifficultyLevel.Medium);
		}
		if ((Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Hard) | (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Inzane) | (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Very_Hard))
		{
			score.Lives = 7;
		}
		Settings.GetInstance().UnlockDifficulty();
		base.spawnPlayerNormally = true;
	}

	protected override void PreloadGraphicalContent()
	{
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/brainlargetransglow");
		contentManager.Load<Texture2D>("GFX/Sprites/cablesback");
		contentManager.Load<Texture2D>("GFX/Sprites/cablesfront");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionspritepurple");
		contentManager.Load<Texture2D>("GFX/Sprites/eye");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Base/black line lalalal");
		contentManager.Load<Texture2D>("GFX/Base/756-v1");
		contentManager.Load<Texture2D>("GFX/Base/756");
		contentManager.Load<Texture2D>("GFX/Base/756-v5");
		contentManager.Load<Texture2D>("GFX/Base/756-v3");
		contentManager.Load<Texture2D>("GFX/Base/756-v4");
		contentManager.Load<Texture2D>("GFX/Base/756-v6");
		contentManager.Load<Texture2D>("GFX/Base/756-v8");
		preloadBattleSkull = BattleSkull.NewBattleSkull(Collection, base.Game);
		preloadBattleSkull.Setup(new Vector2(-1000f, -1000f));
		Collection.Add((GameComponent)(object)preloadBattleSkull);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (preloadBattleSkull != null)
		{
			Collection.Remove((GameComponent)(object)preloadBattleSkull);
			preloadBattleSkull = null;
		}
	}

	protected override void PopulateEventList()
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += slowdown;
		waitEvent = Wait(9.3f);
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += slowdown;
		waitEvent.OnFinished += returnlives;
		eventList.SetLastEventAsCheckPoint();
		SkullSpawner gameEvent = new SkullSpawner(base.Game, 10f, 1f, maze: false, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		gameEvent = new SkullSpawner(base.Game, 10f, 2f, maze: false, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		gameEvent = new SkullSpawner(base.Game, 20f, 3.3f, maze: false, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: true);
		gameEvent.OnFinished += speedup;
		eventList.AddHalt();
		Wait(5f);
		MessageEvent messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.OnFinished += swapBG1;
		Wait(1.5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		Wait(0.2f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.SetupAsWarning((float)Math.PI * 3f / 4f);
		Wait(0.2f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.SetupAsWarning((float)Math.PI / 4f);
		Wait(3f);
		Walls walls = new Walls(base.Game, 0);
		eventList.AddEvent(walls, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new SkullSpawner(base.Game, 0f, 1f, maze: true, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: false);
		walls.LinkWith(gameEvent);
		BattleSkullEvent gameEvent2 = new BattleSkullEvent(base.Game, 0f, 0.5f);
		eventList.AddEvent(gameEvent2, halting: false);
		eventList.AddHalt();
		walls.LinkWith(gameEvent2);
		Wait(6f);
		messageEvent = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.OnFinished += swapBG2;
		walls = new Walls(base.Game, 1);
		eventList.AddEvent(walls, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new SkullSpawner(base.Game, 0f, 3f, maze: true, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: false);
		walls.LinkWith(gameEvent);
		eventList.AddHalt();
		Wait(4f);
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent);
		messageEvent.OnFinished += swapBG3;
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += slowdown;
		eventList.SetLastEventAsCheckPoint();
		eventList.AddHalt();
		StarMineSpawner starMineSpawner = new StarMineSpawner(base.Game, 20f, 0.7f);
		eventList.AddEvent(starMineSpawner);
		eventList.AddHalt();
		starMineSpawner.OnFinished += speedup;
		starMineSpawner = new StarMineSpawner(base.Game, 15f, 1.4f);
		eventList.AddEvent(starMineSpawner);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		starMineSpawner = new StarMineSpawner(base.Game, 15f, 2f);
		eventList.AddEvent(starMineSpawner);
		eventList.AddHalt();
		starMineSpawner = new StarMineSpawner(base.Game, 20f, 2.5f);
		eventList.AddEvent(starMineSpawner);
		eventList.AddHalt();
		starMineSpawner = new StarMineSpawner(base.Game, 5f, 0.7f);
		eventList.AddEvent(starMineSpawner, halting: false);
		starMineSpawner.OnFinished += bossspeed;
		eventList.SetLastEventAsCheckPoint();
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		Wait(5f);
		gameEvent = new SkullSpawner(base.Game, 0f, 0.1f, maze: false, bonusonly: true);
		eventList.AddEvent(gameEvent, halting: false);
		starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.75f);
		eventList.AddEvent(starMineSpawner, halting: false);
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.SetBase();
		eventList.AddEvent(junkBossSpawner);
		eventList.AddHalt();
		junkBossSpawner.LinkWith(gameEvent);
		junkBossSpawner.LinkWith(starMineSpawner);
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent);
		messageEvent.OnFinished += swapBG4;
		Wait(5f);
		UnlockEvent gameEvent3 = new UnlockEvent(base.Game, "Crazy Game", Unlockables.Items.CrazyGame, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(gameEvent3);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += speedup;
		gameEvent = new SkullSpawner(base.Game, 0f, 0.8f, maze: true, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: false);
		starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.6f);
		eventList.AddEvent(starMineSpawner, halting: false);
		walls = new Walls(base.Game, 3);
		eventList.AddEvent(walls, halting: true);
		eventList.AddHalt();
		walls.LinkWith(gameEvent);
		walls.LinkWith(starMineSpawner);
		Wait(1f);
		gameEvent3 = new UnlockEvent(base.Game, "Boss Train", Unlockables.Items.BossTrain, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent);
		messageEvent.OnFinished += swapBG5;
		BrainBossHard();
		FakeBossEasy();
		gameEvent3 = new UnlockEvent(base.Game, "Power Up", Unlockables.Items.PowerUp, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.HarderDifficulties, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.InsaneDifficulty, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent3.OnFinished += Victory;
	}

	private void swapBG5(GameEvent sender)
	{
		Background.SetAlienBase6();
	}

	private void swapBG4(GameEvent sender)
	{
		Background.SetAlienBase5();
	}

	private void swapBG3(GameEvent sender)
	{
		Background.SetAlienBase4();
	}

	private void swapBG2(GameEvent sender)
	{
		Background.SetAlienBase3();
	}

	private void swapBG1(GameEvent sender)
	{
		Background.SetAlienBase2();
	}

	private void returnlives(GameEvent sender)
	{
		if (score.Lives >= 0)
		{
			while (score.Lives < 7)
			{
				score.AddLife();
			}
		}
	}

	private void FakeBossEasy()
	{
		WaitEvent a_event = Wait(3f);
		eventList.MakeConditional(a_event, Settings.DifficultyLevel.Easy, Settings.DifficultyLevel.Medium);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent);
		eventList.MakeConditional(messageEvent, Settings.DifficultyLevel.Easy, Settings.DifficultyLevel.Medium);
		eventList.SetLastEventAsCheckPoint();
		a_event = Wait(3f);
		eventList.MakeConditional(a_event, Settings.DifficultyLevel.Easy, Settings.DifficultyLevel.Medium);
		FakeBossSpawner fakeBossSpawner = new FakeBossSpawner(base.Game);
		eventList.AddEvent(fakeBossSpawner);
		eventList.MakeConditional(fakeBossSpawner, Settings.DifficultyLevel.Easy, Settings.DifficultyLevel.Medium);
		eventList.AddHalt();
	}

	private void BrainBossHard()
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += spawn1ups;
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		waitEvent = new WaitEvent(base.Game, 10f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		waitEvent = new WaitEvent(base.Game, 0.1f);
		waitEvent.OnFinished += speedupuber1;
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		waitEvent.OnFinished += playbossmusic;
		waitEvent = new WaitEvent(base.Game, 5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		Walls walls = new Walls(base.Game, 4);
		eventList.AddEvent(walls);
		eventList.AddHalt();
		eventList.MakeConditional(walls, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent);
		eventList.MakeConditional(messageEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		walls = new Walls(base.Game, 4);
		eventList.AddEvent(walls);
		eventList.AddHalt();
		walls.OnFinished += bossspeed;
		eventList.MakeConditional(walls, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		BrainBossSpawner brainBossSpawner = new BrainBossSpawner(base.Game, challenge: false);
		eventList.AddEvent(brainBossSpawner);
		eventList.MakeConditional(brainBossSpawner, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		eventList.AddHalt();
	}

	private void playbossmusic(GameEvent sender)
	{
		base.SoundManager.PlayMusic(Songs.Kylikova);
		base.SoundManager.SetMusicRate(50f);
	}

	private void spawn1ups(GameEvent sender)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		UFO uFO = UFO.NewUFO(Collection, base.Game);
		uFO.Setup(new Vector2(400f, -100f), isBig: true, EnemyBehaviour.classic);
		uFO.SetAsBonus(Powerup.PowerupType.OneUp);
		Collection.Add((GameComponent)(object)uFO);
		Collection.ClearCache();
	}

	private void Victory(GameEvent sender)
	{
		ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.ThirdAct);
		if (Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.TrueEnding);
		}
		Victory();
	}

	private void slowdown(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 0.2f) / 16.666666f);
	}

	private void speedup(GameEvent sender)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 4.3f * Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty)) / 16.666666f);
	}

	private void speedupuber1(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 0.72f));
	}

	private void bossspeed(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 3f) / 16.666666f);
	}

	private WaitEvent Wait(float time)
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, time);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		return waitEvent;
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		Victory();
	}
}
