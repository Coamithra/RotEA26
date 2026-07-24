using System;
using EvilAliensWeb.Compat;
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
		// Tutorial is always the clean instrumental -- it LockDifficulty(Very_Hard)s
		// for gameplay tuning, so it can't key on difficulty; force clean.
		base.SoundManager.PlayMusic(Songs.ClassicClean);
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
		contentManager.Load<Texture2D>("GFX/Sprites/eye_idle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
	}

	// True when the tutorial player is on an actual gamepad. Player 0 is the "starter" that
	// launched this level (Game1.MenuFinished -> oracle.AddPlayer(starter)): a real gamepad
	// => PadOne..PadFour, while keyboard/mouse select AND mouse-click AND the on-screen touch
	// overlay all resolve to ControlDevice.Keyboard. So joystick prompts show only for an
	// actual gamepad; everything else (keyboard, mouse, touch) gets mouse & keyboard phrasing.
	// DeviceIsPlaying is the same non-throwing query GameScene uses everywhere for keyboard
	// detection. NOTE: this MUST be evaluated at display time (via messageByDevice's resolver),
	// not while PopulateEventList builds the event list — the list is built in the GameScene
	// constructor at boot, long before any player is added, so a build-time read is always false.
	private bool UsingGamepad => oracle.DeviceIsPlaying(ControlDevice.PadOne) || oracle.DeviceIsPlaying(ControlDevice.PadTwo) || oracle.DeviceIsPlaying(ControlDevice.PadThree) || oracle.DeviceIsPlaying(ControlDevice.PadFour);

	// Pacing philosophy (card 4aab0629): the old list was strictly SERIAL — every line of
	// text halted the queue for its full 6.5s before anything moved, so each powerup lesson
	// was ~16-20s of mostly reading. Now text and action land on the SAME beat (non-halting
	// messages over halting spawners), the fixed 9.5s post-wave waits are replaced by
	// advance-on-pickup (WaitForPickupEvent, timeout fallback), and the "channel surf"
	// holo-sim bursts (Compat/HoloSim) punctuate the simulation booting up / shutting down.
	// One layout rule: two TutorialMessages draw at the same spot, so overlap is only ever
	// text-with-action, never text-with-text — a lesson's message is LinkWith'd to its
	// pickup gate so a fast pickup clears the banner before the next lesson's text.
	protected override void PopulateEventList()
	{
		if (EvilAliensWeb.Compat.DebugFlags.TutorialTraining)
		{
			// DEBUG (?tutorialtraining): skip the whole tutorial and drop straight into the final
			// power-up training beat (eye boss + PowerUpTrainingEvent), so the R-banner timing bug
			// can be reproduced in seconds instead of playing through every lesson.
			PopulatePowerUpTrainingOnly();
			return;
		}
		wait(2f);
		message("Welcome to the Trial Simulation Chamber", 4.5f);
		burst(1f);
		message("Activating Tutorial Mode...", 3f);
		messageByDevice("Use Left Stick to Move", "Use WASD or Arrow Keys to Move", isCheckpoint: true);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		messageEvent.OnFinished += messageEvent_OnFinished;
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		wait(4f);
		// Fire prompt + the practice UFO on the same beat; the prompt clears the moment
		// the kill lands so a fast kill can't leave it overlapping the next message.
		TutorialMessageEvent firePrompt = messageByDevice("Use Right Stick to Fire", "Aim with the Mouse, hold Left Click to Fire", isCheckpoint: true, halting: false);
		SingleEnemySpawner gameEvent = new SingleEnemySpawner(base.Game);
		gameEvent.LinkWith(firePrompt);
		eventList.AddEvent(gameEvent);
		eventList.AddHalt();
		wait(1f);
		powerupLesson("Pick up B's for a bomb", Powerup.PowerupType.Blast, 10f);
		messageByDevice("Press Left or Right Trigger to activate a\nbomb (you can carry 3)", "Right Click to activate a bomb (you can\ncarry 3)");
		powerupLesson("Pick up O's for a protective shield", Powerup.PowerupType.Option, 9f);
		powerupLesson("Pick up R's to increase range", Powerup.PowerupType.Range, 9f);
		powerupLesson("Pick up F's to increase rate of fire", Powerup.PowerupType.FirePower, 9f);
		WaitEvent waitEvent = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += displayEnhancement;
		eventList.SetLastEventAsCheckPoint();
		message("Your last Enhancement is stored under your\nscore", 5.5f);
		message("The number next to it displays its current\nPower Level", 6f);
		wait(1.5f);
		waitEvent = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += displayPowerbar;
		message("Power up your Enhancement by filling the\nPower Bar", 5.5f);
		message("The Power Bar can be filled by shooting\nenemies", 5.5f);
		message("High combos fill the Power Bar faster", 5.5f);
		wait(1.5f);
		waitEvent = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += spawnPunchingBag;
		PowerUpTrainingEvent gameEvent2 = new PowerUpTrainingEvent(base.Game);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		wait(3f);
		waitEvent = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(waitEvent);
		waitEvent.OnFinished += killboss;
		wait(1.5f);
		message("Well Done", 4f);
		burst(1f);
		message("Terminating Tutorial...", 3f);
		UnlockEvent unlockEvent = new UnlockEvent(base.Game, "Evil Aliens Classic", Unlockables.Items.ClassicAliens, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent, halting: true);
		unlockEvent.OnFinished += end;
		eventList.AddHalt();
	}

	// DEBUG fast-boot (?tutorialtraining): just the tutorial's FINAL beat. Mirrors the tail of
	// PopulateEventList — reveal the enhancement + power-bar HUD, spawn the eye "punching bag"
	// boss (spawnPunchingBag, which also enables combos) and the PowerUpTrainingEvent (every
	// powerup streams in, a banner explains each powered-up effect), then killboss -> Well Done
	// -> unlock -> Victory. The reading-only interstitial messages are dropped so the boss +
	// training (where the R-banner bug lives) is reached in a couple of seconds.
	private void PopulatePowerUpTrainingOnly()
	{
		wait(0.5f);
		WaitEvent hud = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(hud);
		hud.OnFinished += displayEnhancement;
		hud = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(hud);
		hud.OnFinished += displayPowerbar;
		message("Power up your Enhancements by shooting\nthe target", 3f);
		WaitEvent bag = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(bag);
		bag.OnFinished += spawnPunchingBag;
		PowerUpTrainingEvent training = new PowerUpTrainingEvent(base.Game);
		eventList.AddEvent(training);
		eventList.AddHalt();
		wait(2f);
		WaitEvent kill = new WaitEvent(base.Game, 0.01f);
		eventList.AddEvent(kill);
		kill.OnFinished += killboss;
		wait(1f);
		message("Well Done", 3f);
		burst(1f);
		message("Terminating Tutorial...", 3f);
		UnlockEvent unlockEvent = new UnlockEvent(base.Game, "Evil Aliens Classic", Unlockables.Items.ClassicAliens, AnimatedMessage.UnlockType.challenge, level);
		eventList.AddEvent(unlockEvent, halting: true);
		unlockEvent.OnFinished += end;
		eventList.AddHalt();
	}

	// One powerup lesson beat: the message types out WHILE its powerup-carrying bonus UFOs
	// stream in (both non-halting), and the queue waits only on the pickup gate — the
	// tutorial moves exactly as fast as the player does, with the timeout as the ceiling.
	// The gate terminates the message + the wave with it (LinkWith), so a fast pickup
	// clears the lesson cleanly; the short trailing wait is a breather between lessons.
	// LessonMinShowSeconds keeps the banner up long enough to finish typing + be read even
	// when the player grabs the powerup instantly (the "text cut off after a few letters"
	// bug on the later lessons, where the player already knows to grab on sight).
	private const float LessonMinShowSeconds = 2.75f;

	private void powerupLesson(string text, Powerup.PowerupType type, float timeoutSeconds)
	{
		TutorialMessageEvent msg = message(text, 5f, isCheckpoint: true, halting: false);
		BonusUFOSpawner wave = new BonusUFOSpawner(base.Game, 4f, 1.5f, type);
		eventList.AddEvent(wave, halting: false);
		WaitForPickupEvent grab = new WaitForPickupEvent(base.Game, type, timeoutSeconds, LessonMinShowSeconds);
		grab.LinkWith(msg);
		grab.LinkWith(wave);
		eventList.AddEvent(grab);
		eventList.AddHalt();
		wait(1.2f);
	}

	// Fire a holo-sim "channel surf" glitch spike (Compat/HoloSim) when the queue reaches
	// this point. Non-halting, so it lands on the same beat as whatever follows it.
	private void burst(float strength)
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.01f);
		waitEvent.OnFinished += delegate
		{
			HoloSim.FireBurst(strength);
		};
		eventList.AddEvent(waitEvent, halting: false);
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

	private void messageEvent_OnFinished(GameEvent sender)
	{
		Asteroid asteroid = Asteroid.NewAsteroid(Collection, base.Game);
		asteroid.Setup(new Vector2(400f, -80f), (float)Math.PI / 2f, 0.4f, reallyBig: false, randomSpeedOffset: false);
		Collection.Add((GameComponent)(object)asteroid);
	}

	private TutorialMessageEvent message(string message, bool isCheckpoint)
	{
		return this.message(message, 6.5f, isCheckpoint);
	}

	private TutorialMessageEvent message(string message)
	{
		return this.message(message, 6.5f);
	}

	private TutorialMessageEvent message(string message, float time)
	{
		return this.message(message, time, isCheckpoint: false);
	}

	// Core message add. halting (the default) shows the text alone: the queue waits out its
	// lifetime plus a 0.6s beat. halting:false lets the following event(s) run on the SAME
	// beat (text over action) — the caller owns sequencing and should LinkWith the message
	// to whatever ends the beat so banners can never stack (they share one screen position).
	private TutorialMessageEvent message(string message, float time, bool isCheckpoint, bool halting = true)
	{
		TutorialMessageEvent gameEvent = new TutorialMessageEvent(base.Game, time, message);
		eventList.AddEvent(gameEvent, halting);
		if (halting)
		{
			eventList.AddHalt();
		}
		if (isCheckpoint)
		{
			eventList.SetLastEventAsCheckPoint();
		}
		if (halting)
		{
			wait(0.6f);
		}
		return gameEvent;
	}

	// Device-dependent prompt: picks gamepadText vs mkText when the message is actually
	// shown (see UsingGamepad — must be resolved at display time, not list-build time).
	private TutorialMessageEvent messageByDevice(string gamepadText, string mkText, bool isCheckpoint = false, bool halting = true)
	{
		TutorialMessageEvent gameEvent = new TutorialMessageEvent(base.Game, 6.5f, () => UsingGamepad ? gamepadText : mkText);
		eventList.AddEvent(gameEvent, halting);
		if (halting)
		{
			eventList.AddHalt();
		}
		if (isCheckpoint)
		{
			eventList.SetLastEventAsCheckPoint();
		}
		if (halting)
		{
			wait(0.6f);
		}
		return gameEvent;
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
		// Keep the fullscreen holo-sim filter alive (it fades out on its own the moment
		// the tutorial stops poking — any exit path included; see Compat/HoloSim).
		HoloSim.Poke();
		if (RandomHelper.RandomFromAverage(HoloSim.HiccupRate, gameTime))
		{
			Background.Jump();
			// The background's glitch-slip and a small screen-glitch spike land together.
			HoloSim.FireBurst(0.35f);
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
