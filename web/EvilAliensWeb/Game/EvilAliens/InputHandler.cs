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
		// A scripted cursor (eaMouseAt / eval MouseAt) wins over the real mouse, so automation
		// can put the pointer ON a menu entry or the back tip -- `eaPress('Mouse1')` alone only
		// ever supplied the button. Never set on an ordinary boot.
		if (EvilAliensWeb.Compat.DebugInput.TryGetMouseOverride(out float overrideX, out float overrideY))
		{
			mousepos = new Vector2(overrideX, overrideY);
		}
		// The two mouse buttons are resolved ONCE, before the key loop, because Esc (5) is
		// polled before Mouse1 (6) and the clickable back tip below needs the settled left
		// button. Each source is still evaluated exactly once per tick:
		//  - MouseLatch.Filter drops a press that began OUTSIDE the canvas (card 0fe23476) --
		//    KNI's own mouse listeners are on the window, so a click on the room-code prompt,
		//    the fullscreen button or a tuning panel otherwise reaches the menu underneath.
		//  - MouseLatch.Consume folds in a click SHORTER than one tick, which this poll cannot
		//    see at all (card 724f2abc); both samples read Released while the cursor POSITION
		//    survives, so a menu row hover-highlights and never invokes.
		bool mouse1Held = MouseLatch.FilterOffCanvas((int)MyKeys.Mouse1, (int)(state).LeftButton == 1) | MouseLatch.Consume((int)MyKeys.Mouse1);
		bool mouse2Held = MouseLatch.FilterOffCanvas((int)MyKeys.Mouse2, (int)(state).RightButton == 1) | MouseLatch.Consume((int)MyKeys.Mouse2);
		// Card 2a4110d0: a click on the bottom-left "(B) back" tip drawn last frame acts as
		// Esc, which is already "back" everywhere (menus, the pause overlay, the brag screen).
		// Consumed here so the recorded box lives exactly one frame -- see Compat/BackTipHit.
		// The RISING EDGE, not the level: `pressedAndIdle` still holds the previous tick's
		// state at this point (the loop below is what updates it), so this is the same edge
		// `Pressed(MyKeys.Mouse1)` will report. A level would fire on a press that began
		// elsewhere -- see ConsumeClick.
		// `DebugInput.Peek`, not `Consume`: the key loop below does the one real consume for
		// Mouse1 (a scripted hold is a countdown, so consuming twice in a tick eats two ticks
		// of it). Without this a scripted click reached `HandleMouse` -- which reads Mouse1
		// from the loop -- but never the back tip, leaving the one surface that has no other
		// automation route unreachable.
		bool mouse1Down = mouse1Held || EvilAliensWeb.Compat.DebugInput.Peek((int)MyKeys.Mouse1);
		bool mouse1Pressed = mouse1Down && !pressedAndIdle[(int)MyKeys.Mouse1];
		bool backTipClicked = EvilAliensWeb.Compat.BackTipHit.ConsumeClick(mousepos, mouse1Pressed);
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
				held |= mouse1Held;
				break;
			case 7:
				held |= mouse2Held;
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
			// The clickable back tip, added AFTER that mask so the fullscreen-exit Esc guard
			// (which counts down against the raw keyboard Esc) can neither swallow the click
			// nor be spent by it.
			if (i == (int)MyKeys.Esc)
			{
				held |= backTipClicked;
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
