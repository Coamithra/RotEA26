using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class Darkener : DrawableGameComponent
{
	private Texture2D black;

	private string buttonTipA;

	private string buttonTipB;

	private bool displayButtonTips;

	private SpriteFont font;

	private Texture2D AButton;

	private Texture2D BButton;

	public Darkener(Game game, string buttonTipA, string buttonTipB)
		: base(game)
	{
		base.DrawOrder = 1800;
		this.buttonTipA = buttonTipA;
		this.buttonTipB = buttonTipB;
	}

	public Darkener(Game game)
		: base(game)
	{
		base.DrawOrder = 1800;
		buttonTipA = "";
		buttonTipB = "";
	}

	public void SetButtonTips(string A, string B)
	{
		buttonTipA = A;
		buttonTipB = B;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		black = contentManager.Load<Texture2D>("GFX/Menu/blank");
		font = contentManager.Load<SpriteFont>("GFX/Menu/menufont");
		AButton = contentManager.Load<Texture2D>("GFX/Preview/small_face_a");
		BButton = contentManager.Load<Texture2D>("GFX/Preview/small_face_b");
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		_ = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
		spriteBatchWrapper.Draw(black, new Rectangle(0, 0, 800, 600), new Color(new Vector4(0f, 0f, 0f, 0.5f)));
		drawButtons();
	}

	private void drawButtons()
	{
		float num = 0.5f;
		float num2 = 0.8f;
		float num3 = (General.SafeZone).Left;
		float num4 = (float)(General.SafeZone).Bottom - MathHelper.Max((float)AButton.LogicalHeight() * num, font.MeasureString("yo").Y * num2);
		float num5 = num3 + (float)AButton.LogicalWidth() * num + font.MeasureString(" ").X * num2;
		float num6 = (float)(General.SafeZone).Right - font.MeasureString(buttonTipA).X * num2;
		float num7 = num6 - (float)BButton.LogicalWidth() * num - font.MeasureString(" ").X * num2;
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		if (buttonTipB != "")
		{
			spriteBatchWrapper.Draw(BButton, new Vector2(num3, num4), 0f, num, center: false, Color.White);
			spriteBatchWrapper.DrawString(buttonTipB, new Vector2(num5, num4), Color.AliceBlue, 0f, centered: false, num2, (SpriteEffects)0, 1f);
		}
		if (buttonTipA != "")
		{
			spriteBatchWrapper.Draw(AButton, new Vector2(num7, num4), 0f, num, center: false, Color.White);
			spriteBatchWrapper.DrawString(buttonTipA, new Vector2(num6, num4), Color.AliceBlue, 0f, centered: false, num2, (SpriteEffects)0, 1f);
		}
	}
}
