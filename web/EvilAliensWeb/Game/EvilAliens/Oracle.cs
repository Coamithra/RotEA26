using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class Oracle : GameComponent, IOracleService
{
	public const int MaxPlayers = 4;

	private List<AlienDrawableGameComponent> baddies = new List<AlienDrawableGameComponent>();

	private List<PlayerInfo> players = new List<PlayerInfo>();

	private List<PlayerShip> playerShips = new List<PlayerShip>();

	private Background background;

	private Timer slowmotimer = new Timer(1f, repeating: false);

	private float slowmotion = 1f;

	private List<ParatrooperBrain> paratrooperBrains = new List<ParatrooperBrain>();

	private List<Powerup> powerups = new List<Powerup>();

	private List<StarMine> starmines = new List<StarMine>();

	public float Slowmotion => slowmotion;

	public Vector2 BackgroundSpeed
	{
		get
		{
			if (background != null)
			{
				return background.ScrollSpeed;
			}
			return Vector2.Zero;
		}
	}

	// The current alien-base floor colour (crossfaded across the Level-3 floor switches), or null off
	// that floor. Wall.DrawTowerShafts3D fogs its tower bases toward this so they recede into whatever
	// floor is scrolling under them. Mirrors BackgroundSpeed's null-safe wrap of `background`.
	public Color? AlienBaseFloorColor => background?.AlienBaseFloorColor();

	public int Players
	{
		get
		{
			int playingCount = 0;
			foreach (PlayerInfo player in players)
			{
				if (player.isPlaying)
				{
					playingCount++;
				}
			}
			return playingCount;
		}
	}

	public bool AllShipsDead => playerShips.Count == 0;

	public int LiveShips => playerShips.Count;

	Oracle IOracleService.Oracle => this;

	public void SetSlowmotion(float seconds)
	{
		slowmotion = 0.4f;
		if (slowmotimer.Active)
		{
			slowmotimer.Duration = MathHelper.Max(slowmotimer.TimeLeft, seconds * 1000f);
		}
		else
		{
			slowmotimer.Duration = seconds * 1000f;
		}
		slowmotimer.Reset();
		slowmotimer.Start();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		slowmotimer.Update(gameTime);
		if (slowmotimer.Finished)
		{
			slowmotion = 1f;
		}
		if (playerShips.Count == 0)
		{
			slowmotimer.Stop();
			slowmotion = 1f;
		}
	}

	public bool DeviceIsPlaying(ControlDevice device)
	{
		bool anyPlaying = false;
		foreach (PlayerInfo player in players)
		{
			anyPlaying |= player.isPlaying && player.controller == device;
		}
		return anyPlaying;
	}

	// Returns the slot the player was seated in -- callers that spawn the ship need the real
	// slot, not `Players - 1` (which only agrees while the table is densely filled; online
	// co-op's host-allocated roster is sparse).
	public int AddPlayer(ControlDevice starter)
	{
		if (starter != ControlDevice.AI && DeviceIsPlaying(starter))
		{
			throw new Exception("device is already playing");
		}
		int slot = FirstFreeSlot();
		if (slot < 0)
		{
			throw new Exception("maximum players exceeded");
		}
		PlayerInfo playerInfo = players[slot];
		playerInfo.isPlaying = true;
		playerInfo.controller = starter;
		players[slot] = playerInfo;
		return slot;
	}

	public void ResetPlayers()
	{
		foreach (PlayerInfo player in players)
		{
			player.Reset();
		}
	}

	// Online co-op (coverage-gaps follow-up): seat a network-driven puppet in a SPECIFIC slot,
	// so a client's AI-friend puppet lands in the SAME slot index the host runs it in (keeping
	// per-slot score/lives attribution consistent). Returns false if the slot is out of range or
	// already occupied -- the caller must never squat a live human/remote slot.
	public bool AddPlayerAt(int slot, ControlDevice device)
	{
		if (slot < 0 || slot >= 4 || players[slot].isPlaying)
		{
			return false;
		}
		PlayerInfo info = players[slot];
		info.isPlaying = true;
		info.controller = device;
		players[slot] = info;
		return true;
	}

	// Online co-op (card 4d904410): the host allocates every slot, so the table is SPARSE -- a
	// hole is normal (a granted slot the peer hasn't filled yet, a friend puppet that died and
	// freed a low slot). Everything that walks the roster must ask this rather than assume the
	// seated slots are 0..Players-1.
	public bool IsSeated(int slot)
	{
		return slot >= 0 && slot < 4 && players[slot].isPlaying;
	}

	// 1-based position of `slot` among the SEATED slots (0 if it isn't seated). The spawn/respawn
	// spread formulas want "the Nth player present" -- `slot + 1` only agrees while the table is
	// dense, and with a host-allocated sparse roster it pushes high slots off-screen.
	public int SeatOrdinal(int slot)
	{
		if (!IsSeated(slot))
		{
			return 0;
		}
		int n = 0;
		for (int i = 0; i <= slot; i++)
		{
			if (players[i].isPlaying)
			{
				n++;
			}
		}
		return n;
	}

	// First free slot at or above `from`, or -1 when the roster is full. The host's slot
	// allocator (NetSession) -- keeping it here means AddPlayer's own scan and the net
	// allocator agree on what "free" means.
	public int FirstFreeSlot(int from = 0)
	{
		for (int i = Math.Max(0, from); i < 4; i++)
		{
			if (!players[i].isPlaying)
			{
				return i;
			}
		}
		return -1;
	}

	// Online co-op: move a seated slot's registration to another slot, keeping its device and
	// taking the destination's hue. Only the JOIN peer's primary ever moves, and only in the
	// dev ?net= flow (it boots into a level at slot 0 and learns its host-granted slot when it
	// pairs); the caller re-stamps any live ship's Owner. No-op unless `from` is seated and
	// `to` is free.
	public bool MovePlayerSlot(int from, int to)
	{
		if (from == to || !IsSeated(from) || to < 0 || to >= 4 || players[to].isPlaying)
		{
			return false;
		}
		ControlDevice device = players[from].controller;
		players[from].Reset();
		PlayerInfo info = players[to];
		info.isPlaying = true;
		info.controller = device;
		players[to] = info;
		return true;
	}

	// Release a slot seated by AddPlayerAt (the friend left / died); a no-op on a slot that is
	// not playing or holds a different device, so it can never free a live human/remote slot.
	public void RemovePlayerAt(int slot, ControlDevice device)
	{
		if (slot < 0 || slot >= 4 || !players[slot].isPlaying || players[slot].controller != device)
		{
			return;
		}
		players[slot].Reset();
	}

	// Release a SINGLE seated slot mid-level (card 2001fbd8): when a join-in-progress peer
	// leaves, its Remote slot must be freed so the host reverts to single-player (Players == 1
	// again, so NetListing re-lists + the empty-slot beacon returns). ResetPlayers can't be
	// reused -- it would also wipe the host's own slot. No-op if the device isn't seated.
	// PlayerInfo is a reference type, so mutating the list element in place is enough.
	public void ReleasePlayer(ControlDevice device)
	{
		int i = GetPlayerIndex(device);
		if (i >= 0)
		{
			players[i].Reset();
		}
	}

	public float Hue(int i)
	{
		return players[i].hue;
	}

	public void SetHue(float hue, int i)
	{
		if (i >= 4)
		{
			throw new Exception("invalid player nr., " + i);
		}
		players[i].hue = hue;
	}

	public ControlDevice Controller(int i)
	{
		if (!players[i].isPlaying)
		{
			throw new Exception("Player " + i + " is not playing!");
		}
		return players[i].controller;
	}

	// Re-point a SEATED slot at a different device, keeping everything else about it (score,
	// lives, hue, position). Card e6927ef8: TeamChallenge's auto-pilot partner hands its seat to
	// the first real pad that joins, and the slot must survive the handover -- releasing and
	// re-seating would drop the tether and the slot's score with it. Refuses an unseated slot and
	// refuses to create a DUPLICATE of a device already playing, which is what AddPlayer's own
	// guard protects (ControlDevice.AI excepted there, and there can be several of those).
	// The caller owns the live PlayerShip's own `controller` copy -- see PlayerShip.AdoptController.
	public bool SetController(int slot, ControlDevice device)
	{
		if (slot < 0 || slot >= 4 || !players[slot].isPlaying)
		{
			return false;
		}
		if (device != ControlDevice.AI && GetPlayerIndex(device) >= 0)
		{
			return false;
		}
		players[slot].controller = device;
		return true;
	}

	public int GetPlayerIndex(ControlDevice device)
	{
		for (int i = 0; i < 4; i++)
		{
			if (players[i].isPlaying && players[i].controller == device)
			{
				return i;
			}
		}
		return -1;
	}

	public void SetPlayerPosition(int player, Vector2 position)
	{
		PlayerInfo playerInfo = players[player];
		playerInfo.position = position;
		players[player] = playerInfo;
	}

	public List<PlayerShip> GetShips()
	{
		return playerShips;
	}

	public Oracle(Game game)
		: base(game)
	{
		for (int i = 0; i < 4; i++)
		{
			PlayerInfo playerInfo = new PlayerInfo(i);
			switch (i)
			{
			case 0:
				playerInfo.hue = -1f;
				break;
			case 1:
				playerInfo.hue = 300f;
				break;
			case 2:
				playerInfo.hue = 0f;
				break;
			case 3:
				playerInfo.hue = 39f;
				break;
			default:
				playerInfo.hue = -1f;
				break;
			}
			players.Add(playerInfo);
		}
		game.Components.ComponentAdded += Components_ComponentAdded;
		game.Components.ComponentRemoved += Components_ComponentRemoved;
	}

	// Undo the constructor's subscription. The game's own oracle lives as long as the game and
	// never needs this -- it exists for a SCRATCH oracle built purely as a roster fixture
	// (Compat/Net/NetSlotTest), which would otherwise keep mirroring the live world's ships and
	// leak a handler pair per construction. Idempotent: -= on an unsubscribed handler is a no-op.
	internal void DetachFromComponents()
	{
		base.Game.Components.ComponentAdded -= Components_ComponentAdded;
		base.Game.Components.ComponentRemoved -= Components_ComponentRemoved;
	}

	private void Components_ComponentRemoved(object sender, GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent is PlayerShip)
		{
			playerShips.Remove((PlayerShip)(object)e.GameComponent);
		}
		if (e.GameComponent == background)
		{
			background = null;
		}
	}

	private void Components_ComponentAdded(object sender, GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent is PlayerShip)
		{
			playerShips.Add((PlayerShip)(object)e.GameComponent);
		}
		if (e.GameComponent is Background)
		{
			background = (Background)(object)e.GameComponent;
		}
	}

	public Vector2 GetRandomPlayerPosition()
	{
		if (playerShips.Count > 0)
		{
			int index = RandomHelper.Random.Next(playerShips.Count);
			return playerShips[index].GetPosition();
		}
		return new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f));
	}

	public Vector2 GetPlayerPosition(int index)
	{
		if (!players[index].isPlaying)
		{
			throw new Exception("Player " + index + " is not playing!");
		}
		return players[index].position;
	}

	public bool IsAlive(int player)
	{
		bool alive = false;
		foreach (PlayerShip playerShip in playerShips)
		{
			alive |= playerShip.Owner == player;
		}
		return alive;
	}

	public PlayerShip GetRandomPlayerShip()
	{
		if (playerShips.Count > 0)
		{
			int index = RandomHelper.Random.Next(playerShips.Count);
			return playerShips[index];
		}
		return null;
	}

	// The AI's whole world model: PlayerShip's DoAIMove/DoAIFire/doAIBomb see ONLY what this
	// returns, so a type missing here is a type the bot can neither shoot nor dodge. Card
	// f4d1721f added the second group below for exactly that reason -- BrainBoss and FakeBoss
	// gate the end of Level 3 and were invisible, so the AI stood next to the boss it was
	// supposed to kill; SpiderBoss and PlasmaBall were invisible as HAZARDS, which is why the
	// spider-boss fight read as "no idea what it's doing" (it wasn't dodging a boss it couldn't
	// see). Keep this a superset of both consumer contracts -- PlayerShip.IsAiThreat (mirrors
	// PlayerShip.CollidesWith) and PlayerShip.IsAiShootable (mirrors Bullet.CollidesWith) --
	// and let those two predicates, not this list, decide what to do with each entry.
	// (TutorialLevel.killboss also walks this, but only ever looks for PunchingBag.)
	public List<AlienDrawableGameComponent> GetBaddies()
	{
		baddies.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent component = item;
			if (component is EvilBullet || component is UFO || component is Asteroid || component is Braineroid || component is JunkBoss || component is Ball || component is Boss || component is Spider || component is StationaryBoss || component is MarsBoss || component is EvilSkull || component is Lazer || component is ClassicBoss || component is DeathStar || component is Wall || component is BattleSkull || component is FlyingSpider || (component is Explosion && ((Explosion)(object)component).Collides) || component is StarMine || component is SweepUFO || component is PunchingBag
				|| component is BrainBoss || component is FakeBoss || component is SpiderBoss || component is PlasmaBall || component is ParatrooperAlien || component is ParatrooperBrain || component is Parachute)
			{
				baddies.Add((AlienDrawableGameComponent)(object)component);
			}
		}
		return baddies;
	}

	public List<ParatrooperBrain> GetParatrooperBrains()
	{
		paratrooperBrains.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent component = item;
			if (component is ParatrooperBrain)
			{
				paratrooperBrains.Add((ParatrooperBrain)(object)component);
			}
		}
		return paratrooperBrains;
	}

	public List<Powerup> GetPowerups()
	{
		powerups.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent component = item;
			if (component is Powerup)
			{
				powerups.Add((Powerup)(object)component);
			}
		}
		return powerups;
	}

	public List<StarMine> GetStarMines()
	{
		starmines.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent component = item;
			if (component is StarMine)
			{
				starmines.Add((StarMine)(object)component);
			}
		}
		return starmines;
	}

	public int NrOfShipConnectors()
	{
		int connectors = 0;
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent component = item;
			if (component is ShipConnector)
			{
				connectors++;
			}
		}
		return connectors;
	}

	internal PlayerShip GetPlayerShip(int player)
	{
		foreach (PlayerShip playerShip in playerShips)
		{
			if (playerShip.Owner == player)
			{
				return playerShip;
			}
		}
		return null;
	}
}
