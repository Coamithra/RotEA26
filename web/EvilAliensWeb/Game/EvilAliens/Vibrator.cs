using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace EvilAliens;

public class Vibrator : GameComponent, IVibratorService
{
	private List<VibratorInfo> VibrationTimers;

	private Vector2[] power = (Vector2[])(object)new Vector2[4];

	Vibrator IVibratorService.Vibrator => this;

	public Vibrator(Game game)
		: base(game)
	{
		VibrationTimers = new List<VibratorInfo>();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		for (int i = 0; i < 4; i++)
		{
			ref Vector2 powerSlot = ref power[i];
			powerSlot = Vector2.Zero;
		}
		for (int num = VibrationTimers.Count - 1; num > 0; num--)
		{
			VibrationTimers[num].timer.Update(gameTime);
			ref Vector2 powerSlot = ref power[(int)VibrationTimers[num].player];
			powerSlot = Vector2.Max(VibrationTimers[num].power, power[(int)VibrationTimers[num].player]);
			if (VibrationTimers[num].timer.Finished)
			{
				VibrationTimers.RemoveAt(num);
			}
		}
		for (int j = 0; j < 4; j++)
		{
			GamePad.SetVibration((PlayerIndex)j, power[j].X, power[j].Y);
		}
	}

	public void addVibration(Vector2 power, float time, PlayerIndex playerIndex)
	{
		VibratorInfo item = default(VibratorInfo);
		item.timer = new Timer(time, repeating: false);
		item.power = power;
		item.player = playerIndex;
		VibrationTimers.Add(item);
	}

	public void DisableVibrations()
	{
		VibrationTimers.Clear();
	}
}
