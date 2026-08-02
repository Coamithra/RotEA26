using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class GameEventList
{
	// `checkpoint` is the event that IS the checkpoint (card 4a3b22b7). A level whose script
	// mutates persistent scene state -- music, backdrop, floor -- needs to know WHICH checkpoint
	// was entered so it can re-assert that section's state: RevertToCheckpoint walks back to the
	// nearest checkpoint at or before the death, which can be several sections earlier, and it
	// restores neither the backdrop nor the track. Only InsaneBossI uses the argument; GameScene's
	// own subscriber ignores it.
	public delegate void CheckPointReached(GameEventList sender, GameEvent checkpoint);

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
				this.OnCheckPointReached(this, gameEvent);
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

	// AI bench (card f4d1721f): how far the script has walked, and how long it is. `pos` is the
	// index progressList() has consumed up to, so it is exactly "how far the level got" — the
	// number that says whether the AI finished a level or stalled on a halting boss it never
	// damaged. Read-only, only ever called behind ?aibench.
	internal int BenchPos => pos;

	internal int BenchCount => eventList.Count;

	// ---- Checkpoint/section debug seams (card 4a3b22b7) ---------------------------------------
	// The reported bug is a checkpoint revert that jumps ACROSS a section change: the walk-back
	// below lands on the nearest earlier checkpoint, which for the alien-base transition is the
	// spider-boss one two sections back. Reaching that window in play means dying inside a ~10s
	// slot after a multi-minute boss run, at whatever difficulty the RNG allows -- not something a
	// probe can wait for. These let the console oracle put the REAL event list at a chosen
	// position and call the REAL RevertToCheckpoint, so the walk-back and the level's re-assert
	// are exercised end to end in milliseconds. Debug-surface only (Compat/BossTrainTest.cs).

	// The index every checkpoint sits at, in script order -- what the oracle checks the level's
	// own section map against.
	internal List<int> DebugCheckpointIndices()
	{
		List<int> result = new List<int>();
		for (int i = 0; i < eventList.Count; i++)
		{
			if (checkpoints.Contains(eventList[i]))
			{
				result.Add(i);
			}
		}
		return result;
	}

	internal GameEvent EventAt(int index)
	{
		return (index >= 0 && index < eventList.Count) ? eventList[index] : null;
	}

	// Park the walker at `p` WITHOUT running any of the script up to it. Deliberately does not
	// touch activeEvents/halted: the caller's next move is RevertToCheckpoint, which clears both.
	internal void DebugSetPos(int p)
	{
		pos = MathHelper.Clamp(p, 1, eventList.Count);
	}

	public void MakeConditional(GameEvent a_event, Settings.DifficultyLevel minDifficulty, Settings.DifficultyLevel maxDifficulty)
	{
		DifficultyRange value = default(DifficultyRange);
		value.min = minDifficulty;
		value.max = maxDifficulty;
		difficultyRanges.Add(a_event, value);
	}
}
