using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class GameEvent
{
	public delegate void GameEventMessage(GameEvent sender);

	protected Game game;

	private Timer lifetime;

	protected Oracle Oracle;

	private List<GameEvent> linkedwith;

	private bool infinite;

	private bool active;

	protected ComponentBin collectionHelper;

	public event GameEventMessage OnFinished;

	public GameEvent(Game game, float lifetime)
	{
		this.game = game;
		this.lifetime = new Timer(lifetime * 1000f, repeating: false);
		if (lifetime == 0f)
		{
			infinite = true;
		}
		else
		{
			infinite = false;
		}
		collectionHelper = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		Oracle = ServiceHelper.Get<IOracleService>().Oracle;
		linkedwith = new List<GameEvent>();
		Reset();
	}

	public void LinkWith(GameEvent gameEvent)
	{
		linkedwith.Add(gameEvent);
	}

	public virtual void Reset()
	{
		lifetime.Reset();
		lifetime.Start();
		active = true;
	}

	protected void Terminate()
	{
		if (!active)
		{
			return;
		}
		if (this.OnFinished != null)
		{
			this.OnFinished(this);
		}
		active = false;
		foreach (GameEvent item in linkedwith)
		{
			item.Terminate();
		}
	}

	public virtual void Update(GameTime gameTime)
	{
		lifetime.Update(gameTime);
		if (lifetime.Finished & active & !infinite)
		{
			Terminate();
		}
	}
}
