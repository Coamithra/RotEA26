using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BloomPostprocess;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens;

public class ComponentBin : IComponentBinService
{
	private GameComponentCollection collection;

	// Spawn/death hardening (card 02d9ad67): births are INSTANT — Add() puts the component
	// straight into Game.Components (KNI journals the updateable/drawable lists, so mid-update
	// mutation is safe and a new component never Updates before the next tick; it IS visible to
	// collisions/Oracle scans/purges immediately, which is the point). Deaths stay QUEUED in
	// deathList (instant removal would corrupt the collision pass and change within-tick
	// gameplay) but flush TWICE per tick: at the existing mid-tick point AND at top-of-tick
	// (TopOfTickFlush), so a collision-phase kill can never run one more full "zombie" Update.
	// NOTE: KNI runs component.Initialize() synchronously inside collection.Add, so every call
	// site must fully configure (Setup) BEFORE Add — enforced by tools/audit_add_order.py.
	private List<GameComponent> deathList = new List<GameComponent>();

	private List<GameComponent> idleList = new List<GameComponent>();

	private Queue<List<GameComponent>> inactive = new Queue<List<GameComponent>>();

	// Standing purge filter: Purge<T> records T here, and any Add of a matching type until the
	// next top-of-tick flush is diverted to the recycle pool — closing the "clear-all ran early
	// in the tick, a component updating later re-spawned" race (incl. collision-phase kill side
	// effects the same tick). Cleared in TopOfTickFlush, BEFORE the next tick's component
	// updates, so legitimate next-tick spawns are never eaten.
	private List<Type> pendingPurges = new List<Type>();

	private Game game;

	// Perf batch 2: a persistently-maintained list of every IComponentWatcher present in the
	// world, so an add/remove no longer rescans all of (collection + idleList + inactive) and
	// type-checks each to find the watchers — the notify is now O(watchers). This list mirrors
	// the multiset (collection + idleList + Σinactive); every watcher's reaction keys off
	// e.GameComponent alone, so notify order is immaterial. Collection membership is tracked
	// via the ComponentAdded/Removed events (which also fire for the few components added
	// straight to Game.Components, bypassing Add()); the other sub-lists are tracked at their
	// own mutation sites (Recycle/Push/Pop/ClearCache/FullReset).
	private List<IComponentWatcher> watchers = new List<IComponentWatcher>();

	private List<GameComponent> deathListCopy = new List<GameComponent>();

	ComponentBin IComponentBinService.ComponentBin => this;

	// The owning Game — for the eaBinTest console suite (Compat/BinTest.cs).
	internal Game Game => game;

	public void FullReset()
	{
		deathList.Clear();
		idleList.Clear();
		inactive.Clear();
		pendingPurges.Clear();
		RebuildWatchers();
	}

	public void ClearCache()
	{
		idleList.Clear();
		RebuildWatchers();
	}

