using Microsoft.Xna.Framework;

namespace EvilAliens;

public class PlayerInfo
{
	public int NR;

	public float hue;

	public bool isPlaying;

	public ControlDevice controller;

	public Vector2 position;

	public PlayerInfo(int NR)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		this.NR = NR;
		hue = -1f;
		isPlaying = false;
		controller = ControlDevice.Keyboard;
		position = Vector2.Zero;
	}

	public void Reset()
	{
		isPlaying = false;
	}
}
