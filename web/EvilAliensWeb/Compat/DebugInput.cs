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

		// Which menus are LIVE right now (card 72143c11). MenuSub1 has no modality: every menu
		// in the collection runs HandleInput every tick, so two live menus means two selections
		// moving and two entries invoked per press -- a bug that is invisible in a screenshot
		// (the top menu draws over the other) and has no other observable. This is what a probe
		// asserts on; nothing in the game reads it.
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
		// by entering the Online Co-op flow and nothing clears it across a level launch, so a
		// lobby match that ends mid-level returns to a menu holding it -- which needs a real
		// paired peer to reach. Everything the probe then exercises (NetUpdate's notice branch)
		// is the real code.
		[JSInvokable("debugMenuNetMode")]
		public static string MenuNetMode()
		{
			EvilAliens.IComponentBinService svc = EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>();
			System.Collections.Generic.List<EvilAliens.MenuScene> scenes = svc?.ComponentBin?.Live<EvilAliens.MenuScene>();
			if (scenes == null || scenes.Count == 0)
			{
				return "[menunetmode] no live MenuScene -- boot ?menu first";
			}
			scenes[0].NetDebugForceNetMode();
			return "[menunetmode] netMode=true on the live MenuScene";
		}

		private static System.Collections.Generic.List<EvilAliens.MenuSub1> LiveMenus()
		{
			EvilAliens.IComponentBinService svc = EvilAliens.ServiceHelper.Get<EvilAliens.IComponentBinService>();
			return svc?.ComponentBin?.Live<EvilAliens.MenuSub1>();
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
		[JSInvokable("debugOracleRoster")]
		public static string OracleRoster()
		{
			EvilAliens.Oracle oracle = EvilAliens.ServiceHelper.Get<EvilAliens.IOracleService>()?.Oracle;
			if (oracle == null)
			{
				return "[debug] eaOracleRoster: no oracle service (game not booted yet?)";
			}
			string seats = "";
			for (int slot = 0; slot < EvilAliens.Oracle.MaxPlayers; slot++)
			{
				if (oracle.IsSeated(slot))
				{
					seats += (seats.Length > 0 ? "," : "") + slot + ":" + oracle.Controller(slot);
				}
			}
			return "[debug] eaOracleRoster: players=" + oracle.Players
				+ " seated=" + (seats.Length > 0 ? seats : "-");
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