	// Rebuild the persistent watcher list from scratch (the multiset the notify path iterates:
	// collection + idleList + Σinactive). Cheap because it only runs at the rare
	// reset/cache-clear boundaries — it also re-syncs `watchers` so any incremental drift
	// can't survive past a level load.
	private void RebuildWatchers()
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		watchers.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)collection)
		{
			WatcherAdd(item);
		}
		foreach (GameComponent idle in idleList)
		{
			WatcherAdd(idle);
		}
		foreach (List<GameComponent> list in inactive)
		{
			foreach (GameComponent item in list)
			{
				WatcherAdd(item);
			}
		}
	}

	public int idleSize()
	{
		return idleList.Count;
	}

	public ComponentBin(Game game)
	{
		this.game = game;
		collection = game.Components;
		this.game.Components.ComponentAdded += Components_ComponentAdded;
		this.game.Components.ComponentRemoved += Components_ComponentRemoved;
	}

	public bool DEBUGdeathlistcontains(GameComponent g)
	{
		return deathList.Contains(g);
	}

	private bool dontTouchThisComponent(GameComponent g)
	{
		if (!(g is BloomComponent) && !(g is StorageDeviceManager) && !(g is GamerServicesComponent) && !(g is Debugger) && !(g is SpriteBatchWrapper) && !(g is Oracle))
		{
			return g is AwardmentBlade;
		}
		return true;
	}

	// standing=true (the default) arms the standing filter: any Add of a T until the next
	// top-of-tick flush (i.e. from components updating later this tick, or kill side effects
	// in this tick's collision phase) is diverted to the recycle pool instead of resurrecting
	// the cleared world. Pass standing=false ONLY for a clear-the-field-and-respawn-NOW purge
	// whose own call chain re-adds a T in the same tick (GameScene.UpdateStartup: the purge is
	// immediately followed by SpawnAllPlayers + the Get Ready banners).
	public void Purge<T>(bool standing = true) where T : GameComponent
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		foreach (GameComponent item in (Collection<IGameComponent>)(object)collection)
		{
			GameComponent val = item;
			if (val is T)
			{
				Remove(val);
			}
		}
		if (standing && !pendingPurges.Contains(typeof(T)))
		{
			pendingPurges.Add(typeof(T));
		}
	}

	public void Push()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		List<GameComponent> list = new List<GameComponent>();
		inactive.Enqueue(list);
		foreach (GameComponent item in (Collection<IGameComponent>)(object)collection)
		{
			GameComponent val = item;
			if (!dontTouchThisComponent(val))
			{
				val.Enabled = false;
				list.Add(val);
				WatcherAdd(val);
			}
		}
	}

	public void Pop()
	{
		foreach (GameComponent item in inactive.Dequeue())
		{
			// Online co-op (card 11.2): never thaw a frozen client puppet back into a live
			// AI on unpause -- its Update must stay off for its whole life.
			item.Enabled = !EvilAliensWeb.Compat.Net.NetSession.IsFrozenPuppet(item);
			WatcherRemove(item);
		}
	}

	// Add/remove a component from the persistent watcher multiset (no-op if it isn't a watcher).
	private void WatcherAdd(GameComponent g)
	{
		if (g is IComponentWatcher w)
		{
			watchers.Add(w);
		}
	}

	private void WatcherRemove(GameComponent g)
	{
		if (g is IComponentWatcher w)
		{
			watchers.Remove(w);
		}
	}

	private void Components_ComponentAdded(object src, GameComponentCollectionEventArgs args)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		if (args.GameComponent is GameComponent item)
		{
			// The component just entered `collection`; if it was sitting in idleList (a re-add
			// that didn't go through Recycle) it now leaves idle. Both are mirrored in `watchers`.
			if (idleList.Contains(item))
			{
				idleList.Remove(item);
				WatcherRemove(item);
			}
			WatcherAdd(item);
		}
		NotifyWatchers(args, added: true);
	}

	private void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs args)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		if (args.GameComponent is GameComponent item)
		{
			// The component left `collection` and (per the original) joins idleList — a net-zero
			// move for `watchers`. It also drops out of any inactive list it was pushed into.
			WatcherRemove(item);
			idleList.Add(item);
			WatcherAdd(item);
			foreach (List<GameComponent> item2 in inactive)
			{
				if (item2.Contains(item))
				{
					item2.Remove(item);
					WatcherRemove(item);
				}
			}
		}
		NotifyWatchers(args, added: false);
	}

	// Notify the persistent watcher set. Snapshot the count so a reaction that adds/removes
	// (instant add appends to `watchers` inline; removals are deferred to deathList) can't
	// disturb the in-flight iteration.
	private void NotifyWatchers(GameComponentCollectionEventArgs args, bool added)
	{
		int n = watchers.Count;
		for (int i = 0; i < n; i++)
		{
			IComponentWatcher w = watchers[i];
			if (added)
			{
				w.OnComponentAdded(args);
			}
			else
			{
				w.OnComponentRemoved(args);
			}
		}
	}

	private void test(List<GameComponent> list, string name)
	{
		for (int i = 0; i < list.Count - 1; i++)
		{
			for (int j = i + 1; j < list.Count; j++)
			{
				if (list[i] == list[j])
				{
					throw new Exception("duplicate item found in " + name);
				}
			}
		}
	}

	// Mid-tick flush (Game1.UpdateInner, after component updates / before collisions): deaths
	// queued during the update phase leave here, exactly like the original — a dying component
	// never collides in the tick it self-removed. (Births are instant now; nothing to flush.)
	public void Update()
	{
		FlushDeaths();
	}

	// Top-of-tick flush (Game1.UpdateInner, BEFORE component updates): deaths queued during the
	// previous tick's collision phase leave here, so a killed component never gets one more
	// full "zombie" Update (moving, firing, spawning — the final-bullet-across-the-paused-
	// screen class of bug). Also the expiry point of the standing purge filter: clearing it
	// here, before any component updates, means the filter covers the purge tick's stragglers
	// + collision side effects + removal cascades, but never eats a legitimate next-tick spawn.
	public void TopOfTickFlush()
	{
		FlushDeaths();
		pendingPurges.Clear();
	}

	private void FlushDeaths()
	{
		if (EvilAliensWeb.Compat.DebugFlags.BinLog)
		{
			test(deathList, "deathList");
			test(idleList, "idleList");
		}
		deathListCopy.Clear();
		foreach (GameComponent death in deathList)
		{
			deathListCopy.Add(death);
		}
		deathList.Clear();
		foreach (GameComponent item in deathListCopy)
		{
			((Collection<IGameComponent>)(object)collection).Remove((IGameComponent)(object)item);
		}
	}

	public T Recycle<T>() where T : GameComponent
	{
		T val = default(T);
		foreach (GameComponent idle in idleList)
		{
			if (idle is T)
			{
				val = (T)(object)idle;
			}
		}
		if (val != null)
		{
			idleList.Remove((GameComponent)(object)val);
			WatcherRemove((GameComponent)(object)val);
		}
		return val;
	}

	public bool ContainsType<T>() where T : GameComponent
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		bool flag = false;
		foreach (GameComponent item in (Collection<IGameComponent>)(object)collection)
		{
			GameComponent val = item;
			flag = flag || val is T;
		}
		return flag;
	}

	public void Add(GameComponent component)
	{
		// Online co-op (card 11.2): a JOIN peer's world is host-authoritative -- game code
		// must never grow it. Any replicable-type add that is NOT the puppet layer's own
		// (spawner strays after a pause tick, KilledBy side effects like asteroid splits or
		// bonus powerup drops) is swallowed into the recycle pool; the host's authoritative
		// copy replicates in as a puppet instead. Single branch outside a net session.
		if (EvilAliensWeb.Compat.Net.NetSession.SuppressWorldSpawn(component)
			&& !((Collection<IGameComponent>)(object)collection).Contains((IGameComponent)(object)component))
		{
			DivertToIdle(component);
			return;
		}
		// Standing purge filter: this type was cleared this tick — a late same-tick spawn
		// (or a resurrect-by-Add of an instance the purge already queued dead) must not
		// outlive the wipe. See pendingPurges / TopOfTickFlush.
		if (IsPendingPurged(component))
		{
			if (!((Collection<IGameComponent>)(object)collection).Contains((IGameComponent)(object)component))
			{
				DivertToIdle(component);
			}
			if (EvilAliensWeb.Compat.DebugFlags.BinLog)
			{
				Console.WriteLine("[bin] purge-filter diverted " + ((object)component).GetType().Name);
			}
			return;
		}
		deathList.Remove(component);
		if (((Collection<IGameComponent>)(object)collection).Contains((IGameComponent)(object)component))
		{
			component.Initialize();
			// Same pause rule as the fresh-add path below: a live world object re-added
			// while the world is pushed stays frozen (it already sits in a pause layer,
			// so Pop is what re-enables it) — re-enabling here would break the freeze.
			if (!(inactive.Count > 0 && component is AlienDrawableGameComponent))
			{
				component.Enabled = true;
			}
			return;
		}
		// Instant add: enters Game.Components NOW (KNI runs Initialize inside this call and
		// journals the update/draw registration, so it first Updates next tick but is already
		// visible to collisions, Oracle scans and purges — no hidden pending world).
		((Collection<IGameComponent>)(object)collection).Add((IGameComponent)(object)component);
		// Pause-aware: a world object added while the world is pushed joins the freeze (and
		// the newest pause layer, so Pop thaws it). Non-world components (pause menus,
		// darkener, overlays) must keep running — they ARE the pause UI.
		if (inactive.Count > 0 && component is AlienDrawableGameComponent)
		{
			component.Enabled = false;
			List<GameComponent> newest = null;
			foreach (List<GameComponent> layer in inactive)
			{
				newest = layer;
			}
			newest.Add(component);
			WatcherAdd(component);
			if (EvilAliensWeb.Compat.DebugFlags.BinLog)
			{
				Console.WriteLine("[bin] pause-froze " + ((object)component).GetType().Name);
			}
		}
		else
		{
			component.Enabled = true;
		}
	}

	private void DivertToIdle(GameComponent component)
	{
		deathList.Remove(component);
		if (!idleList.Contains(component))
		{
			idleList.Add(component);
			WatcherAdd(component);
		}
	}

	// Drop a component from the recycle pool (watcher bookkeeping included). Only the
	// eaBinTest suite uses this — its scratch components must not accumulate in the pool.
	internal void PruneIdle(GameComponent component)
	{
		if (idleList.Remove(component))
		{
			WatcherRemove(component);
		}
	}

	private bool IsPendingPurged(GameComponent component)
	{
		for (int i = 0; i < pendingPurges.Count; i++)
		{
			if (pendingPurges[i].IsInstanceOfType(component))
			{
				return true;
			}
		}
		return false;
	}

	public void Remove(GameComponent component)
	{
		if (!deathList.Contains(component))
		{
			deathList.Add(component);
		}
	}
}
