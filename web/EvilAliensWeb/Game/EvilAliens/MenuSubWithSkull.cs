using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

// The main-menu header. (Name kept for MenuScene's sake; the "skull" is gone —
// see below.) Revenge reskin: the old grey "Revenge of the Evil Aliens" text and
// the separate evilskull are both retired. The new title-revenged logo already
// carries the alien/UFO mascot, so the skull was double-billing and crowding it.
// The logo is high-res (1642x656), so it's drawn through the native-res
// HiResOverlay — crisp at window resolution instead of being squeezed through the
// 800x600 menu render target — re-centred and enlarged into the freed space, with
// an arcade pulse + subtle wobble.
internal class MenuSubWithSkull : MenuSub1
{
	private Texture2D title;

	public MenuSubWithSkull(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		title = Content.Load<Texture2D>("GFX/Menu/title-revenged");
		// The chroma-keyed title is straight alpha; the game (and overlay) work in
		// premultiplied alpha, so convert it once at load.
		HiResOverlay.Premultiply(title);
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;

		// Arcade marquee feel: a gentle scale "breathe" plus a subtle rotational
		// wobble at a detuned frequency (so it sways rather than ticks), and a tiny
		// vertical bob. Driven off wall-clock game time; pivots about the slot centre.
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		const float TwoPi = 6.28318548f;
		float pulse = 1f + 0.018f * (float)Math.Sin(TwoPi * 0.9f * t);   // ~+/-1.8% breathe
		float wobble = 0.0105f * (float)Math.Sin(TwoPi * 0.55f * t);     // ~+/-0.6 deg sway
		int bob = (int)Math.Round(1.0 * Math.Sin(TwoPi * 0.4f * t));     // +/-1 px design-space

		// Horizontally-centred slot near the top of the 800x600 design surface.
		// AspectFit keeps the 2.5:1 logo undistorted regardless of the slot aspect,
		// and centres it within the slot. The overlay maps this design rect through
		// the presenter transform so it lines up with the rest of the menu.
		Rectangle titleSlot = new Rectangle(130, 30 + bob, 540, 210);
		HiResOverlay.Draw(title, titleSlot, Color.White, OverlayFit.AspectFit, wobble, pulse, glow: true);

		base.DrawMenu(gameTime, 75f);
	}
}
