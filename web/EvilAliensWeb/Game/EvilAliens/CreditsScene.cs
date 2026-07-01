using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

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

	private bool terminated;

	public event FinishedHandler OnFinished;

	public CreditsScene(Game game)
		: base(game)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		// Web port: load unpacked web assets via WebContentManager (KNI can't read the
		// original .xnb). Kept scene-local so it can Unload() when the scene finishes.
		content = new WebContentManager((IServiceProvider)game.Services, "Content");
		texturetoload = "GFX/Menu/planet";
		castDisplayer = new CastDisplayer(base.Game);
		castDisplayer.owner = (GameComponent)(object)this;
	}

	public override void Initialize()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
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
		lines.Add("As the debris of the destroyed Fleet");
		lines.Add("Commander Drone rushes past your");
		lines.Add("cockpit, the remaining alien ships");
		lines.Add("scatter and retreat.");
		lines.Add("");
		lines.Add("The Earth is saved for now.");
		lines.Add("");
		lines.Add("But you know that your home will");
		lines.Add("never truly be safe. Not while the");
		lines.Add("aliens are allowed to fester like a");
		lines.Add("cancerous sore upon the solar system.");
		lines.Add("");
		lines.Add("The threat must be stopped. ");
		lines.Add("It is time to take the fight to them.");
		lines.Add("");
		lines.Add("To Mars!");
		texturetoload = "GFX/Credits/mars_joost";
		bg = content.Load<Texture2D>(texturetoload);
		color = Color.White;
		nextlevel = Levels.Level2;
		base.SoundManager.PlayNarration("victor_level1");
	}

	public void SetupLevel2()
	{
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		castWillBeDisplayed = false;
		lines.Clear();
		lines.Add("Having fought your way past the alien");
		lines.Add("defenses as well as Martian wildlife,");
		lines.Add("you approach the invaders' base.");
		lines.Add("");
		lines.Add("Cannons blazing, you make your way");
		lines.Add("inside.");
		lines.Add("");
		lines.Add("Your mission is clear: to find and");
		lines.Add("dispatch the alien Overmind once and");
		lines.Add("for all.");
		lines.Add("");
		lines.Add("As you enter the azure fortress");
		lines.Add("through one of the many tunnels, a");
		lines.Add("chill runs down your spine.");
		lines.Add("");
		lines.Add("It is quiet. Too quiet.");
		texturetoload = "GFX/Credits/Slawekmars2";
		bg = content.Load<Texture2D>(texturetoload);
		color = Color.White;
		nextlevel = Levels.Level3;
		base.SoundManager.PlayNarration("victor_level2");
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
			lines.Add("Chaos is already spreading");
			lines.Add("throughout the alien ranks as you");
			lines.Add("make your way to the exit.");
			lines.Add("");
			lines.Add("Without their leader to sustain them, ");
			lines.Add("the aliens' empire will crumble into ");
			lines.Add("oblivion.");
			lines.Add("");
			lines.Add("As you leave the planet's atmosphere");
			lines.Add("the steerless alien base self");
			lines.Add("destructs, engulfing you in the bright");
			lines.Add("red glow of victory.");
			lines.Add("");
			lines.Add("The game is over. ");
			lines.Add("The Earth is safe. ");
			lines.Add("Well done.");
			base.SoundManager.PlayNarration("victor_level3_hard");
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
			base.SoundManager.PlayNarration("victor_level3_normal");
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
		lines.Add("Tekno Frannansa");
		lines.Add("(www.evilsuperbrain.com)");
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
		lines.Add("Brian (Announcer)");
		lines.Add("Victor (Narrator)");
		lines.Add("voices synthesized by ElevenLabs");
		lines.Add("");
		lines.Add("IN LOVING MEMORY OF:");
		lines.Add("Microsoft Sam");
		lines.Add("our original announcer, 2008 - 2026");
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
		// Stage 10: full-screen fade in 800x600 design space (scaled by RenderScale.Matrix).
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, 800, 600), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)alpha));
	}

	protected void fadeBackBufferToBlack(int alpha)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		// Stage 10: full-screen fade in 800x600 design space (scaled by RenderScale.Matrix).
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, 800, 600), new Color((byte)0, (byte)0, (byte)0, (byte)alpha));
	}

	protected override void LoadContent()
	{
		base.LoadContent();
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
		base.Update(gameTime);
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
		// Idempotent: Terminate is reachable twice in one Update (a skip press AND
		// fadeouttimer.Finished on the same tick) and across ticks. A second call would
		// fire OnFinished again and double-add menuScene/bragScene to the component
		// collection, which KNI rejects. Guard so only the first call takes effect.
		if (terminated)
		{
			return;
		}
		terminated = true;
		base.SoundManager.StopNarration();
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
		base.GraphicsDevice.Clear(Color.Black);
		base.SpriteBatch.Draw(bg, new Rectangle(0, 0, 800, 600), color);
		float num = 0f;
		for (int i = 0; i < lines.Count; i++)
		{
			base.SpriteBatch.DrawString(font, lines[i], new Vector2(100f, textpos + num), Color.Blue, 0f, new Vector2(0f, 0f), 1f, (SpriteEffects)0, 1f);
			base.SpriteBatch.DrawString(font, lines[i], new Vector2(98f, textpos - 2f + num), Color.LightBlue, 0f, new Vector2(0f, 0f), 1f, (SpriteEffects)0, 1f);
			num += (float)font.LineSpacing;
		}
		base.Draw(gameTime);
		fadeBackBufferToWhite((int)(fadetimer.Normalized * 255f));
		fadeBackBufferToBlack((int)(255f - fadeouttimer.Normalized * 255f));
	}
}
