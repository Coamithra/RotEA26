using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;

namespace EvilAliens;

internal class NewPreviewScene : Scene
{
	private enum State
	{
		PreWait,
		Explanation,
		Video,
		UpsellScreenFade,
		UpsellScreen
	}

	public delegate void ExitEvent();

	private const double TEXT_START_TIME = 28.0;

	private const double TEXT_STOP_TIME = 66.0;

	private bool showExplanation;

	private Video video;

	private VideoPlayer player;

	private State state;

	private Timer timer;

	private float fade;

	private List<Texture2D> splashScreens;

	private List<Texture2D> screenshots;

	private Texture2D blank;

	private List<Texture2D> finalSplashes;

	private Texture2D upsell;

	private Texture2D AButton;

	private Texture2D BButton;

	private SpriteFont font;

	public ExitEvent onExit;

	private SoundManager soundManager;

	private List<string> reviewTexts;

	public NewPreviewScene(Game game)
		: base(game)
	{
		timer = new Timer(1f, repeating: false);
		splashScreens = new List<Texture2D>();
		screenshots = new List<Texture2D>();
		finalSplashes = new List<Texture2D>();
		soundManager = ServiceHelper.Get<ISoundManagerService>().SoundManager;
		reviewTexts = new List<string>();
		reviewTexts.Add("Three full missions");
		reviewTexts.Add("Seven special challenge missions");
		reviewTexts.Add("Unlockable awards,\ndifficulties and cheats");
		reviewTexts.Add("Up to 4 player local play");
		reviewTexts.Add("\"This game I loved!\"\n    - XNARoundup");
		reviewTexts.Add("\"Not one to miss\"\n    - Indie Tank");
		reviewTexts.Add("\"Can't help but adore it\"\n    - XNPlay");
		reviewTexts.Add("\"Recommended for any shmup fans\nwho want a good challenge\"\n    - Small Cave Games");
	}

	protected override void LoadContent()
	{
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Expected O, but got Unknown
		base.LoadContent();
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
		blank = Content.Load<Texture2D>("GFX/Menu/blank");
		upsell = Content.Load<Texture2D>("GFX/Preview/poster");
		AButton = Content.Load<Texture2D>("GFX/Preview/small_face_a");
		BButton = Content.Load<Texture2D>("GFX/Preview/small_face_b");
		player = new VideoPlayer();
		video = Content.Load<Video>("VFX/AliensPromoNew");
	}

	public override void Initialize()
	{
		base.Initialize();
		setState(State.PreWait);
		base.SoundManager.StopMusic();
	}

	public void Setup(bool showExplanation)
	{
		this.showExplanation = showExplanation;
	}

	private void setState(State newState)
	{
		State state = this.state;
		State state2 = state;
		if (state2 == State.Video)
		{
			player.Stop();
		}
		this.state = newState;
		switch (this.state)
		{
		case State.PreWait:
			soundManager.StopMusic();
			timer.Duration = 2000f;
			break;
		case State.UpsellScreenFade:
			fade = 0f;
			timer.Duration = 4500f;
			break;
		case State.UpsellScreen:
			fade = 1f;
			timer.Duration = 0f;
			break;
		case State.Explanation:
			fade = 1f;
			timer.Duration = 9000f;
			break;
		case State.Video:
			player.Play(video);
			break;
		}
		timer.Reset();
		timer.Start();
	}

