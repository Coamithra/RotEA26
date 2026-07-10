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
	// The discrete, per-difficulty knobs for the challenge: how many hits you can take,
	// how many kills win, how many saucers/mines can be on screen at once, how fast the
	// saucers drift + the plasma cruises, and the spawn cadences. Spawns (saucer/mine/
	// mothership) are random Poisson rolls whose MEAN gap is the *Ms field; the per-saucer
	// arm/charge timers stay authored absolute ms.
	private struct DifficultyTuning
	{
		public int Hearts;         // lives (hearts) you start with
		public int KillTarget;     // saucers to splat to win
		public int MaxSaucers;     // simultaneous-saucer ceiling
		public float SaucerSpeedMul; // WebcamUfo drift-speed scale
		public float PlasmaSpeedMul; // WebcamPlasma cruise-speed scale
		// Cadence in milliseconds. SpawnIntervalMs = AVERAGE gap between saucer spawns (a random
		// Poisson roll at rate 1/SpawnIntervalMs — not a fixed timer). ArmDelayMs = wander time
		// before a saucer starts charging (its "rate of fire" — a saucer fires exactly once per
		// arm cycle, so bigger = fires less often); ChargeTimeMs = the blink-charge windup before
		// the orb releases. Arm/charge are authored absolute ms + a small ±jitter at spawn for
		// variety (NO difficulty-modifier divisor — each tier's feel is set directly).
		public float SpawnIntervalMs;
		public float ArmDelayMs;
		public float ChargeTimeMs;
		// DeathStar-mine hazard (F2): MaxMines = simultaneous cap; MineSpawnMs = AVERAGE gap between
		// mine spawns (Poisson roll, rate 1/MineSpawnMs); MineLifeMs = how long a mine wanders
		// before it flies off + despawns.
		public int MaxMines;
		public float MineSpawnMs;
		public float MineLifeMs;
		// Screen-bisecting mothership laser (F1): MothershipMs = AVERAGE ms between bisect events
		// (0 disables). Spawns are a random Poisson process at rate 1/MothershipMs (RandomFromAverage),
		// NOT a fixed timer — so the gaps vary around this mean. Never two at once. The event itself
		// (enter -> charge -> fire -> leave) isn't otherwise tuned per-tier here — it's a fixed
		// choreography, only how OFTEN it happens on average.
		public float MothershipMs;
	}

	// Shipped baseline, indexed by (int)Settings.DifficultyLevel (Easy..Inzane).
	// These are a starting point — A/B them live with the ?wc* debug flags (see
	// Compat/DebugFlags.cs) then bake the chosen numbers back here.
	private static readonly DifficultyTuning[] Tunings = new DifficultyTuning[]
	{
		new DifficultyTuning { Hearts = 5, KillTarget = 75, MaxSaucers = 15, SaucerSpeedMul = 0.85f, PlasmaSpeedMul = 0.75f, SpawnIntervalMs = 1000f, ArmDelayMs = 15000f, ChargeTimeMs = 4500f, MaxMines = 3, MineSpawnMs = 6000f, MineLifeMs = 8000f, MothershipMs = 12000f }, // Easy
		new DifficultyTuning { Hearts = 4, KillTarget = 75, MaxSaucers = 15, SaucerSpeedMul = 1.0f,  PlasmaSpeedMul = 0.9f,  SpawnIntervalMs = 1000f, ArmDelayMs = 9500f, ChargeTimeMs = 3600f, MaxMines = 3, MineSpawnMs = 6000f, MineLifeMs = 7000f, MothershipMs = 12000f }, // Medium
		new DifficultyTuning { Hearts = 3, KillTarget = 75, MaxSaucers = 15, SaucerSpeedMul = 1.15f, PlasmaSpeedMul = 1.05f, SpawnIntervalMs = 1000f, ArmDelayMs = 7000f, ChargeTimeMs = 3000f, MaxMines = 3, MineSpawnMs = 6000f, MineLifeMs = 6500f, MothershipMs = 12000f }, // Hard
		new DifficultyTuning { Hearts = 2, KillTarget = 150, MaxSaucers = 15, SaucerSpeedMul = 1.3f,  PlasmaSpeedMul = 1.2f,  SpawnIntervalMs = 1000f, ArmDelayMs = 5500f, ChargeTimeMs = 2600f, MaxMines = 3, MineSpawnMs = 6000f, MineLifeMs = 6000f, MothershipMs = 12000f }, // Very_Hard
		new DifficultyTuning { Hearts = 2, KillTarget = 75, MaxSaucers = 15, SaucerSpeedMul = 1.5f,  PlasmaSpeedMul = 1.4f,  SpawnIntervalMs = 1000f, ArmDelayMs = 4500f, ChargeTimeMs = 2200f, MaxMines = 3, MineSpawnMs = 6000f, MineLifeMs = 5500f, MothershipMs = 12000f }, // Inzane
	};

	// The active run's resolved tuning (difficulty row + any ?wc* debug overrides),
	// set in Initialize before play begins.
	private DifficultyTuning tuning;

	// Difficulty-scaled webcam knobs that aren't plain struct columns: the max simultaneous
	// plasma orbs the saucers keep on screen, and which mothership shapes are unlocked. All
	// three are (re)computed from the tier's difficulty number in ResolveTuning.
	private int resolvedMaxPlasma = 1;

	private bool mothershipAllowCenter = true;

	private bool mothershipAllowHorizontal = true;

	// Last DebugFlags.WebcamTuneVersion this run resolved against — the live tuner
	// panel (?wctune) bumps the version on every edit and UpdateNormal re-resolves.
	private int appliedTuneVersion;

	private int kills;

	private int hearts;

	private bool won;

	private bool introShown;

	// Saucers (SpawnIntervalMs), mines (MineSpawnMs) and the mothership (MothershipMs) all spawn
	// on a random Poisson roll (RandomFromAverage, rate 1/interval) in their Spawn* methods, not
	// timers — so no per-spawner Timer field is needed here.

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
		// --- Difficulty-SCALED knobs (webcam difficulty pass) -----------------------------
		// Anchored on the values chosen for Medium and scaled by the tier's difficulty number
		// (GetDifficultyValue: Easy .35 / Medium .6 / Hard .8 / Very_Hard 1 / Inzane 1.2), so
		// Easy/Hard/Inzane fall out automatically. `m` is the per-tier base value (ramp-immune —
		// the challenge locks difficulty). These OVERWRITE the matching table columns; the
		// URL/panel overrides below still layer on top for live tuning.
		float m = Settings.GetInstance().GetDifficultyValue(difficulty);
		// Lives (hearts): Medium 6, more on Easy, fewer on the hard tiers (decreasing in m).
		//   -> Easy 8 / Medium 6 / Hard 5 / Very_Hard 3 / Inzane 2.
		tuning.Hearts = MathHelper.Clamp((int)Math.Round(6.0 + (0.6f - m) * 7.5, MidpointRounding.AwayFromZero), 1, 9);
		// Kill target: two anchors — Medium 50, Very_Hard 150 — linear in m; Easy floored at 30.
		//   -> Easy 30 / Medium 50 / Hard 100 / Very_Hard 150 / Inzane 200.
		tuning.KillTarget = Math.Max(30, (int)Math.Round(50.0 + (m - 0.6f) * 250.0, MidpointRounding.AwayFromZero));
		// Space mines on screen at once: Medium 1, up to 4 on Inzane (floor 1).
		//   -> Easy 1 / Medium 1 / Hard 2 / Very_Hard 3 / Inzane 4.
		tuning.MaxMines = MathHelper.Clamp((int)Math.Round(1.0 + (m - 0.6f) * 5.0, MidpointRounding.AwayFromZero), 1, 4);
		// UFO chargeup telegraph DOUBLED (Medium 3600 -> 7200) — the authored per-tier curve x2.
		tuning.ChargeTimeMs *= 2f;
		// UFO fire chance HALVED = arm delay doubled (Medium 9500 -> 19000) — authored curve x2.
		tuning.ArmDelayMs *= 2f;
		// Mothership cadence: Medium ~half as often as Very_Hard (24000 vs 12000 ms), linear in m.
		//   -> Easy 31500 / Medium 24000 / Hard 18000 / Very_Hard 12000 / Inzane 6000.
		tuning.MothershipMs = 12000f + (1f - m) * 30000f;
		// Max plasma orbs the saucers keep on screen: Medium 2, 1 on Easy, up to 3 on Hard+.
		//   -> Easy 1 / Medium 2 / Hard 2 / Very_Hard 3 / Inzane 3.
		resolvedMaxPlasma = MathHelper.Clamp(1 + (int)Math.Round((m - 0.35f) / 0.85f * 2.0, MidpointRounding.AwayFromZero), 1, 3);
		// Mothership variety unlocks with difficulty: Easy/Medium fire ONLY the off-centre 35%/65%
		// vertical columns; Hard adds the centre column; Very_Hard+ adds the horizontal sweep.
		mothershipAllowCenter = m >= 0.8f;
		mothershipAllowHorizontal = m >= 1.0f;
		// ----------------------------------------------------------------------------------
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
			SpawnSaucers(gameTime);
			TestPlayerTouchesSaucers();     // GOOD collision: instant (the player WANTS to hit saucers)
			TestPlasmaHitsPlayer(dt);       // BAD collisions: need LeewayMs of STEADY contact to count
			SpawnMines(gameTime);
			TestMinesHitPlayer(dt);
			SpawnMothership(gameTime);
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

	private void SpawnSaucers(GameTime gameTime)
	{
		if (won)
		{
			return;
		}
		// fill straight to the tier's ceiling — no kill-ramp, keep it busy from the start
		if (ufos.Count >= tuning.MaxSaucers)
		{
			return;
		}
		// Random Poisson spawn averaging SpawnIntervalMs between saucers (rate = 1000/ms in
		// hits/sec), not a fixed timer — so the gaps vary around the mean.
		if (!RandomHelper.RandomFromAverage(1000f / tuning.SpawnIntervalMs, gameTime))
		{
			return;
		}
		WebcamUfo webcamUfo = WebcamUfo.NewWebcamUfo(Collection, base.Game);
		// Arm/charge cadence stays a per-tier authored absolute ms + a small +/-jitter for
		// variety (NO difficulty-modifier divisor, no within-run ramp — the authored value IS
		// the delay). Tune per tier live via the ?wctune panel or ?wcarm/?wccharge.
		float armDelay = tuning.ArmDelayMs * CadenceJitter();
		float blinkTime = tuning.ChargeTimeMs * CadenceJitter();
		webcamUfo.Setup(RandomEdgePosition(), armDelay, blinkTime, tuning.SaucerSpeedMul);
		webcamUfo.OnFired += ufo_OnFired;
		webcamUfo.CanFire = BelowPlasmaCap;   // soft "N plasma balls at a time" cap (resolvedMaxPlasma; see WebcamUfo)
		Collection.Add((GameComponent)(object)webcamUfo);
		ufos.Add(webcamUfo);
	}

	// True while the field has fewer than the tier's plasma cap (resolvedMaxPlasma) of live orbs
	// — the gate WebcamUfo checks before firing, so no more than that many balls are out at once
	// (an about-to-fire saucer holds its charge while the field is at the cap). Counts non-dead
	// balls (one that died this tick may linger in `plasmas` until the next Prune).
	private bool BelowPlasmaCap()
	{
		int alive = 0;
		foreach (WebcamPlasma plasma in plasmas)
		{
			if (!plasma.IsDead)
			{
				alive++;
			}
		}
		return alive < resolvedMaxPlasma;
	}

	// Both saucers AND mines enter only from the TOP, LEFT, or RIGHT — never the bottom — and when
	// they come from a SIDE they enter in the TOP 40% of the screen (y in [60, 240]). So a hazard
	// always drifts in from above the player rather than creeping up from underneath, and its
	// wander (which flows around the player) starts from a readable, high-up position.
	private const float SpawnSideMaxY = 600f * 0.4f;   // 240: bottom of the top 40% band

	private static Vector2 RandomEdgePosition()
	{
		switch (RandomHelper.Random.Next(3))
		{
		case 0:
			return new Vector2(-40f, RandomHelper.RandomNextFloat(60f, SpawnSideMaxY));   // left, top 40%
		case 1:
			return new Vector2(840f, RandomHelper.RandomNextFloat(60f, SpawnSideMaxY));   // right, top 40%
		default:
			return new Vector2(RandomHelper.RandomNextFloat(60f, 740f), -40f);            // top
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
	private void SpawnMines(GameTime gameTime)
	{
		if (won || mines.Count >= tuning.MaxMines)
		{
			return;
		}
		// Random Poisson spawn averaging MineSpawnMs between mines (rate = 1000/ms in hits/sec).
		if (!RandomHelper.RandomFromAverage(1000f / tuning.MineSpawnMs, gameTime))
		{
			return;
		}
		WebcamMine mine = WebcamMine.NewWebcamMine(Collection, base.Game);
		mine.Setup(RandomEdgePosition(), tuning.MineLifeMs * CadenceJitter());
		Collection.Add((GameComponent)(object)mine);
		mines.Add(mine);
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

	// F1: launch a screen-bisecting mothership on a random Poisson roll averaging tuning.MothershipMs
	// between spawns (0 disables), never more than one at a time. Rate = 1/MothershipMs converted to
	// hits/sec for RandomFromAverage; while one is alive we don't roll, so the average gap is measured
	// from when the field is clear.
	private void SpawnMothership(GameTime gameTime)
	{
		if (won || tuning.MothershipMs <= 0f || motherships.Count > 0)
		{
			return;
		}
		// hits/sec = 1000 / MothershipMs (ms) — e.g. 12000ms -> 0.0833/s -> ~12s average wait.
		if (!RandomHelper.RandomFromAverage(1000f / tuning.MothershipMs, gameTime))
		{
			return;
		}
		WebcamMothership ship = WebcamMothership.NewWebcamMothership(Collection, base.Game);
		ship.Setup(PickBisectOrientation(), mothershipAllowCenter);
		Collection.Add((GameComponent)(object)ship);
		motherships.Add(ship);
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
		// Horizontal cross-screen sweeps only unlock on the harder tiers (mothershipAllowHorizontal,
		// set in ResolveTuning); Easy/Medium/Hard get vertical-only motherships.
		if (!mothershipAllowHorizontal)
		{
			return WebcamMothership.Bisect.VerticalDown;
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
