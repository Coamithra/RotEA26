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
		base.DrawOrder = 2000;
	}

	public override void Draw(GameTime gameTime)
	{
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
		float columnX = (General.SafeZone).Left;
		float columnW = (float)((General.SafeZone).Right - (General.SafeZone).Left) / 4f;
		columnX += columnW * ((float)i + 0.5f);
		if (activeDevices.Count > i)
		{
			float textScale = 0.8f;
			float lineH = font.MeasureString("x").Y * textScale;
			float rowY = 100f;
			ControlDevice controlDevice = activeDevices[i];
			PlayerSettings playerSettings = Settings.GetInstance().GetPlayerSettings(controlDevice);
			int selectedRow = selectedEntries[i];
			if (done[i])
			{
				selectedRow = 1000;
			}
			base.SpriteBatch.DrawMetalString(controlDevice.ToString(), new Vector2(columnX, rowY), Color.AliceBlue, 0f, centered: true, textScale);
			rowY += lineH * 2f;
			rowY = drawSetting(columnX, textScale, lineH, rowY, "Rumble", !playerSettings.DisableRumble, selectedRow == 0, gameTime);
			rowY = drawSetting(columnX, textScale, lineH, rowY, "Swap Sticks", playerSettings.InvertSticks, selectedRow == 1, gameTime);
			rowY = drawSetting(columnX, textScale, lineH, rowY, "Done", null, selectedRow == 2, gameTime);
		}
		else
		{
			base.SpriteBatch.DrawMetalString("Press\nStart", new Vector2(columnX, 100f), Color.AliceBlue, 0f, centered: true, 1f);
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
		bool allDone = true;
		foreach (bool item in done)
		{
			allDone = allDone && item;
		}
		if (allDone && !exiting)
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
		int deviceIndex = activeDevices.IndexOf(device);
		if (deviceIndex != -1)
		{
			selectedEntries[deviceIndex] = MyMath.Mod(selectedEntries[deviceIndex] + direction, 3);
		}
	}

	private float drawSetting(float x, float scale, float ystep, float y, string name, bool? value, bool selected, GameTime gameTime)
	{
		Color color;
		if (selected)
		{
			float pulseAmount = 15f / font.MeasureString(name).X * scale;
			float pulseTime = (float)gameTime.TotalGameTime.TotalSeconds;
			float pulsePhase = MyMath.Mod(pulseTime / 2f, 1f);
			color = Color.AliceBlue;
			scale *= 1f + pulseAmount * brainPulsate.Evaluate(pulsePhase);
		}
		else
		{
			color = Color.Gray;
		}
		base.SpriteBatch.DrawMetalString(name, new Vector2(x, y), color, 0f, centered: true, scale);
		if (value.HasValue)
		{
			y += ystep;
			base.SpriteBatch.DrawMetalString(MenuScene.boolToGameString(value.Value), new Vector2(x, y), color, 0f, centered: true, scale);
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
