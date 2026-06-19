using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;

namespace EvilAliens;

internal class TrailerScene : Scene
{
	public enum TrailerMode
	{
		RocketRiot,
		EvilAliens
	}

	public delegate void FinishedHandler(object sender);

	private Video video;

	private VideoPlayer player;

	private Texture2D videoTexture;

	private TrailerMode mode;

	private bool oldStretchSetting;

	public event FinishedHandler OnFinished;

	public TrailerScene(Game game)
		: base(game)
	{
		((DrawableGameComponent)this).DrawOrder = 2000;
	}

	public void Setup(TrailerMode mode)
	{
		this.mode = mode;
	}

	protected override void LoadContent()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		((DrawableGameComponent)this).LoadContent();
		player = new VideoPlayer();
	}

	public override void Initialize()
	{
		((DrawableGameComponent)this).Initialize();
		base.SoundManager.StopMusic();
		oldStretchSetting = Settings.GetInstance().Stretch;
		string text;
		switch (mode)
		{
		case TrailerMode.RocketRiot:
			text = "Rocket Riot";
			Settings.GetInstance().Stretch = true;
			break;
		case TrailerMode.EvilAliens:
			text = "AliensPromoNew";
			break;
		default:
			throw new Exception("Unknown video");
		}
		video = Content.Load<Video>("VFX/" + text);
		player.Play(video);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		((GameComponent)this).Update(gameTime);
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
		if ((int)player.State == 0 || flag)
		{
			player.Stop();
			base.SoundManager.PlayMusic(Songs.Sjaak);
			Settings.GetInstance().Stretch = oldStretchSetting;
			if (this.OnFinished != null)
			{
				this.OnFinished(this);
			}
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)this).Draw(gameTime);
		videoTexture = player.GetTexture();
		if (videoTexture == null)
		{
			return;
		}
		switch (mode)
		{
		case TrailerMode.RocketRiot:
		{
			Rectangle source = default(Rectangle);
			if (GraphicsAdapter.DefaultAdapter.IsWideScreen)
			{
				((Rectangle)(ref source))._002Ector(0, 0, videoTexture.Width, videoTexture.Height);
			}
			else
			{
				int num = (int)(4f * (float)videoTexture.Height / 3f);
				((Rectangle)(ref source))._002Ector((videoTexture.Width - num) / 2, 0, num, videoTexture.Height);
			}
			base.SpriteBatch.Draw(videoTexture, source, new Rectangle(0, 0, 800, 600), Color.White);
			break;
		}
		case TrailerMode.EvilAliens:
			base.SpriteBatch.Draw(videoTexture, new Rectangle(0, 0, 800, 600), Color.White);
			break;
		}
	}
}
