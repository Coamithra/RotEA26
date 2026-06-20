using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

// The main-menu header. (Name kept for MenuScene's sake; the "skull" is gone —
// see below.) Revenge reskin: the old grey "Revenge of the Evil Aliens" text and
// the separate evilskull are both retired. The new title-revenged logo already
// carries the alien/UFO mascot, so the skull was double-billing and crowding it.
// The logo is high-res (1642x656); Stage 10 draws it straight into the scene (the
// menu's render-resolution offscreen target) via the SpriteBatchWrapper, so it's
// crisp at window resolution AND picks up the same main bloom as everything else —
// no separate overlay pass — re-centred and enlarged into the freed space, with an
// arcade pulse + subtle wobble.
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
		// The chroma-keyed title is straight alpha; the game works in premultiplied
		// alpha, so convert it once at load.
		TextureUtil.Premultiply(title);
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

		// Stage 10: the title is drawn straight into the scene (the menu's render-sized
		// offscreen target — this runs inside MenuSub1.Draw while it's bound), so it's
		// crisp at the unified render resolution AND it picks up the SAME bloom as
		// everything else (the old native-res overlay had its own separate glow pass).
		// AspectFit the 2.5:1 logo, undistorted, into a horizontally-centred 540x210 slot
		// near the top of the 800x600 design surface; pulse/wobble pivot about its centre.
		float fit = Math.Min(540f / (float)title.Width, 210f / (float)title.Height);
		Vector2 titleCentre = new Vector2(400f, 135f + (float)bob);
		base.SpriteBatch.Draw(title, titleCentre, wobble, fit * pulse, center: true, Color.White);

		base.DrawMenu(gameTime, 75f);
	}
}
