using System;
using System.Collections.Generic;
using BloomPostprocess;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

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

	private Texture2D backdrop;

	private Texture2D blankTexture;

	private ContentManager content;

	private List<Star> stars = new List<Star>();

	private List<Star> idleStars = new List<Star>();

	private RenderTarget2D myRenderTarget;

	private MenuSubWithSkull mainMenu;

	private SubMenuLevelChoice challengeSelector;

	private SubMenuLevelChoice levelSelector;

	private SubMenuAwardments awardmentsMenu;

	private SubMenuAwardmentText awardmentTextMenu;

	private MenuSub1 optionsMenu;

	private MenuSub1 cheatsMenu;

	private MenuSub1 colorsMenu;

	private MenuSub1 confirmationMenu;

	private MenuSub1 playerSettingsMenu;

	private MenuSub1 playtestMenu;

	private MenuSub1 trailerMenu;

	private DifficultyMenu difficultyMenu;

	private GammaMenu gammaMenu;

	private ScreenResizeMenu screenResizeMenu;

	private TrailerScene trailerScene;

	private MenuSub1 difficultyCaller;

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
		mainMenu.AddEntry("Awardments", Unlockables.Items.Awardments);
		mainMenu.AddEntryEvent(mainMenu_AwardmentsSelected);
		mainMenu.AddEntry("Cheats", Unlockables.Items.Cheats);
		mainMenu.AddEntryEvent(mainMenu_CheatsSelected);
		mainMenu.OnTimeOut += mainMenu_DemoSelected;
		mainMenu.AddEntry("Exit");
		mainMenu.AddEntryEvent(mainMenu_ExitSelected);
		mainMenu.OnExit += mainMenu_OnExit;
		levelSelector = new SubMenuLevelChoice(base.Game);
		levelSelector.OnExit += levelSelector_OnExit;
		levelSelector.AddEntry("Mission 1");
		levelSelector.AddEntryData("GFX/Screenshots/level1empty", "The Evil Aliens must be repelled!", Levels.Level1);
		levelSelector.AddEntryEvent(levelSelector_levelSelected);
		levelSelector.AddEntry("Mission 2", Unlockables.Items.Level2);
		levelSelector.AddEntryData("GFX/Screenshots/level2empty", "Mars Attacks!", Levels.Level2);
		levelSelector.AddEntryEvent(levelSelector_levelSelected);
		levelSelector.AddEntry("Mission 3", Unlockables.Items.Level3);
		levelSelector.AddEntryData("GFX/Screenshots/level3empty", "Invade the Alien base!", Levels.Level3);
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
		challengeSelector.AddEntryData("GFX/Screenshots/SpaceDodge", "Move fast and dodge the oncoming asteroids!", Levels.SpaceDodge);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Braineroids", Unlockables.Items.Braineroids);
		challengeSelector.AddEntryData("GFX/Screenshots/ss1", "What the arcade classic could have looked like..", Levels.Braineroids);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Evil Aliens Classic", Unlockables.Items.ClassicAliens);
		challengeSelector.AddEntryData("GFX/Screenshots/classicss", "Can you beat the game that started it all?", Levels.ClassicAliens);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Paratrooper", Unlockables.Items.Paratrooper);
		challengeSelector.AddEntryData("GFX/Screenshots/Paratrooper", "Paratrooper!", Levels.Paratrooper);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Base Pressure", Unlockables.Items.OwnLevel);
		challengeSelector.AddEntryData("GFX/Screenshots/OwnLevel", "Can you manoeuvre through the narrow passageways?", Levels.OwnLevel);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Crazy Game", Unlockables.Items.CrazyGame);
		challengeSelector.AddEntryData("GFX/Screenshots/crazygamess", "The year is 2501. Your planet has just been overrun by an\nevil alien force known only as The Dots.\nYou managed to escape the fate of your planet by hopping\ninto your shuttle and blasting off, but The Dots are right\non your tail!\nHow long can you last before they destroy you too?", Levels.CrazyGame);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Boss Train", Unlockables.Items.BossTrain);
		challengeSelector.AddEntryData("GFX/Screenshots/InsaneBossI", "Defeat the Alien bosses for great victory", Levels.InsaneBossI);
		challengeSelector.AddEntryEvent(challengeSelector_levelSelected);
		challengeSelector.AddEntry("Team Challenge", Unlockables.Items.TeamChallenge);
		challengeSelector.AddEntryData("GFX/Screenshots/teamchallengess", "Fly the new MX2 Dual Pilot Vessel to victory!\nRequires two players", Levels.TeamChallenge);
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
		if (GraphicsAdapter.DefaultAdapter.IsWideScreen)
		{
			optionsMenu.AddEntry("Stretch Screen: " + boolToGameString(Settings.GetInstance().Stretch));
			optionsMenu.AddEntryEvent(optionsMenu_StretchSelected);
		}
		optionsMenu.AddEntry("Reset All Progress");
		optionsMenu.AddEntryEvent(optionsMenu_LockAllSelected);
		optionsMenu.AddEntry("Modify Screen Size");
		optionsMenu.AddEntryEvent(optionsMenu_ScreenSizeSelected);
		optionsMenu.AddEntry("Gamma Correction");
		optionsMenu.AddEntryEvent(optionsMenu_GammaCorrectionSelected);
		playerSettingsMenu = new PlayerSettingsMenu(game, darken: true);
		optionsMenu.AddEntry("Controller Settings");
		optionsMenu.AddEntryEvent(optionsMenu_PlayerOptionsSelected);
		playerSettingsMenu.OnExit += playerSettingsMenu_OnExit;
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
		screenResizeMenu = new ScreenResizeMenu(game);
		screenResizeMenu.OnFinished += screenResizeMenu_OnFinished;
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
		playtestMenu = new MenuSub1(base.Game);
		playtestMenu.AddEntry("Unlock Everything");
		playtestMenu.AddEntryEvent(playtestMenu_UnlockAllSelected);
		playtestMenu.AddEntry("Invincibility: " + boolToGameString(Settings.GetInstance().Invulnerability));
		playtestMenu.AddEntryEvent(playtestMenu_InvincibilitySelected);
		playtestMenu.AddEntry("Back");
		playtestMenu.AddEntryEvent(playtestMenu_OnExit);
		playtestMenu.OnExit += playtestMenu_OnExit;
		trailerMenu = new MenuSub1(base.Game);
		trailerMenu.AddEntry("Revenge of the Evil Aliens");
		trailerMenu.AddEntryEvent(trailerMenu_EvilAliensSelected);
		trailerMenu.AddEntry("Rocket Riot");
		trailerMenu.AddEntryEvent(trailerMenu_RocketRiotSelected);
		trailerMenu.AddEntry("Back");
		trailerMenu.AddEntryEvent(trailerMenu_BackSelected);
		trailerMenu.OnExit += trailerMenu_BackSelected;
		trailerScene = new TrailerScene(base.Game);
		trailerScene.OnFinished += trailerScene_OnFinished;
		base.DrawOrder = 1;
	}

	private void trailerMenu_RocketRiotSelected(object sender)
	{
		trailerMenu.Remove();
		trailerScene.Setup(TrailerScene.TrailerMode.RocketRiot);
		Collection.Add((GameComponent)(object)trailerScene);
	}

	private void trailerMenu_EvilAliensSelected(object sender)
	{
		trailerMenu.Remove();
		trailerScene.Setup(TrailerScene.TrailerMode.EvilAliens);
		Collection.Add((GameComponent)(object)trailerScene);
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

	private void screenResizeMenu_OnFinished(object sender)
	{
		Settings.GetInstance().SaveThreaded();
		Collection.Remove((GameComponent)(object)screenResizeMenu);
		base.Visible = true;
		base.Enabled = true;
		((DrawableGameComponent)optionsMenu).Visible = true;
		((GameComponent)optionsMenu).Enabled = true;
	}

	private void confirmationMenu_YesSelected(MenuSub1 sender)
	{
		Achievements.GetInstance().Reset();
		Achievements.GetInstance().SaveNoThread();
		Settings.GetInstance().DisableCheats();
		Settings.GetInstance().SaveNoThread();
		Unlockables.GetInstance().Reset();
		Unlockables.GetInstance().SaveNoThread();
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

	private void playtestMenu_UnlockAllSelected(MenuSub1 sender)
	{
		for (int i = 0; i < Unlockables.GetInstance().Collection.Count; i++)
		{
			Unlockables.GetInstance().Collection[(Unlockables.Items)i] = true;
		}
		Unlockables.GetInstance().Collection[Unlockables.Items.Friends] = false;
		Unlockables.GetInstance().Collection[Unlockables.Items.TeamChallenge] = false;
		Unlockables.GetInstance().SaveThreaded();
	}

	private void playtestMenu_InvincibilitySelected(MenuSub1 sender)
	{
		Settings.GetInstance().Invulnerability = !Settings.GetInstance().Invulnerability;
		sender.SetEntry("Invulnerability: " + boolToGameString(Settings.GetInstance().Invulnerability));
	}

	private void playtestMenu_OnExit(MenuSub1 sender)
	{
		playtestMenu.Remove();
		optionsMenu.Show();
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

	private void optionsMenu_ExplosionsSelected(MenuSub1 sender)
	{
		Settings.GetInstance().BasicExplosions = !Settings.GetInstance().BasicExplosions;
		sender.SetEntry("Fancy Explosions: " + boolToGameString(!Settings.GetInstance().BasicExplosions));
	}

	private void optionsMenu_BloomSelected(MenuSub1 sender)
	{
		Settings.GetInstance().Bloom = !Settings.GetInstance().Bloom;
		Settings.GetInstance().Interpolate = Settings.GetInstance().Bloom;
		Settings.GetInstance().BasicExplosions = !Settings.GetInstance().Bloom;
		sender.SetEntry("Fancy GFX: " + boolToGameString(Settings.GetInstance().Bloom));
		((DrawableGameComponent)ServiceHelper.Get<IBloomService>().BloomComponent).Visible = Settings.GetInstance().Bloom;
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

	private void optionsMenu_ScreenSizeSelected(MenuSub1 sender)
	{
		Collection.Add((GameComponent)(object)screenResizeMenu);
		base.Visible = false;
		base.Enabled = false;
		((DrawableGameComponent)optionsMenu).Visible = false;
		((GameComponent)optionsMenu).Enabled = false;
	}

	private void optionsMenu_GammaCorrectionSelected(MenuSub1 sender)
	{
		Collection.Add((GameComponent)(object)gammaMenu);
		base.Visible = false;
		base.Enabled = false;
		((DrawableGameComponent)optionsMenu).Visible = false;
		((GameComponent)optionsMenu).Enabled = false;
	}

	private void optionsMenu_PlaytestOptionsSelected(MenuSub1 sender)
	{
		playtestMenu.Show();
		optionsMenu.Remove();
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
		else
		{
			if (!base.InputHandler.PadPressed(PadKeys.Start, 3) && !base.InputHandler.PadPressed(PadKeys.A, 3))
			{
				throw new NotSupportedException();
			}
			controlDevice = ControlDevice.PadFour;
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
		int num = 1;
		if (Unlockables.GetInstance().Collection[Unlockables.Items.HarderDifficulties])
		{
			num = 3;
		}
		if (Unlockables.GetInstance().Collection[Unlockables.Items.InsaneDifficulty])
		{
			num = 4;
		}
		Settings.GetInstance().SetDifficultyTo((Settings.DifficultyLevel)MyMath.Mod((int)(Settings.GetInstance().CurrentDifficulty + 1), num + 1));
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
		mainMenu.Show();
		levelSelector.Remove();
	}

	private void challengeSelector_OnExit(MenuSub1 sender)
	{
		mainMenu.Show();
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
		switch (RandomHelper.Random.Next(3))
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
		sender.Remove();
	}

	private void mainMenu_ExitSelected(MenuSub1 sender)
	{
		((Game1)(object)base.Game).WantExit();
	}

	public override void Initialize()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
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
		state = MenuState.Normal;
		timer = TimeSpan.Zero;
		backdrop = content.Load<Texture2D>("GFX/Menu/planet");
		currentBackdropSize = MathHelper.Max(800f / (float)backdrop.Width, 600f / (float)backdrop.Height);
		originalBackdropSize = currentBackdropSize;
		if (!hidemainmenu)
		{
			Collection.Add((GameComponent)(object)mainMenu);
		}
		hidemainmenu = false;
		base.Initialize();
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
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Expected O, but got Unknown
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		base.LoadContent();
		stargfx = content.Load<Texture2D>("GFX/Menu/star");
		blankTexture = content.Load<Texture2D>("GFX/Menu/blank");
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
		if (myRenderTarget == null)
		{
			// Stage 5: size to the 800x600 design resolution (the presenter target),
			// NOT the window back buffer — the menu is authored for 800x600 (backdrop
			// origin (400,300)) and is blitted 1:1 at (0,0) onto the 800x600 sceneTarget,
			// so a window-sized target misaligns it. Also use Color (RGBA8): the original
			// Bgr565 ((SurfaceFormat)1) is not a valid WebGL render-target format and
			// renders black.
			myRenderTarget = new RenderTarget2D(base.GraphicsDevice, 800, 600, false, SurfaceFormat.Color, DepthFormat.None, 0, (RenderTargetUsage)1);
			base.GraphicsDevice.SetRenderTarget(0, myRenderTarget);
			base.GraphicsDevice.Clear(Color.Black);
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		}
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.Flush();
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.GraphicsDevice.SetRenderTarget(0, myRenderTarget);
		bool flag = false;
		if (state != MenuState.FadeToGame)
		{
			flag = true;
			flag &= Achievements.GetInstance().Data[Levels.Level1].difficulty >= Settings.DifficultyLevel.Inzane;
			flag &= Achievements.GetInstance().Data[Levels.Level2].difficulty >= Settings.DifficultyLevel.Inzane;
			flag &= Achievements.GetInstance().Data[Levels.Level3].difficulty >= Settings.DifficultyLevel.Inzane;
			if (flag)
			{
				base.SpriteBatch.Draw(backdrop, origin, 0f, currentBackdropSize, center: true, Color.Red);
			}
			else
			{
				base.SpriteBatch.Draw(backdrop, origin, 0f, currentBackdropSize, center: true);
			}
		}
		bool flag2 = Achievements.GetInstance().Data[Levels.Braineroids].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.ClassicAliens].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.CrazyGame].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.InsaneBossI].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.OwnLevel].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.Paratrooper].difficulty >= Settings.DifficultyLevel.Inzane && Achievements.GetInstance().Data[Levels.SpaceDodge].difficulty >= Settings.DifficultyLevel.Inzane;
		foreach (Star star in stars)
		{
			star.Draw(flag2);
		}
		if (flag && flag2)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Insane);
		}
		if (Achievements.GetInstance().Data[Levels.Braineroids].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.ClassicAliens].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.CrazyGame].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.InsaneBossI].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.OwnLevel].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.Paratrooper].difficulty >= Settings.DifficultyLevel.Hard && Achievements.GetInstance().Data[Levels.SpaceDodge].difficulty >= Settings.DifficultyLevel.Hard)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.Challenges);
		}
		base.SpriteBatch.Flush();
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		base.SpriteBatch.Draw(myRenderTarget.GetTexture(), Vector2.Zero, Color.White);
		drawButtonTips();
		if (state == MenuState.FadeToGame)
		{
			int num = Convert.ToInt16(currentFade);
			if (num < 0)
			{
				num = 0;
			}
			if (num > 255)
			{
				num = 255;
			}
			fadeBackBufferToWhite(num);
		}
	}

	private void drawButtonTips()
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		float num = 0.5f;
		float num2 = 0.8f;
		float num3 = (General.SafeZone).Left;
		float num4 = (float)(General.SafeZone).Bottom - MathHelper.Max((float)AButton.Height * num, font.MeasureString("yo").Y * num2);
		float num5 = num3 + (float)AButton.Width * num + font.MeasureString(" ").X * num2;
		float num6 = (float)(General.SafeZone).Right - font.MeasureString("select").X * num2;
		float num7 = num6 - (float)BButton.Width * num - font.MeasureString(" ").X * num2;
		base.SpriteBatch.Draw(BButton, new Vector2(num3, num4), 0f, num, center: false, Color.White);
		base.SpriteBatch.DrawString("back", new Vector2(num5, num4), Color.AliceBlue, 0f, centered: false, num2, (SpriteEffects)0, 1f);
		base.SpriteBatch.Draw(AButton, new Vector2(num7, num4), 0f, num, center: false, Color.White);
		base.SpriteBatch.DrawString("select", new Vector2(num6, num4), Color.AliceBlue, 0f, centered: false, num2, (SpriteEffects)0, 1f);
	}

	public override void Update(GameTime gameTime)
	{
		if (!General.IsTrial)
		{
			RemovePreviewOption();
		}
		timer += gameTime.ElapsedGameTime;
		HandleStars(gameTime);
		float num = 16.666666f;
		float num2 = Convert.ToSingle(Math.Pow(1.000100016593933, timer.TotalMilliseconds / (double)num));
		currentBackdropSize = originalBackdropSize * num2;
		if (state != MenuState.FadeToGame)
		{
			return;
		}
		num2 = Convert.ToSingle(Math.Pow(1.0499999523162842, (timer - fadestarted).TotalMilliseconds / (double)num));
		currentFade = num2 * 7.5f;
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
			this.OnPreviewSelected(this, showExplanation: false);
			break;
		case NextState.StartPreviewForced:
			this.OnPreviewSelected(this, showExplanation: true);
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
		float num = ((state != 0) ? 2.36f : 0.06f);
		float num2;
		for (num2 = Convert.ToSingle((double)num * gameTime.ElapsedGameTime.TotalMilliseconds); num2 > 1f; num2 -= 1f)
		{
			CreateStar(moveit: false);
		}
		float num3 = num2;
		if (RandomHelper.RandomNextFloat(0f, 1f) <= num3)
		{
			CreateStar(moveit: false);
		}
		Star[] array = stars.ToArray();
		bool hyperspace = state == MenuState.FadeToGame;
		Star[] array2 = array;
		foreach (Star star in array2)
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
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		float num = RandomHelper.RandomNextFloat(0.001f, 0.8f);
		float num2 = (float)Math.PI * 2f * RandomHelper.RandomNextFloat(0f, 1f);
		float size = RandomHelper.RandomNextFloat(0.002f, 0.005f);
		Star star;
		if (idleStars.Count == 0)
		{
			star = new Star(base.Game as Game1, stargfx, origin, size, num2, num);
		}
		else
		{
			star = idleStars[0];
			idleStars.RemoveAt(0);
			star.Reset(origin, size, num2, num);
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
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		Viewport viewport = base.GraphicsDevice.Viewport;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, (viewport).Width, (viewport).Height), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)alpha));
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
