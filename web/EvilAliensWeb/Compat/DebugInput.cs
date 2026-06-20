using System;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
	// Reliable input injection for automated/headless testing, immune to the rAF
	// frame-timing miss that makes synthetic key events so painful here.
	//
	// The root cause of the churn: InputHandler polls Keyboard.GetState() ONCE per game
	// tick and edge-detects. A scripted keydown+keyup fired between two ticks is added
	// and removed before any poll observes it, so the press is silently dropped (the
	// classic "stuck on Press Start, tapping Enter does nothing" loop). Holding a real OS
	// key across a frame works but is fiddly to time and impossible to script reliably.
	//
	// Fix: don't race the poll. JS pushes a key here as a per-key tick COUNTER, and
	// InputHandler drains it from INSIDE the tick (Consume). Because the forced-down
	// state lives in C# and is only ever read/decremented by the game loop, it cannot
	// fall between polls — the next tick is guaranteed to see it.
	//
	// Drive it from the browser console or automation (eaPress is the JS wrapper in
	// wwwroot/index.html):
	//   eaPress('Enter')        // single-frame tap  (menu select / Press Start)
	//   eaPress('Up')           // navigate up one entry
	//   eaPress('Esc')          // back / cancel
	//   eaPress('Left', 30)     // HOLD Left ~30 ticks (gameplay movement)
	// Keys: Up Down Left Right Enter Esc Mouse1 Mouse2 Generic_Start, plus aliases
	// (w/a/s/d, start/select/confirm -> Enter, back/cancel -> Esc, fire/shoot -> Mouse1).
	public static class DebugInput
	{
		// Per-MyKeys countdown of ticks still to force "down". Sized to the enum.
		private static readonly int[] holdTicks =
			new int[Enum.GetValues(typeof(EvilAliens.MyKeys)).Length];

		// JS bridge: DotNet.invokeMethod('EvilAliensWeb', 'debugPress', key, frames).
		// `frames` is how many ticks to hold the key down (>=1; 1 == a single tap).
		// Re-pressing extends to the longest pending hold.
		[JSInvokable("debugPress")]
		public static void Press(string key, int frames)
		{
			if (!TryMap(key, out EvilAliens.MyKeys mk))
			{
				Console.WriteLine("[debug] eaPress: unknown key '" + key + "'");
				return;
			}
			if (frames < 1)
			{
				frames = 1;
			}
			int idx = (int)mk;
			if (frames > holdTicks[idx])
			{
				holdTicks[idx] = frames;
			}
			Console.WriteLine("[debug] eaPress " + mk + " x" + frames + " frame(s)");
		}

		// Called once per MyKeys per InputHandler tick: returns true (and decrements)
		// while injected ticks remain. Folded into the keyboard `flag`, so the existing
		// press/hold edge detection treats it exactly like a held physical key — first
		// forced tick = a fresh Pressed edge, the rest = Down, then a clean release.
		internal static bool Consume(int idx)
		{
			if (idx < 0 || idx >= holdTicks.Length || holdTicks[idx] <= 0)
			{
				return false;
			}
			holdTicks[idx]--;
			return true;
		}

		private static bool TryMap(string key, out EvilAliens.MyKeys mk)
		{
			mk = default(EvilAliens.MyKeys);
			if (string.IsNullOrWhiteSpace(key))
			{
				return false;
			}
			string k = key.Trim();
			if (Enum.TryParse<EvilAliens.MyKeys>(k, ignoreCase: true, out mk))
			{
				return true;
			}
			switch (k.ToLowerInvariant())
			{
			case "up":
			case "w":
				mk = EvilAliens.MyKeys.Up;
				return true;
			case "down":
			case "s":
				mk = EvilAliens.MyKeys.Down;
				return true;
			case "left":
			case "a":
				mk = EvilAliens.MyKeys.Left;
				return true;
			case "right":
			case "d":
				mk = EvilAliens.MyKeys.Right;
				return true;
			case "return":
			case "start":
			case "select":
			case "confirm":
			case "ok":
				mk = EvilAliens.MyKeys.Enter;
				return true;
			case "escape":
			case "back":
			case "cancel":
				mk = EvilAliens.MyKeys.Esc;
				return true;
			case "fire":
			case "shoot":
			case "mouse":
				mk = EvilAliens.MyKeys.Mouse1;
				return true;
			default:
				return false;
			}
		}
	}
}
