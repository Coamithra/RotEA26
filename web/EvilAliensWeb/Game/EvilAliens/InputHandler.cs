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
			GamePadState state = GamePad.GetState((PlayerIndex)i);
			GamePadThumbSticks thumbSticks = (state).ThumbSticks;
			return (thumbSticks).Right * new Vector2(1f, -1f);
		}
		GamePadState state2 = GamePad.GetState((PlayerIndex)i);
		GamePadThumbSticks thumbSticks2 = (state2).ThumbSticks;
		return (thumbSticks2).Left * new Vector2(1f, -1f);
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
			GamePadState state = GamePad.GetState((PlayerIndex)i, (GamePadDeadZone)2);
			GamePadThumbSticks thumbSticks = (state).ThumbSticks;
			return (thumbSticks).Left * new Vector2(1f, -1f);
		}
		GamePadState state2 = GamePad.GetState((PlayerIndex)i, (GamePadDeadZone)2);
		GamePadThumbSticks thumbSticks2 = (state2).ThumbSticks;
		return (thumbSticks2).Right * new Vector2(1f, -1f);
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
			padConnected[i] = (state).IsConnected;
			for (int j = 0; j < padKeysValues.Count; j++)
			{
				bool held = false;
				switch (j)
				{
				case 9:
				{
					GamePadButtons buttons5 = (state).Buttons;
					held = (int)(buttons5).A == 1;
					break;
				}
				case 10:
				{
					GamePadButtons buttons = (state).Buttons;
					held = (int)(buttons).B == 1;
					break;
				}
				case 5:
				{
					GamePadButtons buttons3 = (state).Buttons;
					held = (int)(buttons3).Back == 1;
					break;
				}
				case 4:
				{
					GamePadButtons buttons8 = (state).Buttons;
					held = (int)(buttons8).Start == 1;
					break;
				}
				case 8:
				{
					bool num17 = held;
					GamePadButtons buttons6 = (state).Buttons;
					held = num17 | ((int)(buttons6).LeftShoulder == 1);
					bool num18 = held;
					GamePadButtons buttons7 = (state).Buttons;
					held = num18 | ((int)(buttons7).RightShoulder == 1);
					bool num19 = held;
					GamePadTriggers triggers3 = (state).Triggers;
					held = num19 | ((triggers3).Left > 0.5f);
					bool num20 = held;
					GamePadTriggers triggers4 = (state).Triggers;
					held = num20 | ((triggers4).Right > 0.5f);
					break;
				}
				case 6:
				{
					bool num7 = held;
					GamePadButtons buttons2 = (state).Buttons;
					held = num7 | ((int)(buttons2).LeftShoulder == 1);
					bool num8 = held;
					GamePadTriggers triggers = (state).Triggers;
					held = num8 | ((triggers).Left > 0.5f);
					break;
				}
				case 7:
				{
					bool num12 = held;
					GamePadButtons buttons4 = (state).Buttons;
					held = num12 | ((int)(buttons4).RightShoulder == 1);
					bool num13 = held;
					GamePadTriggers triggers2 = (state).Triggers;
					held = num13 | ((triggers2).Right > 0.5f);
					break;
				}
				case 2:
				{
					if (padkeysdown[i][j])
					{
						bool num4 = held;
						GamePadThumbSticks thumbSticks3 = (state).ThumbSticks;
						held = num4 | ((thumbSticks3).Left.X < -0.42000002f);
					}
					else
					{
						bool num5 = held;
						GamePadThumbSticks thumbSticks4 = (state).ThumbSticks;
						held = num5 | ((thumbSticks4).Left.X < -0.58f);
					}
					bool num6 = held;
					GamePadDPad dPad2 = (state).DPad;
					held = num6 | ((int)(dPad2).Left == 1);
					break;
				}
				case 3:
				{
					if (padkeysdown[i][j])
					{
						bool num14 = held;
						GamePadThumbSticks thumbSticks7 = (state).ThumbSticks;
						held = num14 | ((thumbSticks7).Left.X > 0.42000002f);
					}
					else
					{
						bool num15 = held;
						GamePadThumbSticks thumbSticks8 = (state).ThumbSticks;
						held = num15 | ((thumbSticks8).Left.X > 0.58f);
					}
					bool num16 = held;
					GamePadDPad dPad4 = (state).DPad;
					held = num16 | ((int)(dPad4).Right == 1);
					break;
				}
				case 0:
				{
					if (padkeysdown[i][j])
					{
						bool num9 = held;
						GamePadThumbSticks thumbSticks5 = (state).ThumbSticks;
						held = num9 | ((thumbSticks5).Left.Y > 0.42000002f);
					}
					else
					{
						bool num10 = held;
						GamePadThumbSticks thumbSticks6 = (state).ThumbSticks;
						held = num10 | ((thumbSticks6).Left.Y > 0.58f);
					}
					bool num11 = held;
					GamePadDPad dPad3 = (state).DPad;
					held = num11 | ((int)(dPad3).Up == 1);
					break;
				}
				case 1:
				{
					if (padkeysdown[i][j])
					{
						bool num = held;
						GamePadThumbSticks thumbSticks = (state).ThumbSticks;
						held = num | ((thumbSticks).Left.Y < -0.42000002f);
					}
					else
					{
						bool num2 = held;
						GamePadThumbSticks thumbSticks2 = (state).ThumbSticks;
						held = num2 | ((thumbSticks2).Left.Y < -0.58f);
					}
					bool num3 = held;
					GamePadDPad dPad = (state).DPad;
					held = num3 | ((int)(dPad).Down == 1);
					break;
				}
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
