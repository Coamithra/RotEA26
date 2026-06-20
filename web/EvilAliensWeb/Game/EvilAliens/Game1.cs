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
using EvilAliensWeb.Compat;

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

	// Stage-4 presenter: the 800x600 design is rendered into this offscreen target each
	// frame, then blitted scaled+letterboxed to KNI's window-sized back buffer (Draw).
	private RenderTarget2D sceneTarget;

	// Native-resolution overlay layer (HiResOverlay). Sized to the window back
	// buffer; high-res art (menu title, channel-flip splash reveal) is composited
	// here AFTER the 800x600 scene blit so it stays crisp instead of being squeezed
	// through the 800x600 presenter. Recreated when the window size changes (Draw).
	private RenderTarget2D overlayTarget;

	// Bloom-on-overlay (the title glow): a half-res ping-pong pair the overlay is blurred
	// through, then composited back additively. Lit only when the Bloom setting is on and
	// a glow-flagged request (the menu title) is present. Recreated with the window size.
	private RenderTarget2D glowTargetA;
	private RenderTarget2D glowTargetB;
	private Effect glowBlur;

	// Premultiplied additive (One/One) for the glow halo — NOT BlendState.Additive, which
	// is SourceAlpha/One (it re-premultiplies and would dim the premultiplied glow).
	private static readonly BlendState PremultipliedAdditive = new BlendState
	{
		ColorSourceBlend = Blend.One,
		ColorDestinationBlend = Blend.One,
		AlphaSourceBlend = Blend.One,
		AlphaDestinationBlend = Blend.One,
	};

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
		// The Xbox 360 original ran on the HiDef profile; KNI defaults a new device to
		// Reach, which rejects 32-bit index buffers (the game uses them) with
		// "Reach profile does not support 32 bit indices". WebGL 2 supports them, so
		// request HiDef to match the original feature set.
		graphics.GraphicsProfile = GraphicsProfile.HiDef;
		// NOTE: do NOT pin PreferredBackBuffer here. KNI's BlazorGL backend forces the
		// back buffer to the browser window size and rewrites PreferredBackBuffer on
		// every resize (GameWindow.OnResize -> UpdateBackBufferSize), so any fixed size
		// gets clobbered. Instead the game renders at its native 800x600 into an offscreen
		// target and Draw() blits that scaled to the window back buffer (see sceneTarget).
		// Web port: load the unpacked PNG/font/curve assets through WebContentManager.
		// `content` is rooted at "Content" (names like "GFX/x"); base.Content is rooted
		// at "" because some call sites ask with a "Content/" prefix. Both normalise to
		// the same wwwroot/Content root inside WebContentManager.
		content = new WebContentManager((IServiceProvider)base.Services, "Content");
		base.Content = new WebContentManager((IServiceProvider)base.Services, "");
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
		// Stage 5 (shaders): bloom is back in the component list (its .fx are ported).
		// It draws at DrawOrder 950 into the presenter target; Visible follows the
		// Bloom setting (default on).
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)bloom);
		((DrawableGameComponent)(object)bloom).Visible = Settings.GetInstance().Bloom;
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
		// Revenge reskin: studio logo first, then the classic "I made this!" meme as
		// the finale (index 1) — where the channel-flip glitch reveals the revenged
		// splash (90% the 4:3 "revenged", ~10% a portrait "pure" shot, 50/50 glasses).
		splashScene.AddSplash("GFX/Splash/ealogo");
		splashScene.AddSplash("GFX/Splash/uglysplash22");
		splashScene.SetChannelFlip(1, "GFX/Splash/uglysplash22-revenged",
			"GFX/Splash/uglysplash22-revenged-pure", "GFX/Splash/uglysplash22-revenged-pure-glasses");
		splashScene.OnFinished += SplashFinished;
		// Debug (?skipsplash / ?menu / ?level=...): jump past the splash sequence straight
		// to the Press Start screen (what SplashFinished would otherwise swap in). Normal
		// boot goes through the splash.
		if (DebugFlags.SkipSplash)
		{
			((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)startScreen);
		}
		else
		{
			((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)splashScene);
		}
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
		// Debug (?level=...): bypass the menu and boot straight into the requested level.
		// menuScene is still created + wired above, so returning from the level (or losing)
		// drops back to a normal menu via gameScene_OnFinished.
		if (DebugFlags.Level.HasValue)
		{
			LaunchLevelDirect(DebugFlags.Level.Value);
		}
		else
		{
			((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)menuScene);
		}
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
		// Stage 5 (shaders): the gamma .fx isn't ported yet. Leave it null; DrawInner
		// skips the gamma/resolve composite when it's missing and presents the scene
		// directly (the back buffer is already the 800x600 design resolution).
		try
		{
			gamma = base.Content.Load<Effect>("Content/GFX/Effects/gamma");
		}
		catch (Exception ex)
		{
			System.Console.WriteLine("[Stage5] gamma effect load failed: " + ex);
			gamma = null;
		}
		try
		{
			glowBlur = base.Content.Load<Effect>("Content/GFX/Effects/glowblur");
		}
		catch (Exception ex)
		{
			System.Console.WriteLine("[overlay] glow blur effect load failed: " + ex);
			glowBlur = null;
		}
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
		LaunchLevel(selectedLevel);
	}

	// Add the GameScene for `selectedLevel` to the live component bin. Shared by the
	// normal menu path (MenuFinished) and the ?level=... debug direct-launch.
	private void LaunchLevel(Levels selectedLevel)
	{
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

	// Debug (?level=...): start a level without going through the menu. Mirrors
	// MenuFinished's player/brag setup, but skips the menuScene removal (it was never
	// shown) and forces a keyboard starter.
	private void LaunchLevelDirect(Levels selectedLevel)
	{
		collectionHelper.ClearCache();
		oracle.ResetPlayers();
		oracle.AddPlayer(ControlDevice.Keyboard);
		bragScene.StoreCompletionProgress();
		LaunchLevel(selectedLevel);
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
				// Web port: no background threads and no real "exit" in a browser tab;
				// the original spun a thread to delay+Exit. Just stop ticking the game.
				lock (Savable.syncObj)
				{
					base.Exit();
				}
			}
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
		// Presenter: render the fixed 800x600 design into an offscreen target, then blit
		// it scaled+letterboxed to KNI's window-sized back buffer. KNI forces the back
		// buffer to the window size on every resize, so scaling at present time (instead
		// of pinning the back buffer) is the only stable approach. The game's many
		// SetRenderTarget(0, null) "return to back buffer" calls are redirected to this
		// target via Xna3GraphicsDeviceCompat.BaseRenderTarget so the whole frame
		// composites at 800x600. (This is also the groundwork for the Stage-5 composite.)
		if (sceneTarget == null || ((GraphicsResource)sceneTarget).IsDisposed)
		{
			sceneTarget = new RenderTarget2D(base.GraphicsDevice, 800, 600, false,
				base.GraphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None,
				0, RenderTargetUsage.PreserveContents);
		}
		Xna3GraphicsDeviceCompat.BaseRenderTarget = sceneTarget;
		base.GraphicsDevice.SetRenderTarget(sceneTarget);

		if (graphics.IsFullScreen)
		{
			try
			{
				DrawInner(gameTime);
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
		else
		{
			DrawInner(gameTime);
		}

		// Present the 800x600 scene target to the real (window-sized) back buffer.
		Xna3GraphicsDeviceCompat.BaseRenderTarget = null;
		base.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
		base.GraphicsDevice.Clear(Color.Black);
		PresentationParameters pp = base.GraphicsDevice.PresentationParameters;
		float scale = Math.Min((float)pp.BackBufferWidth / 800f, (float)pp.BackBufferHeight / 600f);
		int destW = (int)(800f * scale);
		int destH = (int)(600f * scale);
		Rectangle dest = new Rectangle((pp.BackBufferWidth - destW) / 2, (pp.BackBufferHeight - destH) / 2, destW, destH);
		// Stage 5: gamma correction is applied here, on the final present blit.
		// sceneTarget already holds the fully composited 800x600 frame, so applying
		// the gamma pixel shader as the scene is scaled+letterboxed to the window is
		// equivalent to the original full-screen gamma post-process (and avoids the
		// XNA-3.x ResolveBackBuffer round-trip, which the presenter made redundant).
		if (gamma != null)
		{
			gamma.Parameters["Gamma"].SetValue(Settings.GetInstance().Gamma);
		}
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, gamma);
		spriteBatch.Draw((Texture2D)(object)sceneTarget, dest, Color.White);
		spriteBatch.End();

		// Native-res overlay: draw any high-res art (menu title, channel-flip reveal)
		// queued this frame on top of the scaled scene at full window resolution,
		// aligned to the same letterboxed rect so 800x600 design coords line up 1:1.
		PresentHiResOverlay(dest, scale);
	}

	// Drains the per-frame HiResOverlay queue into the window-sized overlay target at
	// native resolution, then composites it over the (already gamma'd) back buffer
	// with matching gamma. Each request's 800x600 design rect is mapped through the
	// SAME scale+offset the presenter used for the scene blit, so overlay art sits
	// pixel-aligned with the 800x600 layer — but sampled once, crisply, from the
	// full-res source. Keeping the overlay in its own target leaves a clean seam for
	// an optional bloom/glow pass on it later (the title's "does it need bloom" pass).
	private void PresentHiResOverlay(Rectangle sceneDest, float sceneScale)
	{
		var queue = HiResOverlay.Queue;
		if (queue.Count == 0)
		{
			HiResOverlay.Clear();
			return;
		}

		GraphicsDevice gd = base.GraphicsDevice;
		PresentationParameters pp = gd.PresentationParameters;
		int w = pp.BackBufferWidth;
		int h = pp.BackBufferHeight;

		if (overlayTarget == null || ((GraphicsResource)overlayTarget).IsDisposed
			|| ((Texture2D)overlayTarget).Width != w || ((Texture2D)overlayTarget).Height != h)
		{
			if (overlayTarget != null && !((GraphicsResource)overlayTarget).IsDisposed)
			{
				((Texture2D)overlayTarget).Dispose();
			}
			// SurfaceFormat.Color (RGBA8): the overlay needs a real alpha channel for
			// the straight->premultiplied composite; Bgr565 back buffers would drop it.
			overlayTarget = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color,
				DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
		}

		// Draw the queued art into the transparent overlay target at native res.
		gd.SetRenderTarget(overlayTarget);
		gd.Clear(Color.Transparent);
		bool anyGlow = false;
		for (int i = 0; i < queue.Count; i++)
		{
			HiResOverlay.Request r = queue[i];
			if (r.Texture == null)
			{
				continue;
			}
			anyGlow |= r.Glow;

			// design-space slot -> window slot (presenter transform).
			float slotX = (float)sceneDest.X + (float)r.DesignRect.X * sceneScale;
			float slotY = (float)sceneDest.Y + (float)r.DesignRect.Y * sceneScale;
			float slotW = (float)r.DesignRect.Width * sceneScale;
			float slotH = (float)r.DesignRect.Height * sceneScale;

			// fit the texture inside the slot.
			float texW = ((Texture2D)r.Texture).Width;
			float texH = ((Texture2D)r.Texture).Height;
			float drawW, drawH;
			switch (r.Fit)
			{
			case OverlayFit.AspectFit:
			{
				float s = Math.Min(slotW / texW, slotH / texH);
				drawW = texW * s;
				drawH = texH * s;
				break;
			}
			case OverlayFit.Cover:
			{
				float s = Math.Max(slotW / texW, slotH / texH);
				drawW = texW * s;
				drawH = texH * s;
				break;
			}
			default:
				drawW = slotW;
				drawH = slotH;
				break;
			}
			drawW *= r.Scale;
			drawH *= r.Scale;

			Vector2 centre = new Vector2(slotX + slotW / 2f, slotY + slotH / 2f);
			Vector2 origin = new Vector2(texW / 2f, texH / 2f);
			Vector2 scaleVec = new Vector2(drawW / texW, drawH / texH);
			Rectangle windowDest = new Rectangle(
				(int)(centre.X - drawW / 2f), (int)(centre.Y - drawH / 2f),
				(int)drawW, (int)drawH);

			r.Configure?.Invoke(r.Effect, windowDest);

			// Premultiplied-alpha convention: overlay source art is premultiplied (the
			// game assets already are; the title is premultiplied at load, the channel
			// flip outputs premultiplied), so composite into the premultiplied overlay
			// target with premultiplied AlphaBlend (matches SpriteBatchWrapper's choke).
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
				SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, r.Effect);
			spriteBatch.Draw(r.Texture, centre, null, r.Tint, r.Rotation, origin, scaleVec,
				SpriteEffects.None, 0f);
			spriteBatch.End();
		}

		// Optional bloom on the overlay (the title glow): blur a half-res copy of the
		// overlay and composite it back additively under the crisp art. Only when the
		// Bloom setting is on and a glow-flagged request (the title) is present.
		bool doGlow = anyGlow && glowBlur != null && Settings.GetInstance().Bloom;
		if (doGlow)
		{
			BuildOverlayGlow(gd, w, h);
		}

		// Composite the overlay over the back buffer, gamma-matched to the scene blit.
		gd.SetRenderTarget((RenderTarget2D)null);
		if (gamma != null)
		{
			gamma.Parameters["Gamma"].SetValue(Settings.GetInstance().Gamma);
		}
		if (doGlow)
		{
			// Additive glow halo first (premultiplied One/One), crisp art drawn over it.
			spriteBatch.Begin(SpriteSortMode.Deferred, PremultipliedAdditive, SamplerState.LinearClamp, null, null, gamma);
			spriteBatch.Draw((Texture2D)(object)glowTargetA, new Rectangle(0, 0, w, h), Color.White * GlowIntensity);
			spriteBatch.End();
		}
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, gamma);
		spriteBatch.Draw((Texture2D)(object)overlayTarget, new Rectangle(0, 0, w, h), Color.White);
		spriteBatch.End();

		HiResOverlay.Clear();
	}

	// Glow strength and blur reach (in half-res texels) for the overlay bloom. Tunable.
	private const float GlowIntensity = 0.65f;
	private const float GlowRadius = 2.0f;

	// Blur a half-res copy of overlayTarget into glowTargetA (downsample -> horizontal
	// blur -> vertical blur), leaving the bloom halo ready to composite additively.
	private void BuildOverlayGlow(GraphicsDevice gd, int w, int h)
	{
		int gw = Math.Max(1, w / 2);
		int gh = Math.Max(1, h / 2);
		if (glowTargetA == null || ((GraphicsResource)glowTargetA).IsDisposed
			|| ((Texture2D)glowTargetA).Width != gw || ((Texture2D)glowTargetA).Height != gh)
		{
			if (glowTargetA != null && !((GraphicsResource)glowTargetA).IsDisposed)
			{
				((Texture2D)glowTargetA).Dispose();
				((Texture2D)glowTargetB).Dispose();
			}
			glowTargetA = new RenderTarget2D(gd, gw, gh, false, SurfaceFormat.Color, DepthFormat.None);
			glowTargetB = new RenderTarget2D(gd, gw, gh, false, SurfaceFormat.Color, DepthFormat.None);
		}

		// downsample overlay -> A (premultiplied content; the title's brightness becomes
		// the glow source, transparent areas contribute nothing)
		gd.SetRenderTarget(glowTargetA);
		gd.Clear(Color.Transparent);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, null);
		spriteBatch.Draw((Texture2D)(object)overlayTarget, new Rectangle(0, 0, gw, gh), Color.White);
		spriteBatch.End();

		// horizontal blur A -> B
		glowBlur.Parameters["Texel"].SetValue(new Vector2(GlowRadius / (float)gw, 0f));
		gd.SetRenderTarget(glowTargetB);
		gd.Clear(Color.Transparent);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, glowBlur);
		spriteBatch.Draw((Texture2D)(object)glowTargetA, new Rectangle(0, 0, gw, gh), Color.White);
		spriteBatch.End();

		// vertical blur B -> A
		glowBlur.Parameters["Texel"].SetValue(new Vector2(0f, GlowRadius / (float)gh));
		gd.SetRenderTarget(glowTargetA);
		gd.Clear(Color.Transparent);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, glowBlur);
		spriteBatch.Draw((Texture2D)(object)glowTargetB, new Rectangle(0, 0, gw, gh), Color.White);
		spriteBatch.End();
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
		// Stage 5 (shaders): the gamma post-process used to composite the resolved
		// back buffer here via ResolveBackBuffer + a full-screen gamma draw. The
		// Stage-4 presenter already renders the whole frame into sceneTarget, so
		// gamma is now applied on the final present blit in Draw() instead (no
		// ResolveBackBuffer round-trip needed). See Draw().
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
