using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class ClassicAliens : GameScene
{
	private TrialChamber trialChamber;

	public ClassicAliens(Game game)
		: base(game, Levels.ClassicAliens)
	{
		trialChamber = new TrialChamber(game);
		base.OnFinished += ClassicAliens_OnFinished;
	}

	public override void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
		base.OnComponentAdded(e);
		if (e.GameComponent is PlayerShip)
		{
			PlayerShip playerShip = (PlayerShip)(object)e.GameComponent;
			playerShip.AddRangePowerups(3);
		}
	}

	private void ClassicAliens_OnFinished(object sender, FinishedArgs args)
	{
		score.EnableCombos();
		Collection.Remove((GameComponent)(object)trialChamber);
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/andromeda");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/earth");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		score.DisableCombos();
		Background.SetSimpleSpaceClassic();
		base.SoundManager.PlayMusic(Songs.Classic);
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
		spawnType = PlayerSpawnType.North;
		Collection.Add((GameComponent)(object)trialChamber);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (RandomHelper.RandomFromAverage(0.3f, gameTime))
		{
			Background.Jump();
		}
	}

	private void showMessage(string message, float time, bool isCheckpoint)
	{
		TutorialMessageEvent gameEvent = new TutorialMessageEvent(((GameComponent)this).Game, time, message);
		eventList.AddEvent(gameEvent);
		eventList.AddHalt();
		if (isCheckpoint)
		{
			eventList.SetLastEventAsCheckPoint();
		}
		WaitEvent gameEvent2 = new WaitEvent(((GameComponent)this).Game, 0.6f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
	}

	protected override void PopulateEventList()
	{
		showMessage("Welcome to the Trial Simulation Chamber", 6.5f, isCheckpoint: false);
		showMessage("Activating Training Mode...", 6.5f, isCheckpoint: false);
		WaitEvent gameEvent = new WaitEvent(((GameComponent)this).Game, 0.01f);
		eventList.AddEvent(gameEvent);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		for (int i = 1; i < 21; i++)
		{
			ClassicSpawner classicSpawner = new ClassicSpawner(((GameComponent)this).Game, i);
			eventList.AddEvent(classicSpawner, halting: true);
			eventList.AddHalt();
			if (i % 4 == 0 && i < 20)
			{
				FakeBossSpawner fakeBossSpawner = new FakeBossSpawner(((GameComponent)this).Game);
				eventList.AddEvent(fakeBossSpawner);
				eventList.AddHalt();
				eventList.MakeConditional(fakeBossSpawner, Settings.DifficultyLevel.Very_Hard, Settings.DifficultyLevel.Inzane);
				fakeBossSpawner.ForceDifficulty((Settings.DifficultyLevel)(i / 4));
				MessageEvent messageEvent = new MessageEvent(((GameComponent)this).Game, "Wave Completed!", SoundManager.Texts.WaveCompleted);
				eventList.AddEvent(messageEvent);
				eventList.MakeConditional(messageEvent, Settings.DifficultyLevel.Very_Hard, Settings.DifficultyLevel.Inzane);
				messageEvent.OnFinished += spawnbonus;
				gameEvent = new WaitEvent(((GameComponent)this).Game, 2f);
				eventList.AddEvent(gameEvent);
				eventList.AddHalt();
				eventList.MakeConditional(gameEvent, Settings.DifficultyLevel.Very_Hard, Settings.DifficultyLevel.Inzane);
			}
			if (i > 10 && i <= 15)
			{
				eventList.MakeConditional(classicSpawner, Settings.DifficultyLevel.Medium, Settings.DifficultyLevel.Inzane);
			}
			if (i > 15 && i <= 20)
			{
				eventList.MakeConditional(classicSpawner, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
			}
			if (i == 20)
			{
				classicSpawner.OnFinished += classicSpawner_OnFinished;
			}
			if (i == 4)
			{
				classicSpawner.OnFinished += classicSpawner_OnFinished2;
			}
		}
		WaitEvent waitEvent = new WaitEvent(((GameComponent)this).Game, 4f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		MessageEvent messageEvent2 = new MessageEvent(((GameComponent)this).Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		messageEvent2.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent2, halting: true);
		eventList.AddHalt();
		eventList.MakeConditional(messageEvent2, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		ClassicBossSpawner classicBossSpawner = new ClassicBossSpawner(((GameComponent)this).Game);
		eventList.AddEvent(classicBossSpawner, halting: true);
		eventList.AddHalt();
		eventList.MakeConditional(classicBossSpawner, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		showMessage("Well Done", 6.5f, isCheckpoint: false);
		showMessage("Terminating Training...", 6.5f, isCheckpoint: false);
		waitEvent = new WaitEvent(((GameComponent)this).Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += win;
	}

	private void spawnbonus(GameEvent sender)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		Powerup powerup = Powerup.NewPowerup(Collection, ((GameComponent)this).Game);
		powerup.Setup(new Vector2(RandomHelper.RandomNextFloat(10f, 790f), 624f));
		switch (RandomHelper.Random.Next(2))
		{
		case 0:
			powerup.MakeType(Powerup.PowerupType.Option);
			break;
		case 1:
			powerup.MakeType(Powerup.PowerupType.FirePower);
			break;
		}
		Collection.Add((GameComponent)(object)powerup);
	}

	private void classicSpawner_OnFinished(GameEvent sender)
	{
		Background.QueueEarthSim();
	}

	private void classicSpawner_OnFinished2(GameEvent sender)
	{
		Background.QueueAndromeda();
	}

	private void win(GameEvent sender)
	{
		Victory();
	}
}
