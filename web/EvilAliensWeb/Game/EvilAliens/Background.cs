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

	private float fadeFactor;

	public Vector2 ScrollSpeed => scrollspeed;

	public event XFadeFinishedEvent OnXFadeFinished;

	public Background(Game game)
		: base(game)
	{
		base.DrawOrder = 0;
		scrollspeedchangetimer.Stop();
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
			doodadname = "GFX/Sprites/earth";
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			doodadscale = 0.15f;
			doodadscrollspeed = new Vector2(1f, 1f);
			doodadPos = new Vector2(620f, (float)(-doodad.Height) * doodadscale / 2f);
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
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
			doodadscale = 1.6f;
			doodadscrollspeed = new Vector2(1.55f, 1.55f);
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
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
		if (XFade.Active)
		{
			XFade.Update(gameTime);
			if (XFade.Finished && this.OnXFadeFinished != null)
			{
				this.OnXFadeFinished();
			}
		}
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
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			backgroundLayer.Draw(base.SpriteBatch, gameTime);
		}
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		if (showdoodad)
		{
			base.SpriteBatch.BlendMode = doodadblendmode;
			base.SpriteBatch.Draw(doodad, doodadPos, 0f, doodadscale, center: true, doodadcolor);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		}
		float factor = Convert.ToSingle((double)(0.15f + oscilatereach) + Math.Sin((double)oscilatespeed * timer.TotalMilliseconds) * (double)oscilatereach);
		fadeBackBufferToBlack(factor);
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
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 1.5f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.66f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/tileablestarfield");
		backgroundImage.texturenames[0, 0] = "GFX/Game/tileablestarfield";
		backgroundImage.size = 0.8f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 1f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 2f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 1.5f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		scrollspeedreset = new Vector2(0f, 0.2f) / 16.666666f;
		oscilatereach = 0.1f;
		oscilatespeed = 0.001f;
		Reset();
	}

	public void SetSimpleSpace()
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01de: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b9: Unknown result type (might be due to invalid IL or missing references)
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 1.5f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.66f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 2f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 1.5f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(Color.Teal, 0.3f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Tutorial/grid3");
		backgroundImage.texturenames[0, 0] = "GFX/Tutorial/grid3";
		backgroundImage.size = 1.6f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].Width * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].Height * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.45f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		scrollspeedreset = new Vector2(0f, 0.2f) / 16.666666f;
		oscilatereach = 0f;
		oscilatespeed = 0f;
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

	internal void Jump()
	{
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			if (RandomHelper.Random.Next(2) == 0)
			{
				backgroundLayer.position.X = RandomHelper.RandomNextFloat(0f, backgroundLayer.realsize.X);
				backgroundLayer.position.Y = RandomHelper.RandomNextFloat(0f, backgroundLayer.realsize.Y);
			}
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
			doodadscale = 1.6f;
			doodadscrollspeed = new Vector2(1.55f, 1.55f);
			doodadcolor = new Color(0.7f, 0.7f, 0.7f, 1f);
			doodadblendmode = (SpriteBlendMode)2;
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