	public override void Update(GameTime gameTime)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		timer.Update(gameTime);
		switch (state)
		{
		case State.PreWait:
			if (timer.Finished)
			{
				if (showExplanation)
				{
					setState(State.Explanation);
				}
				else
				{
					setState(State.Video);
				}
			}
			break;
		case State.Video:
			if ((int)player.State == 0 || player.PlayPosition.TotalSeconds > 83.0)
			{
				setState(State.UpsellScreen);
			}
			break;
		case State.UpsellScreenFade:
		{
			float duration = timer.Duration;
			if (timer.TimeElapsed < duration)
			{
				fade = timer.TimeElapsed / duration;
			}
			else
			{
				fade = 1f;
			}
			if (timer.Finished)
			{
				setState(State.UpsellScreen);
			}
			break;
		}
		case State.Explanation:
			if (timer.TimeLeft < 2000f)
			{
				fade = timer.TimeLeft / 2000f;
			}
			if (timer.Finished)
			{
				setState(State.Video);
			}
			break;
		}
		handleInput();
	}

	private void handleInput()
	{
		if (base.InputHandler.Pressed(MyKeys.Right))
		{
			timer.Duration = 0f;
			timer.Reset();
		}
		if (state == State.UpsellScreen || state == State.UpsellScreenFade)
		{
			bool flag = false;
			bool flag2 = false;
			int num = -1;
			flag2 |= base.InputHandler.Pressed(MyKeys.Enter);
			flag |= base.InputHandler.Pressed(MyKeys.Esc);
			for (int i = 0; i < 4; i++)
			{
				if (base.InputHandler.PadPressed(PadKeys.A, i))
				{
					flag2 = true;
					num = i;
				}
				flag |= base.InputHandler.PadPressed(PadKeys.B, i);
				flag |= base.InputHandler.PadPressed(PadKeys.Back, i);
			}
			if (!General.IsTrial)
			{
				flag = true;
			}
			if (flag && onExit != null)
			{
				onExit();
			}
			if (flag2)
			{
				buySelected(num);
			}
		}
		else
		{
			bool flag3 = false;
			flag3 |= base.InputHandler.Pressed(MyKeys.Enter);
			flag3 |= base.InputHandler.Pressed(MyKeys.Esc);
			for (int j = 0; j < 4; j++)
			{
				flag3 |= base.InputHandler.PadPressed(PadKeys.A, j);
				flag3 |= base.InputHandler.PadPressed(PadKeys.B, j);
				flag3 |= base.InputHandler.PadPressed(PadKeys.Back, j);
				flag3 |= base.InputHandler.PadPressed(PadKeys.Start, j);
			}
			if (flag3)
			{
				setState(State.UpsellScreenFade);
			}
		}
	}

	private void buySelected(int player)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Invalid comparison between Unknown and I4
		bool flag = false;
		for (int i = 0; i < ((ReadOnlyCollection<SignedInGamer>)(object)Gamer.SignedInGamers).Count; i++)
		{
			if ((int)((ReadOnlyCollection<SignedInGamer>)(object)Gamer.SignedInGamers)[i].PlayerIndex == player)
			{
				flag = true;
				tryBuy(((ReadOnlyCollection<SignedInGamer>)(object)Gamer.SignedInGamers)[i]);
			}
		}
		if (!flag && !Guide.IsVisible)
		{
			try
			{
				Guide.ShowSignIn(1, true);
			}
			catch (Exception)
			{
			}
		}
	}

	private void tryBuy(SignedInGamer gamer)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		if (gamer.IsSignedInToLive)
		{
			if (gamer.Privileges.AllowPurchaseContent)
			{
				if (!Guide.IsVisible)
				{
					try
					{
						Guide.ShowMarketplace(gamer.PlayerIndex);
					}
					catch (Exception)
					{
					}
				}
			}
			else
			{
				showErrorBlade("You are not allowed to purchase content with this account. Please sign in with a different account to purchase Revenge of the Evil Aliens.", gamer.PlayerIndex);
			}
		}
		else
		{
			showErrorBlade("You are not signed in to LIVE. Please sign in to purchase Revenge of the Evil Aliens.", gamer.PlayerIndex);
		}
	}

	private void showErrorBlade(string p, PlayerIndex player)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		if (Guide.IsVisible)
		{
			return;
		}
		try
		{
			Guide.BeginShowMessageBox(player, "Error", p, (IEnumerable<string>)new string[1] { "Ok" }, 0, (MessageBoxIcon)1, (AsyncCallback)null, (object)null);
		}
		catch (Exception)
		{
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0423: Unknown result type (might be due to invalid IL or missing references)
		//IL_0428: Unknown result type (might be due to invalid IL or missing references)
		//IL_0433: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		//IL_0256: Unknown result type (might be due to invalid IL or missing references)
		//IL_025b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0266: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0327: Unknown result type (might be due to invalid IL or missing references)
		//IL_0347: Unknown result type (might be due to invalid IL or missing references)
		//IL_0354: Unknown result type (might be due to invalid IL or missing references)
		//IL_035f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0378: Unknown result type (might be due to invalid IL or missing references)
		//IL_037d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0388: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		base.GraphicsDevice.Clear(Color.Black);
		base.Draw(gameTime);
		switch (state)
		{
		case State.Video:
		{
			Texture2D texture = player.GetTexture();
			if (texture != null)
			{
				base.SpriteBatch.Draw(texture, new Rectangle(0, 0, 800, 600), Color.White);
			}
			double num9 = player.PlayPosition.TotalSeconds - 28.0;
			double num10 = 38.0;
			if (num9 > 0.0 && num9 < num10)
			{
				float scale = 1.5f;
				double num11 = num10 / (double)reviewTexts.Count;
				int num12 = (int)(num9 / num11);
				float num13 = MyMath.Mod((float)num9, (float)num11);
				float num14 = (float)num11 / 15f;
				if (num12 < 0)
				{
					num12 = 0;
				}
				if (num12 > reviewTexts.Count)
				{
					num9 = reviewTexts.Count - 1;
				}
				if (num13 < num14)
				{
					scale = MyMath.PowerCurve(3.7f, 1.5f, 2f, num13 / num14);
				}
				base.SpriteBatch.DrawString(reviewTexts[num12], new Vector2(401.5f, 201.5f), Color.Black, 0f, centered: true, scale, (SpriteEffects)0, 1f);
				base.SpriteBatch.DrawString(reviewTexts[num12], new Vector2(400f, 200f), Color.LightBlue, 0f, centered: true, scale, (SpriteEffects)0, 1f);
			}
			break;
		}
		case State.PreWait:
		{
			float num8 = 0f;
			if (timer.TimeElapsed < 1000f)
			{
				num8 = 1f - timer.TimeElapsed / 1000f;
			}
			base.SpriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(new Vector4(1f, 1f, 1f, num8)));
			break;
		}
		case State.UpsellScreenFade:
		case State.UpsellScreen:
		{
			base.SpriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), Color.Black);
			float num = 0.5f;
			float num2 = 0.8f;
			base.SpriteBatch.Draw(upsell, new Rectangle(0, 0, 800, 600), new Color(Color.White, fade));
			float num3 = (General.SafeZone).Left;
			float num4 = (float)(General.SafeZone).Bottom - MathHelper.Max((float)AButton.Height * num, font.MeasureString("yo").Y * num2);
			float num5 = num3 + (float)AButton.Width * num + font.MeasureString(" ").X * num2;
			float num6 = (float)(General.SafeZone).Right - font.MeasureString("buy").X * num2;
			float num7 = num6 - (float)BButton.Width * num - font.MeasureString(" ").X * num2;
			base.SpriteBatch.Draw(BButton, new Vector2(num3, num4), 0f, num, center: false, new Color(Color.White, fade));
			base.SpriteBatch.DrawString("forsake humanity", new Vector2(num5, num4), new Color(Color.AliceBlue, fade), 0f, centered: false, num2, (SpriteEffects)0, 1f);
			base.SpriteBatch.Draw(AButton, new Vector2(num7, num4), 0f, num, center: false, new Color(Color.White, fade));
			base.SpriteBatch.DrawString("buy", new Vector2(num6, num4), new Color(Color.AliceBlue, fade), 0f, centered: false, num2, (SpriteEffects)0, 1f);
			break;
		}
		case State.Explanation:
		{
			string text = "This game mode is not available in the trial version\nof Revenge of the Evil Aliens.\n\nTo access this mode, please buy the full game.";
			base.SpriteBatch.DrawString(text, new Vector2(400f, 300f), new Color(Color.AliceBlue, fade), 0f, centered: true, 0.8f, (SpriteEffects)0, 1f);
			break;
		}
		}
	}
}
