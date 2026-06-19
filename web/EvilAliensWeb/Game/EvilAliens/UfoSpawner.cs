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
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
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
