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
	// Deliberately a FLAG, not a counter: two real clicks inside one tick collapse to one press.
	// Queuing the second would inject a phantom click on a later frame, which is worse than
	// dropping a click no human made. Clicks in CONSECUTIVE ticks collapse too -- `held` is
	// continuous across them, so InputHandler's pressedAndIdle sees no rising edge on the second.
	// So an automated click LOOP needs a gap tick between clicks, exactly as DebugInput says of
	// repeated eaPress taps; a single click needs nothing.
	public static class MouseLatch
	{
		// Indexed by MyKeys so InputHandler can pass its loop index straight through, exactly as
		// it does for DebugInput.Consume. Derived from the enum rather than written as 6/7, so a
		// reordered MyKeys cannot silently latch the wrong key.
		private const int Mouse1Key = (int)EvilAliens.MyKeys.Mouse1;

		private const int Mouse2Key = (int)EvilAliens.MyKeys.Mouse2;

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

		// --- Clicks that are not on the canvas are not game input (card 0fe23476) ---
		//
		// The latch above is canvas-scoped for exactly that reason, but the latch is only the
		// sub-tick RESCUE -- the primary read is InputHandler polling Mouse.GetState(), and
		// KNI's own listeners are on the WINDOW (nkast.Wasm.Dom Window.js: 'mousedown' /
		// 'mouseup' / 'mousemove'). So a click on any outside-#app UI is still delivered to the
		// game at that cursor position, and the game happily acts on it. The reported symptom
		// was the join-by-code flow: the room-code prompt (wwwroot/webrtc.js promptCode) is a
		// DOM overlay drawn over a live NetStatusMenu whose single entry is CANCEL, and the
		// JOIN button sits right over that row -- so clicking JOIN cancelled the join. Nothing
		// about the DOM z-order could fix it; the game never sees the DOM event, only the
		// button state. The same leak fires the fullscreen button, the FPS HUD tag and the
		// tuning panels into the menu (and the ship) underneath them.
		//
		// So JS flags a press that began off-canvas and the mouse buttons read as released for
		// as long as it lasts. It is a LEVEL, not an edge: releasing on the physical mouseup
		// (rather than after one tick) is what stops a drag that starts on an overlay and ends
		// over the canvas from landing as a click, and the swallow-until-release below is what
		// stops the release itself from reading as a fresh press.
		private static bool suppressed;

		// True from an off-canvas pointerdown until its pointerup. Set/cleared from JS
		// (wwwroot/index.html, alongside eaMouseDown).
		[JSInvokable("eaMouseSuppress")]
		public static void SetSuppressed(bool value)
		{
			if (value)
			{
				suppressed = true;
				// A press that started off-canvas must not become a press the moment it is
				// released, so drop anything the canvas latch had already banked this tick.
				mouse1 = false;
				mouse2 = false;
			}
			else
			{
				suppressed = false;
			}
		}

		// Called once per tick per button with the raw physical state. While an off-canvas press
		// is live the button reads released; once JS clears the flag it stays swallowed until
		// that button is PHYSICALLY released, so the tail of the same press cannot produce a
		// rising edge in InputHandler (the phantom click a plain flag would inject). Per-button,
		// because the two are polled in the same tick with independent states.
		private static bool swallow1;

		private static bool swallow2;

		// What this button reported LAST tick. The suppression flag is per-GESTURE in JS but
		// applied per-BUTTON here, so without this an off-canvas press would cancel an unrelated
		// button that was ALREADY down: hold fire on the canvas, right-click the FPS HUD tag,
		// and the ship stops shooting mid-hold until you release and re-press. A press that was
		// already being reported carries on; only a press that STARTS while suppressed is
		// swallowed, which is the whole intent.
		private static bool reported1;

		private static bool reported2;

		internal static bool FilterOffCanvas(int idx, bool rawDown)
		{
			ref bool swallow = ref swallow1;
			ref bool reported = ref reported1;
			if (idx == Mouse2Key)
			{
				swallow = ref swallow2;
				reported = ref reported2;
			}
			else if (idx != Mouse1Key)
			{
				return rawDown;
			}
			bool result;
			if (suppressed && !(reported && rawDown))
			{
				swallow = true;
				result = false;
			}
			else if (swallow)
			{
				if (rawDown)
				{
					result = false;
				}
				else
				{
					swallow = false;
					result = false;
				}
			}
			else
			{
				result = rawDown;
			}
			reported = result;
			return result;
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
