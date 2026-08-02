using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public static class RandomHelper
{
	private static Random _random = new Random();

	public static Random Random => _random;

	// The seed ?seed=<n> put in force, or null for the shipped unseeded boot. Read-only
	// diagnostic -- nothing in the game branches on it.
	public static int? SeededWith { get; private set; }

	// Make this boot's gameplay RNG reproducible (card d937c721). Called ONCE, from
	// DebugFlags.Parse, which every host runs before the first tick -- so a seeded run
	// starts its stream at position 0 exactly as an unseeded one does, and the only
	// difference between two same-seed runs is nothing at all. Without it, two eahl runs
	// of one level diverge (measured on ?level=OwnLevel: mean |diff| 0.2, MAX 210 of 255),
	// which is noise larger than most effects a screenshot A/B is trying to measure.
	//
	// This reaches THIS stream only. Quad's and ShipConnector's FX RNGs, Juice's shake RNG
	// and SplashScene's rng are deliberately separate instances so a cosmetic draw cannot
	// advance the gameplay stream, and they stay unseeded: seeding them is a bigger change
	// than this card, and their contribution to a frame diff is bounded by whether the rig
	// even shows a laser, a connector, a shake or the splash.
	//
	// Any int is a legal seed, negatives included (.NET Core's Random handles int.MinValue).
	public static void Reseed(int seed)
	{
		_random = new Random(seed);
		SeededWith = seed;
	}

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
