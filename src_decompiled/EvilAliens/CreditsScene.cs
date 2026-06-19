using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class CreditsScene : Scene
{
	public delegate void FinishedHandler(object sender, Levels nextlevel);

	private Timer fadetimer = new Timer(800f, repeating: false);

	private Timer fadeouttimer = new Timer(8000f, repeating: false);

	private SpriteFont font;

	private ContentManager content;

	private float textpos;

	private Texture2D bg;

	private string texturetoload;

	private List<string> lines = new List<string>();

	private Color color;

	private bool shutup;

	private int paragraph;

	private bool displayingcast;

	private CastDisplayer castDisplayer;

	private Texture2D blankTexture;

	private Levels nextlevel;

	private bool castWillBeDisplayed;

	public event FinishedHandler OnFinished;

	public CreditsScene(Game game)
		: base(game)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		content = new ContentManager((IServiceProvider)game.Services, "Content");
		texturetoload = "GFX/Menu/planet";
		castDisplayer = new CastDisplayer(((GameComponent)this).Game);
		castDisplayer.owner = (GameComponent)(object)this;
	}

	public override void Initialize()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)this).Initialize();
		GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				SignedInGamer current = enumerator.Current;
				switch (nextlevel)
				{
				case Levels.Level1:
					current.Presence.PresenceMode = (GamerPresenceMode)50;
					break;
				case Levels.Level2:
					current.Presence.PresenceMode = (GamerPresenceMode)32;
					break;
				case Levels.Level3:
					current.Presence.PresenceMode = (GamerPresenceMode)32;
					break;
				default:
					current.Presence.PresenceMode = (GamerPresenceMode)32;
					break;
				}
			}
		}
		finally
		{
			((IDisposable)enumerator).Dispose();
		}
		base.SoundManager.PlayMusic(Songs.SjaakSlow);
		fadetimer.Reset();
		fadetimer.Start();
		fadeouttimer.Stop();
		fadeouttimer.Reset();
		paragraph = -1;
		shutup = false;
		textpos = 650f;
		displayingcast = false;
	}

	public void SetupLevel1()
	{
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		castWillBeDisplayed = false;
		lines.Clear();
		lines.Add("As the debris of the destroyed Fleet ");
		lines.Add("Commander Drone rushes past your cockpit, ");
		lines.Add("the remaining alien ships scatter and ");
		lines.Add("retreat.");
		lines.Add("");
		lines.Add("The Earth is saved for now.");
		lines.Add("");
		lines.Add("But you know that your home will never ");
		lines.Add("truly be safe. Not while the aliens are ");
		lines.Add("allowed to fester like a cancerous sore ");
		lines.Add("upon the solar system.");
		lines.Add("");
		lines.Add("The threat must be stopped. ");
		lines.Add("It is time to take the fight to them.");
		lines.Add("");
		lines.Add("To Mars!");
		texturetoload = "GFX/Credits/mars_joost";
		bg = content.Load<Texture2D>(texturetoload);
		color = Color.White;
		nextlevel = Levels.Level2;
	}

	public void SetupLevel2()
	{
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		castWillBeDisplayed = false;
		lines.Clear();
		lines.Add("Having fought your way past the alien ");
		lines.Add("defenses as well as Martian wildlife, ");
		lines.Add("you approach the invaders' base.");
		lines.Add("");
		lines.Add("Cannons blazing, you make your way inside.");
		lines.Add("");
		lines.Add("Your mission is clear: to find and dispatch ");
		lines.Add("the alien Overmind once and for all.");
		lines.Add("");
		lines.Add("As you enter the azure fortress through ");
		lines.Add("one of the many tunnels, a chill runs ");
		lines.Add("down your spine. ");
		lines.Add("");
		lines.Add("It is quiet. Too quiet.");
		texturetoload = "GFX/Credits/Slawekmars2";
		bg = content.Load<Texture2D>(texturetoload);
		color = Color.White;
		nextlevel = Levels.Level3;
	}

	public void SetupLevel3()
	{
		//IL_0327: Unknown result type (might be due to invalid IL or missing references)
		//IL_032c: Unknown result type (might be due to invalid IL or missing references)
		lines.Clear();
		if (Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard)
		{
			castWillBeDisplayed = true;
			lines.Add("You have done it!");
			lines.Add("");
			lines.Add("The Overmind has been destroyed!");
			lines.Add("");
			lines.Add("Chaos is already spreading throughout ");
			lines.Add("the alien ranks as you make your way ");
			lines.Add("to the exit.");
			lines.Add("");
			lines.Add("Without their leader to sustain them, ");
			lines.Add("the aliens' empire will crumble into ");
			lines.Add("oblivion.");
			lines.Add("");
			lines.Add("As you leave the planet's atmosphere ");
			lines.Add("the steerless alien base self destructs, ");
			lines.Add("engulfing you in the bright red glow ");
			lines.Add("of victory.");
			lines.Add("");
			lines.Add("The game is over. ");
			lines.Add("The Earth is safe. ");
			lines.Add("Well done.");
		}
		else
		{
			castWillBeDisplayed = false;
			lines.Add("Congratulations! You are victorious! ");
			lines.Add("");
			lines.Add("The Evil Aliens' base lies in ruins. ");
			lines.Add("Their fleet is decimated. ");
			lines.Add("Their leader reduced to pulp. ");
			lines.Add("");
			lines.Add("Yet you know that it was only a ");
			lines.Add("Lieutenant that you have slain. ");
			lines.Add("The Overmind still lives. ");
			lines.Add("");
			lines.Add("You know that one day the aliens ");
			lines.Add("will be back, and it will be ");
			lines.Add("up to you to once again save the ");
			lines.Add("day.");
			lines.Add("");
			lines.Add("And it will be much HARDER this ");
			lines.Add("time...");
			lines.Add("");
			lines.Add("");
			lines.Add("");
			lines.Add("");
			lines.Add("");
			lines.Add("");
			lines.Add("");
			lines.Add("");
			SetupCredits();
		}
		texturetoload = "GFX/Menu/planet";
		bg = content.Load<Texture2D>(texturetoload);
		color = Color.Red;
		nextlevel = Levels.Level1;
	}

	private void SetupCredits()
	{
		lines.Add("CREDITS:");
		lines.Add("");
		lines.Add("PROGRAMMING AND DESIGN: ");
		lines.Add("Harald (CoamIthra) Maassen");
		lines.Add("");
		lines.Add("MUSIC:");
		lines.Add("Peter Brannan");
		lines.Add("");
		lines.Add("ADDITIONAL MUSIC:");
		lines.Add("D'r Sjaak ");
		lines.Add("Ralf Pisters ");
		lines.Add("BluntWAX");
		lines.Add("Johann Sebastian Bach");
		lines.Add("");
		lines.Add("GRAPHICS: ");
		lines.Add("Danny Holten");
		lines.Add("Sebastiaan Overdam");
		lines.Add("Rudy Rijsdijk");
		lines.Add("Alexander Yedidovich");
		lines.Add("Emma Maassen ");
		lines.Add("Joost Peters");
		lines.Add("Tekno Frannansa (www.evilsuperbrain.com)");
		lines.Add("Slawek Wojtowicz ");
		lines.Add("Tom Rutjens");
		lines.Add("");
		lines.Add("PLAYTESTING: ");
		lines.Add("Rucky Brunsman");
		lines.Add("Jan Ouwens");
		lines.Add("Matthew Doucette");
		lines.Add("Carl (BogTurtleCarl) Erikson");
		lines.Add("Andy (The ZMan) Dunn");
		lines.Add("Steve Mulligan");
		lines.Add("Byju Mubarak Saiyed");
		lines.Add("Fadeela Saiyed");
		lines.Add("Patrick J. Barrett III (& son)");
		lines.Add("Kaarel Lapimaa");
		lines.Add("Louis Lavallee");
		lines.Add("Jay Watts");
		lines.Add("UberGeekGames");
		lines.Add("Dark Omen Games");
		lines.Add("");
		lines.Add("FEATURING THE VOICE TALENT OF:");
		lines.Add("Microsoft Sam");
		lines.Add("");
		lines.Add("SPECIAL THANKS TO:");
		lines.Add("The XNA team and community ");
		lines.Add("Andy (The ZMan) Dunn");
		lines.Add("Carl (BogTurtleCarl) Erikson");
		lines.Add("Tom Claus");
		lines.Add("\"bee\" ");
		lines.Add("Greg Kuperberg ");
		lines.Add("Google ");
		lines.Add("NASA");
		lines.Add("Mom");
		lines.Add("");
		lines.Add("And you!");
	}

	protected void fadeBackBufferToWhite(int alpha)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		Viewport viewport = ((DrawableGameComponent)this).GraphicsDevice.Viewport;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, ((Viewport)(ref viewport)).Width, ((Viewport)(ref viewport)).Height), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)alpha));
	}

	protected void fadeBackBufferToBlack(int alpha)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		Viewport viewport = ((DrawableGameComponent)this).GraphicsDevice.Viewport;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, ((Viewport)(ref viewport)).Width, ((Viewport)(ref viewport)).Height), new Color((byte)0, (byte)0, (byte)0, (byte)alpha));
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
		bg = content.Load<Texture2D>(texturetoload);
		blankTexture = content.Load<Texture2D>("GFX/Menu/blank");
	}

	public override void Update(GameTime gameTime)
	{
		fadetimer.Update(gameTime);
		fadeouttimer.Update(gameTime);
		if (displayingcast && castDisplayer.done)
		{
			lines.Clear();
			SetupCredits();
			paragraph = -1;
			shutup = false;
			textpos = 650f;
			displayingcast = false;
		}
		bool flag = false;
		flag |= base.InputHandler.Pressed(MyKeys.Enter) || base.InputHandler.Pressed(MyKeys.Esc);
		for (int i = 0; i < 4; i++)
		{
			flag |= base.InputHandler.PadPressed(PadKeys.Start, i);
			flag |= base.InputHandler.PadPressed(PadKeys.Back, i);
			flag |= base.InputHandler.PadPressed(PadKeys.A, i);
			flag |= base.InputHandler.PadPressed(PadKeys.B, i);
			flag |= base.InputHandler.PadPressed(PadKeys.LTRT, i);
		}
		if (flag && !displayingcast && !castWillBeDisplayed)
		{
			Terminate();
		}
		((GameComponent)this).Update(gameTime);
		if (fadeouttimer.Finished)
		{
			Terminate();
			return;
		}
		textpos -= 0.025f * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		float num = font.LineSpacing;
		int j;
		for (j = -1; textpos + (float)(j + 1) * num <= 560f; j++)
		{
		}
		if (j >= 0 && j < lines.Count)
		{
			if (lines[j] == "CREDITS:")
			{
				shutup = true;
			}
			if (lines[j] == "Well done." && !displayingcast)
			{
				castWillBeDisplayed = false;
				displayingcast = true;
				Collection.Add((GameComponent)(object)castDisplayer);
			}
		}
		if (j >= lines.Count)
		{
			if (textpos + (float)lines.Count * num <= 400f && !fadeouttimer.Active && !displayingcast)
			{
				fadeouttimer.Start();
				fadeouttimer.Reset();
			}
		}
		else if (j >= 0 && paragraph != j && (j - 1 == -1 || lines[j - 1] == ""))
		{
			int k = j;
			string text = "";
			for (; k < lines.Count && lines[k] != ""; k++)
			{
				text += lines[k];
			}
			_ = shutup;
			paragraph = j;
		}
	}

	private void Terminate()
	{
		if (this.OnFinished != null)
		{
			this.OnFinished(this, nextlevel);
		}
		Collection.Remove((GameComponent)(object)this);
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		((DrawableGameComponent)this).GraphicsDevice.Clear(Color.Black);
		base.SpriteBatch.Draw(bg, new Rectangle(0, 0, 800, 600), color);
		float num = 0f;
		for (int i = 0; i < lines.Count; i++)
		{
			base.SpriteBatch.DrawString(font, lines[i], new Vector2(100f, textpos + num), Color.Blue, 0f, new Vector2(0f, 0f), 1f, (SpriteEffects)0, 1f);
			base.SpriteBatch.DrawString(font, lines[i], new Vector2(98f, textpos - 2f + num), Color.LightBlue, 0f, new Vector2(0f, 0f), 1f, (SpriteEffects)0, 1f);
			num += (float)font.LineSpacing;
		}
		((DrawableGameComponent)this).Draw(gameTime);
		fadeBackBufferToWhite((int)(fadetimer.Normalized * 255f));
		fadeBackBufferToBlack((int)(255f - fadeouttimer.Normalized * 255f));
	}
}
