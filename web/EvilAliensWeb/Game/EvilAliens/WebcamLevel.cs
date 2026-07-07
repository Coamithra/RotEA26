using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// "I Made This!" (Levels.WebcamAliens) — a remake of the 2004 webcam game the
// splash-screen meme comes from. The PLAYER'S OWN CAMERA IMAGE is the ship:
// the browser side (wwwroot/webcam.js) shows a camera-setup dialog, removes the
// room background, and overlays the mirrored person on the game's starfield;
// per frame it pushes a 40x30 person-mask grid that this scene hit-tests
// against (Compat/WebcamInterop.cs — the full JS/C# split is documented there).
//
// Rules (faithful to the original, per the splash screenshot: a row of hearts
// top-left, a kill counter top-right, saucers on a starfield):
//   * Saucers fly in from the screen edges and wander. TOUCH one with your
//     body to asplode it.
//   * Left alone, a saucer starts blinking faster and faster, then fires one
//     big slow plasma orb at you. If it hits your image you lose a heart;
//     0 hearts = game over.
//   * Asplode enough saucers to win.
//
// It runs through the challenge difficulty menu like the other challenges, and
// the per-difficulty feel (hearts / kills-to-win / saucer cap + speed / plasma
// speed) comes from the Tunings table below; harder tiers earn the lyrics music
// (SoundManager.ClassicForDifficulty()). Live-tune with the ?wc* debug flags.
//
// There is NO PlayerShip in this level (spawnPlayerNormally = false) — the
// pause menu, score HUD, victory/defeat flows are all inherited from GameScene,
// but lives are the hearts here, not the stock lives strip (score.Lives stays 0
// so LoseLife() lands directly in the GameOver flow when the hearts run out).
internal class WebcamLevel : GameScene
{
	// The discrete, per-difficulty knobs for the challenge. The generic cadence
	// (saucer arm/blink/spawn timing) already scales off Settings.DifficultyModifier
	// in SpawnSaucers; this table adds the things that don't come from that single
	// float: how many hits you can take, how many kills win, how many saucers can be
	// on screen at once, and how fast the saucers drift + the plasma cruises.
	private struct DifficultyTuning
	{
		public int Hearts;         // lives (hearts) you start with
		public int KillTarget;     // saucers to splat to win
		public int MaxSaucers;     // simultaneous-saucer ceiling
		public float SaucerSpeedMul; // WebcamUfo drift-speed scale
		public float PlasmaSpeedMul; // WebcamPlasma cruise-speed scale
		// Cadence as ABSOLUTE per-tier durations in milliseconds (NO difficulty-modifier
		// divisor — each tier's feel is authored directly, then a small ±jitter is added at
		// spawn for variety). SpawnIntervalMs = gap between successive saucer spawns;
		// ArmDelayMs = wander time before a saucer starts charging (its "rate of fire" — a
		// saucer fires exactly once per arm cycle, so bigger = fires less often); ChargeTimeMs
		// = the blink-charge windup before the orb releases. Plan: author Easy + Very_Hard by
		// feel, then interpolate the middle tiers off Settings.DifficultyModifier.
		public float SpawnIntervalMs;
		public float ArmDelayMs;
		public float ChargeTimeMs;
		// DeathStar-mine hazard (F2): MaxMines = simultaneous cap; MineSpawnMs = gap between
		// mine spawns; MineLifeMs = how long a mine wanders before it flies off + despawns.
		public int MaxMines;
		public float MineSpawnMs;
		public float MineLifeMs;
		// Screen-bisecting mothership laser (F1): MothershipMs = gap between bisect events
		// (0 disables). The event itself (enter -> charge -> fire -> leave) isn't otherwise
		// tuned per-tier here — it's a fixed choreography, only how OFTEN it happens.
		public float MothershipMs;
	}

