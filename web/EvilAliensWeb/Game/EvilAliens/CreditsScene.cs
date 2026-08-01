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

	// Card 6d7a4b64: the narrator voice line no longer fires the instant the crawl is set up.
	// SetupLevelN() stashes the VO cue in pendingNarration; Initialize() starts this delay timer
	// (the scene's per-showing reset point); Update() plays the line once the timer finishes,
	// giving the player ~2.5s to settle into the new screen before the narrator speaks.
	private Timer narrationDelayTimer = new Timer(2500f, repeating: false);

	private string pendingNarration;

	private bool narrationStarted;

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

	// Card bee8f0e0: the crawl tapers like a Star Wars opening -- larger at the bottom of the
	// screen, smaller at the top, easing linearly through 1.0 at mid screen. DRAW-TIME ONLY:
	// every line keeps its nominal grid row (textpos + i*LineSpacing), so Update's scroll, its
	// line-index math, the Cast handoff and the fade timers are untouched. Override with
	// ?crawlskew=<f>; ?crawlskew=0 restores the flat 2008 crawl exactly (the Draw path
	// short-circuits back to the original two DrawString calls).
	//
	// The card asked for +-20%; the request is CLAMPED to what fits the screen and the shipped
	// text saturates it at ~+-0.08, so this constant is the ask, not the result (mid-screen
	// stays pixel-identical to the pre-card crawl either way). See EnsureCrawlGeometry.
	private const float DefaultCrawlSkew = 0.2f;

	// Nominal Y at which the taper is 1.0 (mid screen), and the half-band it eases over --
	// so y=600 (screen bottom) reads 1+skew and y=0 (screen top) reads 1-skew, clamped
	// outside. A line scrolling in from y=650 therefore enters at the same scale it has at
	// the bottom edge rather than growing without bound.
	private const float CrawlSkewMidY = 300f;

	private const float CrawlSkewHalfBand = 300f;

	// Slack left at the design-space edges when working out how much taper fits: MeasureString
	// returns advance widths, and a glyph's ink can sit a hair beyond its advance.
	private const float CrawlEdgeMargin = 4f;

	// Horizontal pivot of the taper: the crawl block's own centre, so the trapezoid is
	// symmetric about the text instead of sliding sideways off the left margin. Cached
	// because it needs a MeasureString over every line; SetupCredits appends mid-scene, so
	// the cache is invalidated on the line count it was measured at.
	private float crawlPivotX;

	// The taper actually applied, = min(requested, what fits). The crawl's widest line is
	// ~669 of the 800 design px, so the card's requested +-20% would push its right end
	// ~36px off screen and eat the last character; the amount is therefore CLAMPED to the
	// largest that keeps the widest line inside the screen (~+-0.08 with the shipped text).
	// Dynamic, off the measured text: any ?crawlskew= value is safe, it just saturates.
	private float crawlEffectiveSkew;

	private int crawlPivotLineCount = -1;

	public event FinishedHandler OnFinished;

	public CreditsScene(Game game)
		: base(game)
	{
		// Web port: load unpacked web assets via WebContentManager (KNI can't read the
		// original .xnb). Kept scene-local so it can Unload() when the scene finishes.
		content = new WebContentManager((IServiceProvider)game.Services, "Content");
		texturetoload = "GFX/Menu/planet";
		castDisplayer = new CastDisplayer(base.Game);
		castDisplayer.owner = (GameComponent)(object)this;
	}

	public override void Initialize()
	{
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
		base.SoundManager.PlayMusic(Songs.LastSignal);
		fadetimer.Reset();
		fadetimer.Start();
		fadeouttimer.Stop();
		fadeouttimer.Reset();
		paragraph = -1;
		shutup = false;
		textpos = 650f;
		displayingcast = false;
		// The Terminate() guard must be per-showing: this scene is a boot-time singleton
		// re-added after every level completion, and Initialize() is its reset point. Left
		// set, the second showing's Terminate() early-returns and the credits never hand
		// off to the menu.
		terminated = false;
		// Delay the narrator line (card 6d7a4b64). SetupLevelN() ran before this Add/Initialize
		// and already stashed pendingNarration; arm the countdown from the moment the crawl
		// becomes active so the voice starts ~2.5s in, not instantly.
		narrationStarted = false;
		narrationDelayTimer.Reset();
		narrationDelayTimer.Start();
		// Re-measure the taper pivot for this showing's text (card bee8f0e0). SetupLevelN()
		// ran before this Add/Initialize, so `lines` is already the new set -- and two
		// different setups can share a line COUNT, which is all the in-scene cache key can
		// see, so the reset has to happen here rather than relying on that key alone.
		crawlPivotLineCount = -1;
		// KNI runs LoadContent() once per component instance EVER (guarded), but this
		// scene is a boot-time singleton that Unload()s its per-scene content when removed
		// (OnComponentRemoved) and is re-added after every level completion. Re-load the
		// content textures every showing: a no-op cache hit while nothing was unloaded, a
		// fresh decode after Unload() -- otherwise the second credits roll draws disposed
		// textures. (SetupLevelN() ran before this Add/Initialize and set texturetoload.)
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
		bg = content.Load<Texture2D>(texturetoload);
		blankTexture = content.Load<Texture2D>("GFX/Menu/blank");
	}

	public void SetupLevel1()
	{
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
		pendingNarration = "victor_level1";
	}

	public void SetupLevel2()
	{
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
		pendingNarration = "victor_level2";
	}

	public void SetupLevel3()
	{
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
			pendingNarration = "victor_level3_hard";
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
			pendingNarration = "victor_level3_normal";
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
		lines.Add("ADDITIONAL PROGRAMMING:");
		lines.Add("Dario Amodei");
		lines.Add("");
		lines.Add("MUSIC:");
		lines.Add("Peter Brannan");
		lines.Add("");
		lines.Add("ADDITIONAL MUSIC:");
		lines.Add("D'r Sjaak ");
		lines.Add("Ralf Pisters ");
		lines.Add("BluntWAX");
		lines.Add("Johann Sebastian Bach");
		lines.Add("Mikey Shulman");
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
		lines.Add("ADDITIONAL GRAPHICS:");
		lines.Add("Sam Altman");
		lines.Add("Demis Hassabis");
		lines.Add("Andromeda photo: Adam Evans");
		lines.Add("(CC BY 2.0)");
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
		lines.Add("The KNI engine");
		lines.Add("Nikos Kastellanos (nkast)");
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
		// Stage 10: full-screen fade in 800x600 design space (scaled by RenderScale.Matrix).
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, 800, 600), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)alpha));
	}

	protected void fadeBackBufferToBlack(int alpha)
	{
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
		narrationDelayTimer.Update(gameTime);
		// Fire the stashed narrator line once the delay elapses (card 6d7a4b64). Guarded by
		// !terminated so a skip during the delay window (Terminate() -> StopNarration) cancels
		// the line rather than letting it start afterwards; narrationStarted keeps it one-shot.
		if (!narrationStarted && !terminated && narrationDelayTimer.Finished && pendingNarration != null)
		{
			narrationStarted = true;
			base.SoundManager.PlayNarration(pendingNarration);
		}
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
		// ?crawlpos=<designY> parks the crawl at a chosen scroll position instead of scrolling
		// it (card bee8f0e0): the taper is a function of each line's Y, so a timed screenshot
		// of a moving crawl proves nothing. Everything below still runs off the parked value,
		// so parking past the end of the text will start the fade-out / Cast handoff exactly
		// as scrolling there would -- park inside the crawl to hold a frame.
		if (DebugFlags.CrawlPos.HasValue)
		{
			textpos = DebugFlags.CrawlPos.Value;
		}
		else
		{
			textpos -= 0.025f * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		}
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

	// Free this scene's per-scene WebContentManager when it leaves the component
	// collection. CreditsScene is a boot-time singleton re-added after every level
	// completion; without this its credit backgrounds + font + blank stayed resident for
	// the whole session (WebContentManager.Unload now actually frees them). Fires once per
	// removal, AFTER the scene's final Draw -- ComponentBin defers the remove to its next
	// flush, which runs before Draw -- so no disposed texture is ever drawn; Initialize()
	// re-loads them on the next showing. This is now the LAST component doing the
	// own-manager + Unload-on-removal dance -- HelpText/InstructionsMenu were converted to the
	// shared manager in card 4d47c5ba so their art could be warmed once at boot. Credits keeps
	// its own deliberately: its backgrounds are a large per-showing set, not two fixed sheets,
	// so paying the decode again beats holding them for the session.
	public void Unload()
	{
		content.Unload();
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			Unload();
		}
	}

	// The taper amount ASKED for: ?crawlskew= wins, else the baked default. What is actually
	// drawn is this clamped to what fits -- see EnsureCrawlGeometry. Negatives are refused at
	// parse time.
	private static float RequestedCrawlSkew => DebugFlags.CrawlSkew ?? DefaultCrawlSkew;

	// Uniform per-line scale at a nominal design Y: 1+skew at the screen bottom, 1-skew at
	// the top, 1.0 at mid screen, clamped outside the band.
	private static float CrawlScaleAt(float y, float skew)
	{
		return 1f + skew * MathHelper.Clamp((y - CrawlSkewMidY) / CrawlSkewHalfBand, -1f, 1f);
	}

	// Draw one crawl line scaled about (anchorX, anchorY) instead of its own top-left corner:
	// the world position works out as `anchor - (anchor - pos) * scale`, which is `pos` exactly
	// at scale 1 and keeps the block centred (and the shadow's 2px offset proportional) at any
	// other scale. `pos` is the position the un-skewed 2008 code drew at.
	private void DrawCrawlLine(string text, Vector2 pos, float anchorX, float anchorY, float scale, Color lineColor)
	{
		base.SpriteBatch.DrawString(font, text, new Vector2(anchorX, anchorY), lineColor, 0f,
			new Vector2(anchorX - pos.X, anchorY - pos.Y), scale, (SpriteEffects)0, 1f);
	}

	// Measure the taper's geometry for the current line set: the pivot (centre of the widest
	// line, so the trapezoid is symmetric about the text block) and how much taper actually
	// fits. Recomputed only when the line set changes (SetupCredits appends mid-scene).
	private void EnsureCrawlGeometry(float requestedSkew)
	{
		if (crawlPivotLineCount == lines.Count)
		{
			return;
		}
		crawlPivotLineCount = lines.Count;
		float widest = 0f;
		for (int i = 0; i < lines.Count; i++)
		{
			widest = Math.Max(widest, font.MeasureString(lines[i]).X);
		}
		crawlPivotX = 100f + widest / 2f;
		// Scaling about the block centre moves the right end out by half the added width, so
		// the widest line stays on screen while 100 + (w/2)*(1 + s) <= 800 - margin. Solve for
		// the largest s and clamp the request to it: over-asking saturates instead of pushing
		// text off the edge (the shipped text tops out around +-0.08).
		// (the -2 turns the solved max SCALE into a skew: skew = maxScale - 1)
		float fits = ((widest > 0f) ? (2f * (700f - CrawlEdgeMargin) / widest - 2f) : requestedSkew);
		crawlEffectiveSkew = Math.Max(0f, Math.Min(requestedSkew, fits));
		// The widest line at the largest scale is the whole crawl's horizontal extent -- the
		// one thing a screenshot cannot judge and the one way this can fail silently (text
		// pushed off the 800px design width). Report requested AND effective so the clamp is
		// visible rather than a mystery; a probe asserts both.
		float maxScale = 1f + crawlEffectiveSkew;
		float left = crawlPivotX - (crawlPivotX - 100f) * maxScale;
		float right = left + widest * maxScale;
		// fit= is the invariant a probe can assert without pinning font metrics: whatever the
		// text and the requested amount, the widest line stays inside the 800px design width.
		string fit = ((left >= 0f && right <= 800f) ? "ok" : "OVERFLOW");
		Console.WriteLine($"[crawl] skew={requestedSkew:0.000} effective={crawlEffectiveSkew:0.000} fits={Math.Max(0f, fits):0.000} fit={fit} pivot={crawlPivotX:0.0} lines={lines.Count} maxline={widest:0.0} span=[{left:0.0},{right:0.0}]");
	}

	public override void Draw(GameTime gameTime)
	{
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.GraphicsDevice.Clear(Color.Black);
		base.SpriteBatch.Draw(bg, new Rectangle(0, 0, 800, 600), color);
		float half = (float)font.LineSpacing * 0.5f;
		float skew = 0f;
		if (RequestedCrawlSkew > 0f)
		{
			EnsureCrawlGeometry(RequestedCrawlSkew);
			skew = crawlEffectiveSkew;
		}
		float num = 0f;
		for (int i = 0; i < lines.Count; i++)
		{
			if (skew > 0f)
			{
				// Anchor on the line's own grid row centre so it grows symmetrically into the
				// gap above and below rather than drifting off its row (LineSpacing 45 over
				// ~30px of ink leaves headroom for the +20% end).
				float anchorY = textpos + num + half;
				float scale = CrawlScaleAt(anchorY, skew);
				DrawCrawlLine(lines[i], new Vector2(100f, textpos + num), crawlPivotX, anchorY, scale, Color.Blue);
				DrawCrawlLine(lines[i], new Vector2(98f, textpos - 2f + num), crawlPivotX, anchorY, scale, Color.LightBlue);
			}
			else
			{
				base.SpriteBatch.DrawString(font, lines[i], new Vector2(100f, textpos + num), Color.Blue, 0f, new Vector2(0f, 0f), 1f, (SpriteEffects)0, 1f);
				base.SpriteBatch.DrawString(font, lines[i], new Vector2(98f, textpos - 2f + num), Color.LightBlue, 0f, new Vector2(0f, 0f), 1f, (SpriteEffects)0, 1f);
			}
			num += (float)font.LineSpacing;
		}
		base.Draw(gameTime);
		fadeBackBufferToWhite((int)(fadetimer.Normalized * 255f));
		fadeBackBufferToBlack((int)(255f - fadeouttimer.Normalized * 255f));
	}
}
