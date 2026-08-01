using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
	// Makes the bottom-left "(B) back" button tip CLICKABLE (card 2a4110d0).
	//
	// The tip is a label, not a menu entry: it is drawn by the scene chrome
	// (MenuScene.drawButtonTips, Darkener.drawButtons for the pause overlay, and
	// BragScene.drawButtons -- three verbatim copies of one 2008 layout), never by a
	// DrawMenu, so it records no RecordEntryHit box and MenuSub1.HandleMouse cannot see it.
	// A player who has just learned that every menu row is clickable reasonably clicks it
	// and nothing happens.
	//
	// Rather than teach each of the three drawers to route a click to its own back action --
	// which the pause overlay cannot do anyway, since Darkener only DRAWS the tip while
	// PausedScene owns the input -- a click inside the tip is folded into InputHandler as a
	// synthetic MyKeys.Esc for one tick. Esc is already "back" for every consumer
	// (MenuSub1.HandleInput's backPressed, the pause menu, the brag screen), so all three
	// surfaces are covered by one seam and none of their input owners is touched.
	//
	// The one-frame contract is the same one MenuSub1's entry boxes already run on: Draw
	// records the box, the NEXT Update consumes it. Consume clears the recording, so a frame
	// that draws no tip (i.e. gameplay) leaves nothing behind for a stray click to hit --
	// which is what keeps this out of the "a clickable overlay eats the shots aimed at that
	// corner" trap the canvas-scoped MouseLatch exists to avoid.
	public static class BackTipHit
	{
		// 800x600 design space, matching InputHandler.MousePosition.
		private static Rectangle rect;

		// Live for exactly one frame: set by Record, spent by ConsumeClick. This is the one
		// the INPUT path reads, so a frame that drew no tip offers a stray click nothing.
		private static bool recorded;

		// Ticks since the tip was last drawn. The overlap report runs in Draw, AFTER Update has
		// already spent `recorded`, so it cannot use that flag -- but it must not assert against
		// a box that is no longer on screen either: a screen drawing no tip (or a tip last drawn
		// by a different scene) would otherwise get a verdict about stale geometry, which is
		// worse than no verdict at all given the report IS the guard. So age it instead: Record
		// zeroes it, each tick's ConsumeClick ages it, and the report ignores anything older
		// than one tick.
		private static int rectAge = int.MaxValue;

		// Called by whichever scene chrome drew the B tip this frame. The icon and the label
		// both draw from their TOP-left at the same `top` (the tips baseline), so the box is
		// icon-left..label-right by top..bottom -- pass the safe-zone bottom the baseline was
		// derived from and the height is exact by construction in all three copies.
		public static void Record(float left, float right, float top, float bottom)
		{
			int x = (int)System.Math.Floor(left);
			int y = (int)System.Math.Floor(top);
			rect = new Rectangle(x, y, System.Math.Max(1, (int)System.Math.Ceiling(right) - x), System.Math.Max(1, (int)System.Math.Ceiling(bottom) - y));
			recorded = true;
			rectAge = 0;
		}

		// True when a left-click was PRESSED on the tip drawn last frame. Clears the recording
		// either way, so the box lives exactly one frame.
		//
		// `leftPressed` must be the rising EDGE, not the button level: every other mouse
		// consumer here edge-detects (MenuSub1.HandleMouse goes through InputHandler.Pressed),
		// and a level would make this fire on a press that began somewhere else entirely --
		// mouse-down on a menu row and drag to the corner would back out, and in-game a held
		// fire button plus Esc would un-pause the instant Darkener drew the tip under the
		// resting cursor.
		internal static bool ConsumeClick(Vector2 cursorDesign, bool leftPressed)
		{
			bool hit = recorded && leftPressed && rect.Contains(new Point((int)cursorDesign.X, (int)cursorDesign.Y));
			recorded = false;
			if (rectAge < int.MaxValue)
			{
				rectAge++;
			}
			return hit;
		}

		// The box drawn on the CURRENT or previous frame, for verification (asserting it
		// overlaps no menu entry box -- if it did, one click would both go back AND activate a
		// row). False once the tip has stopped being drawn, so no verdict is ever reported
		// about a box that is not on screen.
		internal static bool TryGetRect(out Rectangle bounds)
		{
			bounds = rect;
			return rectAge <= 1;
		}
	}
}
