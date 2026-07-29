using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens;

internal class TeamChallenge : GameScene
{
	private ShipConnector connector;

	// Online co-op (card 11.3): the remote puppet ship may join a beat after our own ship
	// spawns, so connector creation is deferred until both ships exist. Armed by OnReset
	// (each life), cleared on creation. Shared-fate death checking only starts once the
	// link existed this life (netLinkUp) -- otherwise the one-ship startup window would
	// read as "partner died".
	private bool netConnectorPending;

	private bool netLinkUp;

	public TeamChallenge(Game game)
		: base(game, Levels.TeamChallenge)
	{
		base.OnReset += TeamChallenge_OnReset;
	}

	private void TeamChallenge_OnReset()
	{
		if (EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			netConnectorPending = true;
			netLinkUp = false;
			return;
		}
		connector = ShipConnector.NewAlien(Collection, base.Game);
		connector.Setup(oracle.GetShips()[0], oracle.GetShips()[1]);
		Collection.Add((GameComponent)(object)connector);
	}

	// The peer broke the tether on its screen (or-of-either-peer, idempotent).
	internal override void NetApplyTetherBreak()
	{
		if (connector != null)
		{
			connector.NetBreakSilently();
		}
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == connector)
		{
			connector = null;
		}
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)3);
		Background.SetSpace();
		// Difficulty is now the menu-selected one (routed through DifficultyMenu as a
		// LevelType.Challenge, like the other challenges), instead of a hard-coded Medium — so
		// lock at whatever the player picked and let the music variant follow it (lyric cut
		// earned on Hard+, clean instrumental below), same as AsteroidChase/ClassicAliens.
		base.SoundManager.PlayMusic(SoundManager.ClassicForDifficulty());
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
		// The device that launched the level, BEFORE ResetPlayers wipes the roster. Every other
		// scene just plays whatever Game1.MenuFinished seated; this one re-seats its own slots
		// (it needs two), and seating slot 0 as Keyboard REGARDLESS -- what the 2008 build did --
		// hands a pad-only player a ship they cannot steer.
		int primarySlot = EvilAliensWeb.Compat.Net.NetSession.LocalPrimarySlot;
		ControlDevice starter = oracle.IsSeated(primarySlot) ? oracle.Controller(primarySlot) : ControlDevice.Keyboard;
		oracle.ResetPlayers();
		ControlDevice primary = ResolvePrimarySeat(starter, base.InputHandler.PadConnected);
		// Online co-op (card 4d904410): the host allocates every slot, so seat our primary in
		// the slot we were granted (offline / host-side that is 0, exactly as before).
		if (!oracle.AddPlayerAt(primarySlot, primary))
		{
			oracle.AddPlayer(primary);
		}
		// Online co-op: seat ONLY the local device -- the partner joins as
		// ControlDevice.Remote through the net layer. Seating a local second device here
		// would (a) squat the slot the remote puppet needs and (b) for a pad, trip the
		// disconnected-gamepad force-pause every tick (GameScene.Update's PadConnected
		// check) -- the card's "pause triggers are local devices only" gotcha.
		if (!EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			oracle.AddPlayer(ResolvePartnerSeat(primary, base.InputHandler.PadConnected, EvilAliensWeb.Compat.DebugFlags.TeamPartner));
		}
	}

	// ---- Who sits where (card e6927ef8) -------------------------------------------------
	// The 2008 build seated Keyboard + PadOne flat, and on this port that made the level
	// UNPLAYABLE for a keyboard-only player: GameScene.Update raises pauseRequested on every tick
	// a seated pad device reads !InputHandler.PadConnected(i), so the world was pushed into the
	// pause menu, dismissed, and re-paused next tick, forever (measured: ticks=0 prog=2/52 over
	// 37 sim-seconds -- the world never advanced at all).
	// Both seats are now resolved from what is actually THERE, by two pure functions so
	// eaTeamSeat() / tools/sim/logic_probe can table-drive every case instead of needing a live
	// level and four physical gamepads (the NetSession.OwnsSlotCore idiom).
	// THE INVARIANT BOTH UPHOLD: neither seat is ever a pad that is not connected -- precisely
	// the force-pause's precondition, so the loop is unreachable by construction. GameScene's
	// guard itself is deliberately untouched: a pad dying MID-RUN should still say so.

	// Slot 0: the device that launched the level, if it can actually drive a ship. A pad-only
	// player navigated the menu with their pad, and handing them a Keyboard ship (the 2008
	// behaviour) leaves them steering nothing. Anything that is not a live local ship driver --
	// Generic (no input case AND no key bound to Generic_Start on this port), AI, the net
	// puppets, or a pad that has gone away since the menu -- falls back to Keyboard, which is
	// always drivable here (mouse aim included).
	internal static ControlDevice ResolvePrimarySeat(ControlDevice starter, Func<int, bool> padConnected)
	{
		int pad = PadIndexOf(starter);
		return (pad >= 0 && padConnected(pad)) ? starter : ControlDevice.Keyboard;
	}

	// Slot 1: the partner. A connected pad THE PRIMARY IS NOT USING means a second human is
	// there, which is the level's original two-human co-op; otherwise ControlDevice.AI flies it
	// as an auto-pilot partner (the level-select briefing says so, and TryAdoptJoinDevice below
	// hands the seat over the moment a real pad joins).
	// How WELL the bot plays this level is not established: the completion matrix's TeamChallenge
	// row is a TIMEOUT at ~90 deaths with both ships bot-driven, and its one clean run (VICTORY
	// 402s, 0 deaths) was an ?invuln control. The bot makes the level reachable and playable, which
	// beats a permanent pause menu; do not read it as "the bot can finish it for you".
	internal static ControlDevice ResolvePartnerSeat(ControlDevice primary, Func<int, bool> padConnected, EvilAliensWeb.Compat.DebugFlags.TeamPartnerSeat forced)
	{
		if (forced == EvilAliensWeb.Compat.DebugFlags.TeamPartnerSeat.Ai)
		{
			return ControlDevice.AI;
		}
		// ?teampartner=pad restores the pre-card seating VERBATIM -- an unconditional PadOne,
		// connected or not. It is the deliberate way to reach the force-pause this card removed,
		// i.e. the negative control, so it must not be softened by the checks below.
		if (forced == EvilAliensWeb.Compat.DebugFlags.TeamPartnerSeat.Pad)
		{
			return ControlDevice.PadOne;
		}
		for (int i = 0; i < 4; i++)
		{
			if (padConnected(i) && PadDeviceAt(i) != primary)
			{
				return PadDeviceAt(i);
			}
		}
		return ControlDevice.AI;
	}

	private static ControlDevice PadDeviceAt(int i)
	{
		return i switch
		{
			0 => ControlDevice.PadOne,
			1 => ControlDevice.PadTwo,
			2 => ControlDevice.PadThree,
			3 => ControlDevice.PadFour,
			_ => throw new Exception()
		};
	}

	// The pad index a device reads, or -1 for anything that is not a pad.
	internal static int PadIndexOf(ControlDevice device)
	{
		return device switch
		{
			ControlDevice.PadOne => 0,
			ControlDevice.PadTwo => 1,
			ControlDevice.PadThree => 2,
			ControlDevice.PadFour => 3,
			_ => -1
		};
	}

	// A real pad pressing Start TAKES OVER the auto-pilot's seat instead of adding a third ship.
	// This is what keeps two-human co-op working at all: the browser Gamepad API only exposes a
	// pad AFTER a button is pressed on it in the page, so player two's idle pad reads
	// DISCONNECTED while Initialize is resolving the seats -- their pad is essentially invisible
	// until they join. Without this, that Start press would seat a third player, and the tether
	// only ever links GetShips()[0]/[1], so the partner they meant to be would fly free while a
	// bot stayed bolted to player one.
	// The same seat, so the slot keeps its score, lives and place in the tether; only the driver
	// changes. Pads only (Keyboard/Generic can never reach here -- slot 0 holds the keyboard, and
	// a Generic join needs a key that is not bound on this port), and only while the AI holds the
	// seat, so a second pad joining a genuine two-human game still goes through the normal path.
	protected override bool TryAdoptJoinDevice(ControlDevice device)
	{
		if (EvilAliensWeb.Compat.Net.NetSession.Active || PadIndexOf(device) < 0)
		{
			return false;
		}
		int slot = oracle.GetPlayerIndex(ControlDevice.AI);
		if (slot < 0 || !oracle.SetController(slot, device))
		{
			return false;
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.Owner == slot)
			{
				ship.AdoptController(device);
			}
		}
		Console.WriteLine("[teamchallenge] " + device + " took over the auto-pilot partner seat " + slot);
		return true;
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		base.UpdateNormal(gameTime);
		if (EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			UpdateNormalNet();
			return;
		}
		if (oracle.GetShips().Count >= 2)
		{
			return;
		}
		Collection.Remove((GameComponent)(object)connector);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.Asplode();
		}
		LoseLife();
	}

	private void UpdateNormalNet()
	{
		if (netConnectorPending)
		{
			if (oracle.GetShips().Count >= 2)
			{
				netConnectorPending = false;
				netLinkUp = true;
				connector = ShipConnector.NewAlien(Collection, base.Game);
				connector.Setup(oracle.GetShips()[0], oracle.GetShips()[1]);
				Collection.Add((GameComponent)(object)connector);
			}
			return; // partner not up yet -- the one-ship window is not a death
		}
		if (!netLinkUp || oracle.GetShips().Count >= 2)
		{
			return;
		}
		// Shared fate: one ship died (local death or the remote's alive=false edge) ->
		// both go. Asplode only the ships WE own (the partner's death display is its own
		// peer's alive=false edge -- Asplode'ing the puppet would fire OnDeath machinery
		// for a ship we don't control); the life decrement + reset broadcast are
		// host-authoritative (LoseLife no-ops on a client and the EvReset mirrors it).
		netLinkUp = false;
		Collection.Remove((GameComponent)(object)connector);
		// Removal is deferred a tick -- null now so a late EvTetherBreak in that window
		// can't re-break the torn-down connector.
		connector = null;
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.Controller != ControlDevice.Remote)
			{
				ship.Asplode();
			}
		}
		LoseLife();
	}

	protected override void PopulateEventList()
	{
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 20f, 0.1f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 20f, 3f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 5f, 0.1f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.2f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 4.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 30f, 0.2f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 30f, 5.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 5f, 1.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(MyMath.VectorToAngle(new Vector2(-800f, -600f)));
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		AsteroidSpawner asteroidSpawner = new AsteroidSpawner(base.Game, 42f, 4f, startWithBig: true);
		eventList.AddEvent(asteroidSpawner, halting: true);
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		WaitEvent waitEvent = new WaitEvent(base.Game, 2.5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		BrainSpawner brainSpawner = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		brainSpawner = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 40f, 0.15f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 40f, 2.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		brainSpawner = new BrainSpawner(base.Game, 30f, 0.075f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 10f, 1.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 3f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 10f, 0.33f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		bonusSpawner = new BonusSpawner(base.Game, 24f, 0.1f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 24f, 3f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner = new UfoSpawner(base.Game, 24f, 0.5f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 6f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.SetLastEventAsCheckPoint();
		ufoSpawner = new UfoSpawner(base.Game, 6f, 0.4f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(base.Game);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		bossSpawner.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(bossSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 35f, 4f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 35f, 0.125f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		ufoSpawner = new UfoSpawner(base.Game, 35f, 0.66f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 10f, 2.25f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		bonusSpawner = new BonusSpawner(base.Game, 10f, 0.5f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.5f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		junkBossSpawner.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.1f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		junkBossSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "Great!", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		SkullSpawner skullSpawner = new SkullSpawner(base.Game, 60f, 1.2f, maze: false, bonusonly: false);
		eventList.AddEvent(skullSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		BattleSkullEvent battleSkullEvent = new BattleSkullEvent(base.Game, 60f, 0.2f);
		eventList.AddEvent(battleSkullEvent, halting: true);
		eventList.AddHalt();
		battleSkullEvent.OnFinished += jbspawner_OnFinished;
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		Victory();
	}
}
