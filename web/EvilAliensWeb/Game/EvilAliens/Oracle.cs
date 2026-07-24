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
			int num = 0;
			foreach (PlayerInfo player in players)
			{
				if (player.isPlaying)
				{
					num++;
				}
			}
			return num;
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
		bool flag = false;
		foreach (PlayerInfo player in players)
		{
			flag |= player.isPlaying && player.controller == device;
		}
		return flag;
	}

	public void AddPlayer(ControlDevice starter)
	{
		if (starter != ControlDevice.AI && DeviceIsPlaying(starter))
		{
			throw new Exception("device is already playing");
		}
		for (int i = 0; i < 4; i++)
		{
			PlayerInfo playerInfo = players[i];
			if (!playerInfo.isPlaying)
			{
				playerInfo.isPlaying = true;
				playerInfo.controller = starter;
				players[i] = playerInfo;
				return;
			}
		}
		throw new Exception("maximum players exceeded");
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
		bool flag = false;
		foreach (PlayerShip playerShip in playerShips)
		{
			flag |= playerShip.Owner == player;
		}
		return flag;
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

	public List<AlienDrawableGameComponent> GetBaddies()
	{
		baddies.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent val = item;
			if (val is EvilBullet || val is UFO || val is Asteroid || val is Braineroid || val is JunkBoss || val is Ball || val is Boss || val is Spider || val is StationaryBoss || val is MarsBoss || val is EvilSkull || val is Lazer || val is ClassicBoss || val is DeathStar || val is Wall || val is BattleSkull || val is FlyingSpider || (val is Explosion && ((Explosion)(object)val).Collides) || val is StarMine || val is SweepUFO || val is PunchingBag)
			{
				baddies.Add((AlienDrawableGameComponent)(object)val);
			}
		}
		return baddies;
	}

	public List<ParatrooperBrain> GetParatrooperBrains()
	{
		paratrooperBrains.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent val = item;
			if (val is ParatrooperBrain)
			{
				paratrooperBrains.Add((ParatrooperBrain)(object)val);
			}
		}
		return paratrooperBrains;
	}

	public List<Powerup> GetPowerups()
	{
		powerups.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent val = item;
			if (val is Powerup)
			{
				powerups.Add((Powerup)(object)val);
			}
		}
		return powerups;
	}

	public List<StarMine> GetStarMines()
	{
		starmines.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent val = item;
			if (val is StarMine)
			{
				starmines.Add((StarMine)(object)val);
			}
		}
		return starmines;
	}

	public int NrOfShipConnectors()
	{
		int num = 0;
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			GameComponent val = item;
			if (val is ShipConnector)
			{
				num++;
			}
		}
		return num;
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
