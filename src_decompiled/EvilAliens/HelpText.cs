using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class HelpText : DrawableGameComponent, IComponentWatcher
{
	public enum Displays
	{
		Lead,
		Keyboard,
		Gamepad,
		Powerups,
		Combo,
		End
	}

	private enum States
	{
		waiting,
		displaying
	}

	private const float FIRST_WAIT_DURATION = 5000f;

	private const float TEXT_DURATION = 12000f;

	private const float WAIT_DURATION = 12000f;

	private const float LAST_WAIT_DURATION = 30000f;

	private Timer stateTimer = new Timer(1f, repeating: false);

	private ContentManager localContent;

	private Texture2D keyboardlayout;

	private Texture2D controllerlayout;

	private Texture2D blankTexture;

	private Texture2D powerupbubble;

	private float visibility;

	private bool fadingin;

	private Displays currentlyDisplaying;

	private States state;

	private ComponentBin collection;

	private ContentManager content;

	private SpriteFont font;

	private InputHandler inputHandler;

	private SoundManager sound;

	private SpriteBatchWrapper spriteBatch;

	public HelpText(Game game)
		: base(game)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		((DrawableGameComponent)this).DrawOrder = 2000;
		localContent = new ContentManager((IServiceProvider)((GameComponent)this).Game.Services, "Content");
	}

	public void Unload()
	{
		localContent.Unload();
	}

	public override void Initialize()
	{
		state = States.waiting;
		stateTimer.Duration = 5000f;
		stateTimer.Reset();
		stateTimer.Start();
		currentlyDisplaying = Displays.Lead;
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		inputHandler = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
		((DrawableGameComponent)this).Initialize();
		((DrawableGameComponent)this).LoadContent();
	}

	public void SetDisplay(Displays display)
	{
		currentlyDisplaying = display;
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		keyboardlayout = localContent.Load<Texture2D>("GFX/Help/Controls Keyboard");
		controllerlayout = localContent.Load<Texture2D>("GFX/Help/Controls Joypad");
		blankTexture = content.Load<Texture2D>("GFX/Menu/blank");
		powerupbubble = content.Load<Texture2D>("GFX/Sprites/powerupbw");
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Update(GameTime gameTime)
	{
		((GameComponent)this).Update(gameTime);
		stateTimer.Update(gameTime);
		if (fadingin)
		{
			if (visibility < 1f)
			{
				visibility += (float)gameTime.ElapsedGameTime.TotalSeconds;
				if (visibility > 1f)
				{
					visibility = 1f;
				}
			}
		}
		else if (visibility > 0f)
		{
			visibility -= (float)gameTime.ElapsedGameTime.TotalSeconds;
			if (visibility < 0f)
			{
				visibility = 0f;
			}
		}
		switch (state)
		{
		case States.waiting:
			fadingin = false;
			if (stateTimer.Finished)
			{
				stateTimer.Duration = 12000f;
				stateTimer.Reset();
				stateTimer.Start();
				currentlyDisplaying++;
				if (currentlyDisplaying == Displays.End)
				{
					currentlyDisplaying = Displays.Keyboard;
				}
				state = States.displaying;
				if (currentlyDisplaying == Displays.Keyboard)
				{
					currentlyDisplaying++;
				}
			}
			break;
		case States.displaying:
			fadingin = true;
			if (stateTimer.Finished)
			{
				stateTimer.Duration = 12000f;
				if (currentlyDisplaying == Displays.Combo)
				{
					stateTimer.Duration = 30000f;
				}
				stateTimer.Reset();
				stateTimer.Start();
				state = States.waiting;
			}
			break;
		}
	}

	protected void fadeBackBufferToBlack(int alpha)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		Viewport viewport = ((DrawableGameComponent)this).GraphicsDevice.Viewport;
		spriteBatch.Draw(blankTexture, new Rectangle(0, 0, ((Viewport)(ref viewport)).Width, ((Viewport)(ref viewport)).Height), new Color((byte)0, (byte)0, (byte)0, (byte)alpha));
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Unknown result type (might be due to invalid IL or missing references)
		//IL_0243: Unknown result type (might be due to invalid IL or missing references)
		//IL_026c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Unknown result type (might be due to invalid IL or missing references)
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_028a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		if (visibility > 0f)
		{
			fadeBackBufferToBlack((byte)(visibility * 200f));
			switch (currentlyDisplaying)
			{
			case Displays.Keyboard:
				spriteBatch.Draw(keyboardlayout, Vector2.Zero, new Color(new Vector4(1f, 1f, 1f, visibility)));
				spriteBatch.Flush();
				break;
			case Displays.Gamepad:
				spriteBatch.Draw(controllerlayout, Vector2.Zero, new Color(new Vector4(1f, 1f, 1f, visibility)));
				spriteBatch.Flush();
				break;
			case Displays.Powerups:
			{
				Color color2 = default(Color);
				((Color)(ref color2))._002Ector(new Vector4(0.37f, 0.63f, 1f, visibility));
				spriteBatch.Draw(powerupbubble, new Vector2(400f, 100f), 0f, 2f, center: true, color2);
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
			case Displays.Combo:
			{
				Color color = default(Color);
				((Color)(ref color))._002Ector(new Vector4(0.37f, 0.63f, 1f, visibility));
				string text = "Combos";
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
		((DrawableGameComponent)this).Draw(gameTime);
	}

	private void ExplainPowerup(Powerup.PowerupType powerupType, float y, string p)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		Color color = default(Color);
		((Color)(ref color))._002Ector(new Vector4(0.37f, 0.63f, 1f, visibility));
		SpriteBatchWrapper spriteBatchWrapper = spriteBatch;
		string text = Powerup.PowerUpString(powerupType);
		Vector2 position = new Vector2(80f, y);
		Color val = Powerup.PowerUpColor(powerupType);
		spriteBatchWrapper.DrawString(text, position, new Color(new Vector4(((Color)(ref val)).ToVector3(), visibility)), 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
		spriteBatch.DrawString(p, new Vector2(120f, y), color, 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
	}

	internal void Reset()
	{
		currentlyDisplaying = Displays.Keyboard;
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			Unload();
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
