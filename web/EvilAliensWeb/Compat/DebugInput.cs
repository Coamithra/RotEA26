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

		// Per-MyKeys persistent "held" flag for the on-screen touch controls (Stage 9).
		// Unlike holdTicks (a tick countdown for scripted taps), these stay down until JS
		// clears them on touchend/cancel — an on-screen D-pad/fire button held with a
		// finger behaves like a physical key held across many frames.
		private static readonly bool[] touchHeld =
			new bool[Enum.GetValues(typeof(EvilAliens.MyKeys)).Length];

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

		// JS bridge for the on-screen touch controls (eaHold in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugHold', key, down). Sets/clears the
		// persistent held state for `key` so it reads as down for as long as the finger
		// stays on the button. Unknown keys are ignored.
		[JSInvokable("debugHold")]
		public static void Hold(string key, bool down)
		{
			if (TryMap(key, out EvilAliens.MyKeys mk))
			{
				touchHeld[(int)mk] = down;
			}
		}

		// JS bridge for QA/demo of the cinematic slow-motion effect (eaSlowmo in
		// wwwroot/index.html): DotNet.invokeMethod('EvilAliensWeb', 'debugSlowmo', seconds).
		// Triggers the same slow-motion burst the fully-powered 1up does (Oracle.SetSlowmotion)
		// so the ghost-trail look can be seen on demand without grinding a powerup. The Oracle
		// service is registered for the whole game's life, so this only no-ops meaningfully in a
		// menu because Oracle.Update resets slowmo to 1f whenever no player ship is alive — i.e.
		// it bites only inside a level with a live ship. Not gameplay input. The null guard
		// below is purely defensive (before the game is constructed).
		[JSInvokable("debugSlowmo")]
		public static void Slowmo(float seconds)
		{
			if (seconds <= 0f)
			{
				seconds = 12f;
			}
			EvilAliens.IOracleService svc = EvilAliens.ServiceHelper.Get<EvilAliens.IOracleService>();
			if (svc?.Oracle == null)
			{
				Console.WriteLine("[debug] eaSlowmo: oracle not ready (game not constructed yet)");
				return;
			}
			svc.Oracle.SetSlowmotion(seconds);
			Console.WriteLine("[debug] eaSlowmo " + seconds + "s");
		}

		// JS bridge for QA/demo of the screen shake (eaShake in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugShake', trauma). Adds shake trauma
		// (0..1; 0/omitted => a solid 0.6 burst) so the camera shake can be seen/tuned on
		// demand anywhere — it's a pure present-blit effect, so it works even in a menu.
		[JSInvokable("debugShake")]
		public static void Shake(float trauma)
		{
			if (trauma <= 0f)
			{
				trauma = 0.6f;
			}
			Juice.AddTrauma(trauma > 1f ? 1f : trauma);
			Console.WriteLine("[debug] eaShake " + trauma);
		}

		// JS bridge for QA/demo of the hit-stop (eaHitstop in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugHitstop', ms). Freezes game time for
		// `ms` milliseconds of real time (0/omitted => 120ms) — most visible in a level
		// with things moving, e.g. ?level=Level1.
		[JSInvokable("debugHitstop")]
		public static void Hitstop(float ms)
		{
			if (ms <= 0f)
			{
				ms = 120f;
			}
			Juice.AddHitStop(ms / 1000f);
			Console.WriteLine("[debug] eaHitstop " + ms + "ms");
		}

		// Called once per MyKeys per InputHandler tick: returns true (and decrements)
		// while injected ticks remain. Folded into the keyboard `flag`, so the existing
		// press/hold edge detection treats it exactly like a held physical key — first
		// forced tick = a fresh Pressed edge, the rest = Down, then a clean release.
		internal static bool Consume(int idx)
		{
			if (idx < 0 || idx >= holdTicks.Length)
			{
				return false;
			}
			// A scripted tap (countdown) OR a held touch button both read as "down".
			if (holdTicks[idx] > 0)
			{
				holdTicks[idx]--;
				return true;
			}
			return touchHeld[idx];
		}

		private static bool TryMap(string key, out EvilAliens.MyKeys mk)
		{
			mk = default(EvilAliens.MyKeys);
			if (string.IsNullOrWhiteSpace(key))
			{
				return false;
			}
			string k = key.Trim();
			// Enum.TryParse ALSO accepts raw numeric strings ("42") and any undefined
			// underlying value, which would flow straight into holdTicks[(int)mk]/
			// touchHeld[(int)mk] in Press/Hold and throw IndexOutOfRangeException. Only a
			// real, defined member name may map — reject a leading digit/sign and verify
			// the parsed value is actually defined.
			if (k.Length > 0 && !char.IsDigit(k[0]) && k[0] != '+' && k[0] != '-'
				&& Enum.TryParse<EvilAliens.MyKeys>(k, ignoreCase: true, out mk)
				&& Enum.IsDefined(typeof(EvilAliens.MyKeys), mk))
			{
				return true;
			}
			// Clear any bogus value a successful-but-out-of-range TryParse left in mk before
			// falling through to the alias switch (which sets mk itself on a hit).
			mk = default(EvilAliens.MyKeys);
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
