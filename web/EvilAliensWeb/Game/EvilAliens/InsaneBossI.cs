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
		contentManager.Load<Texture2D>("GFX/Sprites/eye_idle");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_attract");
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
		contentManager.Load<Texture2D>("GFX/Sprites/brainbosshd");
		contentManager.Load<Texture2D>("GFX/Sprites/brainbossaura");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
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
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		bossSpawner.LinkWith(bonusSpawner);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(bossSpawner);
		eventList.AddHalt();
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.12f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		junkBossSpawner.LinkWith(ufoSpawner);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.046f, randomly: true);
		eventList.AddEvent(bonusSpawner, halting: false);
		junkBossSpawner.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.053f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		junkBossSpawner.LinkWith(ufoSpawner);
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
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.2f, randomly: true);
		bonusSpawner.SetMars();
		marsBossSpawner.LinkWith(bonusSpawner);
		eventList.AddEvent(bonusSpawner, halting: false);
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
		Wait(2f);
		waitEvent = Wait(5f);
		waitEvent.OnFinished += GoAlienBase;
		Wait(5f);
		StarMineSpawner starMineSpawner = new StarMineSpawner(base.Game, 5f, 0.7f);
		eventList.AddEvent(starMineSpawner, halting: false);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		Wait(5f);
		SkullSpawner skullSpawner = new SkullSpawner(base.Game, 0f, 0.1f, maze: false, bonusonly: true);
		eventList.AddEvent(skullSpawner, halting: false);
		starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.75f);
		eventList.AddEvent(starMineSpawner, halting: false);
		junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.SetBase();
		eventList.AddEvent(junkBossSpawner);
		eventList.AddHalt();
		junkBossSpawner.LinkWith(skullSpawner);
		junkBossSpawner.LinkWith(starMineSpawner);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		Wait(3f);
		FakeBossSpawner fakeBossSpawner = new FakeBossSpawner(base.Game);
		eventList.AddEvent(fakeBossSpawner);
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

	// Online co-op (card ca4fd94f): this is the one level whose script swaps the whole backdrop
	// mid-run, so a join peer -- whose event list never runs -- gets the swap off the wire. The
	// base call switches the backdrop; this mirrors the rest of the matching Go* handler.
	//
	// Two things the handlers do are deliberately NOT mirrored. PlayMusic already replicates as
	// its own EvMusic beat, so re-firing it here would fight that. Collection.Purge<Ball> is
	// host-authoritative: the host's purge broadcasts an EvDeath per removal and the client's
	// puppets die from those -- purging locally would strand their ids.
	//
	// spawnType is not mirrored either, so a client respawning inside the Mars section enters
	// from the south rather than the west. Local and cosmetic (it only picks the entry point in
	// SpawnPlayer/SpawnAllPlayers, and the ship's real position replicates), but it is a known
	// difference rather than a fix.
	internal override void NetApplySceneChange(EvilAliensWeb.Compat.Net.NetBackgroundOp op)
	{
		base.NetApplySceneChange(op);
		if (op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneMars)
		{
			Collection.Add((GameComponent)(object)f);
		}
		else
		{
			Collection.Remove((GameComponent)(object)f);
		}
	}

	// The Floor mirror above, as state -- so eaNetBgTest actually covers it and a two-window
	// eaNetBg() diff can see it. It is read from the live collection rather than a bool this class
	// keeps, because the thing worth checking is that the FLOOR is there, not that we remember
	// adding it (this scene owns the only Floor in play).
	protected override string NetSceneChangeState()
	{
		// Membership alone is not enough, for the reason ComponentBin.TryAdd spells out: a Remove
		// is QUEUED to the death list and the component is still in the collection until the next
		// flush, so "is the floor there" has to mean live NEXT tick. Without this the self-test's
		// own wipe reads as not having happened and the floor leg passes vacuously.
		bool live = Collection.ContainsType<Floor>() && !Collection.DEBUGdeathlistcontains((GameComponent)(object)f);
		return "floor=" + (live ? "1" : "0");
	}

	internal override void NetSceneChangeTestWipe()
	{
		// A fresh joiner ran its own Initialize, which does not add the floor -- only GoMars does.
		Collection.Remove((GameComponent)(object)f);
	}

	private void halt(GameEvent sender)
	{
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
