using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

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
		base.DrawOrder = 2000;
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
		base.Initialize();
		base.LoadContent();
	}

	public void SetDisplay(Displays display)
	{
		currentlyDisplaying = display;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		// The two control diagrams come from the SHARED content manager (card 4d47c5ba).
		// They used to live in a private WebContentManager this component Unload()ed on
		// removal, so every attract cycle re-decoded 2x 1548x1188 -- and nothing could warm
		// them, since a warm populates the shared cache and WebContentManager shares none.
		// Shared, they are decoded once per session by Game1.QueueIdleWarm and this is a
		// cache hit; nothing disposes them, so the defensive re-load in Initialize that
		// guarded against the Unload is gone with it.
		keyboardlayout = content.Load<Texture2D>("GFX/Help/Controls_Keyboard");
		controllerlayout = content.Load<Texture2D>("GFX/Help/Controls_Joypad");
		blankTexture = content.Load<Texture2D>("GFX/Menu/blank");
		powerupbubble = content.Load<Texture2D>("GFX/Sprites/powerupbw");
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
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
				// Web port (Stage 9): the Xbox build skipped the Keyboard layout (joypad
				// only). On the web, cycle the keyboard controls slide in too.
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
		// Stage 10: full-screen fade in 800x600 design space (scaled by RenderScale.Matrix).
		spriteBatch.Draw(blankTexture, new Rectangle(0, 0, 800, 600), new Color((byte)0, (byte)0, (byte)0, (byte)alpha));
	}

	public override void Draw(GameTime gameTime)
	{
		if (visibility > 0f)
		{
			fadeBackBufferToBlack((byte)(visibility * 200f));
			switch (currentlyDisplaying)
			{
			case Displays.Keyboard:
				spriteBatch.Draw(keyboardlayout, Vector2.Zero, 0f, 800f / (float)keyboardlayout.LogicalWidth(), center: false, new Color(new Vector4(1f, 1f, 1f, visibility)));
				spriteBatch.Flush();
				break;
			case Displays.Gamepad:
				spriteBatch.Draw(controllerlayout, Vector2.Zero, 0f, 800f / (float)controllerlayout.LogicalWidth(), center: false, new Color(new Vector4(1f, 1f, 1f, visibility)));
				spriteBatch.Flush();
				break;
			case Displays.Powerups:
			{
				Color color2 = new Color(new Vector4(0.37f, 0.63f, 1f, visibility));
				spriteBatch.Draw(powerupbubble, new Vector2(400f, 100f), 0f, 2f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/powerupbw", powerupbubble.LogicalWidth()), center: true, color2);
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
				Color color = new Color(new Vector4(0.37f, 0.63f, 1f, visibility));
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
		base.Draw(gameTime);
	}

	private void ExplainPowerup(Powerup.PowerupType powerupType, float y, string p)
	{
		Color color = new Color(new Vector4(0.37f, 0.63f, 1f, visibility));
		SpriteBatchWrapper spriteBatchWrapper = spriteBatch;
		string text = Powerup.PowerUpString(powerupType);
		// The powerup label is left-aligned at x=80 and the description starts at x=120.
		// Single-char labels (B/O/F/R/2) fit the gap, but "1up" is 3 glyphs wide and
		// overruns into the description, so nudge that wider label left to clear it.
		float labelX = (powerupType == Powerup.PowerupType.OneUp) ? 60f : 80f;
		Vector2 position = new Vector2(labelX, y);
		Color val = Powerup.PowerUpColor(powerupType);
		spriteBatchWrapper.DrawString(text, position, new Color(new Vector4((val).ToVector3(), visibility)), 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
		spriteBatch.DrawString(p, new Vector2(120f, y), color, 0f, Vector2.Zero, 0.8f, (SpriteEffects)0, 0f);
		spriteBatch.Flush();
	}

	internal void Reset()
	{
		currentlyDisplaying = Displays.Keyboard;
	}

	// Nothing to release on removal since card 4d47c5ba: the two control diagrams moved to
	// the shared content manager, which this component does not own and must never Unload.
	// The hook stays because IComponentWatcher requires it and this is the seam a future
	// per-instance resource would be freed from.
	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
