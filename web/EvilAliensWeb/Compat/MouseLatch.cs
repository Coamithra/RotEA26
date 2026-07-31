using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
	// Rescues a mouse click SHORTER than one game tick (card 724f2abc).
	//
	// Exactly the problem Compat/DebugInput.cs opens with, for real DOM mouse buttons rather
	// than scripted keys: InputHandler polls Mouse.GetState() once per tick and edge-detects, so a
	// mousedown/mouseup pair that both land BETWEEN two polls reads Released at both samples
	// and Pressed(MyKeys.Mouse1) never fires. The cursor POSITION survives it (KNI retains the
	// last mousemove), which is what makes the symptom so misleading: a menu row hover-
	// highlights under the cursor and simply never invokes, reading as a menu bug when in fact
	// every Pressed(MyKeys.Mouse1) consumer is affected -- MenuSub1.HandleMouse, StartScreen's
	// "Press Start", SplashScene's skip. A human click is 50-150 ms (3-9 ticks at 60 Hz) and is
	// safe; a click on a hitching frame, or any automated one, is not (measured: a CDP
	// left_click holds the button for 0.9 ms).
	//
	// Fix, same shape as DebugInput's: don't race the poll. JS pushes the mousedown edge here
	// the moment it happens (wwwroot/index.html, next to eaPointerOnCanvas) and InputHandler
	// ORs it into `held` from INSIDE the tick, so the next tick is guaranteed to see it.
	//
	// Deliberately a FLAG, not a counter: two real clicks inside one tick collapse to one
	// press. Queuing the second would inject a phantom click on a later frame, which is worse
	// than dropping a click no human made.
	public static class MouseLatch
	{
		// Indexed by MyKeys (6 = Mouse1, 7 = Mouse2) so InputHandler can pass its loop index
		// straight through, exactly as it does for DebugInput.Consume.
		private const int Mouse1Key = 6;

		private const int Mouse2Key = 7;

		private static bool mouse1;

		private static bool mouse2;

		// Called from JS on a CANVAS mousedown. Canvas-scoped on purpose: a window-level
		// listener would also latch a game press for every click on the outside-#app UI (the
		// fullscreen button, the touch D-pad, the FPS HUD, the tuning panels), which is exactly
		// the "a clickable panel over a shoot-em-up eats the shots aimed at that corner" problem
		// those are kept off the canvas to avoid.
		[JSInvokable("eaMouseDown")]
		public static void OnMouseDown(int button)
		{
			if (button == 0)
			{
				mouse1 = true;
			}
			else if (button == 2)
			{
				mouse2 = true;
			}
		}

		// True once per latched press, for the one tick that consumes it. Any index that is not
		// a mouse button never latches.
		internal static bool Consume(int idx)
		{
			if (idx == Mouse1Key)
			{
				bool latched = mouse1;
				mouse1 = false;
				return latched;
			}
			if (idx == Mouse2Key)
			{
				bool latched = mouse2;
				mouse2 = false;
				return latched;
			}
			return false;
		}
	}
}
