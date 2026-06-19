using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens;

internal class TeamChallenge : GameScene
{
	private ShipConnector connector;

	public TeamChallenge(Game game)
		: base(game, Levels.TeamChallenge)
	{
		base.OnReset += TeamChallenge_OnReset;
	}

	private void TeamChallenge_OnReset()
	{
		connector = ShipConnector.NewAlien(Collection, ((GameComponent)this).Game);
		connector.Setup(oracle.GetShips()[0], oracle.GetShips()[1]);
		Collection.Add((GameComponent)(object)connector);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == connector)
		{
			connector = null;
		}
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)3);
		Background.SetSpace();
		base.SoundManager.PlayMusic(Songs.Classic);
		base.Initialize();
		Settings.GetInstance().LockDifficulty(Settings.DifficultyLevel.Medium);
		oracle.ResetPlayers();
		oracle.AddPlayer(ControlDevice.Keyboard);
		oracle.AddPlayer(ControlDevice.PadOne);
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		base.UpdateNormal(gameTime);
		if (oracle.GetShips().Count >= 2)
		{
			return;
		}
		Collection.Remove((GameComponent)(object)connector);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.Asplode();
		}
		LoseLife();
	}

	protected override void PopulateEventList()
	{
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		BonusSpawner gameEvent = new BonusSpawner(((GameComponent)this).Game, 20f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		UfoSpawner gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 20f, 3f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 5f, 0.1f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 10f, 0.2f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		MessageEvent gameEvent3 = new MessageEvent(((GameComponent)this).Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 10f, 4.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 30f, 0.2f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 30f, 5.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 5f, 1.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		gameEvent3.SetupAsWarning(MyMath.VectorToAngle(new Vector2(-800f, -600f)));
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		AsteroidSpawner gameEvent4 = new AsteroidSpawner(((GameComponent)this).Game, 42f, 4f, startWithBig: true);
		eventList.AddEvent(gameEvent4, halting: true);
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 10f, 5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		WaitEvent gameEvent5 = new WaitEvent(((GameComponent)this).Game, 2.5f);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game);
		eventList.AddEvent(gameEvent3, halting: false);
		BrainSpawner gameEvent6 = new BrainSpawner(((GameComponent)this).Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(gameEvent6, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent6 = new BrainSpawner(((GameComponent)this).Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(gameEvent6, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 40f, 0.15f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 40f, 2.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		gameEvent6 = new BrainSpawner(((GameComponent)this).Game, 30f, 0.075f, wrapping: true);
		eventList.AddEvent(gameEvent6, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 10f, 1.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 10f, 3f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 10f, 0.33f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 24f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 24f, 3f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 24f, 0.5f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 6f, 2f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 6f, 0.4f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		gameEvent3.SetupAsWarning(4.712389f);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(((GameComponent)this).Game);
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		bossSpawner.LinkWith(gameEvent);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 0f, 2f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 0f, 0.33f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		eventList.AddEvent(bossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 35f, 4f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 35f, 0.125f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 35f, 0.66f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 10f, 2.25f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent5 = new WaitEvent(((GameComponent)this).Game, 5f);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(((GameComponent)this).Game, 10f, 0.5f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent3 = new MessageEvent(((GameComponent)this).Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		gameEvent3.SetupAsWarning(4.712389f);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(((GameComponent)this).Game);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 0f, 0.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		gameEvent2 = new UfoSpawner(((GameComponent)this).Game, 0f, 0.1f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(((GameComponent)this).Game, "Great!", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent3, halting: false);
		SkullSpawner gameEvent7 = new SkullSpawner(((GameComponent)this).Game, 60f, 1.2f, maze: false, bonusonly: false);
		eventList.AddEvent(gameEvent7, halting: false);
		eventList.SetLastEventAsCheckPoint();
		BattleSkullEvent battleSkullEvent = new BattleSkullEvent(((GameComponent)this).Game, 60f, 0.2f);
		eventList.AddEvent(battleSkullEvent, halting: true);
		eventList.AddHalt();
		battleSkullEvent.OnFinished += jbspawner_OnFinished;
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		Victory();
	}
}