	// Shipped baseline, indexed by (int)Settings.DifficultyLevel (Easy..Inzane).
	// These are a starting point — A/B them live with the ?wc* debug flags (see
	// Compat/DebugFlags.cs) then bake the chosen numbers back here.
	private static readonly DifficultyTuning[] Tunings = new DifficultyTuning[]
	{
		new DifficultyTuning { Hearts = 5, KillTarget = 12, MaxSaucers = 3, SaucerSpeedMul = 0.85f, PlasmaSpeedMul = 0.75f, SpawnIntervalMs = 6000f, ArmDelayMs = 15000f, ChargeTimeMs = 4500f, MaxMines = 2, MineSpawnMs = 4000f, MineLifeMs = 8000f, MothershipMs = 20000f }, // Easy
		new DifficultyTuning { Hearts = 4, KillTarget = 16, MaxSaucers = 4, SaucerSpeedMul = 1.0f,  PlasmaSpeedMul = 0.9f,  SpawnIntervalMs = 3600f, ArmDelayMs = 9500f, ChargeTimeMs = 3600f, MaxMines = 3, MineSpawnMs = 3200f, MineLifeMs = 7000f, MothershipMs = 16000f }, // Medium
		new DifficultyTuning { Hearts = 3, KillTarget = 20, MaxSaucers = 5, SaucerSpeedMul = 1.15f, PlasmaSpeedMul = 1.05f, SpawnIntervalMs = 2800f, ArmDelayMs = 7000f, ChargeTimeMs = 3000f, MaxMines = 3, MineSpawnMs = 2600f, MineLifeMs = 6500f, MothershipMs = 13000f }, // Hard
		new DifficultyTuning { Hearts = 2, KillTarget = 26, MaxSaucers = 6, SaucerSpeedMul = 1.3f,  PlasmaSpeedMul = 1.2f,  SpawnIntervalMs = 2200f, ArmDelayMs = 5500f, ChargeTimeMs = 2600f, MaxMines = 4, MineSpawnMs = 2200f, MineLifeMs = 6000f, MothershipMs = 11000f }, // Very_Hard
		new DifficultyTuning { Hearts = 2, KillTarget = 32, MaxSaucers = 7, SaucerSpeedMul = 1.5f,  PlasmaSpeedMul = 1.4f,  SpawnIntervalMs = 1800f, ArmDelayMs = 4500f, ChargeTimeMs = 2200f, MaxMines = 5, MineSpawnMs = 1800f, MineLifeMs = 5500f, MothershipMs = 9000f }, // Inzane
	};

	// The active run's resolved tuning (difficulty row + any ?wc* debug overrides),
	// set in Initialize before play begins.
	private DifficultyTuning tuning;

	// Last DebugFlags.WebcamTuneVersion this run resolved against — the live tuner
	// panel (?wctune) bumps the version on every edit and UpdateNormal re-resolves.
	private int appliedTuneVersion;

	private int kills;

	private int hearts;

	private bool won;

	private bool introShown;

	private Timer spawnTimer = new Timer(1800f, repeating: false);

	// F2 mines: gap between DeathStar-mine spawns (duration set live from tuning.MineSpawnMs).
	private Timer mineTimer = new Timer(2200f, repeating: false);

	// F1 mothership: gap between screen-bisecting laser events (duration = tuning.MothershipMs).
	private Timer mothershipTimer = new Timer(11000f, repeating: false);

	// Post-hit mercy window: incoming plasma/mine/beam still bursts but doesn't hurt.
	private Timer graceTimer = new Timer(2200f, repeating: false);

	private readonly List<WebcamUfo> ufos = new List<WebcamUfo>();

	private readonly List<WebcamPlasma> plasmas = new List<WebcamPlasma>();

	private readonly List<WebcamMine> mines = new List<WebcamMine>();

	private readonly List<WebcamMothership> motherships = new List<WebcamMothership>();

	private SpriteFont font;

	private Texture2D heart;

	private Texture2D blank;

	public WebcamLevel(Game game)
		: base(game, Levels.WebcamAliens)
	{
		// draw the hearts/kill-counter HUD above the stock score HUD (1000)
		base.DrawOrder = DrawOrders.DrawOrderHUD + 1;
		AllowAIFriends = false;
		base.OnFinished += WebcamLevel_OnFinished;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		font = contentManager.Load<SpriteFont>("GFX/menu/menufont");
		heart = contentManager.Load<Texture2D>("GFX/Sprites/heart");
		blank = contentManager.Load<Texture2D>("GFX/Menu/blank");
	}

