using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class InstructionsMenu : DrawableGameComponent
{
	public delegate void ExitEvent(object sender);

	private List<Texture2D> instructionTextures = new List<Texture2D>();

	private ContentManager localContent;

	private Texture2D keyboardlayout;

	private Texture2D controllerlayout;

	private Texture2D blankTexture;

	private Texture2D powerupbubble;

	private SpriteFont font;

	private SpriteBatchWrapper spriteBatch;

	private HelpText.Displays currentlyDisplaying;

	private InputHandler input;

	public event ExitEvent OnExit;

	public InstructionsMenu(Game game)
		: base(game)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		// Web port: load unpacked web assets via WebContentManager (KNI can't read .xnb).
		localContent = new WebContentManager((IServiceProvider)base.Game.Services, "Content");
		base.DrawOrder = 2000;
	}

	public override void Initialize()
	{
		base.Initialize();
		currentlyDisplaying = HelpText.Displays.Lead;
		base.LoadContent();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		keyboardlayout = localContent.Load<Texture2D>("GFX/Help/Controls Keyboard");
		controllerlayout = localContent.Load<Texture2D>("GFX/Help/Controls Joypad");
		blankTexture = contentManager.Load<Texture2D>("GFX/Menu/blank");
		powerupbubble = contentManager.Load<Texture2D>("GFX/Sprites/powerupbw");
		font = contentManager.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		while (currentlyDisplaying == HelpText.Displays.Lead || currentlyDisplaying == HelpText.Displays.End)
		{
			displayNext();
		}
		bool flag = false;
		flag |= input.Pressed(MyKeys.Esc);
		for (int i = 0; i < 4; i++)
		{
			flag |= input.PadPressed(PadKeys.Back, i);
			flag |= input.PadPressed(PadKeys.B, i);
		}
		bool flag2 = false;
		flag2 |= input.Pressed(MyKeys.Enter);
		flag2 |= input.Pressed(MyKeys.Right);
		for (int j = 0; j < 4; j++)
		{
			flag2 |= input.PadPressed(PadKeys.Start, j);
			flag2 |= input.PadPressed(PadKeys.A, j);
			flag2 |= input.PadPressed(PadKeys.RT, j);
		}
		bool flag3 = false;
		flag3 |= input.Pressed(MyKeys.Left);
		for (int k = 0; k < 4; k++)
		{
			flag3 |= input.PadPressed(PadKeys.LT, k);
		}
		if (flag && this.OnExit != null)
		{
			this.OnExit(this);
		}
		if (flag2)
		{
			displayNext();
		}
		if (flag3)
		{
			displayPrevious();
		}
	}

	private void displayNext()
	{
		currentlyDisplaying++;
		if (currentlyDisplaying >= HelpText.Displays.End)
		{
			currentlyDisplaying = HelpText.Displays.Lead;
		}
		// Web port (Stage 9): the Xbox build skipped Displays.Keyboard here so the
		// controls screen only ever showed the joypad. On the web the keyboard IS the
		// primary input, so show "Controls (Keyboard)" (Controls Keyboard.png) too.
		if (currentlyDisplaying == HelpText.Displays.Lead)
		{
			displayNext();
		}
	}

	private void displayPrevious()
	{
		currentlyDisplaying--;
		if (currentlyDisplaying <= HelpText.Displays.Lead)
		{
			currentlyDisplaying = HelpText.Displays.End;
		}
		// Web port (Stage 9): keep the keyboard layout in the cycle (see displayNext).
		if (currentlyDisplaying == HelpText.Displays.End)
		{
			displayPrevious();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_023c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_0250: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_029e: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c6: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		switch (currentlyDisplaying)
		{
		case HelpText.Displays.Keyboard:
			spriteBatch.Draw(keyboardlayout, Vector2.Zero, 0f, 800f / (float)keyboardlayout.Width, center: false, new Color(new Vector4(1f, 1f, 1f, 1f)));
			spriteBatch.Flush();
			break;
		case HelpText.Displays.Gamepad:
			spriteBatch.Draw(controllerlayout, Vector2.Zero, 0f, 800f / (float)controllerlayout.Width, center: false, new Color(new Vector4(1f, 1f, 1f, 1f)));
			spriteBatch.Flush();
			break;
		case HelpText.Displays.Powerups:
		{
			Color color2 = default(Color);
			(color2) = new Color(new Vector4(0.37f, 0.63f, 1f, 1f));
			spriteBatch.Draw(powerupbubble, new Vector2(400f, 100f), 0f, 2f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/powerupbw", powerupbubble.Width), center: true, color2);
			spriteBatch.Flush();
			string text2 = "Enhancements";
			spriteBatch.DrawString(font, text2, new Vector2(400f, 180f), color2, 0f, font.MeasureString(text2) / 2f, 1.5f, (SpriteEffects)0, 0f);
			spriteBatch.Flush();
			float num3 = 220f;
			float num4 = 40f;
			for (int j = 0; j < 6; j++)
			{
				switch (j)
				{
				case 0:
					ExplainPowerup(Powerup.PowerupType.Blast, num3, "Bomb");
					break;
				case 1:
					ExplainPowerup(Powerup.PowerupType.FirePower, num3, "Increased rate of fire");
					break;
				case 2:
					ExplainPowerup(Powerup.PowerupType.Range, num3, "Increased range");
					break;
				case 3:
					ExplainPowerup(Powerup.PowerupType.Option, num3, "Shield");
					break;
				case 4:
					ExplainPowerup(Powerup.PowerupType.Linker, num3, "(Multiplayer) Enables docking");
					break;
				case 5:
					ExplainPowerup(Powerup.PowerupType.OneUp, num3, "Extra life");
					break;
				}
				num3 += num4;
			}
			break;
		}
		case HelpText.Displays.Combo:
		{
			Color color = default(Color);
			(color) = new Color(new Vector4(0.37f, 0.63f, 1f, 1f));
			string text = "Power Up";
			spriteBatch.DrawString(font, text, new Vector2(400f, 100f), color, 0f, font.MeasureString(text) / 2f, 1.5f, (SpriteEffects)0, 0f);
			spriteBatch.Flush();
			text = "Hit enemies to Power Up your current Enhancement.";
			spriteBatch.DrawString(font, text, new Vector2(400f, 140f), color, 0f, new Vector2((font.MeasureString(text) / 2f).X, 0f), 0.8f, (SpriteEffects)0, 0f);
			spriteBatch.Flush();
			float num = 220f;
			float num2 = 40f;
			for (int i = 0; i < 6; i++)
			{
				switch (i)
				{
				case 0:
					ExplainPowerup(Powerup.PowerupType.Blast, num, "Larger bombs");
					break;
				case 1:
					ExplainPowerup(Powerup.PowerupType.FirePower, num, "Exploding bullets");
					break;
				case 2:
					ExplainPowerup(Powerup.PowerupType.Range, num, "Bouncing bullets");
					break;
				case 3:
					ExplainPowerup(Powerup.PowerupType.Option, num, "Faster shields");
					break;
				case 4:
					ExplainPowerup(Powerup.PowerupType.Linker, num, "(Multiplayer) Faster respawn");
					break;
				case 5:
					ExplainPowerup(Powerup.PowerupType.OneUp, num, "?");
					break;
				}
				num += num2;
			}
			break;
		}
		}
	}

	private void ExplainPowerup(Powerup.PowerupType powerupType, float y, string p)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		Color color = default(Color);
		(color) = new Color(new Vector4(0.37f, 0.63f, 1f, 1f));
		SpriteBatchWrapper spriteBatchWrapper = spriteBatch;
		string text = Powerup.PowerUpString(powerupType);
		Vector2 position = new Vector2(80f, y);
		Color val = Powerup.PowerUpColor(powerupType);
		spriteBatchWrapper.DrawString(text, position, new Color(new Vector4((val).ToVector3(), 1f)), 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
		spriteBatch.DrawString(p, new Vector2(120f, y), color, 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
	}

	internal void Unload()
	{
		localContent.Unload();
	}
}
