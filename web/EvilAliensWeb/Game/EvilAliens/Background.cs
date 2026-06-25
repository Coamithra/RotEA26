using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class Background : Scene
{
	public delegate void XFadeFinishedEvent();

	private Timer XFade = new Timer(1500f, repeating: false);

	private RenderTarget2D rendertarget;

	private TimeSpan timer = TimeSpan.Zero;

	private BackgroundState state;

	private List<BackgroundImage> backgroundLayers;

	private List<BackgroundImage> foregroundLayers;

	private Timer layerXFadeTimer = new Timer(1000f, repeating: false);

	private Texture2D blank;

	private Vector2 scrollspeed;

	private Vector2 targetscrollspeed;

	private Vector2 scrollspeedinitial;

	private Vector2 scrollspeedreset;

	private Timer scrollspeedchangetimer = new Timer(1333f, repeating: false);

	private float scrollspeedmodifier;

	private float oscilatereach;

	private float oscilatespeed;

	private Texture2D doodad;

	private string doodadname;

	private Vector2 doodadscrollspeed;

	private Vector2 doodadPos;

	private float doodadscale;

	private bool showdoodad;

	private Color doodadcolor;

	private SpriteBlendMode doodadblendmode;

	// Earth fly-by parallax fix (card: "Physics of star background and earth"). While a planet
	// doodad crosses, the BACKGROUND speed (scrollspeedmodifier — which the earth doodad ignores)
	// is ramped down so both starfields nearly freeze and the earth reads as the fastest, nearest
	// object instead of being overtaken by the near drifting stars. doodadStarSlowdown is the target
	// star-speed fraction while THIS doodad crosses (1 = no effect: andromeda / holodeck sim-earth).
	// doodadEnterFromTop is the crossing direction, captured at queue time. These describe the
	// doodad (like doodadscale/doodadcolor); they are NOT a second speed modifier.
	private float doodadStarSlowdown = 1f;

	private bool doodadEnterFromTop = true;

	private float fadeFactor;

	// Stage 13 reskin: the new space background — a procedural, infinite, scrolling
	// grid of overlapping high-res nebula tiles, crossfaded by starwindow.fx. Set by
	// SetSpace (which then leaves backgroundLayers empty); null for every other scene.
	private ProceduralStarfield starfield;

	// The near (foreground) star layer: a handful of INDIVIDUAL stars (DriftingStars)
	// drawn additively on top of the far nebula, each with its own speed / scale / twinkle
	// so they don't read as one uniform moving wall. Set alongside starfield in SetSpace;
	// null otherwise.
	private DriftingStars nearStars;

	// Holodeck (trial-simulation chamber). When the simulator background is active, Jump()
	// fires a brief, deliberate "projection hiccup" instead of teleporting the layers:
	// a stutter-slip and/or a brightness flicker driven over glitchTimer.
	private bool isHolodeck;

	private BackgroundImage holoGrid;

	private Timer glitchTimer = new Timer(170f, repeating: false);

	private Vector2 glitchSlip;

	// A light pulse that sweeps down through the holodeck grid every once in a while.
	// pulseTimer = one sweep's travel; pulseCooldown = the (randomised) gap between sweeps.
	private Timer pulseTimer = new Timer(1500f, repeating: false);

	private Timer pulseCooldown = new Timer(10000f, repeating: false);

	private bool pulseActive;

	public Vector2 ScrollSpeed => scrollspeed;

	// True while a fly-by doodad (hero earth / sim-earth / small earth / andromeda)
	// is crossing the screen. WaitForDoodadEvent polls this so Level 1 can hold the
	// sideways asteroid-belt phase until the earth has left the screen.
	public bool DoodadActive => showdoodad;

	public event XFadeFinishedEvent OnXFadeFinished;

	public Background(Game game)
		: base(game)
	{
		base.DrawOrder = 0;
		scrollspeedchangetimer.Stop();
		glitchTimer.Stop();
		pulseTimer.Stop();
		pulseCooldown.Stop();
		showdoodad = false;
		backgroundLayers = new List<BackgroundImage>();
		foregroundLayers = new List<BackgroundImage>();
		XFade.Stop();
	}

	public void SetSpeed(Vector2 speed)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		targetscrollspeed = speed;
		scrollspeedinitial = scrollspeed;
		scrollspeedchangetimer.Reset();
		scrollspeedchangetimer.Start();
	}

	public void QueueSmallEarth()
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		if (!showdoodad)
		{
			// Minor "earth in the corner" appearance uses a dedicated small texture
			// (256px) so we don't decode the big hero strip (~1392x1822) just to draw
			// a ~110px dot. scale 0.45 on the 243px disk == the old 730px disk at 0.15.
			doodadname = "GFX/Sprites/earth_small";
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			doodadscale = 0.45f;
			doodadscrollspeed = new Vector2(1f, 1f);
			doodadPos = new Vector2(620f, (float)(-doodad.Height) * doodadscale / 2f);
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			// Milder than the hero earth (small corner planet): slow the stars to ~25%.
			doodadStarSlowdown = 0.25f;
			doodadEnterFromTop = true;
		}
	}

	public void QueueEarth()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		if (!showdoodad)
		{
			doodadname = "GFX/Sprites/earth";
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			// Hi-res NASA Blue Marble hero disk: earth.png is now the FULL-res source
			// crop (~1822px disk), cropped to a central vertical strip (tools/earth/
			// build_earth.py), so scale falls 0.8 -> 0.6467 to keep the on-screen size
			// identical (1806*0.6467 == old 730*1.6 == 1168 px) while rendering crisp.
			doodadscale = 0.6467f;
			// X scroll is ZERO: the hero earth only descends vertically, staying
			// horizontally centred so its cropped sides never reach the screen edge.
			doodadscrollspeed = new Vector2(0f, 1.55f);
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			// Hero earth: near-freeze the starfields (~12%) while it glides across.
			doodadStarSlowdown = 0.12f;
			doodadEnterFromTop = scrollspeed.Y > 0f;
			if (scrollspeed.Y > 0f)
			{
				doodadPos = new Vector2(400f, (float)(-doodad.Height) * doodadscale / 2f);
			}
			else
			{
				doodadPos = new Vector2(400f, 600f + (float)doodad.Height * doodadscale / 2f);
			}
		}
	}

	public void QueueAndromeda()
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		if (!showdoodad)
		{
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			doodadname = "GFX/Sprites/andromeda";
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			doodadscale = 1f;
			doodadscrollspeed = new Vector2(1f, 1f);
			// A distant galaxy, not a planet — no star slowdown (also clears a prior earth's value).
			doodadStarSlowdown = 1f;
			if (scrollspeed.Y > 0f)
			{
				doodadPos = new Vector2(400f, (float)(-doodad.Height) * doodadscale / 2f);
			}
			else
			{
				doodadPos = new Vector2(400f, 600f + (float)doodad.Height * doodadscale / 2f);
			}
		}
	}

	protected void fadeBackBufferToWhite(float factor)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		factor = MathHelper.Clamp(factor, 0f, 1f);
		int num = Convert.ToInt16(factor * 255f);
		// Stage 10: full-screen fade in 800x600 design space (scaled by RenderScale.Matrix).
		base.SpriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)num));
	}

	protected void fadeBackBufferToBlack(float factor)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		factor = MathHelper.Clamp(factor, 0f, 1f);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.SpriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(new Vector4(0f, 0f, 0f, factor)));
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_021e: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		timer += gameTime.ElapsedGameTime;
		if (showdoodad)
		{
			doodadPos += doodadscrollspeed * scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (scrollspeed.Y > 0f && ((doodadPos.Y > 600f + (float)doodad.Height * doodadscale / 2f) | (doodadPos.X > 800f + (float)doodad.Width * doodadscale / 2f)))
			{
				showdoodad = false;
			}
			if (scrollspeed.Y < 0f && ((doodadPos.Y < (float)(-doodad.Height) * doodadscale / 2f) | (doodadPos.X > 800f + (float)doodad.Width * doodadscale / 2f)))
			{
				showdoodad = false;
			}
		}
		scrollspeedchangetimer.Update(gameTime);
		if (scrollspeedchangetimer.Active)
		{
			scrollspeed.X = MathHelper.Lerp(scrollspeedinitial.X, targetscrollspeed.X, 1f - scrollspeedchangetimer.Normalized);
			scrollspeed.Y = MathHelper.Lerp(scrollspeedinitial.Y, targetscrollspeed.Y, 1f - scrollspeedchangetimer.Normalized);
		}
		if (scrollspeedchangetimer.Finished)
		{
			scrollspeed = targetscrollspeed;
			scrollspeedchangetimer.Reset();
		}
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			backgroundLayer.Move(scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * scrollspeedmodifier);
		}
		foreach (BackgroundImage foregroundLayer in foregroundLayers)
		{
			foregroundLayer.Move(scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * scrollspeedmodifier);
		}
		Vector2 starDelta = scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * scrollspeedmodifier;
		if (starfield != null)
		{
			starfield.Advance(starDelta);
		}
		// The near stars get the same base scroll delta (each applies its own per-star
		// parallax) plus the elapsed time for their twinkle.
		if (nearStars != null)
		{
			nearStars.Advance(starDelta, (float)gameTime.ElapsedGameTime.TotalMilliseconds);
		}
		UpdateHoloGlitch(gameTime);
		UpdateHoloPulse(gameTime);
		switch (state)
		{
		case BackgroundState.LeavingHyperspace:
			if (timer.TotalMilliseconds > 1.0)
			{
				fadeFactor -= 0.0005f * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
				if (fadeFactor < 0f)
				{
					fadeFactor = 0f;
				}
				scrollspeedmodifier = 1f + fadeFactor * 10f;
			}
			break;
		case BackgroundState.End:
			if (timer.TotalMilliseconds > 3500.0)
			{
				fadeFactor += 0.0005f * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
				if (fadeFactor < 0f)
				{
					fadeFactor = 0f;
				}
				scrollspeedmodifier = 1f + fadeFactor * 30f;
			}
			break;
		}
		// Earth fly-by parallax fix: fold the planet-crossing slowdown into the SAME background
		// speed knob the hyperspace/End ramps above drive. The earth doodad moves on scrollspeed
		// alone (no scrollspeedmodifier), so this slows ONLY the starfields/bg layers, letting the
		// earth read as the fastest, nearest object. No-op (factor 1) when no planet is crossing.
		scrollspeedmodifier *= DoodadStarSlowdownFactor();
		if (XFade.Active)
		{
			XFade.Update(gameTime);
			if (XFade.Finished && this.OnXFadeFinished != null)
			{
				this.OnXFadeFinished();
			}
		}
	}

	// Star-slowdown envelope for the earth fly-by (card: "Physics of star background and earth").
	// Returns the factor (<= 1) to multiply into scrollspeedmodifier so the starfields slow while a
	// planet doodad crosses; 1 means no change. Keyed to the doodad's own on-screen progress (its
	// centre vs the screen edges, accounting for the disk's half-height) so it is robust to any
	// scrollspeed and to both crossing directions. Shape over the full visible crossing:
	//   hold full -> ramp down to doodadStarSlowdown -> hold slow -> ramp back to full as it leaves,
	// which gives "earth pops in at high speed, the stars slow massively, then speed up as it exits".
	private float DoodadStarSlowdownFactor()
	{
		if (!showdoodad || doodad == null || doodadStarSlowdown >= 1f)
		{
			return 1f;
		}
		float halfH = (float)doodad.Height * doodadscale * 0.5f;
		// enter/exit edges of the doodad centre across the screen, by crossing direction.
		float enter = doodadEnterFromTop ? (0f - halfH) : (600f + halfH);
		float exit = doodadEnterFromTop ? (600f + halfH) : (0f - halfH);
		float span = exit - enter;
		if (Math.Abs(span) < 0.0001f)
		{
			return 1f;
		}
		float prog = (doodadPos.Y - enter) / span; // 0 = just appearing, 1 = fully gone
		const float holdIn = 0.1f;   // earth pops in while stars still streak
		const float rampIn = 0.15f;  // stars decelerate
		const float rampOut = 0.22f; // stars re-accelerate as the trailing edge leaves
		float t; // 0 = full star speed, 1 = fully slowed
		if (prog < holdIn)
		{
			t = 0f;
		}
		else if (prog < holdIn + rampIn)
		{
			t = (prog - holdIn) / rampIn;
		}
		else if (prog <= 1f - rampOut)
		{
			t = 1f;
		}
		else
		{
			t = (1f - prog) / rampOut;
		}
		t = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(t, 0f, 1f));
		return MathHelper.Lerp(1f, doodadStarSlowdown, t);
	}

	public void DrawForeground(GameTime gameTime)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		if (XFade.Active)
		{
			float num = 1f - XFade.Normalized;
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			// Stage 10: render-sized RT -> 1:1 identity composite (DrawPresent).
			base.SpriteBatch.DrawPresent(rendertarget, Vector2.Zero, Vector2.Zero, 1f, new Color(new Vector4(1f, 1f, 1f, num)));
		}
		foreach (BackgroundImage foregroundLayer in foregroundLayers)
		{
			foregroundLayer.Draw(base.SpriteBatch, gameTime);
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		if (XFade.Active)
		{
			base.SpriteBatch.Flush();
			EnsureRenderTarget();
			base.GraphicsDevice.SetRenderTarget(0, rendertarget);
		}
		if (starfield != null)
		{
			// Render-space, additive, custom-window batch — flush the wrapper's
			// design-space batch first so the two SpriteBatches don't overlap.
			base.SpriteBatch.Flush();
			starfield.Brightness = DebugToggles.Active ? DebugToggles.StarfieldBrightness : 1f;
			starfield.Draw();
		}
		// Near drifting stars ON TOP of the far nebula (its own additive SpriteBatch, so no
		// flush needed). Drawn before the doodad/planet so the planet still occludes them.
		if (nearStars != null)
		{
			nearStars.Brightness = DebugToggles.Active ? DebugToggles.StarfieldBrightness : 1f;
			nearStars.Draw();
		}
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			backgroundLayer.Draw(base.SpriteBatch, gameTime);
		}
		DrawHoloPulse();
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		if (showdoodad)
		{
			base.SpriteBatch.BlendMode = doodadblendmode;
			base.SpriteBatch.Draw(doodad, doodadPos, 0f, doodadscale, center: true, doodadcolor);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		}
		float factor = Convert.ToSingle((double)(0.15f + oscilatereach) + Math.Sin((double)oscilatespeed * timer.TotalMilliseconds) * (double)oscilatereach);
		if (!DebugToggles.Active || DebugToggles.BgVeil)
		{
			fadeBackBufferToBlack(factor);
		}
		if (fadeFactor > 0f)
		{
			fadeBackBufferToWhite(fadeFactor);
		}
		if (XFade.Active)
		{
			base.SpriteBatch.Flush();
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			// Stage 10: render-sized RT -> 1:1 identity composite (DrawPresent).
			base.SpriteBatch.DrawPresent(rendertarget, Vector2.Zero, Vector2.Zero, 1f, Color.White);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		}
	}

	internal void FadeOut()
	{
		timer = default(TimeSpan);
		state = BackgroundState.End;
		fadeFactor = 0f;
	}

	public void SetAlienBase6()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v8");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v8";
		backgroundLayers[0].StartSwitch();
	}

	public void SetAlienBase5()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v6");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v6";
		backgroundLayers[0].StartSwitch();
	}

	public void SetAlienBase4()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v4");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v4";
		backgroundLayers[0].StartSwitch();
	}

	public void SetAlienBase3()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v3");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v3";
		backgroundLayers[0].StartSwitch();
	}

	public void SetAlienBase2()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v5");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v5";
		backgroundLayers[0].StartSwitch();
	}

	public void SetAlienBase()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0295: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		isHolodeck = false;
		holoGrid = null;
		DisposeStarfield();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756");
		backgroundImage.texturenames[0, 0] = "GFX/Base/756";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.66f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Base/2331-v5");
		backgroundImage.texturenames[0, 0] = "GFX/Base/2331-v5";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.52f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = new Vector2(400f, 300f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Base/2331-v5");
		backgroundImage.texturenames[0, 0] = "GFX/Base/2331-v5";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.8f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		scrollspeedreset = new Vector2(0f, 4.5f) / 16.666666f;
		oscilatereach = 0.233f;
		oscilatespeed = 0.0003f;
		Reset();
	}

	public void SetSpace()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0295: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		isHolodeck = false;
		holoGrid = null;
		// Stage 13 reskin: replace the three hand-placed Starfield2/tileablestarfield
		// layers with a deterministic, infinite, scrolling grid of overlapping high-res
		// nebula tiles, crossfaded by starwindow.fx. See ProceduralStarfield. The legacy
		// backgroundLayers list stays empty for space; Update/Draw drive `starfield`.
		DisposeStarfield();
		starfield = new ProceduralStarfield();
		starfield.LoadContent(Content, base.GraphicsDevice);
		// Near (foreground) star layer: a handful of individual drifting stars cut from the
		// space_near tiles, each with its own speed / scale / twinkle (see DriftingStars).
		nearStars = new DriftingStars();
		nearStars.LoadContent(Content, base.GraphicsDevice);
		scrollspeedreset = new Vector2(0f, 0.2f) / 16.666666f;
		oscilatereach = 0.1f;
		oscilatespeed = 0.001f;
		Reset();
	}

	public void SetSimpleSpace()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		// Holodeck / trial-simulation chamber. Space here is PROJECTED, not real: the stars
		// stay (a space-combat sim that showed no stars would be dull AND a poor simulation)
		// but are cool-tinted + dimmed so they read as part of the projection, while the grid
		// becomes the hero -- a bright cyan near layer over a dim far layer for depth. A gentle
		// pulse breathes (oscilate*), and Jump() fires deliberate holo-glitches (see Update).
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		DisposeStarfield();
		// simulated stars, far: cool + dim, straight alpha
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.45f, 0.7f, 0.95f, 1f);
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 1.5f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.5f;
		backgroundLayers.Add(backgroundImage);
		// simulated stars, near: additive cool glint
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.3f, 0.55f, 0.8f, 1f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 2f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 1.2f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		// holo-grid, far: dim, large, slow -> depth
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.22f, 0.55f, 0.66f, 0.3f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Tutorial/grid3");
		backgroundImage.texturenames[0, 0] = "GFX/Tutorial/grid3";
		backgroundImage.size = 2.4f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.25f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		// holo-grid, near: cyan hero -> the layer the glitch slips most
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.42f, 0.82f, 0.95f, 0.55f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Tutorial/grid3");
		backgroundImage.texturenames[0, 0] = "GFX/Tutorial/grid3";
		backgroundImage.size = 1.5f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.5f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		holoGrid = backgroundImage;
		isHolodeck = true;
		pulseActive = false;
		pulseCooldown.Duration = RandomHelper.RandomNextFloat(4000f, 9000f);
		pulseCooldown.Reset();
		pulseCooldown.Start();
		scrollspeedreset = new Vector2(0f, 0.2f) / 16.666666f;
		oscilatereach = 0.06f;
		oscilatespeed = 0.0025f;
		Reset();
	}

	public void Reset()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		XFade.Stop();
		showdoodad = false;
		state = BackgroundState.LeavingHyperspace;
		fadeFactor = 0.998f;
		scrollspeed = scrollspeedreset;
		scrollspeedmodifier = 10f;
	}

	// Dispose the procedural starfield (the SpriteBatch it owns) and forget it, so a
	// non-space background falls back to backgroundLayers. Safe to call when null.
	private void DisposeStarfield()
	{
		if (starfield != null)
		{
			starfield.Dispose();
			starfield = null;
		}
		if (nearStars != null)
		{
			nearStars.Dispose();
			nearStars = null;
		}
	}

	internal void SetMars()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b0: Unknown result type (might be due to invalid IL or missing references)
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		isHolodeck = false;
		holoGrid = null;
		DisposeStarfield();
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/MarsBG/clouds-background");
		backgroundImage.texturenames[0, 0] = "GFX/MarsBG/clouds-background";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.3f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/MarsBG/marshills");
		backgroundImage.texturenames[0, 0] = "GFX/MarsBG/marshills";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.7f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[6, 1];
		backgroundImage.texturenames = new string[6, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/MarsBG/mars1");
		backgroundImage.texturenames[0, 0] = "GFX/MarsBG/mars1";
		backgroundImage.textures[1, 0] = Content.Load<Texture2D>("GFX/MarsBG/mars2");
		backgroundImage.texturenames[1, 0] = "GFX/MarsBG/mars2";
		backgroundImage.textures[2, 0] = Content.Load<Texture2D>("GFX/MarsBG/mars3");
		backgroundImage.texturenames[2, 0] = "GFX/MarsBG/mars3";
		backgroundImage.textures[3, 0] = Content.Load<Texture2D>("GFX/MarsBG/mars4");
		backgroundImage.texturenames[3, 0] = "GFX/MarsBG/mars4";
		backgroundImage.textures[4, 0] = Content.Load<Texture2D>("GFX/MarsBG/mars5");
		backgroundImage.texturenames[4, 0] = "GFX/MarsBG/mars5";
		backgroundImage.textures[5, 0] = Content.Load<Texture2D>("GFX/MarsBG/mars6");
		backgroundImage.texturenames[5, 0] = "GFX/MarsBG/mars6";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)(backgroundImage.textures[0, 0].Width + backgroundImage.textures[1, 0].Width + backgroundImage.textures[2, 0].Width + backgroundImage.textures[3, 0].Width + backgroundImage.textures[4, 0].Width + backgroundImage.textures[5, 0].Width) * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 1f;
		backgroundImage.mirrorX = true;
		ref Vector2 realsize = ref backgroundImage.realsize;
		realsize.X *= 2f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/MarsBG/clouds-foreground2");
		backgroundImage.texturenames[0, 0] = "GFX/MarsBG/clouds-foreground2";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 2.5f;
		foregroundLayers.Add(backgroundImage);
		scrollspeedreset = new Vector2(-10f, 0f) / 16.666666f;
		oscilatereach = 0.1f;
		oscilatespeed = 5E-05f;
		Reset();
	}

	protected override void LoadContent()
	{
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Expected O, but got Unknown
		base.LoadContent();
		if (doodadname != null)
		{
			doodad = Content.Load<Texture2D>(doodadname);
		}
		blank = Content.Load<Texture2D>("GFX/Game/blank");
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			backgroundLayer.LoadGraphics(Content);
		}
		foreach (BackgroundImage foregroundLayer in foregroundLayers)
		{
			foregroundLayer.LoadGraphics(Content);
		}
		EnsureRenderTarget();
	}

	// Stage 10: the cross-fade (XFade) renders a background into this offscreen target,
	// then blits it over the new background to dissolve between them. Size it to the
	// unified render resolution (RenderScale) so it composites 1:1 with the scene, and
	// use SurfaceFormat.Color (RGBA8) — the original 16-bit format renders nothing on
	// WebGL (same trap Stage 5 hit with the menu targets). Recreated on a size change.
	private void EnsureRenderTarget()
	{
		int w = RenderScale.Width;
		int h = RenderScale.Height;
		if (rendertarget != null && ((Texture2D)rendertarget).Width == w && ((Texture2D)rendertarget).Height == h)
		{
			return;
		}
		if (rendertarget != null)
		{
			((Texture2D)rendertarget).Dispose();
		}
		rendertarget = new RenderTarget2D(base.GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, (RenderTargetUsage)1);
	}

	protected override void UnloadContent()
	{
		base.UnloadContent();
		DisposeStarfield();
		if (rendertarget != null)
		{
			((Texture2D)rendertarget).Dispose();
		}
		rendertarget = null;
	}

	public void CrossFade()
	{
		XFade.Start();
		XFade.Reset();
	}

	public void SetSpaceClassic()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		SetSpace();
		scrollspeedreset = new Vector2(0f, -0.2f) / 16.666666f;
		Reset();
	}

	// Trigger one deliberate "projection hiccup". Called by the simulator levels
	// (TutorialLevel/ClassicAliens). The old behaviour teleported each layer to a random
	// position -- a hard pop that read like a rendering seam; now it kicks off a short,
	// subtle position-only stutter-slip driven over glitchTimer in UpdateHoloGlitch. No
	// brightness flash (a tinted flicker read as a distracting random-colour flash).
	internal void Jump()
	{
		if (!isHolodeck)
		{
			return;
		}
		glitchTimer.Reset();
		glitchTimer.Start();
		glitchSlip = new Vector2(RandomHelper.RandomNextFloat(-7f, 7f), RandomHelper.RandomNextFloat(-3f, 3f));
	}

	// Per-frame: clear last frame's transient slip, then (while a glitch burst is active)
	// apply a small steppy "digital" stutter so it reads as an intentional holographic
	// hiccup rather than a smooth smear. The grid slips most; the rest of the projection
	// (stars, far grid) barely moves, so it doesn't pull the eye off gameplay.
	private void UpdateHoloGlitch(GameTime gameTime)
	{
		if (!isHolodeck)
		{
			return;
		}
		foreach (BackgroundImage layer in backgroundLayers)
		{
			layer.drawOffset = Vector2.Zero;
		}
		glitchTimer.Update(gameTime);
		if (!glitchTimer.Active)
		{
			return;
		}
		float p = 1f - glitchTimer.Normalized;
		float[] slipSteps = new float[3] { 1f, -0.5f, 0.2f };
		Vector2 off = glitchSlip * slipSteps[Math.Min((int)(p * 3f), 2)];
		if (holoGrid != null)
		{
			holoGrid.drawOffset = off;
		}
		foreach (BackgroundImage layer in backgroundLayers)
		{
			if (layer != holoGrid)
			{
				layer.drawOffset = off * 0.15f;
			}
		}
	}

	// Drives the grid light-pulse: while a sweep runs, advance it; otherwise count down the
	// randomised cooldown and kick off the next sweep when it elapses. DrawHoloPulse renders it.
	private void UpdateHoloPulse(GameTime gameTime)
	{
		if (!isHolodeck)
		{
			return;
		}
		if (pulseActive)
		{
			pulseTimer.Update(gameTime);
			if (!pulseTimer.Active)
			{
				pulseActive = false;
				pulseCooldown.Duration = RandomHelper.RandomNextFloat(8000f, 16000f);
				pulseCooldown.Reset();
				pulseCooldown.Start();
			}
		}
		else
		{
			pulseCooldown.Update(gameTime);
			if (!pulseCooldown.Active)
			{
				pulseActive = true;
				pulseTimer.Reset();
				pulseTimer.Start();
			}
		}
	}

	// A soft cyan light band that sweeps top->bottom through the holodeck. Built from a few
	// stacked, centred additive strips (a cheap triangular falloff) so where it passes the
	// grid lines surge brighter; the bloom present-pass softens it further. Drawn in 800x600
	// design space (scaled by RenderScale.Matrix), like the fade overlays.
	private void DrawHoloPulse()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		if (!isHolodeck || !pulseActive)
		{
			return;
		}
		float p = 1f - pulseTimer.Normalized;
		float bandFull = 220f;
		float centerY = MathHelper.Lerp(0f - bandFull, 600f + bandFull, p);
		float envelope = MathHelper.Clamp(Convert.ToSingle(Math.Sin((double)p * Math.PI)), 0f, 1f);
		float peak = 0.5f * envelope;
		int layers = 10;
		base.SpriteBatch.BlendMode = (SpriteBlendMode)2;
		for (int i = 0; i < layers; i++)
		{
			float h = bandFull * (float)(layers - i) / (float)layers;
			base.SpriteBatch.Draw(blank, new Rectangle(0, (int)(centerY - h / 2f), 800, (int)h), new Color(0.5f, 0.9f, 1f, peak / (float)layers));
		}
	}

	public void SetSimpleSpaceClassic()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		SetSimpleSpace();
		scrollspeedreset = new Vector2(0f, -0.2f) / 16.666666f;
		Reset();
	}

	public void QueueEarthSim()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		if (!showdoodad)
		{
			doodadname = "GFX/Sprites/earth";
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			// Same full-res hero strip as QueueEarth -> scale 0.6467, X scroll 0
			// (centred vertical descent so the cropped strip's sides never show).
			doodadscale = 0.6467f;
			doodadscrollspeed = new Vector2(0f, 1.55f);
			doodadcolor = new Color(0.7f, 0.7f, 0.7f, 1f);
			doodadblendmode = (SpriteBlendMode)2;
			// Holodeck sim-earth (projected, over the grid starfield) is out of scope — no slowdown.
			doodadStarSlowdown = 1f;
			if (scrollspeed.Y > 0f)
			{
				doodadPos = new Vector2(400f, (float)(-doodad.Height) * doodadscale / 2f);
			}
			else
			{
				doodadPos = new Vector2(400f, 600f + (float)doodad.Height * doodadscale / 2f);
			}
		}
	}

	public void SetAlienBaseDark()
	{
		SetAlienBase();
		oscilatereach = 0.5f;
		oscilatespeed = 0f;
		Reset();
	}
}
