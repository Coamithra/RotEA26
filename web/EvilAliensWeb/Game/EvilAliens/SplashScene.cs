using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class SplashScene : Scene
{
	public delegate void FinishedHandler(object sender);

	private TimeSpan stateTimer = TimeSpan.Zero;

	private ContentManager localContent;

	private Texture2D currentSplash;

	private bool displaySplash;

	private Texture2D blankTexture;

	private int currentTextureNumber;

	private int currentFade;

	private SplashSceneState state;

	private List<Texture2D> textures = new List<Texture2D>();

	private List<string> texturenames = new List<string>();

	private int fadetime = 200;

	private int pre = 800;

	private int showtime = 2250;

	private int pausetime = 1500;

	// --- channel-flip ("change the channel") transition on one chosen splash ---
	// The flip splash holds its OLD image for ~75% of a normal splash dwell, then a TV
	// glitch CROSSFADES it into one of the revenged images: the old distorts and
	// dissolves while the new emerges distorted and settles crisp (no vertical scroll;
	// see channelflip.fx). The normal show/pause/fade dwell then runs on the REVEAL
	// (the retime: the wait time lands after the effect). Drawn crisp into the unified
	// scene via SpriteBatchWrapper.DrawEffect + channelflip.fx.
	private Effect channelFlip;

	private int flipIndex = -1;

	private string flipRevengedName, flipPureName, flipGlassesName;

	private Texture2D flipRevenged, flipPure, flipGlasses;

	private Texture2D chosenNew;

	private Vector4 chosenNewRect;

	private bool variantPicked;

	private readonly Random rng = new Random();

	private double holdMs;                  // old image visible before the flip fires (= showtime * HOLD_FRAC)

	private const double HOLD_FRAC = 0.55;  // hold the old "I made this" ~55% of a normal splash dwell before the glitch

	private const double FLIP_MS = 650.0;   // glitch + crossfade duration

	private double effShowtime;             // showtime + (flip ? hold+FLIP : text ? textShowtime : 0)

	// --- text-only splash (drawn with the menu font, not a texture) ---
	// One entry may be a text splash: its `currentLines` reveal stanza-by-stanza on a
	// comedic-beat timer, centred and auto-fit to the 800x600 design frame. The overall
	// in/out still rides the shared fade-to-black overlay; each stanza adds its own
	// fade-in alpha on top.
	private SpriteFont textFont;

	private int textIndex = -1;

	private List<string[]> splashTexts = new List<string[]>();

	private string[] currentLines;

	private double textShowtime;

	private double[] textRevealAt;  // absolute reveal time per stanza (length-relative gaps)

	private const double TEXT_FIRST_MS = 700.0;    // first stanza appears

	// Reading-time gap between stanza reveals: a base beat + per-character dwell, so the
	// pause BEFORE the next line scales with the length of the line currently showing —
	// a long line lingers long enough to read, a short quip flicks past.
	private const double TEXT_GAP_BASE_MS = 550.0;   // minimum beat between reveals

	private const double TEXT_GAP_PER_CHAR_MS = 34.0; // extra dwell per character of the line just shown

	private const double TEXT_DRAMATIC_BEAT_MS = 1100.0; // extra hang after an ellipsis-led setup line, for effect

	private const double TEXT_STANZA_FADE_MS = 380.0; // each stanza's fade-in

	private const double TEXT_HOLD_MS = 2600.0;    // dwell after the last stanza lands

	public event FinishedHandler OnFinished;

	public SplashScene(Game game)
		: base(game)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		// Web port: load the unpacked web assets (PNG/font) rather than the original
		// .xnb, which KNI can't read (see Game1's content wiring). The scene keeps its
		// own manager so it can Unload() the splash textures when it finishes.
		localContent = new WebContentManager((IServiceProvider)game.Services, "Content");
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			base.UnloadContent();
		}
	}

	public void SetTimers(int apre, int ashowtime, int apausetime, int afadetime)
	{
		pre = apre;
		showtime = ashowtime;
		pausetime = apausetime;
		fadetime = afadetime;
	}

	// Mark splash `index` as the channel-flip one, and name the three reveal
	// candidates (the 4:3 "revenged" default + the two portrait "pure" shots). Must
	// be called before Initialize so LoadContent picks them up.
	public void SetChannelFlip(int index, string revengedName, string pureName, string glassesName)
	{
		flipIndex = index;
		flipRevengedName = revengedName;
		flipPureName = pureName;
		flipGlassesName = glassesName;
	}

	public override void Initialize()
	{
		base.Initialize();
		base.LoadContent();
	}

	public void AddSplash(string filename)
	{
		Texture2D item = localContent.Load<Texture2D>(filename);
		texturenames.Add(filename);
		textures.Add(item);
		splashTexts.Add(null);
	}

	// Register a text-only splash (no texture). `lines` are the reveal beats: each
	// stanza fades in in turn for comedic timing. Must be called before Initialize.
	public void AddTextSplash(string[] lines)
	{
		texturenames.Add(null);
		textures.Add(null);
		splashTexts.Add(lines);
		textIndex = textures.Count - 1;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blankTexture = localContent.Load<Texture2D>("GFX/Splash/blank");
		textures.Clear();
		foreach (string texturename in texturenames)
		{
			// null name == a text splash; it carries no texture.
			textures.Add(texturename == null ? null : localContent.Load<Texture2D>(texturename));
		}
		if (textIndex >= 0)
		{
			textFont = localContent.Load<SpriteFont>("GFX/Menu/menufont");
		}
		if (flipIndex >= 0)
		{
			// channelflip.fx may be missing on a partial deploy; degrade to a plain
			// (un-flipped) old splash rather than crashing the boot sequence.
			try
			{
				channelFlip = localContent.Load<Effect>("GFX/Effects/channelflip");
			}
			catch (Exception ex)
			{
				System.Console.WriteLine("[channelflip] effect load failed: " + ex);
				channelFlip = null;
			}
			flipRevenged = localContent.Load<Texture2D>(flipRevengedName);
			flipPure = localContent.Load<Texture2D>(flipPureName);
			flipGlasses = localContent.Load<Texture2D>(flipGlassesName);
		}
	}

	// Called when a splash starts displaying: extend the showtime for the flip splash
	// (so the reveal gets the full dwell) and roll the reveal variant.
	private void BeginDisplay()
	{
		currentLines = splashTexts[currentTextureNumber];
		bool isText = (currentLines != null) && (textFont != null);
		bool isFlip = (currentTextureNumber == flipIndex);
		holdMs = (double)showtime * HOLD_FRAC;
		if (isText)
		{
			// Build the reveal schedule: each stanza's reveal is offset from the previous
			// by a gap that scales with the PREVIOUS stanza's length (its reading time), so
			// long lines hold and short ones flick past. The last stanza lands at its reveal
			// time, plus its fade-in and a final hold.
			textRevealAt = new double[currentLines.Length];
			double at = TEXT_FIRST_MS;
			for (int i = 0; i < currentLines.Length; i++)
			{
				textRevealAt[i] = at;
				at += TEXT_GAP_BASE_MS + currentLines[i].Length * TEXT_GAP_PER_CHAR_MS;
				// An ellipsis-led line (".. in 2008") trails off as a setup — let it hang an
				// extra beat before the next line lands, for comedic effect.
				if (currentLines[i].TrimStart().StartsWith(".."))
				{
					at += TEXT_DRAMATIC_BEAT_MS;
				}
			}
			textShowtime = textRevealAt[currentLines.Length - 1]
				+ TEXT_STANZA_FADE_MS + TEXT_HOLD_MS;
			effShowtime = textShowtime;
		}
		else
		{
			effShowtime = (double)showtime + (isFlip ? (holdMs + FLIP_MS) : 0.0);
		}
		variantPicked = false;
		if (isFlip)
		{
			PickFlipVariant();
		}
	}

	// ~1-in-10 reveals a portrait "pure" shot (50/50 plain vs sunglasses); otherwise
	// the 4:3 "revenged" default. The reveal is pillarboxed into the 4:3 splash frame.
	private void PickFlipVariant()
	{
		if (rng.NextDouble() < 0.10)
		{
			chosenNew = (rng.Next(2) == 0) ? flipPure : flipGlasses;
		}
		else
		{
			chosenNew = flipRevenged;
		}
		chosenNewRect = FitRect(chosenNew);
		variantPicked = (chosenNew != null) && (channelFlip != null);
	}

	// AspectFit the reveal into the 800x600 (4:3) splash frame, as a uv sub-rect
	// (xy = offset, zw = scale) for channelflip.fx; black outside it (pillar/letterbox).
	private static Vector4 FitRect(Texture2D tex)
	{
		float frame = 800f / 600f;
		float a = (float)tex.Width / (float)tex.Height;
		if (a >= frame)
		{
			float vS = frame / a;
			return new Vector4(0f, (1f - vS) / 2f, 1f, vS);
		}
		float uS = a / frame;
		return new Vector4((1f - uS) / 2f, 0f, uS, 1f);
	}

	// 0 during the hold, 0..1 across the flip, 1 afterwards.
	private float FlipProgress()
	{
		double t = stateTimer.TotalMilliseconds;
		if (t < holdMs)
		{
			return 0f;
		}
		if (t < holdMs + FLIP_MS)
		{
			return (float)((t - holdMs) / FLIP_MS);
		}
		return 1f;
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

	// Draw the text splash: stanzas centred & auto-fit to the 800x600 design frame,
	// each fading in on its own comedic beat. Positions are fixed from the start (laid
	// out for the full block) so nothing jumps as later stanzas pop in; the shared
	// fade-to-black overlay still rides on top for the whole-block in/out.
	private void DrawTextSplash()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		string[] lines = currentLines;
		double t = stateTimer.TotalMilliseconds;

		// Auto-fit: scale so the widest stanza fits the frame with a margin.
		float maxW = 1f;
		for (int i = 0; i < lines.Length; i++)
		{
			float w = textFont.MeasureString(lines[i]).X;
			if (w > maxW)
			{
				maxW = w;
			}
		}
		float scale = Math.Min(1f, 720f / maxW);
		float lineH = (float)textFont.LineSpacing * scale;
		float gap = lineH * 1.7f;                       // stanza spacing (blank-line feel)
		float blockH = (float)(lines.Length - 1) * gap + lineH;
		float top = 300f - blockH / 2f;

		for (int i = 0; i < lines.Length; i++)
		{
			double revealAt = textRevealAt[i];
			float a = MathHelper.Clamp((float)((t - revealAt) / TEXT_STANZA_FADE_MS), 0f, 1f);
			if (a <= 0f)
			{
				continue;
			}
			float w = textFont.MeasureString(lines[i]).X * scale;
			Vector2 pos = new Vector2(400f - w / 2f, top + (float)i * gap);
			// Straight alpha (NonPremultiplied blend): keep RGB full, vary only alpha.
			Color c = new Color((byte)240, (byte)248, (byte)255, (byte)(a * 255f));
			// Stage 13: chrome sheen on the text splash (the "lovingly crafted without AI" gag).
			// The per-stanza fade rides in the tint alpha, which DrawMetalString preserves.
			base.SpriteBatch.DrawMetalString(textFont, lines[i], pos, c, 0f, Vector2.Zero, scale);
		}
	}

	public void Unload()
	{
		state = SplashSceneState.stopped;
		localContent.Unload();
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		if (state == SplashSceneState.stopped)
		{
			return;
		}
		// Stage 10: splashes are drawn in 800x600 design space (RenderScale.Matrix scales
		// them up to fill the render target); the Clear wipes the whole bound target.
		Rectangle dest = new Rectangle(0, 0, 800, 600);
		base.GraphicsDevice.Clear(Color.Black);

		bool isText = (currentLines != null) && (textFont != null);
		bool isFlip = !isText && (currentTextureNumber == flipIndex) && variantPicked && (channelFlip != null);
		if (displaySplash && isText)
		{
			DrawTextSplash();
		}
		else if (displaySplash & (currentSplash != null))
		{
			if (isFlip)
			{
				// The channel-flip SHADER only runs once the crossfade actually starts
				// (prog > 0). During the pre-glitch HOLD we draw the old splash with the
				// PLAIN Draw + the shared black-overlay fade — byte-identical to the EA
				// logo's path, which fades correctly — so the "I made this" fade-in can't
				// be entangled with the shader at all. (The old bug: routing the hold's
				// fade through the shader's premultiplied Fade / a separate DrawEffect
				// batch never composited the fade the way the plain path does.) At the
				// handoff prog is 0+ and currentFade is already 255 (hold is well past the
				// fade-in), and the shader at prog~=0 == the old image, so there's no pop.
				float prog = FlipProgress();
				if (prog <= 0f)
				{
					base.SpriteBatch.Draw(currentSplash, dest, Color.White);
				}
				else
				{
					float time = (float)gameTime.TotalGameTime.TotalSeconds;
					Texture2D oldTex = currentSplash;
					Texture2D newTex = chosenNew;
					Vector4 nrect = chosenNewRect;
					Effect fx = channelFlip;
					// Stage 10: the channel-flip reveal rides the unified scene path — drawn
					// in 800x600 design space through the channelflip pixel effect (s0 = the
					// old splash, the reveal bound as NewTexture), scaled up to render res by
					// RenderScale.Matrix. Fade=1: the global fade rides the overlay below.
					base.SpriteBatch.DrawEffect(oldTex, new Rectangle(0, 0, 800, 600), fx,
						delegate(Effect eff, Rectangle d)
						{
							eff.Parameters["Progress"].SetValue(prog);
							eff.Parameters["Time"].SetValue(time);
							eff.Parameters["Fade"].SetValue(1f);
							eff.Parameters["NewRect"].SetValue(nrect);
							eff.Parameters["NewTexture"].SetValue(newTex);
						});
				}
			}
			else
			{
				base.SpriteBatch.Draw(currentSplash, dest, Color.White);
			}
		}
		// Global fade-to-black for ALL splash types (image, text, AND the channel-flip):
		// the flip shader now renders at full opacity, so this single overlay is the one
		// fade path — the "I made this" fades in/out identically to the EA logo.
		{
			int num = 255 - currentFade;
			if (num < 0)
			{
				num = 0;
			}
			if (num > 255)
			{
				num = 255;
			}
			fadeBackBufferToBlack(num);
		}
		base.SpriteBatch.DrawString("v2026.0", new Vector2(700f, 550f), Color.AliceBlue, 0f, Vector2.Zero, 0.5f, (SpriteEffects)0, 1f);
	}

	public override void Update(GameTime gameTime)
	{
		// Clamp the per-frame step: WASM boot hands the first Update a huge dt (the
		// whole load time), which would otherwise blow through the splash timers in a
		// single frame and skip the channel-flip entirely. Normal frames (~16ms) are
		// well under the cap, so real-time playback is unchanged.
		TimeSpan dt = gameTime.ElapsedGameTime;
		if (dt.TotalMilliseconds > 100.0)
		{
			dt = TimeSpan.FromMilliseconds(100.0);
		}
		stateTimer += dt;
		switch (state)
		{
		case SplashSceneState.loading:
			if ((stateTimer.TotalMilliseconds > (double)pre) & (textures.Count != 0))
			{
				state = SplashSceneState.displaying;
				stateTimer = TimeSpan.Zero;
				currentTextureNumber = 0;
				currentSplash = textures[currentTextureNumber];
				displaySplash = true;
				BeginDisplay();
			}
			break;
		case SplashSceneState.displaying:
		{
			double num = 255f / (float)fadetime;
			if (stateTimer.TotalMilliseconds < (double)fadetime)
			{
				num *= stateTimer.TotalMilliseconds;
				currentFade = Convert.ToInt32(num);
			}
			else if (effShowtime - (double)fadetime < stateTimer.TotalMilliseconds)
			{
				num *= effShowtime - stateTimer.TotalMilliseconds;
				currentFade = Convert.ToInt32(num);
			}
			else
			{
				currentFade = 255;
			}
			if (stateTimer.TotalMilliseconds > effShowtime)
			{
				state = SplashSceneState.paused;
				stateTimer = TimeSpan.Zero;
				displaySplash = false;
			}
			break;
		}
		case SplashSceneState.paused:
			if (!(stateTimer.TotalMilliseconds > (double)pausetime))
			{
				break;
			}
			if (currentTextureNumber + 1 == textures.Count)
			{
				state = SplashSceneState.stopped;
				displaySplash = false;
				if (this.OnFinished != null)
				{
					this.OnFinished(this);
				}
			}
			else
			{
				stateTimer = TimeSpan.Zero;
				displaySplash = true;
				currentTextureNumber++;
				currentSplash = textures[currentTextureNumber];
				state = SplashSceneState.displaying;
				currentFade = 0;
				BeginDisplay();
			}
			break;
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
		if (flag)
		{
			state = SplashSceneState.stopped;
			displaySplash = false;
			if (this.OnFinished != null)
			{
				this.OnFinished(this);
			}
		}
	}
}
