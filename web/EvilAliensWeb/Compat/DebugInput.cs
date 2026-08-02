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

		// Esc-suppression window (card b0a2f525). The browser RESERVES Esc to leave DOM
		// fullscreen and it cannot be preventDefault'd -- but that same Esc keydown is ALSO
		// delivered to KNI's keyboard state, so InputHandler would read it and ALSO step back
		// in the menu (Esc doing two things at once). On fullscreenchange->exit the JS side
		// (eaSuppressEsc in index.html) opens a short window here; InputHandler masks the raw
		// keyboard Esc while it's active so leaving fullscreen doesn't also navigate back.
		// escGrace = a minimum window (covers the exit keydown arriving a tick or two AFTER
		// the fullscreenchange event); escGuard = a hard cap so a genuinely held Esc can't
		// keep Esc dead forever (fail-open).
		private static int escGraceTicks;
		private static int escGuardTicks;

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

		// --- scripted CURSOR POSITION (the other half of eaPress, PR #255 follow-up) ---
		//
		// `eaPress('Mouse1')` injects the BUTTON, but every mouse consumer in the game is
		// position-dependent: `MenuSub1.HandleMouse` hit-tests the cursor against the entry
		// boxes, and `BackTipHit` against the back tip's box. The position came only from
		// `Mouse.GetState()`, which a script cannot move -- under `eahl` it is wherever SDL
		// happens to report -- so a scripted click could never land ON anything and the whole
		// mouse surface was unreachable from the project's own automation seam. Every menu
		// click therefore needed a real Chrome pass, including the ones whose only browser-
		// specific ingredient was that they involve a mouse at all.
		//
		// Design space (800x600), i.e. the same coordinates `RecordEntryHit` and
		// `BackTipHit.Record` store, so a probe can read a box off a `[backtip]` line and click
		// it. Persistent until cleared, like `Hold` and unlike `Press`: a click is at least a
		// press tick and a release tick, and a one-shot position would strand the second one.
		private static bool mouseOverride;

		private static float mouseOverrideX;

		private static float mouseOverrideY;

		// JS bridge: DotNet.invokeMethod('EvilAliensWeb', 'debugMouseAt', x, y) / eaMouseAt(x,y).
		[JSInvokable("debugMouseAt")]
		public static void MouseAt(double x, double y)
		{
			// A NaN would park the cursor nowhere and make every hit test silently miss -- the
			// exact swallow-a-bad-value failure the file-wide flag convention exists to stop, and
			// `eaMouseAt(0)` produces one all by itself (y is undefined -> NaN). Report and
			// refuse, like Press does for an unknown key.
			if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
			{
				Console.WriteLine("[debug] eaMouseAt: ignoring non-finite position (" + x + "," + y
					+ ") -- expected two design-space numbers; the cursor is unchanged");
				return;
			}
			mouseOverride = true;
			mouseOverrideX = (float)x;
			mouseOverrideY = (float)y;
			Console.WriteLine("[debug] eaMouseAt " + mouseOverrideX + "," + mouseOverrideY
				+ " (design space; eaMouseClear releases the real mouse)");
		}

		// Hand the cursor back to the real mouse. Prints unconditionally (the surrounding seams
		// all do) so a probe can assert the release happened rather than reading an empty `ok`.
		[JSInvokable("debugMouseClear")]
		public static void MouseClear()
		{
			string had = mouseOverride ? (mouseOverrideX + "," + mouseOverrideY) : "nothing parked";
			mouseOverride = false;
			Console.WriteLine("[debug] eaMouseClear -- cursor back on the real mouse (was " + had + ")");
		}

		internal static bool TryGetMouseOverride(out float x, out float y)
		{
			x = mouseOverrideX;
			y = mouseOverrideY;
			return mouseOverride;
		}

		// Non-destructive read of a SCRIPTED hold. `Consume` DECREMENTS one, so it must be called
		// exactly once per tick per key -- InputHandler needs the Mouse1 state one step EARLIER
		// than the key loop reaches it (Esc is polled before Mouse1, and the back tip folds into
		// Esc), so it peeks there and lets the loop do the single real consume.
		//
		// **Deliberately NOT `|| touchHeld[idx]`, unlike `Consume`.** `touchHeld[Mouse1]` is the
		// on-screen FIRE button, so including it would let a touch player's held FIRE fire a
		// synthetic Esc whenever the (stale, untouched) mouse position happened to sit in the
		// back tip's box -- shipped behaviour, not a debug seam, and the touch overlay already
		// has its own BACK button. Touch gets no new behaviour from the mouse work; the same
		// rule the MouseLatch pointerType filter follows.
		internal static bool PeekScripted(int idx)
		{
			if (idx < 0 || idx >= holdTicks.Length)
			{
				return false;
			}
			return holdTicks[idx] > 0;
		}

		// JS bridge for QA/demo of the cinematic slow-motion effect (eaSlowmo in
		// wwwroot/index.html): DotNet.invokeMethod('EvilAliensWeb', 'debugSlowmo', seconds).
		// Triggers the same slow-motion burst the fully-powered 1up does (Oracle.SetSlowmotion)
		// so the ghost-trail look can be seen on demand without grinding a powerup. The Oracle
		// service is registered for the whole game's life, so this only no-ops meaningfully in a
		// menu because Oracle.Update resets slowmo to 1f whenever no player ship is alive — i.e.
		// it bites only inside a level with a live ship. INSIDE A CO-OP SESSION it also reaches
		// the OTHER peer (card a66e190a): SetSlowmotion announces itself as EvSlowmo, so a
		// console call here slows both worlds, not just this one. Not gameplay input. The null guard
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
			// Report the refusal rather than no-opping silently: inside a co-op session every
			// hit-stop is refused (card 68f62e92), so without this the hook looks broken.
			if (Juice.HitStopSuppressed)
			{
				Console.WriteLine("[debug] eaHitstop " + ms + "ms SUPPRESSED -- an online co-op session refuses every hit-stop (?nethitstop=1 to allow)");
				return;
			}
			Juice.AddHitStop(ms / 1000f);
			Console.WriteLine("[debug] eaHitstop " + ms + "ms");
		}

		// JS bridge for the wall-tower cost meter (eaWallPerf / eaWallStats in wwwroot/index.html,
		// polled by the eaWalls slider panel). WallPerf(on) arms the accumulators; WallStats() returns
		// one formatted line (fps, frame ms + p95, tower-pass ms, slice draws). Off by default, so a
		// normal boot never touches the stopwatch.
		//
		// The panel POLLS this ~4x/second rather than pushing per frame: a per-frame JS interop call
		// would itself cost more than the thing being measured.
		[JSInvokable("debugWallPerf")]
		public static void WallPerf(bool on)
		{
			WallProfiler.SetEnabled(on);
		}

		[JSInvokable("debugWallStats")]
		public static string WallStats()
		{
			return WallProfiler.Report();
		}

		// The preload-manifest export, reachable from the HEADLESS host. eahl's `eval` binds by
		// reflection to the public statics on THIS class only (tools/headless/Program.cs), while
		// the browser reaches the exporter directly -- window.eaPreloadExport calls
		// DotNet.invokeMethod('EvilAliensWeb','ExportPreloadManifest') on LoadProfiler, bypassing
		// DebugInput entirely. So without this passthrough the whole ?loadlog -> export loop is
		// browser-only, and growing the manifest means driving Chrome. Deliberately NOT
		// [JSInvokable]: LoadProfiler.ExportPreloadManifest already carries that attribute and
		// index.html already binds to it, so the browser surface is unchanged.
		// Headlessly the "download" lands as <dir of --out>/preload_manifest.txt
		// (HeadlessJsRuntime.WriteDownload); the text is returned (and printed by `eval`) either way.
		public static string PreloadExport()
		{
			return LoadProfiler.ExportPreloadManifest();
		}

		// JS bridge for the join-in-progress scenery diff (eaNetBg in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetBg'). Returns the live deep-state the
		// JIP catch-up replays (card 45a4e48d) as one parseable line, so a joiner's scenery is
		// verified by DIFFING the two peers' output rather than by screenshotting a fly-by that
		// moves every frame (root CLAUDE.md: never verify motion with timed live screenshots).
		// Run it in both windows' consoles once the joiner is up; the lines must match.
		[JSInvokable("debugNetBg")]
		public static string NetBg()
		{
			EvilAliens.GameScene scene = EvilAliens.GameScene.NetActiveScene;
			return scene == null ? "[netbg] no level" : "[netbg] " + scene.NetCatchUpStateLine();
		}

		// JS bridge for the JIP catch-up round-trip self-test (eaNetBgTest in index.html).
		// One tab, no peer, no timing: capture the burst, wipe the scenery to a fresh joiner's,
		// replay through the real client apply path, diff. See GameScene.NetCatchUpSelfTest --
		// it is destructive (the screen re-runs the hyperspace entry), so it is a console
		// command, never something a boot flag arms.
		[JSInvokable("debugNetBgTest")]
		public static string NetBgTest()
		{
			EvilAliens.GameScene scene = EvilAliens.GameScene.NetActiveScene;
			return scene == null ? "[netbgtest] no level" : scene.NetCatchUpSelfTest();
		}

		// JS bridge for the flying-spider population readout (eaFlySpiders in wwwroot/index.html;
		// card 9c92962e). The group-flatten's cost is per BACKGROUND spider, so any frame-time
		// number is meaningless without the count that produced it -- the first numbers on that
		// card compared two runs whose populations were never equal and read the difference as a
		// flatten cost. Returns the live counts plus the active flatten mode and box size, so a
		// figure pasted onto the card carries its own conditions.
		[JSInvokable("debugFlySpiders")]
		public static string FlySpiders()
		{
			return EvilAliens.FlyingSpiderCensus.Report();
		}

		// eaBraineroidGlowBatch(on): flip the Braineroid glow draw between the batched driver
		// (on, the shipped path) and the pre-card per-brain path (off). The appearance A/B --
		// flip it between two screenshots with NO tick in between so both paths draw the SAME
		// world; gameplay RNG is unseeded, so two boots are not comparable. See BraineroidGlows.
		[JSInvokable("debugBraineroidGlowBatch")]
		public static string BraineroidGlowBatch(bool on)
		{
			EvilAliens.BraineroidGlows.Suppressed = !on;
			return "[braineroidglow] batched=" + (on ? "true" : "false")
				+ " driving=" + (EvilAliens.BraineroidGlows.Active ? "true" : "false");
		}

		// eaCollisionBench(n, iters): the pinned collision broad-phase bench + its behaviour-
		// neutrality check against the pre-card algorithm (card 391e11d2). MENU-only; see
		// Compat/CollisionBench.cs. Args are passed through VERBATIM, exactly like
		// debugNetPuppetBench: the defaults live in the JS facade and apply only when an argument
		// is OMITTED, so a supplied 0 reaches the C# guard and is reported rather than swallowed.
		// (`eval` binds on arity, so both args are always required headlessly.)
		[JSInvokable("debugCollisionBench")]
		public static string CollisionBench(int n, int iters)
		{
			return EvilAliensWeb.Compat.CollisionBench.Run(n, iters);
		}

		// eaWorldCensus(on?): batches opened per frame + the live component population by type.
		// The FPS HUD says WHERE the time goes; this says what produced it. Arming clears the
		// window, so call it once, let the scene settle, then call it again to read.
		[JSInvokable("debugWorldCensus")]
		public static string Census(bool on)
		{
			if (on && !WorldCensus.Enabled)
			{
				WorldCensus.SetEnabled(true);
				return "[census] armed";
			}
			if (!on)
			{
				WorldCensus.SetEnabled(false);
				return "[census] off";
			}
			Microsoft.Xna.Framework.Game game =
				EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>()?.ComponentBin?.Game;
			return WorldCensus.Report(game);
		}

		// eaNetVelScan(on?): the offline audit behind the teleport marker (cards 8dabe812 ->
		// e79bb994). Arm it, play/soak a level, call again to read, per replicable type, the
		// fastest observed velocity against NetSession.MaxObservedSpeedPxPerMs AND how many
		// repositions that type ANNOUNCED. Needs NO net session -- it measures the GAME's motion
		// and the GAME's marking, which is exactly why it can audit both where a live-session
		// diagnostic cannot. It REFUSES to arm inside a session (it consumes the markers).
		[JSInvokable("debugNetVelScan")]
		public static string NetVelScan(bool arm)
		{
			Microsoft.Xna.Framework.Game game =
				EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>()?.ComponentBin?.Game;
			if (arm)
			{
				return EvilAliensWeb.Compat.Net.NetVelocityScan.Arm(game);
			}
			// Reading DISARMS: the scan owns a GameComponent and a ComponentRemoved subscription,
			// and there is no other call that would ever take them down. Report first -- disarming
			// clears the tallies the report is made of.
			string report = EvilAliensWeb.Compat.Net.NetVelocityScan.Report();
			EvilAliensWeb.Compat.Net.NetVelocityScan.Disarm();
			return report;
		}

		// JS bridge for the dev-build FPS HUD (eaFps in wwwroot/index.html; card 22e655b5).
		// FpsProfile(on) arms the per-phase accumulators, FpsStats() returns the HUD's JSON
		// payload and FpsStatsLine() the one-line console form. Same polling contract as the
		// wall meter above — the HUD reads this ~4x/second, never per frame.
		[JSInvokable("debugFpsProfile")]
		public static void FpsProfile(bool on)
		{
			FrameProfiler.SetEnabled(on);
		}

		[JSInvokable("debugFpsStats")]
		public static string FpsStats()
		{
			return FrameProfiler.Report();
		}

		[JSInvokable("debugFpsStatsLine")]
		public static string FpsStatsLine()
		{
			return FrameProfiler.StatsLine();
		}

		// Mean GL draw calls per frame, pushed from JS (the HUD patches drawElements/drawArrays
		// — see index.html eaFps). Counted there because BlazorGL's cost is per-CALL and JS sees
		// every source of them at once (sprite batches, bloom passes, the walls' 3D primitives)
		// without touching SpriteBatchWrapper. The HUD renders its own copy; this push exists so
		// the console one-liner is complete, and rides the 4Hz poll — NOT per frame, which would
		// cost more interop than the thing being measured.
		[JSInvokable("debugFpsGlCalls")]
		public static void FpsGlCalls(int calls)
		{
			FrameProfiler.NoteGlCalls(calls);
		}

		// eaFps.test(): run the frame-window maths over a synthetic frame series and report
		// measured vs expected. The point is the vsync trap itself — `work` ms of work
		// delivered every `interval` ms must read as 1000/interval fps and 1000/work headroom.
		[JSInvokable("debugFpsSelfTest")]
		public static string FpsSelfTest(double workMs, double intervalMs, int frames)
		{
			return FrameProfiler.SelfTest(workMs, intervalMs, frames);
		}

		// JS bridge for the hitbox debug overlay (eaHitboxes in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugHitboxes', on). Toggles the ?hitboxes
		// overlay at runtime (draws every collidable's collision shape colour-coded by kind);
		// same as booting with ?hitboxes but flippable live from the console.
		[JSInvokable("debugHitboxes")]
		public static void Hitboxes(bool on)
		{
			DebugFlags.SetShowHitboxes(on);
			Console.WriteLine("[debug] eaHitboxes " + (on ? "ON" : "OFF"));
		}

		// JS bridge for the ComponentBin lifecycle scenario suite (eaBinTest in
		// wwwroot/index.html): DotNet.invokeMethod('EvilAliensWeb', 'debugBinTest'). Runs
		// Compat/BinTest.Run() against the live bin and returns the PASS/FAIL report.
		[JSInvokable("debugBinTest")]
		public static string BinTest()
		{
			return EvilAliensWeb.Compat.BinTest.Run();
		}

		// JS bridge for the AI telemetry (eaAiBench in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugAiBench'). Returns Compat/AiBench's report
		// -- wall contacts, the heading-reversal jitter rate, fire idleness, level progress and
		// the run verdict. Only meaningful on a ?aibench boot (card f4d1721f).
		[JSInvokable("debugAiBench")]
		public static string AiBench()
		{
			return EvilAliensWeb.Compat.AiBench.Report();
		}

		// eaAiBench.reset() -- rearm the counters mid-run (e.g. to score one wall section
		// rather than the whole soak). Does not touch the game.
		[JSInvokable("debugAiBenchReset")]
		public static string AiBenchReset()
		{
			EvilAliensWeb.Compat.AiBench.Reset();
			return "[aibench] counters reset";
		}

		// eaAiBench.world() -- census of the live components vs what the AI's world model
		// (Oracle.GetBaddies) actually contains. The answer to "the level is stalled and the bot
		// is shooting something -- what is it blind to?".
		[JSInvokable("debugAiBenchWorld")]
		public static string AiBenchWorld()
		{
			return EvilAliensWeb.Compat.AiBench.World();
		}

		// eaAiBench.soak(seconds) -- headless AI soak: tick the real game loop at a fixed 60Hz
		// dt with no Draw, in bounded chunks. The ONLY way to soak the AI reliably from
		// automation: a background tab throttles rAF to ~1Hz, so a rendered run measures nothing.
		[JSInvokable("debugAiBenchRun")]
		public static string AiBenchRun(double chunkSeconds)
		{
			return EvilAliensWeb.Compat.AiBench.RunHeadless(chunkSeconds);
		}

		// One finished run's counters as `key=value` pairs, for eaAiBench.matrix()'s table
		// (card 9391f95a). Deliberately separate from AiBench() -- that one is a human report
		// and free to be reformatted; a sweep that scraped it would break on a cosmetic edit.
		[JSInvokable("debugAiBenchRow")]
		public static string AiBenchRow()
		{
			return EvilAliensWeb.Compat.AiBench.Row();
		}

		// JS bridge for eaScore() -- the per-slot score/combo dump. Card b0ab09ec's two-window
		// comparison is "do the peers agree on the tally", which reading HUD pixels answers
		// badly (the panels are small, chrome-shaded and mid-animation); this prints the
		// numbers, plus the provisional total still riding on top of the host's score.
		[JSInvokable("debugScoreDump")]
		public static string ScoreDump()
		{
			EvilAliens.ScoreVisualiser sv = EvilAliens.ServiceHelper.Get<EvilAliens.IScoreService>().Score;
			EvilAliens.Oracle oracle = EvilAliens.ServiceHelper.Get<EvilAliens.IOracleService>().Oracle;
			var sb = new System.Text.StringBuilder("[score] lives=").Append(sv.Lives);
			for (int i = 0; i < EvilAliens.Oracle.MaxPlayers; i++)
			{
				sb.Append(" | s").Append(i).Append(oracle.IsSeated(i) ? "=" : "(empty)=")
					.Append((int)sv.PointScore(i)).Append(" combo=").Append(sv.Combo(i));
				float pending = EvilAliensWeb.Compat.Net.NetPuppets.UnsettledFor(i);
				if (pending != 0f)
				{
					sb.Append(" unsettled=").Append(pending.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
				}
				// Card 1a3ad45a: whose simulation the slot's combo and powerup levels come from,
				// and what they are. `own` is the whole point of the two-window comparison -- the
				// SAME slot must read own=1 on one console and own=0 on the other, and the combo
				// and lv= figures beside it must agree across the pair.
				if (i < EvilAliens.ScoreVisualiser.SlotCount)
				{
					int[] levels = new int[EvilAliensWeb.Compat.Net.NetProtocol.HudLevelCount];
					sv.NetReadHudState(i, levels, out _, out EvilAliens.Powerup.PowerupType? activeType, out float progress);
					sb.Append(" own=").Append(EvilAliensWeb.Compat.Net.NetSession.OwnsSlot(i) ? 1 : 0)
						.Append(" pu=").Append(!activeType.HasValue
							? "none"
							: activeType.Value.ToString() + "@" + ((int)(progress * 100f)) + "%")
						.Append(" lv=").Append(string.Join(",", levels));
				}
			}
			return sb.ToString();
		}

		// JS bridge for the co-op score-reconciliation self-test (eaNetScore in
		// wwwroot/index.html, card b0ab09ec). Drives NetScoreLedger -- the real policy -- on a
		// VIRTUAL clock against a synthetic two-peer kill stream, running the old max() adoption
		// over the identical stream first so the drift it fixes is demonstrated, not asserted;
		// then round-trips a real EvDeath through ApplyAwards against the live ScoreVisualiser.
		// Needs no session and no second tab: the failure is a slow tally drift, and a
		// backgrounded peer tab throttles to ~1 tick/sec so two windows cannot show it anyway.
		[JSInvokable("debugNetScoreTest")]
		public static string NetScoreTest(int kills, int comboSkew, int rttMs, int seed)
		{
			return EvilAliensWeb.Compat.Net.NetScoreLedger.SelfTest(kills, comboSkew, rttMs, seed)
				+ "\n\n" + EvilAliensWeb.Compat.Net.NetPuppets.WireRoundTripTest();
		}

		// JS bridge for the world-snapshot unknown-id attribution (eaNetSnap in
		// wwwroot/index.html, card 48ab9b2f). Drives the real NetPuppets.OnSnapshotEntry so the
		// three branches that all `return false` -- rebuilt / left dead / refused -- are proved
		// to report the kind they took, and pins the derived snapTurn arithmetic. A
		// classification is invisible in any frame, so this is data, not a two-window run.
		[JSInvokable("debugNetSnapTest")]
		public static string NetSnapTest()
		{
			return EvilAliensWeb.Compat.Net.NetSnapshotTest.Run();
		}

		// JS bridge for the join-peer death-FX suite (eaNetDeathFx in wwwroot/index.html; cards
		// 4e406eba / 303bfb5b / 13aa596c). Both halves of "the enemy just vanishes on P2": an
		// unattributed real death arriving as KillerSelf, and a deferred death (BattleSkull,
		// the surviving MarsBoss) releasing its puppet from the freeze so the animation plays.
		// Menu-only and leave-no-trace; the FX are an absence, so this is data, not a screenshot.
		[JSInvokable("debugNetDeathFx")]
		public static string NetDeathFx()
		{
			return EvilAliensWeb.Compat.Net.NetDeathFxTest.Run();
		}

		// JS bridge for the step-4 scenario harness (eaNetScenarios in wwwroot/index.html, card
		// 25ad0659). Five scenarios over ONE real session with a scripted wire peer: the three
		// generous-claim shapes, the OneUp overlap, and the id-churn self-heal that carries the
		// residual first-wipe pupPops probe. Menu-only and leave-no-trace; scenario 6 is
		// NetSceneOrder below, which needs a level.
		[JSInvokable("debugNetScenarios")]
		public static string NetScenarios()
		{
			return EvilAliensWeb.Compat.Net.NetScenarioTest.Run();
		}

		// JS bridge for the transient-feedback beats (eaNetFx in wwwroot/index.html; cards
		// 43e85936 / 57ea30cd / ee939dd1 / 8d063d33 / c146422f). Real EvFx frames from a
		// scripted host over a NetWire into a real client session, asserting the EFFECT on the
		// live puppet -- the hit blink and the detach burst are private state that no metric
		// moves and no frame can be timed to. Menu-only and leave-no-trace.
		[JSInvokable("debugNetFx")]
		public static string NetFx()
		{
			return EvilAliensWeb.Compat.Net.NetFxTest.Run();
		}

		// JS bridge for the per-peer presentation effects (eaNetLocalFx in wwwroot/index.html;
		// cards 7a8ec0d3 / a66e190a). One suite for one question -- which peer sees an effect:
		// a floating score is the killer's alone (asserted as "no popup AND the score still
		// moved", with an owned-slot claim beside it as the control), and the 1up slow motion
		// crosses the wire in both directions without echoing. Menu-only and leave-no-trace.
		[JSInvokable("debugNetLocalFx")]
		public static string NetLocalFx()
		{
			return EvilAliensWeb.Compat.Net.NetLocalFxTest.Run();
		}

		// JS bridge for the teleport marker (eaNetTeleport in wwwroot/index.html, card e79bb994).
		// A real HOST session's snapshot frames read off the wire (flag set, declared velocity
		// rather than the jump's finite difference) and a real CLIENT session's puppet snapping
		// instead of blending -- each with the identical jump left UNMARKED beside it, since the
		// pre-card code also ended up in the right PLACE and it was the velocity that poisoned
		// the dead reckoning. Menu-only and leave-no-trace.
		[JSInvokable("debugNetTeleport")]
		public static string NetTeleport()
		{
			return EvilAliensWeb.Compat.Net.NetTeleportTest.Run();
		}

		// JS bridge for scenario 6 (eaNetSceneOrder in wwwroot/index.html, card 25ad0659 step 4).
		// Reset / pause / checkpoint ORDERING against a REAL GameScene -- which is what makes it
		// DESTRUCTIVE and keeps it out of NetScenarios: a stand-in scene would make every
		// assertion about the stand-in. Run it in a throwaway ?level=Level2&invuln boot.
		[JSInvokable("debugNetSceneOrder")]
		public static string NetSceneOrder()
		{
			return EvilAliensWeb.Compat.Net.NetSceneOrderTest.Run();
		}

		// JS bridge for the INetEntity seam (eaNetEntity in wwwroot/index.html, card 25ad0659
		// step 2c-ii). The compiler already covers the migration -- the core fields changed
		// TYPE, so no call site can still read the concrete one. What it cannot cover is a
		// mis-wired explicit forward (two floats swap silently) or a missing discriminant
		// (Powerup not answering NetPickup turns every remote pickup into an explosion), so
		// this drives every member to a DISTINCT value and runs the `is` tests as the control.
		[JSInvokable("debugNetEntityTest")]
		public static string NetEntityTest()
		{
			return EvilAliensWeb.Compat.Net.NetEntityTest.Run();
		}

		// JS bridge for the anchored-motion lane (eaNetMotion, card c1a38ef9): the sent Lazer
		// rates and the FlyingSpider path anchor. Every way that lane can break is SILENT and
		// degrades to the pre-card build, which shipped and merely looks rougher -- so the
		// suite asserts the predicate, the real descriptors' bytes and what the real per-tick
		// drive does over a chosen dt, each beside the pre-card behaviour as its control.
		[JSInvokable("debugNetMotionTest")]
		public static string NetMotionTest()
		{
			return EvilAliensWeb.Compat.Net.NetMotionTest.Run();
		}

		// JS bridge for the Level-3 wall's replication (eaNetWalls, cards 4392bd30 / 80749dc4):
		// the wire's lossy scale, the derived-scale opt-out that refuses it, the drawn-block ==
		// collision-tile invariant that the lossy scale broke, and the anchored scroll speed.
		// Every one of those is invisible in a frame taken on either peer alone -- the wall looks
		// like an ordinary wall on both screens, it is only the two together that disagree.
		[JSInvokable("debugNetWallTest")]
		public static string NetWalls()
		{
			return EvilAliensWeb.Compat.Net.NetWallTest.Run();
		}

		// Park a session-ending notice at the menus (card 72143c11), with no peer and no
		// session -- the only offline way to reach MenuScene.NetUpdate's notice path, since
		// every production writer of MenuNotice is inside NetSession.Stop(). MenuScene polls
		// it on its next tick, so step a frame after calling this.
		[JSInvokable("debugNetNotice")]
		public static string NetNotice(string text)
		{
			string notice = string.IsNullOrEmpty(text)
				? "The other player disconnected\nMatch ended"
				: text.Replace("|", "\n");
			EvilAliensWeb.Compat.Net.NetSession.SetMenuNoticeForTest(notice);
			return "[netnotice] queued: " + notice.Replace("\n", " / ");
		}

		// Which menus are in the world right now (card 72143c11). MenuSub1 has no modality: a
		// menu in the collection runs HandleInput every tick, so TWO of them means two selections
		// moving and two entries invoked per press -- a bug that is invisible in a screenshot
		// (the top menu draws over the other) and has no other observable. This is what a probe
		// asserts on; nothing in the game reads it.
		//
		// Removal is QUEUED, so step a frame after whatever you expect to have closed a menu --
		// see ComponentBin.InCollection for why the count is a tick behind a Remove.
		[JSInvokable("debugMenuCensus")]
		public static string MenuCensus()
		{
			System.Collections.Generic.List<EvilAliens.MenuSub1> live = LiveMenus();
			if (live == null)
			{
				return "[menucensus] no ComponentBin service";
			}
			string names = "(none)";
			if (live.Count > 0)
			{
				string[] typeNames = new string[live.Count];
				for (int i = 0; i < live.Count; i++)
				{
					typeNames[i] = live[i].GetType().Name;
				}
				names = string.Join(",", typeNames);
			}
			return "[menucensus] count=" + live.Count + " live=" + names;
		}

		// Put the live MenuScene into net-lobby mode (card 72143c11). It is the ONE precondition
		// of the overlapping-notice bug a headless run cannot otherwise produce: netMode is set
		// by entering the Online Co-op flow, and reaching it for real needs a paired peer.
		// Everything the probe then exercises (NetUpdate's notice branch) is the real code.
		// Since card c337222a the flag no longer SURVIVES a level launch (MenuScene.Initialize
		// clears it), so this is also how a probe plants the stale flag a menu round trip must
		// clear -- read it back with MenuNetState below.
		[JSInvokable("debugMenuNetMode")]
		public static string MenuNetMode()
		{
			EvilAliens.MenuScene scene = LiveMenuScene();
			if (scene == null)
			{
				return "[menunetmode] no live MenuScene -- boot ?menu first";
			}
			scene.NetDebugForceNetMode();
			return "[menunetmode] netMode=true on the live MenuScene";
		}

		// Read the live MenuScene's net-flow UI state (card c337222a). None of those four fields
		// is visible in a frame, so this is the only observable for "did a level launch leave the
		// menu believing it is still inside the Online Co-op flow".
		[JSInvokable("debugMenuNetState")]
		public static string MenuNetState()
		{
			EvilAliens.MenuScene scene = LiveMenuScene();
			if (scene == null)
			{
				return "[menunetstate] no live MenuScene -- boot ?menu first";
			}
			return "[menunetstate] " + scene.NetDebugStateLine();
		}

		private static EvilAliens.MenuScene LiveMenuScene()
		{
			EvilAliens.IComponentBinService svc = EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>();
			System.Collections.Generic.List<EvilAliens.MenuScene> scenes = svc?.ComponentBin?.InCollection<EvilAliens.MenuScene>();
			return (scenes == null || scenes.Count == 0) ? null : scenes[0];
		}

		private static System.Collections.Generic.List<EvilAliens.MenuSub1> LiveMenus()
		{
			EvilAliens.IComponentBinService svc = EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>();
			return svc?.ComponentBin?.InCollection<EvilAliens.MenuSub1>();
		}

		// JS bridge for the pinned many-puppet drive bench (eaNetPuppetBench in
		// wwwroot/index.html, card 25ad0659 step 2c-ii). Puts n real puppets in the world
		// through the real self-heal path and times NetPuppets.Drive in a plain loop, in
		// ABSOLUTE microseconds. The FrameProfiler cannot answer this: Drive runs inside
		// base.Update, so it lands in UpdComponents under the whole world, while UpdNet covers
		// only NetSession.Update. Best headless -- no rAF paces this loop.
		[JSInvokable("debugNetPuppetBench")]
		public static string NetPuppetBench(int n, int iters)
		{
			return EvilAliensWeb.Compat.Net.NetPuppetBench.Run(n, iters);
		}

		// JS bridge for the in-process wire + wire-level codec round trips (eaNetWire in
		// wwwroot/index.html, card 25ad0659). Drives NetWire/InMemoryTransport -- the transport
		// contract two browser-only impls could only be observed to satisfy incidentally -- then
		// puts every codec's real frames on it and decodes what the far endpoint received, which
		// no encode/decode pair can do (a matching pair of wrong offsets passes one). Needs no
		// Game, no session and no level, which is what also makes it runnable from
		// tools/sim/logic_probe with no browser at all.
		[JSInvokable("debugNetWireTest")]
		public static string NetWireTest()
		{
			return EvilAliensWeb.Compat.Net.NetWireTest.Run();
		}

		// JS bridge for the INetHost seam (eaNetHost in wwwroot/index.html, card 25ad0659 step
		// 2a). Asserts BOTH halves of the step's claim -- that the production host still reads
		// exactly what each call site read, and that the injected clock genuinely reaches the
		// real NetImpairment queue (a consumer that kept its own Environment.TickCount64 would
		// leave every downstream scenario a race). Game-free, so logic_probe runs it too.
		[JSInvokable("debugNetHostTest")]
		public static string NetHostTest()
		{
			return EvilAliensWeb.Compat.Net.NetHostTest.Run();
		}

		// JS bridge for the reset / TryAdd ship-puppet spawn scenario (eaNetResetSpawn in
		// wwwroot/index.html, card 25ad0659 step 1b). Pairs a REAL client session to a scripted
		// host over an in-process NetWire and drives NetSession.SpawnPuppet / SpawnFriend through
		// an EvReset that purges from inside the rx drain -- the only purge site that can reach
		// their bin.TryAdd branch. **DESTRUCTIVE**: it needs a live GameScene and leaves the scene
		// in its reset branch, so run it in a throwaway ?level= boot (it refuses outright if a real
		// session is up, and restores the roster either way).
		[JSInvokable("debugNetResetSpawn")]
		public static string NetResetSpawn()
		{
			return EvilAliensWeb.Compat.Net.NetResetSpawnTest.Run();
		}

		// JS bridge for the Level 1 intro-cinematic suite (eaNetIntroGate in wwwroot/index.html,
		// card 8a7772d6): the replicated player-spawn hold and the cosmetic intro bullet volley,
		// driven over an in-process NetWire against the REAL Level 1 script. **DESTRUCTIVE and
		// LEVEL-1-ONLY**: it pairs a session onto the live level, ticks the real scene and spawns
		// the local ship, so run it in a throwaway ?level=Level1 boot -- and run it EARLY, within
		// the ~10s cutscene, or its first precondition reports that the intro is already over.
		[JSInvokable("debugNetIntroGate")]
		public static string NetIntroGate()
		{
			return EvilAliensWeb.Compat.Net.NetIntroGateTest.Run();
		}

		// JS bridge for the remote-powerup-pickup suite (eaNetPickup in wwwroot/index.html; cards
		// 83271f3d / 10f9dba4 / d53431b4). Pairs a REAL host session to a scripted client, adopts
		// its ship puppet, and drives real EvClaim pickups plus the real MsgHudState level mirror
		// at it -- the two halves that add up on a live observer, which is why the option count is
		// asserted over the combined sequence rather than either path alone. **DESTRUCTIVE**: it
		// needs a live GameScene and spends real pickups into the live panels, so run it in a
		// throwaway ?level=Level2&invuln boot.
		[JSInvokable("debugNetPickupTest")]
		public static string NetPickup()
		{
			return EvilAliensWeb.Compat.Net.NetPickupTest.Run();
		}

		// JS bridge for the single-tap bullet-count suite (eaNetFire in wwwroot/index.html, card
		// a5c2a39b). Asserts the firing-hold contract as a pure decision over every fire rate,
		// then COUNTS the bullets a scripted single tap re-fires on a real remote puppet -- with
		// the pre-card packet pattern beside it, which must still produce the doubled tap.
		// **DESTRUCTIVE**: it needs a live GameScene and fires real bullets into it, so run it in
		// a throwaway ?level=Level2&invuln boot.
		[JSInvokable("debugNetFireTest")]
		public static string NetFire()
		{
			return EvilAliensWeb.Compat.Net.NetFireTest.Run();
		}

		// JS bridge for the co-op per-slot combo/powerup self-test (eaNetCombo in
		// wwwroot/index.html, card 1a3ad45a). Round-trips the real MsgHudState wire format,
		// then drives the real PowerupData exp curve over two divergent combo streams -- running
		// the OLD ungated behaviour first, so the slow motion and the stray powerup levels it
		// used to inflict on a slot this peer does not own are demonstrated, not asserted.
		[JSInvokable("debugNetComboTest")]
		public static string NetComboTest()
		{
			return EvilAliensWeb.Compat.Net.NetComboTest.Run();
		}

		// JS bridge for the co-op kick/block rules (eaKickTest in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugKickTest'). Runs
		// Compat/Net/NetKickTest.Run() and returns the PASS/FAIL report.
		[JSInvokable("debugKickTest")]
		public static string KickTest()
		{
			return EvilAliensWeb.Compat.Net.NetKickTest.Run();
		}

		// JS bridge for the host pause menu's Online Play decision (eaHostMenu in
		// wwwroot/index.html): DotNet.invokeMethod('EvilAliensWeb', 'debugHostMenuTest'). Runs
		// Compat/Net/NetHostMenuTest.Run() (the exhaustive state sweep) and returns the PASS/FAIL
		// report. Pure -- needs no session, level, peer or listing.
		[JSInvokable("debugHostMenuTest")]
		public static string HostMenuTest()
		{
			return EvilAliensWeb.Compat.Net.NetHostMenuTest.Run();
		}

		// The LIVE counterpart: what the Online Play row resolves to RIGHT NOW. The suite above
		// asserts the predicate; this reports the state it is being asked about, which is what
		// separates "the predicate is wrong" from "this game genuinely has nothing to offer".
		[JSInvokable("debugHostMenu")]
		public static string HostMenu()
		{
			return EvilAliensWeb.Compat.Net.NetHostMenu.Dump();
		}

		// The LIVE-SESSION half (eaHostMenu.live in wwwroot/index.html): a real host session with
		// a scripted peer over an in-process NetWire, so the decision is read back through the
		// live statics rather than the synthetic states HostMenuTest sweeps -- plus the kick the
		// menu row makes. Menu-only and leave-no-trace; it SKIPS itself near a live world.
		[JSInvokable("debugHostMenuLive")]
		public static string HostMenuLive()
		{
			return EvilAliensWeb.Compat.Net.NetHostMenuLiveTest.Run();
		}

		// JS bridge for the decorative-swarm replication (eaNetCosmetic in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetCosmetic'). Runs
		// Compat/Net/NetCosmeticTest.Run() and returns the PASS/FAIL report. Leave-no-trace, so
		// it is safe at any point in play -- and its apply leg only runs INSIDE a level.
		[JSInvokable("debugNetCosmetic")]
		public static string NetCosmetic()
		{
			return EvilAliensWeb.Compat.Net.NetCosmeticTest.Run();
		}

		// JS bridge for the primary-slot negotiation (eaSlotTest in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugSlotTest'). Runs
		// Compat/Net/NetSlotTest.Run() and returns the PASS/FAIL report.
		[JSInvokable("debugSlotTest")]
		public static string SlotTest()
		{
			return EvilAliensWeb.Compat.Net.NetSlotTest.Run();
		}

		// JS bridge for the level-select thumbnail capture (eaShotNow in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugShotNow', 'arm'|'save'). The capture+save
		// path normally runs only at level EXIT behind an on-screen busy-ness heuristic, which
		// puts the alpha seal in ScreenshotSaver.SaveScreenShot (card d67755d2) out of reach of
		// any cheap probe. Two calls with a Draw between them: `arm` grabs the frame in the
		// post-Draw hook, `save` composites + persists it and prints the `[shot]` line the probe
		// asserts on (under ?loadlog). Reports which step it could not take rather than failing
		// silently -- "no GameScene", "nothing grabbed yet" and "this level does not capture at
		// all" are three different mistakes.
		//
		// DESTRUCTIVE, like eaNetResetSpawn / eaNetSceneOrder: `save` goes through the production
		// ScreenshotSaver.SaveScreenShot, so it OVERWRITES this level's real saved thumbnail --
		// on the live site that is a write to IndexedDB. Run it in a throwaway boot, not in a
		// game whose level-select art you care about.
		[JSInvokable("debugShotNow")]
		public static string ShotNow(string step)
		{
			string s = (step ?? "").Trim().ToLowerInvariant();
			string why;
			switch (s)
			{
			case "arm":
				return EvilAliens.GameScene.DebugArmSnapshot(out why)
					? "[shotnow] armed" : "[shotnow] cannot arm: " + why;
			case "save":
				return EvilAliens.GameScene.DebugSaveSnapshot(out why)
					? "[shotnow] saved" : "[shotnow] cannot save: " + why;
			default:
				return "[shotnow] expected 'arm' or 'save', got '" + s + "'";
			}
		}

		// JS bridge for the texture-load probe (eaTexProbe in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugTexProbe', 'GFX/Base/756'). Reports which
		// precompiled sibling shipped, which file the texture ACTUALLY came from, its actual vs
		// logical size, and its mip level count -- and on failure the whole exception chain,
		// which is the one thing KNI's own FileNotFoundException throws away. See
		// Compat/TexProbe.cs.
		[JSInvokable("debugTexProbe")]
		public static string TexProbeRun(string assetName)
		{
			return EvilAliensWeb.Compat.TexProbe.Run(assetName);
		}

		// JS bridge for the background tile-cull oracle (eaBgCull in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugBgCull'). Sweeps the real cull predicate,
		// dry-runs scenario layers (incl. mirrored and TALL ones, which no shipped background
		// is) through the real Draw, censuses the live layers, and diffs the cull against its
		// pre-ef55b76e form across a scroll-phase sweep. See Compat/BgCullTest.cs -- the cull's
		// correctness is invisible to a screenshot, so it is read as data (and that file's header
		// says which of the four parts can actually fail).
		[JSInvokable("debugBgCull")]
		public static string BgCull()
		{
			return EvilAliensWeb.Compat.BgCullTest.Run();
		}

		// JS bridge for TeamChallenge's partner-seat oracle (eaTeamSeat in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugTeamSeat'). Drives the real
		// TeamChallenge.ResolvePartnerSeat over every pad-connection mask, so the fix for card
		// e6927ef8 is verified without four physical gamepads, and runs the pre-card
		// always-PadOne policy as the negative control. See Compat/TeamSeatTest.cs.
		[JSInvokable("debugTeamSeat")]
		public static string TeamSeat()
		{
			return EvilAliensWeb.Compat.TeamSeatTest.Run();
		}

		// JS bridge for the Boss Train's section-state oracle (eaBossTrain in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugBossTrain'). Checks every checkpoint's declared
		// section against a forward walk of the real script, then drives the REAL
		// RevertToCheckpoint from the alien-base window and reads the section + track back, with the
		// pre-card behaviour as the negative control. Needs a live ?level=InsaneBossI boot.
		// DESTRUCTIVE -- it moves the script position and the section, so run it in a throwaway
		// boot, never mid-playthrough. See Compat/BossTrainTest.cs.
		[JSInvokable("debugBossTrain")]
		public static string BossTrain()
		{
			return EvilAliensWeb.Compat.BossTrainTest.Run();
		}

		// JS bridge for the death/reset path (eaKillShips in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugKillShips'). Asplodes every
		// LOCALLY-OWNED PlayerShip through the real Asplode()->Die() path, so the scene's
		// AllShipsDead check fires LoseLife (host) / the host's EvReset mirrors (client).
		// Asplode's only guard is !IsDead, so this bites through ?invuln and the post-respawn
		// invulnerability window alike -- it is a scripted death, NOT a simulated hazard hit.
		// Written for the two-tab co-op gate (card 9009a1c4): a death/reset is the
		// standing-purge-filter window worth testing, it needs BOTH co-op ships down, and
		// waiting for the ?aiplayer AI to die is neither timely nor repeatable.
		// Remote/RemoteFriend puppets are skipped -- their owner decides their deaths, and
		// asploding one here would fake a death the owning peer never sent.
		[JSInvokable("debugKillShips")]
		public static string KillShips()
		{
			Microsoft.Xna.Framework.Game game =
				EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>().ComponentBin.Game;
			// Collect BEFORE asploding: Asplode() adds two Explosions, and bin adds are instant
			// (card 02d9ad67), so killing inside the enumeration would mutate Game.Components
			// mid-foreach.
			System.Collections.Generic.List<EvilAliens.PlayerShip> targets =
				new System.Collections.Generic.List<EvilAliens.PlayerShip>();
			foreach (Microsoft.Xna.Framework.IGameComponent item
				in (System.Collections.ObjectModel.Collection<Microsoft.Xna.Framework.IGameComponent>)(object)game.Components)
			{
				if (item is EvilAliens.PlayerShip ship
					&& ship.Controller != EvilAliens.ControlDevice.Remote
					&& ship.Controller != EvilAliens.ControlDevice.RemoteFriend)
				{
					targets.Add(ship);
				}
			}
			foreach (EvilAliens.PlayerShip ship in targets)
			{
				ship.Asplode();
			}
			return "[debug] eaKillShips asploded " + targets.Count + " local ship(s)";
		}

		// JS bridge for the awardment banner (eaAward in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugAward', 'Pacifist'). Queues an awardment
		// through the REAL AwardmentBlade.AwardAchievement path, so the banner enters, shows
		// and exits exactly as it would in play.
		//
		// Written for card d2f746d5: every in-game trigger is minutes deep behind a condition a
		// rig cannot produce (Pacifist is 90s of not firing on Hard+, Dunce a 180s boss timer,
		// the rest are level completions), so the banner -- and since card 57555583 the LAZY
		// content load behind it -- had no way to be exercised at all.
		//
		// An ALREADY-UNLOCKED awardment is RE-LOCKED first, because otherwise this seam is
		// useless on any save that has played the game -- both AwardAchievement and
		// AwardmentBlade.Update drop an unlocked awardment, so there is no banner to see. The
		// re-lock is announced on its own line: a capture taken after one must never be mistaken
		// for the untouched path.
		//
		// It is NOT "in memory only": the blade's own Enter transition calls
		// SetAwardmentIsUnlocked(true) + SaveThreaded(), which rewrites the whole Achievements.xml.
		// The save therefore ends up as it started -- but only because the banner actually runs,
		// which is why the cheat gate below is tested BEFORE anything is mutated.
		//
		// The cheat gate is REPORTED rather than bypassed -- it is a live property of the run,
		// not stale save state, and a seam whose failure looks like its success is worse than
		// none.
		[JSInvokable("debugAward")]
		public static string Award(string awardment)
		{
			// The comma test is not decoration: Enum.TryParse accepts "FirstAct,SecondAct" as a
			// bitwise OR even though Awardment is not [Flags], and the result passes IsDefined --
			// so a typo'd list would quietly award something the caller never named.
			if (awardment == null || awardment.IndexOf(',') >= 0
				|| !System.Enum.TryParse<EvilAliens.Awardment>(awardment, ignoreCase: true, out var which)
				|| !System.Enum.IsDefined(typeof(EvilAliens.Awardment), which))
			{
				return "[debug] eaAward: unknown awardment '" + awardment + "' (expected one of: "
					+ string.Join(", ", System.Enum.GetNames(typeof(EvilAliens.Awardment))) + ")";
			}
			EvilAliens.AwardmentBlade blade =
				EvilAliens.ServiceHelper.Get<EvilAliens.IAwardmentBladeService>()?.get();
			if (blade == null)
			{
				return "[debug] eaAward: the awardment blade is not available yet (the game is "
					+ "still booting -- try again once a scene is up).";
			}
			// The cheat gate is tested BEFORE the re-lock, and that ORDER is the whole safety of
			// this seam: re-locking and then bailing out would drop the unlock on the floor, and
			// the next SaveThreaded from anywhere serialises the whole singleton -- so a cheating
			// session would silently lose an awardment the player had really earned.
			if (EvilAliens.Settings.GetInstance().CheckForCheats())
			{
				return "[debug] eaAward: a cheat is active, so " + which + " is dropped -- exactly "
					+ "as in play (Settings.CheckForCheats). No banner, and nothing was touched.";
			}
			string relocked = "";
			if (EvilAliens.Achievements.GetInstance().GetAwardmentIsUnlocked((int)which))
			{
				EvilAliens.Achievements.GetInstance().SetAwardmentIsUnlocked((int)which, value: false);
				relocked = "[debug] eaAward: re-locked " + which + " for this session before awarding "
					+ "-- this is NOT the untouched path\n";
			}
			blade.AwardAchievement(which);
			// The two durations mirror AwardmentBlade.Update's bladeTimer values (170f enter,
			// 6500f show); nothing links them, so retune both together.
			return relocked + "[debug] eaAward queued " + which + " (\"" + blade.AwardmentName(which)
				+ "\") -- the banner takes ~170ms to enter and shows for 6.5s.";
		}

		// JS bridge for the on-demand roster dump (eaNetRoster in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetRoster'). Prints the same roster=
		// string the 5s [net] metrics line carries, plus resets=, at the instant it is called.
		// Written for the reset-with-couch-players gate (card af0eb00a): the assertion is
		// before-vs-after a ~2.7s reset, which the metrics cadence can straddle entirely.
		[JSInvokable("debugNetRoster")]
		public static string NetRoster()
		{
			return EvilAliensWeb.Compat.Net.NetSession.RosterDump();
		}

		// JS bridge for the OFFLINE roster (eaOracleRoster in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugOracleRoster'). Reads the live Oracle
		// directly, so it needs no session, no level and no gamepads -- unlike eaNetRoster(),
		// which early-returns without a net session and so cannot see the MENU roster, which is
		// exactly where a seat left behind by the last level or attract demo does its damage
		// (the menu-lobby handshake allocates from it). eaScore() also reports seated-ness per
		// slot; what this adds is the CONTROLLER DEVICE per seat, which is what distinguishes an
		// attract demo's leftover AI seats from a real player's.
		//
		// `aliveSlots=` lists the slots that own a LIVE PlayerShip right now, off Oracle.IsAlive --
		// the same read SpawnAllPlayers respawns off. BRACKETED so it cannot be misread as a
		// count: `aliveSlots=[0]` is slot 0 flying, `aliveSlots=[]` is a shipless world.
		// A seat stays seated across a death, so seated-ness
		// alone cannot say whether there is a ship in the world -- which is a PRECONDITION for
		// anything that kills one, and `death_fade.txt` asserts on it for exactly that reason
		// (card af4c3694). Do not reach for `eval Census` instead: WorldCensus.Report prints only
		// the fourteen most populous types, so PlayerShip=1 silently drops off a busy scene.
		[JSInvokable("debugOracleRoster")]
		public static string OracleRoster()
		{
			EvilAliens.Oracle oracle = EvilAliens.ServiceHelper.Get<EvilAliens.IOracleService>()?.Oracle;
			if (oracle == null)
			{
				return "[debug] eaOracleRoster: no oracle service (game not booted yet?)";
			}
			string seats = "";
			string alive = "";
			for (int slot = 0; slot < EvilAliens.Oracle.MaxPlayers; slot++)
			{
				if (oracle.IsSeated(slot))
				{
					seats += (seats.Length > 0 ? "," : "") + slot + ":" + oracle.Controller(slot);
				}
				if (oracle.IsAlive(slot))
				{
					alive += (alive.Length > 0 ? "," : "") + slot;
				}
			}
			return "[debug] eaOracleRoster: players=" + oracle.Players
				+ " seated=" + (seats.Length > 0 ? seats : "-")
				+ " aliveSlots=[" + alive + "]";
		}

		// JS bridge for a couch join RIGHT NOW (eaNetCouchJoin in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugNetCouchJoin'). Makes the same
		// NetSession.TrySeatLocalJoin call a real gamepad Start press makes. HOST-SIDE that
		// works before a peer has paired, which ?netlocal cannot do (TickLocalJoinSim is gated
		// behind PeerUp -- correctly: pre-pairing, AllocateSeat cannot yet know which seat the
		// joiner needs). That pre-pairing window is the only way to fill the roster ahead of a
		// joiner, i.e. the sole trigger for the host's RejectFull path (card af0eb00a). On a
		// CLIENT it is still PeerUp-gated, because a client seat has to be asked for.
		[JSInvokable("debugNetCouchJoin")]
		public static string NetCouchJoin()
		{
			if (!EvilAliensWeb.Compat.Net.NetSession.Active)
			{
				return "[debug] eaNetCouchJoin: no net session (needs a ?net= boot)";
			}
			string outcome = EvilAliensWeb.Compat.Net.NetSession.DebugCouchJoin();
			return "[debug] eaNetCouchJoin: " + outcome + "\n  "
				+ EvilAliensWeb.Compat.Net.NetSession.RosterDump();
		}

		// JS bridge for the live colorize-tuner slider panel (eaHue in wwwroot/index.html,
		// shown on the ?harness=battleskull page): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetHue', start, end, target, trackHp, cycle, loop). Overrides the BattleSkull
		// hue band/target in real time so the sliders retune without a page reload — same
		// effect as the ?huestart/?hueend/?huetarget/?huecycle/?hueloop URL flags, just live.
		// trackHp == true pins nothing (Target follows HP, the shipped default); false pins the
		// Target to `target`. Only bites while the harness is up (HarnessColorize gates on it).
		[JSInvokable("debugSetHue")]
		public static void SetHue(double start, double end, double target, bool trackHp, bool cycle, double loop)
		{
			DebugFlags.SetHueOverride(
				(float)start,
				(float)end,
				trackHp ? (float?)null : (float)target,
				cycle,
				(float)loop);
		}

		// JS bridge for the live laser-tuner slider panel (eaLazer in wwwroot/index.html, shown on
		// the ?lazershot showcase): DotNet.invokeMethod('EvilAliensWeb', 'debugSetLazer',
		// chargeScale, capScale, arcRate, tendrilSpeed). Overrides the four Quad/LazerGenerator FX
		// knobs in real time so the sliders retune without a page reload — same effect as the
		// ?lazerchargescale/?lazercapscale/?lazerarcs/?lazertendrilspeed URL flags, just live.
		[JSInvokable("debugSetLazer")]
		public static void SetLazer(double chargeScale, double capScale, double arcRate, double tendrilSpeed)
		{
			DebugFlags.SetLazerOverride(
				(float)chargeScale,
				(float)capScale,
				(float)arcRate,
				(float)tendrilSpeed);
		}

		// JS bridge for the live holo-sim tuner slider panel (eaHolo in wwwroot/index.html, shown on
		// ?level=Tutorial / ?level=ClassicAliens / a bare ?holotune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetHolo', green, greenPulse, burst, staticRate, filter). Overrides the simulator
		// filter knobs in real time — same effect as the ?hologreen/?hologreenpulse/?holoburst/
		// ?holostaticrate/?holofilter URL flags, just live (HoloSim reads them every frame).
		[JSInvokable("debugSetHolo")]
		public static void SetHolo(double green, double greenPulse, double burst, double staticRate, double filter)
		{
			DebugFlags.SetHoloOverride(
				(float)green,
				(float)greenPulse,
				(float)burst,
				(float)staticRate,
				(float)filter);
		}

		// JS bridge for the live bomb-ripple tuner slider panel (eaRipple in wwwroot/index.html,
		// shown on ?rippletune): DotNet.invokeMethod('EvilAliensWeb', 'debugSetRipple',
		// master, amp, radius, duration, width, falloff, rim, phase).
		// Same effect as the ?ripple/?rippleamp/?rippleradius/?rippleduration/?ripplewidth/
		// ?ripplefalloff/?ripplerim/?ripplephase URL flags, just live -- BombRipple resolves
		// every one of them per frame in PackedRings rather than baking them in at Fire, so a
		// drag retunes rings that are ALREADY travelling, and the parked ring too.
		// A NEGATIVE phase means "not parked", since the JS side has no null to send.
		[JSInvokable("debugSetRipple")]
		public static void SetRipple(double master, double amp, double radius, double duration,
			double width, double falloff, double rim, double phase)
		{
			DebugFlags.SetRippleOverride(
				(float)master,
				(float)amp,
				(float)radius,
				(float)duration,
				(float)width,
				(float)falloff,
				(float)rim,
				phase < 0.0 ? (float?)null : (float)phase);
		}

		// Fire a ripple ring on demand at a design-space point (eaRipple.fire(x, y, power) /
		// `eval RippleFire 400 300 2`). A real bomb needs a bomb pickup and a live ship, so
		// this is how a rig -- browser or eahl -- reaches the effect without playing for one.
		// Defaults to screen centre at full power.
		[JSInvokable("debugRippleFire")]
		public static void RippleFire(double x = 400.0, double y = 300.0, double power = 4.0)
		{
			BombRipple.Fire(new Microsoft.Xna.Framework.Vector2((float)x, (float)y),
				(int)System.Math.Round(power));
			Console.WriteLine("[ripple] fired at " + x + "," + y + " power=" + (int)System.Math.Round(power));
		}

		// Park ONE ripple ring at `phase` (0..1) of its life and hold it there for a still
		// screenshot, or un-park with a NEGATIVE value (the JS side has no null to send) --
		// the live equivalent of the ?ripplephase= boot flag. `eaRipple.park(0.35)` /
		// `eval RipplePark 0.35`.
		[JSInvokable("debugRipplePark")]
		public static void RipplePark(double phase = -1.0)
		{
			DebugFlags.SetRipplePhaseOverride(phase < 0.0 ? (float?)null : (float)phase);
			Console.WriteLine("[ripple] park=" + (phase < 0.0 ? "live" : phase.ToString()));
		}

		// Report the ripple state as data (`eaRipple.state()` / `eval RippleState`): whether a
		// ring is live, the knobs in force and the parked phase if any. What the committed
		// probe tools/headless/probes/bomb_ripple.txt asserts against.
		//
		// Every value is read back off BombRipple's OWN resolved accessors, never re-derived
		// here as `DebugFlags.X ?? BombRipple.DefaultX`: a second copy of the fallback chain
		// drifts, and it would have printed a Duration below the 0.01 s floor the renderer
		// actually applies -- a readout that lies is worse than no readout.
		[JSInvokable("debugRippleState")]
		public static void RippleState()
		{
			Console.WriteLine("[ripple] visible=" + BombRipple.Visible
				+ " master=" + BombRipple.Master
				+ " amp=" + BombRipple.Amplitude
				+ " radius=" + BombRipple.Radius
				+ " duration=" + BombRipple.Duration
				+ " width=" + BombRipple.Width
				+ " falloff=" + BombRipple.Falloff
				+ " rim=" + BombRipple.Rim
				+ " mini=" + DebugFlags.RippleMini
				+ " phase=" + (DebugFlags.RipplePhase.HasValue
					? DebugFlags.RipplePhase.Value.ToString()
					: "live"));
		}

		// Report the WORLD clock as data (`eaWorldClock()` / `eval WorldClock`), card d79a2f48.
		// The clock every Draw-time cosmetic reads instead of gameTime.TotalGameTime, plus the
		// freeze depth that gates it -- which is the whole mechanism, and the only part of it a
		// probe can assert. A screenshot pair can show a paused frame is identical; it cannot say
		// WHY, and "identical" also passes on a build that stopped drawing.
		[JSInvokable("debugWorldClock")]
		public static void WorldClock()
		{
			// A MISSING bin reports depth=none, never 0: `frozen=False depth=0` is exactly what a
			// genuinely running world prints, so a broken service lookup would read as healthy --
			// and pause_world_clock.txt asserts that very string on two of its three legs.
			EvilAliens.ComponentBin bin =
				EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>()?.ComponentBin;
			Console.WriteLine("[worldclock] seconds=" + WorldTime.Seconds.ToString("0.000")
				+ " frozen=" + (bin != null && bin.FreezeDepth > 0)
				+ " depth=" + ((bin != null) ? bin.FreezeDepth.ToString() : "none"));
		}

		// Rezero the world clock (`eaWorldClockReset()` / `eval WorldClockReset`), card d79a2f48.
		// It exists so a probe can assert an EXACT reading: the clock is otherwise an absolute
		// count from process start, so every assertion about it would be a boot-tick count that
		// an unrelated change to the boot sequence silently invalidates. Rezero, step a known
		// number of frames, assert the seconds -- boot-independent, and it reads the same under
		// a freeze (where the answer is "still 0.000"). Cosmetic phases only, so a rezero mid-play
		// just re-phases some shimmers.
		[JSInvokable("debugWorldClockReset")]
		public static void WorldClockReset()
		{
			WorldTime.Reset();
			WorldClock();
		}

		// JS bridge for the live connector-tuner slider panel (eaConnector in wwwroot/index.html, shown
		// on ?level=TeamChallenge / a bare ?connectortune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetConnector', boltCount, arcRate, jitter, pulse, glow). Overrides the ShipConnector
		// lightning knobs in real time — same effect as the ?connectorbolts/?connectorarcs/
		// ?connectorjitter/?connectorpulse/?connectorglow URL flags, just live.
		[JSInvokable("debugSetConnector")]
		public static void SetConnector(double boltCount, double arcRate, double jitter, double pulse, double glow)
		{
			DebugFlags.SetConnectorOverride(
				(int)System.Math.Round(boltCount),
				(float)arcRate,
				(float)jitter,
				(float)pulse,
				(float)glow);
		}

		// JS bridge for the live wall-tower slider panel (eaWalls in wwwroot/index.html, shown on
		// ?level=Level3&wallsonly / a bare ?walltune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetWalls', towers, depth, fog, sideDark, sideTile, faceLight, faceAngle, topLift,
		// bands, wisps, wispSpeed). Overrides the Level-3 tower knobs in real time so the sliders
		// retune without a page reload — same effect as the ?walltowers/?walldepth/?wallfog/
		// ?wallsidedark/?wallsidetile/?wallfacelight/?wallfaceangle/?walltoplift/?wall3dbands/
		// ?wallwisps/?wallwispspeed URL flags, just live. (?wallfogcolor stays URL-only — a colour
		// picker is a different widget and the haze reads fine off the two brightness knobs.)
		[JSInvokable("debugSetWalls")]
		public static void SetWalls(bool towers, double depth, double fog, double sideDark, double sideTile, double faceLight, double faceAngle, double topLift, double bands, double wisps, double wispSpeed)
		{
			DebugFlags.SetWallsOverride(
				towers,
				(float)depth,
				(float)fog,
				(float)sideDark,
				(float)sideTile,
				(float)faceLight,
				(float)faceAngle,
				(float)topLift,
				(float)bands,
				(float)wisps,
				(float)wispSpeed);
		}

		// JS bridge for the live spider-tuner slider panel (eaSpider in wwwroot/index.html, shown on
		// ?harness=spiderjump / ?level=Level2&spiders / ?spidertune): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetSpider', jumpFrame, landFrame, jumpX, pinJumpX, shadowX, shadowY, shadowScale, airX, airY).
		// Overrides the Mars jumping-spider alignment knobs in real time so the sliders retune without a
		// page reload — same effect as the ?spiderjumpframe/?spiderlandframe/?spiderjumpx/?spidershadow*/
		// ?spiderair* URL flags, just live. airX/airY nudge the airborne flying sprite so the launch +
		// landing transitions line up. pinJumpX == false => leave the launch X RANDOM per spider (the
		// shipped behaviour, jumpX ignored); true => pin it to `jumpX` so dialing is repeatable.
		[JSInvokable("debugSetSpider")]
		public static void SetSpider(double jumpFrame, double landFrame, double jumpX, bool pinJumpX, double shadowX, double shadowY, double shadowScale, double airX, double airY, double phase, bool freezePhase)
		{
			DebugFlags.SetSpiderOverride(
				(float)jumpFrame,
				(float)landFrame,
				pinJumpX ? (float?)(float)jumpX : null,
				(float)shadowX,
				(float)shadowY,
				(float)shadowScale,
				(float)airX,
				(float)airY,
				freezePhase ? (float?)(float)phase : null);
		}

		// JS bridge for the live webcam-tuner stepper panel (eaWcTune in wwwroot/index.html,
		// shown on the webcam level when ?wctune is set): DotNet.invokeMethod('EvilAliensWeb',
		// 'debugSetWcTune', hearts, kills, saucers, saucerSpeed, plasmaSpeed, spawnInterval,
		// armDelay, chargeTime). Overrides the eight WebcamLevel.Tunings[] knobs in real time
		// — ABSOLUTE final values (what you'd bake into the table), unlike the ?wc* URL
		// multipliers. WebcamLevel picks the change up on its next Update via WebcamTuneVersion.
		[JSInvokable("debugSetWcTune")]
		public static void SetWcTune(int hearts, int kills, int saucers, double saucerSpeed, double plasmaSpeed, double spawnInterval, double armDelay, double chargeTime, int mineMax, double mineSpawn)
		{
			DebugFlags.SetWebcamTuneOverride(hearts, kills, saucers, (float)saucerSpeed, (float)plasmaSpeed, (float)spawnInterval, (float)armDelay, (float)chargeTime, mineMax, (float)mineSpawn);
		}

		// Companion: drop all runtime webcam-tuner overrides (the panel's "Reset to tier"
		// button) so the level falls back to its shipped Tunings[] row + any ?wc* URL flags.
		[JSInvokable("debugClearWcTune")]
		public static void ClearWcTune()
		{
			DebugFlags.ClearWebcamTuneOverride();
		}

		// JS bridge for the live network-impairment panel (eaNetSim in wwwroot/index.html, shown
		// on a ?net boot that also passes ?netsim, or via eaNetSim.show()/eaNetSim(...) from the
		// console): DotNet.invokeMethod('EvilAliensWeb', 'debugSetNetSim', lagMs, lossPct,
		// jitterMs). Overrides the artificial impairment applied to INBOUND net traffic in real
		// time -- same effect as ?netlag=/?netloss=, just live, plus jitter which has no URL flag
		// (panel-only by design: it is the knob that makes the stream lane actually REORDER, so
		// it belongs next to the other two rather than in a boot URL).
		[JSInvokable("debugSetNetSim")]
		public static void SetNetSim(double lagMs, double lossPct, double jitterMs)
		{
			DebugFlags.SetNetSimOverride((float)lagMs, (float)lossPct, (float)jitterMs);
		}

		// Companion self-test (the panel's "Self-test" button / console eaNetSim.test(...)):
		// runs N synthetic packets per lane through a real NetImpairment on a VIRTUAL clock and
		// returns the measured delay / drop rate / per-lane reorder count as one line. This is
		// the card's primary verification -- impairment is behaviour over time, so the repo rule
		// is to read the data, not a frame.
		[JSInvokable("debugNetSimTest")]
		public static string NetSimTest(double lagMs, double lossPct, double jitterMs, int packets)
		{
			return Net.NetImpairment.SelfTest((float)lagMs, (float)lossPct, (float)jitterMs, packets);
		}

		// JS bridge for the ?texviewer control panel (eaTexViewer in wwwroot/index.html):
		// DotNet.invokeMethod('EvilAliensWeb', 'debugSetTexViewer', cmd). Enqueues a panel
		// command ("next"/"prev"/"flip:1"/"mode:1"/"pick:0"/"zoom:2.5"/"fit") that
		// TexViewerScene drains each Update. Save is done JS-side (POST /api/texdecide),
		// so it never routes through here.
		[JSInvokable("debugSetTexViewer")]
		public static void SetTexViewer(string cmd)
		{
			TexViewerInterop.Post(cmd);
		}

		// JS bridge (eaSuppressEsc in index.html), fired from a fullscreenchange listener when
		// the browser LEAVES fullscreen. Opens the Esc-swallow window so the Esc that exited
		// fullscreen doesn't also step back a menu. Idempotent -- re-fires just refresh it.
		[JSInvokable("debugSuppressEsc")]
		public static void SuppressEsc()
		{
			escGraceTicks = 8;    // ~130ms min: covers the exit keydown landing a tick or two late
			escGuardTicks = 40;   // ~0.66s hard cap so a held Esc can't kill Esc permanently
		}

		// Called once per InputHandler tick for the Esc key with its RAW keyboard-down state.
		// Returns true while the post-fullscreen-exit Esc should be swallowed: through the grace
		// window, then for as long as Esc stays CONTINUOUSLY held (the exit press), up to the
		// guard cap. The first tick with the grace elapsed AND Esc released ENDS the window --
		// both counters are zeroed -- so a deliberate second Esc press is never swallowed by a
		// leftover guard; the guard only bounds the continuous-hold case (fail-open).
		internal static bool EscSuppressActive(bool rawEscDown)
		{
			if (escGraceTicks <= 0 && !rawEscDown)
			{
				escGuardTicks = 0;
				return false;
			}
			if (escGuardTicks <= 0)
			{
				escGraceTicks = 0;
				return false;
			}
			if (escGraceTicks > 0)
			{
				escGraceTicks--;
			}
			escGuardTicks--;
			return true;
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