	protected override void PopulateEventList()
	{
		// The gameplay is all direct spawning in UpdateNormal; the event list only
		// needs a checkpoint so the InfiniteLives-cheat reset path has something
		// to revert to.
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		score.DisableCombos();
		// the meme screenshot's plain starfield, not the holodeck sim chamber
		Background.SetSpace();
		Settings settings = Settings.GetInstance();
		// The player picked a difficulty in the challenge difficulty menu — this level
		// routes through MenuScene.challengeSelector_levelSelected like every other
		// challenge, so Settings.CurrentDifficulty is already their choice. ?wcdiff
		// forces a tier for live tuning without unlocking it in the menu; it writes the
		// shared CurrentDifficulty (not persisted — the difficulty menu is the only
		// saver), so a tester who then exits to the menu carries that tier until they
		// re-pick. Debug-flag only, so a shipped build never hits this.
		if (DebugFlags.WebcamDifficulty.HasValue)
		{
			settings.CurrentDifficulty = DebugFlags.WebcamDifficulty.Value;
		}
		ResolveTuning(settings.CurrentDifficulty);
		appliedTuneVersion = DebugFlags.WebcamTuneVersion;
		SeedTunePanel();
		// Hard+ earns the full Japanese-vocal "classic" cut; Easy/Medium get the clean
		// lyric-free instrumental (SoundManager.ClassicForDifficulty()).
		base.SoundManager.PlayMusic(SoundManager.ClassicForDifficulty());
		base.Initialize();
		// GameScene showed the keyboard player's crosshair cursor, but there is no
		// ship to aim here — the player's body is the pointer. Keep it hidden.
		((DrawableGameComponent)ServiceHelper.Get<IMousePointerService>().MousePointer).Visible = false;
		// Lock the modifier at the picked tier so the mid-level ramp-up doesn't drift it.
		settings.LockDifficulty();
		// No PlayerShip: the player is the webcam image. Keep score.Lives at 0 so
		// the stock lives strip stays empty and LoseLife() (called when the last
		// heart goes) drops straight into the GameOver flow.
		spawnPlayerNormally = false;
		score.Lives = 0;
		kills = 0;
		hearts = tuning.Hearts;
		won = false;
		introShown = false;
		ufos.Clear();
		plasmas.Clear();
		mines.Clear();
		motherships.Clear();
		spawnTimer.Reset();
		spawnTimer.Start();
		mineTimer.Duration = tuning.MineSpawnMs;
		mineTimer.Reset();
		mineTimer.Start();
		mothershipTimer.Duration = tuning.MothershipMs;
		mothershipTimer.Reset();
		mothershipTimer.Start();
		graceTimer.Reset();
		graceTimer.Stop();
		// Hand the browser the stage: camera picker + preview + background
		// removal. The level idles underneath until the player joins (or exits
		// via the dialog's Back, which lands in the Cancelled poll below).
		WebcamInterop.BeginSetup();
	}

