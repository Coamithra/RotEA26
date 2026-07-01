using System;
using System.Collections.ObjectModel;
using System.IO;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

internal abstract class GameScene : Scene
{
	protected enum PlayerSpawnType
	{
		South,
		West,
		North
	}

	private enum GameState
	{
		Startup,
		Nothing,
		Normal,
		Resetting,
		Victory,
		GameOver
	}

	public delegate void FinishedHandler(object sender, FinishedArgs args);

	protected delegate void ResetHandler();

	public enum FinishedMode
	{
		finishedlevel,
		lostlevel,
		exit
	}

	public struct FinishedArgs
	{
		public FinishedMode mode;

		public Levels level;

		public FinishedArgs(FinishedMode mode, Levels level)
		{
			this.mode = mode;
			this.level = level;
		}
	}

	private int screenshotspamnr = -1;

	private bool ScreenShotSpamEnabled;

	private ResolveTexture2D MyScreenShot;

	private bool cheatwarningshown;

	protected bool isDemo;

	private bool snapshotMadeThisSession;

	private bool snapshotExists;

	private Timer snapshottimer = new Timer(5000f, repeating: false);

	private Timer snapshotdelaytimer = new Timer(800f, repeating: false);

	protected Levels level;

	private Timer AIJoinTimer = new Timer(6000f, repeating: true);

	protected bool AllowAIFriends = true;

	private AnimatedMessage defeatmessage;

	private bool xfading;

	private bool xfadedone;

	private bool defeatmessageshown;

	private MousePointer _cursor;

	protected PlayerSpawnType spawnType;

	private GameState _state;

	private bool _spawnplayernormally;

	private bool _wanttochangespawnplayer;

	private TimeSpan _timer;

	protected GameEventList eventList;

	protected Background Background;

	protected ForegroundPlaceholder Foreground;

	protected Oracle oracle;

	private Darkener darkener;

	private PausedScene pausedScene;

	private PlayerSettingsMenu playerOptions;

	private InstructionsMenu instructionsMenu;

	private ConfirmationMenu exitConfirmationMenu;

	private bool shipCreated;

	private bool eventFired;

	protected ScoreVisualiser score;

	private Timer pausestopper;

	private UFO u;

	private Game1.PostDrawEvent game1PostDrawEvent;

	public Levels Level => level;

	protected bool spawnPlayerNormally
	{
		get
		{
			return _spawnplayernormally;
		}
		set
		{
			if (value)
			{
				_wanttochangespawnplayer = true;
			}
			else
			{
				_spawnplayernormally = false;
			}
		}
	}

	public event FinishedHandler OnFinished;

	protected event ResetHandler OnReset;

	public GameScene(Game game, Levels level)
		: base(game)
	{
		this.level = level;
		Background = new Background(game);
		Background.OnXFadeFinished += Background_OnXFadeFinished;
		Foreground = new ForegroundPlaceholder(game, Background);
		oracle = ServiceHelper.Get<IOracleService>().Oracle;
		score = ServiceHelper.Get<IScoreService>().Score;
		eventList = new GameEventList(game);
		eventList.OnCheckPointReached += eventList_OnCheckPointReached;
		PopulateEventList();
		pausestopper = new Timer(200f, repeating: false);
		darkener = new Darkener(base.Game, "select", "back");
		pausedScene = new PausedScene(base.Game);
		pausedScene.OnExit += pausedScene_OnExit;
		pausedScene.AddEntry("Continue");
		pausedScene.AddEntryEvent(pausedScene_ContinueSelected);
		pausedScene.AddEntry("Controller Settings");
		pausedScene.AddEntryEvent(pausedScene_PlayerOptionsSelected);
		pausedScene.AddEntry("Instructions");
		pausedScene.AddEntryEvent(pausedScene_InstructionsSelected);
		pausedScene.AddEntry("Exit to Main Menu");
		pausedScene.AddEntryEvent(pausedScene_ExitSelected);
		exitConfirmationMenu = new ConfirmationMenu(base.Game, "Are you sure you want to exit this game session?");
		exitConfirmationMenu.OnExit += exitConfirmationMenu_NoSelected;
		exitConfirmationMenu.AddEntry("Yes");
		exitConfirmationMenu.AddEntryEvent(exitConfirmationMenu_YesSelected);
		exitConfirmationMenu.AddEntry("No");
		exitConfirmationMenu.AddEntryEvent(exitConfirmationMenu_NoSelected);
		playerOptions = new PlayerSettingsMenu(game, darken: false);
		playerOptions.OnExit += playerOptions_OnExit;
		instructionsMenu = new InstructionsMenu(game);
		instructionsMenu.OnExit += instructionsMenu_OnExit;
		spawnType = PlayerSpawnType.South;
	}

