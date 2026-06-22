using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class TutorialLevel : GameScene
{
	private const int InitialLives = 7;

	public TutorialLevel(Game game)
		: base(game, Levels.Tutorial)
	{
		base.OnFinished += TutorialLevel_OnFinished;
	}

	private void TutorialLevel_OnFinished(object sender, FinishedArgs args)
	{
		score.EnableCombos();
		score.IsTutorial = false;
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)21);
		Background.SetSimpleSpace();
		base.SoundManager.PlayMusic(Songs.Classic);
		base.Initialize();
		Settings.GetInstance().LockDifficulty(Settings.DifficultyLevel.Very_Hard);
		base.spawnPlayerNormally = true;
		score.DisableCombos();
		score.IsTutorial = true;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/large_asteroid");
		contentManager.Load<Texture2D>("GFX/Sprites/eye");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
	}

	protected override void PopulateEventList()
	{
		wait(4f);
		message("Welcome to the Trial Simulation Chamber");
		message("Activating Tutorial Mode...");
		wait(1f);
		message("Use Left Stick to Move", isCheckpoint: true);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		messageEvent.OnFinished += messageEvent_OnFinished;
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		wait(6f);
		message("Use Right Stick to Fire", isCheckpoint: true);
		SingleEnemySpawner gameEvent = new SingleEnemySpawner(base.Game);
		eventList.AddEvent(gameEvent);
		eventList.AddHalt();
		wait(1f);
		message("Enhancements:", isCheckpoint: true);
		message("Pick up B's for a bomb");
		message("Press Left or Right Trigger to activate a bomb");
		message("You can carry up to 3 bombs");
		bonusWave(Powerup.PowerupType.Blast);
		wait(9.5f);
		message("Pick up O's for a protective shield");
		bonusWave(Powerup.PowerupType.Option);
		wait(9.5f);
		message("Pick up R's to increase range");
		bonusWave(Powerup.PowerupType.Range);
		wait(9.5f);
		message("Pick up F's to increase rate of fire");
		bonusWave(Powerup.PowerupType.FirePower);
		wait(9.5f);
		WaitEvent waitEvent = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += displayEnhancement;
		eventList.SetLastEventAsCheckPoint();
		message("Your last Enhancement is stored under your score", 7f);
		message("The number next to it displays its current Power Level", 8.5f);
		wait(3f);
		waitEvent = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += displayPowerbar;
		message("Power up your Enhancement by filling the Power Bar");
		message("The Power Bar can be filled by shooting enemies");
		message("High combos fill the Power Bar faster");
		wait(3f);
		waitEvent = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += spawnPunchingBag;
		PowerUpTrainingEvent gameEvent2 = new PowerUpTrainingEvent(base.Game);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		wait(6f);
		waitEvent = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += killboss;
		wait(3f);
		message("Well Done");
		message("Terminating Tutorial...");
		UnlockEvent unlockEvent = new UnlockEvent(base.Game, "Evil Aliens Classic", Unlockables.Items.ClassicAliens, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent, halting: true);
		unlockEvent.OnFinished += end;
		eventList.AddHalt();
	}

	private void killboss(GameEvent sender)
	{
		foreach (AlienDrawableGameComponent baddy in oracle.GetBaddies())
		{
			if (baddy is PunchingBag)
			{
				((PunchingBag)baddy).Terminate();
			}
		}
	}

	private void end(GameEvent sender)
	{
		Victory();
	}

	private void displayPowerbar(GameEvent sender)
	{
		score.Tutorial_Show(ScoreVisualiser.ScorePart.Powerbar);
	}

	private void displayEnhancement(GameEvent sender)
	{
		score.Tutorial_Show(ScoreVisualiser.ScorePart.Enhancement);
	}

	private void spawnPunchingBag(GameEvent sender)
	{
		PunchingBag component = PunchingBag.NewPunchingBag(Collection, base.Game);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.RemovePowerup();
		}
		Collection.Add((GameComponent)(object)component);
		score.EnableCombos();
	}

	private void bonusWave(Powerup.PowerupType powerup)
	{
		BonusUFOSpawner gameEvent = new BonusUFOSpawner(base.Game, 4f, 1.5f, powerup);
		eventList.AddEvent(gameEvent);
		eventList.AddHalt();
	}

	private void messageEvent_OnFinished(GameEvent sender)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		Asteroid asteroid = Asteroid.NewAsteroid(Collection, base.Game);
		asteroid.Setup(new Vector2(400f, -80f), (float)Math.PI / 2f, 0.4f, reallyBig: false, randomSpeedOffset: false);
		Collection.Add((GameComponent)(object)asteroid);
	}

	private void message(string message, bool isCheckpoint)
	{
		this.message(message, 6.5f, isCheckpoint);
	}

	private void message(string message)
	{
		this.message(message, 6.5f);
	}

	private void message(string message, float time)
	{
		this.message(message, time, isCheckpoint: false);
	}

	private void message(string message, float time, bool isCheckpoint)
	{
		TutorialMessageEvent gameEvent = new TutorialMessageEvent(base.Game, time, message);
		eventList.AddEvent(gameEvent);
		eventList.AddHalt();
		if (isCheckpoint)
		{
			eventList.SetLastEventAsCheckPoint();
		}
		wait(0.6f);
	}

	private void wait(float time)
	{
		WaitEvent gameEvent = new WaitEvent(base.Game, time);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
	}

	private void waitevent_OnFinished(GameEvent sender)
	{
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (RandomHelper.RandomFromAverage(0.2f, gameTime))
		{
			Background.Jump();
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.SetTutorial();
		}
	}

	private void invuln(GameEvent sender)
	{
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.TemporaryInvulnerability(600);
		}
	}
}
