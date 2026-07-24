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
		oracle.ResetPlayers();
		// Online co-op (card 4d904410): the host allocates every slot, so seat our primary in
		// the slot we were granted (offline / host-side that is 0, exactly as before).
		if (!oracle.AddPlayerAt(EvilAliensWeb.Compat.Net.NetSession.LocalPrimarySlot, ControlDevice.Keyboard))
		{
			oracle.AddPlayer(ControlDevice.Keyboard);
		}
		// Online co-op: seat ONLY the local device -- the partner joins as
		// ControlDevice.Remote through the net layer. Seating the offline PadOne here
		// would (a) squat the slot the remote puppet needs and (b) trip the
		// disconnected-gamepad force-pause every tick (GameScene.Update's PadConnected
		// check) -- the card's "pause triggers are local devices only" gotcha.
		if (!EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			oracle.AddPlayer(ControlDevice.PadOne);
		}
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
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		BonusSpawner gameEvent = new BonusSpawner(base.Game, 20f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		UfoSpawner gameEvent2 = new UfoSpawner(base.Game, 20f, 3f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UfoSpawner(base.Game, 5f, 0.1f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent = new BonusSpawner(base.Game, 10f, 0.2f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		MessageEvent gameEvent3 = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 10f, 4.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(base.Game, 30f, 0.2f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 30f, 5.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 5f, 1.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent3 = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		gameEvent3.SetupAsWarning(MyMath.VectorToAngle(new Vector2(-800f, -600f)));
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		AsteroidSpawner gameEvent4 = new AsteroidSpawner(base.Game, 42f, 4f, startWithBig: true);
		eventList.AddEvent(gameEvent4, halting: true);
		gameEvent = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 10f, 5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		WaitEvent gameEvent5 = new WaitEvent(base.Game, 2.5f);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent3, halting: false);
		BrainSpawner gameEvent6 = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(gameEvent6, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent6 = new BrainSpawner(base.Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(gameEvent6, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(base.Game, 40f, 0.15f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 40f, 2.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		gameEvent6 = new BrainSpawner(base.Game, 30f, 0.075f, wrapping: true);
		eventList.AddEvent(gameEvent6, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UfoSpawner(base.Game, 10f, 1.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 10f, 3f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(base.Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 10f, 0.33f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent = new BonusSpawner(base.Game, 24f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 24f, 3f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		gameEvent2 = new UfoSpawner(base.Game, 24f, 0.5f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 6f, 2f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent2 = new UfoSpawner(base.Game, 6f, 0.4f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		gameEvent3.SetupAsWarning(4.712389f);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(base.Game);
		gameEvent = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		bossSpawner.LinkWith(gameEvent);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		eventList.AddEvent(bossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 35f, 4f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(base.Game, 35f, 0.125f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new UfoSpawner(base.Game, 35f, 0.66f, big: true);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent2 = new UfoSpawner(base.Game, 10f, 2.25f, big: false);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game);
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent5 = new WaitEvent(base.Game, 5f);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent = new BonusSpawner(base.Game, 10f, 0.5f, randomly: false);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent3 = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		gameEvent3.SetupAsWarning(4.712389f);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.5f, big: false);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		gameEvent2 = new UfoSpawner(base.Game, 0f, 0.1f, big: true);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent3 = new MessageEvent(base.Game, "Great!", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent3, halting: false);
		SkullSpawner gameEvent7 = new SkullSpawner(base.Game, 60f, 1.2f, maze: false, bonusonly: false);
		eventList.AddEvent(gameEvent7, halting: false);
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
