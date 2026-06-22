using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class InsaneBossI : GameScene
{
	private bool backgroundchanged;

	private Floor f;

	public InsaneBossI(Game game)
		: base(game, Levels.InsaneBossI)
	{
		f = new Floor(base.Game);
		base.OnFinished += InsaneBossI_OnFinished;
	}

	private void InsaneBossI_OnFinished(object sender, FinishedArgs args)
	{
		Collection.Remove((GameComponent)(object)f);
	}

	public override void Initialize()
	{
		base.Initialize();
		setPresence((GamerPresenceMode)34);
		switch (Settings.GetInstance().CurrentDifficulty)
		{
		case Settings.DifficultyLevel.Hard:
			score.Lives = 5;
			break;
		case Settings.DifficultyLevel.Very_Hard:
			score.Lives = 5;
			break;
		case Settings.DifficultyLevel.Inzane:
			score.Lives = 1;
			break;
		}
		spawnType = PlayerSpawnType.South;
		Background.SetSpace();
		base.SoundManager.PlayMusic(Songs.Level1);
		backgroundchanged = false;
		Settings.GetInstance().LockDifficulty();
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/andromeda");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/earth");
		contentManager.Load<Texture2D>("GFX/Sprites/eye");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipA");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipB");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop_green");
		contentManager.Load<Texture2D>("GFX/Sprites/spider_sheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris1");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris3");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderjump");
		contentManager.Load<Texture2D>("GFX/Sprites/ufometpootjes");
		contentManager.Load<Texture2D>("GFX/Sprites/wing1");
		contentManager.Load<Texture2D>("GFX/Sprites/shadow");
		contentManager.Load<Texture2D>("GFX/Sprites/cablesback");
		contentManager.Load<Texture2D>("GFX/Sprites/cablesfront");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionspritepurple");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		contentManager.Load<Texture2D>("GFX/Spider/spiderfly");
		contentManager.Load<Texture2D>("GFX/Spider/spiderjump");
		contentManager.Load<Texture2D>("GFX/Spider/spiderland");
		contentManager.Load<Texture2D>("GFX/Spider/spiderstand");
	}

	protected override void PopulateEventList()
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += GoSpace;
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(base.Game);
		BonusSpawner gameEvent = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		bossSpawner.LinkWith(gameEvent);
		UfoSpawner gameEvent2 = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		eventList.AddEvent(bossSpawner);
		eventList.AddHalt();
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.12f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		gameEvent = new BonusSpawner(base.Game, 0f, 0.046f, randomly: true);
		eventList.AddEvent(gameEvent, halting: false);
		junkBossSpawner.LinkWith(gameEvent);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.053f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		waitEvent = Wait(5f);
		waitEvent.OnFinished += GoMars;
		Wait(3f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning((float)Math.PI / 8f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		MarsBossSpawner marsBossSpawner = new MarsBossSpawner(base.Game);
		Wait(3f);
		StationarySpawner stationarySpawner = new StationarySpawner(base.Game, 560f, 0f, 0.8f);
		stationarySpawner.SetChances(0f, 0f, 0f, 1f);
		marsBossSpawner.LinkWith(stationarySpawner);
		eventList.AddEvent(stationarySpawner, halting: false);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning(3.7699115f);
		eventList.AddEvent(messageEvent, halting: false);
		Wait(3f);
		gameEvent = new BonusSpawner(base.Game, 0f, 0.2f, randomly: true);
		gameEvent.SetMars();
		marsBossSpawner.LinkWith(gameEvent);
		eventList.AddEvent(gameEvent, halting: false);
		eventList.AddEvent(marsBossSpawner, halting: true);
		eventList.AddHalt();
		Wait(6.5f);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		eventList.AddEvent(messageEvent);
		messageEvent.SetupAsWarning(0f);
		eventList.SetLastEventAsCheckPoint();
		waitEvent = Wait(4f);
		waitEvent.OnFinished += halt;
		SpiderBossEvent spiderBossEvent = new SpiderBossEvent(base.Game);
		eventList.AddEvent(spiderBossEvent, halting: false);
		Wait(8f);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		spiderBossEvent.LinkWith(gameEvent2);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.2f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		spiderBossEvent.LinkWith(gameEvent2);
		gameEvent = new BonusSpawner(base.Game, 0f, 0.08f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		spiderBossEvent.LinkWith(gameEvent);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.15f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		gameEvent2.DoNotScale();
		spiderBossEvent.LinkWith(gameEvent2);
		eventList.AddHalt();
		Wait(2f);
		waitEvent = Wait(5f);
		waitEvent.OnFinished += GoAlienBase;
		Wait(5f);
		StarMineSpawner gameEvent3 = new StarMineSpawner(base.Game, 5f, 0.7f);
		eventList.AddEvent(gameEvent3, halting: false);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		Wait(5f);
		SkullSpawner gameEvent4 = new SkullSpawner(base.Game, 0f, 0.1f, maze: false, bonusonly: true);
		eventList.AddEvent(gameEvent4, halting: false);
		gameEvent3 = new StarMineSpawner(base.Game, 0f, 0.75f);
		eventList.AddEvent(gameEvent3, halting: false);
		junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.SetBase();
		eventList.AddEvent(junkBossSpawner);
		eventList.AddHalt();
		junkBossSpawner.LinkWith(gameEvent4);
		junkBossSpawner.LinkWith(gameEvent3);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		Wait(3f);
		FakeBossSpawner gameEvent5 = new FakeBossSpawner(base.Game);
		eventList.AddEvent(gameEvent5);
		eventList.AddHalt();
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.MakeConditional(messageEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		eventList.SetLastEventAsCheckPoint();
		waitEvent = Wait(5f);
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		BrainBossSpawner brainBossSpawner = new BrainBossSpawner(base.Game, challenge: true);
		eventList.AddEvent(brainBossSpawner);
		eventList.MakeConditional(brainBossSpawner, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += Victory;
	}

	private void halt(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(-0.2f, 0f) / 16.666666f);
	}

	private void Victory(GameEvent sender)
	{
		Victory();
	}

	private void GoAlienBase(GameEvent sender)
	{
		base.SoundManager.PlayMusic(Songs.Level3);
		Background.SetAlienBase();
		Collection.Remove((GameComponent)(object)f);
		spawnType = PlayerSpawnType.South;
	}

	private void GoSpace(GameEvent sender)
	{
		if (backgroundchanged)
		{
			base.SoundManager.PlayMusic(Songs.Level1);
			Background.SetSpace();
			Collection.Remove((GameComponent)(object)f);
		}
		spawnType = PlayerSpawnType.South;
	}

	private WaitEvent Wait(float seconds)
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, seconds);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		return waitEvent;
	}

	private void GoMars(GameEvent sender)
	{
		base.SoundManager.PlayMusic(Songs.Level2);
		Background.SetMars();
		backgroundchanged = true;
		Collection.Add((GameComponent)(object)f);
		Collection.Purge<Ball>();
		spawnType = PlayerSpawnType.West;
	}
}
