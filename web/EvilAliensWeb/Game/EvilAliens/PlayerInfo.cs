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