	private void exitConfirmationMenu_NoSelected(MenuSub1 sender)
	{
		sender.Remove();
		pausedScene.Show();
	}

	private void exitConfirmationMenu_YesSelected(MenuSub1 sender)
	{
		sender.Remove();
		Collection.Pop();
		Collection.Remove((GameComponent)(object)darkener);
		pausestopper.Start();
		pausestopper.Reset();
		_state = GameState.Nothing;
		Terminate(FinishedMode.exit);
	}

	private void instructionsMenu_OnExit(object sender)
	{
		darkener.SetButtonTips("select", "back");
		instructionsMenu.Unload();
		pausedScene.Show();
		Collection.Remove((GameComponent)(object)instructionsMenu);
	}

	private void playerOptions_OnExit(MenuSub1 sender)
	{
		pausedScene.Show();
		playerOptions.Remove();
	}

	private void eventList_OnCheckPointReached(GameEventList sender)
	{
		score.Save();
	}

	protected void LoseLife()
	{
		if (Settings.GetInstance().DirectRespawn)
		{
			Collection.Purge<PlayerShip>();
			Collection.Purge<PlayerShipSummon>();
			_timer = TimeSpan.Zero;
			_state = GameState.Resetting;
			Settings.GetInstance().ResetDifficulty();
			return;
		}
		xfading = false;
		_state = GameState.Resetting;
		_timer = TimeSpan.Zero;
		Collection.Purge<PlayerShip>();
		Collection.Purge<PlayerShipSummon>();
		if (score.Lives >= 0 && !Settings.GetInstance().InfiniteLives)
		{
			if (score.Lives == 0)
			{
				defeatmessageshown = false;
				_state = GameState.GameOver;
			}
			else
			{
				score.RemoveLife();
			}
		}
	}

	protected void Defeat()
	{
		Terminate(FinishedMode.lostlevel);
	}

