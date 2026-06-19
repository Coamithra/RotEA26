using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;
using BloomPostprocess;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Game1 : Game
{
	public delegate void PostDrawEvent();

	public static bool Test = false;

	private int exitTicks;

	private bool wantExit;

	private GraphicsDeviceManager graphics;

	private InputHandler inputHandler;

	private SoundManager soundManager;

	private SpriteBatchWrapper spriteBatchWrapper;

	private SpriteBatch spriteBatch;

	private ComponentBin collectionHelper;

	private CollisionHandler collisionHandler;

	private ContentManagerWrapper contentManagerWrapper;

	private ScoreVisualiser score;

	private Oracle oracle;

	private Vibrator vibrator;

	private ContentManager content;

	private StartScreen startScreen;

	private SplashScene splashScene;

	private MenuScene menuScene;

	private CreditsScene creditsScene;

	private NewPreviewScene previewScene;

	private AsteroidChase spaceDodge;

	private BraineroidsLevel braineroids;

	private OwnLevel ownLevel;

	private Level1 level1;

	private Level2 level2;

	private Level3 level3;

	private ClassicAliens classicAliens;

	private InsaneBossI insaneBossI;

	private TeamChallenge teamchallenge;

	private Demo1 demo1;

	private Demo2 demo2;

	private Demo3 demo3;

	private CrazyGame crazyGame;

	private Paratrooper paratrooper;

	private TutorialLevel tutorialLevel;

	private BragScene bragScene;

	private BloomComponent bloom;

	private Texture2D blackPixel;

	private ResolveTexture2D resolveTarget;

	private bool isWideScreen;

	private GamerServicesComponent gamerServicesComponent;

	private MousePointer cursor;

	private static Game1 instance;

	public static PostDrawEvent onPostDraw;

	private AwardmentBlade awardmentBlade;

	private Effect gamma;

	public Game1()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected O, but got Unknown
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e1: Expected O, but got Unknown
		instance = this;
		ServiceHelper.Game = (Game)(object)this;
		graphics = new GraphicsDeviceManager((Game)(object)this);
		content = new ContentManager((IServiceProvider)base.Services, "Content");
		inputHandler = new InputHandler();
		base.Services.AddService(typeof(IInputHandlerService), (object)inputHandler);
		collectionHelper = new ComponentBin((Game)(object)this);
		collisionHandler = new CollisionHandler((Game)(object)this);
		ServiceHelper.Add((IComponentBinService)collectionHelper);
		contentManagerWrapper = new ContentManagerWrapper(content);
		ServiceHelper.Add((IContentManagerService)contentManagerWrapper);
		soundManager = new SoundManager((Game)(object)this);
		base.Services.AddService(typeof(ISoundManagerService), (object)soundManager);
		base.IsFixedTimeStep = false;
		spriteBatchWrapper = new SpriteBatchWrapper((Game)(object)this);
		base.Services.AddService(typeof(ISpriteBatchWrapperService), (object)spriteBatchWrapper);
		cursor = new MousePointer((Game)(object)this);
		ServiceHelper.Add((IMousePointerService)cursor);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)cursor);
		((DrawableGameComponent)cursor).Visible = false;
		score = new ScoreVisualiser((Game)(object)this);
		ServiceHelper.Add((IScoreService)score);
		oracle = new Oracle((Game)(object)this);
		ServiceHelper.Add((IOracleService)oracle);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)oracle);
		vibrator = new Vibrator((Game)(object)this);
		ServiceHelper.Add((IVibratorService)vibrator);
		// graphics.MinimumPixelShaderProfile = (ShaderProfile)4; // removed in XNA 4.0
		bloom = new BloomComponent((Game)(object)this);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)bloom);
		bloom.Settings = BloomSettings.PresetSettings[5];
		ServiceHelper.Add((IBloomService)bloom);
		graphics.PreparingDeviceSettings += graphics_PreparingDeviceSettings;
		gamerServicesComponent = new GamerServicesComponent((Game)(object)this);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)gamerServicesComponent);
	}

	public static List<T> GetEnumValues<T>()
	{
		Type typeFromHandle = typeof(T);
		List<T> list = new List<T>();
		if (typeFromHandle.IsEnum)
		{
			FieldInfo[] fields = typeFromHandle.GetFields(BindingFlags.Static | BindingFlags.Public);
			FieldInfo[] array = fields;
			foreach (FieldInfo fieldInfo in array)
			{
				list.Add((T)fieldInfo.GetValue(null));
			}
		}
		return list;
	}

	private void graphics_PreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
	{
		e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = (RenderTargetUsage)1;
	}

	protected override void Initialize()
	{
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)spriteBatchWrapper);
		startScreen = new StartScreen((Game)(object)this);
		startScreen.OnFinished += startScreen_OnFinished;
		splashScene = new SplashScene((Game)(object)this);
		splashScene.SetTimers(1000, 3000, 1200, 400);
		splashScene.AddSplash("GFX/Splash/uglysplash22");
		splashScene.AddSplash("GFX/Splash/ealogo");
		splashScene.OnFinished += SplashFinished;
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)splashScene);
		demo3 = new Demo3((Game)(object)this);
		demo3.OnFinished += gameScene_OnFinished;
		demo2 = new Demo2((Game)(object)this);
		demo2.OnFinished += gameScene_OnFinished;
		demo1 = new Demo1((Game)(object)this);
		demo1.OnFinished += gameScene_OnFinished;
		ownLevel = new OwnLevel((Game)(object)this);
		ownLevel.OnFinished += gameScene_OnFinished;
		level1 = new Level1((Game)(object)this);
		level1.OnFinished += gameScene_OnFinished;
		level2 = new Level2((Game)(object)this);
		level2.OnFinished += gameScene_OnFinished;
		level3 = new Level3((Game)(object)this);
		level3.OnFinished += gameScene_OnFinished;
		classicAliens = new ClassicAliens((Game)(object)this);
		classicAliens.OnFinished += gameScene_OnFinished;
		insaneBossI = new InsaneBossI((Game)(object)this);
		insaneBossI.OnFinished += gameScene_OnFinished;
		teamchallenge = new TeamChallenge((Game)(object)this);
		teamchallenge.OnFinished += gameScene_OnFinished;
		spaceDodge = new AsteroidChase((Game)(object)this);
		spaceDodge.OnFinished += gameScene_OnFinished;
		braineroids = new BraineroidsLevel((Game)(object)this);
		braineroids.OnFinished += gameScene_OnFinished;
		crazyGame = new CrazyGame((Game)(object)this);
		crazyGame.OnFinished += gameScene_OnFinished;
		paratrooper = new Paratrooper((Game)(object)this);
		paratrooper.OnFinished += gameScene_OnFinished;
		tutorialLevel = new TutorialLevel((Game)(object)this);
		tutorialLevel.OnFinished += gameScene_OnFinished;
		previewScene = new NewPreviewScene((Game)(object)this);
		NewPreviewScene newPreviewScene = previewScene;
		newPreviewScene.onExit = (NewPreviewScene.ExitEvent)Delegate.Combine(newPreviewScene.onExit, new NewPreviewScene.ExitEvent(previewScene_onExit));
		creditsScene = new CreditsScene((Game)(object)this);
		creditsScene.OnFinished += creditsScene_OnFinished;
		bragScene = new BragScene((Game)(object)this);
		bragScene.OnExit += bragScene_onExit;
		awardmentBlade = new AwardmentBlade((Game)(object)this);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)awardmentBlade);
		ServiceHelper.Add((IAwardmentBladeService)awardmentBlade);
		base.Initialize();
	}

	private void startScreen_OnFinished(object sender)
	{
		if (menuScene != null)
		{
			menuScene.CleanUp();
		}
		menuScene = new MenuScene((Game)(object)this);
		menuScene.OnFinished += MenuFinished;
		menuScene.OnFullScreen += GoFullScreen;
		menuScene.OnVSyncChange += menuScene_OnVSyncChange;
		menuScene.OnPreviewSelected += menuScene_previewSelected;
		menuScene.OnResetSelected += menuScene_OnResetSelected;
		menuScene.OnBragSelected += menuScene_OnBragSelected;
		((Collection<IGameComponent>)(object)base.Components).Remove((IGameComponent)(object)startScreen);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)menuScene);
	}

	private void menuScene_OnBragSelected(object sender)
	{
		collectionHelper.Remove((GameComponent)(object)menuScene);
		collectionHelper.Add((GameComponent)(object)bragScene);
	}

	private void menuScene_OnResetSelected(object sender)
	{
		Storage.Reset(this);
	}

	private void bragScene_onExit()
	{
		collectionHelper.Add((GameComponent)(object)menuScene);
		collectionHelper.Remove((GameComponent)(object)bragScene);
	}

	private void creditsScene_OnFinished(object sender, Levels nextlevel)
	{
		collectionHelper.Add((GameComponent)(object)bragScene);
	}

	protected override void UnloadContent()
	{
		base.UnloadContent();
		content.Unload();
	}

	protected override void LoadContent()
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		base.LoadContent();
		spriteBatch = new SpriteBatch(base.GraphicsDevice);
		blackPixel = base.Content.Load<Texture2D>("Content/GFX/Splash/blank");
		PresentationParameters presentationParameters = base.GraphicsDevice.PresentationParameters;
		int backBufferWidth = presentationParameters.BackBufferWidth;
		int backBufferHeight = presentationParameters.BackBufferHeight;
		SurfaceFormat backBufferFormat = presentationParameters.BackBufferFormat;
		resolveTarget = new ResolveTexture2D(base.GraphicsDevice, backBufferWidth, backBufferHeight, 1, backBufferFormat);
		isWideScreen = GraphicsAdapter.DefaultAdapter.IsWideScreen;
		if (!isWideScreen)
		{
			Settings.GetInstance().Scale = 0.9f;
		}
		gamma = base.Content.Load<Effect>("Content/GFX/Effects/gamma");
	}

	public void GoFullScreen(object sender)
	{
		graphics.IsFullScreen = Settings.GetInstance().FullScreen;
		graphics.SynchronizeWithVerticalRetrace = Settings.GetInstance().VSync;
		graphics.ApplyChanges();
	}

	protected void MenuFinished(object sender, ControlDevice starter, Levels selectedLevel)
	{
		collectionHelper.ClearCache();
		collectionHelper.Remove((GameComponent)(object)menuScene);
		oracle.ResetPlayers();
		oracle.AddPlayer(starter);
		bragScene.StoreCompletionProgress();
		switch (selectedLevel)
		{
		case Levels.Tutorial:
			collectionHelper.Add((GameComponent)(object)tutorialLevel);
			break;
		case Levels.Braineroids:
			collectionHelper.Add((GameComponent)(object)braineroids);
			break;
		case Levels.SpaceDodge:
			collectionHelper.Add((GameComponent)(object)spaceDodge);
			break;
		case Levels.OwnLevel:
			collectionHelper.Add((GameComponent)(object)ownLevel);
			break;
		case Levels.Level1:
			collectionHelper.Add((GameComponent)(object)level1);
			break;
		case Levels.Level2:
			collectionHelper.Add((GameComponent)(object)level2);
			break;
		case Levels.Level3:
			collectionHelper.Add((GameComponent)(object)level3);
			break;
		case Levels.ClassicAliens:
			collectionHelper.Add((GameComponent)(object)classicAliens);
			break;
		case Levels.InsaneBossI:
			collectionHelper.Add((GameComponent)(object)insaneBossI);
			break;
		case Levels.TeamChallenge:
			collectionHelper.Add((GameComponent)(object)teamchallenge);
			break;
		case Levels.Demo1:
			collectionHelper.Add((GameComponent)(object)demo1);
			break;
		case Levels.Demo2:
			collectionHelper.Add((GameComponent)(object)demo2);
			break;
		case Levels.Demo3:
			collectionHelper.Add((GameComponent)(object)demo3);
			break;
		case Levels.CrazyGame:
			collectionHelper.Add((GameComponent)(object)crazyGame);
			break;
		case Levels.Paratrooper:
			collectionHelper.Add((GameComponent)(object)paratrooper);
			break;
		default:
			throw new Exception("Level not implemented!");
		}
	}

	private void gameScene_OnFinished(object sender, GameScene.FinishedArgs args)
	{
		switch (args.mode)
		{
		case GameScene.FinishedMode.finishedlevel:
			switch (((GameScene)sender).Level)
			{
			case Levels.Level1:
				creditsScene.SetupLevel1();
				collectionHelper.Add((GameComponent)(object)creditsScene);
				break;
			case Levels.Level2:
				creditsScene.SetupLevel2();
				collectionHelper.Add((GameComponent)(object)creditsScene);
				break;
			case Levels.Level3:
				creditsScene.SetupLevel3();
				collectionHelper.Add((GameComponent)(object)creditsScene);
				break;
			default:
				collectionHelper.Add((GameComponent)(object)menuScene);
				break;
			}
			break;
		case GameScene.FinishedMode.exit:
			collectionHelper.Add((GameComponent)(object)menuScene);
			break;
		case GameScene.FinishedMode.lostlevel:
			collectionHelper.Add((GameComponent)(object)menuScene);
			break;
		}
	}

	private void previewScene_onExit()
	{
		collectionHelper.Remove((GameComponent)(object)previewScene);
		collectionHelper.Add((GameComponent)(object)menuScene);
	}

	protected void SplashFinished(object sender)
	{
		splashScene.Unload();
		((Collection<IGameComponent>)(object)base.Components).Remove((IGameComponent)(object)splashScene);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)startScreen);
	}

	private void menuScene_OnVSyncChange(object sender)
	{
		graphics.SynchronizeWithVerticalRetrace = Settings.GetInstance().VSync;
		graphics.ApplyChanges();
	}

	private void menuScene_previewSelected(object sender, bool showExplanation)
	{
		previewScene.Setup(showExplanation);
		collectionHelper.Remove((GameComponent)(object)menuScene);
		collectionHelper.Add((GameComponent)(object)previewScene);
	}

	protected override void Update(GameTime gameTime)
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		MyDebug.Assert(((Collection<IGameComponent>)(object)base.Components).Contains((IGameComponent)(object)gamerServicesComponent));
		Settings.GetInstance().Update();
		Achievements.GetInstance().Update();
		Unlockables.GetInstance().Update();
		int num = Settings.GetInstance().Turbo;
		if (Guide.IsVisible)
		{
			num = 0;
			vibrator.DisableVibrations();
		}
		float slowmotion = oracle.Slowmotion;
		if (num != 100 || slowmotion != 1f)
		{
			float num2 = (float)num / 100f * slowmotion;
			gameTime = new GameTime(new TimeSpan((long)((float)gameTime.TotalGameTime.Ticks * num2)), new TimeSpan((long)((float)gameTime.ElapsedGameTime.Ticks * num2)));
		}
		if (graphics.IsFullScreen)
		{
			try
			{
				UpdateInner(gameTime);
				return;
			}
			catch (Exception innerException)
			{
				try
				{
					graphics.IsFullScreen = false;
					graphics.ApplyChanges();
				}
				catch (Exception)
				{
				}
				throw new Exception("see inner exception (Error.txt) for details", innerException);
			}
		}
		UpdateInner(gameTime);
	}

	private void UpdateInner(GameTime gameTime)
	{
		if (!wantExit)
		{
			inputHandler.Update();
			((GameComponent)vibrator).Update(gameTime);
			soundManager.Update(gameTime);
			Storage.Update(gameTime, this);
			base.Update(gameTime);
			collectionHelper.Update();
			collisionHandler.DetectCollisions();
		}
		else
		{
			exitTicks++;
			if (exitTicks == 10)
			{
				Thread thread = new Thread(exitFunc);
				thread.Start();
			}
		}
	}

	private void exitFunc()
	{
		Thread.Sleep(1000);
		lock (Savable.syncObj)
		{
			base.Exit();
		}
	}

	protected override void Draw(GameTime gameTime)
	{
		if (oracle.Slowmotion == 1f)
		{
			bloom.Settings = BloomSettings.PresetSettings[5];
		}
		else
		{
			bloom.Settings = BloomSettings.PresetSettings[3];
		}
		if (graphics.IsFullScreen)
		{
			try
			{
				DrawInner(gameTime);
				return;
			}
			catch (Exception innerException)
			{
				try
				{
					graphics.IsFullScreen = false;
					graphics.ApplyChanges();
				}
				catch (Exception)
				{
				}
				throw new Exception("See inner exception (error.txt): ", innerException);
			}
		}
		DrawInner(gameTime);
	}

	private static void Output(string fileName, string data)
	{
		using StreamWriter streamWriter = new StreamWriter(fileName);
		streamWriter.WriteLine(data);
	}

	private void DrawInner(GameTime gameTime)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0244: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		spriteBatchWrapper.Flush();
		if (onPostDraw != null)
		{
			onPostDraw();
		}
		float num = ((!isWideScreen || Settings.GetInstance().Stretch) ? 1f : 0.75f);
		base.GraphicsDevice.ResolveBackBuffer(resolveTarget);
		base.GraphicsDevice.Clear(Color.Black);
		spriteBatchWrapper.Flush();
		gamma.Parameters[0].SetValue(Settings.GetInstance().Gamma);
		spriteBatch.Begin((SpriteBlendMode)0, (SpriteSortMode)0, (SaveStateMode)0);
		gamma.Begin();
		gamma.CurrentTechnique.Passes[0].Begin();
		spriteBatch.Draw((Texture2D)(object)resolveTarget, new Vector2(400f, 300f), (Rectangle?)null, Color.White, 0f, new Vector2((float)(((Texture2D)resolveTarget).Width / 2), (float)(((Texture2D)resolveTarget).Height / 2)), Settings.GetInstance().Scale * new Vector2(num, 1f), (SpriteEffects)0, 0f);
		gamma.CurrentTechnique.Passes[0].End();
		gamma.End();
		spriteBatch.End();
		if (Settings.GetInstance().HideSafeArea)
		{
			Rectangle safeZone = General.SafeZone;
			spriteBatchWrapper.Draw(blackPixel, new Rectangle(0, 0, 800, (safeZone).Top), Color.Black);
			spriteBatchWrapper.Draw(blackPixel, new Rectangle(0, 0, (safeZone).Left, 600), Color.Black);
			spriteBatchWrapper.Draw(blackPixel, new Rectangle(0, (safeZone).Bottom, 800, 600), Color.Black);
			spriteBatchWrapper.Draw(blackPixel, new Rectangle((safeZone).Right, 0, 800, 600), Color.Black);
			spriteBatchWrapper.Flush();
		}
		if (wantExit)
		{
			base.GraphicsDevice.Clear(Color.Black);
		}
	}

	internal void WantExit()
	{
		wantExit = true;
	}

	internal void Reset()
	{
		soundManager.StopMusic();
		List<IGameComponent> list = new List<IGameComponent>();
		foreach (IGameComponent item in (Collection<IGameComponent>)(object)base.Components)
		{
			list.Add(item);
		}
		foreach (IGameComponent item2 in list)
		{
			bool flag = !(item2 is MousePointer);
			flag = flag && !(item2 is Oracle);
			flag = flag && !(item2 is BloomComponent);
			flag = flag && !(item2 is GamerServicesComponent);
			flag = flag && !(item2 is SpriteBatchWrapper);
			flag = flag && !(item2 is Debugger);
			if (flag && !(item2 is AwardmentBlade))
			{
				((Collection<IGameComponent>)(object)base.Components).Remove(item2);
			}
		}
		collectionHelper.FullReset();
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)startScreen);
	}

	internal static void SettingsLoaded()
	{
		instance.GoFullScreen(instance);
		((DrawableGameComponent)instance.bloom).Visible = Settings.GetInstance().Bloom;
	}
}
