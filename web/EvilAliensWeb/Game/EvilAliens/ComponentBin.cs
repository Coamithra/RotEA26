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

	private List<GameComponent> birthList = new List<GameComponent>();

	private List<GameComponent> deathList = new List<GameComponent>();

	private List<GameComponent> idleList = new List<GameComponent>();

	private Queue<List<GameComponent>> inactive = new Queue<List<GameComponent>>();

	private Game game;

	// Perf batch 2: a persistently-maintained list of every IComponentWatcher present in the
	// world, so an add/remove no longer rescans all of (collection + birthList + idleList +
	// inactive) and type-checks each to find the watchers — the notify is now O(watchers).
	// This list mirrors the multiset (collection + idleList + Σinactive); the small, transient
	// birthList (this frame's pending adds) is iterated directly at notify time, matching the
	// original scan order (collection/birth/idle/inactive) closely enough — every watcher's
	// reaction keys off e.GameComponent alone, so notify order is immaterial. Collection
	// membership is tracked via the ComponentAdded/Removed events (which also fire for the few
	// components added straight to Game.Components, bypassing Add()); the other sub-lists are
	// tracked at their own mutation sites (Recycle/Push/Pop/Purge/ClearCache/FullReset).
	private List<IComponentWatcher> watchers = new List<IComponentWatcher>();

	private List<GameComponent> birthListCopy = new List<GameComponent>();

	private List<GameComponent> deathListCopy = new List<GameComponent>();

	ComponentBin IComponentBinService.ComponentBin => this;

	public void FullReset()
	{
		birthList.Clear();
		deathList.Clear();
		idleList.Clear();
		inactive.Clear();
		RebuildWatchers();
	}

	public void ClearCache()
	{
		idleList.Clear();
		RebuildWatchers();
	}

	// Rebuild the persistent watcher list from scratch (the multiset the notify path iterates:
	// collection + idleList + Σinactive; birthList is iterated separately at notify time). Cheap
	// because it only runs at the rare reset/cache-clear boundaries — it also re-syncs `watchers`
	// so any incremental drift can't survive past a level load.
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

	public void Purge<T>() where T : GameComponent
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
		for (int i = 0; i < birthList.Count; i++)
		{
			GameComponent val2 = birthList[i];
			if (val2 is T)
			{
				birthList.RemoveAt(i);
				idleList.Add(val2);
				WatcherAdd(val2);
				i--;
			}
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
		foreach (GameComponent birth in birthList)
		{
			birth.Enabled = false;
		}
	}

	public void Pop()
	{
		foreach (GameComponent item in inactive.Dequeue())
		{
			item.Enabled = true;
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

	// Notify the persistent watcher set, then this frame's still-pending birthList adds (which
	// the original scan also covered). Snapshot the counts so a reaction that queues an add/
	// remove (deferred to deathList/birthList) can't disturb the in-flight iteration.
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
		int m = birthList.Count;
		for (int i = 0; i < m; i++)
		{
			if (birthList[i] is IComponentWatcher w)
			{
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

	public void Update()
	{
		birthListCopy.Clear();
		foreach (GameComponent birth in birthList)
		{
			birthListCopy.Add(birth);
		}
		birthList.Clear();
		foreach (GameComponent item in birthListCopy)
		{
			((Collection<IGameComponent>)(object)collection).Add((IGameComponent)(object)item);
		}
		deathListCopy.Clear();
		foreach (GameComponent death in deathList)
		{
			deathListCopy.Add(death);
		}
		deathList.Clear();
		foreach (GameComponent item2 in deathListCopy)
		{
			((Collection<IGameComponent>)(object)collection).Remove((IGameComponent)(object)item2);
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
		foreach (GameComponent birth in birthList)
		{
			flag = flag || birth is T;
		}
		return flag;
	}

	public void Add(GameComponent component)
	{
		deathList.Remove(component);
		if (!birthList.Contains(component))
		{
			if (((Collection<IGameComponent>)(object)collection).Contains((IGameComponent)(object)component))
			{
				component.Initialize();
			}
			else
			{
				birthList.Add(component);
			}
		}
		component.Enabled = true;
	}

	public void Remove(GameComponent component)
	{
		if (!deathList.Contains(component))
		{
			deathList.Add(component);
		}
	}
}
