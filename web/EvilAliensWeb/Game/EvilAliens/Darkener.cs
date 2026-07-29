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

	// The in-game pause overlay's button tips -- the same layout as
	// MenuScene.drawButtonTips (this method is the 2008 copy of it), with the tip strings
	// supplied by the caller instead of hardcoded "back"/"select".
	private void drawButtons()
	{
		float iconScale = 0.5f;
		float textScale = 0.8f;
		float tipBIconX = (General.SafeZone).Left;
		// Both icons sit on this baseline, so it must clear the TALLER of the two (the 2008
		// original measured AButton's height alone). Same fix as MenuScene.drawButtonTips.
		float tipsY = (float)(General.SafeZone).Bottom - MathHelper.Max(MathHelper.Max((float)AButton.LogicalHeight(), (float)BButton.LogicalHeight()) * iconScale, font.MeasureString("yo").Y * textScale);
		// Each label clears the WIDTH of the icon actually drawn beside it: BButton sits at
		// tipBIconX, AButton at tipAIconX. The 2008 original had the two widths CROSSED (the
		// B tip cleared AButton's, the A icon subtracted BButton's), exactly as
		// MenuScene.drawButtonTips and BragScene.drawButtons did. No-op today -- small_face_a and
		// small_face_b are both 60x60 with no precompiled sibling -- so this changes no pixel;
		// it is here so re-authoring either icon at a different size can't silently misplace
		// a label.
		float tipBTextX = tipBIconX + (float)BButton.LogicalWidth() * iconScale + font.MeasureString(" ").X * textScale;
		float tipATextX = (float)(General.SafeZone).Right - font.MeasureString(buttonTipA).X * textScale;
		float tipAIconX = tipATextX - (float)AButton.LogicalWidth() * iconScale - font.MeasureString(" ").X * textScale;
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		if (buttonTipB != "")
		{
			spriteBatchWrapper.Draw(BButton, new Vector2(tipBIconX, tipsY), 0f, iconScale, center: false, Color.White);
			spriteBatchWrapper.DrawString(buttonTipB, new Vector2(tipBTextX, tipsY), Color.AliceBlue, 0f, centered: false, textScale, (SpriteEffects)0, 1f);
		}
		if (buttonTipA != "")
		{
			spriteBatchWrapper.Draw(AButton, new Vector2(tipAIconX, tipsY), 0f, iconScale, center: false, Color.White);
			spriteBatchWrapper.DrawString(buttonTipA, new Vector2(tipATextX, tipsY), Color.AliceBlue, 0f, centered: false, textScale, (SpriteEffects)0, 1f);
		}
	}
}
