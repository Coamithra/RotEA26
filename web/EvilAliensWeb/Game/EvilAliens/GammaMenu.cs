using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class GammaMenu : Scene
{
	public delegate void FinishedHandler(object sender);

	private const float MINGAMMA = 0.6f;

	private const float MAXGAMMA = 2.2f;

	private Texture2D starfield;

	private Vector2 starfieldPos;

	private SpriteFont font;

	private Texture2D ufo;

	private AnimationData ufoAnimation;

	private float curframe;

	private Texture2D barUnlit;

	private Texture2D barLit;

	private Texture2D barEdge;

	public event FinishedHandler OnFinished;

	public GammaMenu(Game game)
		: base(game)
	{
	}

	public override void Initialize()
	{
		base.Initialize();
		starfieldPos = Vector2.Zero;
		base.DrawOrder = 2;
		curframe = 0f;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		starfield = Content.Load<Texture2D>("GFX/Game/tileablestarfield");
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
		ufo = Content.Load<Texture2D>("GFX/Sprites/ufosheet");
		ufoAnimation = new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f);
		barLit = Content.Load<Texture2D>("GFX/HUD/BarLit");
		barUnlit = Content.Load<Texture2D>("GFX/HUD/BarUnlit2");
		barEdge = Content.Load<Texture2D>("GFX/HUD/BarLitEdge");
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
		base.SpriteBatch.Draw(starfield, starfieldPos);
		base.SpriteBatch.Draw(starfield, starfieldPos - new Vector2(0f, (float)starfield.LogicalHeight()));
		base.SpriteBatch.Draw(starfield, starfieldPos + new Vector2(0f, (float)starfield.LogicalHeight()));
		float num = font.LineSpacing;
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		string text = "Modify Gamma until";
		Vector2 origin = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f - num * 3f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		text = "the crosses are visible";
		origin = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f - num * 2f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		text = "Use left stick to modify";
		origin = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f + num), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		text = "Press A when ready";
		origin = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f + num * 2f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		int num2 = (int)curframe;
		int num3 = num2 / ufoAnimation.columns;
		int num4 = num2 % ufoAnimation.columns;
		int num5 = ufo.LogicalWidth() - (ufoAnimation.columns - 1) * ufoAnimation.separatingspace;
		num5 /= ufoAnimation.columns;
		int num6 = ufo.LogicalHeight() - (ufoAnimation.rows - 1) * ufoAnimation.separatingspace;
		num6 /= ufoAnimation.rows;
		Rectangle source = default(Rectangle);
		(source) = new Rectangle(num4 * (num5 + ufoAnimation.separatingspace), num3 * (num6 + ufoAnimation.separatingspace), num5, num6);
		float ssf = AlienDrawableGameComponent.SuperSampleFactor(ufoAnimation.TextureName, num5);
		float num7 = (float)(General.SafeZone).Left + (float)num5 / ssf / 4f;
		float num8 = (float)(General.SafeZone).Top + (float)num6 / ssf / 4f;
		float num9 = (float)(General.SafeZone).Right - (float)num5 / ssf / 4f;
		float num10 = (float)(General.SafeZone).Bottom - (float)num6 / ssf / 4f;
		base.SpriteBatch.Draw(ufo, source, new Vector2(num7, num8), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num9, num8), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num7, num10), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num9, num10), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		Vector2 val = font.MeasureString("X");
		float num11 = 300f;
		float num12 = 500f;
		for (int i = 0; i < 10; i++)
		{
			float num13 = (float)(i + 1) * 0.1f;
			float num14 = MathHelper.Lerp(num11, num12, (float)i / 9f);
			base.SpriteBatch.DrawString(font, "X", new Vector2(num14, 280f), new Color(new Vector3(num13)), 0f, val / 2f, 1f, (SpriteEffects)0, 0f);
		}
		drawPowerbar();
	}

	private void drawPowerbar()
	{
		float num = 1f;
		Vector2 val = default(Vector2);
		(val) = new Vector2((float)(415 - barUnlit.LogicalWidth() / 2), 205f);
		Vector2 val2 = val;
		Vector2 val3 = default(Vector2);
		(val3) = new Vector2(-16f, 13f);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		Color aliceBlue = Color.AliceBlue;
		float num2 = 1f;
		base.SpriteBatch.Draw(barUnlit, val2 + val3, 0f, Vector2.One * num, center: false, new Color(aliceBlue, num2));
		float num3 = 1f - (Settings.GetInstance().Gamma - 0.6f) / 1.6f;
		if (num3 > 0f)
		{
			float num4 = (float)Math.Round(21f + 75f * num3);
			base.SpriteBatch.Draw(barLit, new Rectangle(0, 0, (int)num4, barLit.LogicalHeight()), val + val3, 0f, 1f, center: false, new Color(aliceBlue, num2));
			base.SpriteBatch.Draw(barEdge, val + val3 + new Vector2(num4, 0f), 0f, Vector2.One, center: false, new Color(aliceBlue, num2));
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		ref Vector2 reference = ref starfieldPos;
		reference.Y += (float)gameTime.ElapsedGameTime.TotalSeconds * 20f;
		if (starfieldPos.Y > 600f)
		{
			ref Vector2 reference2 = ref starfieldPos;
			reference2.Y -= (float)starfield.LogicalHeight();
		}
		bool flag = false;
		for (int i = 0; i < 4; i++)
		{
			if (base.InputHandler.PadDown(PadKeys.Up, i) || base.InputHandler.PadDown(PadKeys.Left, i))
			{
				flag = true;
			}
		}
		flag |= base.InputHandler.Down(MyKeys.Left) || base.InputHandler.Down(MyKeys.Up);
		bool flag2 = false;
		for (int j = 0; j < 4; j++)
		{
			if (base.InputHandler.PadDown(PadKeys.Down, j) || base.InputHandler.PadDown(PadKeys.Right, j))
			{
				flag2 = true;
			}
		}
		flag2 |= base.InputHandler.Down(MyKeys.Right) || base.InputHandler.Down(MyKeys.Down);
		if (flag)
		{
			Settings.GetInstance().Gamma += (float)gameTime.ElapsedGameTime.TotalSeconds * 0.65f;
		}
		if (flag2)
		{
			Settings.GetInstance().Gamma -= (float)gameTime.ElapsedGameTime.TotalSeconds * 0.65f;
		}
		Settings.GetInstance().Gamma = MathHelper.Clamp(Settings.GetInstance().Gamma, 0.6f, 2.2f);
		curframe = (curframe + ufoAnimation.fps * (float)gameTime.ElapsedGameTime.TotalSeconds) % (float)(ufoAnimation.rows * ufoAnimation.columns);
		bool flag3 = false;
		for (int k = 0; k < 4; k++)
		{
			flag3 |= base.InputHandler.PadPressed(PadKeys.A, k);
			flag3 |= base.InputHandler.PadPressed(PadKeys.B, k);
			flag3 |= base.InputHandler.PadPressed(PadKeys.Back, k);
			flag3 |= base.InputHandler.PadPressed(PadKeys.Start, k);
		}
		flag3 |= base.InputHandler.Pressed(MyKeys.Enter);
		if ((flag3 | base.InputHandler.Pressed(MyKeys.Esc)) && this.OnFinished != null)
		{
			this.OnFinished(this);
		}
	}
}
