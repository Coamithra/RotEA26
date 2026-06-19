using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

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

	public event FinishedHandler OnFinished;

	public SplashScene(Game game)
		: base(game)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		localContent = new ContentManager((IServiceProvider)game.Services, "Content");
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
	}

	protected void fadeBackBufferToBlack(int alpha)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		Viewport viewport = base.GraphicsDevice.Viewport;
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, (viewport).Width, (viewport).Height), new Color((byte)0, (byte)0, (byte)0, (byte)alpha));
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
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		if (state != SplashSceneState.stopped)
		{
			Viewport viewport = base.GraphicsDevice.Viewport;
			Rectangle dest = default(Rectangle);
			(dest) = new Rectangle(0, 0, (viewport).Width, (viewport).Height);
			base.GraphicsDevice.Clear(Color.Black);
			if (displaySplash & (currentSplash != null))
			{
				base.SpriteBatch.Draw(currentSplash, dest, Color.White);
			}
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
			base.SpriteBatch.DrawString("v2.10", new Vector2(730f, 550f), Color.AliceBlue, 0f, Vector2.Zero, 0.5f, (SpriteEffects)0, 1f);
		}
	}

	public override void Update(GameTime gameTime)
	{
		stateTimer += gameTime.ElapsedGameTime;
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
			else if ((double)(showtime - fadetime) < stateTimer.TotalMilliseconds)
			{
				num *= (double)showtime - stateTimer.TotalMilliseconds;
				currentFade = Convert.ToInt32(num);
			}
			else
			{
				currentFade = 255;
			}
			if (stateTimer.TotalMilliseconds > (double)showtime)
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
