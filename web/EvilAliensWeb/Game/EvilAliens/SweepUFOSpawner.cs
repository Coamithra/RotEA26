using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class SweepUFOSpawner : GenericSpawner
{
	public SweepUFOSpawner(Game game, float duration, float hitspersec)
		: base(game, duration, hitspersec, randomly: false, scaleSpawns: true)
	{
	}

	protected override void DoEvent(GameTime gameTime)
	{
		int num = Math.Max(3, (int)(5f * Settings.GetInstance().DifficultyModifier));
		bool targetplayer = RandomHelper.RandomNextBool();
		for (int i = 0; i < num; i++)
		{
			SweepUFO sweepUFO = SweepUFO.NewSweepUFO(collectionHelper, game);
			sweepUFO.Setup(targetplayer, i, num);
			collectionHelper.Add((GameComponent)(object)sweepUFO);
		}
	}
}
