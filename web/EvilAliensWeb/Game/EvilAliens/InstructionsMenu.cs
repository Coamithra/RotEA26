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
		base.DrawOrder = 2000;
	}

	public override void Initialize()
	{
		// base.Initialize() runs the LoadContent OVERRIDE; the bare base.LoadContent() that used
		// to follow was a no-op -- see the matching note in HelpText.Initialize.
		base.Initialize();
		currentlyDisplaying = HelpText.Displays.Lead;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		// Shared content manager, not a private one (card 4d47c5ba) -- see the matching note
		// in HelpText.LoadContent. Each GameScene owns an InstructionsMenu, so the old
		// private managers also meant one COPY of the pair per level opened, each re-decoded
		// on every pause -> Instructions.
		keyboardlayout = contentManager.Load<Texture2D>("GFX/Help/Controls_Keyboard");
		controllerlayout = contentManager.Load<Texture2D>("GFX/Help/Controls_Joypad");
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
		bool backPressed = false;
		backPressed |= input.Pressed(MyKeys.Esc);
		for (int i = 0; i < 4; i++)
		{
			backPressed |= input.PadPressed(PadKeys.Back, i);
			backPressed |= input.PadPressed(PadKeys.B, i);
		}
		bool nextPressed = false;
		nextPressed |= input.Pressed(MyKeys.Enter);
		nextPressed |= input.Pressed(MyKeys.Right);
		for (int j = 0; j < 4; j++)
		{
			nextPressed |= input.PadPressed(PadKeys.Start, j);
			nextPressed |= input.PadPressed(PadKeys.A, j);
			nextPressed |= input.PadPressed(PadKeys.RT, j);
		}
		bool prevPressed = false;
		prevPressed |= input.Pressed(MyKeys.Left);
		for (int k = 0; k < 4; k++)
		{
			prevPressed |= input.PadPressed(PadKeys.LT, k);
		}
		if (backPressed && this.OnExit != null)
		{
			this.OnExit(this);
		}
		if (nextPressed)
		{
			displayNext();
		}
		if (prevPressed)
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
		// primary input, so show "Controls (Keyboard)" (Controls_Keyboard.png) too.
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
		base.Draw(gameTime);
		switch (currentlyDisplaying)
		{
		case HelpText.Displays.Keyboard:
			spriteBatch.Draw(keyboardlayout, Vector2.Zero, 0f, 800f / (float)keyboardlayout.LogicalWidth(), center: false, new Color(new Vector4(1f, 1f, 1f, 1f)));
			spriteBatch.Flush();
			break;
		case HelpText.Displays.Gamepad:
			spriteBatch.Draw(controllerlayout, Vector2.Zero, 0f, 800f / (float)controllerlayout.LogicalWidth(), center: false, new Color(new Vector4(1f, 1f, 1f, 1f)));
			spriteBatch.Flush();
			break;
		case HelpText.Displays.Powerups:
		{
			Color color2 = new Color(new Vector4(0.37f, 0.63f, 1f, 1f));
			spriteBatch.Draw(powerupbubble, new Vector2(400f, 100f), 0f, 2f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/powerupbw", powerupbubble.LogicalWidth()), center: true, color2);
			spriteBatch.Flush();
			string heading = "Enhancements";
			spriteBatch.DrawMetalString(font, heading, new Vector2(400f, 180f), color2, 0f, font.MeasureString(heading) / 2f, 1.5f);
			spriteBatch.Flush();
			float rowY = 220f;
			float rowStep = 40f;
			for (int j = 0; j < 6; j++)
			{
				switch (j)
				{
				case 0:
					ExplainPowerup(Powerup.PowerupType.Blast, rowY, "Bomb");
					break;
				case 1:
					ExplainPowerup(Powerup.PowerupType.FirePower, rowY, "Increased rate of fire");
					break;
				case 2:
					ExplainPowerup(Powerup.PowerupType.Range, rowY, "Increased range");
					break;
				case 3:
					ExplainPowerup(Powerup.PowerupType.Option, rowY, "Shield");
					break;
				case 4:
					ExplainPowerup(Powerup.PowerupType.Linker, rowY, "(Multiplayer) Enables docking");
					break;
				case 5:
					ExplainPowerup(Powerup.PowerupType.OneUp, rowY, "Extra life");
					break;
				}
				rowY += rowStep;
			}
			break;
		}
		case HelpText.Displays.Combo:
		{
			Color color = new Color(new Vector4(0.37f, 0.63f, 1f, 1f));
			string line = "Power Up";
			spriteBatch.DrawMetalString(font, line, new Vector2(400f, 100f), color, 0f, font.MeasureString(line) / 2f, 1.5f);
			spriteBatch.Flush();
			line = "Hit enemies to Power Up your current Enhancement.";
			spriteBatch.DrawString(font, line, new Vector2(400f, 140f), color, 0f, new Vector2((font.MeasureString(line) / 2f).X, 0f), 0.8f, (SpriteEffects)0, 0f);
			spriteBatch.Flush();
			float rowY = 220f;
			float rowStep = 40f;
			for (int i = 0; i < 6; i++)
			{
				switch (i)
				{
				case 0:
					ExplainPowerup(Powerup.PowerupType.Blast, rowY, "Larger bombs");
					break;
				case 1:
					ExplainPowerup(Powerup.PowerupType.FirePower, rowY, "Exploding bullets");
					break;
				case 2:
					ExplainPowerup(Powerup.PowerupType.Range, rowY, "Bouncing bullets");
					break;
				case 3:
					ExplainPowerup(Powerup.PowerupType.Option, rowY, "Faster shields");
					break;
				case 4:
					ExplainPowerup(Powerup.PowerupType.Linker, rowY, "(Multiplayer) Faster respawn");
					break;
				case 5:
					ExplainPowerup(Powerup.PowerupType.OneUp, rowY, "?");
					break;
				}
				rowY += rowStep;
			}
			break;
		}
		}
	}

	private void ExplainPowerup(Powerup.PowerupType powerupType, float y, string p)
	{
		Color color = new Color(new Vector4(0.37f, 0.63f, 1f, 1f));
		SpriteBatchWrapper spriteBatchWrapper = spriteBatch;
		string name = Powerup.PowerUpString(powerupType);
		Vector2 position = new Vector2(80f, y);
		Color tint = Powerup.PowerUpColor(powerupType);
		spriteBatchWrapper.DrawString(name, position, new Color(new Vector4((tint).ToVector3(), 1f)), 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
		spriteBatch.DrawString(p, new Vector2(120f, y), color, 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
	}
}
