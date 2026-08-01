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

	// The live collision handler — for the eaBinTest console suite (Compat/BinTest.cs), whose
	// mid-pass-spawn scenarios drive DetectCollisions() directly. Same one-line accessor
	// pattern as ComponentBin.Game.
	internal CollisionHandler CollisionHandler => collisionHandler;

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

	// Debug (?texviewer): the per-sprite DXT-vs-RAW texture-format viewer. Like harnessScene it's
	// constructed + wired below, and startScreen_OnFinished routes into it (instead of the menu)
	// when DebugFlags.TexViewer is set.
	private EvilAliensWeb.Compat.TexViewerScene texViewerScene;

	// Web-port laser showcase (?lazershot): a LIVE (animating) stage — chargeup swarm + a
	// full-grown beam on the starfield — for tuning the laser FX. Created in Initialize,
	// launched from startScreen_OnFinished instead of the menu when DebugFlags.Lazershot is set.
	private EvilAliensWeb.Compat.LazerShowcaseScene lazerShowcaseScene;

	// Web-port text showcase (?textshot): a FROZEN grid of the flattened HUD text (score /
	// combo / POWER UP pop, plain + chrome) so one screenshot judges the text rendering.
	// Created in Initialize, launched from startScreen_OnFinished when DebugFlags.Textshot is set.
	private EvilAliensWeb.Compat.TextShowcaseScene textShowcaseScene;

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

	private WebcamLevel webcamLevel;

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

	// Stage 10 unified presenter: the WHOLE frame — legacy 800x600 art (upscaled via
	// RenderScale.Matrix) AND the hi-res art (menu title, channel-flip splash reveal,
	// drawn at native density) — is rendered into this one offscreen target, sized to
	// the window's 4:3 letterbox (RenderScale.Width x Height). Bloom operates on
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

	// Tutorial holo-sim fullscreen filter (Compat/HoloSim + holosim.fx): scanlines + edge
	// cyan cast + channel-surf glitch bursts while the trial-simulation runs. Applied in
	// ApplyHoloSim on the same seam as the slowmo trail; ping-pongs through this RT (a
	// SpriteBatch effect pass can't read and write sceneTarget at once). Lazily created on
	// first use; recreated on resize (same lifecycle as slowmoTrail).
	private RenderTarget2D holoRT;

	private Effect holoSim;

	// Cached holosim.fx params (avoid per-frame string-keyed Parameters[] lookups, same
	// convention as SpriteBatchWrapper.SetMetalParams; null-conditional sets degrade
	// gracefully if a compiled variant ever drops one).
	private EffectParameter hsIntensity;

	private EffectParameter hsGreen;

	private EffectParameter hsBurst;

	private EffectParameter hsTime;

	// Incremental menu warm: the heavy menu PNG decodes that used to block LoadContent
	// are queued (QueueMenuWarm) and drained one-per-Update-tick during the splash /
	// Press-Start idle time (PumpWarmQueue), with a synchronous drain (DrainWarmQueue)
	// guaranteed before the menu is first built. See QueueMenuWarm for the why.
	private readonly Queue<Action> warmQueue = new Queue<Action>();

	// Low-priority warm: assets a level will want but the MENU doesn't (currently the
	// space-background tile set — see QueueIdleWarm). Pumped one-per-tick only after
	// warmQueue is empty, and deliberately NOT part of DrainWarmQueue: a player mashing
	// past the splash must never wait on these before the menu shows. Worst case (a
	// level entered before the queue drains) the leftovers decode where they always did
	// — synchronously in Background.SetSpace() on the level's loading tick — and the
	// queued entries become free cache hits afterwards; either order is safe.
	// Known minor edge: a ?level= boot into a NON-space level (e.g. Level2/Mars) never
	// calls SetSpace, so the leftovers trickle-decode one-per-tick during early gameplay
	// (each sub-watchdog, ~40ms .dds / ~20ms star PNG). Debug-only boots; accepted.
	// They no longer log as COLD against that level (card 4d47c5ba brackets every warm),
	// and LoadProfiler.RecordTexture now DROPS a warm that lands outside the (boot)
	// sentinel rather than filing it under the level -- so eaPreloadExport can no longer
	// bake the space tile set into a non-space level's manifest section from such a run,
	// which is what the COLD lines used to be the (noisy) warning about.
	private readonly Queue<Action> idleWarmQueue = new Queue<Action>();

	// Pre-launch LEVEL warm (card fe25712a): a level's whole preload used to decode
	// inside ONE JS-driven tick (GameScene.LoadContent runs synchronously when the
	// scene is Added), starving the browser event loop for seconds -> Chrome's "page
	// unresponsive" popup. Before the level component is added, WarmThenLaunch queues
	// the level's manifest texture set (LoadProfiler.ManifestAssets) here and
	// PumpLevelWarm decodes ONE per tick — tickJS returns to rAF between decodes, so
	// the browser paints/responds throughout — then runs pendingLevelLaunch (the
	// actual scene Add). The level's own PreloadGraphicalContent/ApplyManifest stay
	// unchanged and synchronous; the warmed textures are cache hits, so the remaining
	// blocking tick is short. A level with no manifest entries launches immediately
	// (today's behaviour — self-healing fallback).
	private readonly Queue<Action> levelWarmQueue = new Queue<Action>();

	private Action pendingLevelLaunch;

	public Game1()
	{
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
		// ?mute: the two audio subsystems share no bus, so silencing takes both switches.
		// Here rather than in DebugFlags.Parse (which runs before the game exists, so the
		// audio device is not up) and rather than SoundManager's ctor (which runs before the
		// content service is registered). Applied once at boot; nothing re-reads the flag,
		// so an unmuted build is byte-identical.
		if (DebugFlags.Mute)
		{
			Microsoft.Xna.Framework.Audio.SoundEffect.MasterVolume = 0f;   // SFX + speech
			MusicInterop.SetMute(true);                                    // WebAudio music bus
			Console.WriteLine("[debug] ?mute is on -- SFX, speech and music are all silenced for this boot.");
		}
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
		webcamLevel = new WebcamLevel((Game)(object)this);
		webcamLevel.OnFinished += gameScene_OnFinished;
		tutorialLevel = new TutorialLevel((Game)(object)this);
		tutorialLevel.OnFinished += gameScene_OnFinished;
		harnessScene = new EvilAliensWeb.Compat.HarnessScene((Game)(object)this);
		harnessScene.OnExitToMenu = harnessScene_OnExitToMenu;
		bulletShowcaseScene = new EvilAliensWeb.Compat.BulletShowcaseScene((Game)(object)this);
		bulletShowcaseScene.OnExitToMenu = bulletShowcaseScene_OnExitToMenu;
		texViewerScene = new EvilAliensWeb.Compat.TexViewerScene((Game)(object)this);
		texViewerScene.OnExitToMenu = texViewerScene_OnExitToMenu;
		lazerShowcaseScene = new EvilAliensWeb.Compat.LazerShowcaseScene((Game)(object)this);
		lazerShowcaseScene.OnExitToMenu = lazerShowcaseScene_OnExitToMenu;
		textShowcaseScene = new EvilAliensWeb.Compat.TextShowcaseScene((Game)(object)this);
		textShowcaseScene.OnExitToMenu = textShowcaseScene_OnExitToMenu;
		creditsScene = new CreditsScene((Game)(object)this);
		creditsScene.OnFinished += creditsScene_OnFinished;
		bragScene = new BragScene((Game)(object)this);
		bragScene.OnExit += bragScene_onExit;
		awardmentBlade = new AwardmentBlade((Game)(object)this);
		((Collection<IGameComponent>)(object)base.Components).Add((IGameComponent)(object)awardmentBlade);
		ServiceHelper.Add((IAwardmentBladeService)awardmentBlade);
		// Online co-op (?net=host / ?net=join): open the loopback session. Start() is a
		// no-op when no ?net flag was parsed, so a plain boot constructs nothing net-side.
		EvilAliensWeb.Compat.Net.NetSession.Start((Game)(object)this);
		base.Initialize();
	}

	private void startScreen_OnFinished(object sender)
	{
		// Guarantee the menu's art is fully decoded before it's built/shown. The per-tick
		// pump normally finishes warming during the splash; this catches anything still
		// queued (e.g. the splash was mashed past) so the menu never pops in piecemeal.
		DrainWarmQueue();
		// Debug (?invuln): this used to write Settings.GetInstance().Invulnerability = true
		// here, which PERSISTED into the localStorage save on the next Settings.SaveThreaded()
		// (options exit, difficulty pick, etc.) -- so one test session with ?invuln left every
		// LATER plain boot invulnerable forever (the field is otherwise unreachable in the
		// shipped UI -- see PlayerShip.CollidesWith / WebcamLevel.PlayerHit, which now read
		// DebugFlags.Invuln directly instead). Do NOT reintroduce a write to Settings here --
		// the flag must stay a session-only runtime override, like ?unlockall.
		// Debug (?difficulty=Easy..Inzane): pin the difficulty before any level boots. The
		// spider-boss helper's speed + aim are difficulty-scaled, so this makes the test
		// deterministic. No flag => the saved/menu-chosen difficulty is left untouched.
		if (DebugFlags.Difficulty.HasValue)
		{
			Settings.GetInstance().CurrentDifficulty = DebugFlags.Difficulty.Value;
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
		// Debug (?castbrain): reuse the harness scene to show the end-credits Cast screen parked
		// on the animated "Brain Spawn" entry (HarnessScene handles the cast-brain mode). Esc
		// drops back to the menu via harnessScene_OnExitToMenu, same as the sprite harness.
		else if (DebugFlags.CastBrain)
		{
			collectionHelper.Add((GameComponent)(object)harnessScene);
		}
		// Debug (?cast): reuse the harness scene to run the FULL end-credits Cast screen
		// (HarnessScene handles the full-cast mode). Esc drops back to the menu, same as above.
		else if (DebugFlags.CastShow)
		{
			collectionHelper.Add((GameComponent)(object)harnessScene);
		}
		// Debug (?texviewer): bypass the menu and boot straight into the texture-format viewer.
		// menuScene is still wired above, so Esc drops back via texViewerScene_OnExitToMenu.
		else if (DebugFlags.TexViewer)
		{
			collectionHelper.Add((GameComponent)(object)texViewerScene);
		}
		// Debug (?bulletshot): bypass the menu and boot straight into the bullet showcase.
		// menuScene is still wired above, so Esc drops back via bulletShowcaseScene_OnExitToMenu.
		else if (DebugFlags.Bulletshot)
		{
			collectionHelper.Add((GameComponent)(object)bulletShowcaseScene);
		}
		// Debug (?lazershot): bypass the menu and boot straight into the live laser showcase.
		// menuScene is still wired above, so Esc drops back via lazerShowcaseScene_OnExitToMenu.
		else if (DebugFlags.Lazershot)
		{
			collectionHelper.Add((GameComponent)(object)lazerShowcaseScene);
		}
		// Debug (?textshot): bypass the menu and boot straight into the frozen text showcase.
		// menuScene is still wired above, so Esc drops back via textShowcaseScene_OnExitToMenu.
		else if (DebugFlags.Textshot)
		{
			collectionHelper.Add((GameComponent)(object)textShowcaseScene);
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
		base.LoadContent();
		spriteBatch = new SpriteBatch(base.GraphicsDevice);
		blackPixel = base.Content.Load<Texture2D>("Content/GFX/Splash/blank");
		isWideScreen = GraphicsAdapter.DefaultAdapter.IsWideScreen;
		if (!isWideScreen)
		{
			Settings.GetInstance().Scale = 0.9f;
		}
		// Tutorial holo-sim filter; null on failure = filter silently off, game unaffected.
		try
		{
			holoSim = base.Content.Load<Effect>("Content/GFX/Effects/holosim");
			hsIntensity = holoSim.Parameters["Intensity"];
			hsGreen = holoSim.Parameters["Green"];
			hsBurst = holoSim.Parameters["Burst"];
			hsTime = holoSim.Parameters["Time"];
		}
		catch (Exception ex)
		{
			System.Console.WriteLine("[holosim] effect load failed: " + ex);
			holoSim = null;
		}
		QueueMenuWarm();
		QueueIdleWarm();
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
		// The level-select carousel's stock art (card 4d47c5ba). ScreenshotSaver.Init() loads
		// all twelve SYNCHRONOUSLY the instant Start is pressed -- it runs from
		// StartScreen.Update immediately BEFORE OnFinished, i.e. before DrainWarmQueue -- so
		// they used to land as one ~350-470ms block on the Press Start -> menu handoff, the
		// last thing between the player and the menu. Queued here they are pumped during the
		// splash (which has ~1000 idle ticks for a queue of ~24) and Init() becomes cache hits.
		//
		// Queued AFTER the menu-critical set above on purpose: warmQueue is FIFO and
		// DrainWarmQueue's contract is "the menu's own art is decoded before the menu is
		// built", so nothing may be inserted ahead of it. The drain does no work for these
		// either way -- Init() has already decoded whatever the pump did not reach.
		//
		// ScreenshotSaver owns the list so the warm set and the load set cannot drift.
		foreach (string stockShot in ScreenshotSaver.StockShots)
		{
			EnqueueWarm<Texture2D>(stockShot);
		}
	}

	// Space-background tile set (card 97727578). Background.SetSpace() loads all of these
	// synchronously inside a level's Initialize() — BEFORE base.Initialize() reaches the
	// LoadContent preload bracket — so PreloadGraphicalContent cannot warm them first. (Nor
	// could the manifest when this was written; WarmThenLaunch's pre-launch warm now runs
	// BEFORE Initialize, so a manifest entry does reach these — that is how the identical
	// SetMars/marsbg gap is fixed, card 74b30beb. Boot-warming stays the better fit here:
	// SetSpace is used by most levels, so paying once per session beats listing the set on
	// every one of them.) The FIRST space scene of a session paid ~0.5s extra on its loading
	// tick (12 nebula .dds uploads + 8 star PNG decodes + a shader compile). The shared
	// content manager never unloads mid-session, so warming once at boot turns every SetSpace
	// into cache hits. Low-priority (idleWarmQueue): needed before the first LEVEL, not
	// before the menu.
	private void QueueIdleWarm()
	{
		for (int i = 0; i < 12; i++)
		{
			EnqueueIdleWarm<Texture2D>($"GFX/Game/space/space{i:00}");
		}
		for (int i = 0; i < 8; i++)
		{
			EnqueueIdleWarm<Texture2D>($"GFX/Game/space/star{i:00}");
		}
		// ProceduralStarfield's crossfade shader — same first-SetSpace load moment.
		EnqueueIdleWarm<Effect>("GFX/Effects/starwindow");
		// The two 1548x1188 control diagrams (card 4d47c5ba). HelpText (every attract demo)
		// and InstructionsMenu (every in-level pause -> Instructions) draw them, and both used
		// to own a PRIVATE WebContentManager that they Unload()ed on removal -- so the pair was
		// re-decoded on every single showing, forever. Both now read them from this shared
		// manager, which is what makes warming them possible at all; a per-level manifest entry
		// never could (tried and reverted in card 74b30beb: the manifest warms Game1.content,
		// and their copies lived in a different cache).
		//
		// IDLE queue, not the menu queue: neither screen is menu-first-frame art, and
		// DrainWarmQueue is synchronous -- putting 2 multi-megapixel decodes there would make a
		// player who mashes past the splash WAIT for art that is not on screen yet, trading a
		// hidden warm for a visible one. Same reasoning as the space tiles above.
		EnqueueIdleWarm<Texture2D>("GFX/Help/Controls_Keyboard");
		EnqueueIdleWarm<Texture2D>("GFX/Help/Controls_Joypad");
		// The awardment banner's sheet (card 57555583). AwardmentBlade used to decode it in its
		// own LoadContent -- i.e. inside base.Initialize(), BEFORE this method exists to warm
		// anything -- so the boot always paid for a component that only draws when an awardment
		// pops. Its load is lazy now and this is what keeps that lazy load a cache hit.
		//
		// IDLE queue, not the menu queue: the banner pops mid-LEVEL, it is not menu-first-frame
		// art, and DrainWarmQueue is synchronous -- adding it to the menu queue would put its
		// decode back on the Press-Start -> menu handoff that card 4d47c5ba just cleared. Its
		// `menufont` is already covered by QueueMenuWarm above.
		EnqueueIdleWarm<Texture2D>("GFX/Sprites/awardmentblade");
	}

	// Queue one asset to be warmed later (during splash idle, or the pre-menu drain).
	private void EnqueueWarm<T>(string assetName)
	{
		warmQueue.Enqueue(() => Warm<T>(assetName));
	}

	private void EnqueueIdleWarm<T>(string assetName)
	{
		idleWarmQueue.Enqueue(() => Warm<T>(assetName));
	}

	// Warm at most ONE queued asset per call — invoked once per Update tick so the heavy
	// MB-scale decodes spread across the splash's idle frames instead of blocking boot.
	// Menu assets first (the pre-menu drain depends on that queue emptying fastest), then
	// the low-priority idle set.
	private void PumpWarmQueue()
	{
		if (warmQueue.Count > 0)
		{
			warmQueue.Dequeue()();
		}
		else if (idleWarmQueue.Count > 0)
		{
			idleWarmQueue.Dequeue()();
		}
	}

	// Decode every still-queued asset NOW. Called the instant before the menu is first
	// built so the menu is guaranteed fully warm even if the splash was skipped before the
	// per-tick pump could finish — worst case this is the old synchronous batch decode.
	// Deliberately leaves idleWarmQueue alone: nothing there is needed for the menu, and
	// blocking the menu on ~20 background tiles would trade a hidden warm for a visible wait.
	//
	// Reaching this with a non-empty queue means the pump never got its ~24 ticks -- a debug
	// boot, or a player who double-tapped past the splash -- so the whole remainder decodes in
	// ONE synchronous tick here. That tick reports itself under ?loadlog since card cccd763a;
	// LoadProfiler.EndWarmDrain owns the wording and the gate (see it for why the count is
	// queue entries rather than decodes). A full-splash boot drains nothing and stays silent.
	private void DrainWarmQueue()
	{
		if (warmQueue.Count == 0)
		{
			return;
		}
		int drained = warmQueue.Count;
		long drainStart = EvilAliensWeb.Compat.LoadProfiler.BeginWarmDrain();
		while (warmQueue.Count > 0)
		{
			warmQueue.Dequeue()();
		}
		EvilAliensWeb.Compat.LoadProfiler.EndWarmDrain(drained, drainStart);
	}

	// Best-effort warm of a single asset into the shared content manager's cache. Guarded
	// per-asset (not as one batch) so a single missing or unreadable asset can't abort
	// warming the others — and must never block boot.
	private void Warm<T>(string assetName)
	{
		// Tell the load profiler this decode is deliberate, so ?loadlog stops reporting the
		// warm queues doing their job as COLD gaps (card 4d47c5ba). All THREE queues funnel
		// through here (menu, idle and the pre-launch levelWarmQueue) and nothing else does, so
		// a boot decode from anywhere else still surfaces -- which is the point. The level warm
		// also sits inside a BeginPreload/EndPreload bracket, which takes precedence, so it goes
		// on counting as a preload rather than becoming invisible.
		EvilAliensWeb.Compat.LoadProfiler.BeginWarm();
		try
		{
			content.Load<T>(assetName);
		}
		catch (Exception ex)
		{
			System.Console.WriteLine("[warm] " + assetName + " warm failed: " + ex.Message);
		}
		finally
		{
			EvilAliensWeb.Compat.LoadProfiler.EndWarm();
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
		// Freeze the menu for the pre-launch warm: OnFinished fires from the menu's
		// FadeToGame Update with the fade held at full, so an un-frozen menu would
		// re-fire OnFinished every tick until it's removed. Enabled=false stops its
		// Update (input + attract timer included) while it keeps drawing the faded
		// frame — the player sees the same fade-hold "loading" as before, but the
		// browser stays responsive. ComponentBin.Add re-enables on the next Add, so
		// returning to the menu after the level is unaffected.
		menuScene.Enabled = false;
		WarmThenLaunch(selectedLevel, delegate
		{
			collectionHelper.ClearCache();
			collectionHelper.Remove((GameComponent)(object)menuScene);
			oracle.ResetPlayers();
			// Online co-op (card 4d904410): the host allocates every roster slot, so a joining
			// peer seats its starter in the slot it was GRANTED, not simply the first free one.
			// Offline (and host-side) LocalPrimarySlot is 0, which is what AddPlayer would pick
			// anyway right after a ResetPlayers.
			int primarySlot = EvilAliensWeb.Compat.Net.NetSession.LocalPrimarySlot;
			if (!oracle.AddPlayerAt(primarySlot, starter))
			{
				oracle.AddPlayer(starter);
			}
			bragScene.StoreCompletionProgress();
			AddLevelComponent(selectedLevel);
		});
	}

	// Warm `level`'s manifest texture set one-per-tick (see levelWarmQueue), then run
	// `launch` (the actual scene setup + Add). Covers every level-launch path: the
	// menu (MenuFinished — incl. the attract-demo auto-launch, which is a MenuFinished
	// with Demo1/2/3) and the ?level= debug boot (LaunchLevelDirect). Bracketed as a
	// preload for the LoadProfiler so the hitch watchdog doesn't flag the deliberate
	// one-decode ticks and ?loadlog attributes the decodes to the level as preloads.
	// Per-tick wall-clock budget for PumpLevelWarm, as Stopwatch ticks. 8ms leaves room in a
	// 16.7ms frame for the loading-screen draw; it is a floor, not a cap -- the budget is
	// checked AFTER each warm, so one slow decode always completes rather than being split.
	private static readonly long LevelWarmBudgetTicks = System.Diagnostics.Stopwatch.Frequency / 125;

	private void WarmThenLaunch(Levels level, Action launch)
	{
		if (pendingLevelLaunch != null)
		{
			// Shouldn't happen (the menu is frozen while warming; ?level= fires once)
			// — but if it ever does, the NEW request wins and the old launch is dropped.
			System.Console.WriteLine("[levelwarm] launch requested while another was warming — replacing");
			levelWarmQueue.Clear();
			pendingLevelLaunch = null;
		}
		List<string> ids = EvilAliensWeb.Compat.LoadProfiler.ManifestAssets(level.ToString());
		if (ids.Count == 0)
		{
			launch();
			return;
		}
		EvilAliensWeb.Compat.LoadProfiler.BeginPreload(level.ToString());
		foreach (string id in ids)
		{
			string captured = id;
			levelWarmQueue.Enqueue(() => Warm<Texture2D>(captured));
		}
		pendingLevelLaunch = launch;
	}

	// Decode queued level assets until this tick's budget is spent; when the queue drains,
	// close the preload bracket and run the deferred launch. The launch runs on its own tick
	// (not the last decode's) so the browser gets a paint between the final warm and the
	// level's remaining synchronous LoadContent work.
	//
	// BUDGETED rather than strictly one-per-tick: a captured manifest section names every
	// texture the level touches (card 74b30beb), which is 26-82 entries, and one-per-tick
	// would put a fixed ~0.5-1.4s floor on EVERY entry into that level. Most entries are not
	// decodes at all -- the shared content manager never unloads mid-session, so a retry after
	// death, a replayed challenge, or any asset an earlier level already pulled in costs
	// nothing but a dictionary hit, and spending a whole frame on each is pure latency. A
	// budget keeps the property that actually matters (a real multi-megabyte decode blows it
	// on its own, so it still gets a tick to itself and the browser still paints between
	// decodes) while a fully-cached queue drains in one tick.
	private void PumpLevelWarm()
	{
		if (pendingLevelLaunch == null)
		{
			return;
		}
		if (levelWarmQueue.Count > 0)
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			do
			{
				levelWarmQueue.Dequeue()();
			}
			while (levelWarmQueue.Count > 0
				&& System.Diagnostics.Stopwatch.GetTimestamp() - started < LevelWarmBudgetTicks);
			return;
		}
		Action launch = pendingLevelLaunch;
		pendingLevelLaunch = null;
		EvilAliensWeb.Compat.LoadProfiler.EndPreload();
		launch();
	}

	// Add the GameScene for `selectedLevel` to the live component bin. Shared by the
	// normal menu path (MenuFinished) and the ?level=... debug direct-launch — both
	// via the WarmThenLaunch pre-launch warm.
	private void AddLevelComponent(Levels selectedLevel)
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
		case Levels.WebcamAliens:
			collectionHelper.Add((GameComponent)(object)webcamLevel);
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
		WarmThenLaunch(selectedLevel, delegate
		{
			collectionHelper.ClearCache();
			oracle.ResetPlayers();
			// Same host-granted seat as MenuFinished (card 4d904410). A ?net=join tab pairs while
			// it boots, so the grant can land BEFORE this runs -- seating slot 0 regardless would
			// leave the ship in a slot the wire doesn't know about (and NetSession's live re-seat
			// has nothing to move yet at grant time).
			int primarySlot = EvilAliensWeb.Compat.Net.NetSession.LocalPrimarySlot;
			if (!oracle.AddPlayerAt(primarySlot, ControlDevice.Keyboard))
			{
				oracle.AddPlayer(ControlDevice.Keyboard);
			}
			// ?aifriends=<n> verification seam: seed the Mechanical Friends cheat on a direct
			// ?level= boot so AI helper ships auto-join (two-tab AI-friend replication testing).
			if (EvilAliensWeb.Compat.DebugFlags.AiFriends > 0)
			{
				Settings.GetInstance().Friends = EvilAliensWeb.Compat.DebugFlags.AiFriends;
			}
			bragScene.StoreCompletionProgress();
			AddLevelComponent(selectedLevel);
		});
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

	private void texViewerScene_OnExitToMenu()
	{
		texViewerScene.Teardown();
		collectionHelper.Remove((GameComponent)(object)texViewerScene);
		collectionHelper.Add((GameComponent)(object)menuScene);
	}

	private void lazerShowcaseScene_OnExitToMenu()
	{
		lazerShowcaseScene.Teardown();
		collectionHelper.Remove((GameComponent)(object)lazerShowcaseScene);
		collectionHelper.Add((GameComponent)(object)menuScene);
	}

	private void textShowcaseScene_OnExitToMenu()
	{
		textShowcaseScene.Teardown();
		collectionHelper.Remove((GameComponent)(object)textShowcaseScene);
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

	// The dev-build FPS HUD's "update" row (Compat/FrameProfiler, card 22e655b5). The body
	// moved into UpdateCore so the whole of it -- including the settings/achievements pumps
	// and the turbo/slowmo/hit-stop time rescale, not just UpdateInner -- lands inside the
	// bracket; anything left outside a section shows up as unexplained "other" in the HUD.
	// try/finally because UpdateCore has an early return and a rethrow path.
	protected override void Update(GameTime gameTime)
	{
		long profileStart = FrameProfiler.Begin();
		try
		{
			UpdateCore(gameTime);
		}
		finally
		{
			FrameProfiler.End(FrameSection.Update, profileStart);
		}
	}

	private void UpdateCore(GameTime gameTime)
	{
		// AI bench fast-forward (?aiff=<n>, card f4d1721f): run the sim n times per rendered frame
		// so an unattended AI soak covers a whole level in a fraction of the wall clock, WITHOUT
		// changing the per-tick physics it is measuring (which Settings.Turbo, a dt scale, would).
		// Each repeat gets a synthesised 60Hz dt, NOT the frame's own: IsFixedTimeStep is false
		// here, so drawing one frame per n sims inflates the real delta by ~n and repeating THAT
		// would be a giant timestep -- worse than the dt scaling this exists to avoid.
		// Never in a net session: both peers must run at one pace.
		int repeats = EvilAliensWeb.Compat.DebugFlags.AiFastForward;
		if (repeats > 1 && !EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			TimeSpan step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60L);
			TimeSpan total = gameTime.TotalGameTime;
			for (int i = 0; i < repeats; i++)
			{
				total += step;
				UpdateScaled(new GameTime(total, step));
				// A launch warm is one decode per tick and must not be raced through -- the point
				// of the warm is that the browser paints between decodes.
				if (pendingLevelLaunch != null)
				{
					break;
				}
			}
			return;
		}
		UpdateScaled(gameTime);
	}

	// The real per-tick path: global singletons, the turbo/slow-mo/hit-stop time scale, then the
	// world. Split out of UpdateCore so the ?aiff fast-forward above and the headless AI soak
	// (AiBench.RunHeadless via BenchTick) both drive the SAME code a normal frame does -- a
	// second hand-rolled copy of the scaling would silently drift from the game being measured.
	private void UpdateScaled(GameTime gameTime)
	{
		Settings.GetInstance().Update();
		Achievements.GetInstance().Update();
		Unlockables.GetInstance().Update();
		int num = Settings.GetInstance().Turbo;
		if (EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			// Online co-op locks the sim pace: a turbo host would run its authoritative
			// world faster than the client renders it (card 11.4 session descriptor).
			num = 100;
		}
		if (Guide.IsVisible)
		{
			num = 0;
			vibrator.DisableVibrations();
		}
		float slowmotion = oracle.Slowmotion;
		// Game juice (Compat/Juice.cs): tick the screen shake + hit-stop with the UNSCALED
		// frame delta, BEFORE the time scaling below — the shake must keep moving and the
		// freeze must be able to end while game time is stopped. The hit-stop then folds
		// into the same turbo*slowmotion scale the game already applies (0 while frozen).
		Juice.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
		float hitstop = Juice.TimeScale;
		if (num != 100 || slowmotion != 1f || hitstop != 1f)
		{
			float num2 = (float)num / 100f * slowmotion * hitstop;
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

	// AI bench headless soak seam (card f4d1721f): one full game tick at a caller-chosen dt with
	// NO Draw. Rendering is the expensive half and a soak does not need it -- and a background
	// tab throttles rAF (and MessageChannel) to ~1 Hz, so a rendered soak measures nothing.
	internal void BenchTick(GameTime gameTime)
	{
		UpdateScaled(gameTime);
	}

	private void UpdateInner(GameTime gameTime)
	{
		if (!wantExit)
		{
			// Card 02d9ad67: flush deaths queued during the PREVIOUS tick's collision phase
			// before any component updates — a killed component must never get one more
			// "zombie" Update (move/fire/spawn from the grave). Also expires the standing
			// purge filter (see ComponentBin.TopOfTickFlush).
			collectionHelper.TopOfTickFlush();
			// Card b0ab09ec: the flush above is what emits this tick's EvDeath events, so the
			// scores are captured for EvScoreSync HERE -- every award now in them has already
			// been announced. Reading them at send time (after DetectCollisions, below) would
			// leak an award whose EvDeath is still queued, and the client would count it twice.
			EvilAliensWeb.Compat.Net.NetSession.SnapshotScoresForSync();
			inputHandler.Update();
			((GameComponent)vibrator).Update(gameTime);
			soundManager.Update(gameTime);
			Storage.Update(gameTime, this);
			// FPS HUD sub-rows: every game component's Update, then the collision sweep (the
			// collision matrix' DDA fills are the known hot spot), then the net layer. Three
			// separate brackets because "update is expensive" is not actionable but "collision
			// is 6ms of it" is. They nest inside the Update bracket, so the HUD subtracts only
			// the parent from the tick.
			long profComponents = FrameProfiler.Begin();
			base.Update(gameTime);
			collectionHelper.Update();
			FrameProfiler.End(FrameSection.UpdComponents, profComponents);
			long profCollision = FrameProfiler.Begin();
			collisionHandler.DetectCollisions();
			FrameProfiler.End(FrameSection.UpdCollision, profCollision);
			long profNet = FrameProfiler.Begin();
			// Online co-op: drain received messages + send the ~30Hz ship stream ON the game
			// tick (never from JS callbacks). Placed before the level-warm early-return so
			// heartbeats keep flowing while a launch is warming. A single branch when inactive.
			EvilAliensWeb.Compat.Net.NetSession.Update();
			// Card 2001fbd8: keep the public-game listing in step with the running game
			// (open/update/close the listing, drain its phase callbacks, start a session on a
			// join-in-progress pairing). A plain boot has no GameScene up, so it early-returns.
			EvilAliensWeb.Compat.Net.NetListing.Tick((Game)(object)this);
			FrameProfiler.End(FrameSection.UpdNet, profNet);
			// AI telemetry (card f4d1721f): advance the run clock, latch level progress + verdict,
			// print the periodic summary. A single branch when ?aibench is off.
			EvilAliensWeb.Compat.AiBench.Update(gameTime, (Game)(object)this);
			// A pending level launch takes warm priority (and excludes the other
			// queues that tick, so a tick never pays two decodes).
			if (pendingLevelLaunch != null)
			{
				PumpLevelWarm();
				return;
			}
			// Warm one queued asset per tick (menu queue first, then the low-priority
			// idle set — see PumpWarmQueue). No-op once both queues are drained.
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
		// FPS HUD "scene" row: opened here rather than around DrawInner alone so the scene
		// target's realloc-check / SetRenderTarget / full clear are attributed to drawing
		// instead of silently inflating the HUD's "other" remainder. The fullscreen branch
		// below rethrows, so it closes the section itself before throwing (a try/finally here
		// would re-indent the whole method for a branch that is dead on web -- KNI's BlazorGL
		// never reports IsFullScreen).
		long profScene = FrameProfiler.Begin();
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
		// drawn at native density, sharing one bloom + present blit.
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
				// Close the FPS HUD's scene section before unwinding, so a failed frame is
				// recorded with the work it actually did instead of silently landing in "other".
				FrameProfiler.End(FrameSection.DrawScene, profScene);
				throw new Exception("See inner exception (error.txt): ", innerException);
			}
		}
		else
		{
			DrawInner(gameTime);
		}
		FrameProfiler.End(FrameSection.DrawScene, profScene);

		// FPS HUD "post" row: both full-frame post-processes together. They're one line item
		// because they share a cost shape (a full-screen pass over sceneTarget) and both are
		// zero on most frames -- a non-zero "post" is itself the finding.
		long profPost = FrameProfiler.Begin();
		// Cinematic slow-motion ghost trails: post-process the fully composited (and
		// bloomed) frame in sceneTarget before the present blit. No-op unless the 1up
		// slowmo is active (and ramping). Leaves the render target on sceneTarget, which
		// the present block immediately switches off below.
		ApplySlowmoTrail(gameTime);

		// Tutorial holo-sim filter: same seam, runs after the trail so the ghosts get
		// scanlined too. Leaves the render target on sceneTarget like the trail does.
		ApplyHoloSim(gameTime);
		FrameProfiler.End(FrameSection.DrawPost, profPost);

		// FPS HUD "present" row: the letterbox blit. Scales with WINDOW size, not
		// scene complexity, so it's the row that moves when you resize rather than when you
		// spawn enemies.
		long profPresent = FrameProfiler.Begin();
		// Present the scene target to the real (window-sized) back buffer, letterboxed.
		Xna3GraphicsDeviceCompat.BaseRenderTarget = null;
		base.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
		base.GraphicsDevice.Clear(Color.Black);
		// Letterbox geometry from the single source of truth (RenderScale), so the present
		// blit and the inverse mouse mapping (WindowToDesign) round identically.
		Rectangle dest = RenderScale.WindowDestRect(pp.BackBufferWidth, pp.BackBufferHeight);
		// sceneTarget holds the fully composited frame (legacy + hi-res, bloomed); it is
		// blitted straight to the window here. The blit is 1:1 when the render size equals
		// the letterbox (uncapped); a bilinear upscale when RenderScale's height cap kicks in.
		// Card a35c5f31 removed the gamma pixel shader that used to run on this blit -- it was
		// the 2008 Xbox TV-calibration control (Settings.Gamma, default 1.0 => pow(c, 1.0), a
		// measured byte-exact no-op), obsolete on a colour-managed browser. The port renders
		// entirely in sRGB space, as the original did; that is deliberate, not a defect.
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, null);
		if (Juice.ShakeActive)
		{
			// Screen shake (Compat/Juice.cs): jolt the present blit itself — offset + a small
			// roll + a slight zoom so the shaken frame keeps covering the letterbox instead of
			// flashing background at the edges. Shaking here (not the game's draw matrix) means
			// the WHOLE composited frame moves as one (scene, HUD, bloom) and no gameplay
			// coordinate (collision, mouse aim via WindowToDesign) is ever affected. The zoom
			// scales with the sampled magnitude, so ?shake= tuning keeps edges covered too.
			float toWindow = (float)dest.Width / RenderScale.DesignWidth;
			Vector2 center = new Vector2(dest.X + dest.Width * 0.5f, dest.Y + dest.Height * 0.5f)
				+ Juice.ShakeOffset * toWindow;
			float zoom = 1f + 0.06f * Juice.ShakeMagnitude;
			Vector2 blitScale = new Vector2(
				(float)dest.Width / ((Texture2D)sceneTarget).Width,
				(float)dest.Height / ((Texture2D)sceneTarget).Height) * zoom;
			Vector2 origin = new Vector2(((Texture2D)sceneTarget).Width * 0.5f, ((Texture2D)sceneTarget).Height * 0.5f);
			spriteBatch.Draw((Texture2D)(object)sceneTarget, center, null, Color.White, Juice.ShakeRoll, origin, blitScale, SpriteEffects.None, 0f);
		}
		else
		{
			spriteBatch.Draw((Texture2D)(object)sceneTarget, dest, Color.White);
		}
		spriteBatch.End();
		FrameProfiler.End(FrameSection.DrawPresent, profPresent);
	}

	// FPS HUD "swap" row. Game.Tick calls Update, then Draw, then EndDraw — and EndDraw is
	// where the back buffer is actually presented, OUTSIDE the Draw override above. That
	// matters more than it sounds: WebGL commands are queued, so this is where a GPU-bound
	// frame's real cost finally lands as a blocking wait. Without this bracket a 22ms frame
	// showed 3ms of attributed work and 19ms of unexplained "other", which points the
	// optimizer at exactly the wrong place. A big `swap` means the GPU is the bottleneck and
	// no amount of CPU-side work will move it.
	protected override void EndDraw()
	{
		long profileStart = FrameProfiler.Begin();
		try
		{
			base.EndDraw();
		}
		finally
		{
			FrameProfiler.End(FrameSection.Swap, profileStart);
		}
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

	// Tutorial "trial simulation" fullscreen filter (Compat/HoloSim + holosim.fx): while the
	// tutorial pokes HoloSim alive, run sceneTarget through the filter shader into holoRT and
	// copy it back — a SpriteBatch effect pass can't sample the target it renders to, hence
	// the ping-pong. Two opaque full-frame blits, only while the filter is visible; every
	// other scene skips at the first branch. Envelopes advance on RAW Draw time (cosmetic —
	// keeps shimmering through hit-stop, like the metal sheen).
	private void ApplyHoloSim(GameTime gameTime)
	{
		float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
		if (dt <= 0f)
		{
			dt = 1f / 60f;
		}
		HoloSim.Update(dt);
		if (!HoloSim.Visible || holoSim == null)
		{
			return;
		}
		if (holoRT == null || ((GraphicsResource)holoRT).IsDisposed
			|| ((Texture2D)holoRT).Width != RenderScale.Width
			|| ((Texture2D)holoRT).Height != RenderScale.Height)
		{
			if (holoRT != null && !((GraphicsResource)holoRT).IsDisposed)
			{
				((GraphicsResource)holoRT).Dispose();
			}
			PresentationParameters hpp = base.GraphicsDevice.PresentationParameters;
			// DiscardContents (unlike slowmoTrail's PreserveContents): the RT is fully
			// overwritten by an opaque full-rect draw every time it's bound.
			holoRT = new RenderTarget2D(base.GraphicsDevice, RenderScale.Width, RenderScale.Height, false,
				hpp.BackBufferFormat, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
		}
		Rectangle full = new Rectangle(0, 0, RenderScale.Width, RenderScale.Height);
		hsIntensity?.SetValue(HoloSim.Intensity);
		hsGreen?.SetValue(HoloSim.Green);
		hsBurst?.SetValue(HoloSim.Burst);
		hsTime?.SetValue(HoloSim.Time);
		base.GraphicsDevice.SetRenderTarget(holoRT);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, null, null, holoSim);
		spriteBatch.Draw((Texture2D)(object)sceneTarget, full, Color.White);
		spriteBatch.End();
		base.GraphicsDevice.SetRenderTarget(sceneTarget);
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
		spriteBatch.Draw((Texture2D)(object)holoRT, full, Color.White);
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
		base.Draw(gameTime);
		spriteBatchWrapper.Flush();
		if (onPostDraw != null)
		{
			onPostDraw();
		}
		// ?hitboxes / eaHitboxes(): draw every live collidable's collision shape over the
		// composited (and bloomed) frame, colour-coded by kind, so a sprite whose Draw is
		// offset from its Position/hitbox shows the drift in real gameplay. In design space
		// via the wrapper (like the HideSafeArea draw below), on top of everything. OFF by
		// default and out of DebugFlags.Active, so a shipped build is unaffected.
		if (DebugFlags.ShowHitboxes)
		{
			HitboxOverlay.Draw(base.GraphicsDevice, spriteBatchWrapper, collisionHandler.Collidables);
			spriteBatchWrapper.Flush();
		}
		if (Settings.GetInstance().HideSafeArea)
		{
			Rectangle safeZone = General.SafeZone;
			spriteBatchWrapper.Draw(blackPixel, new Rectangle(0, 0, 800, (safeZone).Top), Color.Black);
			spriteBatchWrapper.Draw(blackPixel, new Rectangle(0, 0, (safeZone).Left, 600), Color.Black);
			spriteBatchWrapper.Draw(blackPixel, new Rectangle(0, (safeZone).Bottom, 800, 600), Color.Black);
			spriteBatchWrapper.Draw(blackPixel, new Rectangle((safeZone).Right, 0, 800, 600), Color.Black);
			spriteBatchWrapper.Flush();
		}
		// Card 02a96ff6: while the pre-launch level warm decodes textures one-per-tick
		// (pendingLevelLaunch != null — see WarmThenLaunch/PumpLevelWarm), the menu holds
		// its fade at full black with no progress feedback. Draw a subtle "loading" pulse
		// on top so the warm doesn't read as a frozen frame. Design-space via the wrapper,
		// like the overlays above; ends the instant the launch fires (queue drained).
		if (pendingLevelLaunch != null)
		{
			DrawLevelWarmIndicator(gameTime);
			spriteBatchWrapper.Flush();
		}
		if (wantExit)
		{
			base.GraphicsDevice.Clear(Color.Black);
		}
	}

	// Subtle loading indicator shown during the pre-launch level warm (see the call site
	// in DrawInner). A breathing "LOADING" in the menu font over a row of three marching
	// pulse dots (blackPixel squares), centred low on the 800x600 design frame. Pure
	// Draw-time cosmetic keyed off gameTime — no state, no content, no debug flag: it only
	// appears while a warm is in flight, which is inherently a loading moment.
	private void DrawLevelWarmIndicator(GameTime gameTime)
	{
		// Straight-alpha discipline: base.Draw leaves the wrapper's BlendMode wherever the
		// last component set it, and the partial-alpha breathe/dots need NonPremultiplied.
		// The setter only flushes on an actual change, so this is a no-op in the common case.
		spriteBatchWrapper.BlendMode = (SpriteBlendMode)1;
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		// Whole-word breathe: ~0.9 Hz sine, alpha 0.45..0.85.
		float breathe = 0.65f + 0.2f * (float)Math.Sin(t * 5.6f);
		string label = "LOADING";
		Vector2 wordPos = new Vector2(400f, 520f);
		// Faint drop shadow so the word stays legible on any warm frame (menu fade is
		// black, but ?level= direct boots may not be), then the word itself.
		spriteBatchWrapper.DrawString(label, wordPos + new Vector2(2f, 2f), new Color(0f, 0f, 0f, breathe * 0.6f), 0f, centered: true, 0.5f, SpriteEffects.None, 0f);
		spriteBatchWrapper.DrawString(label, wordPos, new Color(1f, 1f, 1f, breathe), 0f, centered: true, 0.5f, SpriteEffects.None, 0f);
		// Three marching dots: each fades in a phase-shifted wave so the row reads as a
		// left-to-right sweep. Fixed positions (drawn as squares) => no layout jitter.
		const int dotCount = 3;
		const float dotSize = 7f;
		const float dotSpacing = 24f;
		float rowY = 552f;
		float startX = 400f - dotSpacing * (dotCount - 1) / 2f;
		for (int i = 0; i < dotCount; i++)
		{
			float phase = t * 4.2f - i * 0.9f;
			float a = 0.2f + 0.6f * (0.5f + 0.5f * (float)Math.Sin(phase));
			float cx = startX + i * dotSpacing;
			Rectangle dot = new Rectangle((int)(cx - dotSize / 2f), (int)(rowY - dotSize / 2f), (int)dotSize, (int)dotSize);
			spriteBatchWrapper.Draw(blackPixel, dot, new Color(1f, 1f, 1f, a));
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
		foreach (IGameComponent item in list)
		{
			bool flag = !(item is MousePointer);
			flag = flag && !(item is Oracle);
			flag = flag && !(item is BloomComponent);
			flag = flag && !(item is GamerServicesComponent);
			flag = flag && !(item is SpriteBatchWrapper);
			flag = flag && !(item is Debugger);
			if (flag && !(item is AwardmentBlade))
			{
				((Collection<IGameComponent>)(object)base.Components).Remove(item);
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
