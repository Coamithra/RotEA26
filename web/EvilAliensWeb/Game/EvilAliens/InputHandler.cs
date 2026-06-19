using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

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
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Invalid comparison between Unknown and I4
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Invalid comparison between Unknown and I4
		Keyboard.GetState();
		MouseState state = Mouse.GetState();
		mousepos = new Vector2((float)(state).X, (float)(state).Y);
		bool flag = false;
		for (int i = 0; i < keysToCheck.Length; i++)
		{
			flag = false;
			switch (i)
			{
			case 6:
				flag = (int)(state).LeftButton == 1;
				break;
			case 7:
				flag = (int)(state).RightButton == 1;
				break;
			}
			if (flag)
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
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Invalid comparison between Unknown and I4
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Invalid comparison between Unknown and I4
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Invalid comparison between Unknown and I4
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Invalid comparison between Unknown and I4
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Invalid comparison between Unknown and I4
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Invalid comparison between Unknown and I4
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Invalid comparison between Unknown and I4
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Invalid comparison between Unknown and I4
		//IL_0292: Unknown result type (might be due to invalid IL or missing references)
		//IL_0297: Unknown result type (might be due to invalid IL or missing references)
		//IL_029b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Unknown result type (might be due to invalid IL or missing references)
		//IL_0276: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02da: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Invalid comparison between Unknown and I4
		//IL_0315: Unknown result type (might be due to invalid IL or missing references)
		//IL_031a: Unknown result type (might be due to invalid IL or missing references)
		//IL_031e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0324: Invalid comparison between Unknown and I4
		//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f2: Invalid comparison between Unknown and I4
		//IL_024a: Unknown result type (might be due to invalid IL or missing references)
		//IL_024f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0253: Unknown result type (might be due to invalid IL or missing references)
		//IL_0259: Invalid comparison between Unknown and I4
		for (int i = 0; i < 4; i++)
		{
			GamePadState state = GamePad.GetState((PlayerIndex)i);
			padConnected[i] = (state).IsConnected;
			for (int j = 0; j < padKeysValues.Count; j++)
			{
				bool flag = false;
				switch (j)
				{
				case 9:
				{
					GamePadButtons buttons5 = (state).Buttons;
					flag = (int)(buttons5).A == 1;
					break;
				}
				case 10:
				{
					GamePadButtons buttons = (state).Buttons;
					flag = (int)(buttons).B == 1;
					break;
				}
				case 5:
				{
					GamePadButtons buttons3 = (state).Buttons;
					flag = (int)(buttons3).Back == 1;
					break;
				}
				case 4:
				{
					GamePadButtons buttons8 = (state).Buttons;
					flag = (int)(buttons8).Start == 1;
					break;
				}
				case 8:
				{
					bool num17 = flag;
					GamePadButtons buttons6 = (state).Buttons;
					flag = num17 | ((int)(buttons6).LeftShoulder == 1);
					bool num18 = flag;
					GamePadButtons buttons7 = (state).Buttons;
					flag = num18 | ((int)(buttons7).RightShoulder == 1);
					bool num19 = flag;
					GamePadTriggers triggers3 = (state).Triggers;
					flag = num19 | ((triggers3).Left > 0.5f);
					bool num20 = flag;
					GamePadTriggers triggers4 = (state).Triggers;
					flag = num20 | ((triggers4).Right > 0.5f);
					break;
				}
				case 6:
				{
					bool num7 = flag;
					GamePadButtons buttons2 = (state).Buttons;
					flag = num7 | ((int)(buttons2).LeftShoulder == 1);
					bool num8 = flag;
					GamePadTriggers triggers = (state).Triggers;
					flag = num8 | ((triggers).Left > 0.5f);
					break;
				}
				case 7:
				{
					bool num12 = flag;
					GamePadButtons buttons4 = (state).Buttons;
					flag = num12 | ((int)(buttons4).RightShoulder == 1);
					bool num13 = flag;
					GamePadTriggers triggers2 = (state).Triggers;
					flag = num13 | ((triggers2).Right > 0.5f);
					break;
				}
				case 2:
				{
					if (padkeysdown[i][j])
					{
						bool num4 = flag;
						GamePadThumbSticks thumbSticks3 = (state).ThumbSticks;
						flag = num4 | ((thumbSticks3).Left.X < -0.42000002f);
					}
					else
					{
						bool num5 = flag;
						GamePadThumbSticks thumbSticks4 = (state).ThumbSticks;
						flag = num5 | ((thumbSticks4).Left.X < -0.58f);
					}
					bool num6 = flag;
					GamePadDPad dPad2 = (state).DPad;
					flag = num6 | ((int)(dPad2).Left == 1);
					break;
				}
				case 3:
				{
					if (padkeysdown[i][j])
					{
						bool num14 = flag;
						GamePadThumbSticks thumbSticks7 = (state).ThumbSticks;
						flag = num14 | ((thumbSticks7).Left.X > 0.42000002f);
					}
					else
					{
						bool num15 = flag;
						GamePadThumbSticks thumbSticks8 = (state).ThumbSticks;
						flag = num15 | ((thumbSticks8).Left.X > 0.58f);
					}
					bool num16 = flag;
					GamePadDPad dPad4 = (state).DPad;
					flag = num16 | ((int)(dPad4).Right == 1);
					break;
				}
				case 0:
				{
					if (padkeysdown[i][j])
					{
						bool num9 = flag;
						GamePadThumbSticks thumbSticks5 = (state).ThumbSticks;
						flag = num9 | ((thumbSticks5).Left.Y > 0.42000002f);
					}
					else
					{
						bool num10 = flag;
						GamePadThumbSticks thumbSticks6 = (state).ThumbSticks;
						flag = num10 | ((thumbSticks6).Left.Y > 0.58f);
					}
					bool num11 = flag;
					GamePadDPad dPad3 = (state).DPad;
					flag = num11 | ((int)(dPad3).Up == 1);
					break;
				}
				case 1:
				{
					if (padkeysdown[i][j])
					{
						bool num = flag;
						GamePadThumbSticks thumbSticks = (state).ThumbSticks;
						flag = num | ((thumbSticks).Left.Y < -0.42000002f);
					}
					else
					{
						bool num2 = flag;
						GamePadThumbSticks thumbSticks2 = (state).ThumbSticks;
						flag = num2 | ((thumbSticks2).Left.Y < -0.58f);
					}
					bool num3 = flag;
					GamePadDPad dPad = (state).DPad;
					flag = num3 | ((int)(dPad).Down == 1);
					break;
				}
				}
				if (flag)
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
