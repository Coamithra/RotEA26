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

	// checkScreenShot's weighted Game.Components scan is a per-frame snapshot of
	// on-screen busy-ness that arms the 800ms snapshotdelaytimer once it crosses 30.
	// Sampling it every Nth frame (instead of every frame once snapshottimer expires)
	// cuts the full-component scan to 1/N during calm stretches. The >30 threshold is
	// unchanged; the trade is that a busy spike lasting fewer than N frames can fall
	// between samples and be missed — acceptable because the thumbnail only needs one
	// representative busy frame over the 5s snapshot cadence, and any sustained action
	// is sampled many times within that window.
	private const int SnapshotScanInterval = 6;

	private int snapshotScanCounter;

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

	// ---- Online co-op (card 11.3) ------------------------------------------------------
	// The scene NetSession reaches for replicated state transitions (reset/victory/
	// checkpoint/background beats/pause). Set in Initialize, cleared in Terminate.
	internal static GameScene NetActiveScene;

	// True while OUR pause menu is up (drives EvPause + the overlap rules with a remote pause).
	private bool netLocalPauseUp;

	// True while WE pushed the collection for the remote peer's pause.
	private bool netRemotePauseHeld;

	private EvilAliensWeb.Compat.Net.NetPauseOverlay netPauseOverlay;

	// The host's interactive replacement for netPauseOverlay (card 0b8a300b). Non-null only
	// while it is actually up, i.e. a remote pause that outlasted NetSession's offer delay.
	private EvilAliensWeb.Compat.Net.NetKickMenu netKickMenu;

	private bool netKickMenuUp;

	// ?netkickshot only: when to fire the one-shot freeze+show, so the capture gets a live
	// level behind the menu rather than a scene that never drew. REAL time, not a tick count:
	// GameScene.Update does not run at a steady rate through the level intro (a 120-tick
	// counter measured ~47s here), and the net layer times everything on TickCount64 anyway.
	// 0 = not armed.
	private const long NetKickMenuDelayMs = 2500;

	private long netKickMenuAt;

	private EvilAliensWeb.Compat.Net.NetWaitOverlay netWaitOverlay;

	private bool netPeerStalled;

	public Levels Level => level;

	protected bool spawnPlayerNormally
	{
		get
		{
			// Online co-op (card 11.2): a scripted no-ship phase (Level1's intro sets this
			// false and hands the ship spawn to demo_OnFinished) lives in the level script,
			// which never runs on a join peer -- without this override a client would never
			// spawn its local ship (or LoseLife on wipe). The client's ship always uses the
			// generic startup/respawn path; the intro choreography stays host-only.
			// (WebcamLevel's permanent no-ship design is unaffected: its enemy types aren't
			// replicable, so webcam co-op isn't a supported session in the first place.)
			return _spawnplayernormally || EvilAliensWeb.Compat.Net.NetSession.IsClient;
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
		// Online co-op anti-griefing (card 0b8a300b). Built here with the other pause-time
		// menus, but only ever shown to the HOST, and only once a remote pause has outlasted
		// NetSession's offer delay -- see NetKickMenu for why this is the host's only agency.
		netKickMenu = new EvilAliensWeb.Compat.Net.NetKickMenu(base.Game);
		netKickMenu.OnExit += netKickMenu_KeepWaitingSelected;
		netKickMenu.AddEntry("Keep Waiting");
		netKickMenu.AddEntryEvent(netKickMenu_KeepWaitingSelected);
		netKickMenu.AddEntry("Kick Player");
		netKickMenu.AddEntryEvent(netKickMenu_KickSelected);
		netKickMenu.AddEntry("Kick and Block");
		netKickMenu.AddEntryEvent(netKickMenu_KickAndBlockSelected);
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
		// Leaving to the main menu — clear the pause muffle so the menu music isn't
		// left ducked/muddy (this path never goes through pausedScene_OnExit).
		base.SoundManager.SetPauseMuffle(on: false);
		// Online co-op: unfreeze the peer before we go (the real leave flow is card 11.4).
		netLocalPauseUp = false;
		EvilAliensWeb.Compat.Net.NetSession.OnLocalPause(on: false);
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
		// Online co-op: the client saves the same baseline so a later reset's score.Load()
		// restores identically (the script -- and so this callback -- is host-only).
		EvilAliensWeb.Compat.Net.NetSession.OnCheckpoint();
	}

	protected void LoseLife()
	{
		// Online co-op (card 11.3): death/reset is host-authoritative -- ONE broadcast.
		// A client never decides a reset itself; it mirrors the host's branch in
		// NetApplyReset when the EvReset arrives.
		if (EvilAliensWeb.Compat.Net.NetSession.IsClient)
		{
			return;
		}
		if (Settings.GetInstance().DirectRespawn)
		{
			Collection.Purge<PlayerShip>();
			Collection.Purge<PlayerShipSummon>();
			_timer = TimeSpan.Zero;
			_state = GameState.Resetting;
			Settings.GetInstance().ResetDifficulty();
			EvilAliensWeb.Compat.Net.NetSession.OnHostReset(EvilAliensWeb.Compat.Net.NetSession.ResetModeRespawn);
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
		EvilAliensWeb.Compat.Net.NetSession.OnHostReset(_state == GameState.GameOver
			? EvilAliensWeb.Compat.Net.NetSession.ResetModeGameOver
			: EvilAliensWeb.Compat.Net.NetSession.ResetModeReset);
	}

	// ---- Online co-op (card 11.3): replicated state-machine seams -----------------------

	// Client-side mirror of the host's LoseLife branch (mode = NetSession.ResetMode*).
	internal void NetApplyReset(byte mode)
	{
		// Card b0ab09ec: drop any provisional score credits first. A reset reverts the score to
		// the checkpoint baseline (score.Load), and the purge storm that follows removes the
		// very entities those credits belong to -- carrying them across would add kills from
		// before the revert on top of the restored baseline.
		EvilAliensWeb.Compat.Net.NetPuppets.ResetScoreLedger();
		Collection.Purge<PlayerShip>();
		Collection.Purge<PlayerShipSummon>();
		_timer = TimeSpan.Zero;
		switch (mode)
		{
		case EvilAliensWeb.Compat.Net.NetSession.ResetModeRespawn:
			_state = GameState.Resetting;
			Settings.GetInstance().ResetDifficulty();
			break;
		case EvilAliensWeb.Compat.Net.NetSession.ResetModeGameOver:
			defeatmessageshown = false;
			_state = GameState.GameOver;
			break;
		default:
			xfading = false;
			_state = GameState.Resetting;
			// Mirror the host's decrement for instant HUD feedback; the 1Hz EvScoreSync
			// carries the authoritative value regardless.
			if (score.Lives > 0 && !Settings.GetInstance().InfiniteLives)
			{
				score.RemoveLife();
			}
			break;
		}
	}

	internal void NetApplyVictory()
	{
		if (_state == GameState.Normal)
		{
			Victory();
		}
	}

	internal void NetApplyCheckpoint()
	{
		score.Save();
	}

	internal void NetApplyBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp op, Vector2 v)
	{
		switch (op)
		{
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSpeed:
			Background.SetSpeed(v);
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueEarth:
			Background.QueueEarth();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueSmallEarth:
			Background.QueueSmallEarth();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueAndromeda:
			Background.QueueAndromeda();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.EngageBeltSlowdown:
			Background.EngageBeltSlowdown();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.DisengageBeltSlowdown:
			Background.DisengageBeltSlowdown();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase2:
			Background.SetAlienBase2();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase3:
			Background.SetAlienBase3();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase4:
			Background.SetAlienBase4();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase5:
			Background.SetAlienBase5();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase6:
			Background.SetAlienBase6();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetDoodadPos:
			Background.NetSetDoodadPos(v);
			break;
		}
	}

	// Join-in-progress catch-up (card 45a4e48d), host side: bring a peer whose GameScene has
	// just come up (EvReady) up to the scenery state our level script already reached. The
	// joiner ran its own Initialize, so it holds the level's INITIAL background + music and --
	// the script being host-only (11.2 sim-split) -- will never reach those beats itself.
	// Everything here is an ordinary reliable beat event, so the client applies it through the
	// same paths the live ops use.
	internal void NetReplayCatchUp()
	{
		Background.NetReplayCatchUp(EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp);
		EvilAliensWeb.Compat.Net.NetSession.OnMusic(base.SoundManager.NetCurrentSong);
	}

	// The catch-up state as one parseable line, for the eaNetBg() console dump.
	internal string NetCatchUpStateLine()
	{
		return Background.NetStateLine() + " song=" + base.SoundManager.NetCurrentSong;
	}

	// Round-trip self-test for the JIP catch-up (card 45a4e48d), driven by eaNetBgTest() from
	// the console. The catch-up is a pure function -- host state -> a burst of ops -> client
	// state -- so it is provable in ONE tab with no peer and no timing, which is the only
	// honest way to check it: the thing under test is a fly-by whose position changes every
	// frame, so a screenshot (or a diff of two live windows that tick independently) can never
	// be exact. This is exact.
	//
	// Capture the burst, wipe the scenery back to what a fresh joiner's Initialize leaves
	// behind (Background.Reset), replay the burst through the REAL client apply path, and
	// compare the state line. DEBUG ONLY and deliberately destructive: Reset re-runs the
	// hyperspace entry, so the screen flashes. Run it in a solo tab -- inside a live host
	// session the replayed ops would also egress to the peer (idempotent, but noise).
	internal string NetCatchUpSelfTest()
	{
		string before = NetCatchUpStateLine();
		System.Collections.Generic.List<(EvilAliensWeb.Compat.Net.NetBackgroundOp Op, Vector2 V)> burst
			= new System.Collections.Generic.List<(EvilAliensWeb.Compat.Net.NetBackgroundOp, Vector2)>();
		Background.NetReplayCatchUp((op, v) => burst.Add((op, v)));
		int song = base.SoundManager.NetCurrentSong;
		Background.NetTestWipe();
		base.SoundManager.NetApplyMusic(-1);
		string joiner = NetCatchUpStateLine();
		foreach ((EvilAliensWeb.Compat.Net.NetBackgroundOp Op, Vector2 V) op in burst)
		{
			NetApplyBackgroundOp(op.Op, op.V);
		}
		base.SoundManager.NetApplyMusic(song);
		string after = NetCatchUpStateLine();
		// Name the ops, not just the count: a leg the level never fired is absent from this list,
		// so a PASS can't be read as covering more than the run actually exercised.
		string ops = burst.Count == 0 ? "(none)" : string.Join(",", burst.ConvertAll(o => o.Op.ToString()));
		return "[netbgtest] " + (after == before ? "PASS" : "FAIL") + " ops=" + ops
			+ "\n  host   : " + before
			+ "\n  joiner : " + joiner
			+ "\n  caught : " + after;
	}

	// TeamChallenge overrides this to break its tether on the peer's EvTetherBreak.
	internal virtual void NetApplyTetherBreak()
	{
	}

	// The REMOTE peer paused/resumed. Freeze/unfreeze our world like a local pause, but
	// with a hint overlay instead of an interactive menu. Called from NetSession (which
	// keeps ticking while the collection is pushed). Overlap rules: if OUR pause menu is
	// up the world is already frozen -- just remember the flag (NetSession.RemotePaused);
	// the local resume paths re-freeze if it is still set.
	internal void NetSetRemotePaused(bool on)
	{
		if (on)
		{
			if (netRemotePauseHeld || netLocalPauseUp)
			{
				return;
			}
			netRemotePauseHeld = true;
			Collection.Push();
			if (netPauseOverlay == null)
			{
				netPauseOverlay = new EvilAliensWeb.Compat.Net.NetPauseOverlay(base.Game);
			}
			Collection.Add((GameComponent)(object)netPauseOverlay);
			base.SoundManager.SetPauseMuffle(on: true);
		}
		else
		{
			if (!netRemotePauseHeld)
			{
				return;
			}
			netRemotePauseHeld = false;
			NetHideKickMenu(restoreOverlay: false);
			Collection.Remove((GameComponent)(object)netPauseOverlay);
			Collection.Pop();
			base.SoundManager.SetPauseMuffle(on: false);
			pausestopper.Start();
			pausestopper.Reset();
		}
	}

	// Card 0b8a300b: swap the passive curtain for the host's kick menu. Called by NetSession
	// once the remote pause has outlasted its offer delay. Added AFTER the Push (like the
	// overlay and the local pause menu), which is what keeps it Enabled over the frozen world.
	// Returns false when there was nothing to show (no freeze of ours to put it over) -- the
	// caller MUST NOT latch its "offered" flag on a false, or the offer is silently burned and
	// never comes back. See NetSession.TickKickOffer.
	internal bool NetShowKickMenu()
	{
		if (netKickMenuUp || !netRemotePauseHeld)
		{
			return false;
		}
		netKickMenuUp = true;
		// One at a time: the menu carries its own dim and its own "the other player has paused"
		// prompt, so leaving the overlay under it would double-darken and say it twice.
		Collection.Remove((GameComponent)(object)netPauseOverlay);
		netKickMenu.Reset();
		// No Setup(device) on purpose: unlike every other menu here there is no triggering
		// device to inherit (the PEER paused), and MenuSub1 treats a null controller as "any
		// device". Pinning it to Keyboard would leave a gamepad host unable to work its only
		// escape hatch.
		netKickMenu.Show(); // Show() does the Collection.Add itself -- the pausedScene pattern.
		return true;
	}

	// restoreOverlay: true when the pause is still on and we are only retracting the offer
	// ("Keep Waiting"); false when the freeze itself is ending and the caller removes the
	// overlay anyway.
	internal void NetHideKickMenu(bool restoreOverlay)
	{
		if (!netKickMenuUp)
		{
			return;
		}
		netKickMenuUp = false;
		netKickMenu.RemoveInstantly();
		if (restoreOverlay && netRemotePauseHeld)
		{
			Collection.Add((GameComponent)(object)netPauseOverlay);
		}
	}

	private void netKickMenu_KeepWaitingSelected(MenuSub1 sender)
	{
		if (!EvilAliensWeb.Compat.Net.NetSession.RemotePaused)
		{
			// The ?netkickshot harness froze us with no peer, so there is no pause to go back to
			// waiting for and nothing that would ever re-offer the menu -- hand the level back
			// instead of leaving the tab wedged behind the overlay.
			NetHideKickMenu(restoreOverlay: false);
			NetSetRemotePaused(on: false);
			return;
		}
		NetHideKickMenu(restoreOverlay: true);
		// Re-arm rather than retire: a griefer holding pause forever must not get one refusal
		// and then a permanently frozen host.
		EvilAliensWeb.Compat.Net.NetSession.RearmKickOffer();
	}

	private void netKickMenu_KickSelected(MenuSub1 sender)
	{
		NetKick(block: false);
	}

	private void netKickMenu_KickAndBlockSelected(MenuSub1 sender)
	{
		NetKick(block: true);
	}

	// KickPeer unfreezes us synchronously (it clears the remote pause, which pops the
	// collection), so drop the menu FIRST -- it was added after that Push and must not be
	// left drawing over a world that is running again.
	private void NetKick(bool block)
	{
		NetHideKickMenu(restoreOverlay: false);
		EvilAliensWeb.Compat.Net.NetSession.KickPeer(block);
	}

	// Peer stream has gone quiet, but the drop verdict has not been called yet (card 11.5).
	// Banner only -- unlike a remote PAUSE this does NOT push the collection: the world keeps
	// running (the host stays authoritative, a client dead-reckons) because the overwhelmingly
	// common cause is a backgrounded tab burst-sending, which self-heals in under a second.
	internal void NetSetPeerStalled(bool on)
	{
		if (on == netPeerStalled)
		{
			return;
		}
		netPeerStalled = on;
		if (on)
		{
			if (netWaitOverlay == null)
			{
				netWaitOverlay = new EvilAliensWeb.Compat.Net.NetWaitOverlay(base.Game);
			}
			Collection.Add((GameComponent)(object)netWaitOverlay);
		}
		else
		{
			Collection.Remove((GameComponent)(object)netWaitOverlay);
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

	// Shared per-difficulty policy, applied by each story level (Level1/2/3) right after base.Initialize
	// -- which clears the two flags below to false, so a non-Easy tier ends up non-adaptive. This block
	// was copy-pasted verbatim in all three levels.
	// "Easy" is NOT a flat-low setting -- it is the ADAPTIVE / forgiving mode: it SEEDS the modifier at
	// Medium's value (so the opening isn't trivially slow), turns on DirectRespawn (die -> respawn in
	// place, keep level progress), and flags AdaptiveDifficulty. That flag is a slight misnomer: the
	// time-ramp (Settings.Update) runs on EVERY non-locked difficulty regardless; the flag only changes
	// the edges -- on death Settings.ResetDifficulty eases 20% (x0.8) instead of hard-resetting to the
	// tier floor, and the ramp ceiling rises to Inzane*2 instead of tier*2. So Easy vs Medium start
	// identical (both 0.6) and only diverge once you die or survive a long time. Hard and up instead
	// get extra starting lives.
	protected void ApplyDifficultyPolicy()
	{
		Settings settings = Settings.GetInstance();
		if (settings.CurrentDifficulty == Settings.DifficultyLevel.Easy)
		{
			settings.DirectRespawn = true;
			settings.AdaptiveDifficulty = true;
			settings.DifficultyModifier = settings.GetDifficultyValue(Settings.DifficultyLevel.Medium);
		}
		if (settings.CurrentDifficulty == Settings.DifficultyLevel.Hard
			|| settings.CurrentDifficulty == Settings.DifficultyLevel.Very_Hard
			|| settings.CurrentDifficulty == Settings.DifficultyLevel.Inzane)
		{
			score.Lives = 7;
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
		snapshotScanCounter = 0;
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
		NetActiveScene = this;
		netLocalPauseUp = false;
		netRemotePauseHeld = false;
		netKickMenuUp = false;
		pausestopper.Reset();
		pausestopper.Stop();
		Background.Reset();
		((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)Background);
		((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)Foreground);
		eventList.Reset();
		score.Reset();
		score.Save();
		score.Lives = -1;
		Collection.Add((GameComponent)(object)score);
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
				StorageContainer container = null;
				try
				{
					container = Storage.StorageDeviceManager.Device.OpenContainer("EvilAliens");
					snapshotExists = File.Exists(container.Path + level.ToString() + ".dat");
				}
				catch (Exception)
				{
				}
				finally
				{
					if (container != null)
					{
						container.Dispose();
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
		if (EvilAliensWeb.Compat.Net.NetSession.RemotePaused)
		{
			// The peer paused before this scene existed (level-load race / reconnect) --
			// pick the freeze up now instead of missing the edge.
			NetSetRemotePaused(on: true);
		}
		else if (EvilAliensWeb.Compat.DebugFlags.NetKickShot)
		{
			// ?netkickshot: park the host's kick menu with no peer at all, purely so its
			// appearance can be screenshot (the ?gamebrowser fake-entry precedent). Drives the
			// REAL freeze + swap, so what lands in the capture is the real thing; the Kick
			// entries are inert because KickPeer no-ops without a session. Armed here, fired a
			// couple of seconds into Update -- see netKickMenuAt.
			netKickMenuAt = Environment.TickCount64 + NetKickMenuDelayMs;
		}
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
		base.SoundManager.SetPauseMuffle(on: false);
		NetLocalPauseReleased();
	}

	// Every local resume path funnels here: tell the peer, and if the REMOTE peer still
	// holds a pause of its own, immediately re-freeze under the overlay instead.
	private void NetLocalPauseReleased()
	{
		netLocalPauseUp = false;
		EvilAliensWeb.Compat.Net.NetSession.OnLocalPause(on: false);
		if (EvilAliensWeb.Compat.Net.NetSession.RemotePaused)
		{
			NetSetRemotePaused(on: true);
		}
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
		contentManager.Load<Texture2D>("GFX/Sprites/lazerglow");
		contentManager.Load<Texture2D>("GFX/Sprites/lazerbeam");
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

	// ?netscript (card 11.3): a compressed ~60s event list firing every replicated
	// script-beat type -- message, red warning, background ops, checkpoints, a music
	// switch, victory -- so a two-tab net run verifies the whole beat surface in about a
	// minute instead of a full-level soak. Levels opt in at the top of their
	// PopulateEventList (Level1 does). UnlockEvent is deliberately absent: it would grant
	// real profile unlocks as a test side effect; unlock replication rides the real run.
	protected void PopulateNetScriptTest()
	{
		MessageEvent messageEvent = new MessageEvent(base.Game, "Net script test!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 10f, 1.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		WaitEvent waitEvent = new WaitEvent(base.Game, 2f);
		waitEvent.OnFinished += delegate
		{
			Background.QueueAndromeda();
			Background.SetSpeed(new Vector2(0f, 1f) / 16.666666f);
			// Left engaged on purpose: this is the one rig that parks a level in the
			// belt-slowdown state, which is what gives the JIP catch-up's belt leg (card
			// 45a4e48d) any coverage at all -- Level 1's real engage sits deep in its script.
			Background.EngageBeltSlowdown();
		};
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 8f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent = new WaitEvent(base.Game, 2f);
		waitEvent.OnFinished += delegate
		{
			base.SoundManager.PlayMusic(Songs.Level3);
		};
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 3f);
		waitEvent.OnFinished += delegate
		{
			Victory();
		};
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
	}

	protected void Victory()
	{
		// Online co-op: the script (and so the win trigger) is host-only -- broadcast it.
		// No-op on a client (its Victory only ever runs FROM the event, via NetApplyVictory).
		EvilAliensWeb.Compat.Net.NetSession.OnHostVictory();
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
		int blockSize = 20;
		for (int i = 0; i < 800 / blockSize; i++)
		{
			for (int j = 0; j < 600 / blockSize; j++)
			{
				TestBlock testBlock = TestBlock.NewTestBlock(Collection, base.Game);
				testBlock.Setup(new Vector2((float)(i * blockSize), (float)(j * blockSize)), new Vector2((float)((i + 1) * blockSize), (float)((j + 1) * blockSize)));
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
		// ?netkickshot: let the level actually get going, THEN freeze it under the kick menu.
		// Doing this in Initialize instead would park the world before it has drawn a frame, so
		// the capture would show the menu over a blank scene -- and the real thing always
		// freezes a running level. One-shot; ticks down on the normal update, so it fires once
		// the scene is genuinely live.
		if (netKickMenuAt != 0 && Environment.TickCount64 >= netKickMenuAt)
		{
			netKickMenuAt = 0;
			NetSetRemotePaused(on: true);
			NetShowKickMenu();
		}
		bool pauseRequested = false;
		ControlDevice controlDevice = ControlDevice.AI;
		if ((base.InputHandler.Pressed(MyKeys.Enter) || base.InputHandler.Pressed(MyKeys.Esc)) && oracle.DeviceIsPlaying(ControlDevice.Keyboard))
		{
			pauseRequested = true;
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
				pauseRequested = true;
				controlDevice = controlDevice2;
			}
		}
		if (base.InputHandler.Pressed(MyKeys.Generic_Start) && oracle.DeviceIsPlaying(ControlDevice.Generic))
		{
			pauseRequested = true;
			controlDevice = ControlDevice.Generic;
		}
		if (pauseRequested & !pausestopper.Active)
		{
			Collection.Push();
			Collection.Add((GameComponent)(object)darkener);
			pausedScene.Reset();
			pausedScene.Setup(controlDevice);
			pausedScene.Show();
			exitConfirmationMenu.Setup(controlDevice);
			// Duck + muffle the BGM ("underwater" feel) while paused; every resume/exit
			// path below clears it. Sub-menus (Instructions / Controller Settings) return
			// to pausedScene, so they stay muffled — the game is still paused.
			base.SoundManager.SetPauseMuffle(on: true);
			// Online co-op: pause is a replicated event; the trigger above is local-device
			// only by construction (local keyboard/pads -- ControlDevice.Remote is not a pad).
			netLocalPauseUp = true;
			EvilAliensWeb.Compat.Net.NetSession.OnLocalPause(on: true);
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
		base.SoundManager.SetPauseMuffle(on: false);
		NetLocalPauseReleased();
	}

	// Card 11.4: the peer left a menu-lobby match -- the match ends for both sides. Called
	// by NetSession AFTER the session is stopped (so the remote-pause freeze is already
	// released). Force-exit to the main menu unless the level is already winding itself
	// down (the victory/game-over choreography finishes locally either way). Any local
	// pause menu depth (pause / instructions / controller settings / exit confirmation)
	// is unwound the same way exitConfirmationMenu_YesSelected does.
	// True while the level is ending NORMALLY (shared victory / game-over wind-down):
	// the peer's scene tearing down first is expected then, not a disconnect -- suppress
	// the "player left" notice.
	internal bool NetEndingNormally => _state == GameState.Victory || _state == GameState.GameOver;

	// ---- AI bench seams (card f4d1721f) -- read-only, only called behind ?aibench ----------

	internal int BenchEventPos => eventList.BenchPos;

	internal int BenchEventCount => eventList.BenchCount;

	// The run verdict, or null while the level is still being played. These two states are the
	// only terminal ones -- everything else (Startup/Nothing/Normal/Resetting) is "still going",
	// including a mid-level death, which costs a life but is not the end of the run.
	internal string BenchVerdict => _state switch
	{
		GameState.Victory => "VICTORY",
		GameState.GameOver => "GAME OVER",
		_ => null
	};

	internal void NetApplyPeerLeft()
	{
		if (NetEndingNormally)
		{
			return;
		}
		if (netLocalPauseUp)
		{
			Collection.Pop();
			Collection.Remove((GameComponent)(object)darkener);
			pausedScene.RemoveInstantly();
			exitConfirmationMenu.RemoveInstantly();
			playerOptions.RemoveInstantly();
			Collection.Remove((GameComponent)(object)instructionsMenu);
			netLocalPauseUp = false;
			pausestopper.Start();
			pausestopper.Reset();
			base.SoundManager.SetPauseMuffle(on: false);
		}
		Terminate(FinishedMode.exit);
	}

	// A scene may claim a joining device for a seat it ALREADY holds instead of letting a new
	// player be added (card e6927ef8: TeamChallenge hands its auto-pilot partner's seat to the
	// first real pad that presses Start, keeping that slot's score and its place in the tether).
	// Return true to mean "handled -- do not add a player". Default: no scene claims anything.
	protected virtual bool TryAdoptJoinDevice(ControlDevice device)
	{
		return false;
	}

	private void AddPlayer(ControlDevice controlDevice, bool spawnPlayer)
	{
		// Online co-op (card 4d904410): while a session is up the HOST allocates every slot, so a
		// couch player joining here must take the slot the host says -- see NetSession.TrySeatLocalJoin
		// (host: allocate locally; client: EvJoinRequest -> the ship spawns on the grant, not now).
		if (EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			EvilAliensWeb.Compat.Net.NetSession.TrySeatLocalJoin(controlDevice, spawnPlayer);
			return;
		}
		if (TryAdoptJoinDevice(controlDevice))
		{
			return;
		}
		int slot = oracle.AddPlayer(controlDevice);
		if (spawnPlayer)
		{
			SpawnPlayer(controlDevice, slot);
		}
	}

	// Spawn the ship for an already-seated slot. `slot` is the seat AddPlayer/the net allocator
	// actually took -- never `oracle.Players - 1`, which only agrees while the table is dense.
	internal void SpawnPlayer(ControlDevice controlDevice, int slot)
	{
		PlayerShip playerShip = Collection.Recycle<PlayerShip>();
		if (playerShip == null)
		{
			playerShip = new PlayerShip(base.Game);
		}
		switch (spawnType)
		{
		case PlayerSpawnType.South:
			playerShip.Setup(slot, new Vector2(400f, 648f), startup: true, invulnerable: true, 4.712389f);
			break;
		case PlayerSpawnType.West:
			playerShip.Setup(slot, new Vector2(-48f, 300f), startup: true, invulnerable: true, 0f);
			break;
		case PlayerSpawnType.North:
			playerShip.Setup(slot, new Vector2(400f, -48f), startup: true, invulnerable: false, (float)Math.PI / 2f);
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

	// Whether a Start press RIGHT NOW would spawn the joiner's ship itself, or only seat the
	// slot and leave the spawning to SpawnAllPlayers. Each state passes its own verdict below
	// (false while Resetting/GameOver, shipCreated during Startup, spawnPlayerNormally in
	// Normal), so it cannot be derived from outside -- latched here for the eaNetCouchJoin
	// debug seam, which must take the same branch a real pad press would.
	internal bool JoinWouldSpawnNow { get; private set; }

	private void CheckPlayerJoins(bool spawnPlayer)
	{
		JoinWouldSpawnNow = spawnPlayer;
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
		// Online co-op: the HOST runs AI "friend" ships (Mechanical Friends cheat) and streams
		// each one to the client, which shows it as a ControlDevice.RemoteFriend puppet whose
		// bullets re-fire locally (coverage-gaps follow-up -- see Compat/Net/NetSession.Friends).
		// The client must NOT auto-join AI friends of its own (they'd be host-authoritative
		// duplicates); its budget is filled by the host's replicated puppets instead.
		// In a net session, only the HOST adds AI friends, and only AFTER the client's Remote ship
		// has taken its slot: that pins the roster order (local, remote, then friends) so a friend's
		// oracle slot is the same index on both peers (identity mapping in NetSession.Friends keeps
		// per-slot score/lives sync consistent, and the client's high slots stay free for the puppets).
		bool aiFriendsAllowedHere = !EvilAliensWeb.Compat.Net.NetSession.Active
			|| (EvilAliensWeb.Compat.Net.NetSession.IsHost && oracle.DeviceIsPlaying(ControlDevice.Remote));
		if (AIJoinTimer.Finished && AllowAIFriends && aiFriendsAllowedHere
			&& oracle.Players < Settings.GetInstance().Friends + 1 && oracle.Players < 4)
		{
			AddPlayer(ControlDevice.AI, spawnPlayerNormally);
		}
		CheckPlayerJoins(spawnPlayerNormally);
		// Online co-op (card 11.2): the world is HOST-authoritative. A join peer never runs
		// the level script/spawners -- enemies arrive as replicated NetPuppets instead.
		if (!EvilAliensWeb.Compat.Net.NetSession.SuppressLevelScript)
		{
			eventList.Update(gameTime);
		}
		if (oracle.AllShipsDead & spawnPlayerNormally)
		{
			LoseLife();
		}
	}

	protected void Terminate(FinishedMode mode)
	{
		// Before NetActiveScene goes null (after that NetSession can no longer reach us) and
		// before the purges, which only cover AlienDrawableGameComponent and friends -- the
		// stall banner is a plain DrawableGameComponent in the GLOBAL bin, so nothing else
		// would ever remove it. A level that ends while stalled would otherwise leave
		// "WAITING FOR OTHER PLAYER" drawing over the credits and menus, and because level
		// scenes are singletons that get re-added, the stale netPeerStalled would make the
		// banner never appear again on the next play of that level.
		NetSetPeerStalled(on: false);
		// Same reasoning as the stall banner above: the kick menu is added outside the pushed
		// layer, so nothing else here would take it down, and level scenes are re-added
		// singletons -- a stale netKickMenuUp would make the offer never appear again.
		NetHideKickMenu(restoreOverlay: false);
		// Blocks are scoped to ONE level run (the card's "for that session only"). This is also
		// what stops them outliving the host's game entirely, since NetSession.Stop deliberately
		// does not clear them.
		EvilAliensWeb.Compat.Net.NetSession.ClearBlockedPeers();
		// KEEP THIS ABOVE THE PURGES (card 74403f83). ComponentBin.Add exempts the puppet layer
		// from the standing purge filter, and the only thing stopping that exemption dropping a
		// puppet into a scene that is tearing down is that EvSpawn / the snapshot path are gated
		// on NetActiveScene -- which has to be null BEFORE the purges arm the filter. Moving this
		// below them, or adding a purge above it, silently reopens the orphan hazard.
		if (NetActiveScene == this)
		{
			NetActiveScene = null;
		}
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
		if (snapshotdelaytimer.Finished)
		{
			snapshotdelaytimer.Reset();
			snapshotdelaytimer.Stop();
			float chancePercent = 10f;
			if (ScreenShotSpamEnabled)
			{
				chancePercent = 100f;
			}
			if (RandomHelper.RandomNextFloat(0f, 100f) <= chancePercent || (!snapshotExists && !snapshotMadeThisSession))
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
		if (++snapshotScanCounter < SnapshotScanInterval)
		{
			return;
		}
		snapshotScanCounter = 0;
		float interestScore = 0f;
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent component = item;
			if (!(component is AlienDrawableGameComponent))
			{
				continue;
			}
			Vector2 position = ((AlienDrawableGameComponent)(object)component).Position;
			if (!(position.X > 800f) && !(position.X < 0f) && !(position.Y > 600f) && !(position.Y < 0f))
			{
				interestScore += 1f;
				if (component is Explosion)
				{
					interestScore += 1f;
				}
				if (component is EvilBullet)
				{
					interestScore -= 0.66f;
				}
				if (component is Bullet)
				{
					interestScore -= 1f;
				}
				if (component is Lazer)
				{
					interestScore += 0.5f;
				}
				if (component is Asteroid && !((Asteroid)(object)component).Collides)
				{
					interestScore -= 1f;
				}
				if (component is FlyingSpider && !((FlyingSpider)(object)component).Collides)
				{
					interestScore -= 1f;
				}
				if (component is BloodExplosion)
				{
					interestScore += 1f;
				}
				if (component is Blast && ((Blast)(object)component).IsMini)
				{
					interestScore -= 0.8f;
				}
			}
		}
		if (interestScore > 30f)
		{
			snapshotdelaytimer.Start();
		}
	}

	private void takeScreenShot()
	{
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
			// Hook for levels that need to grab something extra at the exact snapshot
			// instant (the webcam level composites its player overlay in — the overlay
			// is torn down before SaveScreenShot runs, so it must be captured now).
			OnScreenshotResolved();
		}
	}

	// Called the instant the scene frame is resolved into MyScreenShot (during Draw).
	// Base does nothing; WebcamLevel overrides it to stash the player overlay.
	protected virtual void OnScreenshotResolved()
	{
	}

	// Arm a one-off screenshot regardless of the on-screen busy-ness heuristic. The
	// generic checkScreenShot only fires once a scene crosses ~30 on-screen entities,
	// which a sparse level (the webcam challenge) never does — this lets such a level
	// request a shot at a good moment. Guarded so it never double-captures.
	protected void ForceSnapshot()
	{
		if (!General.ScreenshotEnabled(level) || snapshotMadeThisSession || snapshottimer.Active)
		{
			return;
		}
		if (game1PostDrawEvent == null)
		{
			game1PostDrawEvent = takeScreenShot;
		}
		Game1.onPostDraw = (Game1.PostDrawEvent)Delegate.Combine(Game1.onPostDraw, game1PostDrawEvent);
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
			// standing: false — this is a clear-the-field-and-respawn-NOW purge: the ships
			// (AlienDrawableGameComponent) and the Get Ready banners (AnimatedMessage, via
			// ShowStartMessages) are re-added in this same tick and must not be diverted by
			// the standing purge filter (card 02d9ad67).
			Collection.Purge<AlienDrawableGameComponent>(standing: false);
			Collection.Purge<AnimatedMessage>(standing: false);
			Collection.Purge<TutorialMessage>(standing: false);
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
		if (!isDemo)
		{
			score.ShowStartMessages();
		}
		// Walk every SEATED slot, not 0..Players-1: online co-op's roster is host-allocated and
		// therefore sparse, and a hole would otherwise silently skip a real player's respawn.
		// The spread across the spawn edge is keyed to the player's ORDINAL among the seated
		// slots (not the raw slot index), so a dense offline roster spawns exactly where it
		// always did while a sparse one still spreads evenly.
		for (int i = 0; i < Oracle.MaxPlayers; i++)
		{
			if (!oracle.IsSeated(i))
			{
				continue;
			}
			int ordinal = oracle.SeatOrdinal(i);
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
					playerShip.Setup(i, new Vector2(800f / ((float)oracle.Players + 1f) * (float)ordinal, 648f), startup: true, invulnerable: false, 4.712389f);
					break;
				case PlayerSpawnType.West:
					playerShip.Setup(i, new Vector2(-48f, 600f / ((float)oracle.Players + 1f) * (float)ordinal), startup: true, invulnerable: false, 0f);
					break;
				case PlayerSpawnType.North:
					playerShip.Setup(i, new Vector2(800f / ((float)oracle.Players + 1f) * (float)ordinal, -48f), startup: true, invulnerable: false, (float)Math.PI / 2f);
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
