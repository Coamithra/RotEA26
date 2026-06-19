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
		return _random.NextDouble() <= (double)(hitsPerSec * (float)gameTime.ElapsedGameTime.TotalSeconds);
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
