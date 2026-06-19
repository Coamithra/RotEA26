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

	private List<IComponentWatcher> tmp = new List<IComponentWatcher>();

	private List<GameComponent> birthListCopy = new List<GameComponent>();

	private List<GameComponent> deathListCopy = new List<GameComponent>();

	ComponentBin IComponentBinService.ComponentBin => this;

	public void FullReset()
	{
		birthList.Clear();
		deathList.Clear();
		idleList.Clear();
		inactive.Clear();
	}

	public void ClearCache()
	{
		idleList.Clear();
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
		}
	}

	private void Components_ComponentAdded(object src, GameComponentCollectionEventArgs args)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Expected O, but got Unknown
		if (args.GameComponent is GameComponent)
		{
			GameComponent item = (GameComponent)args.GameComponent;
			if (idleList.Contains(item))
			{
				idleList.Remove(item);
			}
		}
		MyDebug.Assert(tmp.Count == 0);
		for (int i = 0; i < ((Collection<IGameComponent>)(object)collection).Count; i++)
		{
			GameComponent val = (GameComponent)((Collection<IGameComponent>)(object)collection)[i];
			if (val is IComponentWatcher)
			{
				tmp.Add((IComponentWatcher)val);
			}
		}
		foreach (GameComponent birth in birthList)
		{
			if (birth is IComponentWatcher)
			{
				tmp.Add((IComponentWatcher)birth);
			}
		}
		foreach (GameComponent idle in idleList)
		{
			if (idle is IComponentWatcher)
			{
				tmp.Add((IComponentWatcher)idle);
			}
		}
		foreach (List<GameComponent> item2 in inactive)
		{
			foreach (GameComponent item3 in item2)
			{
				if (item3 is IComponentWatcher)
				{
					tmp.Add((IComponentWatcher)item3);
				}
			}
		}
		foreach (IComponentWatcher item4 in tmp)
		{
			item4.OnComponentAdded(args);
		}
		tmp.Clear();
	}

	private void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs args)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected O, but got Unknown
		if (args.GameComponent is GameComponent)
		{
			GameComponent item = (GameComponent)args.GameComponent;
			idleList.Add(item);
			foreach (List<GameComponent> item2 in inactive)
			{
				if (item2.Contains(item))
				{
					item2.Remove(item);
				}
			}
		}
		MyDebug.Assert(tmp.Count == 0);
		for (int i = 0; i < ((Collection<IGameComponent>)(object)collection).Count; i++)
		{
			GameComponent val = (GameComponent)((Collection<IGameComponent>)(object)collection)[i];
			if (val is IComponentWatcher)
			{
				tmp.Add((IComponentWatcher)val);
			}
		}
		foreach (GameComponent birth in birthList)
		{
			if (birth is IComponentWatcher)
			{
				tmp.Add((IComponentWatcher)birth);
			}
		}
		foreach (GameComponent idle in idleList)
		{
			if (idle is IComponentWatcher)
			{
				tmp.Add((IComponentWatcher)idle);
			}
		}
		foreach (List<GameComponent> item3 in inactive)
		{
			foreach (GameComponent item4 in item3)
			{
				if (item4 is IComponentWatcher)
				{
					tmp.Add((IComponentWatcher)item4);
				}
			}
		}
		foreach (IComponentWatcher item5 in tmp)
		{
			item5.OnComponentRemoved(args);
		}
		tmp.Clear();
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