	private void Background_OnXFadeFinished()
	{
		xfadedone = true;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			((DrawableGameComponent)_cursor).Visible = false;
		}
	}

	protected virtual void setPresence(GamerPresenceMode presenceMode)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				SignedInGamer current = enumerator.Current;
				if (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Inzane)
				{
					current.Presence.PresenceMode = (GamerPresenceMode)25;
				}
				else
				{
					current.Presence.PresenceMode = presenceMode;
				}
			}
		}
		finally
		{
			((IDisposable)enumerator).Dispose();
		}
	}

	public override void Initialize()
	{
		Settings.GetInstance().AdaptiveDifficulty = false;
		Settings.GetInstance().DirectRespawn = false;
		snapshotMadeThisSession = false;
		snapshottimer.Reset();
		snapshottimer.Start();
		snapshotdelaytimer.Reset();
		snapshotdelaytimer.Stop();
		AIJoinTimer.Reset();
		AIJoinTimer.Start();
		xfading = false;
		_spawnplayernormally = true;
		_wanttochangespawnplayer = false;
		shipCreated = false;
		eventFired = false;
		_state = GameState.Startup;
		_timer = TimeSpan.Zero;
		_cursor = ServiceHelper.Get<IMousePointerService>().MousePointer;
		pausestopper.Reset();
		pausestopper.Stop();
		Background.Reset();
		((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)Background);
		((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)Foreground);
		eventList.Reset();
		Collection.Add((GameComponent)(object)score);
		score.Reset();
		score.Save();
		score.Lives = -1;
		Settings.GetInstance().ResetDifficulty();
		if (oracle.DeviceIsPlaying(ControlDevice.Keyboard))
		{
			((DrawableGameComponent)_cursor).Visible = true;
		}
		base.Initialize();
		lock (Savable.syncObj)
		{
			if (Storage.StorageEnabled)
			{
				StorageContainer val = null;
				try
				{
					val = Storage.StorageDeviceManager.Device.OpenContainer("EvilAliens");
					snapshotExists = File.Exists(val.Path + level.ToString() + ".dat");
				}
				catch (Exception)
				{
				}
				finally
				{
					if (val != null)
					{
						val.Dispose();
					}
				}
			}
			else
			{
				snapshotExists = false;
			}
		}
		GC.Collect();
		PreloadGraphicalContent();
		cheatwarningshown = false;
	}

	private void pausedScene_InstructionsSelected(MenuSub1 sender)
	{
		pausedScene.Remove();
		Collection.Add((GameComponent)(object)instructionsMenu);
		darkener.SetButtonTips("next", "back");
	}

	private void pausedScene_PlayerOptionsSelected(MenuSub1 sender)
	{
		ControlDevice starter;
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
		else
		{
			// Mouse-click activation (Stage 13 made every MenuSub1 entry clickable) presses
			// none of the device keys; on web the mouse is the keyboard player, so default to
			// Keyboard instead of throwing (the old NotSupportedException froze the whole tab).
			starter = ControlDevice.Keyboard;
		}
		playerOptions.Starter = starter;
		pausedScene.Remove();
		playerOptions.Show();
	}

	private void pausedScene_ContinueSelected(MenuSub1 sender)
	{
		pausestopper.Start();
		pausestopper.Reset();
		Collection.Pop();
		Collection.Remove((GameComponent)(object)darkener);
		sender.RemoveInstantly();
	}

	private void pausedScene_ExitSelected(MenuSub1 sender)
	{
		sender.Remove();
		exitConfirmationMenu.Show();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		// Bracket the preload so the LoadProfiler (debug ?loadlog) can tell intended
		// preloads from cold in-game decodes, and so ApplyManifest's loads count as
		// preloads. ApplyManifest warms any extra assets the committed/localStorage
		// manifest lists for this level (the self-improving gap-fill); a no-op with
		// no manifest. All three are cheap no-ops in a release build.
		EvilAliensWeb.Compat.LoadProfiler.BeginPreload(level.ToString());
		PreloadGraphicalContent();
		EvilAliensWeb.Compat.LoadProfiler.ApplyManifest(level.ToString());
		EvilAliensWeb.Compat.LoadProfiler.EndPreload();
	}

	protected virtual void PreloadGraphicalContent()
	{
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0278: Unknown result type (might be due to invalid IL or missing references)
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/bulletevil");
		contentManager.Load<Texture2D>("GFX/Sprites/bulletgood");
		contentManager.Load<Texture2D>("GFX/Sprites/explosion");
		contentManager.Load<Texture2D>("GFX/Sprites/playersheet");
		contentManager.Load<Texture2D>("GFX/Sprites/smoke");
		contentManager.Load<Texture2D>("GFX/Sprites/blast");
		contentManager.Load<Texture2D>("GFX/Sprites/arrow");
		contentManager.Load<Texture2D>("GFX/Sprites/connector");
		contentManager.Load<Texture2D>("GFX/Sprites/option");
		contentManager.Load<Texture2D>("GFX/Sprites/photocamera");
		contentManager.Load<Texture2D>("GFX/Sprites/singleconnectorglow");
		contentManager.Load<Texture2D>("GFX/Sprites/powerupbw");
		contentManager.Load<SpriteFont>("GFX/menu/menufont");
		Explosion explosion = Explosion.NewExplosion(Collection, base.Game);
		explosion.Setup(new Vector2(-1000f, -1000f), 5f, 3f, 0f, 0f);
		Collection.Add((GameComponent)(object)explosion);
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(Collection, base.Game);
		bloodExplosion.Setup(new Vector2(-1000f, -1000f), 5f, 3f, 0f, 0f);
		Collection.Add((GameComponent)(object)bloodExplosion);
		LazerGenerator lazerGenerator = LazerGenerator.NewLazerGenerator(Collection, base.Game);
		lazerGenerator.Setup(new Vector2(-1000f, -1000f), 5f, 3f, 0f, 0f);
		lazerGenerator.SetupSilent();
		Collection.Add((GameComponent)(object)lazerGenerator);
		Lazer lazer = Lazer.NewLazer(Collection, base.Game);
		lazer.SetupSingleShot(new Vector2(-1000f, -1000f), (float)Math.PI, 10f, playSound: false);
		Collection.Add((GameComponent)(object)lazer);
		lazer = Lazer.NewLazer(Collection, base.Game);
		lazer.SetupSingleShot(new Vector2(-1000f, -1000f), (float)Math.PI, 10f, playSound: false);
		Collection.Add((GameComponent)(object)lazer);
		lazer = Lazer.NewLazer(Collection, base.Game);
		lazer.SetupSingleShot(new Vector2(-1000f, -1000f), (float)Math.PI, 10f, playSound: false);
		Collection.Add((GameComponent)(object)lazer);
		u = UFO.NewUFO(Collection, base.Game);
		u.Setup(new Vector2(-1000f, -1000f), isBig: true, EnemyBehaviour.normal);
		Collection.Add((GameComponent)(object)u);
	}

	protected abstract void PopulateEventList();

	protected void Victory()
	{
		System.Console.WriteLine("[trace] GameScene.Victory() level=" + level + " difficulty=" + Settings.GetInstance().CurrentDifficulty);
		_state = GameState.Victory;
		if (!Settings.GetInstance().CheckForCheats())
		{
			Achievements.GetInstance().Data[level].isFinished = true;
			if (Settings.GetInstance().CurrentDifficulty > Achievements.GetInstance().Data[level].difficulty)
			{
				Achievements.GetInstance().Data[level].difficulty = Settings.GetInstance().CurrentDifficulty;
			}
			Achievements.GetInstance().Data[level].hiscore = MathHelper.Max(Achievements.GetInstance().Data[level].hiscore, score.HighScore);
			Achievements.GetInstance().SaveThreaded();
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.Win();
		}
		Background.FadeOut();
		_timer = default(TimeSpan);
	}

	protected void TestBlocks()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		int num = 20;
		for (int i = 0; i < 800 / num; i++)
		{
			for (int j = 0; j < 600 / num; j++)
			{
				TestBlock testBlock = TestBlock.NewTestBlock(Collection, base.Game);
				testBlock.Setup(new Vector2((float)(i * num), (float)(j * num)), new Vector2((float)((i + 1) * num), (float)((j + 1) * num)));
				Collection.Add((GameComponent)(object)testBlock);
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (u != null)
		{
			Collection.Remove((GameComponent)(object)u);
			u = null;
		}
		snapshottimer.Update(gameTime);
		snapshotdelaytimer.Update(gameTime);
		pausestopper.Update(gameTime);
		bool flag = false;
		ControlDevice controlDevice = ControlDevice.AI;
		if ((base.InputHandler.Pressed(MyKeys.Enter) || base.InputHandler.Pressed(MyKeys.Esc)) && oracle.DeviceIsPlaying(ControlDevice.Keyboard))
		{
			flag = true;
			controlDevice = ControlDevice.Keyboard;
		}
		for (int i = 0; i < 4; i++)
		{
			ControlDevice controlDevice2 = i switch
			{
				0 => ControlDevice.PadOne, 
				1 => ControlDevice.PadTwo, 
				2 => ControlDevice.PadThree, 
				3 => ControlDevice.PadFour, 
				_ => throw new Exception(), 
			};
			if (oracle.DeviceIsPlaying(controlDevice2) && (!base.InputHandler.PadConnected(i) || base.InputHandler.PadPressed(PadKeys.Start, i)))
			{
				flag = true;
				controlDevice = controlDevice2;
			}
		}
		if (base.InputHandler.Pressed(MyKeys.Generic_Start) && oracle.DeviceIsPlaying(ControlDevice.Generic))
		{
			flag = true;
			controlDevice = ControlDevice.Generic;
		}
		if (flag & !pausestopper.Active)
		{
			Collection.Push();
			Collection.Add((GameComponent)(object)darkener);
			pausedScene.Reset();
			pausedScene.Setup(controlDevice);
			pausedScene.Show();
			exitConfirmationMenu.Setup(controlDevice);
			return;
		}
		Settings.GetInstance().Update(gameTime);
		switch (_state)
		{
		case GameState.Normal:
			UpdateNormal(gameTime);
			break;
		case GameState.Startup:
			UpdateStartup(gameTime);
			break;
		case GameState.Resetting:
			UpdateResetting(gameTime);
			break;
		case GameState.Victory:
			UpdateWin(gameTime);
			break;
		case GameState.GameOver:
			UpdateGameOver(gameTime);
			break;
		}
		base.Update(gameTime);
		if (_wanttochangespawnplayer)
		{
			_wanttochangespawnplayer = false;
			_spawnplayernormally = true;
		}
	}

	private void UpdateGameOver(GameTime gameTime)
	{
		_timer += gameTime.ElapsedGameTime;
		if ((_timer.TotalSeconds > 4.0) & !defeatmessageshown)
		{
			defeatmessage = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			defeatmessage.Setup("Mission\nFailed", SoundManager.Texts.MissionFailed, AnimatedMessage.MessageType.defeat);
			Collection.Add((GameComponent)(object)defeatmessage);
			defeatmessage.OnFinished += defeatmessage_OnFinished;
			defeatmessageshown = true;
			base.SoundManager.PlayCue("evillaugh");
		}
	}

	private void defeatmessage_OnFinished(object sender)
	{
		Defeat();
	}

	private void pausedScene_OnExit(MenuSub1 sender)
	{
		pausestopper.Start();
		pausestopper.Reset();
		Collection.Remove((GameComponent)(object)darkener);
		Collection.Pop();
		sender.RemoveInstantly();
	}

	private void AddPlayer(ControlDevice controlDevice, bool spawnPlayer)
	{
		oracle.AddPlayer(controlDevice);
		if (spawnPlayer)
		{
			SpawnPlayer(controlDevice);
		}
	}

	private void SpawnPlayer(ControlDevice controlDevice)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		PlayerShip playerShip = Collection.Recycle<PlayerShip>();
		if (playerShip == null)
		{
			playerShip = new PlayerShip(base.Game);
		}
		switch (spawnType)
		{
		case PlayerSpawnType.South:
			playerShip.Setup(oracle.Players - 1, new Vector2(400f, 648f), startup: true, invulnerable: true, 4.712389f);
			break;
		case PlayerSpawnType.West:
			playerShip.Setup(oracle.Players - 1, new Vector2(-48f, 300f), startup: true, invulnerable: true, 0f);
			break;
		case PlayerSpawnType.North:
			playerShip.Setup(oracle.Players - 1, new Vector2(400f, -48f), startup: true, invulnerable: false, (float)Math.PI / 2f);
			break;
		}
		if (controlDevice == ControlDevice.Keyboard)
		{
			((DrawableGameComponent)_cursor).Visible = true;
		}
		Collection.Add((GameComponent)(object)playerShip);
	}

	private void UpdateWin(GameTime gameTime)
	{
		_timer += gameTime.ElapsedGameTime;
		if (_timer.TotalMilliseconds > 4000.0)
		{
			Collection.Purge<AlienDrawableGameComponent>();
		}
		if (_timer.TotalMilliseconds >= 7000.0)
		{
			System.Console.WriteLine("[trace] UpdateWin -> Terminate(finishedlevel) level=" + level);
			Terminate(FinishedMode.finishedlevel);
		}
	}

	private void UpdateResetting(GameTime gameTime)
	{
		if (Settings.GetInstance().DirectRespawn)
		{
			_timer += gameTime.ElapsedGameTime;
			CheckPlayerJoins(spawnPlayer: false);
			if (_timer.TotalSeconds > 3.0)
			{
				SpawnAllPlayers(invulnerable: true);
				_state = GameState.Normal;
			}
			return;
		}
		_timer += gameTime.ElapsedGameTime;
		CheckPlayerJoins(spawnPlayer: false);
		if ((_timer.TotalSeconds > 3.0) & !xfading)
		{
			Background.CrossFade();
			xfading = true;
			xfadedone = false;
		}
		if ((_timer.TotalSeconds > 3.0) & xfadedone)
		{
			Collection.Purge<AlienDrawableGameComponent>();
			Collection.Purge<AnimatedMessage>();
			Collection.Purge<TutorialMessage>();
			shipCreated = false;
			eventFired = false;
			_state = GameState.Startup;
			_timer = TimeSpan.Zero;
			score.Load();
			eventList.RevertToCheckpoint();
			Settings.GetInstance().ResetDifficulty();
			snapshotdelaytimer.Stop();
			snapshotdelaytimer.Reset();
			snapshottimer.Start();
			snapshottimer.Reset();
		}
	}

	private void CheckPlayerJoins(bool spawnPlayer)
	{
		if (base.InputHandler.Pressed(MyKeys.Enter) & !oracle.DeviceIsPlaying(ControlDevice.Keyboard))
		{
			AddPlayer(ControlDevice.Keyboard, spawnPlayer);
		}
		if (base.InputHandler.PadPressed(PadKeys.Start, 0) & !oracle.DeviceIsPlaying(ControlDevice.PadOne))
		{
			AddPlayer(ControlDevice.PadOne, spawnPlayer);
		}
		if (base.InputHandler.PadPressed(PadKeys.Start, 1) & !oracle.DeviceIsPlaying(ControlDevice.PadTwo))
		{
			AddPlayer(ControlDevice.PadTwo, spawnPlayer);
		}
		if (base.InputHandler.PadPressed(PadKeys.Start, 2) & !oracle.DeviceIsPlaying(ControlDevice.PadThree))
		{
			AddPlayer(ControlDevice.PadThree, spawnPlayer);
		}
		if (base.InputHandler.PadPressed(PadKeys.Start, 3) & !oracle.DeviceIsPlaying(ControlDevice.PadFour))
		{
			AddPlayer(ControlDevice.PadFour, spawnPlayer);
		}
		if (base.InputHandler.Pressed(MyKeys.Generic_Start) & !oracle.DeviceIsPlaying(ControlDevice.Generic))
		{
			AddPlayer(ControlDevice.Generic, spawnPlayer);
		}
	}

	protected virtual void UpdateNormal(GameTime gameTime)
	{
		if (General.ScreenshotEnabled(level))
		{
			checkScreenShot();
		}
		AIJoinTimer.Update(gameTime);
		if (AIJoinTimer.Finished && AllowAIFriends && oracle.Players < Settings.GetInstance().Friends + 1 && oracle.Players < 4)
		{
			AddPlayer(ControlDevice.AI, spawnPlayerNormally);
		}
		CheckPlayerJoins(spawnPlayerNormally);
		eventList.Update(gameTime);
		if (oracle.AllShipsDead & spawnPlayerNormally)
		{
			LoseLife();
		}
	}

	protected void Terminate(FinishedMode mode)
	{
		System.Console.WriteLine("[trace] GameScene.Terminate(" + mode + ") level=" + level);
		Collection.Purge<AnimatedMessage>();
		Collection.Purge<TutorialMessage>();
		Collection.Purge<AlienDrawableGameComponent>();
		if (this.OnFinished != null)
		{
			this.OnFinished(this, new FinishedArgs(mode, level));
		}
		Collection.Remove((GameComponent)(object)Background);
		Collection.Remove((GameComponent)(object)score);
		Collection.Remove((GameComponent)(object)this);
		if (snapshotMadeThisSession)
		{
			try
			{
				ScreenshotSaver.SaveScreenShot((Texture2D)(object)MyScreenShot, level);
			}
			catch (Exception)
			{
			}
		}
	}

	private void checkScreenShot()
	{
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		if (snapshotdelaytimer.Finished)
		{
			snapshotdelaytimer.Reset();
			snapshotdelaytimer.Stop();
			float num = 10f;
			if (ScreenShotSpamEnabled)
			{
				num = 100f;
			}
			if (RandomHelper.RandomNextFloat(0f, 100f) <= num || (!snapshotExists && !snapshotMadeThisSession))
			{
				if (game1PostDrawEvent == null)
				{
					game1PostDrawEvent = takeScreenShot;
				}
				Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Combine(Game1.onPostDraw, game1PostDrawEvent);
			}
			else
			{
				score.SnapshotRed();
			}
			snapshottimer.Start();
			snapshottimer.Reset();
		}
		if (snapshottimer.Active)
		{
			return;
		}
		float num2 = 0f;
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent val = item;
			if (!(val is AlienDrawableGameComponent))
			{
				continue;
			}
			Vector2 position = ((AlienDrawableGameComponent)(object)val).Position;
			if (!(position.X > 800f) && !(position.X < 0f) && !(position.Y > 600f) && !(position.Y < 0f))
			{
				num2 += 1f;
				if (val is Explosion)
				{
					num2 += 1f;
				}
				if (val is EvilBullet)
				{
					num2 -= 0.66f;
				}
				if (val is Bullet)
				{
					num2 -= 1f;
				}
				if (val is Lazer)
				{
					num2 += 0.5f;
				}
				if (val is Asteroid && !((Asteroid)(object)val).Collides)
				{
					num2 -= 1f;
				}
				if (val is FlyingSpider && !((FlyingSpider)(object)val).Collides)
				{
					num2 -= 1f;
				}
				if (val is BloodExplosion)
				{
					num2 += 1f;
				}
				if (val is Blast && ((Blast)(object)val).IsMini)
				{
					num2 -= 0.8f;
				}
			}
		}
		if (num2 > 30f)
		{
			snapshotdelaytimer.Start();
		}
	}

	private void takeScreenShot()
	{
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected O, but got Unknown
		Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Remove(Game1.onPostDraw, game1PostDrawEvent);
		if (((Collection<IGameComponent>)(object)base.Game.Components).Contains((IGameComponent)(object)this))
		{
			// Stage 10: the level-select screenshot resolves the scene target, which is now
			// the render-resolution 4:3 target (not the window back buffer) — size it to
			// match so ResolveBackBuffer copies 1:1 and the thumbnail keeps the 4:3 aspect.
			int shotW = EvilAliensWeb.Compat.RenderScale.Width;
			int shotH = EvilAliensWeb.Compat.RenderScale.Height;
			if (MyScreenShot == null || ((Texture2D)MyScreenShot).Width != shotW || ((Texture2D)MyScreenShot).Height != shotH)
			{
				if (MyScreenShot != null)
				{
					((GraphicsResource)MyScreenShot).Dispose();
				}
				MyScreenShot = new ResolveTexture2D(base.GraphicsDevice, shotW, shotH, 1, base.GraphicsDevice.PresentationParameters.BackBufferFormat);
			}
			try
			{
				base.GraphicsDevice.ResolveBackBuffer(MyScreenShot);
			}
			catch (Exception)
			{
			}
			_ = ScreenShotSpamEnabled;
			score.Snapshot();
			snapshotMadeThisSession = true;
		}
	}

	private void UpdateStartup(GameTime gameTime)
	{
		if (!cheatwarningshown && Settings.GetInstance().CheckForCheats())
		{
			AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			animatedMessage.Setup("Warning!\n\nCheats have been enabled.\nProgress will not be saved.", SoundManager.Texts.Nothing, AnimatedMessage.MessageType.cheatwarning);
			Collection.Add((GameComponent)(object)animatedMessage);
			cheatwarningshown = true;
		}
		_timer += gameTime.ElapsedGameTime;
		CheckPlayerJoins(shipCreated);
		if (shipCreated & !eventFired & (this.OnReset != null))
		{
			this.OnReset();
			eventFired = true;
		}
		if ((_timer.TotalMilliseconds > 1300.0) & !shipCreated & spawnPlayerNormally)
		{
			Collection.Purge<AlienDrawableGameComponent>();
			Collection.Purge<AnimatedMessage>();
			Collection.Purge<TutorialMessage>();
			SpawnAllPlayers(invulnerable: false);
			shipCreated = true;
		}
		if (_timer.TotalMilliseconds > 2700.0)
		{
			_state = GameState.Normal;
			_timer = TimeSpan.Zero;
		}
	}

	protected void SpawnAllPlayers(bool invulnerable)
	{
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		if (!isDemo)
		{
			score.ShowStartMessages();
		}
		for (int i = 0; i < oracle.Players; i++)
		{
			if (!oracle.IsAlive(i))
			{
				PlayerShip playerShip = Collection.Recycle<PlayerShip>();
				if (playerShip == null)
				{
					playerShip = new PlayerShip(base.Game);
				}
				switch (spawnType)
				{
				case PlayerSpawnType.South:
					playerShip.Setup(i, new Vector2(800f / ((float)oracle.Players + 1f) * (float)(i + 1), 648f), startup: true, invulnerable: false, 4.712389f);
					break;
				case PlayerSpawnType.West:
					playerShip.Setup(i, new Vector2(-48f, 600f / ((float)oracle.Players + 1f) * (float)(i + 1)), startup: true, invulnerable: false, 0f);
					break;
				case PlayerSpawnType.North:
					playerShip.Setup(i, new Vector2(800f / ((float)oracle.Players + 1f) * (float)(i + 1), -48f), startup: true, invulnerable: false, (float)Math.PI / 2f);
					break;
				}
				if (invulnerable)
				{
					playerShip.TemporaryInvulnerability();
				}
				Collection.Add((GameComponent)(object)playerShip);
				if (oracle.DeviceIsPlaying(ControlDevice.Keyboard))
				{
					((DrawableGameComponent)_cursor).Visible = true;
				}
			}
		}
	}
}
