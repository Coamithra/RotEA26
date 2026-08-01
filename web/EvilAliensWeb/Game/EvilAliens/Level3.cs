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
		ApplyDifficultyPolicy();
		Settings.GetInstance().UnlockDifficulty();
		base.spawnPlayerNormally = true;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/brainbosshd");
		contentManager.Load<Texture2D>("GFX/Sprites/brainbossaura");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_idle");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_attract");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Base/black_line_lalalal");
		contentManager.Load<Texture2D>("GFX/Base/756-v1");
		contentManager.Load<Texture2D>("GFX/Base/756");
		contentManager.Load<Texture2D>("GFX/Base/756-v5");
		contentManager.Load<Texture2D>("GFX/Base/756-v3");
		contentManager.Load<Texture2D>("GFX/Base/756-v4");
		contentManager.Load<Texture2D>("GFX/Base/756-v6");
		contentManager.Load<Texture2D>("GFX/Base/756-v8");
		// The GPU analogue of the throwaway enemy spawns below (and in GameScene): compile+link the
		// Level-3 tower BasicEffect's GL program now, on the loading screen, instead of at the first
		// wall. ANGLE defers that compile to a program's first draw and Chrome caches it, so the first
		// DrawGeometry3D of a cold session otherwise stalled ~120ms mid-play (Trello 3e81fdcd) — the one
		// first-use cost no texture preload covers, since it is the program, not an asset. Idempotent, so
		// the tower scenes (Level3/Demo3/OwnLevel) that each warm it only pay the compile once.
		SpriteBatch.WarmGeometry3D();
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
		if (EvilAliensWeb.Compat.DebugFlags.WallsOnly)
		{
			// DEBUG (?level=Level3&wallsonly): skip the whole wave sequence and loop the walls
			// sections back to back, so the 3D tower rendering can be watched without minutes of
			// play per iteration. Mirrors Level2's ?spiderboss. Pair with ?invuln.
			PopulateWallsOnly();
			return;
		}
		if (EvilAliensWeb.Compat.DebugFlags.WallPopTest)
		{
			// DEBUG (?level=Level3&wallpoptest): chain ten SMALL (~2-screen) wall sections and drop
			// the scroll to ~10% once the 2nd loads, so the entry pop is slow + unmistakable. Pair
			// with ?invuln (and ?walltrace for numbers).
			PopulateWallPopTest();
			return;
		}
		if (EvilAliensWeb.Compat.DebugFlags.BrainBoss)
		{
			// DEBUG (?brainboss): skip Level 3's whole wave sequence and drop straight into the
			// real BrainBoss fight -- UNCONDITIONALLY (any difficulty), so the brain-boss animated
			// overlays + hit_boss SFX can be verified without grinding the level or being on Hard+.
			// Mirrors the boss tail of BrainBossHard() (music -> spawn -> halt), then Victory.
			PopulateBrainBossOnly();
			return;
		}
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
		SkullSpawner skullSpawner = new SkullSpawner(base.Game, 10f, 1f, maze: false, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: true);
		eventList.AddHalt();
		skullSpawner = new SkullSpawner(base.Game, 10f, 2f, maze: false, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: true);
		eventList.AddHalt();
		skullSpawner = new SkullSpawner(base.Game, 20f, 3.3f, maze: false, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: true);
		skullSpawner.OnFinished += speedup;
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
		skullSpawner = new SkullSpawner(base.Game, 0f, 1f, maze: true, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: false);
		walls.LinkWith(skullSpawner);
		BattleSkullEvent battleSkullEvent = new BattleSkullEvent(base.Game, 0f, 0.5f);
		eventList.AddEvent(battleSkullEvent, halting: false);
		eventList.AddHalt();
		walls.LinkWith(battleSkullEvent);
		Wait(6f);
		messageEvent = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.OnFinished += swapBG2;
		walls = new Walls(base.Game, 1);
		eventList.AddEvent(walls, halting: true);
		eventList.SetLastEventAsCheckPoint();
		skullSpawner = new SkullSpawner(base.Game, 0f, 3f, maze: true, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: false);
		walls.LinkWith(skullSpawner);
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
		skullSpawner = new SkullSpawner(base.Game, 0f, 0.1f, maze: false, bonusonly: true);
		eventList.AddEvent(skullSpawner, halting: false);
		starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.75f);
		eventList.AddEvent(starMineSpawner, halting: false);
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.SetBase();
		eventList.AddEvent(junkBossSpawner);
		eventList.AddHalt();
		junkBossSpawner.LinkWith(skullSpawner);
		junkBossSpawner.LinkWith(starMineSpawner);
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent);
		messageEvent.OnFinished += swapBG4;
		Wait(5f);
		UnlockEvent unlockEvent = new UnlockEvent(base.Game, "Crazy Game", Unlockables.Items.CrazyGame, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += speedup;
		skullSpawner = new SkullSpawner(base.Game, 0f, 0.8f, maze: true, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: false);
		starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.6f);
		eventList.AddEvent(starMineSpawner, halting: false);
		walls = new Walls(base.Game, 3);
		eventList.AddEvent(walls, halting: true);
		eventList.AddHalt();
		walls.LinkWith(skullSpawner);
		walls.LinkWith(starMineSpawner);
		Wait(1f);
		unlockEvent = new UnlockEvent(base.Game, "Boss Train", Unlockables.Items.BossTrain, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent);
		messageEvent.OnFinished += swapBG5;
		BrainBossHard();
		FakeBossEasy();
		unlockEvent = new UnlockEvent(base.Game, "Power Up", Unlockables.Items.PowerUp, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		unlockEvent = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.HarderDifficulties, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		unlockEvent = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.InsaneDifficulty, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		unlockEvent.OnFinished += Victory;
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

	// DEBUG (?wallsonly): gives lives, jumps to the level's normal walls-section scroll speed, then
	// runs the three big wall variations back to back (twice) with nothing else spawning, so the
	// 3D towers are seen in REAL play. Reached only via DebugFlags.WallsOnly -- live play unaffected.
	private void PopulateWallsOnly()
	{
		WaitEvent waitEvent = Wait(0.1f);
		waitEvent.OnFinished += returnlives;
		waitEvent.OnFinished += speedup;
		// 1 = the dense maze (most tower-like), 0 = tall sparse pillars, 3 = big diagonal slabs.
		int[] variations = new int[3] { 1, 0, 3 };
		// Cycle the alien-base FLOOR through all six variants as the sections scroll, so the tower fog
		// (which tracks the live floor colour via oracle.AlienBaseFloorColor) can be watched changing
		// without playing the whole level. Same handlers the real Level 3 uses; the initial floor is
		// SetAlienBase (set in Populate), then one swap per section.
		GameEvent.GameEventMessage[] floorSwaps = { swapBG1, swapBG2, swapBG3, swapBG4, swapBG5 };
		int section = 0;
		for (int cycle = 0; cycle < 2; cycle++)
		{
			for (int i = 0; i < variations.Length; i++)
			{
				Walls walls = new Walls(base.Game, variations[i]);
				if (section < floorSwaps.Length)
				{
					walls.OnFinished += floorSwaps[section];
				}
				section++;
				eventList.AddEvent(walls, halting: true);
				eventList.SetLastEventAsCheckPoint();
				eventList.AddHalt();
			}
		}
		WaitEvent victoryEvent = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(victoryEvent, halting: true);
		eventList.AddHalt();
		victoryEvent.OnFinished += Victory;
	}

	// DEBUG (?wallpoptest): chain ten SMALL (~2-screen) wall sections (poptest0..9.txt), each a
	// distinct pattern, halting so they play strictly one after another. Section 0 runs at normal
	// speed; the instant it ends (section 1 loads at the top) the scroll drops to ~10% via
	// popTestSlow, so every later section's ENTRY is slow and unmistakable -- making it obvious
	// whether the "pop" tracks a block's screen POSITION (a geometry/cull effect) or is a one-off
	// load/cache hitch (which would happen once, not on every slow entry).
	private void PopulateWallPopTest()
	{
		WaitEvent waitEvent = Wait(0.1f);
		waitEvent.OnFinished += returnlives;
		waitEvent.OnFinished += speedup;   // section 0 at normal speed
		for (int i = 0; i < 10; i++)
		{
			Walls walls = new Walls(base.Game, "poptest" + i + ".txt");
			if (i == 0)
			{
				walls.OnFinished += popTestSlow;   // drop to 10% as section 1 enters
			}
			eventList.AddEvent(walls, halting: true);
			eventList.SetLastEventAsCheckPoint();
			eventList.AddHalt();
		}
		WaitEvent victoryEvent = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(victoryEvent, halting: true);
		eventList.AddHalt();
		victoryEvent.OnFinished += Victory;
	}

	private void popTestSlow(GameEvent sender)
	{
		// 10% of the normal wall-section speed (speedup uses 4.3 x difficulty).
		Background.SetSpeed(new Vector2(0f, 0.43f * Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty)) / 16.666666f);
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

	// DEBUG (?brainboss): the boss tail of BrainBossHard() with the difficulty gate REMOVED --
	// gives lives, plays the boss music, spawns the real BrainBoss, halts until it dies, then
	// Victory. Reached only via DebugFlags.BrainBoss, so live play is unaffected.
	private void PopulateBrainBossOnly()
	{
		WaitEvent waitEvent = Wait(0.1f);
		waitEvent.OnFinished += returnlives;
		waitEvent.OnFinished += playbossmusic;
		BrainBossSpawner brainBossSpawner = new BrainBossSpawner(base.Game, challenge: false);
		eventList.AddEvent(brainBossSpawner);
		eventList.AddHalt();
		WaitEvent victoryEvent = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(victoryEvent, halting: true);
		eventList.AddHalt();
		victoryEvent.OnFinished += Victory;
	}

	private void playbossmusic(GameEvent sender)
	{
		base.SoundManager.PlayMusic(Songs.Kylikova);
		base.SoundManager.SetMusicRate(50f);
	}

	private void spawn1ups(GameEvent sender)
	{
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
		Background.SetSpeed(new Vector2(0f, 0.2f) / 16.666666f);
	}

	private void speedup(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(0f, 4.3f * Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty)) / 16.666666f);
	}

	private void speedupuber1(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(0f, 0.72f));
	}

	private void bossspeed(GameEvent sender)
	{
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