	// Pick the difficulty row, then layer any ?wc* debug overrides on top (absolute
	// for the counts, a multiplier for the speeds), then the live tuner panel's
	// runtime overrides (?wctune — ABSOLUTE final values) on top of everything.
	// See Compat/DebugFlags.cs.
	private void ResolveTuning(Settings.DifficultyLevel difficulty)
	{
		int idx = (int)difficulty;
		if (idx < 0)
		{
			idx = 0;
		}
		if (idx >= Tunings.Length)
		{
			idx = Tunings.Length - 1;
		}
		tuning = Tunings[idx];
		if (DebugFlags.WebcamHearts.HasValue)
		{
			tuning.Hearts = DebugFlags.WebcamHearts.Value;
		}
		if (DebugFlags.WebcamKills.HasValue)
		{
			tuning.KillTarget = DebugFlags.WebcamKills.Value;
		}
		if (DebugFlags.WebcamSaucers.HasValue)
		{
			tuning.MaxSaucers = DebugFlags.WebcamSaucers.Value;
		}
		if (DebugFlags.WebcamSaucerSpeed.HasValue)
		{
			tuning.SaucerSpeedMul *= DebugFlags.WebcamSaucerSpeed.Value;
		}
		if (DebugFlags.WebcamPlasmaSpeed.HasValue)
		{
			tuning.PlasmaSpeedMul *= DebugFlags.WebcamPlasmaSpeed.Value;
		}
		if (DebugFlags.WebcamSpawnInterval.HasValue)
		{
			tuning.SpawnIntervalMs = DebugFlags.WebcamSpawnInterval.Value;
		}
		if (DebugFlags.WebcamArmDelay.HasValue)
		{
			tuning.ArmDelayMs = DebugFlags.WebcamArmDelay.Value;
		}
		if (DebugFlags.WebcamChargeTime.HasValue)
		{
			tuning.ChargeTimeMs = DebugFlags.WebcamChargeTime.Value;
		}
		if (DebugFlags.WebcamMineMax.HasValue)
		{
			tuning.MaxMines = DebugFlags.WebcamMineMax.Value;
		}
		if (DebugFlags.WebcamMineSpawn.HasValue)
		{
			tuning.MineSpawnMs = DebugFlags.WebcamMineSpawn.Value;
		}
		if (DebugFlags.WebcamMineLife.HasValue)
		{
			tuning.MineLifeMs = DebugFlags.WebcamMineLife.Value;
		}
		if (DebugFlags.WebcamMothership.HasValue)
		{
			tuning.MothershipMs = DebugFlags.WebcamMothership.Value;
		}
		if (DebugFlags.WebcamTuneHearts.HasValue)
		{
			tuning.Hearts = DebugFlags.WebcamTuneHearts.Value;
		}
		if (DebugFlags.WebcamTuneKills.HasValue)
		{
			tuning.KillTarget = DebugFlags.WebcamTuneKills.Value;
		}
		if (DebugFlags.WebcamTuneSaucers.HasValue)
		{
			tuning.MaxSaucers = DebugFlags.WebcamTuneSaucers.Value;
		}
		if (DebugFlags.WebcamTuneSaucerSpeed.HasValue)
		{
			tuning.SaucerSpeedMul = DebugFlags.WebcamTuneSaucerSpeed.Value;
		}
		if (DebugFlags.WebcamTunePlasmaSpeed.HasValue)
		{
			tuning.PlasmaSpeedMul = DebugFlags.WebcamTunePlasmaSpeed.Value;
		}
		if (DebugFlags.WebcamTuneSpawnInterval.HasValue)
		{
			tuning.SpawnIntervalMs = DebugFlags.WebcamTuneSpawnInterval.Value;
		}
		if (DebugFlags.WebcamTuneArmDelay.HasValue)
		{
			tuning.ArmDelayMs = DebugFlags.WebcamTuneArmDelay.Value;
		}
		if (DebugFlags.WebcamTuneChargeTime.HasValue)
		{
			tuning.ChargeTimeMs = DebugFlags.WebcamTuneChargeTime.Value;
		}
		if (DebugFlags.WebcamTuneMineMax.HasValue)
		{
			tuning.MaxMines = DebugFlags.WebcamTuneMineMax.Value;
		}
		if (DebugFlags.WebcamTuneMineSpawn.HasValue)
		{
			tuning.MineSpawnMs = DebugFlags.WebcamTuneMineSpawn.Value;
		}
	}

	// A live tuner-panel edit landed (?wctune): re-resolve the knobs and apply what
	// must change in place — hearts snap to the new count, speed changes rescale the
	// saucers/orbs already on screen (KillTarget/MaxSaucers are read live anyway).
	// Every apply re-seeds the panel so it always shows the level's actual resolved
	// values (this is what makes its "Reset to tier" button round-trip).
	private void ApplyLiveTuning()
	{
		int oldHearts = tuning.Hearts;
		float oldSaucerSpeed = tuning.SaucerSpeedMul;
		float oldPlasmaSpeed = tuning.PlasmaSpeedMul;
		ResolveTuning(Settings.GetInstance().CurrentDifficulty);
		appliedTuneVersion = DebugFlags.WebcamTuneVersion;
		if (tuning.Hearts != oldHearts)
		{
			hearts = tuning.Hearts;
		}
		if (tuning.SaucerSpeedMul != oldSaucerSpeed)
		{
			foreach (WebcamUfo ufo in ufos)
			{
				ufo.SetSpeedMultiplier(tuning.SaucerSpeedMul);
			}
		}
		if (tuning.PlasmaSpeedMul != oldPlasmaSpeed)
		{
			foreach (WebcamPlasma plasma in plasmas)
			{
				plasma.SetSpeedMultiplier(tuning.PlasmaSpeedMul);
			}
		}
		Console.WriteLine("[wctune] applied: hearts=" + tuning.Hearts + " kills=" + tuning.KillTarget
			+ " saucers=" + tuning.MaxSaucers + " saucerspeed=" + tuning.SaucerSpeedMul
			+ " plasmaspeed=" + tuning.PlasmaSpeedMul + " spawnMs=" + tuning.SpawnIntervalMs
			+ " armMs=" + tuning.ArmDelayMs + " chargeMs=" + tuning.ChargeTimeMs);
		SeedTunePanel();
	}

