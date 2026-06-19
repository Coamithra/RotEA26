using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class GameEventList
{
	public delegate void CheckPointReached(GameEventList sender);

	private Game game;

	private List<GameEvent> eventList;

	private List<GameEvent> halts;

	private List<GameEvent> checkpoints;

	private List<GameEvent> haltingEvents;

	private Dictionary<GameEvent, DifficultyRange> difficultyRanges;

	private int pos;

	private List<GameEvent> activeEvents;

	private int currentlyWaitingFor;

	private bool halted;

	private List<GameEvent> activeEventsCopy = new List<GameEvent>();

	public event CheckPointReached OnCheckPointReached;

	public GameEventList(Game game)
	{
		eventList = new List<GameEvent>();
		halts = new List<GameEvent>();
		haltingEvents = new List<GameEvent>();
		activeEvents = new List<GameEvent>();
		checkpoints = new List<GameEvent>();
		this.game = game;
		difficultyRanges = new Dictionary<GameEvent, DifficultyRange>();
	}

	public void Update(GameTime gameTime)
	{
		activeEventsCopy.Clear();
		foreach (GameEvent activeEvent in activeEvents)
		{
			activeEventsCopy.Add(activeEvent);
		}
		foreach (GameEvent item in activeEventsCopy)
		{
			item.Update(gameTime);
		}
	}

	public void AddEvent(GameEvent gameEvent)
	{
		AddEvent(gameEvent, halting: true);
	}

	public void AddEvent(GameEvent gameEvent, bool halting)
	{
		if (halting)
		{
			gameEvent.OnFinished += HaltingEventFinished;
			haltingEvents.Add(gameEvent);
		}
		else
		{
			gameEvent.OnFinished += RegularEventFinished;
		}
		eventList.Add(gameEvent);
		progressList();
	}

	public void AddHalt()
	{
		halts.Add(eventList[eventList.Count - 1]);
		if (pos == eventList.Count)
		{
			if (currentlyWaitingFor == 0)
			{
				throw new Exception("event queue halted but no halting events were processed");
			}
			if (halted)
			{
				throw new Exception("already halted");
			}
			halted = true;
		}
	}

	public void SetLastEventAsCheckPoint()
	{
		if (eventList.Count == 0)
		{
			throw new Exception("no event to set as checkpoint");
		}
		checkpoints.Add(eventList[eventList.Count - 1]);
	}

	public void RevertToCheckpoint()
	{
		currentlyWaitingFor = 0;
		activeEvents.Clear();
		halted = false;
		if (pos == 0)
		{
			throw new Exception("no events have been added - cannot revert");
		}
		pos--;
		while ((0 < pos) & !checkpoints.Contains(eventList[pos]))
		{
			pos--;
		}
		progressList();
	}

	private void RegularEventFinished(GameEvent sender)
	{
		activeEvents.Remove(sender);
	}

	private void HaltingEventFinished(GameEvent sender)
	{
		currentlyWaitingFor--;
		if (currentlyWaitingFor == 0)
		{
			halted = false;
			progressList();
		}
		RegularEventFinished(sender);
	}

	public void Reset()
	{
		pos = 0;
		halted = false;
		activeEvents.Clear();
		currentlyWaitingFor = 0;
		progressList();
	}

	private void progressList()
	{
		while (!halted & (pos < eventList.Count))
		{
			GameEvent gameEvent = eventList[pos];
			bool flag = true;
			if (difficultyRanges.ContainsKey(gameEvent) && (Settings.GetInstance().CurrentDifficulty < difficultyRanges[gameEvent].min || Settings.GetInstance().CurrentDifficulty > difficultyRanges[gameEvent].max))
			{
				flag = false;
			}
			if (!flag)
			{
				pos++;
				continue;
			}
			activeEvents.Add(gameEvent);
			gameEvent.Reset();
			if (checkpoints.Contains(gameEvent) && this.OnCheckPointReached != null)
			{
				this.OnCheckPointReached(this);
			}
			if (haltingEvents.Contains(gameEvent))
			{
				currentlyWaitingFor++;
			}
			if (halts.Contains(gameEvent))
			{
				if (currentlyWaitingFor == 0)
				{
					throw new Exception("event queue halted but no halting events were processed");
				}
				halted = true;
			}
			pos++;
		}
	}

	public void MakeConditional(GameEvent a_event, Settings.DifficultyLevel minDifficulty, Settings.DifficultyLevel maxDifficulty)
	{
		DifficultyRange value = default(DifficultyRange);
		value.min = minDifficulty;
		value.max = maxDifficulty;
		difficultyRanges.Add(a_event, value);
	}
}
