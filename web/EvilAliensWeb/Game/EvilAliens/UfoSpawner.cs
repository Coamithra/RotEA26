using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class UfoSpawner : GenericSpawner
{
	private bool big;

	private bool fastEntry;

	private bool mars;

	private List<float> entryDirections = new List<float>();

	public UfoSpawner(Game game, float lifetime, float firesPerSecond, bool big)
		: base(game, lifetime, firesPerSecond)
	{
		this.big = big;
		SetScaleWithMultiplayer(value: true);
		entryDirections.Add(-(float)Math.PI / 2f);
	}

	public void DoNotScale()
	{
		SetScaleSpawns(value: false);
	}

	protected override void DoEvent(GameTime gameTime)
	{
		float num = 24f;
		if (big)
		{
			num = 85f;
		}
		float angle = entryDirections[RandomHelper.Random.Next(entryDirections.Count)];
		float num2 = 600f;
		if (mars)
		{
			num2 = 500f - num;
		}
		Vector2 val = new Vector2(RandomHelper.RandomNextFloat(0f, 800f), RandomHelper.RandomNextFloat(0f, 600f)) + MyMath.AngleToVector(angle) * 1000f;
		val = Vector2.Clamp(val, new Vector2(0f - num), new Vector2(800f + num, num2 + num));
		if (mars)
		{
			// Mars levels have GROUND along the screen bottom (Floor.bottom = 560; the terrain
			// reads from ~y 540). `num2 = 500f - num` above was meant to keep a spawning
			// saucer's underside above that, but the shared clamp re-adds num (max Y =
			// num2 + num = 500 for every size), so a big saucer (num 85) could enter with its
			// underside at ~585 -- half-buried in the ground -- and clamping the 0..600 roll
			// piled ~1 in 6 entries at exactly that lowest point. Mars entries are side-only
			// (SetupMars/SetupMarsWest), so Y is the free axis: re-roll it uniformly over the
			// sky band, capping the underside at y <= 500. Spawn count/pacing unchanged.
			val.Y = RandomHelper.RandomNextFloat(0f, num2);
		}
		UFO uFO;
		if (big)
		{
			uFO = UFO.NewUFO(collectionHelper, game);
			uFO.Setup(val, isBig: true, EnemyBehaviour.normal);
		}
		else
		{
			uFO = UFO.NewUFO(collectionHelper, game);
			uFO.Setup(val, isBig: false, EnemyBehaviour.normal);
		}
		if (fastEntry)
		{
			uFO.SpeedUp();
			uFO.FlyInTime(7000f);
		}
		collectionHelper.Add((GameComponent)(object)uFO);
	}

	public void SetupThreeDirectional()
	{
		entryDirections.Add((float)Math.PI);
		entryDirections.Add(0f);
	}

	public void SetupMars()
	{
		mars = true;
		entryDirections.Remove(-(float)Math.PI / 2f);
		entryDirections.Add(0f);
	}

	public void SetupMarsWest()
	{
		mars = true;
		entryDirections.Remove(-(float)Math.PI / 2f);
		entryDirections.Add((float)Math.PI);
	}

	public void SetupFastEntry()
	{
		fastEntry = true;
	}

	internal void SetupAsteroidChase()
	{
		entryDirections.Remove(-(float)Math.PI / 2f);
		entryDirections.Add(0f);
		entryDirections.Add((float)Math.PI / 2f);
	}
}
