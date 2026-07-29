using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Level1 : GameScene
{
	private const int InitialLives = 7;

	public Level1(Game game)
		: base(game, Levels.Level1)
	{
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)13);
		Background.SetSpace();
		base.SoundManager.PlayMusic(Songs.Level1);
		base.Initialize();
		Settings.GetInstance().UnlockDifficulty();
		ApplyDifficultyPolicy();
		// The intro demo owns the ship spawn (demo_OnFinished) -- but ?netscript replaces
		// the event list (no demo), so the generic spawn path must stay on there.
		if (!EvilAliensWeb.Compat.DebugFlags.NetScript)
		{
			base.spawnPlayerNormally = false;
		}
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

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/andromeda");
		contentManager.Load<Texture2D>("GFX/Sprites/large_asteroid");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/brainlargetransglow");
		contentManager.Load<Texture2D>("GFX/Sprites/earth");
		contentManager.Load<Texture2D>("GFX/Sprites/earth_small");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_idle");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_attract");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipA");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipB");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
	}

	protected override void PopulateEventList()
	{
		if (EvilAliensWeb.Compat.DebugFlags.NetScript)
		{
			PopulateNetScriptTest();
			return;
		}
		DevCommentEvent devCommentEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_1);
		eventList.AddEvent(devCommentEvent, halting: false);
		// The hero earth is queued at player pop-in (demo_OnFinished), NOT here at level start,
		// so it enters after the UFO intro -- see that handler + Background.DoodadStarSlowdownFactor.
		Lvl1StartDemoEvent lvl1StartDemoEvent = new Lvl1StartDemoEvent(base.Game);
		eventList.AddEvent(lvl1StartDemoEvent);
		eventList.AddHalt();
		lvl1StartDemoEvent.OnFinished += demo_OnFinished;
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += resetlives;
		UfoFormationSpawner ufoFormationSpawner = new UfoFormationSpawner(base.Game, 6);
		eventList.AddEvent(ufoFormationSpawner);
		eventList.AddHalt();
		ufoFormationSpawner = new UfoFormationSpawner(base.Game, 1);
		eventList.AddEvent(ufoFormationSpawner, halting: false);
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 20f, 0.1f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 20f, 1f, big: false);
		eventList.AddEvent(ufoSpawner);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 5f, 0.1f, big: false);
		eventList.AddEvent(ufoSpawner);
		eventList.AddHalt();
		MessageEvent messageEvent = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		ufoFormationSpawner = new UfoFormationSpawner(base.Game, 12);
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.2f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		ufoFormationSpawner.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 1.33f, big: false);
		ufoSpawner.SetupThreeDirectional();
		eventList.AddEvent(ufoSpawner, halting: false);
		ufoFormationSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(ufoFormationSpawner);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 5f, 1.5f, big: false);
		eventList.AddEvent(ufoSpawner);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		// Earth fly-by gate: hold the sideways asteroid-belt scroll (spawner_OnFinished
		// sets the (0.25,0.6) speed) until the hero earth has finished crossing and left
		// the screen. The fly-by is purely vertical + centred, so it never drifts off-axis
		// into the cropped strip's edges. If the earth is already gone this passes instantly.
		WaitForDoodadEvent earthFlybyGate = new WaitForDoodadEvent(base.Game, Background);
		eventList.AddEvent(earthFlybyGate);
		eventList.AddHalt();
		earthFlybyGate.OnFinished += spawner_OnFinished;
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(MyMath.VectorToAngle(new Vector2(-800f, -600f)));
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		AsteroidSpawner asteroidSpawner = new AsteroidSpawner(base.Game, 42f, 4f, startWithBig: true);
		asteroidSpawner.OnFinished += asteroids_OnFinished;
		eventList.AddEvent(asteroidSpawner, halting: true);
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 2.5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += waitevent_OnFinished;
		UnlockEvent unlockEvent = new UnlockEvent(base.Game, "Space Dodge!", Unlockables.Items.SpaceDodge, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		BrainSpawner brainSpawner = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		brainSpawner.OnFinished += message_OnFinished;
		brainSpawner = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 40f, 0.15f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 40f, 1.3f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		brainSpawner = new BrainSpawner(base.Game, 30f, 0.06f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 10f, 1.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		unlockEvent = new UnlockEvent(base.Game, "Braineroids!", Unlockables.Items.Braineroids, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 3f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		devCommentEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_2);
		eventList.AddEvent(devCommentEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 0.33f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		bonusSpawner = new BonusSpawner(base.Game, 24f, 0.1f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 24f, 3f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner = new UfoSpawner(base.Game, 24f, 0.5f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.OnFinished += message_OnFinished2;
		ufoSpawner = new UfoSpawner(base.Game, 6f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.SetLastEventAsCheckPoint();
		ufoSpawner = new UfoSpawner(base.Game, 6f, 0.4f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(base.Game);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		bossSpawner.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(bossSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		devCommentEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_3);
		eventList.AddEvent(devCommentEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 35f, 4f, big: false);
		ufoSpawner.SetupThreeDirectional();
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 35f, 0.125f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 35f, 0.66f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 10f, 2.25f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		devCommentEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_4);
		eventList.AddEvent(devCommentEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += waitevent_OnFinished2;
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.5f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.OnFinished += invuln;
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
		unlockEvent = new UnlockEvent(base.Game, "Infinite Lives", Unlockables.Items.InfiniteLives, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		unlockEvent = new UnlockEvent(base.Game, "Next Mission!", Unlockables.Items.Level2, AnimatedMessage.UnlockType.level, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		unlockEvent = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.InsaneDifficulty, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(unlockEvent, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 1f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += jbspawner_OnFinished;
	}

	private void invuln(GameEvent sender)
	{
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.TemporaryInvulnerability(600);
		}
	}

	private void message_OnFinished(GameEvent sender)
	{
		Background.QueueAndromeda();
	}

	private void message_OnFinished2(GameEvent sender)
	{
		Background.QueueSmallEarth();
	}

	private void waitevent_OnFinished(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(0f, 0.2f) / 16.666666f);
	}

	private void waitevent_OnFinished2(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(0f, 7.6f) / 16.666666f);
		Collection.ClearCache();
	}

	private void spawner_OnFinished(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(0.25f, 0.6f) / 16.666666f);
		// Card "asteroid field animation": as the sideways belt starts scrolling, slow the near
		// stars so the fastest star reads clearly slower than the slowest asteroid (the same
		// depth cue as the earth fly-by). Disengaged when the belt wave finishes (asteroids_OnFinished).
		Background.EngageBeltSlowdown();
	}

	private void asteroids_OnFinished(GameEvent sender)
	{
		// The belt wave has run its course -- ramp the near stars back up to full speed.
		Background.DisengageBeltSlowdown();
	}

	private void demo_OnFinished(GameEvent sender)
	{
		SpawnAllPlayers(invulnerable: true);
		base.spawnPlayerNormally = true;
		// Card "earth animation improvements": the hero earth enters AS the player pops in
		// (not at level start during the UFO intro). Queuing it here syncs its entrance with
		// the rapid starfield slow-down (Background.DoodadStarSlowdownFactor) so the near-frozen
		// stars sell the earth as the fast, nearest object while the player takes control.
		Background.QueueEarth();
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.FirstAct);
		Victory();
	}
}
