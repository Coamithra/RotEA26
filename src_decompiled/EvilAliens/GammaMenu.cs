using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)this).Initialize();
		starfieldPos = Vector2.Zero;
		((DrawableGameComponent)this).DrawOrder = 2;
		curframe = 0f;
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
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
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0341: Unknown result type (might be due to invalid IL or missing references)
		//IL_0351: Unknown result type (might be due to invalid IL or missing references)
		//IL_0368: Unknown result type (might be due to invalid IL or missing references)
		//IL_036e: Unknown result type (might be due to invalid IL or missing references)
		//IL_037e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0395: Unknown result type (might be due to invalid IL or missing references)
		//IL_039b: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0441: Unknown result type (might be due to invalid IL or missing references)
		//IL_0448: Unknown result type (might be due to invalid IL or missing references)
		//IL_044d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0457: Unknown result type (might be due to invalid IL or missing references)
		//IL_045e: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)this).Draw(gameTime);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
		base.SpriteBatch.Draw(starfield, starfieldPos);
		base.SpriteBatch.Draw(starfield, starfieldPos - new Vector2(0f, (float)starfield.Height));
		base.SpriteBatch.Draw(starfield, starfieldPos + new Vector2(0f, (float)starfield.Height));
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
		int num5 = ufo.Width - (ufoAnimation.columns - 1) * ufoAnimation.separatingspace;
		num5 /= ufoAnimation.columns;
		int num6 = ufo.Height - (ufoAnimation.rows - 1) * ufoAnimation.separatingspace;
		num6 /= ufoAnimation.rows;
		Rectangle source = default(Rectangle);
		((Rectangle)(ref source))._002Ector(num4 * (num5 + ufoAnimation.separatingspace), num3 * (num6 + ufoAnimation.separatingspace), num5, num6);
		float num7 = (float)((Rectangle)(ref General.SafeZone)).Left + (float)num5 / 4f;
		float num8 = (float)((Rectangle)(ref General.SafeZone)).Top + (float)num6 / 4f;
		float num9 = (float)((Rectangle)(ref General.SafeZone)).Right - (float)num5 / 4f;
		float num10 = (float)((Rectangle)(ref General.SafeZone)).Bottom - (float)num6 / 4f;
		base.SpriteBatch.Draw(ufo, source, new Vector2(num7, num8), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num9, num8), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num7, num10), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num9, num10), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
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
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		float num = 1f;
		Vector2 val = default(Vector2);
		((Vector2)(ref val))._002Ector((float)(415 - barUnlit.Width / 2), 205f);
		Vector2 val2 = val;
		Vector2 val3 = default(Vector2);
		((Vector2)(ref val3))._002Ector(-16f, 13f);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		Color aliceBlue = Color.AliceBlue;
		float num2 = 1f;
		base.SpriteBatch.Draw(barUnlit, val2 + val3, 0f, Vector2.One * num, center: false, new Color(aliceBlue, num2));
		float num3 = 1f - (Settings.GetInstance().Gamma - 0.6f) / 1.6f;
		if (num3 > 0f)
		{
			float num4 = (float)Math.Round(21f + 75f * num3);
			base.SpriteBatch.Draw(barLit, new Rectangle(0, 0, (int)num4, barLit.Height), val + val3, 0f, 1f, center: false, new Color(aliceBlue, num2));
			base.SpriteBatch.Draw(barEdge, val + val3 + new Vector2(num4, 0f), 0f, Vector2.One, center: false, new Color(aliceBlue, num2));
		}
	}

	public override void Update(GameTime gameTime)
	{
		((GameComponent)this).Update(gameTime);
		ref Vector2 reference = ref starfieldPos;
		reference.Y += (float)gameTime.ElapsedGameTime.TotalSeconds * 20f;
		if (starfieldPos.Y > 600f)
		{
			ref Vector2 reference2 = ref starfieldPos;
			reference2.Y -= (float)starfield.Height;
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
