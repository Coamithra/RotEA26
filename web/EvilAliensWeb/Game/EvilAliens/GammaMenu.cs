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
		float lineH = font.LineSpacing;
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		string line = "Modify Gamma until";
		Vector2 origin = font.MeasureString(line) / 2f;
		base.SpriteBatch.DrawString(font, line, new Vector2(400f, 300f - lineH * 3f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		line = "the crosses are visible";
		origin = font.MeasureString(line) / 2f;
		base.SpriteBatch.DrawString(font, line, new Vector2(400f, 300f - lineH * 2f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		line = "Use left stick to modify";
		origin = font.MeasureString(line) / 2f;
		base.SpriteBatch.DrawString(font, line, new Vector2(400f, 300f + lineH), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		line = "Press A when ready";
		origin = font.MeasureString(line) / 2f;
		base.SpriteBatch.DrawString(font, line, new Vector2(400f, 300f + lineH * 2f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		int frameIndex = (int)curframe;
		int frameRow = frameIndex / ufoAnimation.columns;
		int frameCol = frameIndex % ufoAnimation.columns;
		int cellW = ufo.LogicalWidth() - (ufoAnimation.columns - 1) * ufoAnimation.separatingspace;
		cellW /= ufoAnimation.columns;
		int cellH = ufo.LogicalHeight() - (ufoAnimation.rows - 1) * ufoAnimation.separatingspace;
		cellH /= ufoAnimation.rows;
		Rectangle source = new Rectangle(frameCol * (cellW + ufoAnimation.separatingspace), frameRow * (cellH + ufoAnimation.separatingspace), cellW, cellH);
		float ssf = AlienDrawableGameComponent.SuperSampleFactor(ufoAnimation.TextureName, cellW);
		float leftX = (float)(General.SafeZone).Left + (float)cellW / ssf / 4f;
		float topY = (float)(General.SafeZone).Top + (float)cellH / ssf / 4f;
		float rightX = (float)(General.SafeZone).Right - (float)cellW / ssf / 4f;
		float bottomY = (float)(General.SafeZone).Bottom - (float)cellH / ssf / 4f;
		base.SpriteBatch.Draw(ufo, source, new Vector2(leftX, topY), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(rightX, topY), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(leftX, bottomY), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(rightX, bottomY), 0f, 1f / ssf, center: true, Color.White, (SpriteEffects)0);
		Vector2 crossSize = font.MeasureString("X");
		float firstCrossX = 300f;
		float lastCrossX = 500f;
		for (int i = 0; i < 10; i++)
		{
			float brightness = (float)(i + 1) * 0.1f;
			float crossX = MathHelper.Lerp(firstCrossX, lastCrossX, (float)i / 9f);
			base.SpriteBatch.DrawString(font, "X", new Vector2(crossX, 280f), new Color(new Vector3(brightness)), 0f, crossSize / 2f, 1f, (SpriteEffects)0, 0f);
		}
		drawPowerbar();
	}

	private void drawPowerbar()
	{
		float barScale = 1f;
		Vector2 barPos = new Vector2((float)(415 - barUnlit.LogicalWidth() / 2), 205f);
		Vector2 unlitBarPos = barPos;
		Vector2 barOffset = new Vector2(-16f, 13f);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		Color aliceBlue = Color.AliceBlue;
		float barAlpha = 1f;
		base.SpriteBatch.Draw(barUnlit, unlitBarPos + barOffset, 0f, Vector2.One * barScale, center: false, new Color(aliceBlue, barAlpha));
		float darknessFraction = 1f - (Settings.GetInstance().Gamma - 0.6f) / 1.6f;
		if (darknessFraction > 0f)
		{
			float litWidth = (float)Math.Round(21f + 75f * darknessFraction);
			base.SpriteBatch.Draw(barLit, new Rectangle(0, 0, (int)litWidth, barLit.LogicalHeight()), barPos + barOffset, 0f, 1f, center: false, new Color(aliceBlue, barAlpha));
			base.SpriteBatch.Draw(barEdge, barPos + barOffset + new Vector2(litWidth, 0f), 0f, Vector2.One, center: false, new Color(aliceBlue, barAlpha));
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
		bool brightenHeld = false;
		for (int i = 0; i < 4; i++)
		{
			if (base.InputHandler.PadDown(PadKeys.Up, i) || base.InputHandler.PadDown(PadKeys.Left, i))
			{
				brightenHeld = true;
			}
		}
		brightenHeld |= base.InputHandler.Down(MyKeys.Left) || base.InputHandler.Down(MyKeys.Up);
		bool darkenHeld = false;
		for (int j = 0; j < 4; j++)
		{
			if (base.InputHandler.PadDown(PadKeys.Down, j) || base.InputHandler.PadDown(PadKeys.Right, j))
			{
				darkenHeld = true;
			}
		}
		darkenHeld |= base.InputHandler.Down(MyKeys.Right) || base.InputHandler.Down(MyKeys.Down);
		if (brightenHeld)
		{
			Settings.GetInstance().Gamma += (float)gameTime.ElapsedGameTime.TotalSeconds * 0.65f;
		}
		if (darkenHeld)
		{
			Settings.GetInstance().Gamma -= (float)gameTime.ElapsedGameTime.TotalSeconds * 0.65f;
		}
		Settings.GetInstance().Gamma = MathHelper.Clamp(Settings.GetInstance().Gamma, 0.6f, 2.2f);
		curframe = (curframe + ufoAnimation.fps * (float)gameTime.ElapsedGameTime.TotalSeconds) % (float)(ufoAnimation.rows * ufoAnimation.columns);
		bool dismissPressed = false;
		for (int k = 0; k < 4; k++)
		{
			dismissPressed |= base.InputHandler.PadPressed(PadKeys.A, k);
			dismissPressed |= base.InputHandler.PadPressed(PadKeys.B, k);
			dismissPressed |= base.InputHandler.PadPressed(PadKeys.Back, k);
			dismissPressed |= base.InputHandler.PadPressed(PadKeys.Start, k);
		}
		dismissPressed |= base.InputHandler.Pressed(MyKeys.Enter);
		if ((dismissPressed | base.InputHandler.Pressed(MyKeys.Esc)) && this.OnFinished != null)
		{
			this.OnFinished(this);
		}
	}
}
