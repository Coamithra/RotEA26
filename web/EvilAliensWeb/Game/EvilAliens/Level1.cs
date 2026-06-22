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
		if (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Easy)
		{
			Settings.GetInstance().DirectRespawn = true;
			Settings.GetInstance().AdaptiveDifficulty = true;
			Settings.GetInstance().DifficultyModifier = Settings.GetInstance().GetDifficultyValue(Settings.DifficultyLevel.Medium);
		}
		base.spawnPlayerNormally = false;
		if ((Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Hard) | (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Inzane) | (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Very_Hard))
		{
			score.Lives = 7;
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
		//IL_02cf: Unknown result type (might be due to invalid IL or missing references)
		DevCommentEvent gameEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_1);
		eventList.AddEvent(gameEvent, halting: false);
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(waitEvent, halting: false);
		waitEvent.OnFinished += waitevent_OnFinished3;
		Lvl1StartDemoEvent lvl1StartDemoEvent = new Lvl1StartDemoEvent(base.Game);
		eventList.AddEvent(lvl1StartDemoEvent);
		eventList.AddHalt();
		lvl1StartDemoEvent.OnFinished += demo_OnFinished;
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += resetlives;
		UfoFormationSpawner gameEvent2 = new UfoFormationSpawner(base.Game, 6);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent2 = new UfoFormationSpawner(base.Game, 1);
		eventList.AddEvent(gameEvent2, halting: false);
		BonusSpawner gameEvent3 = new BonusSpawner(base.Game, 20f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		UfoSpawner gameEvent4 = new UfoSpawner(base.Game, 20f, 1f, big: false);
		eventList.AddEvent(gameEvent4);
		eventList.AddHalt();
		gameEvent4 = new UfoSpawner(base.Game, 5f, 0.1f, big: false);
		eventList.AddEvent(gameEvent4);
		eventList.AddHalt();
		MessageEvent gameEvent5 = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(gameEvent5, halting: false);
		gameEvent2 = new UfoFormationSpawner(base.Game, 12);
		gameEvent3 = new BonusSpawner(base.Game, 10f, 0.2f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		eventList.SetLastEventAsCheckPoint();
		gameEvent2.LinkWith(gameEvent3);
		gameEvent4 = new UfoSpawner(base.Game, 0f, 1.33f, big: false);
		gameEvent4.SetupThreeDirectional();
		eventList.AddEvent(gameEvent4, halting: false);
		gameEvent2.LinkWith(gameEvent4);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent5, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 5f, 1.5f, big: false);
		eventList.AddEvent(gameEvent4);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent4.OnFinished += spawner_OnFinished;
		gameEvent5 = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		gameEvent5.SetupAsWarning(MyMath.VectorToAngle(new Vector2(-800f, -600f)));
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		AsteroidSpawner gameEvent6 = new AsteroidSpawner(base.Game, 42f, 4f, startWithBig: true);
		eventList.AddEvent(gameEvent6, halting: true);
		gameEvent3 = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 10f, 5f, big: false);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 2.5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += waitevent_OnFinished;
		UnlockEvent gameEvent7 = new UnlockEvent(base.Game, "Space Dodge!", Unlockables.Items.SpaceDodge, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(gameEvent7, halting: true);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent5, halting: false);
		BrainSpawner brainSpawner = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		brainSpawner.OnFinished += message_OnFinished;
		brainSpawner = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent3 = new BonusSpawner(base.Game, 40f, 0.15f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 40f, 1.3f, big: false);
		eventList.AddEvent(gameEvent4, halting: true);
		brainSpawner = new BrainSpawner(base.Game, 30f, 0.06f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		gameEvent4 = new UfoSpawner(base.Game, 10f, 1.5f, big: false);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		gameEvent7 = new UnlockEvent(base.Game, "Braineroids!", Unlockables.Items.Braineroids, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(gameEvent7, halting: true);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent5, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 10f, 3f, big: false);
		eventList.AddEvent(gameEvent4, halting: false);
		eventList.SetLastEventAsCheckPoint();
		gameEvent3 = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_2);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 10f, 0.33f, big: true);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		gameEvent3 = new BonusSpawner(base.Game, 24f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 24f, 3f, big: false);
		eventList.AddEvent(gameEvent4, halting: true);
		gameEvent4 = new UfoSpawner(base.Game, 24f, 0.5f, big: true);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent5, halting: false);
		gameEvent5.OnFinished += message_OnFinished2;
		gameEvent4 = new UfoSpawner(base.Game, 6f, 2f, big: false);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent4 = new UfoSpawner(base.Game, 6f, 0.4f, big: true);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		gameEvent5.SetupAsWarning(4.712389f);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(base.Game);
		gameEvent3 = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		bossSpawner.LinkWith(gameEvent3);
		gameEvent4 = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(gameEvent4, halting: false);
		bossSpawner.LinkWith(gameEvent4);
		gameEvent4 = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(gameEvent4, halting: false);
		bossSpawner.LinkWith(gameEvent4);
		eventList.AddEvent(bossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent5, halting: false);
		gameEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_3);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 35f, 4f, big: false);
		gameEvent4.SetupThreeDirectional();
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent3 = new BonusSpawner(base.Game, 35f, 0.125f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent4 = new UfoSpawner(base.Game, 35f, 0.66f, big: true);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		gameEvent4 = new UfoSpawner(base.Game, 10f, 2.25f, big: false);
		eventList.AddEvent(gameEvent4, halting: true);
		eventList.AddHalt();
		gameEvent5 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent5, halting: false);
		gameEvent = new DevCommentEvent(base.Game, DevCommentEvent.CommentVersion.level1_4);
		eventList.AddEvent(gameEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += waitevent_OnFinished2;
		gameEvent3 = new BonusSpawner(base.Game, 10f, 0.5f, randomly: false);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent5 = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		gameEvent5.SetupAsWarning(4.712389f);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.OnFinished += invuln;
		gameEvent4 = new UfoSpawner(base.Game, 0f, 0.12f, big: false);
		eventList.AddEvent(gameEvent4, halting: false);
		junkBossSpawner.LinkWith(gameEvent4);
		gameEvent3 = new BonusSpawner(base.Game, 0f, 0.046f, randomly: true);
		eventList.AddEvent(gameEvent3, halting: false);
		junkBossSpawner.LinkWith(gameEvent3);
		gameEvent4 = new UfoSpawner(base.Game, 0f, 0.053f, big: true);
		eventList.AddEvent(gameEvent4, halting: false);
		junkBossSpawner.LinkWith(gameEvent4);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent7 = new UnlockEvent(base.Game, "Infinite Lives", Unlockables.Items.InfiniteLives, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(gameEvent7, halting: true);
		eventList.AddHalt();
		gameEvent7 = new UnlockEvent(base.Game, "Next Mission!", Unlockables.Items.Level2, AnimatedMessage.UnlockType.level, level);
		eventList.AddEvent(gameEvent7, halting: true);
		eventList.AddHalt();
		gameEvent7 = new UnlockEvent(base.Game, "Insane Difficulty", Unlockables.Items.InsaneDifficulty, AnimatedMessage.UnlockType.difficulty, level);
		eventList.AddEvent(gameEvent7, halting: true);
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
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 0.2f) / 16.666666f);
	}

	private void waitevent_OnFinished2(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 7.6f) / 16.666666f);
		Collection.ClearCache();
	}

	private void waitevent_OnFinished3(GameEvent sender)
	{
		Background.QueueEarth();
	}

	private void spawner_OnFinished(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0.25f, 0.6f) / 16.666666f);
	}

	private void demo_OnFinished(GameEvent sender)
	{
		SpawnAllPlayers(invulnerable: true);
		base.spawnPlayerNormally = true;
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.FirstAct);
		Victory();
	}
}
