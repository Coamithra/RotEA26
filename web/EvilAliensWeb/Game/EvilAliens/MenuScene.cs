using System;
using System.Collections.Generic;
using BloomPostprocess;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class MenuScene : Scene
{
	private enum NextState
	{
		StartLevel,
		StartPreview,
		StartPreviewForced
	}

	public delegate void FinishedHandler(object sender, ControlDevice starter, Levels selectedLevel);

	public delegate void PreviewSelectedEvent(object sender, bool showExplanation);

	public delegate void FullScreenHandler(object sender);

	public delegate void VSyncChange(object sender);

	public delegate void ResetSelected(object sender);

	public delegate void BragSelected(object sender);

	private const float fadespeed = 1.05f;

	private NextState nextState;

	private bool hidemainmenu;

	private Oracle oracle;

	private ControlDevice starter;

	private MenuState state;

	private Levels selectedLevel;

	private float currentFade;

	private TimeSpan timer;

	private TimeSpan fadestarted;

	private Texture2D stargfx;

	private float currentBackdropSize;

	private float originalBackdropSize;

	// Ceiling for the menu backdrop's slow zoom-in: it eases toward this multiple of the
	// fitted size and tapers to a stop, rather than growing forever (see Update).
	private const float BackdropZoomCap = 2f;

	private Texture2D backdrop;

	private Texture2D blankTexture;

	private Texture2D hudring;

	private Texture2D vignette;

	// HUD ring "autofocus hunt": instead of a steady spin, the reticle darts to a new
	// angle, holds, twitches, and occasionally reverses or sweeps — like a camera lens
	// hunting for focus. State advanced in UpdateRing(), consumed in DrawHudDecor().
	private float ringAngle;

	private float ringFrom;

	private float ringTo;

	private double ringMoveStart;

	private double ringMoveDur = 0.0001;

	private double ringHoldUntil;

	private bool ringHolding = true;

	private float ringDirAccumDeg;

	private float ringDrift;

	private float ringDriftVel;

	// Ambient-coast guard rails. ringDrift is a free-running integrator (ringDrift +=
	// ringDriftVel * dt) and ringDriftVel is (re)seeded from each dart's ~5% momentum;
	// left unbounded/undamped the coast could run past the intended slow "hunt" feel and
	// the accumulator grow without limit over a long menu idle. RingDriftVelMax hard-caps
	// the coast rate no matter the dart params; RingDriftVelDecay bleeds inherited momentum
	// toward 0 each frame so the coast is a brief drift, not a permanent spin; and ringDrift
	// is wrapped to (-pi, pi] (rotation is mod-2pi identical) so it can't creep unbounded.
	private const float RingDriftVelMax = 0.12f;   // rad/s — a touch above the fastest authored seed

	private const float RingDriftVelDecay = 0.6f;  // per-second exponential bleed of coast momentum

	// HUD ring centre "recalibration": the ring re-centres on whichever menu is active
	// (main vs. a submenu). OnComponentAdded sets ringTargetMenu when a menu is shown;
	// the centre then eases (with overshoot) from where it was to the new menu's centre.
	private MenuSub1 ringTargetMenu;

	private Vector2 ringCentre = new Vector2(400f, 403f);

	private Vector2 ringCentreFrom;

	private Vector2 ringCentreTo;

	private double ringCentreTweenStart;

	private double ringCentreTweenDur = 0.5;

	private bool ringCentreTweening;

	private bool ringCentreInit;

	private ContentManager content;

	private List<Star> stars = new List<Star>();

	private List<Star> idleStars = new List<Star>();

	private RenderTarget2D myRenderTarget;

	private MenuSubWithSkull mainMenu;

	private SubMenuLevelChoice challengeSelector;

	// Online co-op lobby state (card 11.4). netMode = the menu flow is inside the
	// Online Co-op path (reroutes the shared selectors' exits, locks the launch to the
	// session); netNoticeUp = the status panel is showing a session-ending notice
	// ("player left", "update required") and waits for an acknowledge.
	private MenuSub1 netMenu;

	private NetStatusMenu netStatusMenu;

	private MenuSub1 netPickMenu;

	// Public game browser (card 2001fbd8): the "Join Online Game" carousel. browsingGames =
	// the carousel is up and driving NetGameBrowser (before any game is picked).
	private SubMenuOnlineGames onlineGamesMenu;

	private bool browsingGames;

	private bool netModeFlag;

	// Property, not a bare field: the level carousel needs the same value to decide whether to
	// explain the WebcamAliens refusal, and there are several assignment sites -- pushing it
	// here is what keeps the refusal and its explanation from ever disagreeing.
	private bool netMode
	{
		get => netModeFlag;
		set
		{
			netModeFlag = value;
			if (challengeSelector != null)
			{
				challengeSelector.NetMode = value;
			}
		}
	}

	private bool netStatusShown;

	private bool netNoticeUp;

	private SubMenuLevelChoice levelSelector;

	private SubMenuAwardments awardmentsMenu;

	private SubMenuAwardmentText awardmentTextMenu;

	private MenuSub1 optionsMenu;

	private MenuSub1 cheatsMenu;

	private MenuSub1 colorsMenu;

	private MenuSub1 confirmationMenu;

	private MenuSub1 playerSettingsMenu;

	private MenuSub1 trailerMenu;

	private DifficultyMenu difficultyMenu;

	private GammaMenu gammaMenu;

	private TrailerScene trailerScene;

	private MenuSub1 difficultyCaller;

	private MousePointer _cursor;

	private SpriteFont font;

	private Texture2D AButton;

	private Texture2D BButton;

	private List<Levels> levelsValues = Game1.GetEnumValues<Levels>();

	private Vector2 origin => new Vector2(400f, 300f);

	public event FinishedHandler OnFinished;

	public event PreviewSelectedEvent OnPreviewSelected;

	public event FullScreenHandler OnFullScreen;

	public event VSyncChange OnVSyncChange;

	public event ResetSelected OnResetSelected;

	public event BragSelected OnBragSelected;

	public MenuScene(Game game)
		: base(game)
	{
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		oracle = ServiceHelper.Get<IOracleService>().Oracle;
		_cursor = ServiceHelper.Get<IMousePointerService>().MousePointer;
		mainMenu = new MenuSubWithSkull(base.Game);
		if (General.IsTrial)
		{
			mainMenu.AddEntry("View Trailer");
			mainMenu.AddEntryEvent(mainMenu_PreviewSelected);
		}
		mainMenu.AddEntry("Start");
		mainMenu.AddEntryEvent(mainMenu_StartSelected);
		mainMenu.AddEntry("Options");
		mainMenu.AddEntryEvent(mainMenu_OptionsSelected);
		mainMenu.AddEntry("Tutorial");
		mainMenu.AddEntryEvent(mainMenu_TutorialSelected);
		mainMenu.AddEntry("Challenges", Unlockables.Items.Challenges);
		mainMenu.AddEntryEvent(mainMenu_ChallengesSelected);
		// Web-port addition (card 11.4), deliberately UNGATED: menu-driven online co-op
		// (host shows a room code, friend enters it; see Compat/Net/NetLobby).
		mainMenu.AddEntry("Online Co-op");
		mainMenu.AddEntryEvent(mainMenu_OnlineSelected);
		mainMenu.AddEntry("Awardments", Unlockables.Items.Awardments);
		mainMenu.AddEntryEvent(mainMenu_AwardmentsSelected);
		mainMenu.AddEntry("Cheats", Unlockables.Items.Cheats);
		mainMenu.AddEntryEvent(mainMenu_CheatsSelected);
		// Debug (?noattract): leave the idle timeout unwired so the menu never drops into
		// a random demo while you're testing it. Normal boot keeps the attract demo.
		if (!DebugFlags.NoAttract)
		{
			mainMenu.OnTimeOut += mainMenu_DemoSelected;
		}
		mainMenu.AddEntry("Exit");
		mainMenu.AddEntryEvent(mainMenu_ExitSelected);
		mainMenu.OnExit += mainMenu_OnExit;
		levelSelector = new SubMenuLevelChoice(base.Game);
		levelSelector.OnExit += levelSelector_OnExit;
		levelSelector.AddEntry("Mission 1");
		levelSelector.AddEntryData("The Evil Aliens must be repelled!", Levels.Level1);
		levelSelector.AddEntryEvent(levelSelector_levelSelected);
		levelSelector.AddEntry("Mission 2", Unlockables.Items.Level2);
		levelSelector.AddEntryData("Mars Attacks!", Levels.Level2);
		levelSelector.AddEntryEvent(levelSelector_levelSelected);
		levelSelector.AddEntry("Mission 3", Unlockables.Items.Level3);
		levelSelector.AddEntryData("Invade the Alien base!", Levels.Level3);
		levelSelector.AddEntryEvent(levelSelector_levelSelected);
		confirmationMenu = new ConfirmationMenu(base.Game, "Are you sure?\nThis will erase all progress..");
		confirmationMenu.AddEntry("Yes");
		confirmationMenu.AddEntryEvent(confirmationMenu_YesSelected);
		confirmationMenu.AddEntry("No");
		confirmationMenu.AddEntryEvent(confirmationMenu_NoSelected);
		confirmationMenu.OnExit += confirmationMenu_NoSelected;
		challengeSelector = new SubMenuLevelChoice(base.Game);
		challengeSelector.OnExit += challengeSelector_OnExit;
		challengeSelector.AddEntry("Space Dodge!", Unlockables.Items.SpaceDodge);
		challengeSelector.AddEntryData("Move fast and dodge the oncoming asteroids!", Levels.SpaceDodge);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Braineroids", Unlockables.Items.Braineroids);
		challengeSelector.AddEntryData("What the arcade classic could have looked like..", Levels.Braineroids);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Evil Aliens Classic", Unlockables.Items.ClassicAliens);
		challengeSelector.AddEntryData("Can you beat the game that started it all?", Levels.ClassicAliens);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Paratrooper", Unlockables.Items.Paratrooper);
		challengeSelector.AddEntryData("Paratrooper!", Levels.Paratrooper);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Base Pressure", Unlockables.Items.OwnLevel);
		challengeSelector.AddEntryData("Can you manoeuvre through the narrow passageways?", Levels.OwnLevel);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Crazy Game", Unlockables.Items.CrazyGame);
		challengeSelector.AddEntryData("The year is 2501. Your planet has just been overrun by an\nevil alien force known only as The Dots.\nYou managed to escape the fate of your planet by hopping\ninto your shuttle and blasting off, but The Dots are right\non your tail!\nHow long can you last before they destroy you too?", Levels.CrazyGame);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Boss Train", Unlockables.Items.BossTrain);
		challengeSelector.AddEntryData("Defeat the Alien bosses for great victory", Levels.InsaneBossI);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Team Challenge", Unlockables.Items.TeamChallenge);
		// The "two players" line is the 2008 briefing; the second sentence is the web port's
		// (card e6927ef8). The partner seat resolves to the first CONNECTED gamepad, or to an
		// auto-pilot AI partner when there is none -- said HERE because this is where the player
		// decides to launch, and because an in-level banner has nowhere safe to live (one added
		// during Startup is eaten by UpdateStartup's 1300ms Purge<AnimatedMessage>, one added in
		// Normal collides with the script's own "Get ready!" beat).
		challengeSelector.AddEntryData("Fly the new MX2 Dual Pilot Vessel to victory!\nRequires two players -- plug in a gamepad for player two,\nor an auto-pilot partner takes the second seat.", Levels.TeamChallenge);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		// Web-port addition, deliberately UNGATED (no Unlockables item): the remake of
		// the 2004 webcam game the "I made this!" splash is from. The screenshot is the
		// meme's embedded mini-screenshot (tools/webcam/build_webcam_assets.py).
		challengeSelector.AddEntry("I Made This!");
		challengeSelector.AddEntryData("The legendary 2004 webcam game, remade. YOU are the ship:\nyour camera puts you in the starfield. Swat the saucers with\nyour body before they blink, aim... and FIRE.\nRequires a webcam", Levels.WebcamAliens);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		cheatsMenu = new MenuSub1(base.Game);
		cheatsMenu.AddEntry("Infinite Lives: " + boolToGameString(Settings.GetInstance().InfiniteLives), Unlockables.Items.InfiniteLives);
		cheatsMenu.AddEntryEvent(cheatsMenu_InfiniteLivesSelected);
		cheatsMenu.AddEntry("Power Up: " + boolToGameString(Settings.GetInstance().PowerUp), Unlockables.Items.PowerUp);
		cheatsMenu.AddEntryEvent(cheatsMenu_PowerUpSelected);
		cheatsMenu.AddEntry("Turbo: " + Settings.GetInstance().Turbo + "%", Unlockables.Items.Turbo);
		cheatsMenu.AddEntryEvent(cheatsMenu_TurboSelected);
		cheatsMenu.AddEntry("Mechanical Friends: " + Settings.GetInstance().Friends, Unlockables.Items.Friends);
		cheatsMenu.AddEntryEvent(cheatsMenu_FriendsSelected);
		cheatsMenu.AddEntry("Disable All");
		cheatsMenu.AddEntryEvent(cheatsMenu_DisableAll);
		cheatsMenu.AddEntry("Back");
		cheatsMenu.AddEntryEvent(cheatsMenu_OnExit);
		cheatsMenu.OnExit += cheatsMenu_OnExit;
		optionsMenu = new MenuSub1(base.Game);
		optionsMenu.AddEntry("Music: " + boolToGameString(Settings.GetInstance().PlayMusic));
		optionsMenu.AddEntryEvent(optionsMenu_MusicSelected);
		// Opt-in: capture a level-select thumbnail of the webcam challenge (it contains
		// the player's camera image, so it's off by default). See General.ScreenshotEnabled.
		optionsMenu.AddEntry("Webcam Screenshots: " + boolToGameString(Settings.GetInstance().WebcamScreenshot));
		optionsMenu.AddEntryEvent(optionsMenu_WebcamScreenshotSelected);
		// Card 2001fbd8: while ON (default), an eligible game is listed online so strangers can
		// join it (Compat/Net/NetListing). Easy to find + the pause menu shows the listed state.
		optionsMenu.AddEntry("Allow Online Joins: " + boolToGameString(Settings.GetInstance().AllowOnlineJoins));
		optionsMenu.AddEntryEvent(optionsMenu_AllowOnlineJoinsSelected);
		// Reticle render mode: On = the reticle IS the OS cursor (zero-lag hardware); Off = the
		// reticle is a sprite drawn in-game following the mouse. There's a reticle either way.
		// See MousePointer / Settings.HWMouse. (Handler: optionsMenu_HWMouseSelected.)
		optionsMenu.AddEntry("Hardware Mouse: " + boolToGameString(Settings.GetInstance().HWMouse));
		optionsMenu.AddEntryEvent(optionsMenu_HWMouseSelected);
		if (GraphicsAdapter.DefaultAdapter.IsWideScreen)
		{
			optionsMenu.AddEntry("Stretch Screen: " + boolToGameString(Settings.GetInstance().Stretch));
			optionsMenu.AddEntryEvent(optionsMenu_StretchSelected);
		}
		optionsMenu.AddEntry("Reset All Progress");
		optionsMenu.AddEntryEvent(optionsMenu_LockAllSelected);
		// "Modify Screen Size" (the old XBLIG TV-safe-area slider, Settings.Scale) removed --
		// resolution is browser-driven since the Stage-10 unified presenter (RenderScale), so
		// the option no longer did anything (Settings.Scale is set once in Game1.LoadContent
		// for narrow displays and never read by any draw path). Settings.Scale itself is left
		// in place (XML-serialized, appended fields must not be removed) — just unreachable
		// from the menu now. Trello card 993db245.
		optionsMenu.AddEntry("Gamma Correction");
		optionsMenu.AddEntryEvent(optionsMenu_GammaCorrectionSelected);
		playerSettingsMenu = new PlayerSettingsMenu(game, darken: true);
		optionsMenu.AddEntry("Controller Settings");
		optionsMenu.AddEntryEvent(optionsMenu_PlayerOptionsSelected);
		playerSettingsMenu.OnExit += playerSettingsMenu_OnExit;
		// "Trailers" plays the two 2008 promo videos. The original VFX/*.wmv (VC-1) can't play
		// in a browser and the web port has no video loader, so the trailer menu now hands off
		// to an embedded YouTube player (Compat/TrailerInterop -> eaTrailer) instead of the dead
		// video TrailerScene — no Content.Load<Video>("VFX/..") (the old crash path, Stage 14).
		optionsMenu.AddEntry("Trailers");
		optionsMenu.AddEntryEvent(optionsMenu_OnTrailersSelected);
		optionsMenu.AddEntry("Back");
		optionsMenu.AddEntryEvent(optionsMenu_OnExit);
		optionsMenu.OnExit += optionsMenu_OnExit;
		colorsMenu = new MenuSub1(base.Game);
		colorsMenu.AddEntry("P1: " + (PlayerColors)oracle.Hue(0));
		colorsMenu.AddEntryEvent(colorsMenu_P1Selected);
		colorsMenu.AddEntry("P2: " + (PlayerColors)oracle.Hue(1));
		colorsMenu.AddEntryEvent(colorsMenu_P2Selected);
		colorsMenu.AddEntry("P3: " + (PlayerColors)oracle.Hue(2));
		colorsMenu.AddEntryEvent(colorsMenu_P3Selected);
		colorsMenu.AddEntry("P4: " + (PlayerColors)oracle.Hue(3));
		colorsMenu.AddEntryEvent(colorsMenu_P4Selected);
		colorsMenu.AddEntry("Back");
		colorsMenu.AddEntryEvent(colorsMenu_OnExit);
		colorsMenu.OnExit += colorsMenu_OnExit;
		difficultyMenu = new DifficultyMenu(base.Game);
		difficultyMenu.OnExit += difficultyMenu_OnExit;
		difficultyMenu.OnDifficultySelected += difficultyMenu_difficultySelected;
		gammaMenu = new GammaMenu(game);
		gammaMenu.OnFinished += gammaMenu_OnFinished;
		awardmentsMenu = new SubMenuAwardments(game);
		List<Awardment> enumValues = Game1.GetEnumValues<Awardment>();
		AwardmentBlade awardmentBlade = ServiceHelper.Get<IAwardmentBladeService>().get();
		foreach (Awardment item in enumValues)
		{
			awardmentsMenu.AddEntry(awardmentBlade.AwardmentName(item));
			awardmentsMenu.AddEntryEvent(awardmentsMenu_awardmentSelected);
		}
		awardmentsMenu.OnExit += awardmentsMenu_OnExit;
		awardmentTextMenu = new SubMenuAwardmentText(game);
		awardmentTextMenu.OnExit += awardmentTextMenu_OnExit;
		trailerMenu = new MenuSub1(base.Game);
		trailerMenu.AddEntry("Revenge of the Evil Aliens (2008)");
		trailerMenu.AddEntryEvent(trailerMenu_EvilAliensSelected);
		trailerMenu.AddEntry("Rocket Riot (2009)");
		trailerMenu.AddEntryEvent(trailerMenu_RocketRiotSelected);
		trailerMenu.AddEntry("Back");
		trailerMenu.AddEntryEvent(trailerMenu_BackSelected);
		trailerMenu.OnExit += trailerMenu_BackSelected;
		trailerScene = new TrailerScene(base.Game);
		trailerScene.OnFinished += trailerScene_OnFinished;
		// Online co-op lobby (card 11.4): Host/Join submenu, the phase/status panel, and
		// the post-connect Missions/Challenges picker (host side). The join side's code
		// entry is an HTML overlay (eaRtc.promptCode) -- real text input, house pattern.
		netMenu = new MenuSub1(base.Game);
		netMenu.AddEntry("Host Game");
		netMenu.AddEntryEvent(netMenu_HostSelected);
		netMenu.AddEntry("Join by Code");
		netMenu.AddEntryEvent(netMenu_JoinSelected);
		// Card 2001fbd8: browse + join OPEN games without needing a code over a call.
		netMenu.AddEntry("Join Online Game");
		netMenu.AddEntryEvent(netMenu_JoinOnlineSelected);
		netMenu.AddEntry("Back");
		netMenu.AddEntryEvent(netMenu_BackSelected);
		netMenu.OnExit += netMenu_BackSelected;
		onlineGamesMenu = new SubMenuOnlineGames(base.Game);
		onlineGamesMenu.OnGameSelected += onlineGames_GameSelected;
		onlineGamesMenu.OnExit += onlineGames_BackSelected;
		netStatusMenu = new NetStatusMenu(base.Game, "");
		netStatusMenu.AddEntry("Cancel");
		netStatusMenu.AddEntryEvent(netStatus_CancelSelected);
		netStatusMenu.OnExit += netStatus_CancelSelected;
		netPickMenu = new MenuSub1(base.Game);
		netPickMenu.AddEntry("Missions");
		netPickMenu.AddEntryEvent(netPick_MissionsSelected);
		netPickMenu.AddEntry("Challenges");
		netPickMenu.AddEntryEvent(netPick_ChallengesSelected);
		netPickMenu.AddEntry("Cancel");
		netPickMenu.AddEntryEvent(netPick_CancelSelected);
		netPickMenu.OnExit += netPick_CancelSelected;
		base.DrawOrder = 1;
	}

	// Trailers play in an embedded YouTube overlay (Compat/TrailerInterop -> eaTrailer in
	// index.html), NOT the dead video TrailerScene — the original VFX/*.wmv can't play in a
	// browser. The trailerMenu stays shown underneath the full-screen overlay, so closing it
	// (Back/Esc, JS-owned) returns here. YouTube ids map TrailerScene.TrailerMode 1:1.
	private void trailerMenu_RocketRiotSelected(object sender)
	{
		EvilAliensWeb.Compat.TrailerInterop.Play("4zN0h1xmwF8");
	}

	private void trailerMenu_EvilAliensSelected(object sender)
	{
		EvilAliensWeb.Compat.TrailerInterop.Play("v732YJ4wHjc");
	}

	private void trailerMenu_BackSelected(object sender)
	{
		trailerMenu.Remove();
		optionsMenu.Show();
	}

	private void trailerScene_OnFinished(object sender)
	{
		Collection.Remove((GameComponent)(object)trailerScene);
		trailerMenu.Show();
	}

	private void gammaMenu_OnFinished(object sender)
	{
		Settings.GetInstance().SaveThreaded();
		Collection.Remove((GameComponent)(object)gammaMenu);
		base.Visible = true;
		base.Enabled = true;
		((DrawableGameComponent)optionsMenu).Visible = true;
		((GameComponent)optionsMenu).Enabled = true;
	}

	private void awardmentTextMenu_OnExit(MenuSub1 sender)
	{
		awardmentTextMenu.Remove();
		awardmentsMenu.Show();
	}

	private void awardmentsMenu_awardmentSelected(MenuSub1 sender)
	{
		awardmentTextMenu.SetAwardment((Awardment)sender.GetSelectedEntry);
		awardmentsMenu.Remove();
		awardmentTextMenu.Show();
	}

	private void awardmentsMenu_OnExit(MenuSub1 sender)
	{
		awardmentsMenu.Remove();
		mainMenu.Show();
	}

	private void playerSettingsMenu_OnExit(MenuSub1 sender)
	{
		optionsMenu.Show();
		playerSettingsMenu.Remove();
	}

	private void confirmationMenu_YesSelected(MenuSub1 sender)
	{
		// SaveIgnoringSuppression, not SaveNoThread: under ?unlockall those two savables refuse
		// to save (card 36db5d75), which would make this reset half-apply -- screenshots really
		// deleted, Settings written, but every unlock resurrecting on the next reload. Erasing
		// to a clean slate is the one direction suppression must not block.
		Achievements.GetInstance().Reset();
		Achievements.GetInstance().SaveIgnoringSuppression();
		Settings.GetInstance().DisableCheats();
		Settings.GetInstance().SaveNoThread();
		Unlockables.GetInstance().Reset();
		Unlockables.GetInstance().SaveIgnoringSuppression();
		ScreenshotSaver.DeleteScreenshots();
		confirmationMenu.Remove();
		optionsMenu.Show();
	}

	private void confirmationMenu_NoSelected(MenuSub1 sender)
	{
		confirmationMenu.Remove();
		optionsMenu.Show();
	}

	private void cheatsMenu_OnExit(MenuSub1 sender)
	{
		Settings.GetInstance().SaveThreaded();
		mainMenu.Show();
		sender.Remove();
	}

	private void cheatsMenu_DisableAll(MenuSub1 sender)
	{
		Settings.GetInstance().DisableCheats();
		cheatsMenu.SetEntry(0, "Infinite Lives: " + boolToGameString(Settings.GetInstance().InfiniteLives));
		cheatsMenu.SetEntry(1, "Power Up: " + boolToGameString(Settings.GetInstance().PowerUp));
		cheatsMenu.SetEntry(2, "Turbo: " + Settings.GetInstance().Turbo + "%");
		cheatsMenu.SetEntry(3, "Mechanical Friends: " + Settings.GetInstance().Friends);
	}

	private void cheatsMenu_PowerUpSelected(MenuSub1 sender)
	{
		Settings.GetInstance().PowerUp = !Settings.GetInstance().PowerUp;
		sender.SetEntry("Power Up: " + boolToGameString(Settings.GetInstance().PowerUp));
	}

	private void cheatsMenu_ConnectorSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Connector = !Settings.GetInstance().Connector;
		sender.SetEntry("Multiplayer Joined: " + boolToGameString(Settings.GetInstance().Connector));
	}

	private void cheatsMenu_GalagaModeSelected(MenuSub1 sender)
	{
		Settings.GetInstance().GalagaMode = !Settings.GetInstance().GalagaMode;
		sender.SetEntry("Galaga Mode: " + boolToGameString(Settings.GetInstance().GalagaMode));
	}

	private void cheatsMenu_InfiniteLivesSelected(MenuSub1 sender)
	{
		Settings.GetInstance().InfiniteLives = !Settings.GetInstance().InfiniteLives;
		sender.SetEntry("Infinite Lives: " + boolToGameString(Settings.GetInstance().InfiniteLives));
	}

	private void cheatsMenu_TurboSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Turbo = Settings.GetInstance().Turbo + 10;
		if (Settings.GetInstance().Turbo > 200)
		{
			Settings.GetInstance().Turbo = 50;
		}
		sender.SetEntry("Turbo: " + Settings.GetInstance().Turbo + "%");
	}

	private void cheatsMenu_FriendsSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Friends++;
		if (Settings.GetInstance().Friends > 3)
		{
			Settings.GetInstance().Friends = 0;
		}
		sender.SetEntry("Mechanical Friends: " + Settings.GetInstance().Friends);
	}

	private void difficultyMenu_OnExit(MenuSub1 sender)
	{
		difficultyCaller.Show();
		difficultyMenu.Remove();
	}

	private void difficultyMenu_difficultySelected(MenuSub1 sender)
	{
		Settings.GetInstance().SetDifficultyTo((Settings.DifficultyLevel)sender.GetSelectedEntry);
		Settings.GetInstance().SaveThreaded();
		// Online co-op host: the pick is final here -- replicate it so the client
		// mirrors the launch (locked level + difficulty). A session that died mid-pick
		// aborts the launch; the NetUpdate notice poll redraws the flow.
		if (netMode)
		{
			if (!EvilAliensWeb.Compat.Net.NetSession.IsHost || !EvilAliensWeb.Compat.Net.NetSession.PeerUp)
			{
				sender.Remove();
				return;
			}
			EvilAliensWeb.Compat.Net.NetSession.SendLaunch(selectedLevel, Settings.GetInstance().CurrentDifficulty);
		}
		fadestarted = timer;
		currentFade = 0f;
		state = MenuState.FadeToGame;
		nextState = NextState.StartLevel;
		if (base.InputHandler.Pressed(MyKeys.Enter))
		{
			starter = ControlDevice.Keyboard;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 0) || base.InputHandler.PadPressed(PadKeys.A, 0))
		{
			starter = ControlDevice.PadOne;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 1) || base.InputHandler.PadPressed(PadKeys.A, 1))
		{
			starter = ControlDevice.PadTwo;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 2) || base.InputHandler.PadPressed(PadKeys.A, 2))
		{
			starter = ControlDevice.PadThree;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 3) || base.InputHandler.PadPressed(PadKeys.A, 3))
		{
			starter = ControlDevice.PadFour;
		}
		else if (base.InputHandler.Pressed(MyKeys.Generic_Start))
		{
			starter = ControlDevice.Generic;
		}
		else
		{
			// Mouse-click activation (Stage 13 made every MenuSub1 entry clickable) presses
			// none of the device keys; on web the mouse is the keyboard player, so default to
			// Keyboard instead of leaving starter at the enum default (PadOne), which would
			// bind the level to a nonexistent gamepad (instant pause loop).
			starter = ControlDevice.Keyboard;
		}
		sender.Remove();
	}

	private void colorsMenu_OnExit(MenuSub1 sender)
	{
		optionsMenu.Show();
		colorsMenu.Remove();
	}

	private PlayerColors changeColor(int i)
	{
		PlayerColors playerColors = (PlayerColors)(int)oracle.Hue(i) switch
		{
			PlayerColors.Red => PlayerColors.Pink, 
			PlayerColors.Pink => PlayerColors.Blue, 
			PlayerColors.Blue => PlayerColors.Purple, 
			PlayerColors.Purple => PlayerColors.Gold, 
			PlayerColors.Gold => PlayerColors.Lime, 
			PlayerColors.Lime => PlayerColors.Red, 
			_ => PlayerColors.Blue, 
		};
		oracle.SetHue((float)playerColors, i);
		return playerColors;
	}

	private void colorsMenu_P1Selected(MenuSub1 sender)
	{
		sender.SetEntry("P1: " + changeColor(0));
	}

	private void colorsMenu_P2Selected(MenuSub1 sender)
	{
		sender.SetEntry("P2: " + changeColor(1));
	}

	private void colorsMenu_P3Selected(MenuSub1 sender)
	{
		sender.SetEntry("P3: " + changeColor(2));
	}

	private void colorsMenu_P4Selected(MenuSub1 sender)
	{
		sender.SetEntry("P4: " + changeColor(3));
	}

	private void optionsMenu_ColorsSelected(MenuSub1 sender)
	{
		colorsMenu.Show();
		optionsMenu.Remove();
	}

	private void optionsMenu_LockAllSelected(MenuSub1 sender)
	{
		confirmationMenu.Show();
		optionsMenu.Remove();
	}

	private void optionsMenu_UnlockAllSelected(MenuSub1 sender)
	{
		for (int i = 0; i < Unlockables.GetInstance().Collection.Count; i++)
		{
			Unlockables.GetInstance().Collection[(Unlockables.Items)i] = true;
		}
		Unlockables.GetInstance().SaveThreaded();
	}

	private void optionsMenu_HaxSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Invulnerability = !Settings.GetInstance().Invulnerability;
		sender.SetEntry("Invulnerability: " + boolToGameString(Settings.GetInstance().Invulnerability));
	}

	private void optionsMenu_AdaptiveDifficultySelected(MenuSub1 sender)
	{
		Settings.GetInstance().AdaptiveDifficulty = !Settings.GetInstance().AdaptiveDifficulty;
		sender.SetEntry("Adaptive Difficulty: " + boolToGameString(Settings.GetInstance().AdaptiveDifficulty));
	}

	private void optionsMenu_ToonShaderSelected(MenuSub1 sender)
	{
		Settings.GetInstance().ToonShader = !Settings.GetInstance().ToonShader;
		sender.SetEntry("Toon Shading: " + boolToGameString(Settings.GetInstance().ToonShader));
	}

	private void optionsMenu_HWMouseSelected(MenuSub1 sender)
	{
		Settings.GetInstance().HWMouse = !Settings.GetInstance().HWMouse;
		sender.SetEntry("Hardware Mouse: " + boolToGameString(Settings.GetInstance().HWMouse));
	}

	private void optionsMenu_VSyncSelected(MenuSub1 sender)
	{
		Settings.GetInstance().VSync = !Settings.GetInstance().VSync;
		sender.SetEntry("Vertical Sync: " + boolToGameString(Settings.GetInstance().VSync));
		if (this.OnVSyncChange != null)
		{
			this.OnVSyncChange(this);
		}
	}

	private void optionsMenu_BloomSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Bloom = !Settings.GetInstance().Bloom;
		Settings.GetInstance().Interpolate = Settings.GetInstance().Bloom;
		sender.SetEntry("Fancy GFX: " + boolToGameString(Settings.GetInstance().Bloom));
		((DrawableGameComponent)ServiceHelper.Get<IBloomService>().BloomComponent).Visible = Settings.GetInstance().Bloom;
	}

	private void optionsMenu_WebcamScreenshotSelected(MenuSub1 sender)
	{
		Settings.GetInstance().WebcamScreenshot = !Settings.GetInstance().WebcamScreenshot;
		sender.SetEntry("Webcam Screenshots: " + boolToGameString(Settings.GetInstance().WebcamScreenshot));
	}

	private void optionsMenu_AllowOnlineJoinsSelected(MenuSub1 sender)
	{
		Settings.GetInstance().AllowOnlineJoins = !Settings.GetInstance().AllowOnlineJoins;
		sender.SetEntry("Allow Online Joins: " + boolToGameString(Settings.GetInstance().AllowOnlineJoins));
	}

	private void optionsMenu_MusicSelected(MenuSub1 sender)
	{
		Settings.GetInstance().PlayMusic = !Settings.GetInstance().PlayMusic;
		sender.SetEntry("Music: " + boolToGameString(Settings.GetInstance().PlayMusic));
		if (Settings.GetInstance().PlayMusic)
		{
			base.SoundManager.PlayMusic(Songs.Sjaak);
		}
		else
		{
			base.SoundManager.StopMusic();
		}
	}

	private void optionsMenu_FullscreenSelected(MenuSub1 sender)
	{
		Settings.GetInstance().FullScreen = !Settings.GetInstance().FullScreen;
		this.OnFullScreen(this);
	}

	private void optionsMenu_SafeAreaSelected(MenuSub1 sender)
	{
		Settings.GetInstance().HideSafeArea = !Settings.GetInstance().HideSafeArea;
		sender.SetEntry("Hide Safe Area: " + boolToGameString(Settings.GetInstance().HideSafeArea));
	}

	private void optionsMenu_GammaCorrectionSelected(MenuSub1 sender)
	{
		Collection.Add((GameComponent)(object)gammaMenu);
		base.Visible = false;
		base.Enabled = false;
		((DrawableGameComponent)optionsMenu).Visible = false;
		((GameComponent)optionsMenu).Enabled = false;
	}

	private void optionsMenu_PlayerOptionsSelected(MenuSub1 sender)
	{
		ControlDevice controlDevice;
		if (base.InputHandler.Pressed(MyKeys.Enter))
		{
			controlDevice = ControlDevice.Keyboard;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 0) || base.InputHandler.PadPressed(PadKeys.A, 0))
		{
			controlDevice = ControlDevice.PadOne;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 1) || base.InputHandler.PadPressed(PadKeys.A, 1))
		{
			controlDevice = ControlDevice.PadTwo;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 2) || base.InputHandler.PadPressed(PadKeys.A, 2))
		{
			controlDevice = ControlDevice.PadThree;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 3) || base.InputHandler.PadPressed(PadKeys.A, 3))
		{
			controlDevice = ControlDevice.PadFour;
		}
		else
		{
			// Mouse-click activation (Stage 13 made every MenuSub1 entry clickable) presses
			// none of the device keys; on web the mouse is the keyboard player, so default to
			// Keyboard instead of throwing (the old NotSupportedException froze the whole tab).
			controlDevice = ControlDevice.Keyboard;
		}
		((PlayerSettingsMenu)playerSettingsMenu).Starter = controlDevice;
		playerSettingsMenu.Show();
		optionsMenu.Remove();
	}

	private void optionsMenu_StretchSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Stretch = !Settings.GetInstance().Stretch;
		sender.SetEntry("Stretch Screen: " + boolToGameString(Settings.GetInstance().Stretch));
	}

	private void optionsMenu_DifficultySelected(MenuSub1 sender)
	{
		int maxDifficulty = 1;
		if (Unlockables.GetInstance().Collection[Unlockables.Items.HarderDifficulties])
		{
			maxDifficulty = 3;
		}
		if (Unlockables.GetInstance().Collection[Unlockables.Items.InsaneDifficulty])
		{
			maxDifficulty = 4;
		}
		Settings.GetInstance().SetDifficultyTo((Settings.DifficultyLevel)MyMath.Mod((int)(Settings.GetInstance().CurrentDifficulty + 1), maxDifficulty + 1));
		sender.SetEntry("Difficulty: " + Settings.GetInstance().CurrentDifficulty.ToString().Replace("_", " "));
	}

	private void optionsMenu_OnExit(MenuSub1 sender)
	{
		optionsMenu.Remove();
		mainMenu.Show();
		Settings.GetInstance().SaveThreaded();
	}

	private void optionsMenu_OnTrailersSelected(MenuSub1 sender)
	{
		if (General.IsTrial)
		{
			nextState = NextState.StartPreview;
			fadestarted = timer;
			currentFade = 0f;
			state = MenuState.FadeToGame;
			optionsMenu.Remove();
			mainMenu.Remove();
		}
		else
		{
			optionsMenu.Remove();
			trailerMenu.Show();
		}
	}

	public static string boolToGameString(bool b)
	{
		if (b)
		{
			return "Enabled";
		}
		return "Disabled";
	}

	private void levelSelector_levelSelected(MenuSub1 sender)
	{
		selectedLevel = ((SubMenuLevelChoice)sender).GetSelectedLevel();
		difficultyCaller = sender;
		if (General.IsTrial && selectedLevel != Levels.Level1)
		{
			sender.Remove();
			mainMenu_PreviewSelected(sender);
			return;
		}
		difficultyMenu.Show();
		difficultyMenu.Level = selectedLevel;
		difficultyMenu.levelType = DifficultyMenu.LevelType.Regular;
		difficultyMenu.Reset();
		sender.Remove();
	}

	private void challengeSelector_levelSelected(MenuSub1 sender)
	{
		// Online co-op excludes the webcam challenge (the camera IS the controller and
		// the mask is wall-clock local -- see plans/stage11-online-coop.md). The entry
		// stays visible and unselectable; SubMenuLevelChoice.DrawCarouselOverlay swaps its
		// briefing for the reason, so this refusal is explained rather than silent.
		if (netMode && ((SubMenuLevelChoice)sender).GetSelectedLevel() == Levels.WebcamAliens)
		{
			return;
		}
		selectedLevel = ((SubMenuLevelChoice)sender).GetSelectedLevel();
		difficultyCaller = sender;
		if (General.IsTrial && selectedLevel != Levels.Level1)
		{
			sender.Remove();
			mainMenu_PreviewSelected(sender);
			return;
		}
		difficultyMenu.Show();
		difficultyMenu.Level = selectedLevel;
		difficultyMenu.levelType = DifficultyMenu.LevelType.Challenge;
		difficultyMenu.Reset();
		sender.Remove();
	}

	private void levelSelector_OnExit(MenuSub1 sender)
	{
		if (netMode)
		{
			netPickMenu.Show();
		}
		else
		{
			mainMenu.Show();
		}
		levelSelector.Remove();
	}

	private void challengeSelector_OnExit(MenuSub1 sender)
	{
		if (netMode)
		{
			netPickMenu.Show();
		}
		else
		{
			mainMenu.Show();
		}
		challengeSelector.Remove();
	}

	private void mainMenu_OnExit(MenuSub1 sender)
	{
		if (this.OnResetSelected != null)
		{
			this.OnResetSelected(this);
		}
	}

	private void mainMenu_DemoSelected(MenuSub1 sender)
	{
		fadestarted = timer;
		currentFade = 0f;
		state = MenuState.FadeToGame;
		nextState = NextState.StartLevel;
		starter = ControlDevice.AI;
		// Debug (?demo=1|2|3): pin the roll so one demo can be reached on demand -- capturing a
		// demo's preload gaps needs ONE demo per process (the content manager is shared), and a
		// committed probe cannot retry, so an unseeded roll made both a coin flip (card e63601a4).
		// Unset => the normal random pick, so a shipped build is unchanged.
		// The draw happens either way and its result is then DISCARDED: RandomHelper.Random is the
		// shared stream, so short-circuiting it would shift every later roll in a pinned run --
		// including UFO.MakeSmall's landed-sheet pick, which is exactly what the demos' manifest
		// entries hedge against. A pinned capture must sample the same stream a real launch does.
		int demoRoll = RandomHelper.Random.Next(3);
		if (DebugFlags.DemoPick.HasValue)
		{
			demoRoll = DebugFlags.DemoPick.Value - 1;
		}
		switch (demoRoll)
		{
		case 0:
			selectedLevel = Levels.Demo1;
			break;
		case 1:
			selectedLevel = Levels.Demo2;
			break;
		case 2:
			selectedLevel = Levels.Demo3;
			break;
		default:
			selectedLevel = Levels.ClassicAliens;
			break;
		}
		mainMenu.Remove();
	}

	private void mainMenu_StartSelected(MenuSub1 sender)
	{
		mainMenu.Remove();
		levelSelector.Show();
	}

	private void mainMenu_CheatsSelected(MenuSub1 sender)
	{
		cheatsMenu.Show();
		mainMenu.Remove();
	}

	private void mainMenu_ChallengesSelected(MenuSub1 sender)
	{
		mainMenu.Remove();
		challengeSelector.Show();
	}

	// ---- Online co-op lobby (card 11.4) ------------------------------------------------

	private void mainMenu_OnlineSelected(MenuSub1 sender)
	{
		netMode = true;
		mainMenu.Remove();
		netMenu.Show();
	}

	private void netMenu_HostSelected(MenuSub1 sender)
	{
		EvilAliensWeb.Compat.Net.NetLobby.HostGame(base.Game);
		netMenu.Remove();
		ShowNetStatus("Contacting server...");
	}

	private void netMenu_JoinSelected(MenuSub1 sender)
	{
		EvilAliensWeb.Compat.Net.NetLobby.JoinGame(base.Game);
		netMenu.Remove();
		ShowNetStatus("Enter the room code");
	}

	private void netMenu_BackSelected(MenuSub1 sender)
	{
		netMode = false;
		netMenu.Remove();
		mainMenu.Show();
	}

	// Card 2001fbd8: open the public game browser. NetGameBrowser opens the browse socket;
	// the carousel reads its list. NetUpdate ticks the browser and skips the lobby-status
	// logic until a game is picked (browsingGames).
	private void netMenu_JoinOnlineSelected(MenuSub1 sender)
	{
		netMenu.Remove();
		browsingGames = true;
		EvilAliensWeb.Compat.Net.NetGameBrowser.Start();
		onlineGamesMenu.Show();
	}

	// A game was picked from the carousel: join its code through the normal 11.4 flow (the
	// host is mid-level, so it becomes join-in-progress). Leaving browse mode lets NetUpdate's
	// lobby-status logic take over.
	private void onlineGames_GameSelected(string code)
	{
		browsingGames = false;
		EvilAliensWeb.Compat.Net.NetGameBrowser.Stop();
		onlineGamesMenu.RemoveInstantly();
		EvilAliensWeb.Compat.Net.NetLobby.JoinWithCode(base.Game, code);
		ShowNetStatus("Connecting...");
	}

	private void onlineGames_BackSelected(MenuSub1 sender)
	{
		browsingGames = false;
		EvilAliensWeb.Compat.Net.NetGameBrowser.Stop();
		onlineGamesMenu.Remove();
		if (DebugFlags.GameBrowser)
		{
			// The ?gamebrowser boot has no netMenu behind it -- return to the main menu.
			netMode = false;
			mainMenu.Show();
		}
		else
		{
			netMenu.Show();
		}
	}

	private void netStatus_CancelSelected(MenuSub1 sender)
	{
		HideNetStatus();
		if (netNoticeUp)
		{
			// Acknowledging a session-ending notice returns to the top (the lobby flow
			// context is gone).
			netNoticeUp = false;
			netMode = false;
			mainMenu.Show();
			return;
		}
		EvilAliensWeb.Compat.Net.NetLobby.Cancel();
		netMenu.Show();
	}

	private void netPick_MissionsSelected(MenuSub1 sender)
	{
		netPickMenu.Remove();
		levelSelector.Show();
	}

	private void netPick_ChallengesSelected(MenuSub1 sender)
	{
		netPickMenu.Remove();
		challengeSelector.Show();
	}

	private void netPick_CancelSelected(MenuSub1 sender)
	{
		EvilAliensWeb.Compat.Net.NetLobby.Cancel();
		netPickMenu.Remove();
		netMenu.Show();
	}

	private void ShowNetStatus(string text)
	{
		netStatusMenu.SetText(text);
		if (!netStatusShown)
		{
			netStatusMenu.Show();
			netStatusShown = true;
		}
	}

	private void HideNetStatus()
	{
		if (netStatusShown)
		{
			netStatusMenu.RemoveInstantly();
			netStatusShown = false;
		}
	}

	// Best-effort close of every menu the net flow can have open (session died from
	// under it). RemoveInstantly/Collection.Remove of a not-shown scene is a no-op.
	private void CloseNetFlowMenus()
	{
		HideNetStatus();
		netMenu.RemoveInstantly();
		netPickMenu.RemoveInstantly();
		onlineGamesMenu.RemoveInstantly();
		levelSelector.RemoveInstantly();
		challengeSelector.RemoveInstantly();
		difficultyMenu.RemoveInstantly();
		EvilAliensWeb.Compat.Net.NetGameBrowser.Stop();
		browsingGames = false;
		EvilAliensWeb.Compat.Net.WebRtcInterop.ClosePrompt();
	}

	// Per-tick lobby pump: drains the JS-side phase queue, keeps the status panel's text
	// current, advances the host to the level pick on connect, mirrors the host's launch
	// on the client, and surfaces session-ending notices from any point in the flow.
	private void NetUpdate()
	{
		string notice = EvilAliensWeb.Compat.Net.NetSession.TakeMenuNotice();
		if (notice != null)
		{
			if (netMode)
			{
				CloseNetFlowMenus();
			}
			else
			{
				mainMenu.RemoveInstantly(); // fresh menu re-entry after an in-level match end
			}
			EvilAliensWeb.Compat.Net.NetLobby.Cancel();
			netMode = true; // the status panel is net-flow UI; cleared on acknowledge
			netNoticeUp = true;
			ShowNetStatus(notice);
			return;
		}
		if (!netMode || netNoticeUp)
		{
			return;
		}
		if (browsingGames)
		{
			// The carousel owns the screen: drain the browser's room list + pings. The lobby
			// status logic below stays parked until a game is picked (onlineGames_GameSelected).
			EvilAliensWeb.Compat.Net.NetGameBrowser.Tick();
			return;
		}
		EvilAliensWeb.Compat.Net.NetLobby.Tick();
		if (EvilAliensWeb.Compat.Net.NetSession.TakePendingLaunch(out Levels level, out Settings.DifficultyLevel difficulty))
		{
			NetLaunchMirror(level, difficulty);
			return;
		}
		if (!netStatusShown)
		{
			return;
		}
		switch (EvilAliensWeb.Compat.Net.NetLobby.Phase)
		{
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Contacting:
			netStatusMenu.SetText("Contacting server...");
			break;
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Hosting:
			netStatusMenu.SetText("Room code:  " + EvilAliensWeb.Compat.Net.NetLobby.RoomCode
				+ "\nTell your friend!\nWaiting for them to join...");
			break;
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Prompting:
			netStatusMenu.SetText("Enter the room code");
			break;
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Connecting:
			netStatusMenu.SetText("Connecting...");
			break;
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Failed:
			netStatusMenu.SetText(EvilAliensWeb.Compat.Net.NetLobby.FailText);
			break;
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Connected:
			// PeerUp = the v4 handshake (build hash + flags) settled too, not just ICE.
			if (!EvilAliensWeb.Compat.Net.NetSession.PeerUp)
			{
				netStatusMenu.SetText("Connecting...");
			}
			else if (EvilAliensWeb.Compat.Net.NetLobby.IsHosting)
			{
				HideNetStatus();
				netPickMenu.Show();
			}
			else
			{
				netStatusMenu.SetText("Connected!\nThe host is choosing a mission...");
			}
			break;
		case EvilAliensWeb.Compat.Net.NetLobby.LobbyPhase.Idle:
			// The code-entry overlay was cancelled.
			HideNetStatus();
			netMenu.Show();
			break;
		}
	}

	// Client side of EvLaunch: mirror the host's pick through the exact same fade ->
	// OnFinished -> warm -> launch path the local menus use.
	// Both arguments are validated at the wire boundary (NetProtocol.TryDecodeLaunchEvent), so
	// this can hand them straight to the real launch path. An unvalidated level would reach
	// Game1.AddLevelComponent's throwing default arm, and an unvalidated difficulty would land
	// in the XML-serialized Settings.CurrentDifficulty -- see the contract in NetProtocol.
	private void NetLaunchMirror(Levels level, Settings.DifficultyLevel difficulty)
	{
		Settings.GetInstance().SetDifficultyTo(difficulty);
		selectedLevel = level;
		starter = ControlDevice.Keyboard;
		HideNetStatus();
		fadestarted = timer;
		currentFade = 0f;
		state = MenuState.FadeToGame;
		nextState = NextState.StartLevel;
	}

	private void mainMenu_AwardmentsSelected(MenuSub1 sender)
	{
		mainMenu.Remove();
		awardmentsMenu.Show();
	}

	private void mainMenu_PreviewSelected(MenuSub1 sender)
	{
		if (sender == mainMenu)
		{
			nextState = NextState.StartPreview;
		}
		else
		{
			nextState = NextState.StartPreviewForced;
		}
		fadestarted = timer;
		currentFade = 0f;
		state = MenuState.FadeToGame;
		mainMenu.Remove();
	}

	private void mainMenu_OptionsSelected(MenuSub1 sender)
	{
		optionsMenu.Show();
		mainMenu.Remove();
	}

	private void mainMenu_bragSelected(MenuSub1 sender)
	{
		mainMenu.RemoveInstantly();
		if (this.OnBragSelected != null)
		{
			this.OnBragSelected(this);
		}
	}

	private void mainMenu_TutorialSelected(MenuSub1 sender)
	{
		fadestarted = timer;
		currentFade = 0f;
		state = MenuState.FadeToGame;
		nextState = NextState.StartLevel;
		selectedLevel = Levels.Tutorial;
		if (base.InputHandler.Pressed(MyKeys.Enter))
		{
			starter = ControlDevice.Keyboard;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 0) || base.InputHandler.PadPressed(PadKeys.A, 0))
		{
			starter = ControlDevice.PadOne;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 1) || base.InputHandler.PadPressed(PadKeys.A, 1))
		{
			starter = ControlDevice.PadTwo;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 2) || base.InputHandler.PadPressed(PadKeys.A, 2))
		{
			starter = ControlDevice.PadThree;
		}
		else if (base.InputHandler.PadPressed(PadKeys.Start, 3) || base.InputHandler.PadPressed(PadKeys.A, 3))
		{
			starter = ControlDevice.PadFour;
		}
		else if (base.InputHandler.Pressed(MyKeys.Generic_Start))
		{
			starter = ControlDevice.Generic;
		}
		else
		{
			// Mouse-click activation (Stage 13 made every MenuSub1 entry clickable) presses
			// none of the device keys; on web the mouse is the keyboard player, so default to
			// Keyboard instead of leaving starter at the enum default (PadOne), which would
			// bind the level to a nonexistent gamepad (instant pause loop).
			starter = ControlDevice.Keyboard;
		}
		sender.Remove();
	}

	private void mainMenu_ExitSelected(MenuSub1 sender)
	{
		// Web-port "boss key": there's no real Exit in a browser tab, so "close" the
		// game and hand off to the fake productivity suite in wwwroot/office/ (see
		// Compat/ExitInterop + wwwroot/index.html eaQuit). WantExit() still blacks out
		// the canvas underneath the JS fade in case navigation is somehow blocked.
		((Game1)(object)base.Game).WantExit();
		EvilAliensWeb.Compat.ExitInterop.Quit();
	}

	public override void Initialize()
	{
		GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				SignedInGamer current = enumerator.Current;
				current.Presence.PresenceMode = (GamerPresenceMode)46;
			}
		}
		finally
		{
			((IDisposable)enumerator).Dispose();
		}
		for (int i = 0; i < 200; i++)
		{
			CreateStar(moveit: true);
		}
		base.SoundManager.PlayMusic(Songs.Sjaak);
		// Debug (?unlockall): reveal every gated menu option (Cheats, all challenges,
		// Level 2/3, Challenges/Awardments) and mark all awardments unlocked, so the whole
		// menu can be walked through.
		//
		// Session-only, and since card 36db5d75 that is ENFORCED rather than merely claimed:
		// Achievements and Unlockables refuse to save at all while the flag is on. It mutates
		// both singletons and plenty of unrelated code persists them later, so finishing one
		// level in a ?unlockall session used to make the unlock permanent. See
		// Savable.SuppressSave for why the WRITE is suppressed rather than the mutation avoided.
		if (DebugFlags.UnlockAll)
		{
			foreach (Unlockables.Items item in Game1.GetEnumValues<Unlockables.Items>())
			{
				Unlockables.GetInstance().Unlock(item);
			}
			int awardCount = Game1.GetEnumValues<Awardment>().Count;
			for (int i = 0; i < awardCount; i++)
			{
				Achievements.GetInstance().SetAwardmentIsUnlocked(i, true);
			}
		}
		state = MenuState.Normal;
		timer = TimeSpan.Zero;
		// Reset the HUD ring's "autofocus hunt" dart machine alongside `timer` above.
		// Initialize() runs on EVERY re-entry to the menu (level -> credits -> menu, not just
		// the first boot), but until now only `timer` was zeroed here -- ringMoveStart/
		// ringHoldUntil are ABSOLUTE timestamps measured against that same timer. Left stale
		// from before a level launch (worst case: leaving mid-dart, ringHolding == false), they
		// could sit far AHEAD of the freshly-reset `now`, and UpdateRing's smoothstep+Lerp
		// extrapolates wildly for the deeply-out-of-range `u` this produces -- read as the ring
		// "spinning at incredibly high speed" right after finishing a level (Trello fdbe3be0).
		// The 2026-07-03 fix (6b2c2a7) capped the ambient COAST drift for a long menu IDLE but
		// never touched this DART state, which only desyncs on a menu re-entry -- a different
		// trigger, so it survived that fix. These are exactly the field initializers above,
		// so first boot is unchanged; only a re-entry now gets the same fresh, calm state.
		ringAngle = 0f;
		ringFrom = 0f;
		ringTo = 0f;
		ringMoveStart = 0.0;
		ringMoveDur = 0.0001;
		ringHoldUntil = 0.0;
		ringHolding = true;
		ringDirAccumDeg = 0f;
		ringDrift = 0f;
		ringDriftVel = 0f;
		backdrop = content.Load<Texture2D>("GFX/Menu/planet");
		currentBackdropSize = MathHelper.Max(800f / (float)backdrop.LogicalWidth(), 600f / (float)backdrop.LogicalHeight());
		originalBackdropSize = currentBackdropSize;
		if (DebugFlags.GameBrowser)
		{
			// ?gamebrowser: boot straight into the online-game carousel with injected fake
			// entries (no server, no WebRTC) so its appearance can be screenshotted.
			// ?gamebrowser=fallback swaps in the variant that also lists two levels with no
			// bundled art, for the EnsureArt fallback probe.
			EvilAliensWeb.Compat.Net.NetGameBrowser.InjectFakeGames(DebugFlags.GameBrowserFallback);
			netMode = true;
			browsingGames = true;
			onlineGamesMenu.Show();
		}
		else if (!hidemainmenu)
		{
			Collection.Add((GameComponent)(object)mainMenu);
		}
		hidemainmenu = false;
		base.Initialize();
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		// Leaving the menu (-> level / attract demo): hide the reticle so it doesn't
		// linger; the next scene (GameScene) decides its own cursor visibility.
		if (e.GameComponent == this)
		{
			((DrawableGameComponent)_cursor).Visible = false;
		}
	}

	protected override void UnloadContent()
	{
		base.UnloadContent();
		if (myRenderTarget != null)
		{
			((Texture2D)myRenderTarget).Dispose();
		}
		myRenderTarget = null;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		stargfx = content.Load<Texture2D>("GFX/Menu/star");
		blankTexture = content.Load<Texture2D>("GFX/Menu/blank");
		hudring = content.Load<Texture2D>("GFX/Menu/hudring");
		vignette = content.Load<Texture2D>("GFX/Menu/vignette");
		backdrop = content.Load<Texture2D>("GFX/Menu/planet");
		AButton = Content.Load<Texture2D>("GFX/Preview/small_face_a");
		BButton = Content.Load<Texture2D>("GFX/Preview/small_face_b");
		foreach (Star star in stars)
		{
			star.ReloadSprite(stargfx);
		}
		foreach (Star idleStar in idleStars)
		{
			idleStar.ReloadSprite(stargfx);
		}
		EnsureRenderTarget();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	// Stage 10: the menu backdrop + stars render into this offscreen target, then it's
	// composited 1:1 into the scene. Size it to the unified render resolution (RenderScale)
	// so it aligns with the scene and stays crisp; Color (RGBA8) because Bgr565 renders
	// nothing on WebGL (Stage 5). PreserveContents ((RenderTargetUsage)1) is kept and the
	// target is cleared once on (re)creation — the "lightspeed warp" star trail relies on
	// PreserveContents and NOT being cleared during FadeToGame. Recreated on size change (a resize mid-warp resets the star trail; rare and self-heals).
	private void EnsureRenderTarget()
	{
		int w = EvilAliensWeb.Compat.RenderScale.Width;
		int h = EvilAliensWeb.Compat.RenderScale.Height;
		if (myRenderTarget != null && ((Texture2D)myRenderTarget).Width == w && ((Texture2D)myRenderTarget).Height == h)
		{
			return;
		}
		if (myRenderTarget != null)
		{
			((Texture2D)myRenderTarget).Dispose();
		}
		myRenderTarget = new RenderTarget2D(base.GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, (RenderTargetUsage)1);
		base.GraphicsDevice.SetRenderTarget(0, myRenderTarget);
		base.GraphicsDevice.Clear(Color.Black);
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
	}

	public override void Draw(GameTime gameTime)
	{
		base.SpriteBatch.Flush();
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		EnsureRenderTarget();
		base.GraphicsDevice.SetRenderTarget(0, myRenderTarget);
		bool showInzaneBackdrop = false;
		if (state != MenuState.FadeToGame)
		{
			showInzaneBackdrop = true;
			showInzaneBackdrop &= Achievements.GetInstance().Data[Levels.Level1].difficulty >= Settings.DifficultyLevel.Inzane;
			showInzaneBackdrop &= Achievements.GetInstance().Data[Levels.Level2].difficulty >= Settings.DifficultyLevel.Inzane;
			showInzaneBackdrop &= Achievements.GetInstance().Data[Levels.Level3].difficulty >= Settings.DifficultyLevel.Inzane;
			if (showInzaneBackdrop)
			{
				base.SpriteBatch.Draw(backdrop, origin, 0f, currentBackdropSize, center: true, Color.Red);
			}
			else
			{
				base.SpriteBatch.Draw(backdrop, origin, 0f, currentBackdropSize, center: true);
			}
			base.SpriteBatch.Draw(vignette, new Rectangle(0, 0, 800, 600), Color.White);
			DrawHudDecor();
		}
		bool allChallengesInzane = Achievements.GetInstance().Data[Levels.Braineroids].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.ClassicAliens].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.CrazyGame].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.InsaneBossI].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.OwnLevel].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.Paratrooper].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.SpaceDodge].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.WebcamAliens].difficulty >= Settings.DifficultyLevel.Inzane;
		foreach (Star star in stars)
		{
			star.Draw(allChallengesInzane);
		}
		if (showInzaneBackdrop && allChallengesInzane)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Insane);
		}
		if (Achievements.GetInstance().Data[Levels.Braineroids].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.ClassicAliens].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.CrazyGame].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.InsaneBossI].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.OwnLevel].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.Paratrooper].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.SpaceDodge].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.WebcamAliens].difficulty >= Settings.DifficultyLevel.Hard)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Challenges);
		}
		base.SpriteBatch.Flush();
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		// Stage 10: the RT is render-sized — composite 1:1 into the scene via the
		// identity-transform DrawPresent (a normal scaled draw would double the scale).
		base.SpriteBatch.DrawPresent(myRenderTarget, Vector2.Zero, Vector2.Zero, 1f, Color.White);
		drawButtonTips();
		if (state == MenuState.FadeToGame)
		{
			int fadeAlpha = Convert.ToInt16(currentFade);
			if (fadeAlpha < 0)
			{
				fadeAlpha = 0;
			}
			if (fadeAlpha > 255)
			{
				fadeAlpha = 255;
			}
			fadeBackBufferToWhite(fadeAlpha);
		}
	}

	// Advances the HUD ring's "autofocus hunt": it holds at an angle, then darts to a
	// new one with a quick eased move, then holds again. Move size is mostly small
	// twitches with the occasional medium adjust or big sweep, direction is random, and
	// holds are usually brief with the odd longer "locked" pause — reads as a robotic
	// lens hunting focus rather than a steady spin.
	private void UpdateRing(GameTime gameTime)
	{
		double now = timer.TotalSeconds;
		float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
		// Ambient background coast: a slow drift that inherits the LAST dart's direction
		// and a sliver of its speed (set in the dart branch below), so the ring keeps
		// gently rotating the way it last moved instead of a fixed constant spin. The coast
		// momentum bleeds off (so it stays a brief drift, not a permanent spin) and the
		// accumulator is wrapped so it can't grow unbounded over a long menu idle.
		ringDriftVel *= (float)Math.Exp(-RingDriftVelDecay * dt);
		ringDrift += ringDriftVel * dt;
		ringDrift = MathHelper.WrapAngle(ringDrift);
		Random rng = RandomHelper.Random;
		if (ringHolding)
		{
			if (now < ringHoldUntil)
				return;
			double roll = rng.NextDouble();
			float magDeg;
			if (roll < 0.6)
				magDeg = rng.Next(4, 18);     // small twitch
			else if (roll < 0.9)
				magDeg = rng.Next(20, 55);    // medium adjust
			else
				magDeg = rng.Next(70, 140);   // big sweep (one dart always < 180)
			float sign = (rng.NextDouble() < 0.5) ? -1f : 1f;
			// Never travel more than 180 degrees in one continuous direction: if this
			// move would push the running same-direction total past 180, reverse instead
			// (brief holds between same-way darts otherwise read as one big >180 sweep).
			if (Math.Sign(sign) == Math.Sign(ringDirAccumDeg) && Math.Abs(ringDirAccumDeg) + magDeg > 180f)
				sign = -sign;
			if (Math.Sign(sign) == Math.Sign(ringDirAccumDeg))
				ringDirAccumDeg += sign * magDeg;
			else
				ringDirAccumDeg = sign * magDeg;
			ringFrom = ringAngle;
			ringTo = ringAngle + sign * MathHelper.ToRadians(magDeg);
			ringMoveDur = 0.30 + 0.006 * magDeg; // unhurried: ~0.35s small .. ~1.1s big
			// Ambient coast inherits this dart's direction + ~5% of its angular speed, so
			// the ring keeps drifting the way it last moved (a bit of angular momentum).
			// Hard-clamped to RingDriftVelMax so the coast can never exceed the intended
			// slow max, regardless of the dart magnitude/duration.
			ringDriftVel = sign * (float)(MathHelper.ToRadians(magDeg) / ringMoveDur) * 0.05f;
			ringDriftVel = MathHelper.Clamp(ringDriftVel, -RingDriftVelMax, RingDriftVelMax);
			ringMoveStart = now;
			ringHolding = false;
		}
		else
		{
			double u = (now - ringMoveStart) / ringMoveDur;
			if (u >= 1.0)
			{
				ringAngle = ringTo;
				ringHolding = true;
				ringHoldUntil = now + ((rng.NextDouble() < 0.3) ? (2.5 + rng.NextDouble() * 2.5) : (0.9 + rng.NextDouble() * 1.6));
			}
			else
			{
				float s = (float)u;
				s = s * s * (3f - 2f * s); // smoothstep ease
				ringAngle = MathHelper.Lerp(ringFrom, ringTo, s);
			}
		}
	}

	// Menu "manager" hook: the shared ComponentBin notifies every IComponentWatcher when
	// a component is added, so when a menu (main or submenu) is shown we make it the HUD
	// ring's target — the ring then eases over to re-centre on it (see UpdateRingCentre).
	public override void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
		base.OnComponentAdded(e);
		if (e.GameComponent is MenuSub1 menu)
			ringTargetMenu = menu;
	}

	// Eases the ring's centre toward the active menu's list centre. When the target
	// changes (you enter/leave a submenu) it kicks off a quick "recalibrate" tween with
	// overshoot (ease-out-back) — the lens darts past the new centre and settles back.
	private void UpdateRingCentre(GameTime gameTime)
	{
		// The active menu becomes the ring's target the moment it's shown, but we hold the
		// recalibrate until it has finished its zoom-in (IsEntering clears) so the ring
		// reacts to the menu having appeared rather than sliding alongside it.
		if (ringTargetMenu != null && !ringTargetMenu.IsEntering)
		{
			Vector2 target = ringTargetMenu.GetListCentre();
			if (!ringCentreInit)
			{
				ringCentre = target;
				ringCentreTo = target;
				ringCentreInit = true;
			}
			else if ((target - ringCentreTo).LengthSquared() > 1f) // active menu changed -> recalibrate
			{
				ringCentreFrom = ringCentre;
				ringCentreTo = target;
				ringCentreTweenStart = timer.TotalSeconds;
				ringCentreTweening = true;
			}
		}
		if (ringCentreTweening)
		{
			double u = (timer.TotalSeconds - ringCentreTweenStart) / ringCentreTweenDur;
			if (u >= 1.0)
			{
				ringCentre = ringCentreTo;
				ringCentreTweening = false;
			}
			else
			{
				ringCentre = Vector2.Lerp(ringCentreFrom, ringCentreTo, EaseOutBack((float)u));
			}
		}
		else if (ringCentreInit)
		{
			ringCentre = ringCentreTo;
		}
	}

	// Ease-out-back: overshoots the target then settles (a "tween with overshoot").
	private static float EaseOutBack(float t)
	{
		const float c1 = 1.9f;          // a touch more overshoot than the textbook 1.70158
		const float c3 = c1 + 1f;
		float u = t - 1f;
		return 1f + c3 * u * u * u + c1 * u * u;
	}

	// Stage 13 menu reskin: a sci-fi HUD layer drawn into the scene target (so it
	// sits BEHIND the menu's own composited render target) — a slowly-rotating
	// targeting reticle centred behind the menu list, plus four corner brackets.
	// Drawn in 800x600 design space, dim + slightly cool so the menu text reads on
	// top and the whole thing only gently feeds the scene bloom.
	private void DrawHudDecor()
	{
		// Reticle centre is eased toward the active menu by UpdateRingCentre (it re-centres
		// with an overshoot when you enter/leave a submenu).
		base.SpriteBatch.Draw(hudring, ringCentre, ringAngle + ringDrift, 580f / (float)hudring.LogicalHeight(), center: true, new Color(124, 186, 152, 175));
		Color bc = new Color(132, 188, 152, 180);
		int inset = 20, arm = 56, th = 3, R = 800, B = 600;
		Bracket(inset, inset, arm, th, bc, 1, 1);
		Bracket(R - inset, inset, arm, th, bc, -1, 1);
		Bracket(inset, B - inset, arm, th, bc, 1, -1);
		Bracket(R - inset, B - inset, arm, th, bc, -1, -1);
	}

	// One L-shaped corner bracket: (cx,cy) is the corner point; (dx,dy) point the
	// two arms inward (e.g. +1,+1 = top-left). Built from the white `blank` sprite.
	private void Bracket(int cx, int cy, int arm, int th, Color c, int dx, int dy)
	{
		int hx = (dx > 0) ? cx : cx - arm;
		int hy = (dy > 0) ? cy : cy - th;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(hx, hy, arm, th), c);
		int vx = (dx > 0) ? cx : cx - th;
		int vy = (dy > 0) ? cy : cy - arm;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(vx, vy, th, arm), c);
	}

	private void drawButtonTips()
	{
		float iconScale = 0.5f;
		float textScale = 0.8f;
		float backIconX = (General.SafeZone).Left;
		// Both icons sit on this baseline, so it must clear the TALLER of the two (the 2008
		// original measured AButton's height alone).
		float tipsY = (float)(General.SafeZone).Bottom - MathHelper.Max(MathHelper.Max((float)AButton.LogicalHeight(), (float)BButton.LogicalHeight()) * iconScale, font.MeasureString("yo").Y * textScale);
		// Each label clears the WIDTH of the icon actually drawn beside it: BButton sits at
		// backIconX, AButton at selectIconX. The 2008 original had the two widths CROSSED
		// (back cleared AButton's, select cleared BButton's). That is a provable no-op today
		// -- small_face_a and small_face_b are both 60x60 with no precompiled sibling, so the
		// two LogicalWidth() calls return the same number -- so this changes no pixel; it is
		// here so re-authoring either icon at a different width can't silently misplace a label.
		// Card 8d6883f3 completed the set: the height axis above, and the same two bugs in the
		// other two verbatim copies of this layout -- Darkener.drawButtons (the pause overlay)
		// and BragScene.drawButtons. So "an icon can be re-authored at a different size" now
		// holds on both axes in all three.
		float backTextX = backIconX + (float)BButton.LogicalWidth() * iconScale + font.MeasureString(" ").X * textScale;
		float selectTextX = (float)(General.SafeZone).Right - font.MeasureString("select").X * textScale;
		float selectIconX = selectTextX - (float)AButton.LogicalWidth() * iconScale - font.MeasureString(" ").X * textScale;
		base.SpriteBatch.Draw(BButton, new Vector2(backIconX, tipsY), 0f, iconScale, center: false, Color.White);
		base.SpriteBatch.DrawString("back", new Vector2(backTextX, tipsY), Color.AliceBlue, 0f, centered: false, textScale, (SpriteEffects)0, 1f);
		// Card 2a4110d0: make that tip clickable. Both parts draw from their top-left at
		// tipsY, so the box runs icon-left to label-right and down by the taller of the two.
		EvilAliensWeb.Compat.BackTipHit.Record(backIconX, backTextX + font.MeasureString("back").X * textScale, tipsY, (float)(General.SafeZone).Bottom);
		base.SpriteBatch.Draw(AButton, new Vector2(selectIconX, tipsY), 0f, iconScale, center: false, Color.White);
		base.SpriteBatch.DrawString("select", new Vector2(selectTextX, tipsY), Color.AliceBlue, 0f, centered: false, textScale, (SpriteEffects)0, 1f);
	}

	public override void Update(GameTime gameTime)
	{
		// Menus use the plain OS arrow, NOT the aiming reticle, and never play the reticle
		// intro (card 51276dcd: the spin should introduce the pointer->reticle change at the
		// START OF GAMEPLAY, not in the menu). MousePointer maps Visible==false -> "menu"
		// (arrow); force it off here so a stray Visible==true left over from a level can't
		// leak the gameplay reticle into the menu. Only acts on the true->false edge (no
		// event fires when already false), so this is cheap.
		if (((DrawableGameComponent)_cursor).Visible)
		{
			((DrawableGameComponent)_cursor).Visible = false;
		}
		if (!General.IsTrial)
		{
			RemovePreviewOption();
		}
		timer += gameTime.ElapsedGameTime;
		NetUpdate();
		UpdateRing(gameTime);
		UpdateRingCentre(gameTime);
		HandleStars(gameTime);
		float frameMs = 16.666666f;
		// Backdrop Ken-Burns zoom. This used to be an unbounded exponential
		// (1.0001^frames) keyed off the ever-accumulating menu timer, so the planet
		// crept bigger with no ceiling the whole time the menu sat idle. Now it's an
		// exponential *approach* to a 2x cap: it eases toward 2x and tapers to a stop.
		// The approach rate is the old curve's initial per-ms growth (ln(base)/frameMs),
		// so the zoom starts identically and only slows as it nears the cap.
		float curve = (float)(BackdropZoomCap - (BackdropZoomCap - 1.0) * Math.Exp(-Math.Log(1.000100016593933) / frameMs * timer.TotalMilliseconds));
		currentBackdropSize = originalBackdropSize * curve;
		if (state != MenuState.FadeToGame)
		{
			return;
		}
		// The same local, reused for an unrelated quantity: past this point it is the
		// fade ramp, not the backdrop zoom above.
		curve = Convert.ToSingle(Math.Pow(1.0499999523162842, (timer - fadestarted).TotalMilliseconds / (double)frameMs));
		currentFade = curve * 7.5f;
		if (!(currentFade > 255f))
		{
			return;
		}
		currentFade = 255f;
		switch (nextState)
		{
		case NextState.StartLevel:
			this.OnFinished(this, starter, selectedLevel);
			break;
		case NextState.StartPreview:
			this.OnPreviewSelected?.Invoke(this, showExplanation: false);
			break;
		case NextState.StartPreviewForced:
			this.OnPreviewSelected?.Invoke(this, showExplanation: true);
			break;
		}
		foreach (Star star in stars)
		{
			idleStars.Add(star);
		}
		stars.Clear();
	}

	private void HandleStars(GameTime gameTime)
	{
		float starsPerMs = ((state != 0) ? 2.36f : 0.06f);
		float starBudget;
		for (starBudget = Convert.ToSingle((double)starsPerMs * gameTime.ElapsedGameTime.TotalMilliseconds); starBudget > 1f; starBudget -= 1f)
		{
			CreateStar(moveit: false);
		}
		float fractionalStar = starBudget;
		if (RandomHelper.RandomNextFloat(0f, 1f) <= fractionalStar)
		{
			CreateStar(moveit: false);
		}
		Star[] starsSnapshot = stars.ToArray();
		bool hyperspace = state == MenuState.FadeToGame;
		Star[] starsToMove = starsSnapshot;
		foreach (Star star in starsToMove)
		{
			star.Move(hyperspace, gameTime);
			if (star.IsOffScreen(800, 600))
			{
				stars.Remove(star);
				idleStars.Add(star);
			}
		}
	}

	private void CreateStar(bool moveit)
	{
		float speed = RandomHelper.RandomNextFloat(0.001f, 0.8f);
		float direction = (float)Math.PI * 2f * RandomHelper.RandomNextFloat(0f, 1f);
		float size = RandomHelper.RandomNextFloat(0.002f, 0.005f);
		Star star;
		if (idleStars.Count == 0)
		{
			star = new Star(base.Game as Game1, stargfx, origin, size, direction, speed);
		}
		else
		{
			star = idleStars[0];
			idleStars.RemoveAt(0);
			star.Reset(origin, size, direction, speed);
		}
		stars.Add(star);
		if (moveit)
		{
			int factor = RandomHelper.Random.Next(0, 2000);
			star.MoveForward(factor);
		}
	}

	protected void fadeBackBufferToWhite(int alpha)
	{
		// Stage 10: full-screen fade in 800x600 design space (RenderScale.Matrix scales it
		// to fill the render target); reading the viewport would over/under-cover it.
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, 800, 600), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)alpha));
	}

	internal void PreSelectLevel(Levels level)
	{
		levelSelector.Show();
		hidemainmenu = true;
		levelSelector.SelectLevel(level);
	}

	internal void RemovePreviewOption()
	{
		mainMenu.RemoveEntry("View Trailer");
	}

	internal void CleanUp()
	{
		this.OnFinished = null;
		this.OnFullScreen = null;
		this.OnVSyncChange = null;
		this.OnPreviewSelected = null;
		this.OnResetSelected = null;
	}
}