	// Push the resolved tuning into the eaWcTune panel (?wctune only). Idempotent —
	// show() re-renders in place when the panel already exists.
	private void SeedTunePanel()
	{
		if (DebugFlags.WebcamTune)
		{
			WebcamInterop.TuneShow(Settings.GetInstance().CurrentDifficulty.ToString(),
				tuning.Hearts, tuning.KillTarget, tuning.MaxSaucers,
				tuning.SaucerSpeedMul, tuning.PlasmaSpeedMul,
				tuning.SpawnIntervalMs, tuning.ArmDelayMs, tuning.ChargeTimeMs,
				tuning.MaxMines, tuning.MineSpawnMs, tuning.MineLifeMs, tuning.MothershipMs);
		}
	}

	private void WebcamLevel_OnFinished(object sender, FinishedArgs args)
	{
		// every exit path (victory, defeat, pause-exit, cancel) releases the camera
		if (DebugFlags.WebcamTune)
		{
			WebcamInterop.TuneHide();
		}
		WebcamInterop.Stop();
		score.EnableCombos();
		ufos.Clear();
		plasmas.Clear();
		mines.Clear();
		motherships.Clear();
	}

	public override void Update(GameTime gameTime)
	{
		graceTimer.Update(gameTime);
		spawnTimer.Update(gameTime);
		mineTimer.Update(gameTime);
		mothershipTimer.Update(gameTime);
		// Backed out of the camera-setup dialog: leave the level like a pause-menu
		// exit would. (Stop() flips the state off Cancelled, so this fires once.)
		if (WebcamInterop.State == WebcamInterop.SessionState.Cancelled)
		{
			WebcamInterop.Stop();
			Terminate(FinishedMode.exit);
			return;
		}
		base.Update(gameTime);
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		base.UpdateNormal(gameTime);
		Prune(ufos);
		Prune(plasmas);
		Prune(mines);
		Prune(motherships);
		// Live tuner panel (?wctune): pick up an edit before the Playing gate so a
		// change made during camera setup (or applied on unpause) still lands.
		if (DebugFlags.WebcamTuneVersion != appliedTuneVersion)
		{
			ApplyLiveTuning();
		}
		if (WebcamInterop.State != WebcamInterop.SessionState.Playing)
		{
			return;
		}
		if (!introShown)
		{
			introShown = true;
			AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			animatedMessage.Setup("Splat " + tuning.KillTarget + " saucers\nwith your body!", SoundManager.Texts.GetReady, AnimatedMessage.MessageType.starwarsblue);
			Collection.Add((GameComponent)(object)animatedMessage);
		}
		if (WebcamInterop.PlayerVisible)
		{
			float dt = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			SpawnSaucers();
			TestPlayerTouchesSaucers();     // GOOD collision: instant (the player WANTS to hit saucers)
			TestPlasmaHitsPlayer(dt);       // BAD collisions: need LeewayMs of STEADY contact to count
			SpawnMines();
			TestMinesHitPlayer(dt);
			SpawnMothership();
			TestBeamHitsPlayer(dt);
		}
		if (kills >= tuning.KillTarget && !won)
		{
			won = true;
			Victory();
			AnimatedMessage animatedMessage2 = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			animatedMessage2.Setup("Wave Completed!", SoundManager.Texts.WaveCompleted, AnimatedMessage.MessageType.starwarsblue);
			Collection.Add((GameComponent)(object)animatedMessage2);
		}
	}

