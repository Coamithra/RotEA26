using Microsoft.Xna.Framework;

namespace EvilAliens;

// Stage 13 menu reskin — one place for the menu palette so the shared base menus
// (MenuSub1) and the rich main-menu chrome (MenuSubWithSkull) stay in lockstep.
// Idle rows are toxic green (the logo's "EVIL ALIENS" green), the selected row is
// electric violet; both are bright enough to read AND to feed the scene bloom.
// Colours are straight (non-premultiplied) alpha to match the content pipeline.
internal static class MenuTheme
{
	// Row text
	public static readonly Color Idle = new Color(150, 235, 90);       // toxic green
	public static readonly Color Selected = new Color(202, 140, 255);  // electric violet

	// Selected-row glow / aura (use with a per-frame alpha)
	public static readonly Color Glow = new Color(170, 80, 255);       // vivid purple

	// Main-menu HUD frame
	public static readonly Color FrameIdle = new Color(86, 140, 104);     // dim green steel
	public static readonly Color FrameSelected = new Color(196, 120, 255); // bright violet
	public static readonly Color FrameFill = new Color(6, 5, 14, 185);     // translucent dark fill

	// Decor (HUD ring, corner brackets)
	public static readonly Color Decor = new Color(96, 150, 120, 130);

	// Returns `c` with its alpha replaced (RGB untouched — straight alpha).
	public static Color WithAlpha(Color c, int a)
	{
		c.A = (byte)(a < 0 ? 0 : (a > 255 ? 255 : a));
		return c;
	}
}
