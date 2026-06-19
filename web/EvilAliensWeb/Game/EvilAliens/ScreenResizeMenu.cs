using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class ScreenResizeMenu : Scene
{
	public delegate void FinishedHandler(object sender);

	private Texture2D starfield;

	private Vector2 starfieldPos;

	private SpriteFont font;

	private Texture2D ufo;

	private AnimationData ufoAnimation;

	private float curframe;

	public event FinishedHandler OnFinished;

	public ScreenResizeMenu(Game game)
		: base(game)
	{
	}

	public override void Initialize()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		starfieldPos = Vector2.Zero;
		base.DrawOrder = 2;
		curframe = 0f;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		starfield = Content.Load<Texture2D>("GFX/Game/Starfield2");
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
		ufo = Content.Load<Texture2D>("GFX/Sprites/ufosheet");
		ufoAnimation = new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f);
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
		base.Draw(gameTime);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
		base.SpriteBatch.Draw(starfield, starfieldPos);
		base.SpriteBatch.Draw(starfield, starfieldPos - new Vector2(0f, (float)starfield.Height));
		base.SpriteBatch.Draw(starfield, starfieldPos + new Vector2(0f, (float)starfield.Height));
		float num = font.LineSpacing;
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		string text = "Modify screen size until";
		Vector2 origin = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f - num * 3f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		text = "all UFOs are clearly visible";
		origin = font.MeasureString(text) / 2f;
		base.SpriteBatch.DrawString(font, text, new Vector2(400f, 300f - num * 2f), Color.AliceBlue, 0f, origin, 1f, (SpriteEffects)0, 1f);
		text = "Use left stick to resize";
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
		(source) = new Rectangle(num4 * (num5 + ufoAnimation.separatingspace), num3 * (num6 + ufoAnimation.separatingspace), num5, num6);
		float num7 = (float)(General.SafeZone).Left + (float)num5 / 4f;
		float num8 = (float)(General.SafeZone).Top + (float)num6 / 4f;
		float num9 = (float)(General.SafeZone).Right - (float)num5 / 4f;
		float num10 = (float)(General.SafeZone).Bottom - (float)num6 / 4f;
		base.SpriteBatch.Draw(ufo, source, new Vector2(num7, num8), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num9, num8), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num7, num10), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
		base.SpriteBatch.Draw(ufo, source, new Vector2(num9, num10), 0f, 1f, center: true, Color.White, (SpriteEffects)0);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
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
			Settings.GetInstance().Scale -= (float)gameTime.ElapsedGameTime.TotalSeconds * 0.2f;
		}
		if (flag2)
		{
			Settings.GetInstance().Scale += (float)gameTime.ElapsedGameTime.TotalSeconds * 0.2f;
		}
		curframe = (curframe + ufoAnimation.fps * (float)gameTime.ElapsedGameTime.TotalSeconds) % (float)(ufoAnimation.rows * ufoAnimation.columns);
		Settings.GetInstance().Scale = MathHelper.Clamp(Settings.GetInstance().Scale, 0.8f, 1f);
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
