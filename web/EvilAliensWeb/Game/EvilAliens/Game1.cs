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

	// Web-port debug sprite harness (?harness=...): a single frozen object on a space
	// background drawn through the real pipeline. Created in Initialize, launched from
	// startScreen_OnFinished instead of the menu when DebugFlags.Harness is set.
	private EvilAliensWeb.Compat.HarnessScene harnessScene;

	// Web-port bullet showcase (?bulletshot): a frozen tableau of the ship + UFOs + both
	// bullet types on the starfield, for redrawing the bullet sprites. Created in Initialize,
	// launched from startScreen_OnFinished instead of the menu when DebugFlags.Bulletshot is set.
	private EvilAliensWeb.Compat.BulletShowcaseScene bulletShowcaseScene;

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

	private bool isWideScreen;

	private GamerServicesComponent gamerServicesComponent;

	private MousePointer cursor;

	private static Game1 instance;

	public static PostDrawEvent onPostDraw;

	private AwardmentBlade awardmentBlade;

	private Effect gamma;

	// Stage 10 unified presenter: the WHOLE frame — legacy 800x600 art (upscaled via
	// RenderScale.Matrix) AND the hi-res art (menu title, channel-flip splash reveal,
	// drawn at native density) — is rendered into this one offscreen target, sized to
	// the window's 4:3 letterbox (RenderScale.Width x Height). Bloom + gamma operate on
	// it, then Draw blits it letterboxed to KNI's window-sized back buffer. (Replaces
	// the Stage-9 split where hi-res art rode a separate native-res overlay pass.)
	// Recreated when the render size changes (Draw).
	private RenderTarget2D sceneTarget;

	// Cinematic slow-motion motion-trail feedback buffer (ApplySlowmoTrail). Holds an
	// exponential moving average of the scene so moving objects smear into fading ghost
	// trails while the 1up-powerup slowmo is active. Lazily created the first time slowmo
	// engages; recreated on resize (same lifecycle as sceneTarget). slowmoTrailMix ramps
	// the effect in/out (0 = off) so engaging/leaving slowmo doesn't pop.
	private RenderTarget2D slowmoTrail;

	private float slowmoTrailMix;

	// Incremental menu warm: the heavy menu PNG decodes that used to block LoadContent
	// are queued (QueueMenuWarm) and drained one-per-Update-tick during the splash /
	// Press-Start idle time (PumpWarmQueue), with a synchronous drain (DrainWarmQueue)
	// guaranteed before the menu is first built. See QueueMenuWarm for the why.
	private readonly Queue<Action> warmQueue = new Queue<Action>();

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
		// Revenge reskin: studio logo (index 0), then the classic "I made this!" meme
		// (index 1) — where the channel-flip glitch CROSSFADES the old meme into the
		// revenged splash (90% the 4:3 "revenged", ~10% a portrait "pure" shot, 50/50
		// glasses) — and FINALLY a text "confession" (index 2) that lands as the reveal:
		// now that you've seen the game's been messed with, here's what happened. Each
		// text-array entry is a reveal beat that fades in on its own comedic timer.
		splashScene.AddSplash("GFX/Splash/easplashredone");
		splashScene.AddSplash("GFX/Splash/uglysplash22");
		splashScene.SetChannelFlip(1, "GFX/Splash/uglysplash22-revenged",
			"GFX/Splash/uglysplash22-revenged-pure", "GFX/Splash/uglysplash22-revenged-pure-glasses");
		splashScene.AddTextSplash(new string[]
		{
			"This game was lovingly crafted without the use of AI",
			".. in 2008",
			"Then, in 2026, I used a BUNCH of AI",
			"Like, a LOT",
			"I'm sorry :("
		});
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
		harnessScene = new EvilAliensWeb.Compat.HarnessScene((Game)(object)this);
		harnessScene.OnExitToMenu = harnessScene_OnExitToMenu;
		bulletShowcaseScene = new EvilAliensWeb.Compat.BulletShowcaseScene((Game)(object)this);
		bulletShowcaseScene.OnExitToMenu = bulletShowcaseScene_OnExitToMenu;
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
		// Guarantee the menu's art is fully decoded before it's built/shown. The per-tick
		// pump normally finishes warming during the splash; this catches anything still
		// queued (e.g. the splash was mashed past) so the menu never pops in piecemeal.
		DrainWarmQueue();
		// Debug (?invuln): turn the Invulnerability cheat on before any level can spawn a
		// player. Settings has loaded by the time Press Start completes, so this sticks.
		if (DebugFlags.Invuln)
		{
			Settings.GetInstance().Invulnerability = true;
		}
		if (menuScene != null)
		{
			menuScene.CleanUp();
		}
		menuScene = new MenuScene((Game)(object)this);
		menuScene.OnFinished += MenuFinished;
		menuScene.OnFullScreen += GoFullScreen;
		menuScene.OnVSyncChange += menuScene_OnVSyncChange;
		menuScene.OnResetSelected += menuScene_OnResetSelected;
		menuScene.OnBragSelected += menuScene_OnBragSelected;
		((Collection<IGameComponent>)(object)base.Components).Remove((IGameComponent)(object)startScreen);
		// Debug (?harness=...): bypass the menu and boot straight into the sprite harness.
		// menuScene is still created + wired above, so pressing Esc drops back to the menu
		// via harnessScene_OnExitToMenu.
		if (DebugFlags.Harness != null)
		{
			collectionHelper.Add((GameComponent)(object)harnessScene);
		}
		// Debug (?bulletshot): bypass the menu and boot straight into the bullet showcase.
		// menuScene is still wired above, so Esc drops back via bulletShowcaseScene_OnExitToMenu.
		else if (DebugFlags.Bulletshot)
		{
			collectionHelper.Add((GameComponent)(object)bulletShowcaseScene);
		}
		// Debug (?level=...): bypass the menu and boot straight into the requested level.
		// menuScene is still created + wired above, so returning from the level (or losing)
		// drops back to a normal menu via gameScene_OnFinished.
		else if (DebugFlags.Level.HasValue)
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
		// The "brag to a friend" interstitial only displays anything when an Xbox LIVE
		// gamer is signed in — never on the web build (SignedInGamers is empty). When it
		// would immediately fall through to Done it still costs a wasted tick: a bare
		// starfield frame plus a cold content load before it hands to the menu, which is
		// the first visible "stage" of the jarring end-of-level -> menu pop-in. Skip
		// straight to the menu in that case; only route through brag when it would show.
		if (bragScene.WouldShow())
		{
			collectionHelper.Add((GameComponent)(object)bragScene);
		}
		else
		{
			collectionHelper.Add((GameComponent)(object)menuScene);
		}
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
		isWideScreen = GraphicsAdapter.DefaultAdapter.IsWideScreen;
		if (!isWideScreen)
		{
			Settings.GetInstance().Scale = 0.9f;
		}
		// Gamma (Stage 5) is loaded here and applied on the present blit in Draw(). If the
		// load fails it stays null and the present blit simply skips the gamma shader.
		try
		{
			gamma = base.Content.Load<Effect>("Content/GFX/Effects/gamma");
		}
		catch (Exception ex)
		{
			System.Console.WriteLine("[Stage5] gamma effect load failed: " + ex);
			gamma = null;
		}
		QueueMenuWarm();
	}

	// Decode the main menu's art ONCE so the first time the menu is shown it appears in a
	// single frame instead of revealing in ~0.5s stages as each uncached MB-scale PNG (the
	// planet backdrop, the title logo) decodes on the WASM main thread mid-transition. This
	// is what made the end-of-level credits -> menu handoff (a path that never displayed the
	// menu before) pop in piecemeal. It warms the menu's whole first-frame set (plus one deep
	// submenu asset that would otherwise pop on first open — see evilskull below); the heavy
	// decodes are the planet backdrop and the title logo (the MB-scale ones), the rest are
	// cheap but warmed too so the first frame is fully ready. The menu scenes
	// (MenuScene/MenuSub1/MenuSubWithSkull) all load through this one shared content manager
	// (Scene.Content == IContentManagerService.ContentManager == this `content`), whose cache
	// is keyed by resolved path, so warming it here populates the exact entries their Load()
	// calls hit. (CreditsScene uses its OWN content manager, so its bg isn't warmed — but the
	// credits crawl fades its bg in, so a cold decode there isn't the jarring part.)
	//
	// Rather than decode synchronously in LoadContent (which lengthened the black loading
	// screen BEFORE the first splash, while the multi-second splash sequence — the natural
	// place to hide loading — sat idle), the decodes are ENQUEUED here and pumped one-per-
	// Update-tick (PumpWarmQueue) during the splash / Press-Start idle time, then drained
	// synchronously the instant before the menu is first built (DrainWarmQueue in
	// startScreen_OnFinished). So the splash appears sooner and the warm hides behind it,
	// while the "menu is fully warm before it's shown" invariant is preserved on every path
	// (including a player mashing past the whole splash, where the drain catches the rest).
	private void QueueMenuWarm()
	{
		EnqueueWarm<Texture2D>("GFX/Menu/planet");
		EnqueueWarm<Texture2D>("GFX/Menu/title-revenged");
		EnqueueWarm<Texture2D>("GFX/Menu/star");
		EnqueueWarm<Texture2D>("GFX/Menu/blank");
		EnqueueWarm<Texture2D>("GFX/Menu/pointer");
		EnqueueWarm<Texture2D>("GFX/Menu/hudring");
		EnqueueWarm<Texture2D>("GFX/Menu/vignette");
		EnqueueWarm<Texture2D>("GFX/Preview/small_face_a");
		EnqueueWarm<Texture2D>("GFX/Preview/small_face_b");
		EnqueueWarm<SpriteFont>("GFX/Menu/menufont");
		EnqueueWarm<Curve>("GFX/Effects/BrainCurve");
		// Not a first-frame asset: the supersampled skull shown in the awardment text view
		// (Main menu -> Awardments -> select). SubMenuAwardmentText.LoadContent loads it cold
		// on first Show, so that deep submenu popped once as the ~0.4MP PNG decoded on the WASM
		// main thread. Warming it here moves that decode off the first-show path -- a small
		// fixed cost paid every boot (like the rest of this list) to kill the pop.
		EnqueueWarm<Texture2D>("GFX/Menu/evilskull");
	}

	// Queue one asset to be warmed later (during splash idle, or the pre-menu drain).
	private void EnqueueWarm<T>(string assetName)
	{
		warmQueue.Enqueue(() => Warm<T>(assetName));
	}

	// Warm at most ONE queued asset per call — invoked once per Update tick so the heavy
	// MB-scale decodes spread across the splash's idle frames instead of blocking boot.
	private void PumpWarmQueue()
	{
		if (warmQueue.Count > 0)
		{
			warmQueue.Dequeue()();
		}
	}

	// Decode every still-queued asset NOW. Called the instant before the menu is first
	// built so the menu is guaranteed fully warm even if the splash was skipped before the
	// per-tick pump could finish — worst case this is the old synchronous batch decode.
	private void DrainWarmQueue()
	{
		while (warmQueue.Count > 0)
		{
			warmQueue.Dequeue()();
		}
	}

	// Best-effort warm of a single asset into the shared content manager's cache. Guarded
	// per-asset (not as one batch) so a single missing or unreadable asset can't abort
	// warming the others — and must never block boot.
	private void Warm<T>(string assetName)
	{
		try
		{
			content.Load<T>(assetName);
		}
		catch (Exception ex)
		{
			System.Console.WriteLine("[menu-warm] " + assetName + " warm failed: " + ex.Message);
		}
	}

	public void GoFullScreen(object sender)
	{
		// Web port (Stage 9): drive the browser Fullscreen API via JS interop rather than
		// KNI's graphics.IsFullScreen — BlazorGL doesn't honour it (and toggling it can be
		// unsupported). The canvas already fills the window and Draw() letterboxes the fixed
		// 800x600 scene, so fullscreen needs no graphics changes beyond applying VSync. Kept
		// graphics.IsFullScreen at its default (false) so Update/Draw take the plain path.
		try
		{
			graphics.SynchronizeWithVerticalRetrace = Settings.GetInstance().VSync;
			graphics.ApplyChanges();
		}
		catch (Exception)
		{
		}
		EvilAliensWeb.Compat.FullscreenInterop.Set(Settings.GetInstance().FullScreen);
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

	// Esc out of the sprite harness: drop the harness (and the object + background it
	// added) and show the normal menu.
	private void harnessScene_OnExitToMenu()
	{
		harnessScene.Teardown();
		collectionHelper.Remove((GameComponent)(object)harnessScene);
		collectionHelper.Add((GameComponent)(object)menuScene);
	}

	private void bulletShowcaseScene_OnExitToMenu()
	{
		bulletShowcaseScene.Teardown();
		collectionHelper.Remove((GameComponent)(object)bulletShowcaseScene);
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
			// Warm one queued menu asset per tick, hiding the heavy decodes behind the
			// splash / Press-Start idle time (see QueueMenuWarm). No-op once drained.
			PumpWarmQueue();
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
		if (DebugToggles.Active)
		{
			((DrawableGameComponent)(object)bloom).Visible = DebugToggles.Bloom;
		}
		// Stage 10 unified presenter: render the WHOLE frame into one offscreen target
		// sized to the window's 4:3 letterbox, then blit it letterboxed to KNI's
		// window-sized back buffer. KNI forces the back buffer to the window size on
		// every resize, so scaling at present time (instead of pinning the back buffer)
		// is the only stable approach. The game's many SetRenderTarget(0, null) "return
		// to back buffer" calls are redirected to this target via
		// Xna3GraphicsDeviceCompat.BaseRenderTarget so the whole frame composites here;
		// legacy 800x600 draws are scaled up by RenderScale.Matrix and hi-res art is
		// drawn at native density, sharing one bloom + gamma + present blit.
		PresentationParameters pp = base.GraphicsDevice.PresentationParameters;
		RenderScale.Update(pp.BackBufferWidth, pp.BackBufferHeight);
		if (sceneTarget == null || ((GraphicsResource)sceneTarget).IsDisposed
			|| ((Texture2D)sceneTarget).Width != RenderScale.Width
			|| ((Texture2D)sceneTarget).Height != RenderScale.Height)
		{
			if (sceneTarget != null && !((GraphicsResource)sceneTarget).IsDisposed)
			{
				((GraphicsResource)sceneTarget).Dispose();
			}
			sceneTarget = new RenderTarget2D(base.GraphicsDevice, RenderScale.Width, RenderScale.Height, false,
				pp.BackBufferFormat, DepthFormat.None,
				0, RenderTargetUsage.PreserveContents);
		}
		Xna3GraphicsDeviceCompat.BaseRenderTarget = sceneTarget;
		base.GraphicsDevice.SetRenderTarget(sceneTarget);
		// Clear the scene target to black every frame. It's a PreserveContents target (so
		// within-frame SetRenderTarget round-trips for bloom/cross-fade keep their content),
		// which means it is NOT auto-cleared between frames. The legacy backgrounds fully
		// repainted it with an opaque base layer, so that was invisible; the new additive
		// ProceduralStarfield only ADDS, so without this clear it accumulates frame-over-
		// frame and runs away to white (unbounded with the veil off; ~3x with it on).
		base.GraphicsDevice.Clear(Color.Black);

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

		// Cinematic slow-motion ghost trails: post-process the fully composited (and
		// bloomed) frame in sceneTarget before the present blit. No-op unless the 1up
		// slowmo is active (and ramping). Leaves the render target on sceneTarget, which
		// the present block immediately switches off below.
		ApplySlowmoTrail(gameTime);

		// Present the scene target to the real (window-sized) back buffer, letterboxed.
		Xna3GraphicsDeviceCompat.BaseRenderTarget = null;
		base.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
		base.GraphicsDevice.Clear(Color.Black);
		// Letterbox geometry from the single source of truth (RenderScale), so the present
		// blit and the inverse mouse mapping (WindowToDesign) round identically.
		Rectangle dest = RenderScale.WindowDestRect(pp.BackBufferWidth, pp.BackBufferHeight);
		// Gamma correction is applied here, on the final present blit. sceneTarget holds
		// the fully composited frame (legacy + hi-res, bloomed). Blitting it through the
		// gamma pixel shader as it's scaled+letterboxed to the window is equivalent to a
		// full-screen gamma post-process. The blit is 1:1 when the render size equals the
		// letterbox (uncapped); a bilinear upscale when RenderScale's height cap kicks in.
		Effect gx = (DebugToggles.Active && !DebugToggles.Gamma) ? null : gamma;
		if (gx != null)
		{
			gx.Parameters["Gamma"].SetValue(Settings.GetInstance().Gamma);
		}
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, gx);
		spriteBatch.Draw((Texture2D)(object)sceneTarget, dest, Color.White);
		spriteBatch.End();
	}

	// Cinematic slow-motion motion blur ("ghost trails"). The base slowmo (1up powerup ->
	// Oracle.SetSlowmotion) only scales game time + swaps a bloom preset; this adds a real
	// movie bullet-time smear on top. Technique: a frame-feedback / accumulation buffer
	// (the established post-process motion-blur approach) -- slowmoTrail holds an exponential
	// moving average of the scene (trail = trail*decay + scene*(1-decay)), which is then mixed
	// back over the crisp current frame as scene = lerp(scene, trail, k). Because the EMA
	// converges to the input for a STATIC pixel, still areas (HUD, idle sprites) are left
	// unchanged -- only moving objects, where the trail lags the current frame, leave fading
	// echoes in the direction of motion. slowmoTrailMix eases the whole thing in/out so
	// engaging/leaving slowmo doesn't pop. Runs after DrawInner, so it post-processes the
	// already-bloomed sceneTarget (the ghosts carry the glow too).
	private void ApplySlowmoTrail(GameTime gameTime)
	{
		if (!DebugFlags.SlowmoTrail)
		{
			return;
		}
		bool active = oracle.Slowmotion != 1f;
		bool wasZero = slowmoTrailMix <= 0f;
		// dt-correct the two per-frame constants below (ease 0.15, decay 0.88) so the trail
		// looks the same at any refresh rate — IsFixedTimeStep is false, so a 120Hz display
		// would otherwise ease twice as fast and decay twice as much per frame (half-length
		// trails). `frames` re-expresses the real frame delta in 60Hz-frame units; clamped so
		// a stall (tab refocus, GC hitch) can't over-correct into a black flash.
		float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
		if (dt <= 0f) dt = 1f / 60f;
		if (dt > 0.1f) dt = 0.1f;
		float frames = dt * 60f;
		// Ease toward fully-on (active) or fully-off; ~0.15/60Hz-frame is a snappy ~0.25s ramp.
		float target = active ? 1f : 0f;
		float easeAlpha = 1f - (float)Math.Pow(1.0 - 0.15, frames);
		slowmoTrailMix += (target - slowmoTrailMix) * easeAlpha;
		if (slowmoTrailMix < 0.004f)
		{
			slowmoTrailMix = 0f;
			return;
		}
		if (slowmoTrailMix > 1f)
		{
			slowmoTrailMix = 1f;
		}

		bool seed = wasZero && active;
		if (slowmoTrail == null || ((GraphicsResource)slowmoTrail).IsDisposed
			|| ((Texture2D)slowmoTrail).Width != RenderScale.Width
			|| ((Texture2D)slowmoTrail).Height != RenderScale.Height)
		{
			if (slowmoTrail != null && !((GraphicsResource)slowmoTrail).IsDisposed)
			{
				((GraphicsResource)slowmoTrail).Dispose();
			}
			PresentationParameters pp = base.GraphicsDevice.PresentationParameters;
			slowmoTrail = new RenderTarget2D(base.GraphicsDevice, RenderScale.Width, RenderScale.Height, false,
				pp.BackBufferFormat, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
			seed = true;
		}

		// The tunable decay is authored per 60Hz-frame; raise it to `frames` so the effective
		// per-frame decay yields a frame-rate-independent trail length. Everything downstream
		// (the black decay draw + the additive feed's 1-decay) reads this corrected value.
		float decay = (float)Math.Pow(DebugFlags.SlowmoTrailDecay ?? 0.88f, frames);
		float strength = DebugFlags.SlowmoTrailStrength ?? 0.8f;
		float k = strength * slowmoTrailMix;
		Rectangle full = new Rectangle(0, 0, RenderScale.Width, RenderScale.Height);

		base.GraphicsDevice.SetRenderTarget(slowmoTrail);
		if (seed)
		{
			// First slowmo frame: seed the trail with the current frame so the lerp below
			// doesn't briefly darken the image while the buffer fills from black.
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
			spriteBatch.Draw((Texture2D)(object)sceneTarget, full, Color.White);
			spriteBatch.End();
		}
		else
		{
			// trail *= decay  (NonPremultiplied black at alpha (1-decay): dest*decay + 0).
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
			spriteBatch.Draw(blackPixel, full, new Color(0f, 0f, 0f, 1f - decay));
			spriteBatch.End();
			// trail.rgb += scene*(1-decay)  (Additive; sceneTarget alpha is ~1 everywhere).
			// The trail's ALPHA channel is intentionally let run (additive add of ~1/frame),
			// so it stays saturated at 1 on this 8-bit UNORM render target (pp.BackBufferFormat
			// is never a float format on BlazorGL/WebGL). The composite below depends on that:
			// trail.a == 1 makes the NonPremultiplied lerp's effective alpha == k.
			float w = 1f - decay;
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
			spriteBatch.Draw((Texture2D)(object)sceneTarget, full, new Color(w, w, w, 1f));
			spriteBatch.End();
		}

		// scene = lerp(scene, trail, k). NonPremultiplied draws trail over scene with effective
		// alpha = trail.a * k; trail.a is saturated to 1 (see the feed step), so this is exactly
		// scene*(1-k) + trail*k.
		base.GraphicsDevice.SetRenderTarget(sceneTarget);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
		spriteBatch.Draw((Texture2D)(object)slowmoTrail, full, new Color(1f, 1f, 1f, k));
		spriteBatch.End();
	}

	private static void Output(string fileName, string data)
	{
		using StreamWriter streamWriter = new StreamWriter(fileName);
		streamWriter.WriteLine(data);
	}

	private void DrawInner(GameTime gameTime)
	{
		// Stage 13: feed the chrome-sheen glint clock once per frame so every DrawMetalString
		// call site (the bespoke menu renderers) animates without needing GameTime in scope.
		spriteBatchWrapper.MetalTime = (float)gameTime.TotalGameTime.TotalSeconds;
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
