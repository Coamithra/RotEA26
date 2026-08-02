using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public static class RandomHelper
{
	private static Random _random = new Random();

	public static Random Random => _random;

	public static float RandomNextAngle()
	{
		return RandomNextFloat(0f, (float)Math.PI * 2f);
	}

	public static bool RandomFromAverage(float hitsPerSec, GameTime gameTime)
	{
		return RandomFromAverage(hitsPerSec, (float)gameTime.ElapsedGameTime.TotalSeconds);
	}

	// The same roll for a caller holding a delta rather than a GameTime -- a Draw-side clock on
	// Compat/WorldTime, where the frame's own ElapsedGameTime is the WRONG number (card d79a2f48):
	// it ignores the pause, the hit-stop and the slow-mo the world delta carries, so the rate
	// would be per REAL second while everything it gates runs on world seconds.
	public static bool RandomFromAverage(float hitsPerSec, float dtSeconds)
	{
		return _random.NextDouble() <= (double)(hitsPerSec * dtSeconds);
	}

	public static float RandomNextFloat(float min, float max)
	{
		float num = Convert.ToSingle(_random.NextDouble());
		num *= max - min;
		return num + min;
	}

	internal static bool RandomNextBool()
	{
		return Random.Next(2) == 1;
	}
}
