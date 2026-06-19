using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class UfoFormationSpawner : GameEvent
{
	private int waves;

	private int startwaves;

	private bool left;

	private Timer wavetimer = new Timer(3000f, repeating: false);

	public UfoFormationSpawner(Game game, int waves)
		: base(game, 0f)
	{
		startwaves = waves;
		this.waves = waves;
	}

	public override void Reset()
	{
		waves = startwaves;
		left = true;
		base.Reset();
		wavetimer.Reset();
		wavetimer.Start();
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		wavetimer.Update(gameTime);
		if (wavetimer.Finished)
		{
			int num = Math.Max(2, (int)(4f * Settings.GetInstance().DifficultyModifier * Settings.GetInstance().MultiPlayerDifficultyModifier(Oracle.LiveShips)));
			float num2 = ((!left) ? 650f : 150f);
			for (int i = 0; i < num; i++)
			{
				for (int j = 0; j < i + 1; j++)
				{
					UFO uFO = UFO.NewUFO(collectionHelper, game);
					float num3 = j * 60 - i * 30;
					uFO.Setup(new Vector2(num2 + num3, (float)(-48 - i * 60)), isBig: false, EnemyBehaviour.normal);
					if (i == 0)
					{
						uFO.SetAsBonus();
					}
					collectionHelper.Add((GameComponent)(object)uFO);
					uFO.FlyInTime(3000f);
				}
			}
			waves--;
			left = !left;
			if (waves == 0)
			{
				Terminate();
			}
			wavetimer.Reset();
			wavetimer.Start();
		}
		base.Update(gameTime);
	}
}
