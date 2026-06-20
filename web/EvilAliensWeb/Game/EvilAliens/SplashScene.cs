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
	// The flip splash holds its OLD image briefly, then a sudden TV glitch + push
	// reveals one of the revenged images; the normal show/pause/fade dwell then runs
	// on the REVEAL (the retime: the wait time lands after the effect). The reveal is
	// drawn crisp into the unified scene via SpriteBatchWrapper.DrawEffect + channelflip.fx.
	private Effect channelFlip;

	private int flipIndex = -1;

	private string flipRevengedName, flipPureName, flipGlassesName;

	private Texture2D flipRevenged, flipPure, flipGlasses;

	private Texture2D chosenNew;

	private Vector4 chosenNewRect;

	private bool variantPicked;

	private readonly Random rng = new Random();

	private const double HOLD_MS = 700.0;   // old image visible before the flip fires

	private const double FLIP_MS = 450.0;   // sudden glitch + push duration

	private double effShowtime;             // showtime + (flip ? HOLD+FLIP : 0)

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
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		blankTexture = localContent.Load<Texture2D>("GFX/Splash/blank");
		textures.Clear();
		foreach (string texturename in texturenames)
		{
			textures.Add(localContent.Load<Texture2D>(texturename));
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
		bool isFlip = (currentTextureNumber == flipIndex);
		effShowtime = (double)showtime + (isFlip ? (HOLD_MS + FLIP_MS) : 0.0);
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
		if (t < HOLD_MS)
		{
			return 0f;
		}
		if (t < HOLD_MS + FLIP_MS)
		{
			return (float)((t - HOLD_MS) / FLIP_MS);
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

		bool isFlip = (currentTextureNumber == flipIndex) && variantPicked && (channelFlip != null);
		if (displaySplash & (currentSplash != null))
		{
			if (isFlip)
			{
				// Hand the reveal to the native-res overlay: s0 = the OLD splash
				// (currentSplash), the chosen reveal bound as the NewTexture param,
				// the glitch driven by Progress, the fade by Fade. Full 4:3 design slot.
				float prog = FlipProgress();
				float time = (float)gameTime.TotalGameTime.TotalSeconds;
				float fade = (float)currentFade / 255f;
				Texture2D oldTex = currentSplash;
				Texture2D newTex = chosenNew;
				Vector4 nrect = chosenNewRect;
				Effect fx = channelFlip;
				// Stage 10: the channel-flip reveal now rides the unified scene path — drawn
				// in 800x600 design space through the channelflip pixel effect (s0 = the old
				// splash, the reveal bound as NewTexture), scaled up to render res by
				// RenderScale.Matrix. (Was a bolt-on native-res overlay pass pre-Stage-10.)
				base.SpriteBatch.DrawEffect(oldTex, new Rectangle(0, 0, 800, 600), fx,
					delegate(Effect eff, Rectangle d)
					{
						eff.Parameters["Progress"].SetValue(prog);
						eff.Parameters["Time"].SetValue(time);
						eff.Parameters["Fade"].SetValue(fade);
						eff.Parameters["NewRect"].SetValue(nrect);
						eff.Parameters["NewTexture"].SetValue(newTex);
					});
			}
			else
			{
				base.SpriteBatch.Draw(currentSplash, dest, Color.White);
			}
		}
		if (!isFlip)
		{
			// The flip path fades via the shader's Fade uniform; other splashes use
			// the classic fade-to-black overlay.
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
