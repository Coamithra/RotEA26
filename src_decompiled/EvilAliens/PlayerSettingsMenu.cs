using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class PlayerSettingsMenu : MenuSub1
{
	private Texture2D black;

	public ControlDevice Starter;

	private List<ControlDevice> activeDevices;

	private List<int> selectedEntries;

	private List<bool> done;

	private bool darken;

	private bool exiting;

	public PlayerSettingsMenu(Game game, bool darken)
		: base(game)
	{
		this.darken = darken;
		activeDevices = new List<ControlDevice>();
		selectedEntries = new List<int>();
		done = new List<bool>();
		allowNormalExit = false;
		((DrawableGameComponent)this).DrawOrder = 2000;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		if (darken)
		{
			base.SpriteBatch.Draw(black, new Rectangle(0, 0, 800, 600), new Color((byte)0, (byte)0, (byte)0, (byte)128));
		}
		base.Draw(gameTime);
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		base.DrawMenu(gameTime, yoffset);
		for (int i = 0; i < 4; i++)
		{
			drawPlayerSettings(i, gameTime);
		}
	}

	private void drawPlayerSettings(int i, GameTime gameTime)
	{
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		float num = ((Rectangle)(ref General.SafeZone)).Left;
		float num2 = (float)(((Rectangle)(ref General.SafeZone)).Right - ((Rectangle)(ref General.SafeZone)).Left) / 4f;
		num += num2 * ((float)i + 0.5f);
		if (activeDevices.Count > i)
		{
			float num3 = 0.8f;
			float num4 = font.MeasureString("x").Y * num3;
			float num5 = 100f;
			ControlDevice controlDevice = activeDevices[i];
			PlayerSettings playerSettings = Settings.GetInstance().GetPlayerSettings(controlDevice);
			int num6 = selectedEntries[i];
			if (done[i])
			{
				num6 = 1000;
			}
			base.SpriteBatch.DrawString(controlDevice.ToString(), new Vector2(num, num5), Color.AliceBlue, 0f, centered: true, num3, (SpriteEffects)0, 1f);
			num5 += num4 * 2f;
			num5 = drawSetting(num, num3, num4, num5, "Rumble", !playerSettings.DisableRumble, num6 == 0, gameTime);
			num5 = drawSetting(num, num3, num4, num5, "Swap Sticks", playerSettings.InvertSticks, num6 == 1, gameTime);
			num5 = drawSetting(num, num3, num4, num5, "Done", null, num6 == 2, gameTime);
		}
		else
		{
			base.SpriteBatch.DrawString("Press\nStart", new Vector2(num, 100f), Color.AliceBlue, 0f, centered: true, 1f, (SpriteEffects)0, 1f);
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (base.InputHandler.Pressed(MyKeys.Up) || base.InputHandler.Pressed(MyKeys.Left))
		{
			moveSelection(ControlDevice.Keyboard, -1);
		}
		if (base.InputHandler.Pressed(MyKeys.Down) || base.InputHandler.Pressed(MyKeys.Right))
		{
			moveSelection(ControlDevice.Keyboard, 1);
		}
		if (base.InputHandler.Pressed(MyKeys.Enter))
		{
			startPressed(ControlDevice.Keyboard);
		}
		if (base.InputHandler.Pressed(MyKeys.Esc))
		{
			cancelPressed(ControlDevice.Keyboard);
		}
		for (int i = 0; i < 4; i++)
		{
			ControlDevice controlDevice = i switch
			{
				0 => ControlDevice.PadOne, 
				1 => ControlDevice.PadTwo, 
				2 => ControlDevice.PadThree, 
				3 => ControlDevice.PadFour, 
				_ => throw new Exception(), 
			};
			if (base.InputHandler.PadPressed(PadKeys.Up, i) || base.InputHandler.PadPressed(PadKeys.Left, i))
			{
				moveSelection(controlDevice, -1);
			}
			if (base.InputHandler.PadPressed(PadKeys.Down, i) || base.InputHandler.PadPressed(PadKeys.Right, i))
			{
				moveSelection(controlDevice, 1);
			}
			if (base.InputHandler.PadPressed(PadKeys.Start, i) || base.InputHandler.PadPressed(PadKeys.A, i))
			{
				startPressed(controlDevice);
			}
			if (base.InputHandler.PadPressed(PadKeys.Back, i) || base.InputHandler.PadPressed(PadKeys.B, i))
			{
				cancelPressed(controlDevice);
			}
		}
		bool flag = true;
		foreach (bool item in done)
		{
			flag = flag && item;
		}
		if (flag && !exiting)
		{
			Settings.GetInstance().SaveThreaded();
			exiting = true;
			doExit();
		}
	}

	private void cancelPressed(ControlDevice controlDevice)
	{
		if (activeDevices.Contains(controlDevice))
		{
			done[activeDevices.IndexOf(controlDevice)] = !done[activeDevices.IndexOf(controlDevice)];
		}
	}

	private void startPressed(ControlDevice controlDevice)
	{
		if (activeDevices.Contains(controlDevice))
		{
			PlayerSettings playerSettings = Settings.GetInstance().GetPlayerSettings(controlDevice);
			switch (selectedEntries[activeDevices.IndexOf(controlDevice)])
			{
			case 0:
				playerSettings.DisableRumble = !playerSettings.DisableRumble;
				break;
			case 1:
				playerSettings.InvertSticks = !playerSettings.InvertSticks;
				break;
			case 2:
				done[activeDevices.IndexOf(controlDevice)] = true;
				break;
			}
		}
		else
		{
			activeDevices.Add(controlDevice);
			selectedEntries.Add(0);
			done.Add(item: false);
		}
	}

	private void moveSelection(ControlDevice device, int direction)
	{
		int num = activeDevices.IndexOf(device);
		if (num != -1)
		{
			selectedEntries[num] = MyMath.Mod(selectedEntries[num] + direction, 3);
		}
	}

	private float drawSetting(float x, float scale, float ystep, float y, string name, bool? value, bool selected, GameTime gameTime)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		Color color;
		if (selected)
		{
			float num = 15f / font.MeasureString(name).X * scale;
			float num2 = (float)gameTime.TotalGameTime.TotalSeconds;
			float num3 = MyMath.Mod(num2 / 2f, 1f);
			color = Color.AliceBlue;
			scale *= 1f + num * brainPulsate.Evaluate(num3);
		}
		else
		{
			color = Color.Gray;
		}
		base.SpriteBatch.DrawString(name, new Vector2(x, y), color, 0f, centered: true, scale, (SpriteEffects)0, 1f);
		if (value.HasValue)
		{
			y += ystep;
			base.SpriteBatch.DrawString(MenuScene.boolToGameString(value.Value), new Vector2(x, y), color, 0f, centered: true, scale, (SpriteEffects)0, 1f);
		}
		y += ystep * 2f;
		return y;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		black = Content.Load<Texture2D>("GFX/Menu/blank");
	}

	public override void Initialize()
	{
		base.Initialize();
		activeDevices.Clear();
		activeDevices.Add(Starter);
		selectedEntries.Clear();
		selectedEntries.Add(0);
		done.Clear();
		done.Add(item: false);
		exiting = false;
	}
}
