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

// INetScene (card 25ad0659 step 2c) is the net layer's view of this class: the fifteen members
// NetSession reaches on the live scene, so a headless scenario can assert on the ORDER of the
// state transitions a host broadcasts without a Game. The members it names are made `public`
// rather than explicitly implemented -- an implicit implementation must be public, and fifteen
// explicit stubs would be fifteen more names to keep in step. GameScene.NetActiveScene keeps its
// concrete type; the seam is NetScene.Current, which reads through it.
internal abstract class GameScene : Scene, EvilAliensWeb.Compat.Net.INetScene
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
		// No Unload() here since card 4d47c5ba -- the control diagrams now live in the shared
		// content manager (warmed once at boot), which this scene does not own.
		pausedScene.Show();
		Collection.Remove((GameComponent)(object)instructionsMenu);
	}

	private void playerOptions_OnExit(MenuSub1 sender)
	{
		pausedScene.Show();
		playerOptions.Remove();
	}

	private void eventList_OnCheckPointReached(GameEventList sender, GameEvent checkpoint)
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
	public void NetApplyReset(byte mode)
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

	public void NetApplyVictory()
	{
		if (_state == GameState.Normal)
		{
			Victory();
		}
	}

	public void NetApplyCheckpoint()
	{
		score.Save();
	}

	public void NetApplyBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp op, Vector2 v)
	{
		// SetAlienBaseN rewrites layer 0, which is only an alien-base floor on an alien-base scene:
		// on a space scene there is no layer 0 at all (IndexOutOfRange, taking the level down) and
		// on Mars layer 0 is the SKY, which it would quietly paint a base tile over. The
		// legitimate orderings can never do either -- Level 3 is an alien base throughout, and a
		// catch-up burst replays the scene op first -- but a publicly listed game has a stranger on
		// the far end. Logged ONCE: a stranger can send these at packet rate.
		if (IsNetAlienBaseTextureOp(op) && !Background.NetOnAlienBaseScene)
		{
			if (!netBadBaseOpLogged)
			{
				netBadBaseOpLogged = true;
				Console.WriteLine("[net] ignoring " + op + " off the wire -- not on an alien-base scene"
					+ " (logged once)");
			}
			return;
		}
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
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneSpace:
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneMars:
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneAlienBase:
			NetApplySceneChange(op);
			break;
		}
	}

	private bool netBadBaseOpLogged;

	private static bool IsNetAlienBaseTextureOp(EvilAliensWeb.Compat.Net.NetBackgroundOp op)
	{
		return op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase2
			|| op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase3
			|| op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase4
			|| op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase5
			|| op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase6;
	}

	// The host's script swapped the whole backdrop mid-level (card ca4fd94f). Only InsaneBossI
	// does that today, and its swaps carry side effects beyond the backdrop -- hence the virtual:
	// a level whose Go* handler does more than call the setter overrides this and mirrors the
	// rest. Everything a level mirrors here must be LOCAL: the music already replicates as its own
	// EvMusic beat, and world contents are host-authoritative.
	internal virtual void NetApplySceneChange(EvilAliensWeb.Compat.Net.NetBackgroundOp op)
	{
		switch (op)
		{
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneSpace:
			Background.SetSpace();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneMars:
			Background.SetMars();
			break;
		case EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneAlienBase:
			Background.SetAlienBase();
			break;
		}
	}

	// ---- Online co-op (card 9a3175d0): decorative swarms as one "effect on/off" beat --------

	// One entry per decorative swarm currently running in this level. It means two things at
	// once, deliberately, because they are the same fact from the two ends of the wire:
	//   * on the HOST it is the LATCH -- what a join-in-progress peer has to be caught up to
	//     (Spawner null; the host's real spawner lives in the level script's eventList);
	//   * on a CLIENT it is the live effect -- our own copy of the spawner, which we tick
	//     ourselves because the script that owns the host's never runs here.
	// Sharing the list is what lets NetCatchUpStateLine print the same field on both peers, so
	// two windows can be diffed for "are we running the same scenery".
	private struct NetCosmeticEntry
	{
		public EvilAliensWeb.Compat.Net.NetCosmeticKind Kind;
		public float Rate;
		public GameEvent Spawner;
		// Host only: how many of our own spawners of this kind are announcing. The beat is per
		// KIND but netAnnounced is per SPAWNER, so two overlapping spawners of one kind (nothing
		// ships that today, but a level script is one line away from it) would otherwise have the
		// first one's Terminate send an "off" while the second is still spawning -- killing the
		// joiner's scenery for the rest of the level, silently, with the host's own screen full.
		public int Refs;
	}

	private readonly System.Collections.Generic.List<NetCosmeticEntry> netCosmeticSwarms
		= new System.Collections.Generic.List<NetCosmeticEntry>();

	// A rate off the wire drives GenericSpawner's `while (num >= 1f) DoEvent()` loop, so a
	// non-finite one wedges the tick outright and a merely huge one spawns its whole backlog in
	// a single frame -- and a publicly listed game has a stranger on the other end
	// (Background.NetSetDoodadPos guards NaN for the same reason).
	//
	// This bounds the AUTHORED rate, which is not the rate in flight: GenericSpawner multiplies
	// it per tick by DifficultyModifier and MultiPlayerDifficultyModifier, so the effective
	// ceiling is a few times this. Hence a ceiling near the shipped rates (the densest are
	// Level 2's fog swarm at 5.5/s and AsteroidChase's belt at 5/s) rather than a round big
	// number -- the point is "cannot be weaponised", not "room for a future swarm". A denser
	// swarm that genuinely wants more raises this deliberately.
	private const float NetCosmeticMaxRate = 12f;

	// Host: our level script just turned a decorative swarm on or off. Latch it, then send it.
	//
	// Latching FIRST is the point: NetSession.OnCosmeticSwarm early-returns while no peer is
	// connected, and for a LISTED single-player game that is exactly the window whose beats a
	// join-in-progress peer will need replayed. Background's netLast* latches exist for the same
	// reason, and are kept for the same distance from the send path.
	internal static void NetNoteCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind kind, bool on, float rate)
	{
		// A client's OWN copies of these spawners announce too (they are the same class running
		// the same Update) -- it must never latch or emit, or its live set would double up with a
		// latch of itself.
		if (EvilAliensWeb.Compat.Net.NetSession.IsClient)
		{
			return;
		}
		// Emit only on the 0<->1 edge of the per-kind refcount, so an overlapping pair of our own
		// spawners cannot send an "off" that outlives the effect (see NetCosmeticEntry.Refs). With
		// no scene there is nothing to count against, which only happens outside a level -- where
		// no spawner runs anyway.
		if (NetActiveScene == null || NetActiveScene.NetLatchCosmeticSwarm(kind, on, rate))
		{
			EvilAliensWeb.Compat.Net.NetSession.OnCosmeticSwarm(kind, on, rate);
		}
	}

	// Returns whether this announce CHANGED whether the kind is running -- i.e. whether the peer
	// needs telling.
	private bool NetLatchCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind kind, bool on, float rate)
	{
		for (int i = 0; i < netCosmeticSwarms.Count; i++)
		{
			if (netCosmeticSwarms[i].Kind != kind)
			{
				continue;
			}
			NetCosmeticEntry e = netCosmeticSwarms[i];
			if (on)
			{
				// A repeat from a SECOND spawner joins the count; the newest rate wins, since the
				// peer runs one spawner per kind either way.
				e.Refs++;
				e.Rate = rate;
				netCosmeticSwarms[i] = e;
				return false;
			}
			if (--e.Refs > 0)
			{
				netCosmeticSwarms[i] = e;
				return false;
			}
			netCosmeticSwarms.RemoveAt(i);
			return true;
		}
		if (!on)
		{
			return false;
		}
		netCosmeticSwarms.Add(new NetCosmeticEntry { Kind = kind, Rate = rate, Refs = 1 });
		return true;
	}

	// Client: the host turned a decorative swarm on or off -- run (or stop running) our own copy.
	// Idempotent: a repeated "on" replaces the spawner (the rate may have changed), an "off" for
	// something we are not running is a no-op. Entities already in flight are left alone, which
	// is what the host does too -- its spawner stopping does not kill what it already spawned.
	public void NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind kind, bool on, float rate)
	{
		NetDropCosmeticSwarm(kind);
		if (!on || !float.IsFinite(rate) || rate <= 0f)
		{
			return;
		}
		// Clamp ONCE, here, so the entry, the state line and the spawner all report the same
		// number -- storing the raw rate and clamping only what reaches the spawner would make
		// eaNetBg()'s two-window diff lie about what is actually running.
		rate = MathHelper.Min(rate, NetCosmeticMaxRate);
		GameEvent spawner = NetBuildCosmeticSpawner(kind, rate);
		if (spawner != null)
		{
			// The eventList Resets an event as it activates it (GameEventList.progressList), and
			// some spawners read constructor-body fields only from Reset -- AsteroidSpawner's
			// startedWithAReallyBigOne is set from `startBig`, which the base constructor's own
			// Reset call cannot see yet. Start ours the same way the script would.
			spawner.Reset();
			netCosmeticSwarms.Add(new NetCosmeticEntry { Kind = kind, Rate = rate, Spawner = spawner });
		}
	}

	private GameEvent NetBuildCosmeticSpawner(EvilAliensWeb.Compat.Net.NetCosmeticKind kind, float rate)
	{
		switch (kind)
		{
		case EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground:
			return new FlyingSpiderEvent(base.Game, 0f, rate, isbackground: true);
		case EvilAliensWeb.Compat.Net.NetCosmeticKind.BackgroundAsteroids:
		{
			// startWithBig:false + SetBackGroundOnly() is what makes this copy purely decorative:
			// the host's spawner emits ONE collidable asteroid (and an opening big one) per event
			// alongside the two background ones, and those stay replicated as puppets. Spawning
			// our own would put a real hazard on this screen and nowhere else.
			//
			// KNOWN LIMIT, accepted: AsteroidSpawner sweeps its entry HEADING on its own timers
			// from Reset, so our copy's decorations only fly parallel to the replicated rocks
			// beside them while the two cycles stay in phase. For a live pairing they start
			// within an RTT of each other and effectively do; a JOIN-IN-PROGRESS peer starts its
			// cycle when the catch-up beat lands and is out of phase for the rest of that belt,
			// so its grey rocks drift at a visibly different angle from the real ones. Fixing it
			// properly means streaming the angle, which is the per-entity cost this whole card
			// removes -- so it is a decoration-vs-decoration mismatch we take on purpose.
			AsteroidSpawner asteroids = new AsteroidSpawner(base.Game, 0f, rate, startWithBig: false);
			asteroids.SetBackGroundOnly();
			return asteroids;
		}
		default:
			// An unknown kind from a newer peer. The build-hash handshake makes this unreachable
			// between real builds; ignoring it degrades to "no scenery", never to a crash.
			return null;
		}
	}

	private void NetDropCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind kind)
	{
		for (int i = netCosmeticSwarms.Count - 1; i >= 0; i--)
		{
			if (netCosmeticSwarms[i].Kind == kind)
			{
				netCosmeticSwarms.RemoveAt(i);
			}
		}
	}

	// Both peers, at the checkpoint revert: the host's eventList drops its active events without
	// terminating them (so no "off" beat is ever sent), and the purge in the same block wipes the
	// scenery itself. Clearing here keeps the two ends symmetric -- the host's re-activated
	// spawner re-announces on its next tick, and one that the revert left BEHIND correctly stays
	// off on both screens.
	private void NetClearCosmeticSwarms()
	{
		netCosmeticSwarms.Clear();
	}

	// Host: replay the swarms our script already started, for a peer that just came up (EvReady).
	// `emit` is the sink rather than a hard call to NetSession.OnCosmeticSwarm for the same reason
	// Background.NetReplayCatchUp takes one -- it is what makes the catch-up testable as a pure
	// latch -> wire -> apply function in one tab, with no second peer.
	internal void NetReplayCosmeticSwarms(Action<EvilAliensWeb.Compat.Net.NetCosmeticKind, float> emit)
	{
		foreach (NetCosmeticEntry e in netCosmeticSwarms)
		{
			emit(e.Kind, e.Rate);
		}
	}

	// The client-apply leg of eaNetCosmetic() (card 9a3175d0). It lives here rather than in
	// NetCosmeticTest because the live swarm set is this scene's, and the leg has to put back
	// exactly what it found -- entries hold a spawner reference on a client, so restoring them
	// as a fresh latch would stop the joiner's scenery dead.
	//
	// What it proves: a beat off the wire builds the right effect, a repeat REPLACES rather than
	// stacks (a checkpoint revert re-announces, so this happens in every real run), an off beat
	// removes it, and a hostile or broken rate cannot reach GenericSpawner's
	// `while (num >= 1f) DoEvent()` loop -- a NaN or a huge rate there wedges the tick outright,
	// and a publicly listed game has a stranger on the other end.
	internal void NetCosmeticSelfTest(Action<bool, string> Check)
	{
		System.Collections.Generic.List<NetCosmeticEntry> saved
			= new System.Collections.Generic.List<NetCosmeticEntry>(netCosmeticSwarms);

		netCosmeticSwarms.Clear();
		Check(NetCosmeticStateField() == "-", "an empty set prints as '-'");

		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, 5.5f);
		Check(netCosmeticSwarms.Count == 1 && netCosmeticSwarms[0].Spawner is FlyingSpiderEvent,
			"an 'on' beat builds the type's real spawner");

		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.BackgroundAsteroids, on: true, 4f);
		Check(netCosmeticSwarms.Count == 2, "a second kind runs alongside the first");
		Check(netCosmeticSwarms.Count == 2 && netCosmeticSwarms[1].Spawner is AsteroidSpawner,
			"the second kind builds ITS spawner");

		// A repeat is the checkpoint-revert case: the host's re-activated event announces again.
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, 3f);
		Check(netCosmeticSwarms.Count == 2, "a repeated 'on' REPLACES rather than stacking");

		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: false, 0f);
		Check(netCosmeticSwarms.Count == 1
			&& netCosmeticSwarms[0].Kind == EvilAliensWeb.Compat.Net.NetCosmeticKind.BackgroundAsteroids,
			"an 'off' beat removes only that kind");
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: false, 0f);
		Check(netCosmeticSwarms.Count == 1, "an 'off' for something we are not running is a no-op");

		// Hostile / broken rates.
		netCosmeticSwarms.Clear();
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, float.NaN);
		Check(netCosmeticSwarms.Count == 0, "a NaN rate is refused");
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground,
			on: true, float.PositiveInfinity);
		Check(netCosmeticSwarms.Count == 0, "an infinite rate is refused");
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, -1f);
		Check(netCosmeticSwarms.Count == 0, "a negative rate is refused");
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, 1e9f);
		Check(netCosmeticSwarms.Count == 1
			&& netCosmeticSwarms[0].Rate == NetCosmeticMaxRate
			&& NetCosmeticSpawnerRate(netCosmeticSwarms[0].Spawner) == NetCosmeticMaxRate,
			"an absurd rate is clamped, in the entry AND the spawner");
		// Positive control for the clamp: a real rate must pass through untouched, or the check
		// above would also pass with every rate pinned to the ceiling.
		netCosmeticSwarms.Clear();
		NetApplyCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, 5.5f);
		Check(netCosmeticSwarms.Count == 1 && netCosmeticSwarms[0].Rate == 5.5f
			&& NetCosmeticSpawnerRate(netCosmeticSwarms[0].Spawner) == 5.5f,
			"a shipped rate reaches the spawner unclamped (positive control)");

		// The HOST latch's refcount. Its failure mode is the nastiest one here -- an "off" from
		// the first of two overlapping spawners, killing the joiner's scenery while the host's
		// own screen stays full -- and it is unreachable from any shipped level script, so it can
		// only ever be checked here.
		netCosmeticSwarms.Clear();
		Check(NetLatchCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, 5.5f),
			"the first spawner's 'on' is worth a beat");
		Check(!NetLatchCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: true, 2f),
			"a second spawner of the same kind sends nothing");
		Check(netCosmeticSwarms.Count == 1 && netCosmeticSwarms[0].Rate == 2f,
			"...but the newest rate wins");
		Check(!NetLatchCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: false, 0f),
			"the FIRST of the two ending sends nothing");
		Check(netCosmeticSwarms.Count == 1, "...and the swarm stays latched");
		Check(NetLatchCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: false, 0f),
			"the LAST one ending is worth a beat");
		Check(netCosmeticSwarms.Count == 0, "...and clears the latch");
		Check(!NetLatchCosmeticSwarm(EvilAliensWeb.Compat.Net.NetCosmeticKind.FlyingSpiderBackground, on: false, 0f),
			"an 'off' with nothing latched sends nothing");

		netCosmeticSwarms.Clear();
		netCosmeticSwarms.AddRange(saved);
	}

	private static float NetCosmeticSpawnerRate(GameEvent spawner)
	{
		return spawner is GenericSpawner g ? g.HitsPerSecond : -1f;
	}

	// Join-in-progress catch-up (card 45a4e48d), host side: bring a peer whose GameScene has
	// just come up (EvReady) up to the scenery state our level script already reached. The
	// joiner ran its own Initialize, so it holds the level's INITIAL background + music and --
	// the script being host-only (11.2 sim-split) -- will never reach those beats itself.
	// Everything here is an ordinary reliable beat event, so the client applies it through the
	// same paths the live ops use.
	public void NetReplayCatchUp()
	{
		Background.NetReplayCatchUp(EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp);
		// Card 9a3175d0: and the decorative swarms, which are the same kind of "already fired,
		// and the script will never fire it again" state as the background ops.
		NetReplayCosmeticSwarms((kind, rate) =>
			EvilAliensWeb.Compat.Net.NetSession.OnCosmeticSwarm(kind, on: true, rate));
		EvilAliensWeb.Compat.Net.NetSession.OnMusic(base.SoundManager.NetCurrentSong);
	}

	// The catch-up state as one parseable line, for the eaNetBg() console dump.
	internal string NetCatchUpStateLine()
	{
		string line = Background.NetStateLine() + " song=" + base.SoundManager.NetCurrentSong
			+ " cosmetic=" + NetCosmeticStateField();
		string levelState = NetSceneChangeState();
		return (levelState.Length == 0) ? line : line + " " + levelState;
	}

	// Whatever a level mirrors in NetApplySceneChange BEYOND the backdrop, as one field, so that
	// mirror is visible to the eaNetBg() two-window diff and covered by the eaNetBgTest round trip
	// -- Background's state line cannot see it, and a mirror nothing can observe is a mirror
	// nobody notices breaking. Empty for the levels that mirror nothing, which is all but one, so
	// their state line is unchanged.
	protected virtual string NetSceneChangeState()
	{
		return "";
	}

	// The wipe half of the same seam (debug only, the eaNetBgTest round trip): put that
	// level-specific state back to what a peer that just ran its own Initialize holds. Without it
	// the state survives the wipe and its leg of the round trip passes vacuously -- the same trap
	// Background.NetTestWipe's entry-scene rebuild exists for.
	internal virtual void NetSceneChangeTestWipe()
	{
	}

	// The decorative swarms as one field. Prints the KIND and RATE only, which both peers hold
	// (the host as its latch, a client as its live spawners), so two windows can be diffed for
	// "same scenery running" -- the entities themselves are supposed to be in different places.
	private string NetCosmeticStateField()
	{
		if (netCosmeticSwarms.Count == 0)
		{
			return "-";
		}
		string s = "";
		foreach (NetCosmeticEntry e in netCosmeticSwarms)
		{
			if (s.Length > 0)
			{
				s += ",";
			}
			s += e.Kind.ToString() + "@" + e.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		return s;
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
	//
	// One deliberate residue: replaying the cosmetic leg puts the swarm entries back through the
	// CLIENT apply path, so on a host they end up holding a spawner object nobody ticks (only the
	// SuppressLevelScript branch of UpdateNormal ticks them). Kind and rate -- the whole of what
	// the catch-up replays and what the state line reports -- are unchanged, so a later pairing
	// still catches a joiner up correctly.
	internal string NetCatchUpSelfTest()
	{
		string before = NetCatchUpStateLine();
		System.Collections.Generic.List<(EvilAliensWeb.Compat.Net.NetBackgroundOp Op, Vector2 V)> burst
			= new System.Collections.Generic.List<(EvilAliensWeb.Compat.Net.NetBackgroundOp, Vector2)>();
		Background.NetReplayCatchUp((op, v) => burst.Add((op, v)));
		// Card 9a3175d0: the decorative swarms ride the same catch-up. Captured through the REAL
		// wire codec rather than by calling the apply path directly -- a byte-layout slip in
		// EncodeCosmeticSwarmEvent is exactly the class of bug a self-test that skipped the
		// encode could not see.
		System.Collections.Generic.List<byte[]> cosmeticBurst = new System.Collections.Generic.List<byte[]>();
		NetReplayCosmeticSwarms((kind, rate) => cosmeticBurst.Add(
			EvilAliensWeb.Compat.Net.NetProtocol.EncodeCosmeticSwarmEvent(0, (byte)kind, on: true, rate)));
		int song = base.SoundManager.NetCurrentSong;
		Background.NetTestWipe();
		NetSceneChangeTestWipe();
		NetClearCosmeticSwarms();
		base.SoundManager.NetApplyMusic(-1);
		string joiner = NetCatchUpStateLine();
		foreach (byte[] ev in cosmeticBurst)
		{
			NetApplyCosmeticSwarm((EvilAliensWeb.Compat.Net.NetCosmeticKind)ev[4], ev[5] != 0,
				EvilAliensWeb.Compat.Net.NetProtocol.ReadF32(ev, 6));
		}
		foreach ((EvilAliensWeb.Compat.Net.NetBackgroundOp Op, Vector2 V) op in burst)
		{
			NetApplyBackgroundOp(op.Op, op.V);
		}
		base.SoundManager.NetApplyMusic(song);
		string after = NetCatchUpStateLine();
		// Name the ops, not just the count: a leg the level never fired is absent from this list,
		// so a PASS can't be read as covering more than the run actually exercised. The cosmetic
		// swarms are named the same way and for the same reason -- Level 1's belt fires the
		// asteroid leg and no spider leg, Level 2 the reverse, so neither run covers both.
		string ops = burst.Count == 0 ? "(none)" : string.Join(",", burst.ConvertAll(o => o.Op.ToString()));
		ops += " cosmetic=" + (cosmeticBurst.Count == 0 ? "(none)"
			: string.Join(",", cosmeticBurst.ConvertAll(e => ((EvilAliensWeb.Compat.Net.NetCosmeticKind)e[4]).ToString())));
		return "[netbgtest] " + (after == before ? "PASS" : "FAIL") + " ops=" + ops
			+ "\n  host   : " + before
			+ "\n  joiner : " + joiner
			+ "\n  caught : " + after;
	}

	// The peer broke a tether on its screen (or-of-either-peer, idempotent).
	//
	// TeamChallenge extends this for its own scripted tether. The base body covers the Linker
	// ("2") powerup's connector, which since card 83271f3d can actually form in an online session:
	// each peer builds and breaks its own copy locally off its own collisions, so a hit only one
	// screen saw would otherwise leave this one tethered to a puppet whose owner is already free.
	public virtual void NetApplyTetherBreak()
	{
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.NetBreakConnectors();
		}
	}

	// The REMOTE peer paused/resumed. Freeze/unfreeze our world like a local pause, but
	// with a hint overlay instead of an interactive menu. Called from NetSession (which
	// keeps ticking while the collection is pushed). Overlap rules: if OUR pause menu is
	// up the world is already frozen -- just remember the flag (NetSession.RemotePaused);
	// the local resume paths re-freeze if it is still set.
	public void NetSetRemotePaused(bool on)
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
	public bool NetShowKickMenu()
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
	public void NetSetPeerStalled(bool on)
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

	// The one-batch Braineroid glow driver (card 391e11d2). Built lazily and kept across plays
	// like the scene itself, which is a re-added singleton.
	private BraineroidGlows[] braineroidGlows;

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
		// Level scenes are re-added singletons, so start every play from an empty decorative-swarm
		// set. Terminate clears it too; this covers any exit path that never reached Terminate.
		NetClearCosmeticSwarms();
		pausestopper.Reset();
		pausestopper.Stop();
		Background.Reset();
		// Arm the scene-swap replication for this play (card ca4fd94f). Deliberately NOT folded
		// into Reset(): every scene setter calls that too, so it cannot tell a level entry from a
		// mid-level swap. Initialize can, and a checkpoint revert does not re-run it.
		Background.NetBeginLevel();
		((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)Background);
		((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)Foreground);
		eventList.Reset();
		score.Reset();
		score.Save();
		score.Lives = -1;
		Collection.Add((GameComponent)(object)score);
		// One additive batch for every Braineroid glow on screen, instead of two blend flips per
		// brain (card 391e11d2). Owned here rather than by a level because four different
		// spawners across several levels produce Braineroids, and every one of them runs inside a
		// GameScene. Removed in Terminate -- level scenes are re-added singletons, so a drawable
		// left in the bin would draw over later scenes.
		if (braineroidGlows == null)
		{
			braineroidGlows = new BraineroidGlows[BraineroidGlows.Bands.Length];
			for (int i = 0; i < braineroidGlows.Length; i++)
			{
				braineroidGlows[i] = new BraineroidGlows(base.Game, BraineroidGlows.Bands[i]);
			}
		}
		for (int i = 0; i < braineroidGlows.Length; i++)
		{
			Collection.Add((GameComponent)(object)braineroidGlows[i]);
		}
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
			// The whole-SCENE swap (card ca4fd94f) leads, because a scene setter runs Reset() and
			// would wipe the doodad/speed/belt state the next beat sets -- this order leaves the
			// end of the script covering every catch-up leg at once. It goes to the ALIEN BASE
			// rather than anywhere else on purpose: that also parks the level on a scene with a
			// base layer, which is what gives the SetAlienBaseN beat below (and so that leg of the
			// catch-up) its first rig on a space level.
			Background.SetAlienBase();
		};
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 2f);
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
		// Both decorative-swarm kinds (card 9a3175d0), left running for the rest of the script:
		// the joiner must show scenery of its own from its own spawners, and the ASTEROID one is
		// the mixed case worth watching -- the two background rocks of each DoEvent stop being
		// replicated while the real one beside them still arrives as a puppet. A fog swarm over a
		// space level is not what Level 2 looks like; this is a beat rig, not a look rig.
		FlyingSpiderEvent netScriptFog = new FlyingSpiderEvent(base.Game, 0f, 5.5f, isbackground: true);
		eventList.AddEvent(netScriptFog, halting: false);
		AsteroidSpawner netScriptBelt = new AsteroidSpawner(base.Game, 0f, 2f, startWithBig: false);
		eventList.AddEvent(netScriptBelt, halting: false);
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
			// The floor-texture switch, which only works because the scene beat above parked us on
			// an alien base. Left switched for the rest of the script so the catch-up's
			// SetAlienBaseN leg is live at the end of a run.
			Background.SetAlienBase2();
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
			ControlDevice padDevice = i switch
			{
				0 => ControlDevice.PadOne, 
				1 => ControlDevice.PadTwo, 
				2 => ControlDevice.PadThree, 
				3 => ControlDevice.PadFour, 
				_ => throw new Exception(), 
			};
			if (oracle.DeviceIsPlaying(padDevice) && (!base.InputHandler.PadConnected(i) || base.InputHandler.PadPressed(PadKeys.Start, i)))
			{
				pauseRequested = true;
				controlDevice = padDevice;
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
	public bool NetEndingNormally => _state == GameState.Victory || _state == GameState.GameOver;

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

	public void NetApplyPeerLeft()
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
	public void SpawnPlayer(ControlDevice controlDevice, int slot)
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
			NetClearCosmeticSwarms();
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
	public bool JoinWouldSpawnNow { get; private set; }

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
		else
		{
			// ...but the DECORATIVE swarms the host announced are ours to run (card 9a3175d0):
			// they take no NetIds and no snapshot turns, so nothing replicates them in. Ticking
			// them HERE rather than from a component of their own is what gets pause, victory
			// and resetting for free -- UpdateNormal only runs in GameState.Normal, and a pause
			// Push disables the whole scene. Every one has an infinite lifetime, so none can
			// Terminate mid-loop and mutate the list underneath it.
			for (int i = 0; i < netCosmeticSwarms.Count; i++)
			{
				// A LATCH entry (host side) carries no spawner. A client cannot make one --
				// NetNoteCosmeticSwarm refuses to latch here -- but this loop is on the tick path
				// and the failure would be a hard crash, so it is not worth asserting instead.
				netCosmeticSwarms[i].Spawner?.Update(gameTime);
			}
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
		// Level scenes are re-added singletons (the stall-banner reasoning above), so a swarm
		// left in the list would be replayed to a joiner -- or ticked by a client -- on the NEXT
		// play of this level, where the script never announced it.
		NetClearCosmeticSwarms();
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
		if (braineroidGlows != null)
		{
			for (int i = 0; i < braineroidGlows.Length; i++)
			{
				Collection.Remove((GameComponent)(object)braineroidGlows[i]);
			}
		}
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
		// Unseat everyone on the way OUT, not just on the way in (card ee96ea61). The launch
		// paths all ResetPlayers() before they seat, so between a scene ending and the next
		// launch the roster used to hold whatever the last level or attract demo left behind --
		// and that window is exactly where the menu-lobby handshake runs. NetSession's host-side
		// allocator reads the roster unguarded (its client twin LocalBlockedSlots is guarded),
		// so an attract demo's leftover seats reached FirstMutuallyFreeSlot and could answer a
		// perfectly good joiner with RejectFull, or push their primary onto the wrong HUD panel.
		// Cheap and total: PlayerInfo.Reset() only clears isPlaying, so no score, hue or unlock
		// progress rides on this. LAST in the method deliberately -- OnFinished above has
		// already queued the next scene (credits/menu), and neither seats anyone, so nothing in
		// this teardown's own flow can be undone by it.
		oracle.ResetPlayers();
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

	// Debug seam (card d67755d2): drive the level-select thumbnail capture WITHOUT ending the
	// level. `SaveScreenShot` -- and so the alpha seal in it -- normally runs only from
	// Terminate, and reaching it for real means an on-screen busy-ness heuristic (>30 entities,
	// two timers) followed by a pause-menu quit, which is neither cheap nor deterministic for a
	// probe. Console `eaShotNow()` / `eval ShotNow` under eahl.
	//
	// TWO steps because the grab itself happens in the post-Draw hook: Arm on one tick, Save on
	// a later one. Save reports whether it had anything to persist, so a rig cannot mistake "no
	// snapshot was ever grabbed" for a pass.
	internal static bool DebugArmSnapshot(out string why)
	{
		why = null;
		GameScene scene = NetActiveScene;
		if (scene == null)
		{
			why = "no live GameScene";
			return false;
		}
		// ForceSnapshot's FIRST guard is General.ScreenshotEnabled, which it fails SILENTLY -- and
		// it is false for WebcamAliens unless the player opted into Settings.WebcamScreenshot
		// (default off), i.e. the default case on the one level with a bespoke capture path.
		// Clearing the other two guards below does not clear that one, so without this test the
		// seam would report "armed" and then "nothing to save", which reads as a broken rig
		// rather than a level that does not capture.
		if (!General.ScreenshotEnabled(scene.level))
		{
			why = scene.level + " does not capture a level-select thumbnail "
				+ "(General.ScreenshotEnabled is false; WebcamAliens needs Settings.WebcamScreenshot)";
			return false;
		}
		// ForceSnapshot no-ops once a shot has been made this session; clear that so the seam is
		// repeatable within one level run.
		scene.snapshotMadeThisSession = false;
		scene.snapshottimer.Stop();
		scene.snapshottimer.Reset();
		scene.ForceSnapshot();
		return true;
	}

	internal static bool DebugSaveSnapshot(out string why)
	{
		why = null;
		GameScene scene = NetActiveScene;
		if (scene == null)
		{
			why = "no live GameScene";
			return false;
		}
		if (!scene.snapshotMadeThisSession || scene.MyScreenShot == null)
		{
			why = "nothing grabbed yet -- call arm first, then step at least one frame";
			return false;
		}
		ScreenshotSaver.SaveScreenShot((Texture2D)(object)scene.MyScreenShot, scene.level);
		return true;
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
			// The script is about to run for the first time, so whatever backdrop is up now is the
			// one this level's Initialize built -- i.e. the one a join peer gets for free. Both
			// call orders are settled by here (Level1 sets its scene BEFORE base.Initialize(),
			// InsaneBossI after), and NetNoteEntryScene is one-shot per Initialize, so the Startup
			// a checkpoint revert passes back through cannot re-capture a swapped scene as the
			// entry -- which would silently drop the latch a joiner needs.
			Background.NetNoteEntryScene();
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
