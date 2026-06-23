using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Level2 : GameScene
{
	private const int InitialLives = 7;

	private SoundEffectInstance bees;

	private Floor floor;

	private SpiderBoss preloadBoss;

	public Level2(Game game)
		: base(game, Levels.Level2)
	{
		base.OnFinished += Level2_OnFinished;
		base.OnReset += Level2_OnReset;
		spawnType = PlayerSpawnType.West;
		floor = new Floor(base.Game);
	}

	private void Level2_OnFinished(object sender, FinishedArgs args)
	{
		Collection.Remove((GameComponent)(object)floor);
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)13);
		base.SoundManager.PlayMusic(Songs.Level2);
		Collection.Add((GameComponent)(object)floor);
		Background.SetMars();
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
		if (EvilAliensWeb.Compat.DebugFlags.Win)
		{
			Settings.GetInstance().CurrentDifficulty = Settings.DifficultyLevel.Hard;
			System.Console.WriteLine("[trace] Level2 DEBUG ?win: forced difficulty=Hard");
		}
		base.spawnPlayerNormally = true;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop_green");
		contentManager.Load<Texture2D>("GFX/Sprites/spider_sheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris1");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris3");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderjump");
		contentManager.Load<Texture2D>("GFX/Sprites/ufometpootjes");
		contentManager.Load<Texture2D>("GFX/Sprites/wing1");
		contentManager.Load<Texture2D>("GFX/Sprites/shadow");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipA");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipB");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Sprites/Smallship_landed");
		contentManager.Load<Texture2D>("GFX/Sprites/Mediumship_landed");
		contentManager.Load<Texture2D>("GFX/Sprites/Mothership_landed");
		contentManager.Load<Texture2D>("GFX/Spider/spiderfly");
		contentManager.Load<Texture2D>("GFX/Spider/spiderjump");
		contentManager.Load<Texture2D>("GFX/Spider/spiderland");
		contentManager.Load<Texture2D>("GFX/Spider/spiderstand");
		preloadBoss = SpiderBoss.NewSpiderBoss(Collection, base.Game);
		preloadBoss.Setup(intro: false);
		preloadBoss.SetupPreload();
		Collection.Add((GameComponent)(object)preloadBoss);
	}

	public override void Update(GameTime gameTime)
	{
		if (preloadBoss != null)
		{
			Collection.Remove((GameComponent)(object)preloadBoss);
			preloadBoss = null;
		}
		base.Update(gameTime);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			base.SoundManager.Stop(bees);
		}
	}

	private void Level2_OnReset()
	{
		base.SoundManager.Stop(bees);
	}

	protected override void PopulateEventList()
	{
		if (EvilAliensWeb.Compat.DebugFlags.Win)
		{
			// DEBUG repro (?win): skip the whole level and run ONLY the ending unlock
			// sequence -> Victory, mirroring the real tail below, so the Hard ending
			// handoff can be reproduced in seconds instead of a full playthrough.
			UnlockEvent ue = new UnlockEvent(base.Game, "Base Pressure", Unlockables.Items.OwnLevel, AnimatedMessage.UnlockType.challenge, level);
			eventList.AddEvent(ue, halting: true);
			eventList.AddHalt();
			ue = new UnlockEvent(base.Game, "Turbo", Unlockables.Items.Turbo, AnimatedMessage.UnlockType.cheat, level);
			eventList.AddEvent(ue, halting: true);
			eventList.AddHalt();
			ue = new UnlockEvent(base.Game, "Next Mission!", Unlockables.Items.Level3, AnimatedMessage.UnlockType.level, level);
			eventList.AddEvent(ue, halting: true);
			eventList.AddHalt();
			ue = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.InsaneDifficulty, AnimatedMessage.UnlockType.difficulty, level);
			eventList.AddEvent(ue, halting: true);
			eventList.AddHalt();
			ue.OnFinished += Victory;
			return;
		}
		WaitEvent waitEvent = Wait(0.1f);
		waitEvent.OnFinished += resetlives;
		StationaryWave(8f, 3f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(3f, 0f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 0f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 0.8f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 1.8f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 3.5f);
		Wait(6f);
		MessageEvent gameEvent = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 0f, 0.5f, big: false);
		ufoSpawner.SetupMars();
		eventList.AddEvent(ufoSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		WaitEvent waitEvent2 = new WaitEvent(base.Game, 0.1f);
		waitEvent2.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.5f, big: false);
		ufoSpawner.SetupMars();
		ufoSpawner.SetupFastEntry();
		waitEvent2.LinkWith(ufoSpawner);
		eventList.AddEvent(ufoSpawner, halting: false);
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 0f, 0.1f, randomly: true);
		bonusSpawner.SetMars();
		eventList.AddEvent(bonusSpawner, halting: false);
		waitEvent2.LinkWith(bonusSpawner);
		StationaryWave(4f, 3f, 96f, 4f, 0f, 0f);
		Wait(4f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		Wait(3f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		gameEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		gameEvent.SetupAsWarning((float)Math.PI / 8f);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent, halting: false);
		waitEvent.OnFinished += spawnStationaryBoss;
		Wait(5f);
		StationaryWave(1f, 6f, 1f, 1f, 0f, 0f);
		Wait(4f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		Wait(3f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		gameEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		gameEvent.SetupAsWarning((float)Math.PI / 8f);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent, halting: false);
		waitEvent.OnFinished += spawnStationaryBoss;
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		Wait(3f);
		StationaryWave(1f, 6f, 1f, 1f, 0f, 0f);
		eventList.AddEvent(waitEvent2, halting: false);
		waitEvent = new WaitEvent(base.Game, 10f);
		eventList.AddEvent(waitEvent, halting: true);
		waitEvent.OnFinished += slowdown;
		eventList.AddHalt();
		gameEvent = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 25f, 2.1f, big: false);
		ufoSpawner.SetupMarsWest();
		eventList.AddEvent(ufoSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		ufoSpawner = new UfoSpawner(base.Game, 25f, 0.15f, big: true);
		ufoSpawner.SetupMarsWest();
		eventList.AddEvent(ufoSpawner, halting: false);
		bonusSpawner = new BonusSpawner(base.Game, 25f, 0.2f, randomly: true);
		eventList.AddEvent(bonusSpawner, halting: false);
		Wait(5f);
		StationaryWave(20f, 0.5f, 0f, 0f, 0f, 1f);
		waitEvent = new WaitEvent(base.Game, 6f);
		eventList.AddEvent(waitEvent, halting: true);
		waitEvent.OnFinished += speedup;
		eventList.AddHalt();
		gameEvent = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		gameEvent.SetupAsWarning((float)Math.PI / 8f);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		MarsBossSpawner marsBossSpawner = new MarsBossSpawner(base.Game);
		Wait(3f);
		StationarySpawner stationarySpawner = new StationarySpawner(base.Game, 560f, 0f, 0.8f);
		stationarySpawner.SetChances(0f, 0f, 0f, 1f);
		marsBossSpawner.LinkWith(stationarySpawner);
		eventList.AddEvent(stationarySpawner, halting: false);
		Wait(5f);
		gameEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		gameEvent.SetupAsWarning(3.7699115f);
		eventList.AddEvent(gameEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.2f, randomly: true);
		bonusSpawner.SetMars();
		marsBossSpawner.LinkWith(bonusSpawner);
		eventList.AddEvent(bonusSpawner, halting: false);
		eventList.AddEvent(marsBossSpawner, halting: true);
		eventList.AddHalt();
		Wait(6.5f);
		gameEvent = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent);
		Wait(5f);
		UnlockEvent gameEvent2 = new UnlockEvent(base.Game, "Paratroopers", Unlockables.Items.Paratrooper, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent, halting: true);
		waitEvent.OnFinished += slowDownBasedOnDifficulty;
		eventList.AddHalt();
		Wait(2f);
		WaitEvent waitEvent3 = Wait(0.1f);
		waitEvent3.OnFinished += beesSoundOn;
		eventList.SetLastEventAsCheckPoint();
		FlyingSpiderEvent gameEvent3 = new FlyingSpiderEvent(base.Game, 0f, 5.5f, isbackground: true);
		eventList.AddEvent(gameEvent3, halting: false);
		Wait(4f);
		FlyingSpiderEvent gameEvent4 = new FlyingSpiderEvent(base.Game, 0f, 2f, isbackground: false);
		eventList.AddEvent(gameEvent4, halting: false);
		Wait(2.5f);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 1.6f, big: false);
		ufoSpawner.SetupMarsWest();
		eventList.AddEvent(ufoSpawner, halting: false);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.2f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		Wait(25f);
		SweepUFOSpawner sweepUFOSpawner = new SweepUFOSpawner(base.Game, 35f, 0.13f);
		eventList.AddEvent(sweepUFOSpawner, halting: true);
		sweepUFOSpawner.LinkWith(bonusSpawner);
		sweepUFOSpawner.LinkWith(ufoSpawner);
		sweepUFOSpawner.LinkWith(gameEvent4);
		eventList.AddHalt();
		Wait(1f);
		gameEvent = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent);
		waitEvent3 = Wait(5f);
		waitEvent3.LinkWith(gameEvent3);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		eventList.AddEvent(gameEvent);
		gameEvent.SetupAsWarning(0f);
		waitEvent = Wait(4f);
		waitEvent.OnFinished += beesSoundOff;
		waitEvent.OnFinished += halt;
		SpiderBossEvent spiderBossEvent = new SpiderBossEvent(base.Game);
		eventList.AddEvent(spiderBossEvent, halting: false);
		Wait(8f);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		spiderBossEvent.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.2f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		spiderBossEvent.LinkWith(ufoSpawner);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.08f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		spiderBossEvent.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.15f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner.DoNotScale();
		spiderBossEvent.LinkWith(ufoSpawner);
		eventList.AddHalt();
		waitEvent = Wait(5f);
		waitEvent.OnFinished += speedup;
		waitEvent.OnFinished += invuln;
		Wait(2f);
		gameEvent2 = new UnlockEvent(base.Game, "Base Pressure", Unlockables.Items.OwnLevel, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UnlockEvent(base.Game, "Turbo", Unlockables.Items.Turbo, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UnlockEvent(base.Game, "Next Mission!", Unlockables.Items.Level3, AnimatedMessage.UnlockType.level, level);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.InsaneDifficulty, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2.OnFinished += Victory;
	}

	private void invuln(GameEvent sender)
	{
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.TemporaryInvulnerability(600);
		}
	}

	private void halt(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(-0.2f, 0f) / 16.666666f);
	}

	private void beesSoundOn(GameEvent sender)
	{
		bees = base.SoundManager.Play("bees");
	}

	private void beesSoundOff(GameEvent sender)
	{
		base.SoundManager.Stop(bees);
	}

	private void resetlives(GameEvent sender)
	{
		if (score.Lives >= 0)
		{
			while (score.Lives < 7)
			{
				score.AddLife();
			}
		}
	}

	private void Victory(GameEvent sender)
	{
		ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.SecondAct);
		Victory();
	}

	private void spawnBosses(GameEvent sender)
	{
		MarsBoss marsBoss = MarsBoss.NewMarsBoss(Collection, base.Game);
		marsBoss.Setup(MarsBoss.BossPosition.left);
		Collection.Add((GameComponent)(object)marsBoss);
		marsBoss = MarsBoss.NewMarsBoss(Collection, base.Game);
		marsBoss.Setup(MarsBoss.BossPosition.right);
		Collection.Add((GameComponent)(object)marsBoss);
	}

	private void StationaryWave(float time, float chance, float ufochance, float bigufochance, float brainchance, float spiderchance)
	{
		StationarySpawner stationarySpawner = new StationarySpawner(base.Game, 560f, time, chance);
		stationarySpawner.SetChances(ufochance, bigufochance, brainchance, spiderchance);
		eventList.AddEvent(stationarySpawner, halting: true);
		eventList.AddHalt();
	}

	private WaitEvent Wait(float time)
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, time);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		return waitEvent;
	}

	private void UfoWave(float fastufochance, float normalufochance)
	{
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 1f, 0.5f, randomly: false);
		bonusSpawner.SetMars();
		eventList.AddEvent(bonusSpawner, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 3f, fastufochance, big: false);
		ufoSpawner.SetupMars();
		ufoSpawner.SetupFastEntry();
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner = new UfoSpawner(base.Game, 3f, normalufochance, big: false);
		ufoSpawner.SetupMars();
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
	}

	private void slowdown(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(-3f, 0f) / 16.666666f);
	}

	private void slowDownBasedOnDifficulty(GameEvent sender)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		float num = -1f + Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty) * -4f;
		Background.SetSpeed(new Vector2(num, 0f) / 16.666666f);
	}

	private void speedup(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(-10f, 0f) / 16.666666f);
	}

	private void spawnStationaryBoss(GameEvent sender)
	{
		StationaryBoss component = StationaryBoss.NewAlien(Collection, base.Game);
		Collection.Add((GameComponent)(object)component);
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		Victory();
	}
}