	private static void Prune<T>(List<T> list) where T : AlienDrawableGameComponent
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			if (list[i].IsDead)
			{
				list.RemoveAt(i);
			}
		}
	}

	// Per-spawn timing variety: an authored cadence (ms) is multiplied by this so saucers
	// don't arm/fire/spawn in robotic lockstep. +/-15% around the authored value.
	private const float CadenceJitterFrac = 0.15f;

	private static float CadenceJitter()
	{
		return RandomHelper.RandomNextFloat(1f - CadenceJitterFrac, 1f + CadenceJitterFrac);
	}

	// Bad-collision leeway (a gift to the player): a hazard that HURTS you (plasma orb, mothership
	// beam, mine) only lands a hit after the mask has STEADILY overlapped it for this long -- so a
	// jittery webcam mask or a split-second-late dodge doesn't cost a life. Continuous: the
	// per-hazard contact accumulator resets the instant contact breaks. Killing SAUCERS is
	// deliberately NOT leewayed (the player wants that; it stays instant). ?wchitleeway=<ms> tunes it.
	private const float HitLeewayMs = 100f;   // ~0.1s

	private float LeewayMs => DebugFlags.WebcamHitLeeway ?? HitLeewayMs;

	private void SpawnSaucers()
	{
		if (won || !spawnTimer.Finished)
		{
			return;
		}
		// population ramps up as the player racks up kills, capped per difficulty
		int cap = Math.Min(1 + kills / 4, tuning.MaxSaucers);
		if (ufos.Count >= cap)
		{
			// full house: check again shortly
			spawnTimer.Duration = 400f;
			spawnTimer.Reset();
			spawnTimer.Start();
			return;
		}
		WebcamUfo webcamUfo = WebcamUfo.NewWebcamUfo(Collection, base.Game);
		// Cadence = the tier's authored absolute ms + a small +/-jitter for variety. NO
		// difficulty-modifier divisor (removed — each tier's feel is set directly in Tunings),
		// and no within-run "arm faster over time" ramp (removed too, so the authored value
		// IS the delay). Tune per tier live via the ?wctune panel or ?wcarm/?wccharge/?wcspawn.
		float armDelay = tuning.ArmDelayMs * CadenceJitter();
		float blinkTime = tuning.ChargeTimeMs * CadenceJitter();
		webcamUfo.Setup(RandomEdgePosition(), armDelay, blinkTime, tuning.SaucerSpeedMul);
		webcamUfo.OnFired += ufo_OnFired;
		Collection.Add((GameComponent)(object)webcamUfo);
		ufos.Add(webcamUfo);
		spawnTimer.Duration = tuning.SpawnIntervalMs * CadenceJitter();
		spawnTimer.Reset();
		spawnTimer.Start();
	}

	private static Vector2 RandomEdgePosition()
	{
		switch (RandomHelper.Random.Next(4))
		{
		case 0:
			return new Vector2(-40f, RandomHelper.RandomNextFloat(60f, 540f));
		case 1:
			return new Vector2(840f, RandomHelper.RandomNextFloat(60f, 540f));
		case 2:
			return new Vector2(RandomHelper.RandomNextFloat(60f, 740f), -40f);
		default:
			return new Vector2(RandomHelper.RandomNextFloat(60f, 740f), 640f);
		}
	}

	private void ufo_OnFired(WebcamUfo sender, Vector2 target)
	{
		WebcamPlasma webcamPlasma = WebcamPlasma.NewWebcamPlasma(Collection, base.Game);
		webcamPlasma.Setup(sender.Position, target, tuning.PlasmaSpeedMul);
		Collection.Add((GameComponent)(object)webcamPlasma);
		plasmas.Add(webcamPlasma);
		base.SoundManager.PlayCue("lazershotnoloop");
	}

	private void TestPlayerTouchesSaucers()
	{
		foreach (WebcamUfo ufo in ufos)
		{
			if (!ufo.IsDead && WebcamInterop.HitCircle(ufo.Position, ufo.HitRadius))
			{
				KillSaucer(ufo);
			}
		}
	}

	// Asplode a saucer + credit the kill (score + KillTarget progress). Shared by the player's
	// body-swat AND the mothership beam sweeping over it — both are legit kills the player wants.
	private void KillSaucer(WebcamUfo ufo)
	{
		ufo.Asplode();
		kills++;
		score.AddScore(PointValues.WebcamUfo, isCombo: false, ufo.Position, 0);
		// Grab the level-select thumbnail on the first splat: the player is clearly in frame and
		// there's a saucer + explosion on screen. The generic busy-scene trigger never fires here
		// (too few entities). No-op unless the opt-in Settings.WebcamScreenshot is on (ForceSnapshot
		// gates on ScreenshotEnabled).
		if (kills == 1)
		{
			ForceSnapshot();
		}
	}

	// Composite the player's camera overlay into the thumbnail at the snapshot instant
	// (the JS overlay is torn down before ScreenshotSaver.SaveScreenShot runs).
	protected override void OnScreenshotResolved()
	{
		if (Settings.GetInstance().WebcamScreenshot)
		{
			ScreenshotSaver.CaptureWebcamOverlay(base.GraphicsDevice);
		}
	}

	private void TestPlasmaHitsPlayer(float dt)
	{
		foreach (WebcamPlasma plasma in plasmas)
		{
			if (plasma.IsDead)
			{
				continue;
			}
			// slightly forgiving: the orb must reach INTO the body, not just graze it; AND it must
			// stay there for LeewayMs (a brief graze / cam blip passes through harmlessly).
			if (WebcamInterop.HitCircle(plasma.Position, plasma.HitRadius * 0.7f))
			{
				plasma.ContactMs += dt;
				if (plasma.ContactMs >= LeewayMs)
				{
					plasma.Detonate(withZap: true);
					PlayerHit();
				}
			}
			else
			{
				plasma.ContactMs = 0f;   // steady-contact only: any break resets the clock
			}
		}
	}

	// F2: keep the DeathStar-mine population topped up to MaxMines on the MineSpawnMs cadence.
	// Simpler than SpawnSaucers — mines aren't kill-gated and self-despawn on their lifetime.
	private void SpawnMines()
	{
		if (won || !mineTimer.Finished)
		{
			return;
		}
		if (mines.Count >= tuning.MaxMines)
		{
			// full: recheck shortly instead of waiting a whole interval
			mineTimer.Duration = 400f;
			mineTimer.Reset();
			mineTimer.Start();
			return;
		}
		WebcamMine mine = WebcamMine.NewWebcamMine(Collection, base.Game);
		mine.Setup(RandomEdgePosition(), tuning.MineLifeMs * CadenceJitter());
		Collection.Add((GameComponent)(object)mine);
		mines.Add(mine);
		mineTimer.Duration = tuning.MineSpawnMs * CadenceJitter();
		mineTimer.Reset();
		mineTimer.Start();
	}

	// F2: touching a mine costs a life + bursts the blue DeathStar explosion — but only after
	// LeewayMs of steady contact (same gift as the plasma).
	private void TestMinesHitPlayer(float dt)
	{
		foreach (WebcamMine mine in mines)
		{
			if (mine.IsDead)
			{
				continue;
			}
			if (WebcamInterop.HitCircle(mine.Position, mine.HitRadius))
			{
				mine.ContactMs += dt;
				if (mine.ContactMs >= LeewayMs)
				{
					mine.Detonate();
					PlayerHit();
				}
			}
			else
			{
				mine.ContactMs = 0f;
			}
		}
	}

	// F1: launch a screen-bisecting mothership every MothershipMs (0 disables), one at a time.
	private void SpawnMothership()
	{
		if (won || tuning.MothershipMs <= 0f || !mothershipTimer.Finished)
		{
			return;
		}
		if (motherships.Count > 0)
		{
			// one bisector at a time: recheck shortly
			mothershipTimer.Duration = 800f;
			mothershipTimer.Reset();
			mothershipTimer.Start();
			return;
		}
		WebcamMothership ship = WebcamMothership.NewWebcamMothership(Collection, base.Game);
		ship.Setup(PickBisectOrientation());
		Collection.Add((GameComponent)(object)ship);
		motherships.Add(ship);
		mothershipTimer.Duration = tuning.MothershipMs * CadenceJitter();
		mothershipTimer.Reset();
		mothershipTimer.Start();
	}

	// Mostly the vertical top-down bisect; sometimes a horizontal one from a random side.
	// ?wcmothershipdir=vertical|horizontal forces it for testing (null => the random mix).
	private WebcamMothership.Bisect PickBisectOrientation()
	{
		string force = DebugFlags.WebcamMothershipDir;
		if (force == "vertical")
		{
			return WebcamMothership.Bisect.VerticalDown;
		}
		if (force == "horizontal")
		{
			return (RandomHelper.Random.Next(2) == 0) ? WebcamMothership.Bisect.HorizontalFromLeft : WebcamMothership.Bisect.HorizontalFromRight;
		}
		int roll = RandomHelper.Random.Next(5);
		if (roll < 3)
		{
			return WebcamMothership.Bisect.VerticalDown;   // ~60% vertical
		}
		return (roll == 3) ? WebcamMothership.Bisect.HorizontalFromLeft : WebcamMothership.Bisect.HorizontalFromRight;
	}

	// F1: while a mothership's beam is live, standing in it costs a life (grace-gated). The beam
	// also sweeps any space MINES it crosses out of existence (a mercy for the player — plain
	// explosion, no life cost).
	private void TestBeamHitsPlayer(float dt)
	{
		foreach (WebcamMothership ship in motherships)
		{
			if (!ship.BeamActive)
			{
				ship.BeamContactMs = 0f;
				continue;
			}
			// standing in the beam only costs a life after LeewayMs of steady contact.
			if (WebcamInterop.HitBeam(ship.BeamOrigin, ship.BeamDirection, ship.BeamLength, ship.BeamHalfWidth))
			{
				ship.BeamContactMs += dt;
				if (ship.BeamContactMs >= LeewayMs)
				{
					PlayerHit();
					ship.BeamContactMs = 0f;   // re-accumulate; PlayerHit's grace window rate-limits repeats
				}
			}
			else
			{
				ship.BeamContactMs = 0f;
			}
			// the beam clears MINES it crosses INSTANTLY (a mercy for the player, not a hit)...
			foreach (WebcamMine mine in mines)
			{
				if (!mine.IsDead && WebcamMothership.BeamHitsCircle(ship.BeamOrigin, ship.BeamDirection, ship.BeamLength, ship.BeamHalfWidth, mine.Position, mine.HitRadius))
				{
					mine.DestroyByLaser();
				}
			}
			// ...and KILLS saucers it crosses too, with full kill credit (a kill the player wanted).
			foreach (WebcamUfo ufo in ufos)
			{
				if (!ufo.IsDead && WebcamMothership.BeamHitsCircle(ship.BeamOrigin, ship.BeamDirection, ship.BeamLength, ship.BeamHalfWidth, ufo.Position, ufo.HitRadius))
				{
					KillSaucer(ufo);
				}
			}
		}
	}

	private void PlayerHit()
	{
		if (graceTimer.Active || Settings.GetInstance().Invulnerability)
		{
			return;
		}
		// The Doom-derived "boss takes a hit" cue — the same sound the final BrainBoss
		// plays when it's hit (BrainBoss.HitBy). A cheeky reference; replaces the old
		// head_asplode placeholder.
		base.SoundManager.PlayCue("hit_boss");
		graceTimer.Reset();
		graceTimer.Start();
		if (Settings.GetInstance().InfiniteLives)
		{
			return;
		}
		hearts--;
		if (hearts <= 0)
		{
			// score.Lives is 0, so this routes to the standard GameOver flow
			// ("Mission Failed" + the evil laugh, then back to the menu).
			LoseLife();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		SpriteBatchWrapper spriteBatch = base.SpriteBatch;
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		// hearts, top-centre + smaller — the old top-left row overlapped the score (which
		// draws at SafeZone.(Left,Top)); centring clears both the score and the top-right
		// kill counter.
		float heartScale = 0.6f;
		float heartSpacing = 30f;
		float heartsLeft = 400f - (float)(hearts - 1) * heartSpacing / 2f;
		for (int i = 0; i < hearts; i++)
		{
			spriteBatch.Draw(heart, new Vector2(heartsLeft + (float)i * heartSpacing, (float)((General.SafeZone).Top + 18)), 0f, heartScale, center: true, Color.White);
		}
		// kill counter, top-right — the original's "Killed: N"
		if (font != null)
		{
			string text = "Killed: " + kills + " / " + tuning.KillTarget;
			Vector2 size = font.MeasureString(text) * 0.7f;
			spriteBatch.DrawShadowString(text, new Vector2((float)(General.SafeZone).Right - size.X, (float)(General.SafeZone).Top + 6f), 0.7f, Color.Black, Color.White, new Vector2(2f, 2f), 1f, metal: false);
			// prompts: mask gone stale / player out of frame
			if (WebcamInterop.State == WebcamInterop.SessionState.Playing && !WebcamInterop.PlayerVisible && introShown)
			{
				float pulse = 0.6f + 0.4f * (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 4.0);
				string hint = "Step into view!";
				Vector2 hintSize = font.MeasureString(hint);
				spriteBatch.DrawShadowString(hint, new Vector2(400f - hintSize.X / 2f, 150f), 1f, Color.Black, Color.White, new Vector2(2f, 2f), pulse, metal: false);
			}
		}
		// short red sting when a heart is lost (the grace window's opening beat)
		if (graceTimer.Active && graceTimer.TimeElapsed < 450f)
		{
			float a = 0.45f * (1f - graceTimer.TimeElapsed / 450f);
			spriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(1f, 0f, 0f, a));
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}
}
