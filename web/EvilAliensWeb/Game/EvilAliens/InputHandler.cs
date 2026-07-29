using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class InputHandler : IInputHandlerService
{
	private List<PadKeys> padKeysValues;

	private Vector2 mousepos;

	private bool[] pressed = new bool[Game1.GetEnumValues<MyKeys>().Count];

	private bool[] down = new bool[Game1.GetEnumValues<MyKeys>().Count];

	private bool[] pressedAndIdle = new bool[Game1.GetEnumValues<MyKeys>().Count];

	private bool[][] padkeyspressed;

	private bool[][] padkeysdown;

	private bool[][] padkeyspressedAndIdle;

	private bool[] padConnected;

	private Keys[][] keysToCheck;

	public Vector2 MousePosition => mousepos;

	InputHandler IInputHandlerService.InputHandler => this;

	public bool Pressed(MyKeys key)
	{
		return pressed[(int)key];
	}

	public bool Down(MyKeys key)
	{
		return down[(int)key];
	}

	public bool PadPressed(PadKeys key, int i)
	{
		return padkeyspressed[i][(int)key];
	}

	public bool PadDown(PadKeys key, int i)
	{
		return padkeysdown[i][(int)key];
	}

	public InputHandler()
	{
		padKeysValues = Game1.GetEnumValues<PadKeys>();
		padkeyspressed = new bool[4][];
		padkeysdown = new bool[4][];
		padkeyspressedAndIdle = new bool[4][];
		padConnected = new bool[4];
		for (int i = 0; i < 4; i++)
		{
			padkeyspressed[i] = new bool[padKeysValues.Count];
			padkeysdown[i] = new bool[padKeysValues.Count];
			padkeyspressedAndIdle[i] = new bool[padKeysValues.Count];
			for (int j = 0; j < padKeysValues.Count; j++)
			{
				padkeyspressed[i][j] = false;
				padkeysdown[i][j] = false;
				padkeyspressedAndIdle[i][j] = false;
			}
		}
		keysToCheck = new Keys[Game1.GetEnumValues<MyKeys>().Count][];
		for (int k = 0; k < pressed.Length; k++)
		{
			pressed[k] = false;
			down[k] = false;
			pressedAndIdle[k] = false;
		}
		keysToCheck[0] = (Keys[])(object)new Keys[2]
		{
			(Keys)38,
			(Keys)87
		};
		keysToCheck[1] = (Keys[])(object)new Keys[2]
		{
			(Keys)40,
			(Keys)83
		};
		keysToCheck[2] = (Keys[])(object)new Keys[2]
		{
			(Keys)37,
			(Keys)65
		};
		keysToCheck[3] = (Keys[])(object)new Keys[2]
		{
			(Keys)39,
			(Keys)68
		};
		keysToCheck[4] = (Keys[])(object)new Keys[1] { (Keys)13 };
		keysToCheck[5] = (Keys[])(object)new Keys[1] { (Keys)27 };
		keysToCheck[6] = (Keys[])(object)new Keys[0];
		keysToCheck[7] = (Keys[])(object)new Keys[0];
		keysToCheck[8] = (Keys[])(object)new Keys[0];
	}

	public Vector2 LeftStick(int i)
	{
		ControlDevice controller = i switch
		{
			0 => ControlDevice.PadOne, 
			1 => ControlDevice.PadTwo, 
			2 => ControlDevice.PadThree, 
			3 => ControlDevice.PadFour, 
			_ => throw new NotSupportedException(), 
		};
		if (Settings.GetInstance().GetPlayerSettings(controller).InvertSticks)
		{
			GamePadState swappedSticksState = GamePad.GetState((PlayerIndex)i);
			return swappedSticksState.ThumbSticks.Right * new Vector2(1f, -1f);
		}
		GamePadState state = GamePad.GetState((PlayerIndex)i);
		return state.ThumbSticks.Left * new Vector2(1f, -1f);
	}

	public Vector2 RightStick(int i)
	{
		ControlDevice controller = i switch
		{
			0 => ControlDevice.PadOne, 
			1 => ControlDevice.PadTwo, 
			2 => ControlDevice.PadThree, 
			3 => ControlDevice.PadFour, 
			_ => throw new NotSupportedException(), 
		};
		if (Settings.GetInstance().GetPlayerSettings(controller).InvertSticks)
		{
			GamePadState swappedSticksState = GamePad.GetState((PlayerIndex)i, (GamePadDeadZone)2);
			return swappedSticksState.ThumbSticks.Left * new Vector2(1f, -1f);
		}
		GamePadState state = GamePad.GetState((PlayerIndex)i, (GamePadDeadZone)2);
		return state.ThumbSticks.Right * new Vector2(1f, -1f);
	}

	public void Update()
	{
		// Web/PC port: the Xbox build called Keyboard.GetState() but threw the result
		// away — the block that tested keysToCheck[] against the keyboard lived under
		// #if WINDOWS and was stripped from the shipped binary (the keysToCheck table
		// it built was left dead). Restored here, which re-activates every latent
		// keyboard path: menu navigation, Enter to start/select, Esc to back, and the
		// in-game ControlDevice.Keyboard player. keysToCheck[i] = physical Keys for MyKeys i.
		KeyboardState keyboardState = Keyboard.GetState();
		MouseState state = Mouse.GetState();
		// Stage 10 presenter: KNI's back buffer is the browser-WINDOW size, and the mouse
		// arrives in those window pixels — but the game (mouse-aim fire in PlayerShip, the
		// software cursor in MousePointer) works in 800x600 design space. Undo Game1.Draw's
		// letterbox scale+offset so the cursor maps to the design point under it; otherwise
		// the ship fires toward a scaled/shifted phantom point, not where you clicked.
		mousepos = RenderScale.WindowToDesign(new Vector2((float)(state).X, (float)(state).Y));
		bool held = false;
		for (int i = 0; i < keysToCheck.Length; i++)
		{
			held = false;
			if (keysToCheck[i] != null)
			{
				for (int k = 0; k < keysToCheck[i].Length; k++)
				{
					if (keyboardState.IsKeyDown(keysToCheck[i][k]))
					{
						held = true;
						break;
					}
				}
			}
			switch (i)
			{
			case 6:
				held |= (int)(state).LeftButton == 1;
				break;
			case 7:
				held |= (int)(state).RightButton == 1;
				break;
			}
			// Swallow the Esc that the browser delivers when it EXITS fullscreen on Esc, so
			// leaving fullscreen doesn't also step back a menu (card b0a2f525). `held` here is
			// the raw keyboard Esc (the mouse switch above only touches Mouse1/Mouse2); mask it
			// BEFORE the scripted-input add so eaPress('Esc') automation is unaffected. Called
			// once per tick (i loops once over Esc), which is how EscSuppressActive counts down.
			if (i == (int)MyKeys.Esc && DebugInput.EscSuppressActive(held))
			{
				held = false;
			}
			// Debug input injection (immune to the rAF frame-timing miss): force this key
			// down for any remaining injected ticks. Done inside the tick so a scripted
			// tap can't fall between keyboard polls. See Compat/DebugInput.cs / eaPress().
			held |= DebugInput.Consume(i);
			if (held)
			{
				if (!pressedAndIdle[i])
				{
					down[i] = true;
					pressed[i] = true;
					pressedAndIdle[i] = true;
				}
				else
				{
					pressed[i] = false;
				}
			}
			else
			{
				down[i] = false;
				pressed[i] = false;
				pressedAndIdle[i] = false;
			}
		}
		UpdateKeyPads();
	}

	private void UpdateKeyPads()
	{
		for (int i = 0; i < 4; i++)
		{
			GamePadState state = GamePad.GetState((PlayerIndex)i);
			padConnected[i] = state.IsConnected;
			for (int j = 0; j < padKeysValues.Count; j++)
			{
				bool held = false;
				switch (j)
				{
				case 9:
					held = (int)state.Buttons.A == 1;
					break;
				case 10:
					held = (int)state.Buttons.B == 1;
					break;
				case 5:
					held = (int)state.Buttons.Back == 1;
					break;
				case 4:
					held = (int)state.Buttons.Start == 1;
					break;
				case 8:
					held |= (int)state.Buttons.LeftShoulder == 1;
					held |= (int)state.Buttons.RightShoulder == 1;
					held |= state.Triggers.Left > 0.5f;
					held |= state.Triggers.Right > 0.5f;
					break;
				case 6:
					held |= (int)state.Buttons.LeftShoulder == 1;
					held |= state.Triggers.Left > 0.5f;
					break;
				case 7:
					held |= (int)state.Buttons.RightShoulder == 1;
					held |= state.Triggers.Right > 0.5f;
					break;
				case 2:
					if (padkeysdown[i][j])
					{
						held |= state.ThumbSticks.Left.X < -0.42000002f;
					}
					else
					{
						held |= state.ThumbSticks.Left.X < -0.58f;
					}
					held |= (int)state.DPad.Left == 1;
					break;
				case 3:
					if (padkeysdown[i][j])
					{
						held |= state.ThumbSticks.Left.X > 0.42000002f;
					}
					else
					{
						held |= state.ThumbSticks.Left.X > 0.58f;
					}
					held |= (int)state.DPad.Right == 1;
					break;
				case 0:
					if (padkeysdown[i][j])
					{
						held |= state.ThumbSticks.Left.Y > 0.42000002f;
					}
					else
					{
						held |= state.ThumbSticks.Left.Y > 0.58f;
					}
					held |= (int)state.DPad.Up == 1;
					break;
				case 1:
					if (padkeysdown[i][j])
					{
						held |= state.ThumbSticks.Left.Y < -0.42000002f;
					}
					else
					{
						held |= state.ThumbSticks.Left.Y < -0.58f;
					}
					held |= (int)state.DPad.Down == 1;
					break;
				}
				if (held)
				{
					if (!padkeyspressedAndIdle[i][j])
					{
						padkeysdown[i][j] = true;
						padkeyspressed[i][j] = true;
						padkeyspressedAndIdle[i][j] = true;
					}
					else
					{
						padkeyspressed[i][j] = false;
					}
				}
				else
				{
					padkeysdown[i][j] = false;
					padkeyspressed[i][j] = false;
					padkeyspressedAndIdle[i][j] = false;
				}
			}
		}
	}

	public bool PadConnected(int i)
	{
		return padConnected[i];
	}
}
