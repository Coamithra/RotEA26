using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class MenuSubWithSkull : MenuSub1
{
	private Texture2D skull;

	private Texture2D title;

	public MenuSubWithSkull(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		skull = Content.Load<Texture2D>("GFX/Menu/evilskull");
		title = Content.Load<Texture2D>("GFX/Menu/title");
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		float num = base.Game.GraphicsDevice.PresentationParameters.BackBufferWidth;
		float num2 = base.Game.GraphicsDevice.PresentationParameters.BackBufferHeight;
		int num3 = Convert.ToInt16(0.05f * num2);
		int num4 = Convert.ToInt16(0.05f * num);
		int num5 = Convert.ToInt16(0.25f * num);
		int num6 = Convert.ToInt16((float)skull.Height / (float)skull.Width * (float)num5);
		int num7 = Convert.ToInt16(0.05f * num2);
		int num8 = Convert.ToInt16(0.35f * num);
		int num9 = Convert.ToInt16(0.55f * num);
		int num10 = Convert.ToInt16((float)title.Height / (float)title.Width * (float)num9);
		Rectangle dest = default(Rectangle);
		(dest) = new Rectangle(num4, num3, num5, num6);
		Rectangle dest2 = default(Rectangle);
		(dest2) = new Rectangle(num8, num7, num9, num10);
		base.SpriteBatch.Draw(skull, dest, Color.White);
		base.SpriteBatch.Draw(title, dest2, Color.White);
		base.DrawMenu(gameTime, 75f);
	}
}
